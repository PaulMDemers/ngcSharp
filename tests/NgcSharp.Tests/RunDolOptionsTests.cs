using NgcSharp.App;

namespace NgcSharp.Tests;

public sealed class RunDolOptionsTests
{
    [Fact]
    public void ParsesTraceAndMmioOptions()
    {
        StringWriter error = new();

        bool parsed = RunDolOptions.TryParse(
            ["run-dol", "game.dol", "--max-instructions", "42", "--trace-file", "traces/game.trace", "--trace-tail", "8", "--dump-mmio", "--dump-threads", "--dump-message-queues", "--dump-memory", "0x80002000", "0x40", "--dump-memory", "0x803E7800", "0x20", "--dump-pointer-table", "0x80B0E4A0", "0x500", "0x18", "0x0C", "4", "--dump-frame", "frames/game.png", "--dump-gx-frame", "frames/gx.png", "--dump-gx-frame-sweep", "frames/sweep", "10", "25", "4", "--gx-frame-source", "last-display-copy", "--gx-frame-copy-index", "428", "--gx-frame-max-draws", "77", "--gx-frame-skip-draws", "11", "--gx-frame-max-raster-pixels", "12345", "--gx-frame-ignore-efb-copy-clear", "--dump-gx-draws", "frames/gx-draws.txt", "--dump-gx-copies", "frames/gx-copies.csv", "--dump-gx-coverage", "frames/gx-coverage.csv", "--dump-gx-tev-samples", "frames/gx-tev-samples.csv", "--dump-gx-textures", "frames/gx-textures", "--gx-draw-skip-draws", "600", "--gx-draw-max-draws", "42", "--trace-gx-fifo-writes", "frames/gx-writes.csv", "--gx-memory-checkpoint", "0x84EF0", "0x0060E4A0", "0x20000", "frames/checkpoints/draw790.bin", "--gx-disable-auto-texture-snapshots", "--trace-exi", "traces/exi.csv", "--trace-si", "traces/si.csv", "--trace-mmio", "traces/mmio.csv", "--trace-scheduler", "traces/scheduler.csv", "--memory-card-a", "--memory-card-b", "--frame-address", "0x80010000", "--frame-width", "320", "--frame-height", "240", "--frame-format", "rgb565", "--watch-address", "0x800000E4", "--watch-write-value", "0x20000", "--watch-write-range", "0x803E78A0", "0x40", "--watch-write-after", "0x5000", "--watch-load-range", "0x80225180", "0x100", "--stop-after-write-watch", "0x20", "--watch-call-target", "0x80012280", "--watch-call-target", "0x802A5D14", "--watch-call-range", "0x80012000", "0x1000", "--find-memory-word", "0x00020000", "--find-memory-word", "0x802A5D14", "--watch-gpr", "r31", "--watch-gpr-after", "0x300", "--fast-forward-idle", "--fast-forward-write-watch", "--trace-prs-decompress", "--controller-button", "start", "--controller-button", "b+x", "--controller-button-window", "a", "100", "200", "--watch-limit", "0x10", "--profile-pc", "12", "--profile-indirect-call-site", "0x801B1348", "8", "--stop-on-pc", "0x801F58D4", "--stop-on-pc-after", "0x1234", "--trace-pc", "0x800D2684", "--trace-pc", "0x800D2694", "--trace-pc-after", "0x4567", "--stop-on-gx-fifo-offset", "0x84EF0", "--stop-on-hot-pc", "0x100", "--stop-on-hot-pc-after", "0x200", "--no-registers"],
            out RunDolOptions options,
            error);

        Assert.True(parsed, error.ToString());
        Assert.Equal("game.dol", options.Path);
        Assert.Equal(42, options.MaxInstructions);
        Assert.True(options.Trace);
        Assert.Equal("traces/game.trace", options.TracePath);
        Assert.True(options.DumpMmio);
        Assert.True(options.DumpThreads);
        Assert.True(options.DumpMessageQueues);
        Assert.Equal("frames/game.png", options.FrameDumpPath);
        Assert.Equal("frames/gx.png", options.GxFrameDumpPath);
        Assert.Equal(77, options.GxFrameMaxDraws);
        Assert.Equal(11, options.GxFrameSkipDraws);
        Assert.Equal(12345, options.GxFrameMaxRasterPixels);
        Assert.Equal(GxFrameDumpSource.LastDisplayCopy, options.GxFrameSource);
        Assert.Equal(428, options.GxFrameCopyIndex);
        Assert.True(options.GxFrameIgnoreEfbCopyClear);
        Assert.Equal(new GxFrameSweepOptions("frames/sweep", 10, 25, 4), options.GxFrameSweep);
        Assert.Equal("frames/gx-draws.txt", options.GxDrawDumpPath);
        Assert.Equal("frames/gx-copies.csv", options.GxCopyDumpPath);
        Assert.Equal("frames/gx-coverage.csv", options.GxCoverageDumpPath);
        Assert.Equal("frames/gx-tev-samples.csv", options.GxTevSampleDumpPath);
        Assert.Equal("frames/gx-textures", options.GxTextureDumpPath);
        Assert.Equal(600, options.GxDrawSkipDraws);
        Assert.Equal(42, options.GxDrawMaxDraws);
        Assert.Equal("frames/gx-writes.csv", options.GxFifoWriteTracePath);
        GxMemoryCheckpointRequest checkpoint = Assert.Single(options.GxMemoryCheckpoints!);
        Assert.Equal(new GxMemoryCheckpointRequest(0x84EF0, 0x0060_E4A0u, 0x20000, "frames/checkpoints/draw790.bin"), checkpoint);
        Assert.True(options.GxDisableAutoTextureSnapshots);
        Assert.Equal("traces/exi.csv", options.ExiTracePath);
        Assert.Equal("traces/si.csv", options.SiTracePath);
        Assert.Equal("traces/mmio.csv", options.MmioTracePath);
        Assert.Equal("traces/scheduler.csv", options.SchedulerTracePath);
        Assert.True(options.MemoryCardSlotAInserted);
        Assert.True(options.MemoryCardSlotBInserted);
        Assert.Equal(0x8001_0000u, options.FrameAddress);
        Assert.Equal(320, options.FrameWidth);
        Assert.Equal(240, options.FrameHeight);
        Assert.Equal(FramebufferPixelFormat.Rgb565, options.FrameFormat);
        Assert.Equal(0x8000_00E4u, options.WatchAddress);
        Assert.Equal([0x8000_00E4u], options.WatchAddresses);
        Assert.Equal(8, options.TraceTail);
        Assert.Equal(0x803E_7800u, options.DumpMemoryAddress);
        Assert.Equal(32, options.DumpMemoryLength);
        Assert.Equal([new MemoryDumpRequest(0x8000_2000u, 64), new MemoryDumpRequest(0x803E_7800u, 32)], options.DumpMemoryRequests);
        Assert.Equal([new PointerTableDumpRequest(0x80B0_E4A0u, 0x500, 0x18, 0x0C, 4)], options.PointerTableDumpRequests);
        Assert.Equal(12, options.PcProfileTop);
        Assert.Equal(0x801B_1348u, options.IndirectCallSiteProfileAddress);
        Assert.Equal(8, options.IndirectCallSiteProfileTop);
        Assert.Equal(0x801F_58D4u, options.StopOnPc);
        Assert.Equal(0x1234, options.StopOnPcAfter);
        Assert.Equal([0x800D_2684u, 0x800D_2694u], options.TracePcAddresses);
        Assert.Equal(0x4567, options.TracePcAfter);
        Assert.Equal(0x84EF0, options.StopOnGxFifoOffset);
        Assert.Equal(16, options.WatchLimit);
        Assert.Equal(256ul, options.StopOnHotPc);
        Assert.Equal(512, options.StopOnHotPcAfter);
        Assert.Equal(0x0002_0000u, options.WatchWriteValue);
        Assert.Equal(0x803E_78A0u, options.WatchWriteRangeAddress);
        Assert.Equal(64, options.WatchWriteRangeLength);
        Assert.Equal(0x5000, options.WatchWriteAfter);
        Assert.Equal(0x8022_5180u, options.WatchLoadRangeAddress);
        Assert.Equal(0x100, options.WatchLoadRangeLength);
        Assert.Equal(32, options.StopAfterWriteWatch);
        Assert.Equal([0x8001_2280u, 0x802A_5D14u], options.WatchCallTargets);
        Assert.Equal(0x8001_2000u, options.WatchCallRangeAddress);
        Assert.Equal(0x1000, options.WatchCallRangeLength);
        Assert.Equal([0x0002_0000u, 0x802A_5D14u], options.FindMemoryWords);
        Assert.Equal(31, options.WatchGpr);
        Assert.Equal(0x300, options.WatchGprAfter);
        Assert.True(options.FastForwardIdle);
        Assert.True(options.FastForwardWriteWatch);
        Assert.True(options.TracePrsDecompress);
        Assert.Equal(0x1600, options.ControllerButtons);
        ControllerButtonWindow window = Assert.Single(options.ControllerButtonWindows!);
        Assert.Equal(0x0100, window.Buttons);
        Assert.Equal(100, window.StartInstruction);
        Assert.Equal(200, window.EndInstruction);
        Assert.False(options.DumpRegisters);
    }

