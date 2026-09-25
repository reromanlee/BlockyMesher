using System.Linq;
using NUnit.Framework;
using reromanlee.BlockyMesher.Generation;
using reromanlee.BlockyMesher.Storage;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static reromanlee.BlockyMesher.Tests.TestBlocks;

namespace reromanlee.BlockyMesher.Tests
{
    public class WorldTests
    {
        const int Height = 64;

        TestBlocks blocks;
        NoiseTerrainStep terrain;
        TerrainGenerator generator;
        GameObject world;
        GameObject player;
        Landscape landscape;
        LandscapeStreamer streamer;

        [SetUp]
        public void SetUp()
        {
            blocks = new TestBlocks();
            terrain = blocks.Create<NoiseTerrainStep>();
            terrain.surface = blocks.Registry.blocks.First(block => block.id == Dirt);
            terrain.subsurface = terrain.surface;
            terrain.stone = blocks.Registry.blocks.First(block => block.id == Stone);
            terrain.baseHeight = 24;
            terrain.amplitude = 6;
            generator = blocks.Create<TerrainGenerator>();
            generator.steps = new GenerationStep[] { terrain };
            generator.seed = 7;

            world = new GameObject("World");
            landscape = world.AddComponent<Landscape>();
            landscape.Registry = blocks.Registry;
            landscape.SectionsPerColumn = Height / Section.Size;
            landscape.SolidBelowWorld = true;
            streamer = world.AddComponent<LandscapeStreamer>();
            streamer.Generator = generator;
            streamer.ViewRadius = 2;
            player = new GameObject("Player");
            player.transform.position = new Vector3(8, 40, 8);
            streamer.FocusPoints.Add(player.transform);
            streamer.Attach();
        }

        [TearDown]
        public void TearDown()
        {
            streamer.Detach();
            Object.DestroyImmediate(world);
            Object.DestroyImmediate(player);
            blocks.Dispose();
        }

        ushort[] Generate(int2 column, uint seed)
        {
            generator.seed = seed;
            using var buffer = new NativeArray<ushort>(Height * Section.Area, Allocator.TempJob);
            JobHandle cleared = new ClearBlocksJob { Blocks = buffer }.Schedule();
            generator.Schedule(new ColumnContext { Position = column, Height = Height }, buffer, cleared).Complete();
            return buffer.ToArray();
        }

        [Test]
        public void GenerationIsDeterministic()
        {
            CollectionAssert.AreEqual(Generate(new int2(3, -5), 7), Generate(new int2(3, -5), 7));
            CollectionAssert.AreNotEqual(Generate(new int2(3, -5), 7), Generate(new int2(3, -5), 8));
            CollectionAssert.AreNotEqual(Generate(new int2(3, -5), 7), Generate(new int2(4, -5), 7));
        }

        [Test]
        public void TerrainIsLayeredUnderOpenSky()
        {
            terrain.subsurface = blocks.Registry.blocks.First(block => block.id == Leaves);
            ushort[] column = Generate(new int2(0, 0), 7);
            for (int z = 0; z < 16; z += 5)
            for (int x = 0; x < 16; x += 5)
            {
                int top = Height - 1;
                while (column[ColumnContext.Index(x, top, z)] == Air) top--;
                Assert.That(top, Is.InRange(24 - 6, 24 + 6), "within the hills' amplitude");
                Assert.AreEqual(Dirt, column[ColumnContext.Index(x, top, z)], "surface");
                Assert.AreEqual(Leaves, column[ColumnContext.Index(x, top - 1, z)], "subsurface");
                Assert.AreEqual(Stone, column[ColumnContext.Index(x, top - 5, z)], "stone");
            }
        }

        [Test]
        public void ColumnSummaryFindsUniformSectionsAndTheSky()
        {
            ushort[] generated = Generate(new int2(1, 1), 7);
            using var blocksArray = new NativeArray<ushort>(generated, Allocator.TempJob);
            using var uniformIds = new NativeArray<int>(Height / Section.Size, Allocator.TempJob);
            using var skyStart = new NativeArray<ushort>(Section.Area, Allocator.TempJob);
            new ColumnSummaryJob
            {
                Blocks = blocksArray,
                BlockInfos = blocks.Table.Blocks,
                SectionCount = Height / Section.Size,
                UniformIds = uniformIds,
                SkyStart = skyStart,
            }.Schedule().Complete();

            Assert.AreEqual(Air, uniformIds[3], "the top section is all sky");
            Assert.AreEqual(-1, uniformIds[1], "the surface runs through section 1");
            int top = Height - 1;
            while (generated[ColumnContext.Index(0, top, 0)] == Air) top--;
            Assert.AreEqual(top + 1, skyStart[0]);
        }

