namespace NgcSharp.Core;

public static class GameCubeStandaloneBoot
{
    public static void PrepareMemory(GameCubeMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        memory.EnableMainRamTopGuard = true;

        GameCubeBootMemory.WriteCommonLowMemory(
            memory,
            arenaLow: 0,
            arenaHigh: GameCubeBootMemory.StandaloneBi2Address,
            fileSystemTableAddress: 0,
            fileSystemTableMaxSize: 0,
            bi2Address: GameCubeBootMemory.StandaloneBi2Address);

        WriteSyntheticBi2(memory);
    }

    private static void WriteSyntheticBi2(GameCubeMemory memory)
    {
        uint address = GameCubeBootMemory.StandaloneBi2Address;
        GameCubeBootMemory.Write32(memory, address + 0x00, GameCubeAddress.MainRamSize);
        GameCubeBootMemory.Write32(memory, address + 0x04, 0);
        GameCubeBootMemory.Write32(memory, address + 0x08, GameCubeBootMemory.BusClock);
        GameCubeBootMemory.Write32(memory, address + 0x0C, GameCubeBootMemory.CpuClock);
    }
}
