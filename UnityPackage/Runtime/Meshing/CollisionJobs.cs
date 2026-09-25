using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.BlockyMesher.Meshing
{
    /// <summary>A box of blocks inside a section, from <see cref="Min"/> up to, but not including, <see cref="Max"/>.</summary>
    internal struct BoxRange
    {
        public int3 Min;
        public int3 Max;
    }

    /// <summary>
    /// The collision mesh of a section: only faces between collidable and non-collidable blocks,
    /// and neighboring faces merged into rectangles, so PhysX has far fewer triangles to prepare
    /// than the render mesh has. Positions only, as floats, since that is what PhysX reads.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    internal struct CollisionMeshJob : IJob
    {
        [ReadOnly] public NativeArray<byte> Flags;
        public Mesh.MeshData Output;

        public void Execute()
        {
            var corners = new NativeList<float3>(Allocator.Temp);
            var open = new NativeArray<bool>(Section.Area, Allocator.Temp);
            for (int face = 0; face < Faces.Count; face++)
                MergeFaces(face, open, corners);

            int vertexCount = corners.Length;
            var attributes = new NativeArray<VertexAttributeDescriptor>(1, Allocator.Temp);
            attributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
            Output.SetVertexBufferParams(vertexCount, attributes);
            Output.GetVertexData<float3>().CopyFrom(corners.AsArray());

            int indexCount = vertexCount / 4 * 6;
            if (vertexCount > ushort.MaxValue)
            {
                Output.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                NativeArray<uint> indices = Output.GetIndexData<uint>();
                for (int quad = 0, i = 0; i < indexCount; quad += 4, i += 6)
                {
                    indices[i] = (uint)quad; indices[i + 1] = (uint)quad + 1; indices[i + 2] = (uint)quad + 2;
                    indices[i + 3] = (uint)quad + 2; indices[i + 4] = (uint)quad + 3; indices[i + 5] = (uint)quad;
                }
            }
            else
            {
                Output.SetIndexBufferParams(indexCount, IndexFormat.UInt16);
                NativeArray<ushort> indices = Output.GetIndexData<ushort>();
                for (int quad = 0, i = 0; i < indexCount; quad += 4, i += 6)
                {
                    indices[i] = (ushort)quad; indices[i + 1] = (ushort)(quad + 1); indices[i + 2] = (ushort)(quad + 2);
                    indices[i + 3] = (ushort)(quad + 2); indices[i + 4] = (ushort)(quad + 3); indices[i + 5] = (ushort)quad;
                }
            }

            Output.subMeshCount = 1;
            Output.SetSubMesh(0, new SubMeshDescriptor(0, indexCount)
            {
                bounds = new Bounds(new Vector3(8, 8, 8), new Vector3(16, 16, 16)),
                vertexCount = vertexCount,
            }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
        }

        /// <summary>
        /// Goes through the section layer by layer along the face's normal. In each layer, finds the
        /// blocks showing a collision face, and covers them with rectangles: as wide as possible first,
        /// then as tall as the whole width allows.
        /// </summary>
        void MergeFaces(int face, NativeArray<bool> open, NativeList<float3> corners)
        {
            FaceFrame frame = FaceFrame.Of(face);
            int normalAxis = Axis(frame.Normal), uAxis = Axis(frame.U), vAxis = Axis(frame.V);
            bool uPositive = math.csum(frame.U) > 0, vPositive = math.csum(frame.V) > 0;
            int planeOffset = math.csum(frame.Normal) > 0 ? 1 : 0;

            for (int layer = 0; layer < Section.Size; layer++)
            {
                for (int j = 0; j < Section.Size; j++)
                for (int i = 0; i < Section.Size; i++)
                {
                    int3 block = default;
                    block[normalAxis] = layer;
                    block[uAxis] = i;
                    block[vAxis] = j;
                    open[i + j * Section.Size] = IsCollidable(block) && !IsCollidable(block + frame.Normal);
                }

                for (int j = 0; j < Section.Size; j++)
                for (int i = 0; i < Section.Size; i++)
                {
                    if (!open[i + j * Section.Size])
                        continue;
                    int width = 1;
                    while (i + width < Section.Size && open[i + width + j * Section.Size])
                        width++;
                    int height = 1;
                    while (j + height < Section.Size && RowIsOpen(open, i, width, j + height))
                        height++;
                    for (int y = j; y < j + height; y++)
                    for (int x = i; x < i + width; x++)
                        open[x + y * Section.Size] = false;

                    // Corners in the same winding as the render mesh: (0,0), (0,1), (1,1), (1,0) along U and V.
                    int uStart = uPositive ? i : i + width, uEnd = uPositive ? i + width : i;
                    int vStart = vPositive ? j : j + height, vEnd = vPositive ? j + height : j;
                    int plane = layer + planeOffset;
                    corners.Add(Corner(normalAxis, plane, uAxis, uStart, vAxis, vStart));
                    corners.Add(Corner(normalAxis, plane, uAxis, uStart, vAxis, vEnd));
                    corners.Add(Corner(normalAxis, plane, uAxis, uEnd, vAxis, vEnd));
                    corners.Add(Corner(normalAxis, plane, uAxis, uEnd, vAxis, vStart));
                }
            }
        }

        bool IsCollidable(int3 local) =>
            (Flags[Neighborhood.Index(local.x + Neighborhood.Border, local.y + Neighborhood.Border, local.z + Neighborhood.Border)] & (byte)BlockFlags.Collidable) != 0;

        static bool RowIsOpen(NativeArray<bool> open, int start, int width, int row)
        {
            for (int x = start; x < start + width; x++)
            {
                if (!open[x + row * Section.Size])
                    return false;
            }
            return true;
        }

        static float3 Corner(int normalAxis, int plane, int uAxis, int u, int vAxis, int v)
        {
            float3 corner = default;
            corner[normalAxis] = plane;
            corner[uAxis] = u;
            corner[vAxis] = v;
            return corner;
        }

        static int Axis(int3 direction) => direction.x != 0 ? 0 : direction.y != 0 ? 1 : 2;
    }

    /// <summary>
    /// Covers the section's collidable blocks with as few boxes as possible. Used for landscapes that
    /// move, like a raft on a Rigidbody, where Unity doesn't allow concave mesh colliders.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    internal struct BoxMergeJob : IJob
    {
        [ReadOnly] public NativeArray<byte> Flags;
        public NativeList<BoxRange> Boxes;

        public void Execute()
        {
            Boxes.Clear();
            var taken = new NativeArray<bool>(Section.Volume, Allocator.Temp);
            for (int y = 0; y < Section.Size; y++)
            for (int z = 0; z < Section.Size; z++)
            for (int x = 0; x < Section.Size; x++)
            {
                if (!IsFree(taken, x, y, z))
                    continue;

                // Grow along x, then z while the whole row is free, then y while the whole layer is.
                int maxX = x + 1;
                while (maxX < Section.Size && IsFree(taken, maxX, y, z))
                    maxX++;
                int maxZ = z + 1;
                while (maxZ < Section.Size && AreFree(taken, x, maxX, y, y + 1, maxZ, maxZ + 1))
                    maxZ++;
                int maxY = y + 1;
                while (maxY < Section.Size && AreFree(taken, x, maxX, maxY, maxY + 1, z, maxZ))
                    maxY++;

                for (int by = y; by < maxY; by++)
                for (int bz = z; bz < maxZ; bz++)
                for (int bx = x; bx < maxX; bx++)
                    taken[Section.Index(bx, by, bz)] = true;
                Boxes.Add(new BoxRange { Min = new int3(x, y, z), Max = new int3(maxX, maxY, maxZ) });
            }
        }

        bool IsFree(NativeArray<bool> taken, int x, int y, int z) =>
            !taken[Section.Index(x, y, z)]
            && (Flags[Neighborhood.Index(x + Neighborhood.Border, y + Neighborhood.Border, z + Neighborhood.Border)] & (byte)BlockFlags.Collidable) != 0;

        bool AreFree(NativeArray<bool> taken, int minX, int maxX, int minY, int maxY, int minZ, int maxZ)
        {
            for (int y = minY; y < maxY; y++)
            for (int z = minZ; z < maxZ; z++)
            for (int x = minX; x < maxX; x++)
            {
                if (!IsFree(taken, x, y, z))
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Lets PhysX prepare ("cook") a collision mesh off the main thread. Engine call, so no Burst;
    /// on the web it runs on the main thread like every non-Burst job.
    /// </summary>
    internal struct CookColliderJob : IJob
    {
        // Unity 6.3 replaced instance ids with EntityId, and 6.5 removed the old overloads.
#if UNITY_6000_3_OR_NEWER
        public EntityId MeshId;
#else
        public int MeshId;
#endif
        public MeshColliderCookingOptions Options;

        public static CookColliderJob For(Mesh mesh, MeshColliderCookingOptions options) => new()
        {
#if UNITY_6000_3_OR_NEWER
            MeshId = mesh.GetEntityId(),
#else
            MeshId = mesh.GetInstanceID(),
#endif
            Options = options,
        };

        public void Execute() => Physics.BakeMesh(MeshId, false, Options);
    }
}
