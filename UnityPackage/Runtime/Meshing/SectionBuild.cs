using System;
using reromanlee.BlockyMesher.Storage;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.BlockyMesher.Meshing
{
    internal struct BuildSettings
    {
        public bool SmoothLighting;

        /// <summary>Treat everything below y = 0 as solid, which hides the bottom faces of a terrain.</summary>
        public bool SolidBelowWorld;
    }

    /// <summary>
    /// Everything one section build needs, reused from build to build. The main thread copies the
    /// sections around the target (<see cref="CopyInputs"/>), then jobs run one after another:
    /// gather the neighborhood, light it, mesh it (and build its colliders alongside the mesh).
    /// Working on copies is what lets the landscape keep changing while the jobs run.
    /// </summary>
    internal sealed class SectionBuild : IDisposable
    {
        public static readonly Bounds SectionBounds = new(new Vector3(8, 8, 8), new Vector3(16, 16, 16));

        public int2 ColumnPosition { get; private set; }
        public int SectionY { get; private set; }
        public JobHandle Handle { get; private set; }

        NativeArray<ushort> sections = new(27 * Section.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        NativeArray<int> uniformIds = new(27, Allocator.Persistent);
        NativeArray<ushort> skyStarts = new(9 * Section.Area, Allocator.Persistent);
        NativeArray<ushort> blocks = new(Neighborhood.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        NativeArray<byte> boxSkyStart = new(Neighborhood.Area, Allocator.Persistent);
        NativeArray<byte> flags = new(Neighborhood.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        NativeArray<byte> sky = new(Neighborhood.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        NativeArray<byte> blockLight = new(Neighborhood.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        NativeArray<ushort> queue = new(Neighborhood.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        NativeArray<int> passMask = new(1, Allocator.Persistent);
        NativeList<BoxRange> boxes = new(Allocator.Persistent);

        Mesh.MeshDataArray meshData;
        bool hasMeshData;
        Mesh.MeshDataArray collisionData;
        bool hasCollisionData;

        /// <summary>Approximate native memory one build holds, for the stats.</summary>
        public const int MemoryBytes = 27 * Section.Volume * 2 + 27 * 4 + 9 * Section.Area * 2
            + Neighborhood.Volume * (2 + 1 + 1 + 1 + 2) + Neighborhood.Area;

        internal NativeArray<ushort> Blocks => blocks;
        internal NativeArray<byte> Flags => flags;
        internal NativeArray<byte> Sky => sky;
        internal NativeArray<byte> BlockLight => blockLight;

        /// <summary>The finished mesh data, readable after <see cref="Handle"/> completes.</summary>
        internal Mesh.MeshData Result => meshData[0];

        /// <summary>Which render passes the mesh has submeshes for, one bit each. Valid once complete.</summary>
        public int PassMask => passMask[0];

        public int VertexCount => meshData[0].vertexCount;

        /// <summary>The colliders this build made, see <see cref="Schedule"/>.</summary>
        public ColliderMode Colliders { get; private set; }

        /// <summary>Collider boxes, for <see cref="ColliderMode.Boxes"/>. Valid once complete.</summary>
        public NativeList<BoxRange> Boxes => boxes;

        public void CopyInputs(BlockStorage storage, int2 columnPosition, int sectionY)
        {
            ColumnPosition = columnPosition;
            SectionY = sectionY;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int columnIndex = (dx + 1) + (dz + 1) * 3;
                bool exists = storage.TryGetColumn(columnPosition + new int2(dx, dz), out Column column);
                if (exists)
                    NativeArray<ushort>.Copy(column.SkyStart, 0, skyStarts, columnIndex * Section.Area, Section.Area);
                else
                    Clear(skyStarts, columnIndex * Section.Area, Section.Area);

                for (int dy = -1; dy <= 1; dy++)
                {
                    int neighbor = columnIndex + (dy + 1) * 9;
                    int y = sectionY + dy;
                    if (!exists || y < 0 || y >= storage.SectionsPerColumn)
                    {
                        uniformIds[neighbor] = 0;
                        continue;
                    }
                    SectionBlocks section = column.Sections[y];
                    if (section.IsUniform)
                    {
                        uniformIds[neighbor] = section.UniformId;
                    }
                    else
                    {
                        uniformIds[neighbor] = -1;
                        NativeArray<ushort>.Copy(section.Blocks, 0, sections, neighbor * Section.Volume, Section.Volume);
                    }
                }
            }
        }

        public JobHandle Schedule(BlockTable table, BuildSettings settings, ColliderMode colliders = ColliderMode.None)
        {
            meshData = Mesh.AllocateWritableMeshData(1);
            hasMeshData = true;
            Colliders = colliders;
            int boxBottomY = SectionY * Section.Size - Neighborhood.Border;

            JobHandle gather = new GatherJob
            {
                Sections = sections,
                UniformIds = uniformIds,
                SkyStarts = skyStarts,
                BoxBottomY = boxBottomY,
                SolidBelowWorld = settings.SolidBelowWorld,
                Blocks = blocks,
                BoxSkyStart = boxSkyStart,
            }.Schedule();

            JobHandle light = new LightJob
            {
                Blocks = blocks,
                BoxSkyStart = boxSkyStart,
                BlockInfos = table.Blocks,
                BoxBottomY = boxBottomY,
                SolidBelowWorld = settings.SolidBelowWorld,
                Flags = flags,
                Sky = sky,
                BlockLight = blockLight,
                Queue = queue,
            }.Schedule(gather);

            JobHandle mesh = new MeshJob
            {
                Blocks = blocks,
                Flags = flags,
                Sky = sky,
                BlockLight = blockLight,
                BlockInfos = table.Blocks,
                FaceLayers = table.FaceLayers,
                LightColors = table.LightColors,
                SmoothLighting = settings.SmoothLighting,
                Output = meshData[0],
                PassMask = passMask,
            }.Schedule(light);

            // Colliders only read the lit neighborhood too, so they are built alongside the mesh.
            JobHandle collision = default;
            if (colliders == ColliderMode.Mesh)
            {
                collisionData = Mesh.AllocateWritableMeshData(1);
                hasCollisionData = true;
                collision = new CollisionMeshJob { Flags = flags, Output = collisionData[0] }.Schedule(light);
            }
            else if (colliders == ColliderMode.Boxes)
            {
                collision = new BoxMergeJob { Flags = flags, Boxes = boxes }.Schedule(light);
            }
            Handle = JobHandle.CombineDependencies(mesh, collision);
            return Handle;
        }

        /// <summary>Waits for the jobs and moves the result into <paramref name="mesh"/>.</summary>
        public void Apply(Mesh mesh)
        {
            Handle.Complete();
            Mesh.ApplyAndDisposeWritableMeshData(meshData, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            hasMeshData = false;
            mesh.bounds = SectionBounds;
        }

        /// <summary>Moves the collision mesh of a <see cref="ColliderMode.Mesh"/> build into <paramref name="mesh"/>.</summary>
        public void ApplyCollision(Mesh mesh)
        {
            Handle.Complete();
            Mesh.ApplyAndDisposeWritableMeshData(collisionData, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            hasCollisionData = false;
            mesh.bounds = SectionBounds;
        }

        /// <summary>Waits for the jobs and throws away whatever wasn't applied.</summary>
        public void Cancel()
        {
            Handle.Complete();
            if (hasMeshData)
                meshData.Dispose();
            if (hasCollisionData)
                collisionData.Dispose();
            hasMeshData = false;
            hasCollisionData = false;
        }

        public void Dispose()
        {
            Cancel();
            sections.Dispose();
            uniformIds.Dispose();
            skyStarts.Dispose();
            blocks.Dispose();
            boxSkyStart.Dispose();
            flags.Dispose();
            sky.Dispose();
            blockLight.Dispose();
            queue.Dispose();
            passMask.Dispose();
            boxes.Dispose();
        }

        static void Clear(NativeArray<ushort> array, int start, int length)
        {
            for (int i = start; i < start + length; i++)
                array[i] = 0;
        }
    }
}
