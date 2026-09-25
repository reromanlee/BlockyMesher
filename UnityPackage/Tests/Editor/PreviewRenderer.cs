using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using reromanlee.BlockyMesher.Generation;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace reromanlee.BlockyMesher.Tests
{
    /// <summary>
    /// Renders a small showcase world to PNG files in the project's BlockyMesherPreviews folder, to look at lighting,
    /// ambient occlusion and textures after a change. Opt-in (run it from the Test Runner) and it
    /// needs URP as the active render pipeline.
    /// </summary>
    [Explicit, Category("Preview")]
    public class PreviewRenderer
    {
        const ushort Stone = 1, Grass = 2, Dirt = 3, Planks = 4, Glass = 5, Leaves = 6, RedLamp = 7, BlueLamp = 8, WarmLamp = 9, Log = 10, Chalk = 11;
        static readonly Color Sky = new(0.55f, 0.72f, 0.95f);

        readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created)
                if (item != null) Object.DestroyImmediate(item);
            created.Clear();
        }

        [Test]
        public void RenderPreviews()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                Assert.Ignore("The previews need URP as the active render pipeline.");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Sky;
            RenderSettings.fogStartDistance = 45;
            RenderSettings.fogEndDistance = 120;
            BlockLighting.SkyColor = new Color(1f, 0.97f, 0.9f);

            var landscapeObject = new GameObject("Preview Landscape");
            created.Add(landscapeObject);
            var landscape = landscapeObject.AddComponent<Landscape>();
            landscape.Registry = CreateRegistry();
            landscape.SectionsPerColumn = 3;
            landscape.SolidBelowWorld = true;

            var watch = Stopwatch.StartNew();
            Landscape.EditBatch batch = landscape.BatchEdits();
            BuildWorld(landscape);
            long editing = watch.ElapsedMilliseconds;
            long before = landscape.Builder.BuildsFinished;
            batch.Dispose();
            Debug.Log($"Edits took {editing} ms, then {landscape.Builder.BuildsFinished - before} sections were built in {watch.ElapsedMilliseconds - editing} ms");

            landscape.ShowCrack(new Vector3Int(-19, 9, -19), 0.7f);
            string folder = Path.GetFullPath("BlockyMesherPreviews");
            Directory.CreateDirectory(folder);
            Render(folder, "overview", new Vector3(-13, 25, -17), new Vector3(9, 10, 8));
            Render(folder, "overhang", new Vector3(6, 12.5f, -0.5f), new Vector3(6, 10, 6));
            Render(folder, "house", new Vector3(17.5f, 11.6f, 3.4f), new Vector3(17.5f, 10.5f, 8));
            Render(folder, "closeup", new Vector3(11.5f, 17, -4.5f), new Vector3(16, 10, 3));
            Render(folder, "crack", new Vector3(-20.8f, 11.2f, -21.2f), new Vector3(-18.5f, 9.5f, -18.5f));
            Render(folder, "ao", new Vector3(-21, 15, -24), new Vector3(-16, 9, -16));
            Debug.Log($"Previews written to {folder}");
        }

        [Test]
        public void RenderStreamedTerrain()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                Assert.Ignore("The previews need URP as the active render pipeline.");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Sky;
            RenderSettings.fogStartDistance = 50;
            RenderSettings.fogEndDistance = 100;
            BlockLighting.SkyColor = new Color(1f, 0.97f, 0.9f);

            BlockRegistry registry = CreateRegistry();
            var terrain = ScriptableObject.CreateInstance<NoiseTerrainStep>();
            created.Add(terrain);
            terrain.surface = registry.blocks[Grass - 1];
            terrain.subsurface = registry.blocks[Dirt - 1];
            terrain.stone = registry.blocks[Stone - 1];
            terrain.baseHeight = 40;
            terrain.amplitude = 18;
            terrain.scale = 80;
            terrain.caves = true;
            var generator = ScriptableObject.CreateInstance<TerrainGenerator>();
            created.Add(generator);
            generator.steps = new GenerationStep[] { terrain };
            generator.seed = 12345;

            var world = new GameObject("Streamed World");
            created.Add(world);
            var landscape = world.AddComponent<Landscape>();
            landscape.Registry = registry;
            landscape.SectionsPerColumn = 6;
            landscape.SolidBelowWorld = true;
            var streamer = world.AddComponent<LandscapeStreamer>();
            streamer.Generator = generator;
            streamer.ViewRadius = 6;
            var player = new GameObject("Player");
            created.Add(player);
            player.transform.position = new Vector3(8, 70, 8);
            streamer.FocusPoints.Add(player.transform);
            streamer.Attach();
            try
            {
                var watch = Stopwatch.StartNew();
                streamer.LoadEverything();
                Debug.Log($"Streamed {streamer.LoadedColumnCount} columns and built {landscape.Builder.BuildsFinished} sections " +
                    $"in {watch.ElapsedMilliseconds} ms, generation included; {landscape.Builder.Objects.Count} sections have a mesh");

                string folder = Path.GetFullPath("BlockyMesherPreviews");
                Directory.CreateDirectory(folder);
                Render(folder, "streamed", new Vector3(8, 80, 8), new Vector3(60, 45, 70));
                Vector3Int ground = new Vector3Int(8, landscape.Height - 1, 8);
                while (ground.y > 0 && landscape.GetBlock(ground) == 0) ground.y--;
                Render(folder, "streamed-ground", ground + new Vector3(0.5f, 2.7f, 0.5f), ground + new Vector3(40, 4, 25));

                // Find a cave near the player, light it with a lamp, and look into it.
                if (FindCave(landscape, out Vector3Int cave))
                {
                    landscape.SetBlock(cave + new Vector3Int(2, 0, 0), WarmLamp);
                    landscape.CompleteRebuilds();
                    Render(folder, "cave", cave + new Vector3(-1.5f, 0.6f, 0.5f), cave + new Vector3(3, 0, 0.5f));
                }
                Debug.Log($"Previews written to {folder}");
            }
            finally
            {
                streamer.Detach();
            }
        }

        /// <summary>An air block well below the surface with room around it, near the world's origin.</summary>
        static bool FindCave(Landscape landscape, out Vector3Int cave)
        {
            for (int radius = 0; radius < 40; radius++)
            for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            for (int y = 12; y < 40; y++)
            {
                cave = new Vector3Int(x, y, z);
                if (landscape.GetBlock(cave) != 0 || landscape.GetBlock(cave + Vector3Int.up * 6) == 0) continue;
                bool roomy = true;
                for (int dx = -1; dx <= 3 && roomy; dx++)
                    roomy = landscape.GetBlock(cave + new Vector3Int(dx, 0, 0)) == 0 && landscape.GetBlock(cave + new Vector3Int(dx, 1, 0)) == 0;
                if (roomy) return true;
            }
            cave = default;
            return false;
        }

        static void BuildWorld(Landscape landscape)
        {
            void Fill(int3 min, int3 max, ushort id) => landscape.Fill(new BoundsInt(min.x, min.y, min.z, max.x - min.x, max.y - min.y, max.z - min.z), id);
            void Set(int x, int y, int z, ushort id) => landscape.SetBlock(new Vector3Int(x, y, z), id);

            for (int z = -24; z < 40; z++)
            for (int x = -24; x < 40; x++)
            {
                int height = TerrainHeight(x, z);
                Fill(new int3(x, 0, z), new int3(x + 1, height - 3, z + 1), Stone);
                Fill(new int3(x, height - 3, z), new int3(x + 1, height, z + 1), Dirt);
                Set(x, height, z, Grass);
            }

            // A stone platform on four pillars over flattened ground, which is lit only from the sides.
            Fill(new int3(-1, 0, -1), new int3(13, 9, 11), Dirt);
            Fill(new int3(-1, 9, -1), new int3(13, 10, 11), Grass);
            Fill(new int3(-1, 10, -1), new int3(13, 30, 11), 0);
            Fill(new int3(1, 16, 1), new int3(11, 17, 9), Stone);
            foreach (int2 pillar in new[] { new int2(1, 1), new int2(10, 1), new int2(1, 8), new int2(10, 8) })
                Fill(new int3(pillar.x, 0, pillar.y), new int3(pillar.x + 1, 16, pillar.y + 1), Stone);
            Set(8, 10, 6, WarmLamp);

            // A plain pad with a single block, a 2 × 2 block and an L: any shading on it is ambient occlusion.
            Fill(new int3(-22, 0, -22), new int3(-10, 9, -10), Chalk);
            Fill(new int3(-22, 9, -22), new int3(-10, 30, -10), 0);
            Set(-19, 9, -19, Chalk);
            Fill(new int3(-16, 9, -19), new int3(-14, 11, -17), Chalk);
            Fill(new int3(-19, 9, -14), new int3(-13, 10, -13), Chalk);
            Fill(new int3(-19, 9, -16), new int3(-18, 10, -14), Chalk);

            // A plank house on a flattened floor, lit inside by a red and a blue lamp.
            const int floor = 9;
            Fill(new int3(13, 0, 1), new int3(23, floor + 1, 11), Stone);
            Fill(new int3(13, floor + 1, 1), new int3(23, 30, 11), 0);
            Fill(new int3(14, floor + 1, 2), new int3(21, floor + 5, 9), Planks);
            Fill(new int3(15, floor + 1, 3), new int3(20, floor + 4, 8), 0);
            Fill(new int3(17, floor + 1, 2), new int3(18, floor + 3, 3), 0);
            Fill(new int3(14, floor + 2, 4), new int3(15, floor + 3, 7), Glass);
            Set(15, floor + 2, 7, RedLamp);
            Set(19, floor + 2, 7, BlueLamp);

            // A tree with see-through leaves.
            int ground = TerrainHeight(-5, 12);
            Fill(new int3(-7, ground + 4, 10), new int3(-2, ground + 7, 15), Leaves);
            Fill(new int3(-5, ground + 1, 12), new int3(-4, ground + 6, 13), Log);
        }

        static int TerrainHeight(int x, int z) =>
            8 + (int)math.round(2.5f * math.sin(x / 6f) + 2f * math.cos(z / 7f) + math.sin((x + z) / 9f));

        void Render(string folder, string name, Vector3 position, Vector3 target)
        {
            var cameraObject = new GameObject("Preview Camera");
            created.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Sky;
            camera.fieldOfView = 60;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 300;

            var renderTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            created.Add(renderTexture);
            created.Add(image);
            camera.targetTexture = renderTexture;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            RenderTexture.active = previous;
            camera.targetTexture = null;
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
        }

        BlockRegistry CreateRegistry()
        {
            var registry = ScriptableObject.CreateInstance<BlockRegistry>();
            created.Add(registry);
            registry.textures = CreateTextures();
            registry.ambientOcclusion = AssetDatabase.LoadAssetAtPath<Texture2DArray>(BlockRegistry.AmbientOcclusionPath);
            registry.cracks = AssetDatabase.LoadAssetAtPath<Texture2DArray>(BlockRegistry.CracksPath);
            registry.blocks = new[]
            {
                Block("Stone", Stone, new FaceTextures(0)),
                Block("Grass", Grass, new FaceTextures(2, 1, 3)),
                Block("Dirt", Dirt, new FaceTextures(3)),
                Block("Planks", Planks, new FaceTextures(4)),
                Block("Glass", Glass, new FaceTextures(5), RenderPass.Cutout, lightPasses: true, hideSame: true),
                Block("Leaves", Leaves, new FaceTextures(6), RenderPass.Cutout),
                Block("Red Lamp", RedLamp, new FaceTextures(7), emission: 7, light: new Color(1f, 0.25f, 0.2f)),
                Block("Blue Lamp", BlueLamp, new FaceTextures(8), emission: 7, light: new Color(0.25f, 0.45f, 1f)),
                Block("Warm Lamp", WarmLamp, new FaceTextures(9), emission: 7, light: new Color(1f, 0.7f, 0.35f)),
                Block("Log", Log, new FaceTextures(10, 11, 11)),
                Block("Chalk", Chalk, new FaceTextures(12)),
            };
            return registry;
        }

        BlockData Block(string name, ushort id, FaceTextures textures, RenderPass pass = RenderPass.Opaque,
            bool lightPasses = false, bool hideSame = false, int emission = 0, Color? light = null)
        {
            var block = ScriptableObject.CreateInstance<BlockData>();
            created.Add(block);
            block.name = name;
            block.id = id;
            block.textures = textures;
            block.renderPass = pass;
            block.lightPasses = lightPasses;
            block.hideSameNeighborFaces = hideSame;
            block.emission = emission;
            block.lightColor = light ?? Color.white;
            return block;
        }

        Texture2DArray CreateTextures()
        {
            Func<int, int, Color>[] painters =
            {
                (x, y) => Gray(0.52f, 0.1f, x, y, 1),
                (x, y) => Tint(new Color(0.36f, 0.62f, 0.25f), 0.12f, x, y, 2),
                (x, y) => y >= 12 - Noise(x, 0, 3) * 3 ? Tint(new Color(0.36f, 0.62f, 0.25f), 0.12f, x, y, 2) : Tint(new Color(0.53f, 0.38f, 0.25f), 0.12f, x, y, 3),
                (x, y) => Tint(new Color(0.53f, 0.38f, 0.25f), 0.12f, x, y, 3),
                (x, y) => y % 4 == 0 ? new Color(0.45f, 0.33f, 0.2f) : Tint(new Color(0.72f, 0.56f, 0.34f), 0.06f, x, y, 4),
                (x, y) => x == 0 || y == 0 || x == 15 || y == 15 ? new Color(0.85f, 0.93f, 0.95f) : (x == y && x > 3 && x < 9 ? new Color(1, 1, 1, 1) : Color.clear),
                (x, y) => Noise(x, y, 6) < 0.3f ? Color.clear : Tint(new Color(0.22f, 0.5f, 0.18f), 0.15f, x, y, 6),
                (x, y) => Lamp(new Color(1f, 0.35f, 0.3f), x, y),
                (x, y) => Lamp(new Color(0.35f, 0.55f, 1f), x, y),
                (x, y) => Lamp(new Color(1f, 0.78f, 0.45f), x, y),
                (x, y) => Tint(x % 5 == 0 ? new Color(0.3f, 0.21f, 0.12f) : new Color(0.42f, 0.3f, 0.18f), 0.08f, x, y, 10),
                (x, y) => math.abs(math.length(new float2(x - 7.5f, y - 7.5f)) % 3 - 1.5f) < 0.6f ? new Color(0.45f, 0.33f, 0.2f) : new Color(0.65f, 0.5f, 0.3f),
                (x, y) => new Color(0.9f, 0.9f, 0.88f),
            };
            var array = new Texture2DArray(16, 16, painters.Length, TextureFormat.RGBA32, true, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[256];
            for (int layer = 0; layer < painters.Length; layer++)
            {
                for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    pixels[y * 16 + x] = painters[layer](x, y);
                array.SetPixels(pixels, layer);
            }
            array.Apply(true);
            created.Add(array);
            return array;
        }

        static float Noise(int x, int y, int seed)
        {
            uint hash = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            hash ^= hash >> 15;
            return (hash & 0xFFFF) / 65535f;
        }

        static Color Gray(float value, float spread, int x, int y, int seed)
        {
            float v = value + (Noise(x, y, seed) - 0.5f) * spread * 2;
            return new Color(v, v, v);
        }

        static Color Tint(Color color, float spread, int x, int y, int seed)
        {
            float shade = 1 + (Noise(x, y, seed) - 0.5f) * spread * 2;
            return new Color(color.r * shade, color.g * shade, color.b * shade);
        }

        static Color Lamp(Color color, int x, int y)
        {
            float glow = 1 - math.saturate(math.length(new float2(x - 7.5f, y - 7.5f)) / 10);
            return Color.Lerp(color * 0.8f, Color.white, glow * 0.7f);
        }
    }
}
