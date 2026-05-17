namespace NgcSharp.Core;

public sealed record DiscImageInfo(
    string Path,
    DiscImageKind Kind,
    GameCubeDiscHeader DiscHeader,
    ulong DiscSize,
    ulong ContainerSize,
    uint? RvzVersion = null,
    uint? RvzCompatibleVersion = null,
    uint? RvzCompression = null,
    int? RvzCompressionLevel = null,
    uint? RvzChunkSize = null,
    bool HasNkitMarker = false)
{
    private static readonly byte[] RvzMagic = "RVZ\x1"u8.ToArray();

    public static DiscImageInfo Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using FileStream stream = File.OpenRead(path);
        Span<byte> magic = stackalloc byte[4];
        ReadExactly(stream, magic);
        stream.Position = 0;

        if (magic.SequenceEqual(RvzMagic))
        {
            return LoadRvz(path, stream);
        }

        return LoadIso(path, stream);
    }

    private static DiscImageInfo LoadIso(string path, FileStream stream)
    {
        Span<byte> header = stackalloc byte[GameCubeDiscHeader.Size];
        ReadExactly(stream, header);

        GameCubeDiscHeader discHeader = GameCubeDiscHeader.Parse(header);
        ulong size = checked((ulong)stream.Length);
        return new DiscImageInfo(path, DiscImageKind.Iso, discHeader, DiscSize: size, ContainerSize: size);
    }

    private static DiscImageInfo LoadRvz(string path, FileStream stream)
    {
        Span<byte> header1 = stackalloc byte[0x48];
        ReadExactly(stream, header1);

        uint version = BigEndian.ReadUInt32(header1.Slice(0x04, sizeof(uint)));
        uint compatibleVersion = BigEndian.ReadUInt32(header1.Slice(0x08, sizeof(uint)));
        uint header2Size = BigEndian.ReadUInt32(header1.Slice(0x0C, sizeof(uint)));
        ulong discSize = BigEndian.ReadUInt64(header1.Slice(0x24, sizeof(ulong)));
        ulong containerSize = BigEndian.ReadUInt64(header1.Slice(0x2C, sizeof(ulong)));

        if (header2Size < 0x90 || header2Size > 0x10000)
        {
            throw new InvalidDataException($"Unsupported RVZ header 2 size 0x{header2Size:X}.");
        }

        byte[] header2 = new byte[header2Size];
        ReadExactly(stream, header2);

        uint discType = BigEndian.ReadUInt32(header2.AsSpan(0x00, sizeof(uint)));
        if (discType != 1)
        {
            throw new InvalidDataException($"Expected a GameCube RVZ image, found disc type {discType}.");
        }

        uint compression = BigEndian.ReadUInt32(header2.AsSpan(0x04, sizeof(uint)));
        int compressionLevel = unchecked((int)BigEndian.ReadUInt32(header2.AsSpan(0x08, sizeof(uint))));
        uint chunkSize = BigEndian.ReadUInt32(header2.AsSpan(0x0C, sizeof(uint)));
        GameCubeDiscHeader discHeader = GameCubeDiscHeader.Parse(header2.AsSpan(0x10, GameCubeDiscHeader.Size));
        bool hasNkitMarker = ContainsNkitMarker(header2) || ContainsNkitMarker(ReadHeaderTail(stream));

        return new DiscImageInfo(
            path,
            DiscImageKind.Rvz,
            discHeader,
            discSize,
            containerSize,
            RvzVersion: version,
            RvzCompatibleVersion: compatibleVersion,
            RvzCompression: compression,
            RvzCompressionLevel: compressionLevel,
            RvzChunkSize: chunkSize,
            HasNkitMarker: hasNkitMarker);
    }

    private static bool ContainsNkitMarker(ReadOnlySpan<byte> bytes)
    {
        return bytes.IndexOf("NKIT"u8) >= 0;
    }

    private static byte[] ReadHeaderTail(Stream stream)
    {
        long remaining = Math.Max(0, stream.Length - stream.Position);
        byte[] buffer = new byte[Math.Min(0x400, checked((int)Math.Min(remaining, int.MaxValue)))];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        stream.ReadExactly(buffer);
    }
}
