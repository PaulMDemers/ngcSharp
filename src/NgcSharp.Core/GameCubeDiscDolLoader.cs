namespace NgcSharp.Core;

public static class GameCubeDiscDolLoader
{
    private const int TextSectionCount = 7;
    private const int DataSectionCount = 11;
    private const int DolHeaderSize = 0x100;

    public static DolFile LoadMainDol(DiscImageReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        GameCubeDiscLayout layout = GameCubeDiscLayout.Read(reader);
        if (layout.MainDolOffset == 0)
        {
            throw new InvalidDataException("Disc header does not contain a main DOL offset.");
        }

        return LoadDol(reader, layout.MainDolOffset);
    }

    public static int GetMainDolImageSize(DiscImageReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        GameCubeDiscLayout layout = GameCubeDiscLayout.Read(reader);
        if (layout.MainDolOffset == 0)
        {
            throw new InvalidDataException("Disc header does not contain a main DOL offset.");
        }

        byte[] header = reader.ReadBytes(layout.MainDolOffset, DolHeaderSize);
        return GetDolImageSize(header);
    }

    public static DolFile LoadDol(DiscImageReader reader, uint discOffset)
    {
        ArgumentNullException.ThrowIfNull(reader);

        byte[] header = reader.ReadBytes(discOffset, DolHeaderSize);
        int imageSize = GetDolImageSize(header);
        byte[] image = reader.ReadBytes(discOffset, imageSize);
        return DolFile.Parse(image);
    }

    public static int GetDolImageSize(ReadOnlySpan<byte> header)
    {
        if (header.Length < DolHeaderSize)
        {
            throw new InvalidDataException("DOL header is smaller than 0x100 bytes.");
        }

        uint endOffset = DolHeaderSize;
        endOffset = Math.Max(endOffset, GetSectionTableEnd(header, 0x000, 0x090, TextSectionCount));
        endOffset = Math.Max(endOffset, GetSectionTableEnd(header, 0x01C, 0x0AC, DataSectionCount));

        if (endOffset > int.MaxValue)
        {
            throw new InvalidDataException("DOL image is too large to load.");
        }

        return checked((int)endOffset);
    }

    private static uint GetSectionTableEnd(ReadOnlySpan<byte> header, int offsetTable, int sizeTable, int count)
    {
        uint endOffset = 0;

        for (int index = 0; index < count; index++)
        {
            uint sectionOffset = BigEndian.ReadUInt32(header.Slice(offsetTable + index * sizeof(uint), sizeof(uint)));
            uint sectionSize = BigEndian.ReadUInt32(header.Slice(sizeTable + index * sizeof(uint), sizeof(uint)));
            if (sectionSize == 0)
            {
                continue;
            }

            if (sectionOffset < DolHeaderSize)
            {
                throw new InvalidDataException("DOL section points inside the header.");
            }

            endOffset = Math.Max(endOffset, checked(sectionOffset + sectionSize));
        }

        return endOffset;
    }
}
