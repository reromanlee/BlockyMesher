using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace reromanlee.BlockyMesher.Storage
{
    /// <summary>What a single block change did, so the caller knows what to rebuild.</summary>
    internal struct BlockEdit
    {
        public bool Changed;
        public int OldSkyStart;
        public int NewSkyStart;
    }

    /// <summary>
    /// The blocks of one landscape, as columns of sections. Main thread only: jobs never read it,
    /// they get their own copy of the sections they need, so edits can happen at any time.
    /// </summary>
    internal sealed class BlockStorage : IDisposable
    {
        public readonly int SectionsPerColumn;
        public readonly int Height;

        readonly BlockTable table;
        readonly Dictionary<int2, Column> columns = new();
        readonly Stack<Column> freeColumns = new();
        readonly Stack<NativeArray<ushort>> freeArrays = new();

        public BlockStorage(int sectionsPerColumn, BlockTable table)
        {
            SectionsPerColumn = sectionsPerColumn;
            Height = sectionsPerColumn * Section.Size;
            this.table = table;
        }

        public int ColumnCount => columns.Count;
        public int MixedSectionCount { get; private set; }
        public int PooledArrayCount => freeArrays.Count;
        public Dictionary<int2, Column>.ValueCollection Columns => columns.Values;

        public bool TryGetColumn(int2 position, out Column column) => columns.TryGetValue(position, out column);

        /// <summary>Adds an all-air column, or returns the one already there.</summary>
        public Column AddColumn(int2 position)
        {
            if (columns.TryGetValue(position, out Column column))
                return column;
            column = freeColumns.Count > 0 ? freeColumns.Pop() : new Column(SectionsPerColumn);
            column.Reset(position);
            columns.Add(position, column);
            return column;
        }

        public void RemoveColumn(int2 position)
        {
            if (!columns.Remove(position, out Column column))
                return;
            for (int i = 0; i < column.Sections.Length; i++)
                MakeUniform(ref column.Sections[i], 0);
            freeColumns.Push(column);
        }

        /// <summary>Anything outside the stored columns or the world height is air.</summary>
        public ushort GetBlock(int3 position)
        {
            if (position.y < 0 || position.y >= Height || !columns.TryGetValue(Section.ColumnOf(position), out Column column))
                return 0;
            return column.Sections[position.y >> 4][Section.Index(position & 15)];
        }

        public BlockEdit SetBlock(int3 position, ushort id, bool createColumn)
        {
            var edit = new BlockEdit();
            if (position.y < 0 || position.y >= Height)
                return edit;
            int2 columnPosition = Section.ColumnOf(position);
            if (!columns.TryGetValue(columnPosition, out Column column))
            {
                if (!createColumn)
                    return edit;
                column = AddColumn(columnPosition);
            }

            ref SectionBlocks section = ref column.Sections[position.y >> 4];
            int index = Section.Index(position & 15);
            if (section[index] == id)
                return edit;
            if (section.IsUniform)
                MakeMixed(ref section);
            section.Blocks[index] = id;

            int skyIndex = (position.x & 15) | ((position.z & 15) << 4);
            int skyStart = column.SkyStart[skyIndex];
            int newSkyStart = skyStart;
            if (!PassesLight(id))
            {
                if (position.y >= skyStart)
                    newSkyStart = position.y + 1;
            }
            else if (position.y == skyStart - 1)
            {
                newSkyStart = FindSkyStart(column, position.x & 15, position.z & 15, position.y - 1);
            }
            column.SkyStart[skyIndex] = (ushort)newSkyStart;

            edit.Changed = true;
            edit.OldSkyStart = skyStart;
            edit.NewSkyStart = newSkyStart;
            return edit;
        }

        /// <summary>Sets every block from <paramref name="min"/> up to, but not including, <paramref name="max"/>.</summary>
        public void Fill(int3 min, int3 max, ushort id, bool createColumns)
        {
            min.y = math.max(min.y, 0);
            max.y = math.min(max.y, Height);
            if (math.any(min >= max))
                return;

            int2 firstColumn = Section.ColumnOf(min);
            int2 lastColumn = Section.ColumnOf(max - 1);
            for (int columnZ = firstColumn.y; columnZ <= lastColumn.y; columnZ++)
            for (int columnX = firstColumn.x; columnX <= lastColumn.x; columnX++)
            {
                var position = new int2(columnX, columnZ);
                if (!columns.TryGetValue(position, out Column column))
                {
                    if (!createColumns)
                        continue;
                    column = AddColumn(position);
                }
                for (int sectionIndex = min.y >> 4; sectionIndex <= (max.y - 1) >> 4; sectionIndex++)
                {
                    int3 sectionOrigin = new int3(columnX, sectionIndex, columnZ) * Section.Size;
                    int3 localMin = math.max(min - sectionOrigin, 0);
                    int3 localMax = math.min(max - sectionOrigin, Section.Size);
                    ref SectionBlocks section = ref column.Sections[sectionIndex];

                    // A section covered completely becomes (or stays) uniform, no matter what it held.
                    if (math.all(localMin == 0) && math.all(localMax == Section.Size))
                    {
                        MakeUniform(ref section, id);
                        continue;
                    }
                    if (section.IsUniform)
                    {
                        if (section.UniformId == id)
                            continue;
                        MakeMixed(ref section);
                    }
                    for (int y = localMin.y; y < localMax.y; y++)
                    for (int z = localMin.z; z < localMax.z; z++)
                    for (int x = localMin.x; x < localMax.x; x++)
                        section.Blocks[Section.Index(x, y, z)] = id;
                }

                int2 columnOrigin = new int2(columnX, columnZ) * Section.Size;
                int2 skyMin = math.max(min.xz - columnOrigin, 0);
                int2 skyMax = math.min(max.xz - columnOrigin, Section.Size);
                for (int z = skyMin.y; z < skyMax.y; z++)
                for (int x = skyMin.x; x < skyMax.x; x++)
                    column.SkyStart[x | (z << 4)] = (ushort)FindSkyStart(column, x, z, Height - 1);
            }
        }

        /// <summary>Turns a mixed section back into a uniform one when all its blocks became equal.</summary>
        public bool TryCompact(Column column, int sectionIndex)
        {
            ref SectionBlocks section = ref column.Sections[sectionIndex];
            if (section.IsUniform)
                return true;
            ushort first = section.Blocks[0];
            for (int i = 1; i < Section.Volume; i++)
            {
                if (section.Blocks[i] != first)
                    return false;
            }
            MakeUniform(ref section, first);
            return true;
        }

        public void RecomputeSkyStart(Column column)
        {
            for (int z = 0; z < Section.Size; z++)
            for (int x = 0; x < Section.Size; x++)
                column.SkyStart[x | (z << 4)] = (ushort)FindSkyStart(column, x, z, Height - 1);
        }

        public void Dispose()
        {
            foreach (Column column in columns.Values)
            {
                for (int i = 0; i < column.Sections.Length; i++)
                    MakeUniform(ref column.Sections[i], 0);
                column.Dispose();
            }
            columns.Clear();
            while (freeColumns.Count > 0)
                freeColumns.Pop().Dispose();
            while (freeArrays.Count > 0)
                freeArrays.Pop().Dispose();
        }

        bool PassesLight(ushort id) => table[id].Is(BlockFlags.LightPasses);

        /// <summary>Walks down from <paramref name="fromY"/> to the first block that stops light.</summary>
        int FindSkyStart(Column column, int x, int z, int fromY)
        {
            int y = fromY;
            while (y >= 0)
            {
                ref SectionBlocks section = ref column.Sections[y >> 4];
                if (section.IsUniform)
                {
                    if (!PassesLight(section.UniformId))
                        return y + 1;
                    y = (y & ~15) - 1;
                    continue;
                }
                if (!PassesLight(section.Blocks[Section.Index(x, y & 15, z)]))
                    return y + 1;
                y--;
            }
            return 0;
        }

        void MakeMixed(ref SectionBlocks section)
        {
            NativeArray<ushort> blocks = freeArrays.Count > 0
                ? freeArrays.Pop()
                : new NativeArray<ushort>(Section.Volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < Section.Volume; i++)
                blocks[i] = section.UniformId;
            section.Blocks = blocks;
            MixedSectionCount++;
        }

        void MakeUniform(ref SectionBlocks section, ushort id)
        {
            if (section.Blocks.IsCreated)
            {
                freeArrays.Push(section.Blocks);
                section.Blocks = default;
                MixedSectionCount--;
            }
            section.UniformId = id;
        }
    }
}
