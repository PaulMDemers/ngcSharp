using System.Text;

namespace NgcSharp.Core;

public sealed record GameCubeDiscHeader(
    string GameId,
    string MakerCode,
    byte DiscNumber,
    byte Version,
    uint Magic,
    string Title)
{
    public const int Size = 0x80;
    public const uint ExpectedMagic = 0xC233_9F3D;

    public bool IsGameCubeDisc => Magic == ExpectedMagic;

    public static GameCubeDiscHeader Parse(ReadOnlySpan<byte> header)
    {
        if (header.Length < Size)
        {
            throw new InvalidDataException("GameCube disc header is smaller than 0x80 bytes.");
        }

        string gameId = ReadAscii(header[..6]);
        string makerCode = ReadAscii(header.Slice(4, 2));
        byte discNumber = header[6];
        byte version = header[7];
        uint magic = BigEndian.ReadUInt32(header.Slice(0x1C, sizeof(uint)));
        string title = ReadAscii(header.Slice(0x20, 0x60));

        return new GameCubeDiscHeader(gameId, makerCode, discNumber, version, magic, title);
    }

    private static string ReadAscii(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.IndexOf((byte)0);
        if (length < 0)
        {
            length = bytes.Length;
        }

        return Encoding.ASCII.GetString(bytes[..length]).TrimEnd();
    }
}
