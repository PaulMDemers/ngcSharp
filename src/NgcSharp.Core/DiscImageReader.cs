using ZstdSharp;

namespace NgcSharp.Core;

public sealed class DiscImageReader : IDisposable
{
    private const ulong RvzPaddingSectorSize = 0x8000;

    private readonly FileStream _stream;
    private readonly RvzState? _rvz;
    private byte[]? _cachedGroup;
    private int _cachedGroupIndex = -1;
    private bool _disposed;

    private DiscImageReader(FileStream stream, DiscImageInfo info, RvzState? rvz)
    {
        _stream = stream;
        Info = info;
        _rvz = rvz;
    }

    public DiscImageInfo Info { get; }

    public static DiscImageReader Open(string path)
    {
        DiscImageInfo info = DiscImageInfo.Load(path);
        FileStream stream = File.OpenRead(path);
        RvzState? rvz = info.Kind == DiscImageKind.Rvz ? RvzState.Load(stream, info.DiscHeader) : null;
        return new DiscImageReader(stream, info, rvz);
    }

    public byte[] ReadBytes(ulong offset, int size)
    {
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        byte[] buffer = new byte[size];
        Read(offset, buffer);
        return buffer;
    }

    public void Read(ulong offset, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Info.Kind == DiscImageKind.Iso)
        {
            _stream.Position = checked((long)offset);
            _stream.ReadExactly(destination);
            return;
        }

