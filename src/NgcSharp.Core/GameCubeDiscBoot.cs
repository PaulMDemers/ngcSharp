namespace NgcSharp.Core;

public static class GameCubeDiscBoot
{
    private const uint DefaultArenaLow = 0x0000_0000;

    public static GameCubeDiscBootInfo PrepareMemory(DiscImageReader reader, GameCubeMemory memory)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(memory);

        GameCubeDiscLayout layout = GameCubeDiscLayout.Read(reader);
        if (layout.FileSystemTableOffset == 0 || layout.FileSystemTableSize == 0)
        {
            throw new InvalidDataException("Disc header does not contain a usable FST location.");
        }

        uint fstAddress = AlignDown(GameCubeBootMemory.MemoryTop - layout.FileSystemTableSize, 32);
        byte[] discHeader = reader.ReadBytes(0, GameCubeDiscHeader.Size);
        byte[] fst = reader.ReadBytes(layout.FileSystemTableOffset, checked((int)layout.FileSystemTableSize));

        memory.Load(0x8000_0000, discHeader);
        memory.Load(fstAddress, fst);

        uint fstMaxSize = layout.FileSystemTableMaxSize == 0 ? layout.FileSystemTableSize : layout.FileSystemTableMaxSize;
        GameCubeBootMemory.WriteCommonLowMemory(
            memory,
            DefaultArenaLow,
            arenaHigh: fstAddress,
            fileSystemTableAddress: fstAddress,
            fileSystemTableMaxSize: fstMaxSize,
            bi2Address: 0);

        GameCubeBootMemory.Write32(memory, 0x8000_30D4, checked((uint)GameCubeDiscDolLoader.GetMainDolImageSize(reader)));

        return new GameCubeDiscBootInfo(fstAddress, layout.FileSystemTableSize, layout.FileSystemTableMaxSize);
    }

    private static uint AlignDown(uint value, uint alignment)
    {
        return value & ~(alignment - 1);
    }
}
