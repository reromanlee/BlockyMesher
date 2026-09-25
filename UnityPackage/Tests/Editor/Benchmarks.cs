using System;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using reromanlee.BlockyMesher.Generation;
using reromanlee.BlockyMesher.Meshing;
using reromanlee.BlockyMesher.Storage;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using static reromanlee.BlockyMesher.Tests.TestBlocks;

namespace reromanlee.BlockyMesher.Tests
{
    /// <summary>
    /// Opt-in timings and memory figures, printed to the log. Editor numbers include the safety checks
    /// the editor adds to jobs, so players run somewhat faster. For WebGL, measure a build on the
    /// device with a LandscapeStatsOverlay.
    /// </summary>
    [Explicit, Category("Benchmark")]
    public class Benchmarks
    {
        const int Runs = 100;

        [Test]
        public void SectionBuilds()
        {
            var report = new StringBuilder("Section build, milliseconds per section (jobs + mesh upload)\n");
            report.AppendLine("| Section | Burst, one at a time | Burst, all worker threads | Without Burst, one at a time |");
            report.AppendLine("|---|---|---|---|");
            foreach (string name in new[] { "Hills surface", "Caves", "Colored lamps", "Checkerboard (worst case)" })
            {
                using var world = new BuildHarness(4);
                int sectionY = Build(world, name);
                double single = Measure(world, sectionY, parallel: false);
                double parallel = Measure(world, sectionY, parallel: true);
                bool burst = BurstCompiler.Options.EnableBurstCompilation;
                BurstCompiler.Options.EnableBurstCompilation = false;
                double managed;
                try
                {
                    managed = Measure(world, sectionY, parallel: false, runs: 10);
                }
                finally
                {
                    BurstCompiler.Options.EnableBurstCompilation = burst;
                }
                report.AppendLine($"| {name} | {single:0.000} | {parallel:0.000} | {managed:0.00} |");
            }
            report.AppendLine($"Worker threads: {JobsUtility.JobWorkerCount}. \"All worker threads\" is total time divided by sections, built in parallel batches.");
            Debug.Log(report.ToString());
        }

        [Test]
        public void ColumnGeneration()
        {
            using var blocks = new TestBlocks();
            var terrain = blocks.Create<NoiseTerrainStep>();
            terrain.surface = blocks.Registry.blocks[Dirt - 1];
            terrain.subsurface = terrain.surface;
            terrain.stone = blocks.Registry.blocks[Stone - 1];
            var generator = blocks.Create<TerrainGenerator>();
            generator.steps = new GenerationStep[] { terrain };

            var report = new StringBuilder("Column generation, 16 × 256 × 16 blocks, milliseconds per column (Burst, one at a time)\n");
            using var column = new NativeArray<ushort>(256 * Section.Area, Allocator.Persistent);
            using var uniformIds = new NativeArray<int>(16, Allocator.Persistent);
            using var skyStart = new NativeArray<ushort>(Section.Area, Allocator.Persistent);
            foreach (bool caves in new[] { false, true })
            {
                terrain.caves = caves;
                double Run(int index)
                {
                    long start = Stopwatch.GetTimestamp();
                    var context = new ColumnContext { Position = new int2(index, index * 7), Height = 256 };
                    JobHandle cleared = new ClearBlocksJob { Blocks = column }.Schedule();
                    JobHandle generated = generator.Schedule(context, column, cleared);
                    new ColumnSummaryJob { Blocks = column, BlockInfos = blocks.Table.Blocks, SectionCount = 16, UniformIds = uniformIds, SkyStart = skyStart }
                        .Schedule(generated).Complete();
                    return Milliseconds(start);
                }
                for (int i = 0; i < 5; i++) Run(-1 - i);
                double total = 0;
                for (int i = 0; i < Runs; i++) total += Run(i);
                report.AppendLine($"- {(caves ? "With caves" : "Hills only")}: {total / Runs:0.000} ms");
            }
            Debug.Log(report.ToString());
        }

