namespace NgcSharp.Core;

public static class GameCubeAddress
{
    public const int MainRamSize = 24 * 1024 * 1024;

    public const uint MainRamPhysicalStart = 0x0000_0000;
    public const uint MainRamPhysicalEnd = MainRamPhysicalStart + MainRamSize - 1;

    public const uint MainRamCachedStart = 0x8000_0000;
    public const uint MainRamCachedEnd = MainRamCachedStart + MainRamSize - 1;

    public const uint MainRamUncachedStart = 0xC000_0000;
    public const uint MainRamUncachedEnd = MainRamUncachedStart + MainRamSize - 1;

    public const uint EmbeddedFrameBufferStart = 0xC800_0000;
    public const uint HardwareRegistersStart = 0xCC00_0000;
    public const uint IplRomStart = 0xFFF0_0000;

    public static bool TryTranslateMainRam(uint address, out int offset)
    {
        if (address <= MainRamPhysicalEnd)
        {
            offset = checked((int)address);
            return true;
        }

        if (address >= MainRamCachedStart && address <= MainRamCachedEnd)
        {
            offset = checked((int)(address - MainRamCachedStart));
            return true;
        }

        if (address >= MainRamUncachedStart && address <= MainRamUncachedEnd)
        {
            offset = checked((int)(address - MainRamUncachedStart));
            return true;
        }

        offset = 0;
        return false;
    }
}
