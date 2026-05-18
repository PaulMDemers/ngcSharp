using System.Text.Json;
using NgcSharp.Core;
using NgcSharp.Cpu;

namespace NgcSharp.App;

public sealed class DolRunner
{
    private const uint MaxFastForwardMemmoveBytes = 16 * 1024 * 1024;
    private const uint MaxFastForwardStringCopyBytes = 1 * 1024 * 1024;
    private const uint MaxFastForwardStringCompareBytes = 1 * 1024 * 1024;
    private const uint MaxFastForwardPrsOutputBytes = 16 * 1024 * 1024;
    private const uint MaxFastForwardTrigTableEntries = 0x0001_0000;
    private const uint SonicTrigTableInstructionsPerEntry = 180;
    private const uint SonicBitUnpackInstructionsPerRow = 400;
    private const uint SonicGxVertexEmitInstructionsPerVertex = 24;

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
        dol.LoadInto(bus.Memory);
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
        ulong cacheFastForwardInstructions = 0;
        ulong leafFastForwardInstructions = 0;
        ulong memoryCopyFastForwardInstructions = 0;
        ulong textureSampleFastForwardInstructions = 0;
        ulong stringCopyFastForwardInstructions = 0;
        ulong stringCompareFastForwardInstructions = 0;
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
        ulong normalizedStringScanFastForwardInstructions = 0;
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
            int writeWatchMatches = 0;
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

