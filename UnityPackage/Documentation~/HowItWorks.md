# How BlockyMesher works

This page explains the Job System and Burst in plain terms, starting from async code and Tasks,
and then follows a block change all the way to the new mesh on screen.

## The Job System, for someone who knows async

### A job is a Task that carries its own data

With Tasks, you hand a lambda to the thread pool:

```csharp
Task task = Task.Run(() =>
{
    for (int i = 0; i < numbers.Length; i++)
        numbers[i] *= 2;
});
```

The lambda can capture anything, including objects the main thread keeps using at the same time.
That is how race conditions happen.

A job writes the captured variables out as the fields of a struct:

```csharp
[BurstCompile]
struct DoubleJob : IJob
{
    public NativeArray<int> Numbers;

    public void Execute()
    {
        for (int i = 0; i < Numbers.Length; i++)
            Numbers[i] *= 2;
    }
}
```

A job may only hold plain values and native containers such as `NativeArray`. No classes, no
references to GameObjects. That restriction is what lets Unity check who touches which memory, and
what lets Burst compile it.

### The JobHandle is the job's Task

| With Tasks and UniTask | With jobs |
|---|---|
| `Task task = Task.Run(work);` | `JobHandle handle = job.Schedule();` |
| `await first; await second;` or `ContinueWith` | `second.Schedule(firstHandle)` |
| `Task.WhenAll(a, b)` | `JobHandle.CombineDependencies(a, b)` |
| `task.IsCompleted` | `handle.IsCompleted` |
| `await UniTask.WaitUntil(() => task.IsCompleted)` | check `handle.IsCompleted` once per frame, in `Update` |
| `task.Wait()` | `handle.Complete()` |

Chaining is the important one. `lightJob.Schedule(gatherHandle)` means "run the light job once the
gather job is done". The main thread doesn't wait in between, just as with `await`. The package
builds every section as such a chain: gather, then light, then mesh.

`Complete()` differs from `Wait()` in a useful way. If the job hasn't started yet, `Complete()`
doesn't block and wait for a worker: the main thread runs the job itself, right there. So the same
code works with no worker threads at all, as on the web without multithreading: every job simply
runs when something calls `Complete()`.

Some other differences:

- A job runs from start to finish. There is no `await` inside it, and nothing to yield to.
- Unity owns the worker threads, usually one fewer than the CPU has. There is no thread pool and no
  synchronization context.
- Scheduled jobs wait in a batch until `JobHandle.ScheduleBatchedJobs()` is called, so the package
  calls it once it has scheduled a frame's worth of work.
- async/await is for *waiting* without blocking, like loading a file. Jobs are for *computing* in
  parallel. They go together: load with UniTask, crunch with jobs.

### Native memory and the safety checks

A `NativeArray` lives outside the garbage-collected heap. You allocate it and you `Dispose()` it,
like a file handle. In exchange it makes no garbage, so it never causes a garbage collection spike.
That matters most on the web, where the garbage collector runs all at once at the end of a frame
instead of a little at a time.

In the editor, every native container knows which scheduled jobs use it. Touching it from the main
thread while a job still does, or disposing it too early, throws an exception that names the job.
These checks caught a real bug while the package was being written: a landscape freed its block
table while a column job was still reading it, when a scene with an endless terrain unloaded. In
builds, the checks are compiled out and cost nothing.

### Burst

Burst is a compiler for jobs. It turns a job's C# into optimized machine code, and uses SIMD
instructions that work on several numbers at once. It only accepts a subset of C#: structs, math,
and native containers. No classes, no strings, no allocations on the managed heap.

Here is what that buys, building one section of terrain:

| | Time per section |
|---|---|
| The same jobs without Burst, one at a time | 1.16 ms |
| Burst, one at a time | 0.34 ms |
| Burst, 19 worker threads in parallel | 0.06 ms |

The code is the same in all three rows. Only Burst and the number of threads change.

## From a block change to a mesh on screen

