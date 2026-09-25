using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace reromanlee.BlockyMesher.Tests
{
    /// <summary>A small registry covering every kind of block, baked once per test.</summary>
    internal sealed class TestBlocks : IDisposable
    {
        public const ushort Air = 0;
        public const ushort Stone = 1;
        public const ushort Glass = 2;
        public const ushort Leaves = 3;
        public const ushort RedLamp = 4;
        public const ushort BlueLamp = 5;
        public const ushort Water = 6;
        public const ushort Dirt = 7;

        public readonly BlockRegistry Registry;
        public BlockTable Table;

        readonly List<UnityEngine.Object> created = new();

        public TestBlocks()
        {
            Registry = Create<BlockRegistry>();
            Registry.blocks = new[]
            {
                Block("Stone", Stone, RenderPass.Opaque, textures: new FaceTextures(10)),
                Block("Glass", Glass, RenderPass.Cutout, lightPasses: true, hideSame: true, textures: new FaceTextures(20)),
                Block("Leaves", Leaves, RenderPass.Cutout, textures: new FaceTextures(30)),
                Block("RedLamp", RedLamp, RenderPass.Opaque, emission: 7, lightColor: Color.red),
                Block("BlueLamp", BlueLamp, RenderPass.Opaque, emission: 5, lightColor: Color.blue),
                Block("Water", Water, RenderPass.Transparent, lightPasses: true, hideSame: true, collidable: false),
                Block("Dirt", Dirt, RenderPass.Opaque, textures: new FaceTextures(1, 2, 3)),
            };
            Table = Registry.Bake(Allocator.Persistent);
        }

        public BlockData Block(string name, ushort id, RenderPass pass, bool lightPasses = false, int emission = 0,
            Color? lightColor = null, bool hideSame = false, bool collidable = true, FaceTextures textures = default)
        {
            var block = Create<BlockData>();
            block.name = name;
            block.id = id;
            block.renderPass = pass;
            block.lightPasses = lightPasses;
            block.emission = emission;
            block.lightColor = lightColor ?? Color.white;
            block.hideSameNeighborFaces = hideSame;
            block.collidable = collidable;
            block.textures = textures;
            return block;
        }

        public T Create<T>() where T : ScriptableObject
        {
            var instance = ScriptableObject.CreateInstance<T>();
            created.Add(instance);
            return instance;
        }

        public void Dispose()
        {
            Table.Dispose();
            foreach (UnityEngine.Object instance in created)
                UnityEngine.Object.DestroyImmediate(instance);
        }
    }
}
