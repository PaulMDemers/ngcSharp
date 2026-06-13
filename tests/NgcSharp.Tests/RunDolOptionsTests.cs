using NgcSharp.App;

namespace NgcSharp.Tests;

public sealed class RunDolOptionsTests
{
    [Fact]
    public void ParsesTraceAndMmioOptions()
    {
        StringWriter error = new();

        bool parsed = RunDolOptions.TryParse(
            ["run-dol", "game.dol", "--max-instructions", "42", "--trace-file", "traces/game.trace", "--trace-tail", "8", "--dump-mmio", "--dump-threads", "--dump-message-queues", "--dump-memory", "0x80002000", "0x40", "--dump-memory", "0x803E7800", "0x20", "--dump-memory-bin", "0x80FFFE60", "0x590C0", "traces/source-buffer.bin", "--dump-memory-bin-at", "0x123456", "0x8137DF9C", "0xB00", "traces/source-at-copy.bin", "--dump-disasm", "0x80BC82A0", "0x10", "--dump-pointer-table", "0x80B0E4A0", "0x500", "0x18", "0x0C", "4", "--dump-frame", "frames/game.png", "--dump-gx-frame", "frames/gx.png", "--dump-gx-frame-sweep", "frames/sweep", "10", "25", "4", "--gx-frame-source", "last-display-copy", "--gx-frame-copy-index", "428", "--gx-frame-max-draws", "77", "--gx-frame-skip-draws", "11", "--gx-frame-max-raster-pixels", "12345", "--gx-frame-ignore-efb-copy-clear", "--gx-frame-skip-copy-memory-writes", "--dump-gx-draws", "frames/gx-draws.txt", "--dump-gx-copies", "frames/gx-copies.csv", "--dump-gx-copy-events", "frames/gx-copy-events.csv", "--dump-gx-coverage", "frames/gx-coverage.csv", "--dump-gx-triangle-coverage", "frames/gx-triangle-coverage.csv", "--dump-gx-tev-samples", "frames/gx-tev-samples.csv", "--dump-gx-transforms", "frames/gx-transforms.csv", "--dump-gx-state-timeline", "frames/gx-state.csv", "--dump-gx-vertices", "frames/gx-vertices.csv", "--dump-gx-textures", "frames/gx-textures", "--gx-draw-skip-draws", "600", "--gx-draw-max-draws", "42", "--trace-gx-fifo-window", "frames/gx-writes.csv", "0x84E00", "0x200", "--gx-memory-checkpoint", "0x84EF0", "0x0060E4A0", "0x20000", "frames/checkpoints/draw790.bin", "--gx-disable-auto-texture-snapshots", "--trace-exi", "traces/exi.csv", "--trace-si", "traces/si.csv", "--trace-mmio", "traces/mmio.csv", "--trace-scheduler", "traces/scheduler.csv", "--run-summary", "traces/run-summary.json", "--memory-card-a", "--memory-card-b", "--frame-address", "0x80010000", "--frame-width", "320", "--frame-height", "240", "--frame-format", "rgb565", "--watch-address", "0x800000E4", "--watch-write-value", "0x20000", "--watch-write-range", "0x803E78A0", "0x40", "--watch-write-after", "0x5000", "--watch-load-range", "0x80225180", "0x100", "--stop-after-write-watch", "0x20", "--watch-call-target", "0x80012280", "--watch-call-target", "0x802A5D14", "--watch-call-range", "0x80012000", "0x1000", "--find-memory-word", "0x00020000", "--find-memory-word", "0x802A5D14", "--watch-gpr", "r31", "--watch-gpr-after", "0x300", "--fast-forward-idle", "--fast-forward-write-watch", "--disable-sonic-bit-unpack-fast-forward", "--disable-sonic-paired-transform-fast-forward", "--disable-sonic-gx-fast-forward", "--disable-sonic-geometry-fast-forward", "--disable-sonic-resource-fast-forward", "--disable-sonic-resource-lookup-fast-forward", "--disable-sonic-resource-mode-query-fast-forward", "--disable-sonic-resource-state-poll-fast-forward", "--disable-sonic-resource-fixup-fast-forward", "--trace-prs-decompress", "--trace-sonic-path-lookup", "traces/sonic-path.csv", "--trace-sonic-resource-flags", "traces/sonic-flags.csv", "--trace-sonic-matrix-stack", "traces/sonic-matrix-stack.csv", "--trace-sonic-matrix-writer", "traces/sonic-matrix-writer.csv", "--trace-sonic-root-matrix", "traces/sonic-root-matrix.csv", "--trace-sonic-scene-state", "traces/sonic-scene-state.csv", "--trace-sonic-packet-selection", "traces/sonic-packet-selection.csv", "--trace-sonic-traversal-source", "traces/sonic-traversal-source.csv", "--trace-sonic-draw-packets", "traces/sonic-draw-packets.csv", "--trace-sonic-gx-emitters", "traces/sonic-gx-emitters.csv", "--trace-sonic-texture-binds", "traces/sonic-texture-binds.csv", "--trace-sonic-vertex-provenance", "traces/sonic-vertex-provenance.csv", "0x2D1E42", "0x200", "--trace-sonic-transform-inputs", "traces/sonic-transform-inputs.csv", "--trace-sonic-transform-output-range", "0x80B286E0", "0x20", "--trace-sonic-bitstream-decoder", "traces/sonic-bitstream.csv", "0x813184D0", "0x100", "--trace-sonic-input-writes", "traces/sonic-input-writes.csv", "0x81317EB8", "0x20", "--trace-locked-cache-writes", "traces/locked-cache.csv", "0xE0000090", "0x40", "--di-command-latency-cycles", "0x4000", "--controller-button", "start", "--controller-button", "b+x", "--controller-button-window", "a", "100", "200", "--watch-limit", "0x10", "--profile-pc", "12", "--profile-after", "0x123456", "--profile-indirect-call-site", "0x801B1348", "8", "--profile-branch-site", "0x800E3128", "6", "--profile-pc-lr", "0x800E78AC", "5", "--stop-on-pc", "0x801F58D4", "--stop-on-pc-after", "0x1234", "--trace-pc", "0x800D2684", "--trace-pc", "0x800D2694", "--trace-pc-after", "0x4567", "--stop-on-gx-fifo-offset", "0x84EF0", "--stop-on-hot-pc", "0x100", "--stop-on-hot-pc-after", "0x200", "--no-registers"],
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
        Assert.True(options.GxFrameSkipCopyMemoryWrites);
        Assert.Equal(new GxFrameSweepOptions("frames/sweep", 10, 25, 4), options.GxFrameSweep);
        Assert.Equal("frames/gx-draws.txt", options.GxDrawDumpPath);
        Assert.Equal("frames/gx-copies.csv", options.GxCopyDumpPath);
        Assert.Equal("frames/gx-copy-events.csv", options.GxCopyEventDumpPath);
        Assert.Equal("frames/gx-coverage.csv", options.GxCoverageDumpPath);
        Assert.Equal("frames/gx-triangle-coverage.csv", options.GxTriangleCoverageDumpPath);
        Assert.Equal("frames/gx-tev-samples.csv", options.GxTevSampleDumpPath);
        Assert.Equal("frames/gx-transforms.csv", options.GxTransformDumpPath);
        Assert.Equal("frames/gx-state.csv", options.GxStateTimelineDumpPath);
        Assert.Equal("frames/gx-vertices.csv", options.GxVertexDumpPath);
        Assert.Equal("frames/gx-textures", options.GxTextureDumpPath);
        Assert.Equal("frames/gx-writes.csv", options.GxFifoWriteTracePath);
        Assert.Equal(0x84E00, options.GxFifoWriteTraceStart);
        Assert.Equal(0x200, options.GxFifoWriteTraceLength);
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
        Assert.Equal("traces/run-summary.json", options.RunSummaryPath);
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
        Assert.Equal(
            [
                new MemoryBinaryDumpRequest(0x80FF_FE60u, 0x590C0, "traces/source-buffer.bin"),
                new MemoryBinaryDumpRequest(0x8137_DF9Cu, 0xB00, "traces/source-at-copy.bin", 0x123456),
            ],
            options.DumpMemoryBinaryRequests);
        Assert.Equal([new DisassemblyDumpRequest(0x80BC_82A0u, 16)], options.DumpDisassemblyRequests);
        Assert.Equal([new PointerTableDumpRequest(0x80B0_E4A0u, 0x500, 0x18, 0x0C, 4)], options.PointerTableDumpRequests);
        Assert.Equal(12, options.PcProfileTop);
        Assert.Equal(0x123456, options.ProfileAfter);
        Assert.Equal(0x801B_1348u, options.IndirectCallSiteProfileAddress);
        Assert.Equal(8, options.IndirectCallSiteProfileTop);
        Assert.Equal([new BranchSiteProfileRequest(0x800E_3128u, 6)], options.BranchSiteProfiles);
        Assert.Equal([new PcLrProfileRequest(0x800E_78ACu, 5)], options.PcLrProfiles);
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
        Assert.True(options.DisableSonicBitUnpackFastForward);
        Assert.True(options.DisableSonicPairedTransformFastForward);
        Assert.True(options.DisableSonicGxFastForward);
        Assert.True(options.DisableSonicGeometryFastForward);
        Assert.True(options.DisableSonicResourceFastForward);
        Assert.True(options.DisableSonicResourceLookupFastForward);
        Assert.True(options.DisableSonicResourceModeQueryFastForward);
        Assert.True(options.DisableSonicResourceStatePollFastForward);
        Assert.True(options.DisableSonicResourceFixupFastForward);
        Assert.False(options.EnableSonicResourceLookupFastForward);
        Assert.False(options.EnableSonicResourceModeQueryFastForward);
        Assert.False(options.EnableSonicResourceStatePollFastForward);
        Assert.False(options.EnableSonicResourceFixupFastForward);
        Assert.True(options.TracePrsDecompress);
        Assert.Equal("traces/sonic-path.csv", options.SonicPathLookupTracePath);
        Assert.Equal("traces/sonic-flags.csv", options.SonicResourceFlagTracePath);
        Assert.Equal("traces/sonic-matrix-stack.csv", options.SonicMatrixStackTracePath);
        Assert.Equal("traces/sonic-matrix-writer.csv", options.SonicMatrixWriterTracePath);
        Assert.Equal("traces/sonic-root-matrix.csv", options.SonicRootMatrixTracePath);
        Assert.Equal("traces/sonic-scene-state.csv", options.SonicSceneStateTracePath);
        Assert.Equal("traces/sonic-packet-selection.csv", options.SonicPacketSelectionTracePath);
        Assert.Equal("traces/sonic-traversal-source.csv", options.SonicTraversalSourceTracePath);
        Assert.Equal("traces/sonic-draw-packets.csv", options.SonicDrawPacketTracePath);
        Assert.Equal("traces/sonic-gx-emitters.csv", options.SonicGxEmitterTracePath);
        Assert.Equal("traces/sonic-texture-binds.csv", options.SonicTextureBindTracePath);
        Assert.Equal("traces/sonic-vertex-provenance.csv", options.SonicVertexProvenanceTracePath);
        Assert.Equal(0x2D1E42, options.SonicVertexProvenanceTraceStart);
        Assert.Equal(0x200, options.SonicVertexProvenanceTraceLength);
        Assert.Equal("traces/sonic-transform-inputs.csv", options.SonicTransformInputTracePath);
        Assert.Equal(0x80B2_86E0u, options.SonicTransformOutputRangeAddress);
        Assert.Equal(0x20, options.SonicTransformOutputRangeLength);
        Assert.Equal("traces/sonic-bitstream.csv", options.SonicBitstreamDecoderTracePath);
        Assert.Equal(0x8131_84D0u, options.SonicBitstreamDecoderTraceAddress);
        Assert.Equal(0x100, options.SonicBitstreamDecoderTraceLength);
        Assert.Equal("traces/sonic-input-writes.csv", options.SonicInputWriteTracePath);
        Assert.Equal(0x8131_7EB8u, options.SonicInputWriteTraceAddress);
        Assert.Equal(0x20, options.SonicInputWriteTraceLength);
        Assert.Equal("traces/locked-cache.csv", options.LockedCacheWriteTracePath);
        Assert.Equal(0xE000_0090u, options.LockedCacheWriteTraceAddress);
        Assert.Equal(0x40, options.LockedCacheWriteTraceLength);
        Assert.Equal(0x4000ul, options.DiscCommandLatencyCycles);
        Assert.Equal(0x1600, options.ControllerButtons);
        ControllerButtonWindow window = Assert.Single(options.ControllerButtonWindows!);
        Assert.Equal(0x0100, window.Buttons);
        Assert.Equal(100, window.StartInstruction);
        Assert.Equal(200, window.EndInstruction);
        Assert.False(options.DumpRegisters);
    }

    [Fact]
    public void ParsesSonicResourceFixupFastForwardOptIn()
    {
        StringWriter error = new();

        bool parsed = RunDolOptions.TryParse(
            ["run-dol", "game.dol", "--enable-sonic-resource-fixup-fast-forward"],
            out RunDolOptions options,
            error);

        Assert.True(parsed, error.ToString());
        Assert.True(options.EnableSonicResourceFixupFastForward);
        Assert.False(options.DisableSonicResourceFixupFastForward);
    }

    [Fact]
    public void ParsesSonicResourceLookupFastForwardOptIn()
    {
        StringWriter error = new();

        bool parsed = RunDolOptions.TryParse(
            ["run-dol", "game.dol", "--enable-sonic-resource-lookup-fast-forward"],
            out RunDolOptions options,
            error);

        Assert.True(parsed, error.ToString());
        Assert.True(options.EnableSonicResourceLookupFastForward);
        Assert.False(options.DisableSonicResourceLookupFastForward);
    }

    [Fact]
    public void ParsesSonicResourceModeQueryFastForwardOptIn()
    {
        StringWriter error = new();

        bool parsed = RunDolOptions.TryParse(
            ["run-dol", "game.dol", "--enable-sonic-resource-mode-query-fast-forward"],
            out RunDolOptions options,
            error);

        Assert.True(parsed, error.ToString());
        Assert.True(options.EnableSonicResourceModeQueryFastForward);
        Assert.False(options.DisableSonicResourceModeQueryFastForward);
    }

    [Fact]
    public void ParsesSonicResourceStatePollFastForwardOptIn()
    {
        StringWriter error = new();

        bool parsed = RunDolOptions.TryParse(
            ["run-dol", "game.dol", "--enable-sonic-resource-state-poll-fast-forward"],
            out RunDolOptions options,
            error);

        Assert.True(parsed, error.ToString());
        Assert.True(options.EnableSonicResourceStatePollFastForward);
        Assert.False(options.DisableSonicResourceStatePollFastForward);
    }

    [Theory]
    [InlineData("auto", GxFrameDumpSource.Auto)]
    [InlineData("best", GxFrameDumpSource.Auto)]
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
