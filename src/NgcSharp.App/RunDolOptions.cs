namespace NgcSharp.App;

public enum GxFrameDumpSource
{
    Efb,
    Auto,
    LastDisplayCopy,
    LastNonBlackDisplayCopy,
    LargestDisplayCopy,
    LastNonBlackEfb,
    ViFramebuffer,
    LastNonBlackViFramebuffer,
    CopyIndex,
    CopySourceIndex,
}

public sealed record RunDolOptions(
    string Path,
    int MaxInstructions,
    bool Trace,
    string? TracePath,
    bool DumpRegisters,
    bool DumpMmio,
    bool Quiet,
    bool DumpThreads = false,
    string? FrameDumpPath = null,
    string? GxFrameDumpPath = null,
    string? GxDrawDumpPath = null,
    string? GxCopyDumpPath = null,
    string? GxCoverageDumpPath = null,
    string? GxTevSampleDumpPath = null,
    string? GxTextureDumpPath = null,
    string? GxFifoWriteTracePath = null,
    IReadOnlyList<GxMemoryCheckpointRequest>? GxMemoryCheckpoints = null,
    bool GxDisableAutoTextureSnapshots = false,
    string? ExiTracePath = null,
    string? SiTracePath = null,
    string? MmioTracePath = null,
    bool MemoryCardSlotAInserted = false,
    bool MemoryCardSlotBInserted = false,
    uint? FrameAddress = null,
    int? FrameWidth = null,
    int? FrameHeight = null,
    FramebufferPixelFormat FrameFormat = FramebufferPixelFormat.Yuyv,
    uint? WatchAddress = null,
    int? TraceTail = null,
    uint? DumpMemoryAddress = null,
    int? DumpMemoryLength = null,
    IReadOnlyList<MemoryDumpRequest>? DumpMemoryRequests = null,
    IReadOnlyList<PointerTableDumpRequest>? PointerTableDumpRequests = null,
    IReadOnlyList<DisassemblyDumpRequest>? DumpDisassemblyRequests = null,
    int? PcProfileTop = null,
    int? ProfileAfter = null,
    uint? IndirectCallSiteProfileAddress = null,
    int? IndirectCallSiteProfileTop = null,
    IReadOnlyList<BranchSiteProfileRequest>? BranchSiteProfiles = null,
    IReadOnlyList<PcLrProfileRequest>? PcLrProfiles = null,
    uint? StopOnPc = null,
    int? StopOnPcAfter = null,
    IReadOnlyList<uint>? TracePcAddresses = null,
    int? TracePcAfter = null,
    int? StopOnGxFifoOffset = null,
    IReadOnlyList<uint>? WatchAddresses = null,
    int? WatchLimit = null,
    ulong? StopOnHotPc = null,
    int? StopOnHotPcAfter = null,
    uint? WatchWriteValue = null,
    uint? WatchWriteRangeAddress = null,
    int? WatchWriteRangeLength = null,
    int? WatchWriteAfter = null,
    uint? WatchLoadRangeAddress = null,
    int? WatchLoadRangeLength = null,
    IReadOnlyList<uint>? WatchCallTargets = null,
    uint? WatchCallRangeAddress = null,
    int? WatchCallRangeLength = null,
    IReadOnlyList<uint>? FindMemoryWords = null,
    int? StopAfterWriteWatch = null,
    int? WatchGpr = null,
    int? WatchGprAfter = null,
    bool FastForwardIdle = false,
    bool FastForwardWriteWatch = false,
    ushort ControllerButtons = 0,
    IReadOnlyList<ControllerButtonWindow>? ControllerButtonWindows = null,
    bool DumpMessageQueues = false,
    int? GxFrameMaxDraws = null,
    int GxFrameSkipDraws = 0,
    int? GxFrameMaxRasterPixels = null,
    GxFrameSweepOptions? GxFrameSweep = null,
    GxFrameDumpSource GxFrameSource = GxFrameDumpSource.Efb,
    int? GxFrameCopyIndex = null,
    bool GxFrameIgnoreEfbCopyClear = false,
    int GxDrawSkipDraws = 0,
    int GxDrawMaxDraws = 10,
    bool TracePrsDecompress = false,
    string? SchedulerTracePath = null,
    string? RunSummaryPath = null,
    ulong? DiscCommandLatencyCycles = null,
    string? SonicPathLookupTracePath = null,
    string? SonicResourceFlagTracePath = null)
{
    public const int DefaultGxFrameMaxDraws = 500;
    public const int DefaultGxFrameMaxRasterPixels = 8_000_000;
    public const int DefaultGxDrawMaxDraws = 10;

    public static bool TryParse(string[] args, out RunDolOptions options, TextWriter error)
    {
        options = new RunDolOptions(string.Empty, 1000, Trace: false, TracePath: null, DumpRegisters: true, DumpMmio: false, Quiet: false);

        if (args.Length < 2)
        {
            error.WriteLine("Missing DOL path.");
            return false;
        }

        string path = args[1];
        int maxInstructions = 1000;
        bool trace = false;
        string? tracePath = null;
        bool dumpRegisters = true;
        bool dumpMmio = false;
        bool quiet = false;
        bool dumpThreads = false;
        string? frameDumpPath = null;
        string? gxFrameDumpPath = null;
        string? gxDrawDumpPath = null;
        string? gxCopyDumpPath = null;
        string? gxCoverageDumpPath = null;
        string? gxTevSampleDumpPath = null;
        string? gxFifoWriteTracePath = null;
        List<GxMemoryCheckpointRequest> gxMemoryCheckpoints = [];
        bool gxDisableAutoTextureSnapshots = false;
        string? exiTracePath = null;
        string? siTracePath = null;
        string? mmioTracePath = null;
        string? schedulerTracePath = null;
        string? runSummaryPath = null;
        bool memoryCardSlotAInserted = false;
        bool memoryCardSlotBInserted = false;
        uint? frameAddress = null;
        int? frameWidth = null;
        int? frameHeight = null;
        FramebufferPixelFormat frameFormat = FramebufferPixelFormat.Yuyv;
        uint? watchAddress = null;
        List<uint> watchAddresses = [];
        int? traceTail = null;
        uint? dumpMemoryAddress = null;
        int? dumpMemoryLength = null;
        List<MemoryDumpRequest> dumpMemoryRequests = [];
        List<PointerTableDumpRequest> pointerTableDumpRequests = [];
        List<DisassemblyDumpRequest> dumpDisassemblyRequests = [];
        int? pcProfileTop = null;
        int? profileAfter = null;
        uint? indirectCallSiteProfileAddress = null;
        int? indirectCallSiteProfileTop = null;
        List<BranchSiteProfileRequest> branchSiteProfiles = [];
        List<PcLrProfileRequest> pcLrProfiles = [];
        uint? stopOnPc = null;
        int? stopOnPcAfter = null;
        List<uint> tracePcAddresses = [];
        int? tracePcAfter = null;
        int? stopOnGxFifoOffset = null;
        int? watchLimit = null;
        ulong? stopOnHotPc = null;
        int? stopOnHotPcAfter = null;
        uint? watchWriteValue = null;
        uint? watchWriteRangeAddress = null;
        int? watchWriteRangeLength = null;
        int? watchWriteAfter = null;
        uint? watchLoadRangeAddress = null;
        int? watchLoadRangeLength = null;
        List<uint> watchCallTargets = [];
        uint? watchCallRangeAddress = null;
        int? watchCallRangeLength = null;
        List<uint> findMemoryWords = [];
        int? stopAfterWriteWatch = null;
        int? watchGpr = null;
        int? watchGprAfter = null;
        bool fastForwardIdle = false;
        bool fastForwardWriteWatch = false;
        ushort controllerButtons = 0;
        List<ControllerButtonWindow> controllerButtonWindows = [];
        bool dumpMessageQueues = false;
        int? gxFrameMaxDraws = null;
        int gxFrameSkipDraws = 0;
        int? gxFrameMaxRasterPixels = null;
        GxFrameSweepOptions? gxFrameSweep = null;
        GxFrameDumpSource gxFrameSource = GxFrameDumpSource.Efb;
        int? gxFrameCopyIndex = null;
        bool gxFrameIgnoreEfbCopyClear = false;
        int gxDrawSkipDraws = 0;
        int gxDrawMaxDraws = DefaultGxDrawMaxDraws;
        string? gxTextureDumpPath = null;
        bool tracePrsDecompress = false;
        ulong? discCommandLatencyCycles = null;
        string? sonicPathLookupTracePath = null;
        string? sonicResourceFlagTracePath = null;

        for (int index = 2; index < args.Length; index++)
        {
            string arg = args[index];

            switch (arg)
            {
                case "--trace":
                    trace = true;
                    break;
                case "--trace-file":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--trace-file requires a path.");
                        return false;
                    }

                    trace = true;
                    tracePath = args[++index];
                    break;
                case "--dump-registers":
                    dumpRegisters = true;
                    break;
                case "--no-registers":
                    dumpRegisters = false;
                    break;
                case "--dump-mmio":
                    dumpMmio = true;
                    break;
                case "--dump-threads":
                    dumpThreads = true;
                    break;
                case "--dump-message-queues":
                    dumpMessageQueues = true;
                    break;
                case "--dump-frame":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--dump-frame requires a path.");
                        return false;
                    }

                    frameDumpPath = args[++index];
                    break;
                case "--dump-gx-frame":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--dump-gx-frame requires a path.");
                        return false;
                    }

                    gxFrameDumpPath = args[++index];
                    break;
                case "--gx-frame-max-draws":
                    if (index + 1 >= args.Length || !TryParsePositiveInt32(args[++index], out int parsedGxFrameMaxDraws))
                    {
                        error.WriteLine("--gx-frame-max-draws requires a positive integer value.");
                        return false;
                    }

                    gxFrameMaxDraws = parsedGxFrameMaxDraws;
                    break;
                case "--gx-frame-skip-draws":
                    if (index + 1 >= args.Length || !TryParseNonNegativeInt32(args[++index], out int parsedGxFrameSkipDraws))
                    {
                        error.WriteLine("--gx-frame-skip-draws requires a non-negative integer value.");
                        return false;
                    }

                    gxFrameSkipDraws = parsedGxFrameSkipDraws;
                    break;
                case "--gx-frame-max-raster-pixels":
                    if (index + 1 >= args.Length || !TryParsePositiveInt32(args[++index], out int parsedGxFrameMaxRasterPixels))
                    {
                        error.WriteLine("--gx-frame-max-raster-pixels requires a positive integer value.");
                        return false;
                    }

                    gxFrameMaxRasterPixels = parsedGxFrameMaxRasterPixels;
                    break;
                case "--dump-gx-frame-sweep":
                    if (index + 4 >= args.Length
                        || !TryParseNonNegativeInt32(args[index + 2], out int parsedSweepStart)
                        || !TryParsePositiveInt32(args[index + 3], out int parsedSweepStep)
                        || !TryParsePositiveInt32(args[index + 4], out int parsedSweepCount))
                    {
                        error.WriteLine("--dump-gx-frame-sweep requires a directory, non-negative start skip, positive step, and positive count.");
                        return false;
                    }

                    gxFrameSweep = new GxFrameSweepOptions(args[index + 1], parsedSweepStart, parsedSweepStep, parsedSweepCount);
                    index += 4;
                    break;
                case "--gx-frame-ignore-efb-copy-clear":
                    gxFrameIgnoreEfbCopyClear = true;
                    break;
                case "--gx-frame-source":
                    if (index + 1 >= args.Length || !TryParseGxFrameSource(args[++index], out gxFrameSource))
                    {
                        error.WriteLine("--gx-frame-source must be one of: efb, auto, last-display-copy, last-nonblack-display-copy, largest-display-copy, last-nonblack-efb, vi-framebuffer, last-nonblack-vi-framebuffer, copy-index, copy-source-index.");
                        return false;
                    }

                    break;
                case "--gx-frame-copy-index":
                    if (index + 1 >= args.Length || !TryParsePositiveInt32(args[++index], out int parsedGxFrameCopyIndex))
                    {
                        error.WriteLine("--gx-frame-copy-index requires a positive integer value.");
                        return false;
                    }

                    gxFrameCopyIndex = parsedGxFrameCopyIndex;
                    break;
                case "--dump-gx-draws":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--dump-gx-draws requires a path.");
                        return false;
                    }

                    gxDrawDumpPath = args[++index];
                    break;
                case "--dump-gx-copies":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--dump-gx-copies requires a path.");
                        return false;
                    }

                    gxCopyDumpPath = args[++index];
                    break;
                case "--dump-gx-coverage":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--dump-gx-coverage requires a path.");
                        return false;
                    }

                    gxCoverageDumpPath = args[++index];
                    break;
                case "--dump-gx-tev-samples":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--dump-gx-tev-samples requires a path.");
                        return false;
                    }

                    gxTevSampleDumpPath = args[++index];
                    break;
                case "--dump-gx-textures":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--dump-gx-textures requires a directory.");
                        return false;
                    }

                    gxTextureDumpPath = args[++index];
                    break;
                case "--gx-draw-skip-draws":
                    if (index + 1 >= args.Length || !TryParseNonNegativeInt32(args[++index], out int parsedGxDrawSkipDraws))
                    {
                        error.WriteLine("--gx-draw-skip-draws requires a non-negative integer value.");
                        return false;
                    }

                    gxDrawSkipDraws = parsedGxDrawSkipDraws;
                    break;
                case "--gx-draw-max-draws":
                    if (index + 1 >= args.Length || !TryParsePositiveInt32(args[++index], out int parsedGxDrawMaxDraws))
                    {
                        error.WriteLine("--gx-draw-max-draws requires a positive integer value.");
                        return false;
                    }

                    gxDrawMaxDraws = parsedGxDrawMaxDraws;
                    break;
                case "--trace-gx-fifo-writes":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--trace-gx-fifo-writes requires a path.");
                        return false;
                    }

                    gxFifoWriteTracePath = args[++index];
                    break;
                case "--gx-memory-checkpoint":
                    if (index + 4 >= args.Length
                        || !TryParseNonNegativeInt32(args[++index], out int parsedCheckpointFifoOffset)
                        || !TryParseUInt32(args[++index], out uint parsedCheckpointAddress)
                        || !TryParsePositiveInt32(args[++index], out int parsedCheckpointLength))
                    {
                        error.WriteLine("--gx-memory-checkpoint requires a FIFO byte offset, memory address, byte length, and output path.");
                        return false;
                    }

                    gxMemoryCheckpoints.Add(new GxMemoryCheckpointRequest(parsedCheckpointFifoOffset, parsedCheckpointAddress, parsedCheckpointLength, args[++index]));
                    break;
                case "--gx-disable-auto-texture-snapshots":
                    gxDisableAutoTextureSnapshots = true;
                    break;
                case "--trace-exi":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--trace-exi requires a path.");
                        return false;
                    }

                    exiTracePath = args[++index];
                    break;
                case "--trace-si":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--trace-si requires a path.");
                        return false;
                    }

                    siTracePath = args[++index];
                    break;
                case "--trace-mmio":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--trace-mmio requires a path.");
                        return false;
                    }

                    mmioTracePath = args[++index];
                    break;
                case "--trace-scheduler":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--trace-scheduler requires a path.");
                        return false;
                    }

                    schedulerTracePath = args[++index];
                    break;
                case "--run-summary":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--run-summary requires a path.");
                        return false;
                    }

                    runSummaryPath = args[++index];
                    break;
                case "--memory-card-a":
                    memoryCardSlotAInserted = true;
                    break;
                case "--memory-card-b":
                    memoryCardSlotBInserted = true;
                    break;
                case "--frame-address":
                    if (index + 1 >= args.Length || !TryParseUInt32(args[++index], out uint parsedAddress))
                    {
                        error.WriteLine("--frame-address requires a decimal or 0x-prefixed address.");
                        return false;
                    }

                    frameAddress = parsedAddress;
                    break;
                case "--frame-width":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out int parsedWidth))
                    {
                        error.WriteLine("--frame-width requires an integer value.");
                        return false;
                    }

                    frameWidth = parsedWidth;
                    break;
                case "--frame-height":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out int parsedHeight))
                    {
                        error.WriteLine("--frame-height requires an integer value.");
                        return false;
                    }

                    frameHeight = parsedHeight;
                    break;
                case "--frame-format":
                    if (index + 1 >= args.Length || !FramebufferPixelFormatParser.TryParse(args[++index], out frameFormat))
                    {
                        error.WriteLine("--frame-format must be one of: rgb565, yuyv, uyvy, xrgb8888.");
                        return false;
                    }

                    break;
                case "--watch-address":
                    if (index + 1 >= args.Length || !TryParseUInt32(args[++index], out uint parsedWatchAddress))
                    {
                        error.WriteLine("--watch-address requires a decimal or 0x-prefixed address.");
                        return false;
                    }

                    watchAddress ??= parsedWatchAddress;
                    watchAddresses.Add(parsedWatchAddress);
                    break;
                case "--trace-tail":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out int parsedTraceTail))
                    {
                        error.WriteLine("--trace-tail requires an integer value.");
                        return false;
                    }

                    traceTail = parsedTraceTail;
                    break;
                case "--dump-memory":
                    if (index + 2 >= args.Length || !TryParseUInt32(args[++index], out uint parsedDumpMemoryAddress) || !TryParsePositiveInt32(args[++index], out int parsedDumpMemoryLength))
                    {
                        error.WriteLine("--dump-memory requires an address and byte length.");
                        return false;
                    }

                    dumpMemoryAddress = parsedDumpMemoryAddress;
                    dumpMemoryLength = parsedDumpMemoryLength;
                    dumpMemoryRequests.Add(new MemoryDumpRequest(parsedDumpMemoryAddress, parsedDumpMemoryLength));
                    break;
                case "--dump-pointer-table":
                    if (index + 5 >= args.Length
                        || !TryParseUInt32(args[++index], out uint parsedPointerTableAddress)
                        || !TryParsePositiveInt32(args[++index], out int parsedPointerTableCount)
                        || !TryParsePositiveInt32(args[++index], out int parsedPointerTableStride)
                        || !TryParseNonNegativeInt32(args[++index], out int parsedPointerTablePointerOffset)
                        || !TryParseNonNegativeInt32(args[++index], out int parsedPointerTableTargetWords))
                    {
                        error.WriteLine("--dump-pointer-table requires an address, positive count, positive stride, non-negative pointer offset, and non-negative target word count.");
                        return false;
                    }

                    if (parsedPointerTablePointerOffset > parsedPointerTableStride - sizeof(uint))
                    {
                        error.WriteLine("--dump-pointer-table pointer offset must fit within the entry stride.");
                        return false;
                    }

                    pointerTableDumpRequests.Add(new PointerTableDumpRequest(parsedPointerTableAddress, parsedPointerTableCount, parsedPointerTableStride, parsedPointerTablePointerOffset, parsedPointerTableTargetWords));
                    break;
                case "--dump-disasm":
                    if (index + 2 >= args.Length || !TryParseUInt32(args[++index], out uint parsedDisasmAddress) || !TryParsePositiveInt32(args[++index], out int parsedDisasmInstructionCount))
                    {
                        error.WriteLine("--dump-disasm requires an address and positive instruction count.");
                        return false;
                    }

                    dumpDisassemblyRequests.Add(new DisassemblyDumpRequest(parsedDisasmAddress, parsedDisasmInstructionCount));
                    break;
                case "--profile-pc":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out int parsedPcProfileTop))
                    {
                        error.WriteLine("--profile-pc requires an integer value.");
                        return false;
                    }

                    pcProfileTop = parsedPcProfileTop;
                    break;
                case "--profile-after":
                    if (index + 1 >= args.Length || !TryParseNonNegativeInt32(args[++index], out int parsedProfileAfter))
                    {
                        error.WriteLine("--profile-after requires a non-negative decimal or 0x-prefixed instruction count.");
                        return false;
                    }

                    profileAfter = parsedProfileAfter;
                    break;
                case "--profile-indirect-call-site":
                    if (index + 2 >= args.Length || !TryParseUInt32(args[++index], out uint parsedIndirectCallSite) || !TryParsePositiveInt32(args[++index], out int parsedIndirectCallSiteTop))
                    {
                        error.WriteLine("--profile-indirect-call-site requires a call-site address and positive top count.");
                        return false;
                    }

                    indirectCallSiteProfileAddress = parsedIndirectCallSite;
                    indirectCallSiteProfileTop = parsedIndirectCallSiteTop;
                    break;
                case "--profile-branch-site":
                    if (index + 2 >= args.Length || !TryParseUInt32(args[++index], out uint parsedBranchSite) || !TryParsePositiveInt32(args[++index], out int parsedBranchSiteTop))
                    {
                        error.WriteLine("--profile-branch-site requires a branch-site address and positive top count.");
                        return false;
                    }

                    branchSiteProfiles.Add(new BranchSiteProfileRequest(parsedBranchSite, parsedBranchSiteTop));
                    break;
                case "--profile-pc-lr":
                    if (index + 2 >= args.Length || !TryParseUInt32(args[++index], out uint parsedPcLrSite) || !TryParsePositiveInt32(args[++index], out int parsedPcLrTop))
                    {
                        error.WriteLine("--profile-pc-lr requires a PC address and positive top count.");
                        return false;
                    }

                    pcLrProfiles.Add(new PcLrProfileRequest(parsedPcLrSite, parsedPcLrTop));
                    break;
                case "--stop-on-pc":
                    if (index + 1 >= args.Length || !TryParseUInt32(args[++index], out uint parsedStopOnPc))
                    {
                        error.WriteLine("--stop-on-pc requires a decimal or 0x-prefixed address.");
                        return false;
                    }

                    stopOnPc = parsedStopOnPc;
                    break;
                case "--stop-on-pc-after":
                    if (index + 1 >= args.Length || !TryParseNonNegativeInt32(args[++index], out int parsedStopOnPcAfter))
                    {
                        error.WriteLine("--stop-on-pc-after requires a non-negative decimal or 0x-prefixed instruction count.");
                        return false;
                    }

                    stopOnPcAfter = parsedStopOnPcAfter;
                    break;
                case "--trace-pc":
                    if (index + 1 >= args.Length || !TryParseUInt32(args[++index], out uint parsedTracePc))
                    {
                        error.WriteLine("--trace-pc requires a decimal or 0x-prefixed address.");
                        return false;
                    }

                    tracePcAddresses.Add(parsedTracePc);
                    break;
                case "--trace-pc-after":
                    if (index + 1 >= args.Length || !TryParseNonNegativeInt32(args[++index], out int parsedTracePcAfter))
                    {
                        error.WriteLine("--trace-pc-after requires a non-negative decimal or 0x-prefixed instruction count.");
                        return false;
                    }

                    tracePcAfter = parsedTracePcAfter;
                    break;
                case "--stop-on-gx-fifo-offset":
                    if (index + 1 >= args.Length || !TryParseNonNegativeInt32(args[++index], out int parsedStopOnGxFifoOffset))
                    {
                        error.WriteLine("--stop-on-gx-fifo-offset requires a non-negative decimal or 0x-prefixed byte offset.");
                        return false;
                    }

                    stopOnGxFifoOffset = parsedStopOnGxFifoOffset;
                    break;
                case "--watch-limit":
                    if (index + 1 >= args.Length || !TryParsePositiveInt32(args[++index], out int parsedWatchLimit))
                    {
                        error.WriteLine("--watch-limit requires a positive decimal or 0x-prefixed integer value.");
                        return false;
                    }

                    watchLimit = parsedWatchLimit;
                    break;
                case "--watch-write-value":
                    if (index + 1 >= args.Length || !TryParseUInt32(args[++index], out uint parsedWatchWriteValue))
                    {
                        error.WriteLine("--watch-write-value requires a decimal or 0x-prefixed 32-bit value.");
                        return false;
                    }

                    watchWriteValue = parsedWatchWriteValue;
                    break;
                case "--watch-write-range":
                    if (index + 2 >= args.Length || !TryParseUInt32(args[++index], out uint parsedWatchWriteRangeAddress) || !TryParsePositiveInt32(args[++index], out int parsedWatchWriteRangeLength))
                    {
                        error.WriteLine("--watch-write-range requires an address and byte length.");
                        return false;
                    }

                    watchWriteRangeAddress = parsedWatchWriteRangeAddress;
                    watchWriteRangeLength = parsedWatchWriteRangeLength;
                    break;
                case "--watch-write-after":
                    if (index + 1 >= args.Length || !TryParseNonNegativeInt32(args[++index], out int parsedWatchWriteAfter))
                    {
                        error.WriteLine("--watch-write-after requires a non-negative decimal or 0x-prefixed instruction count.");
                        return false;
                    }

                    watchWriteAfter = parsedWatchWriteAfter;
                    break;
                case "--watch-load-range":
                    if (index + 2 >= args.Length || !TryParseUInt32(args[++index], out uint parsedWatchLoadRangeAddress) || !TryParsePositiveInt32(args[++index], out int parsedWatchLoadRangeLength))
                    {
                        error.WriteLine("--watch-load-range requires an address and byte length.");
                        return false;
                    }

                    watchLoadRangeAddress = parsedWatchLoadRangeAddress;
                    watchLoadRangeLength = parsedWatchLoadRangeLength;
                    break;
                case "--watch-call-target":
                    if (index + 1 >= args.Length || !TryParseUInt32(args[++index], out uint parsedWatchCallTarget))
                    {
                        error.WriteLine("--watch-call-target requires a decimal or 0x-prefixed address.");
                        return false;
                    }

                    watchCallTargets.Add(parsedWatchCallTarget);
                    break;
                case "--watch-call-range":
                    if (index + 2 >= args.Length || !TryParseUInt32(args[++index], out uint parsedWatchCallRangeAddress) || !TryParsePositiveInt32(args[++index], out int parsedWatchCallRangeLength))
                    {
                        error.WriteLine("--watch-call-range requires an address and byte length.");
                        return false;
                    }

                    watchCallRangeAddress = parsedWatchCallRangeAddress;
                    watchCallRangeLength = parsedWatchCallRangeLength;
                    break;
                case "--find-memory-word":
                    if (index + 1 >= args.Length || !TryParseUInt32(args[++index], out uint parsedFindMemoryWord))
                    {
                        error.WriteLine("--find-memory-word requires a decimal or 0x-prefixed 32-bit value.");
                        return false;
                    }

                    findMemoryWords.Add(parsedFindMemoryWord);
                    break;
                case "--stop-after-write-watch":
                    if (index + 1 >= args.Length || !TryParsePositiveInt32(args[++index], out int parsedStopAfterWriteWatch))
                    {
                        error.WriteLine("--stop-after-write-watch requires a positive decimal or 0x-prefixed integer value.");
                        return false;
                    }

                    stopAfterWriteWatch = parsedStopAfterWriteWatch;
                    break;
                case "--watch-gpr":
                    if (index + 1 >= args.Length || !TryParseGprIndex(args[++index], out int parsedWatchGpr))
                    {
                        error.WriteLine("--watch-gpr requires a register index 0-31 or name r0-r31.");
                        return false;
                    }

                    watchGpr = parsedWatchGpr;
                    break;
                case "--watch-gpr-after":
                    if (index + 1 >= args.Length || !TryParseNonNegativeInt32(args[++index], out int parsedWatchGprAfter))
                    {
                        error.WriteLine("--watch-gpr-after requires a non-negative decimal or 0x-prefixed instruction count.");
                        return false;
                    }

                    watchGprAfter = parsedWatchGprAfter;
                    break;
                case "--fast-forward-idle":
                    fastForwardIdle = true;
                    break;
                case "--fast-forward-write-watch":
                    fastForwardWriteWatch = true;
                    break;
                case "--trace-prs-decompress":
                    tracePrsDecompress = true;
                    break;
                case "--trace-sonic-path-lookup":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--trace-sonic-path-lookup requires a CSV path.");
                        return false;
                    }

                    sonicPathLookupTracePath = args[++index];
                    break;
                case "--trace-sonic-resource-flags":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--trace-sonic-resource-flags requires a CSV path.");
                        return false;
                    }

                    sonicResourceFlagTracePath = args[++index];
                    break;
                case "--di-command-latency-cycles":
                    if (index + 1 >= args.Length || !TryParseUInt64(args[++index], out ulong parsedDiscCommandLatencyCycles))
                    {
                        error.WriteLine("--di-command-latency-cycles requires a non-negative decimal or 0x-prefixed integer value.");
                        return false;
                    }

                    discCommandLatencyCycles = parsedDiscCommandLatencyCycles;
                    break;
                case "--controller-buttons":
                case "--controller-button":
                    if (index + 1 >= args.Length || !TryParseControllerButtons(args[++index], out ushort parsedControllerButtons))
                    {
                        error.WriteLine("--controller-button(s) requires a 16-bit mask or one or more names: left, right, down, up, z, r, l, a, b, x, y, start.");
                        return false;
                    }

                    controllerButtons |= parsedControllerButtons;
                    break;
                case "--controller-button-window":
                    if (index + 3 >= args.Length
                        || !TryParseControllerButtons(args[++index], out ushort parsedWindowButtons)
                        || !TryParseNonNegativeInt32(args[++index], out int parsedWindowStart)
                        || !TryParseNonNegativeInt32(args[++index], out int parsedWindowEnd)
                        || parsedWindowEnd < parsedWindowStart)
                    {
                        error.WriteLine("--controller-button-window requires buttons, start instruction, and end instruction.");
                        return false;
                    }

                    controllerButtonWindows.Add(new ControllerButtonWindow(parsedWindowButtons, parsedWindowStart, parsedWindowEnd));
                    break;
                case "--stop-on-hot-pc":
                    if (index + 1 >= args.Length || !TryParseUInt64(args[++index], out ulong parsedStopOnHotPc))
                    {
                        error.WriteLine("--stop-on-hot-pc requires a decimal or 0x-prefixed instruction count.");
                        return false;
                    }

                    stopOnHotPc = parsedStopOnHotPc;
                    break;
                case "--stop-on-hot-pc-after":
                    if (index + 1 >= args.Length || !TryParsePositiveInt32(args[++index], out int parsedStopOnHotPcAfter))
                    {
                        error.WriteLine("--stop-on-hot-pc-after requires a positive decimal or 0x-prefixed instruction count.");
                        return false;
                    }

                    stopOnHotPcAfter = parsedStopOnHotPcAfter;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                case "--max-instructions":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out maxInstructions))
                    {
                        error.WriteLine("--max-instructions requires an integer value.");
                        return false;
                    }

                    break;
                default:
                    if (int.TryParse(arg, out maxInstructions))
                    {
                        break;
                    }

                    error.WriteLine($"Unknown run-dol option '{arg}'.");
                    return false;
            }
        }

        if (maxInstructions < 0)
        {
            error.WriteLine("--max-instructions must be non-negative.");
            return false;
        }

        if (frameWidth is <= 0)
        {
            error.WriteLine("--frame-width must be positive.");
            return false;
        }

        if (frameHeight is <= 0)
        {
            error.WriteLine("--frame-height must be positive.");
            return false;
        }

        if (traceTail is <= 0)
        {
            error.WriteLine("--trace-tail must be positive.");
            return false;
        }

        if (pcProfileTop is <= 0)
        {
            error.WriteLine("--profile-pc count must be positive.");
            return false;
        }

        if (indirectCallSiteProfileTop is <= 0)
        {
            error.WriteLine("--profile-indirect-call-site count must be positive.");
            return false;
        }

        if (stopOnHotPc is 0)
        {
            error.WriteLine("--stop-on-hot-pc count must be positive.");
            return false;
        }

        options = new RunDolOptions(path, maxInstructions, trace, tracePath, dumpRegisters, dumpMmio, quiet, dumpThreads, frameDumpPath, gxFrameDumpPath, gxDrawDumpPath, gxCopyDumpPath, gxCoverageDumpPath, gxTevSampleDumpPath, gxTextureDumpPath, gxFifoWriteTracePath, gxMemoryCheckpoints, gxDisableAutoTextureSnapshots, exiTracePath, siTracePath, mmioTracePath, memoryCardSlotAInserted, memoryCardSlotBInserted, frameAddress, frameWidth, frameHeight, frameFormat, watchAddress, traceTail, dumpMemoryAddress, dumpMemoryLength, dumpMemoryRequests, pointerTableDumpRequests, dumpDisassemblyRequests, pcProfileTop, profileAfter, indirectCallSiteProfileAddress, indirectCallSiteProfileTop, branchSiteProfiles, pcLrProfiles, stopOnPc, stopOnPcAfter, tracePcAddresses, tracePcAfter, stopOnGxFifoOffset, watchAddresses, watchLimit, stopOnHotPc, stopOnHotPcAfter, watchWriteValue, watchWriteRangeAddress, watchWriteRangeLength, watchWriteAfter, watchLoadRangeAddress, watchLoadRangeLength, watchCallTargets, watchCallRangeAddress, watchCallRangeLength, findMemoryWords, stopAfterWriteWatch, watchGpr, watchGprAfter, fastForwardIdle, fastForwardWriteWatch, controllerButtons, controllerButtonWindows, dumpMessageQueues, gxFrameMaxDraws, gxFrameSkipDraws, gxFrameMaxRasterPixels, gxFrameSweep, gxFrameSource, gxFrameCopyIndex, gxFrameIgnoreEfbCopyClear, gxDrawSkipDraws, gxDrawMaxDraws, tracePrsDecompress, schedulerTracePath, runSummaryPath, discCommandLatencyCycles, sonicPathLookupTracePath, sonicResourceFlagTracePath);
        return true;
    }

    private static bool TryParseGxFrameSource(string text, out GxFrameDumpSource source)
    {
        switch (text.ToLowerInvariant())
        {
            case "efb":
                source = GxFrameDumpSource.Efb;
                return true;
            case "auto":
            case "best":
            case "best-frame":
                source = GxFrameDumpSource.Auto;
                return true;
            case "last-display-copy":
            case "display-copy":
            case "xfb":
                source = GxFrameDumpSource.LastDisplayCopy;
                return true;
            case "last-nonblack-display-copy":
            case "last-nonblack-xfb":
                source = GxFrameDumpSource.LastNonBlackDisplayCopy;
                return true;
            case "largest-display-copy":
            case "largest-xfb":
                source = GxFrameDumpSource.LargestDisplayCopy;
                return true;
            case "last-nonblack-efb":
            case "last-nonblack-efb-copy-source":
                source = GxFrameDumpSource.LastNonBlackEfb;
                return true;
            case "vi-framebuffer":
            case "vi-xfb":
                source = GxFrameDumpSource.ViFramebuffer;
                return true;
            case "last-nonblack-vi-framebuffer":
            case "last-nonblack-vi-xfb":
                source = GxFrameDumpSource.LastNonBlackViFramebuffer;
                return true;
            case "copy-index":
            case "display-copy-index":
                source = GxFrameDumpSource.CopyIndex;
                return true;
            case "copy-source-index":
            case "display-copy-source-index":
                source = GxFrameDumpSource.CopySourceIndex;
                return true;
            default:
                source = default;
                return false;
        }
    }

    private static bool TryParseGprIndex(string text, out int index)
    {
        if (text.StartsWith("r", StringComparison.OrdinalIgnoreCase))
        {
            text = text[1..];
        }

        return int.TryParse(text, out index) && index is >= 0 and <= 31;
    }

    private static bool TryParseControllerButtons(string text, out ushort buttons)
    {
        buttons = 0;
        if (TryParseUInt32(text, out uint mask))
        {
            if (mask > ushort.MaxValue)
            {
                return false;
            }

            buttons = (ushort)mask;
            return true;
        }

        ushort parsed = 0;
        string[] names = text.Split([',', '+', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0)
        {
            return false;
        }

        foreach (string name in names)
        {
            if (!TryGetControllerButton(name, out ushort button))
            {
                return false;
            }

            parsed |= button;
        }

        buttons = parsed;
        return true;
    }

    private static bool TryGetControllerButton(string name, out ushort button)
    {
        button = name.ToLowerInvariant() switch
        {
            "left" => 0x0001,
            "right" => 0x0002,
            "down" => 0x0004,
            "up" => 0x0008,
            "z" => 0x0010,
            "r" => 0x0020,
            "l" => 0x0040,
            "a" => 0x0100,
            "b" => 0x0200,
            "x" => 0x0400,
            "y" => 0x0800,
            "start" or "menu" or "pause" => 0x1000,
            "none" => 0x0000,
            _ => 0xFFFF,
        };

        return button != 0xFFFF;
    }

    private static bool TryParseUInt32(string text, out uint value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, provider: null, out value);
        }

        return uint.TryParse(text, out value);
    }

    private static bool TryParseUInt64(string text, out ulong value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, provider: null, out value);
        }

        return ulong.TryParse(text, out value);
    }

    private static bool TryParsePositiveInt32(string text, out int value)
    {
        value = 0;
        if (!TryParseUInt64(text, out ulong parsed) || parsed == 0 || parsed > int.MaxValue)
        {
            return false;
        }

        value = (int)parsed;
        return true;
    }

    private static bool TryParseNonNegativeInt32(string text, out int value)
    {
        value = 0;
        if (!TryParseUInt64(text, out ulong parsed) || parsed > int.MaxValue)
        {
            return false;
        }

        value = (int)parsed;
        return true;
    }
}
