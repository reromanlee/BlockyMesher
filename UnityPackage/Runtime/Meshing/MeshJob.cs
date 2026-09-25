using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.BlockyMesher.Meshing
{
    /// <summary>
    /// Turns the lit neighborhood into the section's mesh, written straight into Unity's mesh
    /// buffers. Faces are grouped by render pass (opaque, cutout, transparent), one submesh per pass
    /// that has any.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    internal struct MeshJob : IJob
    {
        [ReadOnly] public NativeArray<ushort> Blocks;
        [ReadOnly] public NativeArray<byte> Flags;
        [ReadOnly] public NativeArray<byte> Sky;
        [ReadOnly] public NativeArray<byte> BlockLight;
        [ReadOnly] public NativeArray<BlockInfo> BlockInfos;
        [ReadOnly] public NativeArray<ushort> FaceLayers;
        [ReadOnly] public NativeArray<Color32> LightColors;
        public bool SmoothLighting;

        public Mesh.MeshData Output;

        /// <summary>One bit per render pass that has faces. Only those passes become submeshes, in pass order.</summary>
        [WriteOnly] public NativeArray<int> PassMask;

        public void Execute()
        {
            // First find the visible faces, so the mesh buffers can be sized exactly, then write them.
            var visibleFaces = new NativeArray<byte>(Section.Volume, Allocator.Temp);
            int3 faceCounts = FindVisibleFaces(visibleFaces);
            int faceCount = faceCounts.x + faceCounts.y + faceCounts.z;
            int vertexCount = faceCount * 4;

            Output.SetVertexBufferParams(vertexCount, SectionVertex.Attributes(Allocator.Temp));
            Output.SetIndexBufferParams(faceCount * 6, vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16);
            NativeArray<SectionVertex> vertices = Output.GetVertexData<SectionVertex>();
            NativeArray<ushort> shortIndices = Output.indexFormat == IndexFormat.UInt16 ? Output.GetIndexData<ushort>() : default;
            NativeArray<uint> longIndices = Output.indexFormat == IndexFormat.UInt32 ? Output.GetIndexData<uint>() : default;

            int3 nextFace = new int3(0, faceCounts.x, faceCounts.x + faceCounts.y);
            for (int i = 0; i < Section.Volume; i++)
            {
                int faces = visibleFaces[i];
                if (faces == 0)
                    continue;
                int3 local = Section.Local(i);
                int cell = Neighborhood.Index(local.x + Neighborhood.Border, local.y + Neighborhood.Border, local.z + Neighborhood.Border);
                ushort id = Blocks[cell];
                int pass = (int)BlockInfos[id].Pass;
                for (int face = 0; face < Faces.Count; face++)
                {
                    if ((faces & (1 << face)) == 0)
                        continue;
                    int faceIndex = nextFace[pass]++;
                    WriteFace(faceIndex, cell, local, face, id, vertices, shortIndices, longIndices);
                }
            }

            // Most sections only hold opaque blocks: one submesh, one material, one draw call.
            int passMask = (faceCounts.x > 0 ? 1 : 0) | (faceCounts.y > 0 ? 2 : 0) | (faceCounts.z > 0 ? 4 : 0);
            PassMask[0] = passMask;
            Output.subMeshCount = math.countbits(passMask);
            var bounds = new Bounds(new Vector3(8, 8, 8), new Vector3(16, 16, 16));
            int firstFace = 0;
            int subMeshIndex = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                int count = faceCounts[pass];
                if (count == 0)
                    continue;
                var subMesh = new SubMeshDescriptor(firstFace * 6, count * 6)
                {
                    bounds = bounds,
                    firstVertex = firstFace * 4,
                    vertexCount = count * 4,
                };
                Output.SetSubMesh(subMeshIndex++, subMesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
                firstFace += count;
            }
        }

        /// <summary>Stores a bit per visible face for every block of the section, and counts them per pass.</summary>
        int3 FindVisibleFaces(NativeArray<byte> visibleFaces)
        {
            int3 counts = 0;
            for (int i = 0; i < Section.Volume; i++)
            {
                int3 local = Section.Local(i);
                int cell = Neighborhood.Index(local.x + Neighborhood.Border, local.y + Neighborhood.Border, local.z + Neighborhood.Border);
                byte flags = Flags[cell];
                if ((flags & (byte)BlockFlags.Visible) == 0)
                    continue;
                ushort id = Blocks[cell];
                int faces = 0;
                for (int face = 0; face < Faces.Count; face++)
                {
                    int neighbor = cell + Step(face);
                    bool hidden = (Flags[neighbor] & (byte)BlockFlags.Opaque) != 0
                        || ((flags & (byte)BlockFlags.HideSameNeighbor) != 0 && Blocks[neighbor] == id);
                    if (!hidden)
                        faces |= 1 << face;
                }
                visibleFaces[i] = (byte)faces;
                counts[(int)BlockInfos[id].Pass] += math.countbits(faces);
            }
            return counts;
        }

        void WriteFace(int faceIndex, int cell, int3 local, int face, ushort id,
            NativeArray<SectionVertex> vertices, NativeArray<ushort> shortIndices, NativeArray<uint> longIndices)
        {
            FaceFrame frame = FaceFrame.Of(face);
            int front = cell + Neighborhood.Offset(frame.Normal);
            int stepU = Neighborhood.Offset(frame.U);
            int stepV = Neighborhood.Offset(frame.V);
            byte occlusion = OcclusionCase(front, stepU, stepV);
            byte layer = (byte)math.min(FaceLayers[id * Faces.Count + face], 255);

            int firstVertex = faceIndex * 4;
            int4 brightness = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                int2 uv = FaceFrame.Corner(corner);
                int3 position = local + frame.Origin + uv.x * frame.U + uv.y * frame.V;
                CornerLight(cell, front, uv.x == 0 ? -stepU : stepU, uv.y == 0 ? -stepV : stepV, out int sky, out int blockLight);
                brightness[corner] = sky + (blockLight & 7);
                vertices[firstVertex + corner] = new SectionVertex
                {
                    X = (byte)position.x,
                    Y = (byte)position.y,
                    Z = (byte)position.z,
                    Corner = (byte)(uv.x | (uv.y << 1)),
                    BlockLight = Tint(blockLight),
                    Layer = layer,
                    Occlusion = occlusion,
                    Sky = (byte)sky,
                };
            }

            // Split the quad along the diagonal whose corners are lit most alike, so a single bright or
            // dark corner fades evenly instead of streaking across the face.
            int split = math.abs(brightness.x - brightness.z) > math.abs(brightness.y - brightness.w) ? 1 : 0;
            int firstIndex = faceIndex * 6;
            WriteIndex(firstIndex + 0, firstVertex + split, shortIndices, longIndices);
            WriteIndex(firstIndex + 1, firstVertex + split + 1, shortIndices, longIndices);
            WriteIndex(firstIndex + 2, firstVertex + split + 2, shortIndices, longIndices);
            WriteIndex(firstIndex + 3, firstVertex + split + 2, shortIndices, longIndices);
            WriteIndex(firstIndex + 4, firstVertex + ((split + 3) & 3), shortIndices, longIndices);
            WriteIndex(firstIndex + 5, firstVertex + split, shortIndices, longIndices);
        }

        /// <summary>
        /// Smooth lighting: the brightest of the front block and the blocks around this corner that
        /// light can reach it from (see <see cref="LightConnections"/>). Flat lighting: the front block.
        /// </summary>
        void CornerLight(int cell, int front, int towardU, int towardV, out int sky, out int blockLight)
        {
            sky = Sky[front];
            blockLight = BlockLight[front];
            if (!SmoothLighting)
                return;

            int frontU = front + towardU;
            int frontV = front + towardV;
            int ownU = cell + towardU;
            int ownV = cell + towardV;
            int frontUV = frontU + towardV;
            int ownUV = ownU + towardV;
            int passable = PassesLight(frontU) | (PassesLight(frontV) << 1) | (PassesLight(ownU) << 2)
                | (PassesLight(ownV) << 3) | (PassesLight(frontUV) << 4) | (PassesLight(ownUV) << 5);

            int connected = LightConnections.Table[passable];
            if ((connected & 1) != 0) Brightest(frontU, ref sky, ref blockLight);
            if ((connected & 2) != 0) Brightest(frontV, ref sky, ref blockLight);
            if ((connected & 4) != 0) Brightest(ownU, ref sky, ref blockLight);
            if ((connected & 8) != 0) Brightest(ownV, ref sky, ref blockLight);
            if ((connected & 16) != 0) Brightest(frontUV, ref sky, ref blockLight);
            if ((connected & 32) != 0) Brightest(ownUV, ref sky, ref blockLight);
        }

        void Brightest(int cell, ref int sky, ref int blockLight)
        {
            sky = math.max(sky, Sky[cell]);
            int candidate = BlockLight[cell];
            if ((candidate & 7) > (blockLight & 7))
                blockLight = candidate;
        }

        /// <summary>
        /// Which of the 8 blocks around the front block, in the face's plane, are solid: one bit each,
        /// row by row from the (-U, -V) corner. The shader draws the matching prebaked shadow tile.
        /// </summary>
        byte OcclusionCase(int front, int stepU, int stepV)
        {
            return (byte)(IsOpaque(front - stepU - stepV)
                | (IsOpaque(front - stepV) << 1)
                | (IsOpaque(front + stepU - stepV) << 2)
                | (IsOpaque(front - stepU) << 3)
                | (IsOpaque(front + stepU) << 4)
                | (IsOpaque(front - stepU + stepV) << 5)
                | (IsOpaque(front + stepV) << 6)
                | (IsOpaque(front + stepU + stepV) << 7));
        }

        Color32 Tint(int blockLight)
        {
            int level = blockLight & 7;
            if (level == 0)
                return default;
            Color32 color = LightColors[math.min(blockLight >> 3, LightColors.Length - 1)];
            return new Color32((byte)(color.r * level / 7), (byte)(color.g * level / 7), (byte)(color.b * level / 7), 255);
        }

        int PassesLight(int cell) => (Flags[cell] & (byte)BlockFlags.LightPasses) != 0 ? 1 : 0;

        int IsOpaque(int cell) => (Flags[cell] & (byte)BlockFlags.Opaque) != 0 ? 1 : 0;

        static int Step(int face) => face switch
        {
            (int)Face.Right => Neighborhood.StepX,
            (int)Face.Left => -Neighborhood.StepX,
            (int)Face.Up => Neighborhood.StepY,
            (int)Face.Down => -Neighborhood.StepY,
            (int)Face.Forward => Neighborhood.StepZ,
            _ => -Neighborhood.StepZ,
        };

        static void WriteIndex(int at, int vertex, NativeArray<ushort> shortIndices, NativeArray<uint> longIndices)
        {
            if (shortIndices.IsCreated)
                shortIndices[at] = (ushort)vertex;
            else
                longIndices[at] = (uint)vertex;
        }
    }
}
