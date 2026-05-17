namespace NgcSharp.Core;

public sealed record GameCubeDiscLayout(
    uint MainDolOffset,
    uint FileSystemTableOffset,
    uint FileSystemTableSize,
    uint FileSystemTableMaxSize)
{
    public static GameCubeDiscLayout Read(DiscImageReader reader)
    {
        byte[] bytes = reader.ReadBytes(0x420, 0x10);
        return new GameCubeDiscLayout(
            BigEndian.ReadUInt32(bytes.AsSpan(0x00, 4)),
            BigEndian.ReadUInt32(bytes.AsSpan(0x04, 4)),
            BigEndian.ReadUInt32(bytes.AsSpan(0x08, 4)),
            BigEndian.ReadUInt32(bytes.AsSpan(0x0C, 4)));
    }
}