```
 Main thread                                   A worker thread, or the main thread when there are none
 ───────────                                   ────────────────────────────────────────────────────────
 SetBlock: sections within 2 blocks of
 it rebuild now, those its light can
 reach within the frame budget
        │
 Copy the 27 sections around it ─────────────► GatherJob  one 32³ box: the section and 8 blocks around it
                                                   │
                                               LightJob   skylight and block light for the whole box
                                                   │
                                               MeshJob    faces, light and AO tiles, into Mesh.MeshData
                                                   │      (plus BoxMergeJob or CollisionMeshJob)
 Later, when IsCompleted:                          │
 Complete(), apply the mesh, set up ◄──────────────┘
 colliders
```

### Copying

A job can't read the landscape's storage while the main thread might change it. So the main thread
copies the 27 sections around the one it builds, the section itself included, into arrays owned by
that build. A section made of a single kind of block is copied as one number. After that, the build
needs nothing from the main thread and the player can keep editing.

### Gathering and lighting

`GatherJob` lays the 27 sections out as a single 32 × 32 × 32 box: the section plus an 8-block
border. Light fades out within 7 blocks, so nothing outside the box can change how the section looks.

`LightJob` lights the whole box from scratch, which is why light is never stored anywhere:

- **Skylight.** Each column of blocks remembers where its open sky starts: the height above which
  every block lets light through. Blocks there get full light, level 7. Light then spreads sideways
  and down, one level dimmer per block, around any block that stops it.
- **Block light.** Blocks with emission spread their light the same way, carrying their color. The
  light spreads level by level, brightest first, so where two lights overlap the brighter one gets
  there first and wins.
- **Color.** A registry holds up to 16 light colors. A block's light is stored as its level (3 bits)
  and its color's index (the bits above), in one byte.

### Meshing

`MeshJob` visits every block of the section and emits the faces that can be seen:

- A face that touches an opaque block is hidden. Blocks that hide the faces between two of their
  own kind, like glass, hide those too.
- **Smooth light.** Each corner of a face averages the light of the four blocks in front of it that
  meet at that corner. Only blocks that light could actually reach from the face count, so light
  doesn't leak around the corner of a wall. That rule is a 64-entry table, checked against a real
  flood fill in all 64 cases.
- Each face is split into two triangles along the diagonal whose light is more even, which avoids
  streaks.
- **Ambient occlusion.** The eight blocks around the face's front block, in the face's plane, give
  eight solid-or-not bits: a number from 0 to 255. The face stores that number, and the shader draws
  the matching tile. More on that below.
- **Vertices.** Each is 12 bytes, all whole numbers stored as bytes:
  - its position in the section and which corner of the texture it is;
  - its block light, already colored;
  - its texture layer, ambient occlusion case and sky light level.
- Opaque, cutout and transparent faces go into separate submeshes, and only the passes a section
  actually uses get one.
- The job writes straight into `Mesh.MeshData`, Unity's mesh memory. Applying it on the main thread
  copies nothing.

Colliders are built alongside the mesh. `CollisionMeshJob` merges flat neighboring faces into large
rectangles for a mesh collider, and PhysX then prepares that mesh in `CookColliderJob`.
`BoxMergeJob` merges blocks into as few boxes as it can, for landscapes on a Rigidbody.

### Presenting

Every frame, the landscape picks up builds whose handles report `IsCompleted`, calls `Complete()`
(which returns at once, since they are done) and applies them. Once a mesh is on the GPU, its CPU
copy is dropped. A mesh without a CPU copy can't take new data in a build, so the next rebuild writes
into a fresh mesh.

## Endless terrain

`LandscapeStreamer` decides which columns exist. Every frame, it:

1. **Finishes generated columns.** Each one is copied into the landscape's storage, and the player's
   edits for it are applied on top.
2. **Drops far columns.** Columns load within the view radius plus 1.5 columns, and unload beyond
   the view radius plus 2.5. Moving back and forth across a border doesn't make them flicker in and
   out.
3. **Starts missing columns,** nearest first, and those in front of the camera before those behind
   it. Each one is a chain of jobs:

```
ClearBlocksJob ─► your generation steps, in order ─► ColumnSummaryJob
                                                     which sections hold a single kind of block,
                                                     and where the sky starts in each column
```

A section only builds once all eight columns around it are loaded. Otherwise its edges would be lit
and meshed as if the world ended there, and would need building again.

### Edits

