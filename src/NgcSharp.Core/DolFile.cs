namespace NgcSharp.Core;

public sealed class DolFile
{
    private const int TextSectionCount = 7;
    private const int DataSectionCount = 11;
    private const int HeaderSize = 0x100;

    private DolFile(
        IReadOnlyList<DolSection> textSections,
        IReadOnlyList<DolSection> dataSections,
        uint bssAddress,
        uint bssSize,
        uint entryPoint)
    {
        TextSections = textSections;
        DataSections = dataSections;
        BssAddress = bssAddress;
        BssSize = bssSize;
        EntryPoint = entryPoint;
    }

    public IReadOnlyList<DolSection> TextSections { get; }

    public IReadOnlyList<DolSection> DataSections { get; }

    public uint BssAddress { get; }

    public uint BssSize { get; }

    public uint EntryPoint { get; }

    public IEnumerable<DolSection> Sections => TextSections.Concat(DataSections);

    public static DolFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllBytes(path));
    }

    public static DolFile Parse(ReadOnlySpan<byte> image)
    {
        if (image.Length < HeaderSize)
        {
            throw new InvalidDataException("DOL image is smaller than its 0x100-byte header.");
        }

        Span<uint> textOffsets = stackalloc uint[TextSectionCount];
        Span<uint> dataOffsets = stackalloc uint[DataSectionCount];
        Span<uint> textAddresses = stackalloc uint[TextSectionCount];
        Span<uint> dataAddresses = stackalloc uint[DataSectionCount];
        Span<uint> textSizes = stackalloc uint[TextSectionCount];
        Span<uint> dataSizes = stackalloc uint[DataSectionCount];

        ReadUInt32Table(image, 0x000, textOffsets);
        ReadUInt32Table(image, 0x01C, dataOffsets);
        ReadUInt32Table(image, 0x048, textAddresses);
        ReadUInt32Table(image, 0x064, dataAddresses);
        ReadUInt32Table(image, 0x090, textSizes);
        ReadUInt32Table(image, 0x0AC, dataSizes);

        uint bssAddress = BigEndian.ReadUInt32(image.Slice(0x0D8, sizeof(uint)));
        uint bssSize = BigEndian.ReadUInt32(image.Slice(0x0DC, sizeof(uint)));
        uint entryPoint = BigEndian.ReadUInt32(image.Slice(0x0E0, sizeof(uint)));

        List<DolSection> textSections = BuildSections(image, "text", textOffsets, textAddresses, textSizes);
        List<DolSection> dataSections = BuildSections(image, "data", dataOffsets, dataAddresses, dataSizes);

        return new DolFile(textSections, dataSections, bssAddress, bssSize, entryPoint);
    }

    public void LoadInto(GameCubeMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        memory.Clear(BssAddress, BssSize);

        foreach (DolSection section in Sections)
        {
            memory.Load(section.Address, section.Data.Span);
        }
    }

    private static void ReadUInt32Table(ReadOnlySpan<byte> image, int offset, Span<uint> destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            destination[index] = BigEndian.ReadUInt32(image.Slice(offset + index * sizeof(uint), sizeof(uint)));
        }
    }

    private static List<DolSection> BuildSections(
        ReadOnlySpan<byte> image,
        string kind,
        ReadOnlySpan<uint> fileOffsets,
        ReadOnlySpan<uint> addresses,
        ReadOnlySpan<uint> sizes)
    {
        List<DolSection> sections = [];

        for (int index = 0; index < sizes.Length; index++)
        {
            uint size = sizes[index];
            if (size == 0)
            {
                continue;
            }

            uint fileOffset = fileOffsets[index];
            if (fileOffset > int.MaxValue || size > int.MaxValue || fileOffset + size > image.Length)
            {
                throw new InvalidDataException($"DOL {kind}{index} section points outside the image.");
            }

            byte[] data = image.Slice(checked((int)fileOffset), checked((int)size)).ToArray();
            sections.Add(new DolSection($"{kind}{index}", fileOffset, addresses[index], data));
        }

        return sections;
    }
}
