using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static reromanlee.BlockyMesher.Tests.TestBlocks;

namespace reromanlee.BlockyMesher.Tests
{
    public class LandscapeTests
    {
        TestBlocks blocks;
        GameObject gameObject;
        Landscape landscape;

        [SetUp]
        public void SetUp()
        {
            blocks = new TestBlocks();
            gameObject = new GameObject("Landscape");
            landscape = gameObject.AddComponent<Landscape>();
            landscape.Registry = blocks.Registry;
            landscape.SectionsPerColumn = 2;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gameObject);
            blocks.Dispose();
        }

        List<SectionObject> Sections => landscape.Builder.Objects.ToList();

        [Test]
        public void AssigningARegistryMakesItReady()
        {
            Assert.IsTrue(landscape.IsReady);
            Assert.AreEqual(32, landscape.Height);
        }

        [Test]
        public void ALandscapeWithoutARegistryIgnoresEdits()
        {
            var empty = new GameObject("Empty").AddComponent<Landscape>();
            try
            {
                Assert.IsFalse(empty.IsReady);
                Assert.IsFalse(empty.SetBlock(Vector3Int.zero, Stone));
                Assert.AreEqual(0, empty.GetBlock(Vector3Int.zero));
            }
            finally
            {
                Object.DestroyImmediate(empty.gameObject);
            }
        }

        [Test]
        public void ABlockGetsASectionWithSixFaces()
        {
            landscape.SetBlock(new Vector3Int(1, 2, 3), Stone);
            Assert.AreEqual(1, Sections.Count);
            Assert.AreEqual(24, Sections[0].Mesh.vertexCount);
            Assert.AreEqual(Stone, landscape.GetBlock(new Vector3Int(1, 2, 3)));
        }

        [Test]
        public void SectionsSitAtTheirPosition()
        {
            landscape.SetBlock(new Vector3Int(-5, 20, 40), Stone);
            Assert.AreEqual(new Vector3(-16, 16, 32), Sections.Single().GameObject.transform.localPosition);
        }

        [Test]
        public void RemovingTheLastBlockHidesItsSection()
        {
            landscape.SetBlock(Vector3Int.one, Stone);
            landscape.SetBlock(Vector3Int.one, 0);
            Assert.AreEqual(0, Sections.Count);
        }

        [Test]
        public void EditsAcrossABorderUpdateBothSections()
        {
            landscape.SetBlock(new Vector3Int(15, 5, 5), Stone);
            landscape.SetBlock(new Vector3Int(16, 5, 5), Stone);
            Assert.AreEqual(2, Sections.Count);
            foreach (SectionObject section in Sections)
                Assert.AreEqual(5 * 4, section.Mesh.vertexCount);
        }

        [Test]
        public void OnlyRenderPassesWithFacesGetAMaterial()
        {
            landscape.SetBlock(new Vector3Int(1, 1, 1), Stone);
            Assert.AreEqual(1, Sections.Single().GameObject.GetComponent<MeshRenderer>().sharedMaterials.Length);

            landscape.SetBlock(new Vector3Int(5, 1, 1), Glass);
            Material[] materials = Sections.Single().GameObject.GetComponent<MeshRenderer>().sharedMaterials;
            Assert.AreEqual(2, materials.Length);
            Assert.AreSame(blocks.Registry.GetMaterial(RenderPass.Opaque), materials[0]);
            Assert.AreSame(blocks.Registry.GetMaterial(RenderPass.Cutout), materials[1]);
        }

        [Test]
        public void FillingWholeSectionsKeepsThemCheap()
        {
            landscape.Fill(new BoundsInt(0, 0, 0, 16, 16, 16), Stone);
            Assert.AreEqual(0, landscape.Storage.MixedSectionCount);
            Assert.AreEqual(6 * 16 * 16 * 4, Sections.Single().Mesh.vertexCount);
        }

        [Test]
        public void APatternRoundTrips()
        {
            for (int i = 0; i < 200; i++)
                landscape.SetBlock(new Vector3Int(i * 7 % 13, i * 3 % 11, i * 5 % 17), (ushort)(i % 3 == 0 ? Dirt : Stone));
            var area = new BoundsInt(0, 0, 0, 13, 11, 17);
            BlockPattern pattern = landscape.Export(area);
            try
            {
                var copy = new GameObject("Copy").AddComponent<Landscape>();
                copy.Registry = blocks.Registry;
                copy.Place(pattern, new Vector3Int(-40, 3, 9));
                foreach (Vector3Int position in area.allPositionsWithin)
                    Assert.AreEqual(landscape.GetBlock(position), copy.GetBlock(position + new Vector3Int(-40, 3, 9)), $"{position}");
                Object.DestroyImmediate(copy.gameObject);
            }
            finally
            {
                Object.DestroyImmediate(pattern);
            }
        }

