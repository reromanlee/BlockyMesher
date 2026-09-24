using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace reromanlee.BlockyMesher.Tests
{
    public class RegistryTests
    {
        TestBlocks blocks;

        [SetUp] public void SetUp() => blocks = new TestBlocks();
        [TearDown] public void TearDown() => blocks.Dispose();

        [Test]
        public void AirIsBuiltInAsIdZero()
        {
            BlockInfo air = blocks.Table[TestBlocks.Air];
            Assert.IsFalse(air.Is(BlockFlags.Visible));
            Assert.IsTrue(air.Is(BlockFlags.LightPasses));
            Assert.IsFalse(air.Is(BlockFlags.Collidable));
        }

        [Test]
        public void IdsWithoutABlockReadAsAir()
        {
            Assert.AreEqual(BlockInfo.Air.Flags, blocks.Table[999].Flags);
        }

        [Test]
        public void RenderPassDecidesOpacity()
        {
            Assert.IsTrue(blocks.Table[TestBlocks.Stone].Is(BlockFlags.Opaque));
            Assert.IsFalse(blocks.Table[TestBlocks.Glass].Is(BlockFlags.Opaque));
            Assert.IsTrue(blocks.Table[TestBlocks.Glass].Is(BlockFlags.LightPasses));
            Assert.IsTrue(blocks.Table[TestBlocks.Glass].Is(BlockFlags.HideSameNeighbor));
            Assert.IsFalse(blocks.Table[TestBlocks.Leaves].Is(BlockFlags.LightPasses));
            Assert.IsFalse(blocks.Table[TestBlocks.Water].Is(BlockFlags.Collidable));
        }

        [Test]
        public void OpaqueBlocksNeverLetLightThrough()
        {
            BlockData block = blocks.Block("Odd", 9, RenderPass.Opaque, lightPasses: true);
            blocks.Registry.blocks = new[] { block };
            using var table = new DisposableTable(blocks.Registry.Bake(Allocator.Temp));
            Assert.IsFalse(table.Value[9].Is(BlockFlags.LightPasses));
        }

        [Test]
        public void FaceTexturesAreBakedPerFace()
        {
            Assert.AreEqual(1, blocks.Table.FaceLayer(TestBlocks.Dirt, (int)Face.Right));
            Assert.AreEqual(2, blocks.Table.FaceLayer(TestBlocks.Dirt, (int)Face.Up));
            Assert.AreEqual(3, blocks.Table.FaceLayer(TestBlocks.Dirt, (int)Face.Down));
        }

        [Test]
        public void LightColorsAreCollectedIntoAPalette()
        {
            BlockInfo red = blocks.Table[TestBlocks.RedLamp];
            BlockInfo blue = blocks.Table[TestBlocks.BlueLamp];
            Assert.AreEqual(2, blocks.Table.LightColors.Length);
            Assert.AreNotEqual(red.LightColor, blue.LightColor);
            Assert.AreEqual(new Color32(255, 0, 0, 255), blocks.Table.LightColors[red.LightColor]);
            Assert.AreEqual(7, red.Emission);
        }

        [Test]
        public void DuplicateIdsAreReported()
        {
            blocks.Registry.blocks = new[]
            {
                blocks.Block("A", 5, RenderPass.Opaque),
                blocks.Block("B", 5, RenderPass.Opaque),
            };
            StringAssert.Contains("both use id 5", string.Join("\n", blocks.Registry.Validate()));
        }

        [Test]
        public void TooManyLightColorsAreReported()
        {
            var lamps = new BlockData[BlockRegistry.MaxLightColors + 1];
            for (int i = 0; i < lamps.Length; i++)
                lamps[i] = blocks.Block($"Lamp{i}", (ushort)(i + 1), RenderPass.Opaque, emission: 7, lightColor: new Color(i / 20f, 0, 0));
            blocks.Registry.blocks = lamps;
            StringAssert.Contains("different light colors", string.Join("\n", blocks.Registry.Validate()));
        }

        [Test]
        public void MissingTextureLayersAreReported()
        {
            blocks.Registry.textures = new Texture2DArray(4, 4, 8, TextureFormat.RGBA32, false);
            try
            {
                StringAssert.Contains("layer 10 doesn't exist", string.Join("\n", blocks.Registry.Validate()));
            }
            finally
            {
                Object.DestroyImmediate(blocks.Registry.textures);
            }
        }

        readonly struct DisposableTable : System.IDisposable
        {
            public readonly BlockTable Value;
            public DisposableTable(BlockTable value) => Value = value;
            public void Dispose() => Value.Dispose();
        }
    }
}
