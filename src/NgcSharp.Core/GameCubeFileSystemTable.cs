using System.Text;

namespace NgcSharp.Core;

public sealed class GameCubeFileSystemTable
{
    private const int EntrySize = 0x0C;

    private GameCubeFileSystemTable(IReadOnlyList<GameCubeFileSystemEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<GameCubeFileSystemEntry> Entries { get; }

    public IEnumerable<GameCubeFileSystemEntry> Files => Entries.Where(static entry => !entry.IsDirectory);

    public static GameCubeFileSystemTable Load(DiscImageReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        GameCubeDiscLayout layout = GameCubeDiscLayout.Read(reader);
        if (layout.FileSystemTableOffset == 0 || layout.FileSystemTableSize < EntrySize)
        {
            throw new InvalidDataException("Disc header does not contain a usable FST.");
        }

        byte[] bytes = reader.ReadBytes(layout.FileSystemTableOffset, checked((int)layout.FileSystemTableSize));
        int entryCount = checked((int)BigEndian.ReadUInt32(bytes.AsSpan(8, sizeof(uint))));
        if (entryCount <= 0 || entryCount * EntrySize > bytes.Length)
        {
            throw new InvalidDataException("FST root entry contains an invalid entry count.");
        }

        ReadOnlySpan<byte> stringTable = bytes.AsSpan(entryCount * EntrySize);
        List<RawEntry> rawEntries = new(entryCount);
        for (int index = 0; index < entryCount; index++)
        {
            ReadOnlySpan<byte> entry = bytes.AsSpan(index * EntrySize, EntrySize);
            uint typeAndName = BigEndian.ReadUInt32(entry[..4]);
            bool isDirectory = (typeAndName & 0xFF00_0000) != 0;
            rawEntries.Add(new RawEntry(
                isDirectory,
                typeAndName & 0x00FF_FFFF,
                BigEndian.ReadUInt32(entry.Slice(4, 4)),
                BigEndian.ReadUInt32(entry.Slice(8, 4))));
        }

        List<GameCubeFileSystemEntry> entries = new(entryCount);
        BuildEntries(rawEntries, stringTable, entries, directoryIndex: 0, prefix: string.Empty);
        return new GameCubeFileSystemTable(entries);
    }

    private static void BuildEntries(
        IReadOnlyList<RawEntry> rawEntries,
        ReadOnlySpan<byte> stringTable,
        List<GameCubeFileSystemEntry> entries,
        int directoryIndex,
        string prefix)
    {
        RawEntry directory = rawEntries[directoryIndex];
        int nextIndex = checked((int)directory.SizeOrNextIndex);
        entries.Add(new GameCubeFileSystemEntry(
            prefix.Length == 0 ? "/" : prefix,
            IsDirectory: true,
            DiscOffset: 0,
            Size: 0,
            ParentIndex: checked((int)directory.OffsetOrParent),
            NextIndex: nextIndex));

        for (int index = directoryIndex + 1; index < nextIndex;)
        {
            RawEntry rawEntry = rawEntries[index];
            string name = ReadString(stringTable, rawEntry.NameOffset);
            string path = prefix.Length == 0 ? name : $"{prefix}/{name}";

            if (rawEntry.IsDirectory)
            {
                BuildEntries(rawEntries, stringTable, entries, index, path);
                index = checked((int)rawEntry.SizeOrNextIndex);
            }
            else
            {
                entries.Add(new GameCubeFileSystemEntry(
                    path,
                    IsDirectory: false,
                    DiscOffset: rawEntry.OffsetOrParent,
                    Size: rawEntry.SizeOrNextIndex,
                    ParentIndex: directoryIndex,
                    NextIndex: index + 1));
                index++;
            }
        }
    }

    private static string ReadString(ReadOnlySpan<byte> stringTable, uint offset)
    {
        if (offset > stringTable.Length)
        {
            throw new InvalidDataException("FST name offset points outside the string table.");
        }

        ReadOnlySpan<byte> nameBytes = stringTable[checked((int)offset)..];
        int length = nameBytes.IndexOf((byte)0);
        if (length < 0)
        {
            throw new InvalidDataException("FST name is not null terminated.");
        }

        return Encoding.ASCII.GetString(nameBytes[..length]);
    }

    private sealed record RawEntry(bool IsDirectory, uint NameOffset, uint OffsetOrParent, uint SizeOrNextIndex);
}