        [Test]
        public void PatternsStoreOnlyTheBitsTheyNeed()
        {
            var twoKinds = new ushort[16 * 16 * 16];
            for (int i = 0; i < twoKinds.Length; i++)
                twoKinds[i] = (ushort)(i % 2 == 0 ? Stone : 0);
            BlockPattern pattern = BlockPattern.Create(new Vector3Int(16, 16, 16), twoKinds);
            BlockPattern uniform = BlockPattern.Create(new Vector3Int(16, 16, 16), new ushort[16 * 16 * 16]);
            try
            {
                Assert.LessOrEqual(pattern.CompressedBytes, twoKinds.Length / 8 + 16, "one bit per block");
                Assert.LessOrEqual(uniform.CompressedBytes, 16, "a single kind of block takes no bits");
                Assert.AreEqual(Stone, pattern.GetBlock(2, 0, 0));
                Assert.AreEqual(0, pattern.GetBlock(1, 0, 0));
            }
            finally
            {
                Object.DestroyImmediate(pattern);
                Object.DestroyImmediate(uniform);
            }
        }

        [Test]
        public void PlacingLeavesBlocksAloneWhereThePatternIsAir()
        {
            landscape.SetBlock(Vector3Int.zero, Stone);
            BlockPattern pattern = BlockPattern.Create(new Vector3Int(2, 1, 1), new ushort[] { 0, Dirt });
            try
            {
                landscape.Place(pattern, Vector3Int.zero);
                Assert.AreEqual(Stone, landscape.GetBlock(Vector3Int.zero));
                Assert.AreEqual(Dirt, landscape.GetBlock(Vector3Int.right));

                landscape.Place(pattern, Vector3Int.zero, includeAir: true);
                Assert.AreEqual(0, landscape.GetBlock(Vector3Int.zero));
            }
            finally
            {
                Object.DestroyImmediate(pattern);
            }
        }

        [Test]
        public void TheStartingPatternIsPlacedOnStartup()
        {
            BlockPattern pattern = BlockPattern.Create(new Vector3Int(1, 2, 1), new ushort[] { Stone, Dirt });
            try
            {
                landscape.Pattern = pattern;
                Assert.AreEqual(Stone, landscape.GetBlock(Vector3Int.zero));
                Assert.AreEqual(Dirt, landscape.GetBlock(Vector3Int.up));
                Assert.AreEqual(1, Sections.Count);
            }
            finally
            {
                Object.DestroyImmediate(pattern);
            }
        }

        [Test]
        public void RaycastFindsTheBlockAndTheFaceItEntered()
        {
            landscape.SetBlock(new Vector3Int(5, 5, 5), Stone);
            Assert.IsTrue(landscape.Raycast(new Ray(new Vector3(5.5f, 10, 5.5f), Vector3.down), 20, out BlockHit hit));
            Assert.AreEqual(new Vector3Int(5, 5, 5), hit.Block);
            Assert.AreEqual(Vector3Int.up, hit.Normal);
            Assert.AreEqual(new Vector3Int(5, 6, 5), hit.Adjacent);
            Assert.AreEqual(4, hit.Distance, 1e-4f);
            Assert.AreEqual(6, hit.Point.y, 1e-4f);
            Assert.IsFalse(landscape.Raycast(new Ray(new Vector3(5.5f, 10, 5.5f), Vector3.down), 3, out _), "too short");
            Assert.IsFalse(landscape.Raycast(new Ray(new Vector3(9.5f, 10, 5.5f), Vector3.down), 20, out _), "nothing there");
        }

        [Test]
        public void RaycastFollowsTheTransform()
        {
            gameObject.transform.SetPositionAndRotation(new Vector3(100, -3, 7), Quaternion.Euler(0, 90, 0));
            landscape.SetBlock(new Vector3Int(5, 5, 5), Stone);
            Vector3 center = landscape.BlockToWorld(new Vector3Int(5, 5, 5));
            Assert.AreEqual(new Vector3Int(5, 5, 5), landscape.WorldToBlock(center));
            Assert.IsTrue(landscape.Raycast(new Ray(center + Vector3.up * 5, Vector3.down), 20, out BlockHit hit));
            Assert.AreEqual(new Vector3Int(5, 5, 5), hit.Block);
            Assert.AreEqual(Vector3Int.up, hit.Normal);
        }

