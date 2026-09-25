using System;
using System.Collections.Generic;
using reromanlee.BlockyMesher.Meshing;
using reromanlee.BlockyMesher.Storage;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.BlockyMesher.Tests
{
    /// <summary>A small world to place blocks in, and a section build to run on it.</summary>
    internal sealed class BuildHarness : IDisposable
    {
        public readonly TestBlocks Blocks = new();
        public readonly BlockStorage Storage;
        public readonly SectionBuild Build = new();
        public BuildSettings Settings = new() { SmoothLighting = true };

        int2 builtColumn;
        int builtSectionY;

        public BuildHarness(int sectionsPerColumn = 4)
        {
            Storage = new BlockStorage(sectionsPerColumn, Blocks.Table);
        }

        public void Set(int x, int y, int z, ushort id) => Storage.SetBlock(new int3(x, y, z), id, createColumn: true);

        public void Fill(int3 min, int3 max, ushort id) => Storage.Fill(min, max, id, createColumns: true);

        public void Run(int2 column = default, int sectionY = 0)
        {
            Build.Cancel();
            builtColumn = column;
            builtSectionY = sectionY;
            Build.CopyInputs(Storage, column, sectionY);
            Build.Schedule(Blocks.Table, Settings).Complete();
        }

        int Cell(int3 world)
        {
            int3 box = world - new int3(builtColumn.x, builtSectionY, builtColumn.y) * Section.Size + Neighborhood.Border;
            return Neighborhood.Index(box.x, box.y, box.z);
        }

        public ushort BlockAt(int x, int y, int z) => Build.Blocks[Cell(new int3(x, y, z))];
        public int SkyAt(int x, int y, int z) => Build.Sky[Cell(new int3(x, y, z))];
        public int BlockLightAt(int x, int y, int z) => Build.BlockLight[Cell(new int3(x, y, z))] & 7;
        public int LightColorAt(int x, int y, int z) => Build.BlockLight[Cell(new int3(x, y, z))] >> 3;

        public int VertexCount => Build.Result.vertexCount;
        public int FaceCount => VertexCount / 4;
        public IndexFormat IndexFormat => Build.Result.indexFormat;
        /// <summary>Only passes with faces get a submesh, in pass order.</summary>
        public int IndexCount(RenderPass pass)
        {
            int bit = 1 << (int)pass;
            if ((Build.PassMask & bit) == 0)
                return 0;
            return Build.Result.GetSubMesh(math.countbits(Build.PassMask & (bit - 1))).indexCount;
        }
        public SectionVertex[] Vertices => Build.Result.GetVertexData<SectionVertex>().ToArray();

        public int[] Indices
        {
            get
            {
                Mesh.MeshData data = Build.Result;
                var indices = new List<int>();
                if (data.indexFormat == IndexFormat.UInt16)
                    foreach (ushort index in data.GetIndexData<ushort>()) indices.Add(index);
                else
                    foreach (uint index in data.GetIndexData<uint>()) indices.Add((int)index);
                return indices.ToArray();
            }
        }

        /// <summary>The four corners of every face whose corners all lie on the given plane.</summary>
        public List<SectionVertex[]> FacesAt(Func<SectionVertex, bool> corner)
        {
            var faces = new List<SectionVertex[]>();
            SectionVertex[] vertices = Vertices;
            for (int i = 0; i < vertices.Length; i += 4)
            {
                var face = new[] { vertices[i], vertices[i + 1], vertices[i + 2], vertices[i + 3] };
                if (Array.TrueForAll(face, v => corner(v)))
                    faces.Add(face);
            }
            return faces;
        }

        public void Dispose()
        {
            Build.Dispose();
            Storage.Dispose();
            Blocks.Dispose();
        }
    }
}
