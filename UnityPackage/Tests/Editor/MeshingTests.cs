using NUnit.Framework;
using reromanlee.BlockyMesher.Meshing;
using Unity.Mathematics;
using UnityEngine.Rendering;
using static reromanlee.BlockyMesher.Tests.TestBlocks;

namespace reromanlee.BlockyMesher.Tests
{
    public class MeshingTests
    {
        BuildHarness world;

        [SetUp] public void SetUp() => world = new BuildHarness();
        [TearDown] public void TearDown() => world.Dispose();

        [Test]
        public void AnEmptySectionBuildsAnEmptyMesh()
        {
            world.Run();
            Assert.AreEqual(0, world.VertexCount);
            Assert.AreEqual(0, world.IndexCount(RenderPass.Opaque));
        }

        [Test]
        public void ALoneBlockHasSixFaces()
        {
            world.Set(5, 5, 5, Stone);
            world.Run();
            Assert.AreEqual(6, world.FaceCount);
            Assert.AreEqual(36, world.IndexCount(RenderPass.Opaque));
            Assert.AreEqual(0, world.IndexCount(RenderPass.Cutout));
            Assert.AreEqual(0, world.IndexCount(RenderPass.Transparent));
        }

        [Test]
        public void FacesAgainstSolidBlocksAreHidden()
        {
            world.Set(5, 5, 5, Stone);
            world.Set(6, 5, 5, Stone);
            world.Run();
            Assert.AreEqual(10, world.FaceCount);
        }

        [Test]
        public void NeighborsAreGatheredFromEverySide()
        {
            world.Set(-1, 5, 3, Stone);
            world.Set(16, 5, 3, Glass);
            world.Set(3, 16, 3, Dirt);
            world.Set(3, 5, -1, Leaves);
            world.Set(-8, 0, -8, Water);
            world.Run();
            Assert.AreEqual(Stone, world.BlockAt(-1, 5, 3));
            Assert.AreEqual(Glass, world.BlockAt(16, 5, 3));
            Assert.AreEqual(Dirt, world.BlockAt(3, 16, 3));
            Assert.AreEqual(Leaves, world.BlockAt(3, 5, -1));
            Assert.AreEqual(Water, world.BlockAt(-8, 0, -8));
            Assert.AreEqual(Air, world.BlockAt(-2, 5, 3));
        }

        [TestCase(1, 0, 0)]
        [TestCase(-1, 0, 0)]
        [TestCase(0, 1, 0)]
        [TestCase(0, 0, 1)]
        [TestCase(0, 0, -1)]
        public void FacesAreHiddenByBlocksInTheNextSection(int dx, int dy, int dz)
        {
            var inside = new int3(dx > 0 ? 15 : dx < 0 ? 0 : 5, dy > 0 ? 15 : 5, dz > 0 ? 15 : dz < 0 ? 0 : 5);
            int3 outside = inside + new int3(dx, dy, dz);
            world.Set(inside.x, inside.y, inside.z, Stone);
            world.Set(outside.x, outside.y, outside.z, Stone);
            world.Run();
            Assert.AreEqual(5, world.FaceCount);
        }

        [Test]
        public void BlocksTwoStepsAcrossABorderDoNotHideFaces()
        {
            // The prototype mirrored the neighbor's border, so x = -2 was read as the block next to x = 0.
            world.Set(0, 5, 5, Stone);
            world.Set(-2, 5, 5, Stone);
            world.Run();
            Assert.AreEqual(6, world.FaceCount);
        }

        [Test]
        public void GlassHidesTheFacesBetweenGlass()
        {
            world.Set(5, 5, 5, Glass);
            world.Set(6, 5, 5, Glass);
            world.Run();
            Assert.AreEqual(10 * 6, world.IndexCount(RenderPass.Cutout));
        }

        [Test]
        public void SolidBlocksShowTheirFacesThroughGlass()
        {
            world.Set(5, 5, 5, Glass);
            world.Set(6, 5, 5, Stone);
            world.Run();
            Assert.AreEqual(6 * 6, world.IndexCount(RenderPass.Opaque));
            Assert.AreEqual(5 * 6, world.IndexCount(RenderPass.Cutout));
        }