        [Test]
        public void BoxCollidersCoverTheBlocksWithFewBoxes()
        {
            landscape.Colliders = ColliderMode.Boxes;
            landscape.Fill(new BoundsInt(0, 0, 0, 3, 1, 3), Stone);
            landscape.SetBlock(new Vector3Int(1, 1, 1), Stone);
            BoxCollider[] boxes = Sections.Single().GameObject.GetComponents<BoxCollider>().Where(box => box.enabled).ToArray();
            Assert.AreEqual(2, boxes.Length);
            Assert.AreEqual(new Vector3(3, 1, 3), boxes[0].size);
            Assert.AreEqual(new Vector3(1.5f, 0.5f, 1.5f), boxes[0].center);
            Assert.AreEqual(Vector3.one, boxes[1].size);
        }

        [Test]
        public void MeshCollidersMergeFacesIntoRectangles()
        {
            landscape.Colliders = ColliderMode.Mesh;
            landscape.Fill(new BoundsInt(0, 0, 0, 4, 1, 4), Stone);
            var collider = Sections.Single().GameObject.GetComponent<MeshCollider>();
            Assert.IsNotNull(collider);
            Assert.IsTrue(collider.enabled);
            Assert.AreEqual(6 * 4, collider.sharedMesh.vertexCount, "one rectangle per side of the slab");
        }

        [Test]
        public void WaterDoesNotCollide()
        {
            landscape.Colliders = ColliderMode.Boxes;
            landscape.SetBlock(new Vector3Int(1, 1, 1), Water);
            Assert.AreEqual(0, Sections.Single().GameObject.GetComponents<BoxCollider>().Count(box => box.enabled));
        }

        [Test]
        public void CracksShowOverABlockAndHide()
        {
            landscape.SetBlock(new Vector3Int(2, 3, 4), Stone);
            landscape.ShowCrack(new Vector3Int(2, 3, 4), 0.5f);
            Transform cracks = gameObject.transform.Cast<Transform>().Single(child => child.name == "Cracks");
            Assert.IsTrue(cracks.gameObject.activeSelf);
            Assert.AreEqual(new Vector3(2.5f, 3.5f, 4.5f), cracks.localPosition);
            landscape.HideCrack();
            Assert.IsFalse(cracks.gameObject.activeSelf);
        }

        [Test]
        public void EditsAreReported()
        {
            var changes = new List<(int3, ushort)>();
            landscape.BlockChanged += (position, id) => changes.Add((position, id));
            landscape.SetBlock(new Vector3Int(1, 2, 3), Dirt);
            landscape.SetBlock(new Vector3Int(1, 2, 3), Dirt);
            Assert.AreEqual(1, changes.Count, "setting the same block again changes nothing");
            Assert.AreEqual((new int3(1, 2, 3), Dirt), changes[0]);
        }

        [Test]
        public void BlockBoundsCoverEveryBlock()
        {
            Assert.IsFalse(landscape.TryGetBlockBounds(out _));
            landscape.SetBlock(new Vector3Int(-3, 2, 5), Stone);
            landscape.SetBlock(new Vector3Int(10, 20, -7), Dirt);
            Assert.IsTrue(landscape.TryGetBlockBounds(out BoundsInt bounds));
            Assert.AreEqual(new Vector3Int(-3, 2, -7), bounds.min);
            Assert.AreEqual(new Vector3Int(14, 19, 13), bounds.size);
        }

        [Test]
        public void StatsCountMeshesAndTheirMemory()
        {
            landscape.SetBlock(new Vector3Int(1, 1, 1), Stone);
            LandscapeStats stats = landscape.GetStats();
            Assert.AreEqual(1, stats.Columns);
            Assert.AreEqual(1, stats.SectionMeshes);
            Assert.AreEqual(1, stats.DrawCalls);
            Assert.AreEqual(24, stats.Vertices);
            Assert.AreEqual(12, stats.Triangles);
            Assert.AreEqual(24 * 12 + 36 * 2, stats.MeshBytes, "12-byte vertices and 16-bit indices");
            Assert.AreEqual(Section.Volume * 2 + Section.Area * 2, stats.BlockBytes, "one block array and one sky start map");
            Assert.Greater(stats.WorkBufferBytes, 0);
            StringAssert.Contains("draw calls", stats.ToString());
        }

        [Test]
        public void SectionMeshesDropTheirCpuCopy()
        {
            landscape.SetBlock(new Vector3Int(1, 1, 1), Stone);
            Assert.IsFalse(Sections.Single().Mesh.isReadable);
        }

        [Test]
        public void ClearingRemovesEveryBlockAndSection()
        {
            landscape.Fill(new BoundsInt(-20, 0, -20, 40, 20, 40), Stone);
            landscape.Clear();
            Assert.AreEqual(0, landscape.Storage.ColumnCount);
            Assert.AreEqual(0, Sections.Count);
        }
    }
}
