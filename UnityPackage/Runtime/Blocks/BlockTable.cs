using System;
using Unity.Collections;
using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// A <see cref="BlockRegistry"/> baked into flat native arrays, so jobs can look blocks up by id.
    /// Ids that no block uses read as air.
    /// </summary>
    internal struct BlockTable : IDisposable
    {
        [ReadOnly] public NativeArray<BlockInfo> Blocks;
        [ReadOnly] public NativeArray<ushort> FaceLayers;
        [ReadOnly] public NativeArray<Color32> LightColors;

        public BlockTable(int idCount, int lightColorCount, Allocator allocator)
        {
            Blocks = new NativeArray<BlockInfo>(idCount, allocator);
            FaceLayers = new NativeArray<ushort>(idCount * Faces.Count, allocator);
            LightColors = new NativeArray<Color32>(lightColorCount, allocator);
            for (int id = 0; id < idCount; id++)
                Blocks[id] = BlockInfo.Air;
        }

        public bool IsCreated => Blocks.IsCreated;

        public BlockInfo this[int id] => id < Blocks.Length ? Blocks[id] : Blocks[0];

        public ushort FaceLayer(int id, int face) => FaceLayers[id * Faces.Count + face];

        public void Dispose()
        {
            if (Blocks.IsCreated) Blocks.Dispose();
            if (FaceLayers.IsCreated) FaceLayers.Dispose();
            if (LightColors.IsCreated) LightColors.Dispose();
        }
    }
}
