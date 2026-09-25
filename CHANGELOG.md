# Changelog

## 1.0.0

The first release: the prototype, rebuilt on the Job System and Burst.

- `Landscape`: a world of blocks with its own transform, for standalone objects such as a raft or a
  house. Fill it from code or from a `BlockPattern`, which previews in the Scene view outside Play
  Mode.
- `LandscapeStreamer`: makes a landscape endless. It loads columns around focus points, generates
  them with a `TerrainGenerator` made of `GenerationStep`s, and unloads those left far behind.
  Player edits survive this, and save to bytes and load back.
- `NoiseTerrainStep`: rolling hills, with optional caves.
- Burst jobs for generation, lighting, meshing and colliders. They run in parallel on worker
  threads, or on the main thread within a per-frame time budget when there are none, as on the web
  without multithreading.
- Smooth skylight and colored block light, levels 0 to 7, with up to 16 light colors per registry.
  `BlockLighting.SkyColor` runs a day/night cycle without rebuilding meshes.
- Prebaked ambient occlusion: one tile for each of the 256 ways blocks can surround a face.
- Opaque, cutout and transparent render passes, from a texture array, with a hand-written URP
  shader.
- Mesh colliders near the player, or box colliders for landscapes on a Rigidbody.
- A voxel raycast and a block-breaking crack overlay.
- A stats window, an on-screen stats overlay, benchmarks, and the Infinite Terrain and Raft samples.