The streamer remembers every edit, so a column that unloads and comes back still has them. Edits are
stored per section, in whichever form is smaller:

- a list of the changed blocks, for a section with a few changes;
- a compressed copy of the whole section, for one the player reshaped. Blocks are packed with a
  palette, using as few bits per block as its different blocks need.

`SaveEdits()` writes them all out as bytes. Since they are differences from what the generator
makes, keep the generator and its seed alongside them.

## The frame budget

Each landscape has one budget per frame, 4 ms by default. Streaming, starting builds and presenting
finished ones all count against it, and once it is spent, the rest waits for the next frame.

- **With worker threads,** the main thread only copies, schedules and presents. The heavy work
  happens elsewhere, so frames stay far below the budget.
- **Without worker threads,** the jobs run inside `Complete()` on the main thread, so they count
  against the budget. The budget then directly limits how much building a frame does. The terrain
  loads over more frames, but none of them stutters.
- **Edits are urgent.** The sections within 2 blocks of a changed block are built within the same
  frame, whatever the budget, so breaking a block never shows a delay. Sections further away, which
  only its light reaches, follow within the budget.

Build and generation buffers are pooled, since a burst of streaming uses one per worker thread. After
about 3 seconds with nothing to do, all but one of each are freed.

## Memory

| What | How it is stored |
|---|---|
| A section of a single kind of block, like air or solid stone | one number |
| Any other section | 16 × 16 × 16 two-byte ids: 8 KB, from a pool |
| Where the sky starts in each column | 16 × 16 two-byte heights: 512 bytes |
| Light | not stored: computed during each build |
| Meshes | 12 bytes per vertex, 2 or 4 per index, GPU only |
| Edits | per section, a list of changes or a compressed copy |

At view radius 8, a 256-block-tall terrain of hills holds 293 columns in 4.1 MB of blocks and
5.7 MB of meshes.

## Ambient occlusion

Ambient occlusion is the soft shadow where blocks meet. The package bakes it ahead of time instead
of computing it per vertex:

1. Around every face, eight blocks can be solid or not. That makes 256 cases.
2. **Tools > BlockyMesher > Bake Ambient Occlusion Tiles** draws a 16 × 16 tile for each case. Every
   solid block covers its square of the face's plane. A texel gets darker the more of a small disk
   around it those squares cover. So shadows are darkest where blocks meet, and fade out where a
   neighboring block ends.
3. The tiles are saved as one PNG and imported as a texture array: one layer per case.
4. A face stores its case. The shader samples that layer and darkens the face with it.

Edge texels lie exactly on the face's edges, and the shader samples half a texel in. So two faces
side by side read the same values along their shared edge, without a seam.

To change the look, set `AmbientOcclusionBaker.Strength` and `Radius` and bake again. Baking writes
into the package, so it needs a local or embedded copy of it. Or paint tiles of your own and assign
them to the registry's **Ambient Occlusion** field.

## The shader

A single hand-written URP shader draws every block. Per pixel, it:

1. samples the block texture from the texture array;
2. cuts the pixel away if the block is cutout and the texture is transparent there;
3. multiplies by the light: for each color channel, the brighter of block light and skylight, where
   skylight is the vertex's sky level times `BlockLighting.SkyColor`;
4. multiplies by the ambient occlusion tile;
5. adds fog.

Since the sky's color only comes in here, a day/night cycle changes one global shader value and no
mesh is rebuilt. Each registry shares one material per render pass among all its landscapes, which
keeps them batched together.

## On the web

- **Worker threads.** Since Unity 6.4, Burst jobs run on worker threads in web builds when **Enable
  Native C/C++ Multithreading** is on and the server sends the cross-origin isolation headers listed
  in the README. Otherwise everything runs on the main thread, within the budget.
- **Burst only.** Jobs without `[BurstCompile]` always run on the main thread on the web. Every job
  in the package is Burst-compiled, except `CookColliderJob`, which calls Unity's physics API. It
  only runs for mesh colliders near the player.
- **No parallel writers.** Each section and column is its own chain of single-threaded jobs, and
  chains run in parallel with each other. So the package never needs the parallel writers that have
  a known memory-corruption issue in multithreaded web builds.
- **Texture arrays** need WebGL 2, which every current browser supports.