    [Theory]
    [InlineData("last-nonblack-display-copy", GxFrameDumpSource.LastNonBlackDisplayCopy)]
    [InlineData("largest-display-copy", GxFrameDumpSource.LargestDisplayCopy)]
    [InlineData("last-nonblack-efb", GxFrameDumpSource.LastNonBlackEfb)]
    [InlineData("vi-framebuffer", GxFrameDumpSource.ViFramebuffer)]
    [InlineData("last-nonblack-vi-framebuffer", GxFrameDumpSource.LastNonBlackViFramebuffer)]
    [InlineData("copy-index", GxFrameDumpSource.CopyIndex)]
    [InlineData("copy-source-index", GxFrameDumpSource.CopySourceIndex)]
    public void ParsesAdditionalGxFrameSources(string source, GxFrameDumpSource expected)
    {
        bool parsed = RunDolOptions.TryParse(["run-dol", "game.dol", "--gx-frame-source", source], out RunDolOptions options, TextWriter.Null);

        Assert.True(parsed);
        Assert.Equal(expected, options.GxFrameSource);
    }

    [Fact]
    public void RejectsUnknownOptions()
    {
        StringWriter error = new();

        bool parsed = RunDolOptions.TryParse(["run-dol", "game.dol", "--mystery"], out _, error);

        Assert.False(parsed);
        Assert.Contains("--mystery", error.ToString());
    }

    [Fact]
    public void ParsesDiscDiagnosticOptions()
    {
        StringWriter error = new();

        bool parsed = DiscDiagnosticOptions.TryParse(
            ["diagnose-disc", "game.rvz", "--max-instructions", "1234", "--snapshot-interval", "250", "--probe-word", "waitFlag", "0x803ADCE0", "--out", "artifacts/diag", "--name", "sonic"],
            out DiscDiagnosticOptions options,
            error);

        Assert.True(parsed, error.ToString());
        Assert.Equal("game.rvz", options.Path);
        Assert.Equal(1234, options.MaxInstructions);
        Assert.Equal(250, options.SnapshotInterval);
        DiagnosticMemoryProbe probe = Assert.Single(options.ExtraMemoryProbes!);
        Assert.Equal("waitFlag", probe.Name);
        Assert.Equal(0x803A_DCE0u, probe.Address);
        Assert.Equal("artifacts/diag", options.OutputDirectory);
        Assert.Equal("sonic", options.Name);
    }
}