        ReadRvz(offset, destination);
    }

    public void Dispose()
    {
        _disposed = true;
        _stream.Dispose();
    }

    private void ReadRvz(ulong offset, Span<byte> destination)
    {
        RvzState rvz = _rvz ?? throw new InvalidOperationException("RVZ state is not initialized.");
        int written = 0;

        while (written < destination.Length)
        {
            RvzRawDataEntry rawDataEntry = rvz.FindRawDataEntry(offset);
            ulong rawEntryRelativeOffset = offset - rawDataEntry.DataOffset;
            int groupWithinEntry = checked((int)(rawEntryRelativeOffset / rvz.ChunkSize));
            int groupIndex = checked((int)rawDataEntry.GroupIndex + groupWithinEntry);
            ulong groupDiscOffset = rawDataEntry.DataOffset + (ulong)groupWithinEntry * rvz.ChunkSize;
            int offsetInGroup = checked((int)(offset - groupDiscOffset));
            int groupSize = checked((int)Math.Min(rvz.ChunkSize, rawDataEntry.DataSize - (ulong)groupWithinEntry * rvz.ChunkSize));
            byte[] group = ReadRvzGroup(rvz, groupIndex, groupSize, groupDiscOffset);
            int bytesToCopy = Math.Min(destination.Length - written, group.Length - offsetInGroup);

            group.AsSpan(offsetInGroup, bytesToCopy).CopyTo(destination[written..]);
            written += bytesToCopy;
            offset += checked((uint)bytesToCopy);
        }
    }

    private byte[] ReadRvzGroup(RvzState rvz, int groupIndex, int decompressedSize, ulong groupDiscOffset)
    {
        if (_cachedGroupIndex == groupIndex && _cachedGroup is not null)
        {
            return _cachedGroup;
        }

        RvzGroupEntry groupEntry = rvz.GroupEntries[groupIndex];
        byte[] group;

        if (groupEntry.StoredSize == 0)
        {
            group = new byte[decompressedSize];
        }
        else
        {
            _stream.Position = checked((long)groupEntry.FileOffset);
            byte[] stored = new byte[groupEntry.StoredSize];
            _stream.ReadExactly(stored);

            int unpackedSize = groupEntry.RvzPackedSize == 0 ? decompressedSize : checked((int)groupEntry.RvzPackedSize);
            byte[] unpacked = groupEntry.IsCompressed
                ? DecompressZstd(stored, unpackedSize)
                : stored;

            group = groupEntry.RvzPackedSize == 0
                ? unpacked
                : DecodeRvzPacking(unpacked, decompressedSize, groupDiscOffset);
        }

        _cachedGroup = group;
        _cachedGroupIndex = groupIndex;
        return group;
    }

    private static byte[] DecompressZstd(byte[] source, int decompressedSize)
    {
        byte[] destination = new byte[decompressedSize];
        using Decompressor decompressor = new();
        int written = decompressor.Unwrap(source, destination);
        return written == destination.Length ? destination : destination[..written];
    }

    private static byte[] DecodeRvzPacking(byte[] packed, int outputSize, ulong outputOffset)
    {
        byte[] output = new byte[outputSize];
        int inputOffset = 0;
        int outputPosition = 0;

        while (inputOffset < packed.Length && outputPosition < output.Length)
        {
            uint size = BigEndian.ReadUInt32(packed.AsSpan(inputOffset, sizeof(uint)));
            inputOffset += sizeof(uint);
            bool randomData = (size & 0x8000_0000) != 0;
            int segmentSize = checked((int)(size & 0x7FFF_FFFF));

            if (randomData)
            {
                RvzPaddingGenerator generator = new(packed.AsSpan(inputOffset, 68), outputOffset + (ulong)outputPosition);
                inputOffset += 68;
                generator.Fill(output.AsSpan(outputPosition, segmentSize));
            }
            else
            {
                packed.AsSpan(inputOffset, segmentSize).CopyTo(output.AsSpan(outputPosition));
                inputOffset += segmentSize;
            }

            outputPosition += segmentSize;
        }

        return output;
    }

    private sealed class RvzState
    {
        private RvzState(uint compression, uint chunkSize, RvzRawDataEntry[] rawDataEntries, RvzGroupEntry[] groupEntries)
        {
            Compression = compression;
            ChunkSize = chunkSize;
            RawDataEntries = rawDataEntries;
            GroupEntries = groupEntries;
        }

        public uint Compression { get; }

        public uint ChunkSize { get; }

        public RvzRawDataEntry[] RawDataEntries { get; }

        public RvzGroupEntry[] GroupEntries { get; }

        public static RvzState Load(FileStream stream, GameCubeDiscHeader discHeader)
        {
            stream.Position = 0;
            Span<byte> header1 = stackalloc byte[0x48];
            stream.ReadExactly(header1);
            uint header2Size = BigEndian.ReadUInt32(header1.Slice(0x0C, sizeof(uint)));
            byte[] header2 = new byte[header2Size];
            stream.ReadExactly(header2);

            uint compression = BigEndian.ReadUInt32(header2.AsSpan(0x04, sizeof(uint)));
            uint chunkSize = BigEndian.ReadUInt32(header2.AsSpan(0x0C, sizeof(uint)));
            uint rawDataEntryCount = BigEndian.ReadUInt32(header2.AsSpan(0xB4, sizeof(uint)));
            ulong rawDataEntriesOffset = BigEndian.ReadUInt64(header2.AsSpan(0xB8, sizeof(ulong)));
            uint rawDataEntriesSize = BigEndian.ReadUInt32(header2.AsSpan(0xC0, sizeof(uint)));
            uint groupEntryCount = BigEndian.ReadUInt32(header2.AsSpan(0xC4, sizeof(uint)));
            ulong groupEntriesOffset = BigEndian.ReadUInt64(header2.AsSpan(0xC8, sizeof(ulong)));
            uint groupEntriesSize = BigEndian.ReadUInt32(header2.AsSpan(0xD0, sizeof(uint)));

            if (compression != 5)
            {
                throw new NotSupportedException($"Only zstd RVZ images are supported for now. Compression={compression}.");
            }

            byte[] rawDataBytes = ReadAndDecompressTable(stream, rawDataEntriesOffset, rawDataEntriesSize, checked((int)rawDataEntryCount) * 0x18);
            byte[] groupBytes = ReadAndDecompressTable(stream, groupEntriesOffset, groupEntriesSize, checked((int)groupEntryCount) * 0x0C);

            return new RvzState(
                compression,
                chunkSize,
                ParseRawDataEntries(rawDataBytes, checked((int)rawDataEntryCount)),
                ParseGroupEntries(groupBytes, checked((int)groupEntryCount)));
        }

        public RvzRawDataEntry FindRawDataEntry(ulong offset)
        {
            foreach (RvzRawDataEntry entry in RawDataEntries)
            {
                if (offset >= entry.DataOffset && offset < entry.DataOffset + entry.DataSize)
                {
                    return entry;
                }
            }

            throw new InvalidDataException($"RVZ image does not map disc offset 0x{offset:X}.");
        }

        private static byte[] ReadAndDecompressTable(FileStream stream, ulong offset, uint storedSize, int decompressedSize)
        {
            stream.Position = checked((long)offset);
            byte[] stored = new byte[storedSize];
            stream.ReadExactly(stored);
            return DecompressZstd(stored, decompressedSize);
        }

        private static RvzRawDataEntry[] ParseRawDataEntries(byte[] bytes, int count)
        {
            RvzRawDataEntry[] entries = new RvzRawDataEntry[count];
            for (int index = 0; index < entries.Length; index++)
            {
                ReadOnlySpan<byte> entry = bytes.AsSpan(index * 0x18, 0x18);
                ulong dataOffset = BigEndian.ReadUInt64(entry[..8]);
                ulong dataSize = BigEndian.ReadUInt64(entry.Slice(8, 8));
                ulong sectorRemainder = dataOffset % RvzPaddingSectorSize;
                dataOffset -= sectorRemainder;
                dataSize += sectorRemainder;

                entries[index] = new RvzRawDataEntry(
                    dataOffset,
                    dataSize,
                    BigEndian.ReadUInt32(entry.Slice(16, 4)),
                    BigEndian.ReadUInt32(entry.Slice(20, 4)));
            }

            return entries;
        }

        private static RvzGroupEntry[] ParseGroupEntries(byte[] bytes, int count)
        {
            RvzGroupEntry[] entries = new RvzGroupEntry[count];
            for (int index = 0; index < entries.Length; index++)
            {
                ReadOnlySpan<byte> entry = bytes.AsSpan(index * 0x0C, 0x0C);
                uint dataSize = BigEndian.ReadUInt32(entry.Slice(4, 4));
                entries[index] = new RvzGroupEntry(
                    (ulong)BigEndian.ReadUInt32(entry[..4]) * 4,
                    checked((int)(dataSize & 0x7FFF_FFFF)),
                    (dataSize & 0x8000_0000) != 0,
                    BigEndian.ReadUInt32(entry.Slice(8, 4)));
            }

            return entries;
        }
    }

    private sealed record RvzRawDataEntry(ulong DataOffset, ulong DataSize, uint GroupIndex, uint GroupCount);

    private sealed record RvzGroupEntry(ulong FileOffset, int StoredSize, bool IsCompressed, uint RvzPackedSize);

    private sealed class RvzPaddingGenerator
    {
        private readonly uint[] _buffer = new uint[521];
        private int _wordIndex;
        private int _byteIndex;

        public RvzPaddingGenerator(ReadOnlySpan<byte> seed, ulong outputOffset)
        {
            for (int i = 0; i < 17; i++)
            {
                _buffer[i] = BigEndian.ReadUInt32(seed.Slice(i * 4, 4));
            }

            for (int i = 17; i < _buffer.Length; i++)
            {
                _buffer[i] = (_buffer[i - 17] << 23) ^ (_buffer[i - 16] >> 9) ^ _buffer[i - 1];
            }

            for (int i = 0; i < 4; i++)
            {
                AdvanceBuffer();
            }

            Skip((int)(outputOffset % 0x8000));
        }

        public void Fill(Span<byte> destination)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = NextByte();
            }
        }

        private void Skip(int bytes)
        {
            for (int i = 0; i < bytes; i++)
            {
                NextByte();
            }
        }

        private byte NextByte()
        {
            if (_wordIndex >= _buffer.Length)
            {
                AdvanceBuffer();
                _wordIndex = 0;
                _byteIndex = 0;
            }

            uint value = _buffer[_wordIndex];
            byte result = _byteIndex switch
            {
                0 => (byte)(value >> 24),
                1 => (byte)(value >> 18),
                2 => (byte)(value >> 8),
                _ => (byte)value,
            };

            _byteIndex++;
            if (_byteIndex == 4)
            {
                _byteIndex = 0;
                _wordIndex++;
            }

            return result;
        }

        private void AdvanceBuffer()
        {
            for (int i = 0; i < 32; i++)
            {
                _buffer[i] ^= _buffer[i + 521 - 32];
            }

            for (int i = 32; i < _buffer.Length; i++)
            {
                _buffer[i] ^= _buffer[i - 32];
            }
        }
    }
}
