using NUnit.Framework;
using reromanlee.BlockyMesher.Storage;
using Unity.Mathematics;

namespace reromanlee.BlockyMesher.Tests
{
    public class StorageTests
    {
        TestBlocks blocks;
        BlockStorage storage;

        [SetUp]
        public void SetUp()
        {
            blocks = new TestBlocks();
            storage = new BlockStorage(4, blocks.Table);
        }

        [TearDown]
        public void TearDown()
        {
            storage.Dispose();
            blocks.Dispose();
        }

        [Test]
        public void SectionIndexRoundTrips()
        {
            for (int i = 0; i < Section.Volume; i++)
                Assert.AreEqual(i, Section.Index(Section.Local(i)));
        }

        [Test]
        public void ColumnOfRoundsDownForNegativePositions()
        {
            Assert.AreEqual(new int2(0, 0), Section.ColumnOf(new int3(15, 0, 0)));
            Assert.AreEqual(new int2(1, 0), Section.ColumnOf(new int3(16, 0, 0)));
            Assert.AreEqual(new int2(-1, -2), Section.ColumnOf(new int3(-1, 0, -17)));
        }

        [Test]
        public void BlocksReadBackFromAnyColumn()
        {
            int3[] positions = { new(-20, 5, 33), new(0, 0, 0), new(15, 63, 15) };
            foreach (int3 position in positions)
                Assert.IsTrue(storage.SetBlock(position, TestBlocks.Stone, createColumn: true).Changed);
            foreach (int3 position in positions)
                Assert.AreEqual(TestBlocks.Stone, storage.GetBlock(position));
            Assert.AreEqual(TestBlocks.Air, storage.GetBlock(new int3(1, 0, 0)));
        }

        [Test]
        public void BlocksOutsideTheWorldHeightAreIgnored()
        {
            Assert.IsFalse(storage.SetBlock(new int3(0, storage.Height, 0), TestBlocks.Stone, createColumn: true).Changed);
            Assert.IsFalse(storage.SetBlock(new int3(0, -1, 0), TestBlocks.Stone, createColumn: true).Changed);
            Assert.AreEqual(TestBlocks.Air, storage.GetBlock(new int3(0, storage.Height, 0)));
        }

        [Test]
        public void MissingColumnsAreOnlyCreatedWhenAsked()
        {
            Assert.IsFalse(storage.SetBlock(new int3(100, 0, 0), TestBlocks.Stone, createColumn: false).Changed);
            Assert.AreEqual(0, storage.ColumnCount);
            Assert.IsTrue(storage.SetBlock(new int3(100, 0, 0), TestBlocks.Stone, createColumn: true).Changed);
            Assert.AreEqual(1, storage.ColumnCount);
        }

        [Test]
        public void UniformSectionsOnlyAllocateOnceMixed()
        {
            Column column = storage.AddColumn(int2.zero);
            Assert.AreEqual(0, storage.MixedSectionCount);

            storage.SetBlock(new int3(3, 3, 3), TestBlocks.Stone, createColumn: false);
            Assert.AreEqual(1, storage.MixedSectionCount);

            storage.SetBlock(new int3(3, 3, 3), TestBlocks.Air, createColumn: false);
            Assert.IsTrue(storage.TryCompact(column, 0));
            Assert.AreEqual(0, storage.MixedSectionCount);
            Assert.AreEqual(1, storage.PooledArrayCount);
        }

        [Test]
        public void SkyStartFollowsTheHighestLightBlocker()
        {
            Column column = storage.AddColumn(int2.zero);
            int SkyStart() => column.SkyStart[0];

            storage.SetBlock(new int3(0, 10, 0), TestBlocks.Stone, false);
            Assert.AreEqual(11, SkyStart());
            storage.SetBlock(new int3(0, 5, 0), TestBlocks.Stone, false);
            Assert.AreEqual(11, SkyStart());
            storage.SetBlock(new int3(0, 20, 0), TestBlocks.Glass, false);
            Assert.AreEqual(11, SkyStart(), "glass lets light through");
            storage.SetBlock(new int3(0, 30, 0), TestBlocks.Leaves, false);
            Assert.AreEqual(31, SkyStart(), "leaves block light");

            storage.SetBlock(new int3(0, 30, 0), TestBlocks.Air, false);
            Assert.AreEqual(11, SkyStart());
            storage.SetBlock(new int3(0, 10, 0), TestBlocks.Air, false);
            Assert.AreEqual(6, SkyStart());
            storage.SetBlock(new int3(0, 5, 0), TestBlocks.Air, false);
            Assert.AreEqual(0, SkyStart());
        }

        [Test]
        public void EditsReportHowTheSkyChanged()
        {
            storage.AddColumn(int2.zero);
            BlockEdit edit = storage.SetBlock(new int3(2, 40, 2), TestBlocks.Stone, false);
            Assert.AreEqual(0, edit.OldSkyStart);
            Assert.AreEqual(41, edit.NewSkyStart);
        }

        [Test]
        public void FillingWholeSectionsKeepsThemUniform()
        {
            storage.Fill(new int3(0, 0, 0), new int3(16, 32, 16), TestBlocks.Stone, createColumns: true);
            Assert.AreEqual(0, storage.MixedSectionCount);
            storage.TryGetColumn(int2.zero, out Column column);
            Assert.AreEqual(TestBlocks.Stone, column.Sections[0].UniformId);
            Assert.AreEqual(TestBlocks.Stone, column.Sections[1].UniformId);
            Assert.AreEqual(32, column.SkyStart[0]);
        }

        [Test]
        public void FillingPartOfASectionMixesIt()
        {
            storage.Fill(new int3(-4, 2, -4), new int3(4, 6, 4), TestBlocks.Dirt, createColumns: true);
            Assert.AreEqual(4, storage.ColumnCount);
            Assert.AreEqual(4, storage.MixedSectionCount);
            Assert.AreEqual(TestBlocks.Dirt, storage.GetBlock(new int3(-4, 2, -4)));
            Assert.AreEqual(TestBlocks.Dirt, storage.GetBlock(new int3(3, 5, 3)));
            Assert.AreEqual(TestBlocks.Air, storage.GetBlock(new int3(4, 5, 3)));
            Assert.AreEqual(TestBlocks.Air, storage.GetBlock(new int3(3, 6, 3)));
        }

        [Test]
        public void RemovedColumnsReturnTheirArraysToThePool()
        {
            storage.Fill(new int3(0, 0, 0), new int3(8, 40, 8), TestBlocks.Stone, createColumns: true);
            Assert.AreEqual(3, storage.MixedSectionCount);
            storage.RemoveColumn(int2.zero);
            Assert.AreEqual(0, storage.MixedSectionCount);
            Assert.AreEqual(3, storage.PooledArrayCount);
            Assert.AreEqual(TestBlocks.Air, storage.GetBlock(new int3(1, 1, 1)));
        }
    }
}
