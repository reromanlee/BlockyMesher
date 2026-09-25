using System;
using System.Collections.Generic;
using reromanlee.BlockyMesher.Storage;
using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// A saved box of blocks: a raft a player built, a house, a tree to stamp into terrain.
    /// Blocks are stored compressed (see <see cref="Landscape.Export"/> and <see cref="Landscape.Place"/>),
    /// so even large patterns stay small in memory and on disk.
    /// </summary>
    [CreateAssetMenu(fileName = "BlockPattern", menuName = "BlockyMesher/Block Pattern")]
    public sealed class BlockPattern : ScriptableObject
    {
        [SerializeField] Vector3Int size;
        [SerializeField] int[] palette = { 0 };
        [SerializeField] int bitsPerBlock;
        [SerializeField] byte[] packed = Array.Empty<byte>();

        public Vector3Int Size => size;
        public int Volume => size.x * size.y * size.z;

        /// <summary>Makes a pattern from blocks laid out like a section: x changes fastest, then z, then y.</summary>
        public static BlockPattern Create(Vector3Int size, ReadOnlySpan<ushort> blocks)
        {
            if (size.x < 0 || size.y < 0 || size.z < 0 || blocks.Length != size.x * size.y * size.z)
                throw new ArgumentException($"A {size} pattern needs {size.x * size.y * size.z} blocks, got {blocks.Length}.");
            var pattern = CreateInstance<BlockPattern>();
            var paletteList = new List<int>();
            pattern.size = size;
            pattern.packed = BlockPacking.Pack(blocks, paletteList, out pattern.bitsPerBlock);
            pattern.palette = paletteList.Count > 0 ? paletteList.ToArray() : new[] { 0 };
            return pattern;
        }

        public ushort GetBlock(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= size.x || y >= size.y || z >= size.z)
                return 0;
            return BlockPacking.Read(packed, bitsPerBlock, palette, x + (z + y * size.z) * size.x);
        }

        /// <summary>Every block of the pattern, in the same order as <see cref="Create"/> takes them.</summary>
        public ushort[] GetBlocks()
        {
            var blocks = new ushort[Volume];
            BlockPacking.Unpack(packed, bitsPerBlock, palette, blocks);
            return blocks;
        }

        /// <summary>Bytes the blocks take once compressed.</summary>
        public int CompressedBytes => packed.Length + palette.Length * sizeof(int);
    }
}