        [Test]
        public void MemoryAtViewRadius8()
        {
            using var blocks = new TestBlocks();
            var terrain = blocks.Create<NoiseTerrainStep>();
            terrain.surface = blocks.Registry.blocks[Dirt - 1];
            terrain.subsurface = terrain.surface;
            terrain.stone = blocks.Registry.blocks[Stone - 1];
            terrain.baseHeight = 64;
            terrain.amplitude = 24;
            var generator = blocks.Create<TerrainGenerator>();
            generator.steps = new GenerationStep[] { terrain };

            var world = new GameObject("World");
            var player = new GameObject("Player");
            try
            {
                var landscape = world.AddComponent<Landscape>();
                landscape.Registry = blocks.Registry;
                landscape.SolidBelowWorld = true;
                var streamer = world.AddComponent<LandscapeStreamer>();
                streamer.Generator = generator;
                streamer.ViewRadius = 8;
                player.transform.position = new Vector3(8, 100, 8);
                streamer.FocusPoints.Add(player.transform);
                streamer.Attach();
                long start = Stopwatch.GetTimestamp();
                streamer.LoadEverything();
                double milliseconds = Milliseconds(start);
                LandscapeStats stats = landscape.GetStats();
                streamer.Detach();
                Debug.Log($"View radius 8, 256 blocks tall, hills without caves: streamed in {milliseconds:0} ms with {JobsUtility.JobWorkerCount} worker threads\n{stats}");
            }
            finally
            {
                Object.DestroyImmediate(world);
                Object.DestroyImmediate(player);
            }
        }

        /// <summary>Fills the world for a case and returns the section to build.</summary>
        static int Build(BuildHarness world, string name)
        {
            switch (name)
            {
                case "Hills surface":
                    // Rolling ground crossing the section, with the neighbors around it filled the same way.
                    for (int z = -8; z < 24; z++)
                    for (int x = -8; x < 24; x++)
                    {
                        int top = 20 + (int)math.round(6 * math.sin(x * 0.4f) * math.cos(z * 0.3f));
                        world.Fill(new int3(x, 0, z), new int3(x + 1, top - 3, z + 1), Stone);
                        world.Fill(new int3(x, top - 3, z), new int3(x + 1, top + 1, z + 1), Dirt);
                    }
                    return 1;
                case "Caves":
                    world.Fill(new int3(-8, 0, -8), new int3(24, 48, 24), Stone);
                    for (int y = 8; y < 40; y++)
                    for (int z = -8; z < 24; z++)
                    for (int x = -8; x < 24; x++)
                        if (noise.snoise(new float3(x, y * 1.6f, z) / 12f) > 0.3f) world.Set(x, y, z, Air);
                    return 1;
                case "Colored lamps":
                    world.Fill(new int3(-8, 0, -8), new int3(24, 32, 24), Stone);
                    world.Fill(new int3(-4, 4, -4), new int3(20, 28, 20), Air);
                    for (int i = 0; i < 12; i++)
                        world.Set(i * 5 % 16, 6 + i, i * 3 % 16, (ushort)(i % 2 == 0 ? RedLamp : BlueLamp));
                    return 1;
                default:
                    for (int y = 16; y < 32; y++)
                    for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                        if (((x + y + z) & 1) == 0) world.Set(x, y, z, Stone);
                    return 1;
            }
        }

        static double Measure(BuildHarness world, int sectionY, bool parallel, int runs = Runs)
        {
            int batch = parallel ? Math.Max(1, JobsUtility.JobWorkerCount) : 1;
            var builds = new SectionBuild[batch];
            var meshes = new Mesh[batch];
            for (int i = 0; i < batch; i++)
            {
                builds[i] = new SectionBuild();
                meshes[i] = new Mesh();
            }
            try
            {
                double RunBatch()
                {
                    long start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < batch; i++)
                    {
                        builds[i].CopyInputs(world.Storage, int2.zero, sectionY);
                        builds[i].Schedule(world.Blocks.Table, world.Settings);
                    }
                    JobHandle.ScheduleBatchedJobs();
                    for (int i = 0; i < batch; i++)
                    {
                        builds[i].Apply(meshes[i]);
                        meshes[i].UploadMeshData(true);
                    }
                    return Milliseconds(start);
                }
                for (int i = 0; i < 3; i++) RunBatch();
                double total = 0;
                for (int i = 0; i < runs; i++) total += RunBatch();
                return total / (runs * batch);
            }
            finally
            {
                for (int i = 0; i < batch; i++)
                {
                    builds[i].Dispose();
                    Object.DestroyImmediate(meshes[i]);
                }
            }
        }

        static double Milliseconds(long timestamp) => (Stopwatch.GetTimestamp() - timestamp) * 1000.0 / Stopwatch.Frequency;
    }
}
