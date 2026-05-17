namespace NgcSharp.Hw;

[Flags]
public enum InterruptSource : uint
{
    None = 0,
    PixelEngineToken = 1 << 0,
    PixelEngineFinish = 1 << 1,
    VideoInterface = 1 << 2,
    DiscInterface = 1 << 3,
    AudioInterface = 1 << 4,
    Dsp = 1 << 5,
    MemoryInterface = 1 << 6,
    SerialInterface = 1 << 7,
    ExternalInterface = 1 << 12,
}
