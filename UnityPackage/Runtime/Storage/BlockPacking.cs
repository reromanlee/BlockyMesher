using System;
using System.Collections.Generic;

namespace reromanlee.BlockyMesher.Storage
{
    /// <summary>
    /// Compresses a run of block ids the way block patterns and unloaded edits are stored: a palette
    /// of the ids that occur, then each block as an index into it using as few bits as the palette
    /// needs (2 bits for up to 4 kinds of blocks, 0 when there is only one).
    /// </summary>
    internal static class BlockPacking
    {
        public static byte[] Pack(ReadOnlySpan<ushort> blocks, List<int> palette, out int bitsPerBlock)
        {
            palette.Clear();
            var indices = new Dictionary<ushort, int>();
            foreach (ushort id in blocks)
            {
                if (indices.TryAdd(id, palette.Count))
                    palette.Add(id);
            }

            bitsPerBlock = BitsFor(palette.Count);
            var packed = new byte[(blocks.Length * bitsPerBlock + 7) / 8];
            if (bitsPerBlock == 0)
                return packed;
            for (int i = 0; i < blocks.Length; i++)
                Write(packed, i * bitsPerBlock, bitsPerBlock, indices[blocks[i]]);
            return packed;
        }

        public static void Unpack(byte[] packed, int bitsPerBlock, IReadOnlyList<int> palette, Span<ushort> destination)
        {
            for (int i = 0; i < destination.Length; i++)
                destination[i] = Read(packed, bitsPerBlock, palette, i);
        }

        public static ushort Read(byte[] packed, int bitsPerBlock, IReadOnlyList<int> palette, int index)
        {
            if (bitsPerBlock == 0)
                return (ushort)palette[0];
            int bit = index * bitsPerBlock;
            int value = 0;
            for (int i = 0; i < bitsPerBlock; i++, bit++)
                value |= ((packed[bit >> 3] >> (bit & 7)) & 1) << i;
            return (ushort)palette[value];
        }

        static int BitsFor(int paletteSize)
        {
            int bits = 0;
            while ((1 << bits) < paletteSize)
                bits++;
            return bits;
        }

        static void Write(byte[] packed, int bit, int bitsPerBlock, int value)
        {
            for (int i = 0; i < bitsPerBlock; i++, bit++)
            {
                if (((value >> i) & 1) != 0)
                    packed[bit >> 3] |= (byte)(1 << (bit & 7));
            }
        }
    }
}
