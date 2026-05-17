using NgcSharp.App;
using NgcSharp.Core;

namespace NgcSharp.Tests;

public sealed class FramebufferDumperTests
{
    [Fact]
    public void RejectsOversizedFrameDumpsBeforeAllocatingRgbBuffer()
    {
        GameCubeBus bus = new();
        RunDolOptions options = new(
            "game.dol",
            MaxInstructions: 0,
            Trace: false,
            TracePath: null,
            DumpRegisters: false,
            DumpMmio: false,
            Quiet: true,
            FrameDumpPath: "oversized.png",
            FrameAddress: 0x8000_1000,
            FrameWidth: 4096,
            FrameHeight: 4096,
            FrameFormat: FramebufferPixelFormat.Rgb565);

        bool dumped = FramebufferDumper.TryDump(bus, options, out FramebufferDumpResult? result, out string? error);

        Assert.False(dumped);
        Assert.Null(result);
        Assert.Contains("too large", error);
    }

    [Fact]
    public void CapturesRgb565PixelsAsRgbTriples()
    {
        GameCubeMemory memory = new();
        memory.Write16(0x8000_1000, 0xF800);
        memory.Write16(0x8000_1002, 0x07E0);
        memory.Write16(0x8000_1004, 0x001F);

        byte[] rgb = FramebufferDumper.CaptureRgb(memory, 0x8000_1000, width: 3, height: 1, FramebufferPixelFormat.Rgb565);

        Assert.Equal([255, 0, 0, 0, 255, 0, 0, 0, 255], rgb);
    }

    [Fact]
    public void DumpsPngUsingExplicitFramebufferAddress()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ngcsharp-frame-{Guid.NewGuid():N}.png");
        GameCubeBus bus = new();
        bus.Memory.Write16(0x8000_1000, 0xF800);

        try
        {
            RunDolOptions options = new(
                "game.dol",
                MaxInstructions: 0,
                Trace: false,
                TracePath: null,
                DumpRegisters: false,
                DumpMmio: false,
                Quiet: true,
                FrameDumpPath: path,
                FrameAddress: 0x8000_1000,
                FrameWidth: 1,
                FrameHeight: 1,
                FrameFormat: FramebufferPixelFormat.Rgb565);

            bool dumped = FramebufferDumper.TryDump(bus, options, out FramebufferDumpResult? result, out string? error);

            Assert.True(dumped, error);
            Assert.NotNull(result);
            Assert.Equal(0x8000_1000u, result.Address);
            Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], File.ReadAllBytes(path).Take(8).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DetectsShiftedFramebufferAddressFromVideoInterfaceRegister()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ngcsharp-frame-{Guid.NewGuid():N}.png");
        GameCubeBus bus = new();
        bus.Write32(0xCC00_201C, 0x80);
        bus.Memory.Write16(0x0000_1000, 0x07E0);

        try
        {
            RunDolOptions options = new(
                "game.dol",
                MaxInstructions: 0,
                Trace: false,
                TracePath: null,
                DumpRegisters: false,
                DumpMmio: false,
                Quiet: true,
                FrameDumpPath: path,
                FrameWidth: 1,
                FrameHeight: 1,
                FrameFormat: FramebufferPixelFormat.Rgb565);

            bool dumped = FramebufferDumper.TryDump(bus, options, out FramebufferDumpResult? result, out string? error);

            Assert.True(dumped, error);
            Assert.NotNull(result);
            Assert.Equal(0x0000_1000u, result.Address);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DetectsSplitFramebufferAddressFromVideoInterfaceRegisterPair()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ngcsharp-frame-{Guid.NewGuid():N}.png");
        GameCubeBus bus = new();
        bus.Write16(0xCC00_201C, 0x1002);
        bus.Write16(0xCC00_201E, 0);
        bus.Memory.Write16(0x0002_0000, 0x001F);

        try
        {
            RunDolOptions options = new(
                "game.dol",
                MaxInstructions: 0,
                Trace: false,
                TracePath: null,
                DumpRegisters: false,
                DumpMmio: false,
                Quiet: true,
                FrameDumpPath: path,
                FrameWidth: 1,
                FrameHeight: 1,
                FrameFormat: FramebufferPixelFormat.Rgb565);

            bool dumped = FramebufferDumper.TryDump(bus, options, out FramebufferDumpResult? result, out string? error);

            Assert.True(dumped, error);
            Assert.NotNull(result);
            Assert.Equal(0x0002_0000u, result.Address);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PrefersQueuedFramebufferRegisterPairOverDisplayedPair()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ngcsharp-frame-{Guid.NewGuid():N}.png");
        GameCubeBus bus = new();
        bus.Write16(0xCC00_201C, 0x1002);
        bus.Write16(0xCC00_201E, 0);
        bus.Write16(0xCC00_2020, 0x1009);
        bus.Write16(0xCC00_2022, 0xF000);
        bus.Memory.Write16(0x0009_F000, 0xF800);

        try
        {
            RunDolOptions options = new(
                "game.dol",
                MaxInstructions: 0,
                Trace: false,
                TracePath: null,
                DumpRegisters: false,
                DumpMmio: false,
                Quiet: true,
                FrameDumpPath: path,
                FrameWidth: 1,
                FrameHeight: 1,
                FrameFormat: FramebufferPixelFormat.Rgb565);

            bool dumped = FramebufferDumper.TryDump(bus, options, out FramebufferDumpResult? result, out string? error);

            Assert.True(dumped, error);
            Assert.NotNull(result);
            Assert.Equal(0x0009_F000u, result.Address);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
