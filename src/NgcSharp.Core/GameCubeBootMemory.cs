namespace NgcSharp.Core;

public static class GameCubeBootMemory
{
    public const uint MemoryTop = 0x8180_0000;
    public const uint NormalBootMagic = 0x0D15_EA5E;
    public const uint RetailConsoleType = 0x0000_0003;
    public const uint AramSize = 0x0100_0000;
    public const uint DefaultThreadAddress = 0x803F_0000;
    public const uint BusClock = 0x09A7_EC80;
    public const uint CpuClock = 0x1CF7_C580;
    public const uint StandaloneBi2Address = 0x817F_DF80;

    public static void WriteCommonLowMemory(
        GameCubeMemory memory,
        uint arenaLow,
        uint arenaHigh,
        uint fileSystemTableAddress,
        uint fileSystemTableMaxSize,
        uint bi2Address)
    {
        ArgumentNullException.ThrowIfNull(memory);

        Write32(memory, 0x8000_001C, GameCubeDiscHeader.ExpectedMagic);
        Write32(memory, 0x8000_0020, NormalBootMagic);
        Write32(memory, 0x8000_0024, 1);
        Write32(memory, 0x8000_0028, GameCubeAddress.MainRamSize);
        Write32(memory, 0x8000_002C, RetailConsoleType);
        Write32(memory, 0x8000_0030, arenaLow);
        Write32(memory, 0x8000_0034, arenaHigh);
        Write32(memory, 0x8000_0038, fileSystemTableAddress);
        Write32(memory, 0x8000_003C, fileSystemTableMaxSize);
        Write32(memory, 0x8000_00D0, AramSize);
        Write32(memory, 0x8000_00D8, DefaultThreadAddress);
        Write32(memory, 0x8000_00E4, DefaultThreadAddress);
        Write32(memory, 0x8000_00EC, MemoryTop);
        Write32(memory, 0x8000_00F0, GameCubeAddress.MainRamSize);
        Write32(memory, 0x8000_00F4, bi2Address);
        Write32(memory, 0x8000_00F8, BusClock);
        Write32(memory, 0x8000_00FC, CpuClock);
    }

    internal static void Write32(GameCubeMemory memory, uint address, uint value)
    {
        memory.Write32(address, value);
    }
}