        [Test]
        public void StreamingLoadsTheColumnsAroundThePlayer()
        {
            streamer.LoadEverything();
            int expected = 0;
            for (int z = -6; z <= 6; z++)
            for (int x = -6; x <= 6; x++)
                if (math.distance(new float2(x + 0.5f, z + 0.5f), new float2(0.5f, 0.5f)) <= 2 + 1.5f) expected++;
            Assert.AreEqual(expected, streamer.LoadedColumnCount);

            foreach (SectionObject section in landscape.Builder.Objects)
                Assert.LessOrEqual(math.distance((float2)section.Position.xz + 0.5f, new float2(0.5f, 0.5f)), 2, "only columns in view radius get meshes");
            Assert.Greater(landscape.Builder.Objects.Count, 0);
        }

        [Test]
        public void TheLandscapeCanShutDownWhileColumnsAreGenerating()
        {
            streamer.Tick();
            Assume.That(streamer.GeneratingCount, Is.GreaterThan(0), "needs worker threads to have jobs in flight");
            landscape.enabled = false;
            Assert.AreEqual(0, streamer.GeneratingCount);

            landscape.enabled = true;
            streamer.LoadEverything();
            Assert.Greater(streamer.LoadedColumnCount, 0);
        }

        [Test]
        public void EditsSurviveLeavingAndComingBack()
        {
            streamer.LoadEverything();
            var edited = new Vector3Int(3, 50, 3);
            Assert.IsTrue(landscape.SetBlock(edited, Glass));

            player.transform.position = new Vector3(2000, 40, 8);
            streamer.LoadEverything();
            Assert.IsFalse(streamer.IsLoaded(new Vector3(3, 0, 3)), "the column was unloaded");
            Assert.AreEqual(Air, landscape.GetBlock(edited));

            player.transform.position = new Vector3(8, 40, 8);
            streamer.LoadEverything();
            Assert.AreEqual(Glass, landscape.GetBlock(edited));
        }

        [Test]
        public void EditsOutsideLoadedColumnsAreIgnored()
        {
            streamer.LoadEverything();
            Assert.IsFalse(landscape.SetBlock(new Vector3Int(5000, 30, 0), Stone));
        }

        [Test]
        public void ReshapedSectionsAreKeptAsCompressedCopies()
        {
            streamer.LoadEverything();
            landscape.Fill(new BoundsInt(0, 16, 0, 16, 16, 16), Glass);
            landscape.SetBlock(new Vector3Int(20, 40, 3), Stone);

            player.transform.position = new Vector3(2000, 40, 8);
            streamer.LoadEverything();
            Assert.IsTrue(streamer.Edits.IsKeptAsCopy(new int3(0, 1, 0)));
            Assert.IsFalse(streamer.Edits.IsKeptAsCopy(new int3(1, 2, 0)), "a single edit stays a list entry");
            Assert.Less(streamer.EditBytes, 1024, "a uniform section compresses to almost nothing");
        }

        [Test]
        public void SavedEditsLoadBack()
        {
            streamer.LoadEverything();
            var edited = new Vector3Int(4, 45, 4);
            landscape.SetBlock(edited, RedLamp);
            landscape.Fill(new BoundsInt(-8, 40, -8, 3, 3, 3), Glass);
            byte[] saved = streamer.SaveEdits();

            streamer.ClearEdits();
            streamer.LoadEverything();
            Assert.AreEqual(Air, landscape.GetBlock(edited));

            streamer.LoadEdits(saved);
            streamer.LoadEverything();
            Assert.AreEqual(RedLamp, landscape.GetBlock(edited));
            Assert.AreEqual(Glass, landscape.GetBlock(new Vector3Int(-7, 41, -7)));
        }

        [Test]
        public void CollidersOnlyNearThePlayer()
        {
            landscape.Colliders = ColliderMode.Boxes;
            streamer.PhysicsRadius = 0;
            streamer.LoadEverything();
            foreach (SectionObject section in landscape.Builder.Objects)
            {
                bool hasColliders = section.GameObject.GetComponents<BoxCollider>().Any(box => box.enabled);
                Assert.AreEqual(section.Position.x == 0 && section.Position.z == 0, hasColliders, $"section {section.Position}");
            }
        }
    }
}