        [Test]
        public void EachRenderPassGetsItsOwnSubmesh()
        {
            world.Set(2, 2, 2, Stone);
            world.Set(6, 2, 2, Glass);
            world.Set(10, 2, 2, Water);
            world.Run();
            Assert.AreEqual(36, world.IndexCount(RenderPass.Opaque));
            Assert.AreEqual(36, world.IndexCount(RenderPass.Cutout));
            Assert.AreEqual(36, world.IndexCount(RenderPass.Transparent));
        }

        [Test]
        public void TheWorldFloorHidesBottomFacesOnlyWhenAsked()
        {
            world.Set(5, 0, 5, Stone);
            world.Run();
            Assert.AreEqual(6, world.FaceCount);

            world.Settings.SolidBelowWorld = true;
            world.Run();
            Assert.AreEqual(5, world.FaceCount);
        }

        [Test]
        public void FacesWindClockwiseSeenFromOutside()
        {
            world.Set(5, 5, 5, Stone);
            world.Run();
            SectionVertex[] vertices = world.Vertices;
            int[] indices = world.Indices;
            var center = new float3(5.5f);
            for (int i = 0; i < indices.Length; i += 3)
            {
                float3 a = Position(vertices[indices[i]]);
                float3 b = Position(vertices[indices[i + 1]]);
                float3 c = Position(vertices[indices[i + 2]]);
                float3 normal = math.cross(b - a, c - a);
                Assert.Greater(math.dot(normal, (a + b + c) / 3 - center), 0, $"triangle {i / 3} faces inwards");
            }
        }

        [Test]
        public void VerticesStayInsideTheSection()
        {
            world.Fill(new int3(-3, 0, -3), new int3(19, 20, 19), Stone);
            world.Fill(new int3(2, 2, 2), new int3(14, 14, 14), Air);
            world.Run();
            Assert.Greater(world.VertexCount, 0);
            foreach (SectionVertex vertex in world.Vertices)
                Assert.IsTrue(vertex.X <= 16 && vertex.Y <= 16 && vertex.Z <= 16);
        }

        [Test]
        public void TextureLayersComeFromTheFace()
        {
            world.Set(5, 5, 5, Dirt);
            world.Run();
            Assert.AreEqual(1, world.FacesAt(v => v.Y == 6).Count);
            Assert.AreEqual(2, world.FacesAt(v => v.Y == 6)[0][0].Layer, "top");
            Assert.AreEqual(3, world.FacesAt(v => v.Y == 5)[0][0].Layer, "bottom");
            Assert.AreEqual(1, world.FacesAt(v => v.X == 6)[0][0].Layer, "side");
        }

        [Test]
        public void TheAmbientOcclusionCaseSeesTheBlocksAroundTheFace()
        {
            world.Fill(new int3(0, 0, 0), new int3(16, 1, 16), Stone);
            world.Set(6, 1, 5, Stone);
            world.Run();

            // The top of floor block (5, 0, 5): U is +X and V is +Z, so the block at +X is bit 4.
            var faces = world.FacesAt(v => v.Y == 1 && v.X >= 5 && v.X <= 6 && v.Z >= 5 && v.Z <= 6);
            Assert.AreEqual(1, faces.Count);
            Assert.AreEqual(1 << 4, faces[0][0].Occlusion);
        }

        [Test]
        public void A3DCheckerboardStillFitsSixteenBitIndices()
        {
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
                if (((x + y + z) & 1) == 0) world.Set(x, y, z, Stone);
            world.Run();
            Assert.AreEqual(2048 * 24, world.VertexCount);
            Assert.AreEqual(IndexFormat.UInt16, world.IndexFormat);
        }

        [Test]
        public void MoreThan65535VerticesSwitchToThirtyTwoBitIndices()
        {
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
                world.Set(x, y, z, ((x + y + z) & 1) == 0 ? Glass : Leaves);
            world.Run();
            Assert.AreEqual(4096 * 24, world.VertexCount);
            Assert.AreEqual(IndexFormat.UInt32, world.IndexFormat);
        }

        static float3 Position(SectionVertex vertex) => new(vertex.X, vertex.Y, vertex.Z);
    }
}
