using System.IO;
using NUnit.Framework;
using reromanlee.BlockyMesher.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.BlockyMesher.Tests
{
    public class RenderingTests
    {
        [Test]
        public void AmbientOcclusionIsSeamlessBetweenFacesSideBySide()
        {
            // Two faces side by side, A and then B at +U, see a 4 × 3 strip of blocks in front of them:
            // columns -1..2, rows -1..1. Their own front blocks (0, 0) and (1, 0) are open.
            for (int solid = 0; solid < 1 << 10; solid++)
            {
                int[] strip = new int[12];
                int bit = 0;
                for (int i = 0; i < 12; i++)
                    if (i != 5 && i != 6) strip[i] = (solid >> bit++) & 1;

                int A = CaseAround(strip, 1), B = CaseAround(strip, 2);
                for (float v = 0.05f; v < 1; v += 0.1f)
                    Assert.AreEqual(AmbientOcclusionBaker.Sample(A, 1, v), AmbientOcclusionBaker.Sample(B, 0, v), 1e-4f, $"strip {solid}, v = {v}");
            }
        }

        [Test]
        public void AmbientOcclusionIsSeamlessBetweenFacesAboveEachOther()
        {
            // The same check along V: face A and face B at +V. Swapping the roles of u and v in the
            // case turns the vertical neighbor into a horizontal one.
            for (int occlusionCase = 0; occlusionCase < 256; occlusionCase++)
            for (float t = 0.05f; t < 1; t += 0.1f)
            {
                Assert.AreEqual(AmbientOcclusionBaker.Sample(occlusionCase, t, 0.3f), AmbientOcclusionBaker.Sample(Transpose(occlusionCase), 0.3f, t), 1e-4f);
            }
        }

        [Test]
        public void OpenFacesAreFullyLitAndSolidEdgesDarken()
        {
            Assert.AreEqual(1f, AmbientOcclusionBaker.Sample(0, 0.01f, 0.01f), 1e-4f);
            Assert.Less(AmbientOcclusionBaker.Sample(1 << 3, 0.01f, 0.5f), 0.6f, "left edge");
            Assert.AreEqual(1f, AmbientOcclusionBaker.Sample(1 << 3, 0.99f, 0.5f), 1e-4f, "the shadow ends before the other edge");
            float innerCorner = AmbientOcclusionBaker.Sample(1 << 3 | 1 << 1, 0.01f, 0.01f);
            Assert.Less(innerCorner, AmbientOcclusionBaker.Sample(1 << 3, 0.01f, 0.01f), "two edges darken more than one");
        }

        [Test]
        public void TheBakedPngMatchesTheSampleFunction()
        {
            var png = new Texture2D(2, 2);
            png.LoadImage(File.ReadAllBytes(Path.GetFullPath(BlockRegistry.AmbientOcclusionPath)));
            try
            {
                Assert.AreEqual(256, png.width);
                foreach (int occlusionCase in new[] { 0, 1, 16, 90, 255 })
                {
                    Vector2Int origin = AmbientOcclusionBaker.TileOrigin(occlusionCase);
                    int textureY = png.height - origin.y - AmbientOcclusionBaker.TileSize;
                    for (int y = 0; y < 16; y += 5)
                    for (int x = 0; x < 16; x += 5)
                    {
                        float expected = AmbientOcclusionBaker.Sample(occlusionCase, (x + 0.5f) / 16, (y + 0.5f) / 16);
                        Assert.AreEqual(expected, png.GetPixel(origin.x + x, textureY + y).r, 1 / 255f + 1e-4f, $"case {occlusionCase} at {x}, {y}");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(png);
            }
        }

        [Test]
        public void TheImportedArrayHasOneLayerPerCaseInGridOrder()
        {
            var array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(BlockRegistry.AmbientOcclusionPath);
            Assert.IsNotNull(array, "BlockOcclusion.png must import as a 2D array");
            Assert.AreEqual(256, array.depth);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Reading layers back needs a graphics device.");

            // Case 1 (only the -U, -V corner solid) is dark bottom-left and lit top-right; if the layer
            // order were flipped the same layer would hold case 241 or 16, which don't match that.
            float[] layer = ReadLayer(array, 1);
            Assert.Less(layer[0], 0.8f);
            Assert.AreEqual(1f, layer[15 * 16 + 15], 2 / 255f);
        }

        [Test]
        public void EachRenderPassGetsItsOwnMaterialSetup()
        {
            var registry = ScriptableObject.CreateInstance<BlockRegistry>();
            try
            {
                Material opaque = registry.GetMaterial(RenderPass.Opaque);
                Material cutout = registry.GetMaterial(RenderPass.Cutout);
                Material transparent = registry.GetMaterial(RenderPass.Transparent);
                Assert.AreEqual(BlockRegistry.ShaderName, opaque.shader.name);
                Assert.AreEqual((int)RenderQueue.Geometry, opaque.renderQueue);
                Assert.IsFalse(opaque.IsKeywordEnabled("_ALPHATEST_ON"));
                Assert.IsTrue(cutout.IsKeywordEnabled("_ALPHATEST_ON"));
                Assert.AreEqual((int)RenderQueue.Transparent, transparent.renderQueue);
                Assert.AreEqual(0, transparent.GetFloat("_ZWrite"));
                Assert.AreSame(opaque, registry.GetMaterial(RenderPass.Opaque), "materials are shared");
            }
            finally
            {
                Object.DestroyImmediate(registry);
            }
        }

        // Row by row from (-1, -1), skipping the center: the bit order of MeshJob.OcclusionCase.
        static int CaseAround(int[] strip, int centerColumn)
        {
            int occlusionCase = 0, bit = 0;
            for (int dv = -1; dv <= 1; dv++)
            for (int du = -1; du <= 1; du++)
            {
                if (du == 0 && dv == 0) continue;
                if (strip[(dv + 1) * 4 + centerColumn + du] != 0) occlusionCase |= 1 << bit;
                bit++;
            }
            return occlusionCase;
        }

        static int Transpose(int occlusionCase)
        {
            int[] cells = new int[9];
            int bit = 0;
            for (int i = 0; i < 9; i++)
                if (i != 4) cells[i] = (occlusionCase >> bit++) & 1;
            int result = 0;
            bit = 0;
            for (int dv = 0; dv < 3; dv++)
            for (int du = 0; du < 3; du++)
            {
                if (du == 1 && dv == 1) continue;
                if (cells[du * 3 + dv] != 0) result |= 1 << bit;
                bit++;
            }
            return result;
        }

        static float[] ReadLayer(Texture2DArray array, int layer)
        {
            var target = RenderTexture.GetTemporary(array.width, array.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(array.width, array.height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(array, target, layer, 0);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, array.width, array.height), 0, 0);
                RenderTexture.active = previous;
                Color[] pixels = readback.GetPixels();
                var values = new float[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                    values[i] = pixels[i].r;
                return values;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readback);
            }
        }
    }
}
