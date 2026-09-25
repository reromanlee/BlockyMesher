using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;

namespace reromanlee.BlockyMesher.Storage
{
    /// <summary>
    /// What players changed, kept per section as differences from the generated terrain, so columns
    /// can leave memory and be regenerated later with their edits put back. A section with a few
    /// edits keeps a list of them (4 bytes each). A section that was reshaped keeps a compressed copy
    /// of all its blocks instead: whichever is smaller when its column unloads.
    /// </summary>
    internal sealed class EditStore
    {
        const int FormatVersion = 1;

        /// <summary>Past this many single edits, a compressed copy of the section might be smaller.</summary>
        const int ManyEdits = 128;

        sealed class SectionEdits
        {
            /// <summary>Block index in the section → new id. Null while a copy is kept instead.</summary>
            public Dictionary<ushort, ushort> Blocks = new();

            public int[] Palette;
            public byte[] Packed;
            public int BitsPerBlock;

            /// <summary>Changed in ways a list doesn't capture well: copy the whole section when it unloads.</summary>
            public bool CopyWhenUnloading;

            public bool HasCopy => Packed != null;
            public int Bytes => HasCopy ? Packed.Length + Palette.Length * sizeof(ushort) : Blocks.Count * 4;
        }

        readonly Dictionary<int3, SectionEdits> sections = new();
        readonly List<int> palette = new();
        readonly ushort[] scratch = new ushort[Section.Volume];

        public int SectionCount => sections.Count;

        public long Bytes
        {
            get
            {
                long total = 0;
                foreach (SectionEdits edits in sections.Values)
                    total += edits.Bytes;
                return total;
            }
        }

        public void RecordBlock(int3 block, ushort id)
        {
            SectionEdits edits = Get(block >> 4);
            if (edits.HasCopy || edits.CopyWhenUnloading)
                edits.CopyWhenUnloading = true;
            else
                edits.Blocks[(ushort)Section.Index(block & 15)] = id;
        }

        /// <summary>For area edits, like fills and placed patterns: the section will be copied whole.</summary>
        public void RecordSection(int3 section) => Get(section).CopyWhenUnloading = true;

        /// <summary>Before a column leaves memory: copies the sections that need it.</summary>
        public void Unloading(BlockStorage storage, Column column)
        {
            for (int y = 0; y < column.Sections.Length; y++)
            {
                if (sections.TryGetValue(new int3(column.Position.x, y, column.Position.y), out SectionEdits edits)
                    && (edits.CopyWhenUnloading || edits.Blocks?.Count > ManyEdits))
                {
                    Compact(storage, column, y, edits);
                }
            }
        }

        /// <summary>After a column is generated: puts its edits back. Returns whether it had any.</summary>
        public bool Apply(BlockStorage storage, Column column)
        {
            bool changed = false;
            for (int y = 0; y < column.Sections.Length; y++)
            {
                if (!sections.TryGetValue(new int3(column.Position.x, y, column.Position.y), out SectionEdits edits))
                    continue;
                if (edits.HasCopy)
                {
                    BlockPacking.Unpack(edits.Packed, edits.BitsPerBlock, edits.Palette, scratch);
                    storage.ReplaceSection(column, y, scratch);
                }
                else
                {
                    foreach (KeyValuePair<ushort, ushort> edit in edits.Blocks)
                        storage.SetInSection(column, y, edit.Key, edit.Value);
                }
                changed = true;
            }
            if (changed)
                storage.RecomputeSkyStart(column);
            return changed;
        }

        public void Clear() => sections.Clear();

        /// <summary>Every edit as bytes, including those in columns still loaded.</summary>
        public byte[] Save(BlockStorage storage)
        {
            foreach (KeyValuePair<int3, SectionEdits> entry in sections)
            {
                if (entry.Value.CopyWhenUnloading && storage.TryGetColumn(entry.Key.xz, out Column column))
                    Compact(storage, column, entry.Key.y, entry.Value);
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(FormatVersion);
            writer.Write(sections.Count);
            foreach (KeyValuePair<int3, SectionEdits> entry in sections)
            {
                writer.Write(entry.Key.x);
                writer.Write(entry.Key.y);
                writer.Write(entry.Key.z);
                SectionEdits edits = entry.Value;
                writer.Write(edits.HasCopy);
                if (edits.HasCopy)
                {
                    writer.Write(edits.BitsPerBlock);
                    writer.Write(edits.Palette.Length);
                    foreach (int id in edits.Palette)
                        writer.Write((ushort)id);
                    writer.Write(edits.Packed.Length);
                    writer.Write(edits.Packed);
                }
                else
                {
                    writer.Write(edits.Blocks.Count);
                    foreach (KeyValuePair<ushort, ushort> edit in edits.Blocks)
                    {
                        writer.Write(edit.Key);
                        writer.Write(edit.Value);
                    }
                }
            }
            return stream.ToArray();
        }

        /// <summary>Replaces every edit with those saved by <see cref="Save"/>.</summary>
        public void Load(byte[] data)
        {
            sections.Clear();
            using var reader = new BinaryReader(new MemoryStream(data));
            int version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"These edits were saved in format {version}, only format {FormatVersion} can be read.");
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var key = new int3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                var edits = new SectionEdits();
                if (reader.ReadBoolean())
                {
                    edits.Blocks = null;
                    edits.BitsPerBlock = reader.ReadInt32();
                    edits.Palette = new int[reader.ReadInt32()];
                    for (int p = 0; p < edits.Palette.Length; p++)
                        edits.Palette[p] = reader.ReadUInt16();
                    edits.Packed = reader.ReadBytes(reader.ReadInt32());
                }
                else
                {
                    int editCount = reader.ReadInt32();
                    for (int e = 0; e < editCount; e++)
                        edits.Blocks[reader.ReadUInt16()] = reader.ReadUInt16();
                }
                sections[key] = edits;
            }
        }

        /// <summary>Test hook: whether a section's edits are kept as a compressed copy.</summary>
        internal bool IsKeptAsCopy(int3 section) => sections.TryGetValue(section, out SectionEdits edits) && edits.HasCopy;

        SectionEdits Get(int3 section)
        {
            if (!sections.TryGetValue(section, out SectionEdits edits))
                sections.Add(section, edits = new SectionEdits());
            return edits;
        }

        /// <summary>Copies the section as it is now, unless its edit list is still the smaller of the two.</summary>
        void Compact(BlockStorage storage, Column column, int sectionIndex, SectionEdits edits)
        {
            storage.CopySection(column, sectionIndex, scratch);
            byte[] packed = BlockPacking.Pack(scratch, palette, out int bitsPerBlock);
            int copyBytes = packed.Length + palette.Count * sizeof(ushort);
            if (!edits.CopyWhenUnloading && edits.Blocks != null && edits.Blocks.Count * 4 <= copyBytes)
                return;
            edits.Palette = palette.ToArray();
            edits.Packed = packed;
            edits.BitsPerBlock = bitsPerBlock;
            edits.Blocks = null;
            edits.CopyWhenUnloading = false;
        }
    }
}