        void WriteRunSummary(int exitCode, string? stopReasonOverride = null, string? diagnosticFailure = null, string? exceptionType = null, uint? exceptionAddress = null, uint? exceptionInstruction = null)
        {
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
                    registers = new
                    {
                        lr = $"0x{state.Lr:X8}",
                        ctr = $"0x{state.Ctr:X8}",
                        cr = $"0x{state.Cr:X8}",
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
                    },
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
                    gx = new
                    {
                        fifoBytesWritten = gxFifoBytesWritten,
                        memoryCheckpointsRequested = gxMemoryCheckpoints.Length,
                        memoryCheckpointsWritten = gxMemoryCheckpointsWritten,
                        autoTextureSnapshots = gxTextureSnapshotCollector?.Snapshots.Count ?? 0,
                        autoTextureSnapshotDrawsSeen = gxTextureSnapshotCollector?.DrawsSeen ?? 0,
                    },
                    fastForward = new
                    {
                        idleCycles = idleFastForwardCycles,
                        ctrDelayInstructions = ctrDelayFastForwardInstructions,
                        bulkMemoryInstructions = bulkFastForwardInstructions,
                        cacheInstructions = cacheFastForwardInstructions,
                        leafHelperInstructions = leafFastForwardInstructions,
                        memoryCopyInstructions = memoryCopyFastForwardInstructions,
                        textureSampleInstructions = textureSampleFastForwardInstructions,
                        stringCopyInstructions = stringCopyFastForwardInstructions,
                        stringCompareInstructions = stringCompareFastForwardInstructions,
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
                        normalizedStringScanInstructions = normalizedStringScanFastForwardInstructions,
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

                if (pcProfile is not null)
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
                if (tracedPcSet?.Contains(pc) == true && executed >= options.TracePcAfter.GetValueOrDefault())
                {
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

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardWordFillLoop(state, bus, out skippedInstructions))
                {
                    bulkFastForwardInstructions += (uint)skippedInstructions;
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

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicNormalizedStringScan(state, bus, out skippedInstructions))
                {
                    normalizedStringScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (TryGetWatchedLoad(options, bus, state, currentInstruction, out WatchedLoad watchedLoad))
                {
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

                if (indirectCallSiteProfile is not null
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
            stepObserver?.Invoke(new DolRunStep(executed, state.Pc, ex.Instruction, state, bus, IsFinal: true));
            _error.WriteLine($"Stopped after {executed} instructions: unsupported instruction 0x{ex.Instruction:X8} at 0x{ex.Address:X8} ({PowerPcDisassembler.Disassemble(ex.Instruction)}).");
            bus.MainRamWrite32Observer = previousWriteObserver;
            bus.MainRamWriteObserver = previousWriteAnyObserver;
            bus.Memory.MainRamStoreObserver = previousMemoryStoreObserver;
            bus.Memory.MainRamBulkWriteObserver = previousBulkWriteObserver;
            bus.MmioAccessObserver = previousMmioObserver;
            WriteTraceTail(traceTail, _error);
            WriteRequestedMemoryFinds(options, bus, _error);
            WriteRequestedPointerTableDumps(options, bus, _error);

            if (options.DumpRegisters)
            {
                ConsoleFormatting.WriteRegisters(_error, state);
            }

            if (options.DumpMmio)
            {
                ConsoleFormatting.WriteMmioSummary(_error, bus.MmioAccesses);
            }

            if (options.DumpThreads)
            {
                ConsoleFormatting.WriteThreadSummary(_error, bus.Memory);
            }

            if (options.DumpMessageQueues)
            {
                ConsoleFormatting.WriteMessageQueueSummary(_error, bus.Memory);
            }

            WriteRequestedMemoryDumps(options, bus, _error);

            WriteRunSummary(2, stopReasonOverride: "unsupported-instruction", exceptionType: nameof(UnsupportedInstructionException), exceptionAddress: ex.Address, exceptionInstruction: ex.Instruction);
            return 2;
        }
        catch (AddressTranslationException ex)
        {
            stepObserver?.Invoke(new DolRunStep(executed, state.Pc, 0, state, bus, IsFinal: true));
            _error.WriteLine($"Stopped after {executed} instructions: unmapped memory access at 0x{ex.Address:X8}.");
            bus.MainRamWrite32Observer = previousWriteObserver;
            bus.MainRamWriteObserver = previousWriteAnyObserver;
            bus.Memory.MainRamStoreObserver = previousMemoryStoreObserver;
            bus.Memory.MainRamBulkWriteObserver = previousBulkWriteObserver;
            bus.MmioAccessObserver = previousMmioObserver;
            WriteTraceTail(traceTail, _error);
            WriteRequestedMemoryFinds(options, bus, _error);
            WriteRequestedPointerTableDumps(options, bus, _error);

            if (options.DumpRegisters)
            {
                ConsoleFormatting.WriteRegisters(_error, state);
            }

            if (options.DumpMmio)
            {
                ConsoleFormatting.WriteMmioSummary(_error, bus.MmioAccesses);
            }

            if (options.DumpThreads)
            {
                ConsoleFormatting.WriteThreadSummary(_error, bus.Memory);
            }

            if (options.DumpMessageQueues)
            {
                ConsoleFormatting.WriteMessageQueueSummary(_error, bus.Memory);
            }

            WriteRequestedMemoryDumps(options, bus, _error);

            WriteRunSummary(4, stopReasonOverride: "address-translation", exceptionType: nameof(AddressTranslationException), exceptionAddress: ex.Address);
            return 4;
        }

        bus.MainRamWrite32Observer = previousWriteObserver;
        bus.MainRamWriteObserver = previousWriteAnyObserver;
        bus.Memory.MainRamStoreObserver = previousMemoryStoreObserver;
        bus.Memory.MainRamBulkWriteObserver = previousBulkWriteObserver;
        bus.MmioAccessObserver = previousMmioObserver;
        stepObserver?.Invoke(new DolRunStep(executed, state.Pc, 0, state, bus, IsFinal: true));

        WriteTraceTail(traceTail, _output);
        WriteRequestedMemoryFinds(options, bus, _output);
        WriteRequestedPointerTableDumps(options, bus, _output);

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

            if (normalizedStringScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {normalizedStringScanFastForwardInstructions} normalized string scan instruction(s).");
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

        GxMemorySnapshotSet? gxMemorySnapshots = BuildGxMemorySnapshotSet(gxMemoryCheckpoints, gxTextureSnapshotCollector?.Snapshots);

        if (options.GxFrameDumpPath is not null)
        {
            int gxWidth = options.FrameWidth ?? 640;
            int gxHeight = options.FrameHeight ?? 480;
            int gxFrameMaxDraws = options.GxFrameMaxDraws ?? RunDolOptions.DefaultGxFrameMaxDraws;
            int gxFrameMaxRasterPixels = options.GxFrameMaxRasterPixels ?? RunDolOptions.DefaultGxFrameMaxRasterPixels;
            if (!GxFifoSoftwareRenderer.TryRender(bus.MmioAccesses, bus.Memory, options.GxFrameDumpPath, gxWidth, gxHeight, gxFrameMaxDraws, options.GxFrameSkipDraws, stopAfterMaxDraws: true, maxRasterizedPixels: gxFrameMaxRasterPixels, ignoreEfbCopyClear: options.GxFrameIgnoreEfbCopyClear, source: options.GxFrameSource, displayCopyIndex: options.GxFrameCopyIndex, out GxFifoSoftwareRenderResult? gxFrame, out string? gxFrameError, memorySnapshots: gxMemorySnapshots))
            {
                _error.WriteLine($"GX frame dump failed: {gxFrameError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-frame-dump: {gxFrameError}");
                return 3;
            }

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
        }

        if (options.GxFrameSweep is not null && WriteGxFrameSweep(bus, options, gxMemorySnapshots) != 0)
        {
            WriteRunSummary(3, diagnosticFailure: "gx-frame-sweep");
            return 3;
        }

        if (options.FrameDumpPath is not null)
        {
            if (!FramebufferDumper.TryDump(bus, options, out FramebufferDumpResult? frameDump, out string? frameDumpError))
            {
                _error.WriteLine($"Frame dump failed: {frameDumpError}");
                WriteRunSummary(3, diagnosticFailure: $"frame-dump: {frameDumpError}");
                return 3;
            }

            if (!options.Quiet)
            {
                _output.WriteLine($"Wrote {frameDump!.Width}x{frameDump.Height} {frameDump.Format} frame from 0x{frameDump.Address:X8} to {frameDump.Path}.");
            }
        }

        if (options.GxDrawDumpPath is not null)
        {
            if (!GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(bus.MmioAccesses, bus.Memory, options.GxDrawDumpPath, options.GxDrawSkipDraws, options.GxDrawMaxDraws, out GxFifoDrawDiagnosticResult? gxDraws, out string? gxDrawError))
            {
                _error.WriteLine($"GX draw dump failed: {gxDrawError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-draw-dump: {gxDrawError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string skipped = options.GxDrawSkipDraws == 0 ? string.Empty : $" after skipping {options.GxDrawSkipDraws} draw(s)";
                _output.WriteLine($"Wrote GX draw diagnostics for {gxDraws!.DrawsWritten} draw(s){skipped} to {gxDraws.Path}.");
            }
        }

        if (options.GxCopyDumpPath is not null)
        {
            int gxWidth = options.FrameWidth ?? 640;
            int gxHeight = options.FrameHeight ?? 480;
            int gxFrameMaxRasterPixels = options.GxFrameMaxRasterPixels ?? RunDolOptions.DefaultGxFrameMaxRasterPixels;
            if (!GxFifoSoftwareRenderer.TryWriteCopyDiagnostics(bus.MmioAccesses, bus.Memory, options.GxCopyDumpPath, gxWidth, gxHeight, gxFrameMaxRasterPixels, options.GxFrameIgnoreEfbCopyClear, out GxFifoCopyDiagnosticResult? gxCopies, out string? gxCopyError))
            {
                _error.WriteLine($"GX copy dump failed: {gxCopyError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-copy-dump: {gxCopyError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string rasterState = gxCopies!.RasterBudgetExhausted ? "raster budget exhausted" : "raster budget retained";
                _output.WriteLine($"Wrote {gxCopies.CopiesWritten} GX copy diagnostic event(s) after {gxCopies.TotalDraws} draw(s) to {gxCopies.Path} ({rasterState}).");
            }
        }

        if (options.GxCoverageDumpPath is not null)
        {
            int gxWidth = options.FrameWidth ?? 640;
            int gxHeight = options.FrameHeight ?? 480;
            int gxFrameMaxDraws = options.GxFrameMaxDraws ?? RunDolOptions.DefaultGxFrameMaxDraws;
            int gxFrameMaxRasterPixels = options.GxFrameMaxRasterPixels ?? RunDolOptions.DefaultGxFrameMaxRasterPixels;
            if (!GxFifoSoftwareRenderer.TryWriteDrawCoverageDiagnostics(bus.MmioAccesses, bus.Memory, options.GxCoverageDumpPath, gxWidth, gxHeight, gxFrameMaxRasterPixels, options.GxFrameIgnoreEfbCopyClear, options.GxFrameSkipDraws, gxFrameMaxDraws, out GxFifoCoverageDiagnosticResult? gxCoverage, out string? gxCoverageError))
            {
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
        }

        if (options.GxTevSampleDumpPath is not null)
        {
            if (!GxFifoSoftwareRenderer.TryWriteTevSampleDiagnostics(bus.MmioAccesses, bus.Memory, options.GxTevSampleDumpPath, options.GxDrawSkipDraws, options.GxDrawMaxDraws, out GxFifoTevSampleDiagnosticResult? gxTevSamples, out string? gxTevSampleError))
            {
                _error.WriteLine($"GX TEV sample dump failed: {gxTevSampleError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-tev-sample-dump: {gxTevSampleError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string skipped = options.GxDrawSkipDraws == 0 ? string.Empty : $" after skipping {options.GxDrawSkipDraws} draw(s)";
                _output.WriteLine($"Wrote {gxTevSamples!.SamplesWritten} GX TEV sample(s){skipped} after {gxTevSamples.TotalDraws} draw(s) and {gxTevSamples.CopiesSeen} copy event(s) to {gxTevSamples.Path}.");
            }
        }

        if (options.GxTextureDumpPath is not null)
        {
            if (!GxFifoSoftwareRenderer.TryWriteTextureDiagnostics(bus.MmioAccesses, bus.Memory, options.GxTextureDumpPath, options.GxDrawSkipDraws, options.GxDrawMaxDraws, out GxFifoTextureDiagnosticResult? gxTextures, out string? gxTextureError, memorySnapshots: gxMemorySnapshots))
            {
                _error.WriteLine($"GX texture dump failed: {gxTextureError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-texture-dump: {gxTextureError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string skipped = options.GxDrawSkipDraws == 0 ? string.Empty : $" after skipping {options.GxDrawSkipDraws} draw(s)";
                _output.WriteLine($"Wrote {gxTextures!.TexturesWritten} GX texture dump(s){skipped} after {gxTextures.TotalDraws} draw(s) and {gxTextures.CopiesSeen} copy event(s) to {gxTextures.DirectoryPath} (index {gxTextures.IndexPath}).");
            }
        }

        if (options.DumpRegisters)
        {
            ConsoleFormatting.WriteRegisters(_output, state);
        }

        if (options.DumpMmio)
        {
            ConsoleFormatting.WriteMmioSummary(_output, bus.MmioAccesses);
        }

        if (options.DumpThreads)
        {
            ConsoleFormatting.WriteThreadSummary(_output, bus.Memory);
        }

        if (options.DumpMessageQueues)
        {
            ConsoleFormatting.WriteMessageQueueSummary(_output, bus.Memory);
        }

        if (options.PcProfileTop is int pcProfileTop && pcProfile is not null)
        {
            ConsoleFormatting.WritePcProfile(_output, pcProfile, pcProfileTop, executed);
        }

        if (options.IndirectCallSiteProfileAddress is uint profiledCallSite
            && options.IndirectCallSiteProfileTop is int indirectCallSiteProfileTop
            && indirectCallSiteProfile is not null)
        {
            WriteIndirectCallSiteProfile(_output, profiledCallSite, indirectCallSiteProfile, indirectCallSiteProfileTop);
        }

        WriteRequestedMemoryDumps(options, bus, _output);

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
        skippedInstructions = 0;
        uint pc = state.Pc;
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

        if (first == 0x2C03_0000)
        {
            return TryFastForwardRestoreExternalInterruptLeaf(state, bus, pc, out skippedInstructions);
        }

        return false;
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
            bus.Write32(fifo, bus.Memory.Read32(vertexAddress));
            bus.Write32(fifo, bus.Memory.Read32(vertexAddress + 0x04));
            bus.Write32(fifo, bus.Memory.Read32(vertexAddress + 0x08));
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
        $"[LR=0x{state.Lr:X8} CTR=0x{state.Ctr:X8} CR=0x{state.Cr:X8} r1=0x{state.Gpr[1]:X8} r2=0x{state.Gpr[2]:X8} r3=0x{state.Gpr[3]:X8} r4=0x{state.Gpr[4]:X8} r5=0x{state.Gpr[5]:X8} r6=0x{state.Gpr[6]:X8} r7=0x{state.Gpr[7]:X8} r8=0x{state.Gpr[8]:X8} r9=0x{state.Gpr[9]:X8} r10=0x{state.Gpr[10]:X8} r13=0x{state.Gpr[13]:X8} r31=0x{state.Gpr[31]:X8}]";

    private readonly record struct WatchedLoad(uint EffectiveAddress, uint Value, int TargetRegister);

    private struct GxMemoryCheckpointState(GxMemoryCheckpointRequest request)
    {
        public GxMemoryCheckpointRequest Request { get; } = request;

        public bool Written { get; set; }

        public byte[]? Bytes { get; set; }
    }
}
