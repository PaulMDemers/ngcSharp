using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NgcSharp.Core;
using NgcSharp.Cpu;

namespace NgcSharp.App;

public sealed class DolRunner
{
    private const uint MaxFastForwardMemmoveBytes = 16 * 1024 * 1024;
    private const uint MaxFastForwardStringCopyBytes = 1 * 1024 * 1024;
    private const uint MaxFastForwardStringCompareBytes = 1 * 1024 * 1024;
    private const uint MaxFastForwardStringLengthBytes = 1 * 1024 * 1024;
    private const uint MaxFastForwardPrsOutputBytes = 16 * 1024 * 1024;
    private const uint MaxFastForwardTrigTableEntries = 0x0001_0000;
    private const uint SonicTrigTableInstructionsPerEntry = 180;
    private const uint SonicBitUnpackInstructionsPerRow = 400;
    private const uint SonicGxVertexEmitInstructionsPerVertex = 33;
    private const uint SonicGxTexObjLoadNoCallbackPc = 0x8010_37A4;
    private const uint SonicGxTexObjLoadNoCallbackInstructions = 92;
    private const uint SonicGxPackedStateSetterPc = 0x8010_4FF8;
    private const uint SonicPathLookupEntryPc = 0x800E_ECFC;
    private const uint SonicPathLookupByteTablePc = 0x8010_BA44;
    private const int SonicPathLookupStackWindowBytes = 0xA0;
    private const uint SonicPathLookupRecordScanPc = 0x800E_EEC4;
    private const uint SonicPairedTransform2dLoopPc = 0x8011_DAA8;
    private const uint SonicPairedTransform2dInstructionsPerIteration = 14;
    private const uint SonicPairedTransform2dExitInstructions = 4;
    private const uint SonicGxFloatStripEmitLoopPc = 0x8011_D610;
    private const uint SonicGxFloatStripEmitInstructionsPerIteration = 26;
    private const uint SonicGxFloatStripEmitExitInstructions = 2;
    private const uint SonicGxFloatAttributeStripEmitLoopPc = 0x8011_FF70;
    private const uint SonicGxFloatAttributeStripEmitInstructionsPerIteration = 22;
    private const uint SonicGxFloatAttributeStripEmitExitInstructions = 2;
    private const uint SonicGxCommandListTerminalPc = 0x8011_D184;
    private const uint SonicGxCommandListFetchInstructions = 4;
    private const uint SonicGxCommandListTerminalInstructions = 23;
    private const uint SonicGxCommandDispatchHeaderPc = 0x8011_CD40;
    private const uint SonicGxCommandDispatchHighRangePc = 0x8011_CDE8;
    private const uint SonicGxCommandDispatchExtendedRangePc = 0x8011_CE20;
    private const uint SonicGxCommandMetadataHeaderPc = 0x8011_CE60;
    private const uint SonicGxCommandActiveBatchRecordPc = 0x8011_CE80;
    private const uint SonicGprSaveTailPc = 0x8010_AFCC;
    private const uint SonicGprRestoreTailPc = 0x8010_B018;
    private const uint SonicGxAttributeStateSetterPc = 0x8010_0D44;
    private const uint SonicGxDrawBeginPc = 0x8010_1948;
    private const uint SonicGxDrawBeginFastForwardInstructions = 28;
    private const uint SonicGxVertexDescriptorSetterPc = 0x8010_0830;
    private const uint SonicGxVertexAttributeFlushPc = 0x8010_3D28;
    private const uint SonicGxVertexAttributeHelperPc = 0x8010_3C5C;
    private const uint SonicGxIndexedStripDrawBeginPc = 0x8012_0078;
    private const uint SonicGxIndexedStripTailPc = 0x8012_00FC;
    private const uint SonicGxIndexedStripEpiloguePc = 0x8012_010C;
    private const uint SonicGxIndexedStripEpilogueInstructions = 15;
    private const uint SonicGxFloatTexcoordStripEmitLoopPc = 0x8011_D860;
    private const uint SonicGxFloatTexcoordStripEmitInstructionsPerIteration = 36;
    private const uint SonicGxFloatTexcoordStripEmitExitInstructions = 2;
    private const uint SonicPairedTransform4dLoopPc = 0x8011_DB94;
    private const uint SonicPairedTransform4dInstructionsPerIteration = 20;
    private const uint SonicPairedTransform4dExitInstructions = 11;
    private const uint SonicVectorBlendCopyLoopPc = 0x8012_0D98;
    private const uint SonicVectorBlendCopyInstructionsPerIteration = 47;
    private const uint SonicGeneratedModelPointerScanPc = 0x80BC_B1EC;
    private const uint SonicGeneratedModelPointerScanInstructions = 214;
    private const uint SonicGeneratedRangeScanLoopPc = 0x80BC_BFBC;
    private const uint SonicGeneratedTileRangeScanLoopPc = 0x80BC_C0A4;
    private const uint ExternalInterruptVector = 0x8000_0500;

    private static readonly uint[] VideoInterruptRegisters =
    [
        0xCC00_2030,
        0xCC00_2034,
        0xCC00_2038,
        0xCC00_203C,
    ];

    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public DolRunner(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
    }

    public int Run(RunDolOptions options)
    {
        DolFile dol = DolFile.Load(options.Path);
        return Run(dol, options, new GameCubeBus(), stepObserver: null, prepareStandaloneBoot: true);
    }

    public int Run(DolFile dol, RunDolOptions options)
    {
        return Run(dol, options, new GameCubeBus(), stepObserver: null, prepareStandaloneBoot: true);
    }

    public int Run(DolFile dol, RunDolOptions options, GameCubeBus bus)
    {
        return Run(dol, options, bus, stepObserver: null);
    }

    public int Run(DolFile dol, RunDolOptions options, GameCubeBus bus, Action<DolRunStep>? stepObserver)
    {
        return Run(dol, options, bus, stepObserver, prepareStandaloneBoot: false);
    }

    private int Run(DolFile dol, RunDolOptions options, GameCubeBus bus, Action<DolRunStep>? stepObserver, bool prepareStandaloneBoot)
    {
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        dol.LoadInto(bus.Memory);
        if (options.DiscCommandLatencyCycles is ulong discCommandLatencyCycles)
        {
            bus.DiscInterfaceCommandLatencyOverrideCycles = discCommandLatencyCycles;
        }

        bus.ExternalInterfaceMemoryCardSlotAInserted = options.MemoryCardSlotAInserted;
        bus.ExternalInterfaceMemoryCardSlotBInserted = options.MemoryCardSlotBInserted;
        if (prepareStandaloneBoot)
        {
            GameCubeStandaloneBoot.PrepareMemory(bus.Memory);
        }

        UpdateControllerButtons(bus, options, executedInstructions: 0);

        PowerPcState state = new()
        {
            Pc = dol.EntryPoint,
        };

        PowerPcInterpreter interpreter = new();
        int executed = 0;
        using TextWriter? traceFile = OpenTraceFile(options.TracePath);
        TextWriter traceOutput = traceFile ?? _output;
        uint[] watchedAddresses = BuildWatchAddresses(options);
        HashSet<uint>? tracedPcSet = options.TracePcAddresses is { Count: > 0 } ? [.. options.TracePcAddresses] : null;
        uint[] watchedCallTargets = BuildWatchCallTargets(options);
        HashSet<uint>? watchedCallTargetSet = watchedCallTargets.Length > 0 ? [.. watchedCallTargets] : null;
        bool hasCallRangeWatch = options.WatchCallRangeAddress is not null && options.WatchCallRangeLength is not null;
        Dictionary<uint, uint> watchedValues = [];
        foreach (uint watchedAddress in watchedAddresses)
        {
            watchedValues[watchedAddress] = bus.Read32(watchedAddress);
        }

        int emittedWatchChanges = 0;
        int emittedCallWatchChanges = 0;
        int emittedLoadWatchChanges = 0;
        int emittedGprWatchChanges = 0;
        int emittedPcTraceChanges = 0;
        int emittedPrsTraceChanges = 0;
        int writeWatchMatches = 0;
        int loadWatchMatches = 0;
        int indirectCallWatchMatches = 0;
        int gprWatchChanges = 0;
        int memoryWatchChanges = 0;
        int pcTraceMatches = 0;
        bool watchLimitNoticeEmitted = false;
        bool callWatchLimitNoticeEmitted = false;
        bool loadWatchLimitNoticeEmitted = false;
        bool gprWatchLimitNoticeEmitted = false;
        bool pcTraceLimitNoticeEmitted = false;
        bool prsTraceLimitNoticeEmitted = false;
        uint watchedGprValue = options.WatchGpr is int watchGpr ? state.Gpr[watchGpr] : 0;
        int traceTailCapacity = options.TraceTail.GetValueOrDefault();
        Queue<string>? traceTail = options.TraceTail is not null ? new Queue<string>(traceTailCapacity) : null;
        Dictionary<uint, ulong>? pcProfile = options.PcProfileTop is not null || options.StopOnHotPc is not null ? [] : null;
        Dictionary<uint, ulong>? hotPcProfile = options.StopOnHotPc is not null ? [] : null;
        Dictionary<uint, ulong>? indirectCallSiteProfile = options.IndirectCallSiteProfileAddress is not null ? [] : null;
        Dictionary<uint, Dictionary<uint, ulong>>? branchSiteProfiles = BuildBranchSiteProfileDictionaries(options);
        Dictionary<uint, Dictionary<uint, ulong>>? pcLrProfiles = BuildPcLrProfileDictionaries(options);
        bool stoppedOnPc = false;
        uint? stoppedOnHotPc = null;
        ulong stoppedOnHotPcCount = 0;
        bool stoppedAfterWriteWatch = false;
        int stoppedAfterWriteWatchCount = 0;
        bool stoppedOnGxFifoOffset = false;
        long gxFifoBytesWritten = 0;
        GxMemoryCheckpointState[] gxMemoryCheckpoints = BuildGxMemoryCheckpointStates(options);
        int gxMemoryCheckpointsWritten = 0;
        GxLiveTextureSnapshotCollector? gxTextureSnapshotCollector = GxLiveTextureSnapshotCollector.Create(options);
        bool canFastForwardWithWriteWatch = !HasWriteWatch(options) || options.FastForwardWriteWatch;
        ulong idleFastForwardCycles = 0;
        ulong ctrDelayFastForwardInstructions = 0;
        ulong bulkFastForwardInstructions = 0;
        ulong reverseWordFillFastForwardInstructions = 0;
        ulong cacheFastForwardInstructions = 0;
        ulong leafFastForwardInstructions = 0;
        ulong timeBaseReadFastForwardInstructions = 0;
        ulong externalInterruptLeafFastForwardInstructions = 0;
        ulong memoryCopyFastForwardInstructions = 0;
        ulong textureSampleFastForwardInstructions = 0;
        ulong stringCopyFastForwardInstructions = 0;
        ulong stringCompareFastForwardInstructions = 0;
        ulong stringLengthFastForwardInstructions = 0;
        ulong prsDecompressFastForwardInstructions = 0;
        ulong trigTableFastForwardInstructions = 0;
        ulong bitUnpackFastForwardInstructions = 0;
        ulong tickWaitFastForwardInstructions = 0;
        ulong callbackWaitFastForwardInstructions = 0;
        ulong dotProductFastForwardInstructions = 0;
        ulong resourceLookupFastForwardInstructions = 0;
        ulong gxAttributeFlushFastForwardInstructions = 0;
        ulong bitPlaneCropFastForwardInstructions = 0;
        ulong byteTableLookupFastForwardInstructions = 0;
        ulong gxVertexEmitFastForwardInstructions = 0;
        ulong sonicGxTexObjLoadNoCallbackFastForwardInstructions = 0;
        ulong sonicGxPackedStateSetterFastForwardInstructions = 0;
        ulong normalizedStringScanFastForwardInstructions = 0;
        ulong sonicResourceModeQueryFastForwardInstructions = 0;
        ulong sonicResourceStatePollFastForwardInstructions = 0;
        ulong sonicModeWrapperFastForwardInstructions = 0;
        ulong sonicResourceFixupFastForwardInstructions = 0;
        ulong sonicOverlayInactiveSlotScanFastForwardInstructions = 0;
        ulong sonicPathRecordScanFastForwardInstructions = 0;
        ulong sonicPairedTransform2dFastForwardInstructions = 0;
        ulong sonicGxFloatStripEmitFastForwardInstructions = 0;
        ulong sonicGxFloatAttributeStripEmitFastForwardInstructions = 0;
        ulong sonicGxCommandListFetchFastForwardInstructions = 0;
        ulong sonicGxCommandListTerminalFastForwardInstructions = 0;
        ulong sonicGxCommandDispatchFastForwardInstructions = 0;
        ulong sonicGprSaveRestoreTailFastForwardInstructions = 0;
        ulong sonicGxAttributeStateSetterFastForwardInstructions = 0;
        ulong sonicGxDrawBeginFastForwardInstructions = 0;
        ulong sonicGxVertexDescriptorSetterFastForwardInstructions = 0;
        ulong sonicGxVertexAttributeFlushFastForwardInstructions = 0;
        ulong sonicGxIndexedStripBatchFastForwardInstructions = 0;
        ulong sonicGxIndexedStripDrawBeginFastForwardInstructions = 0;
        ulong sonicGxIndexedStripTailFastForwardInstructions = 0;
        ulong sonicGxIndexedStripEpilogueFastForwardInstructions = 0;
        ulong sonicGxFloatTexcoordStripEmitFastForwardInstructions = 0;
        ulong sonicPairedTransform4dFastForwardInstructions = 0;
        ulong sonicVectorBlendCopyFastForwardInstructions = 0;
        ulong sonicGeneratedModelPointerScanFastForwardInstructions = 0;
        ulong sonicGeneratedRangeScanFastForwardInstructions = 0;
        uint currentPc = state.Pc;
        uint currentInstruction = 0;
        Action<uint, uint>? previousWriteObserver = bus.MainRamWrite32Observer;
        Action<uint, int, uint>? previousWriteAnyObserver = bus.MainRamWriteObserver;
        Action<uint, int, uint>? previousMemoryStoreObserver = bus.Memory.MainRamStoreObserver;
        Action<uint, int>? previousBulkWriteObserver = bus.Memory.MainRamBulkWriteObserver;
        Action<MmioAccess>? previousMmioObserver = bus.MmioAccessObserver;
        using TextWriter? gxFifoWriteTrace = OpenTraceFile(options.GxFifoWriteTracePath);
        if (gxFifoWriteTrace is not null || options.StopOnGxFifoOffset is not null || gxMemoryCheckpoints.Length != 0 || gxTextureSnapshotCollector is not null)
        {
            gxFifoWriteTrace?.WriteLine("instruction,pc,opcode,disassembly,fifo_offset_start,fifo_offset_end,width,address,value");
            bus.MmioAccessObserver = access =>
            {
                previousMmioObserver?.Invoke(access);
                if (access.Kind != MmioAccessKind.Write || access.DeviceName != "GX FIFO")
                {
                    return;
                }

                long offsetStart = gxFifoBytesWritten;
                gxFifoBytesWritten += access.Width;
                gxTextureSnapshotCollector?.Feed(access, bus.Memory);
                gxFifoWriteTrace?.WriteLine($"{executed + 1},0x{currentPc:X8},0x{currentInstruction:X8},\"{PowerPcDisassembler.Disassemble(currentInstruction).Replace("\"", "\"\"", StringComparison.Ordinal)}\",0x{offsetStart:X},0x{gxFifoBytesWritten:X},{access.Width},0x{access.Address:X8},0x{access.Value:X8}");
                if (options.StopOnGxFifoOffset is int stopOnGxFifoOffset
                    && offsetStart <= stopOnGxFifoOffset
                    && gxFifoBytesWritten > stopOnGxFifoOffset)
                {
                    stoppedOnGxFifoOffset = true;
                }

                for (int checkpointIndex = 0; checkpointIndex < gxMemoryCheckpoints.Length; checkpointIndex++)
                {
                    GxMemoryCheckpointState checkpoint = gxMemoryCheckpoints[checkpointIndex];
                    if (checkpoint.Written
                        || offsetStart > checkpoint.Request.FifoOffset
                        || gxFifoBytesWritten <= checkpoint.Request.FifoOffset)
                    {
                        continue;
                    }

                    checkpoint.Written = true;
                    gxMemoryCheckpoints[checkpointIndex] = checkpoint;
                    if (TryWriteGxMemoryCheckpoint(bus.Memory, checkpoint.Request, out byte[]? checkpointBytes, out string? checkpointError))
                    {
                        checkpoint.Bytes = checkpointBytes;
                        gxMemoryCheckpoints[checkpointIndex] = checkpoint;
                        gxMemoryCheckpointsWritten++;
                        if (!options.Quiet)
                        {
                            _output.WriteLine($"Wrote GX memory checkpoint +0x{checkpoint.Request.FifoOffset:X} 0x{checkpoint.Request.Address:X8}+0x{checkpoint.Request.Length:X} to {Path.GetFullPath(checkpoint.Request.Path)}.");
                        }
                    }
                    else
                    {
                        _error.WriteLine($"GX memory checkpoint +0x{checkpoint.Request.FifoOffset:X} failed: {checkpointError}");
                    }
                }
            };
        }

        using TextWriter? siTrace = OpenTraceFile(options.SiTracePath);
        if (siTrace is not null)
        {
            siTrace.WriteLine("instruction,pc,opcode,disassembly,kind,width,address,value");
            Action<MmioAccess>? chainedMmioObserver = bus.MmioAccessObserver;
            bus.MmioAccessObserver = access =>
            {
                chainedMmioObserver?.Invoke(access);
                if (access.DeviceName != "SI")
                {
                    return;
                }

                siTrace.WriteLine($"{executed + 1},0x{currentPc:X8},0x{currentInstruction:X8},\"{PowerPcDisassembler.Disassemble(currentInstruction).Replace("\"", "\"\"", StringComparison.Ordinal)}\",{access.Kind},{access.Width},0x{access.Address:X8},0x{access.Value:X8}");
            };
        }

        using TextWriter? exiTrace = OpenTraceFile(options.ExiTracePath);
        if (exiTrace is not null)
        {
            exiTrace.WriteLine("instruction,pc,opcode,disassembly,kind,width,address,value");
            Action<MmioAccess>? chainedMmioObserver = bus.MmioAccessObserver;
            bus.MmioAccessObserver = access =>
            {
                chainedMmioObserver?.Invoke(access);
                if (access.DeviceName != "EXI")
                {
                    return;
                }

                exiTrace.WriteLine($"{executed + 1},0x{currentPc:X8},0x{currentInstruction:X8},\"{PowerPcDisassembler.Disassemble(currentInstruction).Replace("\"", "\"\"", StringComparison.Ordinal)}\",{access.Kind},{access.Width},0x{access.Address:X8},0x{access.Value:X8}");
            };
        }

        using TextWriter? mmioTrace = OpenTraceFile(options.MmioTracePath);
        if (mmioTrace is not null)
        {
            mmioTrace.WriteLine("instruction,pc,opcode,disassembly,device,kind,width,address,value");
            Action<MmioAccess>? chainedMmioObserver = bus.MmioAccessObserver;
            bus.MmioAccessObserver = access =>
            {
                chainedMmioObserver?.Invoke(access);
                mmioTrace.WriteLine($"{executed + 1},0x{currentPc:X8},0x{currentInstruction:X8},\"{PowerPcDisassembler.Disassemble(currentInstruction).Replace("\"", "\"\"", StringComparison.Ordinal)}\",{access.DeviceName},{access.Kind},{access.Width},0x{access.Address:X8},0x{access.Value:X8}");
            };
        }

        if (HasWriteWatch(options))
        {
            int emittedWriteWatchChanges = 0;
            bool writeWatchLimitNoticeEmitted = false;
            bus.Memory.MainRamStoreObserver = (address, width, value) =>
            {
                previousMemoryStoreObserver?.Invoke(address, width, value);
                if (executed + 1 < options.WatchWriteAfter.GetValueOrDefault())
                {
                    return;
                }

                if (!MatchesWriteWatch(options, address, width, value))
                {
                    return;
                }

                writeWatchMatches++;
                if (options.WatchLimit is null || emittedWriteWatchChanges < options.WatchLimit)
                {
                    _output.WriteLine($"Write watch 0x{address:X8}/{width} <= 0x{value:X8} after {executed + 1} instruction(s), 0x{currentPc:X8}: 0x{currentInstruction:X8} {PowerPcDisassembler.Disassemble(currentInstruction)} {FormatWatchRegisters(state, currentInstruction)}");
                    emittedWriteWatchChanges++;
                }
                else if (!writeWatchLimitNoticeEmitted)
                {
                    _output.WriteLine($"Write watch output limit of {options.WatchLimit} reached; suppressing further write watch changes.");
                    writeWatchLimitNoticeEmitted = true;
                }

                if (options.StopAfterWriteWatch is int stopAfterWriteWatch && writeWatchMatches >= stopAfterWriteWatch)
                {
                    stoppedAfterWriteWatch = true;
                    stoppedAfterWriteWatchCount = writeWatchMatches;
                }
            };
            bus.Memory.MainRamBulkWriteObserver = (address, length) =>
            {
                previousBulkWriteObserver?.Invoke(address, length);
                if (executed + 1 < options.WatchWriteAfter.GetValueOrDefault())
                {
                    return;
                }

                if (!MatchesWriteWatchRange(options, address, length))
                {
                    return;
                }

                writeWatchMatches++;
                uint word = bus.Memory.IsMainRamAddress(address, sizeof(uint)) ? bus.Memory.Read32(address) : 0;
                if (options.WatchLimit is null || emittedWriteWatchChanges < options.WatchLimit)
                {
                    _output.WriteLine($"Bulk write watch 0x{address:X8}+0x{length:X} first32=0x{word:X8} after {executed + 1} instruction(s), 0x{currentPc:X8}: 0x{currentInstruction:X8} {PowerPcDisassembler.Disassemble(currentInstruction)}");
                    emittedWriteWatchChanges++;
                }
                else if (!writeWatchLimitNoticeEmitted)
                {
                    _output.WriteLine($"Write watch output limit of {options.WatchLimit} reached; suppressing further write watch changes.");
                    writeWatchLimitNoticeEmitted = true;
                }

                if (options.StopAfterWriteWatch is int stopAfterWriteWatch && writeWatchMatches >= stopAfterWriteWatch)
                {
                    stoppedAfterWriteWatch = true;
                    stoppedAfterWriteWatchCount = writeWatchMatches;
                }
            };
        }

        using TextWriter? schedulerTrace = OpenTraceFile(options.SchedulerTracePath);
        if (schedulerTrace is not null)
        {
            SchedulerTraceRecorder schedulerTraceRecorder = new(schedulerTrace, bus);
            Action<uint, int, uint>? chainedStoreObserver = bus.Memory.MainRamStoreObserver;
            Action<uint, int>? chainedBulkWriteObserver = bus.Memory.MainRamBulkWriteObserver;
            bus.Memory.MainRamStoreObserver = (address, width, value) =>
            {
                chainedStoreObserver?.Invoke(address, width, value);
                schedulerTraceRecorder.RecordStore(executed + 1, currentPc, currentInstruction, state, address, width, value);
            };
            bus.Memory.MainRamBulkWriteObserver = (address, length) =>
            {
                chainedBulkWriteObserver?.Invoke(address, length);
                schedulerTraceRecorder.RecordBulkWrite(executed + 1, currentPc, currentInstruction, address, length);
            };
        }

        using TextWriter? sonicPathLookupTrace = OpenTraceFile(options.SonicPathLookupTracePath);
        SonicPathLookupPending? sonicPathLookupPending = null;
        int sonicPathLookupCalls = 0;
        int sonicPathLookupMatches = 0;
        int sonicPathLookupMismatches = 0;
        int sonicPathLookupModelFailures = 0;
        long sonicPathLookupActualInstructions = 0;
        int sonicPathLookupMinActualInstructions = int.MaxValue;
        int sonicPathLookupMaxActualInstructions = 0;
        ulong sonicPathLookupElapsedCycles = 0;
        ulong sonicPathLookupMinElapsedCycles = ulong.MaxValue;
        ulong sonicPathLookupMaxElapsedCycles = 0;
        int sonicPathLookupInterruptEntries = 0;
        ulong sonicPathLookupPredictedCycles = 0;
        ulong sonicPathLookupMaxCycleErrorMagnitude = 0;
        int sonicPathLookupEligibleFastForwards = 0;
        if (sonicPathLookupTrace is not null)
        {
            sonicPathLookupTrace.WriteLine("instruction,entry_pc,lr,path,predicted_status,predicted_result,actual_result,match,reason,path_text,actual_instruction_count,elapsed_cycles,decrementer_delta,predicted_cycles,cycle_delta,candidate_entries,segment_comparisons,compare_bytes,fast_forward_eligible,interrupt_entries,entry_r0,entry_r4,entry_r5,entry_r6,entry_cr,actual_r0,actual_r4,actual_r5,actual_r6,actual_cr,actual_lr,actual_ctr,actual_xer,entry_gprs,actual_gprs,stack_base,entry_stack,actual_stack,stack_changed");
        }

        string GetStopReason(string? overrideReason = null)
        {
            if (overrideReason is not null)
            {
                return overrideReason;
            }

            if (state.Halted)
            {
                return "halted";
            }

            if (stoppedOnPc)
            {
                return "pc";
            }

            if (stoppedOnHotPc is not null)
            {
                return "hot-pc";
            }

            if (stoppedAfterWriteWatch)
            {
                return "write-watch";
            }

            if (stoppedOnGxFifoOffset)
            {
                return "gx-fifo-offset";
            }

            return executed >= options.MaxInstructions ? "max-instructions" : "completed";
        }

        GxFifoSoftwareRenderResult? gxFrameDump = null;
        Stopwatch emulationStopwatch = new();
        double memoryFindMilliseconds = 0;
        double pointerTableDumpMilliseconds = 0;
        double gxMemorySnapshotMilliseconds = 0;
        double gxFrameDumpMilliseconds = 0;
        double gxFrameSweepMilliseconds = 0;
        double frameDumpMilliseconds = 0;
        double gxDrawDumpMilliseconds = 0;
        double gxCopyDumpMilliseconds = 0;
        double gxCoverageDumpMilliseconds = 0;
        double gxTevSampleDumpMilliseconds = 0;
        double gxTextureDumpMilliseconds = 0;
        double registerDumpMilliseconds = 0;
        double mmioDumpMilliseconds = 0;
        double threadDumpMilliseconds = 0;
        double messageQueueDumpMilliseconds = 0;
        double pcProfileMilliseconds = 0;
        double indirectCallProfileMilliseconds = 0;
        double memoryDumpMilliseconds = 0;

        void StopEmulationTimer()
        {
            if (emulationStopwatch.IsRunning)
            {
                emulationStopwatch.Stop();
            }
        }

        void WriteRunSummary(int exitCode, string? stopReasonOverride = null, string? diagnosticFailure = null, string? exceptionType = null, uint? exceptionAddress = null, uint? exceptionInstruction = null)
        {
            StopEmulationTimer();
            if (options.RunSummaryPath is null)
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(options.RunSummaryPath);
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                uint summaryInstruction = exceptionInstruction ?? currentInstruction;
                double totalMilliseconds = totalStopwatch.Elapsed.TotalMilliseconds;
                double emulationMilliseconds = emulationStopwatch.Elapsed.TotalMilliseconds;
                double measuredDiagnosticMilliseconds =
                    memoryFindMilliseconds
                    + pointerTableDumpMilliseconds
                    + gxMemorySnapshotMilliseconds
                    + gxFrameDumpMilliseconds
                    + gxFrameSweepMilliseconds
                    + frameDumpMilliseconds
                    + gxDrawDumpMilliseconds
                    + gxCopyDumpMilliseconds
                    + gxCoverageDumpMilliseconds
                    + gxTevSampleDumpMilliseconds
                    + gxTextureDumpMilliseconds
                    + registerDumpMilliseconds
                    + mmioDumpMilliseconds
                    + threadDumpMilliseconds
                    + messageQueueDumpMilliseconds
                    + pcProfileMilliseconds
                    + indirectCallProfileMilliseconds
                    + memoryDumpMilliseconds;
                object summary = new
                {
                    schema = "ngcsharp.run-summary.v1",
                    path = Path.GetFullPath(options.Path),
                    maxInstructions = options.MaxInstructions,
                    executedInstructions = executed,
                    exitCode,
                    stopReason = GetStopReason(stopReasonOverride),
                    diagnosticFailure,
                    exception = exceptionType is null ? null : new
                    {
                        type = exceptionType,
                        address = exceptionAddress is uint address ? $"0x{address:X8}" : null,
                        instruction = exceptionInstruction is uint instruction ? $"0x{instruction:X8}" : null,
                        disassembly = exceptionInstruction is uint exceptionOpcode ? PowerPcDisassembler.Disassemble(exceptionOpcode) : null,
                    },
                    pc = $"0x{state.Pc:X8}",
                    lastPc = $"0x{currentPc:X8}",
                    lastInstruction = $"0x{summaryInstruction:X8}",
                    lastDisassembly = summaryInstruction == 0 ? null : PowerPcDisassembler.Disassemble(summaryInstruction),
                    timings = new
                    {
                        totalMs = RoundMilliseconds(totalMilliseconds),
                        emulationMs = RoundMilliseconds(emulationMilliseconds),
                        postEmulationMs = RoundMilliseconds(Math.Max(0, totalMilliseconds - emulationMilliseconds)),
                        measuredDiagnosticsMs = RoundMilliseconds(measuredDiagnosticMilliseconds),
                        memoryFindMs = RoundMilliseconds(memoryFindMilliseconds),
                        pointerTableDumpMs = RoundMilliseconds(pointerTableDumpMilliseconds),
                        gxMemorySnapshotMs = RoundMilliseconds(gxMemorySnapshotMilliseconds),
                        gxFrameDumpMs = RoundMilliseconds(gxFrameDumpMilliseconds),
                        gxFrameSweepMs = RoundMilliseconds(gxFrameSweepMilliseconds),
                        frameDumpMs = RoundMilliseconds(frameDumpMilliseconds),
                        gxDrawDumpMs = RoundMilliseconds(gxDrawDumpMilliseconds),
                        gxCopyDumpMs = RoundMilliseconds(gxCopyDumpMilliseconds),
                        gxCoverageDumpMs = RoundMilliseconds(gxCoverageDumpMilliseconds),
                        gxTevSampleDumpMs = RoundMilliseconds(gxTevSampleDumpMilliseconds),
                        gxTextureDumpMs = RoundMilliseconds(gxTextureDumpMilliseconds),
                        registerDumpMs = RoundMilliseconds(registerDumpMilliseconds),
                        mmioDumpMs = RoundMilliseconds(mmioDumpMilliseconds),
                        threadDumpMs = RoundMilliseconds(threadDumpMilliseconds),
                        messageQueueDumpMs = RoundMilliseconds(messageQueueDumpMilliseconds),
                        pcProfileMs = RoundMilliseconds(pcProfileMilliseconds),
                        indirectCallProfileMs = RoundMilliseconds(indirectCallProfileMilliseconds),
                        memoryDumpMs = RoundMilliseconds(memoryDumpMilliseconds),
                    },
                    registers = BuildRegisterSummary(state),
                    stopped = new
                    {
                        onPc = stoppedOnPc,
                        onHotPc = stoppedOnHotPc is uint hotPc ? $"0x{hotPc:X8}" : null,
                        onHotPcCount = stoppedOnHotPcCount,
                        afterWriteWatch = stoppedAfterWriteWatch,
                        afterWriteWatchCount = stoppedAfterWriteWatchCount,
                        onGxFifoOffset = stoppedOnGxFifoOffset,
                        gxFifoOffset = options.StopOnGxFifoOffset,
                    },
                    watches = new
                    {
                        memoryChanges = memoryWatchChanges,
                        emittedMemoryChanges = emittedWatchChanges,
                        writeMatches = writeWatchMatches,
                        loadMatches = loadWatchMatches,
                        emittedLoadMatches = emittedLoadWatchChanges,
                        indirectCallMatches = indirectCallWatchMatches,
                        emittedIndirectCallMatches = emittedCallWatchChanges,
                        gprChanges = gprWatchChanges,
                        emittedGprChanges = emittedGprWatchChanges,
                        pcTraceMatches,
                        emittedPcTraceMatches = emittedPcTraceChanges,
                    },
                    discInterface = BuildDiscInterfaceSummary(bus),
                    externalInterface = BuildExternalInterfaceSummary(bus),
                    sonicResourceState = BuildSonicResourceStateSummary(bus, state),
                    sonicPathLookup = sonicPathLookupTrace is null ? null : new
                    {
                        calls = sonicPathLookupCalls,
                        matches = sonicPathLookupMatches,
                        mismatches = sonicPathLookupMismatches,
                        modelFailures = sonicPathLookupModelFailures,
                        actualInstructions = sonicPathLookupActualInstructions,
                        minActualInstructions = sonicPathLookupMinActualInstructions == int.MaxValue ? 0 : sonicPathLookupMinActualInstructions,
                        maxActualInstructions = sonicPathLookupMaxActualInstructions,
                        elapsedCycles = sonicPathLookupElapsedCycles,
                        minElapsedCycles = sonicPathLookupMinElapsedCycles == ulong.MaxValue ? 0 : sonicPathLookupMinElapsedCycles,
                        maxElapsedCycles = sonicPathLookupMaxElapsedCycles,
                        predictedCycles = sonicPathLookupPredictedCycles,
                        cycleDelta = unchecked((long)sonicPathLookupElapsedCycles - (long)sonicPathLookupPredictedCycles),
                        maxCycleErrorMagnitude = sonicPathLookupMaxCycleErrorMagnitude,
                        eligibleFastForwards = sonicPathLookupEligibleFastForwards,
                        interruptEntries = sonicPathLookupInterruptEntries,
                        pending = sonicPathLookupPending is not null,
                    },
                    pcProfile = options.PcProfileTop is int pcProfileTop && pcProfile is not null
                        ? BuildPcProfileSummary(pcProfile, pcProfileTop, executed, options.ProfileAfter.GetValueOrDefault())
                        : null,
                    pcProfileWithoutExternalInterruptLeaves = options.PcProfileTop is int filteredPcProfileTop && pcProfile is not null
                        ? BuildPcProfileWithoutExternalInterruptLeavesSummary(pcProfile, filteredPcProfileTop, executed, options.ProfileAfter.GetValueOrDefault(), bus)
                        : null,
                    indirectCallSiteProfile = options.IndirectCallSiteProfileAddress is uint profiledCallSiteSummary
                        && options.IndirectCallSiteProfileTop is int indirectCallSiteProfileTopSummary
                        && indirectCallSiteProfile is not null
                            ? BuildIndirectCallSiteProfileSummary(profiledCallSiteSummary, indirectCallSiteProfile, indirectCallSiteProfileTopSummary)
                            : null,
                    branchSiteProfiles = branchSiteProfiles is not null
                        ? BuildBranchSiteProfilesSummary(options.BranchSiteProfiles, branchSiteProfiles)
                        : null,
                    pcLrProfiles = pcLrProfiles is not null
                        ? BuildPcLrProfilesSummary(options.PcLrProfiles, pcLrProfiles)
                        : null,
                    gx = new
                    {
                        fifoBytesWritten = gxFifoBytesWritten,
                        memoryCheckpointsRequested = gxMemoryCheckpoints.Length,
                        memoryCheckpointsWritten = gxMemoryCheckpointsWritten,
                        autoTextureSnapshots = gxTextureSnapshotCollector?.Snapshots.Count ?? 0,
                        autoTextureSnapshotDrawsSeen = gxTextureSnapshotCollector?.DrawsSeen ?? 0,
                        frameDump = gxFrameDump is null ? null : new
                        {
                            path = gxFrameDump.Path,
                            width = gxFrameDump.Width,
                            height = gxFrameDump.Height,
                            parsedDraws = gxFrameDump.Draws,
                            renderedQuads = gxFrameDump.RenderedQuads,
                            renderedTriangles = gxFrameDump.RenderedTriangles,
                            degenerateQuads = gxFrameDump.DegenerateQuads,
                            degenerateTriangles = gxFrameDump.DegenerateTriangles,
                            source = FormatGxFrameSourceSlug(gxFrameDump.Source),
                            sourceAddress = gxFrameDump.SourceAddress is uint frameAddress ? $"0x{frameAddress:X8}" : null,
                            sourceFormat = gxFrameDump.SourceFormat?.ToString(),
                            sourceCopyIndex = gxFrameDump.SourceCopyIndex,
                            rasterBudgetExhausted = gxFrameDump.RasterBudgetExhausted,
                            timings = gxFrameDump.Timings is null ? null : new
                            {
                                totalMs = gxFrameDump.Timings.TotalMs,
                                fifoExpansionMs = gxFrameDump.Timings.FifoExpansionMs,
                                bufferInitMs = gxFrameDump.Timings.BufferInitMs,
                                viResolveMs = gxFrameDump.Timings.ViResolveMs,
                                replayMs = gxFrameDump.Timings.ReplayMs,
                                registerWriteMs = gxFrameDump.Timings.RegisterWriteMs,
                                vertexDecodeMs = gxFrameDump.Timings.VertexDecodeMs,
                                rasterizeMs = gxFrameDump.Timings.RasterizeMs,
                                rasterSetupMs = gxFrameDump.Timings.RasterSetupMs,
                                rasterCoverageMs = gxFrameDump.Timings.RasterCoverageMs,
                                rasterDepthTestMs = gxFrameDump.Timings.RasterDepthTestMs,
                                rasterTevTextureMs = gxFrameDump.Timings.RasterTevTextureMs,
                                rasterAlphaTestMs = gxFrameDump.Timings.RasterAlphaTestMs,
                                rasterBlendWriteMs = gxFrameDump.Timings.RasterBlendWriteMs,
                                efbCoverageMs = gxFrameDump.Timings.EfbCoverageMs,
                                efbCopyMs = gxFrameDump.Timings.EfbCopyMs,
                                sourceCaptureMs = gxFrameDump.Timings.SourceCaptureMs,
                                displayCaptureMs = gxFrameDump.Timings.DisplayCaptureMs,
                                sourceSelectionMs = gxFrameDump.Timings.SourceSelectionMs,
                                pngWriteMs = gxFrameDump.Timings.PngWriteMs,
                            },
                            fastPathStats = gxFrameDump.FastPathStats is null ? null : new
                            {
                                singleStageTevFastTriangles = gxFrameDump.FastPathStats.SingleStageTevFastTriangles,
                                singleStageTevFastPixels = gxFrameDump.FastPathStats.SingleStageTevFastPixels,
                                directTextureSamplerTriangles = gxFrameDump.FastPathStats.DirectTextureSamplerTriangles,
                                directTextureSamplerPixels = gxFrameDump.FastPathStats.DirectTextureSamplerPixels,
                                genericTevPixels = gxFrameDump.FastPathStats.GenericTevPixels,
                                legacyTextureFallbackPixels = gxFrameDump.FastPathStats.LegacyTextureFallbackPixels,
                                directTextureFormats = gxFrameDump.FastPathStats.DirectTextureFormats,
                                textureSamplerFallbacks = gxFrameDump.FastPathStats.TextureSamplerFallbacks,
                            },
                            efbCopyStats = gxFrameDump.EfbCopyStats is null ? null : new
                            {
                                displayCopies = gxFrameDump.EfbCopyStats.DisplayCopies,
                                directDisplayCopies = gxFrameDump.EfbCopyStats.DirectDisplayCopies,
                                filteredDisplayCopies = gxFrameDump.EfbCopyStats.FilteredDisplayCopies,
                                textureCopies = gxFrameDump.EfbCopyStats.TextureCopies,
                                clears = gxFrameDump.EfbCopyStats.Clears,
                                depthClears = gxFrameDump.EfbCopyStats.DepthClears,
                                displayCopyMs = gxFrameDump.EfbCopyStats.DisplayCopyMs,
                                textureCopyMs = gxFrameDump.EfbCopyStats.TextureCopyMs,
                                colorClearMs = gxFrameDump.EfbCopyStats.ColorClearMs,
                                depthClearMs = gxFrameDump.EfbCopyStats.DepthClearMs,
                                displayCopyModes = gxFrameDump.EfbCopyStats.DisplayCopyModes,
                                textureCopyFormats = gxFrameDump.EfbCopyStats.TextureCopyFormats,
                            },
                        },
                    },
                    fastForward = new
                    {
                        idleCycles = idleFastForwardCycles,
                        ctrDelayInstructions = ctrDelayFastForwardInstructions,
                        bulkMemoryInstructions = bulkFastForwardInstructions,
                        reverseWordFillInstructions = reverseWordFillFastForwardInstructions,
                        cacheInstructions = cacheFastForwardInstructions,
                        leafHelperInstructions = leafFastForwardInstructions,
                        timeBaseReadInstructions = timeBaseReadFastForwardInstructions,
                        externalInterruptLeafInstructions = externalInterruptLeafFastForwardInstructions,
                        memoryCopyInstructions = memoryCopyFastForwardInstructions,
                        textureSampleInstructions = textureSampleFastForwardInstructions,
                        stringCopyInstructions = stringCopyFastForwardInstructions,
                        stringCompareInstructions = stringCompareFastForwardInstructions,
                        stringLengthInstructions = stringLengthFastForwardInstructions,
                        prsDecompressInstructions = prsDecompressFastForwardInstructions,
                        trigTableInstructions = trigTableFastForwardInstructions,
                        bitUnpackInstructions = bitUnpackFastForwardInstructions,
                        tickWaitInstructions = tickWaitFastForwardInstructions,
                        callbackWaitInstructions = callbackWaitFastForwardInstructions,
                        dotProductInstructions = dotProductFastForwardInstructions,
                        resourceLookupInstructions = resourceLookupFastForwardInstructions,
                        gxAttributeFlushInstructions = gxAttributeFlushFastForwardInstructions,
                        bitPlaneCropInstructions = bitPlaneCropFastForwardInstructions,
                        byteTableLookupInstructions = byteTableLookupFastForwardInstructions,
                        gxVertexEmitInstructions = gxVertexEmitFastForwardInstructions,
                        sonicGxTexObjLoadNoCallbackInstructions = sonicGxTexObjLoadNoCallbackFastForwardInstructions,
                        sonicGxPackedStateSetterInstructions = sonicGxPackedStateSetterFastForwardInstructions,
                        normalizedStringScanInstructions = normalizedStringScanFastForwardInstructions,
                        sonicResourceModeQueryInstructions = sonicResourceModeQueryFastForwardInstructions,
                        sonicResourceStatePollInstructions = sonicResourceStatePollFastForwardInstructions,
                        sonicModeWrapperInstructions = sonicModeWrapperFastForwardInstructions,
                        sonicResourceFixupInstructions = sonicResourceFixupFastForwardInstructions,
                        sonicOverlayInactiveSlotScanInstructions = sonicOverlayInactiveSlotScanFastForwardInstructions,
                        sonicPathRecordScanInstructions = sonicPathRecordScanFastForwardInstructions,
                        sonicPairedTransform2dInstructions = sonicPairedTransform2dFastForwardInstructions,
                        sonicGxFloatStripEmitInstructions = sonicGxFloatStripEmitFastForwardInstructions,
                        sonicGxFloatAttributeStripEmitInstructions = sonicGxFloatAttributeStripEmitFastForwardInstructions,
                        sonicGxCommandListFetchInstructions = sonicGxCommandListFetchFastForwardInstructions,
                        sonicGxCommandListTerminalInstructions = sonicGxCommandListTerminalFastForwardInstructions,
                        sonicGxCommandDispatchInstructions = sonicGxCommandDispatchFastForwardInstructions,
                        sonicGprSaveRestoreTailInstructions = sonicGprSaveRestoreTailFastForwardInstructions,
                        sonicGxAttributeStateSetterInstructions = sonicGxAttributeStateSetterFastForwardInstructions,
                        sonicGxDrawBeginInstructions = sonicGxDrawBeginFastForwardInstructions,
                        sonicGxVertexDescriptorSetterInstructions = sonicGxVertexDescriptorSetterFastForwardInstructions,
                        sonicGxVertexAttributeFlushInstructions = sonicGxVertexAttributeFlushFastForwardInstructions,
                        sonicGxIndexedStripBatchInstructions = sonicGxIndexedStripBatchFastForwardInstructions,
                        sonicGxIndexedStripDrawBeginInstructions = sonicGxIndexedStripDrawBeginFastForwardInstructions,
                        sonicGxIndexedStripTailInstructions = sonicGxIndexedStripTailFastForwardInstructions,
                        sonicGxIndexedStripEpilogueInstructions = sonicGxIndexedStripEpilogueFastForwardInstructions,
                        sonicGxFloatTexcoordStripEmitInstructions = sonicGxFloatTexcoordStripEmitFastForwardInstructions,
                        sonicPairedTransform4dInstructions = sonicPairedTransform4dFastForwardInstructions,
                        sonicVectorBlendCopyInstructions = sonicVectorBlendCopyFastForwardInstructions,
                        sonicGeneratedModelPointerScanInstructions = sonicGeneratedModelPointerScanFastForwardInstructions,
                        sonicGeneratedRangeScanInstructions = sonicGeneratedRangeScanFastForwardInstructions,
                    },
                };

                File.WriteAllText(fullPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _error.WriteLine($"Run summary write failed: {ex.Message}");
            }
        }

        stepObserver?.Invoke(new DolRunStep(0, state.Pc, 0, state, bus, IsInitial: true));
        emulationStopwatch.Start();

        try
        {
            for (; executed < options.MaxInstructions && !state.Halted; executed++)
            {
                UpdateControllerButtons(bus, options, executed);
                bus.SmallDataBaseRegister = state.Gpr[13];
                uint pc = state.Pc;
                if (options.StopOnPc == pc && executed >= options.StopOnPcAfter.GetValueOrDefault())
                {
                    stoppedOnPc = true;
                    break;
                }

                bool profileThisInstruction = executed >= options.ProfileAfter.GetValueOrDefault();
                if (profileThisInstruction && pcProfile is not null)
                {
                    pcProfile.TryGetValue(pc, out ulong count);
                    ulong nextCount = count + 1;
                    pcProfile[pc] = nextCount;
                }

                if (hotPcProfile is not null && executed >= options.StopOnHotPcAfter.GetValueOrDefault())
                {
                    hotPcProfile.TryGetValue(pc, out ulong hotCount);
                    ulong nextHotCount = hotCount + 1;
                    hotPcProfile[pc] = nextHotCount;
                    if (options.StopOnHotPc is ulong hotPcThreshold && nextHotCount >= hotPcThreshold)
                    {
                        stoppedOnHotPc = pc;
                        stoppedOnHotPcCount = nextHotCount;
                        break;
                    }
                }

                currentPc = pc;
                currentInstruction = bus.Read32(pc);
                if (sonicPathLookupTrace is not null)
                {
                    if (sonicPathLookupPending is not null && pc == ExternalInterruptVector)
                    {
                        sonicPathLookupPending = sonicPathLookupPending with { InterruptEntries = sonicPathLookupPending.InterruptEntries + 1 };
                    }

                    if (sonicPathLookupPending is SonicPathLookupPending pending && pc == pending.Lr)
                    {
                        uint actualResult = state.Gpr[3];
                        bool modelMatched = pending.Prediction.Success && actualResult == pending.Prediction.Result;
                        if (pending.Prediction.Success)
                        {
                            if (modelMatched)
                            {
                                sonicPathLookupMatches++;
                            }
                            else
                            {
                                sonicPathLookupMismatches++;
                            }
                        }
                        else
                        {
                            sonicPathLookupModelFailures++;
                        }

                        int actualInstructionCount = executed - pending.EntryInstruction + 1;
                        sonicPathLookupActualInstructions += actualInstructionCount;
                        sonicPathLookupMinActualInstructions = Math.Min(sonicPathLookupMinActualInstructions, actualInstructionCount);
                        sonicPathLookupMaxActualInstructions = Math.Max(sonicPathLookupMaxActualInstructions, actualInstructionCount);
                        ulong elapsedCycles = state.TimeBase - pending.EntryTimeBase;
                        ulong decrementerDelta = unchecked(pending.EntryDecrementer - state.Spr[22]);
                        sonicPathLookupElapsedCycles += elapsedCycles;
                        sonicPathLookupMinElapsedCycles = Math.Min(sonicPathLookupMinElapsedCycles, elapsedCycles);
                        sonicPathLookupMaxElapsedCycles = Math.Max(sonicPathLookupMaxElapsedCycles, elapsedCycles);
                        sonicPathLookupInterruptEntries += pending.InterruptEntries;
                        long cycleDelta = unchecked((long)elapsedCycles - (long)pending.Prediction.EstimatedCycles);
                        sonicPathLookupPredictedCycles += pending.Prediction.EstimatedCycles;
                        sonicPathLookupMaxCycleErrorMagnitude = Math.Max(sonicPathLookupMaxCycleErrorMagnitude, (ulong)Math.Abs(cycleDelta));
                        string actualStackWindow = CaptureMemoryWindowHex(bus.Memory, pending.StackBase, SonicPathLookupStackWindowBytes);
                        if (pending.FastForwardEligible)
                        {
                            sonicPathLookupEligibleFastForwards++;
                        }

                        sonicPathLookupTrace.WriteLine($"{pending.EntryInstruction},0x{pending.EntryPc:X8},0x{pending.Lr:X8},0x{pending.Path:X8},{(pending.Prediction.Success ? "ok" : "model-failure")},0x{pending.Prediction.Result:X8},0x{actualResult:X8},{(pending.Prediction.Success ? modelMatched.ToString().ToLowerInvariant() : string.Empty)},\"{EscapeCsv(pending.Prediction.Reason)}\",\"{EscapeCsv(pending.Prediction.PathText)}\",{actualInstructionCount},{elapsedCycles},{decrementerDelta},{pending.Prediction.EstimatedCycles},{cycleDelta},{pending.Prediction.CandidateEntries},{pending.Prediction.SegmentComparisons},{pending.Prediction.CompareBytes},{pending.FastForwardEligible.ToString().ToLowerInvariant()},{pending.InterruptEntries},0x{pending.EntryR0:X8},0x{pending.EntryR4:X8},0x{pending.EntryR5:X8},0x{pending.EntryR6:X8},0x{pending.EntryCr:X8},0x{state.Gpr[0]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Cr:X8},0x{state.Lr:X8},0x{state.Ctr:X8},0x{state.Xer:X8},\"{FormatGprSnapshot(pending.EntryGpr)}\",\"{FormatGprSnapshot(state.Gpr)}\",0x{pending.StackBase:X8},\"{pending.EntryStackWindow}\",\"{actualStackWindow}\",{(!string.Equals(pending.EntryStackWindow, actualStackWindow, StringComparison.Ordinal)).ToString().ToLowerInvariant()}");
                        sonicPathLookupPending = null;
                    }

                    if (pc == SonicPathLookupEntryPc)
                    {
                        sonicPathLookupCalls++;
                        SonicPathLookupPrediction prediction = PredictSonicPathLookup(bus, state);
                        uint stackBase = unchecked(state.Gpr[1] - 0x50);
                        bool fastForwardEligible = prediction.Success && CanFastForwardSonicPathLookupCycles(state, bus, prediction.EstimatedCycles);
                        sonicPathLookupPending = new SonicPathLookupPending(executed + 1, pc, state.Lr, state.Gpr[3], prediction, state.Gpr[0], state.Gpr[4], state.Gpr[5], state.Gpr[6], state.Cr, [.. state.Gpr], stackBase, CaptureMemoryWindowHex(bus.Memory, stackBase, SonicPathLookupStackWindowBytes), state.TimeBase, state.Spr[22], FastForwardEligible: fastForwardEligible);
                    }
                }

                if (profileThisInstruction && pcLrProfiles is not null && pcLrProfiles.TryGetValue(pc, out Dictionary<uint, ulong>? pcLrProfile))
                {
                    pcLrProfile.TryGetValue(state.Lr, out ulong pcLrCount);
                    pcLrProfile[state.Lr] = pcLrCount + 1;
                }

                if (profileThisInstruction
                    && branchSiteProfiles is not null
                    && branchSiteProfiles.TryGetValue(pc, out Dictionary<uint, ulong>? branchSiteProfile)
                    && TryGetBranchSiteTarget(currentInstruction, pc, state, out uint branchSiteTarget))
                {
                    branchSiteProfile.TryGetValue(branchSiteTarget, out ulong branchSiteTargetCount);
                    branchSiteProfile[branchSiteTarget] = branchSiteTargetCount + 1;
                }

                if (tracedPcSet?.Contains(pc) == true && executed >= options.TracePcAfter.GetValueOrDefault())
                {
                    pcTraceMatches++;
                    if (options.WatchLimit is null || emittedPcTraceChanges < options.WatchLimit)
                    {
                        _output.WriteLine($"PC trace 0x{pc:X8} hit after {executed + 1} instruction(s): 0x{currentInstruction:X8} {PowerPcDisassembler.Disassemble(currentInstruction)} {FormatPcTraceRegisters(state)}");
                        emittedPcTraceChanges++;
                    }
                    else if (!pcTraceLimitNoticeEmitted)
                    {
                        _output.WriteLine($"PC trace output limit of {options.WatchLimit} reached; suppressing further PC trace hits.");
                        pcTraceLimitNoticeEmitted = true;
                    }
                }

                if (options.TracePrsDecompress
                    && currentInstruction == 0x8903_0000
                    && executed >= options.TracePcAfter.GetValueOrDefault())
                {
                    if (options.WatchLimit is null || emittedPrsTraceChanges < options.WatchLimit)
                    {
                        string? signatureMismatch = GetSonicPrsDecompressSignatureMismatch(bus, pc);
                        _output.WriteLine($"PRS trace after {executed + 1} instruction(s): candidate pc=0x{pc:X8} signature={(signatureMismatch is null ? "yes" : "no")} source=0x{state.Gpr[3]:X8} destination=0x{state.Gpr[4]:X8}{(signatureMismatch is null ? string.Empty : $" {signatureMismatch}")}");
                        emittedPrsTraceChanges++;
                    }
                    else if (!prsTraceLimitNoticeEmitted)
                    {
                        _output.WriteLine($"PRS trace output limit of {options.WatchLimit} reached; suppressing further PRS trace hits.");
                        prsTraceLimitNoticeEmitted = true;
                    }
                }

                if (options.FastForwardIdle && TryFastForwardPikminHeapWaitLoop(state, bus, out ulong skippedIdleCycles))
                {
                    idleFastForwardCycles += skippedIdleCycles;
                    continue;
                }

                if (options.FastForwardIdle && TryFastForwardKnownIdleLoop(state, bus, out skippedIdleCycles))
                {
                    idleFastForwardCycles += skippedIdleCycles;
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardCtrDelayLoop(state, bus, out int skippedInstructions))
                {
                    ctrDelayFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardZeroStoreLoop(state, bus, out skippedInstructions))
                {
                    bulkFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardWordFillLoopCore(state, bus, out skippedInstructions, out bool reverseWordFill))
                {
                    bulkFastForwardInstructions += (uint)skippedInstructions;
                    if (reverseWordFill)
                    {
                        reverseWordFillFastForwardInstructions += (uint)skippedInstructions;
                    }

                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardCtrZeroStoreLoop(state, bus, out skippedInstructions))
                {
                    bulkFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardByteCopyLoop(state, bus, out skippedInstructions))
                {
                    bulkFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardCtrByteCopyLoop(state, bus, out skippedInstructions))
                {
                    memoryCopyFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardCtrSingleByteCopyLoop(state, bus, out skippedInstructions))
                {
                    memoryCopyFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardWordCopyLoop(state, bus, out skippedInstructions))
                {
                    memoryCopyFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardCtrCacheBlockLoop(state, bus, out skippedInstructions))
                {
                    cacheFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardExternalInterruptLeafHelper(state, bus, out skippedInstructions))
                {
                    externalInterruptLeafFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardTimeBaseReadLeaf(state, bus, pc, out skippedInstructions))
                {
                    timeBaseReadFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSmallLeafHelper(state, bus, out skippedInstructions))
                {
                    leafFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardLongDivisionLeaf(state, bus, out skippedInstructions))
                {
                    leafFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardMemmoveRoutine(state, bus, out skippedInstructions))
                {
                    memoryCopyFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardNullTerminatedByteCopyLoop(state, bus, out skippedInstructions))
                {
                    stringCopyFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardStringCompareRoutine(state, bus, out skippedInstructions))
                {
                    stringCompareFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardStringLengthRoutine(state, bus, out skippedInstructions))
                {
                    stringLengthFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardTextureSampleLeaf(state, bus, out skippedInstructions))
                {
                    textureSampleFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                Action<string>? tracePrsDecompress = null;
                if (options.TracePrsDecompress)
                {
                    tracePrsDecompress = message =>
                    {
                        if (executed < options.TracePcAfter.GetValueOrDefault())
                        {
                            return;
                        }

                        if (options.WatchLimit is null || emittedPrsTraceChanges < options.WatchLimit)
                        {
                            _output.WriteLine($"PRS trace after {executed + 1} instruction(s): {message}");
                            emittedPrsTraceChanges++;
                        }
                        else if (!prsTraceLimitNoticeEmitted)
                        {
                            _output.WriteLine($"PRS trace output limit of {options.WatchLimit} reached; suppressing further PRS trace hits.");
                            prsTraceLimitNoticeEmitted = true;
                        }
                    };
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicPrsDecompressCore(state, bus, out skippedInstructions, tracePrsDecompress))
                {
                    prsDecompressFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardMetroTrkEventLoop(state, bus, out skippedInstructions))
                {
                    leafFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicTrigTableInit(state, bus, out skippedInstructions))
                {
                    trigTableFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicBitUnpackRows(state, bus, out skippedInstructions))
                {
                    bitUnpackFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicTickWaitLoop(state, bus, out skippedInstructions))
                {
                    tickWaitFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicCallbackWaitLoop(state, bus, out skippedInstructions))
                {
                    callbackWaitFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicDotProductLoop(state, bus, out skippedInstructions))
                {
                    dotProductFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicResourceTableLookup(state, bus, out skippedInstructions))
                {
                    resourceLookupFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxAttributeFlush(state, bus, out skippedInstructions))
                {
                    gxAttributeFlushFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicBitPlaneCrop(state, bus, out skippedInstructions))
                {
                    bitPlaneCropFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicByteTableLookup(state, bus, out skippedInstructions))
                {
                    byteTableLookupFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxVertexEmitLoop(state, bus, out skippedInstructions))
                {
                    gxVertexEmitFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxTexObjLoadNoCallback(state, bus, out skippedInstructions))
                {
                    sonicGxTexObjLoadNoCallbackFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxPackedStateSetter(state, bus, out skippedInstructions))
                {
                    sonicGxPackedStateSetterFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxVertexDescriptorSetter(state, bus, out skippedInstructions))
                {
                    sonicGxVertexDescriptorSetterFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxVertexAttributeFlush(state, bus, out skippedInstructions))
                {
                    sonicGxVertexAttributeFlushFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxIndexedStripBatch(state, bus, out skippedInstructions))
                {
                    sonicGxIndexedStripBatchFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxIndexedStripDrawBegin(state, bus, out skippedInstructions))
                {
                    sonicGxIndexedStripDrawBeginFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxIndexedStripTail(state, bus, out skippedInstructions))
                {
                    sonicGxIndexedStripTailFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxIndexedStripEpilogue(state, bus, out skippedInstructions))
                {
                    sonicGxIndexedStripEpilogueFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxDrawBegin(state, bus, out skippedInstructions))
                {
                    sonicGxDrawBeginFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicNormalizedStringScan(state, bus, out skippedInstructions))
                {
                    normalizedStringScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicPathRecordScan(state, bus, out skippedInstructions))
                {
                    sonicPathRecordScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicPairedTransform2d(state, bus, out skippedInstructions))
                {
                    sonicPairedTransform2dFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicPairedTransform4d(state, bus, out skippedInstructions))
                {
                    sonicPairedTransform4dFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicVectorBlendCopyLoop(state, bus, out skippedInstructions))
                {
                    sonicVectorBlendCopyFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGeneratedModelPointerScan(state, bus, out skippedInstructions))
                {
                    sonicGeneratedModelPointerScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGeneratedRangeScan(state, bus, out skippedInstructions))
                {
                    sonicGeneratedRangeScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxFloatStripEmitLoop(state, bus, out skippedInstructions))
                {
                    sonicGxFloatStripEmitFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxFloatAttributeStripEmitLoop(state, bus, out skippedInstructions))
                {
                    sonicGxFloatAttributeStripEmitFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxCommandListTerminal(state, bus, out skippedInstructions))
                {
                    sonicGxCommandListTerminalFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxCommandListFetch(state, bus, out skippedInstructions))
                {
                    sonicGxCommandListFetchFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxCommandDispatch(state, bus, out skippedInstructions))
                {
                    sonicGxCommandDispatchFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGprSaveRestoreTail(state, bus, out skippedInstructions))
                {
                    sonicGprSaveRestoreTailFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxAttributeStateSetter(state, bus, out skippedInstructions))
                {
                    sonicGxAttributeStateSetterFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicGxFloatTexcoordStripEmitLoop(state, bus, out skippedInstructions))
                {
                    sonicGxFloatTexcoordStripEmitFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicOverlayInactiveSlotScan(state, bus, out skippedInstructions))
                {
                    sonicOverlayInactiveSlotScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicResourceModeQuery(state, bus, out skippedInstructions))
                {
                    sonicResourceModeQueryFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicModeWrapper(state, bus, out skippedInstructions))
                {
                    sonicModeWrapperFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicResourceStatePoll(state, bus, out skippedInstructions))
                {
                    sonicResourceStatePollFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicResourceFixupRecord(state, bus, out skippedInstructions))
                {
                    sonicResourceFixupFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (TryGetWatchedLoad(options, bus, state, currentInstruction, out WatchedLoad watchedLoad))
                {
                    loadWatchMatches++;
                    if (options.WatchLimit is null || emittedLoadWatchChanges < options.WatchLimit)
                    {
                        _output.WriteLine($"Load watch 0x{watchedLoad.EffectiveAddress:X8} => 0x{watchedLoad.Value:X8} into r{watchedLoad.TargetRegister} after {executed + 1} instruction(s), 0x{pc:X8}: 0x{currentInstruction:X8} {PowerPcDisassembler.Disassemble(currentInstruction)} {FormatWatchRegisters(state, currentInstruction)}");
                        emittedLoadWatchChanges++;
                    }
                    else if (!loadWatchLimitNoticeEmitted)
                    {
                        _output.WriteLine($"Load watch output limit of {options.WatchLimit} reached; suppressing further load watch changes.");
                        loadWatchLimitNoticeEmitted = true;
                    }
                }

                if ((watchedCallTargetSet is not null || hasCallRangeWatch) && TryGetIndirectBranchTarget(currentInstruction, state, out uint indirectTarget, out string? targetRegister, out bool link) && MatchesCallWatch(options, watchedCallTargetSet, indirectTarget))
                {
                    indirectCallWatchMatches++;
                    if (options.WatchLimit is null || emittedCallWatchChanges < options.WatchLimit)
                    {
                        string branchKind = link ? "call" : "branch";
                        _output.WriteLine($"Indirect {branchKind} watch {targetRegister}=0x{indirectTarget:X8} after {executed + 1} instruction(s), 0x{pc:X8}: 0x{currentInstruction:X8} {PowerPcDisassembler.Disassemble(currentInstruction)} {FormatWatchRegisters(state, currentInstruction)}");
                        emittedCallWatchChanges++;
                    }
                    else if (!callWatchLimitNoticeEmitted)
                    {
                        _output.WriteLine($"Indirect call watch output limit of {options.WatchLimit} reached; suppressing further indirect call watch changes.");
                        callWatchLimitNoticeEmitted = true;
                    }
                }

                if (profileThisInstruction
                    && indirectCallSiteProfile is not null
                    && options.IndirectCallSiteProfileAddress == pc
                    && TryGetIndirectBranchTarget(currentInstruction, state, out uint profiledIndirectTarget, out _, out bool profiledLink)
                    && profiledLink)
                {
                    indirectCallSiteProfile.TryGetValue(profiledIndirectTarget, out ulong targetCount);
                    indirectCallSiteProfile[profiledIndirectTarget] = targetCount + 1;
                }

                uint instruction = interpreter.Step(state, bus);
                if (options.WatchGpr is int watchedGpr
                    && executed >= options.WatchGprAfter.GetValueOrDefault()
                    && state.Gpr[watchedGpr] != watchedGprValue)
                {
                    gprWatchChanges++;
                    if (options.WatchLimit is null || emittedGprWatchChanges < options.WatchLimit)
                    {
                        _output.WriteLine($"GPR watch r{watchedGpr}: 0x{watchedGprValue:X8} -> 0x{state.Gpr[watchedGpr]:X8} after {executed + 1} instruction(s), 0x{pc:X8}: 0x{instruction:X8} {PowerPcDisassembler.Disassemble(instruction)}");
                        emittedGprWatchChanges++;
                    }
                    else if (!gprWatchLimitNoticeEmitted)
                    {
                        _output.WriteLine($"GPR watch output limit of {options.WatchLimit} reached; suppressing further GPR watch changes.");
                        gprWatchLimitNoticeEmitted = true;
                    }

                    watchedGprValue = state.Gpr[watchedGpr];
                }

                string? traceLine = null;

                foreach (uint observedAddress in watchedAddresses)
                {
                    uint nextWatchedValue = bus.Read32(observedAddress);
                    uint watchedValue = watchedValues[observedAddress];
                    if (nextWatchedValue != watchedValue)
                    {
                        memoryWatchChanges++;
                        if (options.WatchLimit is null || emittedWatchChanges < options.WatchLimit)
                        {
                            _output.WriteLine($"Watch 0x{observedAddress:X8}: 0x{watchedValue:X8} -> 0x{nextWatchedValue:X8} after {executed + 1} instruction(s), 0x{pc:X8}: 0x{instruction:X8} {PowerPcDisassembler.Disassemble(instruction)} {FormatWatchRegisters(state, instruction)}");
                            emittedWatchChanges++;
                        }
                        else if (!watchLimitNoticeEmitted)
                        {
                            _output.WriteLine($"Watch output limit of {options.WatchLimit} reached; suppressing further watch changes.");
                            watchLimitNoticeEmitted = true;
                        }

                        watchedValues[observedAddress] = nextWatchedValue;
                    }
                }

                if (options.Trace && (!options.Quiet || traceFile is not null))
                {
                    traceLine = FormatTraceLine(executed, pc, instruction);
                    traceOutput.WriteLine(traceLine);
                }

                if (traceTail is not null)
                {
                    traceLine ??= FormatTraceLine(executed, pc, instruction);
                    if (traceTail.Count == traceTailCapacity)
                    {
                        traceTail.Dequeue();
                    }

                    traceTail.Enqueue(traceLine);
                }

                stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, instruction, state, bus));

                if (stoppedAfterWriteWatch || stoppedOnGxFifoOffset)
                {
                    executed++;
                    break;
                }
            }
        }
        catch (UnsupportedInstructionException ex)
        {
            StopEmulationTimer();
            stepObserver?.Invoke(new DolRunStep(executed, state.Pc, ex.Instruction, state, bus, IsFinal: true));
            _error.WriteLine($"Stopped after {executed} instructions: unsupported instruction 0x{ex.Instruction:X8} at 0x{ex.Address:X8} ({PowerPcDisassembler.Disassemble(ex.Instruction)}).");
            bus.MainRamWrite32Observer = previousWriteObserver;
            bus.MainRamWriteObserver = previousWriteAnyObserver;
            bus.Memory.MainRamStoreObserver = previousMemoryStoreObserver;
            bus.Memory.MainRamBulkWriteObserver = previousBulkWriteObserver;
            bus.MmioAccessObserver = previousMmioObserver;
            WriteTraceTail(traceTail, _error);
            Stopwatch memoryFindStopwatch = Stopwatch.StartNew();
            WriteRequestedMemoryFinds(options, bus, _error);
            memoryFindMilliseconds += StopAndGetMilliseconds(memoryFindStopwatch);
            Stopwatch pointerTableDumpStopwatch = Stopwatch.StartNew();
            WriteRequestedPointerTableDumps(options, bus, _error);
            pointerTableDumpMilliseconds += StopAndGetMilliseconds(pointerTableDumpStopwatch);

            if (options.DumpRegisters)
            {
                Stopwatch registerDumpStopwatch = Stopwatch.StartNew();
                ConsoleFormatting.WriteRegisters(_error, state);
                registerDumpMilliseconds += StopAndGetMilliseconds(registerDumpStopwatch);
            }

            if (options.DumpMmio)
            {
                Stopwatch mmioDumpStopwatch = Stopwatch.StartNew();
                ConsoleFormatting.WriteMmioSummary(_error, bus.MmioAccesses);
                mmioDumpMilliseconds += StopAndGetMilliseconds(mmioDumpStopwatch);
            }

            if (options.DumpThreads)
            {
                Stopwatch threadDumpStopwatch = Stopwatch.StartNew();
                ConsoleFormatting.WriteThreadSummary(_error, bus.Memory);
                threadDumpMilliseconds += StopAndGetMilliseconds(threadDumpStopwatch);
            }

            if (options.DumpMessageQueues)
            {
                Stopwatch messageQueueDumpStopwatch = Stopwatch.StartNew();
                ConsoleFormatting.WriteMessageQueueSummary(_error, bus.Memory);
                messageQueueDumpMilliseconds += StopAndGetMilliseconds(messageQueueDumpStopwatch);
            }

            Stopwatch memoryDumpStopwatch = Stopwatch.StartNew();
            WriteRequestedMemoryDumps(options, bus, _error);
            memoryDumpMilliseconds += StopAndGetMilliseconds(memoryDumpStopwatch);

            WriteRunSummary(2, stopReasonOverride: "unsupported-instruction", exceptionType: nameof(UnsupportedInstructionException), exceptionAddress: ex.Address, exceptionInstruction: ex.Instruction);
            return 2;
        }
        catch (AddressTranslationException ex)
        {
            StopEmulationTimer();
            stepObserver?.Invoke(new DolRunStep(executed, state.Pc, 0, state, bus, IsFinal: true));
            _error.WriteLine($"Stopped after {executed} instructions: unmapped memory access at 0x{ex.Address:X8}.");
            bus.MainRamWrite32Observer = previousWriteObserver;
            bus.MainRamWriteObserver = previousWriteAnyObserver;
            bus.Memory.MainRamStoreObserver = previousMemoryStoreObserver;
            bus.Memory.MainRamBulkWriteObserver = previousBulkWriteObserver;
            bus.MmioAccessObserver = previousMmioObserver;
            WriteTraceTail(traceTail, _error);
            Stopwatch memoryFindStopwatch = Stopwatch.StartNew();
            WriteRequestedMemoryFinds(options, bus, _error);
            memoryFindMilliseconds += StopAndGetMilliseconds(memoryFindStopwatch);
            Stopwatch pointerTableDumpStopwatch = Stopwatch.StartNew();
            WriteRequestedPointerTableDumps(options, bus, _error);
            pointerTableDumpMilliseconds += StopAndGetMilliseconds(pointerTableDumpStopwatch);

            if (options.DumpRegisters)
            {
                Stopwatch registerDumpStopwatch = Stopwatch.StartNew();
                ConsoleFormatting.WriteRegisters(_error, state);
                registerDumpMilliseconds += StopAndGetMilliseconds(registerDumpStopwatch);
            }

            if (options.DumpMmio)
            {
                Stopwatch mmioDumpStopwatch = Stopwatch.StartNew();
                ConsoleFormatting.WriteMmioSummary(_error, bus.MmioAccesses);
                mmioDumpMilliseconds += StopAndGetMilliseconds(mmioDumpStopwatch);
            }

            if (options.DumpThreads)
            {
                Stopwatch threadDumpStopwatch = Stopwatch.StartNew();
                ConsoleFormatting.WriteThreadSummary(_error, bus.Memory);
                threadDumpMilliseconds += StopAndGetMilliseconds(threadDumpStopwatch);
            }

            if (options.DumpMessageQueues)
            {
                Stopwatch messageQueueDumpStopwatch = Stopwatch.StartNew();
                ConsoleFormatting.WriteMessageQueueSummary(_error, bus.Memory);
                messageQueueDumpMilliseconds += StopAndGetMilliseconds(messageQueueDumpStopwatch);
            }

            Stopwatch memoryDumpStopwatch = Stopwatch.StartNew();
            WriteRequestedMemoryDumps(options, bus, _error);
            memoryDumpMilliseconds += StopAndGetMilliseconds(memoryDumpStopwatch);

            WriteRunSummary(4, stopReasonOverride: "address-translation", exceptionType: nameof(AddressTranslationException), exceptionAddress: ex.Address);
            return 4;
        }

        StopEmulationTimer();
        bus.MainRamWrite32Observer = previousWriteObserver;
        bus.MainRamWriteObserver = previousWriteAnyObserver;
        bus.Memory.MainRamStoreObserver = previousMemoryStoreObserver;
        bus.Memory.MainRamBulkWriteObserver = previousBulkWriteObserver;
        bus.MmioAccessObserver = previousMmioObserver;
        stepObserver?.Invoke(new DolRunStep(executed, state.Pc, 0, state, bus, IsFinal: true));

        WriteTraceTail(traceTail, _output);
        Stopwatch normalMemoryFindStopwatch = Stopwatch.StartNew();
        WriteRequestedMemoryFinds(options, bus, _output);
        memoryFindMilliseconds += StopAndGetMilliseconds(normalMemoryFindStopwatch);
        Stopwatch normalPointerTableDumpStopwatch = Stopwatch.StartNew();
        WriteRequestedPointerTableDumps(options, bus, _output);
        pointerTableDumpMilliseconds += StopAndGetMilliseconds(normalPointerTableDumpStopwatch);

        if (!options.Quiet)
        {
            if (state.Halted)
            {
                _output.WriteLine($"Halted after {executed} instruction(s) at 0x{state.Pc:X8}.");
            }
            else if (stoppedOnPc)
            {
                _output.WriteLine($"Stopped on PC 0x{state.Pc:X8} after {executed} instruction(s).");
            }
            else if (stoppedOnHotPc is uint hotPc)
            {
                _output.WriteLine($"Stopped after PC 0x{hotPc:X8} executed {stoppedOnHotPcCount} time(s), at {executed} instruction(s).");
            }
            else if (stoppedAfterWriteWatch)
            {
                _output.WriteLine($"Stopped after {stoppedAfterWriteWatchCount} write watch match(es), at {executed} instruction(s).");
            }
            else if (stoppedOnGxFifoOffset)
            {
                int stopOffset = options.StopOnGxFifoOffset.GetValueOrDefault();
                _output.WriteLine($"Stopped on GX FIFO offset +0x{stopOffset:X} after {executed} instruction(s) ({gxFifoBytesWritten} FIFO byte(s) captured).");
            }
            else
            {
                _output.WriteLine($"Executed {executed} instruction(s).");
            }

            if (idleFastForwardCycles != 0)
            {
                _output.WriteLine($"Fast-forwarded {idleFastForwardCycles} idle cycle(s).");
            }

            if (bulkFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {bulkFastForwardInstructions} bulk memory initialization instruction(s).");
            }

            if (reverseWordFillFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {reverseWordFillFastForwardInstructions} reverse word-fill instruction(s).");
            }

            if (ctrDelayFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {ctrDelayFastForwardInstructions} CTR delay-loop instruction(s).");
            }

            if (cacheFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {cacheFastForwardInstructions} cache maintenance instruction(s).");
            }

            if (leafFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {leafFastForwardInstructions} small leaf helper instruction(s).");
            }

            if (timeBaseReadFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {timeBaseReadFastForwardInstructions} timebase read instruction(s).");
            }

            if (externalInterruptLeafFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {externalInterruptLeafFastForwardInstructions} external interrupt leaf instruction(s).");
            }

            if (memoryCopyFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {memoryCopyFastForwardInstructions} memory copy instruction(s).");
            }

            if (textureSampleFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {textureSampleFastForwardInstructions} texture sample helper instruction(s).");
            }

            if (stringCopyFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {stringCopyFastForwardInstructions} string copy instruction(s).");
            }

            if (stringCompareFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {stringCompareFastForwardInstructions} string compare instruction(s).");
            }

            if (stringLengthFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {stringLengthFastForwardInstructions} string length instruction(s).");
            }

            if (prsDecompressFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {prsDecompressFastForwardInstructions} PRS decompression instruction(s).");
            }

            if (trigTableFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {trigTableFastForwardInstructions} trigonometry table initialization instruction(s).");
            }

            if (bitUnpackFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {bitUnpackFastForwardInstructions} bit unpack instruction(s).");
            }

            if (tickWaitFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {tickWaitFastForwardInstructions} tick wait instruction(s).");
            }

            if (callbackWaitFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {callbackWaitFastForwardInstructions} callback wait instruction(s).");
            }

            if (dotProductFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {dotProductFastForwardInstructions} dot product instruction(s).");
            }

            if (resourceLookupFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {resourceLookupFastForwardInstructions} resource table lookup instruction(s).");
            }

            if (gxAttributeFlushFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {gxAttributeFlushFastForwardInstructions} GX attribute flush instruction(s).");
            }

            if (bitPlaneCropFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {bitPlaneCropFastForwardInstructions} bit-plane crop instruction(s).");
            }

            if (byteTableLookupFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {byteTableLookupFastForwardInstructions} byte table lookup instruction(s).");
            }

            if (gxVertexEmitFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {gxVertexEmitFastForwardInstructions} GX vertex emission instruction(s).");
            }

            if (sonicGxTexObjLoadNoCallbackFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxTexObjLoadNoCallbackFastForwardInstructions} Sonic GX texture object load instruction(s).");
            }

            if (sonicGxPackedStateSetterFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxPackedStateSetterFastForwardInstructions} Sonic GX packed state setter instruction(s).");
            }

            if (normalizedStringScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {normalizedStringScanFastForwardInstructions} normalized string scan instruction(s).");
            }

            if (sonicResourceModeQueryFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicResourceModeQueryFastForwardInstructions} Sonic resource mode query instruction(s).");
            }

            if (sonicResourceStatePollFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicResourceStatePollFastForwardInstructions} Sonic resource state poll instruction(s).");
            }

            if (sonicModeWrapperFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicModeWrapperFastForwardInstructions} Sonic mode wrapper instruction(s).");
            }

            if (sonicResourceFixupFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicResourceFixupFastForwardInstructions} Sonic resource fixup instruction(s).");
            }

            if (sonicOverlayInactiveSlotScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicOverlayInactiveSlotScanFastForwardInstructions} Sonic overlay inactive slot scan instruction(s).");
            }

            if (sonicPathRecordScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicPathRecordScanFastForwardInstructions} Sonic path record scan instruction(s).");
            }

            if (sonicPairedTransform2dFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicPairedTransform2dFastForwardInstructions} Sonic paired transform 2D instruction(s).");
            }

            if (sonicGxFloatStripEmitFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxFloatStripEmitFastForwardInstructions} Sonic GX float strip emit instruction(s).");
            }

            if (sonicGxFloatAttributeStripEmitFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxFloatAttributeStripEmitFastForwardInstructions} Sonic GX float/attribute strip emit instruction(s).");
            }

            if (sonicGxCommandListFetchFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxCommandListFetchFastForwardInstructions} Sonic GX command-list fetch instruction(s).");
            }

            if (sonicGxCommandListTerminalFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxCommandListTerminalFastForwardInstructions} Sonic GX command-list terminal instruction(s).");
            }

            if (sonicGxCommandDispatchFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxCommandDispatchFastForwardInstructions} Sonic GX command dispatch instruction(s).");
            }

            if (sonicGprSaveRestoreTailFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGprSaveRestoreTailFastForwardInstructions} Sonic GPR save/restore tail instruction(s).");
            }

            if (sonicGxAttributeStateSetterFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxAttributeStateSetterFastForwardInstructions} Sonic GX attribute state setter instruction(s).");
            }

            if (sonicGxDrawBeginFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxDrawBeginFastForwardInstructions} Sonic GX draw begin instruction(s).");
            }

            if (sonicGxVertexDescriptorSetterFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxVertexDescriptorSetterFastForwardInstructions} Sonic GX vertex descriptor setter instruction(s).");
            }

            if (sonicGxIndexedStripBatchFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxIndexedStripBatchFastForwardInstructions} Sonic GX indexed strip batch instruction(s).");
            }

            if (sonicGxIndexedStripDrawBeginFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxIndexedStripDrawBeginFastForwardInstructions} Sonic GX indexed strip draw begin instruction(s).");
            }

            if (sonicGxIndexedStripTailFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxIndexedStripTailFastForwardInstructions} Sonic GX indexed strip tail instruction(s).");
            }

            if (sonicGxIndexedStripEpilogueFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxIndexedStripEpilogueFastForwardInstructions} Sonic GX indexed strip epilogue instruction(s).");
            }

            if (sonicGxFloatTexcoordStripEmitFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxFloatTexcoordStripEmitFastForwardInstructions} Sonic GX float/texcoord strip emit instruction(s).");
            }

            if (sonicPairedTransform4dFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicPairedTransform4dFastForwardInstructions} Sonic paired transform 4D instruction(s).");
            }

            if (sonicVectorBlendCopyFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicVectorBlendCopyFastForwardInstructions} Sonic vector blend/copy instruction(s).");
            }

            if (sonicGeneratedModelPointerScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGeneratedModelPointerScanFastForwardInstructions} Sonic generated model pointer scan instruction(s).");
            }

            if (sonicGeneratedRangeScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGeneratedRangeScanFastForwardInstructions} Sonic generated range-scan instruction(s).");
            }

            if (gxMemoryCheckpoints.Length != 0)
            {
                _output.WriteLine($"Wrote {gxMemoryCheckpointsWritten} of {gxMemoryCheckpoints.Length} GX memory checkpoint(s).");
            }

            if (gxTextureSnapshotCollector is not null)
            {
                _output.WriteLine($"Captured {gxTextureSnapshotCollector.Snapshots.Count} automatic GX texture memory snapshot(s) across {gxTextureSnapshotCollector.DrawsSeen} draw(s).");
            }
        }

        Stopwatch gxMemorySnapshotStopwatch = Stopwatch.StartNew();
        GxMemorySnapshotSet? gxMemorySnapshots = BuildGxMemorySnapshotSet(gxMemoryCheckpoints, gxTextureSnapshotCollector?.Snapshots);
        gxMemorySnapshotMilliseconds += StopAndGetMilliseconds(gxMemorySnapshotStopwatch);

        if (options.GxFrameDumpPath is not null)
        {
            Stopwatch gxFrameDumpStopwatch = Stopwatch.StartNew();
            int gxWidth = options.FrameWidth ?? 640;
            int gxHeight = options.FrameHeight ?? 480;
            int gxFrameMaxDraws = options.GxFrameMaxDraws ?? RunDolOptions.DefaultGxFrameMaxDraws;
            int gxFrameMaxRasterPixels = options.GxFrameMaxRasterPixels ?? RunDolOptions.DefaultGxFrameMaxRasterPixels;
            if (!GxFifoSoftwareRenderer.TryRender(bus.MmioAccesses, bus.Memory, options.GxFrameDumpPath, gxWidth, gxHeight, gxFrameMaxDraws, options.GxFrameSkipDraws, stopAfterMaxDraws: true, maxRasterizedPixels: gxFrameMaxRasterPixels, ignoreEfbCopyClear: options.GxFrameIgnoreEfbCopyClear, source: options.GxFrameSource, displayCopyIndex: options.GxFrameCopyIndex, out GxFifoSoftwareRenderResult? gxFrame, out string? gxFrameError, memorySnapshots: gxMemorySnapshots))
            {
                gxFrameDumpMilliseconds += StopAndGetMilliseconds(gxFrameDumpStopwatch);
                _error.WriteLine($"GX frame dump failed: {gxFrameError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-frame-dump: {gxFrameError}");
                return 3;
            }
            gxFrameDump = gxFrame;

            if (!options.Quiet)
            {
                string skipped = options.GxFrameSkipDraws == 0 ? string.Empty : $" after skipping {options.GxFrameSkipDraws} draw(s)";
                string copyIndex = gxFrame!.SourceCopyIndex is int sourceCopyIndex ? $" copy #{sourceCopyIndex}" : string.Empty;
                string source = gxFrame!.Source == GxFrameDumpSource.Efb
                    ? "EFB"
                    : $"{FormatGxFrameSource(gxFrame.Source)}{copyIndex} at 0x{gxFrame.SourceAddress:X8} ({gxFrame.SourceFormat})";
                string rasterState = gxFrame.RasterBudgetExhausted ? "exhausted" : "retained";
                _output.WriteLine($"Wrote {gxFrame.Width}x{gxFrame.Height} GX diagnostic frame from {source} to {gxFrame.Path} ({gxFrame.Draws} draw(s) parsed{skipped}, capped at {gxFrameMaxDraws}, raster budget {gxFrameMaxRasterPixels} {rasterState}, {gxFrame.RenderedQuads} rendered quad(s), {gxFrame.RenderedTriangles} rendered triangle(s), {gxFrame.DegenerateQuads} degenerate quad(s), {gxFrame.DegenerateTriangles} degenerate triangle(s)).");
            }

            gxFrameDumpMilliseconds += StopAndGetMilliseconds(gxFrameDumpStopwatch);
        }

        if (options.GxFrameSweep is not null)
        {
            Stopwatch gxFrameSweepStopwatch = Stopwatch.StartNew();
            int gxFrameSweepResult = WriteGxFrameSweep(bus, options, gxMemorySnapshots);
            gxFrameSweepMilliseconds += StopAndGetMilliseconds(gxFrameSweepStopwatch);
            if (gxFrameSweepResult != 0)
            {
                WriteRunSummary(3, diagnosticFailure: "gx-frame-sweep");
                return 3;
            }
        }

        if (options.FrameDumpPath is not null)
        {
            Stopwatch frameDumpStopwatch = Stopwatch.StartNew();
            if (!FramebufferDumper.TryDump(bus, options, out FramebufferDumpResult? frameDump, out string? frameDumpError))
            {
                frameDumpMilliseconds += StopAndGetMilliseconds(frameDumpStopwatch);
                _error.WriteLine($"Frame dump failed: {frameDumpError}");
                WriteRunSummary(3, diagnosticFailure: $"frame-dump: {frameDumpError}");
                return 3;
            }

            if (!options.Quiet)
            {
                _output.WriteLine($"Wrote {frameDump!.Width}x{frameDump.Height} {frameDump.Format} frame from 0x{frameDump.Address:X8} to {frameDump.Path}.");
            }

            frameDumpMilliseconds += StopAndGetMilliseconds(frameDumpStopwatch);
        }

        if (options.GxDrawDumpPath is not null)
        {
            Stopwatch gxDrawDumpStopwatch = Stopwatch.StartNew();
            if (!GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(bus.MmioAccesses, bus.Memory, options.GxDrawDumpPath, options.GxDrawSkipDraws, options.GxDrawMaxDraws, out GxFifoDrawDiagnosticResult? gxDraws, out string? gxDrawError))
            {
                gxDrawDumpMilliseconds += StopAndGetMilliseconds(gxDrawDumpStopwatch);
                _error.WriteLine($"GX draw dump failed: {gxDrawError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-draw-dump: {gxDrawError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string skipped = options.GxDrawSkipDraws == 0 ? string.Empty : $" after skipping {options.GxDrawSkipDraws} draw(s)";
                _output.WriteLine($"Wrote GX draw diagnostics for {gxDraws!.DrawsWritten} draw(s){skipped} to {gxDraws.Path}.");
            }

            gxDrawDumpMilliseconds += StopAndGetMilliseconds(gxDrawDumpStopwatch);
        }

        if (options.GxCopyDumpPath is not null)
        {
            Stopwatch gxCopyDumpStopwatch = Stopwatch.StartNew();
            int gxWidth = options.FrameWidth ?? 640;
            int gxHeight = options.FrameHeight ?? 480;
            int gxFrameMaxRasterPixels = options.GxFrameMaxRasterPixels ?? RunDolOptions.DefaultGxFrameMaxRasterPixels;
            if (!GxFifoSoftwareRenderer.TryWriteCopyDiagnostics(bus.MmioAccesses, bus.Memory, options.GxCopyDumpPath, gxWidth, gxHeight, gxFrameMaxRasterPixels, options.GxFrameIgnoreEfbCopyClear, out GxFifoCopyDiagnosticResult? gxCopies, out string? gxCopyError))
            {
                gxCopyDumpMilliseconds += StopAndGetMilliseconds(gxCopyDumpStopwatch);
                _error.WriteLine($"GX copy dump failed: {gxCopyError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-copy-dump: {gxCopyError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string rasterState = gxCopies!.RasterBudgetExhausted ? "raster budget exhausted" : "raster budget retained";
                _output.WriteLine($"Wrote {gxCopies.CopiesWritten} GX copy diagnostic event(s) after {gxCopies.TotalDraws} draw(s) to {gxCopies.Path} ({rasterState}).");
            }

            gxCopyDumpMilliseconds += StopAndGetMilliseconds(gxCopyDumpStopwatch);
        }

        if (options.GxCoverageDumpPath is not null)
        {
            Stopwatch gxCoverageDumpStopwatch = Stopwatch.StartNew();
            int gxWidth = options.FrameWidth ?? 640;
            int gxHeight = options.FrameHeight ?? 480;
            int gxFrameMaxDraws = options.GxFrameMaxDraws ?? RunDolOptions.DefaultGxFrameMaxDraws;
            int gxFrameMaxRasterPixels = options.GxFrameMaxRasterPixels ?? RunDolOptions.DefaultGxFrameMaxRasterPixels;
            if (!GxFifoSoftwareRenderer.TryWriteDrawCoverageDiagnostics(bus.MmioAccesses, bus.Memory, options.GxCoverageDumpPath, gxWidth, gxHeight, gxFrameMaxRasterPixels, options.GxFrameIgnoreEfbCopyClear, options.GxFrameSkipDraws, gxFrameMaxDraws, out GxFifoCoverageDiagnosticResult? gxCoverage, out string? gxCoverageError))
            {
                gxCoverageDumpMilliseconds += StopAndGetMilliseconds(gxCoverageDumpStopwatch);
                _error.WriteLine($"GX coverage dump failed: {gxCoverageError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-coverage-dump: {gxCoverageError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string rasterState = gxCoverage!.RasterBudgetExhausted ? "raster budget exhausted" : "raster budget retained";
                string skipped = options.GxFrameSkipDraws == 0 ? string.Empty : $" after skipping {options.GxFrameSkipDraws} draw(s)";
                _output.WriteLine($"Wrote GX coverage diagnostics for {gxCoverage.DrawsWritten} draw(s){skipped} and {gxCoverage.CopiesSeen} copy event(s) to {gxCoverage.Path} ({rasterState}).");
            }

            gxCoverageDumpMilliseconds += StopAndGetMilliseconds(gxCoverageDumpStopwatch);
        }

        if (options.GxTevSampleDumpPath is not null)
        {
            Stopwatch gxTevSampleDumpStopwatch = Stopwatch.StartNew();
            if (!GxFifoSoftwareRenderer.TryWriteTevSampleDiagnostics(bus.MmioAccesses, bus.Memory, options.GxTevSampleDumpPath, options.GxDrawSkipDraws, options.GxDrawMaxDraws, out GxFifoTevSampleDiagnosticResult? gxTevSamples, out string? gxTevSampleError))
            {
                gxTevSampleDumpMilliseconds += StopAndGetMilliseconds(gxTevSampleDumpStopwatch);
                _error.WriteLine($"GX TEV sample dump failed: {gxTevSampleError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-tev-sample-dump: {gxTevSampleError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string skipped = options.GxDrawSkipDraws == 0 ? string.Empty : $" after skipping {options.GxDrawSkipDraws} draw(s)";
                _output.WriteLine($"Wrote {gxTevSamples!.SamplesWritten} GX TEV sample(s){skipped} after {gxTevSamples.TotalDraws} draw(s) and {gxTevSamples.CopiesSeen} copy event(s) to {gxTevSamples.Path}.");
            }

            gxTevSampleDumpMilliseconds += StopAndGetMilliseconds(gxTevSampleDumpStopwatch);
        }

        if (options.GxTextureDumpPath is not null)
        {
            Stopwatch gxTextureDumpStopwatch = Stopwatch.StartNew();
            if (!GxFifoSoftwareRenderer.TryWriteTextureDiagnostics(bus.MmioAccesses, bus.Memory, options.GxTextureDumpPath, options.GxDrawSkipDraws, options.GxDrawMaxDraws, out GxFifoTextureDiagnosticResult? gxTextures, out string? gxTextureError, memorySnapshots: gxMemorySnapshots))
            {
                gxTextureDumpMilliseconds += StopAndGetMilliseconds(gxTextureDumpStopwatch);
                _error.WriteLine($"GX texture dump failed: {gxTextureError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-texture-dump: {gxTextureError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string skipped = options.GxDrawSkipDraws == 0 ? string.Empty : $" after skipping {options.GxDrawSkipDraws} draw(s)";
                _output.WriteLine($"Wrote {gxTextures!.TexturesWritten} GX texture dump(s){skipped} after {gxTextures.TotalDraws} draw(s) and {gxTextures.CopiesSeen} copy event(s) to {gxTextures.DirectoryPath} (index {gxTextures.IndexPath}).");
            }

            gxTextureDumpMilliseconds += StopAndGetMilliseconds(gxTextureDumpStopwatch);
        }

        if (options.DumpRegisters)
        {
            Stopwatch registerDumpStopwatch = Stopwatch.StartNew();
            ConsoleFormatting.WriteRegisters(_output, state);
            registerDumpMilliseconds += StopAndGetMilliseconds(registerDumpStopwatch);
        }

        if (options.DumpMmio)
        {
            Stopwatch mmioDumpStopwatch = Stopwatch.StartNew();
            ConsoleFormatting.WriteMmioSummary(_output, bus.MmioAccesses);
            mmioDumpMilliseconds += StopAndGetMilliseconds(mmioDumpStopwatch);
        }

        if (options.DumpThreads)
        {
            Stopwatch threadDumpStopwatch = Stopwatch.StartNew();
            ConsoleFormatting.WriteThreadSummary(_output, bus.Memory);
            threadDumpMilliseconds += StopAndGetMilliseconds(threadDumpStopwatch);
        }

        if (options.DumpMessageQueues)
        {
            Stopwatch messageQueueDumpStopwatch = Stopwatch.StartNew();
            ConsoleFormatting.WriteMessageQueueSummary(_output, bus.Memory);
            messageQueueDumpMilliseconds += StopAndGetMilliseconds(messageQueueDumpStopwatch);
        }

        if (options.PcProfileTop is int pcProfileTop && pcProfile is not null)
        {
            Stopwatch pcProfileStopwatch = Stopwatch.StartNew();
            ConsoleFormatting.WritePcProfile(_output, pcProfile, pcProfileTop, executed, options.ProfileAfter.GetValueOrDefault());
            pcProfileMilliseconds += StopAndGetMilliseconds(pcProfileStopwatch);
        }

        if (options.IndirectCallSiteProfileAddress is uint profiledCallSite
            && options.IndirectCallSiteProfileTop is int indirectCallSiteProfileTop
            && indirectCallSiteProfile is not null)
        {
            Stopwatch indirectCallProfileStopwatch = Stopwatch.StartNew();
            WriteIndirectCallSiteProfile(_output, profiledCallSite, indirectCallSiteProfile, indirectCallSiteProfileTop);
            indirectCallProfileMilliseconds += StopAndGetMilliseconds(indirectCallProfileStopwatch);
        }

        if (branchSiteProfiles is not null)
        {
            foreach (BranchSiteProfileRequest request in options.BranchSiteProfiles ?? [])
            {
                if (branchSiteProfiles.TryGetValue(request.Address, out Dictionary<uint, ulong>? profile))
                {
                    WriteBranchSiteProfile(_output, request.Address, profile, request.TopCount);
                }
            }
        }

        if (pcLrProfiles is not null)
        {
            foreach (PcLrProfileRequest request in options.PcLrProfiles ?? [])
            {
                if (pcLrProfiles.TryGetValue(request.Address, out Dictionary<uint, ulong>? profile))
                {
                    WritePcLrProfile(_output, request.Address, profile, request.TopCount);
                }
            }
        }

        Stopwatch finalMemoryDumpStopwatch = Stopwatch.StartNew();
        WriteRequestedDisassemblyDumps(options, bus, _output);
        WriteRequestedMemoryDumps(options, bus, _output);
        memoryDumpMilliseconds += StopAndGetMilliseconds(finalMemoryDumpStopwatch);

        WriteRunSummary(0);
        return 0;
    }

    private static TextWriter? OpenTraceFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

            return new StreamWriter(File.Create(fullPath));
    }

    private static double StopAndGetMilliseconds(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static double RoundMilliseconds(double milliseconds)
    {
        return Math.Round(milliseconds, 3);
    }

    private static object BuildRegisterSummary(PowerPcState state) => new
    {
        lr = $"0x{state.Lr:X8}",
        ctr = $"0x{state.Ctr:X8}",
        cr = $"0x{state.Cr:X8}",
        xer = $"0x{state.Xer:X8}",
        fpscr = $"0x{state.Fpscr:X8}",
        msr = $"0x{state.Msr:X8}",
        dec = $"0x{state.Spr[22]:X8}",
        srr0 = $"0x{state.Spr[26]:X8}",
        srr1 = $"0x{state.Spr[27]:X8}",
        r0 = $"0x{state.Gpr[0]:X8}",
        r1 = $"0x{state.Gpr[1]:X8}",
        r2 = $"0x{state.Gpr[2]:X8}",
        r3 = $"0x{state.Gpr[3]:X8}",
        r4 = $"0x{state.Gpr[4]:X8}",
        r5 = $"0x{state.Gpr[5]:X8}",
        r6 = $"0x{state.Gpr[6]:X8}",
        r10 = $"0x{state.Gpr[10]:X8}",
        r13 = $"0x{state.Gpr[13]:X8}",
        r31 = $"0x{state.Gpr[31]:X8}",
        gqr0 = $"0x{state.Spr[912]:X8}",
        gqr1 = $"0x{state.Spr[913]:X8}",
        gqr2 = $"0x{state.Spr[914]:X8}",
        gqr3 = $"0x{state.Spr[915]:X8}",
        gqr4 = $"0x{state.Spr[916]:X8}",
        gqr5 = $"0x{state.Spr[917]:X8}",
        gqr6 = $"0x{state.Spr[918]:X8}",
        gqr7 = $"0x{state.Spr[919]:X8}",
        gpr = state.Gpr.Select(value => $"0x{value:X8}").ToArray(),
        fpr = Enumerable.Range(0, state.Fpr.Length)
            .Select(index => new
            {
                index,
                value = FormatDouble(state.Fpr[index]),
                pair1 = FormatDouble(state.FprPair1[index]),
                bits = $"0x{unchecked((ulong)BitConverter.DoubleToInt64Bits(state.Fpr[index])):X16}",
                pair1Bits = $"0x{unchecked((ulong)BitConverter.DoubleToInt64Bits(state.FprPair1[index])):X16}",
            })
            .ToArray(),
    };

    private static string FormatDouble(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private int WriteGxFrameSweep(GameCubeBus bus, RunDolOptions options, GxMemorySnapshotSet? gxMemorySnapshots)
    {
        GxFrameSweepOptions sweep = options.GxFrameSweep!;
        int gxWidth = options.FrameWidth ?? 640;
        int gxHeight = options.FrameHeight ?? 480;
        int gxFrameMaxDraws = options.GxFrameMaxDraws ?? RunDolOptions.DefaultGxFrameMaxDraws;
        int gxFrameMaxRasterPixels = options.GxFrameMaxRasterPixels ?? RunDolOptions.DefaultGxFrameMaxRasterPixels;
        string outputDirectory = Path.GetFullPath(sweep.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        string summaryPath = Path.Combine(outputDirectory, "gx-frame-sweep.csv");
        int successes = 0;
        using StreamWriter summary = new(File.Create(summaryPath));
        summary.AutoFlush = true;
        summary.WriteLine("skip,path,source,source_address,source_format,parsed_draws,rendered_quads,rendered_triangles,degenerate_quads,degenerate_triangles,error");

        for (int index = 0; index < sweep.Count; index++)
        {
            long skipLong = (long)sweep.StartSkipDraws + (long)index * sweep.StepDraws;
            if (skipLong > int.MaxValue)
            {
                summary.WriteLine($"{skipLong},,,,,,,,,,skip draw count exceeds Int32.MaxValue");
                continue;
            }

            int skipDraws = (int)skipLong;
            string framePath = Path.Combine(outputDirectory, $"gx-frame-skip-{skipDraws:D6}.png");
            bool rendered = GxFifoSoftwareRenderer.TryRender(bus.MmioAccesses, bus.Memory, framePath, gxWidth, gxHeight, gxFrameMaxDraws, skipDraws, stopAfterMaxDraws: true, maxRasterizedPixels: gxFrameMaxRasterPixels, ignoreEfbCopyClear: options.GxFrameIgnoreEfbCopyClear, source: options.GxFrameSource, displayCopyIndex: options.GxFrameCopyIndex, out GxFifoSoftwareRenderResult? frame, out string? error, memorySnapshots: gxMemorySnapshots);
            if (!rendered)
            {
                summary.WriteLine($"{skipDraws},,,,,,,,,,\"{EscapeCsv(error ?? "unknown GX frame dump error")}\"");
                continue;
            }

            successes++;
            string sourceAddress = frame!.SourceAddress is uint address ? $"0x{address:X8}" : string.Empty;
            string sourceFormat = frame.SourceFormat?.ToString() ?? string.Empty;
            summary.WriteLine($"{skipDraws},\"{EscapeCsv(frame.Path)}\",{frame.Source},{sourceAddress},{sourceFormat},{frame.Draws},{frame.RenderedQuads},{frame.RenderedTriangles},{frame.DegenerateQuads},{frame.DegenerateTriangles},");
        }

        if (successes == 0)
        {
            _error.WriteLine($"GX frame sweep failed: no frame slices were written. See {summaryPath}.");
            return 3;
        }

        if (!options.Quiet)
        {
            _output.WriteLine($"Wrote {successes} GX frame sweep slice(s) to {outputDirectory} (start skip {sweep.StartSkipDraws}, step {sweep.StepDraws}, count {sweep.Count}, draw cap {gxFrameMaxDraws}, raster budget {gxFrameMaxRasterPixels}).");
            _output.WriteLine($"GX frame sweep summary: {summaryPath}");
        }

        return 0;
    }

    private static string EscapeCsv(string text) => text.Replace("\"", "\"\"", StringComparison.Ordinal);

    private static uint[] BuildWatchAddresses(RunDolOptions options)
    {
        if (options.WatchAddresses is { Count: > 0 })
        {
            return options.WatchAddresses.Distinct().ToArray();
        }

        return options.WatchAddress is uint watchedAddress ? [watchedAddress] : [];
    }

    private static void UpdateControllerButtons(GameCubeBus bus, RunDolOptions options, int executedInstructions)
    {
        ushort buttons = options.ControllerButtons;
        if (options.ControllerButtonWindows is { Count: > 0 })
        {
            foreach (ControllerButtonWindow window in options.ControllerButtonWindows)
            {
                if (executedInstructions >= window.StartInstruction && executedInstructions <= window.EndInstruction)
                {
                    buttons |= window.Buttons;
                }
            }
        }

        bus.SerialInterfaceControllerButtons = buttons;
    }

    private static void WriteTraceTail(Queue<string>? traceTail, TextWriter output)
    {
        if (traceTail is not { Count: > 0 })
        {
            return;
        }

        output.WriteLine($"Last {traceTail.Count} instruction(s):");
        foreach (string line in traceTail)
        {
            output.WriteLine(line);
        }
    }

    private static void WriteRequestedMemoryFinds(RunDolOptions options, GameCubeBus bus, TextWriter output)
    {
        if (options.FindMemoryWords is not { Count: > 0 })
        {
            return;
        }

        int maxMatches = options.WatchLimit.GetValueOrDefault(128);
        ReadOnlySpan<byte> ram = bus.Memory.MainRam.Span;
        foreach (uint word in options.FindMemoryWords.Distinct())
        {
            int matches = 0;
            bool omitted = false;
            output.WriteLine($"Memory word search 0x{word:X8}:");
            for (int offset = 0; offset <= ram.Length - sizeof(uint); offset += sizeof(uint))
            {
                if (BigEndian.ReadUInt32(ram.Slice(offset, sizeof(uint))) != word)
                {
                    continue;
                }

                if (matches < maxMatches)
                {
                    output.WriteLine($"0x{GameCubeAddress.MainRamCachedStart + (uint)offset:X8}");
                }
                else
                {
                    omitted = true;
                }

                matches++;
            }

            if (matches == 0)
            {
                output.WriteLine("  no matches");
            }
            else if (omitted)
            {
                output.WriteLine($"  ... omitted {matches - maxMatches} additional match(es)");
            }

            output.WriteLine($"Found {matches} matching word(s).");
        }
    }

    private static void WriteRequestedMemoryDumps(RunDolOptions options, GameCubeBus bus, TextWriter output)
    {
        if (options.DumpMemoryRequests is { Count: > 0 })
        {
            foreach (MemoryDumpRequest request in options.DumpMemoryRequests.Distinct())
            {
                ConsoleFormatting.WriteMemoryDump(output, bus.Memory, request.Address, request.Length);
            }

            return;
        }

        if (options.DumpMemoryAddress is uint dumpMemoryAddress && options.DumpMemoryLength is int dumpMemoryLength)
        {
            ConsoleFormatting.WriteMemoryDump(output, bus.Memory, dumpMemoryAddress, dumpMemoryLength);
        }
    }

    private static void WriteRequestedDisassemblyDumps(RunDolOptions options, GameCubeBus bus, TextWriter output)
    {
        if (options.DumpDisassemblyRequests is not { Count: > 0 })
        {
            return;
        }

        foreach (DisassemblyDumpRequest request in options.DumpDisassemblyRequests.Distinct())
        {
            ConsoleFormatting.WriteDisassemblyDump(output, bus.Memory, request);
        }
    }

    private static void WriteRequestedPointerTableDumps(RunDolOptions options, GameCubeBus bus, TextWriter output)
    {
        if (options.PointerTableDumpRequests is not { Count: > 0 })
        {
            return;
        }

        foreach (PointerTableDumpRequest request in options.PointerTableDumpRequests.Distinct())
        {
            ConsoleFormatting.WritePointerTableDump(output, bus.Memory, request);
        }
    }

    private static bool TryFastForwardKnownIdleLoop(PowerPcState state, GameCubeBus bus, out ulong skippedCycles)
    {
        const uint idlePollPc = 0x801F_BEE8;
        const uint msrExternalInterruptEnable = 0x0000_8000;
        const int cyclesPerIdleSkip = GameCubeBus.VideoCyclesPerScanline;

        skippedCycles = 0;
        if (state.Pc != idlePollPc
            || (state.Msr & msrExternalInterruptEnable) == 0
            || bus.HasPendingExternalInterrupt
            || bus.SmallDataBaseRegister == 0
            || bus.Read32(bus.SmallDataBaseRegister + 0x3230) != 0
            || !HasEnabledVideoInterrupt(bus))
        {
            return false;
        }

        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) == 0 && decrementer == 0)
        {
            return false;
        }

        int cyclesToSkip = cyclesPerIdleSkip;
        if ((decrementer & 0x8000_0000) == 0 && decrementer < cyclesToSkip)
        {
            cyclesToSkip = (int)decrementer;
        }

        state.TimeBase += (uint)cyclesToSkip;
        state.Spr[22] = unchecked(decrementer - (uint)cyclesToSkip);
        bus.Advance((uint)cyclesToSkip);
        skippedCycles = (uint)cyclesToSkip;
        return true;
    }

    private static bool TryFastForwardPikminHeapWaitLoop(PowerPcState state, GameCubeBus bus, out ulong skippedCycles)
    {
        const uint waitPc = 0x8004_66C0;
        const uint msrExternalInterruptEnable = 0x0000_8000;
        const int cyclesPerIdleSkip = GameCubeBus.VideoCyclesPerScanline;

        skippedCycles = 0;
        if (state.Pc != waitPc
            || (state.Msr & msrExternalInterruptEnable) == 0
            || bus.HasPendingExternalInterrupt
            || bus.Read32(waitPc) != 0x8003_032C
            || bus.Read32(waitPc + 4) != 0x2800_0000
            || bus.Read32(waitPc + 8) != 0x4182_FFF8
            || state.Gpr[3] == 0
            || !bus.Memory.IsMainRamAddress(state.Gpr[3], 0x330)
            || bus.Read32(state.Gpr[3] + 0x32C) != 0
            || !HasEnabledVideoInterrupt(bus))
        {
            return false;
        }

        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) == 0 && decrementer == 0)
        {
            return false;
        }

        int cyclesToSkip = cyclesPerIdleSkip;
        if ((decrementer & 0x8000_0000) == 0 && decrementer < cyclesToSkip)
        {
            cyclesToSkip = (int)decrementer;
        }

        state.TimeBase += (uint)cyclesToSkip;
        state.Spr[22] = unchecked(decrementer - (uint)cyclesToSkip);
        bus.Advance((uint)cyclesToSkip);
        skippedCycles = (uint)cyclesToSkip;
        return true;
    }

    private static bool TryFastForwardCtrDelayLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (bus.Read32(pc) != 0x4200_0000 || state.Ctr == 0)
        {
            return false;
        }

        uint iterationsToSkip = Math.Min(state.Ctr, int.MaxValue);
        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) == 0)
        {
            if (decrementer == 0)
            {
                return false;
            }

            iterationsToSkip = Math.Min(iterationsToSkip, decrementer);
        }

        state.Ctr -= iterationsToSkip;
        state.Pc = state.Ctr == 0 ? pc + sizeof(uint) : pc;
        AdvanceFastForwardedInstructions(state, bus, iterationsToSkip);
        skippedInstructions = checked((int)iterationsToSkip);
        return true;
    }

    private static bool TryFastForwardZeroStoreLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint first = bus.Read32(pc);
        if (!TryDecodeDForm(first, primaryOpcode: 36, out int valueRegister, out int baseRegister, out int firstOffset)
            || firstOffset != 4
            || state.Gpr[valueRegister] != 0)
        {
            return false;
        }

        uint addic = bus.Read32(pc + 4);
        if (!TryDecodeDForm(addic, primaryOpcode: 13, out int counterRegister, out int counterSourceRegister, out int addImmediate)
            || counterRegister != counterSourceRegister
            || addImmediate != -1)
        {
            return false;
        }

        for (int offset = 8; offset <= 28; offset += 4)
        {
            uint store = bus.Read32(pc + (uint)offset);
            if (!TryDecodeDForm(store, primaryOpcode: 36, out int nextValueRegister, out int nextBaseRegister, out int storeOffset)
                || nextValueRegister != valueRegister
                || nextBaseRegister != baseRegister
                || storeOffset != offset)
            {
                return false;
            }
        }

        uint storeUpdate = bus.Read32(pc + 32);
        if (!TryDecodeDForm(storeUpdate, primaryOpcode: 37, out int updateValueRegister, out int updateBaseRegister, out int updateOffset)
            || updateValueRegister != valueRegister
            || updateBaseRegister != baseRegister
            || updateOffset != 32)
        {
            return false;
        }

        uint branch = bus.Read32(pc + 36);
        if (branch != 0x4082_FFDC)
        {
            return false;
        }

        uint iterations = state.Gpr[counterRegister];
        if (iterations == 0)
        {
            return false;
        }

        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) == 0)
        {
            return false;
        }

        if (iterations == 0 || iterations > int.MaxValue / 10)
        {
            return false;
        }

        uint startAddress = state.Gpr[baseRegister] + 4;
        ulong clearLength = (ulong)iterations * 32;
        if (clearLength > int.MaxValue
            || !bus.Memory.IsMainRamAddress(startAddress, checked((int)clearLength)))
        {
            return false;
        }

        bus.Memory.Clear(startAddress, (uint)clearLength);
        state.Gpr[counterRegister] = 0;
        state.Gpr[baseRegister] = unchecked(state.Gpr[baseRegister] + (uint)clearLength);
        SetCarry(state, carry: true);
        SetCr0(state, 0);
        state.Pc = pc + 40;

        uint skipped = checked(iterations * 10);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardWordFillLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        return TryFastForwardWordFillLoopCore(state, bus, out skippedInstructions, out _);
    }

    private static bool TryFastForwardWordFillLoopCore(PowerPcState state, GameCubeBus bus, out int skippedInstructions, out bool reverseWordFill)
    {
        skippedInstructions = 0;
        reverseWordFill = false;
        uint pc = state.Pc;
        if (TryFastForwardReverseWordFillLoop(state, bus, pc, out skippedInstructions))
        {
            reverseWordFill = true;
            return true;
        }

        uint first = bus.Read32(pc);
        if (!TryDecodeDForm(first, primaryOpcode: 36, out int valueRegister, out int baseRegister, out int firstOffset)
            || firstOffset != 4)
        {
            return false;
        }

        uint addic = bus.Read32(pc + 4);
        if (!TryDecodeDForm(addic, primaryOpcode: 13, out int counterRegister, out int counterSourceRegister, out int addImmediate)
            || counterRegister != 0
            || counterSourceRegister != 0
            || addImmediate != -1)
        {
            return false;
        }

        for (int offset = 8; offset <= 28; offset += 4)
        {
            uint store = bus.Read32(pc + (uint)offset);
            if (!TryDecodeDForm(store, primaryOpcode: 36, out int nextValueRegister, out int nextBaseRegister, out int storeOffset)
                || nextValueRegister != valueRegister
                || nextBaseRegister != baseRegister
                || storeOffset != offset)
            {
                return false;
            }
        }

        uint storeUpdate = bus.Read32(pc + 32);
        if (!TryDecodeDForm(storeUpdate, primaryOpcode: 37, out int updateValueRegister, out int updateBaseRegister, out int updateOffset)
            || updateValueRegister != valueRegister
            || updateBaseRegister != baseRegister
            || updateOffset != 32)
        {
            return false;
        }

        if (bus.Read32(pc + 36) != 0x4082_FFDC)
        {
            return false;
        }

        uint iterations = state.Gpr[0];
        if (iterations == 0 || iterations > MaxFastForwardMemmoveBytes / 32)
        {
            return false;
        }

        uint iterationsToFill = iterations;
        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) == 0)
        {
            uint iterationsBeforeInterruptEdge = decrementer / 10;
            if (iterationsBeforeInterruptEdge == 0)
            {
                return false;
            }

            iterationsToFill = Math.Min(iterationsToFill, iterationsBeforeInterruptEdge);
        }

        uint startAddress = unchecked(state.Gpr[baseRegister] + 4);
        uint byteCount = checked(iterationsToFill * 32);
        if (!bus.Memory.IsMainRamAddress(startAddress, checked((int)byteCount)))
        {
            return false;
        }

        uint value = state.Gpr[valueRegister];
        for (uint offset = 0; offset < byteCount; offset += sizeof(uint))
        {
            bus.Memory.Write32(startAddress + offset, value);
        }

        uint remainingIterations = iterations - iterationsToFill;
        state.Gpr[0] = remainingIterations;
        state.Gpr[baseRegister] = unchecked(state.Gpr[baseRegister] + byteCount);
        SetCarry(state, carry: true);
        SetCr0(state, remainingIterations);
        state.Pc = remainingIterations == 0 ? pc + 40 : pc;

        uint skipped = checked(iterationsToFill * 10);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardReverseWordFillLoop(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesReverseWordFillLoop(bus, pc) || state.Gpr[0] == 0)
        {
            return false;
        }

        uint iterations = state.Gpr[0];
        if (iterations > MaxFastForwardMemmoveBytes / 64)
        {
            return false;
        }

        uint iterationsToFill = iterations;
        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) == 0)
        {
            uint iterationsBeforeInterruptEdge = decrementer / 18;
            if (iterationsBeforeInterruptEdge == 0)
            {
                return false;
            }

            iterationsToFill = Math.Min(iterationsToFill, iterationsBeforeInterruptEdge);
        }

        uint byteCount = checked(iterationsToFill * 64);
        uint startAddress = unchecked(state.Gpr[7] - byteCount);
        if (!bus.Memory.IsMainRamAddress(startAddress, checked((int)byteCount)))
        {
            return false;
        }

        uint value = state.Gpr[4];
        for (uint offset = 0; offset < byteCount; offset += sizeof(uint))
        {
            bus.Memory.Write32(startAddress + offset, value);
        }

        uint remainingIterations = iterations - iterationsToFill;
        state.Gpr[0] = remainingIterations;
        state.Gpr[7] = startAddress;
        SetCarry(state, carry: true);
        SetCr0(state, remainingIterations);
        state.Pc = remainingIterations == 0 ? pc + 0x48 : pc;

        uint skipped = checked(iterationsToFill * 18);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesReverseWordFillLoop(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x9087_FFFC
        && bus.Read32(pc + 0x04) == 0x9087_FFF8
        && bus.Read32(pc + 0x08) == 0x9087_FFF4
        && bus.Read32(pc + 0x0C) == 0x9087_FFF0
        && bus.Read32(pc + 0x10) == 0x9087_FFEC
        && bus.Read32(pc + 0x14) == 0x9087_FFE8
        && bus.Read32(pc + 0x18) == 0x9087_FFE4
        && bus.Read32(pc + 0x1C) == 0x9087_FFE0
        && bus.Read32(pc + 0x20) == 0x9087_FFDC
        && bus.Read32(pc + 0x24) == 0x9087_FFD8
        && bus.Read32(pc + 0x28) == 0x9087_FFD4
        && bus.Read32(pc + 0x2C) == 0x9087_FFD0
        && bus.Read32(pc + 0x30) == 0x9087_FFCC
        && bus.Read32(pc + 0x34) == 0x9087_FFC8
        && bus.Read32(pc + 0x38) == 0x9087_FFC4
        && bus.Read32(pc + 0x3C) == 0x9487_FFC0
        && bus.Read32(pc + 0x40) == 0x3400_FFFF
        && bus.Read32(pc + 0x44) == 0x4082_FFBC;

    private static bool TryFastForwardCtrZeroStoreLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint first = bus.Read32(pc);
        if (!TryDecodeDForm(first, primaryOpcode: 36, out int valueRegister, out int baseRegister, out int firstOffset)
            || firstOffset != 0
            || state.Gpr[valueRegister] != 0)
        {
            return false;
        }

        uint secondaryAdd = bus.Read32(pc + 4);
        if (!TryDecodeDForm(secondaryAdd, primaryOpcode: 14, out int secondaryRegister, out int secondarySourceRegister, out int secondaryImmediate)
            || secondaryRegister != secondarySourceRegister
            || secondaryImmediate != 8)
        {
            return false;
        }

        for (int instructionOffset = 8, storeOffset = 4; instructionOffset <= 32; instructionOffset += 4, storeOffset += 4)
        {
            uint store = bus.Read32(pc + (uint)instructionOffset);
            if (!TryDecodeDForm(store, primaryOpcode: 36, out int nextValueRegister, out int nextBaseRegister, out int nextStoreOffset)
                || nextValueRegister != valueRegister
                || nextBaseRegister != baseRegister
                || nextStoreOffset != storeOffset)
            {
                return false;
            }
        }

        uint baseAdd = bus.Read32(pc + 36);
        if (!TryDecodeDForm(baseAdd, primaryOpcode: 14, out int baseAddRegister, out int baseAddSourceRegister, out int baseImmediate)
            || baseAddRegister != baseRegister
            || baseAddSourceRegister != baseRegister
            || baseImmediate != 32)
        {
            return false;
        }

        uint branch = bus.Read32(pc + 40);
        if (branch != 0x4200_FFD8)
        {
            return false;
        }

        uint iterations = state.Ctr;
        if (iterations == 0 || iterations > int.MaxValue / 11)
        {
            return false;
        }

        uint iterationsToClear = iterations;
        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) == 0)
        {
            uint iterationsBeforeInterruptEdge = decrementer / 11;
            if (iterationsBeforeInterruptEdge == 0)
            {
                return false;
            }

            iterationsToClear = Math.Min(iterationsToClear, iterationsBeforeInterruptEdge);
        }

        ulong clearLength = (ulong)iterationsToClear * 32;
        uint startAddress = state.Gpr[baseRegister];
        if (clearLength > int.MaxValue
            || !bus.Memory.IsMainRamAddress(startAddress, checked((int)clearLength)))
        {
            return false;
        }

        bus.Memory.Clear(startAddress, (uint)clearLength);
        uint remainingIterations = iterations - iterationsToClear;
        state.Ctr = remainingIterations;
        state.Gpr[baseRegister] = unchecked(state.Gpr[baseRegister] + (uint)clearLength);
        state.Gpr[secondaryRegister] = unchecked(state.Gpr[secondaryRegister] + iterationsToClear * 8);
        state.Pc = remainingIterations == 0 ? pc + 44 : pc;

        uint skipped = checked(iterationsToClear * 11);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardByteCopyLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint loadUpdate = bus.Read32(pc);
        if (!TryDecodeDForm(loadUpdate, primaryOpcode: 35, out int valueRegister, out int sourceRegister, out int sourceOffset)
            || sourceOffset != 1)
        {
            return false;
        }

        uint storeUpdate = bus.Read32(pc + 4);
        if (!TryDecodeDForm(storeUpdate, primaryOpcode: 39, out int storeValueRegister, out int destinationRegister, out int destinationOffset)
            || storeValueRegister != valueRegister
            || destinationOffset != 1)
        {
            return false;
        }

        uint addic = bus.Read32(pc + 8);
        if (!TryDecodeDForm(addic, primaryOpcode: 13, out int counterRegister, out int counterSourceRegister, out int addImmediate)
            || counterRegister != counterSourceRegister
            || addImmediate != -1)
        {
            return false;
        }

        uint branch = bus.Read32(pc + 12);
        if (branch != 0x4082_FFF4)
        {
            return false;
        }

        uint iterations = state.Gpr[counterRegister];
        if (iterations == 0 || iterations > int.MaxValue / 4)
        {
            return false;
        }

        uint iterationsToCopy = iterations;
        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) == 0)
        {
            uint iterationsBeforeInterruptEdge = decrementer / 4;
            if (iterationsBeforeInterruptEdge == 0)
            {
                return false;
            }

            iterationsToCopy = Math.Min(iterationsToCopy, iterationsBeforeInterruptEdge);
        }

        uint sourceStart = unchecked(state.Gpr[sourceRegister] + 1);
        uint destinationStart = unchecked(state.Gpr[destinationRegister] + 1);
        if (!bus.Memory.IsMainRamAddress(sourceStart, checked((int)iterationsToCopy))
            || !bus.Memory.IsMainRamAddress(destinationStart, checked((int)iterationsToCopy)))
        {
            return false;
        }

        byte lastValue = 0;
        for (uint offset = 0; offset < iterationsToCopy; offset++)
        {
            lastValue = bus.Memory.Read8(sourceStart + offset);
            bus.Memory.Write8(destinationStart + offset, lastValue);
        }

        state.Gpr[valueRegister] = lastValue;
        state.Gpr[sourceRegister] = unchecked(state.Gpr[sourceRegister] + iterationsToCopy);
        state.Gpr[destinationRegister] = unchecked(state.Gpr[destinationRegister] + iterationsToCopy);
        uint remainingIterations = iterations - iterationsToCopy;
        state.Gpr[counterRegister] = remainingIterations;
        SetCarry(state, carry: true);
        SetCr0(state, remainingIterations);
        state.Pc = remainingIterations == 0 ? pc + 16 : pc;

        uint skipped = checked(iterationsToCopy * 4);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardCtrByteCopyLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesCtrByteCopyLoop(bus, pc) || state.Ctr == 0)
        {
            return false;
        }

        uint iterations = state.Ctr;
        if (iterations > uint.MaxValue / 19)
        {
            return false;
        }

        uint byteCount = checked(iterations * 8);
        uint skipped = checked(iterations * 19);
        if (byteCount > MaxFastForwardMemmoveBytes
            || skipped > int.MaxValue
            || !CanFastForwardInstructionCount(state, iterations, 19, extraInstructions: 0)
            || !bus.Memory.IsMainRamAddress(state.Gpr[5], checked((int)byteCount))
            || !bus.Memory.IsMainRamAddress(state.Gpr[7], checked((int)byteCount)))
        {
            return false;
        }

        byte lastValue = 0;
        for (uint offset = 0; offset < byteCount; offset++)
        {
            lastValue = bus.Memory.Read8(state.Gpr[5] + offset);
            bus.Memory.Write8(state.Gpr[7] + offset, lastValue);
        }

        state.Gpr[0] = lastValue;
        state.Gpr[5] = unchecked(state.Gpr[5] + byteCount);
        state.Gpr[7] = unchecked(state.Gpr[7] + byteCount);
        state.Ctr = 0;
        state.Pc = pc + 0x4C;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesCtrByteCopyLoop(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x8805_0000
        && bus.Read32(pc + 0x04) == 0x9807_0000
        && bus.Read32(pc + 0x08) == 0x8805_0001
        && bus.Read32(pc + 0x0C) == 0x9807_0001
        && bus.Read32(pc + 0x10) == 0x8805_0002
        && bus.Read32(pc + 0x14) == 0x9807_0002
        && bus.Read32(pc + 0x18) == 0x8805_0003
        && bus.Read32(pc + 0x1C) == 0x9807_0003
        && bus.Read32(pc + 0x20) == 0x8805_0004
        && bus.Read32(pc + 0x24) == 0x9807_0004
        && bus.Read32(pc + 0x28) == 0x8805_0005
        && bus.Read32(pc + 0x2C) == 0x9807_0005
        && bus.Read32(pc + 0x30) == 0x8805_0006
        && bus.Read32(pc + 0x34) == 0x9807_0006
        && bus.Read32(pc + 0x38) == 0x8805_0007
        && bus.Read32(pc + 0x3C) == 0x38A5_0008
        && bus.Read32(pc + 0x40) == 0x9807_0007
        && bus.Read32(pc + 0x44) == 0x38E7_0008
        && bus.Read32(pc + 0x48) == 0x4200_FFB8;

    private static bool TryFastForwardCtrSingleByteCopyLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesCtrSingleByteCopyLoop(bus, pc) || state.Ctr == 0)
        {
            return false;
        }

        uint count = state.Ctr;
        uint skipped = checked(count * 5);
        if (count > MaxFastForwardMemmoveBytes
            || skipped > int.MaxValue
            || !CanFastForwardInstructionCount(state, count, 5, extraInstructions: 0)
            || !bus.Memory.IsMainRamAddress(state.Gpr[5], checked((int)count))
            || !bus.Memory.IsMainRamAddress(state.Gpr[7], checked((int)count)))
        {
            return false;
        }

        byte lastValue = 0;
        for (uint offset = 0; offset < count; offset++)
        {
            lastValue = bus.Memory.Read8(state.Gpr[5] + offset);
            bus.Memory.Write8(state.Gpr[7] + offset, lastValue);
        }

        state.Gpr[0] = lastValue;
        state.Gpr[5] = unchecked(state.Gpr[5] + count);
        state.Gpr[7] = unchecked(state.Gpr[7] + count);
        state.Ctr = 0;
        state.Pc = pc + 0x14;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesCtrSingleByteCopyLoop(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x8805_0000
        && bus.Read32(pc + 0x04) == 0x38A5_0001
        && bus.Read32(pc + 0x08) == 0x9807_0000
        && bus.Read32(pc + 0x0C) == 0x38E7_0001
        && bus.Read32(pc + 0x10) == 0x4200_FFF0;

    private static bool TryFastForwardWordCopyLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesWordCopyLoop(bus, pc) || state.Gpr[4] == 0)
        {
            return false;
        }

        uint iterations = state.Gpr[4];
        if (iterations > uint.MaxValue / 32 || iterations > uint.MaxValue / 18)
        {
            return false;
        }

        uint byteCount = checked(iterations * 32);
        uint skipped = checked(iterations * 18);
        uint sourceStart = unchecked(state.Gpr[6] + 4);
        uint destinationStart = unchecked(state.Gpr[3] + 4);
        if (byteCount > MaxFastForwardMemmoveBytes
            || skipped > int.MaxValue
            || !CanFastForwardInstructionCount(state, iterations, 18, extraInstructions: 0)
            || !bus.Memory.IsMainRamAddress(sourceStart, checked((int)byteCount))
            || !bus.Memory.IsMainRamAddress(destinationStart, checked((int)byteCount)))
        {
            return false;
        }

        uint lastWord = 0;
        for (uint offset = 0; offset < byteCount; offset += sizeof(uint))
        {
            lastWord = bus.Memory.Read32(sourceStart + offset);
            bus.Memory.Write32(destinationStart + offset, lastWord);
        }

        state.Gpr[0] = lastWord;
        state.Gpr[3] = unchecked(state.Gpr[3] + byteCount);
        state.Gpr[4] = 0;
        state.Gpr[6] = unchecked(state.Gpr[6] + byteCount);
        state.Pc = pc + 0x4C;
        SetCr0(state, 0);
        SetCarry(state, carry: true);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesWordCopyLoop(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x8006_0004
        && bus.Read32(pc + 0x04) == 0x3484_FFFF
        && bus.Read32(pc + 0x08) == 0x9003_0004
        && bus.Read32(pc + 0x0C) == 0x8006_0008
        && bus.Read32(pc + 0x10) == 0x9003_0008
        && bus.Read32(pc + 0x14) == 0x8006_000C
        && bus.Read32(pc + 0x18) == 0x9003_000C
        && bus.Read32(pc + 0x1C) == 0x8006_0010
        && bus.Read32(pc + 0x20) == 0x9003_0010
        && bus.Read32(pc + 0x24) == 0x8006_0014
        && bus.Read32(pc + 0x28) == 0x9003_0014
        && bus.Read32(pc + 0x2C) == 0x8006_0018
        && bus.Read32(pc + 0x30) == 0x9003_0018
        && bus.Read32(pc + 0x34) == 0x8006_001C
        && bus.Read32(pc + 0x38) == 0x9003_001C
        && bus.Read32(pc + 0x3C) == 0x8406_0020
        && bus.Read32(pc + 0x40) == 0x9403_0020
        && bus.Read32(pc + 0x44) == 0x4082_FFBC;

    private static bool TryFastForwardCtrCacheBlockLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint cacheInstruction = bus.Read32(pc);
        if (!IsNoopCacheBlockInstruction(cacheInstruction, out _, out int cacheIndexRegister))
        {
            return false;
        }

        uint addi = bus.Read32(pc + 4);
        if (!TryDecodeDForm(addi, primaryOpcode: 14, out int updateRegister, out int updateSourceRegister, out int updateImmediate)
            || updateRegister != updateSourceRegister
            || updateRegister != cacheIndexRegister
            || updateImmediate <= 0)
        {
            return false;
        }

        uint branch = bus.Read32(pc + 8);
        if (branch != 0x4200_FFF8)
        {
            return false;
        }

        uint iterations = state.Ctr;
        if (iterations == 0 || iterations > int.MaxValue / 3)
        {
            return false;
        }

        uint iterationsToSkip = iterations;
        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) == 0)
        {
            uint iterationsBeforeInterruptEdge = decrementer / 3;
            if (iterationsBeforeInterruptEdge == 0)
            {
                return false;
            }

            iterationsToSkip = Math.Min(iterationsToSkip, iterationsBeforeInterruptEdge);
        }

        state.Ctr = iterations - iterationsToSkip;
        state.Gpr[updateRegister] = unchecked(state.Gpr[updateRegister] + iterationsToSkip * (uint)updateImmediate);
        state.Pc = state.Ctr == 0 ? pc + 12 : pc;

        uint skipped = checked(iterationsToSkip * 3);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool IsNoopCacheBlockInstruction(uint instruction, out int rA, out int rB)
    {
        rA = 0;
        rB = 0;
        if ((instruction >> 26) != 31)
        {
            return false;
        }

        int xo = (int)((instruction >> 1) & 0x3FF);
        if (xo is not (54 or 86 or 470 or 982))
        {
            return false;
        }

        rA = (int)((instruction >> 16) & 0x1F);
        rB = (int)((instruction >> 11) & 0x1F);
        return true;
    }

    private static bool TryFastForwardSmallLeafHelper(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint first = bus.Read32(pc);
        if (first == 0x7C80_0734)
        {
            return TryFastForwardFourShortStoreLeaf(state, bus, pc, out skippedInstructions);
        }

        if (first == 0x9421_FFF0)
        {
            return TryFastForwardLongDivisionLeaf(state, bus, out skippedInstructions);
        }

        if (first == 0x9421_FF90)
        {
            return TryFastForwardVariadicRegisterSaveStub(state, bus, pc, out skippedInstructions);
        }

        if (first == 0x3800_0000)
        {
            if (TryFastForwardZeroThreeWordsLeaf(state, bus, pc, out skippedInstructions))
            {
                return true;
            }

            if (TryFastForwardPointerNodeLeaf(state, bus, pc, out skippedInstructions))
            {
                return true;
            }
        }

        if (first == 0x3860_0000)
        {
            return TryFastForwardReturnZeroLeaf(state, bus, pc, out skippedInstructions);
        }

        if (first == 0x8003_0000)
        {
            return TryFastForwardWordEqualsLeaf(state, bus, pc, out skippedInstructions);
        }

        if (first == 0x7C60_00A6)
        {
            if (TryFastForwardDisableExternalInterruptLeaf(state, bus, pc, out skippedInstructions))
            {
                return true;
            }

            if (TryFastForwardEnableExternalInterruptLeaf(state, bus, pc, out skippedInstructions))
            {
                return true;
            }
        }

        if (first == 0x7C6D_42E6 || first == 0x7C6C_42E6)
        {
            return TryFastForwardTimeBaseReadLeaf(state, bus, pc, out skippedInstructions);
        }

        if (first == 0x2C03_0000)
        {
            return TryFastForwardRestoreExternalInterruptLeaf(state, bus, pc, out skippedInstructions);
        }

        return false;
    }

    private static bool TryFastForwardTimeBaseReadLeaf(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (bus.Read32(pc) == 0x7C6C_42E6
            && bus.Read32(pc + 0x04) == 0x4E80_0020
            && CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 2, extraInstructions: 0))
        {
            state.Gpr[3] = (uint)(state.TimeBase + 1);
            state.Pc = state.Lr;
            AdvanceFastForwardedInstructions(state, bus, 2);
            skippedInstructions = 2;
            return true;
        }

        if (bus.Read32(pc) != 0x7C6D_42E6
            || bus.Read32(pc + 0x04) != 0x7C8C_42E6
            || bus.Read32(pc + 0x08) != 0x7CAD_42E6
            || bus.Read32(pc + 0x0C) != 0x7C03_2800
            || bus.Read32(pc + 0x10) != 0x4082_FFF0
            || bus.Read32(pc + 0x14) != 0x4E80_0020
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 6, extraInstructions: 0))
        {
            return false;
        }

        ulong firstReadTime = state.TimeBase + 1;
        ulong lowReadTime = state.TimeBase + 2;
        ulong secondReadTime = state.TimeBase + 3;
        uint firstUpper = (uint)(firstReadTime >> 32);
        uint secondUpper = (uint)(secondReadTime >> 32);
        if (firstUpper != secondUpper)
        {
            return false;
        }

        state.Gpr[3] = firstUpper;
        state.Gpr[4] = (uint)lowReadTime;
        state.Gpr[5] = secondUpper;
        SetCr0(state, 0);
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 6);
        skippedInstructions = 6;
        return true;
    }

    private static bool TryFastForwardExternalInterruptLeafHelper(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint first = bus.Read32(pc);
        if (first == 0x7C60_00A6)
        {
            if (TryFastForwardDisableExternalInterruptLeaf(state, bus, pc, out skippedInstructions))
            {
                return true;
            }

            if (TryFastForwardEnableExternalInterruptLeaf(state, bus, pc, out skippedInstructions))
            {
                return true;
            }
        }

        return first == 0x2C03_0000
            && TryFastForwardRestoreExternalInterruptLeaf(state, bus, pc, out skippedInstructions);
    }

    private static bool TryFastForwardReturnZeroLeaf(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (bus.Read32(pc + 4) != 0x4E80_0020 || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 2, extraInstructions: 0))
        {
            return false;
        }

        state.Gpr[3] = 0;
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 2);
        skippedInstructions = 2;
        return true;
    }

    private static bool TryFastForwardVariadicRegisterSaveStub(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (bus.Read32(pc + 0x00) != 0x9421_FF90
            || bus.Read32(pc + 0x04) != 0x4086_0024
            || bus.Read32(pc + 0x08) != 0xD821_0028
            || bus.Read32(pc + 0x24) != 0xD901_0060
            || bus.Read32(pc + 0x28) != 0x9061_0008
            || bus.Read32(pc + 0x44) != 0x9141_0024
            || bus.Read32(pc + 0x48) != 0x3821_0070
            || bus.Read32(pc + 0x4C) != 0x4E80_0020
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 20, extraInstructions: 0))
        {
            return false;
        }

        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 20);
        skippedInstructions = 20;
        return true;
    }

    private static bool TryFastForwardLongDivisionLeaf(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;

        if (MatchesSignedLongDivisionLoop(bus, pc))
        {
            uint iterations = state.Ctr;
            if (iterations == 0 || !CanFastForwardInstructionCount(state, iterations, instructionsPerIteration: 10, extraInstructions: 8))
            {
                return false;
            }

            for (uint index = 0; index < iterations; index++)
            {
                state.Gpr[4] = AddExtended(state, state.Gpr[4], state.Gpr[4]);
                state.Gpr[3] = AddExtended(state, state.Gpr[3], state.Gpr[3]);
                state.Gpr[8] = AddExtended(state, state.Gpr[8], state.Gpr[8]);
                state.Gpr[7] = AddExtended(state, state.Gpr[7], state.Gpr[7]);
                state.Gpr[0] = SubtractFromCarrying(state, state.Gpr[6], state.Gpr[8]);
                state.Gpr[9] = SubtractFromExtended(state, state.Gpr[5], state.Gpr[7]);
                SetCr0(state, state.Gpr[9]);
                if ((state.Gpr[9] & 0x8000_0000) == 0)
                {
                    state.Gpr[8] = state.Gpr[0];
                    state.Gpr[7] = state.Gpr[9];
                }

                state.Gpr[0] = AddCarrying(state, state.Gpr[10], 1);
            }

            state.Ctr = 0;
            state.Gpr[4] = AddExtended(state, state.Gpr[4], state.Gpr[4]);
            state.Gpr[3] = AddExtended(state, state.Gpr[3], state.Gpr[3]);
            uint dividendSign = bus.Read32(state.Gpr[1] + 8);
            uint divisorSign = bus.Read32(state.Gpr[1] + 12);
            state.Gpr[7] = dividendSign ^ divisorSign;
            SetCr0(state, state.Gpr[7]);
            if (state.Gpr[7] != 0)
            {
                state.Gpr[4] = SubtractFromCarrying(state, state.Gpr[4], 0);
                state.Gpr[3] = SubtractFromZeroExtended(state, state.Gpr[3]);
            }

            state.Gpr[1] = unchecked(state.Gpr[1] + 16);
            state.Pc = state.Lr;
            uint skipped = checked(iterations * 10 + 8);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }

        if (MatchesSignedLongDivisionLeaf(bus, pc))
        {
            ulong divisor = CombineU64(state.Gpr[5], state.Gpr[6]);
            if (divisor == 0 || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 160, extraInstructions: 0))
            {
                return false;
            }

            ulong dividend = CombineU64(state.Gpr[3], state.Gpr[4]);
            long dividendSigned = unchecked((long)dividend);
            long divisorSigned = unchecked((long)divisor);
            ulong dividendMagnitude = dividendSigned < 0 ? unchecked(0ul - dividend) : dividend;
            ulong divisorMagnitude = divisorSigned < 0 ? unchecked(0ul - divisor) : divisor;
            ulong quotient = dividendMagnitude / divisorMagnitude;
            if ((dividendSigned < 0) != (divisorSigned < 0))
            {
                quotient = unchecked(0ul - quotient);
            }

            state.Gpr[1] = unchecked(state.Gpr[1] + 16);
            SetLongResult(state, quotient);
            state.Pc = state.Lr;
            SetCr0(state, state.Gpr[3]);
            AdvanceFastForwardedInstructions(state, bus, 160);
            skippedInstructions = 160;
            return true;
        }

        if (MatchesUnsignedLongDivisionLeaf(bus, pc))
        {
            ulong divisor = CombineU64(state.Gpr[5], state.Gpr[6]);
            if (divisor == 0 || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 120, extraInstructions: 0))
            {
                return false;
            }

            ulong quotient = CombineU64(state.Gpr[3], state.Gpr[4]) / divisor;
            SetLongResult(state, quotient);
            state.Pc = state.Lr;
            SetCr0(state, state.Gpr[3]);
            AdvanceFastForwardedInstructions(state, bus, 120);
            skippedInstructions = 120;
            return true;
        }

        return false;
    }

    private static bool MatchesSignedLongDivisionLoop(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x7C84_2114
        && bus.Read32(pc + 0x04) == 0x7C63_1914
        && bus.Read32(pc + 0x08) == 0x7D08_4114
        && bus.Read32(pc + 0x0C) == 0x7CE7_3914
        && bus.Read32(pc + 0x10) == 0x7C06_4010
        && bus.Read32(pc + 0x14) == 0x7D25_3911
        && bus.Read32(pc + 0x28) == 0x4200_FFD8
        && bus.Read32(pc + 0x2C) == 0x7C84_2114
        && bus.Read32(pc + 0x30) == 0x7C63_1914
        && bus.Read32(pc + 0x4C) == 0x7C63_0190
        && bus.Read32(pc + 0x50) == 0x4800_000C;

    private static bool MatchesSignedLongDivisionLeaf(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x000) == 0x9421_FFF0
        && bus.Read32(pc + 0x004) == 0x5469_0001
        && bus.Read32(pc + 0x014) == 0x54A9_0001
        && bus.Read32(pc + 0x02C) == 0x7C60_0034
        && bus.Read32(pc + 0x034) == 0x7C89_0034
        && bus.Read32(pc + 0x0D4) == 0x7C84_2114
        && bus.Read32(pc + 0x0FC) == 0x4200_FFD8
        && bus.Read32(pc + 0x120) == 0x7C63_0190
        && bus.Read32(pc + 0x130) == 0x3821_0010
        && bus.Read32(pc + 0x134) == 0x4E80_0020;

    private static bool MatchesUnsignedLongDivisionLeaf(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x000) == 0x2C03_0000
        && bus.Read32(pc + 0x004) == 0x7C60_0034
        && bus.Read32(pc + 0x014) == 0x7CCA_0034
        && bus.Read32(pc + 0x0A8) == 0x7C84_2114
        && bus.Read32(pc + 0x0D4) == 0x7D04_4378
        && bus.Read32(pc + 0x0D8) == 0x7CE3_3B78
        && bus.Read32(pc + 0x0DC) == 0x4E80_0020
        && bus.Read32(pc + 0x0E0) == 0x4E80_0020;

    private static ulong CombineU64(uint high, uint low) => ((ulong)high << 32) | low;

    private static void SetLongResult(PowerPcState state, ulong value)
    {
        state.Gpr[3] = (uint)(value >> 32);
        state.Gpr[4] = (uint)value;
    }

    private static uint AddExtended(PowerPcState state, uint left, uint right)
    {
        ulong carry = (state.Xer & 0x2000_0000) != 0 ? 1u : 0u;
        ulong result = (ulong)left + right + carry;
        SetCarry(state, result > uint.MaxValue);
        return (uint)result;
    }

    private static uint AddCarrying(PowerPcState state, uint left, uint right)
    {
        uint result = unchecked(left + right);
        SetCarry(state, result < left);
        return result;
    }

    private static uint SubtractFromCarrying(PowerPcState state, uint subtrahend, uint minuend)
    {
        uint result = unchecked(minuend - subtrahend);
        SetCarry(state, minuend >= subtrahend);
        return result;
    }

    private static uint SubtractFromExtended(PowerPcState state, uint subtrahend, uint minuend)
    {
        ulong carry = (state.Xer & 0x2000_0000) != 0 ? 1u : 0u;
        ulong result = (ulong)minuend + (~subtrahend & 0xFFFF_FFFFu) + carry;
        SetCarry(state, result > uint.MaxValue);
        return (uint)result;
    }

    private static uint SubtractFromZeroExtended(PowerPcState state, uint subtrahend) =>
        SubtractFromExtended(state, subtrahend, 0);

    private static bool TryFastForwardFourShortStoreLeaf(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (bus.Read32(pc + 0x04) != 0xB003_0000
            || bus.Read32(pc + 0x08) != 0x7CA0_0734
            || bus.Read32(pc + 0x0C) != 0x7CC4_0734
            || bus.Read32(pc + 0x10) != 0xB003_0002
            || bus.Read32(pc + 0x14) != 0x7CE0_0734
            || bus.Read32(pc + 0x18) != 0xB083_0004
            || bus.Read32(pc + 0x1C) != 0xB003_0006
            || bus.Read32(pc + 0x20) != 0x4E80_0020)
        {
            return false;
        }

        uint address = state.Gpr[3];
        if (!bus.Memory.IsMainRamAddress(address, 8))
        {
            return false;
        }

        uint v4 = SignExtend16(state.Gpr[4]);
        uint v5 = SignExtend16(state.Gpr[5]);
        uint v6 = SignExtend16(state.Gpr[6]);
        uint v7 = SignExtend16(state.Gpr[7]);
        bus.Memory.Write16(address, (ushort)v4);
        bus.Memory.Write16(address + 2, (ushort)v5);
        bus.Memory.Write16(address + 4, (ushort)v6);
        bus.Memory.Write16(address + 6, (ushort)v7);
        state.Gpr[0] = v7;
        state.Gpr[4] = v6;
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 9);
        skippedInstructions = 9;
        return true;
    }

    private static bool TryFastForwardZeroThreeWordsLeaf(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (bus.Read32(pc + 0x04) != 0x9003_0000
            || bus.Read32(pc + 0x08) != 0x9003_0004
            || bus.Read32(pc + 0x0C) != 0x9003_0008
            || bus.Read32(pc + 0x10) != 0x4E80_0020)
        {
            return false;
        }

        uint address = state.Gpr[3];
        if (!bus.Memory.IsMainRamAddress(address, 12))
        {
            return false;
        }

        state.Gpr[0] = 0;
        bus.Memory.Write32(address, 0);
        bus.Memory.Write32(address + 4, 0);
        bus.Memory.Write32(address + 8, 0);
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 5);
        skippedInstructions = 5;
        return true;
    }

    private static bool TryFastForwardPointerNodeLeaf(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (bus.Read32(pc + 0x04) != 0x9003_0004
            || bus.Read32(pc + 0x08) != 0x9083_0000
            || bus.Read32(pc + 0x0C) != 0x9003_0008
            || bus.Read32(pc + 0x10) != 0x9003_000C
            || bus.Read32(pc + 0x14) != 0x4E80_0020)
        {
            return false;
        }

        uint address = state.Gpr[3];
        if (!bus.Memory.IsMainRamAddress(address, 16))
        {
            return false;
        }

        state.Gpr[0] = 0;
        bus.Memory.Write32(address, state.Gpr[4]);
        bus.Memory.Write32(address + 4, 0);
        bus.Memory.Write32(address + 8, 0);
        bus.Memory.Write32(address + 12, 0);
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 6);
        skippedInstructions = 6;
        return true;
    }

    private static bool TryFastForwardWordEqualsLeaf(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (bus.Read32(pc + 0x04) != 0x7C00_2050
            || bus.Read32(pc + 0x08) != 0x7C00_0034
            || bus.Read32(pc + 0x0C) != 0x5403_D97E
            || bus.Read32(pc + 0x10) != 0x4E80_0020)
        {
            return false;
        }

        uint address = state.Gpr[3];
        if (!bus.Memory.IsMainRamAddress(address, sizeof(uint)))
        {
            return false;
        }

        uint diff = unchecked(state.Gpr[4] - bus.Memory.Read32(address));
        uint leadingZeroes = (uint)uint.LeadingZeroCount(diff);
        state.Gpr[0] = leadingZeroes;
        state.Gpr[3] = leadingZeroes >> 5;
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 5);
        skippedInstructions = 5;
        return true;
    }

    private static bool TryFastForwardDisableExternalInterruptLeaf(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (bus.Read32(pc + 0x04) != 0x5464_045E
            || bus.Read32(pc + 0x08) != 0x7C80_0124
            || bus.Read32(pc + 0x0C) != 0x5463_8FFE
            || bus.Read32(pc + 0x10) != 0x4E80_0020)
        {
            return false;
        }

        uint oldMsr = state.Msr;
        state.Gpr[3] = (oldMsr >> 15) & 1;
        state.Gpr[4] = oldMsr & ~0x8000u;
        state.Msr = state.Gpr[4];
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 5);
        skippedInstructions = 5;
        return true;
    }

    private static bool TryFastForwardEnableExternalInterruptLeaf(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (bus.Read32(pc + 0x04) != 0x6064_8000
            || bus.Read32(pc + 0x08) != 0x7C80_0124
            || bus.Read32(pc + 0x0C) != 0x5463_8FFE
            || bus.Read32(pc + 0x10) != 0x4E80_0020)
        {
            return false;
        }

        uint oldMsr = state.Msr;
        state.Gpr[3] = (oldMsr >> 15) & 1;
        state.Gpr[4] = oldMsr | 0x8000u;
        state.Msr = state.Gpr[4];
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 5);
        skippedInstructions = 5;
        return true;
    }

    private static bool TryFastForwardRestoreExternalInterruptLeaf(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (bus.Read32(pc + 0x04) != 0x7C80_00A6
            || bus.Read32(pc + 0x08) != 0x4182_000C
            || bus.Read32(pc + 0x0C) != 0x6085_8000
            || bus.Read32(pc + 0x10) != 0x4800_0008
            || bus.Read32(pc + 0x14) != 0x5485_045E
            || bus.Read32(pc + 0x18) != 0x7CA0_0124
            || bus.Read32(pc + 0x1C) != 0x5484_8FFE
            || bus.Read32(pc + 0x20) != 0x4E80_0020)
        {
            return false;
        }

        uint oldMsr = state.Msr;
        uint restoredMsr = state.Gpr[3] == 0 ? oldMsr & ~0x8000u : oldMsr | 0x8000u;
        SetCr0ForSignedCompareImmediate(state, state.Gpr[3], 0);
        state.Gpr[4] = (oldMsr >> 15) & 1;
        state.Gpr[5] = restoredMsr;
        state.Msr = restoredMsr;
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 9);
        skippedInstructions = 9;
        return true;
    }

    private static uint SignExtend16(uint value) =>
        unchecked((uint)(short)(value & 0xFFFF));

    private static bool TryFastForwardNullTerminatedByteCopyLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint loadUpdate = bus.Read32(pc);
        if (!TryDecodeDForm(loadUpdate, primaryOpcode: 35, out int valueRegister, out int sourceRegister, out int sourceOffset)
            || sourceOffset != 1)
        {
            return false;
        }

        uint compare = bus.Read32(pc + 4);
        if (compare != (0x2800_0000u | (uint)(valueRegister << 16)))
        {
            return false;
        }

        uint storeUpdate = bus.Read32(pc + 8);
        if (!TryDecodeDForm(storeUpdate, primaryOpcode: 39, out int storeValueRegister, out int destinationRegister, out int destinationOffset)
            || storeValueRegister != valueRegister
            || destinationOffset != 1)
        {
            return false;
        }

        uint branch = bus.Read32(pc + 12);
        if (branch != 0x4082_FFF4 || bus.Read32(pc + 16) != 0x4E80_0020)
        {
            return false;
        }

        uint sourceStart = unchecked(state.Gpr[sourceRegister] + 1);
        if (!bus.Memory.IsMainRamAddress(sourceStart, 1))
        {
            return false;
        }

        uint bytesToCopy = 0;
        bool foundTerminator = false;
        while (bytesToCopy < MaxFastForwardStringCopyBytes)
        {
            uint address = unchecked(sourceStart + bytesToCopy);
            if (!bus.Memory.IsMainRamAddress(address, 1))
            {
                return false;
            }

            bytesToCopy++;
            if (bus.Memory.Read8(address) == 0)
            {
                foundTerminator = true;
                break;
            }
        }

        if (!foundTerminator || bytesToCopy == 0)
        {
            return false;
        }

        uint destinationStart = unchecked(state.Gpr[destinationRegister] + 1);
        if (!bus.Memory.IsMainRamAddress(destinationStart, checked((int)bytesToCopy))
            || !CanFastForwardInstructionCount(state, bytesToCopy, 4, 1))
        {
            return false;
        }

        byte lastValue = 0;
        for (uint offset = 0; offset < bytesToCopy; offset++)
        {
            lastValue = bus.Memory.Read8(sourceStart + offset);
            bus.Memory.Write8(destinationStart + offset, lastValue);
        }

        state.Gpr[valueRegister] = lastValue;
        state.Gpr[sourceRegister] = unchecked(state.Gpr[sourceRegister] + bytesToCopy);
        state.Gpr[destinationRegister] = unchecked(state.Gpr[destinationRegister] + bytesToCopy);
        SetCr0(state, lastValue);
        state.Pc = state.Lr;
        uint skipped = checked(bytesToCopy * 4 + 1);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardStringCompareRoutine(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (bus.Read32(pc + 0x00) != 0x88C3_0000
            || bus.Read32(pc + 0x04) != 0x88A4_0000
            || bus.Read32(pc + 0x08) != 0x7C05_3051
            || bus.Read32(pc + 0x0C) != 0x4182_000C
            || bus.Read32(pc + 0x10) != 0x7C65_3050
            || bus.Read32(pc + 0x14) != 0x4E80_0020
            || bus.Read32(pc + 0x18) != 0x5480_07BE
            || bus.Read32(pc + 0x1C) != 0x5465_07BE
            || bus.Read32(pc + 0x20) != 0x7C00_2840
            || bus.Read32(pc + 0x24) != 0x4082_00C8
            || bus.Read32(pc + 0x28) != 0x2805_0000
            || bus.Read32(pc + 0x2C) != 0x4182_0058
            || bus.Read32(pc + 0x30) != 0x2806_0000
            || bus.Read32(pc + 0x50) != 0x8CA3_0001
            || bus.Read32(pc + 0x54) != 0x8C04_0001
            || bus.Read32(pc + 0x84) != 0x80E3_0000
            || bus.Read32(pc + 0x88) != 0x80CD_2AE4
            || bus.Read32(pc + 0x8C) != 0x80AD_2AE0
            || bus.Read32(pc + 0xA4) != 0x84E3_0004
            || bus.Read32(pc + 0xA8) != 0x8504_0004
            || bus.Read32(pc + 0xD4) != 0x88C3_0000
            || bus.Read32(pc + 0xD8) != 0x88A4_0000
            || bus.Read32(pc + 0xFC) != 0x8CA3_0001
            || bus.Read32(pc + 0x100) != 0x8C04_0001
            || bus.Read32(pc + 0x104) != 0x7C00_2851
            || bus.Read32(pc + 0x114) != 0x2805_0000
            || bus.Read32(pc + 0x118) != 0x4082_FFE4
            || bus.Read32(pc + 0x11C) != 0x3860_0000
            || bus.Read32(pc + 0x120) != 0x4E80_0020)
        {
            return false;
        }

        uint leftAddress = state.Gpr[3];
        uint rightAddress = state.Gpr[4];
        uint comparedBytes = 0;
        byte left = 0;
        byte right = 0;
        bool finished = false;
        while (comparedBytes < MaxFastForwardStringCompareBytes)
        {
            uint leftCurrent = unchecked(leftAddress + comparedBytes);
            uint rightCurrent = unchecked(rightAddress + comparedBytes);
            if (!bus.Memory.IsMainRamAddress(leftCurrent, 1) || !bus.Memory.IsMainRamAddress(rightCurrent, 1))
            {
                return false;
            }

            left = bus.Memory.Read8(leftCurrent);
            right = bus.Memory.Read8(rightCurrent);
            comparedBytes++;
            if (left != right || left == 0)
            {
                finished = true;
                break;
            }
        }

        if (!finished)
        {
            return false;
        }

        if (!CanFastForwardInstructionCount(state, comparedBytes, 12, 24))
        {
            return false;
        }

        uint result = unchecked((uint)(left - right));
        state.Gpr[0] = result;
        state.Gpr[3] = result;
        state.Gpr[5] = right;
        state.Gpr[6] = left;
        SetCr0(state, result);
        state.Pc = state.Lr;
        uint skipped = checked(comparedBytes * 12 + 24);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardStringLengthRoutine(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (bus.Read32(pc + 0x00) != 0x9421_FFF0
            || bus.Read32(pc + 0x04) != 0x93E1_000C
            || bus.Read32(pc + 0x08) != 0x3BE0_FFFF
            || bus.Read32(pc + 0x0C) != 0x93C1_0008
            || bus.Read32(pc + 0x10) != 0x3BC3_FFFF
            || bus.Read32(pc + 0x14) != 0x8C1E_0001
            || bus.Read32(pc + 0x18) != 0x3BFF_0001
            || bus.Read32(pc + 0x1C) != 0x2800_0000
            || bus.Read32(pc + 0x20) != 0x4082_FFF4
            || bus.Read32(pc + 0x24) != 0x7FE3_FB78
            || bus.Read32(pc + 0x28) != 0x83E1_000C
            || bus.Read32(pc + 0x2C) != 0x83C1_0008
            || bus.Read32(pc + 0x30) != 0x3821_0010
            || bus.Read32(pc + 0x34) != 0x4E80_0020)
        {
            return false;
        }

        uint stringAddress = state.Gpr[3];
        uint length = 0;
        bool foundTerminator = false;
        while (length < MaxFastForwardStringLengthBytes)
        {
            uint address = unchecked(stringAddress + length);
            if (!bus.Memory.IsMainRamAddress(address, 1))
            {
                return false;
            }

            if (bus.Memory.Read8(address) == 0)
            {
                foundTerminator = true;
                break;
            }

            length++;
        }

        if (!foundTerminator)
        {
            return false;
        }

        uint skipped = checked(10 + (length + 1) * 4);
        if (!CanFastForwardInstructionCount(state, iterations: skipped, instructionsPerIteration: 1, extraInstructions: 0))
        {
            return false;
        }

        state.Gpr[0] = 0;
        state.Gpr[3] = length;
        SetCr0(state, 0);
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardTextureSampleLeaf(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (bus.Read32(pc + 0x00) != 0xA003_0004
            || bus.Read32(pc + 0x04) != 0x2C00_0005
            || bus.Read32(pc + 0x08) != 0x4182_0008
            || bus.Read32(pc + 0x0C) != 0x4800_005C
            || bus.Read32(pc + 0x10) != 0x80E3_0010
            || bus.Read32(pc + 0x14) != 0x8143_000C
            || bus.Read32(pc + 0x18) != 0x7D05_3B96
            || bus.Read32(pc + 0x1C) != 0xA003_0008
            || bus.Read32(pc + 0x20) != 0x8063_0014
            || bus.Read32(pc + 0x24) != 0x7D24_5396
            || bus.Read32(pc + 0x28) != 0x7D6A_39D6
            || bus.Read32(pc + 0x2C) != 0x7CC0_5396
            || bus.Read32(pc + 0x30) != 0x7C08_39D6
            || bus.Read32(pc + 0x34) != 0x7CE6_59D6
            || bus.Read32(pc + 0x38) != 0x7CC9_51D6
            || bus.Read32(pc + 0x3C) != 0x7C00_2850
            || bus.Read32(pc + 0x40) != 0x7C0A_01D6
            || bus.Read32(pc + 0x44) != 0x7C86_2050
            || bus.Read32(pc + 0x48) != 0x7CA8_39D6
            || bus.Read32(pc + 0x4C) != 0x7C84_0214
            || bus.Read32(pc + 0x50) != 0x7C09_59D6
            || bus.Read32(pc + 0x54) != 0x7C84_2A14
            || bus.Read32(pc + 0x58) != 0x7C80_2214
            || bus.Read32(pc + 0x5C) != 0x7C03_20AE
            || bus.Read32(pc + 0x60) != 0x5403_0636
            || bus.Read32(pc + 0x64) != 0x4E80_0020)
        {
            return false;
        }

        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 25, extraInstructions: 0))
        {
            return false;
        }

        uint descriptor = state.Gpr[3];
        if (!bus.Memory.IsMainRamAddress(descriptor, 0x18))
        {
            return false;
        }

        ushort textureKind = bus.Memory.Read16(descriptor + 4);
        if (textureKind != 5)
        {
            return false;
        }

        uint tileHeight = bus.Memory.Read32(descriptor + 0x10);
        uint tileWidth = bus.Memory.Read32(descriptor + 0x0C);
        if (tileWidth == 0 || tileHeight == 0)
        {
            return false;
        }

        uint coordinateX = state.Gpr[4];
        uint coordinateY = state.Gpr[5];
        uint rowCount = bus.Memory.Read16(descriptor + 8);
        uint dataAddress = bus.Memory.Read32(descriptor + 0x14);
        uint tileY = coordinateY / tileHeight;
        uint tileX = coordinateX / tileWidth;
        uint tileSize = unchecked(tileWidth * tileHeight);
        uint tileRows = rowCount / tileWidth;
        uint tileRowOffset = unchecked(tileY * tileHeight);
        uint tileBlockOffset = unchecked(tileRows * tileSize);
        uint tileColumnOffset = unchecked(tileX * tileWidth);
        uint intraTileY = unchecked(coordinateY - tileRowOffset);
        uint rowOffset = unchecked(tileWidth * intraTileY);
        uint intraTileX = unchecked(coordinateX - tileColumnOffset);
        uint blockYOffset = unchecked(tileY * tileBlockOffset);
        uint blockXOffset = unchecked(tileX * tileSize);
        uint textureOffset = unchecked(blockXOffset + blockYOffset + rowOffset + intraTileX);
        uint effectiveAddress = unchecked(dataAddress + textureOffset);
        if (!bus.Memory.IsMainRamAddress(effectiveAddress, 1))
        {
            return false;
        }

        byte sample = bus.Memory.Read8(effectiveAddress);
        state.Gpr[0] = sample;
        state.Gpr[3] = (uint)(sample & 0xF0);
        state.Gpr[4] = textureOffset;
        state.Gpr[5] = blockYOffset;
        state.Gpr[6] = tileColumnOffset;
        state.Gpr[7] = tileBlockOffset;
        state.Gpr[8] = tileY;
        state.Gpr[9] = tileX;
        state.Gpr[10] = tileWidth;
        state.Gpr[11] = tileSize;
        SetCr0(state, 0);
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 25);
        skippedInstructions = 25;
        return true;
    }

    private static bool TryFastForwardSonicPrsDecompress(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        return TryFastForwardSonicPrsDecompressCore(state, bus, out skippedInstructions, trace: null);
    }

    private static bool TryFastForwardSonicPrsDecompressCore(PowerPcState state, GameCubeBus bus, out int skippedInstructions, Action<string>? trace)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicPrsDecompressRoutine(bus, pc))
        {
            return false;
        }

        uint source = state.Gpr[3];
        uint destination = state.Gpr[4];
        if (!TryDecodeSegaPrs(bus.Memory, source, out byte[] output, out uint sourceEnd, out byte lastFlagByte, out int bitsRemaining, out string? decodeFailure))
        {
            trace?.Invoke($"decode failed pc=0x{pc:X8} source=0x{source:X8} destination=0x{destination:X8}: {decodeFailure}");
            return false;
        }

        if (!bus.Memory.IsMainRamAddress(destination, output.Length))
        {
            trace?.Invoke($"destination out of range pc=0x{pc:X8} source=0x{source:X8} destination=0x{destination:X8} output=0x{output.Length:X}");
            return false;
        }

        uint skipped = EstimateSegaPrsInstructionCount(source, sourceEnd, output.Length);
        if (!CanFastForwardInstructionCount(state, iterations: skipped, instructionsPerIteration: 1, extraInstructions: 0))
        {
            trace?.Invoke($"instruction budget rejected pc=0x{pc:X8} source=0x{source:X8} destination=0x{destination:X8} output=0x{output.Length:X} skipped=0x{skipped:X}");
            return false;
        }

        for (int index = 0; index < output.Length; index++)
        {
            bus.Memory.Write8(destination + (uint)index, output[index]);
        }

        state.Gpr[0] = 0;
        state.Gpr[3] = (uint)output.Length;
        state.Gpr[4] = destination;
        state.Gpr[5] = destination + (uint)output.Length;
        state.Gpr[6] = sourceEnd;
        state.Gpr[7] = destination + (uint)output.Length;
        state.Gpr[8] = lastFlagByte;
        state.Gpr[9] = (uint)bitsRemaining;
        state.Pc = state.Lr;
        SetCr0(state, 0);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        trace?.Invoke($"fast-forwarded pc=0x{pc:X8} source=0x{source:X8} sourceEnd=0x{sourceEnd:X8} destination=0x{destination:X8} output=0x{output.Length:X} skipped=0x{skipped:X}");
        return true;
    }

    private static bool TryFastForwardMetroTrkEventLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (bus.Read32(pc - 0x20) != 0x9421_FFE0
            || bus.Read32(pc - 0x1C) != 0x7C08_02A6
            || bus.Read32(pc - 0x08) != 0x3BC0_0000
            || bus.Read32(pc + 0x00) != 0x3861_0008
            || bus.Read32(pc + 0x04) != 0x4800_01F1
            || bus.Read32(pc + 0xB8) != 0x2C1F_0000
            || bus.Read32(pc + 0xBC) != 0x4182_FF44
            || bus.Read32(pc + 0xC0) != 0x8001_0024
            || bus.Read32(pc + 0xCC) != 0x7C08_03A6)
        {
            return false;
        }

        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 48, extraInstructions: 0))
        {
            return false;
        }

        state.Gpr[31] = 1;
        state.Gpr[30] = 0;
        state.Pc = pc + 0xC0;
        SetCr0(state, 1);
        AdvanceFastForwardedInstructions(state, bus, 48);
        skippedInstructions = 48;
        return true;
    }

    private static bool MatchesSonicPrsDecompressRoutine(GameCubeBus bus, uint pc) =>
        GetSonicPrsDecompressSignatureMismatch(bus, pc) is null;

    private static string? GetSonicPrsDecompressSignatureMismatch(GameCubeBus bus, uint pc)
    {
        ReadOnlySpan<(uint Offset, uint Word)> signature =
        [
            (0x000, 0x8903_0000),
            (0x004, 0x38E4_0000),
            (0x008, 0x38C3_0001),
            (0x00C, 0x3920_0009),
            (0x010, 0x3529_FFFF),
            (0x014, 0x4082_0010),
            (0x024, 0x5503_07FE),
            (0x028, 0x2803_0001),
            (0x030, 0x4082_0014),
            (0x048, 0x4182_FFC8),
            (0x084, 0x4082_000C),
            (0x088, 0x7C64_3850),
            (0x08C, 0x4E80_0020),
            (0x120, 0x2803_0000),
            (0x124, 0x4182_FEEC),
            (0x1A0, 0x4BFF_FE70),
            (0x1A4, 0x4E80_0020),
        ];

        foreach ((uint offset, uint expected) in signature)
        {
            uint actual = bus.Read32(pc + offset);
            if (actual != expected)
            {
                return $"mismatch offset=0x{offset:X} expected=0x{expected:X8} actual=0x{actual:X8}";
            }
        }

        return null;
    }

    private static bool TryDecodeSegaPrs(GameCubeMemory memory, uint source, out byte[] output, out uint sourceEnd, out byte lastFlagByte, out int bitsRemaining, out string? failure)
    {
        output = [];
        sourceEnd = source;
        lastFlagByte = 0;
        bitsRemaining = 0;
        failure = null;
        string? failureMessage = null;
        if (!memory.IsMainRamAddress(source, 1))
        {
            failure = $"source is not main RAM at 0x{source:X8}";
            return false;
        }

        List<byte> decoded = [];
        uint input = source + 1;
        byte flags = memory.Read8(source);
        int remainingFlagBits = 8;

        bool TryReadByte(out byte value)
        {
            value = 0;
            if (unchecked(input - source) > MaxFastForwardPrsOutputBytes || !memory.IsMainRamAddress(input, 1))
            {
                failureMessage = $"input read out of range at 0x{input:X8} after 0x{decoded.Count:X} decoded byte(s)";
                return false;
            }

            value = memory.Read8(input);
            input++;
            return true;
        }

        bool TryReadBit(out int bit)
        {
            bit = 0;
            if (remainingFlagBits == 0)
            {
                if (!TryReadByte(out flags))
                {
                    return false;
                }

                remainingFlagBits = 8;
            }

            bit = flags & 1;
            flags >>= 1;
            remainingFlagBits--;
            return true;
        }

        bool TryAppendCopy(int sourceIndex, int count)
        {
            if (sourceIndex < 0 || count < 0 || decoded.Count + count > MaxFastForwardPrsOutputBytes)
            {
                failureMessage = $"copy out of range sourceIndex=0x{sourceIndex:X} count=0x{count:X} output=0x{decoded.Count:X}";
                return false;
            }

            for (int index = 0; index < count; index++)
            {
                int readIndex = sourceIndex + index;
                if ((uint)readIndex >= (uint)decoded.Count)
                {
                    failureMessage = $"copy read out of range readIndex=0x{readIndex:X} count=0x{count:X} output=0x{decoded.Count:X}";
                    return false;
                }

                decoded.Add(decoded[readIndex]);
            }

            return true;
        }

        while (decoded.Count <= MaxFastForwardPrsOutputBytes)
        {
            if (!TryReadBit(out int commandBit))
            {
                failure = failureMessage ?? "could not read command bit";
                return false;
            }

            if (commandBit == 1)
            {
                if (!TryReadByte(out byte literal))
                {
                    failure = failureMessage ?? "could not read literal byte";
                    return false;
                }

                decoded.Add(literal);
                continue;
            }

            if (!TryReadBit(out int longCopyBit))
            {
                failure = failureMessage ?? "could not read copy mode bit";
                return false;
            }

            if (longCopyBit == 1)
            {
                if (!TryReadByte(out byte tokenLow) || !TryReadByte(out byte tokenHigh))
                {
                    failure = failureMessage ?? "could not read long-copy token";
                    return false;
                }

                int token = tokenLow | (tokenHigh << 8);
                if (token == 0)
                {
                    output = decoded.ToArray();
                    sourceEnd = input;
                    lastFlagByte = flags;
                    bitsRemaining = remainingFlagBits;
                    return true;
                }

                int offset = unchecked((int)(0xFFFF_E000u | ((uint)token >> 3)));
                int count = token & 7;
                if (count == 0)
                {
                    if (!TryReadByte(out byte extendedCount))
                    {
                        failure = failureMessage ?? "could not read extended long-copy count";
                        return false;
                    }

                    count = extendedCount + 1;
                }
                else
                {
                    count += 2;
                }

                if (!TryAppendCopy(decoded.Count + offset, count))
                {
                    failure = failureMessage ?? "long-copy append failed";
                    return false;
                }

                continue;
            }

            int shortCountCode = 0;
            if (!TryReadBit(out int firstLengthBit) || !TryReadBit(out int secondLengthBit) || !TryReadByte(out byte shortOffsetByte))
            {
                failure = failureMessage ?? "could not read short-copy token";
                return false;
            }

            shortCountCode = (firstLengthBit << 1) | secondLengthBit;
            int shortOffset = unchecked((int)(0xFFFF_FF00u | shortOffsetByte));
            if (!TryAppendCopy(decoded.Count + shortOffset, shortCountCode + 2))
            {
                failure = failureMessage ?? "short-copy append failed";
                return false;
            }
        }

        failure = $"decoded output exceeded 0x{MaxFastForwardPrsOutputBytes:X} byte limit";
        return false;
    }

    private static uint EstimateSegaPrsInstructionCount(uint source, uint sourceEnd, int outputLength)
    {
        uint sourceBytes = sourceEnd >= source ? sourceEnd - source : 0;
        ulong estimate = 32ul + sourceBytes * 24ul + (uint)outputLength * 12ul;
        return checked((uint)Math.Min(estimate, int.MaxValue));
    }

    private static bool TryFastForwardSonicTrigTableInit(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint bodyPc = pc switch
        {
            _ when bus.Read32(pc) == 0x4800_0048 => pc + 4,
            _ when bus.Read32(pc) == 0x6FA0_8000 => pc,
            _ when bus.Read32(pc) == 0x7C1C_F800 && bus.Read32(pc + 4) == 0x4180_FFB8 => pc - 0x44,
            _ => 0,
        };

        if (bodyPc == 0
            || !MatchesSonicTrigTableInitLoop(bus, bodyPc))
        {
            return false;
        }

        uint currentEntry = state.Gpr[28];
        uint totalEntries = state.Gpr[31];
        uint angleIndex = state.Gpr[29];
        uint destination = state.Gpr[27];
        if (totalEntries == 0
            || totalEntries > MaxFastForwardTrigTableEntries
            || currentEntry >= totalEntries)
        {
            return false;
        }

        uint entriesToWrite = totalEntries - currentEntry;
        uint bytesToWrite = checked(entriesToWrite * 8);
        if (!CanFastForwardInstructionCount(state, entriesToWrite, SonicTrigTableInstructionsPerEntry, 2)
            || !bus.Memory.IsMainRamAddress(destination, checked((int)bytesToWrite)))
        {
            return false;
        }

        float lastSine = 0.0f;
        float lastCosine = 1.0f;
        for (uint entry = 0; entry < entriesToWrite; entry++)
        {
            double radians = unchecked(angleIndex + entry * 2) * (Math.Tau / MaxFastForwardTrigTableEntries);
            lastSine = MathF.Sin((float)radians);
            lastCosine = MathF.Cos((float)radians);
            WriteSingle(bus.Memory, destination + entry * 8, lastSine);
            WriteSingle(bus.Memory, destination + entry * 8 + 4, lastCosine);
        }

        state.Gpr[27] = destination + bytesToWrite;
        state.Gpr[28] = totalEntries;
        state.Gpr[29] = angleIndex + entriesToWrite * 2;
        state.Gpr[0] = unchecked(angleIndex + (entriesToWrite - 1) * 2) ^ 0x8000_0000;
        state.Fpr[1] = lastSine;
        state.Fpr[28] = ((angleIndex + (entriesToWrite - 1) * 2) & 0xFFFF) / (double)MaxFastForwardTrigTableEntries;
        state.Pc = bodyPc + 0x4C;
        SetCr0(state, 0);

        uint skipped = checked(entriesToWrite * SonicTrigTableInstructionsPerEntry + 2);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicTrigTableInitLoop(GameCubeBus bus, uint bodyPc) =>
        bus.Read32(bodyPc - 0x04) == 0x4800_0048
        && bus.Read32(bodyPc + 0x00) == 0x6FA0_8000
        && bus.Read32(bodyPc + 0x04) == 0x9001_002C
        && bus.Read32(bodyPc + 0x08) == 0x93C1_0028
        && bus.Read32(bodyPc + 0x0C) == 0xC801_0028
        && bus.Read32(bodyPc + 0x10) == 0xEC00_F028
        && bus.Read32(bodyPc + 0x14) == 0xEC1D_0032
        && bus.Read32(bodyPc + 0x18) == 0xEF80_F824
        && bus.Read32(bodyPc + 0x1C) == 0xFC20_E090
        && bus.Read32(bodyPc + 0x20) == 0x4BFF_90D5
        && bus.Read32(bodyPc + 0x24) == 0xD03B_0000
        && bus.Read32(bodyPc + 0x28) == 0x3B7B_0004
        && bus.Read32(bodyPc + 0x2C) == 0xFC20_E090
        && bus.Read32(bodyPc + 0x30) == 0x4BFF_90A5
        && bus.Read32(bodyPc + 0x34) == 0xD03B_0000
        && bus.Read32(bodyPc + 0x38) == 0x3B7B_0004
        && bus.Read32(bodyPc + 0x3C) == 0x3BBD_0002
        && bus.Read32(bodyPc + 0x40) == 0x3B9C_0001
        && bus.Read32(bodyPc + 0x44) == 0x7C1C_F800
        && bus.Read32(bodyPc + 0x48) == 0x4180_FFB8;

    private static void WriteSingle(GameCubeMemory memory, uint address, float value) =>
        memory.Write32(address, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    private static float ReadSingle(GameCubeMemory memory, uint address) =>
        BitConverter.Int32BitsToSingle(unchecked((int)memory.Read32(address)));

    private static bool TryFastForwardSonicBitUnpackRows(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (MatchesSonicBitScanSetup(bus, pc))
        {
            return TryFastForwardSonicBitScanSetup(state, bus, pc, out skippedInstructions);
        }

        if (MatchesSonicBitScanRow(bus, pc))
        {
            return TryFastForwardSonicBitScanRow(state, bus, pc, out skippedInstructions);
        }

        if (MatchesSonicBitUnpackByte(bus, pc))
        {
            return TryFastForwardSonicBitUnpackByte(state, bus, pc, out skippedInstructions);
        }

        if (!MatchesSonicBitUnpackRows(bus, pc))
        {
            return false;
        }

        uint row = state.Gpr[7];
        uint bitOffset = state.Gpr[9];
        uint source = state.Gpr[3];
        if (row >= 24 || bitOffset > 8)
        {
            return false;
        }

        uint remainingRows = 24 - row;
        uint readWriteLength = checked(remainingRows * 3 + 1);
        if (!CanFastForwardInstructionCount(state, remainingRows, SonicBitUnpackInstructionsPerRow, 0)
            || !bus.Memory.IsMainRamAddress(source, checked((int)readWriteLength)))
        {
            return false;
        }

        uint rowBase = source;
        byte lastByte = 0;
        for (uint currentRow = row; currentRow < 24; currentRow++)
        {
            for (uint output = 0; output < 3; output++)
            {
                uint firstBit = bitOffset + output * 8;
                lastByte = GatherEightBits(bus.Memory, rowBase, firstBit);
                bus.Memory.Write8(rowBase + output, lastByte);
            }

            rowBase += 3;
        }

        state.Gpr[0] = 0;
        state.Gpr[3] = rowBase;
        state.Gpr[5] = bitOffset + 24;
        state.Gpr[6] = rowBase;
        state.Gpr[7] = 24;
        state.Gpr[8] = 3;
        state.Gpr[11] = 8;
        state.Gpr[12] = bitOffset + 24;
        state.Gpr[31] = lastByte;
        state.Ctr = 0;
        state.Pc = pc + 0x15C;
        SetCr0(state, 0);

        uint skipped = checked(remainingRows * SonicBitUnpackInstructionsPerRow);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardSonicBitScanRow(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint source = state.Gpr[3];
        uint offset = state.Gpr[4];
        if (offset < 2
            || state.Ctr == 0
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 28, extraInstructions: 0)
            || !bus.Memory.IsMainRamAddress(source + offset - 2, 3))
        {
            return false;
        }

        uint count = 0;
        byte value = bus.Memory.Read8(source + offset);
        if (value == 0)
        {
            count = 8;
            value = bus.Memory.Read8(source + offset - 1);
            if (value == 0)
            {
                count = 16;
                value = bus.Memory.Read8(source + offset - 2);
                if (value == 0)
                {
                    count = 24;
                }
            }
        }

        uint shifted = value;
        if (shifted != 0)
        {
            while ((shifted & 0x80) == 0)
            {
                shifted = (shifted << 1) & 0xFF;
                count++;
            }
        }

        if (count < state.Gpr[10])
        {
            state.Gpr[10] = count;
        }

        state.Gpr[0] = shifted == 0 ? 0u : 0x8000_0000;
        state.Gpr[4] = offset + 3;
        state.Gpr[5] = shifted & 0xFF;
        state.Gpr[6] = count;
        state.Gpr[7] = shifted & 0xFF;
        state.Ctr--;
        SetCr0(state, unchecked(count - state.Gpr[10]));
        state.Pc = state.Ctr != 0 ? pc : pc + 0x6C;

        AdvanceFastForwardedInstructions(state, bus, 28);
        skippedInstructions = 28;
        return true;
    }

    private static bool MatchesSonicBitScanRow(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x7CA3_2214
        && bus.Read32(pc + 0x04) == 0x88E5_0000
        && bus.Read32(pc + 0x08) == 0x38C0_0000
        && bus.Read32(pc + 0x10) == 0x4082_0028
        && bus.Read32(pc + 0x48) == 0x38C6_0001
        && bus.Read32(pc + 0x58) == 0x7C06_5000
        && bus.Read32(pc + 0x60) == 0x7CCA_3378
        && bus.Read32(pc + 0x64) == 0x3884_0003
        && bus.Read32(pc + 0x68) == 0x4200_FF98;

    private static bool TryFastForwardSonicBitScanSetup(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint source = state.Gpr[3];
        if (!CanFastForwardInstructionCount(state, iterations: 24, instructionsPerIteration: 28, extraInstructions: 5)
            || !bus.Memory.IsMainRamAddress(source, 72))
        {
            return false;
        }

        uint bestCount = state.Gpr[10];
        uint lastCount = 24;
        uint lastValue = 0;
        uint offset = 2;
        for (int row = 0; row < 24; row++)
        {
            uint count = 0;
            byte value = bus.Memory.Read8(source + offset);
            if (value == 0)
            {
                count = 8;
                value = bus.Memory.Read8(source + offset - 1);
                if (value == 0)
                {
                    count = 16;
                    value = bus.Memory.Read8(source + offset - 2);
                    if (value == 0)
                    {
                        count = 24;
                    }
                }
            }

            uint shifted = value;
            if (shifted != 0)
            {
                while ((shifted & 0x80) == 0)
                {
                    shifted = (shifted << 1) & 0xFF;
                    count++;
                }
            }

            if (count < bestCount)
            {
                bestCount = count;
            }

            lastCount = count;
            lastValue = shifted & 0xFF;
            offset += 3;
        }

        state.Gpr[0] = lastValue == 0 ? 0u : 0x8000_0000;
        state.Gpr[4] = 74;
        state.Gpr[5] = lastValue;
        state.Gpr[6] = lastCount;
        state.Gpr[7] = lastValue;
        state.Gpr[10] = bestCount;
        state.Ctr = 0;
        SetCr0(state, unchecked(lastCount - bestCount));
        state.Pc = pc + 0x78;

        const uint skipped = 24 * 28 + 5;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = (int)skipped;
        return true;
    }

    private static bool MatchesSonicBitScanSetup(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x3800_0018
        && bus.Read32(pc + 0x04) == 0x7C09_03A6
        && bus.Read32(pc + 0x08) == 0x3880_0002
        && bus.Read32(pc + 0x0C) == 0x7CA3_2214
        && bus.Read32(pc + 0x10) == 0x88E5_0000
        && bus.Read32(pc + 0x14) == 0x38C0_0000
        && bus.Read32(pc + 0x1C) == 0x4082_0028
        && bus.Read32(pc + 0x4C) == 0x38C6_0001
        && bus.Read32(pc + 0x5C) == 0x7C06_5000
        && bus.Read32(pc + 0x68) == 0x7CCA_3378
        && bus.Read32(pc + 0x70) == 0x3884_0003
        && bus.Read32(pc + 0x74) == 0x4200_FF98;

    private static bool TryFastForwardSonicBitUnpackByte(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint bitIndex = state.Gpr[12];
        uint outputBit = state.Gpr[11];
        uint source = state.Gpr[3];
        uint destination = state.Gpr[6];
        if (outputBit > 8
            || state.Gpr[8] >= 3
            || state.Gpr[7] >= 24
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 130, extraInstructions: 0)
            || !bus.Memory.IsMainRamAddress(source, 4)
            || !bus.Memory.IsMainRamAddress(destination, 1))
        {
            return false;
        }

        uint result = state.Gpr[31] & (outputBit == 0 ? 0u : (1u << (int)outputBit) - 1u);
        while (outputBit < 8)
        {
            if (bitIndex < 24)
            {
                byte sourceByte = bus.Memory.Read8(source + bitIndex / 8);
                result |= (uint)((sourceByte >> (int)(bitIndex & 7)) & 1) << (int)outputBit;
            }

            bitIndex++;
            outputBit++;
        }

        byte value = (byte)result;
        bus.Memory.Write8(destination, value);
        state.Gpr[31] = value;
        state.Gpr[11] = 8;
        state.Gpr[12] = bitIndex;
        state.Gpr[8]++;
        state.Gpr[5] += 8;
        state.Gpr[6]++;
        state.Ctr = 0;

        if (state.Gpr[8] < 3)
        {
            SetCr0(state, unchecked(state.Gpr[8] - 3));
            state.Pc = pc - 0x14;
        }
        else
        {
            state.Gpr[7]++;
            state.Gpr[3] += 3;
            SetCr0(state, unchecked(state.Gpr[7] - 24));
            state.Pc = state.Gpr[7] < 24 ? pc - 0x20 : pc + 0x13C;
        }

        AdvanceFastForwardedInstructions(state, bus, 130);
        skippedInstructions = 130;
        return true;
    }

    private static bool MatchesSonicBitUnpackByte(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x2C0C_0018
        && bus.Read32(pc + 0x04) == 0x4080_0030
        && bus.Read32(pc + 0x20) == 0x7C03_00AE
        && bus.Read32(pc + 0x2C) == 0x7C00_5830
        && bus.Read32(pc + 0x38) == 0x398C_0001
        && bus.Read32(pc + 0x110) == 0x4200_FEF0
        && bus.Read32(pc + 0x114) == 0x3908_0001
        && bus.Read32(pc + 0x118) == 0x9BE6_0000
        && bus.Read32(pc + 0x120) == 0x38A5_0008
        && bus.Read32(pc + 0x124) == 0x38C6_0001
        && bus.Read32(pc + 0x128) == 0x4180_FEC4;

    private static bool MatchesSonicBitUnpackRows(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x38C3_0000
        && bus.Read32(pc + 0x04) == 0x3900_0000
        && bus.Read32(pc + 0x08) == 0x3800_0002
        && bus.Read32(pc + 0x0C) == 0x7C09_03A6
        && bus.Read32(pc + 0x10) == 0x3985_0000
        && bus.Read32(pc + 0x14) == 0x3BE0_0000
        && bus.Read32(pc + 0x18) == 0x3960_0000
        && bus.Read32(pc + 0x1C) == 0x2C0C_0018
        && bus.Read32(pc + 0x20) == 0x4080_0030
        && bus.Read32(pc + 0x40) == 0x7C03_00AE
        && bus.Read32(pc + 0x4C) == 0x7C00_5830
        && bus.Read32(pc + 0x58) == 0x398C_0001
        && bus.Read32(pc + 0x134) == 0x3908_0001
        && bus.Read32(pc + 0x138) == 0x9BE6_0000
        && bus.Read32(pc + 0x13C) == 0x2C08_0003
        && bus.Read32(pc + 0x140) == 0x38A5_0008
        && bus.Read32(pc + 0x144) == 0x38C6_0001
        && bus.Read32(pc + 0x148) == 0x4180_FEC4
        && bus.Read32(pc + 0x14C) == 0x38E7_0001
        && bus.Read32(pc + 0x150) == 0x2C07_0018
        && bus.Read32(pc + 0x154) == 0x3863_0003
        && bus.Read32(pc + 0x158) == 0x4180_FEA8;

    private static byte GatherEightBits(GameCubeMemory memory, uint baseAddress, uint firstBit)
    {
        uint result = 0;
        for (uint bit = 0; bit < 8; bit++)
        {
            uint bitIndex = firstBit + bit;
            byte sourceByte = memory.Read8(baseAddress + bitIndex / 8);
            result |= (uint)((sourceByte >> (int)(bitIndex & 7)) & 1) << (int)bit;
        }

        return (byte)result;
    }

    private static bool TryFastForwardSonicTickWaitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (bus.Read32(pc + 0x00) != 0x4BFD_B905
            || bus.Read32(pc + 0x04) != 0x80AD_8DC0
            || bus.Read32(pc + 0x08) != 0x808D_8DC4
            || bus.Read32(pc + 0x0C) != 0x3804_FFFF
            || bus.Read32(pc + 0x10) != 0x7C05_0214
            || bus.Read32(pc + 0x14) != 0x7C03_0040
            || bus.Read32(pc + 0x18) != 0x4081_FFE8
            || bus.Read32(pc - 0x246FC) != 0x806D_8B00
            || bus.Read32(pc - 0x246F8) != 0x4E80_0020)
        {
            return false;
        }

        uint tickAddress = unchecked(state.Gpr[13] + (uint)(short)0x8B00);
        uint baseAddress = unchecked(state.Gpr[13] + (uint)(short)0x8DC0);
        uint delayAddress = unchecked(state.Gpr[13] + (uint)(short)0x8DC4);
        uint callbackAddress = unchecked(state.Gpr[13] + (uint)(short)0x8B14);
        uint activeFlagAddress = unchecked(state.Gpr[13] + (uint)(short)0x85E8);
        if (!bus.Memory.IsMainRamAddress(tickAddress, sizeof(uint))
            || !bus.Memory.IsMainRamAddress(baseAddress, sizeof(uint))
            || !bus.Memory.IsMainRamAddress(delayAddress, sizeof(uint)))
        {
            return false;
        }

        uint tick = bus.Memory.Read32(tickAddress);
        uint baseTick = bus.Memory.Read32(baseAddress);
        uint delay = bus.Memory.Read32(delayAddress);
        uint threshold = unchecked(baseTick + delay - 1);
        if (tick > threshold)
        {
            return false;
        }

        uint nextTick = threshold + 1;
        uint waitTicks = unchecked(nextTick - tick);
        if (waitTicks == 0 || waitTicks > 1_000_000 || !CanFastForwardInstructionCount(state, waitTicks, 8, 0))
        {
            return false;
        }

        bus.Memory.Write32(tickAddress, nextTick);
        if (bus.Memory.IsMainRamAddress(callbackAddress, sizeof(uint))
            && bus.Memory.IsMainRamAddress(activeFlagAddress, sizeof(uint))
            && bus.Memory.Read32(callbackAddress) == 0x8002_2F5C)
        {
            uint activeFlag = bus.Memory.Read32(activeFlagAddress);
            if (activeFlag != 0)
            {
                bus.Memory.Write32(activeFlagAddress, activeFlag - 1);
            }
        }

        state.Gpr[0] = threshold;
        state.Gpr[3] = nextTick;
        state.Gpr[4] = delay;
        state.Gpr[5] = baseTick;
        state.Pc = pc + 0x1C;
        SetCr0(state, 1);
        uint skipped = checked(waitTicks * 8);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardSonicCallbackWaitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (bus.Read32(pc + 0x00) != 0x800D_85E8
            || bus.Read32(pc + 0x04) != 0x2C00_0000
            || bus.Read32(pc + 0x08) != 0x4181_FFF8
            || bus.Read32(pc + 0x0C) != 0x800D_85F0
            || bus.Read32(pc + 0x10) != 0x900D_85E8
            || bus.Read32(pc + 0x14) != 0x4E80_0020)
        {
            return false;
        }

        uint callbackAddress = unchecked(state.Gpr[13] + (uint)(short)0x8B14);
        uint activeFlagAddress = unchecked(state.Gpr[13] + (uint)(short)0x85E8);
        if (!bus.Memory.IsMainRamAddress(callbackAddress, sizeof(uint))
            || !bus.Memory.IsMainRamAddress(activeFlagAddress, sizeof(uint))
            || bus.Memory.Read32(callbackAddress) != 0x8002_2F5C)
        {
            return false;
        }

        uint activeFlag = bus.Memory.Read32(activeFlagAddress);
        if (activeFlag == 0 || activeFlag > 1024 || !CanFastForwardInstructionCount(state, activeFlag, 3, 0))
        {
            return false;
        }

        bus.Memory.Write32(activeFlagAddress, 0);
        state.Gpr[0] = 0;
        state.Pc = pc + 0x0C;
        SetCr0(state, 0);
        uint skipped = checked(activeFlag * 3);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardSonicDotProductLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicDotProductLoop(bus, pc))
        {
            return false;
        }

        uint iterations = state.Gpr[0];
        uint fixedVector = state.Gpr[6];
        uint input = state.Gpr[4];
        uint destination = state.Gpr[5];
        uint accumulatorAddress = unchecked(state.Gpr[2] + (uint)(short)0xAB68);
        if (iterations == 0
            || iterations > 1024
            || !CanFastForwardInstructionCount(state, iterations, 105, 1)
            || !bus.Memory.IsMainRamAddress(fixedVector, 0x80)
            || !bus.Memory.IsMainRamAddress(input, checked((int)(iterations * 0x80)))
            || !bus.Memory.IsMainRamAddress(destination, checked((int)(iterations * sizeof(uint))))
            || !bus.Memory.IsMainRamAddress(accumulatorAddress, sizeof(uint)))
        {
            return false;
        }

        float initialAccumulator = ReadSingle(bus.Memory, accumulatorAddress);
        uint currentInput = input;
        for (uint iteration = 0; iteration < iterations; iteration++)
        {
            float sum = initialAccumulator;
            for (uint offset = 0; offset <= 0x4C; offset += sizeof(uint))
            {
                sum = (float)(ReadSingle(bus.Memory, fixedVector + offset) * ReadSingle(bus.Memory, currentInput + offset) + sum);
            }

            for (uint offset = 0; offset <= 0x2C; offset += sizeof(uint))
            {
                sum = (float)(ReadSingle(bus.Memory, fixedVector + 0x50 + offset) * ReadSingle(bus.Memory, currentInput + 0x50 + offset) + sum);
            }

            WriteSingle(bus.Memory, destination + iteration * sizeof(uint), sum);
            currentInput += 0x80;
        }

        state.Gpr[0] = 0;
        state.Gpr[3] = fixedVector + 0x50;
        state.Gpr[4] = input + iterations * 0x80;
        state.Gpr[5] = destination + iterations * sizeof(uint);
        state.Pc = state.Lr;
        SetCr0(state, 0);
        SetCarry(state, carry: true);
        uint skipped = checked(iterations * 105 + 1);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicDotProductLoop(GameCubeBus bus, uint pc) =>
        bus.Read32(pc - 0x08) == 0x38C3_0000
        && bus.Read32(pc - 0x04) == 0x3800_0040
        && bus.Read32(pc + 0x00) == 0xC064_0000
        && bus.Read32(pc + 0x04) == 0x3866_0004
        && bus.Read32(pc + 0x08) == 0xC002_AB68
        && bus.Read32(pc + 0x10) == 0xC086_0000
        && bus.Read32(pc + 0x0EC) == 0xC024_004C
        && bus.Read32(pc + 0x0F0) == 0xC046_004C
        && bus.Read32(pc + 0x0F4) == 0x3884_0050
        && bus.Read32(pc + 0x0F8) == 0xEC04_00FA
        && bus.Read32(pc + 0x0FC) == 0xEC02_007A
        && bus.Read32(pc + 0x100) == 0xC083_0000
        && bus.Read32(pc + 0x104) == 0x3400_FFFF
        && bus.Read32(pc + 0x190) == 0xEC04_00FA
        && bus.Read32(pc + 0x194) == 0xEC02_007A
        && bus.Read32(pc + 0x198) == 0xD005_0000
        && bus.Read32(pc + 0x19C) == 0x38A5_0004
        && bus.Read32(pc + 0x1A0) == 0x4082_FE60
        && bus.Read32(pc + 0x1A4) == 0x4E80_0020;

    private static bool TryFastForwardSonicResourceTableLookup(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicResourceTableLookup(bus, pc))
        {
            return false;
        }

        uint countAddress = unchecked(state.Gpr[13] - 29196u);
        uint tableAddressAddress = unchecked(state.Gpr[13] - 29192u);
        if (!bus.Memory.IsMainRamAddress(countAddress, sizeof(uint))
            || !bus.Memory.IsMainRamAddress(tableAddressAddress, sizeof(uint)))
        {
            return false;
        }

        uint entryCount = bus.Memory.Read32(countAddress);
        uint tableAddress = bus.Memory.Read32(tableAddressAddress);
        const uint maxEntries = 0x10000;
        if (entryCount > maxEntries
            || (entryCount != 0 && !bus.Memory.IsMainRamAddress(tableAddress, checked((int)(entryCount * 0x18u)))))
        {
            return false;
        }

        uint key = state.Gpr[3];
        uint result = 0xFFFF_FFFF;
        uint finalEntryAddress = tableAddress;
        uint finalPointer = 0;
        uint examined = 0;
        bool matched = false;

        for (; examined < entryCount; examined++)
        {
            uint entryAddress = tableAddress + examined * 0x18u;
            finalEntryAddress = entryAddress;
            finalPointer = bus.Memory.Read32(entryAddress + 0x0C);
            if (finalPointer == 0 || !bus.Memory.IsMainRamAddress(finalPointer, sizeof(uint)))
            {
                continue;
            }

            uint candidateKey = bus.Memory.Read32(finalPointer);
            if (candidateKey == key)
            {
                result = examined;
                matched = true;
                break;
            }
        }

        uint skipped = matched
            ? checked(9u + examined * 9u + 8u)
            : checked(9u + entryCount * 6u);
        if (!CanFastForwardInstructionCount(state, skipped, instructionsPerIteration: 1, extraInstructions: 0))
        {
            return false;
        }

        state.Gpr[3] = result;
        state.Gpr[4] = matched ? finalEntryAddress : tableAddress + entryCount * 0x18u;
        state.Gpr[5] = finalPointer;
        state.Gpr[6] = matched ? examined : entryCount;
        state.Ctr = matched ? entryCount - examined : 0;
        SetCr0(state, result);
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicResourceTableLookup(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x38C0_0000
        && bus.Read32(pc + 0x04) == 0x808D_8DF8
        && bus.Read32(pc + 0x08) == 0x800D_8DF4
        && bus.Read32(pc + 0x10) == 0x7C09_03A6
        && bus.Read32(pc + 0x14) == 0x2C00_0000
        && bus.Read32(pc + 0x20) == 0x80A4_000C
        && bus.Read32(pc + 0x24) == 0x2805_0000
        && bus.Read32(pc + 0x2C) == 0x8005_0000
        && bus.Read32(pc + 0x30) == 0x7C03_0040
        && bus.Read32(pc + 0x38) == 0x7CC3_3378
        && bus.Read32(pc + 0x40) == 0x3884_0018
        && bus.Read32(pc + 0x44) == 0x38C6_0001
        && bus.Read32(pc + 0x48) == 0x4200_FFD8
        && bus.Read32(pc + 0x4C) == 0x3860_FFFF
        && bus.Read32(pc + 0x50) == 0x4E80_0020;

    private static bool TryFastForwardSonicGxAttributeFlush(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        bool fromEntry = MatchesSonicGxAttributeFlushEntry(bus, pc);
        bool fromLoop = MatchesSonicGxAttributeFlushLoop(bus, pc);
        if (!fromEntry && !fromLoop)
        {
            return false;
        }

        uint baseAddress;
        uint startSlot;
        if (fromEntry)
        {
            uint globalAddressAddress = unchecked(state.Gpr[13] - 31872u);
            if (!bus.Memory.IsMainRamAddress(globalAddressAddress, sizeof(uint)))
            {
                return false;
            }

            baseAddress = bus.Memory.Read32(globalAddressAddress);
            startSlot = 0;
        }
        else
        {
            baseAddress = state.Gpr[10];
            startSlot = state.Gpr[12] & 0xFF;
            if (startSlot >= 8 || state.Gpr[11] != startSlot * sizeof(uint))
            {
                return false;
            }
        }

        if (!bus.Memory.IsMainRamAddress(baseAddress + 0x4EE, 1)
            || !bus.Memory.IsMainRamAddress(baseAddress + 0x5C, 0x20))
        {
            return false;
        }

        byte flags = bus.Memory.Read8(baseAddress + 0x4EE);
        int activeSlots = 0;
        for (int slot = (int)startSlot; slot < 8; slot++)
        {
            activeSlots += (flags >> slot) & 1;
        }

        uint skipped = checked((fromEntry ? 7u : 0u) + (8u - startSlot) * 7u + (uint)activeSlots * 12u);
        if (!CanFastForwardInstructionCount(state, skipped, instructionsPerIteration: 1, extraInstructions: 0))
        {
            return false;
        }

        const uint fifo = 0xCC00_8000;
        for (int slot = (int)startSlot; slot < 8; slot++)
        {
            if ((flags & (1 << slot)) == 0)
            {
                continue;
            }

            uint offset = (uint)(slot * sizeof(uint));
            bus.Write8(fifo, 0x08);
            bus.Write8(fifo, (byte)(0x70 | slot));
            bus.Write32(fifo, bus.Memory.Read32(baseAddress + 0x1C + offset));
            bus.Write8(fifo, 0x08);
            bus.Write8(fifo, (byte)(0x80 | slot));
            bus.Write32(fifo, bus.Memory.Read32(baseAddress + 0x3C + offset));
            bus.Write8(fifo, 0x08);
            bus.Write8(fifo, (byte)(0x90 | slot));
            bus.Write32(fifo, bus.Memory.Read32(baseAddress + 0x5C + offset));
        }

        bus.Memory.Write8(baseAddress + 0x4EE, 0);
        state.Gpr[0] = 0;
        state.Gpr[3] = baseAddress;
        state.Gpr[7] = 0xCC01_0000;
        state.Gpr[10] = baseAddress;
        state.Gpr[11] = 32;
        state.Gpr[12] = 8;
        state.Pc = state.Lr;
        SetCr0(state, 0);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicGxAttributeFlushEntry(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x814D_8380
        && bus.Read32(pc + 0x04) == 0x3980_0000
        && bus.Read32(pc + 0x08) == 0x3960_0000
        && bus.Read32(pc + 0x0C) == 0x3CE0_CC01
        && bus.Read32(pc + 0x10) == 0x4800_0070
        && MatchesSonicGxAttributeFlushLoop(bus, pc + 0x14);

    private static bool MatchesSonicGxAttributeFlushLoop(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x5589_063E
        && bus.Read32(pc + 0x04) == 0x886A_04EE
        && bus.Read32(pc + 0x10) == 0x7C60_0039
        && bus.Read32(pc + 0x14) == 0x4182_0050
        && bus.Read32(pc + 0x28) == 0x9867_8000
        && bus.Read32(pc + 0x5C) == 0x7C0A_002E
        && bus.Read32(pc + 0x68) == 0x398C_0001
        && bus.Read32(pc + 0x70) == 0x2800_0008
        && bus.Read32(pc + 0x74) == 0x4180_FF8C
        && bus.Read32(pc + 0x78) == 0x806D_8380
        && bus.Read32(pc + 0x7C) == 0x3800_0000
        && bus.Read32(pc + 0x80) == 0x9803_04EE
        && bus.Read32(pc + 0x84) == 0x4E80_0020;

    private static bool TryFastForwardSonicGxTexObjLoadNoCallback(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxTexObjLoadNoCallback(bus, state.Pc)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicGxTexObjLoadNoCallbackInstructions, extraInstructions: 0))
        {
            return false;
        }

        uint textureObject = state.Gpr[3];
        uint samplerObject = state.Gpr[4];
        uint textureMap = state.Gpr[5];
        if (textureMap > 7
            || !bus.Memory.IsMainRamAddress(textureObject, 0x20)
            || !bus.Memory.IsMainRamAddress(samplerObject, 8))
        {
            return false;
        }

        byte objectFlags = bus.Memory.Read8(textureObject + 0x1F);
        if ((objectFlags & 0x02) == 0)
        {
            return false;
        }

        uint stackPointer = state.Gpr[1];
        uint newStackPointer = unchecked(stackPointer - 40);
        if (!bus.Memory.IsMainRamAddress(newStackPointer, 48))
        {
            return false;
        }

        uint stateBlockPointerAddress = unchecked(state.Gpr[13] + 0xFFFF_8380u);
        if (!bus.Memory.IsMainRamAddress(stateBlockPointerAddress, sizeof(uint)))
        {
            return false;
        }

        uint stateBlock = bus.Memory.Read32(stateBlockPointerAddress);
        if (!bus.Memory.IsMainRamAddress(stateBlock, 0x4F4))
        {
            return false;
        }

        uint table0 = unchecked(state.Gpr[13] + 0xFFFF_8398u);
        uint table1 = unchecked(state.Gpr[13] + 0xFFFF_83A0u);
        uint table2 = unchecked(state.Gpr[13] + 0xFFFF_83A8u);
        uint table3 = unchecked(state.Gpr[13] + 0xFFFF_83B0u);
        uint table4 = unchecked(state.Gpr[13] + 0xFFFF_83B8u);
        uint table5 = unchecked(state.Gpr[13] + 0xFFFF_83C0u);
        if (!bus.Memory.IsMainRamAddress(table0 + textureMap, 1)
            || !bus.Memory.IsMainRamAddress(table1 + textureMap, 1)
            || !bus.Memory.IsMainRamAddress(table2 + textureMap, 1)
            || !bus.Memory.IsMainRamAddress(table3 + textureMap, 1)
            || !bus.Memory.IsMainRamAddress(table4 + textureMap, 1)
            || !bus.Memory.IsMainRamAddress(table5 + textureMap, 1))
        {
            return false;
        }

        uint oldLr = state.Lr;
        uint oldR28 = state.Gpr[28];
        uint oldR29 = state.Gpr[29];
        uint oldR30 = state.Gpr[30];
        uint oldR31 = state.Gpr[31];

        bus.Write32(stackPointer + 4, oldLr);
        bus.Write32(newStackPointer, stackPointer);
        bus.Write32(newStackPointer + 36, oldR31);
        bus.Write32(newStackPointer + 32, oldR30);
        bus.Write32(newStackPointer + 28, oldR29);
        bus.Write32(newStackPointer + 24, oldR28);

        uint originalWord8 = bus.Memory.Read32(textureObject + 0x08);
        uint originalSampler0 = bus.Memory.Read32(samplerObject + 0x00);
        uint originalSampler4 = bus.Memory.Read32(samplerObject + 0x04);
        uint word0 = ReplaceTopByte(bus.Memory.Read32(textureObject + 0x00), bus.Memory.Read8(table0 + textureMap));
        uint word4 = ReplaceTopByte(bus.Memory.Read32(textureObject + 0x04), bus.Memory.Read8(table1 + textureMap));
        uint word8 = ReplaceTopByte(originalWord8, bus.Memory.Read8(table2 + textureMap));
        uint sampler0 = ReplaceTopByte(originalSampler0, bus.Memory.Read8(table3 + textureMap));
        uint sampler4 = ReplaceTopByte(originalSampler4, bus.Memory.Read8(table4 + textureMap));
        uint word12 = ReplaceTopByte(bus.Memory.Read32(textureObject + 0x0C), bus.Memory.Read8(table5 + textureMap));

        bus.Memory.Write32(textureObject + 0x00, word0);
        bus.Memory.Write32(textureObject + 0x04, word4);
        bus.Memory.Write32(textureObject + 0x08, word8);
        bus.Memory.Write32(samplerObject + 0x00, sampler0);
        bus.Memory.Write32(samplerObject + 0x04, sampler4);
        bus.Memory.Write32(textureObject + 0x0C, word12);

        const uint fifo = 0xCC00_8000;
        bus.Write8(fifo, 0x61);
        bus.Write32(fifo, word0);
        bus.Write8(fifo, 0x61);
        bus.Write32(fifo, word4);
        bus.Write8(fifo, 0x61);
        bus.Write32(fifo, word8);
        bus.Write8(fifo, 0x61);
        bus.Write32(fifo, sampler0);
        bus.Write8(fifo, 0x61);
        bus.Write32(fifo, sampler4);
        bus.Write8(fifo, 0x61);
        bus.Write32(fifo, word12);

        uint textureMapOffset = textureMap << 2;
        bus.Memory.Write32(stateBlock + 0x45C + textureMapOffset, word8);
        bus.Memory.Write32(stateBlock + 0x47C + textureMapOffset, word0);
        uint dirtyFlags = bus.Memory.Read32(stateBlock + 0x4F0) | 1u;
        bus.Memory.Write32(stateBlock + 0x4F0, dirtyFlags);
        bus.Memory.Write16(stateBlock + 2, 0);

        state.Gpr[0] = oldLr;
        state.Gpr[1] = stackPointer;
        state.Gpr[3] = stateBlock;
        state.Gpr[4] = stateBlock;
        state.Gpr[5] = textureMapOffset;
        state.Gpr[6] = originalSampler4;
        state.Gpr[7] = originalSampler0;
        state.Gpr[8] = originalWord8;
        state.Gpr[28] = oldR28;
        state.Gpr[29] = oldR29;
        state.Gpr[30] = oldR30;
        state.Gpr[31] = oldR31;
        state.Lr = oldLr;
        state.Pc = oldLr & 0xFFFF_FFFCu;
        SetCr0(state, (uint)(objectFlags & 0x02));

        AdvanceFastForwardedInstructions(state, bus, SonicGxTexObjLoadNoCallbackInstructions);
        skippedInstructions = checked((int)SonicGxTexObjLoadNoCallbackInstructions);
        return true;
    }

    private static bool MatchesSonicGxTexObjLoadNoCallback(GameCubeBus bus, uint pc) =>
        pc == SonicGxTexObjLoadNoCallbackPc
        && bus.Read32(pc + 0x00) == 0x7C08_02A6
        && bus.Read32(pc + 0x04) == 0x38ED_83A8
        && bus.Read32(pc + 0x08) == 0x9001_0004
        && bus.Read32(pc + 0x0C) == 0x9421_FFD8
        && bus.Read32(pc + 0x10) == 0x93E1_0024
        && bus.Read32(pc + 0x14) == 0x3FE0_CC01
        && bus.Read32(pc + 0x18) == 0x93C1_0020
        && bus.Read32(pc + 0x1C) == 0x3BC0_0061
        && bus.Read32(pc + 0x20) == 0x93A1_001C
        && bus.Read32(pc + 0x24) == 0x3BA5_0000
        && bus.Read32(pc + 0x28) == 0x38AD_83B8
        && bus.Read32(pc + 0x2C) == 0x9381_0018
        && bus.Read32(pc + 0x30) == 0x7C7C_1B78
        && bus.Read32(pc + 0x34) == 0x80C3_0000
        && bus.Read32(pc + 0x38) == 0x386D_8398
        && bus.Read32(pc + 0x3C) == 0x7C03_E8AE
        && bus.Read32(pc + 0x40) == 0x386D_83A0
        && bus.Read32(pc + 0x44) == 0x5400_C00E
        && bus.Read32(pc + 0x48) == 0x50C0_023E
        && bus.Read32(pc + 0x4C) == 0x901C_0000
        && bus.Read32(pc + 0x50) == 0x38CD_83B0
        && bus.Read32(pc + 0x54) == 0x7C03_E8AE
        && bus.Read32(pc + 0x58) == 0x386D_83C0
        && bus.Read32(pc + 0x5C) == 0x811C_0004
        && bus.Read32(pc + 0x60) == 0x5400_C00E
        && bus.Read32(pc + 0x64) == 0x5100_023E
        && bus.Read32(pc + 0x68) == 0x901C_0004
        && bus.Read32(pc + 0x6C) == 0x7C07_E8AE
        && bus.Read32(pc + 0x70) == 0x811C_0008
        && bus.Read32(pc + 0x74) == 0x5400_C00E
        && bus.Read32(pc + 0x78) == 0x5100_023E
        && bus.Read32(pc + 0x7C) == 0x901C_0008
        && bus.Read32(pc + 0x80) == 0x7C06_E8AE
        && bus.Read32(pc + 0x84) == 0x80E4_0000
        && bus.Read32(pc + 0x88) == 0x5400_C00E
        && bus.Read32(pc + 0x8C) == 0x50E0_023E
        && bus.Read32(pc + 0x90) == 0x9004_0000
        && bus.Read32(pc + 0x94) == 0x7C05_E8AE
        && bus.Read32(pc + 0x98) == 0x80C4_0004
        && bus.Read32(pc + 0x9C) == 0x5400_C00E
        && bus.Read32(pc + 0xA0) == 0x50C0_023E
        && bus.Read32(pc + 0xA4) == 0x9004_0004
        && bus.Read32(pc + 0xA8) == 0x7C03_E8AE
        && bus.Read32(pc + 0xAC) == 0x80BC_000C
        && bus.Read32(pc + 0xB0) == 0x5400_C00E
        && bus.Read32(pc + 0xB4) == 0x50A0_023E
        && bus.Read32(pc + 0xB8) == 0x901C_000C
        && bus.Read32(pc + 0xBC) == 0x9BDF_8000
        && bus.Read32(pc + 0xC0) == 0x801C_0000
        && bus.Read32(pc + 0xC4) == 0x901F_8000
        && bus.Read32(pc + 0xC8) == 0x9BDF_8000
        && bus.Read32(pc + 0xCC) == 0x801C_0004
        && bus.Read32(pc + 0xD0) == 0x901F_8000
        && bus.Read32(pc + 0xD4) == 0x9BDF_8000
        && bus.Read32(pc + 0xD8) == 0x801C_0008
        && bus.Read32(pc + 0xDC) == 0x901F_8000
        && bus.Read32(pc + 0xE0) == 0x9BDF_8000
        && bus.Read32(pc + 0xE4) == 0x8004_0000
        && bus.Read32(pc + 0xE8) == 0x901F_8000
        && bus.Read32(pc + 0xEC) == 0x9BDF_8000
        && bus.Read32(pc + 0xF0) == 0x8004_0004
        && bus.Read32(pc + 0xF4) == 0x901F_8000
        && bus.Read32(pc + 0xF8) == 0x9BDF_8000
        && bus.Read32(pc + 0xFC) == 0x801C_000C
        && bus.Read32(pc + 0x100) == 0x901F_8000
        && bus.Read32(pc + 0x104) == 0x881C_001F
        && bus.Read32(pc + 0x108) == 0x5400_07BD
        && bus.Read32(pc + 0x10C) == 0x4082_003C
        && bus.Read32(pc + 0x148) == 0x806D_8380
        && bus.Read32(pc + 0x14C) == 0x57A5_103A
        && bus.Read32(pc + 0x150) == 0x809C_0008
        && bus.Read32(pc + 0x154) == 0x3800_0000
        && bus.Read32(pc + 0x158) == 0x7C63_2A14
        && bus.Read32(pc + 0x15C) == 0x9083_045C
        && bus.Read32(pc + 0x160) == 0x806D_8380
        && bus.Read32(pc + 0x164) == 0x809C_0000
        && bus.Read32(pc + 0x168) == 0x7C63_2A14
        && bus.Read32(pc + 0x16C) == 0x9083_047C
        && bus.Read32(pc + 0x170) == 0x808D_8380
        && bus.Read32(pc + 0x174) == 0x8064_04F0
        && bus.Read32(pc + 0x178) == 0x6063_0001
        && bus.Read32(pc + 0x17C) == 0x9064_04F0
        && bus.Read32(pc + 0x180) == 0x806D_8380
        && bus.Read32(pc + 0x184) == 0xB003_0002
        && bus.Read32(pc + 0x188) == 0x8001_002C
        && bus.Read32(pc + 0x18C) == 0x83E1_0024
        && bus.Read32(pc + 0x190) == 0x83C1_0020
        && bus.Read32(pc + 0x194) == 0x83A1_001C
        && bus.Read32(pc + 0x198) == 0x8381_0018
        && bus.Read32(pc + 0x19C) == 0x3821_0028
        && bus.Read32(pc + 0x1A0) == 0x7C08_03A6
        && bus.Read32(pc + 0x1A4) == 0x4E80_0020;

    private static bool TryFastForwardSonicGxPackedStateSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxPackedStateSetter(bus, state.Pc))
        {
            return false;
        }

        uint mode = state.Gpr[3];
        uint skipped = mode switch
        {
            1 => 62,
            3 => 64,
            _ => 65,
        };
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        uint stateBlockPointerAddress = unchecked(state.Gpr[13] + 0xFFFF_8380u);
        if (!bus.Memory.IsMainRamAddress(stateBlockPointerAddress, sizeof(uint)))
        {
            return false;
        }

        uint stateBlock = bus.Memory.Read32(stateBlockPointerAddress);
        uint packedAddress = stateBlock + 0x1D0;
        if (!bus.Memory.IsMainRamAddress(stateBlock, 0x1D4))
        {
            return false;
        }

        uint modeEnable = mode is 1 or 3 ? 1u : 0u;
        uint mode3LeadingZeroes = (uint)uint.LeadingZeroCount(unchecked(3u - mode));
        uint mode2LeadingZeroes = (uint)uint.LeadingZeroCount(unchecked(2u - mode));
        uint arg6Bits = Rlwinm(state.Gpr[6], 12, 0, 19);
        uint arg4Bits = Rlwinm(state.Gpr[4], 8, 0, 23);
        uint arg5Bits = Rlwinm(state.Gpr[5], 5, 0, 26);

        uint word = bus.Memory.Read32(packedAddress);
        word = Rlwinm(word, 0, 0, 30) | modeEnable;
        bus.Memory.Write32(packedAddress, word);

        word = bus.Memory.Read32(packedAddress);
        word = Rlwinm(word, 0, 21, 19) | Rlwinm(mode3LeadingZeroes, 6, 0, 20);
        bus.Memory.Write32(packedAddress, word);

        word = bus.Memory.Read32(packedAddress);
        word = Rlwinm(word, 0, 31, 29) | Rlwinm(mode2LeadingZeroes, 28, 4, 30);
        bus.Memory.Write32(packedAddress, word);

        word = bus.Memory.Read32(packedAddress);
        word = Rlwinm(word, 0, 20, 15) | arg6Bits;
        bus.Memory.Write32(packedAddress, word);

        word = bus.Memory.Read32(packedAddress);
        word = Rlwinm(word, 0, 24, 20) | arg4Bits;
        bus.Memory.Write32(packedAddress, word);

        word = bus.Memory.Read32(packedAddress);
        word = Rlwinm(word, 0, 27, 23) | arg5Bits;
        bus.Memory.Write32(packedAddress, word);

        word = bus.Memory.Read32(packedAddress);
        word = Rlwinm(word, 0, 8, 31) | 0x4100_0000u;
        bus.Memory.Write32(packedAddress, word);

        const uint fifo = 0xCC00_8000;
        bus.Write8(fifo, 0x61);
        bus.Write32(fifo, word);
        bus.Memory.Write16(stateBlock + 2, 0);

        state.Gpr[0] = 0;
        state.Gpr[3] = word;
        state.Gpr[4] = stateBlock;
        state.Gpr[5] = 0xCC01_0000;
        state.Gpr[6] = packedAddress;
        state.Gpr[7] = packedAddress;
        state.Gpr[8] = packedAddress;
        state.Gpr[9] = packedAddress;
        state.Gpr[10] = packedAddress;
        state.Pc = state.Lr & 0xFFFF_FFFCu;
        SetCr0ForSignedCompareImmediate(state, mode, mode == 1 ? 1 : 3);

        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicGxPackedStateSetter(GameCubeBus bus, uint pc)
    {
        if (pc != SonicGxPackedStateSetterPc)
        {
            return false;
        }

        ReadOnlySpan<uint> instructions =
        [
            0x2C03_0001, 0x3920_0001, 0x4182_0010, 0x2C03_0003,
            0x4182_0008, 0x3920_0000, 0x810D_8380, 0x2003_0003,
            0x7C07_0034, 0x3948_01D0, 0x8108_01D0, 0x2003_0002,
            0x5503_003C, 0x7C63_4B78, 0x906A_0000, 0x7C00_0034,
            0x54E3_3028, 0x812D_8380, 0x5408_E13C, 0x8409_01D0,
            0x54C7_6026, 0x5486_402E, 0x5400_0566, 0x7C00_1B78,
            0x9009_0000, 0x54A4_2834, 0x3860_0061, 0x812D_8380,
            0x3CA0_CC01, 0x3800_0000, 0x3949_01D0, 0x8129_01D0,
            0x5529_07FA, 0x7D28_4378, 0x910A_0000, 0x810D_8380,
            0x3928_01D0, 0x8108_01D0, 0x5508_051E, 0x7D07_3B78,
            0x90E9_0000, 0x80ED_8380, 0x3907_01D0, 0x80E7_01D0,
            0x54E7_0628, 0x7CE6_3378, 0x90C8_0000, 0x80CD_8380,
            0x38E6_01D0, 0x80C6_01D0, 0x54C6_06EE, 0x7CC4_2378,
            0x9087_0000, 0x808D_8380, 0x38C4_01D0, 0x8084_01D0,
            0x5484_023E, 0x6484_4100, 0x9086_0000, 0x9865_8000,
            0x808D_8380, 0x8064_01D0, 0x9065_8000, 0xB004_0002,
            0x4E80_0020,
        ];
        for (int index = 0; index < instructions.Length; index++)
        {
            if (bus.Read32(pc + (uint)(index * sizeof(uint))) != instructions[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFastForwardSonicGxVertexEmitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicGxVertexEmitLoop(bus, pc))
        {
            return false;
        }

        uint vertices = state.Gpr[30];
        uint stream = state.Gpr[24];
        uint vertexBase = state.Gpr[25];
        uint extraStreamStride = state.Gpr[31];
        if (vertices == 0
            || vertices > 0x10000
            || extraStreamStride > 0x40
            || !CanFastForwardInstructionCount(state, vertices, SonicGxVertexEmitInstructionsPerVertex, extraInstructions: 0))
        {
            return false;
        }

        ulong streamBytes = (ulong)vertices * (6ul + extraStreamStride);
        if (streamBytes > int.MaxValue || !bus.Memory.IsMainRamAddress(stream, checked((int)streamBytes)))
        {
            return false;
        }

        uint currentStream = stream;
        for (uint vertex = 0; vertex < vertices; vertex++)
        {
            short index = unchecked((short)bus.Memory.Read16(currentStream));
            uint shiftedIndex = unchecked((uint)index << 5);
            uint vertexAddress = unchecked(vertexBase + shiftedIndex);
            if (!bus.Memory.IsMainRamAddress(vertexAddress, 0x1C))
            {
                return false;
            }

            currentStream += 6 + extraStreamStride;
        }

        currentStream = stream;
        uint lastVertexAddress = state.Gpr[27];
        uint lastFirstHalf = state.Gpr[29];
        uint lastSecondHalf = state.Gpr[28];
        uint lastShiftedIndex = state.Gpr[0];
        uint lastX = 0;
        uint lastY = 0;
        uint lastZ = 0;
        const uint fifo = 0xCC00_8000;

        for (uint vertex = 0; vertex < vertices; vertex++)
        {
            short index = unchecked((short)bus.Memory.Read16(currentStream));
            currentStream += sizeof(ushort);
            lastShiftedIndex = unchecked((uint)index << 5);
            uint vertexAddress = unchecked(vertexBase + lastShiftedIndex);
            lastFirstHalf = unchecked((uint)(short)bus.Memory.Read16(currentStream));
            currentStream += sizeof(ushort);
            lastSecondHalf = unchecked((uint)(short)bus.Memory.Read16(currentStream));
            currentStream += sizeof(ushort);
            lastX = bus.Memory.Read32(vertexAddress);
            lastY = bus.Memory.Read32(vertexAddress + 0x04);
            lastZ = bus.Memory.Read32(vertexAddress + 0x08);
            bus.Write32(fifo, lastX);
            bus.Write32(fifo, lastY);
            bus.Write32(fifo, lastZ);
            bus.Write32(fifo, bus.Memory.Read32(vertexAddress + 0x18));
            bus.Write16(fifo, (ushort)lastFirstHalf);
            bus.Write16(fifo, (ushort)lastSecondHalf);
            currentStream += extraStreamStride;
            lastVertexAddress = vertexAddress;
        }

        state.Gpr[0] = lastShiftedIndex;
        state.Gpr[3] = lastFirstHalf;
        state.Gpr[4] = lastSecondHalf;
        state.Gpr[5] = 0xCC01_0000;
        state.Gpr[24] = currentStream;
        state.Gpr[27] = lastVertexAddress;
        state.Gpr[28] = lastSecondHalf;
        state.Gpr[29] = lastFirstHalf;
        state.Gpr[30] = 0;
        state.Fpr[1] = BitConverter.Int32BitsToSingle(unchecked((int)lastX));
        state.Fpr[2] = BitConverter.Int32BitsToSingle(unchecked((int)lastY));
        state.Fpr[3] = BitConverter.Int32BitsToSingle(unchecked((int)lastZ));
        state.FprPair1[1] = state.Fpr[1];
        state.FprPair1[2] = state.Fpr[2];
        state.FprPair1[3] = state.Fpr[3];
        state.Pc = pc + 0x54;
        SetCr0(state, 0);

        uint skipped = checked(vertices * SonicGxVertexEmitInstructionsPerVertex);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicGxVertexEmitLoop(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0xA818_0000
        && bus.Read32(pc + 0x04) == 0x3B18_0002
        && bus.Read32(pc + 0x08) == 0x5400_2834
        && bus.Read32(pc + 0x0C) == 0x7F79_0214
        && bus.Read32(pc + 0x10) == 0xABB8_0000
        && bus.Read32(pc + 0x14) == 0x3B18_0002
        && bus.Read32(pc + 0x18) == 0xAB98_0000
        && bus.Read32(pc + 0x1C) == 0x3B18_0002
        && bus.Read32(pc + 0x20) == 0xC03B_0000
        && bus.Read32(pc + 0x24) == 0xC05B_0004
        && bus.Read32(pc + 0x28) == 0xC07B_0008
        && bus.Read32(pc + 0x2C) == 0x4800_0071
        && bus.Read32(pc + 0x30) == 0x807B_0018
        && bus.Read32(pc + 0x34) == 0x4800_005D
        && bus.Read32(pc + 0x38) == 0x7FA3_EB78
        && bus.Read32(pc + 0x3C) == 0x7F84_E378
        && bus.Read32(pc + 0x40) == 0x4800_0041
        && bus.Read32(pc + 0x44) == 0x7F18_FA14
        && bus.Read32(pc + 0x48) == 0x3BDE_FFFF
        && bus.Read32(pc + 0x4C) == 0x2C1E_0000
        && bus.Read32(pc + 0x50) == 0x4082_FFB0
        && bus.Read32(pc + 0x80) == 0x3CA0_CC01
        && bus.Read32(pc + 0x84) == 0xB065_8000
        && bus.Read32(pc + 0x88) == 0xB085_8000
        && bus.Read32(pc + 0x8C) == 0x4E80_0020
        && bus.Read32(pc + 0x90) == 0x3C80_CC01
        && bus.Read32(pc + 0x94) == 0x9064_8000
        && bus.Read32(pc + 0x98) == 0x4E80_0020
        && bus.Read32(pc + 0x9C) == 0x3C60_CC01
        && bus.Read32(pc + 0xA0) == 0xD023_8000
        && bus.Read32(pc + 0xA4) == 0xD043_8000
        && bus.Read32(pc + 0xA8) == 0xD063_8000
        && bus.Read32(pc + 0xAC) == 0x4E80_0020;

    private static bool TryFastForwardSonicGxDrawBegin(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicGxDrawBegin(bus, pc)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicGxDrawBeginFastForwardInstructions, extraInstructions: 0))
        {
            return false;
        }

        uint stackPointer = state.Gpr[1];
        uint newStackPointer = unchecked(stackPointer - 40);
        if (!bus.Memory.IsMainRamAddress(newStackPointer, 48))
        {
            return false;
        }

        uint stateBlockPointerAddress = unchecked(state.Gpr[13] + 0xFFFF_8380u);
        if (!bus.Memory.IsMainRamAddress(stateBlockPointerAddress, sizeof(uint)))
        {
            return false;
        }

        uint stateBlock = bus.Memory.Read32(stateBlockPointerAddress);
        if (!bus.Memory.IsMainRamAddress(stateBlock, 0x4F4))
        {
            return false;
        }

        uint dirtyFlags = bus.Memory.Read32(stateBlock + 0x4F0);
        uint needClear = bus.Memory.Read32(stateBlock);
        if (dirtyFlags != 0 || needClear == 0)
        {
            return false;
        }

        uint oldLr = state.Lr;
        uint oldR29 = state.Gpr[29];
        uint oldR30 = state.Gpr[30];
        uint oldR31 = state.Gpr[31];
        uint primitive = state.Gpr[3] | state.Gpr[4];
        uint vertexCount = state.Gpr[5];
        const uint fifo = 0xCC00_8000;

        bus.Write32(stackPointer + 4, oldLr);
        bus.Write32(newStackPointer, stackPointer);
        bus.Write32(newStackPointer + 36, oldR31);
        bus.Write32(newStackPointer + 32, oldR30);
        bus.Write32(newStackPointer + 28, oldR29);
        bus.Write8(fifo, (byte)primitive);
        bus.Write16(fifo, (ushort)vertexCount);

        state.Gpr[0] = oldLr;
        state.Gpr[3] = 0xCC01_0000;
        state.Gpr[6] = stateBlock;
        state.Gpr[29] = oldR29;
        state.Gpr[30] = oldR30;
        state.Gpr[31] = oldR31;
        state.Lr = oldLr;
        state.Pc = oldLr & 0xFFFF_FFFCu;
        SetCr0ForUnsignedCompareImmediate(state, needClear, 0);

        AdvanceFastForwardedInstructions(state, bus, SonicGxDrawBeginFastForwardInstructions);
        skippedInstructions = checked((int)SonicGxDrawBeginFastForwardInstructions);
        return true;
    }

    private static bool TryFastForwardSonicGxIndexedStripDrawBegin(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxIndexedStripDrawBegin(bus, state.Pc))
        {
            return false;
        }

        uint stream = state.Gpr[24];
        if (!bus.Memory.IsMainRamAddress(stream, sizeof(ushort)))
        {
            return false;
        }

        int signedCount = unchecked((short)bus.Memory.Read16(stream));
        uint vertexCount = unchecked((uint)Math.Abs(signedCount));
        if (vertexCount == 0 || vertexCount > 0xFFFF)
        {
            return false;
        }

        uint skipped = checked((uint)(signedCount < 0 ? 40 : 39));
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        uint stackPointer = state.Gpr[1];
        uint newStackPointer = unchecked(stackPointer - 40);
        if (!bus.Memory.IsMainRamAddress(newStackPointer, 48))
        {
            return false;
        }

        uint stateBlockPointerAddress = unchecked(state.Gpr[13] + 0xFFFF_8380u);
        if (!bus.Memory.IsMainRamAddress(stateBlockPointerAddress, sizeof(uint)))
        {
            return false;
        }

        uint stateBlock = bus.Memory.Read32(stateBlockPointerAddress);
        if (!bus.Memory.IsMainRamAddress(stateBlock, 0x4F4))
        {
            return false;
        }

        uint dirtyFlags = bus.Memory.Read32(stateBlock + 0x4F0);
        uint needClear = bus.Memory.Read32(stateBlock);
        if (dirtyFlags != 0 || needClear == 0)
        {
            return false;
        }

        const uint fifo = 0xCC00_8000;
        uint oldR29 = state.Gpr[29];
        uint oldR31 = state.Gpr[31];
        uint returnAddress = SonicGxIndexedStripDrawBeginPc + 0x28;

        bus.Write32(stackPointer + 4, returnAddress);
        bus.Write32(newStackPointer, stackPointer);
        bus.Write32(newStackPointer + 36, oldR31);
        bus.Write32(newStackPointer + 32, vertexCount);
        bus.Write32(newStackPointer + 28, oldR29);
        bus.Write8(fifo, 0x98);
        bus.Write16(fifo, (ushort)vertexCount);

        state.Gpr[0] = returnAddress;
        state.Gpr[3] = 0xCC01_0000;
        state.Gpr[4] = 0;
        state.Gpr[5] = vertexCount;
        state.Gpr[6] = stateBlock;
        state.Gpr[24] = unchecked(stream + sizeof(ushort));
        state.Gpr[29] = oldR29;
        state.Gpr[30] = vertexCount;
        state.Gpr[31] = oldR31;
        state.Lr = returnAddress;
        state.Pc = SonicGxIndexedStripDrawBeginPc + 0x30;
        SetCr0ForUnsignedCompareImmediate(state, needClear, 0);

        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardSonicGxIndexedStripBatch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxIndexedStripDrawBegin(bus, state.Pc))
        {
            return false;
        }

        uint strips = state.Gpr[26];
        uint stream = state.Gpr[24];
        uint vertexBase = state.Gpr[25];
        uint extraStreamStride = state.Gpr[31];
        if (strips == 0 || strips > 0x10000 || extraStreamStride > 0x40)
        {
            return false;
        }

        uint stackPointer = state.Gpr[1];
        uint newStackPointer = unchecked(stackPointer - 40);
        if (!bus.Memory.IsMainRamAddress(newStackPointer, 48))
        {
            return false;
        }

        uint stateBlockPointerAddress = unchecked(state.Gpr[13] + 0xFFFF_8380u);
        if (!bus.Memory.IsMainRamAddress(stateBlockPointerAddress, sizeof(uint)))
        {
            return false;
        }

        uint stateBlock = bus.Memory.Read32(stateBlockPointerAddress);
        if (!bus.Memory.IsMainRamAddress(stateBlock, 0x4F4))
        {
            return false;
        }

        uint dirtyFlags = bus.Memory.Read32(stateBlock + 0x4F0);
        uint needClear = bus.Memory.Read32(stateBlock);
        if (dirtyFlags != 0 || needClear == 0)
        {
            return false;
        }

        uint scanStream = stream;
        ulong totalInstructions = 0;
        for (uint strip = 0; strip < strips; strip++)
        {
            if (!bus.Memory.IsMainRamAddress(scanStream, sizeof(ushort)))
            {
                return false;
            }

            int signedCount = unchecked((short)bus.Memory.Read16(scanStream));
            uint vertexCount = unchecked((uint)Math.Abs(signedCount));
            if (vertexCount == 0 || vertexCount > 0xFFFF)
            {
                return false;
            }

            scanStream += sizeof(ushort);
            ulong streamBytes = (ulong)vertexCount * (6ul + extraStreamStride);
            if (streamBytes > int.MaxValue || !bus.Memory.IsMainRamAddress(scanStream, checked((int)streamBytes)))
            {
                return false;
            }

            uint vertexStream = scanStream;
            for (uint vertex = 0; vertex < vertexCount; vertex++)
            {
                short index = unchecked((short)bus.Memory.Read16(vertexStream));
                uint shiftedIndex = unchecked((uint)index << 5);
                uint vertexAddress = unchecked(vertexBase + shiftedIndex);
                if (!bus.Memory.IsMainRamAddress(vertexAddress, 0x1C))
                {
                    return false;
                }

                vertexStream += 6 + extraStreamStride;
            }

            totalInstructions += (uint)(signedCount < 0 ? 40 : 39);
            totalInstructions += (ulong)vertexCount * SonicGxVertexEmitInstructionsPerVertex;
            totalInstructions += 5;
            if (totalInstructions > int.MaxValue)
            {
                return false;
            }

            scanStream = vertexStream;
        }

        uint skipped = checked((uint)totalInstructions);
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        const uint fifo = 0xCC00_8000;
        uint currentStream = stream;
        uint returnAddress = SonicGxIndexedStripDrawBeginPc + 0x28;
        uint currentR29 = state.Gpr[29];
        uint lastVertexAddress = state.Gpr[27];
        uint lastFirstHalf = state.Gpr[29];
        uint lastSecondHalf = state.Gpr[28];
        uint lastShiftedIndex = state.Gpr[0];
        uint lastX = 0;
        uint lastY = 0;
        uint lastZ = 0;

        for (uint strip = 0; strip < strips; strip++)
        {
            int signedCount = unchecked((short)bus.Memory.Read16(currentStream));
            uint vertexCount = unchecked((uint)Math.Abs(signedCount));
            currentStream += sizeof(ushort);

            bus.Write32(stackPointer + 4, returnAddress);
            bus.Write32(newStackPointer, stackPointer);
            bus.Write32(newStackPointer + 36, state.Gpr[31]);
            bus.Write32(newStackPointer + 32, vertexCount);
            bus.Write32(newStackPointer + 28, currentR29);
            bus.Write8(fifo, 0x98);
            bus.Write16(fifo, (ushort)vertexCount);

            for (uint vertex = 0; vertex < vertexCount; vertex++)
            {
                short index = unchecked((short)bus.Memory.Read16(currentStream));
                currentStream += sizeof(ushort);
                lastShiftedIndex = unchecked((uint)index << 5);
                uint vertexAddress = unchecked(vertexBase + lastShiftedIndex);
                lastFirstHalf = unchecked((uint)(short)bus.Memory.Read16(currentStream));
                currentStream += sizeof(ushort);
                lastSecondHalf = unchecked((uint)(short)bus.Memory.Read16(currentStream));
                currentStream += sizeof(ushort);
                lastX = bus.Memory.Read32(vertexAddress);
                lastY = bus.Memory.Read32(vertexAddress + 0x04);
                lastZ = bus.Memory.Read32(vertexAddress + 0x08);
                bus.Write32(fifo, lastX);
                bus.Write32(fifo, lastY);
                bus.Write32(fifo, lastZ);
                bus.Write32(fifo, bus.Memory.Read32(vertexAddress + 0x18));
                bus.Write16(fifo, (ushort)lastFirstHalf);
                bus.Write16(fifo, (ushort)lastSecondHalf);
                currentStream += extraStreamStride;
                lastVertexAddress = vertexAddress;
            }

            currentR29 = lastFirstHalf;
        }

        state.Gpr[0] = lastShiftedIndex;
        state.Gpr[3] = lastFirstHalf;
        state.Gpr[4] = lastSecondHalf;
        state.Gpr[5] = 0xCC01_0000;
        state.Gpr[6] = stateBlock;
        state.Gpr[24] = currentStream;
        state.Gpr[26] = 0;
        state.Gpr[27] = lastVertexAddress;
        state.Gpr[28] = lastSecondHalf;
        state.Gpr[29] = lastFirstHalf;
        state.Gpr[30] = 0;
        state.Fpr[1] = BitConverter.Int32BitsToSingle(unchecked((int)lastX));
        state.Fpr[2] = BitConverter.Int32BitsToSingle(unchecked((int)lastY));
        state.Fpr[3] = BitConverter.Int32BitsToSingle(unchecked((int)lastZ));
        state.FprPair1[1] = state.Fpr[1];
        state.FprPair1[2] = state.Fpr[2];
        state.FprPair1[3] = state.Fpr[3];
        state.Lr = SonicGxIndexedStripTailPc + 4;
        state.Pc = SonicGxIndexedStripTailPc + 0x10;
        SetCr0ForUnsignedCompareImmediate(state, 0, 0);

        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardSonicGxIndexedStripTail(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxIndexedStripTail(bus, state.Pc)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 5, extraInstructions: 0))
        {
            return false;
        }

        uint nextStripCount = unchecked(state.Gpr[26] - 1);
        state.Gpr[26] = nextStripCount;
        state.Lr = SonicGxIndexedStripTailPc + 4;
        SetCr0ForUnsignedCompareImmediate(state, nextStripCount, 0);
        state.Pc = nextStripCount != 0 ? SonicGxIndexedStripDrawBeginPc : SonicGxIndexedStripTailPc + 0x10;
        AdvanceFastForwardedInstructions(state, bus, 5);
        skippedInstructions = 5;
        return true;
    }

    private static bool TryFastForwardSonicGxIndexedStripEpilogue(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxIndexedStripEpilogue(bus, state.Pc)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicGxIndexedStripEpilogueInstructions, extraInstructions: 0))
        {
            return false;
        }

        uint stackPointer = state.Gpr[1];
        if (!bus.Memory.IsMainRamAddress(stackPointer + 24, 40))
        {
            return false;
        }

        uint savedLr = bus.Memory.Read32(stackPointer + 60);
        state.Gpr[0] = savedLr;
        state.Gpr[11] = stackPointer + 56;
        for (int register = 24; register <= 31; register++)
        {
            uint offset = (uint)(24 + (register - 24) * sizeof(uint));
            state.Gpr[register] = bus.Memory.Read32(stackPointer + offset);
        }

        state.Gpr[1] = stackPointer + 56;
        state.Lr = savedLr;
        state.Pc = savedLr & 0xFFFF_FFFCu;
        AdvanceFastForwardedInstructions(state, bus, SonicGxIndexedStripEpilogueInstructions);
        skippedInstructions = checked((int)SonicGxIndexedStripEpilogueInstructions);
        return true;
    }

    private static bool MatchesSonicGxDrawBegin(GameCubeBus bus, uint pc) =>
        pc == SonicGxDrawBeginPc
        && bus.Read32(pc + 0x00) == 0x7C08_02A6
        && bus.Read32(pc + 0x04) == 0x9001_0004
        && bus.Read32(pc + 0x08) == 0x9421_FFD8
        && bus.Read32(pc + 0x0C) == 0x93E1_0024
        && bus.Read32(pc + 0x10) == 0x3BE5_0000
        && bus.Read32(pc + 0x14) == 0x93C1_0020
        && bus.Read32(pc + 0x18) == 0x3BC4_0000
        && bus.Read32(pc + 0x1C) == 0x93A1_001C
        && bus.Read32(pc + 0x20) == 0x3BA3_0000
        && bus.Read32(pc + 0x24) == 0x80CD_8380
        && bus.Read32(pc + 0x28) == 0x8006_04F0
        && bus.Read32(pc + 0x2C) == 0x2800_0000
        && bus.Read32(pc + 0x30) == 0x4182_006C
        && bus.Read32(pc + 0x9C) == 0x806D_8380
        && bus.Read32(pc + 0xA0) == 0x8003_0000
        && bus.Read32(pc + 0xA4) == 0x2800_0000
        && bus.Read32(pc + 0xA8) == 0x4082_0008
        && bus.Read32(pc + 0xB0) == 0x7FC0_EB78
        && bus.Read32(pc + 0xB4) == 0x3C60_CC01
        && bus.Read32(pc + 0xB8) == 0x9803_8000
        && bus.Read32(pc + 0xBC) == 0xB3E3_8000
        && bus.Read32(pc + 0xC0) == 0x8001_002C
        && bus.Read32(pc + 0xC4) == 0x83E1_0024
        && bus.Read32(pc + 0xC8) == 0x83C1_0020
        && bus.Read32(pc + 0xCC) == 0x83A1_001C
        && bus.Read32(pc + 0xD0) == 0x3821_0028
        && bus.Read32(pc + 0xD4) == 0x7C08_03A6
        && bus.Read32(pc + 0xD8) == 0x4E80_0020;

    private static bool TryFastForwardSonicGxVertexDescriptorSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxVertexDescriptorSetter(bus, state.Pc))
        {
            return false;
        }

        uint descriptor = state.Gpr[3];
        uint value = state.Gpr[4];
        uint caseInstructions = descriptor switch
        {
            9 or 11 => 8,
            13 => 6,
            _ => 0,
        };
        if (caseInstructions == 0)
        {
            return false;
        }

        uint stateBlockPointerAddress = unchecked(state.Gpr[13] + 0xFFFF_8380u);
        if (!bus.Memory.IsMainRamAddress(stateBlockPointerAddress, sizeof(uint)))
        {
            return false;
        }

        uint stateBlock = bus.Memory.Read32(stateBlockPointerAddress);
        if (!bus.Memory.IsMainRamAddress(stateBlock, 0x4F4))
        {
            return false;
        }

        byte vertexMatrixFlag = bus.Memory.Read8(stateBlock + 0x41C);
        byte normalMatrixFlag = bus.Memory.Read8(stateBlock + 0x41D);
        uint tailInstructions = vertexMatrixFlag != 0 ? 17u : normalMatrixFlag != 0 ? 20u : 15u;
        uint skipped = checked(8 + caseInstructions + tailInstructions);
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        uint word14 = bus.Memory.Read32(stateBlock + 0x14);
        uint word18 = bus.Memory.Read32(stateBlock + 0x18);
        uint finalR4 = value;
        uint target = descriptor switch
        {
            9 => 0x8010_0968u,
            11 => 0x8010_0A00u,
            _ => 0x8010_0A40u,
        };

        if (descriptor == 9)
        {
            word14 = Rlwinm(word14, 0, 23, 20) | Rlwinm(value, 9, 0, 22);
            bus.Memory.Write32(stateBlock + 0x14, word14);
            finalR4 = stateBlock + 0x14;
        }
        else if (descriptor == 11)
        {
            word14 = Rlwinm(word14, 0, 19, 16) | Rlwinm(value, 13, 0, 18);
            bus.Memory.Write32(stateBlock + 0x14, word14);
            finalR4 = stateBlock + 0x14;
        }
        else
        {
            word18 = Rlwinm(word18, 0, 0, 29) | value;
            bus.Memory.Write32(stateBlock + 0x18, word18);
        }

        if (vertexMatrixFlag != 0 || normalMatrixFlag != 0)
        {
            uint matrixIndex = bus.Memory.Read32(stateBlock + 0x418);
            word14 = bus.Memory.Read32(stateBlock + 0x14);
            word14 = Rlwinm(word14, 0, 21, 18) | Rlwinm(matrixIndex, 11, 0, 20);
            bus.Memory.Write32(stateBlock + 0x14, word14);
            finalR4 = stateBlock + 0x14;
        }
        else
        {
            word14 = bus.Memory.Read32(stateBlock + 0x14);
            word14 = Rlwinm(word14, 0, 21, 18);
            bus.Memory.Write32(stateBlock + 0x14, word14);
        }

        uint dirtyFlags = bus.Memory.Read32(stateBlock + 0x4F0) | 0x08u;
        bus.Memory.Write32(stateBlock + 0x4F0, dirtyFlags);

        state.Gpr[0] = dirtyFlags;
        state.Gpr[3] = stateBlock;
        state.Gpr[4] = finalR4;
        state.Gpr[5] = 0x801D_2668;
        state.Ctr = target;
        state.Pc = state.Lr & 0xFFFF_FFFCu;
        SetCr0ForUnsignedCompareImmediate(state, vertexMatrixFlag != 0 ? vertexMatrixFlag : normalMatrixFlag, 0);

        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicGxVertexDescriptorSetter(GameCubeBus bus, uint pc) =>
        pc == SonicGxVertexDescriptorSetterPc
        && bus.Read32(pc + 0x000) == 0x2803_0019
        && bus.Read32(pc + 0x004) == 0x4181_0300
        && bus.Read32(pc + 0x008) == 0x3CA0_801D
        && bus.Read32(pc + 0x00C) == 0x38A5_2668
        && bus.Read32(pc + 0x010) == 0x5460_103A
        && bus.Read32(pc + 0x014) == 0x7C05_002E
        && bus.Read32(pc + 0x018) == 0x7C09_03A6
        && bus.Read32(pc + 0x01C) == 0x4E80_0420
        && bus.Read32(pc + 0x138) == 0x806D_8380
        && bus.Read32(pc + 0x13C) == 0x5480_482C
        && bus.Read32(pc + 0x140) == 0x3883_0014
        && bus.Read32(pc + 0x144) == 0x8063_0014
        && bus.Read32(pc + 0x148) == 0x5463_05E8
        && bus.Read32(pc + 0x14C) == 0x7C60_0378
        && bus.Read32(pc + 0x150) == 0x9004_0000
        && bus.Read32(pc + 0x154) == 0x4800_01B0
        && bus.Read32(pc + 0x1D0) == 0x806D_8380
        && bus.Read32(pc + 0x1D4) == 0x5480_6824
        && bus.Read32(pc + 0x1D8) == 0x3883_0014
        && bus.Read32(pc + 0x1DC) == 0x8063_0014
        && bus.Read32(pc + 0x1E0) == 0x5463_04E0
        && bus.Read32(pc + 0x1E4) == 0x7C60_0378
        && bus.Read32(pc + 0x1E8) == 0x9004_0000
        && bus.Read32(pc + 0x1EC) == 0x4800_0118
        && bus.Read32(pc + 0x210) == 0x806D_8380
        && bus.Read32(pc + 0x214) == 0x8403_0018
        && bus.Read32(pc + 0x218) == 0x5400_003A
        && bus.Read32(pc + 0x21C) == 0x7C00_2378
        && bus.Read32(pc + 0x220) == 0x9003_0000
        && bus.Read32(pc + 0x224) == 0x4800_00E0
        && bus.Read32(pc + 0x304) == 0x806D_8380
        && bus.Read32(pc + 0x308) == 0x8803_041C
        && bus.Read32(pc + 0x30C) == 0x2800_0000
        && bus.Read32(pc + 0x310) == 0x4082_0010
        && bus.Read32(pc + 0x314) == 0x8803_041D
        && bus.Read32(pc + 0x318) == 0x2800_0000
        && bus.Read32(pc + 0x31C) == 0x4182_0024
        && bus.Read32(pc + 0x320) == 0x3883_0014
        && bus.Read32(pc + 0x324) == 0x8003_0418
        && bus.Read32(pc + 0x328) == 0x8063_0014
        && bus.Read32(pc + 0x32C) == 0x5400_5828
        && bus.Read32(pc + 0x330) == 0x5463_0564
        && bus.Read32(pc + 0x334) == 0x7C60_0378
        && bus.Read32(pc + 0x338) == 0x9004_0000
        && bus.Read32(pc + 0x33C) == 0x4800_0010
        && bus.Read32(pc + 0x340) == 0x8403_0014
        && bus.Read32(pc + 0x344) == 0x5400_0564
        && bus.Read32(pc + 0x348) == 0x9003_0000
        && bus.Read32(pc + 0x34C) == 0x806D_8380
        && bus.Read32(pc + 0x350) == 0x8003_04F0
        && bus.Read32(pc + 0x354) == 0x6000_0008
        && bus.Read32(pc + 0x358) == 0x9003_04F0
        && bus.Read32(pc + 0x35C) == 0x4E80_0020;

    private static bool TryFastForwardSonicGxVertexAttributeFlush(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxVertexAttributeFlush(bus, state.Pc))
        {
            return false;
        }

        uint stateBlockPointerAddress = unchecked(state.Gpr[13] + 0xFFFF_8380u);
        uint oldStackPointer = state.Gpr[1];
        uint stackPointer = unchecked(oldStackPointer - 40u);
        if (!bus.Memory.IsMainRamAddress(stateBlockPointerAddress, sizeof(uint))
            || !bus.Memory.IsMainRamAddress(stackPointer, 48))
        {
            return false;
        }

        uint stateBlock = bus.Memory.Read32(stateBlockPointerAddress);
        if (!bus.Memory.IsMainRamAddress(stateBlock, 0x4E0)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 2048, extraInstructions: 0))
        {
            return false;
        }

        uint skipped = 0;
        uint originalLr = state.Lr;
        uint originalR27 = state.Gpr[27];
        uint originalR28 = state.Gpr[28];
        uint originalR29 = state.Gpr[29];
        uint originalR30 = state.Gpr[30];
        uint originalR31 = state.Gpr[31];

        bus.Memory.Write32(oldStackPointer + 4, originalLr);
        bus.Memory.Write32(stackPointer, oldStackPointer);
        state.Gpr[1] = stackPointer;
        bus.Memory.Write32(stackPointer + 20, originalR27);
        bus.Memory.Write32(stackPointer + 24, originalR28);
        bus.Memory.Write32(stackPointer + 28, originalR29);
        bus.Memory.Write32(stackPointer + 32, originalR30);
        bus.Memory.Write32(stackPointer + 36, originalR31);
        skipped += 4;

        state.Gpr[3] = stateBlock;
        state.Gpr[0] = bus.Memory.Read32(stateBlock + 0x4DC);
        skipped += 2;
        SetCr0ForUnsignedCompareImmediate(state, state.Gpr[0], 0xFF);
        skipped++;
        if (state.Gpr[0] == 0xFF)
        {
            skipped++;
            return FinishSonicGxVertexAttributeFlush(state, bus, skipped, originalLr, originalR27, originalR28, originalR29, originalR30, originalR31, out skippedInstructions);
        }

        skipped++;
        state.Gpr[0] = bus.Memory.Read32(stateBlock + 0x204);
        state.Gpr[30] = state.Gpr[0];
        state.Gpr[3] = Rlwinm(state.Gpr[0], 22, 28, 31);
        state.Gpr[31] = unchecked(state.Gpr[3] + 1u);
        state.Gpr[27] = Rlwinm(state.Gpr[0], 16, 29, 31);
        skipped += 5;
        skipped++;

        while (true)
        {
            SetCr0ForUnsignedCompareImmediate(state, state.Gpr[30], state.Gpr[27]);
            skipped++;
            if (state.Gpr[30] >= state.Gpr[27])
            {
                skipped++;
                break;
            }

            skipped++;
            uint word120 = bus.Memory.Read32(stateBlock + 0x120);
            if (state.Gpr[30] > 3)
            {
                return false;
            }

            switch (state.Gpr[30])
            {
                case 0:
                    state.Gpr[3] = stateBlock;
                    state.Gpr[0] = word120;
                    state.Gpr[29] = Rlwinm(state.Gpr[0], 0, 29, 31);
                    state.Gpr[28] = Rlwinm(state.Gpr[0], 29, 29, 31);
                    skipped += 7;
                    break;
                case 1:
                    state.Gpr[3] = stateBlock;
                    state.Gpr[0] = word120;
                    state.Gpr[29] = Rlwinm(state.Gpr[0], 26, 29, 31);
                    state.Gpr[28] = Rlwinm(state.Gpr[0], 23, 29, 31);
                    skipped += 10;
                    break;
                case 2:
                    state.Gpr[3] = stateBlock;
                    state.Gpr[0] = word120;
                    state.Gpr[29] = Rlwinm(state.Gpr[0], 20, 29, 31);
                    state.Gpr[28] = Rlwinm(state.Gpr[0], 17, 29, 31);
                    skipped += 6;
                    break;
                default:
                    state.Gpr[3] = stateBlock;
                    state.Gpr[0] = word120;
                    state.Gpr[29] = Rlwinm(state.Gpr[0], 14, 29, 31);
                    state.Gpr[28] = Rlwinm(state.Gpr[0], 11, 29, 31);
                    skipped += 9;
                    break;
            }

            state.Gpr[3] = stateBlock;
            state.Gpr[0] = 1;
            state.Gpr[0] = ShiftLeftWord(state.Gpr[0], state.Gpr[28]);
            state.Gpr[3] = bus.Memory.Read32(stateBlock + 0x4DC);
            state.Gpr[0] = state.Gpr[3] & state.Gpr[0];
            SetCr0(state, state.Gpr[0]);
            skipped += 5;
            if (state.Gpr[0] == 0)
            {
                skipped++;
                state.Gpr[3] = state.Gpr[29];
                state.Gpr[4] = state.Gpr[28];
                skipped += 2;
                if (!TryFastForwardSonicGxVertexAttributeHelperBody(state, bus))
                {
                    return false;
                }

                state.Lr = 0x8010_3DF8;
                skipped += 52;
            }
            else
            {
                skipped++;
            }

            state.Gpr[30] = unchecked(state.Gpr[30] + 1u);
            skipped++;
        }

        state.Gpr[27] = 0;
        state.Gpr[30] = state.Gpr[27];
        skipped += 3;
        while (true)
        {
            SetCr0ForUnsignedCompareImmediate(state, state.Gpr[27], state.Gpr[31]);
            skipped++;
            if (state.Gpr[27] >= state.Gpr[31])
            {
                skipped++;
                break;
            }

            skipped++;
            state.Gpr[5] = stateBlock;
            state.Gpr[3] = unchecked(state.Gpr[30] + 0x49Cu);
            state.Gpr[4] = Rlwinm(state.Gpr[27], 1, 0, 29);
            if (!bus.Memory.IsMainRamAddress(unchecked(state.Gpr[5] + state.Gpr[3]), sizeof(uint))
                || !bus.Memory.IsMainRamAddress(unchecked(state.Gpr[5] + state.Gpr[4] + 0x100u), sizeof(uint)))
            {
                return false;
            }

            state.Gpr[3] = bus.Memory.Read32(unchecked(state.Gpr[5] + state.Gpr[3]));
            state.Gpr[0] = Rlwinm(state.Gpr[27], 0, 31, 31);
            state.Gpr[4] = unchecked(state.Gpr[4] + 0x100u);
            state.Gpr[4] = unchecked(state.Gpr[5] + state.Gpr[4]);
            state.Gpr[29] = Rlwinm(state.Gpr[3], 0, 24, 22);
            skipped += 8;
            state.Gpr[0] = bus.Memory.Read32(state.Gpr[4]);
            if (state.Gpr[27] % 2 != 0)
            {
                state.Gpr[28] = Rlwinm(state.Gpr[0], 17, 29, 31);
                skipped += 4;
            }
            else
            {
                state.Gpr[28] = Rlwinm(state.Gpr[0], 29, 29, 31);
                skipped += 3;
            }

            SetCr0ForUnsignedCompareImmediate(state, state.Gpr[29], 0xFF);
            skipped++;
            if (state.Gpr[29] != 0xFF)
            {
                skipped++;
                state.Gpr[0] = unchecked(state.Gpr[0] + 1u);
                state.Gpr[3] = bus.Memory.Read32(stateBlock + 0x4DC);
                state.Gpr[0] = ShiftLeftWord(state.Gpr[0], state.Gpr[28]);
                state.Gpr[0] = state.Gpr[3] & state.Gpr[0];
                SetCr0(state, state.Gpr[0]);
                skipped += 4;
                if (state.Gpr[0] == 0)
                {
                    skipped++;
                    state.Gpr[3] = state.Gpr[29];
                    state.Gpr[4] = state.Gpr[28];
                    skipped += 2;
                    if (!TryFastForwardSonicGxVertexAttributeHelperBody(state, bus))
                    {
                        return false;
                    }

                    state.Lr = 0x8010_3E70;
                    skipped += 52;
                }
                else
                {
                    skipped++;
                }
            }
            else
            {
                skipped++;
            }

            state.Gpr[30] = unchecked(state.Gpr[30] + 4u);
            state.Gpr[27] = unchecked(state.Gpr[27] + 1u);
            skipped += 2;
        }

        return FinishSonicGxVertexAttributeFlush(state, bus, skipped, originalLr, originalR27, originalR28, originalR29, originalR30, originalR31, out skippedInstructions);
    }

    private static bool TryFastForwardSonicGxVertexAttributeHelperBody(PowerPcState state, GameCubeBus bus)
    {
        uint stateBlockPointerAddress = unchecked(state.Gpr[13] + 0xFFFF_8380u);
        if (!bus.Memory.IsMainRamAddress(stateBlockPointerAddress, sizeof(uint)))
        {
            return false;
        }

        uint stateBlock = bus.Memory.Read32(stateBlockPointerAddress);
        uint destinationOffset = Rlwinm(state.Gpr[4], 2, 0, 29);
        uint sourceOffset = Rlwinm(state.Gpr[3], 2, 0, 29);
        uint sourceBase = unchecked(stateBlock + sourceOffset);
        uint destinationBase = unchecked(stateBlock + destinationOffset);
        if (!bus.Memory.IsMainRamAddress(sourceBase + 0x47C, sizeof(uint))
            || !bus.Memory.IsMainRamAddress(destinationBase + 0xD8, sizeof(uint))
            || !bus.Memory.IsMainRamAddress(stateBlock + 2, sizeof(ushort)))
        {
            return false;
        }

        uint value45C = bus.Memory.Read32(sourceBase + 0x45C);
        uint fifoRegister = unchecked(destinationOffset + 97u);
        uint wordB8 = bus.Memory.Read32(destinationBase + 0xB8);
        wordB8 = Rlwimi(wordB8, value45C, 0, 22, 31);
        bus.Memory.Write32(destinationBase + 0xB8, wordB8);

        uint wordD8 = bus.Memory.Read32(destinationBase + 0xD8);
        wordD8 = Rlwimi(wordD8, value45C, 22, 22, 31);
        bus.Memory.Write32(destinationBase + 0xD8, wordD8);

        uint value47C = bus.Memory.Read32(sourceBase + 0x47C);
        wordB8 = bus.Memory.Read32(destinationBase + 0xB8);
        uint highBits = Rlwinm(value47C, 30, 30, 31);
        uint highFlag = unchecked(1u - highBits);
        uint lowBits = Rlwinm(value47C, 0, 30, 31);
        uint lowFlag = uint.LeadingZeroCount(unchecked(1u - lowBits));
        wordB8 = (wordB8 & 0xFFFF_03FFu) | Rlwinm(lowFlag, 11, 8, 15);
        bus.Memory.Write32(destinationBase + 0xB8, wordB8);

        uint highFlagBits = uint.LeadingZeroCount(highFlag);
        wordD8 = bus.Memory.Read32(destinationBase + 0xD8);
        wordD8 = (wordD8 & 0xFFFE_0000u) | Rlwinm(highFlagBits, 11, 8, 15);
        bus.Memory.Write32(destinationBase + 0xD8, wordD8);

        const uint fifo = 0xCC00_8000;
        bus.Write8(fifo, (byte)fifoRegister);
        bus.Write32(fifo, bus.Memory.Read32(destinationBase + 0xB8));
        bus.Write8(fifo, (byte)fifoRegister);
        uint finalWordD8 = bus.Memory.Read32(destinationBase + 0xD8);
        bus.Write32(fifo, finalWordD8);
        bus.Memory.Write16(stateBlock + 2, 0);

        state.Gpr[0] = finalWordD8;
        state.Gpr[3] = 0;
        state.Gpr[4] = destinationBase;
        state.Gpr[5] = stateBlock;
        state.Gpr[6] = 0xCC01_0000;
        state.Gpr[7] = fifoRegister;
        state.Gpr[8] = destinationBase;
        state.Gpr[9] = value47C;
        state.Gpr[10] = destinationBase;
        return true;
    }

    private static bool FinishSonicGxVertexAttributeFlush(
        PowerPcState state,
        GameCubeBus bus,
        uint skipped,
        uint originalLr,
        uint originalR27,
        uint originalR28,
        uint originalR29,
        uint originalR30,
        uint originalR31,
        out int skippedInstructions)
    {
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped + 5, extraInstructions: 0))
        {
            skippedInstructions = 0;
            return false;
        }

        state.Gpr[27] = originalR27;
        state.Gpr[28] = originalR28;
        state.Gpr[29] = originalR29;
        state.Gpr[30] = originalR30;
        state.Gpr[31] = originalR31;
        state.Gpr[0] = bus.Memory.Read32(state.Gpr[1] + 44);
        state.Gpr[1] = unchecked(state.Gpr[1] + 40u);
        state.Lr = originalLr;
        state.Pc = originalLr & 0xFFFF_FFFCu;
        skipped += 5;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicGxVertexAttributeFlush(GameCubeBus bus, uint pc) =>
        pc == SonicGxVertexAttributeFlushPc
        && bus.Read32(pc + 0x000) == 0x7C08_02A6
        && bus.Read32(pc + 0x004) == 0x9001_0004
        && bus.Read32(pc + 0x008) == 0x9421_FFD8
        && bus.Read32(pc + 0x00C) == 0xBF61_0014
        && bus.Read32(pc + 0x010) == 0x806D_8380
        && bus.Read32(pc + 0x014) == 0x8003_04DC
        && bus.Read32(pc + 0x018) == 0x2800_00FF
        && bus.Read32(pc + 0x01C) == 0x4182_013C
        && bus.Read32(pc + 0x020) == 0x8003_0204
        && bus.Read32(pc + 0x024) == 0x3BC0_0000
        && bus.Read32(pc + 0x028) == 0x5403_B73E
        && bus.Read32(pc + 0x02C) == 0x3BE3_0001
        && bus.Read32(pc + 0x030) == 0x541B_877E
        && bus.Read32(pc + 0x034) == 0x4800_00A0
        && bus.Read32(pc + 0x0D4) == 0x7C1E_D840
        && bus.Read32(pc + 0x0D8) == 0x4180_FF60
        && bus.Read32(pc + 0x0DC) == 0x3B60_0000
        && bus.Read32(pc + 0x0E0) == 0x3BDB_0000
        && bus.Read32(pc + 0x0E4) == 0x4800_006C
        && bus.Read32(pc + 0x150) == 0x7C1B_F840
        && bus.Read32(pc + 0x154) == 0x4180_FF94
        && bus.Read32(pc + 0x158) == 0xBB61_0014
        && bus.Read32(pc + 0x15C) == 0x8001_002C
        && bus.Read32(pc + 0x160) == 0x3821_0028
        && bus.Read32(pc + 0x164) == 0x7C08_03A6
        && bus.Read32(pc + 0x168) == 0x4E80_0020
        && MatchesSonicGxVertexAttributeHelper(bus);

    private static bool MatchesSonicGxVertexAttributeHelper(GameCubeBus bus) =>
        bus.Read32(SonicGxVertexAttributeHelperPc + 0x000) == 0x80AD_8380
        && bus.Read32(SonicGxVertexAttributeHelperPc + 0x004) == 0x5480_103A
        && bus.Read32(SonicGxVertexAttributeHelperPc + 0x008) == 0x5469_103A
        && bus.Read32(SonicGxVertexAttributeHelperPc + 0x028) == 0x50A3_05BE
        && bus.Read32(SonicGxVertexAttributeHelperPc + 0x044) == 0x50A4_B5BE
        && bus.Read32(SonicGxVertexAttributeHelperPc + 0x0A8) == 0x98E6_8000
        && bus.Read32(SonicGxVertexAttributeHelperPc + 0x0B4) == 0x9006_8000
        && bus.Read32(SonicGxVertexAttributeHelperPc + 0x0B8) == 0x98E6_8000
        && bus.Read32(SonicGxVertexAttributeHelperPc + 0x0C0) == 0x9006_8000
        && bus.Read32(SonicGxVertexAttributeHelperPc + 0x0C4) == 0xB065_0002
        && bus.Read32(SonicGxVertexAttributeHelperPc + 0x0C8) == 0x4E80_0020;

    private static bool MatchesSonicGxIndexedStripDrawBegin(GameCubeBus bus, uint pc) =>
        pc == SonicGxIndexedStripDrawBeginPc
        && bus.Read32(pc + 0x00) == 0xA818_0000
        && bus.Read32(pc + 0x04) == 0x3B18_0002
        && bus.Read32(pc + 0x08) == 0x7C1E_0378
        && bus.Read32(pc + 0x0C) == 0x2C1E_0000
        && bus.Read32(pc + 0x10) == 0x4080_0008
        && bus.Read32(pc + 0x14) == 0x7FDE_00D0
        && bus.Read32(pc + 0x18) == 0x3860_0098
        && bus.Read32(pc + 0x1C) == 0x3880_0000
        && bus.Read32(pc + 0x20) == 0x57C5_043E
        && bus.Read32(pc + 0x24) == 0x4BFE_18AD
        && bus.Read32(pc + 0x28) == 0x4800_0004
        && bus.Read32(pc + 0x2C) == 0x4800_0004
        && MatchesSonicGxDrawBegin(bus, SonicGxDrawBeginPc)
        && MatchesSonicGxVertexEmitLoop(bus, SonicGxIndexedStripDrawBeginPc + 0x30);

    private static bool MatchesSonicGxIndexedStripTail(GameCubeBus bus, uint pc) =>
        pc == SonicGxIndexedStripTailPc
        && bus.Read32(pc + 0x00) == 0x4800_0029
        && bus.Read32(pc + 0x04) == 0x3B5A_FFFF
        && bus.Read32(pc + 0x08) == 0x281A_0000
        && bus.Read32(pc + 0x0C) == 0x4082_FF70
        && bus.Read32(pc + 0x28) == 0x4E80_0020;

    private static bool MatchesSonicGxIndexedStripEpilogue(GameCubeBus bus, uint pc) =>
        pc == SonicGxIndexedStripEpiloguePc
        && bus.Read32(pc + 0x00) == 0x8001_003C
        && bus.Read32(pc + 0x04) == 0x3961_0038
        && bus.Read32(pc + 0x08) == 0x4BFE_AEF9
        && bus.Read32(pc + 0x0C) == 0x3821_0038
        && bus.Read32(pc + 0x10) == 0x7C08_03A6
        && bus.Read32(pc + 0x14) == 0x4E80_0020
        && bus.Read32(0x8010_B00C) == 0x830B_FFE0
        && bus.Read32(0x8010_B010) == 0x832B_FFE4
        && bus.Read32(0x8010_B014) == 0x834B_FFE8
        && MatchesSonicGprRestoreTail(bus, SonicGprRestoreTailPc);

    private static bool TryFastForwardSonicGxCommandListTerminal(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxCommandListTerminal(bus, state.Pc)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicGxCommandListTerminalInstructions, extraInstructions: 0))
        {
            return false;
        }

        uint stream = state.Gpr[20];
        uint stackPointer = state.Gpr[1];
        if (!bus.Memory.IsMainRamAddress(stream, sizeof(ushort))
            || !bus.Memory.IsMainRamAddress(stackPointer + 80, 56))
        {
            return false;
        }

        short command = unchecked((short)bus.Memory.Read16(stream));
        if (command != 0x00FF)
        {
            return false;
        }

        uint returnAddress = bus.Memory.Read32(stackPointer + 132);
        state.Gpr[0] = returnAddress;
        state.Gpr[11] = stackPointer + 128;
        for (int register = 20; register <= 31; register++)
        {
            uint offset = (uint)(80 + (register - 20) * sizeof(uint));
            state.Gpr[register] = bus.Memory.Read32(stackPointer + offset);
        }

        state.Gpr[1] = stackPointer + 128;
        state.Lr = returnAddress;
        state.Pc = returnAddress & 0xFFFF_FFFCu;
        SetCr0ForSignedCompareImmediate(state, 255, 255);
        AdvanceFastForwardedInstructions(state, bus, SonicGxCommandListTerminalInstructions);
        skippedInstructions = checked((int)SonicGxCommandListTerminalInstructions);
        return true;
    }

    private static bool TryFastForwardSonicGxCommandListFetch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxCommandListFetch(bus, state.Pc)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicGxCommandListFetchInstructions, extraInstructions: 0))
        {
            return false;
        }

        uint stream = state.Gpr[20];
        if (!bus.Memory.IsMainRamAddress(stream, sizeof(ushort)))
        {
            return false;
        }

        short command = unchecked((short)bus.Memory.Read16(stream));
        if (command == 0x00FF)
        {
            return false;
        }

        state.Gpr[20] = stream + sizeof(ushort);
        state.Gpr[28] = unchecked((uint)command);
        state.Pc = SonicGxCommandDispatchHeaderPc;
        SetCr0ForSignedCompareImmediate(state, unchecked((uint)command), 255);
        AdvanceFastForwardedInstructions(state, bus, SonicGxCommandListFetchInstructions);
        skippedInstructions = checked((int)SonicGxCommandListFetchInstructions);
        return true;
    }

    private static bool MatchesSonicGxCommandListTerminal(GameCubeBus bus, uint pc) =>
        pc == SonicGxCommandListTerminalPc
        && bus.Read32(pc + 0x00) == 0xAB94_0000
        && bus.Read32(pc + 0x04) == 0x3A94_0002
        && bus.Read32(pc + 0x08) == 0x2C1C_00FF
        && bus.Read32(pc + 0x0C) == 0x4082_FBB0
        && bus.Read32(pc + 0x10) == 0x8001_0084
        && bus.Read32(pc + 0x14) == 0x3961_0080
        && bus.Read32(pc + 0x18) == 0x4BFE_DE61
        && bus.Read32(pc + 0x1C) == 0x3821_0080
        && bus.Read32(pc + 0x20) == 0x7C08_03A6
        && bus.Read32(pc + 0x24) == 0x4E80_0020
        && bus.Read32(0x8010_AFFC) == 0x828B_FFD0
        && bus.Read32(0x8010_B000) == 0x82AB_FFD4
        && bus.Read32(0x8010_B004) == 0x82CB_FFD8
        && bus.Read32(0x8010_B008) == 0x82EB_FFDC
        && bus.Read32(0x8010_B00C) == 0x830B_FFE0
        && bus.Read32(0x8010_B010) == 0x832B_FFE4
        && bus.Read32(0x8010_B014) == 0x834B_FFE8
        && bus.Read32(0x8010_B018) == 0x836B_FFEC
        && bus.Read32(0x8010_B01C) == 0x838B_FFF0
        && bus.Read32(0x8010_B020) == 0x83AB_FFF4
        && bus.Read32(0x8010_B024) == 0x83CB_FFF8
        && bus.Read32(0x8010_B028) == 0x83EB_FFFC
        && bus.Read32(0x8010_B02C) == 0x4E80_0020;

    private static bool MatchesSonicGxCommandListFetch(GameCubeBus bus, uint pc) =>
        pc == SonicGxCommandListTerminalPc
        && bus.Read32(pc + 0x00) == 0xAB94_0000
        && bus.Read32(pc + 0x04) == 0x3A94_0002
        && bus.Read32(pc + 0x08) == 0x2C1C_00FF
        && bus.Read32(pc + 0x0C) == 0x4082_FBB0
        && MatchesSonicGxCommandDispatchHeader(bus, SonicGxCommandDispatchHeaderPc);

    private static bool TryFastForwardSonicGxCommandDispatch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (MatchesSonicGxCommandDispatchHeader(bus, state.Pc))
        {
            const uint skipped = 4;
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            uint command = state.Gpr[28] & 0xFFu;
            state.Gpr[0] = command;
            state.Gpr[25] = command;
            state.Pc = command >= 8 ? SonicGxCommandDispatchHighRangePc : SonicGxCommandDispatchHeaderPc + 0x10;
            SetCr0ForSignedCompareImmediate(state, command, 8);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }

        if (MatchesSonicGxCommandDispatchHighRange(bus, state.Pc))
        {
            const uint skipped = 2;
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            uint command = state.Gpr[25];
            state.Pc = unchecked((int)command) >= 16 ? SonicGxCommandDispatchHighRangePc + 0x38 : SonicGxCommandDispatchHighRangePc + 0x08;
            SetCr0ForSignedCompareImmediate(state, command, 16);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }

        if (MatchesSonicGxCommandDispatchExtendedRange(bus, state.Pc))
        {
            const uint skipped = 2;
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            uint command = state.Gpr[25];
            state.Pc = unchecked((int)command) >= 64 ? SonicGxCommandDispatchExtendedRangePc + 0x40 : SonicGxCommandDispatchExtendedRangePc + 0x08;
            SetCr0ForSignedCompareImmediate(state, command, 64);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }

        if (MatchesSonicGxCommandMetadataHeader(bus, state.Pc))
        {
            const uint skipped = 8;
            uint stream = state.Gpr[20];
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0)
                || !bus.Memory.IsMainRamAddress(stream, 4))
            {
                return false;
            }

            uint signedPayload = unchecked((uint)(int)(short)bus.Memory.Read16(stream + 2));
            state.Gpr[3] = stream;
            state.Gpr[20] = unchecked(stream + 4);
            state.Gpr[27] = bus.Memory.Read16(stream);
            state.Gpr[0] = signedPayload;
            state.Gpr[26] = signedPayload;
            SetCr0ForSignedCompareImmediate(state, state.Gpr[24], 0);
            state.Pc = unchecked((int)state.Gpr[24]) <= 0 ? SonicGxCommandMetadataHeaderPc + 0x60 : SonicGxCommandMetadataHeaderPc + 0x20;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }

        if (MatchesSonicGxCommandActiveBatchRecord(bus, state.Pc))
        {
            uint oldSlot = state.Gpr[23];
            uint stackOffset = Rlwinm(oldSlot, 2, 0, 29);
            uint streamStackAddress = unchecked(state.Gpr[30] + stackOffset);
            uint flagStackAddress = unchecked(state.Gpr[31] + stackOffset);
            bool terminal = unchecked((int)oldSlot) >= unchecked((int)state.Gpr[24]);
            uint skipped = terminal ? 10u : 14u;
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0)
                || !bus.Memory.IsMainRamAddress(streamStackAddress, sizeof(uint))
                || !bus.Memory.IsMainRamAddress(flagStackAddress, sizeof(uint)))
            {
                return false;
            }

            uint commandFlag = state.Gpr[25] == 66 ? 1u : 0u;
            bus.Memory.Write32(streamStackAddress, state.Gpr[20]);
            bus.Memory.Write32(flagStackAddress, commandFlag);
            state.Gpr[3] = stackOffset;
            state.Gpr[0] = oldSlot;
            state.Gpr[23] = unchecked(oldSlot + 1u);
            SetCr0ForSignedCompare(state, oldSlot, state.Gpr[24]);
            if (terminal)
            {
                state.Pc = SonicGxCommandActiveBatchRecordPc + 0x38;
            }
            else
            {
                state.Gpr[0] = Rlwinm(unchecked(state.Gpr[27] - 1u), 1, 0, 30);
                state.Gpr[20] = unchecked(state.Gpr[20] + state.Gpr[0]);
                state.Pc = SonicGxCommandListTerminalPc;
            }

            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }

        return false;
    }

    private static bool MatchesSonicGxCommandDispatchHeader(GameCubeBus bus, uint pc) =>
        pc == SonicGxCommandDispatchHeaderPc
        && bus.Read32(pc + 0x00) == 0x5780_063E
        && bus.Read32(pc + 0x04) == 0x7C19_0734
        && bus.Read32(pc + 0x08) == 0x2C19_0008
        && bus.Read32(pc + 0x0C) == 0x4080_009C
        && bus.Read32(pc + 0x10) == 0x2C19_0004
        && bus.Read32(SonicGxCommandDispatchHighRangePc) == 0x2C19_0010;

    private static bool MatchesSonicGxCommandDispatchHighRange(GameCubeBus bus, uint pc) =>
        pc == SonicGxCommandDispatchHighRangePc
        && bus.Read32(pc + 0x00) == 0x2C19_0010
        && bus.Read32(pc + 0x04) == 0x4080_0034
        && bus.Read32(pc + 0x08) == 0x2C18_0000
        && bus.Read32(pc + 0x38) == 0x2C19_0040;

    private static bool MatchesSonicGxCommandDispatchExtendedRange(GameCubeBus bus, uint pc) =>
        pc == SonicGxCommandDispatchExtendedRangePc
        && bus.Read32(pc + 0x00) == 0x2C19_0040
        && bus.Read32(pc + 0x04) == 0x4080_003C
        && bus.Read32(pc + 0x08) == 0xAB34_0000
        && bus.Read32(pc + 0x40) == 0x7E83_A378;

    private static bool MatchesSonicGxCommandMetadataHeader(GameCubeBus bus, uint pc) =>
        pc == SonicGxCommandMetadataHeaderPc
        && bus.Read32(pc + 0x00) == 0x7E83_A378
        && bus.Read32(pc + 0x04) == 0x3A94_0002
        && bus.Read32(pc + 0x08) == 0xA363_0000
        && bus.Read32(pc + 0x0C) == 0xA814_0000
        && bus.Read32(pc + 0x10) == 0x3A94_0002
        && bus.Read32(pc + 0x14) == 0x7C1A_0378
        && bus.Read32(pc + 0x18) == 0x2C18_0000
        && bus.Read32(pc + 0x1C) == 0x4081_0044;

    private static bool MatchesSonicGxCommandActiveBatchRecord(GameCubeBus bus, uint pc) =>
        pc == SonicGxCommandActiveBatchRecordPc
        && bus.Read32(pc + 0x00) == 0x56E3_103A
        && bus.Read32(pc + 0x04) == 0x7E9E_192E
        && bus.Read32(pc + 0x08) == 0x2019_0042
        && bus.Read32(pc + 0x0C) == 0x7C00_0034
        && bus.Read32(pc + 0x10) == 0x5400_D97E
        && bus.Read32(pc + 0x14) == 0x7C1F_192E
        && bus.Read32(pc + 0x18) == 0x7EE0_BB78
        && bus.Read32(pc + 0x1C) == 0x3AF7_0001
        && bus.Read32(pc + 0x20) == 0x7C00_C000
        && bus.Read32(pc + 0x24) == 0x4080_0014
        && bus.Read32(pc + 0x28) == 0x381B_FFFF
        && bus.Read32(pc + 0x2C) == 0x5400_083C
        && bus.Read32(pc + 0x30) == 0x7E94_0214
        && bus.Read32(pc + 0x34) == 0x4800_02D0
        && bus.Read32(pc + 0x38) == 0x7EDC_B378;

    private static bool TryFastForwardSonicGprSaveRestoreTail(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        bool saveTail = MatchesSonicGprSaveTail(bus, pc, out int saveFirstRegister, out int saveSkippedInstructions);
        bool restoreTail = MatchesSonicGprRestoreTail(bus, pc, out int restoreFirstRegister, out int restoreSkippedInstructions);
        if (!saveTail && !restoreTail)
        {
            return false;
        }

        int firstRegister = saveTail ? saveFirstRegister : restoreFirstRegister;
        int skipped = saveTail ? saveSkippedInstructions : restoreSkippedInstructions;
        int registerCount = 32 - firstRegister;
        uint byteCount = (uint)(registerCount * sizeof(uint));
        uint baseAddress = unchecked(state.Gpr[11] - byteCount);
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: (uint)skipped, extraInstructions: 0)
            || !bus.Memory.IsMainRamAddress(baseAddress, checked((int)byteCount)))
        {
            return false;
        }

        if (saveTail)
        {
            for (int register = firstRegister; register <= 31; register++)
            {
                uint offset = (uint)((register - firstRegister) * sizeof(uint));
                bus.Memory.Write32(baseAddress + offset, state.Gpr[register]);
            }
        }
        else
        {
            for (int register = firstRegister; register <= 31; register++)
            {
                uint offset = (uint)((register - firstRegister) * sizeof(uint));
                state.Gpr[register] = bus.Memory.Read32(baseAddress + offset);
            }
        }

        state.Pc = state.Lr & 0xFFFF_FFFCu;
        AdvanceFastForwardedInstructions(state, bus, (uint)skipped);
        skippedInstructions = skipped;
        return true;
    }

    private static bool MatchesSonicGprSaveTail(GameCubeBus bus, uint pc) =>
        MatchesSonicGprSaveTail(bus, pc, out _, out _);

    private static bool MatchesSonicGprSaveTail(GameCubeBus bus, uint pc, out int firstRegister, out int skippedInstructions) =>
        MatchesSonicGprTail(
            bus,
            pc,
            SonicGprSaveTailPc - 12,
            [0x930B_FFE0, 0x932B_FFE4, 0x934B_FFE8, 0x936B_FFEC, 0x938B_FFF0, 0x93AB_FFF4, 0x93CB_FFF8, 0x93EB_FFFC, 0x4E80_0020],
            out firstRegister,
            out skippedInstructions);

    private static bool MatchesSonicGprRestoreTail(GameCubeBus bus, uint pc) =>
        MatchesSonicGprRestoreTail(bus, pc, out _, out _);

    private static bool MatchesSonicGprRestoreTail(GameCubeBus bus, uint pc, out int firstRegister, out int skippedInstructions) =>
        MatchesSonicGprTail(
            bus,
            pc,
            SonicGprRestoreTailPc - 12,
            [0x830B_FFE0, 0x832B_FFE4, 0x834B_FFE8, 0x836B_FFEC, 0x838B_FFF0, 0x83AB_FFF4, 0x83CB_FFF8, 0x83EB_FFFC, 0x4E80_0020],
            out firstRegister,
            out skippedInstructions);

    private static bool MatchesSonicGprTail(
        GameCubeBus bus,
        uint pc,
        uint basePc,
        ReadOnlySpan<uint> instructions,
        out int firstRegister,
        out int skippedInstructions)
    {
        firstRegister = 0;
        skippedInstructions = 0;
        uint offset = pc - basePc;
        if (pc < basePc || offset % sizeof(uint) != 0)
        {
            return false;
        }

        uint instructionIndex = offset / sizeof(uint);
        if (instructionIndex >= instructions.Length - 1)
        {
            return false;
        }

        for (uint index = instructionIndex; index < instructions.Length; index++)
        {
            if (bus.Read32(basePc + index * sizeof(uint)) != instructions[(int)index])
            {
                return false;
            }
        }

        firstRegister = 24 + (int)instructionIndex;
        skippedInstructions = instructions.Length - (int)instructionIndex;
        return true;
    }

    private static bool TryFastForwardSonicGxAttributeStateSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxAttributeStateSetter(bus, state.Pc))
        {
            return false;
        }

        uint attribute = state.Gpr[3];
        uint parameter = state.Gpr[4];
        uint caseIndex = unchecked(parameter - 9u);
        if (caseIndex is not (0 or 4))
        {
            return false;
        }

        uint stateBlockPointerAddress = unchecked(state.Gpr[13] + 0xFFFF_8380u);
        if (!bus.Memory.IsMainRamAddress(stateBlockPointerAddress, sizeof(uint)))
        {
            return false;
        }

        uint stateBlock = bus.Memory.Read32(stateBlockPointerAddress);
        uint skipped = caseIndex == 0 ? 43u : 44u;
        if (!bus.Memory.IsMainRamAddress(stateBlock, 0x4F4)
            || attribute > 7
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        uint attributeOffset = attribute * sizeof(uint);
        uint wordAddress = unchecked(stateBlock + attributeOffset + 0x1C);
        uint originalWord = bus.Memory.Read32(wordAddress);
        uint word = originalWord;
        uint finalR6;
        uint finalR8;
        uint target;
        if (caseIndex == 0)
        {
            word = Rlwinm(word, 0, 0, 30) | state.Gpr[5];
            bus.Memory.Write32(wordAddress, word);
            uint shiftedR6 = Rlwinm(state.Gpr[6], 1, 0, 30);
            word = bus.Memory.Read32(wordAddress);
            word = Rlwinm(word, 0, 31, 27) | shiftedR6;
            bus.Memory.Write32(wordAddress, word);
            uint r7Bits = Rlwinm(state.Gpr[7], 4, 20, 27);
            word = bus.Memory.Read32(wordAddress);
            uint maskedThirdWord = Rlwinm(word, 0, 28, 22);
            word = maskedThirdWord | r7Bits;
            bus.Memory.Write32(wordAddress, word);
            finalR6 = shiftedR6;
            finalR8 = unchecked(stateBlock + attributeOffset + 0x3C);
            target = 0x8010_0D80;
        }
        else
        {
            word = Rlwinm(word, 0, 11, 9) | Rlwinm(state.Gpr[5], 21, 0, 10);
            bus.Memory.Write32(wordAddress, word);
            word = bus.Memory.Read32(wordAddress);
            uint maskedSecondWord = Rlwinm(word, 0, 10, 6);
            word = maskedSecondWord | Rlwinm(state.Gpr[6], 22, 0, 9);
            bus.Memory.Write32(wordAddress, word);
            word = bus.Memory.Read32(wordAddress);
            word = Rlwinm(word, 0, 7, 1) | Rlwinm(state.Gpr[7], 25, 0, 6);
            bus.Memory.Write32(wordAddress, word);
            finalR6 = maskedSecondWord;
            finalR8 = originalWord;
            target = 0x8010_0E78;
        }

        uint attributeByte = attribute & 0xFFu;
        uint attributeMask = Rlwinm(unchecked(attributeByte + 1u), (int)attributeByte, 24, 31);
        uint dirtyFlags = bus.Memory.Read32(stateBlock + 0x4F0) | 0x10u;
        byte oldDirtyAttributeFlags = bus.Memory.Read8(stateBlock + 0x4EE);
        byte dirtyAttributeFlags = (byte)(oldDirtyAttributeFlags | attributeMask);
        bus.Memory.Write32(stateBlock + 0x4F0, dirtyFlags);
        bus.Memory.Write8(stateBlock + 0x4EE, dirtyAttributeFlags);

        state.Gpr[0] = dirtyAttributeFlags;
        state.Gpr[3] = oldDirtyAttributeFlags;
        state.Gpr[4] = stateBlock;
        state.Gpr[5] = stateBlock;
        state.Gpr[6] = finalR6;
        state.Gpr[8] = finalR8;
        state.Gpr[9] = unchecked(stateBlock + attributeOffset + 0x5C);
        state.Gpr[10] = 0x801D_26D0;
        state.Ctr = target;
        state.Pc = state.Lr & 0xFFFF_FFFCu;
        SetCr0ForUnsignedCompareImmediate(state, caseIndex, 16);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicGxAttributeStateSetter(GameCubeBus bus, uint pc) =>
        pc == SonicGxAttributeStateSetterPc
        && bus.Read32(pc + 0x000) == 0x3804_FFF7
        && bus.Read32(pc + 0x004) == 0x810D_8380
        && bus.Read32(pc + 0x008) == 0x5464_103A
        && bus.Read32(pc + 0x00C) == 0x7D28_2214
        && bus.Read32(pc + 0x010) == 0x2800_0010
        && bus.Read32(pc + 0x014) == 0x3889_001C
        && bus.Read32(pc + 0x018) == 0x3909_003C
        && bus.Read32(pc + 0x01C) == 0x3929_005C
        && bus.Read32(pc + 0x020) == 0x4181_0308
        && bus.Read32(pc + 0x024) == 0x3D40_801D
        && bus.Read32(pc + 0x028) == 0x394A_26D0
        && bus.Read32(pc + 0x02C) == 0x5400_103A
        && bus.Read32(pc + 0x030) == 0x7C0A_002E
        && bus.Read32(pc + 0x034) == 0x7C09_03A6
        && bus.Read32(pc + 0x038) == 0x4E80_0420
        && bus.Read32(pc + 0x03C) == 0x8004_0000
        && bus.Read32(pc + 0x040) == 0x54C6_083C
        && bus.Read32(pc + 0x044) == 0x5400_003C
        && bus.Read32(pc + 0x048) == 0x7C00_2B78
        && bus.Read32(pc + 0x04C) == 0x9004_0000
        && bus.Read32(pc + 0x050) == 0x54E0_2536
        && bus.Read32(pc + 0x054) == 0x80A4_0000
        && bus.Read32(pc + 0x058) == 0x54A5_07F6
        && bus.Read32(pc + 0x05C) == 0x7CA5_3378
        && bus.Read32(pc + 0x060) == 0x90A4_0000
        && bus.Read32(pc + 0x064) == 0x80A4_0000
        && bus.Read32(pc + 0x068) == 0x54A5_072C
        && bus.Read32(pc + 0x06C) == 0x7CA0_0378
        && bus.Read32(pc + 0x070) == 0x9004_0000
        && bus.Read32(pc + 0x074) == 0x4800_02B4
        && bus.Read32(pc + 0x134) == 0x8104_0000
        && bus.Read32(pc + 0x138) == 0x54A0_A814
        && bus.Read32(pc + 0x13C) == 0x5505_02D2
        && bus.Read32(pc + 0x140) == 0x7CA0_0378
        && bus.Read32(pc + 0x144) == 0x9004_0000
        && bus.Read32(pc + 0x148) == 0x54C5_B012
        && bus.Read32(pc + 0x14C) == 0x54E0_C80C
        && bus.Read32(pc + 0x150) == 0x80C4_0000
        && bus.Read32(pc + 0x154) == 0x54C6_028C
        && bus.Read32(pc + 0x158) == 0x7CC5_2B78
        && bus.Read32(pc + 0x15C) == 0x90A4_0000
        && bus.Read32(pc + 0x160) == 0x80A4_0000
        && bus.Read32(pc + 0x164) == 0x54A5_01C2
        && bus.Read32(pc + 0x168) == 0x7CA0_0378
        && bus.Read32(pc + 0x16C) == 0x9004_0000
        && bus.Read32(pc + 0x170) == 0x4800_01B8
        && bus.Read32(pc + 0x328) == 0x80AD_8380
        && bus.Read32(pc + 0x32C) == 0x5460_063E
        && bus.Read32(pc + 0x330) == 0x3860_0001
        && bus.Read32(pc + 0x334) == 0x8085_04F0
        && bus.Read32(pc + 0x338) == 0x7C60_0030
        && bus.Read32(pc + 0x33C) == 0x5400_063E
        && bus.Read32(pc + 0x340) == 0x6083_0010
        && bus.Read32(pc + 0x344) == 0x9065_04F0
        && bus.Read32(pc + 0x348) == 0x808D_8380
        && bus.Read32(pc + 0x34C) == 0x8864_04EE
        && bus.Read32(pc + 0x350) == 0x7C60_0378
        && bus.Read32(pc + 0x354) == 0x9804_04EE
        && bus.Read32(pc + 0x358) == 0x4E80_0020;

    private static bool TryFastForwardSonicGxFloatStripEmitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicGxFloatStripEmitLoop(bus, pc))
        {
            return false;
        }

        uint vertices = state.Gpr[30];
        uint stream = state.Gpr[26];
        uint vertexBase = state.Gpr[27];
        uint extraStreamStride = state.Gpr[31];
        if (vertices == 0
            || vertices > 0x10000
            || extraStreamStride > 0x40)
        {
            return false;
        }

        uint skipped = checked(vertices * SonicGxFloatStripEmitInstructionsPerIteration + SonicGxFloatStripEmitExitInstructions);
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        ulong streamBytes = (ulong)vertices * (sizeof(ushort) + extraStreamStride);
        if (streamBytes > int.MaxValue || !bus.Memory.IsMainRamAddress(stream, checked((int)streamBytes)))
        {
            return false;
        }

        uint currentStream = stream;
        for (uint vertex = 0; vertex < vertices; vertex++)
        {
            short index = unchecked((short)bus.Memory.Read16(currentStream));
            uint vertexAddress = unchecked(vertexBase + ((uint)index << 5));
            if (!bus.Memory.IsMainRamAddress(vertexAddress, 0x18))
            {
                return false;
            }

            currentStream = unchecked(currentStream + sizeof(ushort) + extraStreamStride);
        }

        currentStream = stream;
        uint lastShiftedIndex = state.Gpr[0];
        uint lastVertexAddress = state.Gpr[29];
        double lastX = state.Fpr[1];
        double lastY = state.Fpr[2];
        double lastZ = state.Fpr[3];
        const uint fifo = 0xCC00_8000;

        for (uint vertex = 0; vertex < vertices; vertex++)
        {
            short index = unchecked((short)bus.Memory.Read16(currentStream));
            currentStream = unchecked(currentStream + sizeof(ushort));
            lastShiftedIndex = unchecked((uint)index << 5);
            lastVertexAddress = unchecked(vertexBase + lastShiftedIndex);

            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x04));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x08));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x0C));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x10));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x14));

            lastX = SingleBitsToFprDouble(bus.Memory.Read32(lastVertexAddress + 0x0C));
            lastY = SingleBitsToFprDouble(bus.Memory.Read32(lastVertexAddress + 0x10));
            lastZ = SingleBitsToFprDouble(bus.Memory.Read32(lastVertexAddress + 0x14));
            currentStream = unchecked(currentStream + extraStreamStride);
        }

        state.Gpr[0] = lastShiftedIndex;
        state.Gpr[3] = 0xCC01_0000;
        state.Gpr[26] = currentStream;
        state.Gpr[29] = lastVertexAddress;
        state.Gpr[30] = 0;
        state.Fpr[1] = lastX;
        state.Fpr[2] = lastY;
        state.Fpr[3] = lastZ;
        state.Lr = pc + 0x44;
        state.Pc = pc + 0x44;
        SetCr0(state, 0);

        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicGxFloatStripEmitLoop(GameCubeBus bus, uint pc) =>
        pc == SonicGxFloatStripEmitLoopPc
        && bus.Read32(pc - 0x30) == 0xA81A_0000
        && bus.Read32(pc - 0x2C) == 0x3B5A_0002
        && bus.Read32(pc - 0x28) == 0x7C1E_0378
        && bus.Read32(pc - 0x24) == 0x2C1E_0000
        && bus.Read32(pc - 0x20) == 0x4080_0008
        && bus.Read32(pc - 0x1C) == 0x7FDE_00D0
        && bus.Read32(pc - 0x18) == 0x3860_0098
        && bus.Read32(pc - 0x14) == 0x3880_0000
        && bus.Read32(pc - 0x10) == 0x57C5_043E
        && bus.Read32(pc - 0x0C) == 0x4BFE_4345
        && bus.Read32(pc - 0x08) == 0x4800_0004
        && bus.Read32(pc - 0x04) == 0x4800_0004
        && bus.Read32(pc + 0x00) == 0xA81A_0000
        && bus.Read32(pc + 0x04) == 0x3B5A_0002
        && bus.Read32(pc + 0x08) == 0x5400_2834
        && bus.Read32(pc + 0x0C) == 0x7FBB_0214
        && bus.Read32(pc + 0x10) == 0xC03D_0000
        && bus.Read32(pc + 0x14) == 0xC05D_0004
        && bus.Read32(pc + 0x18) == 0xC07D_0008
        && bus.Read32(pc + 0x1C) == 0x4800_0065
        && bus.Read32(pc + 0x20) == 0xC03D_000C
        && bus.Read32(pc + 0x24) == 0xC05D_0010
        && bus.Read32(pc + 0x28) == 0xC07D_0014
        && bus.Read32(pc + 0x2C) == 0x4800_0041
        && bus.Read32(pc + 0x30) == 0x7F5A_FA14
        && bus.Read32(pc + 0x34) == 0x3BDE_FFFF
        && bus.Read32(pc + 0x38) == 0x2C1E_0000
        && bus.Read32(pc + 0x3C) == 0x4082_FFC4
        && bus.Read32(pc + 0x40) == 0x4800_0029
        && bus.Read32(pc + 0x44) == 0x3B9C_FFFF
        && bus.Read32(pc + 0x48) == 0x281C_0000
        && bus.Read32(pc + 0x4C) == 0x4082_FF84
        && bus.Read32(pc + 0x68) == 0x4E80_0020
        && bus.Read32(pc + 0x6C) == 0x3C60_CC01
        && bus.Read32(pc + 0x70) == 0xD023_8000
        && bus.Read32(pc + 0x74) == 0xD043_8000
        && bus.Read32(pc + 0x78) == 0xD063_8000
        && bus.Read32(pc + 0x7C) == 0x4E80_0020
        && bus.Read32(pc + 0x80) == 0x3C60_CC01
        && bus.Read32(pc + 0x84) == 0xD023_8000
        && bus.Read32(pc + 0x88) == 0xD043_8000
        && bus.Read32(pc + 0x8C) == 0xD063_8000
        && bus.Read32(pc + 0x90) == 0x4E80_0020;

    private static bool TryFastForwardSonicGxFloatAttributeStripEmitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicGxFloatAttributeStripEmitLoop(bus, pc))
        {
            return false;
        }

        uint vertices = state.Gpr[30];
        uint stream = state.Gpr[26];
        uint vertexBase = state.Gpr[27];
        uint extraStreamStride = state.Gpr[31];
        if (vertices == 0
            || vertices > 0x10000
            || extraStreamStride > 0x40)
        {
            return false;
        }

        uint skipped = checked(vertices * SonicGxFloatAttributeStripEmitInstructionsPerIteration + SonicGxFloatAttributeStripEmitExitInstructions);
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        ulong streamBytes = (ulong)vertices * (sizeof(ushort) + extraStreamStride);
        if (streamBytes > int.MaxValue || !bus.Memory.IsMainRamAddress(stream, checked((int)streamBytes)))
        {
            return false;
        }

        uint currentStream = stream;
        for (uint vertex = 0; vertex < vertices; vertex++)
        {
            short index = unchecked((short)bus.Memory.Read16(currentStream));
            uint vertexAddress = unchecked(vertexBase + ((uint)index << 5));
            if (!bus.Memory.IsMainRamAddress(vertexAddress, 0x1C))
            {
                return false;
            }

            currentStream = unchecked(currentStream + sizeof(ushort) + extraStreamStride);
        }

        currentStream = stream;
        uint lastShiftedIndex = state.Gpr[0];
        uint lastVertexAddress = state.Gpr[29];
        uint lastAttribute = state.Gpr[3];
        double lastX = state.Fpr[1];
        double lastY = state.Fpr[2];
        double lastZ = state.Fpr[3];
        const uint fifo = 0xCC00_8000;

        for (uint vertex = 0; vertex < vertices; vertex++)
        {
            short index = unchecked((short)bus.Memory.Read16(currentStream));
            currentStream = unchecked(currentStream + sizeof(ushort));
            lastShiftedIndex = unchecked((uint)index << 5);
            lastVertexAddress = unchecked(vertexBase + lastShiftedIndex);

            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x04));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x08));
            lastAttribute = bus.Memory.Read32(lastVertexAddress + 0x18);
            bus.Write32(fifo, lastAttribute);

            lastX = SingleBitsToFprDouble(bus.Memory.Read32(lastVertexAddress));
            lastY = SingleBitsToFprDouble(bus.Memory.Read32(lastVertexAddress + 0x04));
            lastZ = SingleBitsToFprDouble(bus.Memory.Read32(lastVertexAddress + 0x08));
            currentStream = unchecked(currentStream + extraStreamStride);
        }

        state.Gpr[0] = lastShiftedIndex;
        state.Gpr[3] = lastAttribute;
        state.Gpr[4] = 0xCC01_0000;
        state.Gpr[26] = currentStream;
        state.Gpr[29] = lastVertexAddress;
        state.Gpr[30] = 0;
        state.Fpr[1] = lastX;
        state.Fpr[2] = lastY;
        state.Fpr[3] = lastZ;
        state.Lr = pc + 0x3C;
        state.Pc = pc + 0x3C;
        SetCr0(state, 0);

        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicGxFloatAttributeStripEmitLoop(GameCubeBus bus, uint pc) =>
        pc == SonicGxFloatAttributeStripEmitLoopPc
        && bus.Read32(pc - 0x30) == 0xA81A_0000
        && bus.Read32(pc - 0x2C) == 0x3B5A_0002
        && bus.Read32(pc - 0x28) == 0x7C1E_0378
        && bus.Read32(pc - 0x24) == 0x2C1E_0000
        && bus.Read32(pc - 0x20) == 0x4080_0008
        && bus.Read32(pc - 0x1C) == 0x7FDE_00D0
        && bus.Read32(pc - 0x18) == 0x3860_0098
        && bus.Read32(pc - 0x14) == 0x3880_0000
        && bus.Read32(pc - 0x10) == 0x57C5_043E
        && bus.Read32(pc - 0x0C) == 0x4BFE_19E5
        && bus.Read32(pc - 0x08) == 0x4800_0004
        && bus.Read32(pc - 0x04) == 0x4800_0004
        && bus.Read32(pc + 0x00) == 0xA81A_0000
        && bus.Read32(pc + 0x04) == 0x3B5A_0002
        && bus.Read32(pc + 0x08) == 0x5400_2834
        && bus.Read32(pc + 0x0C) == 0x7FBB_0214
        && bus.Read32(pc + 0x10) == 0xC03D_0000
        && bus.Read32(pc + 0x14) == 0xC05D_0004
        && bus.Read32(pc + 0x18) == 0xC07D_0008
        && bus.Read32(pc + 0x1C) == 0x4800_0055
        && bus.Read32(pc + 0x20) == 0x807D_0018
        && bus.Read32(pc + 0x24) == 0x4800_0041
        && bus.Read32(pc + 0x28) == 0x7F5A_FA14
        && bus.Read32(pc + 0x2C) == 0x3BDE_FFFF
        && bus.Read32(pc + 0x30) == 0x2C1E_0000
        && bus.Read32(pc + 0x34) == 0x4082_FFCC
        && bus.Read32(pc + 0x38) == 0x4800_0029
        && bus.Read32(pc + 0x60) == 0x4E80_0020
        && bus.Read32(pc + 0x64) == 0x3C80_CC01
        && bus.Read32(pc + 0x68) == 0x9064_8000
        && bus.Read32(pc + 0x6C) == 0x4E80_0020
        && bus.Read32(pc + 0x70) == 0x3C60_CC01
        && bus.Read32(pc + 0x74) == 0xD023_8000
        && bus.Read32(pc + 0x78) == 0xD043_8000
        && bus.Read32(pc + 0x7C) == 0xD063_8000
        && bus.Read32(pc + 0x80) == 0x4E80_0020;

    private static bool TryFastForwardSonicGxFloatTexcoordStripEmitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicGxFloatTexcoordStripEmitLoop(bus, pc))
        {
            return false;
        }

        uint vertices = state.Gpr[31];
        uint stream = state.Gpr[25];
        uint vertexBase = state.Gpr[26];
        if (vertices == 0 || vertices > 0x10000)
        {
            return false;
        }

        uint skipped = checked(vertices * SonicGxFloatTexcoordStripEmitInstructionsPerIteration + SonicGxFloatTexcoordStripEmitExitInstructions);
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        ulong streamBytes = (ulong)vertices * (sizeof(ushort) * 3ul);
        if (streamBytes > int.MaxValue || !bus.Memory.IsMainRamAddress(stream, checked((int)streamBytes)))
        {
            return false;
        }

        uint currentStream = stream;
        for (uint vertex = 0; vertex < vertices; vertex++)
        {
            short index = unchecked((short)bus.Memory.Read16(currentStream));
            uint vertexAddress = unchecked(vertexBase + ((uint)index << 5));
            if (!bus.Memory.IsMainRamAddress(vertexAddress, 0x18))
            {
                return false;
            }

            currentStream = unchecked(currentStream + sizeof(ushort) * 3u);
        }

        currentStream = stream;
        uint lastShiftedIndex = state.Gpr[0];
        uint lastVertexAddress = state.Gpr[28];
        uint lastFirstHalf = state.Gpr[30];
        uint lastSecondHalf = state.Gpr[29];
        double lastX = state.Fpr[1];
        double lastY = state.Fpr[2];
        double lastZ = state.Fpr[3];
        const uint fifo = 0xCC00_8000;

        for (uint vertex = 0; vertex < vertices; vertex++)
        {
            short index = unchecked((short)bus.Memory.Read16(currentStream));
            currentStream = unchecked(currentStream + sizeof(ushort));
            lastShiftedIndex = unchecked((uint)index << 5);
            lastVertexAddress = unchecked(vertexBase + lastShiftedIndex);
            lastFirstHalf = unchecked((uint)(short)bus.Memory.Read16(currentStream));
            currentStream = unchecked(currentStream + sizeof(ushort));
            lastSecondHalf = unchecked((uint)(short)bus.Memory.Read16(currentStream));
            currentStream = unchecked(currentStream + sizeof(ushort));

            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x04));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x08));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x0C));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x10));
            bus.Write32(fifo, bus.Memory.Read32(lastVertexAddress + 0x14));
            bus.Write16(fifo, (ushort)lastFirstHalf);
            bus.Write16(fifo, (ushort)lastSecondHalf);

            lastX = SingleBitsToFprDouble(bus.Memory.Read32(lastVertexAddress + 0x0C));
            lastY = SingleBitsToFprDouble(bus.Memory.Read32(lastVertexAddress + 0x10));
            lastZ = SingleBitsToFprDouble(bus.Memory.Read32(lastVertexAddress + 0x14));
        }

        state.Gpr[0] = lastShiftedIndex;
        state.Gpr[3] = lastFirstHalf;
        state.Gpr[4] = lastSecondHalf;
        state.Gpr[5] = 0xCC01_0000;
        state.Gpr[25] = currentStream;
        state.Gpr[28] = lastVertexAddress;
        state.Gpr[29] = lastSecondHalf;
        state.Gpr[30] = lastFirstHalf;
        state.Gpr[31] = 0;
        state.Fpr[1] = lastX;
        state.Fpr[2] = lastY;
        state.Fpr[3] = lastZ;
        state.Lr = pc + 0x5C;
        state.Pc = pc + 0x5C;
        SetCr0(state, 0);

        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicGxFloatTexcoordStripEmitLoop(GameCubeBus bus, uint pc) =>
        pc == SonicGxFloatTexcoordStripEmitLoopPc
        && bus.Read32(pc - 0x30) == 0xA819_0000
        && bus.Read32(pc - 0x2C) == 0x3B39_0002
        && bus.Read32(pc - 0x28) == 0x7C1F_0378
        && bus.Read32(pc - 0x24) == 0x2C1F_0000
        && bus.Read32(pc - 0x20) == 0x4080_0008
        && bus.Read32(pc - 0x1C) == 0x7FFF_00D0
        && bus.Read32(pc - 0x18) == 0x3860_0098
        && bus.Read32(pc - 0x14) == 0x3880_0000
        && bus.Read32(pc - 0x10) == 0x57E5_043E
        && bus.Read32(pc - 0x0C) == 0x4BFE_40F5
        && bus.Read32(pc - 0x08) == 0x4800_0004
        && bus.Read32(pc - 0x04) == 0x4800_0004
        && bus.Read32(pc + 0x00) == 0xA819_0000
        && bus.Read32(pc + 0x04) == 0x3B39_0002
        && bus.Read32(pc + 0x08) == 0x5400_2834
        && bus.Read32(pc + 0x0C) == 0x7F9A_0214
        && bus.Read32(pc + 0x10) == 0xABD9_0000
        && bus.Read32(pc + 0x14) == 0x3B39_0002
        && bus.Read32(pc + 0x18) == 0xABB9_0000
        && bus.Read32(pc + 0x1C) == 0x3B39_0002
        && bus.Read32(pc + 0x20) == 0xC03C_0000
        && bus.Read32(pc + 0x24) == 0xC05C_0004
        && bus.Read32(pc + 0x28) == 0xC07C_0008
        && bus.Read32(pc + 0x2C) == 0x4800_007D
        && bus.Read32(pc + 0x30) == 0xC03C_000C
        && bus.Read32(pc + 0x34) == 0xC05C_0010
        && bus.Read32(pc + 0x38) == 0xC07C_0014
        && bus.Read32(pc + 0x3C) == 0x4800_0059
        && bus.Read32(pc + 0x40) == 0x7FC3_F378
        && bus.Read32(pc + 0x44) == 0x7FA4_EB78
        && bus.Read32(pc + 0x48) == 0x4800_003D
        && bus.Read32(pc + 0x4C) == 0x3BFF_FFFF
        && bus.Read32(pc + 0x50) == 0x2C1F_0000
        && bus.Read32(pc + 0x54) == 0x4082_FFAC
        && bus.Read32(pc + 0x58) == 0x4800_0029
        && bus.Read32(pc + 0x5C) == 0x3B7B_FFFF
        && bus.Read32(pc + 0x60) == 0x281B_0000
        && bus.Read32(pc + 0x64) == 0x4082_FF6C
        && bus.Read32(pc + 0x80) == 0x4E80_0020
        && bus.Read32(pc + 0x84) == 0x3CA0_CC01
        && bus.Read32(pc + 0x88) == 0xB065_8000
        && bus.Read32(pc + 0x8C) == 0xB085_8000
        && bus.Read32(pc + 0x90) == 0x4E80_0020
        && bus.Read32(pc + 0x94) == 0x3C60_CC01
        && bus.Read32(pc + 0x98) == 0xD023_8000
        && bus.Read32(pc + 0x9C) == 0xD043_8000
        && bus.Read32(pc + 0xA0) == 0xD063_8000
        && bus.Read32(pc + 0xA4) == 0x4E80_0020
        && bus.Read32(pc + 0xA8) == 0x3C60_CC01
        && bus.Read32(pc + 0xAC) == 0xD023_8000
        && bus.Read32(pc + 0xB0) == 0xD043_8000
        && bus.Read32(pc + 0xB4) == 0xD063_8000
        && bus.Read32(pc + 0xB8) == 0x4E80_0020;

    private static double SingleBitsToFprDouble(uint value) =>
        BitConverter.Int32BitsToSingle(unchecked((int)value));

    private static uint ReplaceTopByte(uint value, byte topByte) =>
        ((uint)topByte << 24) | (value & 0x00FF_FFFF);

    private static bool TryFastForwardSonicBitPlaneCrop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicBitPlaneCrop(bus, pc))
        {
            return false;
        }

        uint source = state.Gpr[3];
        if (!bus.Memory.IsMainRamAddress(source, 24 * 3)
            || !CanFastForwardInstructionCount(state, iterations: 24 * 3, instructionsPerIteration: 36, extraInstructions: 80))
        {
            return false;
        }

        byte[] rows = new byte[24 * 3];
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index] = bus.Memory.Read8(source + (uint)index);
        }

        int leftMargin = 25;
        int rightMargin = 25;
        for (int row = 0; row < 24; row++)
        {
            int rowOffset = row * 3;
            leftMargin = Math.Min(leftMargin, CountLeadingZeroBits24(rows[rowOffset], rows[rowOffset + 1], rows[rowOffset + 2]));
            rightMargin = Math.Min(rightMargin, CountTrailingZeroBits24(rows[rowOffset], rows[rowOffset + 1], rows[rowOffset + 2]));
        }

        for (int row = 0; row < 24; row++)
        {
            int rowOffset = row * 3;
            for (int outputByte = 0; outputByte < 3; outputByte++)
            {
                int output = 0;
                int bitStart = leftMargin + outputByte * 8;
                for (int outputBit = 0; outputBit < 8; outputBit++)
                {
                    int sourceBit = bitStart + outputBit;
                    if (sourceBit < 24 && GetSonicBitPlaneBit(rows, rowOffset, sourceBit) != 0)
                    {
                        output |= 1 << outputBit;
                    }
                }

                bus.Memory.Write8(source + (uint)(rowOffset + outputByte), (byte)output);
            }
        }

        uint width = unchecked((uint)(24 - leftMargin - rightMargin));
        uint result = (width & 0x8000_0000) != 0 || width == 0 ? 18u : width;
        uint skipped = 24u * 3u * 36u + 80u;
        state.Gpr[0] = unchecked(24u - (uint)leftMargin);
        state.Gpr[3] = result;
        state.Gpr[5] = unchecked((uint)leftMargin + 24u);
        state.Gpr[6] = source + 72;
        state.Gpr[7] = 24;
        state.Gpr[8] = 3;
        state.Gpr[9] = unchecked((uint)leftMargin);
        state.Gpr[10] = unchecked((uint)rightMargin);
        state.Gpr[11] = 8;
        state.Gpr[12] = 24;
        state.Ctr = 0;
        SetCr0(state, width);
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static int CountLeadingZeroBits24(byte first, byte second, byte third)
    {
        if (first != 0)
        {
            return CountTrailingZeroBits8(first);
        }

        if (second != 0)
        {
            return 8 + CountTrailingZeroBits8(second);
        }

        if (third != 0)
        {
            return 16 + CountTrailingZeroBits8(third);
        }

        return 24;
    }

    private static int CountTrailingZeroBits24(byte first, byte second, byte third)
    {
        if (third != 0)
        {
            return CountLeadingZeroBits8(third);
        }

        if (second != 0)
        {
            return 8 + CountLeadingZeroBits8(second);
        }

        if (first != 0)
        {
            return 16 + CountLeadingZeroBits8(first);
        }

        return 24;
    }

    private static int CountTrailingZeroBits8(byte value)
    {
        for (int bit = 0; bit < 8; bit++)
        {
            if (((value >> bit) & 1) != 0)
            {
                return bit;
            }
        }

        return 8;
    }

    private static int CountLeadingZeroBits8(byte value)
    {
        for (int bit = 7; bit >= 0; bit--)
        {
            if (((value >> bit) & 1) != 0)
            {
                return 7 - bit;
            }
        }

        return 8;
    }

    private static int GetSonicBitPlaneBit(byte[] rows, int rowOffset, int bit)
    {
        int byteIndex = bit / 8;
        int bitIndex = bit - byteIndex * 8;
        return (rows[rowOffset + byteIndex] >> bitIndex) & 1;
    }

    private static bool MatchesSonicBitPlaneCrop(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x000) == 0x9421_FFD8
        && bus.Read32(pc + 0x004) == 0x3800_0018
        && bus.Read32(pc + 0x008) == 0x7C09_03A6
        && bus.Read32(pc + 0x014) == 0x3920_0019
        && bus.Read32(pc + 0x018) == 0x3940_0019
        && bus.Read32(pc + 0x074) == 0x4182_FFEC
        && bus.Read32(pc + 0x104) == 0x38E0_0000
        && bus.Read32(pc + 0x108) == 0x38A9_0000
        && bus.Read32(pc + 0x110) == 0x3900_0000
        && bus.Read32(pc + 0x118) == 0x7C09_03A6
        && bus.Read32(pc + 0x240) == 0x9BE6_0000
        && bus.Read32(pc + 0x264) == 0x2009_0018
        && bus.Read32(pc + 0x268) == 0x7C6A_0051
        && bus.Read32(pc + 0x274) == 0x83E1_0024
        && bus.Read32(pc + 0x278) == 0x3821_0028
        && bus.Read32(pc + 0x27C) == 0x4E80_0020;

    private sealed record SonicPathLookupPending(
        int EntryInstruction,
        uint EntryPc,
        uint Lr,
        uint Path,
        SonicPathLookupPrediction Prediction,
        uint EntryR0,
        uint EntryR4,
        uint EntryR5,
        uint EntryR6,
        uint EntryCr,
        uint[] EntryGpr,
        uint StackBase,
        string EntryStackWindow,
        ulong EntryTimeBase,
        uint EntryDecrementer,
        bool FastForwardEligible = false,
        int InterruptEntries = 0);

    private sealed record SonicPathLookupPrediction(
        bool Success,
        uint Result,
        string Reason,
        string PathText,
        ulong EstimatedCycles = 0,
        uint CandidateEntries = 0,
        uint SegmentComparisons = 0,
        uint CompareBytes = 0);

    private readonly record struct SonicPathLookupEntry(uint Word0, uint ParentIndex, uint EndIndex)
    {
        public bool HasChildren => (Word0 & 0xFF00_0000) != 0;

        public uint NameOffset => Word0 & 0x00FF_FFFF;
    }

    private static string FormatGprSnapshot(IReadOnlyList<uint> gpr)
    {
        string[] fields = new string[32];
        for (int index = 0; index < fields.Length; index++)
        {
            fields[index] = $"r{index}=0x{gpr[index]:X8}";
        }

        return string.Join(';', fields);
    }

    private static string CaptureMemoryWindowHex(GameCubeMemory memory, uint address, int length)
    {
        if (!memory.IsMainRamAddress(address, length))
        {
            return string.Empty;
        }

        char[] chars = new char[length * 2];
        const string hex = "0123456789ABCDEF";
        for (int offset = 0; offset < length; offset++)
        {
            byte value = memory.Read8(address + (uint)offset);
            chars[offset * 2] = hex[value >> 4];
            chars[offset * 2 + 1] = hex[value & 0x0F];
        }

        return new string(chars);
    }

    private static SonicPathLookupPrediction PredictSonicPathLookup(GameCubeBus bus, PowerPcState state)
    {
        GameCubeMemory memory = bus.Memory;
        uint candidateEntries = 0;
        uint segmentComparisons = 0;
        uint compareBytes = 0;
        SonicPathLookupPrediction Fail(string reason, string pathText) =>
            new(false, 0, reason, pathText, EstimateSonicPathLookupCycles(candidateEntries, segmentComparisons, compareBytes), candidateEntries, segmentComparisons, compareBytes);

        SonicPathLookupPrediction Ok(uint result, string reason, string pathText) =>
            new(true, result, reason, pathText, EstimateSonicPathLookupCycles(candidateEntries, segmentComparisons, compareBytes), candidateEntries, segmentComparisons, compareBytes);

        if (!TryMatchSonicByteTableLookup(bus, SonicPathLookupByteTablePc, out uint byteTableAddress)
            || !memory.IsMainRamAddress(byteTableAddress, 0x100))
        {
            return Fail("byte-table-signature", string.Empty);
        }

        uint pathAddress = state.Gpr[3];
        if (!TryReadNullTerminatedAscii(memory, pathAddress, out string pathText))
        {
            return Fail("path-string", string.Empty);
        }

        uint smallData = state.Gpr[13];
        if (!TryReadMainRam32(memory, unchecked(smallData - 30068), out uint entryTable)
            || !TryReadMainRam32(memory, unchecked(smallData - 30064), out uint nameTable)
            || !TryReadMainRam32(memory, unchecked(smallData - 30056), out uint currentIndex)
            || !TryReadMainRam32(memory, unchecked(smallData - 30052), out uint warningFlag))
        {
            return Fail("globals", pathText);
        }

        uint pathCursor = pathAddress;
        for (uint iterations = 0; iterations < MaxFastForwardStringLengthBytes; iterations++)
        {
            if (!memory.IsMainRamAddress(pathCursor, 1))
            {
                return Fail("path-cursor", pathText);
            }

            byte first = memory.Read8(pathCursor);
            if (first == 0)
            {
                return Ok(currentIndex, "empty-or-complete", pathText);
            }

            if (first == (byte)'/')
            {
                currentIndex = 0;
                pathCursor++;
                continue;
            }

            if (first == (byte)'.')
            {
                if (!memory.IsMainRamAddress(pathCursor + 1, 1))
                {
                    return Fail("dot-peek", pathText);
                }

                byte second = memory.Read8(pathCursor + 1);
                if (second == (byte)'.')
                {
                    if (!memory.IsMainRamAddress(pathCursor + 2, 1))
                    {
                        return Fail("dotdot-peek", pathText);
                    }

                    byte third = memory.Read8(pathCursor + 2);
                    if (third == (byte)'/')
                    {
                        if (!TryReadSonicPathLookupEntry(memory, entryTable, currentIndex, out SonicPathLookupEntry parentEntry))
                        {
                            return Fail("parent-entry", pathText);
                        }

                        currentIndex = parentEntry.ParentIndex;
                        pathCursor += 3;
                        continue;
                    }

                    if (third == 0)
                    {
                        if (!TryReadSonicPathLookupEntry(memory, entryTable, currentIndex, out SonicPathLookupEntry parentEntry))
                        {
                            return Fail("parent-entry", pathText);
                        }

                        return Ok(parentEntry.ParentIndex, "terminal-parent", pathText);
                    }
                }
                else if (second == (byte)'/')
                {
                    pathCursor += 2;
                    continue;
                }
                else if (second == 0)
                {
                    return Ok(currentIndex, "terminal-self", pathText);
                }
            }

            if (!TryFindSonicPathSegmentEnd(memory, pathCursor, out uint segmentEnd, out bool hasTrailingSlash))
            {
                return Fail("segment-end", pathText);
            }

            if (warningFlag == 0 && !SonicPathSegmentAvoidsWarning(memory, pathCursor, segmentEnd))
            {
                return Fail("warning-path", pathText);
            }

            if (!TryReadSonicPathLookupEntry(memory, entryTable, currentIndex, out SonicPathLookupEntry currentEntry))
            {
                return Fail("current-entry", pathText);
            }

            uint segmentLength = unchecked(segmentEnd - pathCursor);
            uint candidateIndex = currentIndex + 1;
            bool matched = false;
            while (candidateIndex < currentEntry.EndIndex)
            {
                if (!TryReadSonicPathLookupEntry(memory, entryTable, candidateIndex, out SonicPathLookupEntry candidateEntry))
                {
                    return Fail("candidate-entry", pathText);
                }

                candidateEntries++;
                if (candidateEntry.HasChildren || !hasTrailingSlash)
                {
                    uint nameAddress = unchecked(nameTable + candidateEntry.NameOffset);
                    segmentComparisons++;
                    if (!TrySonicPathSegmentMatches(memory, byteTableAddress, nameAddress, pathCursor, segmentLength, out bool segmentMatches, out bool nameWasPrefix, out uint comparedBytes))
                    {
                        return Fail("segment-compare", pathText);
                    }

                    compareBytes += comparedBytes;
                    if (segmentMatches)
                    {
                        currentIndex = candidateIndex;
                        matched = true;
                        break;
                    }

                    if (nameWasPrefix)
                    {
                        candidateIndex = candidateEntry.HasChildren ? candidateEntry.EndIndex : candidateIndex + 1;
                        continue;
                    }
                }

                candidateIndex++;
            }

            if (!matched)
            {
                return Ok(0xFFFF_FFFF, "not-found", pathText);
            }

            if (!hasTrailingSlash)
            {
                return Ok(currentIndex, "found", pathText);
            }

            pathCursor = segmentEnd + 1;
        }

        return Fail("iteration-limit", pathText);
    }

    private static ulong EstimateSonicPathLookupCycles(uint candidateEntries, uint segmentComparisons, uint compareBytes)
    {
        const ulong fixedRoutineCost = 12_250;
        const ulong candidateCost = 34;
        const ulong extraByteCost = 64;
        uint extraCompareBytes = compareBytes > candidateEntries ? compareBytes - candidateEntries : 0;
        return fixedRoutineCost
            + candidateEntries * candidateCost
            + extraCompareBytes * extraByteCost;
    }

    private static bool TryReadMainRam32(GameCubeMemory memory, uint address, out uint value)
    {
        value = 0;
        if (!memory.IsMainRamAddress(address, sizeof(uint)))
        {
            return false;
        }

        value = memory.Read32(address);
        return true;
    }

    private static bool TryReadSonicPathLookupEntry(GameCubeMemory memory, uint entryTable, uint index, out SonicPathLookupEntry entry)
    {
        entry = default;
        if (index > 0x0010_0000)
        {
            return false;
        }

        uint address = unchecked(entryTable + index * 12);
        if (!memory.IsMainRamAddress(address, 12))
        {
            return false;
        }

        entry = new SonicPathLookupEntry(memory.Read32(address), memory.Read32(address + 4), memory.Read32(address + 8));
        return true;
    }

    private static bool TryReadNullTerminatedAscii(GameCubeMemory memory, uint address, out string text)
    {
        text = string.Empty;
        List<char> chars = [];
        for (uint offset = 0; offset < MaxFastForwardStringLengthBytes; offset++)
        {
            uint current = unchecked(address + offset);
            if (!memory.IsMainRamAddress(current, 1))
            {
                return false;
            }

            byte value = memory.Read8(current);
            if (value == 0)
            {
                text = new string([.. chars]);
                return true;
            }

            chars.Add(value is >= 0x20 and <= 0x7E ? (char)value : '.');
        }

        return false;
    }

    private static bool TryFindSonicPathSegmentEnd(GameCubeMemory memory, uint segmentStart, out uint segmentEnd, out bool hasTrailingSlash)
    {
        segmentEnd = segmentStart;
        hasTrailingSlash = false;
        for (uint offset = 0; offset < MaxFastForwardStringLengthBytes; offset++)
        {
            uint current = unchecked(segmentStart + offset);
            if (!memory.IsMainRamAddress(current, 1))
            {
                return false;
            }

            byte value = memory.Read8(current);
            if (value == 0)
            {
                segmentEnd = current;
                return true;
            }

            if (value == (byte)'/')
            {
                segmentEnd = current;
                hasTrailingSlash = true;
                return true;
            }
        }

        return false;
    }

    private static bool SonicPathSegmentAvoidsWarning(GameCubeMemory memory, uint segmentStart, uint segmentEnd)
    {
        bool sawDot = false;
        uint suffixStart = 0;
        for (uint current = segmentStart; current < segmentEnd; current++)
        {
            byte value = memory.Read8(current);
            if (value == (byte)'.')
            {
                uint offset = unchecked(current - segmentStart);
                if (offset <= 8 && sawDot)
                {
                    return false;
                }

                if (!sawDot)
                {
                    suffixStart = current + 1;
                    sawDot = true;
                }
            }
            else if (value == (byte)' ')
            {
                return false;
            }
        }

        return !sawDot || unchecked(segmentEnd - suffixStart) <= 3;
    }

    private static bool TrySonicPathSegmentMatches(GameCubeMemory memory, uint byteTableAddress, uint nameAddress, uint segmentStart, uint segmentLength, out bool matches, out bool nameWasPrefix, out uint comparedBytes)
    {
        matches = false;
        nameWasPrefix = false;
        comparedBytes = 0;
        for (uint offset = 0; offset <= segmentLength; offset++)
        {
            uint nameCurrent = unchecked(nameAddress + offset);
            if (!memory.IsMainRamAddress(nameCurrent, 1))
            {
                return false;
            }

            byte nameByte = memory.Read8(nameCurrent);
            if (nameByte == 0)
            {
                matches = offset == segmentLength;
                nameWasPrefix = offset < segmentLength;
                return true;
            }

            if (offset >= segmentLength)
            {
                return true;
            }

            uint segmentCurrent = unchecked(segmentStart + offset);
            if (!memory.IsMainRamAddress(segmentCurrent, 1))
            {
                return false;
            }

            byte segmentByte = memory.Read8(segmentCurrent);
            comparedBytes++;
            if (MapSonicByteTable(memory, byteTableAddress, nameByte) != MapSonicByteTable(memory, byteTableAddress, segmentByte))
            {
                return true;
            }
        }

        return true;
    }

    private static bool CanFastForwardSonicPathLookupCycles(PowerPcState state, GameCubeBus bus, ulong cycles)
    {
        if (cycles == 0
            || cycles > 32_768
            || bus.HasPendingExternalInterrupt
            || !CanFastForwardInstructionCount(state, (uint)cycles, instructionsPerIteration: 1, extraInstructions: 0))
        {
            return false;
        }

        DiscInterfaceDebugSnapshot disc = bus.GetDiscInterfaceDebugSnapshot();
        if (disc.HasPendingCommand && disc.PendingCommandCycles <= cycles + 64)
        {
            return false;
        }

        if (TryGetCyclesUntilNextEnabledVideoInterrupt(bus, out ulong videoCycles) && videoCycles <= cycles + 64)
        {
            return false;
        }

        return true;
    }

    private static bool TryGetCyclesUntilNextEnabledVideoInterrupt(GameCubeBus bus, out ulong cycles)
    {
        cycles = 0;
        ulong best = ulong.MaxValue;
        foreach (uint address in VideoInterruptRegisters)
        {
            if (!bus.TryGetMmioValue(address, out uint value)
                || (value & GameCubeBus.VideoInterruptEnable) == 0)
            {
                continue;
            }

            uint targetLine = value & GameCubeBus.VideoInterruptLineMask;
            ulong currentScanline = bus.VideoCycleCounter / GameCubeBus.VideoCyclesPerScanline;
            ulong currentLine = currentScanline % GameCubeBus.VideoScanlinesPerFrame;
            ulong linesUntil = targetLine > currentLine
                ? targetLine - currentLine
                : targetLine + (ulong)GameCubeBus.VideoScanlinesPerFrame - currentLine;
            ulong cycleOffset = bus.VideoCycleCounter % GameCubeBus.VideoCyclesPerScanline;
            ulong candidate = checked(linesUntil * (ulong)GameCubeBus.VideoCyclesPerScanline - cycleOffset);
            if (candidate == 0)
            {
                candidate = (ulong)GameCubeBus.VideoScanlinesPerFrame * GameCubeBus.VideoCyclesPerScanline;
            }

            best = Math.Min(best, candidate);
        }

        if (best == ulong.MaxValue)
        {
            return false;
        }

        cycles = best;
        return true;
    }

    private static bool TryFastForwardSonicByteTableLookup(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!TryMatchSonicByteTableLookup(bus, pc, out uint tableAddress))
        {
            return false;
        }

        uint input = state.Gpr[3];
        if (input == 0xFFFF_FFFF)
        {
            state.Gpr[3] = 0xFFFF_FFFF;
            SetCr0ForSignedCompareImmediate(state, input, -1);
            state.Pc = state.Lr;
            AdvanceFastForwardedInstructions(state, bus, 3);
            skippedInstructions = 3;
            return true;
        }

        uint index = input & 0xFF;
        uint address = tableAddress + index;
        if (!bus.Memory.IsMainRamAddress(address, 1))
        {
            return false;
        }

        state.Gpr[0] = tableAddress;
        state.Gpr[3] = bus.Memory.Read8(address);
        state.Gpr[4] = tableAddress & 0xFFFF_0000u;
        SetCr0ForSignedCompareImmediate(state, input, -1);
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 9);
        skippedInstructions = 9;
        return true;
    }

    private static bool TryFastForwardSonicNormalizedStringScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicNormalizedStringScan(bus, pc, out uint tableAddress)
            || !bus.Memory.IsMainRamAddress(tableAddress, 0x100))
        {
            return false;
        }

        uint first = state.Gpr[20];
        uint second = state.Gpr[21];
        uint currentFirst = first;
        uint currentSecond = second;
        uint lastMappedFirst = state.Gpr[22];
        uint lastMappedSecond = state.Gpr[3];
        uint lastPeek = state.Gpr[0];
        uint iterations = 0;
        bool mismatch = false;
        bool terminal = false;

        while (iterations < MaxFastForwardStringCompareBytes)
        {
            if (!bus.Memory.IsMainRamAddress(currentFirst, 1)
                || !bus.Memory.IsMainRamAddress(currentSecond, 1))
            {
                return false;
            }

            byte firstByte = bus.Memory.Read8(currentFirst);
            currentFirst++;
            lastMappedFirst = MapSonicByteTable(bus.Memory, tableAddress, firstByte);
            byte secondByte = bus.Memory.Read8(currentSecond);
            currentSecond++;
            lastMappedSecond = MapSonicByteTable(bus.Memory, tableAddress, secondByte);
            iterations++;

            if (lastMappedSecond != lastMappedFirst)
            {
                mismatch = true;
                break;
            }

            if (!bus.Memory.IsMainRamAddress(currentFirst, 1))
            {
                return false;
            }

            lastPeek = unchecked((uint)(sbyte)bus.Memory.Read8(currentFirst));
            if (lastPeek == 0)
            {
                terminal = true;
                break;
            }
        }

        if (iterations == 0
            || (!mismatch && !terminal)
            || !CanFastForwardInstructionCount(state, iterations, 32, extraInstructions: 0))
        {
            return false;
        }

        state.Gpr[0] = mismatch ? tableAddress : lastPeek;
        state.Gpr[3] = lastMappedSecond;
        state.Gpr[4] = tableAddress & 0xFFFF_0000u;
        state.Gpr[20] = currentFirst;
        state.Gpr[21] = currentSecond;
        state.Gpr[22] = lastMappedFirst;

        if (mismatch)
        {
            SetCr0ForSignedCompare(state, lastMappedSecond, lastMappedFirst);
            state.Pc = pc + 0x28;
        }
        else
        {
            SetCr0(state, lastPeek);
            state.Pc = pc + 0x3C;
        }

        uint skipped = checked(iterations * 32);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardSonicPathRecordScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicPathRecordScan(bus, pc, out uint byteTableAddress)
            || !bus.Memory.IsMainRamAddress(byteTableAddress, 0x100)
            || state.Gpr[30] > 1)
        {
            return false;
        }

        GameCubeMemory memory = bus.Memory;
        uint smallData = state.Gpr[13];
        if (!TryReadMainRam32(memory, unchecked(smallData - 30068), out uint entryTable)
            || !TryReadMainRam32(memory, unchecked(smallData - 30064), out uint nameTable))
        {
            return false;
        }

        uint parentOffset = state.Gpr[29];
        if (parentOffset > 0x00C0_0000 || parentOffset % 12 != 0)
        {
            return false;
        }

        uint parentEndAddress = unchecked(entryTable + parentOffset + 8);
        if (!memory.IsMainRamAddress(parentEndAddress, sizeof(uint)))
        {
            return false;
        }

        uint endIndex = memory.Read32(parentEndAddress);
        uint candidateIndex = state.Gpr[26];
        if (candidateIndex >= endIndex)
        {
            return false;
        }

        uint segmentStart = state.Gpr[23];
        uint segmentLength = state.Gpr[27];
        bool hasTrailingSlash = state.Gpr[30] == 1;
        uint skippedCandidates = 0;
        uint skippedInstructionEstimate = 0;
        uint lastCandidateOffset = state.Gpr[28];
        uint lastCandidateWord = state.Gpr[4];

        while (candidateIndex < endIndex && skippedCandidates < 4096)
        {
            if (!TryReadSonicPathLookupEntry(memory, entryTable, candidateIndex, out SonicPathLookupEntry candidateEntry))
            {
                if (skippedCandidates == 0)
                {
                    return false;
                }

                break;
            }

            uint candidateOffset = checked(candidateIndex * 12);
            bool shouldCompare = candidateEntry.HasChildren || !hasTrailingSlash;
            uint nextIndex;
            uint candidateCost;
            if (shouldCompare)
            {
                uint nameAddress = unchecked(nameTable + candidateEntry.NameOffset);
                if (!TrySonicPathSegmentMatches(memory, byteTableAddress, nameAddress, segmentStart, segmentLength, out bool segmentMatches, out bool nameWasPrefix, out uint comparedBytes))
                {
                    if (skippedCandidates == 0)
                    {
                        return false;
                    }

                    break;
                }

                if (segmentMatches)
                {
                    break;
                }

                nextIndex = nameWasPrefix && candidateEntry.HasChildren ? candidateEntry.EndIndex : candidateIndex + 1;
                candidateCost = checked(40u + comparedBytes * 32u);
            }
            else
            {
                nextIndex = candidateIndex + 1;
                candidateCost = 24;
            }

            if (nextIndex <= candidateIndex)
            {
                if (skippedCandidates == 0)
                {
                    return false;
                }

                break;
            }

            skippedCandidates++;
            skippedInstructionEstimate = checked(skippedInstructionEstimate + candidateCost);
            lastCandidateOffset = candidateOffset;
            lastCandidateWord = candidateEntry.Word0;
            candidateIndex = nextIndex;
        }

        if (skippedCandidates == 0
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skippedInstructionEstimate, extraInstructions: 0))
        {
            return false;
        }

        state.Gpr[0] = endIndex;
        state.Gpr[3] = entryTable;
        state.Gpr[4] = lastCandidateWord;
        state.Gpr[26] = candidateIndex;
        state.Gpr[28] = lastCandidateOffset;
        SetCr0ForUnsignedCompareImmediate(state, candidateIndex, endIndex);
        state.Pc = candidateIndex < endIndex ? pc : pc + 0xF4;
        AdvanceFastForwardedInstructions(state, bus, skippedInstructionEstimate);
        skippedInstructions = checked((int)skippedInstructionEstimate);
        return true;
    }

    private static bool TryFastForwardSonicPairedTransform2d(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint iterations = state.Ctr;
        if (!MatchesSonicPairedTransform2dLoop(bus, pc)
            || iterations == 0
            || iterations > 0x0001_0000
            || state.Gpr[7] != iterations)
        {
            return false;
        }

        uint skipped = checked(iterations * SonicPairedTransform2dInstructionsPerIteration + SonicPairedTransform2dExitInstructions);
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        try
        {
            (double Lane0, double Lane1) f0 = (state.Fpr[0], state.FprPair1[0]);
            (double Lane0, double Lane1) f1 = (state.Fpr[1], state.FprPair1[1]);
            (double Lane0, double Lane1) f2 = (state.Fpr[2], state.FprPair1[2]);
            (double Lane0, double Lane1) f3 = (state.Fpr[3], state.FprPair1[3]);
            (double Lane0, double Lane1) f4 = (state.Fpr[4], state.FprPair1[4]);
            (double Lane0, double Lane1) f5 = (state.Fpr[5], state.FprPair1[5]);
            (double Lane0, double Lane1) f6 = (state.Fpr[6], state.FprPair1[6]);
            (double Lane0, double Lane1) f7 = (state.Fpr[7], state.FprPair1[7]);
            (double Lane0, double Lane1) f8 = (state.Fpr[8], state.FprPair1[8]);
            (double Lane0, double Lane1) f9 = (state.Fpr[9], state.FprPair1[9]);
            (double Lane0, double Lane1) f10 = (state.Fpr[10], state.FprPair1[10]);
            (double Lane0, double Lane1) f11 = (state.Fpr[11], state.FprPair1[11]);
            (double Lane0, double Lane1) f12 = (state.Fpr[12], state.FprPair1[12]);
            (double Lane0, double Lane1) f13 = (state.Fpr[13], state.FprPair1[13]);
            uint outputCursor = state.Gpr[5];
            uint inputCursor = state.Gpr[6];
            uint nextWord = state.Gpr[8];
            uint swizzledWord = state.Gpr[10];

            for (uint iteration = 0; iteration < iterations; iteration++)
            {
                WriteSonicPairedTransform2dOutput(bus, ref outputCursor, f12, f13, swizzledWord);

                f10 = PairedMaddsScalar0(f0, f8, f6);
                f11 = PairedMaddsScalar0(f1, f8, f7);
                f10 = PairedMaddsScalar1(f2, f8, f10);
                f11 = PairedMaddsScalar1(f3, f8, f11);
                f12 = PairedMaddsScalar0(f4, f9, f10);
                f13 = PairedMaddsScalar0(f5, f9, f11);

                inputCursor = unchecked(inputCursor + 4);
                f8 = ReadPairedSingleFloatPair(bus, inputCursor);
                inputCursor = unchecked(inputCursor + 8);
                f9 = (ReadSingleFloat(bus, inputCursor), 1.0d);
                swizzledWord = RotateLeft8(nextWord);
                inputCursor = unchecked(inputCursor + 4);
                nextWord = bus.Read32(inputCursor);
            }

            WriteSonicPairedTransform2dOutput(bus, ref outputCursor, f12, f13, swizzledWord);

            state.Fpr[8] = f8.Lane0;
            state.FprPair1[8] = f8.Lane1;
            state.Fpr[9] = f9.Lane0;
            state.FprPair1[9] = f9.Lane1;
            state.Fpr[10] = f10.Lane0;
            state.FprPair1[10] = f10.Lane1;
            state.Fpr[11] = f11.Lane0;
            state.FprPair1[11] = f11.Lane1;
            state.Fpr[12] = f12.Lane0;
            state.FprPair1[12] = f12.Lane1;
            state.Fpr[13] = f13.Lane0;
            state.FprPair1[13] = f13.Lane1;
            state.Gpr[5] = outputCursor;
            state.Gpr[6] = inputCursor;
            state.Gpr[8] = nextWord;
            state.Gpr[10] = swizzledWord;
            state.Ctr = 0;
            state.Pc = state.Lr & 0xFFFF_FFFCu;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool TryFastForwardSonicPairedTransform4d(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint iterations = state.Ctr;
        if (!MatchesSonicPairedTransform4dLoop(bus, pc)
            || iterations == 0
            || iterations > 0x0001_0000
            || state.Gpr[7] != iterations)
        {
            return false;
        }

        uint skipped = checked(iterations * SonicPairedTransform4dInstructionsPerIteration + SonicPairedTransform4dExitInstructions);
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        try
        {
            (double Lane0, double Lane1) f0 = (state.Fpr[0], state.FprPair1[0]);
            (double Lane0, double Lane1) f1 = (state.Fpr[1], state.FprPair1[1]);
            (double Lane0, double Lane1) f2 = (state.Fpr[2], state.FprPair1[2]);
            (double Lane0, double Lane1) f3 = (state.Fpr[3], state.FprPair1[3]);
            (double Lane0, double Lane1) f4 = (state.Fpr[4], state.FprPair1[4]);
            (double Lane0, double Lane1) f5 = (state.Fpr[5], state.FprPair1[5]);
            (double Lane0, double Lane1) f6 = (state.Fpr[6], state.FprPair1[6]);
            (double Lane0, double Lane1) f7 = (state.Fpr[7], state.FprPair1[7]);
            (double Lane0, double Lane1) f8 = (state.Fpr[8], state.FprPair1[8]);
            (double Lane0, double Lane1) f9 = (state.Fpr[9], state.FprPair1[9]);
            (double Lane0, double Lane1) f10 = (state.Fpr[10], state.FprPair1[10]);
            (double Lane0, double Lane1) f11 = (state.Fpr[11], state.FprPair1[11]);
            (double Lane0, double Lane1) f12 = (state.Fpr[12], state.FprPair1[12]);
            (double Lane0, double Lane1) f13 = (state.Fpr[13], state.FprPair1[13]);
            (double Lane0, double Lane1) f14 = (state.Fpr[14], state.FprPair1[14]);
            (double Lane0, double Lane1) f15 = (state.Fpr[15], state.FprPair1[15]);
            (double Lane0, double Lane1) f16 = (state.Fpr[16], state.FprPair1[16]);
            (double Lane0, double Lane1) f17 = (state.Fpr[17], state.FprPair1[17]);
            (double Lane0, double Lane1) f18 = (state.Fpr[18], state.FprPair1[18]);
            uint outputCursor = state.Gpr[5];
            uint inputCursor = state.Gpr[6];

            for (uint iteration = 0; iteration < iterations; iteration++)
            {
                f11 = PairedMaddsScalar0(f0, f8, f6);
                WriteSonicPairedTransform4dOutput(bus, ref outputCursor, f15, f16, f17, f18);
                f12 = PairedMaddsScalar0(f1, f8, f7);
                f13 = PairedMulsScalar1(f0, f9);
                f14 = PairedMulsScalar1(f1, f9);
                f11 = PairedMaddsScalar1(f2, f8, f11);
                f12 = PairedMaddsScalar1(f3, f8, f12);
                inputCursor = unchecked(inputCursor + 8);
                f8 = ReadPairedSingleFloatPair(bus, inputCursor);
                f13 = PairedMaddsScalar0(f2, f10, f13);
                f14 = PairedMaddsScalar0(f3, f10, f14);
                f15 = PairedMaddsScalar0(f4, f9, f11);
                f16 = PairedMaddsScalar0(f5, f9, f12);
                inputCursor = unchecked(inputCursor + 8);
                f9 = ReadPairedSingleFloatPair(bus, inputCursor);
                f17 = PairedMaddsScalar1(f4, f10, f13);
                f18 = PairedMaddsScalar1(f5, f10, f14);
                inputCursor = unchecked(inputCursor + 8);
                f10 = ReadPairedSingleFloatPair(bus, inputCursor);
            }

            WriteSonicPairedTransform4dOutput(bus, ref outputCursor, f15, f16, f17, f18);

            uint stackPointer = state.Gpr[1];
            if (!bus.Memory.IsMainRamAddress(stackPointer + 8, 40))
            {
                return false;
            }

            state.Fpr[8] = f8.Lane0;
            state.FprPair1[8] = f8.Lane1;
            state.Fpr[9] = f9.Lane0;
            state.FprPair1[9] = f9.Lane1;
            state.Fpr[10] = f10.Lane0;
            state.FprPair1[10] = f10.Lane1;
            state.Fpr[11] = f11.Lane0;
            state.FprPair1[11] = f11.Lane1;
            state.Fpr[12] = f12.Lane0;
            state.FprPair1[12] = f12.Lane1;
            state.Fpr[13] = f13.Lane0;
            state.FprPair1[13] = f13.Lane1;
            state.Fpr[14] = ReadDouble(bus, stackPointer + 8);
            state.FprPair1[14] = state.Fpr[14];
            state.Fpr[15] = ReadDouble(bus, stackPointer + 16);
            state.FprPair1[15] = state.Fpr[15];
            state.Fpr[16] = ReadDouble(bus, stackPointer + 24);
            state.FprPair1[16] = state.Fpr[16];
            state.Fpr[17] = ReadDouble(bus, stackPointer + 32);
            state.FprPair1[17] = state.Fpr[17];
            state.Fpr[18] = ReadDouble(bus, stackPointer + 40);
            state.FprPair1[18] = state.Fpr[18];
            state.Gpr[1] = unchecked(stackPointer + 64);
            state.Gpr[5] = outputCursor;
            state.Gpr[6] = inputCursor;
            state.Ctr = 0;
            SetCr0ForSignedCompareImmediate(state, state.Gpr[7], 0);
            state.Pc = state.Lr & 0xFFFF_FFFCu;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool TryFastForwardSonicVectorBlendCopyLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint iterations = state.Ctr;
        if (!MatchesSonicVectorBlendCopyLoop(bus, pc)
            || iterations == 0
            || iterations > 0x0001_0000
            || state.Gpr[10] == 0
            || state.Gpr[11] == 0
            || state.Gpr[12] != 0
            || state.Gpr[25] != 0
            || state.Gpr[26] == 0
            || state.Gpr[27] != 0
            || !CanFastForwardInstructionCount(state, iterations, SonicVectorBlendCopyInstructionsPerIteration, extraInstructions: 0))
        {
            return false;
        }

        try
        {
            double blendScaleA = state.Fpr[31];
            double blendScaleB = ReadSingleFloat(bus, state.Gpr[1] + 0x38);
            uint inputCursor = state.Gpr[4];
            uint outputCursor = state.Gpr[7];
            uint blendCursorA = state.Gpr[31];
            uint blendCursorB = state.Gpr[29];
            double f0 = state.Fpr[0];
            double f1 = state.Fpr[1];
            double f2 = state.Fpr[2];

            for (uint iteration = 0; iteration < iterations; iteration++)
            {
                for (uint lane = 0; lane < 3; lane++)
                {
                    uint offset = lane * sizeof(uint);
                    double sourceA = ReadSingleFloat(bus, blendCursorA + offset);
                    f2 = (float)(sourceA * blendScaleA);
                    f1 = ReadSingleFloat(bus, blendCursorB + offset);
                    f0 = (float)(f1 * blendScaleB + f2);
                    WriteSingleFloat(bus, outputCursor + offset, f0);
                }

                blendCursorA = unchecked(blendCursorA + 12);
                blendCursorB = unchecked(blendCursorB + 12);
                outputCursor = unchecked(outputCursor + 12);
                inputCursor = unchecked(inputCursor + 12);

                for (uint lane = 0; lane < 3; lane++)
                {
                    uint offset = lane * sizeof(uint);
                    f0 = ReadSingleFloat(bus, inputCursor + offset);
                    WriteSingleFloat(bus, outputCursor + offset, f0);
                }

                outputCursor = unchecked(outputCursor + 12);
                inputCursor = unchecked(inputCursor + 12);
            }

            state.Fpr[0] = f0;
            state.FprPair1[0] = f0;
            state.Fpr[1] = f1;
            state.FprPair1[1] = f1;
            state.Fpr[2] = f2;
            state.FprPair1[2] = f2;
            state.Gpr[4] = inputCursor;
            state.Gpr[7] = outputCursor;
            state.Gpr[29] = blendCursorB;
            state.Gpr[31] = blendCursorA;
            state.Ctr = 0;
            SetCr0ForSignedCompareImmediate(state, state.Gpr[25], 0);
            state.Pc = SonicVectorBlendCopyLoopPc + 0x154;
            uint skipped = checked(iterations * SonicVectorBlendCopyInstructionsPerIteration);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool TryFastForwardSonicGeneratedRangeScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (pc == SonicGeneratedRangeScanLoopPc)
        {
            return TryFastForwardSonicGeneratedRangeScanLoop(
                state,
                bus,
                out skippedInstructions,
                limit: 2048,
                indexStride: 56,
                groupStride: 0,
                baseOffset: 0x0001_A800,
                loopPc: SonicGeneratedRangeScanLoopPc,
                loopExitPc: 0x80BC_C094,
                firstSkipInstructions: 25,
                secondSkipInstructions: 46,
                matcher: MatchesSonicGeneratedRangeScanLoop);
        }

        if (pc == SonicGeneratedTileRangeScanLoopPc)
        {
            return TryFastForwardSonicGeneratedRangeScanLoop(
                state,
                bus,
                out skippedInstructions,
                limit: 256,
                indexStride: 68,
                groupStride: 17408,
                baseOffset: 0x0002_6800,
                loopPc: SonicGeneratedTileRangeScanLoopPc,
                loopExitPc: 0x80BC_C228,
                firstSkipInstructions: 27,
                secondSkipInstructions: 50,
                matcher: MatchesSonicGeneratedTileRangeScanLoop);
        }

        return false;
    }

    private static bool TryFastForwardSonicGeneratedModelPointerScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGeneratedModelPointerScan(bus, state.Pc)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicGeneratedModelPointerScanInstructions, extraInstructions: 0))
        {
            return false;
        }

        uint inputPointer = state.Gpr[3];
        uint stackPointer = state.Gpr[1];
        uint newStackPointer = unchecked(stackPointer - 24);
        if (!bus.Memory.IsMainRamAddress(newStackPointer, 32))
        {
            return false;
        }

        uint tableContainer = bus.Read32(0x80BD_3668);
        if (!bus.Memory.IsMainRamAddress(tableContainer + 0x20, sizeof(uint)))
        {
            return false;
        }

        uint pointerTable = bus.Memory.Read32(tableContainer + 0x20);
        const int entries = 18;
        const int stride = 20;
        if (!bus.Memory.IsMainRamAddress(pointerTable, entries * stride))
        {
            return false;
        }

        for (int index = 0; index < entries; index++)
        {
            if (bus.Memory.Read32(pointerTable + (uint)(index * stride)) == inputPointer)
            {
                return false;
            }
        }

        uint oldLr = state.Lr;
        uint oldR30 = state.Gpr[30];
        uint oldR31 = state.Gpr[31];

        bus.Write32(stackPointer + 4, oldLr);
        bus.Write32(newStackPointer, stackPointer);
        bus.Write32(newStackPointer + 20, oldR31);
        bus.Write32(newStackPointer + 16, oldR30);

        state.Gpr[0] = oldLr;
        state.Gpr[1] = stackPointer;
        state.Gpr[3] = pointerTable;
        state.Gpr[30] = oldR30;
        state.Gpr[31] = oldR31;
        state.Lr = oldLr;
        state.Pc = oldLr & 0xFFFF_FFFCu;
        SetCr0ForSignedCompareImmediate(state, entries, entries);

        AdvanceFastForwardedInstructions(state, bus, SonicGeneratedModelPointerScanInstructions);
        skippedInstructions = checked((int)SonicGeneratedModelPointerScanInstructions);
        return true;
    }

    private static bool TryFastForwardSonicGeneratedRangeScanLoop(
        PowerPcState state,
        GameCubeBus bus,
        out int skippedInstructions,
        int limit,
        int indexStride,
        int groupStride,
        uint baseOffset,
        uint loopPc,
        uint loopExitPc,
        uint firstSkipInstructions,
        uint secondSkipInstructions,
        Func<GameCubeBus, uint, bool> matcher)
    {
        skippedInstructions = 0;
        if (!matcher(bus, state.Pc))
        {
            return false;
        }

        int index = unchecked((int)state.Gpr[31]);
        if (index < 0 || index >= limit)
        {
            return false;
        }

        try
        {
            uint tableBasePointerAddress = 0x80BD_4F58;
            uint tableBase = bus.Read32(tableBasePointerAddress);
            float lower = ReadSingleFloat(bus, 0x80BE_CA10);
            float upper = ReadSingleFloat(bus, 0x80BE_CA08);
            if (float.IsNaN(lower) || float.IsNaN(upper))
            {
                return false;
            }

            uint skipped = 0;
            while (index < limit)
            {
                uint offset = unchecked((uint)(state.Gpr[30] * (uint)groupStride + (uint)index * (uint)indexStride) + baseOffset);
                uint valueAddress = unchecked(tableBase + offset);
                int value = unchecked((int)bus.Read32(valueAddress));
                double converted = value;

                state.Gpr[3] = tableBasePointerAddress;
                if (converted < lower)
                {
                    if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: checked(skipped + firstSkipInstructions), extraInstructions: 0))
                    {
                        break;
                    }

                    SetSonicGeneratedRangeScanFirstSkipState(state, bus, value, converted, lower, loopPc == SonicGeneratedTileRangeScanLoopPc);
                    skipped = checked(skipped + firstSkipInstructions);
                }
                else if (converted >= upper)
                {
                    if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: checked(skipped + secondSkipInstructions), extraInstructions: 0))
                    {
                        break;
                    }

                    SetSonicGeneratedRangeScanSecondSkipState(state, bus, tableBase, value, converted, lower, index, loopPc == SonicGeneratedTileRangeScanLoopPc);
                    skipped = checked(skipped + secondSkipInstructions);
                }
                else
                {
                    break;
                }

                index++;
                state.Gpr[31] = unchecked((uint)index);
                SetCr0ForSignedCompareImmediate(state, state.Gpr[31], limit);
                state.Pc = index < limit ? loopPc : loopExitPc;
            }

            if (skipped == 0)
            {
                return false;
            }

            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static void SetSonicGeneratedRangeScanFirstSkipState(PowerPcState state, GameCubeBus bus, int value, double converted, float threshold, bool tileLoop)
    {
        state.Gpr[3] = 0x80BE_CA10;
        state.Gpr[4] = tileLoop ? 0x4330_0000 : 0x80BF_0000;
        state.Gpr[5] = tileLoop ? 0x80BD_3120 : state.Gpr[31] * 56;
        state.Fpr[0] = threshold;
        state.FprPair1[0] = state.Fpr[0];
        state.Fpr[1] = converted;
        state.FprPair1[1] = converted;
        bus.Write32(state.Gpr[1] + 0xC8, 0x4330_0000);
        bus.Write32(state.Gpr[1] + 0xCC, unchecked((uint)(value ^ int.MinValue)));
    }

    private static void SetSonicGeneratedRangeScanSecondSkipState(PowerPcState state, GameCubeBus bus, uint tableBase, int value, double converted, float lower, int index, bool tileLoop)
    {
        state.Gpr[3] = 0x80BE_CA08;
        state.Gpr[4] = tileLoop ? unchecked((uint)(value ^ int.MinValue)) : 0x80BF_0000;
        state.Gpr[5] = tileLoop ? tableBase : unchecked((uint)index * 56);
        state.Fpr[0] = lower;
        state.FprPair1[0] = state.Fpr[0];
        state.Fpr[1] = converted;
        state.FprPair1[1] = converted;
        bus.Write32(state.Gpr[1] + 0xC8, 0x4330_0000);
        bus.Write32(state.Gpr[1] + 0xCC, unchecked((uint)(value ^ int.MinValue)));
    }

    private static bool TryFastForwardSonicResourceModeQuery(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (bus.Read32(pc) != 0x7C08_02A6
            || bus.Read32(pc + 0x04) != 0x9001_0004
            || bus.Read32(pc + 0x08) != 0x9421_FFE8
            || bus.Read32(pc + 0x0C) != 0x93E1_0014
            || bus.Read32(pc + 0x10) != 0x93C1_0010
            || bus.Read32(pc + 0x14) != 0x4BFF_64F1
            || bus.Read32(pc + 0x18) != 0x800D_8AC0
            || bus.Read32(pc + 0x20) != 0x2C00_0000
            || bus.Read32(pc + 0x24) != 0x4182_000C
            || bus.Read32(pc + 0x30) != 0x800D_8AB8
            || bus.Read32(pc + 0x38) != 0x4182_000C
            || bus.Read32(pc + 0x44) != 0x83ED_8AA8
            || bus.Read32(pc + 0x48) != 0x281F_0000
            || bus.Read32(pc + 0x4C) != 0x4082_000C
            || bus.Read32(pc + 0x70) != 0x4BFF_6495
            || bus.Read32(pc + 0x84) != 0x4BFF_64A9
            || bus.Read32(pc + 0x88) != 0x7FC3_F378
            || bus.Read32(pc + 0x8C) != 0x4BFF_64A1
            || bus.Read32(pc + 0x90) != 0x8001_001C
            || bus.Read32(pc + 0x94) != 0x7FE3_FB78
            || bus.Read32(pc + 0x98) != 0x83E1_0014
            || bus.Read32(pc + 0x9C) != 0x83C1_0010
            || bus.Read32(pc + 0xA0) != 0x7C08_03A6
            || bus.Read32(pc + 0xA4) != 0x3821_0018
            || bus.Read32(pc + 0xA8) != 0x4E80_0020)
        {
            return false;
        }

        uint globalBase = state.Gpr[13];
        uint firstFlagAddress = unchecked(globalBase - 30016u);
        uint secondFlagAddress = unchecked(globalBase - 30024u);
        uint modePointerAddress = unchecked(globalBase - 30040u);
        if (!bus.Memory.IsMainRamAddress(firstFlagAddress, sizeof(uint))
            || !bus.Memory.IsMainRamAddress(secondFlagAddress, sizeof(uint))
            || !bus.Memory.IsMainRamAddress(modePointerAddress, sizeof(uint)))
        {
            return false;
        }

        uint result;
        uint skipped;
        if (bus.Memory.Read32(firstFlagAddress) != 0)
        {
            result = 0xFFFF_FFFF;
            skipped = 27;
        }
        else if (bus.Memory.Read32(secondFlagAddress) != 0)
        {
            result = 8;
            skipped = 31;
        }
        else
        {
            uint modePointer = bus.Memory.Read32(modePointerAddress);
            if (modePointer == 0 || modePointer == 0x802B_B700)
            {
                result = 0;
                skipped = 41;
            }
            else
            {
                if (!bus.Memory.IsMainRamAddress(modePointer + 0x0C, sizeof(uint)))
                {
                    return false;
                }

                uint rawMode = bus.Memory.Read32(modePointer + 0x0C);
                result = rawMode == 3 ? 1u : rawMode;
                skipped = 55;
            }
        }

        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        uint oldMsr = state.Msr;
        uint oldInterruptEnable = (oldMsr >> 15) & 1;
        state.Gpr[0] = state.Lr;
        state.Gpr[3] = result;
        state.Gpr[4] = 0;
        state.Gpr[5] = oldMsr;
        state.Msr = oldMsr;
        SetCr0ForSignedCompareImmediate(state, oldInterruptEnable, 0);
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = (int)skipped;
        return true;
    }

    private static bool TryFastForwardSonicResourceStatePoll(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (bus.Read32(pc) != 0x3C80_801D
            || bus.Read32(pc + 0x04) != 0x3884_C168
            || bus.Read32(pc + 0x08) != 0x38C4_0047
            || bus.Read32(pc + 0x0C) != 0x88A4_0047
            || bus.Read32(pc + 0x10) != 0x7CA4_0774
            || bus.Read32(pc + 0x14) != 0x2C04_0014
            || bus.Read32(pc + 0x18) != 0x4182_0018
            || bus.Read32(pc + 0x1C) != 0x3805_FFEB
            || bus.Read32(pc + 0x20) != 0x5400_063E
            || bus.Read32(pc + 0x24) != 0x2800_0002
            || bus.Read32(pc + 0x28) != 0x4081_0008
            || bus.Read32(pc + 0x2C) != 0x908D_897C
            || bus.Read32(pc + 0x30) != 0x3803_0001
            || bus.Read32(pc + 0x34) != 0x2800_000C
            || bus.Read32(pc + 0x38) != 0x4181_0054
            || bus.Read32(pc + 0x3C) != 0x3C60_801D
            || bus.Read32(pc + 0x40) != 0x3863_DC70
            || bus.Read32(pc + 0x44) != 0x5400_103A
            || bus.Read32(pc + 0x48) != 0x7C03_002E
            || bus.Read32(pc + 0x4C) != 0x7C09_03A6
            || bus.Read32(pc + 0x50) != 0x4E80_0420
            || bus.Read32(pc + 0x8C) != 0x8806_0000
            || bus.Read32(pc + 0x90) != 0x7C00_0774
            || bus.Read32(pc + 0x94) != 0x2C00_0014
            || bus.Read32(pc + 0x98) != 0x4D80_0020)
        {
            return false;
        }

        const uint stateBase = 0x801C_C168;
        const uint jumpTable = 0x801C_DC70;
        uint tableIndex = state.Gpr[3] + 1;
        uint tableEntryAddress = jumpTable + tableIndex * sizeof(uint);
        if (tableIndex > 12
            || !bus.Memory.IsMainRamAddress(stateBase + 0x47, 1)
            || !bus.Memory.IsMainRamAddress(tableEntryAddress, sizeof(uint)))
        {
            return false;
        }

        byte rawState = bus.Memory.Read8(stateBase + 0x47);
        int signedState = unchecked((sbyte)rawState);
        if (signedState >= 20 || bus.Memory.Read32(tableEntryAddress) != pc + 0x8C)
        {
            return false;
        }

        uint smallDataStateAddress = unchecked(state.Gpr[13] - 30340u);
        if (!bus.Memory.IsMainRamAddress(smallDataStateAddress, sizeof(uint))
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 25, extraInstructions: 0))
        {
            return false;
        }

        uint stateValue = unchecked((uint)signedState);
        bus.Memory.Write32(smallDataStateAddress, stateValue);
        state.Gpr[0] = stateValue;
        state.Gpr[3] = jumpTable;
        state.Gpr[4] = stateValue;
        state.Gpr[5] = rawState;
        state.Gpr[6] = stateBase + 0x47;
        state.Ctr = pc + 0x8C;
        SetCr0ForSignedCompareImmediate(state, stateValue, 20);
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 25);
        skippedInstructions = 25;
        return true;
    }

    private static bool TryFastForwardSonicResourceFixupRecord(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicResourceFixupLoop(bus, state.Pc))
        {
            return false;
        }

        for (int records = 0; records < 100_000 && state.Pc == 0x800E_81A8; records++)
        {
            if (!TryFastForwardSonicResourceFixupRecordOnce(state, bus, out int recordInstructions, out bool terminalRecord))
            {
                return skippedInstructions != 0;
            }

            skippedInstructions += recordInstructions;
            if (terminalRecord)
            {
                return true;
            }
        }

        return skippedInstructions != 0;
    }

    private static bool TryFastForwardSonicResourceFixupRecordOnce(PowerPcState state, GameCubeBus bus, out int skippedInstructions, out bool terminalRecord)
    {
        skippedInstructions = 0;
        terminalRecord = false;
        if (!MatchesSonicResourceFixupLoop(bus, state.Pc))
        {
            return false;
        }

        GameCubeMemory memory = bus.Memory;
        uint record = state.Gpr[30];
        if (!memory.IsMainRamAddress(record, 8))
        {
            return false;
        }

        ushort offset = memory.Read16(record);
        byte opcode = memory.Read8(record + 2);
        byte tableIndex = memory.Read8(record + 3);
        uint addend = memory.Read32(record + 4);
        uint destination = unchecked(state.Gpr[28] + offset);
        uint baseValue = 0;
        if (state.Gpr[31] != 0)
        {
            uint tableBaseAddress = unchecked(state.Gpr[26] + 0x10);
            if (!memory.IsMainRamAddress(tableBaseAddress, sizeof(uint)))
            {
                return false;
            }

            uint tableBase = memory.Read32(tableBaseAddress);
            uint entryAddress = unchecked(tableBase + (uint)tableIndex * 8);
            if (!memory.IsMainRamAddress(entryAddress, sizeof(uint)))
            {
                return false;
            }

            baseValue = memory.Read32(entryAddress) & 0xFFFF_FFFEu;
        }

        int estimatedInstructions = EstimateSonicResourceFixupInstructions(opcode, state.Gpr[31] != 0, state.Gpr[29] != 0);
        if (estimatedInstructions == 0 || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: (uint)estimatedInstructions, extraInstructions: 0))
        {
            return false;
        }

        state.Gpr[5] = baseValue;
        state.Gpr[28] = destination;
        switch (opcode)
        {
            case 0x00:
            case 0xC9:
                break;
            case 0x01:
                if (!memory.IsMainRamAddress(destination, sizeof(uint)))
                {
                    return false;
                }

                state.Gpr[0] = unchecked(baseValue + addend);
                memory.Write32(destination, state.Gpr[0]);
                break;
            case 0x02:
                if (!memory.IsMainRamAddress(destination, sizeof(uint)))
                {
                    return false;
                }

                state.Gpr[0] = unchecked(baseValue + addend);
                state.Gpr[3] = memory.Read32(destination) & 0xFC00_0003u;
                memory.Write32(destination, state.Gpr[3] | (state.Gpr[0] & 0x03FF_FFFCu));
                break;
            case 0x03:
            case 0x04:
                if (!memory.IsMainRamAddress(destination, sizeof(ushort)))
                {
                    return false;
                }

                state.Gpr[0] = unchecked(baseValue + addend);
                memory.Write16(destination, (ushort)state.Gpr[0]);
                break;
            case 0x05:
                if (!memory.IsMainRamAddress(destination, sizeof(ushort)))
                {
                    return false;
                }

                state.Gpr[0] = unchecked(baseValue + addend);
                memory.Write16(destination, (ushort)(state.Gpr[0] >> 16));
                break;
            case 0x06:
                if (!memory.IsMainRamAddress(destination, sizeof(ushort)))
                {
                    return false;
                }

                state.Gpr[4] = unchecked(baseValue + addend);
                uint high = state.Gpr[4] >> 16;
                if ((state.Gpr[4] & 0x8000) != 0)
                {
                    high++;
                }

                state.Gpr[0] = high;
                memory.Write16(destination, (ushort)high);
                break;
            case 0x0A:
                if (!memory.IsMainRamAddress(destination, sizeof(uint)))
                {
                    return false;
                }

                state.Gpr[0] = unchecked(baseValue + addend - destination);
                state.Gpr[3] = memory.Read32(destination) & 0xFC00_0003u;
                memory.Write32(destination, state.Gpr[3] | (state.Gpr[0] & 0x03FF_FFFCu));
                break;
            case 0xCA:
            case 0xCB:
                uint resourceTableAddress = unchecked(state.Gpr[27] + 0x10);
                if (!memory.IsMainRamAddress(resourceTableAddress, sizeof(uint)))
                {
                    return false;
                }

                uint resourceTable = memory.Read32(resourceTableAddress);
                uint resourceEntry = unchecked(resourceTable + (uint)tableIndex * 8);
                if (!memory.IsMainRamAddress(resourceEntry, sizeof(uint)))
                {
                    return false;
                }

                state.Gpr[23] = resourceEntry;
                state.Gpr[0] = memory.Read32(resourceEntry);
                state.Gpr[28] = state.Gpr[0] & 0xFFFF_FFFEu;
                state.Gpr[29] = (state.Gpr[0] & 1) != 0 ? resourceEntry : 0;
                break;
            default:
                return false;
        }

        state.Gpr[4] = opcode;
        state.Gpr[30] = unchecked(record + 8);
        SetCr0ForUnsignedCompareImmediate(state, opcode, 0xCB);
        state.Pc = opcode == 0xCB ? 0x800E_835C : 0x800E_81A8;
        terminalRecord = opcode == 0xCB;
        skippedInstructions = estimatedInstructions;
        AdvanceFastForwardedInstructions(state, bus, (uint)skippedInstructions);
        return true;
    }

    private static bool MatchesSonicResourceFixupLoop(GameCubeBus bus, uint pc) =>
        pc == 0x800E_81A8
        && bus.Read32(0x800E_81A8) == 0xA01E_0000
        && bus.Read32(0x800E_81AC) == 0x281F_0000
        && bus.Read32(0x800E_81B0) == 0x7F9C_0214
        && bus.Read32(0x800E_8350) == 0x889E_0002
        && bus.Read32(0x800E_8354) == 0x2804_00CB
        && bus.Read32(0x800E_8358) == 0x4082_FE50;

    private static int EstimateSonicResourceFixupInstructions(byte opcode, bool hasBaseTable, bool hasPreviousResource)
    {
        int prefix = hasBaseTable ? 10 : 5;
        return opcode switch
        {
            0x00 => prefix + 11,
            0x01 => prefix + 17,
            0x02 => prefix + 16,
            0x03 => prefix + 18,
            0x04 => prefix + 16,
            0x05 => prefix + 18,
            0x06 => prefix + 17,
            0x0A => prefix + 20,
            0xC9 => prefix + 9,
            0xCA or 0xCB => prefix + 31 + (hasPreviousResource ? 28 : 0),
            _ => 0,
        };
    }

    private static bool TryFastForwardSonicOverlayInactiveSlotScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        const uint pc = 0x80BC_8110;
        const uint slotTable = 0x80BE_D490;
        const uint slotStride = 0x10;
        const uint maxSlots = 64;
        const uint instructionsPerSlot = 10;

        if (state.Pc != pc || !MatchesSonicOverlayInactiveSlotScan(bus))
        {
            return false;
        }

        uint slot = state.Gpr[31];
        if (slot >= maxSlots)
        {
            return false;
        }

        uint slotsToConsider = maxSlots - slot;
        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) == 0)
        {
            uint slotsBeforeInterruptEdge = decrementer / instructionsPerSlot;
            if (slotsBeforeInterruptEdge == 0)
            {
                return false;
            }

            slotsToConsider = Math.Min(slotsToConsider, slotsBeforeInterruptEdge);
        }

        uint inactiveSlots = 0;
        uint lastOffset = 0;
        uint lastValue = 0;
        while (inactiveSlots < slotsToConsider)
        {
            uint currentSlot = slot + inactiveSlots;
            uint offset = checked(currentSlot * slotStride);
            uint entryAddress = slotTable + offset;
            if (!bus.Memory.IsMainRamAddress(entryAddress, sizeof(uint)))
            {
                return false;
            }

            uint value = bus.Memory.Read32(entryAddress);
            if (value == 1)
            {
                break;
            }

            lastOffset = offset;
            lastValue = value;
            inactiveSlots++;
        }

        if (inactiveSlots == 0)
        {
            return false;
        }

        uint nextSlot = slot + inactiveSlots;
        state.Gpr[0] = lastValue;
        state.Gpr[3] = slotTable + lastOffset;
        state.Gpr[4] = lastOffset;
        state.Gpr[31] = nextSlot;
        SetCr0ForSignedCompareImmediate(state, nextSlot, 64);
        state.Pc = nextSlot < maxSlots ? pc : 0x80BC_82B8;

        uint skipped = checked(inactiveSlots * instructionsPerSlot);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicOverlayInactiveSlotScan(GameCubeBus bus) =>
        bus.Read32(0x80BC_8110) == 0x57E4_2036
        && bus.Read32(0x80BC_8114) == 0x3C60_80BF
        && bus.Read32(0x80BC_8118) == 0x3803_D490
        && bus.Read32(0x80BC_811C) == 0x7C60_2214
        && bus.Read32(0x80BC_8120) == 0x8003_0000
        && bus.Read32(0x80BC_8124) == 0x2C00_0001
        && bus.Read32(0x80BC_8128) == 0x4082_0184
        && bus.Read32(0x80BC_82AC) == 0x3BFF_0001
        && bus.Read32(0x80BC_82B0) == 0x2C1F_0040
        && bus.Read32(0x80BC_82B4) == 0x4180_FE5C;

    private static bool TryFastForwardSonicModeWrapper(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (bus.Read32(pc) != 0x7C08_02A6
            || bus.Read32(pc + 0x04) != 0x9001_0004
            || bus.Read32(pc + 0x08) != 0x9421_FFE8
            || bus.Read32(pc + 0x0C) != 0x93E1_0014
            || bus.Read32(pc + 0x10) != 0x7C7F_1B78
            || bus.Read32(pc + 0x14) != 0x4BFF_653D
            || bus.Read32(pc + 0x18) != 0x801F_000C
            || bus.Read32(pc + 0x1C) != 0x2C00_0003
            || bus.Read32(pc + 0x20) != 0x4082_000C
            || bus.Read32(pc + 0x24) != 0x3BE0_0001
            || bus.Read32(pc + 0x28) != 0x4800_0008
            || bus.Read32(pc + 0x2C) != 0x7C1F_0378
            || bus.Read32(pc + 0x30) != 0x4BFF_6549
            || bus.Read32(pc + 0x34) != 0x8001_001C
            || bus.Read32(pc + 0x38) != 0x7FE3_FB78
            || bus.Read32(pc + 0x3C) != 0x83E1_0014
            || bus.Read32(pc + 0x40) != 0x3821_0018
            || bus.Read32(pc + 0x44) != 0x7C08_03A6
            || bus.Read32(pc + 0x48) != 0x4E80_0020)
        {
            return false;
        }

        uint objectAddress = state.Gpr[3];
        uint stackPointer = state.Gpr[1];
        if (!bus.Memory.IsMainRamAddress(objectAddress + 0x0C, sizeof(uint))
            || !bus.Memory.IsMainRamAddress(stackPointer - 24, 32))
        {
            return false;
        }

        uint rawMode = bus.Memory.Read32(objectAddress + 0x0C);
        uint result = rawMode == 3 ? 1u : rawMode;
        uint skipped = rawMode == 3 ? 32u : 31u;
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        uint oldMsr = state.Msr;
        uint oldInterruptEnable = (oldMsr >> 15) & 1;
        uint oldLr = state.Lr;
        bus.Memory.Write32(stackPointer + 4, oldLr);
        bus.Memory.Write32(stackPointer - 4, state.Gpr[31]);

        state.Gpr[0] = oldLr;
        state.Gpr[3] = result;
        state.Gpr[4] = oldInterruptEnable;
        state.Gpr[5] = oldMsr;
        state.Msr = oldMsr;
        state.Lr = oldLr;
        state.Pc = oldLr;
        SetCr0ForSignedCompareImmediate(state, oldInterruptEnable, 0);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = (int)skipped;
        return true;
    }

    private static bool MatchesSonicNormalizedStringScan(GameCubeBus bus, uint pc, out uint tableAddress)
    {
        tableAddress = 0;
        if (bus.Read32(pc + 0x00) != 0x8814_0000
            || bus.Read32(pc + 0x04) != 0x3A94_0001
            || bus.Read32(pc + 0x08) != 0x7C03_0774
            || bus.Read32(pc + 0x0C) != 0x4801_CB35
            || bus.Read32(pc + 0x10) != 0x8815_0000
            || bus.Read32(pc + 0x14) != 0x3AC3_0000
            || bus.Read32(pc + 0x18) != 0x3AB5_0001
            || bus.Read32(pc + 0x1C) != 0x7C03_0774
            || bus.Read32(pc + 0x20) != 0x4801_CB21
            || bus.Read32(pc + 0x24) != 0x7C03_B000
            || bus.Read32(pc + 0x28) != 0x4182_000C
            || bus.Read32(pc + 0x2C) != 0x3800_0000
            || bus.Read32(pc + 0x30) != 0x4800_0030
            || bus.Read32(pc + 0x34) != 0x8814_0000
            || bus.Read32(pc + 0x38) != 0x7C00_0775
            || bus.Read32(pc + 0x3C) != 0x4082_FFC4)
        {
            return false;
        }

        uint byteTablePc = pc + 0x1CB40;
        return TryMatchSonicByteTableLookup(bus, byteTablePc, out tableAddress);
    }

    private static bool MatchesSonicPathRecordScan(GameCubeBus bus, uint pc, out uint tableAddress)
    {
        tableAddress = 0;
        if (pc != SonicPathLookupRecordScanPc
            || bus.Read32(pc + 0x00) != 0x1F9A_000C
            || bus.Read32(pc + 0x04) != 0x7C83_E02E
            || bus.Read32(pc + 0x08) != 0x5480_000F
            || bus.Read32(pc + 0x1C) != 0x2C00_0000
            || bus.Read32(pc + 0x20) != 0x4082_000C
            || bus.Read32(pc + 0x24) != 0x2C1E_0001
            || bus.Read32(pc + 0x28) != 0x4182_0080
            || bus.Read32(pc + 0x2C) != 0x806D_8A90
            || bus.Read32(pc + 0x30) != 0x5480_023E
            || bus.Read32(pc + 0x34) != 0x3AB7_0000
            || bus.Read32(pc + 0x38) != 0x7E83_0214
            || bus.Read32(pc + 0xA8) != 0x800D_8A8C
            || bus.Read32(pc + 0xAC) != 0x7C60_E214
            || bus.Read32(pc + 0xE0) != 0x806D_8A8C
            || bus.Read32(pc + 0xE4) != 0x3803_0008
            || bus.Read32(pc + 0xE8) != 0x7C1D_002E
            || bus.Read32(pc + 0xEC) != 0x7C1A_0040
            || bus.Read32(pc + 0xF0) != 0x4180_FF10
            || bus.Read32(pc + 0xF4) != 0x3860_FFFF)
        {
            return false;
        }

        return MatchesSonicNormalizedStringScan(bus, pc + 0x40, out tableAddress);
    }

    private static bool MatchesSonicPairedTransform2dLoop(GameCubeBus bus, uint pc) =>
        pc == SonicPairedTransform2dLoopPc
        && bus.Read32(pc - 0x78) == 0x80A3_0000
        && bus.Read32(pc - 0x74) == 0x80C3_0004
        && bus.Read32(pc - 0x70) == 0x80E3_0008
        && bus.Read32(pc - 0x6C) == 0x38E7_FFFF
        && bus.Read32(pc - 0x68) == 0x7CE9_03A6
        && bus.Read32(pc - 0x64) == 0xE004_0000
        && bus.Read32(pc - 0x60) == 0x38C6_FFFC
        && bus.Read32(pc - 0x5C) == 0xE024_8008
        && bus.Read32(pc - 0x58) == 0x38A5_FFF8
        && bus.Read32(pc - 0x54) == 0xE0C4_0024
        && bus.Read32(pc - 0x50) == 0xE0E4_802C
        && bus.Read32(pc - 0x4C) == 0xE506_0004
        && bus.Read32(pc - 0x48) == 0xE526_8008
        && bus.Read32(pc - 0x44) == 0x8506_0004
        && bus.Read32(pc - 0x40) == 0x1140_321C
        && bus.Read32(pc - 0x3C) == 0xE044_000C
        && bus.Read32(pc - 0x38) == 0x1161_3A1C
        && bus.Read32(pc - 0x34) == 0xE064_8014
        && bus.Read32(pc - 0x30) == 0x550A_403E
        && bus.Read32(pc - 0x2C) == 0xE0A4_8020
        && bus.Read32(pc - 0x28) == 0xE084_0018
        && bus.Read32(pc - 0x24) == 0x1142_521E
        && bus.Read32(pc - 0x20) == 0x1163_5A1E
        && bus.Read32(pc - 0x1C) == 0x1184_525C
        && bus.Read32(pc - 0x18) == 0x11A5_5A5C
        && bus.Read32(pc - 0x14) == 0xE506_0004
        && bus.Read32(pc - 0x10) == 0xE526_8008
        && bus.Read32(pc - 0x0C) == 0x8506_0004
        && bus.Read32(pc - 0x08) == 0x2C07_0000
        && bus.Read32(pc - 0x04) == 0x4182_003C
        && bus.Read32(pc + 0x00) == 0x1140_321C
        && bus.Read32(pc + 0x04) == 0xF585_0008
        && bus.Read32(pc + 0x08) == 0x1161_3A1C
        && bus.Read32(pc + 0x0C) == 0xF5A5_8008
        && bus.Read32(pc + 0x10) == 0x1142_521E
        && bus.Read32(pc + 0x14) == 0x9545_0010
        && bus.Read32(pc + 0x18) == 0x1163_5A1E
        && bus.Read32(pc + 0x1C) == 0xE506_0004
        && bus.Read32(pc + 0x20) == 0x1184_525C
        && bus.Read32(pc + 0x24) == 0x11A5_5A5C
        && bus.Read32(pc + 0x28) == 0xE526_8008
        && bus.Read32(pc + 0x2C) == 0x550A_403E
        && bus.Read32(pc + 0x30) == 0x8506_0004
        && bus.Read32(pc + 0x34) == 0x4200_FFCC
        && bus.Read32(pc + 0x38) == 0xF585_0008
        && bus.Read32(pc + 0x3C) == 0xF5A5_8008
        && bus.Read32(pc + 0x40) == 0x9545_0010
        && bus.Read32(pc + 0x44) == 0x4E80_0020;

    private static bool MatchesSonicPairedTransform4dLoop(GameCubeBus bus, uint pc) =>
        pc == SonicPairedTransform4dLoopPc
        && bus.Read32(pc - 0x78) == 0xE004_0000
        && bus.Read32(pc - 0x74) == 0x38C6_FFF8
        && bus.Read32(pc - 0x70) == 0xE024_8008
        && bus.Read32(pc - 0x6C) == 0x38A5_FFF4
        && bus.Read32(pc - 0x68) == 0xE0C4_0024
        && bus.Read32(pc - 0x64) == 0xE506_0008
        && bus.Read32(pc - 0x60) == 0xE0E4_802C
        && bus.Read32(pc - 0x5C) == 0xE526_0008
        && bus.Read32(pc - 0x58) == 0x1160_321C
        && bus.Read32(pc - 0x54) == 0xE044_000C
        && bus.Read32(pc - 0x50) == 0x1181_3A1C
        && bus.Read32(pc - 0x4C) == 0xE064_8014
        && bus.Read32(pc - 0x48) == 0x11A0_025A
        && bus.Read32(pc - 0x44) == 0xE546_0008
        && bus.Read32(pc - 0x40) == 0x11C1_025A
        && bus.Read32(pc - 0x3C) == 0xE0A4_8020
        && bus.Read32(pc - 0x38) == 0x1162_5A1E
        && bus.Read32(pc - 0x34) == 0x1183_621E
        && bus.Read32(pc - 0x30) == 0xE084_0018
        && bus.Read32(pc - 0x2C) == 0x11A2_6A9C
        && bus.Read32(pc - 0x28) == 0xE506_0008
        && bus.Read32(pc - 0x24) == 0x11C3_729C
        && bus.Read32(pc - 0x20) == 0x11E4_5A5C
        && bus.Read32(pc - 0x1C) == 0x1205_625C
        && bus.Read32(pc - 0x18) == 0xE526_0008
        && bus.Read32(pc - 0x14) == 0x1224_6A9E
        && bus.Read32(pc - 0x10) == 0x1245_729E
        && bus.Read32(pc - 0x0C) == 0xE546_0008
        && bus.Read32(pc - 0x08) == 0x2C07_0000
        && bus.Read32(pc - 0x04) == 0x4182_0054
        && bus.Read32(pc + 0x00) == 0x1160_321C
        && bus.Read32(pc + 0x04) == 0xF5E5_000C
        && bus.Read32(pc + 0x08) == 0x1181_3A1C
        && bus.Read32(pc + 0x0C) == 0xF605_8008
        && bus.Read32(pc + 0x10) == 0x11A0_025A
        && bus.Read32(pc + 0x14) == 0xF625_0004
        && bus.Read32(pc + 0x18) == 0x11C1_025A
        && bus.Read32(pc + 0x1C) == 0xF645_8008
        && bus.Read32(pc + 0x20) == 0x1162_5A1E
        && bus.Read32(pc + 0x24) == 0x1183_621E
        && bus.Read32(pc + 0x28) == 0xE506_0008
        && bus.Read32(pc + 0x2C) == 0x11A2_6A9C
        && bus.Read32(pc + 0x30) == 0x11C3_729C
        && bus.Read32(pc + 0x34) == 0x11E4_5A5C
        && bus.Read32(pc + 0x38) == 0x1205_625C
        && bus.Read32(pc + 0x3C) == 0xE526_0008
        && bus.Read32(pc + 0x40) == 0x1224_6A9E
        && bus.Read32(pc + 0x44) == 0x1245_729E
        && bus.Read32(pc + 0x48) == 0xE546_0008
        && bus.Read32(pc + 0x4C) == 0x4200_FFB4
        && bus.Read32(pc + 0x50) == 0xF5E5_000C
        && bus.Read32(pc + 0x54) == 0xF605_8008
        && bus.Read32(pc + 0x58) == 0xF625_0004
        && bus.Read32(pc + 0x5C) == 0xF645_8008
        && bus.Read32(pc + 0x60) == 0xC9C1_0008
        && bus.Read32(pc + 0x64) == 0xC9E1_0010
        && bus.Read32(pc + 0x68) == 0xCA01_0018
        && bus.Read32(pc + 0x6C) == 0xCA21_0020
        && bus.Read32(pc + 0x70) == 0xCA41_0028
        && bus.Read32(pc + 0x74) == 0x3821_0040
        && bus.Read32(pc + 0x78) == 0x4E80_0020;

    private static bool MatchesSonicVectorBlendCopyLoop(GameCubeBus bus, uint pc) =>
        pc == SonicVectorBlendCopyLoopPc
        && bus.Read32(pc - 0x10) == 0x7D29_03A6
        && bus.Read32(pc - 0x0C) == 0x2C09_0000
        && bus.Read32(pc - 0x08) == 0x4081_015C
        && bus.Read32(pc + 0x00) == 0x2C0A_0000
        && bus.Read32(pc + 0x04) == 0x4182_008C
        && bus.Read32(pc + 0x08) == 0x281A_0000
        && bus.Read32(pc + 0x0C) == 0x4182_0064
        && bus.Read32(pc + 0x10) == 0xC01F_0000
        && bus.Read32(pc + 0x14) == 0xEC40_07F2
        && bus.Read32(pc + 0x18) == 0xC03D_0000
        && bus.Read32(pc + 0x1C) == 0xC001_0038
        && bus.Read32(pc + 0x20) == 0xEC01_0032
        && bus.Read32(pc + 0x24) == 0xEC02_002A
        && bus.Read32(pc + 0x28) == 0xD007_0000
        && bus.Read32(pc + 0x2C) == 0xC01F_0004
        && bus.Read32(pc + 0x30) == 0xEC40_07F2
        && bus.Read32(pc + 0x34) == 0xC03D_0004
        && bus.Read32(pc + 0x38) == 0xC001_0038
        && bus.Read32(pc + 0x3C) == 0xEC01_0032
        && bus.Read32(pc + 0x40) == 0xEC02_002A
        && bus.Read32(pc + 0x44) == 0xD007_0004
        && bus.Read32(pc + 0x48) == 0xC01F_0008
        && bus.Read32(pc + 0x4C) == 0xEC40_07F2
        && bus.Read32(pc + 0x50) == 0xC03D_0008
        && bus.Read32(pc + 0x54) == 0xC001_0038
        && bus.Read32(pc + 0x58) == 0xEC01_0032
        && bus.Read32(pc + 0x5C) == 0xEC02_002A
        && bus.Read32(pc + 0x60) == 0xD007_0008
        && bus.Read32(pc + 0x64) == 0x3BFF_000C
        && bus.Read32(pc + 0x68) == 0x3BBD_000C
        && bus.Read32(pc + 0x6C) == 0x4800_001C
        && bus.Read32(pc + 0x88) == 0x38E7_000C
        && bus.Read32(pc + 0x8C) == 0x3884_000C
        && bus.Read32(pc + 0x90) == 0x2C0B_0000
        && bus.Read32(pc + 0x94) == 0x4182_008C
        && bus.Read32(pc + 0x98) == 0x281B_0000
        && bus.Read32(pc + 0x9C) == 0x4182_0064
        && bus.Read32(pc + 0x100) == 0xC004_0000
        && bus.Read32(pc + 0x104) == 0xD007_0000
        && bus.Read32(pc + 0x108) == 0xC004_0004
        && bus.Read32(pc + 0x10C) == 0xD007_0004
        && bus.Read32(pc + 0x110) == 0xC004_0008
        && bus.Read32(pc + 0x114) == 0xD007_0008
        && bus.Read32(pc + 0x118) == 0x38E7_000C
        && bus.Read32(pc + 0x11C) == 0x3884_000C
        && bus.Read32(pc + 0x120) == 0x2C0C_0000
        && bus.Read32(pc + 0x124) == 0x4182_0014
        && bus.Read32(pc + 0x138) == 0x2C19_0000
        && bus.Read32(pc + 0x13C) == 0x4182_0014
        && bus.Read32(pc + 0x150) == 0x4200_FEB0;

    private static bool MatchesSonicGeneratedRangeScanLoop(GameCubeBus bus, uint pc) =>
        pc == SonicGeneratedRangeScanLoopPc
        && bus.Read32(pc + 0x00) == 0x3C80_80BD
        && bus.Read32(pc + 0x04) == 0x3884_4F58
        && bus.Read32(pc + 0x08) == 0x8084_0000
        && bus.Read32(pc + 0x0C) == 0x1C7F_0038
        && bus.Read32(pc + 0x10) == 0x3CA3_0001
        && bus.Read32(pc + 0x14) == 0x38A5_A800
        && bus.Read32(pc + 0x18) == 0x7C84_282E
        && bus.Read32(pc + 0x1C) == 0x3C60_80BD
        && bus.Read32(pc + 0x20) == 0x3863_3120
        && bus.Read32(pc + 0x24) == 0xC823_0000
        && bus.Read32(pc + 0x28) == 0x6C83_8000
        && bus.Read32(pc + 0x2C) == 0x9061_00CC
        && bus.Read32(pc + 0x30) == 0x3C60_4330
        && bus.Read32(pc + 0x34) == 0x9061_00C8
        && bus.Read32(pc + 0x38) == 0xC801_00C8
        && bus.Read32(pc + 0x3C) == 0xEC20_0828
        && bus.Read32(pc + 0x40) == 0x3C80_80BF
        && bus.Read32(pc + 0x44) == 0x3864_CA10
        && bus.Read32(pc + 0x48) == 0xC003_0000
        && bus.Read32(pc + 0x4C) == 0xFC01_0040
        && bus.Read32(pc + 0x50) == 0x4C40_1382
        && bus.Read32(pc + 0x54) == 0x4082_0078
        && bus.Read32(pc + 0xA4) == 0xFC01_0040
        && bus.Read32(pc + 0xA8) == 0x4081_0024
        && bus.Read32(pc + 0xC8) == 0x4BFF_8549
        && bus.Read32(pc + 0xCC) == 0x3BFF_0001
        && bus.Read32(pc + 0xD0) == 0x2C1F_0800
        && bus.Read32(pc + 0xD4) == 0x4180_FF2C;

    private static bool MatchesSonicGeneratedModelPointerScan(GameCubeBus bus, uint pc) =>
        pc == SonicGeneratedModelPointerScanPc
        && bus.Read32(pc + 0x00) == 0x7C08_02A6
        && bus.Read32(pc + 0x04) == 0x9001_0004
        && bus.Read32(pc + 0x08) == 0x9421_FFE8
        && bus.Read32(pc + 0x0C) == 0x93E1_0014
        && bus.Read32(pc + 0x10) == 0x93C1_0010
        && bus.Read32(pc + 0x14) == 0x7C7E_1B78
        && bus.Read32(pc + 0x18) == 0x3BE0_0000
        && bus.Read32(pc + 0x1C) == 0x4800_0034
        && bus.Read32(pc + 0x20) == 0x3C60_80BD
        && bus.Read32(pc + 0x24) == 0x3863_3668
        && bus.Read32(pc + 0x28) == 0x8063_0000
        && bus.Read32(pc + 0x2C) == 0x8063_0020
        && bus.Read32(pc + 0x30) == 0x1C1F_0014
        && bus.Read32(pc + 0x34) == 0x7C03_002E
        && bus.Read32(pc + 0x38) == 0x7C00_F040
        && bus.Read32(pc + 0x3C) == 0x4082_0010
        && bus.Read32(pc + 0x40) == 0x3C60_80BD
        && bus.Read32(pc + 0x44) == 0x3863_B5CC
        && bus.Read32(pc + 0x48) == 0x4B55_116D
        && bus.Read32(pc + 0x4C) == 0x3BFF_0001
        && bus.Read32(pc + 0x50) == 0x2C1F_0012
        && bus.Read32(pc + 0x54) == 0x4180_FFCC
        && bus.Read32(pc + 0x58) == 0x8001_001C
        && bus.Read32(pc + 0x5C) == 0x83E1_0014
        && bus.Read32(pc + 0x60) == 0x83C1_0010
        && bus.Read32(pc + 0x64) == 0x3821_0018
        && bus.Read32(pc + 0x68) == 0x7C08_03A6
        && bus.Read32(pc + 0x6C) == 0x4E80_0020;

    private static bool MatchesSonicGeneratedTileRangeScanLoop(GameCubeBus bus, uint pc) =>
        pc == SonicGeneratedTileRangeScanLoopPc
        && bus.Read32(pc + 0x00) == 0x3C60_80BD
        && bus.Read32(pc + 0x04) == 0x3883_4F58
        && bus.Read32(pc + 0x08) == 0x80A4_0000
        && bus.Read32(pc + 0x0C) == 0x1C7E_4400
        && bus.Read32(pc + 0x10) == 0x1C9F_0044
        && bus.Read32(pc + 0x14) == 0x7C63_2214
        && bus.Read32(pc + 0x18) == 0x3C63_0002
        && bus.Read32(pc + 0x1C) == 0x3863_6800
        && bus.Read32(pc + 0x20) == 0x7C65_182E
        && bus.Read32(pc + 0x24) == 0x3CA0_80BD
        && bus.Read32(pc + 0x28) == 0x38A5_3120
        && bus.Read32(pc + 0x2C) == 0xC825_0000
        && bus.Read32(pc + 0x30) == 0x6C63_8000
        && bus.Read32(pc + 0x34) == 0x9061_00CC
        && bus.Read32(pc + 0x38) == 0x3C80_4330
        && bus.Read32(pc + 0x3C) == 0x9081_00C8
        && bus.Read32(pc + 0x40) == 0xC801_00C8
        && bus.Read32(pc + 0x44) == 0xEC20_0828
        && bus.Read32(pc + 0x48) == 0x3C60_80BF
        && bus.Read32(pc + 0x4C) == 0x3863_CA10
        && bus.Read32(pc + 0x50) == 0xC003_0000
        && bus.Read32(pc + 0x54) == 0xFC01_0040
        && bus.Read32(pc + 0x58) == 0x4C40_1382
        && bus.Read32(pc + 0x5C) == 0x4082_011C
        && bus.Read32(pc + 0xB4) == 0xFC01_0040
        && bus.Read32(pc + 0xB8) == 0x4081_00C0
        && bus.Read32(pc + 0x178) == 0x3BFF_0001
        && bus.Read32(pc + 0x17C) == 0x2C1F_0100
        && bus.Read32(pc + 0x180) == 0x4180_FE80;

    private static (double Lane0, double Lane1) ReadPairedSingleFloatPair(GameCubeBus bus, uint address) =>
        (ReadSingleFloat(bus, address), ReadSingleFloat(bus, address + sizeof(uint)));

    private static float ReadSingleFloat(GameCubeBus bus, uint address) =>
        BitConverter.Int32BitsToSingle(unchecked((int)bus.Read32(address)));

    private static double ReadDouble(GameCubeBus bus, uint address)
    {
        ulong value = ((ulong)bus.Read32(address) << 32) | bus.Read32(address + sizeof(uint));
        return BitConverter.UInt64BitsToDouble(value);
    }

    private static void WriteSonicPairedTransform2dOutput(GameCubeBus bus, ref uint cursor, (double Lane0, double Lane1) first, (double Lane0, double Lane1) second, uint word)
    {
        cursor = unchecked(cursor + 8);
        WriteSingleFloat(bus, cursor, first.Lane0);
        WriteSingleFloat(bus, cursor + sizeof(uint), first.Lane1);
        cursor = unchecked(cursor + 8);
        WriteSingleFloat(bus, cursor, second.Lane0);
        cursor = unchecked(cursor + 16);
        bus.Write32(cursor, word);
    }

    private static void WriteSonicPairedTransform4dOutput(GameCubeBus bus, ref uint cursor, (double Lane0, double Lane1) first, (double Lane0, double Lane1) second, (double Lane0, double Lane1) third, (double Lane0, double Lane1) fourth)
    {
        cursor = unchecked(cursor + 12);
        WriteSingleFloat(bus, cursor, first.Lane0);
        WriteSingleFloat(bus, cursor + sizeof(uint), first.Lane1);
        cursor = unchecked(cursor + 8);
        WriteSingleFloat(bus, cursor, second.Lane0);
        cursor = unchecked(cursor + 4);
        WriteSingleFloat(bus, cursor, third.Lane0);
        WriteSingleFloat(bus, cursor + sizeof(uint), third.Lane1);
        cursor = unchecked(cursor + 8);
        WriteSingleFloat(bus, cursor, fourth.Lane0);
    }

    private static void WriteSingleFloat(GameCubeBus bus, uint address, double value) =>
        bus.Write32(address, unchecked((uint)BitConverter.SingleToInt32Bits((float)value)));

    private static (double Lane0, double Lane1) PairedMaddsScalar0((double Lane0, double Lane1) a, (double Lane0, double Lane1) c, (double Lane0, double Lane1) b)
    {
        float scalar = (float)c.Lane0;
        return ((float)a.Lane0 * scalar + (float)b.Lane0, (float)a.Lane1 * scalar + (float)b.Lane1);
    }

    private static (double Lane0, double Lane1) PairedMaddsScalar1((double Lane0, double Lane1) a, (double Lane0, double Lane1) c, (double Lane0, double Lane1) b)
    {
        float scalar = (float)c.Lane1;
        return ((float)a.Lane0 * scalar + (float)b.Lane0, (float)a.Lane1 * scalar + (float)b.Lane1);
    }

    private static (double Lane0, double Lane1) PairedMulsScalar1((double Lane0, double Lane1) a, (double Lane0, double Lane1) c)
    {
        float scalar = (float)c.Lane1;
        return ((float)a.Lane0 * scalar, (float)a.Lane1 * scalar);
    }

    private static uint RotateLeft8(uint value) =>
        (value << 8) | (value >> 24);

    private static uint MapSonicByteTable(GameCubeMemory memory, uint tableAddress, byte value)
    {
        uint input = unchecked((uint)(sbyte)value);
        return input == 0xFFFF_FFFF ? 0xFFFF_FFFF : memory.Read8(tableAddress + (input & 0xFF));
    }

    private static bool TryMatchSonicByteTableLookup(GameCubeBus bus, uint pc, out uint tableAddress)
    {
        tableAddress = 0;
        if (bus.Read32(pc + 0x00) != 0x2C03_FFFF
            || bus.Read32(pc + 0x04) != 0x4082_000C
            || bus.Read32(pc + 0x08) != 0x3860_FFFF
            || bus.Read32(pc + 0x0C) != 0x4E80_0020
            || bus.Read32(pc + 0x10) != 0x3C80_8017
            || bus.Read32(pc + 0x14) != 0x5463_063E
            || bus.Read32(pc + 0x20) != 0x8863_0000
            || bus.Read32(pc + 0x24) != 0x4E80_0020)
        {
            return false;
        }

        uint addi = bus.Read32(pc + 0x18);
        uint add = bus.Read32(pc + 0x1C);
        if (add != 0x7C60_1A14)
        {
            return false;
        }

        tableAddress = unchecked(0x8017_0000u + (uint)(short)(addi & 0xFFFF));
        return true;
    }

    private static void SetCr0ForSignedCompareImmediate(PowerPcState state, uint left, int right)
    {
        int signedLeft = unchecked((int)left);
        uint field = signedLeft == right
            ? 0x2000_0000u
            : signedLeft < right ? 0x8000_0000u : 0x4000_0000u;
        state.Cr = (state.Cr & 0x0FFF_FFFF) | field | (state.Xer & 0x8000_0000) >> 3;
    }

    private static void SetCr0ForSignedCompare(PowerPcState state, uint left, uint right)
    {
        int signedLeft = unchecked((int)left);
        int signedRight = unchecked((int)right);
        uint field = signedLeft == signedRight
            ? 0x2000_0000u
            : signedLeft < signedRight ? 0x8000_0000u : 0x4000_0000u;
        state.Cr = (state.Cr & 0x0FFF_FFFF) | field | (state.Xer & 0x8000_0000) >> 3;
    }

    private static void SetCr0ForUnsignedCompareImmediate(PowerPcState state, uint left, uint right)
    {
        uint field = left == right
            ? 0x2000_0000u
            : left < right ? 0x8000_0000u : 0x4000_0000u;
        state.Cr = (state.Cr & 0x0FFF_FFFF) | field | (state.Xer & 0x8000_0000) >> 3;
    }

    private static bool TryFastForwardMemmoveRoutine(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        uint first = bus.Read32(pc);
        if (first == 0x7C04_1840)
        {
            return TryFastForwardMemmoveFromEntry(state, bus, pc, out skippedInstructions);
        }

        if (first == 0x38A5_0001)
        {
            if (TryFastForwardMemmoveForwardSetup(state, bus, pc, out skippedInstructions))
            {
                return true;
            }

            return TryFastForwardMemmoveBackwardSetup(state, bus, pc, out skippedInstructions);
        }

        if (first == 0x8C04_0001)
        {
            return TryFastForwardMemmoveForwardLoop(state, bus, pc, out skippedInstructions);
        }

        if (first == 0x8C04_FFFF)
        {
            return TryFastForwardMemmoveBackwardLoop(state, bus, pc, out skippedInstructions);
        }

        return false;
    }

    private static bool TryFastForwardMemmoveFromEntry(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (bus.Read32(pc + 0x04) != 0x4180_0028)
        {
            return false;
        }

        uint count = state.Gpr[5];
        if (count == 0)
        {
            return false;
        }

        if (state.Gpr[4] < state.Gpr[3])
        {
            byte lastValue;
            if (!MatchesMemmoveBackwardSetup(bus, pc + 0x2C)
                || !TryCopyMemmoveBackward(state, bus, state.Gpr[4], state.Gpr[3], count, extraInstructions: 9, out lastValue))
            {
                return false;
            }

            state.Gpr[0] = lastValue;
            state.Gpr[5] = 0;
            state.Gpr[6] = state.Gpr[3];
            state.Pc = state.Lr;
            SetCarry(state, carry: true);
            SetCr0(state, 0);
            uint skipped = checked(9 + count * 4);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }

        byte forwardLastValue;
        if (!MatchesMemmoveForwardSetup(bus, pc + 0x08)
            || !TryCopyMemmoveForward(state, bus, state.Gpr[4], state.Gpr[3], count, extraInstructions: 9, out forwardLastValue))
        {
            return false;
        }

        state.Gpr[0] = forwardLastValue;
        state.Gpr[4] = unchecked(state.Gpr[4] + count - 1);
        state.Gpr[5] = 0;
        state.Gpr[6] = unchecked(state.Gpr[3] + count - 1);
        state.Pc = state.Lr;
        SetCarry(state, carry: true);
        SetCr0(state, 0);
        uint forwardSkipped = checked(9 + count * 4);
        AdvanceFastForwardedInstructions(state, bus, forwardSkipped);
        skippedInstructions = checked((int)forwardSkipped);
        return true;
    }

    private static bool TryFastForwardMemmoveForwardSetup(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesMemmoveForwardLoopTail(bus, pc + 0x08))
        {
            return false;
        }

        uint count = state.Gpr[5];
        if (count == 0)
        {
            return false;
        }

        uint sourceStart = unchecked(state.Gpr[4] + 1);
        uint destinationStart = unchecked(state.Gpr[6] + 1);
        if (!TryCopyMemmoveForward(state, bus, sourceStart, destinationStart, count, extraInstructions: 5, out byte lastValue))
        {
            return false;
        }

        state.Gpr[0] = lastValue;
        state.Gpr[4] = unchecked(state.Gpr[4] + count);
        state.Gpr[5] = 0;
        state.Gpr[6] = unchecked(state.Gpr[6] + count);
        state.Pc = state.Lr;
        SetCarry(state, carry: true);
        SetCr0(state, 0);
        uint skipped = checked(5 + count * 4);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardMemmoveForwardLoop(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesMemmoveForwardLoopTail(bus, pc))
        {
            return false;
        }

        uint count = state.Gpr[5];
        if (count == 0)
        {
            return false;
        }

        uint sourceStart = unchecked(state.Gpr[4] + 1);
        uint destinationStart = unchecked(state.Gpr[6] + 1);
        if (!TryCopyMemmoveForward(state, bus, sourceStart, destinationStart, count, extraInstructions: 1, out byte lastValue))
        {
            return false;
        }

        state.Gpr[0] = lastValue;
        state.Gpr[4] = unchecked(state.Gpr[4] + count);
        state.Gpr[5] = 0;
        state.Gpr[6] = unchecked(state.Gpr[6] + count);
        state.Pc = state.Lr;
        SetCarry(state, carry: true);
        SetCr0(state, 0);
        uint skipped = checked(1 + count * 4);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardMemmoveBackwardSetup(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesMemmoveBackwardLoopTail(bus, pc + 0x08))
        {
            return false;
        }

        uint count = state.Gpr[5];
        if (count == 0)
        {
            return false;
        }

        if (!TryCopyMemmoveBackward(state, bus, unchecked(state.Gpr[4] - count), unchecked(state.Gpr[6] - count), count, extraInstructions: 5, out byte lastValue))
        {
            return false;
        }

        state.Gpr[0] = lastValue;
        state.Gpr[4] = unchecked(state.Gpr[4] - count);
        state.Gpr[5] = 0;
        state.Gpr[6] = unchecked(state.Gpr[6] - count);
        state.Pc = state.Lr;
        SetCarry(state, carry: true);
        SetCr0(state, 0);
        uint skipped = checked(5 + count * 4);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardMemmoveBackwardLoop(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesMemmoveBackwardLoopTail(bus, pc))
        {
            return false;
        }

        uint count = state.Gpr[5];
        if (count == 0)
        {
            return false;
        }

        if (!TryCopyMemmoveBackward(state, bus, unchecked(state.Gpr[4] - count), unchecked(state.Gpr[6] - count), count, extraInstructions: 1, out byte lastValue))
        {
            return false;
        }

        state.Gpr[0] = lastValue;
        state.Gpr[4] = unchecked(state.Gpr[4] - count);
        state.Gpr[5] = 0;
        state.Gpr[6] = unchecked(state.Gpr[6] - count);
        state.Pc = state.Lr;
        SetCarry(state, carry: true);
        SetCr0(state, 0);
        uint skipped = checked(1 + count * 4);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesMemmoveForwardSetup(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x3884_FFFF
        && bus.Read32(pc + 0x04) == 0x38C3_FFFF
        && bus.Read32(pc + 0x08) == 0x38A5_0001
        && bus.Read32(pc + 0x0C) == 0x4800_000C
        && MatchesMemmoveForwardLoopTail(bus, pc + 0x10);

    private static bool MatchesMemmoveForwardLoopTail(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x8C04_0001
        && bus.Read32(pc + 0x04) == 0x9C06_0001
        && bus.Read32(pc + 0x08) == 0x34A5_FFFF
        && bus.Read32(pc + 0x0C) == 0x4082_FFF4
        && bus.Read32(pc + 0x10) == 0x4E80_0020;

    private static bool MatchesMemmoveBackwardSetup(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x7C84_2A14
        && bus.Read32(pc + 0x04) == 0x7CC3_2A14
        && bus.Read32(pc + 0x08) == 0x38A5_0001
        && bus.Read32(pc + 0x0C) == 0x4800_000C
        && MatchesMemmoveBackwardLoopTail(bus, pc + 0x10);

    private static bool MatchesMemmoveBackwardLoopTail(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x8C04_FFFF
        && bus.Read32(pc + 0x04) == 0x9C06_FFFF
        && bus.Read32(pc + 0x08) == 0x34A5_FFFF
        && bus.Read32(pc + 0x0C) == 0x4082_FFF4
        && bus.Read32(pc + 0x10) == 0x4E80_0020;

    private static bool TryCopyMemmoveForward(PowerPcState state, GameCubeBus bus, uint sourceStart, uint destinationStart, uint count, uint extraInstructions, out byte lastValue)
    {
        lastValue = 0;
        if (count > MaxFastForwardMemmoveBytes
            || !CanFastForwardInstructionCount(state, count, 4, extraInstructions)
            || !bus.Memory.IsMainRamAddress(sourceStart, checked((int)count))
            || !bus.Memory.IsMainRamAddress(destinationStart, checked((int)count)))
        {
            return false;
        }

        byte[] bytes = new byte[(int)count];
        for (uint offset = 0; offset < count; offset++)
        {
            bytes[(int)offset] = bus.Memory.Read8(sourceStart + offset);
        }

        for (uint offset = 0; offset < count; offset++)
        {
            lastValue = bytes[(int)offset];
            bus.Memory.Write8(destinationStart + offset, lastValue);
        }

        return true;
    }

    private static bool TryCopyMemmoveBackward(PowerPcState state, GameCubeBus bus, uint sourceStart, uint destinationStart, uint count, uint extraInstructions, out byte lastValue)
    {
        lastValue = 0;
        if (count > MaxFastForwardMemmoveBytes
            || !CanFastForwardInstructionCount(state, count, 4, extraInstructions)
            || !bus.Memory.IsMainRamAddress(sourceStart, checked((int)count))
            || !bus.Memory.IsMainRamAddress(destinationStart, checked((int)count)))
        {
            return false;
        }

        byte[] bytes = new byte[(int)count];
        for (uint offset = 0; offset < count; offset++)
        {
            bytes[(int)offset] = bus.Memory.Read8(sourceStart + offset);
        }

        for (uint index = count; index > 0; index--)
        {
            lastValue = bytes[(int)index - 1];
            bus.Memory.Write8(destinationStart + index - 1, lastValue);
        }

        return true;
    }

    private static uint Rlwinm(uint value, int shift, int maskBegin, int maskEnd)
    {
        uint rotated = shift == 0 ? value : (value << shift) | (value >> (32 - shift));
        return rotated & PowerPcMask(maskBegin, maskEnd);
    }

    private static uint Rlwimi(uint original, uint value, int shift, int maskBegin, int maskEnd)
    {
        uint mask = PowerPcMask(maskBegin, maskEnd);
        uint rotated = shift == 0 ? value : (value << shift) | (value >> (32 - shift));
        return (original & ~mask) | (rotated & mask);
    }

    private static uint ShiftLeftWord(uint value, uint shift) => value << (int)(shift & 0x1F);

    private static uint PowerPcMask(int begin, int end)
    {
        uint mask = 0;
        int bit = begin;
        while (true)
        {
            mask |= 1u << (31 - bit);
            if (bit == end)
            {
                return mask;
            }

            bit = (bit + 1) & 0x1F;
        }
    }

    private static bool CanFastForwardInstructionCount(PowerPcState state, uint iterations, uint instructionsPerIteration, uint extraInstructions)
    {
        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) != 0)
        {
            return true;
        }

        ulong skipped = (ulong)iterations * instructionsPerIteration + extraInstructions;
        return skipped <= decrementer;
    }

    private static void AdvanceFastForwardedInstructions(PowerPcState state, GameCubeBus bus, uint instructions)
    {
        state.TimeBase += instructions;
        state.Spr[22] -= instructions;
        bus.Advance(instructions);
    }

    private static bool TryDecodeDForm(uint instruction, int primaryOpcode, out int rD, out int rA, out int immediate)
    {
        rD = 0;
        rA = 0;
        immediate = 0;
        if ((instruction >> 26) != primaryOpcode)
        {
            return false;
        }

        rD = (int)((instruction >> 21) & 0x1F);
        rA = (int)((instruction >> 16) & 0x1F);
        immediate = unchecked((short)(instruction & 0xFFFF));
        return true;
    }

    private static void SetCr0(PowerPcState state, uint value)
    {
        uint field = value switch
        {
            0 => 0x2000_0000,
            _ when (value & 0x8000_0000) != 0 => 0x8000_0000,
            _ => 0x4000_0000,
        };

        state.Cr = (state.Cr & 0x0FFF_FFFF) | field | (state.Xer & 0x8000_0000) >> 3;
    }

    private static void SetCarry(PowerPcState state, bool carry)
    {
        if (carry)
        {
            state.Xer |= 0x2000_0000;
        }
        else
        {
            state.Xer &= ~0x2000_0000u;
        }
    }

    private static bool HasEnabledVideoInterrupt(GameCubeBus bus)
    {
        foreach (uint address in VideoInterruptRegisters)
        {
            if (bus.TryGetMmioValue(address, out uint value) && (value & GameCubeBus.VideoInterruptEnable) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static uint[] BuildWatchCallTargets(RunDolOptions options) =>
        options.WatchCallTargets is { Count: > 0 } ? options.WatchCallTargets.Distinct().ToArray() : [];

    private static Dictionary<uint, Dictionary<uint, ulong>>? BuildBranchSiteProfileDictionaries(RunDolOptions options)
    {
        if (options.BranchSiteProfiles is not { Count: > 0 } requests)
        {
            return null;
        }

        return requests
            .Select(request => request.Address)
            .Distinct()
            .ToDictionary(static address => address, static _ => new Dictionary<uint, ulong>());
    }

    private static Dictionary<uint, Dictionary<uint, ulong>>? BuildPcLrProfileDictionaries(RunDolOptions options)
    {
        if (options.PcLrProfiles is not { Count: > 0 } requests)
        {
            return null;
        }

        return requests
            .Select(request => request.Address)
            .Distinct()
            .ToDictionary(static address => address, static _ => new Dictionary<uint, ulong>());
    }

    private static GxMemoryCheckpointState[] BuildGxMemoryCheckpointStates(RunDolOptions options) =>
        options.GxMemoryCheckpoints is { Count: > 0 }
            ? options.GxMemoryCheckpoints.Select(request => new GxMemoryCheckpointState(request)).ToArray()
            : [];

    private static GxMemorySnapshotSet? BuildGxMemorySnapshotSet(GxMemoryCheckpointState[] checkpoints, IReadOnlyList<GxMemorySnapshot>? automaticSnapshots)
    {
        List<GxMemorySnapshot> snapshots = checkpoints
            .Where(checkpoint => checkpoint.Written && checkpoint.Bytes is not null)
            .Select(checkpoint => new GxMemorySnapshot(checkpoint.Request.FifoOffset, checkpoint.Request.Address, checkpoint.Bytes!, checkpoint.Request.Path))
            .ToList();
        if (automaticSnapshots is { Count: > 0 })
        {
            snapshots.AddRange(automaticSnapshots);
        }

        return snapshots.Count == 0 ? null : new GxMemorySnapshotSet(snapshots);
    }

    private static bool TryWriteGxMemoryCheckpoint(GameCubeMemory memory, GxMemoryCheckpointRequest request, out byte[]? bytes, out string? error)
    {
        bytes = null;
        error = null;
        try
        {
            byte[] capturedBytes = new byte[request.Length];
            for (int offset = 0; offset < capturedBytes.Length; offset++)
            {
                capturedBytes[offset] = memory.Read8(request.Address + (uint)offset);
            }

            string fullPath = Path.GetFullPath(request.Path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(fullPath, capturedBytes);
            bytes = capturedBytes;
            return true;
        }
        catch (Exception ex) when (ex is AddressTranslationException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool HasWriteWatch(RunDolOptions options) =>
        options.WatchWriteValue is not null || (options.WatchWriteRangeAddress is not null && options.WatchWriteRangeLength is not null);

    private static bool MatchesWriteWatch(RunDolOptions options, uint address, int width, uint value)
    {
        if (options.WatchWriteValue is uint watchValue && value == watchValue)
        {
            return true;
        }

        return MatchesWriteWatchRange(options, address, width);
    }

    private static bool MatchesWriteWatchRange(RunDolOptions options, uint address, int width)
    {
        if (options.WatchWriteRangeAddress is uint rangeStart && options.WatchWriteRangeLength is int rangeLength)
        {
            ulong writeStart = address;
            ulong writeEnd = writeStart + (uint)Math.Max(width, 0);
            ulong watchStart = rangeStart;
            ulong watchEnd = watchStart + (uint)rangeLength;
            if (writeStart < watchEnd && writeEnd > watchStart)
            {
                return true;
            }

            if (GameCubeAddress.TryTranslateMainRam(address, out int writeOffset)
                && GameCubeAddress.TryTranslateMainRam(rangeStart, out int watchOffset))
            {
                ulong normalizedWriteStart = (uint)writeOffset;
                ulong normalizedWriteEnd = normalizedWriteStart + (uint)Math.Max(width, 0);
                ulong normalizedWatchStart = (uint)watchOffset;
                ulong normalizedWatchEnd = normalizedWatchStart + (uint)rangeLength;
                return normalizedWriteStart < normalizedWatchEnd && normalizedWriteEnd > normalizedWatchStart;
            }
        }

        return false;
    }

    private static bool TryGetWatchedLoad(RunDolOptions options, GameCubeBus bus, PowerPcState state, uint instruction, out WatchedLoad watchedLoad)
    {
        watchedLoad = default;
        if (options.WatchLoadRangeAddress is not uint rangeStart || options.WatchLoadRangeLength is not int rangeLength)
        {
            return false;
        }

        if (!TryGetLoadEffectiveAddress(state, instruction, out uint effectiveAddress, out int targetRegister, out int byteWidth))
        {
            return false;
        }

        ulong loadStart = effectiveAddress;
        ulong loadEnd = loadStart + (uint)byteWidth;
        ulong watchStart = rangeStart;
        ulong watchEnd = watchStart + (uint)rangeLength;
        if (loadStart >= watchEnd || loadEnd <= watchStart)
        {
            return false;
        }

        try
        {
            uint value = byteWidth switch
            {
                1 => bus.Read8(effectiveAddress),
                2 => bus.Read16(effectiveAddress),
                4 => bus.Read32(effectiveAddress),
                _ => 0,
            };
            watchedLoad = new WatchedLoad(effectiveAddress, value, targetRegister);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool TryGetLoadEffectiveAddress(PowerPcState state, uint instruction, out uint effectiveAddress, out int targetRegister, out int byteWidth)
    {
        effectiveAddress = 0;
        targetRegister = (int)((instruction >> 21) & 0x1F);
        byteWidth = 0;
        int opcode = (int)(instruction >> 26);
        int rA = (int)((instruction >> 16) & 0x1F);
        int rB = (int)((instruction >> 11) & 0x1F);

        switch (opcode)
        {
            case 32 or 33:
                byteWidth = sizeof(uint);
                effectiveAddress = unchecked((rA == 0 ? 0 : state.Gpr[rA]) + (uint)(short)(instruction & 0xFFFF));
                return true;
            case 34 or 35:
                byteWidth = sizeof(byte);
                effectiveAddress = unchecked((rA == 0 ? 0 : state.Gpr[rA]) + (uint)(short)(instruction & 0xFFFF));
                return true;
            case 40 or 41 or 42 or 43:
                byteWidth = sizeof(ushort);
                effectiveAddress = unchecked((rA == 0 ? 0 : state.Gpr[rA]) + (uint)(short)(instruction & 0xFFFF));
                return true;
            case 31:
                int extendedOpcode = (int)((instruction >> 1) & 0x3FF);
                switch (extendedOpcode)
                {
                    case 23 or 55:
                        byteWidth = sizeof(uint);
                        break;
                    case 87 or 119:
                        byteWidth = sizeof(byte);
                        break;
                    case 279 or 311 or 343 or 375:
                        byteWidth = sizeof(ushort);
                        break;
                    default:
                        return false;
                }

                effectiveAddress = unchecked((rA == 0 ? 0 : state.Gpr[rA]) + state.Gpr[rB]);
                return true;
            default:
                return false;
        }
    }

    private static bool MatchesCallWatch(RunDolOptions options, HashSet<uint>? watchedCallTargetSet, uint target)
    {
        if (watchedCallTargetSet?.Contains(target) == true)
        {
            return true;
        }

        if (options.WatchCallRangeAddress is uint rangeStart && options.WatchCallRangeLength is int rangeLength)
        {
            ulong watchStart = rangeStart;
            ulong watchEnd = watchStart + (uint)rangeLength;
            return target >= watchStart && target < watchEnd;
        }

        return false;
    }

    private static bool TryGetIndirectBranchTarget(uint instruction, PowerPcState state, out uint target, out string? targetRegister, out bool link)
    {
        target = 0;
        targetRegister = null;
        link = false;
        if ((instruction >> 26) != 19)
        {
            return false;
        }

        int extendedOpcode = (int)((instruction >> 1) & 0x3FF);
        switch (extendedOpcode)
        {
            case 16:
                target = state.Lr & 0xFFFF_FFFC;
                targetRegister = "LR";
                break;
            case 528:
                target = state.Ctr & 0xFFFF_FFFC;
                targetRegister = "CTR";
                break;
            default:
                return false;
        }

        int bo = (int)((instruction >> 21) & 0x1F);
        int bi = (int)((instruction >> 16) & 0x1F);
        if (!WouldBranch(state, bo, bi))
        {
            return false;
        }

        link = (instruction & 1) != 0;
        return true;
    }

    private static bool TryGetBranchSiteTarget(uint instruction, uint pc, PowerPcState state, out uint target)
    {
        target = 0;
        int opcode = (int)(instruction >> 26);
        switch (opcode)
        {
            case 18:
                target = DecodeBranchTarget(instruction, pc);
                return true;
            case 16:
                target = WouldBranch(state, (int)((instruction >> 21) & 0x1F), (int)((instruction >> 16) & 0x1F))
                    ? DecodeConditionalBranchTarget(instruction, pc)
                    : pc + sizeof(uint);
                return true;
            case 19:
                return TryGetConditionalBranchToRegisterTarget(instruction, pc, state, out target);
            default:
                return false;
        }
    }

    private static bool TryGetConditionalBranchToRegisterTarget(uint instruction, uint pc, PowerPcState state, out uint target)
    {
        target = 0;
        int extendedOpcode = (int)((instruction >> 1) & 0x3FF);
        uint registerTarget = extendedOpcode switch
        {
            16 => state.Lr & 0xFFFF_FFFC,
            528 => state.Ctr & 0xFFFF_FFFC,
            _ => 0,
        };

        if (registerTarget == 0 && extendedOpcode is not 16 and not 528)
        {
            return false;
        }

        target = WouldBranch(state, (int)((instruction >> 21) & 0x1F), (int)((instruction >> 16) & 0x1F))
            ? registerTarget
            : pc + sizeof(uint);
        return true;
    }

    private static uint DecodeBranchTarget(uint instruction, uint pc)
    {
        uint offset = instruction & 0x03FF_FFFC;
        if ((offset & 0x0200_0000) != 0)
        {
            offset |= 0xFC00_0000;
        }

        return (instruction & 0x2) != 0 ? offset : unchecked(pc + offset);
    }

    private static uint DecodeConditionalBranchTarget(uint instruction, uint pc)
    {
        uint offset = unchecked((uint)(short)(instruction & 0xFFFC));
        return (instruction & 0x2) != 0 ? offset : unchecked(pc + offset);
    }

    private static bool WouldBranch(PowerPcState state, int bo, int bi)
    {
        bool ctrOk = true;
        if ((bo & 0x04) == 0)
        {
            uint nextCtr = unchecked(state.Ctr - 1);
            ctrOk = (nextCtr != 0) ^ ((bo & 0x02) != 0);
        }

        bool crOk = true;
        if ((bo & 0x10) == 0)
        {
            crOk = GetConditionRegisterBit(state.Cr, bi) == ((bo & 0x08) != 0);
        }

        return ctrOk && crOk;
    }

    private static bool GetConditionRegisterBit(uint cr, int bit) => ((cr >> (31 - bit)) & 1) != 0;

    private static void WriteIndirectCallSiteProfile(TextWriter output, uint callSite, IReadOnlyDictionary<uint, ulong> profile, int topCount)
    {
        ulong total = 0;
        foreach (ulong count in profile.Values)
        {
            total += count;
        }

        output.WriteLine($"Indirect call-site profile 0x{callSite:X8}: {profile.Count} unique target(s), {total} call(s)");
        foreach (KeyValuePair<uint, ulong> entry in profile.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).Take(topCount))
        {
            double percent = total == 0 ? 0 : (double)entry.Value * 100 / total;
            output.WriteLine($"0x{entry.Key:X8}  {entry.Value,10}  {percent,6:F2}%");
        }
    }

    private static void WriteBranchSiteProfile(TextWriter output, uint branchSite, IReadOnlyDictionary<uint, ulong> profile, int topCount)
    {
        ulong total = profile.Values.Aggregate(0UL, static (sum, count) => sum + count);
        output.WriteLine($"Branch-site profile 0x{branchSite:X8}: {profile.Count} unique target(s), {total} branch(es)");
        foreach (KeyValuePair<uint, ulong> entry in profile.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).Take(topCount))
        {
            double percent = total == 0 ? 0 : (double)entry.Value * 100 / total;
            output.WriteLine($"0x{entry.Key:X8}  {entry.Value,10}  {percent,6:F2}%");
        }
    }

    private static void WritePcLrProfile(TextWriter output, uint pc, IReadOnlyDictionary<uint, ulong> profile, int topCount)
    {
        ulong total = profile.Values.Aggregate(0UL, static (sum, count) => sum + count);
        output.WriteLine($"PC LR profile 0x{pc:X8}: {profile.Count} unique LR value(s), {total} hit(s)");
        foreach (KeyValuePair<uint, ulong> entry in profile.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).Take(topCount))
        {
            double percent = total == 0 ? 0 : (double)entry.Value * 100 / total;
            output.WriteLine($"0x{entry.Key:X8}  {entry.Value,10}  {percent,6:F2}%");
        }
    }

    private static object BuildPcProfileSummary(IReadOnlyDictionary<uint, ulong> profile, int topCount, int executedInstructions, int profileAfter)
    {
        int profiledInstructions = Math.Max(0, executedInstructions - profileAfter);
        return new
        {
            uniqueAddresses = profile.Count,
            startInstruction = profileAfter,
            profiledInstructions,
            totalSamples = profile.Values.Aggregate(0UL, static (total, count) => total + count),
            entries = profile
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key)
                .Take(topCount)
                .Select(entry => new
                {
                    pc = $"0x{entry.Key:X8}",
                    count = entry.Value,
                    percent = profiledInstructions == 0 ? 0 : Math.Round((double)entry.Value * 100 / profiledInstructions, 3),
                })
                .ToArray(),
        };
    }

    private static object? BuildSonicResourceStateSummary(GameCubeBus bus, PowerPcState state)
    {
        const uint stateBase = 0x801C_C168;
        if (!bus.Memory.IsMainRamAddress(stateBase, 0x100))
        {
            return null;
        }

        uint smallDataStateAddress = unchecked(state.Gpr[13] - 30340u);
        uint firstModeFlagAddress = unchecked(state.Gpr[13] - 30016u);
        uint secondModeFlagAddress = unchecked(state.Gpr[13] - 30024u);
        uint modePointerAddress = unchecked(state.Gpr[13] - 30040u);

        return new
        {
            stateBase = $"0x{stateBase:X8}",
            stateByte13 = bus.Memory.Read8(stateBase + 0x13),
            stateByte47 = bus.Memory.Read8(stateBase + 0x47),
            word80 = $"0x{bus.Memory.Read32(stateBase + 0x80):X8}",
            wordEc = $"0x{bus.Memory.Read32(stateBase + 0xEC):X8}",
            smallDataState = TryReadMainRam32(bus, smallDataStateAddress, out uint smallDataState)
                ? $"0x{smallDataState:X8}"
                : null,
            firstModeFlag = TryReadMainRam32(bus, firstModeFlagAddress, out uint firstModeFlag)
                ? $"0x{firstModeFlag:X8}"
                : null,
            secondModeFlag = TryReadMainRam32(bus, secondModeFlagAddress, out uint secondModeFlag)
                ? $"0x{secondModeFlag:X8}"
                : null,
            modePointer = TryReadMainRam32(bus, modePointerAddress, out uint modePointer)
                ? $"0x{modePointer:X8}"
                : null,
            modePointerValue = TryReadMainRam32(bus, modePointer + 0x0C, out uint modePointerValue)
                ? $"0x{modePointerValue:X8}"
                : null,
        };
    }

    private static object BuildExternalInterfaceSummary(GameCubeBus bus)
    {
        ExternalInterfaceDebugSnapshot snapshot = bus.GetExternalInterfaceDebugSnapshot();
        return new
        {
            processorInterruptCause = $"0x{snapshot.ProcessorInterruptCause:X8}",
            processorInterruptMask = $"0x{snapshot.ProcessorInterruptMask:X8}",
            hasPendingExternalInterrupt = snapshot.HasPendingExternalInterrupt,
            channels = snapshot.Channels.Select(channel => new
            {
                channel = channel.Channel,
                parameter = $"0x{channel.Parameter:X8}",
                dmaAddress = $"0x{channel.DmaAddress:X8}",
                dmaLength = $"0x{channel.DmaLength:X8}",
                control = $"0x{channel.Control:X8}",
                data = $"0x{channel.Data:X8}",
                selectedDevice = channel.SelectedDevice,
                deviceConnected = channel.DeviceConnected,
                transferCompleteStatus = channel.TransferCompleteStatus,
                transferCompleteMask = channel.TransferCompleteMask,
                interruptStatus = channel.InterruptStatus,
                interruptMask = channel.InterruptMask,
                externalInterruptStatus = channel.ExternalInterruptStatus,
                externalInterruptMask = channel.ExternalInterruptMask,
                command = $"0x{channel.Command:X8}",
                hasCommand = channel.HasCommand,
                pendingImmediateWrite = channel.PendingImmediateWrite,
                pendingWriteOffset = $"0x{channel.PendingWriteOffset:X8}",
                memoryCardCommand = channel.MemoryCardCommand,
                memoryCardCommandStarted = channel.MemoryCardCommandStarted,
                memoryCardCommandByteCount = channel.MemoryCardCommandByteCount,
                memoryCardAddressBytesReceived = channel.MemoryCardAddressBytesReceived,
                memoryCardAddress = $"0x{channel.MemoryCardAddress:X8}",
                memoryCardOffset = $"0x{channel.MemoryCardOffset:X8}",
                memoryCardDataBytesTransferred = channel.MemoryCardDataBytesTransferred,
                memoryCardStatus = $"0x{channel.MemoryCardStatus:X2}",
                memoryCardInterruptEnabled = channel.MemoryCardInterruptEnabled,
            }).ToArray(),
        };
    }

    private static object BuildDiscInterfaceSummary(GameCubeBus bus)
    {
        DiscInterfaceDebugSnapshot snapshot = bus.GetDiscInterfaceDebugSnapshot();
        return new
        {
            processorInterruptCause = $"0x{snapshot.ProcessorInterruptCause:X8}",
            processorInterruptMask = $"0x{snapshot.ProcessorInterruptMask:X8}",
            hasPendingExternalInterrupt = snapshot.HasPendingExternalInterrupt,
            status = $"0x{snapshot.Status:X8}",
            cover = $"0x{snapshot.Cover:X8}",
            command0 = $"0x{snapshot.Command0:X8}",
            command1 = $"0x{snapshot.Command1:X8}",
            command2 = $"0x{snapshot.Command2:X8}",
            dmaAddress = $"0x{snapshot.DmaAddress:X8}",
            dmaLength = $"0x{snapshot.DmaLength:X8}",
            control = $"0x{snapshot.Control:X8}",
            immediateData = $"0x{snapshot.ImmediateData:X8}",
            configuration = $"0x{snapshot.Configuration:X8}",
            commandLatencyCycles = bus.DiscInterfaceCommandLatencyCycles,
            commandLatencyOverrideCycles = bus.DiscInterfaceCommandLatencyOverrideCycles,
            hasPendingCommand = snapshot.HasPendingCommand,
            pendingCommandCycles = snapshot.PendingCommandCycles,
            pendingCommand = snapshot.PendingCommand is DiscInterfacePendingCommandDebugSnapshot pendingCommand
                ? new
                {
                    sequence = pendingCommand.Sequence,
                    startCycle = pendingCommand.StartCycle,
                    elapsedCycles = pendingCommand.ElapsedCycles,
                    remainingCycles = pendingCommand.RemainingCycles,
                    latencyCycles = pendingCommand.LatencyCycles,
                    command0 = $"0x{pendingCommand.Command0:X8}",
                    command1 = $"0x{pendingCommand.Command1:X8}",
                    command2 = $"0x{pendingCommand.Command2:X8}",
                    dmaAddress = $"0x{pendingCommand.DmaAddress:X8}",
                    dmaLength = $"0x{pendingCommand.DmaLength:X8}",
                    commandName = pendingCommand.CommandName,
                    discOffset = $"0x{pendingCommand.DiscOffset:X8}",
                    commandLength = $"0x{pendingCommand.CommandLength:X8}",
                }
                : null,
            lastError = $"0x{snapshot.LastError:X8}",
            deviceErrorStatus = snapshot.DeviceErrorStatus,
            deviceErrorMask = snapshot.DeviceErrorMask,
            transferCompleteStatus = snapshot.TransferCompleteStatus,
            transferCompleteMask = snapshot.TransferCompleteMask,
            breakStatus = snapshot.BreakStatus,
            breakMask = snapshot.BreakMask,
            recentAccesses = BuildRecentMmioAccessSummary(
                bus.MmioAccesses,
                static access => access.DeviceName == "DI" || (access.DeviceName == "PI" && (access.Address == 0xCC00_3000 || access.Address == 0xCC00_3004)),
                24),
            commandHistory = snapshot.CommandHistory.Select(command => new
            {
                sequence = command.Sequence,
                startCycle = command.StartCycle,
                completeCycle = command.CompleteCycle,
                elapsedCycles = command.CompleteCycle >= command.StartCycle ? command.CompleteCycle - command.StartCycle : 0,
                latencyCycles = command.LatencyCycles,
                command0 = $"0x{command.Command0:X8}",
                command1 = $"0x{command.Command1:X8}",
                command2 = $"0x{command.Command2:X8}",
                dmaAddress = $"0x{command.DmaAddress:X8}",
                dmaLength = $"0x{command.DmaLength:X8}",
                commandName = command.CommandName,
                discOffset = $"0x{command.DiscOffset:X8}",
                commandLength = $"0x{command.CommandLength:X8}",
                status = $"0x{command.Status:X8}",
                lastError = $"0x{command.LastError:X8}",
                processorInterruptPending = command.ProcessorInterruptPending,
            }).ToArray(),
        };
    }

    private static object[] BuildRecentMmioAccessSummary(
        IReadOnlyList<MmioAccess> accesses,
        Func<MmioAccess, bool> predicate,
        int maxCount)
    {
        return accesses
            .Select((access, index) => new { access, index })
            .Where(entry => predicate(entry.access))
            .TakeLast(maxCount)
            .Select(entry => new
            {
                index = entry.index,
                kind = entry.access.Kind.ToString(),
                device = entry.access.DeviceName,
                address = $"0x{entry.access.Address:X8}",
                width = entry.access.Width,
                value = $"0x{entry.access.Value:X8}",
            })
            .ToArray();
    }

    private static bool TryReadMainRam32(GameCubeBus bus, uint address, out uint value)
    {
        value = 0;
        if (!bus.Memory.IsMainRamAddress(address, sizeof(uint)))
        {
            return false;
        }

        value = bus.Memory.Read32(address);
        return true;
    }

    private static object BuildPcProfileWithoutExternalInterruptLeavesSummary(IReadOnlyDictionary<uint, ulong> profile, int topCount, int executedInstructions, int profileAfter, GameCubeBus bus)
    {
        List<KeyValuePair<uint, ulong>> included = [];
        ulong totalSamples = 0;
        ulong excludedSamples = 0;
        int profiledInstructions = Math.Max(0, executedInstructions - profileAfter);

        foreach (KeyValuePair<uint, ulong> entry in profile)
        {
            if (IsExternalInterruptLeafHelperEntry(bus, entry.Key))
            {
                excludedSamples += entry.Value;
                continue;
            }

            included.Add(entry);
            totalSamples += entry.Value;
        }

        return new
        {
            uniqueAddresses = included.Count,
            startInstruction = profileAfter,
            profiledInstructions,
            totalSamples,
            excludedSamples,
            entries = included
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key)
                .Take(topCount)
                .Select(entry => new
                {
                    pc = $"0x{entry.Key:X8}",
                    count = entry.Value,
                    percent = profiledInstructions == 0 ? 0 : Math.Round((double)entry.Value * 100 / profiledInstructions, 3),
                })
                .ToArray(),
        };
    }

    private static bool IsExternalInterruptLeafHelperEntry(GameCubeBus bus, uint pc)
    {
        uint first = bus.Read32(pc);
        if (first == 0x7C60_00A6)
        {
            return bus.Read32(pc + 4) is 0x5464_045E or 0x6064_8000
                && bus.Read32(pc + 8) == 0x7C80_0124
                && bus.Read32(pc + 12) == 0x5463_8FFE
                && bus.Read32(pc + 16) == 0x4E80_0020;
        }

        return first == 0x2C03_0000
            && bus.Read32(pc + 4) == 0x7C80_00A6
            && bus.Read32(pc + 8) == 0x4182_000C
            && bus.Read32(pc + 12) == 0x6085_8000
            && bus.Read32(pc + 16) == 0x4800_0008
            && bus.Read32(pc + 20) == 0x5485_045E
            && bus.Read32(pc + 24) == 0x7CA0_0124
            && bus.Read32(pc + 28) == 0x5484_8FFE
            && bus.Read32(pc + 32) == 0x4E80_0020;
    }

    private static object BuildIndirectCallSiteProfileSummary(uint callSite, IReadOnlyDictionary<uint, ulong> profile, int topCount)
    {
        ulong totalCalls = profile.Values.Aggregate(0UL, static (total, count) => total + count);
        return new
        {
            callSite = $"0x{callSite:X8}",
            uniqueTargets = profile.Count,
            totalCalls,
            entries = profile
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key)
                .Take(topCount)
                .Select(entry => new
                {
                    target = $"0x{entry.Key:X8}",
                    count = entry.Value,
                    percent = totalCalls == 0 ? 0 : Math.Round((double)entry.Value * 100 / totalCalls, 3),
                })
                .ToArray(),
        };
    }

    private static object[] BuildBranchSiteProfilesSummary(IReadOnlyList<BranchSiteProfileRequest>? requests, IReadOnlyDictionary<uint, Dictionary<uint, ulong>> profiles)
    {
        if (requests is not { Count: > 0 })
        {
            return [];
        }

        return requests
            .GroupBy(static request => request.Address)
            .Select(group =>
            {
                BranchSiteProfileRequest request = group.First();
                Dictionary<uint, ulong> profile = profiles[request.Address];
                ulong totalBranches = profile.Values.Aggregate(0UL, static (total, count) => total + count);
                return new
                {
                    branchSite = $"0x{request.Address:X8}",
                    uniqueTargets = profile.Count,
                    totalBranches,
                    entries = profile
                        .OrderByDescending(entry => entry.Value)
                        .ThenBy(entry => entry.Key)
                        .Take(group.Max(static request => request.TopCount))
                        .Select(entry => new
                        {
                            target = $"0x{entry.Key:X8}",
                            count = entry.Value,
                            percent = totalBranches == 0 ? 0 : Math.Round((double)entry.Value * 100 / totalBranches, 3),
                        })
                        .ToArray(),
                };
            })
            .ToArray();
    }

    private static object[] BuildPcLrProfilesSummary(IReadOnlyList<PcLrProfileRequest>? requests, IReadOnlyDictionary<uint, Dictionary<uint, ulong>> profiles)
    {
        if (requests is not { Count: > 0 })
        {
            return [];
        }

        return requests
            .GroupBy(static request => request.Address)
            .Select(group =>
            {
                PcLrProfileRequest request = group.First();
                Dictionary<uint, ulong> profile = profiles[request.Address];
                ulong totalHits = profile.Values.Aggregate(0UL, static (total, count) => total + count);
                return new
                {
                    pc = $"0x{request.Address:X8}",
                    uniqueLrs = profile.Count,
                    totalHits,
                    entries = profile
                        .OrderByDescending(entry => entry.Value)
                        .ThenBy(entry => entry.Key)
                        .Take(group.Max(static request => request.TopCount))
                        .Select(entry => new
                        {
                            lr = $"0x{entry.Key:X8}",
                            count = entry.Value,
                            percent = totalHits == 0 ? 0 : Math.Round((double)entry.Value * 100 / totalHits, 3),
                        })
                        .ToArray(),
                };
            })
            .ToArray();
    }

    private static string FormatGxFrameSource(GxFrameDumpSource source) =>
        source switch
        {
            GxFrameDumpSource.Auto => "automatic best frame",
            GxFrameDumpSource.LastDisplayCopy => "last display copy",
            GxFrameDumpSource.LastNonBlackDisplayCopy => "last nonblack display copy",
            GxFrameDumpSource.LargestDisplayCopy => "largest display copy",
            GxFrameDumpSource.LastNonBlackEfb => "last nonblack EFB copy source",
            GxFrameDumpSource.ViFramebuffer => "VI framebuffer",
            GxFrameDumpSource.LastNonBlackViFramebuffer => "last nonblack VI framebuffer",
            GxFrameDumpSource.CopyIndex => "display copy",
            GxFrameDumpSource.CopySourceIndex => "EFB copy source",
            _ => "EFB",
        };

    private static string FormatGxFrameSourceSlug(GxFrameDumpSource source) =>
        source switch
        {
            GxFrameDumpSource.Auto => "auto",
            GxFrameDumpSource.LastDisplayCopy => "last-display-copy",
            GxFrameDumpSource.LastNonBlackDisplayCopy => "last-nonblack-display-copy",
            GxFrameDumpSource.LargestDisplayCopy => "largest-display-copy",
            GxFrameDumpSource.LastNonBlackEfb => "last-nonblack-efb",
            GxFrameDumpSource.ViFramebuffer => "vi-framebuffer",
            GxFrameDumpSource.LastNonBlackViFramebuffer => "last-nonblack-vi-framebuffer",
            GxFrameDumpSource.CopyIndex => "copy-index",
            GxFrameDumpSource.CopySourceIndex => "copy-source-index",
            _ => "efb",
        };

    private static string FormatTraceLine(int executed, uint pc, uint instruction)
    {
        return $"{executed,8} 0x{pc:X8}: 0x{instruction:X8}  {PowerPcDisassembler.Disassemble(instruction)}";
    }

    private static string FormatWatchRegisters(PowerPcState state, uint instruction)
    {
        int opcode = (int)(instruction >> 26);
        int rSOrD = (int)((instruction >> 21) & 0x1F);
        int rA = (int)((instruction >> 16) & 0x1F);
        int rB = (int)((instruction >> 11) & 0x1F);

        return opcode switch
        {
            31 => $"[r{rSOrD}=0x{state.Gpr[rSOrD]:X8} r{rA}=0x{state.Gpr[rA]:X8} r{rB}=0x{state.Gpr[rB]:X8} EA=0x{unchecked((rA == 0 ? 0 : state.Gpr[rA]) + state.Gpr[rB]):X8} CR=0x{state.Cr:X8} CTR=0x{state.Ctr:X8}]",
            36 or 37 => $"[r{rSOrD}=0x{state.Gpr[rSOrD]:X8} r{rA}=0x{state.Gpr[rA]:X8} EA=0x{unchecked((rA == 0 ? 0 : state.Gpr[rA]) + (uint)(short)(instruction & 0xFFFF)):X8} CR=0x{state.Cr:X8} CTR=0x{state.Ctr:X8}]",
            _ => $"[r0=0x{state.Gpr[0]:X8} r3=0x{state.Gpr[3]:X8} r5=0x{state.Gpr[5]:X8} r6=0x{state.Gpr[6]:X8} r10=0x{state.Gpr[10]:X8} r13=0x{state.Gpr[13]:X8} CR=0x{state.Cr:X8} CTR=0x{state.Ctr:X8}]",
        };
    }

    private static string FormatPcTraceRegisters(PowerPcState state) =>
        $"[LR=0x{state.Lr:X8} CTR=0x{state.Ctr:X8} CR=0x{state.Cr:X8} r1=0x{state.Gpr[1]:X8} r2=0x{state.Gpr[2]:X8} r3=0x{state.Gpr[3]:X8} r4=0x{state.Gpr[4]:X8} r5=0x{state.Gpr[5]:X8} r6=0x{state.Gpr[6]:X8} r7=0x{state.Gpr[7]:X8} r8=0x{state.Gpr[8]:X8} r9=0x{state.Gpr[9]:X8} r10=0x{state.Gpr[10]:X8} r13=0x{state.Gpr[13]:X8} r23=0x{state.Gpr[23]:X8} r24=0x{state.Gpr[24]:X8} r25=0x{state.Gpr[25]:X8} r26=0x{state.Gpr[26]:X8} r27=0x{state.Gpr[27]:X8} r28=0x{state.Gpr[28]:X8} r29=0x{state.Gpr[29]:X8} r30=0x{state.Gpr[30]:X8} r31=0x{state.Gpr[31]:X8}]";

    private readonly record struct WatchedLoad(uint EffectiveAddress, uint Value, int TargetRegister);

    private struct GxMemoryCheckpointState(GxMemoryCheckpointRequest request)
    {
        public GxMemoryCheckpointRequest Request { get; } = request;

        public bool Written { get; set; }

        public byte[]? Bytes { get; set; }
    }
}
