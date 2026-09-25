using System.Collections;
using System.Text;
using NUnit.Framework;
using reromanlee.BlockyMesher.Generation;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace reromanlee.BlockyMesher.Tests
{
    /// <summary>
    /// Opt-in: streams terrain in Play Mode, the way a game does, and reports what it costs per frame
    /// on the main thread. Runs once with the Job System's worker threads, and once without any,
    /// which is what a web build without multithreading gets.
    /// </summary>
    [Explicit, Category("Benchmark")]
    public class FrameBenchmarks
    {
        const float Budget = 4;

        [UnityTest]
        public IEnumerator StreamingFrameCost()
        {
            // The editor compiles each job with Burst the first time it runs; players come compiled.
            yield return Stream(new Result(), 1);

            var report = new StringBuilder($"Streaming in Play Mode: view radius 6, 256 blocks tall, {Budget} ms budget, flying at 20 blocks/s\n");
            int workers = JobsUtility.JobWorkerCount;
            try
            {
                foreach (int threads in new[] { workers, 0 })
                {
                    JobsUtility.JobWorkerCount = threads;
                    var result = new Result();
                    yield return Stream(result, 6);
                    report.AppendLine($"- {(threads == 0 ? "No worker threads (like the web without multithreading)" : $"{threads} worker threads")}:");
                    report.AppendLine($"  first view in {result.LoadFrames} frames ({result.LoadSeconds:0.00} s), worst frame {result.LoadPeakMs:0.00} ms");
                    report.AppendLine($"  flying: {result.FlightAverageMs:0.00} ms per frame on average, worst frame {result.FlightPeakMs:0.00} ms");
                }
            }
            finally
            {
                JobsUtility.JobWorkerCount = workers;
            }
            Debug.Log(report.ToString());
        }

        sealed class Result
        {
            public int LoadFrames;
            public float LoadSeconds;
            public float LoadPeakMs;
            public float FlightAverageMs;
            public float FlightPeakMs;
        }

        static IEnumerator Stream(Result result, int viewRadius)
        {
            var dirt = Block("Dirt", 1);
            var stone = Block("Stone", 2);
            var registry = ScriptableObject.CreateInstance<BlockRegistry>();
            registry.blocks = new[] { dirt, stone };
            var terrain = ScriptableObject.CreateInstance<NoiseTerrainStep>();
            terrain.surface = terrain.subsurface = dirt;
            terrain.stone = stone;
            terrain.baseHeight = 64;
            terrain.amplitude = 24;
            var generator = ScriptableObject.CreateInstance<TerrainGenerator>();
            generator.steps = new GenerationStep[] { terrain };

            var player = new GameObject("Player");
            player.transform.position = new Vector3(8, 110, 8);
            var world = new GameObject("World");
            world.SetActive(false);
            var landscape = world.AddComponent<Landscape>();
            landscape.Registry = registry;
            landscape.SolidBelowWorld = true;
            landscape.FrameBudgetMs = Budget;
            var streamer = world.AddComponent<LandscapeStreamer>();
            streamer.Generator = generator;
            streamer.ViewRadius = viewRadius;
            streamer.FocusPoints.Add(player.transform);
            world.SetActive(true);

            float start = Time.realtimeSinceStartup;
            while (!AllLoaded(landscape, viewRadius) && result.LoadFrames < 5000)
            {
                yield return null;
                result.LoadFrames++;
                result.LoadPeakMs = Mathf.Max(result.LoadPeakMs, landscape.Timer.LastFrameMs);
            }
            result.LoadSeconds = Time.realtimeSinceStartup - start;

            // Fly for a few seconds while terrain streams in ahead.
            float sum = 0;
            int frames = 0;
            float flightStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - flightStart < 4)
            {
                player.transform.position += new Vector3(20 * Time.unscaledDeltaTime, 0, 0);
                yield return null;
                float frameMs = landscape.Timer.LastFrameMs;
                result.FlightPeakMs = Mathf.Max(result.FlightPeakMs, frameMs);
                sum += frameMs;
                frames++;
            }
            result.FlightAverageMs = frames > 0 ? sum / frames : 0;

            Object.Destroy(world);
            Object.Destroy(player);
            foreach (Object item in new Object[] { dirt, stone, registry, terrain, generator })
                Object.Destroy(item);
            yield return null;
        }

        static bool AllLoaded(Landscape landscape, int viewRadius)
        {
            LandscapeStats stats = landscape.GetStats();
            return stats.Columns >= 3.14f * viewRadius * viewRadius && stats.QueuedSections == 0 && stats.BuildingSections == 0 && stats.GeneratingColumns == 0;
        }

        static BlockData Block(string name, int id)
        {
            var block = ScriptableObject.CreateInstance<BlockData>();
            block.name = name;
            block.id = id;
            return block;
        }
    }
}
