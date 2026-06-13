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
    private const uint MaxFastForwardSonicCoordinatePairs = 1 * 1024 * 1024;
    private const uint LockedCacheStart = 0xE000_0000;
    private const uint LockedCacheSize = 16 * 1024;
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
    private const uint SonicResourceFlagOffset = 0xFFFF_8A20;
    private const uint SonicResourceFlagSetQueuedPc = 0x800E_BE54;
    private const uint SonicResourceFlagSetActivePc = 0x800E_C59C;
    private const uint SonicResourceFlagSetListPc = 0x800E_CA38;
    private const uint SonicResourceFlagClearTaskPc = 0x800E_BB5C;
    private const uint SonicResourceFlagClearSelectedPc = 0x800E_BF20;
    private const int SonicResourceTaskSlotOffset = 0x2D0;
    private const uint SonicResourceFlagWaitLoopPc = 0x800E_BEA0;
    private const uint SonicResourceFlagWaitInstructionsPerIteration = 3;
    private const uint MinFastForwardSonicResourceFlagWaitIterations = 32;
    private const uint SonicInterruptStatusProloguePc = 0x800E_6760;
    private const uint SonicInterruptStatusPrologueExitPc = 0x800E_67AC;
    private const uint SonicInterruptStatusPrologueInstructions = 22;
    private const uint SonicInterruptStatusPollPc = 0x800E_67AC;
    private const uint SonicInterruptStatusFirstBitPc = 0x800E_67C8;
    private const uint SonicInterruptStatusSecondBitPc = 0x800E_67F0;
    private const uint SonicInterruptStatusTimerSetupExitPc = 0x800E_6814;
    private const uint SonicInterruptStatusTimestampPc = 0x800E_6814;
    private const uint SonicInterruptStatusTimestampExitPc = 0x800E_6844;
    private const uint SonicInterruptStatusComparePc = 0x800E_6844;
    private const uint SonicInterruptStatusCompareExitPc = 0x800E_68B4;
    private const uint SonicInterruptStatusControlBitPc = 0x800E_6888;
    private const uint SonicInterruptStatusTailPc = 0x800E_68B4;
    private const uint SonicInterruptStatusQueryProloguePc = 0x800E_6954;
    private const uint SonicInterruptStatusQueryPrologueReturnPc = 0x800E_6984;
    private const uint SonicInterruptStatusQueryPrologueInstructions = 12;
    private const uint SonicInterruptStatusQueryPostCallPc = 0x800E_6984;
    private const uint SonicDvdStatusWaitLoopPc = 0x800D_B9AC;
    private const uint MinFastForwardSonicDvdStatusWaitCycles = 64;
    private const uint SonicDvdStatusWaitEventMarginCycles = 64;
    private const uint SonicInitTableLoopTailPc = 0x8006_90D4;
    private const uint SonicInitTableLoopPc = 0x8006_8164;
    private const uint SonicInitTableLoopExitPc = 0x8006_90F0;
    private const uint SonicInitTableLoopTailInstructions = 7;
    private const uint SonicInitTableNullEntryInstructions = 13;
    private const uint SonicRecordHeaderScanLoopPc = 0x8013_2AD8;
    private const uint SonicRecordHeaderScanExitPc = 0x8013_2B08;
    private const int SonicRecordHeaderScanRecordCount = 40;
    private const uint SonicRecordHeaderScanRecordStride = 0x40;
    private const uint SonicFlagRecordScanLoopPc = 0x8013_7E58;
    private const uint SonicFlagRecordScanExitPc = 0x8013_7E7C;
    private const int SonicFlagRecordScanRecordCount = 40;
    private const uint SonicFlagRecordScanRecordStride = 0x64;
    private const uint SonicFlagRecordScanInstructionsPerSkippedRecord = 7;
    private const uint SonicTaskSlotCallbackScanLoopPc = 0x8012_5DEC;
    private const uint SonicTaskSlotCallbackScanExitPc = 0x8012_5E1C;
    private const int SonicTaskSlotCallbackScanSlotCount = 32;
    private const uint SonicTaskSlotCallbackScanSlotStride = 0x10;
    private const uint SonicTaskSlotCallbackScanInstructionsPerNullSlot = 7;
    private const uint SonicBitmaskDispatchScanLoopPc = 0x800E_8024;
    private const uint SonicBitmaskDispatchScanInstructionsPerZeroEntry = 6;
    private const uint MaxFastForwardSonicBitmaskDispatchScanEntries = 64;
    private const uint SonicModeChildStatusPollPc = 0x800E_2C0C;
    private const uint SonicModeChildStatusReadPc = 0x8013_3890;
    private const uint SonicModeChildStatusClearPc = 0x8013_3878;
    private const uint SonicModeChildStatusPointerOffset = 0xFFFF_8510;
    private const uint SonicModeStateUpdatePc = 0x800E_2E20;
    private const uint SonicModeStateBase = 0x801C_C168;
    private const uint SonicModeStateByteAddress = SonicModeStateBase + 0x47;
    private const uint SonicModeStateOutputByteAddress = SonicModeStateBase + 0x13;
    private const uint SonicModeStateJumpTableAddress = 0x801C_DC70;
    private const uint SonicModeCoordinatorProloguePc = 0x800E_30CC;
    private const uint SonicModeCoordinatorPrologueExitPc = 0x800E_30F0;
    private const uint SonicModeCoordinatorPrologueInstructions = 9;
    private const uint SonicModeCoordinatorBodyPc = 0x800E_30F0;
    private const uint SonicModeCoordinatorBodyBranchPc = 0x800E_3124;
    private const uint SonicModeCoordinatorBodyExitPc = 0x800E_3170;
    private const uint SonicModeCoordinatorZeroTailPc = 0x800E_3170;
    private const uint SonicModeCoordinatorEpiloguePc = 0x800E_3374;
    private const uint SonicModeCoordinatorZeroTailInstructions = 11;
    private const uint SonicStatusQueryPc = 0x8013_6CEC;
    private const uint SonicStatusQueryEpiloguePc = 0x8013_6F80;
    private const uint SonicStatusQueryCurrentIdOffset = 0xFFFF_9074;
    private const uint SonicStatusQueryCurrentPointerOffset = 0xFFFF_9078;
    private const uint SonicStatusQueryEarlyReturnInstructions = 25;
    private const uint SonicStatusCallerLoopPc = 0x8001_2B24;
    private const uint SonicStatusCallerPostQueryPc = 0x8001_2B2C;
    private const uint SonicStatusCallerDispatchPc = 0x8001_2B54;
    private const uint SonicStatusCallerLoopBackPc = 0x8001_2B58;
    private const uint SonicStatusCallerExitPc = 0x8001_2B5C;
    private const uint SonicNullSlotScanLoopPc = 0x8011_6BBC;
    private const uint SonicNullSlotScanExitPc = 0x8011_6BE8;
    private const uint SonicNullSlotScanSlotStride = 0x18;
    private const uint SonicNullSlotScanInstructionsPerNullSlot = 6;
    private const uint SonicNullSlotScanInstructionsPerMismatchSlot = 9;
    private const uint SonicNullSlotScanExitInstructions = 2;
    private const uint SonicPoolNullSlotScanLoopPc = 0x8011_6C18;
    private const uint SonicPoolNullSlotScanInstructionsPerOccupiedSlot = 6;
    private const uint SonicPoolSentinelSlotScanLoopPc = 0x8011_6C8C;
    private const uint SonicPoolSentinelSlotScanInstructionsPerNonSentinelSlot = 6;
    private const uint SonicTableKeyScanLoopPc = 0x8011_90B8;
    private const uint SonicTableKeyScanInstructionsPerMiss = 9;
    private const uint SonicModeRefreshCallbackCheckPc = 0x8012_33E0;
    private const uint SonicModeRefreshCounterCheckPc = 0x8012_33EC;
    private const uint SonicModeRefreshModeClassifyPc = 0x8012_340C;
    private const uint SonicModeRefreshCallbackPointerOffset = 0xFFFF_8F04;
    private const uint SonicModeRefreshCounterValueOffset = 0xFFFF_8B00;
    private const uint SonicModeRefreshCallbackNullCheckInstructions = 3;
    private const uint SonicModeRefreshCallbackCompareTailInstructions = 2;
    private const uint SonicModeRefreshCallbackBranchTailInstructions = 1;
    private const uint SonicModeRefreshCounterCheckInstructions = 5;
    private const uint SonicModeRefreshCounterCompareTailInstructions = 2;
    private const uint SonicModeRefreshCounterBranchTailInstructions = 1;
    private const uint SonicModeRefreshCallPc = 0x8012_3478;
    private const uint SonicModeRefreshPostCallPc = 0x8012_3484;
    private const uint SonicModeRefreshLoopPc = 0x8012_33E0;
    private const uint SonicModeRefreshExitPc = 0x8012_3490;
    private const uint SonicModeRefreshObjectPointerOffset = 0xFFFF_8F0C;
    private const uint SonicModeRefreshCallInstructions = 3;
    private const uint SonicModeRefreshPostCallInstructions = 3;
    private const uint SonicTableByteBuildLoopPc = 0x800E_1158;
    private const uint SonicTableByteBuildPostCallPc = 0x800E_1160;
    private const uint SonicTableByteBuildExitPc = 0x800E_117C;
    private const uint SonicTableByteClassifierPc = 0x800E_1F14;
    private const uint SonicTableByteBuildRecordCount = 3423;
    private const uint SonicTableByteBuildRecordStride = 72;
    private const uint SonicTableByteBuildCallInstructions = 2;
    private const uint SonicTableByteBuildPostCallInstructions = 7;
    private const uint SonicLineCopyLoadPc = 0x8013_B454;
    private const uint SonicLineCopyLoopPc = 0x8013_B430;
    private const uint SonicLineCopyExitPc = 0x8013_B460;
    private const uint SonicLineCopyLoadInstructions = 3;
    private const uint SonicLineCopyInstructionsPerByte = 12;
    private const uint SonicLineSkipLoopPc = 0x8013_A39C;
    private const uint SonicLineSkipContinuePc = 0x8013_A3E0;
    private const uint SonicLineSkipNulPc = 0x8013_A3E8;
    private const uint SonicLineSkipInstructionsPerOrdinaryByte = 10;
    private const uint SonicStringAppendScanLoopPc = 0x8010_DF90;
    private const uint SonicStringAppendScanTailPc = 0x8010_DF9C;
    private const uint SonicStringAppendScanInstructionsPerByte = 3;
    private const uint SonicFreeBlockScanLoopPc = 0x8013_9C94;
    private const uint SonicFreeBlockScanExhaustedPc = 0x8013_9CC0;
    private const uint SonicFreeBlockScanFoundPc = 0x8013_9D38;
    private const ushort SonicFreeBlockMagic = 0x4D46;
    private const uint SonicCacheStoreSweepLoopPc = 0x800E_4F88;
    private const uint SonicCacheStoreSweepTailPc = 0x800E_4F98;
    private const uint SonicCacheStoreSweepInstructionsPerIteration = 4;
    private const uint SonicCacheStoreSweepBytesPerIteration = 32;
    private const uint SonicStateZeroFillLoopPc = 0x800F_982C;
    private const uint SonicStateZeroFillTailPc = 0x800F_9858;
    private const uint SonicStateZeroFillInstructionsPerIteration = 11;
    private const uint SonicStateZeroFillBytesPerIteration = 36;
    private const uint SonicManagerSlotScanLoopPc = 0x8012_4F50;
    private const uint SonicManagerSlotScanCallbackPc = 0x8012_4F5C;
    private const uint SonicManagerSlotScanExitPc = 0x8012_4F74;
    private const uint SonicManagerSlotCount = 16;
    private const uint SonicManagerSlotStride = 1080;
    private const uint SonicManagerSlotInactiveInstructions = 7;
    private const uint SonicManagerSlotActivePrefixInstructions = 3;
    private const uint SonicTaskEntryScanLoopPc = 0x8013_03B0;
    private const uint SonicTaskEntryScanActivePc = 0x8013_03C0;
    private const uint SonicTaskEntryScanExitPc = 0x8013_03F4;
    private const uint SonicTaskEntryCount = 16;
    private const uint SonicTaskEntryStride = 156;
    private const uint SonicTaskEntryInactiveInstructions = 8;
    private const uint SonicTaskEntryActivePrefixInstructions = 4;
    private const uint SonicObjectSlotScanLoopPc = 0x8013_38DC;
    private const uint SonicObjectSlotScanActivePc = 0x8013_38EC;
    private const uint SonicObjectSlotScanExitPc = 0x8013_3900;
    private const uint SonicObjectSlotCount = 16;
    private const uint SonicObjectSlotStride = 164;
    private const uint SonicObjectSlotInactiveInstructions = 8;
    private const uint SonicObjectSlotActivePrefixInstructions = 4;
    private const uint SonicHalfwordChecksumLoopPc = 0x8016_F978;
    private const uint SonicHalfwordChecksumTailPc = 0x8016_FA00;
    private const uint SonicHalfwordChecksumSecondLoopPc = 0x8016_FBA8;
    private const uint SonicHalfwordChecksumSecondTailPc = 0x8016_FC30;
    private const uint SonicHalfwordChecksumInstructionsPerIteration = 34;
    private const uint SonicHalfwordChecksumBytesPerIteration = 16;
    private const uint SonicModeQueryPc = 0x800F_13A8;
    private const uint SonicDisableExternalInterruptPc = 0x800E_78AC;
    private const uint SonicRestoreExternalInterruptPc = 0x800E_78D4;
    private const uint SonicModeQueryBusyFlagOffset = 0xFFFF_8AC0;
    private const uint SonicModeQueryFallbackFlagOffset = 0xFFFF_8AB8;
    private const uint SonicModeQueryPointerOffset = 0xFFFF_8AA8;
    private const uint SonicModeQuerySentinelPointer = 0x802B_B700;
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
    private const uint SonicGxCommandMaskUpdatePc = 0x8011_CEC0;
    private const uint SonicGprSaveTailPc = 0x8010_AFCC;
    private const uint SonicGprRestoreTailPc = 0x8010_B018;
    private const uint SonicGxAttributeStateSetterPc = 0x8010_0D44;
    private const uint SonicGxBeginDirectPc = 0x8010_06D8;
    private const uint SonicGxDrawBeginPc = 0x8010_1948;
    private const uint SonicGxDrawBeginFastForwardInstructions = 28;
    private const uint SonicGxCpStateSetterPc = 0x8010_14A8;
    private const uint SonicGxCpStateSetterInstructions = 18;
    private const uint SonicGxTevColorEnvSetterPc = 0x8010_464C;
    private const uint SonicGxTevColorEnvSetterInstructions = 32;
    private const uint SonicGxTevColorEnvSetterTailPc = 0x8010_4698;
    private const uint SonicGxTevColorEnvSetterTailInstructions = 13;
    private const uint SonicGxTevAlphaEnvSetterPc = 0x8010_46CC;
    private const uint SonicGxTevAlphaEnvSetterInstructions = 33;
    private const uint SonicGxTevColorOpSetterPc = 0x8010_4750;
    private const uint SonicGxTevAlphaOpSetterPc = 0x8010_4810;
    private const uint SonicGxTevOpSetterLowModeInstructions = 40;
    private const uint SonicGxTevOpSetterHighModeInstructions = 37;
    private const uint SonicGxTevDefaultWrapperPc = 0x8010_44A8;
    private const uint SonicGxTevDefaultWrapperOwnInstructions = 46;
    private const uint SonicGxTevDefaultWrapperInstructions = SonicGxTevDefaultWrapperOwnInstructions
        + SonicGxTevColorEnvSetterInstructions
        + SonicGxTevAlphaEnvSetterInstructions
        + SonicGxTevOpSetterLowModeInstructions
        + SonicGxTevOpSetterLowModeInstructions;
    private const uint SonicGxVertexDescriptorSetterPc = 0x8010_0830;
    private const uint SonicGxVertexAttributeFlushPc = 0x8010_3D28;
    private const uint SonicGxVertexAttributeHelperPc = 0x8010_3C5C;
    private const uint SonicGxIndexedStripDrawBeginPc = 0x8012_0078;
    private const uint SonicGxIndexedStripTailPc = 0x8012_00FC;
    private const uint SonicGxIndexedStripEpiloguePc = 0x8012_010C;
    private const uint SonicGxIndexedStripEpilogueInstructions = 15;
    private const uint SonicGxFloatTexcoordStripEmitHeaderPc = 0x8011_D830;
    private const uint SonicGxFloatTexcoordStripEmitLoopPc = 0x8011_D860;
    private const uint SonicGxFloatTexcoordStripEmitInstructionsPerIteration = 36;
    private const uint SonicGxFloatTexcoordStripEmitExitInstructions = 2;
    private const uint SonicPairedTransform4dLoopPc = 0x8011_DB94;
    private const uint SonicPairedTransform4dInstructionsPerIteration = 20;
    private const uint SonicPairedTransform4dExitInstructions = 11;
    private const uint SonicPairedTransform4dIndexedLoopPc = 0x8011_DE54;
    private const uint SonicPairedTransform4dIndexedInstructionsPerIteration = 33;
    private const uint SonicPairedTransform4dIndexedExitInstructions = 16;
    private const uint SonicVectorBlendCopyLoopPc = 0x8012_0D98;
    private const uint SonicVectorBlendCopyInstructionsPerIteration = 47;
    private const uint SonicCoordinatePairFillLoopPc = 0x8014_B260;
    private const uint SonicBufferFillFirstLoopPc = 0x800F_C598;
    private const uint SonicBufferFillSecondLoopPc = 0x800F_C5C0;
    private const uint SonicBufferFillThirdLoopPc = 0x800F_C5E8;
    private const uint SonicBufferFillInstructionsPerIteration = 7;
    private const uint MaxFastForwardSonicBufferFillWords = MaxFastForwardMemmoveBytes / sizeof(uint);
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
        return Run(dol, options, new GameCubeBus(), stepObserver: null, prepareStandaloneBoot: true, CancellationToken.None);
    }

    public int Run(DolFile dol, RunDolOptions options)
    {
        return Run(dol, options, new GameCubeBus(), stepObserver: null, prepareStandaloneBoot: true, CancellationToken.None);
    }

    public int Run(DolFile dol, RunDolOptions options, GameCubeBus bus)
    {
        return Run(dol, options, bus, stepObserver: null);
    }

    public int Run(DolFile dol, RunDolOptions options, GameCubeBus bus, Action<DolRunStep>? stepObserver)
    {
        return Run(dol, options, bus, stepObserver, CancellationToken.None);
    }

    public int Run(DolFile dol, RunDolOptions options, GameCubeBus bus, Action<DolRunStep>? stepObserver, CancellationToken cancellationToken)
    {
        return Run(dol, options, bus, stepObserver, prepareStandaloneBoot: false, cancellationToken);
    }

    private int Run(DolFile dol, RunDolOptions options, GameCubeBus bus, Action<DolRunStep>? stepObserver, bool prepareStandaloneBoot, CancellationToken cancellationToken)
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
        int emittedSonicDrawPacketTraceChanges = 0;
        int emittedSonicGxEmitterTraceChanges = 0;
        int emittedSonicVertexProvenanceTraceChanges = 0;
        int emittedSonicTransformInputTraceChanges = 0;
        int emittedSonicMatrixStackTraceChanges = 0;
        int emittedSonicMatrixWriterTraceChanges = 0;
        int emittedSonicRootMatrixTraceChanges = 0;
        int emittedSonicSceneStateTraceChanges = 0;
        int emittedSonicPacketSelectionTraceChanges = 0;
        int emittedSonicTraversalSourceTraceChanges = 0;
        int emittedSonicBitstreamDecoderTraceChanges = 0;
        int emittedSonicInputWriteTraceChanges = 0;
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
        bool sonicBitstreamDecoderTraceLimitNoticeEmitted = false;
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
        bool pendingWriteWatchOldValueValid = false;
        uint pendingWriteWatchOldAddress = 0;
        int pendingWriteWatchOldWidth = 0;
        uint pendingWriteWatchOldValue = 0;
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
        ulong sonicStartCodeScanFastForwardInstructions = 0;
        ulong sonicInterruptStatusPrologueFastForwardInstructions = 0;
        ulong sonicInterruptStatusPollFastForwardInstructions = 0;
        ulong sonicInterruptStatusTimerSetupFastForwardInstructions = 0;
        ulong sonicInterruptStatusTimestampFastForwardInstructions = 0;
        ulong sonicInterruptStatusCompareFastForwardInstructions = 0;
        ulong sonicInterruptStatusTailFastForwardInstructions = 0;
        ulong sonicInterruptStatusQueryPrologueFastForwardInstructions = 0;
        ulong sonicInterruptStatusQueryPostCallFastForwardInstructions = 0;
        ulong sonicDvdStatusWaitFastForwardInstructions = 0;
        ulong sonicInitTableLoopTailFastForwardInstructions = 0;
        ulong sonicInitTableNullEntryFastForwardInstructions = 0;
        ulong sonicRecordHeaderScanFastForwardInstructions = 0;
        ulong sonicFlagRecordScanFastForwardInstructions = 0;
        ulong sonicTaskSlotCallbackScanFastForwardInstructions = 0;
        ulong sonicBitmaskDispatchScanFastForwardInstructions = 0;
        ulong sonicResourceFlagWaitFastForwardInstructions = 0;
        ulong sonicResourceModeQueryFastForwardInstructions = 0;
        ulong sonicResourceStatePollFastForwardInstructions = 0;
        ulong sonicModeChildStatusPollFastForwardInstructions = 0;
        ulong sonicModeStateUpdateFastForwardInstructions = 0;
        ulong sonicModeCoordinatorPrologueFastForwardInstructions = 0;
        ulong sonicModeCoordinatorBodyFastForwardInstructions = 0;
        ulong sonicModeCoordinatorZeroTailFastForwardInstructions = 0;
        ulong sonicModeQueryFastForwardInstructions = 0;
        ulong sonicStatusQueryFastForwardInstructions = 0;
        ulong sonicStatusCallerLoopFastForwardInstructions = 0;
        ulong sonicStatusCallerDispatchFastForwardInstructions = 0;
        ulong sonicTableByteBuildDispatchFastForwardInstructions = 0;
        ulong sonicLineCopyFastForwardInstructions = 0;
        ulong sonicLineSkipFastForwardInstructions = 0;
        ulong sonicStringAppendScanFastForwardInstructions = 0;
        ulong sonicFreeBlockScanFastForwardInstructions = 0;
        ulong sonicCacheStoreSweepFastForwardInstructions = 0;
        ulong sonicStateZeroFillFastForwardInstructions = 0;
        ulong sonicManagerSlotScanFastForwardInstructions = 0;
        ulong sonicTaskEntryScanFastForwardInstructions = 0;
        ulong sonicObjectSlotScanFastForwardInstructions = 0;
        ulong sonicHalfwordChecksumFastForwardInstructions = 0;
        ulong sonicNullSlotScanFastForwardInstructions = 0;
        ulong sonicPoolSlotScanFastForwardInstructions = 0;
        ulong sonicTableKeyScanFastForwardInstructions = 0;
        ulong sonicModeRefreshDispatchFastForwardInstructions = 0;
        ulong sonicModeWrapperFastForwardInstructions = 0;
        ulong sonicResourceFixupFastForwardInstructions = 0;
        ulong sonicOverlayInactiveSlotScanFastForwardInstructions = 0;
        ulong sonicPathLookupFastForwardInstructions = 0;
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
        ulong sonicGxCpStateSetterFastForwardInstructions = 0;
        ulong sonicGxTevColorEnvSetterFastForwardInstructions = 0;
        ulong sonicGxTevAlphaEnvSetterFastForwardInstructions = 0;
        ulong sonicGxTevColorOpSetterFastForwardInstructions = 0;
        ulong sonicGxTevAlphaOpSetterFastForwardInstructions = 0;
        ulong sonicGxTevDefaultWrapperFastForwardInstructions = 0;
        ulong sonicGxVertexDescriptorSetterFastForwardInstructions = 0;
        ulong sonicGxVertexAttributeFlushFastForwardInstructions = 0;
        ulong sonicGxIndexedStripBatchFastForwardInstructions = 0;
        ulong sonicGxIndexedStripDrawBeginFastForwardInstructions = 0;
        ulong sonicGxIndexedStripTailFastForwardInstructions = 0;
        ulong sonicGxIndexedStripEpilogueFastForwardInstructions = 0;
        ulong sonicGxFloatTexcoordStripEmitFastForwardInstructions = 0;
        ulong sonicPairedTransform4dFastForwardInstructions = 0;
        ulong sonicVectorBlendCopyFastForwardInstructions = 0;
        ulong sonicCoordinatePairFillFastForwardInstructions = 0;
        ulong sonicBufferFillFastForwardInstructions = 0;
        ulong sonicGeneratedModelPointerScanFastForwardInstructions = 0;
        ulong sonicGeneratedRangeScanFastForwardInstructions = 0;
        uint currentPc = state.Pc;
        uint currentInstruction = 0;
        Action<uint, uint>? previousWriteObserver = bus.MainRamWrite32Observer;
        Action<uint, int, uint>? previousWriteAnyObserver = bus.MainRamWriteObserver;
        Action<uint, int, uint>? previousLockedCacheWriteObserver = bus.LockedCacheWriteObserver;
        Action<uint, int, uint>? previousMemoryStoreObserver = bus.Memory.MainRamStoreObserver;
        Action<uint, int>? previousBulkWriteObserver = bus.Memory.MainRamBulkWriteObserver;
        Action<MmioAccess>? previousMmioObserver = bus.MmioAccessObserver;
        using TextWriter? gxFifoWriteTrace = OpenTraceFile(options.GxFifoWriteTracePath);
        if (gxFifoWriteTrace is not null || options.StopOnGxFifoOffset is not null || gxMemoryCheckpoints.Length != 0 || gxTextureSnapshotCollector is not null || options.SonicGxEmitterTracePath is not null || options.SonicTextureBindTracePath is not null || options.SonicVertexProvenanceTracePath is not null)
        {
            gxFifoWriteTrace?.WriteLine("instruction,pc,opcode,disassembly,fifo_offset_start,fifo_offset_end,width,address,value");
            long gxFifoTraceStart = options.GxFifoWriteTraceStart.GetValueOrDefault();
            long gxFifoTraceEnd = options.GxFifoWriteTraceStart is null
                ? long.MaxValue
                : checked(gxFifoTraceStart + options.GxFifoWriteTraceLength.GetValueOrDefault());
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
                if (gxFifoWriteTrace is not null && offsetStart < gxFifoTraceEnd && gxFifoBytesWritten > gxFifoTraceStart)
                {
                    gxFifoWriteTrace.WriteLine($"{executed + 1},0x{currentPc:X8},0x{currentInstruction:X8},\"{PowerPcDisassembler.Disassemble(currentInstruction).Replace("\"", "\"\"", StringComparison.Ordinal)}\",0x{offsetStart:X},0x{gxFifoBytesWritten:X},{access.Width},0x{access.Address:X8},0x{access.Value:X8}");
                }
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
                    string change = FormatWriteWatchChange(address, width, value, pendingWriteWatchOldValueValid, pendingWriteWatchOldAddress, pendingWriteWatchOldWidth, pendingWriteWatchOldValue);
                    _output.WriteLine($"Write watch 0x{address:X8}/{width} <= 0x{value:X8}{change} after {executed + 1} instruction(s), 0x{currentPc:X8}: 0x{currentInstruction:X8} {PowerPcDisassembler.Disassemble(currentInstruction)} {FormatWatchRegisters(state, currentInstruction)}");
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

        using TextWriter? sonicInputWriteTrace = OpenTraceFile(options.SonicInputWriteTracePath);
        if (sonicInputWriteTrace is not null)
        {
            const int windowBytes = 0x40;
            uint traceAddress = options.SonicInputWriteTraceAddress.GetValueOrDefault();
            int traceLength = options.SonicInputWriteTraceLength.GetValueOrDefault();
            sonicInputWriteTrace.WriteLine("instruction,pc,opcode,disassembly,kind,width,address,value,r1,r3,r4,r5,r6,r7,r8,r9,r10,r13,r24,r25,r27,r28,r29,r30,r31,trace_address,trace_length,range_bytes,post_write_bytes");
            Action<uint, int, uint>? chainedStoreObserver = bus.Memory.MainRamStoreObserver;
            Action<uint, int>? chainedBulkWriteObserver = bus.Memory.MainRamBulkWriteObserver;
            bus.Memory.MainRamStoreObserver = (address, width, value) =>
            {
                chainedStoreObserver?.Invoke(address, width, value);
                if (!OverlapsAddressRange(address, (uint)width, traceAddress, traceLength)
                    || executed + 1 < options.TracePcAfter.GetValueOrDefault()
                    || (options.WatchLimit is not null && emittedSonicInputWriteTraceChanges >= options.WatchLimit))
                {
                    return;
                }

                WriteSonicInputWriteTraceRow(sonicInputWriteTrace, executed + 1, currentPc, currentInstruction, state, bus.Memory, "store", width, address, value, traceAddress, traceLength, windowBytes);
                emittedSonicInputWriteTraceChanges++;
            };
            bus.Memory.MainRamBulkWriteObserver = (address, length) =>
            {
                chainedBulkWriteObserver?.Invoke(address, length);
                if (!OverlapsAddressRange(address, (uint)length, traceAddress, traceLength)
                    || executed + 1 < options.TracePcAfter.GetValueOrDefault()
                    || (options.WatchLimit is not null && emittedSonicInputWriteTraceChanges >= options.WatchLimit))
                {
                    return;
                }

                uint value = bus.Memory.IsMainRamAddress(address, sizeof(uint)) ? bus.Memory.Read32(address) : 0;
                WriteSonicInputWriteTraceRow(sonicInputWriteTrace, executed + 1, currentPc, currentInstruction, state, bus.Memory, "bulk", length, address, value, traceAddress, traceLength, windowBytes);
                emittedSonicInputWriteTraceChanges++;
            };
        }

        using TextWriter? sonicBitstreamDecoderTrace = OpenTraceFile(options.SonicBitstreamDecoderTracePath);
        if (sonicBitstreamDecoderTrace is not null)
        {
            sonicBitstreamDecoderTrace.WriteLine("instruction,pc,lr,source,source_end,destination,output_length,target_address,target_length,target_output_offset,last_flag_byte,bits_remaining,skipped_instructions,r3,r4,r5,r6,r7,r8,r9,r10,r13,r29,r30,r31,source_bytes,target_output_bytes,output_head_bytes");
        }

        using TextWriter? lockedCacheWriteTrace = OpenTraceFile(options.LockedCacheWriteTracePath);
        if (lockedCacheWriteTrace is not null)
        {
            const int windowBytes = 0x40;
            uint traceAddress = options.LockedCacheWriteTraceAddress.GetValueOrDefault();
            int traceLength = options.LockedCacheWriteTraceLength.GetValueOrDefault();
            int emittedLockedCacheTraceChanges = 0;
            lockedCacheWriteTrace.WriteLine("instruction,pc,opcode,disassembly,width,address,value,r1,r3,r4,r5,r6,r7,r8,r9,r10,r13,r24,r25,r27,r28,r29,r30,r31,range_bytes,post_write_bytes");
            Action<uint, int, uint>? chainedLockedCacheWriteObserver = bus.LockedCacheWriteObserver;
            bus.LockedCacheWriteObserver = (address, width, value) =>
            {
                chainedLockedCacheWriteObserver?.Invoke(address, width, value);
                if (!OverlapsAddressRange(address, (uint)width, traceAddress, traceLength)
                    || executed + 1 < options.TracePcAfter.GetValueOrDefault()
                    || (options.WatchLimit is not null && emittedLockedCacheTraceChanges >= options.WatchLimit))
                {
                    return;
                }

                WriteLockedCacheWriteTraceRow(lockedCacheWriteTrace, executed + 1, currentPc, currentInstruction, state, bus, width, address, value, traceAddress, traceLength, windowBytes);
                emittedLockedCacheTraceChanges++;
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

        using TextWriter? sonicResourceFlagTrace = OpenTraceFile(options.SonicResourceFlagTracePath);
        if (sonicResourceFlagTrace is not null)
        {
            sonicResourceFlagTrace.WriteLine("instruction,pc,lr,operation,flag_address,old_flag,new_flag,xor,mask,task,task_slot,selector,queue_head,queue_tail,r1,r3,r4,r5,r6,r7,r29,r30,r31");
        }

        using TextWriter? sonicMatrixStackTrace = OpenTraceFile(options.SonicMatrixStackTracePath);
        if (sonicMatrixStackTrace is not null)
        {
            sonicMatrixStackTrace.WriteLine("instruction,pc,lr,ctr,cr,matrix_base_pointer,matrix_limit_pointer,current_matrix_pointer,previous_matrix_pointer,r1,r3,r4,r5,r6,r7,r13,r27,r28,r29,r30,r31,r3_bytes,r4_bytes,r5_bytes,r6_bytes,r27_bytes,r30_bytes,base_matrix_bytes,previous_matrix_bytes,current_matrix_bytes");
        }

        using TextWriter? sonicMatrixWriterTrace = OpenTraceFile(options.SonicMatrixWriterTracePath);
        if (sonicMatrixWriterTrace is not null)
        {
            sonicMatrixWriterTrace.WriteLine("instruction,pc,lr,ctr,cr,opcode,disassembly,store_address,r1,r3,r4,r5,r6,r7,r8,r9,r10,r13,r27,r28,r29,r30,r31,f0,f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11,f12,f13,matrix_bytes,source_matrix_bytes,r1_bytes,r3_bytes,r4_bytes,r5_bytes,r6_bytes,r30_bytes");
        }

        using TextWriter? sonicRootMatrixTrace = OpenTraceFile(options.SonicRootMatrixTracePath);
        if (sonicRootMatrixTrace is not null)
        {
            sonicRootMatrixTrace.WriteLine("instruction,pc,phase,lr,ctr,cr,opcode,disassembly,store_address,r1,r3,r4,r5,r6,r7,r8,r9,r10,r13,r27,r28,r29,r30,r31,f0,f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11,f12,f13,f14,f15,f31,left_matrix_bytes,right_matrix_bytes,output_matrix_bytes,root_matrix_bytes,r1_bytes,r3_bytes,r4_bytes,r5_bytes,r6_bytes,r29_bytes");
        }

        using TextWriter? sonicSceneStateTrace = OpenTraceFile(options.SonicSceneStateTracePath);
        if (sonicSceneStateTrace is not null)
        {
            sonicSceneStateTrace.WriteLine("instruction,pc,lr,ctr,cr,packet,packet_kind,object,object_kind,stream0,stream1,vertex_base_pointer,vertex_base,matrix_base_pointer,matrix_limit_pointer,current_matrix_pointer,previous_matrix_pointer,resource_flag_address,resource_flag,state_base,state_byte13,state_byte47,state_word80,state_word_ec,small_data_state_address,small_data_window_address,small_data_state,first_mode_flag_address,first_mode_flag,second_mode_flag_address,second_mode_flag,mode_pointer_address,mode_pointer,mode_pointer_value,r1,r3,r4,r5,r6,r7,r8,r9,r10,r13,r27,r28,r29,r30,r31,packet_bytes,object_bytes,state_bytes,small_data_bytes,mode_pointer_bytes,current_matrix_bytes,previous_matrix_bytes");
        }

        using TextWriter? sonicPacketSelectionTrace = OpenTraceFile(options.SonicPacketSelectionTracePath);
        if (sonicPacketSelectionTrace is not null)
        {
            sonicPacketSelectionTrace.WriteLine("instruction,pc,phase,lr,ctr,cr,packet_source,packet,packet_kind,object,object_kind,stream0,stream1,packet_bound_x_word,packet_bound_y_word,packet_bound_z_word,packet_bound_radius_word,packet_bound_x,packet_bound_y,packet_bound_z,packet_bound_radius,object_x,object_y,object_z,object_w,vertex_base,r1,r3,r4,r5,r6,r7,r8,r9,r10,r13,r27,r28,r29,r30,r31,packet_bytes,object_bytes,r3_bytes,r27_bytes,r30_bytes,r31_bytes,stack_bytes");
        }

        using TextWriter? sonicTraversalSourceTrace = OpenTraceFile(options.SonicTraversalSourceTracePath);
        if (sonicTraversalSourceTrace is not null)
        {
            sonicTraversalSourceTrace.WriteLine("instruction,pc,phase,lr,ctr,cr,packet_source,packet,packet_kind,object,object_kind,stream0,stream1,object_x,object_y,object_z,packet_bound_x,packet_bound_y,packet_bound_z,packet_bound_radius,r1,r3,r4,r5,r6,r7,r8,r9,r10,r13,r27,r28,r29,r30,r31,caller_stack0,caller_stack4,caller_stack8,caller_stack12,r3_bytes,r4_bytes,r5_bytes,r27_bytes,r29_bytes,r30_bytes,r31_bytes,stack_bytes");
        }

        using TextWriter? sonicDrawPacketTrace = OpenTraceFile(options.SonicDrawPacketTracePath);
        if (sonicDrawPacketTrace is not null)
        {
            sonicDrawPacketTrace.WriteLine("instruction,pc,lr,packet,stream0,stream1,vertex_base_pointer,vertex_base,r3,r4,r5,r6,r7,r30,r31,packet_bytes,stream0_bytes,stream1_bytes,vertex_base_bytes");
        }

        using TextWriter? sonicGxEmitterTrace = OpenTraceFile(options.SonicGxEmitterTracePath);
        if (sonicGxEmitterTrace is not null)
        {
            sonicGxEmitterTrace.WriteLine("instruction,gx_fifo_offset,pc,lr,ctr,cr,r1,r3,r4,r5,r6,r7,r8,r9,r10,r13,r24,r25,r27,r28,r29,r30,r31,source_record,stream_cursor,vertex_base,source_x,source_y,source_z,source_color,source_x_float,source_y_float,source_z_float,f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f1_bits,f2_bits,f3_bits");
        }

        using TextWriter? sonicTextureBindTrace = OpenTraceFile(options.SonicTextureBindTracePath);
        if (sonicTextureBindTrace is not null)
        {
            sonicTextureBindTrace.WriteLine("instruction,gx_fifo_offset,pc,lr,texture_object,sampler_object,texture_map,state_block,object_flags,word0,word4,word8,sampler0,sampler4,word12,source_address,width,height,format,mode0,mode1,texture_object_bytes,sampler_object_bytes");
        }

        using TextWriter? sonicVertexProvenanceTrace = OpenTraceFile(options.SonicVertexProvenanceTracePath);
        if (sonicVertexProvenanceTrace is not null)
        {
            sonicVertexProvenanceTrace.WriteLine("instruction,gx_fifo_offset,pc,lr,packet,packet_kind,packet_stream0,packet_stream1,stream_record,stream_cursor,stream_offset,record_bytes,decoded_index,actual_index,index_matches,decoded_attr0,attr0_gpr_r29,attr0_matches,decoded_attr1,attr1_gpr_r28,attr1_matches,vertex_base,source_record,source_x,source_y,source_z,source_color,source_x_float,source_y_float,source_z_float,selected_x_bits,selected_y_bits,selected_z_bits,current_f1,current_f2,current_f3,current_f1_bits,current_f2_bits,current_f3_bits,gpr_r24,gpr_r25,gpr_r27,gpr_r28,gpr_r29,gpr_r30,gpr_r31,source_bytes");
        }

        using TextWriter? sonicTransformInputTrace = OpenTraceFile(options.SonicTransformInputTracePath);
        if (sonicTransformInputTrace is not null)
        {
            sonicTransformInputTrace.WriteLine("instruction,pc,lr,ctr,cr,r1,r3,r4,r5,r6,r7,r8,r9,r10,r13,output_cursor,input_cursor,iterations,gqr0,gqr1,gqr2,gqr3,gqr4,gqr5,gqr6,gqr7,f0,f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11,f12,f13,input_bytes,output_bytes");
        }

        bool cancelled = false;

        string GetStopReason(string? overrideReason = null)
        {
            if (overrideReason is not null)
            {
                return overrideReason;
            }

            if (cancelled)
            {
                return "cancelled";
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
        double gxCopyEventDumpMilliseconds = 0;
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
                    + gxCopyEventDumpMilliseconds
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
                        gxCopyEventDumpMs = RoundMilliseconds(gxCopyEventDumpMilliseconds),
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
                    pcProfileWithoutFastForwardLeaves = options.PcProfileTop is int filteredFastForwardPcProfileTop && pcProfile is not null
                        ? BuildPcProfileWithoutFastForwardLeavesSummary(pcProfile, filteredFastForwardPcProfileTop, executed, options.ProfileAfter.GetValueOrDefault(), bus)
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
                            lifecycle = gxFrameDump.Lifecycle is null ? null : new
                            {
                                requestedSource = FormatGxFrameSourceSlug(gxFrameDump.Lifecycle.RequestedSource),
                                selectedSource = FormatGxFrameSourceSlug(gxFrameDump.Lifecycle.SelectedSource),
                                capturedCurrentEfb = gxFrameDump.Lifecycle.CapturedCurrentEfb,
                                parsedDraws = gxFrameDump.Lifecycle.ParsedDraws,
                                copyEventsSeen = gxFrameDump.Lifecycle.CopyEventsSeen,
                                displayCopiesSeen = gxFrameDump.Lifecycle.DisplayCopiesSeen,
                                textureCopiesSeen = gxFrameDump.Lifecycle.TextureCopiesSeen,
                                selectedCopy = BuildGxCopyMarkerSummary(gxFrameDump.Lifecycle.SelectedCopy),
                                lastDisplayCopy = BuildGxCopyMarkerSummary(gxFrameDump.Lifecycle.LastDisplayCopy),
                                drawsSinceLastDisplayCopy = gxFrameDump.Lifecycle.DrawsSinceLastDisplayCopy,
                                copyEventsSinceLastDisplayCopy = gxFrameDump.Lifecycle.CopyEventsSinceLastDisplayCopy,
                                clearsSinceLastDisplayCopy = gxFrameDump.Lifecycle.ClearsSinceLastDisplayCopy,
                                textureCopiesSinceLastDisplayCopy = gxFrameDump.Lifecycle.TextureCopiesSinceLastDisplayCopy,
                                efbWasClearedAfterLastDisplayCopy = gxFrameDump.Lifecycle.EfbWasClearedAfterLastDisplayCopy,
                                phase = gxFrameDump.Lifecycle.Phase,
                            },
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
                        sonicStartCodeScanInstructions = sonicStartCodeScanFastForwardInstructions,
                        sonicInterruptStatusPrologueInstructions = sonicInterruptStatusPrologueFastForwardInstructions,
                        sonicInterruptStatusPollInstructions = sonicInterruptStatusPollFastForwardInstructions,
                        sonicInterruptStatusTimerSetupInstructions = sonicInterruptStatusTimerSetupFastForwardInstructions,
                        sonicInterruptStatusTimestampInstructions = sonicInterruptStatusTimestampFastForwardInstructions,
                        sonicInterruptStatusCompareInstructions = sonicInterruptStatusCompareFastForwardInstructions,
                        sonicInterruptStatusTailInstructions = sonicInterruptStatusTailFastForwardInstructions,
                        sonicInterruptStatusQueryPrologueInstructions = sonicInterruptStatusQueryPrologueFastForwardInstructions,
                        sonicInterruptStatusQueryPostCallInstructions = sonicInterruptStatusQueryPostCallFastForwardInstructions,
                        sonicDvdStatusWaitInstructions = sonicDvdStatusWaitFastForwardInstructions,
                        sonicInitTableLoopTailInstructions = sonicInitTableLoopTailFastForwardInstructions,
                        sonicInitTableNullEntryInstructions = sonicInitTableNullEntryFastForwardInstructions,
                        sonicRecordHeaderScanInstructions = sonicRecordHeaderScanFastForwardInstructions,
                        sonicFlagRecordScanInstructions = sonicFlagRecordScanFastForwardInstructions,
                        sonicTaskSlotCallbackScanInstructions = sonicTaskSlotCallbackScanFastForwardInstructions,
                        sonicBitmaskDispatchScanInstructions = sonicBitmaskDispatchScanFastForwardInstructions,
                        sonicResourceFlagWaitInstructions = sonicResourceFlagWaitFastForwardInstructions,
                        sonicResourceModeQueryInstructions = sonicResourceModeQueryFastForwardInstructions,
                        sonicResourceStatePollInstructions = sonicResourceStatePollFastForwardInstructions,
                        sonicModeChildStatusPollInstructions = sonicModeChildStatusPollFastForwardInstructions,
                        sonicModeStateUpdateInstructions = sonicModeStateUpdateFastForwardInstructions,
                        sonicModeCoordinatorPrologueInstructions = sonicModeCoordinatorPrologueFastForwardInstructions,
                        sonicModeCoordinatorBodyInstructions = sonicModeCoordinatorBodyFastForwardInstructions,
                        sonicModeCoordinatorZeroTailInstructions = sonicModeCoordinatorZeroTailFastForwardInstructions,
                        sonicModeQueryInstructions = sonicModeQueryFastForwardInstructions,
                        sonicStatusQueryInstructions = sonicStatusQueryFastForwardInstructions,
                        sonicStatusCallerLoopInstructions = sonicStatusCallerLoopFastForwardInstructions,
                        sonicStatusCallerDispatchInstructions = sonicStatusCallerDispatchFastForwardInstructions,
                        sonicTableByteBuildDispatchInstructions = sonicTableByteBuildDispatchFastForwardInstructions,
                        sonicLineCopyInstructions = sonicLineCopyFastForwardInstructions,
                        sonicLineSkipInstructions = sonicLineSkipFastForwardInstructions,
                        sonicStringAppendScanInstructions = sonicStringAppendScanFastForwardInstructions,
                        sonicFreeBlockScanInstructions = sonicFreeBlockScanFastForwardInstructions,
                        sonicCacheStoreSweepInstructions = sonicCacheStoreSweepFastForwardInstructions,
                        sonicStateZeroFillInstructions = sonicStateZeroFillFastForwardInstructions,
                        sonicManagerSlotScanInstructions = sonicManagerSlotScanFastForwardInstructions,
                        sonicTaskEntryScanInstructions = sonicTaskEntryScanFastForwardInstructions,
                        sonicObjectSlotScanInstructions = sonicObjectSlotScanFastForwardInstructions,
                        sonicHalfwordChecksumInstructions = sonicHalfwordChecksumFastForwardInstructions,
                        sonicNullSlotScanInstructions = sonicNullSlotScanFastForwardInstructions,
                        sonicPoolSlotScanInstructions = sonicPoolSlotScanFastForwardInstructions,
                        sonicTableKeyScanInstructions = sonicTableKeyScanFastForwardInstructions,
                        sonicModeRefreshDispatchInstructions = sonicModeRefreshDispatchFastForwardInstructions,
                        sonicModeWrapperInstructions = sonicModeWrapperFastForwardInstructions,
                        sonicResourceFixupInstructions = sonicResourceFixupFastForwardInstructions,
                        sonicOverlayInactiveSlotScanInstructions = sonicOverlayInactiveSlotScanFastForwardInstructions,
                        sonicPathLookupInstructions = sonicPathLookupFastForwardInstructions,
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
                        sonicGxCpStateSetterInstructions = sonicGxCpStateSetterFastForwardInstructions,
                        sonicGxTevColorEnvSetterInstructions = sonicGxTevColorEnvSetterFastForwardInstructions,
                        sonicGxTevAlphaEnvSetterInstructions = sonicGxTevAlphaEnvSetterFastForwardInstructions,
                        sonicGxTevColorOpSetterInstructions = sonicGxTevColorOpSetterFastForwardInstructions,
                        sonicGxTevAlphaOpSetterInstructions = sonicGxTevAlphaOpSetterFastForwardInstructions,
                        sonicGxTevDefaultWrapperInstructions = sonicGxTevDefaultWrapperFastForwardInstructions,
                        sonicGxVertexDescriptorSetterInstructions = sonicGxVertexDescriptorSetterFastForwardInstructions,
                        sonicGxVertexAttributeFlushInstructions = sonicGxVertexAttributeFlushFastForwardInstructions,
                        sonicGxIndexedStripBatchInstructions = sonicGxIndexedStripBatchFastForwardInstructions,
                        sonicGxIndexedStripDrawBeginInstructions = sonicGxIndexedStripDrawBeginFastForwardInstructions,
                        sonicGxIndexedStripTailInstructions = sonicGxIndexedStripTailFastForwardInstructions,
                        sonicGxIndexedStripEpilogueInstructions = sonicGxIndexedStripEpilogueFastForwardInstructions,
                        sonicGxFloatTexcoordStripEmitInstructions = sonicGxFloatTexcoordStripEmitFastForwardInstructions,
                        sonicPairedTransform4dInstructions = sonicPairedTransform4dFastForwardInstructions,
                        sonicVectorBlendCopyInstructions = sonicVectorBlendCopyFastForwardInstructions,
                        sonicCoordinatePairFillInstructions = sonicCoordinatePairFillFastForwardInstructions,
                        sonicBufferFillInstructions = sonicBufferFillFastForwardInstructions,
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
        bool allowSonicGxFastForward = !options.DisableSonicGxFastForward && options.SonicVertexProvenanceTracePath is null;
        bool allowSonicGeometryFastForward = !options.DisableSonicGeometryFastForward;
        bool allowSonicResourceLookupFastForward = options.EnableSonicResourceLookupFastForward && !options.DisableSonicResourceFastForward && !options.DisableSonicResourceLookupFastForward;
        bool allowSonicResourceModeQueryFastForward = options.EnableSonicResourceModeQueryFastForward && !options.DisableSonicResourceFastForward && !options.DisableSonicResourceModeQueryFastForward;
        bool allowSonicResourceStatePollFastForward = options.EnableSonicResourceStatePollFastForward && !options.DisableSonicResourceFastForward && !options.DisableSonicResourceStatePollFastForward;
        bool allowSonicResourceFixupFastForward = !options.DisableSonicResourceFastForward && !options.DisableSonicResourceFixupFastForward;

        try
        {
            for (; executed < options.MaxInstructions && !state.Halted; executed++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

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
                if (options.DumpMemoryBinaryRequests is { Count: > 0 })
                {
                    Stopwatch timedMemoryDumpStopwatch = Stopwatch.StartNew();
                    WriteRequestedMemoryBinaryDumpsAtInstruction(options, bus, _output, executed + 1);
                    memoryDumpMilliseconds += StopAndGetMilliseconds(timedMemoryDumpStopwatch);
                }

                pendingWriteWatchOldValueValid = false;
                if (HasWriteWatch(options)
                    && executed + 1 >= options.WatchWriteAfter.GetValueOrDefault()
                    && TryGetStoreEffectiveAddress(state, currentInstruction, out uint storeAddress, out int storeWidth)
                    && TryReadStoreValue(bus.Memory, storeAddress, storeWidth, out uint oldStoreValue))
                {
                    pendingWriteWatchOldAddress = storeAddress;
                    pendingWriteWatchOldWidth = storeWidth;
                    pendingWriteWatchOldValue = oldStoreValue;
                    pendingWriteWatchOldValueValid = true;
                }

                if (sonicResourceFlagTrace is not null
                    && executed >= options.TracePcAfter.GetValueOrDefault()
                    && TryGetSonicResourceFlagTraceEvent(state, bus, pc, out SonicResourceFlagTraceEvent resourceFlagEvent))
                {
                    sonicResourceFlagTrace.WriteLine($"{executed + 1},0x{pc:X8},0x{state.Lr:X8},{resourceFlagEvent.Operation},0x{resourceFlagEvent.FlagAddress:X8},0x{resourceFlagEvent.OldFlag:X8},0x{resourceFlagEvent.NewFlag:X8},0x{resourceFlagEvent.ChangedBits:X8},0x{resourceFlagEvent.Mask:X8},0x{resourceFlagEvent.Task:X8},{resourceFlagEvent.TaskSlot},0x{resourceFlagEvent.Selector:X8},0x{resourceFlagEvent.QueueHead:X8},0x{resourceFlagEvent.QueueTail:X8},0x{state.Gpr[1]:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8}");
                }

                if (sonicDrawPacketTrace is not null
                    && pc == 0x8011_D414
                    && executed >= options.TracePcAfter.GetValueOrDefault()
                    && (options.WatchLimit is null || emittedSonicDrawPacketTraceChanges < options.WatchLimit))
                {
                    const int packetWindowBytes = 0x40;
                    const int minStreamWindowBytes = 0x60;
                    const int maxStream0WindowBytes = 0x800;
                    const int stream1WindowBytes = 0x200;
                    const int vertexWindowBytes = 0x80;
                    uint packet = state.Gpr[3];
                    uint stream0 = ReadMainRamWordOrZero(bus.Memory, packet);
                    uint stream1 = ReadMainRamWordOrZero(bus.Memory, packet + sizeof(uint));
                    uint vertexBasePointer = unchecked(state.Gpr[13] - 29028u);
                    uint vertexBase = ReadMainRamWordOrZero(bus.Memory, vertexBasePointer);
                    int stream0WindowBytes = Math.Clamp(ReadMainRamHalfWordOrZero(bus.Memory, stream0) + 0x20, minStreamWindowBytes, maxStream0WindowBytes);
                    sonicDrawPacketTrace.WriteLine($"{executed + 1},0x{pc:X8},0x{state.Lr:X8},0x{packet:X8},0x{stream0:X8},0x{stream1:X8},0x{vertexBasePointer:X8},0x{vertexBase:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},\"{CaptureMemoryWindowHex(bus.Memory, packet, packetWindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, stream0, stream0WindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, stream1, stream1WindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, vertexBase, vertexWindowBytes)}\"");
                    emittedSonicDrawPacketTraceChanges++;
                }

                if (sonicSceneStateTrace is not null
                    && pc == 0x8011_D414
                    && executed >= options.TracePcAfter.GetValueOrDefault()
                    && (options.WatchLimit is null || emittedSonicSceneStateTraceChanges < options.WatchLimit))
                {
                    WriteSonicSceneStateTraceRow(sonicSceneStateTrace, executed + 1, pc, state, bus);
                    emittedSonicSceneStateTraceChanges++;
                }

                if (sonicPacketSelectionTrace is not null
                    && IsSonicPacketSelectionTracePc(pc)
                    && executed >= options.TracePcAfter.GetValueOrDefault()
                    && (options.WatchLimit is null || emittedSonicPacketSelectionTraceChanges < options.WatchLimit))
                {
                    WriteSonicPacketSelectionTraceRow(sonicPacketSelectionTrace, executed + 1, pc, state, bus.Memory);
                    emittedSonicPacketSelectionTraceChanges++;
                }

                if (sonicTraversalSourceTrace is not null
                    && IsSonicTraversalSourceTracePc(pc)
                    && executed >= options.TracePcAfter.GetValueOrDefault()
                    && (options.WatchLimit is null || emittedSonicTraversalSourceTraceChanges < options.WatchLimit))
                {
                    WriteSonicTraversalSourceTraceRow(sonicTraversalSourceTrace, executed + 1, pc, state, bus.Memory);
                    emittedSonicTraversalSourceTraceChanges++;
                }

                if (sonicMatrixStackTrace is not null
                    && IsSonicMatrixStackTracePc(pc)
                    && executed >= options.TracePcAfter.GetValueOrDefault()
                    && (options.WatchLimit is null || emittedSonicMatrixStackTraceChanges < options.WatchLimit))
                {
                    const int mainRamWindowBytes = 0x80;
                    const int matrixWindowBytes = 0x30;
                    uint matrixBasePointer = ReadMainRamWordOrZero(bus.Memory, unchecked(state.Gpr[13] - 29184u));
                    uint matrixLimitPointer = ReadMainRamWordOrZero(bus.Memory, unchecked(state.Gpr[13] - 29188u));
                    uint currentMatrixPointer = ReadMainRamWordOrZero(bus.Memory, unchecked(state.Gpr[13] - 29180u));
                    uint previousMatrixPointer = currentMatrixPointer >= matrixBasePointer + matrixWindowBytes
                        ? currentMatrixPointer - matrixWindowBytes
                        : 0u;
                    string previousMatrixBytes = previousMatrixPointer == 0
                        ? string.Empty
                        : CaptureBusWindowHex(bus, previousMatrixPointer, matrixWindowBytes);
                    sonicMatrixStackTrace.WriteLine($"{executed + 1},0x{pc:X8},0x{state.Lr:X8},0x{state.Ctr:X8},0x{state.Cr:X8},0x{matrixBasePointer:X8},0x{matrixLimitPointer:X8},0x{currentMatrixPointer:X8},0x{previousMatrixPointer:X8},0x{state.Gpr[1]:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[13]:X8},0x{state.Gpr[27]:X8},0x{state.Gpr[28]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},\"{CaptureMemoryWindowHex(bus.Memory, state.Gpr[3], mainRamWindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, state.Gpr[4], mainRamWindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, state.Gpr[5], mainRamWindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, state.Gpr[6], mainRamWindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, state.Gpr[27], mainRamWindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, state.Gpr[30], mainRamWindowBytes)}\",\"{CaptureBusWindowHex(bus, matrixBasePointer, matrixWindowBytes)}\",\"{previousMatrixBytes}\",\"{CaptureBusWindowHex(bus, currentMatrixPointer, matrixWindowBytes)}\"");
                    emittedSonicMatrixStackTraceChanges++;
                }

                if (sonicMatrixWriterTrace is not null
                    && IsSonicMatrixWriterTracePc(pc)
                    && executed >= options.TracePcAfter.GetValueOrDefault()
                    && (options.WatchLimit is null || emittedSonicMatrixWriterTraceChanges < options.WatchLimit))
                {
                    WriteSonicMatrixWriterTraceRow(sonicMatrixWriterTrace, executed + 1, pc, currentInstruction, state, bus);
                    emittedSonicMatrixWriterTraceChanges++;
                }

                if (sonicRootMatrixTrace is not null
                    && IsSonicRootMatrixTracePc(pc)
                    && executed >= options.TracePcAfter.GetValueOrDefault()
                    && (options.WatchLimit is null || emittedSonicRootMatrixTraceChanges < options.WatchLimit))
                {
                    WriteSonicRootMatrixTraceRow(sonicRootMatrixTrace, executed + 1, pc, currentInstruction, state, bus);
                    emittedSonicRootMatrixTraceChanges++;
                }

                if (sonicGxEmitterTrace is not null
                    && IsSonicGxEmitterTracePc(pc)
                    && executed >= options.TracePcAfter.GetValueOrDefault()
                    && (options.WatchLimit is null || emittedSonicGxEmitterTraceChanges < options.WatchLimit))
                {
                    uint sourceRecord = state.Gpr[27];
                    uint sourceX = ReadMainRamWordOrZero(bus.Memory, sourceRecord);
                    uint sourceY = ReadMainRamWordOrZero(bus.Memory, sourceRecord + sizeof(uint));
                    uint sourceZ = ReadMainRamWordOrZero(bus.Memory, sourceRecord + (sizeof(uint) * 2));
                    uint sourceColor = ReadMainRamWordOrZero(bus.Memory, sourceRecord + (sizeof(uint) * 6));
                    sonicGxEmitterTrace.WriteLine($"{executed + 1},0x{gxFifoBytesWritten:X},0x{pc:X8},0x{state.Lr:X8},0x{state.Ctr:X8},0x{state.Cr:X8},0x{state.Gpr[1]:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[8]:X8},0x{state.Gpr[9]:X8},0x{state.Gpr[10]:X8},0x{state.Gpr[13]:X8},0x{state.Gpr[24]:X8},0x{state.Gpr[25]:X8},0x{state.Gpr[27]:X8},0x{state.Gpr[28]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},0x{sourceRecord:X8},0x{state.Gpr[24]:X8},0x{state.Gpr[25]:X8},0x{sourceX:X8},0x{sourceY:X8},0x{sourceZ:X8},0x{sourceColor:X8},{FormatSingleBitsAsFloat(sourceX)},{FormatSingleBitsAsFloat(sourceY)},{FormatSingleBitsAsFloat(sourceZ)},{FormatDouble(state.Fpr[1])},{FormatDouble(state.Fpr[2])},{FormatDouble(state.Fpr[3])},{FormatDouble(state.Fpr[4])},{FormatDouble(state.Fpr[5])},{FormatDouble(state.Fpr[6])},{FormatDouble(state.Fpr[7])},{FormatDouble(state.Fpr[8])},{FormatDouble(state.Fpr[9])},{FormatDouble(state.Fpr[10])},{FormatSingleBits(state.Fpr[1])},{FormatSingleBits(state.Fpr[2])},{FormatSingleBits(state.Fpr[3])}");
                    emittedSonicGxEmitterTraceChanges++;
                }

                if (sonicVertexProvenanceTrace is not null
                    && IsSonicVertexProvenanceTracePc(pc)
                    && executed >= options.TracePcAfter.GetValueOrDefault()
                    && gxFifoBytesWritten >= options.SonicVertexProvenanceTraceStart.GetValueOrDefault()
                    && gxFifoBytesWritten < (long)options.SonicVertexProvenanceTraceStart.GetValueOrDefault() + options.SonicVertexProvenanceTraceLength.GetValueOrDefault()
                    && (options.WatchLimit is null || emittedSonicVertexProvenanceTraceChanges < options.WatchLimit))
                {
                    WriteSonicVertexProvenanceTraceRow(sonicVertexProvenanceTrace, executed + 1, gxFifoBytesWritten, pc, state, bus.Memory);
                    emittedSonicVertexProvenanceTraceChanges++;
                }

                if (sonicTransformInputTrace is not null
                    && IsSonicTransformInputTracePc(pc)
                    && executed >= options.TracePcAfter.GetValueOrDefault()
                    && (options.WatchLimit is null || emittedSonicTransformInputTraceChanges < options.WatchLimit))
                {
                    uint inputCursor = pc == SonicPairedTransform4dIndexedLoopPc ? state.Gpr[7] : state.Gpr[6];
                    uint outputCursor = pc == SonicPairedTransform4dIndexedLoopPc ? state.Gpr[6] : state.Gpr[5];
                    ulong outputSpanBytes = options.DisableSonicPairedTransformFastForward
                        ? 0x20UL
                        : EstimateSonicTransformOutputSpanBytes(state.Ctr);
                    int inputWindowBytes = EstimateSonicTransformInputWindowBytes(pc, state.Ctr);
                    int outputWindowBytes = checked((int)Math.Min(outputSpanBytes + 0x20UL, 0x2000UL));
                    if (options.SonicTransformOutputRangeAddress is null
                        || OverlapsAddressRange(outputCursor, outputSpanBytes, options.SonicTransformOutputRangeAddress.Value, options.SonicTransformOutputRangeLength.GetValueOrDefault()))
                    {
                        sonicTransformInputTrace.WriteLine($"{executed + 1},0x{pc:X8},0x{state.Lr:X8},0x{state.Ctr:X8},0x{state.Cr:X8},0x{state.Gpr[1]:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[8]:X8},0x{state.Gpr[9]:X8},0x{state.Gpr[10]:X8},0x{state.Gpr[13]:X8},0x{outputCursor:X8},0x{inputCursor:X8},0x{state.Ctr:X8},0x{state.Spr[912]:X8},0x{state.Spr[913]:X8},0x{state.Spr[914]:X8},0x{state.Spr[915]:X8},0x{state.Spr[916]:X8},0x{state.Spr[917]:X8},0x{state.Spr[918]:X8},0x{state.Spr[919]:X8},{FormatFprPair(state, 0)},{FormatFprPair(state, 1)},{FormatFprPair(state, 2)},{FormatFprPair(state, 3)},{FormatFprPair(state, 4)},{FormatFprPair(state, 5)},{FormatFprPair(state, 6)},{FormatFprPair(state, 7)},{FormatFprPair(state, 8)},{FormatFprPair(state, 9)},{FormatFprPair(state, 10)},{FormatFprPair(state, 11)},{FormatFprPair(state, 12)},{FormatFprPair(state, 13)},\"{CaptureMemoryWindowHex(bus.Memory, inputCursor, inputWindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, outputCursor, outputWindowBytes)}\"");
                        emittedSonicTransformInputTraceChanges++;
                    }
                }

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

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardMemsetRoutine(state, bus, out skippedInstructions))
                {
                    bulkFastForwardInstructions += (uint)skippedInstructions;
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

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardAsciiCaseInsensitiveStringCompareLoop(state, bus, out skippedInstructions))
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
                Action<SonicPrsDecompressTraceEvent>? sonicPrsDecompressTrace = null;
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

                if (sonicBitstreamDecoderTrace is not null)
                {
                    sonicPrsDecompressTrace = traceEvent =>
                    {
                        if (executed < options.TracePcAfter.GetValueOrDefault())
                        {
                            return;
                        }

                        if (options.WatchLimit is not null && emittedSonicBitstreamDecoderTraceChanges >= options.WatchLimit)
                        {
                            if (!sonicBitstreamDecoderTraceLimitNoticeEmitted)
                            {
                                _output.WriteLine($"Sonic bitstream decoder trace limit of {options.WatchLimit} reached; suppressing further rows.");
                                sonicBitstreamDecoderTraceLimitNoticeEmitted = true;
                            }

                            return;
                        }

                        WriteSonicPrsDecompressTraceRow(sonicBitstreamDecoderTrace, executed + 1, state, traceEvent, options.SonicBitstreamDecoderTraceAddress.GetValueOrDefault(), options.SonicBitstreamDecoderTraceLength.GetValueOrDefault());
                        emittedSonicBitstreamDecoderTraceChanges++;
                    };
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicPrsDecompressCore(state, bus, out skippedInstructions, tracePrsDecompress, sonicPrsDecompressTrace, options.SonicBitstreamDecoderTraceAddress, options.SonicBitstreamDecoderTraceLength))
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

                if (options.FastForwardIdle && !options.DisableSonicBitUnpackFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicBitUnpackRows(state, bus, out skippedInstructions))
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

                if (options.FastForwardIdle && allowSonicResourceLookupFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicResourceTableLookup(state, bus, out skippedInstructions))
                {
                    resourceLookupFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxAttributeFlush(state, bus, out skippedInstructions))
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

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxVertexEmitLoop(state, bus, out skippedInstructions))
                {
                    gxVertexEmitFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxTexObjLoadNoCallback(state, bus, sonicTextureBindTrace, executed + 1, gxFifoBytesWritten, out skippedInstructions))
                {
                    sonicGxTexObjLoadNoCallbackFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxPackedStateSetter(state, bus, out skippedInstructions))
                {
                    sonicGxPackedStateSetterFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxCpStateSetter(state, bus, out skippedInstructions))
                {
                    sonicGxCpStateSetterFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxTevDefaultWrapper(state, bus, out skippedInstructions))
                {
                    sonicGxTevDefaultWrapperFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxTevColorEnvSetter(state, bus, out skippedInstructions))
                {
                    sonicGxTevColorEnvSetterFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxTevAlphaEnvSetter(state, bus, out skippedInstructions))
                {
                    sonicGxTevAlphaEnvSetterFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxTevColorOpSetter(state, bus, out skippedInstructions))
                {
                    sonicGxTevColorOpSetterFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxTevAlphaOpSetter(state, bus, out skippedInstructions))
                {
                    sonicGxTevAlphaOpSetterFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxVertexDescriptorSetter(state, bus, out skippedInstructions))
                {
                    sonicGxVertexDescriptorSetterFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxVertexAttributeFlush(state, bus, out skippedInstructions))
                {
                    sonicGxVertexAttributeFlushFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxIndexedStripBatch(state, bus, out skippedInstructions))
                {
                    sonicGxIndexedStripBatchFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxIndexedStripDrawBegin(state, bus, out skippedInstructions))
                {
                    sonicGxIndexedStripDrawBeginFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxIndexedStripTail(state, bus, out skippedInstructions))
                {
                    sonicGxIndexedStripTailFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxIndexedStripEpilogue(state, bus, out skippedInstructions))
                {
                    sonicGxIndexedStripEpilogueFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxDrawBegin(state, bus, out skippedInstructions))
                {
                    sonicGxDrawBeginFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxBeginDirect(state, bus, out skippedInstructions))
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

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicStartCodeScan(state, bus, out skippedInstructions))
                {
                    sonicStartCodeScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicPathRecordScan(state, bus, out skippedInstructions))
                {
                    sonicPathRecordScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicPathLookup(state, bus, out skippedInstructions))
                {
                    sonicPathLookupFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGeometryFastForward && !options.DisableSonicPairedTransformFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicPairedTransform2d(state, bus, out skippedInstructions))
                {
                    sonicPairedTransform2dFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGeometryFastForward && !options.DisableSonicPairedTransformFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicPairedTransform4d(state, bus, out skippedInstructions))
                {
                    sonicPairedTransform4dFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGeometryFastForward && !options.DisableSonicPairedTransformFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicPairedTransform4dIndexedOutput(state, bus, out skippedInstructions))
                {
                    sonicPairedTransform4dFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGeometryFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicVectorBlendCopyLoop(state, bus, out skippedInstructions))
                {
                    sonicVectorBlendCopyFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGeometryFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicCoordinatePairFillLoop(state, bus, out skippedInstructions))
                {
                    sonicCoordinatePairFillFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGeometryFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicBufferFillLoop(state, bus, out skippedInstructions))
                {
                    sonicBufferFillFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGeometryFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGeneratedModelPointerScan(state, bus, out skippedInstructions))
                {
                    sonicGeneratedModelPointerScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGeometryFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGeneratedRangeScan(state, bus, out skippedInstructions))
                {
                    sonicGeneratedRangeScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGeometryFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGeneratedSlotMismatchScan(state, bus, out skippedInstructions))
                {
                    sonicGeneratedRangeScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxFloatStripEmitLoop(state, bus, out skippedInstructions))
                {
                    sonicGxFloatStripEmitFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxFloatAttributeStripEmitLoop(state, bus, out skippedInstructions))
                {
                    sonicGxFloatAttributeStripEmitFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxCommandListTerminal(state, bus, out skippedInstructions))
                {
                    sonicGxCommandListTerminalFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxCommandListFetch(state, bus, out skippedInstructions))
                {
                    sonicGxCommandListFetchFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxCommandDispatch(state, bus, out skippedInstructions))
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

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxAttributeStateSetter(state, bus, out skippedInstructions))
                {
                    sonicGxAttributeStateSetterFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicGxFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicGxFloatTexcoordStripEmitLoop(state, bus, out skippedInstructions))
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

                if (options.FastForwardIdle && allowSonicResourceModeQueryFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicResourceModeQuery(state, bus, out skippedInstructions))
                {
                    sonicResourceModeQueryFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicModeChildStatusPoll(state, bus, out skippedInstructions))
                {
                    sonicModeChildStatusPollFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicModeStateUpdate(state, bus, out skippedInstructions))
                {
                    sonicModeStateUpdateFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicModeCoordinatorPrologue(state, bus, out skippedInstructions))
                {
                    sonicModeCoordinatorPrologueFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicModeCoordinatorBody(state, bus, out skippedInstructions))
                {
                    sonicModeCoordinatorBodyFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicModeCoordinatorZeroTail(state, bus, out skippedInstructions))
                {
                    sonicModeCoordinatorZeroTailFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicStatusCallerDispatch(state, bus, out skippedInstructions))
                {
                    sonicStatusCallerDispatchFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicTableByteBuildDispatch(state, bus, out skippedInstructions))
                {
                    sonicTableByteBuildDispatchFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicLineCopy(state, bus, out skippedInstructions))
                {
                    sonicLineCopyFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicLineSkip(state, bus, out skippedInstructions))
                {
                    sonicLineSkipFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicStringAppendScan(state, bus, out skippedInstructions))
                {
                    sonicStringAppendScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicFreeBlockScan(state, bus, out skippedInstructions))
                {
                    sonicFreeBlockScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicCacheStoreSweep(state, bus, out skippedInstructions))
                {
                    sonicCacheStoreSweepFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicStateZeroFill(state, bus, out skippedInstructions))
                {
                    sonicStateZeroFillFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicManagerSlotScan(state, bus, out skippedInstructions))
                {
                    sonicManagerSlotScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicTaskEntryScan(state, bus, out skippedInstructions))
                {
                    sonicTaskEntryScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicObjectSlotScan(state, bus, out skippedInstructions))
                {
                    sonicObjectSlotScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicHalfwordChecksumLoop(state, bus, out skippedInstructions))
                {
                    sonicHalfwordChecksumFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicStatusQuery(state, bus, out skippedInstructions))
                {
                    sonicStatusQueryFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicNullSlotScanLoop(state, bus, out skippedInstructions))
                {
                    sonicNullSlotScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicPoolSlotScanLoop(state, bus, out skippedInstructions))
                {
                    sonicPoolSlotScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicTableKeyScanLoop(state, bus, out skippedInstructions))
                {
                    sonicTableKeyScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicModeRefreshDispatch(state, bus, out skippedInstructions))
                {
                    sonicModeRefreshDispatchFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicModeQuery(state, bus, out skippedInstructions))
                {
                    sonicModeQueryFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicInterruptStatusPrologue(state, bus, out skippedInstructions))
                {
                    sonicInterruptStatusPrologueFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicInterruptStatusPoll(state, bus, out skippedInstructions))
                {
                    sonicInterruptStatusPollFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicInterruptStatusTimerSetup(state, bus, out skippedInstructions))
                {
                    sonicInterruptStatusTimerSetupFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicInterruptStatusTimestamp(state, bus, out skippedInstructions))
                {
                    sonicInterruptStatusTimestampFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicInterruptStatusCompare(state, bus, out skippedInstructions))
                {
                    sonicInterruptStatusCompareFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicInterruptStatusTail(state, bus, out skippedInstructions))
                {
                    sonicInterruptStatusTailFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicInterruptStatusQueryPrologue(state, bus, out skippedInstructions))
                {
                    sonicInterruptStatusQueryPrologueFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicInterruptStatusQueryPostCall(state, bus, out skippedInstructions))
                {
                    sonicInterruptStatusQueryPostCallFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicDvdStatusWaitLoop(state, bus, out skippedInstructions))
                {
                    sonicDvdStatusWaitFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicInitTableLoopTail(state, bus, out skippedInstructions))
                {
                    sonicInitTableLoopTailFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicInitTableNullEntryLoop(state, bus, out skippedInstructions))
                {
                    sonicInitTableNullEntryFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicRecordHeaderScanLoop(state, bus, out skippedInstructions))
                {
                    sonicRecordHeaderScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicFlagRecordScanLoop(state, bus, out skippedInstructions))
                {
                    sonicFlagRecordScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicTaskSlotCallbackScanLoop(state, bus, out skippedInstructions))
                {
                    sonicTaskSlotCallbackScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicBitmaskDispatchScanLoop(state, bus, out skippedInstructions))
                {
                    sonicBitmaskDispatchScanFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicResourceFlagWaitLoop(state, bus, out skippedInstructions))
                {
                    sonicResourceFlagWaitFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && canFastForwardWithWriteWatch && TryFastForwardSonicModeWrapper(state, bus, out skippedInstructions))
                {
                    sonicModeWrapperFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicResourceStatePollFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicResourceStatePoll(state, bus, out skippedInstructions))
                {
                    sonicResourceStatePollFastForwardInstructions += (uint)skippedInstructions;
                    stepObserver?.Invoke(new DolRunStep(executed + 1, state.Pc, currentInstruction, state, bus));
                    continue;
                }

                if (options.FastForwardIdle && allowSonicResourceFixupFastForward && canFastForwardWithWriteWatch && TryFastForwardSonicResourceFixupRecord(state, bus, out skippedInstructions))
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
                    && TryGetIndirectBranchTarget(currentInstruction, state, out uint profiledIndirectTarget, out _, out _))
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
            bus.LockedCacheWriteObserver = previousLockedCacheWriteObserver;
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
            WriteRequestedMemoryBinaryDumps(options, bus, _error);
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
            bus.LockedCacheWriteObserver = previousLockedCacheWriteObserver;
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
            WriteRequestedMemoryBinaryDumps(options, bus, _error);
            memoryDumpMilliseconds += StopAndGetMilliseconds(memoryDumpStopwatch);

            WriteRunSummary(4, stopReasonOverride: "address-translation", exceptionType: nameof(AddressTranslationException), exceptionAddress: ex.Address);
            return 4;
        }

        StopEmulationTimer();
        bus.MainRamWrite32Observer = previousWriteObserver;
        bus.MainRamWriteObserver = previousWriteAnyObserver;
        bus.LockedCacheWriteObserver = previousLockedCacheWriteObserver;
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

            if (sonicStartCodeScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicStartCodeScanFastForwardInstructions} Sonic start-code scan instruction(s).");
            }

            if (sonicInterruptStatusPrologueFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicInterruptStatusPrologueFastForwardInstructions} Sonic interrupt status prologue instruction(s).");
            }

            if (sonicInterruptStatusPollFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicInterruptStatusPollFastForwardInstructions} Sonic interrupt status poll instruction(s).");
            }

            if (sonicInterruptStatusTimerSetupFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicInterruptStatusTimerSetupFastForwardInstructions} Sonic interrupt status timer setup instruction(s).");
            }

            if (sonicInterruptStatusTimestampFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicInterruptStatusTimestampFastForwardInstructions} Sonic interrupt status timestamp instruction(s).");
            }

            if (sonicInterruptStatusCompareFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicInterruptStatusCompareFastForwardInstructions} Sonic interrupt status compare instruction(s).");
            }

            if (sonicInterruptStatusTailFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicInterruptStatusTailFastForwardInstructions} Sonic interrupt status tail instruction(s).");
            }

            if (sonicInterruptStatusQueryPrologueFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicInterruptStatusQueryPrologueFastForwardInstructions} Sonic interrupt status query prologue instruction(s).");
            }

            if (sonicInterruptStatusQueryPostCallFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicInterruptStatusQueryPostCallFastForwardInstructions} Sonic interrupt status query post-call instruction(s).");
            }

            if (sonicDvdStatusWaitFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicDvdStatusWaitFastForwardInstructions} Sonic DVD status wait instruction(s).");
            }

            if (sonicInitTableLoopTailFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicInitTableLoopTailFastForwardInstructions} Sonic init table loop tail instruction(s).");
            }

            if (sonicInitTableNullEntryFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicInitTableNullEntryFastForwardInstructions} Sonic init table null-entry instruction(s).");
            }

            if (sonicRecordHeaderScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicRecordHeaderScanFastForwardInstructions} Sonic record header scan instruction(s).");
            }

            if (sonicFlagRecordScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicFlagRecordScanFastForwardInstructions} Sonic flag record scan instruction(s).");
            }

            if (sonicTaskSlotCallbackScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicTaskSlotCallbackScanFastForwardInstructions} Sonic task slot callback scan instruction(s).");
            }

            if (sonicBitmaskDispatchScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicBitmaskDispatchScanFastForwardInstructions} Sonic bitmask dispatch scan instruction(s).");
            }

            if (sonicResourceFlagWaitFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicResourceFlagWaitFastForwardInstructions} Sonic resource flag wait instruction(s).");
            }

            if (sonicResourceModeQueryFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicResourceModeQueryFastForwardInstructions} Sonic resource mode query instruction(s).");
            }

            if (sonicResourceStatePollFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicResourceStatePollFastForwardInstructions} Sonic resource state poll instruction(s).");
            }

            if (sonicModeChildStatusPollFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicModeChildStatusPollFastForwardInstructions} Sonic mode child status poll instruction(s).");
            }

            if (sonicModeStateUpdateFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicModeStateUpdateFastForwardInstructions} Sonic mode state update instruction(s).");
            }

            if (sonicModeCoordinatorPrologueFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicModeCoordinatorPrologueFastForwardInstructions} Sonic mode coordinator prologue instruction(s).");
            }

            if (sonicModeCoordinatorBodyFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicModeCoordinatorBodyFastForwardInstructions} Sonic mode coordinator body instruction(s).");
            }

            if (sonicModeCoordinatorZeroTailFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicModeCoordinatorZeroTailFastForwardInstructions} Sonic mode coordinator zero-tail instruction(s).");
            }

            if (sonicModeQueryFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicModeQueryFastForwardInstructions} Sonic mode query instruction(s).");
            }

            if (sonicStatusQueryFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicStatusQueryFastForwardInstructions} Sonic status query instruction(s).");
            }

            if (sonicStatusCallerLoopFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicStatusCallerLoopFastForwardInstructions} Sonic status caller-loop instruction(s).");
            }

            if (sonicStatusCallerDispatchFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicStatusCallerDispatchFastForwardInstructions} Sonic status caller dispatch instruction(s).");
            }

            if (sonicTableByteBuildDispatchFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicTableByteBuildDispatchFastForwardInstructions} Sonic table byte-build dispatch instruction(s).");
            }

            if (sonicLineCopyFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicLineCopyFastForwardInstructions} Sonic line-copy instruction(s).");
            }

            if (sonicLineSkipFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicLineSkipFastForwardInstructions} Sonic line-skip instruction(s).");
            }

            if (sonicStringAppendScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicStringAppendScanFastForwardInstructions} Sonic string-append scan instruction(s).");
            }

            if (sonicFreeBlockScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicFreeBlockScanFastForwardInstructions} Sonic free-block scan instruction(s).");
            }

            if (sonicCacheStoreSweepFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicCacheStoreSweepFastForwardInstructions} Sonic cache store sweep instruction(s).");
            }

            if (sonicStateZeroFillFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicStateZeroFillFastForwardInstructions} Sonic state zero-fill instruction(s).");
            }

            if (sonicManagerSlotScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicManagerSlotScanFastForwardInstructions} Sonic manager-slot scan instruction(s).");
            }

            if (sonicTaskEntryScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicTaskEntryScanFastForwardInstructions} Sonic task-entry scan instruction(s).");
            }

            if (sonicObjectSlotScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicObjectSlotScanFastForwardInstructions} Sonic object-slot scan instruction(s).");
            }

            if (sonicHalfwordChecksumFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicHalfwordChecksumFastForwardInstructions} Sonic halfword checksum instruction(s).");
            }

            if (sonicNullSlotScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicNullSlotScanFastForwardInstructions} Sonic null-slot scan instruction(s).");
            }

            if (sonicPoolSlotScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicPoolSlotScanFastForwardInstructions} Sonic pool-slot scan instruction(s).");
            }

            if (sonicTableKeyScanFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicTableKeyScanFastForwardInstructions} Sonic table-key scan instruction(s).");
            }

            if (sonicModeRefreshDispatchFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicModeRefreshDispatchFastForwardInstructions} Sonic mode refresh dispatch instruction(s).");
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

            if (sonicGxCpStateSetterFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxCpStateSetterFastForwardInstructions} Sonic GX CP state setter instruction(s).");
            }

            if (sonicGxTevColorEnvSetterFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxTevColorEnvSetterFastForwardInstructions} Sonic GX TEV color env setter instruction(s).");
            }

            if (sonicGxTevAlphaEnvSetterFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxTevAlphaEnvSetterFastForwardInstructions} Sonic GX TEV alpha env setter instruction(s).");
            }

            if (sonicGxTevColorOpSetterFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxTevColorOpSetterFastForwardInstructions} Sonic GX TEV color op setter instruction(s).");
            }

            if (sonicGxTevAlphaOpSetterFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxTevAlphaOpSetterFastForwardInstructions} Sonic GX TEV alpha op setter instruction(s).");
            }

            if (sonicGxTevDefaultWrapperFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicGxTevDefaultWrapperFastForwardInstructions} Sonic GX TEV default wrapper instruction(s).");
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

            if (sonicCoordinatePairFillFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicCoordinatePairFillFastForwardInstructions} Sonic coordinate pair fill instruction(s).");
            }

            if (sonicBufferFillFastForwardInstructions != 0)
            {
                _output.WriteLine($"Fast-forwarded {sonicBufferFillFastForwardInstructions} Sonic buffer fill instruction(s).");
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
            if (!GxFifoSoftwareRenderer.TryRender(bus.MmioAccesses, bus.Memory, options.GxFrameDumpPath, gxWidth, gxHeight, gxFrameMaxDraws, options.GxFrameSkipDraws, stopAfterMaxDraws: true, maxRasterizedPixels: gxFrameMaxRasterPixels, ignoreEfbCopyClear: options.GxFrameIgnoreEfbCopyClear, source: options.GxFrameSource, displayCopyIndex: options.GxFrameCopyIndex, out GxFifoSoftwareRenderResult? gxFrame, out string? gxFrameError, skipEfbCopyMemoryWrites: options.GxFrameSkipCopyMemoryWrites, memorySnapshots: gxMemorySnapshots))
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
            int? gxCopyMaxDraws = options.GxFrameSkipDraws != 0 || options.GxFrameMaxDraws.HasValue
                ? options.GxFrameMaxDraws ?? RunDolOptions.DefaultGxFrameMaxDraws
                : null;
            if (!GxFifoSoftwareRenderer.TryWriteCopyDiagnostics(bus.MmioAccesses, bus.Memory, options.GxCopyDumpPath, gxWidth, gxHeight, gxFrameMaxRasterPixels, options.GxFrameIgnoreEfbCopyClear, options.GxFrameSkipDraws, gxCopyMaxDraws, out GxFifoCopyDiagnosticResult? gxCopies, out string? gxCopyError))
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

        if (options.GxCopyEventDumpPath is not null)
        {
            Stopwatch gxCopyEventDumpStopwatch = Stopwatch.StartNew();
            int? gxCopyEventMaxDraws = options.GxFrameSkipDraws != 0 || options.GxFrameMaxDraws.HasValue
                ? options.GxFrameMaxDraws ?? RunDolOptions.DefaultGxFrameMaxDraws
                : null;
            if (!GxFifoSoftwareRenderer.TryWriteCopyEventTimeline(bus.MmioAccesses, options.GxCopyEventDumpPath, options.GxFrameSkipDraws, gxCopyEventMaxDraws, out GxFifoCopyEventTimelineResult? gxCopyEvents, out string? gxCopyEventError))
            {
                gxCopyEventDumpMilliseconds += StopAndGetMilliseconds(gxCopyEventDumpStopwatch);
                _error.WriteLine($"GX copy-event dump failed: {gxCopyEventError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-copy-event-dump: {gxCopyEventError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string skipped = options.GxFrameSkipDraws == 0 ? string.Empty : $" after skipping {options.GxFrameSkipDraws} draw(s)";
                _output.WriteLine($"Wrote {gxCopyEvents!.EventsWritten} GX copy event(s){skipped} after {gxCopyEvents.TotalDraws} draw(s) to {gxCopyEvents.Path}.");
            }

            gxCopyEventDumpMilliseconds += StopAndGetMilliseconds(gxCopyEventDumpStopwatch);
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

        if (options.GxTriangleCoverageDumpPath is not null)
        {
            Stopwatch gxTriangleCoverageDumpStopwatch = Stopwatch.StartNew();
            int gxWidth = options.FrameWidth ?? 640;
            int gxHeight = options.FrameHeight ?? 480;
            int gxFrameMaxDraws = options.GxFrameMaxDraws ?? RunDolOptions.DefaultGxFrameMaxDraws;
            int gxFrameMaxRasterPixels = options.GxFrameMaxRasterPixels ?? RunDolOptions.DefaultGxFrameMaxRasterPixels;
            if (!GxFifoSoftwareRenderer.TryWriteTriangleCoverageDiagnostics(bus.MmioAccesses, bus.Memory, options.GxTriangleCoverageDumpPath, gxWidth, gxHeight, gxFrameMaxRasterPixels, options.GxFrameIgnoreEfbCopyClear, options.GxFrameSkipDraws, gxFrameMaxDraws, out GxFifoTriangleCoverageDiagnosticResult? gxTriangleCoverage, out string? gxTriangleCoverageError))
            {
                gxCoverageDumpMilliseconds += StopAndGetMilliseconds(gxTriangleCoverageDumpStopwatch);
                _error.WriteLine($"GX triangle coverage dump failed: {gxTriangleCoverageError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-triangle-coverage-dump: {gxTriangleCoverageError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string rasterState = gxTriangleCoverage!.RasterBudgetExhausted ? "raster budget exhausted" : "raster budget retained";
                string skipped = options.GxFrameSkipDraws == 0 ? string.Empty : $" after skipping {options.GxFrameSkipDraws} draw(s)";
                _output.WriteLine($"Wrote GX triangle coverage diagnostics for {gxTriangleCoverage.TrianglesWritten} triangle(s) in {gxTriangleCoverage.DrawsWritten} draw(s){skipped} and {gxTriangleCoverage.CopiesSeen} copy event(s) to {gxTriangleCoverage.Path} ({rasterState}).");
            }

            gxCoverageDumpMilliseconds += StopAndGetMilliseconds(gxTriangleCoverageDumpStopwatch);
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

        if (options.GxTransformDumpPath is not null)
        {
            if (!GxFifoSoftwareRenderer.TryWriteTransformDiagnostics(bus.MmioAccesses, bus.Memory, options.GxTransformDumpPath, options.GxDrawSkipDraws, options.GxDrawMaxDraws, out GxFifoTransformDiagnosticResult? gxTransforms, out string? gxTransformError))
            {
                _error.WriteLine($"GX transform dump failed: {gxTransformError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-transform-dump: {gxTransformError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string skipped = options.GxDrawSkipDraws == 0 ? string.Empty : $" after skipping {options.GxDrawSkipDraws} draw(s)";
                _output.WriteLine($"Wrote GX transform diagnostics for {gxTransforms!.DrawsWritten} draw(s){skipped} to {gxTransforms.Path}.");
            }
        }

        if (options.GxStateTimelineDumpPath is not null)
        {
            if (!GxFifoSoftwareRenderer.TryWriteStateTimelineDiagnostics(bus.MmioAccesses, options.GxStateTimelineDumpPath, options.GxDrawSkipDraws, options.GxDrawMaxDraws, out GxFifoStateTimelineDiagnosticResult? gxStateTimeline, out string? gxStateTimelineError))
            {
                _error.WriteLine($"GX state timeline dump failed: {gxStateTimelineError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-state-timeline-dump: {gxStateTimelineError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string skipped = options.GxDrawSkipDraws == 0 ? string.Empty : $" after skipping {options.GxDrawSkipDraws} draw(s)";
                _output.WriteLine($"Wrote {gxStateTimeline!.EventsWritten} GX state timeline event(s){skipped} after {gxStateTimeline.TotalDraws} draw(s) and {gxStateTimeline.CopiesSeen} copy event(s) to {gxStateTimeline.Path}.");
            }
        }

        if (options.GxVertexDumpPath is not null)
        {
            if (!GxFifoSoftwareRenderer.TryWriteVertexDiagnostics(bus.MmioAccesses, bus.Memory, options.GxVertexDumpPath, options.GxDrawSkipDraws, options.GxDrawMaxDraws, out GxFifoVertexDiagnosticResult? gxVertices, out string? gxVertexError))
            {
                _error.WriteLine($"GX vertex dump failed: {gxVertexError}");
                WriteRunSummary(3, diagnosticFailure: $"gx-vertex-dump: {gxVertexError}");
                return 3;
            }

            if (!options.Quiet)
            {
                string skipped = options.GxDrawSkipDraws == 0 ? string.Empty : $" after skipping {options.GxDrawSkipDraws} draw(s)";
                _output.WriteLine($"Wrote GX vertex diagnostics for {gxVertices!.VerticesWritten} vertex row(s) across {gxVertices.DrawsWritten} draw(s){skipped} to {gxVertices.Path}.");
            }
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
        WriteRequestedMemoryBinaryDumps(options, bus, _output);
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

    private static string FormatFprPair(PowerPcState state, int index) =>
        $"{FormatDouble(state.Fpr[index])}|{FormatDouble(state.FprPair1[index])}";

    private static string FormatSingleBits(double value) =>
        $"0x{BitConverter.SingleToUInt32Bits((float)value):X8}";

    private static string FormatSingleBitsAsFloat(uint value) =>
        BitConverter.UInt32BitsToSingle(value).ToString("R", CultureInfo.InvariantCulture);

    private static bool IsSonicGxEmitterTracePc(uint pc) =>
        pc is 0x8012_00C8
            or 0x8012_00CC
            or 0x8012_00D0
            or 0x8012_00D4
            or 0x8011_D8E8
            or 0x8011_D8EC
            or 0x8011_D8F8
            or 0x8011_D8FC
            or 0x8011_D900
            or 0x8011_D90C
            or 0x8011_D910
            or 0x8011_D914
            or 0x8012_012C
            or 0x8012_0130
            or 0x8012_013C
            or 0x8012_0148
            or 0x8012_014C
            or 0x8012_0150;

    private static bool IsSonicVertexProvenanceTracePc(uint pc) =>
        pc is 0x8012_00C8
            or 0x8011_D8E8;

    private static bool IsSonicPacketSelectionTracePc(uint pc) =>
        pc is 0x8011_D414
            or 0x8012_192C
            or 0x8012_1950
            or 0x8012_1A34
            or 0x8011_D484
            or 0x8011_D4C0
            or 0x8011_D4D0
            or 0x8011_D4EC
            or 0x8011_D528
            or 0x8011_D530
            or 0x8011_D534
            or 0x8011_D538
            or 0x8011_D53C
            or 0x8011_D554
            or 0x8011_D568
            or 0x8011_D578;

    private static string GetSonicPacketSelectionPhase(uint pc) =>
        pc switch
        {
            0x8012_192C => "bounds_cull_entry",
            0x8012_1950 => "bounds_cull_after_transform",
            0x8012_1A34 => "bounds_cull_return",
            0x8011_D414 => "renderer_entry",
            0x8011_D484 => "primary_wrapper_entry",
            0x8011_D4C0 => "primary_wrapper_cull_return",
            0x8011_D4D0 => "primary_wrapper_call",
            0x8011_D4EC => "secondary_wrapper_entry",
            0x8011_D528 => "secondary_wrapper_cull_return",
            0x8011_D530 => "secondary_wrapper_pre_flush",
            0x8011_D534 => "secondary_wrapper_pre_call_a",
            0x8011_D538 => "secondary_wrapper_call",
            0x8011_D53C => "secondary_wrapper_post_call",
            0x8011_D554 => "callback_wrapper_entry",
            0x8011_D568 => "callback_wrapper_after_flush",
            0x8011_D578 => "callback_dispatch_call",
            _ => "unknown",
        };

    private static bool IsSonicTraversalSourceTracePc(uint pc) =>
        pc is 0x8011_DF20
            or 0x8011_DF30
            or 0x8011_DF54
            or 0x8011_E17C
            or 0x8011_E188
            or 0x8011_E18C
            or 0x8011_D554
            or 0x8011_D568
            or 0x8011_D578
            or 0x8011_D414;

    private static string GetSonicTraversalSourcePhase(uint pc) =>
        pc switch
        {
            0x8011_DF20 => "object_callback_a_entry",
            0x8011_DF30 => "object_callback_a_return",
            0x8011_DF54 => "object_callback_b_return",
            0x8011_E17C => "traversal_callback_entry",
            0x8011_E188 => "traversal_callback_call_wrapper",
            0x8011_E18C => "traversal_callback_return",
            0x8011_D554 => "callback_wrapper_entry",
            0x8011_D568 => "callback_wrapper_after_flush",
            0x8011_D578 => "callback_dispatch_call",
            0x8011_D414 => "renderer_entry",
            _ => "unknown",
        };

    private static bool IsSonicMatrixStackTracePc(uint pc) =>
        pc is 0x8011_24D0
            or 0x8011_5DAC
            or 0x8011_5DE8
            or 0x8011_60B0
            or 0x8011_6100
            or 0x8011_61BC
            or 0x8011_61C0
            or 0x8011_61C4
            or 0x8011_C19C
            or 0x8011_E064
            or 0x8011_E078
            or 0x8011_E0B4
            or 0x8011_E0C4;

    private static bool IsSonicMatrixWriterTracePc(uint pc) =>
        pc is 0x8011_C164
            or 0x8011_C16C
            or 0x8011_C174
            or 0x8011_C17C
            or 0x8011_C180
            or 0x8011_C184
            or 0x8011_607C
            or 0x8011_6080
            or 0x8011_6084
            or 0x8011_6088
            or 0x8011_608C
            or 0x8011_6090
            or 0x8011_60B8
            or 0x8011_60C0
            or 0x8011_60C8
            or 0x8011_60D0
            or 0x8011_60D4
            or 0x8011_60DC
            or 0x8011_6138
            or 0x8011_6140
            or 0x8011_6150
            or 0x8011_6154
            or 0x8011_6160
            or 0x8011_6164
            or 0x8011_61BC
            or 0x8011_61C0
            or 0x8011_61C4;

    private static bool IsSonicRootMatrixTracePc(uint pc) =>
        pc is 0x800E_D368
            or 0x800E_D57C
            or 0x800E_D5A4
            or 0x800E_D5A8
            or 0x800E_D5B4
            or 0x800E_D5B8
            or 0x800E_D5CC
            or 0x800E_D66C
            or 0x800E_D680
            or 0x800E_D684
            or 0x800E_D688
            or 0x800E_D690
            or 0x800E_D3E4
            or 0x800E_D3F4
            or 0x800E_D3FC
            or 0x800E_D410
            or 0x800E_D418
            or 0x800E_D424
            or 0x800E_D928
            or 0x800E_D930
            or 0x800E_D938
            or 0x800E_D96C
            or 0x800E_D974
            or 0x800E_D97C
            or 0x800E_D984
            or 0x800E_D9B8
            or 0x800E_D9C0
            or 0x800E_D9C8
            or 0x800E_D9D0
            or 0x800E_DA04;

    private static string GetSonicRootMatrixPhase(uint pc) =>
        pc switch
        {
            0x800E_D368 => "multiply_entry",
            0x800E_D57C => "rotation_entry",
            0x800E_D5A4 => "rotation_sin_call",
            0x800E_D5A8 => "rotation_sin_return",
            0x800E_D5B4 => "rotation_cos_call",
            0x800E_D5B8 => "rotation_cos_return",
            0x800E_D5CC => "rotation_store_call",
            0x800E_D66C => "rotation_z_store_zero0",
            0x800E_D680 => "rotation_z_merge_unit",
            0x800E_D684 => "rotation_z_store_row_y",
            0x800E_D688 => "rotation_z_store_row_x",
            0x800E_D690 => "rotation_terminal",
            0x800E_D3E4 => "multiply_store_x0",
            0x800E_D3F4 => "multiply_store_x1",
            0x800E_D3FC => "multiply_store_y0",
            0x800E_D410 => "multiply_store_y1",
            0x800E_D418 => "multiply_store_z0",
            0x800E_D424 => "multiply_terminal",
            0x800E_D928 => "scalar_store_x0",
            0x800E_D930 => "scalar_store_x1",
            0x800E_D938 => "scalar_store_x2",
            0x800E_D96C => "scalar_store_y0",
            0x800E_D974 => "scalar_store_y1",
            0x800E_D97C => "scalar_store_y2",
            0x800E_D984 => "scalar_store_y3",
            0x800E_D9B8 => "scalar_store_z0",
            0x800E_D9C0 => "scalar_store_z1",
            0x800E_D9C8 => "scalar_store_z2",
            0x800E_D9D0 => "scalar_store_z3",
            0x800E_DA04 => "scalar_terminal",
            _ => "unknown",
        };

    private static bool IsSonicTransformInputTracePc(uint pc) =>
        pc is SonicPairedTransform2dLoopPc
            or SonicPairedTransform4dLoopPc
            or SonicPairedTransform4dIndexedLoopPc;

    private static ulong EstimateSonicTransformOutputSpanBytes(uint iterations) =>
        Math.Max(1UL, iterations) * 0x20UL;

    private static int EstimateSonicTransformInputWindowBytes(uint pc, uint iterations)
    {
        ulong bytesPerIteration = pc switch
        {
            SonicPairedTransform2dLoopPc => 0x10UL,
            SonicPairedTransform4dLoopPc => 0x18UL,
            SonicPairedTransform4dIndexedLoopPc => 0x1CUL,
            _ => 0x10UL,
        };

        ulong bytes = checked((Math.Max(1UL, iterations) + 1UL) * bytesPerIteration);
        return checked((int)Math.Min(Math.Max(bytes, 0x100UL), 0x4000UL));
    }

    private static bool OverlapsAddressRange(uint start, ulong length, uint rangeStart, int rangeLength)
    {
        ulong spanStart = start;
        ulong spanEnd = spanStart + Math.Max(1UL, length);
        ulong filterStart = rangeStart;
        ulong filterEnd = filterStart + (uint)rangeLength;
        if (spanStart < filterEnd && filterStart < spanEnd)
        {
            return true;
        }

        if (GameCubeAddress.TryTranslateMainRam(start, out int spanOffset)
            && GameCubeAddress.TryTranslateMainRam(rangeStart, out int filterOffset))
        {
            ulong normalizedSpanStart = (uint)spanOffset;
            ulong normalizedSpanEnd = normalizedSpanStart + Math.Max(1UL, length);
            ulong normalizedFilterStart = (uint)filterOffset;
            ulong normalizedFilterEnd = normalizedFilterStart + (uint)rangeLength;
            return normalizedSpanStart < normalizedFilterEnd && normalizedFilterStart < normalizedSpanEnd;
        }

        return false;
    }

    private static void WriteSonicVertexProvenanceTraceRow(
        TextWriter writer,
        int instructionIndex,
        long gxFifoOffset,
        uint pc,
        PowerPcState state,
        GameCubeMemory memory)
    {
        const int sourceRecordBytes = 0x20;
        uint streamCursor = state.Gpr[24];
        uint vertexBase = state.Gpr[25];
        uint sourceRecord = state.Gpr[27];
        uint streamRecord = streamCursor >= 6 ? streamCursor - 6 : 0;
        bool hasStreamRecord = memory.IsMainRamAddress(streamRecord, 6);
        short decodedIndex = hasStreamRecord ? unchecked((short)memory.Read16(streamRecord)) : (short)0;
        short decodedAttr0 = hasStreamRecord ? unchecked((short)memory.Read16(streamRecord + 2)) : (short)0;
        short decodedAttr1 = hasStreamRecord ? unchecked((short)memory.Read16(streamRecord + 4)) : (short)0;
        int sourceDelta = unchecked((int)(sourceRecord - vertexBase));
        bool hasActualIndex = vertexBase != 0 && sourceDelta % 0x20 == 0;
        int actualIndex = hasActualIndex ? sourceDelta / 0x20 : 0;
        short r29 = unchecked((short)(state.Gpr[29] & 0xFFFF));
        short r28 = unchecked((short)(state.Gpr[28] & 0xFFFF));
        SonicPacketInference packet = InferSonicPacketForStreamRecord(memory, streamRecord);
        string streamOffset = packet.Found
            ? $"0x{unchecked(streamRecord - packet.Stream1):X}"
            : string.Empty;
        uint sourceX = ReadMainRamWordOrZero(memory, sourceRecord);
        uint sourceY = ReadMainRamWordOrZero(memory, sourceRecord + sizeof(uint));
        uint sourceZ = ReadMainRamWordOrZero(memory, sourceRecord + (sizeof(uint) * 2));
        uint sourceColor = ReadMainRamWordOrZero(memory, sourceRecord + (sizeof(uint) * 6));

        writer.WriteLine($"{instructionIndex},0x{gxFifoOffset:X},0x{pc:X8},0x{state.Lr:X8},{FormatOptionalHex32(packet.Packet)},{FormatOptionalHex32(packet.Kind)},{FormatOptionalHex32(packet.Stream0)},{FormatOptionalHex32(packet.Stream1)},0x{streamRecord:X8},0x{streamCursor:X8},{streamOffset},\"{CaptureMemoryWindowHex(memory, streamRecord, 6)}\",{(hasStreamRecord ? decodedIndex.ToString(CultureInfo.InvariantCulture) : string.Empty)},{(hasActualIndex ? actualIndex.ToString(CultureInfo.InvariantCulture) : string.Empty)},{FormatBool(hasStreamRecord && hasActualIndex && decodedIndex == actualIndex)},{(hasStreamRecord ? FormatHex16(decodedAttr0) : string.Empty)},0x{state.Gpr[29]:X8},{FormatBool(hasStreamRecord && decodedAttr0 == r29)},{(hasStreamRecord ? FormatHex16(decodedAttr1) : string.Empty)},0x{state.Gpr[28]:X8},{FormatBool(hasStreamRecord && decodedAttr1 == r28)},0x{vertexBase:X8},0x{sourceRecord:X8},0x{sourceX:X8},0x{sourceY:X8},0x{sourceZ:X8},0x{sourceColor:X8},{FormatSingleBitsAsFloat(sourceX)},{FormatSingleBitsAsFloat(sourceY)},{FormatSingleBitsAsFloat(sourceZ)},0x{sourceX:X8},0x{sourceY:X8},0x{sourceZ:X8},{FormatDouble(state.Fpr[1])},{FormatDouble(state.Fpr[2])},{FormatDouble(state.Fpr[3])},{FormatSingleBits(state.Fpr[1])},{FormatSingleBits(state.Fpr[2])},{FormatSingleBits(state.Fpr[3])},0x{state.Gpr[24]:X8},0x{state.Gpr[25]:X8},0x{state.Gpr[27]:X8},0x{state.Gpr[28]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},\"{CaptureMemoryWindowHex(memory, sourceRecord, sourceRecordBytes)}\"");
    }

    private static void WriteSonicTextureBindTraceRow(
        TextWriter writer,
        int instructionIndex,
        long gxFifoOffset,
        PowerPcState state,
        GameCubeMemory memory,
        uint textureObject,
        uint samplerObject,
        uint textureMap,
        uint stateBlock,
        byte objectFlags,
        uint word0,
        uint word4,
        uint word8,
        uint sampler0,
        uint sampler4,
        uint word12)
    {
        const int objectWindowBytes = 0x20;
        const int samplerWindowBytes = 0x08;
        DecodeSonicTextureBindWords([word0, word4, word8, sampler0, sampler4, word12], out uint? sourceAddress, out int? width, out int? height, out string format, out uint? mode0, out uint? mode1);
        writer.WriteLine(
            string.Join(',',
                instructionIndex.ToString(CultureInfo.InvariantCulture),
                $"0x{gxFifoOffset:X}",
                $"0x{SonicGxTexObjLoadNoCallbackPc:X8}",
                $"0x{state.Lr:X8}",
                $"0x{textureObject:X8}",
                $"0x{samplerObject:X8}",
                textureMap.ToString(CultureInfo.InvariantCulture),
                $"0x{stateBlock:X8}",
                $"0x{objectFlags:X2}",
                $"0x{word0:X8}",
                $"0x{word4:X8}",
                $"0x{word8:X8}",
                $"0x{sampler0:X8}",
                $"0x{sampler4:X8}",
                $"0x{word12:X8}",
                sourceAddress.HasValue ? $"0x{sourceAddress.Value:X8}" : string.Empty,
                width?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                height?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                format,
                mode0.HasValue ? $"0x{mode0.Value:X6}" : string.Empty,
                mode1.HasValue ? $"0x{mode1.Value:X6}" : string.Empty,
                $"\"{CaptureMemoryWindowHex(memory, textureObject, objectWindowBytes)}\"",
                $"\"{CaptureMemoryWindowHex(memory, samplerObject, samplerWindowBytes)}\""));
    }

    private static void DecodeSonicTextureBindWords(ReadOnlySpan<uint> words, out uint? sourceAddress, out int? width, out int? height, out string format, out uint? mode0, out uint? mode1)
    {
        sourceAddress = null;
        width = null;
        height = null;
        format = string.Empty;
        mode0 = null;
        mode1 = null;

        foreach (uint word in words)
        {
            byte register = (byte)(word >> 24);
            uint data = word & 0x00FF_FFFF;
            if (IsSonicTextureRegister(register, SonicTextureRegisterKind.Mode0))
            {
                mode0 = data;
            }
            else if (IsSonicTextureRegister(register, SonicTextureRegisterKind.Mode1))
            {
                mode1 = data;
            }
            else if (IsSonicTextureRegister(register, SonicTextureRegisterKind.Image0))
            {
                width = (int)(data & 0x3FF) + 1;
                height = (int)((data >> 10) & 0x3FF) + 1;
                format = SonicTextureFormatName((int)((data >> 20) & 0xF));
            }
            else if (IsSonicTextureRegister(register, SonicTextureRegisterKind.Image3))
            {
                sourceAddress = (data & 0x00FF_FFFF) << 5;
            }
        }
    }

    private enum SonicTextureRegisterKind
    {
        Mode0,
        Mode1,
        Image0,
        Image3,
    }

    private static bool IsSonicTextureRegister(byte register, SonicTextureRegisterKind kind)
    {
        ReadOnlySpan<byte> registers = kind switch
        {
            SonicTextureRegisterKind.Mode0 => [0x80, 0x81, 0x82, 0x83, 0xA0, 0xA1, 0xA2, 0xA3],
            SonicTextureRegisterKind.Mode1 => [0x84, 0x85, 0x86, 0x87, 0xA4, 0xA5, 0xA6, 0xA7],
            SonicTextureRegisterKind.Image0 => [0x88, 0x89, 0x8A, 0x8B, 0xA8, 0xA9, 0xAA, 0xAB],
            SonicTextureRegisterKind.Image3 => [0x94, 0x95, 0x96, 0x97, 0xB4, 0xB5, 0xB6, 0xB7],
            _ => [],
        };

        return registers.Contains(register);
    }

    private static string SonicTextureFormatName(int format) =>
        format switch
        {
            0 => "I4",
            1 => "I8",
            2 => "IA4",
            3 => "IA8",
            4 => "RGB565",
            5 => "RGB5A3",
            6 => "RGBA8",
            8 => "CI4",
            9 => "CI8",
            10 => "CI14",
            14 => "CMPR",
            _ => $"fmt{format}",
        };

    private static void WriteSonicMatrixWriterTraceRow(
        TextWriter writer,
        int instructionIndex,
        uint pc,
        uint opcode,
        PowerPcState state,
        GameCubeBus bus)
    {
        const int matrixWindowBytes = 0x60;
        const int contextWindowBytes = 0x80;
        string disassembly = PowerPcDisassembler.Disassemble(opcode).Replace("\"", "\"\"", StringComparison.Ordinal);
        uint storeAddress = InferStoreAddress(state, opcode);

        writer.WriteLine($"{instructionIndex},0x{pc:X8},0x{state.Lr:X8},0x{state.Ctr:X8},0x{state.Cr:X8},0x{opcode:X8},\"{disassembly}\",0x{storeAddress:X8},0x{state.Gpr[1]:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[8]:X8},0x{state.Gpr[9]:X8},0x{state.Gpr[10]:X8},0x{state.Gpr[13]:X8},0x{state.Gpr[27]:X8},0x{state.Gpr[28]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},{FormatFprPair(state, 0)},{FormatFprPair(state, 1)},{FormatFprPair(state, 2)},{FormatFprPair(state, 3)},{FormatFprPair(state, 4)},{FormatFprPair(state, 5)},{FormatFprPair(state, 6)},{FormatFprPair(state, 7)},{FormatFprPair(state, 8)},{FormatFprPair(state, 9)},{FormatFprPair(state, 10)},{FormatFprPair(state, 11)},{FormatFprPair(state, 12)},{FormatFprPair(state, 13)},\"{CaptureBusWindowHex(bus, 0xE000_0090, matrixWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[5], matrixWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[1], contextWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[3], contextWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[4], contextWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[5], contextWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[6], contextWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[30], contextWindowBytes)}\"");
    }

    private static void WriteSonicRootMatrixTraceRow(
        TextWriter writer,
        int instructionIndex,
        uint pc,
        uint opcode,
        PowerPcState state,
        GameCubeBus bus)
    {
        const int matrixWindowBytes = 0x30;
        const int contextWindowBytes = 0x80;
        string disassembly = PowerPcDisassembler.Disassemble(opcode).Replace("\"", "\"\"", StringComparison.Ordinal);
        uint storeAddress = InferStoreAddress(state, opcode);

        writer.WriteLine($"{instructionIndex},0x{pc:X8},{GetSonicRootMatrixPhase(pc)},0x{state.Lr:X8},0x{state.Ctr:X8},0x{state.Cr:X8},0x{opcode:X8},\"{disassembly}\",0x{storeAddress:X8},0x{state.Gpr[1]:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[8]:X8},0x{state.Gpr[9]:X8},0x{state.Gpr[10]:X8},0x{state.Gpr[13]:X8},0x{state.Gpr[27]:X8},0x{state.Gpr[28]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},{FormatFprPair(state, 0)},{FormatFprPair(state, 1)},{FormatFprPair(state, 2)},{FormatFprPair(state, 3)},{FormatFprPair(state, 4)},{FormatFprPair(state, 5)},{FormatFprPair(state, 6)},{FormatFprPair(state, 7)},{FormatFprPair(state, 8)},{FormatFprPair(state, 9)},{FormatFprPair(state, 10)},{FormatFprPair(state, 11)},{FormatFprPair(state, 12)},{FormatFprPair(state, 13)},{FormatFprPair(state, 14)},{FormatFprPair(state, 15)},{FormatFprPair(state, 31)},\"{CaptureBusWindowHex(bus, state.Gpr[3], matrixWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[4], matrixWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[5], matrixWindowBytes)}\",\"{CaptureBusWindowHex(bus, 0xE000_0000, matrixWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[1], contextWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[3], contextWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[4], contextWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[5], contextWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[6], contextWindowBytes)}\",\"{CaptureBusWindowHex(bus, state.Gpr[29], contextWindowBytes)}\"");
    }

    private static uint InferPairedSingleStoreAddress(PowerPcState state, uint opcode)
    {
        int rA = (int)((opcode >> 16) & 0x1F);
        int displacement = ((int)(opcode & 0x0FFF) << 20) >> 20;
        uint baseAddress = rA == 0 ? 0u : state.Gpr[rA];
        return unchecked(baseAddress + (uint)displacement);
    }

    private static uint InferStoreAddress(PowerPcState state, uint opcode)
    {
        uint primary = opcode >> 26;
        int rA = (int)((opcode >> 16) & 0x1F);
        uint baseAddress = rA == 0 ? 0u : state.Gpr[rA];
        int displacement = primary == 61
            ? ((int)(opcode & 0x0FFF) << 20) >> 20
            : (short)(opcode & 0xFFFF);
        return unchecked(baseAddress + (uint)displacement);
    }

    private static void WriteSonicSceneStateTraceRow(
        TextWriter writer,
        int instructionIndex,
        uint pc,
        PowerPcState state,
        GameCubeBus bus)
    {
        const uint stateBase = 0x801C_C168;
        const int packetWindowBytes = 0x80;
        const int objectWindowBytes = 0x80;
        const int stateWindowBytes = 0x100;
        const int smallDataWindowBytes = 0x100;
        const int modePointerWindowBytes = 0x80;
        const int matrixWindowBytes = 0x30;

        uint packet = state.Gpr[3];
        uint stream0 = ReadMainRamWordOrZero(bus.Memory, packet);
        uint stream1 = ReadMainRamWordOrZero(bus.Memory, packet + sizeof(uint));
        uint packetKind = ReadMainRamWordOrZero(bus.Memory, packet + 0x18);
        uint objectAddress = unchecked(packet + 0x18u);
        uint objectFlags = ReadMainRamWordOrZero(bus.Memory, objectAddress);
        uint vertexBasePointer = unchecked(state.Gpr[13] - 29028u);
        uint vertexBase = ReadMainRamWordOrZero(bus.Memory, vertexBasePointer);
        uint matrixBasePointer = ReadMainRamWordOrZero(bus.Memory, unchecked(state.Gpr[13] - 29184u));
        uint matrixLimitPointer = ReadMainRamWordOrZero(bus.Memory, unchecked(state.Gpr[13] - 29188u));
        uint currentMatrixPointer = ReadMainRamWordOrZero(bus.Memory, unchecked(state.Gpr[13] - 29180u));
        uint previousMatrixPointer = currentMatrixPointer >= matrixBasePointer + matrixWindowBytes
            ? currentMatrixPointer - matrixWindowBytes
            : 0u;
        string previousMatrixBytes = previousMatrixPointer == 0
            ? string.Empty
            : CaptureBusWindowHex(bus, previousMatrixPointer, matrixWindowBytes);

        uint resourceFlagAddress = unchecked(state.Gpr[13] + SonicResourceFlagOffset);
        uint resourceFlag = ReadMainRamWordOrZero(bus.Memory, resourceFlagAddress);
        uint smallDataStateAddress = unchecked(state.Gpr[13] - 30340u);
        uint firstModeFlagAddress = unchecked(state.Gpr[13] - 30016u);
        uint secondModeFlagAddress = unchecked(state.Gpr[13] - 30024u);
        uint modePointerAddress = unchecked(state.Gpr[13] - 30040u);
        uint smallDataState = ReadMainRamWordOrZero(bus.Memory, smallDataStateAddress);
        uint firstModeFlag = ReadMainRamWordOrZero(bus.Memory, firstModeFlagAddress);
        uint secondModeFlag = ReadMainRamWordOrZero(bus.Memory, secondModeFlagAddress);
        uint modePointer = ReadMainRamWordOrZero(bus.Memory, modePointerAddress);
        uint modePointerValue = ReadMainRamWordOrZero(bus.Memory, modePointer + 0x0C);
        byte stateByte13 = bus.Memory.IsMainRamAddress(stateBase + 0x13, sizeof(byte)) ? bus.Memory.Read8(stateBase + 0x13) : (byte)0;
        byte stateByte47 = bus.Memory.IsMainRamAddress(stateBase + 0x47, sizeof(byte)) ? bus.Memory.Read8(stateBase + 0x47) : (byte)0;
        uint stateWord80 = ReadMainRamWordOrZero(bus.Memory, stateBase + 0x80);
        uint stateWordEc = ReadMainRamWordOrZero(bus.Memory, stateBase + 0xEC);
        uint smallDataWindowAddress = smallDataStateAddress & ~0x3Fu;

        writer.WriteLine($"{instructionIndex},0x{pc:X8},0x{state.Lr:X8},0x{state.Ctr:X8},0x{state.Cr:X8},0x{packet:X8},0x{packetKind:X8},0x{objectAddress:X8},0x{objectFlags & 0xFF:X2},0x{stream0:X8},0x{stream1:X8},0x{vertexBasePointer:X8},0x{vertexBase:X8},0x{matrixBasePointer:X8},0x{matrixLimitPointer:X8},0x{currentMatrixPointer:X8},0x{previousMatrixPointer:X8},0x{resourceFlagAddress:X8},0x{resourceFlag:X8},0x{stateBase:X8},0x{stateByte13:X2},0x{stateByte47:X2},0x{stateWord80:X8},0x{stateWordEc:X8},0x{smallDataStateAddress:X8},0x{smallDataWindowAddress:X8},0x{smallDataState:X8},0x{firstModeFlagAddress:X8},0x{firstModeFlag:X8},0x{secondModeFlagAddress:X8},0x{secondModeFlag:X8},0x{modePointerAddress:X8},0x{modePointer:X8},0x{modePointerValue:X8},0x{state.Gpr[1]:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[8]:X8},0x{state.Gpr[9]:X8},0x{state.Gpr[10]:X8},0x{state.Gpr[13]:X8},0x{state.Gpr[27]:X8},0x{state.Gpr[28]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},\"{CaptureMemoryWindowHex(bus.Memory, packet, packetWindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, objectAddress, objectWindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, stateBase, stateWindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, smallDataWindowAddress, smallDataWindowBytes)}\",\"{CaptureMemoryWindowHex(bus.Memory, modePointer, modePointerWindowBytes)}\",\"{CaptureBusWindowHex(bus, currentMatrixPointer, matrixWindowBytes)}\",\"{previousMatrixBytes}\"");
    }

    private static void WriteSonicPacketSelectionTraceRow(
        TextWriter writer,
        int instructionIndex,
        uint pc,
        PowerPcState state,
        GameCubeMemory memory)
    {
        const int packetWindowBytes = 0x80;
        const int objectWindowBytes = 0x80;
        const int registerWindowBytes = 0x40;
        const int stackWindowBytes = 0x80;

        bool foundPacket = TryInferSonicPacketFromRegisters(memory, state, out string packetSource, out uint packet, out uint packetKind, out uint stream0, out uint stream1, out uint objectAddress);
        uint objectFlags = foundPacket ? ReadMainRamWordOrZero(memory, objectAddress) : 0u;
        uint boundXBits = foundPacket ? ReadMainRamWordOrZero(memory, packet + 0x08) : 0u;
        uint boundYBits = foundPacket ? ReadMainRamWordOrZero(memory, packet + 0x0C) : 0u;
        uint boundZBits = foundPacket ? ReadMainRamWordOrZero(memory, packet + 0x10) : 0u;
        uint boundRadiusBits = foundPacket ? ReadMainRamWordOrZero(memory, packet + 0x14) : 0u;
        uint objectXBits = foundPacket ? ReadMainRamWordOrZero(memory, objectAddress + 0x08) : 0u;
        uint objectYBits = foundPacket ? ReadMainRamWordOrZero(memory, objectAddress + 0x0C) : 0u;
        uint objectZBits = foundPacket ? ReadMainRamWordOrZero(memory, objectAddress + 0x10) : 0u;
        uint objectWBits = foundPacket ? ReadMainRamWordOrZero(memory, objectAddress + 0x14) : 0u;
        uint vertexBase = ReadMainRamWordOrZero(memory, unchecked(state.Gpr[13] - 29028u));

        writer.WriteLine($"{instructionIndex},0x{pc:X8},{GetSonicPacketSelectionPhase(pc)},0x{state.Lr:X8},0x{state.Ctr:X8},0x{state.Cr:X8},{packetSource},{FormatOptionalHex32(packet)},{FormatOptionalHex32(packetKind)},0x{objectAddress:X8},0x{objectFlags & 0xFF:X2},{FormatOptionalHex32(stream0)},{FormatOptionalHex32(stream1)},0x{boundXBits:X8},0x{boundYBits:X8},0x{boundZBits:X8},0x{boundRadiusBits:X8},{FormatSingleBitsAsFloat(boundXBits)},{FormatSingleBitsAsFloat(boundYBits)},{FormatSingleBitsAsFloat(boundZBits)},{FormatSingleBitsAsFloat(boundRadiusBits)},{FormatSingleBitsAsFloat(objectXBits)},{FormatSingleBitsAsFloat(objectYBits)},{FormatSingleBitsAsFloat(objectZBits)},{FormatSingleBitsAsFloat(objectWBits)},0x{vertexBase:X8},0x{state.Gpr[1]:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[8]:X8},0x{state.Gpr[9]:X8},0x{state.Gpr[10]:X8},0x{state.Gpr[13]:X8},0x{state.Gpr[27]:X8},0x{state.Gpr[28]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},\"{CaptureMemoryWindowHex(memory, packet, packetWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, objectAddress, objectWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[3], registerWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[27], registerWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[30], registerWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[31], registerWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[1], stackWindowBytes)}\"");
    }

    private static void WriteSonicTraversalSourceTraceRow(
        TextWriter writer,
        int instructionIndex,
        uint pc,
        PowerPcState state,
        GameCubeMemory memory)
    {
        const int registerWindowBytes = 0x80;
        const int stackWindowBytes = 0xC0;

        bool foundPacket = TryInferSonicPacketFromRegisters(memory, state, out string packetSource, out uint packet, out uint packetKind, out uint stream0, out uint stream1, out uint objectAddress);
        uint boundXBits = foundPacket ? ReadMainRamWordOrZero(memory, packet + 0x08) : 0u;
        uint boundYBits = foundPacket ? ReadMainRamWordOrZero(memory, packet + 0x0C) : 0u;
        uint boundZBits = foundPacket ? ReadMainRamWordOrZero(memory, packet + 0x10) : 0u;
        uint boundRadiusBits = foundPacket ? ReadMainRamWordOrZero(memory, packet + 0x14) : 0u;
        uint objectXBits = foundPacket ? ReadMainRamWordOrZero(memory, objectAddress + 0x08) : 0u;
        uint objectYBits = foundPacket ? ReadMainRamWordOrZero(memory, objectAddress + 0x0C) : 0u;
        uint objectZBits = foundPacket ? ReadMainRamWordOrZero(memory, objectAddress + 0x10) : 0u;
        uint objectKind = foundPacket ? ReadMainRamWordOrZero(memory, objectAddress) & 0xFFu : 0u;
        uint stack0 = ReadMainRamWordOrZero(memory, state.Gpr[1]);
        uint stack4 = ReadMainRamWordOrZero(memory, unchecked(state.Gpr[1] + 4u));
        uint stack8 = ReadMainRamWordOrZero(memory, unchecked(state.Gpr[1] + 8u));
        uint stack12 = ReadMainRamWordOrZero(memory, unchecked(state.Gpr[1] + 12u));

        writer.WriteLine($"{instructionIndex},0x{pc:X8},{GetSonicTraversalSourcePhase(pc)},0x{state.Lr:X8},0x{state.Ctr:X8},0x{state.Cr:X8},{packetSource},{FormatOptionalHex32(packet)},{FormatOptionalHex32(packetKind)},0x{objectAddress:X8},0x{objectKind:X2},{FormatOptionalHex32(stream0)},{FormatOptionalHex32(stream1)},{FormatSingleBitsAsFloat(objectXBits)},{FormatSingleBitsAsFloat(objectYBits)},{FormatSingleBitsAsFloat(objectZBits)},{FormatSingleBitsAsFloat(boundXBits)},{FormatSingleBitsAsFloat(boundYBits)},{FormatSingleBitsAsFloat(boundZBits)},{FormatSingleBitsAsFloat(boundRadiusBits)},0x{state.Gpr[1]:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[8]:X8},0x{state.Gpr[9]:X8},0x{state.Gpr[10]:X8},0x{state.Gpr[13]:X8},0x{state.Gpr[27]:X8},0x{state.Gpr[28]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},0x{stack0:X8},0x{stack4:X8},0x{stack8:X8},0x{stack12:X8},\"{CaptureMemoryWindowHex(memory, state.Gpr[3], registerWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[4], registerWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[5], registerWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[27], registerWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[29], registerWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[30], registerWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[31], registerWindowBytes)}\",\"{CaptureMemoryWindowHex(memory, state.Gpr[1], stackWindowBytes)}\"");
    }

    private static bool TryInferSonicPacketFromRegisters(
        GameCubeMemory memory,
        PowerPcState state,
        out string source,
        out uint packet,
        out uint kind,
        out uint stream0,
        out uint stream1,
        out uint objectAddress)
    {
        (string Source, uint Address)[] candidates =
        [
            ("r3", state.Gpr[3]),
            ("r31", state.Gpr[31]),
            ("r30", state.Gpr[30]),
            ("r27", state.Gpr[27]),
            ("r29", state.Gpr[29]),
        ];

        foreach ((string candidateSource, uint candidateAddress) in candidates)
        {
            if (TryReadSonicPacketCandidate(memory, candidateAddress, out kind, out stream0, out stream1))
            {
                source = candidateSource;
                packet = candidateAddress;
                objectAddress = unchecked(packet + 0x18u);
                return true;
            }

            if (TryReadSonicObjectCandidate(memory, candidateAddress, out packet, out kind, out stream0, out stream1))
            {
                source = $"{candidateSource}.object";
                objectAddress = candidateAddress;
                return true;
            }
        }

        source = string.Empty;
        packet = 0;
        kind = 0;
        stream0 = 0;
        stream1 = 0;
        objectAddress = 0;
        return false;
    }

    private static bool TryReadSonicPacketCandidate(GameCubeMemory memory, uint candidate, out uint kind, out uint stream0, out uint stream1)
    {
        kind = 0;
        stream0 = 0;
        stream1 = 0;
        if (!memory.IsMainRamAddress(candidate, 0x40))
        {
            return false;
        }

        stream0 = memory.Read32(candidate);
        stream1 = memory.Read32(candidate + sizeof(uint));
        kind = memory.Read32(candidate + 0x18);
        uint selfPointer = memory.Read32(candidate + 0x1C);
        return kind is > 0 and <= 0x100
            && selfPointer == candidate
            && memory.IsMainRamAddress(stream0, sizeof(uint))
            && memory.IsMainRamAddress(stream1, sizeof(uint));
    }

    private static bool TryReadSonicObjectCandidate(GameCubeMemory memory, uint candidate, out uint packet, out uint kind, out uint stream0, out uint stream1)
    {
        packet = 0;
        kind = 0;
        stream0 = 0;
        stream1 = 0;
        if (!memory.IsMainRamAddress(candidate, 0x20))
        {
            return false;
        }

        uint objectKind = memory.Read32(candidate);
        uint objectPacket = memory.Read32(candidate + sizeof(uint));
        if (!TryReadSonicPacketCandidate(memory, objectPacket, out kind, out stream0, out stream1)
            || objectKind != kind)
        {
            return false;
        }

        packet = objectPacket;
        return true;
    }

    private static SonicPacketInference InferSonicPacketForStreamRecord(GameCubeMemory memory, uint streamRecord)
    {
        if (!memory.IsMainRamAddress(streamRecord, 6))
        {
            return default;
        }

        uint scanStart = streamRecord & ~3u;
        uint scanEnd = unchecked(streamRecord + 0x4000u);
        for (uint candidate = scanStart; candidate < scanEnd; candidate += sizeof(uint))
        {
            if (!memory.IsMainRamAddress(candidate, 0x44))
            {
                continue;
            }

            uint stream0 = memory.Read32(candidate);
            uint stream1 = memory.Read32(candidate + sizeof(uint));
            uint kind = memory.Read32(candidate + 0x18);
            uint selfPointer = memory.Read32(candidate + 0x1C);
            if (kind == 0
                || kind > 0x100
                || selfPointer != candidate
                || stream1 > streamRecord
                || streamRecord >= stream0
                || stream1 > stream0
                || stream0 > candidate
                || candidate - stream1 > 0x10000)
            {
                continue;
            }

            return new SonicPacketInference(true, candidate, kind, stream0, stream1);
        }

        return default;
    }

    private static string FormatOptionalHex32(uint value) =>
        value == 0 ? string.Empty : $"0x{value:X8}";

    private static string FormatHex16(short value) =>
        $"0x{unchecked((ushort)value):X4}";

    private static string FormatBool(bool value) =>
        value ? "True" : "False";

    private static void WriteSonicInputWriteTraceRow(
        TextWriter writer,
        int instructionIndex,
        uint pc,
        uint opcode,
        PowerPcState state,
        GameCubeMemory memory,
        string kind,
        int width,
        uint address,
        uint value,
        uint rangeAddress,
        int rangeLength,
        int postWriteWindowBytes)
    {
        string disassembly = PowerPcDisassembler.Disassemble(opcode).Replace("\"", "\"\"", StringComparison.Ordinal);
        int capturedRangeLength = Math.Min(rangeLength, 0x100);
        writer.WriteLine($"{instructionIndex},0x{pc:X8},0x{opcode:X8},\"{disassembly}\",{kind},{width},0x{address:X8},0x{value:X8},0x{state.Gpr[1]:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[8]:X8},0x{state.Gpr[9]:X8},0x{state.Gpr[10]:X8},0x{state.Gpr[13]:X8},0x{state.Gpr[24]:X8},0x{state.Gpr[25]:X8},0x{state.Gpr[27]:X8},0x{state.Gpr[28]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},0x{rangeAddress:X8},0x{rangeLength:X},\"{CaptureMemoryWindowHex(memory, rangeAddress, capturedRangeLength)}\",\"{CaptureMemoryWindowHex(memory, address, postWriteWindowBytes)}\"");
    }

    private static void WriteSonicPrsDecompressTraceRow(
        TextWriter writer,
        int instructionIndex,
        PowerPcState state,
        SonicPrsDecompressTraceEvent traceEvent,
        uint targetAddress,
        int targetLength)
    {
        writer.WriteLine($"{instructionIndex},0x{traceEvent.Pc:X8},0x{state.Lr:X8},0x{traceEvent.Source:X8},0x{traceEvent.SourceEnd:X8},0x{traceEvent.Destination:X8},0x{traceEvent.OutputLength:X},0x{targetAddress:X8},0x{targetLength:X},0x{traceEvent.TargetOutputOffset:X},0x{traceEvent.LastFlagByte:X2},{traceEvent.BitsRemaining},0x{traceEvent.SkippedInstructions:X},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[8]:X8},0x{state.Gpr[9]:X8},0x{state.Gpr[10]:X8},0x{state.Gpr[13]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},\"{traceEvent.SourceBytes}\",\"{traceEvent.TargetOutputBytes}\",\"{traceEvent.OutputHeadBytes}\"");
    }

    private static void WriteLockedCacheWriteTraceRow(
        TextWriter writer,
        int instructionIndex,
        uint pc,
        uint opcode,
        PowerPcState state,
        GameCubeBus bus,
        int width,
        uint address,
        uint value,
        uint rangeAddress,
        int rangeLength,
        int postWriteWindowBytes)
    {
        string disassembly = PowerPcDisassembler.Disassemble(opcode).Replace("\"", "\"\"", StringComparison.Ordinal);
        int capturedRangeLength = Math.Min(rangeLength, 0x100);
        writer.WriteLine($"{instructionIndex},0x{pc:X8},0x{opcode:X8},\"{disassembly}\",{width},0x{address:X8},0x{value:X8},0x{state.Gpr[1]:X8},0x{state.Gpr[3]:X8},0x{state.Gpr[4]:X8},0x{state.Gpr[5]:X8},0x{state.Gpr[6]:X8},0x{state.Gpr[7]:X8},0x{state.Gpr[8]:X8},0x{state.Gpr[9]:X8},0x{state.Gpr[10]:X8},0x{state.Gpr[13]:X8},0x{state.Gpr[24]:X8},0x{state.Gpr[25]:X8},0x{state.Gpr[27]:X8},0x{state.Gpr[28]:X8},0x{state.Gpr[29]:X8},0x{state.Gpr[30]:X8},0x{state.Gpr[31]:X8},\"{CaptureBusWindowHex(bus, rangeAddress, capturedRangeLength)}\",\"{CaptureBusWindowHex(bus, address, postWriteWindowBytes)}\"");
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
        summary.WriteLine("skip,path,source,source_address,source_format,source_copy_index,selected_copy_index,selected_copy_kind,selected_copy_draws_seen,selected_copy_fifo_offset,selected_copy_destination_address,last_display_copy_index,last_display_copy_draws_seen,last_display_copy_fifo_offset,draws_since_last_display_copy,copy_events_since_last_display_copy,clears_since_last_display_copy,texture_copies_since_last_display_copy,efb_was_cleared_after_last_display_copy,lifecycle_phase,display_copies_seen,texture_copies_seen,parsed_draws,rendered_quads,rendered_triangles,degenerate_quads,degenerate_triangles,error");

        for (int index = 0; index < sweep.Count; index++)
        {
            long skipLong = (long)sweep.StartSkipDraws + (long)index * sweep.StepDraws;
            if (skipLong > int.MaxValue)
            {
                summary.WriteLine($"{skipLong},,,,,,,,,,,,,,,,,,,,,,,,,,,skip draw count exceeds Int32.MaxValue");
                continue;
            }

            int skipDraws = (int)skipLong;
            string framePath = Path.Combine(outputDirectory, $"gx-frame-skip-{skipDraws:D6}.png");
            bool rendered = GxFifoSoftwareRenderer.TryRender(bus.MmioAccesses, bus.Memory, framePath, gxWidth, gxHeight, gxFrameMaxDraws, skipDraws, stopAfterMaxDraws: true, maxRasterizedPixels: gxFrameMaxRasterPixels, ignoreEfbCopyClear: options.GxFrameIgnoreEfbCopyClear, source: options.GxFrameSource, displayCopyIndex: options.GxFrameCopyIndex, out GxFifoSoftwareRenderResult? frame, out string? error, skipEfbCopyMemoryWrites: options.GxFrameSkipCopyMemoryWrites, memorySnapshots: gxMemorySnapshots);
            if (!rendered)
            {
                summary.WriteLine($"{skipDraws},,,,,,,,,,,,,,,,,,,,,,,,,,,\"{EscapeCsv(error ?? "unknown GX frame dump error")}\"");
                continue;
            }

            successes++;
            string sourceAddress = frame!.SourceAddress is uint address ? $"0x{address:X8}" : string.Empty;
            string sourceFormat = frame.SourceFormat?.ToString() ?? string.Empty;
            GxFifoSoftwareRenderLifecycle? lifecycle = frame.Lifecycle;
            GxFifoSoftwareRenderCopyMarker? selectedCopy = lifecycle?.SelectedCopy;
            GxFifoSoftwareRenderCopyMarker? lastDisplayCopy = lifecycle?.LastDisplayCopy;
            summary.WriteLine(string.Join(
                ',',
                skipDraws.ToString(CultureInfo.InvariantCulture),
                $"\"{EscapeCsv(frame.Path)}\"",
                frame.Source.ToString(),
                sourceAddress,
                sourceFormat,
                FormatNullableInt(frame.SourceCopyIndex),
                FormatNullableInt(selectedCopy?.CopyIndex),
                selectedCopy is GxFifoSoftwareRenderCopyMarker selectedMarker ? selectedMarker.IsDisplayCopy ? "display" : "texture" : string.Empty,
                FormatNullableInt(selectedCopy?.DrawsSeen),
                FormatNullableFifoOffset(selectedCopy?.FifoOffset),
                FormatNullableAddress(selectedCopy?.DestinationAddress),
                FormatNullableInt(lastDisplayCopy?.CopyIndex),
                FormatNullableInt(lastDisplayCopy?.DrawsSeen),
                FormatNullableFifoOffset(lastDisplayCopy?.FifoOffset),
                FormatNullableInt(lifecycle?.DrawsSinceLastDisplayCopy),
                FormatNullableInt(lifecycle?.CopyEventsSinceLastDisplayCopy),
                (lifecycle?.ClearsSinceLastDisplayCopy ?? 0).ToString(CultureInfo.InvariantCulture),
                (lifecycle?.TextureCopiesSinceLastDisplayCopy ?? 0).ToString(CultureInfo.InvariantCulture),
                lifecycle?.EfbWasClearedAfterLastDisplayCopy == true ? "True" : "False",
                lifecycle?.Phase ?? string.Empty,
                (lifecycle?.DisplayCopiesSeen ?? 0).ToString(CultureInfo.InvariantCulture),
                (lifecycle?.TextureCopiesSeen ?? 0).ToString(CultureInfo.InvariantCulture),
                frame.Draws.ToString(CultureInfo.InvariantCulture),
                frame.RenderedQuads.ToString(CultureInfo.InvariantCulture),
                frame.RenderedTriangles.ToString(CultureInfo.InvariantCulture),
                frame.DegenerateQuads.ToString(CultureInfo.InvariantCulture),
                frame.DegenerateTriangles.ToString(CultureInfo.InvariantCulture),
                string.Empty));
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

    private static string FormatNullableInt(int? value) =>
        value is int number ? number.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string FormatNullableAddress(uint? value) =>
        value is uint address ? $"0x{address:X8}" : string.Empty;

    private static string FormatNullableFifoOffset(int? value) =>
        value is int offset ? $"+0x{offset:X}" : string.Empty;

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

    private static void WriteRequestedMemoryBinaryDumps(RunDolOptions options, GameCubeBus bus, TextWriter output)
    {
        if (options.DumpMemoryBinaryRequests is not { Count: > 0 })
        {
            return;
        }

        foreach (MemoryBinaryDumpRequest request in options.DumpMemoryBinaryRequests.Distinct())
        {
            if (request.Instruction is not null)
            {
                continue;
            }

            WriteMemoryBinaryDump(bus, output, request);
        }
    }

    private static void WriteRequestedMemoryBinaryDumpsAtInstruction(RunDolOptions options, GameCubeBus bus, TextWriter output, int instruction)
    {
        if (options.DumpMemoryBinaryRequests is not { Count: > 0 })
        {
            return;
        }

        foreach (MemoryBinaryDumpRequest request in options.DumpMemoryBinaryRequests.Distinct())
        {
            if (request.Instruction != instruction)
            {
                continue;
            }

            WriteMemoryBinaryDump(bus, output, request);
        }
    }

    private static void WriteMemoryBinaryDump(GameCubeBus bus, TextWriter output, MemoryBinaryDumpRequest request)
    {
        if (!bus.Memory.IsMainRamAddress(request.Address, request.Length))
        {
            output.WriteLine($"Cannot dump 0x{request.Address:X8}+0x{request.Length:X}: range is outside main RAM.");
            return;
        }

        byte[] bytes = new byte[request.Length];
        for (int offset = 0; offset < bytes.Length; offset++)
        {
            bytes[offset] = bus.Memory.Read8(request.Address + (uint)offset);
        }

        string fullPath = Path.GetFullPath(request.Path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullPath, bytes);
        output.WriteLine($"Wrote {request.Length} byte(s) from 0x{request.Address:X8} to {fullPath}.");
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

        if (TryFastForwardSmallDataWordLoadLeaf(state, bus, pc, first, out skippedInstructions))
        {
            return true;
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

    private static bool TryFastForwardSmallDataWordLoadLeaf(PowerPcState state, GameCubeBus bus, uint pc, uint first, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!TryDecodeDForm(first, primaryOpcode: 32, out int targetRegister, out int baseRegister, out int offset)
            || targetRegister != 3
            || baseRegister != 13
            || bus.Read32(pc + 4) != 0x4E80_0020
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 2, extraInstructions: 0))
        {
            return false;
        }

        uint address = unchecked(state.Gpr[13] + (uint)offset);
        if (!bus.Memory.IsMainRamAddress(address, sizeof(uint)))
        {
            return false;
        }

        state.Gpr[3] = bus.Memory.Read32(address);
        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, 2);
        skippedInstructions = 2;
        return true;
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

        bool matchesSonicSignedLongDivisionLeaf = MatchesSonicSignedLongDivisionLeaf(bus, pc);
        if (MatchesSignedLongDivisionLeaf(bus, pc) || matchesSonicSignedLongDivisionLeaf)
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

            if (!matchesSonicSignedLongDivisionLeaf)
            {
                state.Gpr[1] = unchecked(state.Gpr[1] + 16);
            }

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

    private static bool MatchesSonicSignedLongDivisionLeaf(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x000) == 0x9421_FFF0
        && bus.Read32(pc + 0x004) == 0x5469_0001
        && bus.Read32(pc + 0x008) == 0x4182_000C
        && bus.Read32(pc + 0x014) == 0x9121_0008
        && bus.Read32(pc + 0x018) == 0x54A9_0001
        && bus.Read32(pc + 0x028) == 0x9121_000C
        && bus.Read32(pc + 0x02C) == 0x2C03_0000
        && bus.Read32(pc + 0x030) == 0x7C60_0034
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

    private static bool TryFastForwardAsciiCaseInsensitiveStringCompareLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesAsciiCaseInsensitiveStringCompareLoop(bus, pc))
        {
            return false;
        }

        uint leftAddress = state.Gpr[3];
        uint rightAddress = state.Gpr[4];
        uint comparedBytes = 0;
        uint skipped = 0;
        uint lastLeft = state.Gpr[6];
        uint lastRight = state.Gpr[7];
        uint lastMappedLeft = state.Gpr[5];
        uint lastMappedRight = state.Gpr[0];
        bool matched = false;
        bool finished = false;

        while (comparedBytes < MaxFastForwardStringCompareBytes)
        {
            uint leftCurrent = unchecked(leftAddress + comparedBytes);
            uint rightCurrent = unchecked(rightAddress + comparedBytes);
            if (!bus.Memory.IsMainRamAddress(leftCurrent, 1) || !bus.Memory.IsMainRamAddress(rightCurrent, 1))
            {
                return false;
            }

            byte left = bus.Memory.Read8(leftCurrent);
            byte right = bus.Memory.Read8(rightCurrent);
            uint iterationInstructions = 4;
            lastLeft = MapAsciiCaseInsensitiveCompareByte(left, out uint leftMapInstructions);
            lastRight = MapAsciiCaseInsensitiveCompareByte(right, out uint rightMapInstructions);
            iterationInstructions += leftMapInstructions + rightMapInstructions;
            lastMappedLeft = SignExtendByteToUInt32((byte)lastLeft);
            lastMappedRight = SignExtendByteToUInt32((byte)lastRight);
            iterationInstructions += 3;
            comparedBytes++;

            if (lastMappedLeft != lastMappedRight)
            {
                iterationInstructions += 3;
                skipped = checked(skipped + iterationInstructions);
                matched = false;
                finished = true;
                break;
            }

            iterationInstructions += 3;
            if (lastLeft == 0)
            {
                iterationInstructions += 2;
                skipped = checked(skipped + iterationInstructions);
                matched = true;
                finished = true;
                break;
            }

            skipped = checked(skipped + iterationInstructions);
        }

        if (!finished || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        state.Gpr[0] = lastMappedRight;
        state.Gpr[3] = matched ? 1u : 0u;
        state.Gpr[4] = unchecked(rightAddress + comparedBytes);
        state.Gpr[5] = lastMappedLeft;
        state.Gpr[6] = lastLeft;
        state.Gpr[7] = lastRight;
        if (matched)
        {
            SetCr0(state, 0);
        }
        else
        {
            SetCr0ForSignedCompare(state, lastMappedLeft, lastMappedRight);
        }

        state.Pc = state.Lr;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesAsciiCaseInsensitiveStringCompareLoop(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x88C3_0000
        && bus.Read32(pc + 0x04) == 0x3863_0001
        && bus.Read32(pc + 0x08) == 0x88E4_0000
        && bus.Read32(pc + 0x0C) == 0x3884_0001
        && bus.Read32(pc + 0x10) == 0x7CC0_0774
        && bus.Read32(pc + 0x14) == 0x2C00_0041
        && bus.Read32(pc + 0x18) == 0x4180_0010
        && bus.Read32(pc + 0x1C) == 0x2C00_005A
        && bus.Read32(pc + 0x20) == 0x4181_0008
        && bus.Read32(pc + 0x24) == 0x38C6_0020
        && bus.Read32(pc + 0x28) == 0x7CE0_0774
        && bus.Read32(pc + 0x2C) == 0x2C00_0041
        && bus.Read32(pc + 0x30) == 0x4180_0010
        && bus.Read32(pc + 0x34) == 0x2C00_005A
        && bus.Read32(pc + 0x38) == 0x4181_0008
        && bus.Read32(pc + 0x3C) == 0x38E7_0020
        && bus.Read32(pc + 0x40) == 0x7CC5_0774
        && bus.Read32(pc + 0x44) == 0x7CE0_0774
        && bus.Read32(pc + 0x48) == 0x7C05_0000
        && bus.Read32(pc + 0x4C) == 0x4082_0014
        && bus.Read32(pc + 0x50) == 0x7CC0_0775
        && bus.Read32(pc + 0x54) == 0x4082_FFAC
        && bus.Read32(pc + 0x58) == 0x3860_0001
        && bus.Read32(pc + 0x5C) == 0x4E80_0020
        && bus.Read32(pc + 0x60) == 0x3860_0000
        && bus.Read32(pc + 0x64) == 0x4E80_0020;

    private static uint MapAsciiCaseInsensitiveCompareByte(byte value, out uint instructionCount)
    {
        int signed = (sbyte)value;
        if (signed < 'A')
        {
            instructionCount = 3;
            return value;
        }

        if (signed > 'Z')
        {
            instructionCount = 5;
            return value;
        }

        instructionCount = 6;
        return (uint)(value + 0x20);
    }

    private static uint SignExtendByteToUInt32(byte value) => unchecked((uint)(sbyte)value);

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
        return TryFastForwardSonicPrsDecompressCore(state, bus, out skippedInstructions, trace: null, prsTrace: null, prsTraceAddress: null, prsTraceLength: null);
    }

    private static bool TryFastForwardSonicPrsDecompressCore(
        PowerPcState state,
        GameCubeBus bus,
        out int skippedInstructions,
        Action<string>? trace,
        Action<SonicPrsDecompressTraceEvent>? prsTrace,
        uint? prsTraceAddress,
        int? prsTraceLength)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicPrsDecompressRoutine(bus, pc))
        {
            return false;
        }

        uint source = state.Gpr[3];
        uint destination = state.Gpr[4];
        if (!TryDecodeSegaPrs(bus.Memory, source, out SegaPrsDecodeResult decodeResult, out uint sourceEnd, out string? decodeFailure))
        {
            trace?.Invoke($"decode failed pc=0x{pc:X8} source=0x{source:X8} destination=0x{destination:X8}: {decodeFailure}");
            return false;
        }

        byte[] output = decodeResult.Output;
        if (!bus.Memory.IsMainRamAddress(destination, output.Length))
        {
            trace?.Invoke($"destination out of range pc=0x{pc:X8} source=0x{source:X8} destination=0x{destination:X8} output=0x{output.Length:X}");
            return false;
        }

        uint skipped = SegaPrsDecoder.EstimateInstructionCount(decodeResult.SourceBytesConsumed, output.Length);
        if (!CanFastForwardInstructionCount(state, iterations: skipped, instructionsPerIteration: 1, extraInstructions: 0))
        {
            trace?.Invoke($"instruction budget rejected pc=0x{pc:X8} source=0x{source:X8} destination=0x{destination:X8} output=0x{output.Length:X} skipped=0x{skipped:X}");
            return false;
        }

        if (prsTrace is not null
            && prsTraceAddress is uint targetAddress
            && prsTraceLength is int targetLength
            && OverlapsAddressRange(destination, (uint)output.Length, targetAddress, targetLength))
        {
            int targetOutputOffset = checked((int)Math.Max(0L, (long)targetAddress - destination));
            int targetCaptureLength = Math.Min(0x100, Math.Min(targetLength, output.Length - targetOutputOffset));
            string targetOutputBytes = targetCaptureLength > 0
                ? Convert.ToHexString(output.AsSpan(targetOutputOffset, targetCaptureLength))
                : string.Empty;

            prsTrace(new SonicPrsDecompressTraceEvent(
                pc,
                source,
                sourceEnd,
                destination,
                output.Length,
                targetOutputOffset,
                decodeResult.LastFlagByte,
                decodeResult.BitsRemaining,
                skipped,
                CaptureMemoryWindowHex(bus.Memory, source, 0x80),
                targetOutputBytes,
                Convert.ToHexString(output.AsSpan(0, Math.Min(0x80, output.Length)))));
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
        state.Gpr[8] = decodeResult.LastFlagByte;
        state.Gpr[9] = (uint)decodeResult.BitsRemaining;
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

    private static bool TryDecodeSegaPrs(GameCubeMemory memory, uint source, out SegaPrsDecodeResult result, out uint sourceEnd, out string? failure)
    {
        result = default;
        sourceEnd = source;
        if (!memory.IsMainRamAddress(source, 1))
        {
            failure = $"source is not main RAM at 0x{source:X8}";
            return false;
        }

        int sourceOffset = memory.TranslateMainRam(source);
        ReadOnlyMemory<byte> sourceBytes = memory.MainRam[sourceOffset..];
        if (!SegaPrsDecoder.TryDecode(sourceBytes, out result, out failure))
        {
            failure = $"source 0x{source:X8}: {failure}";
            return false;
        }

        sourceEnd = source + (uint)result.SourceBytesConsumed;
        return true;
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

        if (MatchesSonicBitPlaneInnerExpand(bus, pc))
        {
            return TryFastForwardSonicBitPlaneInnerExpand(state, bus, pc, out skippedInstructions);
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

    private static bool TryFastForwardSonicBitPlaneInnerExpand(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Ctr == 0)
        {
            return false;
        }

        uint source = state.Gpr[31];
        uint destination = state.Gpr[7];
        uint bytes = Math.Min(state.Ctr, 3);
        if (!CanFastForwardInstructionCount(state, iterations: bytes, instructionsPerIteration: 80, extraInstructions: 0)
            || !bus.Memory.IsMainRamAddress(source, checked((int)bytes)))
        {
            return false;
        }

        uint skipped = 0;
        uint lastAddressMask = destination & 6;
        uint lastExtended = state.Gpr[11];
        for (uint byteIndex = 0; byteIndex < bytes; byteIndex++)
        {
            byte value = bus.Memory.Read8(source);
            source++;
            lastExtended = unchecked((uint)(sbyte)value);
            skipped += 3;

            for (int bit = 0; bit < 8; bit++)
            {
                bool bitSet = ((value >> bit) & 1) != 0;
                lastAddressMask = destination & 6;
                bool wraps = lastAddressMask == 6;
                if (bitSet)
                {
                    if (!bus.Memory.IsMainRamAddress(destination, sizeof(ushort)))
                    {
                        return false;
                    }

                    bus.Memory.Write16(destination, 0);
                }

                skipped += (uint)(bitSet ? 7 : 6) + (wraps ? 1u : 0u);
                destination = unchecked(destination + (wraps ? 0x1Au : 2u));
            }

            skipped++;
        }

        state.Gpr[10] = lastAddressMask;
        state.Gpr[11] = lastExtended;
        state.Gpr[31] = source;
        state.Gpr[7] = destination;
        state.Ctr -= bytes;
        SetCr0ForUnsignedCompareImmediate(state, lastAddressMask, 6);
        state.Pc = state.Ctr != 0 ? pc : pc + 0x130;

        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicBitPlaneInnerExpand(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x895F_0000
        && bus.Read32(pc + 0x04) == 0x3BFF_0001
        && bus.Read32(pc + 0x08) == 0x7D4B_0774
        && bus.Read32(pc + 0x0C) == 0x556A_07FF
        && bus.Read32(pc + 0x14) == 0xB007_0000
        && bus.Read32(pc + 0x18) == 0x54EA_077C
        && bus.Read32(pc + 0x24) == 0x38E7_001A
        && bus.Read32(pc + 0x2C) == 0x38E7_0002
        && bus.Read32(pc + 0x130) == 0x54C7_06F8;

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

    private static bool TryFastForwardSonicGxTexObjLoadNoCallback(PowerPcState state, GameCubeBus bus, TextWriter? textureBindTrace, int instructionIndex, long gxFifoOffset, out int skippedInstructions)
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

        if (textureBindTrace is not null)
        {
            WriteSonicTextureBindTraceRow(
                textureBindTrace,
                instructionIndex,
                gxFifoOffset,
                state,
                bus.Memory,
                textureObject,
                samplerObject,
                textureMap,
                stateBlock,
                objectFlags,
                word0,
                word4,
                word8,
                sampler0,
                sampler4,
                word12);
        }

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

    private static bool TryFastForwardSonicGxCpStateSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxCpStateSetter(bus, state.Pc)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicGxCpStateSetterInstructions, extraInstructions: 0))
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

        uint value = state.Gpr[3] & 0xFFu;
        uint cachedAddress = stateBlock + 0x204;
        uint cached = Rlwinm(bus.Memory.Read32(cachedAddress), 0, 0, 27) | value;
        bus.Memory.Write32(cachedAddress, cached);

        const uint fifo = 0xCC00_8000;
        bus.Write8(fifo, 0x10);
        bus.Write32(fifo, 0x0000_103Fu);
        bus.Write32(fifo, value);

        uint dirtyFlags = bus.Memory.Read32(stateBlock + 0x4F0) | 0x04u;
        bus.Memory.Write32(stateBlock + 0x4F0, dirtyFlags);

        state.Gpr[0] = dirtyFlags;
        state.Gpr[3] = stateBlock;
        state.Gpr[4] = 0xCC01_0000;
        state.Gpr[5] = cached;
        state.Gpr[6] = cachedAddress;
        state.Gpr[7] = value;
        state.Pc = state.Lr & 0xFFFF_FFFCu;

        AdvanceFastForwardedInstructions(state, bus, SonicGxCpStateSetterInstructions);
        skippedInstructions = checked((int)SonicGxCpStateSetterInstructions);
        return true;
    }

    private static bool MatchesSonicGxCpStateSetter(GameCubeBus bus, uint pc) =>
        pc == SonicGxCpStateSetterPc
        && bus.Read32(pc + 0x00) == 0x808D_8380
        && bus.Read32(pc + 0x04) == 0x5467_063E
        && bus.Read32(pc + 0x08) == 0x3860_0010
        && bus.Read32(pc + 0x0C) == 0x38C4_0204
        && bus.Read32(pc + 0x10) == 0x80A4_0204
        && bus.Read32(pc + 0x14) == 0x3C80_CC01
        && bus.Read32(pc + 0x18) == 0x3800_103F
        && bus.Read32(pc + 0x1C) == 0x54A5_0036
        && bus.Read32(pc + 0x20) == 0x7CA5_3B78
        && bus.Read32(pc + 0x24) == 0x90A6_0000
        && bus.Read32(pc + 0x28) == 0x9864_8000
        && bus.Read32(pc + 0x2C) == 0x806D_8380
        && bus.Read32(pc + 0x30) == 0x9004_8000
        && bus.Read32(pc + 0x34) == 0x90E4_8000
        && bus.Read32(pc + 0x38) == 0x8003_04F0
        && bus.Read32(pc + 0x3C) == 0x6000_0004
        && bus.Read32(pc + 0x40) == 0x9003_04F0
        && bus.Read32(pc + 0x44) == 0x4E80_0020;

    private static bool TryFastForwardSonicGxTevDefaultWrapper(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxTevDefaultWrapper(bus, state.Pc)
            || state.Gpr[3] != 0
            || state.Gpr[4] != 0
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicGxTevDefaultWrapperInstructions, extraInstructions: 0))
        {
            return false;
        }

        uint oldStackPointer = state.Gpr[1];
        uint newStackPointer = unchecked(oldStackPointer - 24u);
        if (!bus.Memory.IsMainRamAddress(newStackPointer, 32))
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

        uint oldLr = state.Lr;
        uint oldR30 = state.Gpr[30];
        uint oldR31 = state.Gpr[31];

        state.Gpr[0] = oldLr;
        state.Gpr[6] = 10;
        bus.Memory.Write32(oldStackPointer + 4, oldLr);
        bus.Memory.Write32(newStackPointer, oldStackPointer);
        state.Gpr[1] = newStackPointer;
        bus.Memory.Write32(newStackPointer + 20, oldR31);
        state.Gpr[31] = 5;
        bus.Memory.Write32(newStackPointer + 16, oldR30);
        state.Gpr[30] = 0;
        SetCr0(state, 0);

        int totalHelperInstructions = 0;

        state.Gpr[3] = 0;
        state.Gpr[4] = 15;
        state.Gpr[5] = 8;
        state.Gpr[6] = 10;
        state.Gpr[7] = 15;
        state.Lr = SonicGxTevDefaultWrapperPc + 0x6C;
        state.Pc = SonicGxTevColorEnvSetterPc;
        if (!TryFastForwardSonicGxTevColorEnvSetter(state, bus, out int helperInstructions))
        {
            return false;
        }

        totalHelperInstructions += helperInstructions;

        state.Gpr[3] = 0;
        state.Gpr[6] = 5;
        state.Gpr[4] = 7;
        state.Gpr[5] = 4;
        state.Gpr[7] = 7;
        state.Lr = SonicGxTevDefaultWrapperPc + 0x84;
        state.Pc = SonicGxTevAlphaEnvSetterPc;
        if (!TryFastForwardSonicGxTevAlphaEnvSetter(state, bus, out helperInstructions))
        {
            return false;
        }

        totalHelperInstructions += helperInstructions;

        state.Gpr[3] = 0;
        state.Gpr[4] = 0;
        state.Gpr[5] = 0;
        state.Gpr[6] = 0;
        state.Gpr[7] = 1;
        state.Gpr[8] = 0;
        state.Lr = SonicGxTevDefaultWrapperPc + 0x170;
        state.Pc = SonicGxTevColorOpSetterPc;
        if (!TryFastForwardSonicGxTevColorOpSetter(state, bus, out helperInstructions))
        {
            return false;
        }

        totalHelperInstructions += helperInstructions;

        state.Gpr[3] = 0;
        state.Gpr[4] = 0;
        state.Gpr[5] = 0;
        state.Gpr[6] = 0;
        state.Gpr[7] = 1;
        state.Gpr[8] = 0;
        state.Lr = SonicGxTevDefaultWrapperPc + 0x18C;
        state.Pc = SonicGxTevAlphaOpSetterPc;
        if (!TryFastForwardSonicGxTevAlphaOpSetter(state, bus, out helperInstructions))
        {
            return false;
        }

        totalHelperInstructions += helperInstructions;

        state.Gpr[0] = bus.Memory.Read32(newStackPointer + 28);
        state.Gpr[31] = bus.Memory.Read32(newStackPointer + 20);
        state.Gpr[30] = bus.Memory.Read32(newStackPointer + 16);
        state.Lr = state.Gpr[0];
        state.Gpr[1] = unchecked(newStackPointer + 24u);
        state.Pc = state.Lr & 0xFFFF_FFFCu;

        AdvanceFastForwardedInstructions(state, bus, SonicGxTevDefaultWrapperOwnInstructions);
        skippedInstructions = checked(totalHelperInstructions + (int)SonicGxTevDefaultWrapperOwnInstructions);
        return true;
    }

    private static bool MatchesSonicGxTevDefaultWrapper(GameCubeBus bus, uint pc)
    {
        if (pc != SonicGxTevDefaultWrapperPc)
        {
            return false;
        }

        ReadOnlySpan<uint> instructions =
        [
            0x7C08_02A6, 0x38C0_000A, 0x9001_0004, 0x9421_FFE8,
            0x93E1_0014, 0x3BE0_0005, 0x93C1_0010, 0x7C7E_1B79,
            0x4182_000C, 0x38C0_0000, 0x3BE0_0000, 0x2C04_0002,
            0x4182_008C, 0x4080_0014, 0x2C04_0000, 0x4182_001C,
            0x4080_0048, 0x4800_0110, 0x2C04_0004, 0x4182_00D8,
            0x4080_0104, 0x4800_009C, 0x387E_0000, 0x3880_000F,
            0x38A0_0008, 0x38E0_000F, 0x4800_013D, 0x387E_0000,
            0x38DF_0000, 0x3880_0007, 0x38A0_0004, 0x38E0_0007,
            0x4800_01A5, 0x4800_00D0, 0x387E_0000, 0x3886_0000,
            0x38A0_0008, 0x38C0_0009, 0x38E0_000F, 0x4800_0109,
            0x387E_0000, 0x38FF_0000, 0x3880_0007, 0x38A0_0007,
            0x38C0_0007, 0x4800_0171, 0x4800_009C, 0x387E_0000,
            0x3886_0000, 0x38A0_000C, 0x38C0_0008, 0x38E0_000F,
            0x4800_00D5, 0x387E_0000, 0x38DF_0000, 0x3880_0007,
            0x38A0_0004, 0x38E0_0007, 0x4800_013D, 0x4800_0068,
            0x387E_0000, 0x3880_000F, 0x38A0_000F, 0x38C0_000F,
            0x38E0_0008, 0x4800_00A1, 0x387E_0000, 0x3880_0007,
            0x38A0_0007, 0x38C0_0007, 0x38E0_0004, 0x4800_0109,
            0x4800_0034, 0x387E_0000, 0x38E6_0000, 0x3880_000F,
            0x38A0_000F, 0x38C0_000F, 0x4800_006D, 0x387E_0000,
            0x38FF_0000, 0x3880_0007, 0x38A0_0007, 0x38C0_0007,
            0x4800_00D5, 0x387E_0000, 0x3880_0000, 0x38A0_0000,
            0x38C0_0000, 0x38E0_0001, 0x3900_0000, 0x4800_013D,
            0x387E_0000, 0x3880_0000, 0x38A0_0000, 0x38C0_0000,
            0x38E0_0001, 0x3900_0000, 0x4800_01E1, 0x8001_001C,
            0x83E1_0014, 0x83C1_0010, 0x7C08_03A6, 0x3821_0018,
            0x4E80_0020,
        ];

        for (int index = 0; index < instructions.Length; index++)
        {
            if (bus.Read32(pc + (uint)(index * sizeof(uint))) != instructions[index])
            {
                return false;
            }
        }

        return MatchesSonicGxTevColorEnvSetter(bus, SonicGxTevColorEnvSetterPc)
            && MatchesSonicGxTevAlphaEnvSetter(bus, SonicGxTevAlphaEnvSetterPc)
            && MatchesSonicGxTevColorOpSetter(bus, SonicGxTevColorOpSetterPc)
            && MatchesSonicGxTevAlphaOpSetter(bus, SonicGxTevAlphaOpSetterPc);
    }

    private static bool TryFastForwardSonicGxTevColorEnvSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (MatchesSonicGxTevColorEnvSetterTail(bus, state.Pc))
        {
            return TryFastForwardSonicGxTevColorEnvSetterTail(state, bus, out skippedInstructions);
        }

        if (!MatchesSonicGxTevColorEnvSetter(bus, state.Pc)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicGxTevColorEnvSetterInstructions, extraInstructions: 0))
        {
            return false;
        }

        uint tevStage = state.Gpr[3];
        if (tevStage > 15)
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

        uint cacheAddress = stateBlock + 0x130u + tevStage * sizeof(uint);
        uint arg4Bits = Rlwinm(state.Gpr[4], 12, 0, 19);
        uint arg5Bits = Rlwinm(state.Gpr[5], 8, 0, 23);
        uint arg6Bits = Rlwinm(state.Gpr[6], 4, 0, 27);

        uint initialWord = bus.Memory.Read32(cacheAddress);
        uint word = initialWord;
        word = Rlwinm(word, 0, 20, 15) | arg4Bits;
        bus.Memory.Write32(cacheAddress, word);

        word = Rlwinm(bus.Memory.Read32(cacheAddress), 0, 24, 19) | arg5Bits;
        bus.Memory.Write32(cacheAddress, word);

        uint maskedBeforeArg6 = Rlwinm(bus.Memory.Read32(cacheAddress), 0, 28, 23);
        word = maskedBeforeArg6 | arg6Bits;
        bus.Memory.Write32(cacheAddress, word);

        word = Rlwinm(bus.Memory.Read32(cacheAddress), 0, 0, 27) | state.Gpr[7];
        bus.Memory.Write32(cacheAddress, word);

        const uint fifo = 0xCC00_8000;
        bus.Write8(fifo, 0x61);
        bus.Write32(fifo, word);
        bus.Memory.Write16(stateBlock + 2, 0);

        state.Gpr[0] = 0;
        state.Gpr[3] = stateBlock;
        state.Gpr[4] = word;
        state.Gpr[5] = 0xCC01_0000;
        state.Gpr[6] = maskedBeforeArg6;
        state.Gpr[8] = arg5Bits;
        state.Gpr[9] = cacheAddress;
        state.Pc = state.Lr & 0xFFFF_FFFCu;

        AdvanceFastForwardedInstructions(state, bus, SonicGxTevColorEnvSetterInstructions);
        skippedInstructions = checked((int)SonicGxTevColorEnvSetterInstructions);
        return true;
    }

    private static bool TryFastForwardSonicGxTevColorEnvSetterTail(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicGxTevColorEnvSetterTailInstructions, extraInstructions: 0)
            || state.Gpr[5] != 0xCC01_0000
            || !bus.Memory.IsMainRamAddress(state.Gpr[9], sizeof(uint)))
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

        uint maskedBeforeArg6 = Rlwinm(state.Gpr[6], 0, 28, 23);
        state.Gpr[6] = maskedBeforeArg6;
        uint sourceWord = maskedBeforeArg6 | state.Gpr[4];
        state.Gpr[4] = sourceWord;
        bus.Memory.Write32(state.Gpr[9], sourceWord);

        uint word = Rlwinm(sourceWord, 0, 0, 27) | state.Gpr[7];
        bus.Memory.Write32(state.Gpr[9], word);

        const uint fifo = 0xCC00_8000;
        bus.Write8(fifo, (byte)state.Gpr[3]);
        state.Gpr[3] = stateBlock;
        state.Gpr[4] = word;
        bus.Write32(fifo, word);
        bus.Memory.Write16(stateBlock + 2, (ushort)state.Gpr[0]);
        state.Pc = state.Lr & 0xFFFF_FFFCu;

        AdvanceFastForwardedInstructions(state, bus, SonicGxTevColorEnvSetterTailInstructions);
        skippedInstructions = checked((int)SonicGxTevColorEnvSetterTailInstructions);
        return true;
    }

    private static bool MatchesSonicGxTevColorEnvSetter(GameCubeBus bus, uint pc)
    {
        if (pc != SonicGxTevColorEnvSetterPc)
        {
            return false;
        }

        ReadOnlySpan<uint> instructions =
        [
            0x5463_103A, 0x800D_8380, 0x3923_0130, 0x7D20_4A14,
            0x8069_0000, 0x5480_6026, 0x54A8_402E, 0x5463_051E,
            0x7C60_0378, 0x9009_0000, 0x54C4_2036, 0x3860_0061,
            0x80C9_0000, 0x3CA0_CC01, 0x3800_0000, 0x54C6_0626,
            0x7CC6_4378, 0x90C9_0000, 0x80C9_0000, 0x54C6_072E,
            0x7CC4_2378, 0x9089_0000, 0x8089_0000, 0x5484_0036,
            0x7C84_3B78, 0x9089_0000, 0x9865_8000, 0x806D_8380,
            0x8089_0000, 0x9085_8000, 0xB003_0002, 0x4E80_0020,
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

    private static bool MatchesSonicGxTevColorEnvSetterTail(GameCubeBus bus, uint pc) =>
        pc == SonicGxTevColorEnvSetterTailPc
        && bus.Read32(pc + 0x00) == 0x54C6_072E
        && bus.Read32(pc + 0x04) == 0x7CC4_2378
        && bus.Read32(pc + 0x08) == 0x9089_0000
        && bus.Read32(pc + 0x0C) == 0x8089_0000
        && bus.Read32(pc + 0x10) == 0x5484_0036
        && bus.Read32(pc + 0x14) == 0x7C84_3B78
        && bus.Read32(pc + 0x18) == 0x9089_0000
        && bus.Read32(pc + 0x1C) == 0x9865_8000
        && bus.Read32(pc + 0x20) == 0x806D_8380
        && bus.Read32(pc + 0x24) == 0x8089_0000
        && bus.Read32(pc + 0x28) == 0x9085_8000
        && bus.Read32(pc + 0x2C) == 0xB003_0002
        && bus.Read32(pc + 0x30) == 0x4E80_0020;

    private static bool TryFastForwardSonicGxTevAlphaEnvSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxTevAlphaEnvSetter(bus, state.Pc)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicGxTevAlphaEnvSetterInstructions, extraInstructions: 0))
        {
            return false;
        }

        uint tevStage = state.Gpr[3];
        if (tevStage > 15)
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

        uint cacheAddress = stateBlock + 0x170u + tevStage * sizeof(uint);
        uint arg4Bits = Rlwinm(state.Gpr[4], 13, 0, 18);
        uint arg5Bits = Rlwinm(state.Gpr[5], 10, 0, 21);
        uint arg6Bits = Rlwinm(state.Gpr[6], 7, 0, 24);
        uint arg7Bits = Rlwinm(state.Gpr[7], 4, 0, 27);

        uint initialWord = bus.Memory.Read32(cacheAddress);
        uint word = initialWord;
        word = Rlwinm(word, 0, 19, 15) | arg4Bits;
        bus.Memory.Write32(cacheAddress, word);

        word = Rlwinm(bus.Memory.Read32(cacheAddress), 0, 22, 18) | arg5Bits;
        bus.Memory.Write32(cacheAddress, word);

        uint maskedBeforeArg6 = Rlwinm(bus.Memory.Read32(cacheAddress), 0, 25, 21);
        word = maskedBeforeArg6 | arg6Bits;
        bus.Memory.Write32(cacheAddress, word);

        uint maskedBeforeArg7 = Rlwinm(bus.Memory.Read32(cacheAddress), 0, 28, 24);
        word = maskedBeforeArg7 | arg7Bits;
        bus.Memory.Write32(cacheAddress, word);

        const uint fifo = 0xCC00_8000;
        bus.Write8(fifo, 0x61);
        bus.Write32(fifo, word);
        bus.Memory.Write16(stateBlock + 2, 0);

        state.Gpr[0] = 0;
        state.Gpr[3] = stateBlock;
        state.Gpr[4] = word;
        state.Gpr[5] = 0xCC01_0000;
        state.Gpr[6] = maskedBeforeArg7;
        state.Gpr[7] = maskedBeforeArg6;
        state.Gpr[8] = initialWord;
        state.Gpr[9] = cacheAddress;
        state.Pc = state.Lr & 0xFFFF_FFFCu;

        AdvanceFastForwardedInstructions(state, bus, SonicGxTevAlphaEnvSetterInstructions);
        skippedInstructions = checked((int)SonicGxTevAlphaEnvSetterInstructions);
        return true;
    }

    private static bool MatchesSonicGxTevAlphaEnvSetter(GameCubeBus bus, uint pc)
    {
        if (pc != SonicGxTevAlphaEnvSetterPc)
        {
            return false;
        }

        ReadOnlySpan<uint> instructions =
        [
            0x5463_103A, 0x800D_8380, 0x3923_0170, 0x7D20_4A14,
            0x8109_0000, 0x5483_6824, 0x54A0_502A, 0x5504_04DE,
            0x7C83_1B78, 0x9069_0000, 0x54C6_3830, 0x54E4_2036,
            0x80E9_0000, 0x3860_0061, 0x3CA0_CC01, 0x54E7_05A4,
            0x7CE0_0378, 0x9009_0000, 0x3800_0000, 0x80E9_0000,
            0x54E7_066A, 0x7CE6_3378, 0x90C9_0000, 0x80C9_0000,
            0x54C6_0730, 0x7CC4_2378, 0x9089_0000, 0x9865_8000,
            0x806D_8380, 0x8089_0000, 0x9085_8000, 0xB003_0002,
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

    private static bool TryFastForwardSonicGxTevColorOpSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions) =>
        TryFastForwardSonicGxTevOpSetter(state, bus, cacheBaseOffset: 0x130, color: true, out skippedInstructions);

    private static bool TryFastForwardSonicGxTevAlphaOpSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions) =>
        TryFastForwardSonicGxTevOpSetter(state, bus, cacheBaseOffset: 0x170, color: false, out skippedInstructions);

    private static bool TryFastForwardSonicGxTevOpSetter(
        PowerPcState state,
        GameCubeBus bus,
        uint cacheBaseOffset,
        bool color,
        out int skippedInstructions)
    {
        skippedInstructions = 0;
        bool highMode = unchecked((int)state.Gpr[4]) > 1;
        uint instructionCount = highMode ? SonicGxTevOpSetterHighModeInstructions : SonicGxTevOpSetterLowModeInstructions;
        if (!(color ? MatchesSonicGxTevColorOpSetter(bus, state.Pc) : MatchesSonicGxTevAlphaOpSetter(bus, state.Pc))
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: instructionCount, extraInstructions: 0))
        {
            return false;
        }

        uint tevStage = state.Gpr[3];
        if (tevStage > 15)
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

        uint cacheAddress = stateBlock + cacheBaseOffset + tevStage * sizeof(uint);
        uint mode = state.Gpr[4];
        uint arg5 = state.Gpr[5];
        uint arg6 = state.Gpr[6];
        uint arg7 = state.Gpr[7];
        uint arg8 = state.Gpr[8];

        uint word = bus.Memory.Read32(cacheAddress);
        SetCr0ForSignedCompareImmediate(state, mode, 1);
        word = Rlwimi(Rlwinm(word, 0, 14, 12), mode, 18, 13, 13);
        bus.Memory.Write32(cacheAddress, word);

        uint r9 = state.Gpr[9];
        if (!highMode)
        {
            r9 = bus.Memory.Read32(cacheAddress);
            uint mode6Bits = Rlwinm(arg6, 20, 0, 11);
            uint mode5Bits = Rlwinm(arg5, 16, 0, 15);
            uint masked = Rlwinm(r9, 0, 12, 9);
            state.Gpr[5] = masked;
            state.Gpr[4] = masked | mode6Bits;
            bus.Memory.Write32(cacheAddress, state.Gpr[4]);

            state.Gpr[4] = Rlwinm(bus.Memory.Read32(cacheAddress), 0, 16, 13);
            word = state.Gpr[4] | mode5Bits;
            bus.Memory.Write32(cacheAddress, word);
        }
        else
        {
            word = bus.Memory.Read32(cacheAddress);
            word = Rlwimi(Rlwinm(word, 0, 12, 9), mode, 19, 10, 11);
            bus.Memory.Write32(cacheAddress, word);

            word = Rlwinm(bus.Memory.Read32(cacheAddress), 0, 16, 13) | 0x0003_0000u;
            bus.Memory.Write32(cacheAddress, word);
        }

        state.Gpr[4] = bus.Memory.Read32(cacheAddress);
        word = Rlwinm(arg7, 19, 5, 12);
        uint arg8Bits = Rlwinm(arg8, 22, 0, 9);
        state.Gpr[4] = Rlwinm(state.Gpr[4], 0, 13, 11);
        word = state.Gpr[4] | word;
        bus.Memory.Write32(cacheAddress, word);

        const uint fifo = 0xCC00_8000;
        state.Gpr[4] = 0x61;
        state.Gpr[5] = 0xCC01_0000;
        state.Gpr[7] = Rlwinm(bus.Memory.Read32(cacheAddress), 0, 10, 7);
        state.Gpr[0] = 0;
        state.Gpr[6] = state.Gpr[7] | arg8Bits;
        bus.Memory.Write32(cacheAddress, state.Gpr[6]);

        bus.Write8(fifo, (byte)state.Gpr[4]);
        state.Gpr[4] = stateBlock;
        state.Gpr[3] = bus.Memory.Read32(cacheAddress);
        bus.Write32(fifo, state.Gpr[3]);
        bus.Memory.Write16(stateBlock + 2, 0);
        state.Gpr[9] = r9;
        state.Pc = state.Lr & 0xFFFF_FFFCu;

        AdvanceFastForwardedInstructions(state, bus, instructionCount);
        skippedInstructions = checked((int)instructionCount);
        return true;
    }

    private static bool MatchesSonicGxTevColorOpSetter(GameCubeBus bus, uint pc) =>
        pc == SonicGxTevColorOpSetterPc
        && MatchesSonicGxTevOpSetterBody(bus, pc, 0x3863_0130, 0x808D_8380);

    private static bool MatchesSonicGxTevAlphaOpSetter(GameCubeBus bus, uint pc) =>
        pc == SonicGxTevAlphaOpSetterPc
        && MatchesSonicGxTevOpSetterBody(bus, pc, 0x3863_0170, 0x808D_8380);

    private static bool MatchesSonicGxTevOpSetterBody(GameCubeBus bus, uint pc, uint cacheOffsetInstruction, uint finalStateBlockLoadInstruction) =>
        bus.Read32(pc + 0x00) == 0x5463_103A
        && bus.Read32(pc + 0x04) == 0x800D_8380
        && bus.Read32(pc + 0x08) == cacheOffsetInstruction
        && bus.Read32(pc + 0x0C) == 0x7C60_1A14
        && bus.Read32(pc + 0x10) == 0x8003_0000
        && bus.Read32(pc + 0x14) == 0x2C04_0001
        && bus.Read32(pc + 0x18) == 0x5400_0398
        && bus.Read32(pc + 0x1C) == 0x5080_935A
        && bus.Read32(pc + 0x20) == 0x9003_0000
        && bus.Read32(pc + 0x24) == 0x4181_0030
        && bus.Read32(pc + 0x28) == 0x8123_0000
        && bus.Read32(pc + 0x2C) == 0x54C4_A016
        && bus.Read32(pc + 0x30) == 0x54A0_801E
        && bus.Read32(pc + 0x34) == 0x5525_0312
        && bus.Read32(pc + 0x38) == 0x7CA4_2378
        && bus.Read32(pc + 0x3C) == 0x9083_0000
        && bus.Read32(pc + 0x40) == 0x8083_0000
        && bus.Read32(pc + 0x44) == 0x5484_041A
        && bus.Read32(pc + 0x48) == 0x7C80_0378
        && bus.Read32(pc + 0x4C) == 0x9003_0000
        && bus.Read32(pc + 0x50) == 0x4800_0024
        && bus.Read32(pc + 0x54) == 0x8003_0000
        && bus.Read32(pc + 0x58) == 0x5400_0312
        && bus.Read32(pc + 0x5C) == 0x5080_9A96
        && bus.Read32(pc + 0x60) == 0x9003_0000
        && bus.Read32(pc + 0x64) == 0x8003_0000
        && bus.Read32(pc + 0x68) == 0x5400_041A
        && bus.Read32(pc + 0x6C) == 0x6400_0003
        && bus.Read32(pc + 0x70) == 0x9003_0000
        && bus.Read32(pc + 0x74) == 0x8083_0000
        && bus.Read32(pc + 0x78) == 0x54E0_9958
        && bus.Read32(pc + 0x7C) == 0x5506_B012
        && bus.Read32(pc + 0x80) == 0x5484_0356
        && bus.Read32(pc + 0x84) == 0x7C80_0378
        && bus.Read32(pc + 0x88) == 0x9003_0000
        && bus.Read32(pc + 0x8C) == 0x3880_0061
        && bus.Read32(pc + 0x90) == 0x3CA0_CC01
        && bus.Read32(pc + 0x94) == 0x80E3_0000
        && bus.Read32(pc + 0x98) == 0x3800_0000
        && bus.Read32(pc + 0x9C) == 0x54E7_028E
        && bus.Read32(pc + 0xA0) == 0x7CE6_3378
        && bus.Read32(pc + 0xA4) == 0x90C3_0000
        && bus.Read32(pc + 0xA8) == 0x9885_8000
        && bus.Read32(pc + 0xAC) == finalStateBlockLoadInstruction
        && bus.Read32(pc + 0xB0) == 0x8063_0000
        && bus.Read32(pc + 0xB4) == 0x9065_8000
        && bus.Read32(pc + 0xB8) == 0xB004_0002
        && bus.Read32(pc + 0xBC) == 0x4E80_0020;

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

    private static bool TryFastForwardSonicGxBeginDirect(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicGxBeginDirect(bus, state.Pc))
        {
            return false;
        }

        uint stateBlockPointerAddress = unchecked(state.Gpr[13] + 0xFFFF_8380u);
        if (!bus.Memory.IsMainRamAddress(stateBlockPointerAddress, sizeof(uint)))
        {
            return false;
        }

        uint stateBlock = bus.Memory.Read32(stateBlockPointerAddress);
        if (!bus.Memory.IsMainRamAddress(stateBlock, 0x420))
        {
            return false;
        }

        uint word14 = bus.Memory.Read32(stateBlock + 0x14);
        uint word18 = bus.Memory.Read32(stateBlock + 0x18);
        uint skipped = 0;

        skipped += 4;
        uint positionFlag = Rlwinm(word14, 19, 30, 31) != 0 ? 1u : 0u;
        skipped += positionFlag != 0 ? 2u : 1u;

        skipped += 2;
        uint normalFlag = Rlwinm(word14, 17, 30, 31) != 0 ? 1u : 0u;
        skipped += normalFlag != 0 ? 2u : 1u;

        skipped += 4;
        uint positionNormalCount = positionFlag + normalFlag;
        uint matrixMode;
        if (bus.Memory.Read8(stateBlock + 0x41D) != 0)
        {
            matrixMode = 2;
            skipped += 2;
        }
        else
        {
            skipped += 3;
            if (bus.Memory.Read8(stateBlock + 0x41C) != 0)
            {
                matrixMode = 1;
                skipped += 2;
            }
            else
            {
                matrixMode = 0;
                skipped += 1;
            }
        }

        uint attributeCount = 0;
        uint finalAttributeFlag = 0;
        uint finalAttributeRegister = 0;
        ApplySonicGxBeginAttributeFlag(word18, shift: 0, leadingInstructions: 3, ref skipped, ref attributeCount, ref finalAttributeFlag, ref finalAttributeRegister);
        ApplySonicGxBeginAttributeFlag(word18, shift: 30, leadingInstructions: 2, ref skipped, ref attributeCount, ref finalAttributeFlag, ref finalAttributeRegister);
        ApplySonicGxBeginAttributeFlag(word18, shift: 28, leadingInstructions: 3, ref skipped, ref attributeCount, ref finalAttributeFlag, ref finalAttributeRegister);
        ApplySonicGxBeginAttributeFlag(word18, shift: 26, leadingInstructions: 3, ref skipped, ref attributeCount, ref finalAttributeFlag, ref finalAttributeRegister);
        ApplySonicGxBeginAttributeFlag(word18, shift: 24, leadingInstructions: 3, ref skipped, ref attributeCount, ref finalAttributeFlag, ref finalAttributeRegister);
        ApplySonicGxBeginAttributeFlag(word18, shift: 22, leadingInstructions: 3, ref skipped, ref attributeCount, ref finalAttributeFlag, ref finalAttributeRegister);
        ApplySonicGxBeginAttributeFlag(word18, shift: 20, leadingInstructions: 3, ref skipped, ref attributeCount, ref finalAttributeFlag, ref finalAttributeRegister);
        ApplySonicGxBeginAttributeFlag(word18, shift: 18, leadingInstructions: 3, ref skipped, ref attributeCount, ref finalAttributeFlag, ref finalAttributeRegister);

        const uint tailInstructions = 15;
        skipped += tailInstructions;
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        const uint fifo = 0xCC00_8000;
        uint descriptorWord = (attributeCount << 4) | positionNormalCount | (matrixMode << 2);
        bus.Write8(fifo, 0x10);
        bus.Write32(fifo, 0x0000_1008u);
        bus.Write32(fifo, descriptorWord);
        bus.Memory.Write16(stateBlock + 2, 1);

        state.Gpr[0] = 1;
        state.Gpr[3] = stateBlock;
        state.Gpr[4] = attributeCount << 4;
        state.Gpr[5] = 0xCC01_0000;
        state.Gpr[6] = finalAttributeRegister;
        state.Gpr[7] = positionNormalCount;
        state.Gpr[8] = attributeCount;
        state.Pc = state.Lr & 0xFFFF_FFFCu;
        SetCr0(state, finalAttributeFlag);

        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static void ApplySonicGxBeginAttributeFlag(
        uint word,
        int shift,
        uint leadingInstructions,
        ref uint skipped,
        ref uint attributeCount,
        ref uint finalAttributeFlag,
        ref uint finalAttributeRegister)
    {
        skipped += leadingInstructions;
        uint masked = Rlwinm(word, shift, 30, 31);
        uint flag = masked != 0 ? 1u : 0u;
        skipped += flag != 0 ? 2u : 1u;
        attributeCount += flag;
        finalAttributeFlag = masked;
        finalAttributeRegister = flag;
    }

    private static bool MatchesSonicGxBeginDirect(GameCubeBus bus, uint pc) =>
        pc == SonicGxBeginDirectPc
        && bus.Read32(pc + 0x00) == 0x80AD_8380
        && bus.Read32(pc + 0x04) == 0x8085_0014
        && bus.Read32(pc + 0x08) == 0x5480_9FBF
        && bus.Read32(pc + 0x0C) == 0x4182_000C
        && bus.Read32(pc + 0x1C) == 0x5480_8FBF
        && bus.Read32(pc + 0x30) == 0x8805_041D
        && bus.Read32(pc + 0x48) == 0x8805_041C
        && bus.Read32(pc + 0x60) == 0x80C5_0018
        && bus.Read32(pc + 0x64) == 0x54C0_07BF
        && bus.Read32(pc + 0x78) == 0x54C0_F7BF
        && bus.Read32(pc + 0x8C) == 0x54C0_E7BF
        && bus.Read32(pc + 0xA4) == 0x54C0_D7BF
        && bus.Read32(pc + 0xBC) == 0x54C0_C7BF
        && bus.Read32(pc + 0xD4) == 0x54C0_B7BF
        && bus.Read32(pc + 0xEC) == 0x54C0_A7BF
        && bus.Read32(pc + 0x104) == 0x54C0_97BF
        && bus.Read32(pc + 0x11C) == 0x3800_0010
        && bus.Read32(pc + 0x124) == 0x3CA0_CC01
        && bus.Read32(pc + 0x128) == 0x7D08_3214
        && bus.Read32(pc + 0x12C) == 0x9805_8000
        && bus.Read32(pc + 0x130) == 0x5480_103A
        && bus.Read32(pc + 0x134) == 0x3880_1008
        && bus.Read32(pc + 0x138) == 0x9085_8000
        && bus.Read32(pc + 0x13C) == 0x5504_2036
        && bus.Read32(pc + 0x140) == 0x7CE0_0378
        && bus.Read32(pc + 0x144) == 0x7C80_0378
        && bus.Read32(pc + 0x148) == 0x9005_8000
        && bus.Read32(pc + 0x14C) == 0x3800_0001
        && bus.Read32(pc + 0x150) == 0xB003_0002
        && bus.Read32(pc + 0x154) == 0x4E80_0020;

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
        if (!MatchesSonicGxCommandListFetch(bus, state.Pc))
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

        uint skipped = SonicGxCommandListFetchInstructions;
        bool canFuseDispatch = MatchesSonicGxCommandDispatchHeader(bus, SonicGxCommandDispatchHeaderPc);
        if (canFuseDispatch)
        {
            skipped += EstimateSonicGxCommandDispatchBranchInstructions(command);
        }

        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        state.Gpr[20] = stream + sizeof(ushort);
        state.Gpr[28] = unchecked((uint)command);
        SetCr0ForSignedCompareImmediate(state, unchecked((uint)command), 255);
        if (canFuseDispatch)
        {
            FastForwardSonicGxCommandDispatchBranch(state, command);
        }
        else
        {
            state.Pc = SonicGxCommandDispatchHeaderPc;
        }

        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
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

        if (MatchesSonicGxCommandMaskUpdate(bus, state.Pc))
        {
            uint controlAddress = unchecked(state.Gpr[13] - 29144u);
            uint maskAddress = unchecked(state.Gpr[13] - 29148u);
            uint orAddress = unchecked(state.Gpr[13] - 29152u);
            uint cachedValueAddress = unchecked(state.Gpr[13] - 29056u);
            uint dirtyValueAddress = unchecked(state.Gpr[13] - 29052u);
            if (!bus.Memory.IsMainRamAddress(controlAddress, sizeof(uint))
                || !bus.Memory.IsMainRamAddress(maskAddress, sizeof(uint))
                || !bus.Memory.IsMainRamAddress(orAddress, sizeof(uint))
                || !bus.Memory.IsMainRamAddress(cachedValueAddress, sizeof(uint))
                || !bus.Memory.IsMainRamAddress(dirtyValueAddress, sizeof(uint)))
            {
                return false;
            }

            uint controlFlag = Rlwinm(bus.Memory.Read32(controlAddress), 0, 20, 20);
            uint skipped = controlFlag == 0 ? 16u : 20u;
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            state.Gpr[0] = controlFlag;
            SetCr0ForUnsignedCompareImmediate(state, controlFlag, 0);
            if (controlFlag != 0)
            {
                state.Gpr[0] = bus.Memory.Read32(maskAddress);
                state.Gpr[28] &= state.Gpr[0];
                state.Gpr[0] = bus.Memory.Read32(orAddress);
                state.Gpr[28] |= state.Gpr[0];
            }

            state.Gpr[3] = unchecked((uint)(short)state.Gpr[28]);
            state.Gpr[0] = 8;
            state.Gpr[0] = ShiftRightAlgebraicWord(state, state.Gpr[3], state.Gpr[0]);
            state.Gpr[28] = unchecked((uint)(short)state.Gpr[0]);
            state.Gpr[0] = bus.Memory.Read32(cachedValueAddress);
            state.Gpr[4] = state.Gpr[28] ^ state.Gpr[0];
            state.Gpr[0] = bus.Memory.Read32(dirtyValueAddress);
            state.Gpr[4] |= state.Gpr[0];
            state.Gpr[3] = state.Gpr[28];
            bus.Memory.Write32(cachedValueAddress, state.Gpr[3]);
            state.Gpr[0] = 0;
            bus.Memory.Write32(dirtyValueAddress, state.Gpr[0]);
            state.Pc = SonicGxCommandMaskUpdatePc + 0x50;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }

        return false;
    }

    private static uint EstimateSonicGxCommandDispatchBranchInstructions(short command)
    {
        uint dispatchCommand = unchecked((uint)command) & 0xFFu;
        if (dispatchCommand < 8)
        {
            return 4;
        }

        return dispatchCommand < 16 ? 6u : 8u;
    }

    private static void FastForwardSonicGxCommandDispatchBranch(PowerPcState state, short command)
    {
        uint dispatchCommand = unchecked((uint)command) & 0xFFu;
        state.Gpr[0] = dispatchCommand;
        state.Gpr[25] = dispatchCommand;
        SetCr0ForSignedCompareImmediate(state, dispatchCommand, 8);
        if (dispatchCommand < 8)
        {
            state.Pc = SonicGxCommandDispatchHeaderPc + 0x10;
            return;
        }

        SetCr0ForSignedCompareImmediate(state, dispatchCommand, 16);
        if (dispatchCommand < 16)
        {
            state.Pc = SonicGxCommandDispatchHighRangePc + 0x08;
            return;
        }

        SetCr0ForSignedCompareImmediate(state, dispatchCommand, 64);
        state.Pc = dispatchCommand >= 64 ? SonicGxCommandDispatchExtendedRangePc + 0x40 : SonicGxCommandDispatchExtendedRangePc + 0x08;
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

    private static bool MatchesSonicGxCommandMaskUpdate(GameCubeBus bus, uint pc) =>
        pc == SonicGxCommandMaskUpdatePc
        && bus.Read32(pc + 0x00) == 0x800D_8E28
        && bus.Read32(pc + 0x04) == 0x5400_0528
        && bus.Read32(pc + 0x08) == 0x2800_0000
        && bus.Read32(pc + 0x0C) == 0x4182_0014
        && bus.Read32(pc + 0x10) == 0x800D_8E24
        && bus.Read32(pc + 0x14) == 0x7F9C_0038
        && bus.Read32(pc + 0x18) == 0x800D_8E20
        && bus.Read32(pc + 0x1C) == 0x7F9C_0378
        && bus.Read32(pc + 0x20) == 0x7F83_0734
        && bus.Read32(pc + 0x24) == 0x3800_0008
        && bus.Read32(pc + 0x28) == 0x7C60_0630
        && bus.Read32(pc + 0x2C) == 0x7C1C_0734
        && bus.Read32(pc + 0x30) == 0x800D_8E80
        && bus.Read32(pc + 0x34) == 0x7F84_0278
        && bus.Read32(pc + 0x38) == 0x800D_8E84
        && bus.Read32(pc + 0x3C) == 0x7C84_0378
        && bus.Read32(pc + 0x40) == 0x7F83_E378
        && bus.Read32(pc + 0x44) == 0x906D_8E80
        && bus.Read32(pc + 0x48) == 0x3800_0000
        && bus.Read32(pc + 0x4C) == 0x900D_8E84;

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
        if (MatchesSonicGxFloatTexcoordStripEmitTail(bus, pc))
        {
            const uint skipped = 3;
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            uint nextStripCount = unchecked(state.Gpr[27] - 1u);
            state.Gpr[27] = nextStripCount;
            SetCr0ForUnsignedCompareImmediate(state, nextStripCount, 0);
            state.Pc = nextStripCount != 0 ? SonicGxFloatTexcoordStripEmitHeaderPc : pc + 0x0C;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }

        if (MatchesSonicGxFloatTexcoordStripEmitHeader(bus, pc))
        {
            uint countStream = state.Gpr[25];
            uint stateBlockPointerAddress = unchecked(state.Gpr[13] + 0xFFFF_8380u);
            uint stackPointer = state.Gpr[1];
            uint newStackPointer = unchecked(stackPointer - 40u);
            if (!bus.Memory.IsMainRamAddress(countStream, sizeof(ushort))
                || !bus.Memory.IsMainRamAddress(stateBlockPointerAddress, sizeof(uint))
                || !bus.Memory.IsMainRamAddress(newStackPointer, 48))
            {
                return false;
            }

            int signedVertices = unchecked((short)bus.Memory.Read16(countStream));
            uint vertices = signedVertices < 0 ? unchecked((uint)-signedVertices) : unchecked((uint)signedVertices);
            if (vertices == 0)
            {
                return false;
            }

            uint stateBlock = bus.Memory.Read32(stateBlockPointerAddress);
            if (!bus.Memory.IsMainRamAddress(stateBlock, 0x4F4)
                || bus.Memory.Read32(stateBlock + 0x4F0) != 0
                || bus.Memory.Read32(stateBlock) == 0)
            {
                return false;
            }

            uint prologueInstructions = signedVertices < 0 ? 12u : 11u;
            return TryFastForwardSonicGxFloatTexcoordStripEmitCore(
                state,
                bus,
                SonicGxFloatTexcoordStripEmitLoopPc,
                vertices,
                unchecked(countStream + sizeof(ushort)),
                state.Gpr[26],
                checked(prologueInstructions + SonicGxDrawBeginFastForwardInstructions),
                stateBlock,
                stackPointer,
                SonicGxFloatTexcoordStripEmitHeaderPc + 0x28,
                out skippedInstructions);
        }

        if (!MatchesSonicGxFloatTexcoordStripEmitLoop(bus, pc))
        {
            return false;
        }

        return TryFastForwardSonicGxFloatTexcoordStripEmitCore(
            state,
            bus,
            pc,
            state.Gpr[31],
            state.Gpr[25],
            state.Gpr[26],
            extraInstructions: 0,
            drawBeginStateBlock: 0,
            drawBeginStackPointer: 0,
            drawBeginReturnAddress: 0,
            out skippedInstructions);
    }

    private static bool TryFastForwardSonicGxFloatTexcoordStripEmitCore(
        PowerPcState state,
        GameCubeBus bus,
        uint pc,
        uint vertices,
        uint stream,
        uint vertexBase,
        uint extraInstructions,
        uint drawBeginStateBlock,
        uint drawBeginStackPointer,
        uint drawBeginReturnAddress,
        out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (vertices == 0 || vertices > 0x10000)
        {
            return false;
        }

        uint skipped = checked(extraInstructions + vertices * SonicGxFloatTexcoordStripEmitInstructionsPerIteration + SonicGxFloatTexcoordStripEmitExitInstructions);
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

        if (extraInstructions != 0)
        {
            uint oldR29 = state.Gpr[29];
            uint oldR30 = state.Gpr[30];
            bus.Write32(drawBeginStackPointer + 4, drawBeginReturnAddress);
            bus.Write32(unchecked(drawBeginStackPointer - 40u), drawBeginStackPointer);
            bus.Write32(unchecked(drawBeginStackPointer - 4u), vertices);
            bus.Write32(unchecked(drawBeginStackPointer - 8u), oldR30);
            bus.Write32(unchecked(drawBeginStackPointer - 12u), oldR29);
            bus.Write8(fifo, 0x98);
            bus.Write16(fifo, (ushort)vertices);
            state.Gpr[6] = drawBeginStateBlock;
        }

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

    private static bool MatchesSonicGxFloatTexcoordStripEmitHeader(GameCubeBus bus, uint pc) =>
        pc == SonicGxFloatTexcoordStripEmitHeaderPc
        && MatchesSonicGxFloatTexcoordStripEmitLoop(bus, SonicGxFloatTexcoordStripEmitLoopPc)
        && MatchesSonicGxDrawBegin(bus, SonicGxDrawBeginPc);

    private static bool MatchesSonicGxFloatTexcoordStripEmitTail(GameCubeBus bus, uint pc) =>
        pc == SonicGxFloatTexcoordStripEmitLoopPc + 0x5C
        && bus.Read32(pc + 0x00) == 0x3B7B_FFFF
        && bus.Read32(pc + 0x04) == 0x281B_0000
        && bus.Read32(pc + 0x08) == 0x4082_FF6C
        && MatchesSonicGxFloatTexcoordStripEmitLoop(bus, SonicGxFloatTexcoordStripEmitLoopPc);

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

    private static string CaptureBusWindowHex(GameCubeBus bus, uint address, int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        char[] chars = new char[length * 2];
        const string hex = "0123456789ABCDEF";
        try
        {
            for (int offset = 0; offset < length; offset++)
            {
                byte value = bus.Read8(address + (uint)offset);
                chars[offset * 2] = hex[value >> 4];
                chars[offset * 2 + 1] = hex[value & 0x0F];
            }
        }
        catch (AddressTranslationException)
        {
            return string.Empty;
        }

        return new string(chars);
    }

    private static uint ReadMainRamWordOrZero(GameCubeMemory memory, uint address) =>
        memory.IsMainRamAddress(address, sizeof(uint)) ? memory.Read32(address) : 0;

    private static int ReadMainRamHalfWordOrZero(GameCubeMemory memory, uint address) =>
        memory.IsMainRamAddress(address, sizeof(ushort)) ? memory.Read16(address) : 0;

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
        const ulong fixedRoutineCost = 2_600;
        const ulong candidateCost = 66;
        const ulong extraByteCost = 4;
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

    private static bool TryFastForwardSonicPathLookup(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (!MatchesSonicPathLookupRoutine(bus, pc))
        {
            return false;
        }

        SonicPathLookupPrediction prediction = PredictSonicPathLookup(bus, state);
        if (!prediction.Success
            || prediction.EstimatedCycles > int.MaxValue
            || !CanFastForwardSonicPathLookupCycles(state, bus, prediction.EstimatedCycles)
            || !TryReadMainRam32(bus.Memory, unchecked(state.Gpr[13] - 30068), out uint entryTable))
        {
            return false;
        }

        uint returnAddress = state.Lr;
        state.Gpr[0] = returnAddress;
        state.Gpr[3] = prediction.Result;
        state.Gpr[4] = entryTable;
        state.Pc = returnAddress;

        uint skipped = (uint)prediction.EstimatedCycles;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicPathLookupRoutine(GameCubeBus bus, uint pc) =>
        pc == SonicPathLookupEntryPc
        && bus.Read32(pc + 0x000) == 0x7C08_02A6
        && bus.Read32(pc + 0x004) == 0x9001_0004
        && bus.Read32(pc + 0x008) == 0x9421_FFB8
        && bus.Read32(pc + 0x00C) == 0xBE81_0018
        && bus.Read32(pc + 0x2E0) == 0xBA81_0018
        && bus.Read32(pc + 0x2E4) == 0x8001_004C
        && bus.Read32(pc + 0x2E8) == 0x3821_0048
        && bus.Read32(pc + 0x2EC) == 0x7C08_03A6
        && bus.Read32(pc + 0x2F0) == 0x4E80_0020;

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

    private static bool TryFastForwardSonicStartCodeScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        const uint pc = 0x8014_93B8;
        const uint exitPc = 0x8014_9458;
        const uint maxBytes = 0x10_0000;

        if (state.Pc != pc || !MatchesSonicStartCodeScan(bus))
        {
            return false;
        }

        uint cursor = state.Gpr[29];
        uint end = state.Gpr[31];
        uint scanState = state.Gpr[4];
        if (scanState >= 3 || cursor >= end)
        {
            return false;
        }

        uint remaining = end - cursor;
        if (remaining > maxBytes)
        {
            remaining = maxBytes;
        }

        uint skipped = 0;
        uint consumed = 0;
        uint lastByte = state.Gpr[3];
        uint lastExtsByte = state.Gpr[0];
        uint decrementer = state.Spr[22];
        bool finiteDecrementer = (decrementer & 0x8000_0000) == 0;
        while (consumed < remaining)
        {
            uint address = cursor + consumed;
            if (!bus.Memory.IsMainRamAddress(address, sizeof(byte)))
            {
                return false;
            }

            byte value = bus.Memory.Read8(address);
            uint instructions = AdvanceSonicStartCodeScanState(value, ref scanState);
            if (finiteDecrementer && skipped + instructions > decrementer)
            {
                break;
            }

            lastByte = value;
            lastExtsByte = unchecked((uint)(sbyte)value);
            skipped += instructions;
            consumed++;
            if (scanState == 3)
            {
                break;
            }
        }

        if (consumed == 0)
        {
            return false;
        }

        if (finiteDecrementer && skipped == decrementer)
        {
            return false;
        }

        skipped++;
        state.Gpr[0] = lastExtsByte;
        state.Gpr[3] = lastByte;
        state.Gpr[4] = scanState;
        state.Gpr[29] = cursor + consumed;
        SetCr0ForUnsignedCompareImmediate(state, state.Gpr[29], end);
        state.Pc = state.Gpr[29] < end ? pc : exitPc;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;

        static uint AdvanceSonicStartCodeScanState(byte value, ref uint scanState)
        {
            switch (scanState)
            {
                case 0:
                    if (value == 0)
                    {
                        scanState = 1;
                        return 12;
                    }

                    return 11;
                case 1:
                    scanState = value == 0 ? 2u : 0u;
                    return 14;
                case 2:
                    if (value == 1)
                    {
                        scanState = 3;
                        return 11;
                    }

                    if (value != 0)
                    {
                        scanState = 0;
                        return 13;
                    }

                    return 11;
                default:
                    return 0;
            }
        }
    }

    private static bool MatchesSonicStartCodeScan(GameCubeBus bus) =>
        bus.Read32(0x8014_93B8) == 0x2C04_0002
        && bus.Read32(0x8014_93BC) == 0x887D_0000
        && bus.Read32(0x8014_93C0) == 0x3BBD_0001
        && bus.Read32(0x8014_93C4) == 0x4182_004C
        && bus.Read32(0x8014_93C8) == 0x4080_0014
        && bus.Read32(0x8014_93CC) == 0x2C04_0000
        && bus.Read32(0x8014_93D0) == 0x4182_0018
        && bus.Read32(0x8014_93D4) == 0x4080_0024
        && bus.Read32(0x8014_93E8) == 0x7C60_0775
        && bus.Read32(0x8014_93F8) == 0x7C60_0775
        && bus.Read32(0x8014_9410) == 0x7C60_0774
        && bus.Read32(0x8014_9414) == 0x2C00_0001
        && bus.Read32(0x8014_9434) == 0x387D_FFFC
        && bus.Read32(0x8014_9438) == 0x4800_0141
        && bus.Read32(0x8014_9450) == 0x7C1D_F840
        && bus.Read32(0x8014_9454) == 0x4180_FF64;

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

    private static bool TryFastForwardSonicPairedTransform4dIndexedOutput(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint iterations = state.Ctr;
        if (!MatchesSonicPairedTransform4dIndexedOutputLoop(bus)
            || state.Pc != SonicPairedTransform4dIndexedLoopPc
            || iterations == 0
            || iterations > 0x0001_0000)
        {
            return false;
        }

        uint skipped = checked(iterations * SonicPairedTransform4dIndexedInstructionsPerIteration + SonicPairedTransform4dIndexedExitInstructions);
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
            (double Lane0, double Lane1) f19 = (state.Fpr[19], state.FprPair1[19]);
            (double Lane0, double Lane1) f20 = (state.Fpr[20], state.FprPair1[20]);
            (double Lane0, double Lane1) f21 = (state.Fpr[21], state.FprPair1[21]);
            (double Lane0, double Lane1) f22 = (state.Fpr[22], state.FprPair1[22]);
            (double Lane0, double Lane1) f23 = (state.Fpr[23], state.FprPair1[23]);
            uint outputBase = state.Gpr[9];
            uint outputCursor = state.Gpr[6];
            uint nextOutputCursor = state.Gpr[4];
            uint inputCursor = state.Gpr[7];
            uint lastOffset = state.Gpr[10];

            for (uint iteration = 0; iteration < iterations; iteration++)
            {
                f11 = PairedMaddsScalar0(f0, f8, f6);
                WriteSonicPairedTransform4dIndexedOutput(bus, outputCursor, f15, f16, f17, f18);
                f12 = PairedMaddsScalar0(f1, f8, f7);
                f13 = PairedMulsScalar1(f0, f9);
                f14 = PairedMulsScalar1(f1, f9);
                f11 = PairedMaddsScalar1(f2, f8, f11);
                f12 = PairedMaddsScalar1(f3, f8, f12);
                inputCursor = unchecked(inputCursor + 2);
                f8 = ReadPairedSingleQuantized(bus, state, inputCursor, gqr: 0, single: false);
                f13 = PairedMaddsScalar0(f2, f10, f13);
                f14 = PairedMaddsScalar0(f3, f10, f14);
                f20 = ReadPairedSingleFloatPair(bus, nextOutputCursor);
                f11 = PairedMaddsScalar0(f4, f9, f11);
                f21 = ReadPairedSingleFloatSingle(bus, nextOutputCursor + 8);
                f12 = PairedMaddsScalar0(f5, f9, f12);
                f22 = ReadPairedSingleFloatPair(bus, nextOutputCursor + 12);
                f13 = PairedMaddsScalar1(f4, f10, f13);
                f23 = ReadPairedSingleFloatSingle(bus, nextOutputCursor + 20);
                f14 = PairedMaddsScalar1(f5, f10, f14);
                outputCursor = nextOutputCursor;
                f15 = PairedMaddsScalar0(f11, f19, f20);
                f16 = PairedMaddsScalar0(f12, f19, f21);
                inputCursor = unchecked(inputCursor + 8);
                f9 = ReadPairedSingleQuantized(bus, state, inputCursor, gqr: 0, single: false);
                f17 = PairedMaddsScalar0(f13, f19, f22);
                f18 = PairedMaddsScalar0(f14, f19, f23);
                inputCursor = unchecked(inputCursor + 8);
                f10 = ReadPairedSingleQuantized(bus, state, inputCursor, gqr: 0, single: false);
                inputCursor = unchecked(inputCursor + 8);
                f19 = ReadPairedSingleQuantized(bus, state, inputCursor, gqr: 1, single: true);
                inputCursor = unchecked(inputCursor + 2);
                lastOffset = Rlwinm(unchecked((uint)(short)bus.Memory.Read16(inputCursor)), 5, 0, 26);
                nextOutputCursor = unchecked(outputBase + lastOffset);
            }

            WriteSonicPairedTransform4dIndexedOutput(bus, outputCursor, f15, f16, f17, f18);

            uint stackPointer = state.Gpr[1];
            if (!bus.Memory.IsMainRamAddress(stackPointer + 8, 80))
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
            for (int register = 14; register <= 23; register++)
            {
                double restored = ReadDouble(bus, stackPointer + 8u + (uint)(register - 14) * sizeof(double));
                state.Fpr[register] = restored;
                state.FprPair1[register] = restored;
            }

            state.Gpr[1] = unchecked(stackPointer + 128);
            state.Gpr[4] = nextOutputCursor;
            state.Gpr[6] = outputCursor;
            state.Gpr[7] = inputCursor;
            state.Gpr[10] = lastOffset;
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

    private static bool TryFastForwardSonicCoordinatePairFillLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint iterations = state.Ctr;
        int columnLimit = unchecked((int)state.Gpr[3]);
        if (!MatchesSonicCoordinatePairFillLoop(bus, state.Pc)
            || iterations == 0
            || iterations > MaxFastForwardSonicCoordinatePairs
            || columnLimit <= 0
            || columnLimit > 0x10000
            || !CanFastForwardInstructionCount(state, iterations, instructionsPerIteration: 11, extraInstructions: 0))
        {
            return false;
        }

        ulong outputBytes = (ulong)iterations * 2ul;
        if (outputBytes > int.MaxValue || !bus.Memory.IsMainRamAddress(state.Gpr[5], checked((int)outputBytes)))
        {
            return false;
        }

        uint output = state.Gpr[5];
        uint column = state.Gpr[4];
        uint row = state.Gpr[6];
        uint lastCompareLeft = column;
        uint lastWrittenColumn = column;
        ulong skipped = 0;

        for (uint iteration = 0; iteration < iterations; iteration++)
        {
            lastCompareLeft = column;
            if (unchecked((int)column) >= columnLimit)
            {
                column = 0;
                row++;
                skipped += 11;
            }
            else
            {
                skipped += 9;
            }

            bus.Memory.Write8(output, (byte)row);
            lastWrittenColumn = column;
            bus.Memory.Write8(output + 1, (byte)column);
            column++;
            output += 2;
        }

        state.Gpr[0] = unchecked((uint)(sbyte)(byte)lastWrittenColumn);
        state.Gpr[4] = column;
        state.Gpr[5] = output;
        state.Gpr[6] = row;
        state.Ctr = 0;
        state.Pc = SonicCoordinatePairFillLoopPc + 0x2C;
        SetCr0ForSignedCompare(state, lastCompareLeft, state.Gpr[3]);
        AdvanceFastForwardedInstructions(state, bus, checked((uint)skipped));
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool TryFastForwardSonicBufferFillLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        return state.Pc switch
        {
            SonicBufferFillFirstLoopPc => TryFastForwardSonicBufferFillLoopCore(
                state,
                bus,
                indexRegister: 3,
                cursorRegister: 4,
                valueRegister: 31,
                countOffset: 0,
                loopExitPc: 0x800F_C5B4,
                out skippedInstructions),
            SonicBufferFillSecondLoopPc => TryFastForwardSonicBufferFillLoopCore(
                state,
                bus,
                indexRegister: 4,
                cursorRegister: 5,
                valueRegister: 3,
                countOffset: 4,
                loopExitPc: 0x800F_C5DC,
                out skippedInstructions),
            SonicBufferFillThirdLoopPc => TryFastForwardSonicBufferFillLoopCore(
                state,
                bus,
                indexRegister: 4,
                cursorRegister: 6,
                valueRegister: 3,
                countOffset: 8,
                loopExitPc: 0x800F_C604,
                out skippedInstructions),
            _ => false,
        };
    }

    private static bool TryFastForwardSonicBufferFillLoopCore(
        PowerPcState state,
        GameCubeBus bus,
        int indexRegister,
        int cursorRegister,
        int valueRegister,
        uint countOffset,
        uint loopExitPc,
        out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            if (!MatchesSonicBufferFillLoop(bus, state.Pc))
            {
                return false;
            }

            uint descriptor = state.Gpr[29];
            if (descriptor > uint.MaxValue - countOffset)
            {
                return false;
            }

            uint countAddress = descriptor + countOffset;
            if (!bus.Memory.IsMainRamAddress(countAddress, sizeof(uint)))
            {
                return false;
            }

            uint count = bus.Memory.Read32(countAddress);
            ulong limitWordsWide = (ulong)count * 160ul;
            if (limitWordsWide == 0 || limitWordsWide > MaxFastForwardSonicBufferFillWords)
            {
                return false;
            }

            uint limitWords = checked((uint)limitWordsWide);
            uint index = state.Gpr[indexRegister];
            if (index >= limitWords)
            {
                return false;
            }

            uint remainingWords = limitWords - index;
            ulong byteLengthWide = (ulong)remainingWords * sizeof(uint);
            if (byteLengthWide > int.MaxValue)
            {
                return false;
            }

            uint destination = state.Gpr[cursorRegister];
            if ((destination & 3) != 0 || !bus.Memory.IsMainRamAddress(destination, checked((int)byteLengthWide)))
            {
                return false;
            }

            if (!CanFastForwardInstructionCount(state, remainingWords, SonicBufferFillInstructionsPerIteration, extraInstructions: 0))
            {
                return false;
            }

            uint value = state.Gpr[valueRegister];
            for (uint word = 0; word < remainingWords; word++)
            {
                bus.Write32(destination + word * sizeof(uint), value);
            }

            state.Gpr[0] = limitWords;
            state.Gpr[indexRegister] = limitWords;
            state.Gpr[cursorRegister] = destination + checked(remainingWords * sizeof(uint));
            state.Pc = loopExitPc;
            SetCr0ForUnsignedCompareImmediate(state, limitWords, limitWords);

            uint skipped = checked(remainingWords * SonicBufferFillInstructionsPerIteration);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicBufferFillLoop(GameCubeBus bus, uint pc)
    {
        return pc switch
        {
            SonicBufferFillFirstLoopPc =>
                bus.Read32(pc + 0x00) == 0x93E4_0000
                && bus.Read32(pc + 0x04) == 0x3884_0004
                && bus.Read32(pc + 0x08) == 0x3863_0001
                && bus.Read32(pc + 0x0C) == 0x801D_0000
                && bus.Read32(pc + 0x10) == 0x1C00_00A0
                && bus.Read32(pc + 0x14) == 0x7C03_0040
                && bus.Read32(pc + 0x18) == 0x4180_FFE8,
            SonicBufferFillSecondLoopPc =>
                bus.Read32(pc + 0x00) == 0x9065_0000
                && bus.Read32(pc + 0x04) == 0x38A5_0004
                && bus.Read32(pc + 0x08) == 0x3884_0001
                && bus.Read32(pc + 0x0C) == 0x801D_0004
                && bus.Read32(pc + 0x10) == 0x1C00_00A0
                && bus.Read32(pc + 0x14) == 0x7C04_0040
                && bus.Read32(pc + 0x18) == 0x4180_FFE8,
            SonicBufferFillThirdLoopPc =>
                bus.Read32(pc + 0x00) == 0x9066_0000
                && bus.Read32(pc + 0x04) == 0x38C6_0004
                && bus.Read32(pc + 0x08) == 0x3884_0001
                && bus.Read32(pc + 0x0C) == 0x801D_0008
                && bus.Read32(pc + 0x10) == 0x1C00_00A0
                && bus.Read32(pc + 0x14) == 0x7C04_0040
                && bus.Read32(pc + 0x18) == 0x4180_FFE8,
            _ => false,
        };
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

    private static bool TryFastForwardSonicGeneratedSlotMismatchScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        const uint pc = 0x80BC_7288;
        const uint exitPc = 0x80BC_7EC8;
        const uint tablePointerAddress = 0x80BE_CA1C;
        const uint slotStride = 44;
        const uint slotCompareOffset = 40;
        const uint instructionsPerSlot = 19;
        const uint maxSlots = 0x1_0000;

        if (state.Pc != pc || !MatchesSonicGeneratedSlotMismatchScan(bus))
        {
            return false;
        }

        GameCubeMemory memory = bus.Memory;
        uint slot = state.Gpr[31];
        uint groupOffset = Rlwinm(state.Gpr[30], 5, 0, 26);
        if (!memory.IsMainRamAddress(tablePointerAddress, sizeof(uint)))
        {
            return false;
        }

        uint tableBase = memory.Read32(tablePointerAddress);
        if (!TryAdd(tableBase, groupOffset, out uint groupPointerAddress)
            || !TryAdd(groupPointerAddress, sizeof(uint), out uint countAddress)
            || !memory.IsMainRamAddress(groupPointerAddress, sizeof(uint))
            || !memory.IsMainRamAddress(countAddress, sizeof(uint)))
        {
            return false;
        }

        uint groupBase = memory.Read32(groupPointerAddress);
        uint count = memory.Read32(countAddress);
        if (slot >= count || count > maxSlots)
        {
            return false;
        }

        uint slotsToConsider = count - slot;
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

        uint target = state.Gpr[26];
        uint mismatchedSlots = 0;
        uint lastValue = 0;
        while (mismatchedSlots < slotsToConsider)
        {
            uint currentSlot = slot + mismatchedSlots;
            if (!TryMultiplyAdd(currentSlot, slotStride, slotCompareOffset, out uint slotOffset)
                || !TryAdd(groupBase, slotOffset, out uint valueAddress)
                || !memory.IsMainRamAddress(valueAddress, sizeof(uint)))
            {
                return false;
            }

            uint value = memory.Read32(valueAddress);
            if (value == target)
            {
                break;
            }

            lastValue = value;
            mismatchedSlots++;
        }

        if (mismatchedSlots == 0)
        {
            return false;
        }

        uint nextSlot = slot + mismatchedSlots;
        state.Gpr[0] = groupOffset;
        state.Gpr[3] = lastValue;
        state.Gpr[4] = count;
        state.Gpr[5] = groupOffset;
        state.Gpr[6] = tableBase;
        state.Gpr[31] = nextSlot;
        SetCr0ForSignedCompare(state, nextSlot, count);
        state.Pc = nextSlot < count ? pc : exitPc;

        uint skipped = checked(mismatchedSlots * instructionsPerSlot);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;

        static bool TryAdd(uint left, uint right, out uint result)
        {
            result = unchecked(left + right);
            return result >= left;
        }

        static bool TryMultiplyAdd(uint value, uint multiplier, uint addend, out uint result)
        {
            ulong computed = (ulong)value * multiplier + addend;
            result = (uint)computed;
            return computed <= uint.MaxValue;
        }
    }

    private static bool MatchesSonicGeneratedSlotMismatchScan(GameCubeBus bus) =>
        bus.Read32(0x80BC_7288) == 0x3C60_80BF
        && bus.Read32(0x80BC_728C) == 0x3863_CA1C
        && bus.Read32(0x80BC_7290) == 0x8063_0000
        && bus.Read32(0x80BC_7294) == 0x57C0_2834
        && bus.Read32(0x80BC_7298) == 0x7C83_002E
        && bus.Read32(0x80BC_729C) == 0x1C7F_002C
        && bus.Read32(0x80BC_72A0) == 0x3863_0028
        && bus.Read32(0x80BC_72A4) == 0x7C64_182E
        && bus.Read32(0x80BC_72A8) == 0x7C1A_1840
        && bus.Read32(0x80BC_72AC) == 0x4082_0BF8
        && bus.Read32(0x80BC_7EA4) == 0x3BFF_0001
        && bus.Read32(0x80BC_7EA8) == 0x3C80_80BF
        && bus.Read32(0x80BC_7EAC) == 0x3884_CA1C
        && bus.Read32(0x80BC_7EB0) == 0x80C4_0000
        && bus.Read32(0x80BC_7EB4) == 0x57C5_2834
        && bus.Read32(0x80BC_7EB8) == 0x3885_0004
        && bus.Read32(0x80BC_7EBC) == 0x7C86_202E
        && bus.Read32(0x80BC_7EC0) == 0x7C1F_2000
        && bus.Read32(0x80BC_7EC4) == 0x4180_F3C4;

    private static bool TryFastForwardSonicInterruptStatusPrologue(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicInterruptStatusProloguePc
            || state.Gpr[3] == 2
            || !MatchesSonicInterruptStatusPrologue(bus)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicInterruptStatusPrologueInstructions, extraInstructions: 0))
        {
            return false;
        }

        uint oldStackPointer = state.Gpr[1];
        uint newStackPointer = unchecked(oldStackPointer - 40);
        uint oldLr = state.Lr;
        uint slot = state.Gpr[3];
        uint tablePointer = unchecked(0x802B_A880u + ((slot << 6) & 0xFFFF_FFC0u));
        if (!bus.Memory.IsMainRamAddress(newStackPointer, 40)
            || oldStackPointer > uint.MaxValue - sizeof(uint)
            || !bus.Memory.IsMainRamAddress(oldStackPointer + sizeof(uint), sizeof(uint))
            || !bus.Memory.IsMainRamAddress(tablePointer + 0x0C, sizeof(uint)))
        {
            return false;
        }

        bus.Write32(oldStackPointer + 4, oldLr);
        bus.Write32(newStackPointer, oldStackPointer);
        for (int register = 27; register <= 31; register++)
        {
            bus.Write32(newStackPointer + 20u + (uint)(register - 27) * sizeof(uint), state.Gpr[register]);
        }

        uint oldMsr = state.Msr;
        uint oldInterruptEnable = (oldMsr >> 15) & 1;
        uint controlWord = bus.Memory.Read32(tablePointer + 0x0C);

        state.Gpr[0] = controlWord;
        state.Gpr[1] = newStackPointer;
        state.Gpr[3] = oldInterruptEnable;
        state.Gpr[4] = 0xCC00_0000;
        state.Gpr[5] = unchecked(slot * 20u);
        state.Gpr[6] = 0xCC00_6800;
        state.Gpr[28] = slot;
        state.Gpr[29] = 1;
        state.Gpr[31] = tablePointer;
        state.Lr = 0x800E_679C;
        state.Msr = oldMsr & ~0x8000u;
        SetCr0ForSignedCompareImmediate(state, slot, 2);
        state.Pc = SonicInterruptStatusPrologueExitPc;
        AdvanceFastForwardedInstructions(state, bus, SonicInterruptStatusPrologueInstructions);
        skippedInstructions = checked((int)SonicInterruptStatusPrologueInstructions);
        return true;
    }

    private static bool MatchesSonicInterruptStatusPrologue(GameCubeBus bus) =>
        bus.Read32(SonicInterruptStatusProloguePc + 0x00) == 0x7C08_02A6
        && bus.Read32(SonicInterruptStatusProloguePc + 0x04) == 0x9001_0004
        && bus.Read32(SonicInterruptStatusProloguePc + 0x08) == 0x9421_FFD8
        && bus.Read32(SonicInterruptStatusProloguePc + 0x0C) == 0xBF61_0014
        && bus.Read32(SonicInterruptStatusProloguePc + 0x10) == 0x3B83_0000
        && bus.Read32(SonicInterruptStatusProloguePc + 0x14) == 0x3C60_802C
        && bus.Read32(SonicInterruptStatusProloguePc + 0x18) == 0x2C1C_0002
        && bus.Read32(SonicInterruptStatusProloguePc + 0x1C) == 0x5784_3032
        && bus.Read32(SonicInterruptStatusProloguePc + 0x20) == 0x3803_A880
        && bus.Read32(SonicInterruptStatusProloguePc + 0x24) == 0x7FE0_2214
        && bus.Read32(SonicInterruptStatusProloguePc + 0x28) == 0x4082_000C
        && bus.Read32(SonicInterruptStatusProloguePc + 0x34) == 0x3BA0_0001
        && bus.Read32(SonicInterruptStatusProloguePc + 0x38) == 0x4800_1115
        && bus.Read32(SonicInterruptStatusProloguePc + 0x3C) == 0x1CBC_0014
        && bus.Read32(SonicInterruptStatusProloguePc + 0x40) == 0x801F_000C
        && bus.Read32(SonicInterruptStatusProloguePc + 0x44) == 0x3C80_CC00
        && bus.Read32(SonicInterruptStatusProloguePc + 0x48) == 0x38C4_6800
        && bus.Read32(SonicDisableExternalInterruptPc + 0x00) == 0x7C60_00A6
        && bus.Read32(SonicDisableExternalInterruptPc + 0x04) == 0x5464_045E
        && bus.Read32(SonicDisableExternalInterruptPc + 0x08) == 0x7C80_0124
        && bus.Read32(SonicDisableExternalInterruptPc + 0x0C) == 0x5463_8FFE
        && bus.Read32(SonicDisableExternalInterruptPc + 0x10) == 0x4E80_0020;

    private static bool TryFastForwardSonicInterruptStatusPoll(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicInterruptStatusPollPc
            || !MatchesSonicInterruptStatusPoll(bus)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 7, extraInstructions: 0))
        {
            return false;
        }

        uint controlFlag = state.Gpr[0] & 0x8u;
        uint statusAddress = unchecked(state.Gpr[6] + state.Gpr[5]);
        uint status;
        try
        {
            status = bus.Read32(statusAddress);
        }
        catch (AddressTranslationException)
        {
            return false;
        }

        state.Gpr[6] = statusAddress;
        state.Gpr[7] = status;
        state.Gpr[30] = state.Gpr[3];

        uint skipped;
        if (controlFlag != 0)
        {
            state.Gpr[0] = controlFlag;
            SetCr0(state, controlFlag);
            state.Pc = SonicInterruptStatusControlBitPc;
            skipped = 5;
        }
        else
        {
            uint firstStatusBit = status & 0x800u;
            state.Gpr[0] = firstStatusBit;
            SetCr0(state, firstStatusBit);
            state.Pc = firstStatusBit == 0 ? SonicInterruptStatusSecondBitPc : SonicInterruptStatusFirstBitPc;
            skipped = 7;
        }

        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicInterruptStatusPoll(GameCubeBus bus) =>
        bus.Read32(SonicInterruptStatusPollPc + 0x00) == 0x7CC6_2A14
        && bus.Read32(SonicInterruptStatusPollPc + 0x04) == 0x5400_0739
        && bus.Read32(SonicInterruptStatusPollPc + 0x08) == 0x80E6_0000
        && bus.Read32(SonicInterruptStatusPollPc + 0x0C) == 0x7C7E_1B78
        && bus.Read32(SonicInterruptStatusPollPc + 0x10) == 0x4082_00CC
        && bus.Read32(SonicInterruptStatusPollPc + 0x14) == 0x54E0_0529
        && bus.Read32(SonicInterruptStatusPollPc + 0x18) == 0x4182_002C;

    private static bool TryFastForwardSonicInterruptStatusTimerSetup(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicInterruptStatusSecondBitPc
            || (state.Gpr[7] & 0x1000u) == 0
            || !MatchesSonicInterruptStatusTimerSetup(bus)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 9, extraInstructions: 0))
        {
            return false;
        }

        uint busClock;
        try
        {
            busClock = bus.Read32(0x8000_00F8);
        }
        catch (AddressTranslationException)
        {
            return false;
        }

        uint multiplier = 0x1062_4DD3u;
        uint scaledClock = busClock >> 2;
        uint highProduct = (uint)(((ulong)multiplier * scaledClock) >> 32);
        uint divisor = ((highProduct << 26) | (highProduct >> 6)) & 0x03FF_FFFFu;
        uint secondStatusBit = state.Gpr[7] & 0x1000u;
        state.Gpr[0] = highProduct;
        state.Gpr[3] = multiplier;
        state.Gpr[27] = divisor;
        state.Gpr[31] = 0x8000_0000;
        SetCr0(state, secondStatusBit);
        state.Pc = SonicInterruptStatusTimerSetupExitPc;
        AdvanceFastForwardedInstructions(state, bus, 9);
        skippedInstructions = 9;
        return true;
    }

    private static bool MatchesSonicInterruptStatusTimerSetup(GameCubeBus bus) =>
        bus.Read32(SonicInterruptStatusSecondBitPc + 0x00) == 0x54E0_04E7
        && bus.Read32(SonicInterruptStatusSecondBitPc + 0x04) == 0x4182_0074
        && bus.Read32(SonicInterruptStatusSecondBitPc + 0x08) == 0x3FE0_8000
        && bus.Read32(SonicInterruptStatusSecondBitPc + 0x0C) == 0x801F_00F8
        && bus.Read32(SonicInterruptStatusSecondBitPc + 0x10) == 0x3C60_1062
        && bus.Read32(SonicInterruptStatusSecondBitPc + 0x14) == 0x3863_4DD3
        && bus.Read32(SonicInterruptStatusSecondBitPc + 0x18) == 0x5400_F0BE
        && bus.Read32(SonicInterruptStatusSecondBitPc + 0x1C) == 0x7C03_0016
        && bus.Read32(SonicInterruptStatusSecondBitPc + 0x20) == 0x541B_D1BE;

    private static bool TryFastForwardSonicInterruptStatusTimestamp(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicInterruptStatusTimestampPc
            || !MatchesSonicInterruptStatusTimestamp(bus)
            || !MatchesSonicSignedLongDivisionLeaf(bus, 0x8010_B11C))
        {
            return false;
        }

        PowerPcState originalState = state.Clone();
        try
        {
            uint callerInstructions = 0;
            state.Lr = 0x800E_6818;
            state.Pc = 0x800E_CB48;
            callerInstructions++;
            if (!TryFastForwardTimeBaseReadLeaf(state, bus, 0x800E_CB48, out int timeBaseInstructions)
                || state.Pc != 0x800E_6818)
            {
                CopyPowerPcState(originalState, state);
                return false;
            }

            state.Gpr[6] = state.Gpr[27];
            callerInstructions++;
            state.Gpr[5] = 0;
            callerInstructions++;
            state.Lr = 0x800E_6824;
            state.Pc = 0x8010_B11C;
            callerInstructions++;
            if (!TryFastForwardLongDivisionLeaf(state, bus, out int firstDivisionInstructions)
                || state.Pc != 0x800E_6824)
            {
                CopyPowerPcState(originalState, state);
                return false;
            }

            state.Gpr[5] = 0;
            callerInstructions++;
            state.Gpr[6] = 100;
            callerInstructions++;
            state.Lr = 0x800E_6830;
            state.Pc = 0x8010_B11C;
            callerInstructions++;
            if (!TryFastForwardLongDivisionLeaf(state, bus, out int secondDivisionInstructions)
                || state.Pc != 0x800E_6830)
            {
                CopyPowerPcState(originalState, state);
                return false;
            }

            state.Gpr[0] = (state.Gpr[28] << 2) & 0xFFFF_FFFCu;
            callerInstructions++;
            state.Gpr[3] = 0x8000_30C0;
            callerInstructions++;
            state.Gpr[3] = unchecked(state.Gpr[3] + state.Gpr[0]);
            callerInstructions++;
            if (!bus.Memory.IsMainRamAddress(state.Gpr[3], sizeof(uint)))
            {
                CopyPowerPcState(originalState, state);
                return false;
            }

            state.Gpr[0] = bus.Memory.Read32(state.Gpr[3]);
            callerInstructions++;
            state.Gpr[4] = unchecked(state.Gpr[4] + 1);
            callerInstructions++;
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: callerInstructions, extraInstructions: 0))
            {
                CopyPowerPcState(originalState, state);
                return false;
            }

            state.Pc = SonicInterruptStatusTimestampExitPc;

            AdvanceFastForwardedInstructions(state, bus, callerInstructions);
            skippedInstructions = checked((int)(callerInstructions + (uint)timeBaseInstructions + (uint)firstDivisionInstructions + (uint)secondDivisionInstructions));
            return true;
        }
        catch (AddressTranslationException)
        {
            CopyPowerPcState(originalState, state);
            return false;
        }
    }

    private static bool MatchesSonicInterruptStatusTimestamp(GameCubeBus bus) =>
        bus.Read32(SonicInterruptStatusTimestampPc + 0x00) == 0x4800_6335
        && bus.Read32(SonicInterruptStatusTimestampPc + 0x04) == 0x38DB_0000
        && bus.Read32(SonicInterruptStatusTimestampPc + 0x08) == 0x38A0_0000
        && bus.Read32(SonicInterruptStatusTimestampPc + 0x0C) == 0x4802_48FD
        && bus.Read32(SonicInterruptStatusTimestampPc + 0x10) == 0x38A0_0000
        && bus.Read32(SonicInterruptStatusTimestampPc + 0x14) == 0x38C0_0064
        && bus.Read32(SonicInterruptStatusTimestampPc + 0x18) == 0x4802_48F1
        && bus.Read32(SonicInterruptStatusTimestampPc + 0x1C) == 0x5780_103A
        && bus.Read32(SonicInterruptStatusTimestampPc + 0x20) == 0x387F_30C0
        && bus.Read32(SonicInterruptStatusTimestampPc + 0x24) == 0x7C63_0214
        && bus.Read32(SonicInterruptStatusTimestampPc + 0x28) == 0x8003_0000
        && bus.Read32(SonicInterruptStatusTimestampPc + 0x2C) == 0x3884_0001
        && bus.Read32(0x800E_CB48) == 0x7C6D_42E6
        && bus.Read32(0x800E_CB4C) == 0x7C8C_42E6
        && bus.Read32(0x800E_CB50) == 0x7CAD_42E6
        && bus.Read32(0x800E_CB54) == 0x7C03_2800
        && bus.Read32(0x800E_CB58) == 0x4082_FFF0
        && bus.Read32(0x800E_CB5C) == 0x4E80_0020;

    private static bool TryFastForwardSonicInterruptStatusCompare(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicInterruptStatusComparePc
            || !MatchesSonicInterruptStatusCompare(bus))
        {
            return false;
        }

        uint tableAddress = state.Gpr[3];
        uint timestamp = state.Gpr[4];
        bool storeTimestamp = state.Gpr[0] == 0;
        PowerPcState originalState = state.Clone();
        try
        {
            uint storedTimestamp = storeTimestamp ? timestamp : bus.Read32(tableAddress);
            uint delta = unchecked(timestamp - storedTimestamp);
            uint skipped = storeTimestamp ? 9u : unchecked((int)delta) < 3 ? 8u : 6u;
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            if (storeTimestamp)
            {
                bus.Write32(tableAddress, timestamp);
            }

            SetCr0ForSignedCompareImmediate(state, delta, 3);
            if (unchecked((int)delta) < 3)
            {
                state.Gpr[29] = 0;
            }

            state.Gpr[0] = delta;
            state.Pc = SonicInterruptStatusCompareExitPc;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            CopyPowerPcState(originalState, state);
            return false;
        }
    }

    private static bool MatchesSonicInterruptStatusCompare(GameCubeBus bus) =>
        bus.Read32(SonicInterruptStatusComparePc + 0x00) == 0x2C00_0000
        && bus.Read32(SonicInterruptStatusComparePc + 0x04) == 0x4082_0008
        && bus.Read32(SonicInterruptStatusComparePc + 0x08) == 0x9083_0000
        && bus.Read32(SonicInterruptStatusComparePc + 0x0C) == 0x8003_0000
        && bus.Read32(SonicInterruptStatusComparePc + 0x10) == 0x7C00_2050
        && bus.Read32(SonicInterruptStatusComparePc + 0x14) == 0x2C00_0003
        && bus.Read32(SonicInterruptStatusComparePc + 0x18) == 0x4080_0058
        && bus.Read32(SonicInterruptStatusComparePc + 0x1C) == 0x3BA0_0000
        && bus.Read32(SonicInterruptStatusComparePc + 0x20) == 0x4800_0050;

    private static bool TryFastForwardSonicInterruptStatusTail(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicInterruptStatusTailPc
            || !MatchesSonicInterruptStatusTail(bus))
        {
            return false;
        }

        uint stackPointer = state.Gpr[1];
        if (!bus.Memory.IsMainRamAddress(stackPointer + 20, 28)
            || !bus.Memory.IsMainRamAddress(stackPointer + 44, sizeof(uint)))
        {
            return false;
        }

        uint restoreArgument = state.Gpr[30];
        uint skipped = 8u + (restoreArgument == 0 ? 7u : 8u);
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        uint oldMsr = state.Msr;
        uint restoredMsr = restoreArgument == 0 ? oldMsr & ~0x8000u : oldMsr | 0x8000u;
        SetCr0ForSignedCompareImmediate(state, restoreArgument, 0);
        state.Gpr[3] = state.Gpr[29];
        state.Gpr[4] = (oldMsr >> 15) & 1;
        state.Gpr[5] = restoredMsr;
        state.Msr = restoredMsr;

        for (int register = 27; register <= 31; register++)
        {
            state.Gpr[register] = bus.Memory.Read32(stackPointer + 20u + (uint)(register - 27) * sizeof(uint));
        }

        uint oldLr = bus.Memory.Read32(stackPointer + 44);
        state.Gpr[0] = oldLr;
        state.Gpr[1] = unchecked(stackPointer + 40);
        state.Lr = oldLr;
        state.Pc = oldLr & 0xFFFF_FFFCu;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicInterruptStatusTail(GameCubeBus bus) =>
        bus.Read32(SonicInterruptStatusTailPc + 0x00) == 0x7FC3_F378
        && bus.Read32(SonicInterruptStatusTailPc + 0x04) == 0x4800_101D
        && bus.Read32(SonicInterruptStatusTailPc + 0x08) == 0x7FA3_EB78
        && bus.Read32(SonicInterruptStatusTailPc + 0x0C) == 0xBB61_0014
        && bus.Read32(SonicInterruptStatusTailPc + 0x10) == 0x8001_002C
        && bus.Read32(SonicInterruptStatusTailPc + 0x14) == 0x3821_0028
        && bus.Read32(SonicInterruptStatusTailPc + 0x18) == 0x7C08_03A6
        && bus.Read32(SonicInterruptStatusTailPc + 0x1C) == 0x4E80_0020
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x00) == 0x2C03_0000
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x04) == 0x7C80_00A6
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x08) == 0x4182_000C
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x0C) == 0x6085_8000
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x10) == 0x4800_0008
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x14) == 0x5485_045E
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x18) == 0x7CA0_0124
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x1C) == 0x5484_8FFE
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x20) == 0x4E80_0020;

    private static bool TryFastForwardSonicInterruptStatusQueryPrologue(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicInterruptStatusQueryProloguePc
            || !MatchesSonicInterruptStatusQueryPrologue(bus)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicInterruptStatusQueryPrologueInstructions, extraInstructions: 0))
        {
            return false;
        }

        uint oldStackPointer = state.Gpr[1];
        uint newStackPointer = unchecked(oldStackPointer - 24);
        if (!bus.Memory.IsMainRamAddress(newStackPointer, 24)
            || oldStackPointer > uint.MaxValue - sizeof(uint)
            || !bus.Memory.IsMainRamAddress(oldStackPointer + sizeof(uint), sizeof(uint)))
        {
            return false;
        }

        uint slot = state.Gpr[3];
        uint tableBase = 0x802B_A880;
        uint tableOffset = (slot << 6) & 0xFFFF_FFC0u;
        bus.Write32(oldStackPointer + 4, state.Lr);
        bus.Write32(newStackPointer, oldStackPointer);
        bus.Write32(newStackPointer + 16, state.Gpr[30]);
        bus.Write32(newStackPointer + 20, state.Gpr[31]);

        state.Gpr[0] = tableBase;
        state.Gpr[1] = newStackPointer;
        state.Gpr[3] = slot;
        state.Gpr[4] = tableOffset;
        state.Gpr[30] = slot;
        state.Gpr[31] = unchecked(tableBase + tableOffset);
        state.Lr = SonicInterruptStatusQueryPrologueReturnPc;
        state.Pc = SonicInterruptStatusProloguePc;
        AdvanceFastForwardedInstructions(state, bus, SonicInterruptStatusQueryPrologueInstructions);
        skippedInstructions = checked((int)SonicInterruptStatusQueryPrologueInstructions);
        return true;
    }

    private static bool MatchesSonicInterruptStatusQueryPrologue(GameCubeBus bus) =>
        bus.Read32(SonicInterruptStatusQueryProloguePc + 0x00) == 0x7C08_02A6
        && bus.Read32(SonicInterruptStatusQueryProloguePc + 0x04) == 0x9001_0004
        && bus.Read32(SonicInterruptStatusQueryProloguePc + 0x08) == 0x9421_FFE8
        && bus.Read32(SonicInterruptStatusQueryProloguePc + 0x0C) == 0x93E1_0014
        && bus.Read32(SonicInterruptStatusQueryProloguePc + 0x10) == 0x93C1_0010
        && bus.Read32(SonicInterruptStatusQueryProloguePc + 0x14) == 0x3BC3_0000
        && bus.Read32(SonicInterruptStatusQueryProloguePc + 0x18) == 0x3C60_802C
        && bus.Read32(SonicInterruptStatusQueryProloguePc + 0x1C) == 0x3803_A880
        && bus.Read32(SonicInterruptStatusQueryProloguePc + 0x20) == 0x57C4_3032
        && bus.Read32(SonicInterruptStatusQueryProloguePc + 0x24) == 0x387E_0000
        && bus.Read32(SonicInterruptStatusQueryProloguePc + 0x28) == 0x7FE0_2214
        && bus.Read32(SonicInterruptStatusQueryProloguePc + 0x2C) == 0x4BFF_FDE1;

    private static bool TryFastForwardSonicInterruptStatusQueryPostCall(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicInterruptStatusQueryPostCallPc
            || !MatchesSonicInterruptStatusQueryPostCall(bus))
        {
            return false;
        }

        uint stackPointer = state.Gpr[1];
        if (!bus.Memory.IsMainRamAddress(stackPointer + 16, 16))
        {
            return false;
        }

        uint skipped;
        uint result;
        if (state.Gpr[3] == 0)
        {
            uint tableAddress = unchecked(0x8000_30C0u + ((state.Gpr[30] << 2) & 0xFFFF_FFFCu));
            if (!bus.Memory.IsMainRamAddress(tableAddress, sizeof(uint)))
            {
                return false;
            }

            uint tableValue = bus.Memory.Read32(tableAddress);
            SetCr0ForSignedCompareImmediate(state, tableValue, 0);
            result = tableValue == 0 ? 0xFFFF_FFFFu : 0u;
            skipped = tableValue == 0 ? 17u : 18u;
        }
        else
        {
            uint flagAddress = unchecked(state.Gpr[31] + 0x20);
            if (!bus.Memory.IsMainRamAddress(flagAddress, sizeof(uint)))
            {
                return false;
            }

            uint flag = bus.Memory.Read32(flagAddress);
            if (flag == 0)
            {
                return false;
            }

            SetCr0ForSignedCompareImmediate(state, state.Gpr[3], 0);
            result = 1;
            skipped = 15;
        }

        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        uint oldLr = bus.Memory.Read32(stackPointer + 28);
        state.Gpr[0] = oldLr;
        state.Gpr[1] = unchecked(stackPointer + 24);
        state.Gpr[3] = result;
        state.Gpr[30] = bus.Memory.Read32(stackPointer + 16);
        state.Gpr[31] = bus.Memory.Read32(stackPointer + 20);
        state.Lr = oldLr;
        state.Pc = oldLr & 0xFFFF_FFFCu;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicInterruptStatusQueryPostCall(GameCubeBus bus) =>
        bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x00) == 0x2C03_0000
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x04) == 0x4182_0034
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x08) == 0x801F_0020
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x0C) == 0x2C00_0000
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x10) == 0x4082_0028
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x38) == 0x2C03_0000
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x3C) == 0x4182_000C
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x40) == 0x3860_0001
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x44) == 0x4800_0028
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x48) == 0x3C60_8000
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x4C) == 0x57C0_103A
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x50) == 0x3863_30C0
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x54) == 0x7C03_002E
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x58) == 0x2C00_0000
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x5C) == 0x4182_000C
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x60) == 0x3860_0000
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x64) == 0x4800_0008
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x68) == 0x3860_FFFF
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x6C) == 0x8001_001C
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x70) == 0x83E1_0014
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x74) == 0x83C1_0010
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x78) == 0x7C08_03A6
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x7C) == 0x3821_0018
        && bus.Read32(SonicInterruptStatusQueryPostCallPc + 0x80) == 0x4E80_0020;

    private static bool TryFastForwardSonicDvdStatusWaitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicDvdStatusWaitLoopPc
            || !MatchesSonicDvdStatusWaitLoop(bus)
            || bus.HasPendingExternalInterrupt)
        {
            return false;
        }

        ulong safeCycles = 0;
        if ((bus.Read32(0xCC00_601C) & 1u) != 0)
        {
            DiscInterfaceDebugSnapshot disc = bus.GetDiscInterfaceDebugSnapshot();
            if (!disc.HasPendingCommand || disc.PendingCommandCycles <= SonicDvdStatusWaitEventMarginCycles)
            {
                return false;
            }

            safeCycles = disc.PendingCommandCycles - SonicDvdStatusWaitEventMarginCycles;
        }
        else
        {
            uint slot = state.Gpr[27];
            if (slot >= 2)
            {
                return false;
            }

            uint shutdownFlag = bus.Memory.Read8(0x8000_30E3);
            uint slotAddress = unchecked(0x803B_D080u + slot * 0x110u);
            if ((shutdownFlag & 0x80u) != 0
                || !bus.Memory.IsMainRamAddress(slotAddress, sizeof(uint))
                || bus.Memory.Read32(slotAddress) != 0)
            {
                return false;
            }

            uint decrementer = state.Spr[22];
            if ((decrementer & 0x8000_0000) != 0)
            {
                return false;
            }

            safeCycles = decrementer;
        }

        if (TryGetCyclesUntilNextEnabledVideoInterrupt(bus, out ulong videoCycles))
        {
            if (videoCycles <= SonicDvdStatusWaitEventMarginCycles)
            {
                return false;
            }

            safeCycles = Math.Min(safeCycles, videoCycles - SonicDvdStatusWaitEventMarginCycles);
        }

        if (safeCycles < MinFastForwardSonicDvdStatusWaitCycles || safeCycles > int.MaxValue)
        {
            return false;
        }

        uint skipped = (uint)safeCycles;
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
        {
            return false;
        }

        state.Pc = SonicDvdStatusWaitLoopPc;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicDvdStatusWaitLoop(GameCubeBus bus) =>
        bus.Read32(SonicDvdStatusWaitLoopPc + 0x00) == 0x387B_0000
        && bus.Read32(SonicDvdStatusWaitLoopPc + 0x04) == 0x3881_0084
        && bus.Read32(SonicDvdStatusWaitLoopPc + 0x08) == 0x38A1_0080
        && bus.Read32(SonicDvdStatusWaitLoopPc + 0x0C) == 0x4809_4A7D
        && bus.Read32(SonicDvdStatusWaitLoopPc + 0x10) == 0x2C03_FFFF
        && bus.Read32(SonicDvdStatusWaitLoopPc + 0x14) == 0x4182_FFEC;

    private static bool TryFastForwardSonicInitTableLoopTail(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicInitTableLoopTailPc
            || !MatchesSonicInitTableLoopTail(bus)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicInitTableLoopTailInstructions, extraInstructions: 0))
        {
            return false;
        }

        state.Gpr[26] = unchecked(state.Gpr[26] + 0x30);
        state.Gpr[28] = unchecked(state.Gpr[28] + 4);
        state.Gpr[25] = unchecked(state.Gpr[25] + 4);
        state.Gpr[29] = unchecked(state.Gpr[29] + 1);
        uint signedByte = unchecked((uint)(sbyte)(byte)state.Gpr[29]);
        state.Gpr[0] = signedByte;
        SetCr0ForSignedCompareImmediate(state, signedByte, 43);
        state.Pc = unchecked((sbyte)(byte)state.Gpr[29]) < 43 ? SonicInitTableLoopPc : SonicInitTableLoopExitPc;
        AdvanceFastForwardedInstructions(state, bus, SonicInitTableLoopTailInstructions);
        skippedInstructions = checked((int)SonicInitTableLoopTailInstructions);
        return true;
    }

    private static bool MatchesSonicInitTableLoopTail(GameCubeBus bus) =>
        bus.Read32(SonicInitTableLoopTailPc + 0x00) == 0x3B5A_0030
        && bus.Read32(SonicInitTableLoopTailPc + 0x04) == 0x3B9C_0004
        && bus.Read32(SonicInitTableLoopTailPc + 0x08) == 0x3B39_0004
        && bus.Read32(SonicInitTableLoopTailPc + 0x0C) == 0x3BBD_0001
        && bus.Read32(SonicInitTableLoopTailPc + 0x10) == 0x7FA0_0774
        && bus.Read32(SonicInitTableLoopTailPc + 0x14) == 0x2C00_002B
        && bus.Read32(SonicInitTableLoopTailPc + 0x18) == 0x4180_F078;

    private static bool TryFastForwardSonicInitTableNullEntryLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicInitTableLoopPc
            || !MatchesSonicInitTableNullEntryLoop(bus))
        {
            return false;
        }

        int index = unchecked((sbyte)(byte)state.Gpr[29]);
        if (index < 0 || index >= 43)
        {
            return false;
        }

        uint cursor = state.Gpr[31];
        uint skipped = 0;
        int skippedEntries = 0;
        uint lastHalf = 0;
        uint decrementer = state.Spr[22];
        bool decrementerIsNegative = (decrementer & 0x8000_0000) != 0;
        while (index < 43)
        {
            if (!bus.Memory.IsMainRamAddress(cursor + 0x14, sizeof(uint))
                || !bus.Memory.IsMainRamAddress(cursor + 0x08, sizeof(ushort)))
            {
                break;
            }

            if (bus.Memory.Read32(cursor + 0x14) != 0xFFFF_FFFF)
            {
                break;
            }

            uint candidateSkipped = skipped + SonicInitTableNullEntryInstructions;
            if (!decrementerIsNegative && candidateSkipped > decrementer)
            {
                break;
            }

            skipped = candidateSkipped;
            skippedEntries++;
            lastHalf = bus.Memory.Read16(cursor + 0x08);
            cursor = unchecked(cursor + 0x30);
            state.Gpr[26] = unchecked(state.Gpr[26] + 0x30);
            state.Gpr[28] = unchecked(state.Gpr[28] + 4);
            state.Gpr[25] = unchecked(state.Gpr[25] + 4);
            index++;
        }

        if (skippedEntries == 0)
        {
            return false;
        }

        state.Gpr[3] = 0xFFFF_FFFF;
        state.Gpr[21] = lastHalf;
        state.Gpr[31] = cursor;
        state.Gpr[29] = unchecked((uint)index);
        uint signedByte = unchecked((uint)(sbyte)(byte)state.Gpr[29]);
        state.Gpr[0] = signedByte;
        SetCr0ForSignedCompareImmediate(state, signedByte, 43);
        state.Pc = unchecked((sbyte)(byte)state.Gpr[29]) < 43 ? SonicInitTableLoopPc : SonicInitTableLoopExitPc;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicInitTableNullEntryLoop(GameCubeBus bus) =>
        bus.Read32(SonicInitTableLoopPc + 0x00) == 0x807F_0014
        && bus.Read32(SonicInitTableLoopPc + 0x04) == 0xA2BF_0008
        && bus.Read32(SonicInitTableLoopPc + 0x08) == 0x2C03_FFFF
        && bus.Read32(SonicInitTableLoopPc + 0x0C) == 0x4082_000C
        && bus.Read32(SonicInitTableLoopPc + 0x10) == 0x3BFF_0030
        && bus.Read32(SonicInitTableLoopPc + 0x14) == 0x4800_0F5C
        && MatchesSonicInitTableLoopTail(bus);

    private static bool TryFastForwardSonicRecordHeaderScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicRecordHeaderScanLoopPc || !MatchesSonicRecordHeaderScanLoop(bus))
        {
            return false;
        }

        int index = unchecked((int)state.Gpr[30]);
        if (index < 0 || index >= SonicRecordHeaderScanRecordCount)
        {
            return false;
        }

        uint cursor = state.Gpr[31];
        uint skipped = 0;
        int skippedRecords = 0;
        uint lastRecord = cursor;
        uint lastByte = 0;
        uint decrementer = state.Spr[22];
        bool decrementerIsNegative = (decrementer & 0x8000_0000) != 0;
        while (index < SonicRecordHeaderScanRecordCount)
        {
            if (!bus.Memory.IsMainRamAddress(cursor, 2))
            {
                break;
            }

            byte first = bus.Memory.Read8(cursor);
            uint iterationInstructions;
            if (first == 1)
            {
                byte second = bus.Memory.Read8(cursor + 1);
                if (second == 2)
                {
                    break;
                }

                lastByte = second;
                iterationInstructions = 11;
            }
            else
            {
                lastByte = first;
                iterationInstructions = 8;
            }

            if (!decrementerIsNegative && skipped + iterationInstructions > decrementer)
            {
                break;
            }

            skipped += iterationInstructions;
            skippedRecords++;
            lastRecord = cursor;
            index++;
            cursor = unchecked(cursor + SonicRecordHeaderScanRecordStride);
        }

        if (skippedRecords == 0)
        {
            return false;
        }

        state.Gpr[0] = lastByte;
        state.Gpr[3] = lastRecord;
        state.Gpr[30] = unchecked((uint)index);
        state.Gpr[31] = cursor;
        SetCr0ForSignedCompareImmediate(state, state.Gpr[30], SonicRecordHeaderScanRecordCount);
        state.Pc = index < SonicRecordHeaderScanRecordCount ? SonicRecordHeaderScanLoopPc : SonicRecordHeaderScanExitPc;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicRecordHeaderScanLoop(GameCubeBus bus) =>
        bus.Read32(SonicRecordHeaderScanLoopPc + 0x00) == 0x881F_0000
        && bus.Read32(SonicRecordHeaderScanLoopPc + 0x04) == 0x387F_0000
        && bus.Read32(SonicRecordHeaderScanLoopPc + 0x08) == 0x2C00_0001
        && bus.Read32(SonicRecordHeaderScanLoopPc + 0x0C) == 0x4082_0014
        && bus.Read32(SonicRecordHeaderScanLoopPc + 0x10) == 0x881F_0001
        && bus.Read32(SonicRecordHeaderScanLoopPc + 0x14) == 0x2C00_0002
        && bus.Read32(SonicRecordHeaderScanLoopPc + 0x18) == 0x4082_0008
        && bus.Read32(SonicRecordHeaderScanLoopPc + 0x1C) == 0x4800_002D
        && bus.Read32(SonicRecordHeaderScanLoopPc + 0x20) == 0x3BDE_0001
        && bus.Read32(SonicRecordHeaderScanLoopPc + 0x24) == 0x2C1E_0028
        && bus.Read32(SonicRecordHeaderScanLoopPc + 0x28) == 0x3BFF_0040
        && bus.Read32(SonicRecordHeaderScanLoopPc + 0x2C) == 0x4180_FFD4;

    private static bool TryFastForwardSonicFlagRecordScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicFlagRecordScanLoopPc || !MatchesSonicFlagRecordScanLoop(bus))
        {
            return false;
        }

        int index = unchecked((int)state.Gpr[30]);
        if (index < 0 || index >= SonicFlagRecordScanRecordCount)
        {
            return false;
        }

        uint cursor = state.Gpr[31];
        uint skipped = 0;
        int skippedRecords = 0;
        uint lastFlag = 0;
        uint decrementer = state.Spr[22];
        bool decrementerIsNegative = (decrementer & 0x8000_0000) != 0;
        while (index < SonicFlagRecordScanRecordCount)
        {
            if (!bus.Memory.IsMainRamAddress(cursor, 1))
            {
                break;
            }

            byte flag = bus.Memory.Read8(cursor);
            if (flag == 1)
            {
                break;
            }

            uint candidateSkipped = skipped + SonicFlagRecordScanInstructionsPerSkippedRecord;
            if (!decrementerIsNegative && candidateSkipped > decrementer)
            {
                break;
            }

            skipped = candidateSkipped;
            skippedRecords++;
            lastFlag = flag;
            index++;
            cursor = unchecked(cursor + SonicFlagRecordScanRecordStride);
        }

        if (skippedRecords == 0)
        {
            return false;
        }

        state.Gpr[0] = lastFlag;
        state.Gpr[30] = unchecked((uint)index);
        state.Gpr[31] = cursor;
        SetCr0ForSignedCompareImmediate(state, state.Gpr[30], SonicFlagRecordScanRecordCount);
        state.Pc = index < SonicFlagRecordScanRecordCount ? SonicFlagRecordScanLoopPc : SonicFlagRecordScanExitPc;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicFlagRecordScanLoop(GameCubeBus bus) =>
        bus.Read32(SonicFlagRecordScanLoopPc + 0x00) == 0x881F_0000
        && bus.Read32(SonicFlagRecordScanLoopPc + 0x04) == 0x2C00_0001
        && bus.Read32(SonicFlagRecordScanLoopPc + 0x08) == 0x4082_000C
        && bus.Read32(SonicFlagRecordScanLoopPc + 0x0C) == 0x7FE3_FB78
        && bus.Read32(SonicFlagRecordScanLoopPc + 0x10) == 0x4800_0031
        && bus.Read32(SonicFlagRecordScanLoopPc + 0x14) == 0x3BDE_0001
        && bus.Read32(SonicFlagRecordScanLoopPc + 0x18) == 0x2C1E_0028
        && bus.Read32(SonicFlagRecordScanLoopPc + 0x1C) == 0x3BFF_0064
        && bus.Read32(SonicFlagRecordScanLoopPc + 0x20) == 0x4180_FFE0;

    private static bool TryFastForwardSonicTaskSlotCallbackScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicTaskSlotCallbackScanLoopPc || !MatchesSonicTaskSlotCallbackScanLoop(bus))
        {
            return false;
        }

        int index = unchecked((int)state.Gpr[30]);
        if (index < 0 || index >= SonicTaskSlotCallbackScanSlotCount)
        {
            return false;
        }

        uint cursor = state.Gpr[31];
        uint skipped = 0;
        int skippedSlots = 0;
        uint decrementer = state.Spr[22];
        bool decrementerIsNegative = (decrementer & 0x8000_0000) != 0;
        while (index < SonicTaskSlotCallbackScanSlotCount)
        {
            if (!bus.Memory.IsMainRamAddress(cursor, sizeof(uint)))
            {
                break;
            }

            if (bus.Memory.Read32(cursor) != 0)
            {
                break;
            }

            uint candidateSkipped = skipped + SonicTaskSlotCallbackScanInstructionsPerNullSlot;
            if (!decrementerIsNegative && candidateSkipped > decrementer)
            {
                break;
            }

            skipped = candidateSkipped;
            skippedSlots++;
            index++;
            cursor = unchecked(cursor + SonicTaskSlotCallbackScanSlotStride);
        }

        if (skippedSlots == 0)
        {
            return false;
        }

        state.Gpr[3] = 0;
        state.Gpr[30] = unchecked((uint)index);
        state.Gpr[31] = cursor;
        SetCr0ForSignedCompareImmediate(state, state.Gpr[30], SonicTaskSlotCallbackScanSlotCount);
        state.Pc = index < SonicTaskSlotCallbackScanSlotCount ? SonicTaskSlotCallbackScanLoopPc : SonicTaskSlotCallbackScanExitPc;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicTaskSlotCallbackScanLoop(GameCubeBus bus) =>
        bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x00) == 0x807F_0000
        && bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x04) == 0x2803_0000
        && bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x08) == 0x4182_0018
        && bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x0C) == 0x8183_0000
        && bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x10) == 0x280C_0000
        && bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x14) == 0x4182_000C
        && bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x18) == 0x7D88_03A6
        && bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x1C) == 0x4E80_0021
        && bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x20) == 0x3BDE_0001
        && bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x24) == 0x2C1E_0020
        && bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x28) == 0x3BFF_0010
        && bus.Read32(SonicTaskSlotCallbackScanLoopPc + 0x2C) == 0x4180_FFD4;

    private static bool TryFastForwardSonicBitmaskDispatchScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicBitmaskDispatchScanLoopPc || !MatchesSonicBitmaskDispatchScanLoop(bus))
        {
            return false;
        }

        uint cursor = state.Gpr[3];
        uint mask = state.Gpr[4];
        if (mask == 0)
        {
            return false;
        }

        uint skipped = 0;
        uint skippedEntries = 0;
        uint decrementer = state.Spr[22];
        bool decrementerIsNegative = (decrementer & 0x8000_0000) != 0;
        while (skippedEntries < MaxFastForwardSonicBitmaskDispatchScanEntries)
        {
            if (!bus.Memory.IsMainRamAddress(cursor, sizeof(uint)))
            {
                break;
            }

            uint word = bus.Memory.Read32(cursor);
            if ((mask & word) != 0)
            {
                break;
            }

            uint candidateSkipped = skipped + SonicBitmaskDispatchScanInstructionsPerZeroEntry;
            if (!decrementerIsNegative && candidateSkipped > decrementer)
            {
                break;
            }

            skipped = candidateSkipped;
            skippedEntries++;
            cursor = unchecked(cursor + sizeof(uint));
        }

        if (skippedEntries == 0)
        {
            return false;
        }

        state.Gpr[0] = 0;
        state.Gpr[3] = cursor;
        SetCr0ForUnsignedCompareImmediate(state, 0, 0);
        state.Pc = SonicBitmaskDispatchScanLoopPc;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicBitmaskDispatchScanLoop(GameCubeBus bus) =>
        bus.Read32(SonicBitmaskDispatchScanLoopPc + 0x00) == 0x8003_0000
        && bus.Read32(SonicBitmaskDispatchScanLoopPc + 0x04) == 0x7C80_0038
        && bus.Read32(SonicBitmaskDispatchScanLoopPc + 0x08) == 0x2800_0000
        && bus.Read32(SonicBitmaskDispatchScanLoopPc + 0x0C) == 0x4182_0010
        && bus.Read32(SonicBitmaskDispatchScanLoopPc + 0x10) == 0x7C00_0034
        && bus.Read32(SonicBitmaskDispatchScanLoopPc + 0x14) == 0x7C1D_0734
        && bus.Read32(SonicBitmaskDispatchScanLoopPc + 0x18) == 0x4800_000C
        && bus.Read32(SonicBitmaskDispatchScanLoopPc + 0x1C) == 0x3863_0004
        && bus.Read32(SonicBitmaskDispatchScanLoopPc + 0x20) == 0x4BFF_FFE0;

    private static bool TryFastForwardSonicResourceFlagWaitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if ((pc != SonicResourceFlagWaitLoopPc && pc != SonicResourceFlagWaitLoopPc + 4 && pc != SonicResourceFlagWaitLoopPc + 8)
            || !MatchesSonicResourceFlagWaitLoop(bus))
        {
            return false;
        }

        uint flagAddress = unchecked(state.Gpr[13] + SonicResourceFlagOffset);
        if (!bus.Memory.IsMainRamAddress(flagAddress, sizeof(uint)))
        {
            return false;
        }

        uint flag = pc == SonicResourceFlagWaitLoopPc + 4 ? state.Gpr[0] : bus.Memory.Read32(flagAddress);
        if (flag != 0)
        {
            return false;
        }

        if (pc == SonicResourceFlagWaitLoopPc + 8 && (state.Cr & 0xF000_0000u) != 0x2000_0000u)
        {
            return false;
        }

        uint decrementer = state.Spr[22];
        if ((decrementer & 0x8000_0000) != 0)
        {
            return false;
        }

        uint prefixInstructions = pc == SonicResourceFlagWaitLoopPc
            ? 0
            : pc == SonicResourceFlagWaitLoopPc + 4 ? 2u : 1u;
        if (decrementer < prefixInstructions)
        {
            return false;
        }

        uint iterations = (decrementer - prefixInstructions) / SonicResourceFlagWaitInstructionsPerIteration;
        if (iterations < MinFastForwardSonicResourceFlagWaitIterations)
        {
            return false;
        }

        uint skipped = checked(prefixInstructions + iterations * SonicResourceFlagWaitInstructionsPerIteration);
        state.Gpr[0] = 0;
        SetCr0ForUnsignedCompareImmediate(state, 0, 0);
        state.Pc = SonicResourceFlagWaitLoopPc;
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicResourceFlagWaitLoop(GameCubeBus bus) =>
        bus.Read32(SonicResourceFlagWaitLoopPc + 0x00) == 0x800D_8A20
        && bus.Read32(SonicResourceFlagWaitLoopPc + 0x04) == 0x2800_0000
        && bus.Read32(SonicResourceFlagWaitLoopPc + 0x08) == 0x4182_FFF8;

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
        if (opcode == 0xCB)
        {
            return false;
        }

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

        if ((opcode == 0xCA || opcode == 0xCB) && state.Gpr[29] != 0)
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

        uint nextRecord = unchecked(record + 8);
        if (!memory.IsMainRamAddress(nextRecord + 2, sizeof(byte)))
        {
            return false;
        }

        byte nextOpcode = memory.Read8(nextRecord + 2);
        state.Gpr[4] = nextOpcode;
        state.Gpr[30] = nextRecord;
        SetCr0ForUnsignedCompareImmediate(state, nextOpcode, 0xCB);
        terminalRecord = nextOpcode == 0xCB;
        state.Pc = terminalRecord ? 0x800E_835C : 0x800E_81A8;
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

    private static bool TryFastForwardSonicModeCoordinatorPrologue(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            uint stackPointer = state.Gpr[1];
            uint newStackPointer = unchecked(stackPointer - 40);
            if (state.Pc != SonicModeCoordinatorProloguePc
                || !MatchesSonicModeCoordinatorPrologue(bus)
                || stackPointer > uint.MaxValue - sizeof(uint)
                || !bus.Memory.IsMainRamAddress(stackPointer + sizeof(uint), sizeof(uint))
                || !bus.Memory.IsMainRamAddress(newStackPointer, 40)
                || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicModeCoordinatorPrologueInstructions, extraInstructions: 0))
            {
                return false;
            }

            uint oldLr = state.Lr;
            bus.Write32(stackPointer + 4, oldLr);
            bus.Write32(newStackPointer, stackPointer);
            for (int register = 26; register <= 31; register++)
            {
                bus.Write32(newStackPointer + 16u + (uint)(register - 26) * sizeof(uint), state.Gpr[register]);
            }

            state.Gpr[0] = oldLr;
            state.Gpr[1] = newStackPointer;
            state.Gpr[3] = SonicModeStateBase;
            state.Gpr[28] = 0;
            state.Gpr[29] = 0;
            state.Gpr[31] = SonicModeStateBase + 0x80;
            state.Pc = SonicModeCoordinatorPrologueExitPc;
            AdvanceFastForwardedInstructions(state, bus, SonicModeCoordinatorPrologueInstructions);
            skippedInstructions = checked((int)SonicModeCoordinatorPrologueInstructions);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicModeCoordinatorPrologue(GameCubeBus bus) =>
        bus.Read32(SonicModeCoordinatorProloguePc + 0x00) == 0x7C08_02A6
        && bus.Read32(SonicModeCoordinatorProloguePc + 0x04) == 0x3C60_801D
        && bus.Read32(SonicModeCoordinatorProloguePc + 0x08) == 0x9001_0004
        && bus.Read32(SonicModeCoordinatorProloguePc + 0x0C) == 0x3863_C168
        && bus.Read32(SonicModeCoordinatorProloguePc + 0x10) == 0x9421_FFD8
        && bus.Read32(SonicModeCoordinatorProloguePc + 0x14) == 0xBF41_0010
        && bus.Read32(SonicModeCoordinatorProloguePc + 0x18) == 0x3BE3_0080
        && bus.Read32(SonicModeCoordinatorProloguePc + 0x1C) == 0x3BA0_0000
        && bus.Read32(SonicModeCoordinatorProloguePc + 0x20) == 0x3B80_0000;

    private static bool TryFastForwardSonicModeCoordinatorBody(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        bool startsAtStateBranch = state.Pc == SonicModeCoordinatorBodyPc;
        bool startsAtBodyBranch = state.Pc == SonicModeCoordinatorBodyBranchPc;
        if ((!startsAtStateBranch && !startsAtBodyBranch)
            || !MatchesSonicModeCoordinatorBody(bus)
            || !MatchesSonicModeChildStatusPoll(bus)
            || !MatchesSonicModeQuery(bus)
            || !MatchesSonicModeStateUpdate(bus)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 512, extraInstructions: 0))
        {
            return false;
        }

        PowerPcState originalState = state.Clone();
        try
        {
            uint callerInstructions = 0;
            if (startsAtStateBranch)
            {
                uint stateWordAddress = unchecked(state.Gpr[3] + 0x80);
                if (!bus.Memory.IsMainRamAddress(stateWordAddress, sizeof(uint)))
                {
                    return false;
                }

                uint stateWord = bus.Memory.Read32(stateWordAddress);
                state.Gpr[0] = stateWord;
                SetCr0ForUnsignedCompareImmediate(state, stateWord, 8);
                callerInstructions += 3;
                if (stateWord == 8)
                {
                    CopyPowerPcState(originalState, state);
                    return false;
                }
            }

            state.Gpr[30] = 0;
            callerInstructions++;

            state.Lr = 0x800E_312C;
            state.Pc = SonicModeChildStatusPollPc;
            callerInstructions++;
            if (!TryFastForwardSonicModeChildStatusPoll(state, bus, out int childStatusInstructions) || state.Pc != 0x800E_312C)
            {
                CopyPowerPcState(originalState, state);
                return false;
            }

            state.Gpr[27] = state.Gpr[3];
            callerInstructions++;

            state.Lr = 0x800E_3134;
            state.Pc = SonicModeQueryPc;
            callerInstructions++;
            if (!TryFastForwardSonicModeQuery(state, bus, out int modeQueryInstructions) || state.Pc != 0x800E_3134)
            {
                CopyPowerPcState(originalState, state);
                return false;
            }

            state.Gpr[26] = state.Gpr[3];
            callerInstructions++;
            state.Gpr[0] = unchecked(state.Gpr[26] - 4);
            callerInstructions++;
            SetCr0ForUnsignedCompareImmediate(state, state.Gpr[0], 2);
            callerInstructions++;

            bool updateState = state.Gpr[0] <= 2;
            callerInstructions++;
            if (!updateState)
            {
                SetCr0ForSignedCompareImmediate(state, state.Gpr[26], -1);
                callerInstructions += 2;
                updateState = state.Gpr[26] == 0xFFFF_FFFF;
            }

            if (!updateState)
            {
                SetCr0ForSignedCompareImmediate(state, state.Gpr[26], 11);
                callerInstructions += 2;
                updateState = state.Gpr[26] == 11;
            }

            if (!updateState)
            {
                SetCr0ForSignedCompareImmediate(state, state.Gpr[27], 0);
                callerInstructions += 2;
                if (state.Gpr[27] != 0)
                {
                    state.Gpr[26] = 11;
                    callerInstructions++;
                }
                else
                {
                    SetCr0ForSignedCompareImmediate(state, state.Gpr[30], 4);
                    callerInstructions += 2;
                    if (state.Gpr[30] == 4)
                    {
                        state.Gpr[26] = 11;
                        callerInstructions++;
                    }
                }
            }

            state.Gpr[3] = state.Gpr[26];
            callerInstructions++;
            state.Lr = SonicModeCoordinatorBodyExitPc;
            state.Pc = SonicModeStateUpdatePc;
            callerInstructions++;
            if (!TryFastForwardSonicModeStateUpdate(state, bus, out int stateUpdateInstructions) || state.Pc != SonicModeCoordinatorBodyExitPc)
            {
                CopyPowerPcState(originalState, state);
                return false;
            }

            AdvanceFastForwardedInstructions(state, bus, callerInstructions);
            skippedInstructions = checked((int)(callerInstructions + (uint)childStatusInstructions + (uint)modeQueryInstructions + (uint)stateUpdateInstructions));
            return true;
        }
        catch (AddressTranslationException)
        {
            CopyPowerPcState(originalState, state);
            return false;
        }
    }

    private static bool MatchesSonicModeCoordinatorBody(GameCubeBus bus) =>
        bus.Read32(SonicModeCoordinatorBodyPc + 0x00) == 0x8003_0080
        && bus.Read32(SonicModeCoordinatorBodyPc + 0x04) == 0x2800_0008
        && bus.Read32(SonicModeCoordinatorBodyPc + 0x08) == 0x4082_002C
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x00) == 0x3BC0_0000
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x04) == 0x4BFF_FAE5
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x08) == 0x7C7B_1B78
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x0C) == 0x4800_E279
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x10) == 0x3B43_0000
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x14) == 0x381A_FFFC
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x18) == 0x2800_0002
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x1C) == 0x4081_0028
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x20) == 0x2C1A_FFFF
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x24) == 0x4182_0020
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x28) == 0x2C1A_000B
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x2C) == 0x4182_0018
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x30) == 0x2C1B_0000
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x34) == 0x4082_000C
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x38) == 0x2C1E_0004
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x3C) == 0x4082_0008
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x40) == 0x3B40_000B
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x44) == 0x7F43_D378
        && bus.Read32(SonicModeCoordinatorBodyBranchPc + 0x48) == 0x4BFF_FCB5
        && bus.Read32(SonicModeCoordinatorBodyExitPc + 0x00) == 0x3C60_801D;

    private static void CopyPowerPcState(PowerPcState source, PowerPcState destination)
    {
        destination.Pc = source.Pc;
        destination.Lr = source.Lr;
        destination.Ctr = source.Ctr;
        destination.Cr = source.Cr;
        destination.Xer = source.Xer;
        destination.Fpscr = source.Fpscr;
        destination.Msr = source.Msr;
        destination.TimeBase = source.TimeBase;
        destination.Halted = source.Halted;
        destination.HasReservation = source.HasReservation;
        destination.ReservationAddress = source.ReservationAddress;
        Array.Copy(source.Gpr, destination.Gpr, source.Gpr.Length);
        Array.Copy(source.Fpr, destination.Fpr, source.Fpr.Length);
        Array.Copy(source.FprPair1, destination.FprPair1, source.FprPair1.Length);
        Array.Copy(source.Spr, destination.Spr, source.Spr.Length);
        Array.Copy(source.SegmentRegisters, destination.SegmentRegisters, source.SegmentRegisters.Length);
    }

    private static bool TryFastForwardSonicModeCoordinatorZeroTail(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            uint stackPointer = state.Gpr[1];
            if (state.Pc != SonicModeCoordinatorZeroTailPc
                || !MatchesSonicModeCoordinatorZeroTail(bus)
                || !bus.Memory.IsMainRamAddress(SonicModeStateOutputByteAddress, sizeof(byte))
                || !bus.Memory.IsMainRamAddress(stackPointer + 44, sizeof(uint))
                || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicModeCoordinatorZeroTailInstructions, extraInstructions: 0))
            {
                return false;
            }

            byte outputState = bus.Memory.Read8(SonicModeStateOutputByteAddress);
            if (outputState != 0)
            {
                return false;
            }

            for (int register = 26; register <= 31; register++)
            {
                uint registerAddress = stackPointer + 16u + (uint)(register - 26) * sizeof(uint);
                if (!bus.Memory.IsMainRamAddress(registerAddress, sizeof(uint)))
                {
                    return false;
                }
            }

            state.Gpr[3] = SonicModeStateBase;
            state.Gpr[30] = SonicModeStateOutputByteAddress;
            state.Gpr[0] = 0;
            SetCr0ForSignedCompareImmediate(state, 0, 0);

            for (int register = 26; register <= 31; register++)
            {
                state.Gpr[register] = bus.Memory.Read32(stackPointer + 16u + (uint)(register - 26) * sizeof(uint));
            }

            uint oldLr = bus.Memory.Read32(stackPointer + 44);
            state.Gpr[0] = oldLr;
            state.Gpr[1] = stackPointer + 40;
            state.Lr = oldLr;
            state.Pc = oldLr & 0xFFFF_FFFCu;
            AdvanceFastForwardedInstructions(state, bus, SonicModeCoordinatorZeroTailInstructions);
            skippedInstructions = checked((int)SonicModeCoordinatorZeroTailInstructions);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicModeCoordinatorZeroTail(GameCubeBus bus) =>
        bus.Read32(SonicModeCoordinatorZeroTailPc + 0x00) == 0x3C60_801D
        && bus.Read32(SonicModeCoordinatorZeroTailPc + 0x04) == 0x3863_C168
        && bus.Read32(SonicModeCoordinatorZeroTailPc + 0x08) == 0x3BC3_0013
        && bus.Read32(SonicModeCoordinatorZeroTailPc + 0x0C) == 0x8803_0013
        && bus.Read32(SonicModeCoordinatorZeroTailPc + 0x10) == 0x7C00_0775
        && bus.Read32(SonicModeCoordinatorZeroTailPc + 0x14) == 0x4182_01F0
        && bus.Read32(SonicModeCoordinatorEpiloguePc + 0x00) == 0xBB41_0010
        && bus.Read32(SonicModeCoordinatorEpiloguePc + 0x04) == 0x8001_002C
        && bus.Read32(SonicModeCoordinatorEpiloguePc + 0x08) == 0x3821_0028
        && bus.Read32(SonicModeCoordinatorEpiloguePc + 0x0C) == 0x7C08_03A6
        && bus.Read32(SonicModeCoordinatorEpiloguePc + 0x10) == 0x4E80_0020;

    private static bool TryFastForwardSonicStatusQuery(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            uint stackPointer = state.Gpr[1];
            uint newStackPointer = unchecked(stackPointer - 40);
            uint currentIdAddress = unchecked(state.Gpr[13] + SonicStatusQueryCurrentIdOffset);
            uint currentPointerAddress = unchecked(state.Gpr[13] + SonicStatusQueryCurrentPointerOffset);
            if (state.Pc != SonicStatusQueryPc
                || !MatchesSonicStatusQuery(bus)
                || stackPointer > uint.MaxValue - sizeof(uint)
                || !bus.Memory.IsMainRamAddress(stackPointer + sizeof(uint), sizeof(uint))
                || !bus.Memory.IsMainRamAddress(newStackPointer, 40)
                || !bus.Memory.IsMainRamAddress(currentIdAddress, sizeof(uint))
                || !bus.Memory.IsMainRamAddress(currentPointerAddress, sizeof(uint))
                || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicStatusQueryEarlyReturnInstructions, extraInstructions: 0))
            {
                return false;
            }

            uint queryId = state.Gpr[3];
            uint currentId = bus.Memory.Read32(currentIdAddress);
            if (queryId != currentId)
            {
                return false;
            }

            uint currentPointer = bus.Memory.Read32(currentPointerAddress);
            if (currentPointer == 0 || !bus.Memory.IsMainRamAddress(currentPointer + 1, sizeof(byte)))
            {
                return false;
            }

            uint result = unchecked((uint)(sbyte)bus.Memory.Read8(currentPointer + 1));
            if (result == 3)
            {
                return false;
            }

            uint oldLr = state.Lr;
            bus.Write32(stackPointer + 4, oldLr);
            bus.Write32(newStackPointer, stackPointer);
            for (int register = 26; register <= 31; register++)
            {
                bus.Write32(newStackPointer + 16u + (uint)(register - 26) * sizeof(uint), state.Gpr[register]);
            }

            state.Gpr[0] = oldLr;
            state.Gpr[1] = stackPointer;
            state.Gpr[3] = result;
            for (int register = 26; register <= 31; register++)
            {
                state.Gpr[register] = bus.Memory.Read32(newStackPointer + 16u + (uint)(register - 26) * sizeof(uint));
            }

            state.Lr = oldLr;
            state.Pc = oldLr & 0xFFFF_FFFCu;
            SetCr0ForSignedCompareImmediate(state, result, 3);
            AdvanceFastForwardedInstructions(state, bus, SonicStatusQueryEarlyReturnInstructions);
            skippedInstructions = checked((int)SonicStatusQueryEarlyReturnInstructions);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicStatusQuery(GameCubeBus bus) =>
        bus.Read32(SonicStatusQueryPc + 0x00) == 0x7C08_02A6
        && bus.Read32(SonicStatusQueryPc + 0x04) == 0x9001_0004
        && bus.Read32(SonicStatusQueryPc + 0x08) == 0x9421_FFD8
        && bus.Read32(SonicStatusQueryPc + 0x0C) == 0xBF41_0010
        && bus.Read32(SonicStatusQueryPc + 0x1C) == 0x800D_9074
        && bus.Read32(SonicStatusQueryPc + 0x20) == 0x7C1A_0000
        && bus.Read32(SonicStatusQueryPc + 0x24) == 0x4182_0014
        && bus.Read32(SonicStatusQueryPc + 0x38) == 0x806D_9078
        && bus.Read32(SonicStatusQueryPc + 0x40) == 0x4082_0014
        && bus.Read32(SonicStatusQueryPc + 0x54) == 0x8803_0001
        && bus.Read32(SonicStatusQueryPc + 0x60) == 0x2C1B_0003
        && bus.Read32(SonicStatusQueryPc + 0x64) == 0x4182_000C
        && bus.Read32(SonicStatusQueryPc + 0x68) == 0x7F63_DB78
        && bus.Read32(SonicStatusQueryPc + 0x6C) == 0x4800_0228
        && bus.Read32(SonicStatusQueryEpiloguePc + 0x00) == 0xBB41_0010
        && bus.Read32(SonicStatusQueryEpiloguePc + 0x04) == 0x8001_002C
        && bus.Read32(SonicStatusQueryEpiloguePc + 0x08) == 0x3821_0028
        && bus.Read32(SonicStatusQueryEpiloguePc + 0x0C) == 0x7C08_03A6
        && bus.Read32(SonicStatusQueryEpiloguePc + 0x10) == 0x4E80_0020;

    private static bool TryFastForwardSonicStatusCallerLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicStatusCallerLoopPc
            || !MatchesSonicStatusCallerLoop(bus)
            || !MatchesSonicStatusQuery(bus)
            || !MatchesSonicModeCoordinatorPrologue(bus)
            || !MatchesSonicModeCoordinatorBody(bus)
            || !MatchesSonicModeCoordinatorZeroTail(bus)
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 1024, extraInstructions: 0))
        {
            return false;
        }

        PowerPcState originalState = state.Clone();
        try
        {
            uint currentIdAddress = unchecked(state.Gpr[13] + SonicStatusQueryCurrentIdOffset);
            uint currentPointerAddress = unchecked(state.Gpr[13] + SonicStatusQueryCurrentPointerOffset);
            if (!bus.Memory.IsMainRamAddress(currentIdAddress, sizeof(uint))
                || !bus.Memory.IsMainRamAddress(currentPointerAddress, sizeof(uint)))
            {
                return false;
            }

            uint currentPointer = bus.Memory.Read32(currentPointerAddress);
            if (bus.Memory.Read32(currentIdAddress) != 0
                || currentPointer == 0
                || !bus.Memory.IsMainRamAddress(currentPointer + 1, sizeof(byte)))
            {
                return false;
            }

            uint status = unchecked((uint)(sbyte)bus.Memory.Read8(currentPointer + 1));
            if (status == 3 || status == 4)
            {
                return false;
            }

            uint callerInstructions = 0;
            state.Gpr[3] = 0;
            callerInstructions++;

            state.Lr = 0x8001_2B2C;
            state.Pc = SonicStatusQueryPc;
            callerInstructions++;
            if (!TryFastForwardSonicStatusQuery(state, bus, out int statusQueryInstructions) || state.Pc != 0x8001_2B2C)
            {
                CopyPowerPcState(originalState, state);
                return false;
            }

            SetCr0ForSignedCompareImmediate(state, state.Gpr[3], 3);
            callerInstructions += 2;
            SetCr0ForSignedCompareImmediate(state, state.Gpr[3], 4);
            callerInstructions += 2;

            state.Lr = SonicStatusCallerLoopBackPc;
            state.Pc = SonicModeCoordinatorProloguePc;
            callerInstructions++;
            if (!TryFastForwardSonicModeCoordinatorPrologue(state, bus, out int coordinatorPrologueInstructions)
                || !TryFastForwardSonicModeCoordinatorBody(state, bus, out int coordinatorBodyInstructions)
                || !TryFastForwardSonicModeCoordinatorZeroTail(state, bus, out int coordinatorZeroTailInstructions)
                || state.Pc != SonicStatusCallerLoopBackPc)
            {
                CopyPowerPcState(originalState, state);
                return false;
            }

            state.Pc = SonicStatusCallerLoopPc;
            callerInstructions++;
            AdvanceFastForwardedInstructions(state, bus, callerInstructions);
            skippedInstructions = checked((int)(callerInstructions
                + (uint)statusQueryInstructions
                + (uint)coordinatorPrologueInstructions
                + (uint)coordinatorBodyInstructions
                + (uint)coordinatorZeroTailInstructions));
            return true;
        }
        catch (AddressTranslationException)
        {
            CopyPowerPcState(originalState, state);
            return false;
        }
    }

    private static bool MatchesSonicStatusCallerLoop(GameCubeBus bus) =>
        bus.Read32(SonicStatusCallerLoopPc + 0x00) == 0x3860_0000
        && bus.Read32(SonicStatusCallerLoopPc + 0x04) == 0x4812_41C5
        && bus.Read32(SonicStatusCallerLoopPc + 0x08) == 0x2C03_0003
        && bus.Read32(SonicStatusCallerLoopPc + 0x0C) == 0x4082_0010
        && bus.Read32(SonicStatusCallerLoopPc + 0x1C) == 0x2C03_0004
        && bus.Read32(SonicStatusCallerLoopPc + 0x20) == 0x4082_0010
        && bus.Read32(SonicStatusCallerDispatchPc + 0x00) == 0x480D_0579
        && bus.Read32(SonicStatusCallerLoopBackPc + 0x00) == 0x4BFF_FFCC;

    private static bool TryFastForwardSonicStatusCallerDispatch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicStatusCallerLoop(bus))
        {
            return false;
        }

        switch (state.Pc)
        {
            case SonicStatusCallerLoopPc:
                if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 2, extraInstructions: 0))
                {
                    return false;
                }

                state.Gpr[3] = 0;
                state.Lr = SonicStatusCallerPostQueryPc;
                state.Pc = SonicStatusQueryPc;
                AdvanceFastForwardedInstructions(state, bus, 2);
                skippedInstructions = 2;
                return true;

            case SonicStatusCallerPostQueryPc:
                return TryFastForwardSonicStatusCallerPostQuery(state, bus, out skippedInstructions);

            case SonicStatusCallerDispatchPc:
                if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 1, extraInstructions: 0))
                {
                    return false;
                }

                state.Lr = SonicStatusCallerLoopBackPc;
                state.Pc = SonicModeCoordinatorProloguePc;
                AdvanceFastForwardedInstructions(state, bus, 1);
                skippedInstructions = 1;
                return true;

            case SonicStatusCallerLoopBackPc:
                if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 1, extraInstructions: 0))
                {
                    return false;
                }

                state.Pc = SonicStatusCallerLoopPc;
                AdvanceFastForwardedInstructions(state, bus, 1);
                skippedInstructions = 1;
                return true;

            default:
                return false;
        }
    }

    private static bool TryFastForwardSonicStatusCallerPostQuery(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint result = state.Gpr[3];
        if (result == 3)
        {
            uint storeAddress = unchecked(state.Gpr[13] + 0xFFFF_8084u);
            if (!bus.Memory.IsMainRamAddress(state.Gpr[29], sizeof(byte))
                || !bus.Memory.IsMainRamAddress(storeAddress, sizeof(byte))
                || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 5, extraInstructions: 0))
            {
                return false;
            }

            byte value = bus.Memory.Read8(state.Gpr[29]);
            bus.Write8(storeAddress, value);
            state.Gpr[0] = value;
            SetCr0ForSignedCompareImmediate(state, result, 3);
            state.Pc = SonicStatusCallerExitPc;
            AdvanceFastForwardedInstructions(state, bus, 5);
            skippedInstructions = 5;
            return true;
        }

        if (result == 4)
        {
            uint storeAddress = unchecked(state.Gpr[13] + 0xFFFF_8084u);
            if (!bus.Memory.IsMainRamAddress(storeAddress, sizeof(byte))
                || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 7, extraInstructions: 0))
            {
                return false;
            }

            bus.Write8(storeAddress, 0xFF);
            state.Gpr[0] = 0xFFFF_FFFF;
            SetCr0ForSignedCompareImmediate(state, result, 4);
            state.Pc = SonicStatusCallerExitPc;
            AdvanceFastForwardedInstructions(state, bus, 7);
            skippedInstructions = 7;
            return true;
        }

        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: 4, extraInstructions: 0))
        {
            return false;
        }

        SetCr0ForSignedCompareImmediate(state, result, 4);
        state.Pc = SonicStatusCallerDispatchPc;
        AdvanceFastForwardedInstructions(state, bus, 4);
        skippedInstructions = 4;
        return true;
    }

    private static bool TryFastForwardSonicTableByteBuildDispatch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicTableByteBuildDispatch(bus))
        {
            return false;
        }

        switch (state.Pc)
        {
            case SonicTableByteBuildLoopPc:
                if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicTableByteBuildCallInstructions, extraInstructions: 0))
                {
                    return false;
                }

                state.Gpr[3] = state.Gpr[28];
                state.Lr = SonicTableByteBuildPostCallPc;
                state.Pc = SonicTableByteClassifierPc;
                AdvanceFastForwardedInstructions(state, bus, SonicTableByteBuildCallInstructions);
                skippedInstructions = checked((int)SonicTableByteBuildCallInstructions);
                return true;

            case SonicTableByteBuildPostCallPc:
                if (!bus.Memory.IsMainRamAddress(state.Gpr[30], sizeof(byte))
                    || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicTableByteBuildPostCallInstructions, extraInstructions: 0))
                {
                    return false;
                }

                state.Gpr[29] = unchecked(state.Gpr[29] + 1);
                state.Gpr[0] = unchecked((uint)(sbyte)(byte)state.Gpr[3]);
                bus.Write8(state.Gpr[30], (byte)state.Gpr[0]);
                SetCr0ForSignedCompareImmediate(state, state.Gpr[29], (int)SonicTableByteBuildRecordCount);
                state.Gpr[30] = unchecked(state.Gpr[30] + 1);
                state.Gpr[28] = unchecked(state.Gpr[28] + SonicTableByteBuildRecordStride);
                state.Pc = state.Gpr[29] < SonicTableByteBuildRecordCount ? SonicTableByteBuildLoopPc : SonicTableByteBuildExitPc;
                AdvanceFastForwardedInstructions(state, bus, SonicTableByteBuildPostCallInstructions);
                skippedInstructions = checked((int)SonicTableByteBuildPostCallInstructions);
                return true;

            default:
                return false;
        }
    }

    private static bool MatchesSonicTableByteBuildDispatch(GameCubeBus bus) =>
        bus.Read32(SonicTableByteBuildLoopPc + 0x00) == 0x7F83_E378
        && bus.Read32(SonicTableByteBuildLoopPc + 0x04) == 0x4800_0DB9
        && bus.Read32(SonicTableByteBuildPostCallPc + 0x00) == 0x3BBD_0001
        && bus.Read32(SonicTableByteBuildPostCallPc + 0x04) == 0x7C60_0774
        && bus.Read32(SonicTableByteBuildPostCallPc + 0x08) == 0x981E_0000
        && bus.Read32(SonicTableByteBuildPostCallPc + 0x0C) == 0x2C1D_0D5F
        && bus.Read32(SonicTableByteBuildPostCallPc + 0x10) == 0x3BDE_0001
        && bus.Read32(SonicTableByteBuildPostCallPc + 0x14) == 0x3B9C_0048
        && bus.Read32(SonicTableByteBuildPostCallPc + 0x18) == 0x4180_FFE0
        && bus.Read32(SonicTableByteClassifierPc + 0x00) == 0x9421_FFD8
        && bus.Read32(SonicTableByteClassifierPc + 0x04) == 0x3800_0018;

    private static bool TryFastForwardSonicLineCopy(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (!MatchesSonicLineCopy(bus)
            || (state.Pc != SonicLineCopyLoadPc && state.Pc != SonicLineCopyLoopPc))
        {
            return false;
        }

        try
        {
            uint source = state.Gpr[3];
            uint destination = state.Gpr[6];
            uint currentByte;
            uint skipped = 0;

            if (state.Pc == SonicLineCopyLoadPc)
            {
                if (!bus.Memory.IsMainRamAddress(source, sizeof(byte))
                    || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicLineCopyLoadInstructions, extraInstructions: 0))
                {
                    return false;
                }

                currentByte = bus.Memory.Read8(source);
                state.Gpr[5] = currentByte;
                state.Gpr[4] = unchecked((uint)(sbyte)(byte)currentByte);
                SetCr0ForSignedCompareImmediate(state, state.Gpr[4], 0);
                skipped += SonicLineCopyLoadInstructions;
                if ((byte)currentByte == 0)
                {
                    state.Pc = SonicLineCopyExitPc;
                    AdvanceFastForwardedInstructions(state, bus, skipped);
                    skippedInstructions = checked((int)skipped);
                    return true;
                }
            }
            else
            {
                currentByte = state.Gpr[5] & 0xFF;
                if (!bus.Memory.IsMainRamAddress(source, sizeof(byte)) || bus.Memory.Read8(source) != (byte)currentByte)
                {
                    return false;
                }
            }

            uint copied = 0;
            while (copied <= MaxFastForwardStringCopyBytes)
            {
                state.Gpr[4] = unchecked((uint)(sbyte)(byte)currentByte);
                skipped++;
                SetCr0ForSignedCompareImmediate(state, state.Gpr[4], 13);
                skipped += 2;
                if ((sbyte)(byte)currentByte == 13)
                {
                    state.Gpr[3] = source;
                    state.Gpr[6] = destination;
                    state.Pc = SonicLineCopyExitPc;
                    AdvanceFastForwardedInstructions(state, bus, skipped);
                    skippedInstructions = checked((int)skipped);
                    return true;
                }

                SetCr0ForSignedCompareImmediate(state, state.Gpr[4], 10);
                skipped += 2;
                if ((sbyte)(byte)currentByte == 10)
                {
                    state.Gpr[3] = source;
                    state.Gpr[6] = destination;
                    state.Pc = SonicLineCopyExitPc;
                    AdvanceFastForwardedInstructions(state, bus, skipped);
                    skippedInstructions = checked((int)skipped);
                    return true;
                }

                if (!bus.Memory.IsMainRamAddress(source, sizeof(byte))
                    || !bus.Memory.IsMainRamAddress(destination, sizeof(byte))
                    || bus.Memory.Read8(source) != (byte)currentByte)
                {
                    return false;
                }

                state.Gpr[4] = bus.Memory.Read8(source);
                source = unchecked(source + 1);
                bus.Write8(destination, (byte)state.Gpr[4]);
                destination = unchecked(destination + 1);
                if (!bus.Memory.IsMainRamAddress(source, sizeof(byte)))
                {
                    return false;
                }

                currentByte = bus.Memory.Read8(source);
                state.Gpr[5] = currentByte;
                state.Gpr[4] = unchecked((uint)(sbyte)(byte)currentByte);
                SetCr0ForSignedCompareImmediate(state, state.Gpr[4], 0);
                skipped += 7;
                copied++;

                if ((byte)currentByte == 0)
                {
                    state.Gpr[3] = source;
                    state.Gpr[6] = destination;
                    state.Pc = SonicLineCopyExitPc;
                    AdvanceFastForwardedInstructions(state, bus, skipped);
                    skippedInstructions = checked((int)skipped);
                    return true;
                }

            }

            return false;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicLineCopy(GameCubeBus bus) =>
        bus.Read32(SonicLineCopyLoopPc + 0x00) == 0x7CA4_0774
        && bus.Read32(SonicLineCopyLoopPc + 0x04) == 0x2C04_000D
        && bus.Read32(SonicLineCopyLoopPc + 0x08) == 0x4182_0028
        && bus.Read32(SonicLineCopyLoopPc + 0x0C) == 0x2C04_000A
        && bus.Read32(SonicLineCopyLoopPc + 0x10) == 0x4182_0020
        && bus.Read32(SonicLineCopyLoopPc + 0x14) == 0x8883_0000
        && bus.Read32(SonicLineCopyLoopPc + 0x18) == 0x3863_0001
        && bus.Read32(SonicLineCopyLoopPc + 0x1C) == 0x9886_0000
        && bus.Read32(SonicLineCopyLoopPc + 0x20) == 0x38C6_0001
        && bus.Read32(SonicLineCopyLoadPc + 0x00) == 0x88A3_0000
        && bus.Read32(SonicLineCopyLoadPc + 0x04) == 0x7CA4_0775
        && bus.Read32(SonicLineCopyLoadPc + 0x08) == 0x4082_FFD4
        && bus.Read32(SonicLineCopyExitPc + 0x00) == 0x3860_000A;

    private static bool TryFastForwardSonicLineSkip(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicLineSkipLoopPc || !MatchesSonicLineSkip(bus))
        {
            return false;
        }

        try
        {
            uint cursor = state.Gpr[26];
            uint ordinaryBytes = 0;
            while (ordinaryBytes <= MaxFastForwardStringCopyBytes)
            {
                if (!bus.Memory.IsMainRamAddress(cursor, sizeof(byte)))
                {
                    return false;
                }

                byte currentByte = bus.Memory.Read8(cursor);
                if (currentByte == 0)
                {
                    uint skipped = checked(ordinaryBytes * SonicLineSkipInstructionsPerOrdinaryByte + 3);
                    if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
                    {
                        return false;
                    }

                    state.Gpr[3] = 0;
                    state.Gpr[0] = 0;
                    state.Gpr[26] = cursor;
                    SetCr0ForSignedCompareImmediate(state, 0, 0);
                    state.Pc = SonicLineSkipNulPc;
                    AdvanceFastForwardedInstructions(state, bus, skipped);
                    skippedInstructions = checked((int)skipped);
                    return true;
                }

                if (currentByte == 10)
                {
                    uint skipped = checked(ordinaryBytes * SonicLineSkipInstructionsPerOrdinaryByte + 8);
                    if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
                    {
                        return false;
                    }

                    state.Gpr[3] = currentByte;
                    state.Gpr[0] = unchecked((uint)(sbyte)currentByte);
                    state.Gpr[26] = unchecked(cursor + 1);
                    SetCr0ForSignedCompareImmediate(state, state.Gpr[0], 10);
                    state.Pc = SonicLineSkipContinuePc;
                    AdvanceFastForwardedInstructions(state, bus, skipped);
                    skippedInstructions = checked((int)skipped);
                    return true;
                }

                if (currentByte == 13)
                {
                    uint nextAddress = unchecked(cursor + 1);
                    if (!bus.Memory.IsMainRamAddress(nextAddress, sizeof(byte)))
                    {
                        return false;
                    }

                    byte nextByte = bus.Memory.Read8(nextAddress);
                    bool consumedLf = nextByte == 10;
                    uint skipped = checked(ordinaryBytes * SonicLineSkipInstructionsPerOrdinaryByte + (consumedLf ? 13u : 11u));
                    if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
                    {
                        return false;
                    }

                    state.Gpr[3] = currentByte;
                    state.Gpr[0] = nextByte;
                    state.Gpr[26] = consumedLf ? unchecked(cursor + 2) : nextAddress;
                    SetCr0ForSignedCompareImmediate(state, state.Gpr[0], 10);
                    state.Pc = SonicLineSkipContinuePc;
                    AdvanceFastForwardedInstructions(state, bus, skipped);
                    skippedInstructions = checked((int)skipped);
                    return true;
                }

                cursor = unchecked(cursor + 1);
                ordinaryBytes++;
            }

            return false;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicLineSkip(GameCubeBus bus) =>
        bus.Read32(SonicLineSkipLoopPc + 0x00) == 0x887A_0000
        && bus.Read32(SonicLineSkipLoopPc + 0x04) == 0x7C60_0775
        && bus.Read32(SonicLineSkipLoopPc + 0x08) == 0x4182_0044
        && bus.Read32(SonicLineSkipLoopPc + 0x0C) == 0x7C60_0774
        && bus.Read32(SonicLineSkipLoopPc + 0x10) == 0x2C00_000A
        && bus.Read32(SonicLineSkipLoopPc + 0x14) == 0x4082_000C
        && bus.Read32(SonicLineSkipLoopPc + 0x18) == 0x3B5A_0001
        && bus.Read32(SonicLineSkipLoopPc + 0x1C) == 0x4800_0028
        && bus.Read32(SonicLineSkipLoopPc + 0x20) == 0x2C00_000D
        && bus.Read32(SonicLineSkipLoopPc + 0x24) == 0x4082_0018
        && bus.Read32(SonicLineSkipLoopPc + 0x28) == 0x8C1A_0001
        && bus.Read32(SonicLineSkipLoopPc + 0x2C) == 0x2C00_000A
        && bus.Read32(SonicLineSkipLoopPc + 0x30) == 0x4082_0014
        && bus.Read32(SonicLineSkipLoopPc + 0x34) == 0x3B5A_0001
        && bus.Read32(SonicLineSkipLoopPc + 0x38) == 0x4800_000C
        && bus.Read32(SonicLineSkipLoopPc + 0x3C) == 0x3B5A_0001
        && bus.Read32(SonicLineSkipLoopPc + 0x40) == 0x4BFF_FFC0
        && bus.Read32(SonicLineSkipContinuePc + 0x00) == 0x2C18_0000
        && bus.Read32(SonicLineSkipNulPc + 0x00) == 0x7F63_DB78;

    private static bool TryFastForwardSonicStringAppendScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicStringAppendScanLoopPc || !MatchesSonicStringAppendScan(bus))
        {
            return false;
        }

        try
        {
            uint cursor = state.Gpr[5];
            uint bytes = 0;
            while (bytes <= MaxFastForwardStringCopyBytes)
            {
                cursor = unchecked(cursor + 1);
                if (!bus.Memory.IsMainRamAddress(cursor, sizeof(byte)))
                {
                    return false;
                }

                byte value = bus.Memory.Read8(cursor);
                bytes++;
                if (value != 0)
                {
                    continue;
                }

                uint skipped = checked(bytes * SonicStringAppendScanInstructionsPerByte);
                if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
                {
                    return false;
                }

                state.Gpr[5] = cursor;
                state.Gpr[0] = value;
                SetCr0ForUnsignedCompareImmediate(state, value, 0);
                state.Pc = SonicStringAppendScanTailPc;
                AdvanceFastForwardedInstructions(state, bus, skipped);
                skippedInstructions = checked((int)skipped);
                return true;
            }

            return false;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicStringAppendScan(GameCubeBus bus) =>
        bus.Read32(SonicStringAppendScanLoopPc - 0x08) == 0x3884_FFFF
        && bus.Read32(SonicStringAppendScanLoopPc - 0x04) == 0x38A3_FFFF
        && bus.Read32(SonicStringAppendScanLoopPc + 0x00) == 0x8C05_0001
        && bus.Read32(SonicStringAppendScanLoopPc + 0x04) == 0x2800_0000
        && bus.Read32(SonicStringAppendScanLoopPc + 0x08) == 0x4082_FFF8
        && bus.Read32(SonicStringAppendScanTailPc + 0x00) == 0x38A5_FFFF
        && bus.Read32(SonicStringAppendScanTailPc + 0x04) == 0x8C04_0001
        && bus.Read32(SonicStringAppendScanTailPc + 0x08) == 0x2800_0000
        && bus.Read32(SonicStringAppendScanTailPc + 0x0C) == 0x9C05_0001
        && bus.Read32(SonicStringAppendScanTailPc + 0x10) == 0x4082_FFF4
        && bus.Read32(SonicStringAppendScanTailPc + 0x14) == 0x4E80_0020;

    private static bool TryFastForwardSonicFreeBlockScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicFreeBlockScanLoopPc || !MatchesSonicFreeBlockScan(bus))
        {
            return false;
        }

        try
        {
            uint current = state.Gpr[29];
            uint next = state.Gpr[4];
            uint requestedSize = state.Gpr[5];
            uint skipped = 0;

            for (uint visited = 0; visited <= 65_536; visited++)
            {
                if (!bus.Memory.IsMainRamAddress(current, sizeof(ushort)))
                {
                    return false;
                }

                ushort magic = bus.Memory.Read16(current);
                state.Gpr[0] = magic;
                skipped = checked(skipped + 3);
                if (magic == SonicFreeBlockMagic)
                {
                    uint availableSize = unchecked(next - current - 32);
                    state.Gpr[3] = availableSize;
                    skipped = checked(skipped + 4);
                    if (availableSize >= requestedSize)
                    {
                        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
                        {
                            return false;
                        }

                        state.Gpr[29] = current;
                        state.Gpr[4] = next;
                        SetCr0ForUnsignedCompareImmediate(state, availableSize, requestedSize);
                        state.Pc = SonicFreeBlockScanFoundPc;
                        AdvanceFastForwardedInstructions(state, bus, skipped);
                        skippedInstructions = checked((int)skipped);
                        return true;
                    }
                }

                current = next;
                if (!bus.Memory.IsMainRamAddress(current + 4, sizeof(uint)))
                {
                    return false;
                }

                next = bus.Memory.Read32(current + 4);
                skipped = checked(skipped + 4);
                SetCr0ForUnsignedCompareImmediate(state, next, 0);
                if (next == 0)
                {
                    if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
                    {
                        return false;
                    }

                    state.Gpr[29] = current;
                    state.Gpr[4] = 0;
                    state.Pc = SonicFreeBlockScanExhaustedPc;
                    AdvanceFastForwardedInstructions(state, bus, skipped);
                    skippedInstructions = checked((int)skipped);
                    return true;
                }
            }

            return false;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicFreeBlockScan(GameCubeBus bus) =>
        bus.Read32(SonicFreeBlockScanLoopPc + 0x00) == 0xA01D_0000
        && bus.Read32(SonicFreeBlockScanLoopPc + 0x04) == 0x2800_4D46
        && bus.Read32(SonicFreeBlockScanLoopPc + 0x08) == 0x4082_0014
        && bus.Read32(SonicFreeBlockScanLoopPc + 0x0C) == 0x7C7D_2050
        && bus.Read32(SonicFreeBlockScanLoopPc + 0x10) == 0x3863_FFE0
        && bus.Read32(SonicFreeBlockScanLoopPc + 0x14) == 0x7C03_2840
        && bus.Read32(SonicFreeBlockScanLoopPc + 0x18) == 0x4080_008C
        && bus.Read32(SonicFreeBlockScanLoopPc + 0x1C) == 0x7C9D_2378
        && bus.Read32(SonicFreeBlockScanLoopPc + 0x20) == 0x809D_0004
        && bus.Read32(SonicFreeBlockScanLoopPc + 0x24) == 0x2804_0000
        && bus.Read32(SonicFreeBlockScanLoopPc + 0x28) == 0x4082_FFD8
        && bus.Read32(SonicFreeBlockScanExhaustedPc + 0x00) == 0x3BA0_0000
        && bus.Read32(SonicFreeBlockScanFoundPc + 0x00) == 0x3805_0020;

    private static bool TryFastForwardSonicCacheStoreSweep(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicCacheStoreSweepLoopPc
            || state.Ctr == 0
            || !MatchesSonicCacheStoreSweep(bus)
            || !CanFastForwardInstructionCount(state, state.Ctr, SonicCacheStoreSweepInstructionsPerIteration, extraInstructions: 0))
        {
            return false;
        }

        uint iterations = state.Ctr;
        state.Gpr[3] = unchecked(state.Gpr[3] + iterations * SonicCacheStoreSweepBytesPerIteration);
        state.Ctr = 0;
        state.Pc = SonicCacheStoreSweepTailPc;
        uint skipped = checked(iterations * SonicCacheStoreSweepInstructionsPerIteration);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicCacheStoreSweep(GameCubeBus bus) =>
        bus.Read32(SonicCacheStoreSweepLoopPc - 0x18) == 0x7CA0_00A6
        && bus.Read32(SonicCacheStoreSweepLoopPc - 0x14) == 0x60A5_1000
        && bus.Read32(SonicCacheStoreSweepLoopPc - 0x10) == 0x7CA0_0124
        && bus.Read32(SonicCacheStoreSweepLoopPc - 0x0C) == 0x3C60_8000
        && bus.Read32(SonicCacheStoreSweepLoopPc - 0x08) == 0x3880_0400
        && bus.Read32(SonicCacheStoreSweepLoopPc - 0x04) == 0x7C89_03A6
        && bus.Read32(SonicCacheStoreSweepLoopPc + 0x00) == 0x7C00_1A2C
        && bus.Read32(SonicCacheStoreSweepLoopPc + 0x04) == 0x7C00_186C
        && bus.Read32(SonicCacheStoreSweepLoopPc + 0x08) == 0x3863_0020
        && bus.Read32(SonicCacheStoreSweepLoopPc + 0x0C) == 0x4200_FFF4
        && bus.Read32(SonicCacheStoreSweepTailPc + 0x00) == 0x7C98_E2A6;

    private static bool TryFastForwardSonicStateZeroFill(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicStateZeroFillLoopPc
            || state.Ctr == 0
            || state.Gpr[0] != 0
            || !MatchesSonicStateZeroFill(bus))
        {
            return false;
        }

        uint iterations = state.Ctr;
        uint byteCount = checked(iterations * SonicStateZeroFillBytesPerIteration);
        if (byteCount > int.MaxValue
            || !bus.Memory.IsMainRamAddress(state.Gpr[3], checked((int)byteCount))
            || !CanFastForwardInstructionCount(state, iterations, SonicStateZeroFillInstructionsPerIteration, extraInstructions: 0))
        {
            return false;
        }

        bus.Memory.Clear(state.Gpr[3], byteCount);
        state.Gpr[3] = unchecked(state.Gpr[3] + byteCount);
        state.Ctr = 0;
        state.Pc = SonicStateZeroFillTailPc;
        uint skipped = checked(iterations * SonicStateZeroFillInstructionsPerIteration);
        AdvanceFastForwardedInstructions(state, bus, skipped);
        skippedInstructions = checked((int)skipped);
        return true;
    }

    private static bool MatchesSonicStateZeroFill(GameCubeBus bus) =>
        bus.Read32(SonicStateZeroFillLoopPc - 0x14) == 0x3800_0380
        && bus.Read32(SonicStateZeroFillLoopPc - 0x10) == 0x3C7F_0001
        && bus.Read32(SonicStateZeroFillLoopPc - 0x0C) == 0x7C09_03A6
        && bus.Read32(SonicStateZeroFillLoopPc - 0x08) == 0x3800_0000
        && bus.Read32(SonicStateZeroFillLoopPc - 0x04) == 0x3863_8000
        && bus.Read32(SonicStateZeroFillLoopPc + 0x00) == 0x9003_0000
        && bus.Read32(SonicStateZeroFillLoopPc + 0x04) == 0x9003_0004
        && bus.Read32(SonicStateZeroFillLoopPc + 0x08) == 0x9003_0008
        && bus.Read32(SonicStateZeroFillLoopPc + 0x0C) == 0x9003_000C
        && bus.Read32(SonicStateZeroFillLoopPc + 0x10) == 0x9003_0010
        && bus.Read32(SonicStateZeroFillLoopPc + 0x14) == 0x9003_0014
        && bus.Read32(SonicStateZeroFillLoopPc + 0x18) == 0x9003_0018
        && bus.Read32(SonicStateZeroFillLoopPc + 0x1C) == 0x9003_001C
        && bus.Read32(SonicStateZeroFillLoopPc + 0x20) == 0x9003_0020
        && bus.Read32(SonicStateZeroFillLoopPc + 0x24) == 0x3863_0024
        && bus.Read32(SonicStateZeroFillLoopPc + 0x28) == 0x4200_FFD8
        && bus.Read32(SonicStateZeroFillTailPc + 0x00) == 0x3F7F_0001;

    private static bool TryFastForwardSonicManagerSlotScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicManagerSlotScanLoopPc
            || state.Gpr[30] >= SonicManagerSlotCount
            || !MatchesSonicManagerSlotScan(bus))
        {
            return false;
        }

        try
        {
            uint slot = state.Gpr[30];
            uint cursor = state.Gpr[31];
            uint skipped = 0;
            while (slot < SonicManagerSlotCount)
            {
                if (!bus.Memory.IsMainRamAddress(cursor, sizeof(byte)))
                {
                    return false;
                }

                uint value = bus.Memory.Read8(cursor);
                state.Gpr[0] = value;
                if ((sbyte)(byte)value == 1)
                {
                    skipped = checked(skipped + SonicManagerSlotActivePrefixInstructions);
                    if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
                    {
                        return false;
                    }

                    state.Gpr[30] = slot;
                    state.Gpr[31] = cursor;
                    SetCr0ForSignedCompareImmediate(state, value, 1);
                    state.Pc = SonicManagerSlotScanCallbackPc;
                    AdvanceFastForwardedInstructions(state, bus, skipped);
                    skippedInstructions = checked((int)skipped);
                    return true;
                }

                slot++;
                cursor = unchecked(cursor + SonicManagerSlotStride);
                skipped = checked(skipped + SonicManagerSlotInactiveInstructions);
            }

            if (skipped == 0 || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            state.Gpr[30] = SonicManagerSlotCount;
            state.Gpr[31] = cursor;
            SetCr0ForSignedCompareImmediate(state, SonicManagerSlotCount, (int)SonicManagerSlotCount);
            state.Pc = SonicManagerSlotScanExitPc;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicManagerSlotScan(GameCubeBus bus) =>
        bus.Read32(SonicManagerSlotScanLoopPc - 0x0C) == 0x3C60_8036
        && bus.Read32(SonicManagerSlotScanLoopPc - 0x08) == 0x3BE3_EB20
        && bus.Read32(SonicManagerSlotScanLoopPc - 0x04) == 0x3BC0_0000
        && bus.Read32(SonicManagerSlotScanLoopPc + 0x00) == 0x881F_0000
        && bus.Read32(SonicManagerSlotScanLoopPc + 0x04) == 0x2C00_0001
        && bus.Read32(SonicManagerSlotScanLoopPc + 0x08) == 0x4082_000C
        && bus.Read32(SonicManagerSlotScanLoopPc + 0x0C) == 0x7FE3_FB78
        && bus.Read32(SonicManagerSlotScanLoopPc + 0x10) == 0x4800_0509
        && bus.Read32(SonicManagerSlotScanLoopPc + 0x14) == 0x3BDE_0001
        && bus.Read32(SonicManagerSlotScanLoopPc + 0x18) == 0x2C1E_0010
        && bus.Read32(SonicManagerSlotScanLoopPc + 0x1C) == 0x3BFF_0438
        && bus.Read32(SonicManagerSlotScanLoopPc + 0x20) == 0x4180_FFE0
        && bus.Read32(SonicManagerSlotScanExitPc + 0x00) == 0x3861_0008;

    private static bool TryFastForwardSonicTaskEntryScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicTaskEntryScanLoopPc
            || state.Gpr[30] >= SonicTaskEntryCount
            || !MatchesSonicTaskEntryScan(bus))
        {
            return false;
        }

        try
        {
            uint entry = state.Gpr[30];
            uint cursor = state.Gpr[31];
            uint skipped = 0;
            uint lastR3 = state.Gpr[3];
            while (entry < SonicTaskEntryCount)
            {
                if (!bus.Memory.IsMainRamAddress(cursor, sizeof(byte)))
                {
                    return false;
                }

                uint value = bus.Memory.Read8(cursor);
                state.Gpr[0] = value;
                lastR3 = cursor;
                if ((sbyte)(byte)value == 1)
                {
                    skipped = checked(skipped + SonicTaskEntryActivePrefixInstructions);
                    if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
                    {
                        return false;
                    }

                    state.Gpr[3] = cursor;
                    state.Gpr[30] = entry;
                    state.Gpr[31] = cursor;
                    SetCr0ForSignedCompareImmediate(state, value, 1);
                    state.Pc = SonicTaskEntryScanActivePc;
                    AdvanceFastForwardedInstructions(state, bus, skipped);
                    skippedInstructions = checked((int)skipped);
                    return true;
                }

                entry++;
                cursor = unchecked(cursor + SonicTaskEntryStride);
                skipped = checked(skipped + SonicTaskEntryInactiveInstructions);
            }

            if (skipped == 0 || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            state.Gpr[3] = lastR3;
            state.Gpr[30] = SonicTaskEntryCount;
            state.Gpr[31] = cursor;
            SetCr0ForSignedCompareImmediate(state, SonicTaskEntryCount, (int)SonicTaskEntryCount);
            state.Pc = SonicTaskEntryScanExitPc;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicTaskEntryScan(GameCubeBus bus) =>
        bus.Read32(SonicTaskEntryScanLoopPc - 0x0C) == 0x3BE3_5230
        && bus.Read32(SonicTaskEntryScanLoopPc - 0x04) == 0x3BC0_0000
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x00) == 0x881F_0000
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x04) == 0x387F_0000
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x08) == 0x2C00_0001
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x0C) == 0x4082_0028
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x10) == 0x881F_0001
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x14) == 0x7C00_0774
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x18) == 0x2C00_0002
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x1C) == 0x4082_000C
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x30) == 0x4800_06A9
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x34) == 0x3BDE_0001
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x38) == 0x2C1E_0010
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x3C) == 0x3BFF_009C
        && bus.Read32(SonicTaskEntryScanLoopPc + 0x40) == 0x4180_FFC0
        && bus.Read32(SonicTaskEntryScanExitPc + 0x00) == 0x8001_0014;

    private static bool TryFastForwardSonicObjectSlotScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        if (state.Pc != SonicObjectSlotScanLoopPc
            || state.Gpr[30] >= SonicObjectSlotCount
            || !MatchesSonicObjectSlotScan(bus))
        {
            return false;
        }

        try
        {
            uint slot = state.Gpr[30];
            uint cursor = state.Gpr[31];
            uint skipped = 0;
            uint lastR3 = state.Gpr[3];
            while (slot < SonicObjectSlotCount)
            {
                if (!bus.Memory.IsMainRamAddress(cursor, sizeof(byte)))
                {
                    return false;
                }

                uint value = bus.Memory.Read8(cursor);
                state.Gpr[0] = value;
                lastR3 = cursor;
                if ((sbyte)(byte)value == 1)
                {
                    skipped = checked(skipped + SonicObjectSlotActivePrefixInstructions);
                    if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
                    {
                        return false;
                    }

                    state.Gpr[3] = cursor;
                    state.Gpr[30] = slot;
                    state.Gpr[31] = cursor;
                    SetCr0ForSignedCompareImmediate(state, value, 1);
                    state.Pc = SonicObjectSlotScanActivePc;
                    AdvanceFastForwardedInstructions(state, bus, skipped);
                    skippedInstructions = checked((int)skipped);
                    return true;
                }

                slot++;
                cursor = unchecked(cursor + SonicObjectSlotStride);
                skipped = checked(skipped + SonicObjectSlotInactiveInstructions);
            }

            if (skipped == 0 || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            state.Gpr[3] = lastR3;
            state.Gpr[30] = SonicObjectSlotCount;
            state.Gpr[31] = cursor;
            SetCr0ForSignedCompareImmediate(state, SonicObjectSlotCount, (int)SonicObjectSlotCount);
            state.Pc = SonicObjectSlotScanExitPc;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicObjectSlotScan(GameCubeBus bus) =>
        bus.Read32(SonicObjectSlotScanLoopPc - 0x10) == 0x3C60_8036
        && bus.Read32(SonicObjectSlotScanLoopPc - 0x08) == 0x3BE3_43B0
        && bus.Read32(SonicObjectSlotScanLoopPc - 0x04) == 0x3BC0_0000
        && bus.Read32(SonicObjectSlotScanLoopPc + 0x00) == 0x881F_0000
        && bus.Read32(SonicObjectSlotScanLoopPc + 0x04) == 0x387F_0000
        && bus.Read32(SonicObjectSlotScanLoopPc + 0x08) == 0x2C00_0001
        && bus.Read32(SonicObjectSlotScanLoopPc + 0x0C) == 0x4082_0008
        && bus.Read32(SonicObjectSlotScanLoopPc + 0x10) == 0x4800_0F69
        && bus.Read32(SonicObjectSlotScanLoopPc + 0x14) == 0x3BDE_0001
        && bus.Read32(SonicObjectSlotScanLoopPc + 0x18) == 0x2C1E_0010
        && bus.Read32(SonicObjectSlotScanLoopPc + 0x1C) == 0x3BFF_00A4
        && bus.Read32(SonicObjectSlotScanLoopPc + 0x20) == 0x4180_FFE0
        && bus.Read32(SonicObjectSlotScanExitPc + 0x00) == 0x4800_1EA9;

    private static bool TryFastForwardSonicHalfwordChecksumLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint sourceRegister;
        uint tailPc;
        if (state.Pc == SonicHalfwordChecksumLoopPc)
        {
            sourceRegister = 5;
            tailPc = SonicHalfwordChecksumTailPc;
        }
        else if (state.Pc == SonicHalfwordChecksumSecondLoopPc)
        {
            sourceRegister = 6;
            tailPc = SonicHalfwordChecksumSecondTailPc;
        }
        else
        {
            return false;
        }

        if (state.Ctr == 0 || !MatchesSonicHalfwordChecksumLoop(bus, state.Pc))
        {
            return false;
        }

        try
        {
            uint iterations = state.Ctr;
            uint byteCount = checked(iterations * SonicHalfwordChecksumBytesPerIteration);
            if (byteCount > MaxFastForwardMemmoveBytes
                || !bus.Memory.IsMainRamAddress(state.Gpr[sourceRegister], checked((int)byteCount))
                || !CanFastForwardInstructionCount(state, iterations, SonicHalfwordChecksumInstructionsPerIteration, extraInstructions: 0))
            {
                return false;
            }

            uint cursor = state.Gpr[sourceRegister];
            uint sum = state.Gpr[10];
            uint inverseSum = state.Gpr[11];
            uint value = state.Gpr[9];
            uint inverse = state.Gpr[0];
            for (uint iteration = 0; iteration < iterations; iteration++)
            {
                for (uint offset = 0; offset < SonicHalfwordChecksumBytesPerIteration; offset += sizeof(ushort))
                {
                    value = bus.Memory.Read16(cursor + offset);
                    inverse = ~value;
                    sum = unchecked(sum + value);
                    inverseSum = unchecked(inverseSum + inverse);
                }

                cursor = unchecked(cursor + SonicHalfwordChecksumBytesPerIteration);
            }

            state.Gpr[0] = inverse;
            state.Gpr[sourceRegister] = cursor;
            state.Gpr[9] = value;
            state.Gpr[10] = sum;
            state.Gpr[11] = inverseSum;
            state.Ctr = 0;
            state.Pc = tailPc;
            uint skipped = checked(iterations * SonicHalfwordChecksumInstructionsPerIteration);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool MatchesSonicHalfwordChecksumLoop(GameCubeBus bus, uint pc)
    {
        uint load0;
        uint load2;
        uint increment;
        uint tailPc;
        uint tailInstruction;
        if (pc == SonicHalfwordChecksumLoopPc)
        {
            load0 = 0xA125_0000;
            load2 = 0xA125_0002;
            increment = 0x38A5_0010;
            tailPc = SonicHalfwordChecksumTailPc;
            tailInstruction = 0x70C6_0007;
        }
        else if (pc == SonicHalfwordChecksumSecondLoopPc)
        {
            load0 = 0xA126_0000;
            load2 = 0xA126_0002;
            increment = 0x38C6_0010;
            tailPc = SonicHalfwordChecksumSecondTailPc;
            tailInstruction = 0x7108_0007;
        }
        else
        {
            return false;
        }

        return bus.Read32(pc + 0x00) == load0
            && bus.Read32(pc + 0x04) == 0x7D20_48F8
            && bus.Read32(pc + 0x08) == 0x7D4A_4A14
            && bus.Read32(pc + 0x0C) == load2
            && bus.Read32(pc + 0x10) == 0x7D6B_0214
            && bus.Read32(pc + 0x80) == increment
            && bus.Read32(pc + 0x84) == 0x4200_FF7C
            && bus.Read32(tailPc + 0x00) == tailInstruction;
    }

    private static bool TryFastForwardSonicNullSlotScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            if (state.Pc != SonicNullSlotScanLoopPc
                || !MatchesSonicNullSlotScanLoop(bus)
                || state.Ctr == 0)
            {
                return false;
            }

            uint cursor = state.Gpr[4];
            uint slotsToSkip = 0;
            uint skipped = 0;
            uint lastR0 = state.Gpr[0];
            uint lastR5 = state.Gpr[5];
            bool lastCompareWasNull = false;
            uint lastComparedObjectId = 0;
            while (slotsToSkip < state.Ctr)
            {
                uint pointerAddress = cursor + 0x0C;
                if (!bus.Memory.IsMainRamAddress(pointerAddress, sizeof(uint)))
                {
                    return false;
                }

                uint slotPointer = bus.Memory.Read32(pointerAddress);
                if (slotPointer == 0)
                {
                    lastR5 = 0;
                    lastCompareWasNull = true;
                    skipped = checked(skipped + SonicNullSlotScanInstructionsPerNullSlot);
                }
                else
                {
                    if (!bus.Memory.IsMainRamAddress(slotPointer, sizeof(uint)))
                    {
                        return false;
                    }

                    uint objectId = bus.Memory.Read32(slotPointer);
                    if (objectId == state.Gpr[3])
                    {
                        break;
                    }

                    lastR5 = slotPointer;
                    lastR0 = objectId;
                    lastCompareWasNull = false;
                    lastComparedObjectId = objectId;
                    skipped = checked(skipped + SonicNullSlotScanInstructionsPerMismatchSlot);
                }

                slotsToSkip++;
                cursor = unchecked(cursor + SonicNullSlotScanSlotStride);
            }

            if (slotsToSkip == 0)
            {
                return false;
            }

            bool exhausted = slotsToSkip == state.Ctr;
            if (exhausted)
            {
                skipped = checked(skipped + SonicNullSlotScanExitInstructions);
            }

            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            state.Gpr[4] = cursor;
            state.Gpr[0] = lastR0;
            state.Gpr[5] = lastR5;
            state.Gpr[6] = unchecked(state.Gpr[6] + slotsToSkip);
            state.Ctr -= slotsToSkip;
            if (lastCompareWasNull)
            {
                SetCr0ForUnsignedCompareImmediate(state, 0, 0);
            }
            else
            {
                SetCr0ForSignedCompare(state, state.Gpr[3], lastComparedObjectId);
            }

            if (exhausted)
            {
                state.Gpr[3] = 0xFFFF_FFFF;
                state.Pc = state.Lr & 0xFFFF_FFFCu;
            }
            else
            {
                state.Pc = SonicNullSlotScanLoopPc;
            }

            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool MatchesSonicNullSlotScanLoop(GameCubeBus bus) =>
        bus.Read32(SonicNullSlotScanLoopPc + 0x00) == 0x80A4_000C
        && bus.Read32(SonicNullSlotScanLoopPc + 0x04) == 0x2805_0000
        && bus.Read32(SonicNullSlotScanLoopPc + 0x08) == 0x4182_0018
        && bus.Read32(SonicNullSlotScanLoopPc + 0x20) == 0x3884_0018
        && bus.Read32(SonicNullSlotScanLoopPc + 0x24) == 0x38C6_0001
        && bus.Read32(SonicNullSlotScanLoopPc + 0x28) == 0x4200_FFD8
        && bus.Read32(SonicNullSlotScanExitPc + 0x00) == 0x3860_FFFF
        && bus.Read32(SonicNullSlotScanExitPc + 0x04) == 0x4E80_0020;

    private static bool TryFastForwardSonicPoolSlotScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        if (state.Pc == SonicPoolNullSlotScanLoopPc)
        {
            return TryFastForwardSonicPoolNullSlotScanLoop(state, bus, out skippedInstructions);
        }

        if (state.Pc == SonicPoolSentinelSlotScanLoopPc)
        {
            return TryFastForwardSonicPoolSentinelSlotScanLoop(state, bus, out skippedInstructions);
        }

        skippedInstructions = 0;
        return false;
    }

    private static bool TryFastForwardSonicPoolNullSlotScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            if (!MatchesSonicPoolNullSlotScanLoop(bus) || state.Ctr == 0)
            {
                return false;
            }

            uint cursor = state.Gpr[6];
            uint slotsToSkip = 0;
            uint lastPointer = state.Gpr[0];
            while (slotsToSkip < state.Ctr)
            {
                uint pointerAddress = cursor + 0x0C;
                if (!bus.Memory.IsMainRamAddress(pointerAddress, sizeof(uint)))
                {
                    return false;
                }

                uint pointer = bus.Memory.Read32(pointerAddress);
                if (pointer == 0)
                {
                    break;
                }

                lastPointer = pointer;
                slotsToSkip++;
                cursor = unchecked(cursor + SonicNullSlotScanSlotStride);
            }

            if (slotsToSkip == 0)
            {
                return false;
            }

            uint skipped = checked(slotsToSkip * SonicPoolNullSlotScanInstructionsPerOccupiedSlot);
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            state.Gpr[0] = lastPointer;
            state.Gpr[5] = unchecked(state.Gpr[5] + slotsToSkip);
            state.Gpr[6] = cursor;
            state.Ctr -= slotsToSkip;
            SetCr0ForUnsignedCompareImmediate(state, lastPointer, 0);
            state.Pc = state.Ctr == 0 ? 0x8011_6C3C : SonicPoolNullSlotScanLoopPc;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool MatchesSonicPoolNullSlotScanLoop(GameCubeBus bus) =>
        bus.Read32(SonicPoolNullSlotScanLoopPc + 0x00) == 0x8006_000C
        && bus.Read32(SonicPoolNullSlotScanLoopPc + 0x04) == 0x2800_0000
        && bus.Read32(SonicPoolNullSlotScanLoopPc + 0x08) == 0x4082_0010
        && bus.Read32(SonicPoolNullSlotScanLoopPc + 0x18) == 0x38C6_0018
        && bus.Read32(SonicPoolNullSlotScanLoopPc + 0x1C) == 0x38A5_0001
        && bus.Read32(SonicPoolNullSlotScanLoopPc + 0x20) == 0x4200_FFE0
        && bus.Read32(0x8011_6C3C) == 0x8003_0000;

    private static bool TryFastForwardSonicPoolSentinelSlotScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            if (!MatchesSonicPoolSentinelSlotScanLoop(bus) || state.Ctr == 0)
            {
                return false;
            }

            uint cursor = state.Gpr[5];
            uint slotsToSkip = 0;
            uint lastValue = state.Gpr[3];
            uint lastCompareValue = state.Gpr[0];
            while (slotsToSkip < state.Ctr)
            {
                if (!bus.Memory.IsMainRamAddress(cursor, sizeof(uint)))
                {
                    return false;
                }

                uint value = bus.Memory.Read32(cursor);
                uint compareValue = unchecked(value + 0x0001_0000u);
                if (compareValue == 0x0000_FFFFu)
                {
                    break;
                }

                lastValue = value;
                lastCompareValue = compareValue;
                slotsToSkip++;
                cursor = unchecked(cursor + 0x28);
            }

            if (slotsToSkip == 0)
            {
                return false;
            }

            uint skipped = checked(slotsToSkip * SonicPoolSentinelSlotScanInstructionsPerNonSentinelSlot);
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            state.Gpr[0] = lastCompareValue;
            state.Gpr[3] = lastValue;
            state.Gpr[5] = cursor;
            state.Ctr -= slotsToSkip;
            SetCr0ForUnsignedCompareImmediate(state, lastCompareValue, 0xFFFF);
            state.Pc = state.Ctr == 0 ? 0x8011_6CAC : SonicPoolSentinelSlotScanLoopPc;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool MatchesSonicPoolSentinelSlotScanLoop(GameCubeBus bus) =>
        bus.Read32(SonicPoolSentinelSlotScanLoopPc + 0x00) == 0x8065_0000
        && bus.Read32(SonicPoolSentinelSlotScanLoopPc + 0x04) == 0x3C03_0001
        && bus.Read32(SonicPoolSentinelSlotScanLoopPc + 0x08) == 0x2800_FFFF
        && bus.Read32(SonicPoolSentinelSlotScanLoopPc + 0x0C) == 0x4082_000C
        && bus.Read32(SonicPoolSentinelSlotScanLoopPc + 0x18) == 0x38A5_0028
        && bus.Read32(SonicPoolSentinelSlotScanLoopPc + 0x1C) == 0x4200_FFE4
        && bus.Read32(0x8011_6CAC) == 0x8004_0000;

    private static bool TryFastForwardSonicTableKeyScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            if (state.Pc != SonicTableKeyScanLoopPc || !MatchesSonicTableKeyScanLoop(bus) || state.Ctr == 0)
            {
                return false;
            }

            uint tablePointerAddress = state.Gpr[31];
            if (!bus.Memory.IsMainRamAddress(tablePointerAddress, sizeof(uint)))
            {
                return false;
            }

            uint tableBase = bus.Memory.Read32(tablePointerAddress);
            uint offset = state.Gpr[4];
            uint misses = 0;
            uint lastKey = state.Gpr[0];
            while (misses < state.Ctr)
            {
                uint entryKeyAddress = unchecked(tableBase + offset + 4);
                if (!bus.Memory.IsMainRamAddress(entryKeyAddress, sizeof(uint)))
                {
                    return false;
                }

                uint rawValue = bus.Memory.Read32(entryKeyAddress);
                uint key = rawValue & 0xFFFFu;
                if (key == state.Gpr[29])
                {
                    break;
                }

                lastKey = key;
                misses++;
                offset = unchecked(offset + 12);
            }

            if (misses == 0)
            {
                return false;
            }

            uint skipped = checked(misses * SonicTableKeyScanInstructionsPerMiss);
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            state.Gpr[0] = lastKey;
            state.Gpr[3] = tableBase;
            state.Gpr[4] = offset;
            state.Gpr[28] = unchecked(state.Gpr[28] + misses);
            state.Ctr -= misses;
            SetCr0ForUnsignedCompareImmediate(state, state.Gpr[29], lastKey);
            state.Pc = state.Ctr == 0 ? 0x8011_90DC : SonicTableKeyScanLoopPc;
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool MatchesSonicTableKeyScanLoop(GameCubeBus bus) =>
        bus.Read32(SonicTableKeyScanLoopPc + 0x00) == 0x807F_0000
        && bus.Read32(SonicTableKeyScanLoopPc + 0x04) == 0x3804_0004
        && bus.Read32(SonicTableKeyScanLoopPc + 0x08) == 0x7C03_002E
        && bus.Read32(SonicTableKeyScanLoopPc + 0x0C) == 0x5400_043E
        && bus.Read32(SonicTableKeyScanLoopPc + 0x10) == 0x7C1D_0040
        && bus.Read32(SonicTableKeyScanLoopPc + 0x14) == 0x4182_0010
        && bus.Read32(SonicTableKeyScanLoopPc + 0x18) == 0x3884_000C
        && bus.Read32(SonicTableKeyScanLoopPc + 0x1C) == 0x3B9C_0001
        && bus.Read32(SonicTableKeyScanLoopPc + 0x20) == 0x4200_FFE0
        && bus.Read32(0x8011_90DC) == 0x7FE3_FB78;

    private static bool TryFastForwardSonicModeRefreshDispatch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            if (!MatchesSonicModeRefreshDispatch(bus))
            {
                return false;
            }

            if (state.Pc == SonicModeRefreshCallbackCheckPc
                || state.Pc == SonicModeRefreshCallbackCheckPc + 4
                || state.Pc == SonicModeRefreshCallbackCheckPc + 8)
            {
                uint tailInstructions = state.Pc == SonicModeRefreshCallbackCheckPc
                    ? SonicModeRefreshCallbackNullCheckInstructions
                    : state.Pc == SonicModeRefreshCallbackCheckPc + 4
                        ? SonicModeRefreshCallbackCompareTailInstructions
                        : SonicModeRefreshCallbackBranchTailInstructions;
                if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: tailInstructions, extraInstructions: 0))
                {
                    return false;
                }

                uint callbackPointerAddress = unchecked(state.Gpr[13] + SonicModeRefreshCallbackPointerOffset);
                if (!bus.Memory.IsMainRamAddress(callbackPointerAddress, sizeof(uint)))
                {
                    return false;
                }

                uint callbackPointer = state.Pc == SonicModeRefreshCallbackCheckPc + 4
                    ? state.Gpr[0]
                    : bus.Memory.Read32(callbackPointerAddress);
                if (callbackPointer != 0)
                {
                    return false;
                }

                if (state.Pc == SonicModeRefreshCallbackCheckPc + 8 && (state.Cr & 0xF000_0000u) != 0x2000_0000u)
                {
                    return false;
                }

                state.Gpr[0] = 0;
                SetCr0ForUnsignedCompareImmediate(state, 0, 0);
                state.Pc = SonicModeRefreshModeClassifyPc;
                AdvanceFastForwardedInstructions(state, bus, tailInstructions);
                skippedInstructions = checked((int)tailInstructions);
                return true;
            }

            if (state.Pc == SonicModeRefreshCounterCheckPc
                || state.Pc == SonicModeRefreshCounterCheckPc + 4
                || state.Pc == SonicModeRefreshCounterCheckPc + 8)
            {
                uint tailInstructions = state.Pc == SonicModeRefreshCounterCheckPc
                    ? SonicModeRefreshCounterCheckInstructions
                    : state.Pc == SonicModeRefreshCounterCheckPc + 4
                        ? SonicModeRefreshCounterCompareTailInstructions
                        : SonicModeRefreshCounterBranchTailInstructions;
                if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: tailInstructions, extraInstructions: 0))
                {
                    return false;
                }

                uint counterValueAddress = unchecked(state.Gpr[13] + SonicModeRefreshCounterValueOffset);
                if (state.Pc == SonicModeRefreshCounterCheckPc && !bus.Memory.IsMainRamAddress(counterValueAddress, sizeof(uint)))
                {
                    return false;
                }

                uint counterValue = state.Pc == SonicModeRefreshCounterCheckPc
                    ? bus.Memory.Read32(counterValueAddress)
                    : state.Gpr[3];
                if (state.Pc == SonicModeRefreshCounterCheckPc + 8)
                {
                    if ((state.Cr & 0x8000_0000u) != 0)
                    {
                        return false;
                    }
                }
                else if (state.Gpr[27] < counterValue)
                {
                    return false;
                }

                state.Gpr[3] = counterValue;
                if (state.Pc == SonicModeRefreshCounterCheckPc)
                {
                    state.Lr = SonicModeRefreshCounterCheckPc + 4;
                }

                if (state.Pc != SonicModeRefreshCounterCheckPc + 8)
                {
                    SetCr0ForUnsignedCompare(state, state.Gpr[27], counterValue);
                }

                state.Pc = SonicModeRefreshModeClassifyPc;
                AdvanceFastForwardedInstructions(state, bus, tailInstructions);
                skippedInstructions = checked((int)tailInstructions);
                return true;
            }

            if (state.Pc == SonicModeRefreshCallPc)
            {
                if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicModeRefreshCallInstructions, extraInstructions: 0))
                {
                    return false;
                }

                uint objectPointerHolderAddress = unchecked(state.Gpr[13] + SonicModeRefreshObjectPointerOffset);
                if (!bus.Memory.IsMainRamAddress(objectPointerHolderAddress, sizeof(uint)))
                {
                    return false;
                }

                uint objectPointerHolder = bus.Memory.Read32(objectPointerHolderAddress);
                if (!bus.Memory.IsMainRamAddress(objectPointerHolder, sizeof(uint)))
                {
                    return false;
                }

                state.Gpr[3] = bus.Memory.Read32(objectPointerHolder);
                state.Lr = SonicModeRefreshPostCallPc;
                state.Pc = 0x800F_135C;
                AdvanceFastForwardedInstructions(state, bus, SonicModeRefreshCallInstructions);
                skippedInstructions = checked((int)SonicModeRefreshCallInstructions);
                return true;
            }

            if (state.Pc == SonicModeRefreshPostCallPc)
            {
                if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: SonicModeRefreshPostCallInstructions, extraInstructions: 0))
                {
                    return false;
                }

                uint mode = state.Gpr[3];
                state.Gpr[28] = mode;
                SetCr0ForSignedCompareImmediate(state, mode, 0);
                state.Pc = mode != 0 ? SonicModeRefreshLoopPc : SonicModeRefreshExitPc;
                AdvanceFastForwardedInstructions(state, bus, SonicModeRefreshPostCallInstructions);
                skippedInstructions = checked((int)SonicModeRefreshPostCallInstructions);
                return true;
            }

            return false;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicModeRefreshDispatch(GameCubeBus bus) =>
        bus.Read32(SonicModeRefreshLoopPc + 0x00) == 0x800D_8F04
        && bus.Read32(SonicModeRefreshLoopPc + 0x04) == 0x2800_0000
        && bus.Read32(SonicModeRefreshLoopPc + 0x08) == 0x4182_0024
        && bus.Read32(SonicModeRefreshCounterCheckPc + 0x00) == 0x4BFD_0325
        && bus.Read32(SonicModeRefreshCounterCheckPc + 0x04) == 0x7C1B_1840
        && bus.Read32(SonicModeRefreshCounterCheckPc + 0x08) == 0x4080_0018
        && bus.Read32(0x800F_3710) == 0x806D_8B00
        && bus.Read32(0x800F_3714) == 0x4E80_0020
        && bus.Read32(SonicModeRefreshModeClassifyPc + 0x00) == 0x381C_FFFC
        && bus.Read32(SonicModeRefreshModeClassifyPc + 0x04) == 0x2800_0002
        && bus.Read32(SonicModeRefreshModeClassifyPc + 0x08) == 0x4081_0014
        && bus.Read32(SonicModeRefreshCallPc + 0x00) == 0x806D_8F0C
        && bus.Read32(SonicModeRefreshCallPc + 0x04) == 0x8063_0000
        && bus.Read32(SonicModeRefreshCallPc + 0x08) == 0x4BFC_DEDD
        && bus.Read32(SonicModeRefreshPostCallPc + 0x00) == 0x7C7C_1B78
        && bus.Read32(SonicModeRefreshPostCallPc + 0x04) == 0x2C1C_0000
        && bus.Read32(SonicModeRefreshPostCallPc + 0x08) == 0x4082_FF54
        && bus.Read32(SonicModeRefreshExitPc + 0x00) == 0x2C1F_0000;

    private static bool TryFastForwardSonicModeQuery(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            uint stackPointer = state.Gpr[1];
            uint newStackPointer = unchecked(stackPointer - 24);
            if (state.Pc != SonicModeQueryPc
                || !MatchesSonicModeQuery(bus)
                || stackPointer > uint.MaxValue - sizeof(uint)
                || !bus.Memory.IsMainRamAddress(stackPointer + sizeof(uint), sizeof(uint))
                || !bus.Memory.IsMainRamAddress(newStackPointer, 24))
            {
                return false;
            }

            uint busyFlagAddress = unchecked(state.Gpr[13] + SonicModeQueryBusyFlagOffset);
            uint fallbackFlagAddress = unchecked(state.Gpr[13] + SonicModeQueryFallbackFlagOffset);
            uint pointerAddress = unchecked(state.Gpr[13] + SonicModeQueryPointerOffset);
            if (!bus.Memory.IsMainRamAddress(busyFlagAddress, sizeof(uint))
                || !bus.Memory.IsMainRamAddress(fallbackFlagAddress, sizeof(uint))
                || !bus.Memory.IsMainRamAddress(pointerAddress, sizeof(uint)))
            {
                return false;
            }

            uint oldLr = state.Lr;
            uint oldR30 = state.Gpr[30];
            uint oldR31 = state.Gpr[31];
            uint originalMsr = state.Msr;
            uint outerInterruptEnable = (originalMsr >> 15) & 1;
            uint disabledMsr = originalMsr & ~0x8000u;
            uint result;
            uint r3;
            static uint RestoreExternalInterruptCallInstructions(uint interruptEnable) => interruptEnable == 0 ? 8u : 9u;

            uint skipped = 15;
            uint tailInstructions = 1 + RestoreExternalInterruptCallInstructions(outerInterruptEnable) + 7;
            uint busyFlag = bus.Memory.Read32(busyFlagAddress);
            if (busyFlag != 0)
            {
                result = 0xFFFF_FFFF;
                skipped += 2;
            }
            else
            {
                uint fallbackFlag = bus.Memory.Read32(fallbackFlagAddress);
                skipped += 3;
                if (fallbackFlag != 0)
                {
                    result = 8;
                    skipped += 2;
                }
                else
                {
                    uint pointer = bus.Memory.Read32(pointerAddress);
                    skipped += 3;
                    if (pointer == 0)
                    {
                        result = 0;
                        skipped += 2;
                    }
                    else if (pointer == SonicModeQuerySentinelPointer)
                    {
                        result = 0;
                        skipped += 6;
                    }
                    else
                    {
                        if (!bus.Memory.IsMainRamAddress(pointer + 0x0C, sizeof(uint)))
                        {
                            return false;
                        }

                        skipped += 10;
                        result = bus.Memory.Read32(pointer + 0x0C);
                        skipped += 3;
                        if (result == 3)
                        {
                            result = 1;
                            skipped += 1;
                        }

                        skipped += RestoreExternalInterruptCallInstructions(0);
                    }
                }
            }

            skipped += tailInstructions;
            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            bus.Write32(stackPointer + sizeof(uint), oldLr);
            bus.Write32(newStackPointer, stackPointer);
            bus.Write32(newStackPointer + 16, oldR30);
            bus.Write32(newStackPointer + 20, oldR31);

            r3 = result;
            uint r4 = 0;
            uint r5 = outerInterruptEnable == 0 ? disabledMsr & ~0x8000u : disabledMsr | 0x8000u;
            state.Msr = r5;
            state.Gpr[0] = oldLr;
            state.Gpr[1] = stackPointer;
            state.Gpr[3] = r3;
            state.Gpr[4] = r4;
            state.Gpr[5] = r5;
            state.Gpr[30] = oldR30;
            state.Gpr[31] = oldR31;
            state.Lr = oldLr;
            state.Pc = oldLr & 0xFFFF_FFFCu;
            SetCr0ForSignedCompareImmediate(state, outerInterruptEnable, 0);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicModeQuery(GameCubeBus bus) =>
        bus.Read32(SonicModeQueryPc + 0x00) == 0x7C08_02A6
        && bus.Read32(SonicModeQueryPc + 0x04) == 0x9001_0004
        && bus.Read32(SonicModeQueryPc + 0x08) == 0x9421_FFE8
        && bus.Read32(SonicModeQueryPc + 0x0C) == 0x93E1_0014
        && bus.Read32(SonicModeQueryPc + 0x10) == 0x93C1_0010
        && bus.Read32(SonicModeQueryPc + 0x14) == 0x4BFF_64F1
        && bus.Read32(SonicModeQueryPc + 0x18) == 0x800D_8AC0
        && bus.Read32(SonicModeQueryPc + 0x70) == 0x4BFF_6495
        && bus.Read32(SonicModeQueryPc + 0x84) == 0x4BFF_64A9
        && bus.Read32(SonicModeQueryPc + 0x8C) == 0x4BFF_64A1
        && bus.Read32(SonicModeQueryPc + 0xA0) == 0x7C08_03A6
        && bus.Read32(SonicModeQueryPc + 0xA8) == 0x4E80_0020
        && bus.Read32(SonicDisableExternalInterruptPc + 0x00) == 0x7C60_00A6
        && bus.Read32(SonicDisableExternalInterruptPc + 0x04) == 0x5464_045E
        && bus.Read32(SonicDisableExternalInterruptPc + 0x10) == 0x4E80_0020
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x00) == 0x2C03_0000
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x18) == 0x7CA0_0124
        && bus.Read32(SonicRestoreExternalInterruptPc + 0x20) == 0x4E80_0020;

    private static bool TryFastForwardSonicModeChildStatusPoll(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            uint stackPointer = state.Gpr[1];
            uint newStackPointer = unchecked(stackPointer - 16);
            if (state.Pc != SonicModeChildStatusPollPc
                || !MatchesSonicModeChildStatusPoll(bus)
                || stackPointer > uint.MaxValue - sizeof(uint)
                || !bus.Memory.IsMainRamAddress(stackPointer + sizeof(uint), sizeof(uint))
                || !bus.Memory.IsMainRamAddress(newStackPointer, 16))
            {
                return false;
            }

            uint statusPointerAddress = unchecked(state.Gpr[13] + SonicModeChildStatusPointerOffset);
            if (!bus.Memory.IsMainRamAddress(statusPointerAddress, sizeof(uint)))
            {
                return false;
            }

            uint root = bus.Memory.Read32(statusPointerAddress);
            uint skipped = 7;
            uint pendingR31 = state.Gpr[31];
            uint finalCompareLeft = 0;
            uint finalR3 = root;
            List<uint> childrenToClear = [];

            if (root == 0)
            {
                skipped += 6;
                finalR3 = 0;
            }
            else
            {
                if (!bus.Memory.IsMainRamAddress(root + 0x50, sizeof(uint)))
                {
                    return false;
                }

                pendingR31 = 0;
                uint[] childOffsets = [0x10, 0x34, 0x50];
                for (int slot = 0; slot < childOffsets.Length; slot++)
                {
                    uint childAddress = root + childOffsets[slot];
                    if (!bus.Memory.IsMainRamAddress(childAddress, sizeof(uint)))
                    {
                        return false;
                    }

                    uint child = bus.Memory.Read32(childAddress);
                    skipped += 4;
                    if (child == 0)
                    {
                        continue;
                    }

                    if (!bus.Memory.IsMainRamAddress(child + 0x60, 12))
                    {
                        return false;
                    }

                    uint status = unchecked((uint)(short)bus.Memory.Read16(child + 0x60));
                    skipped += 5;
                    if (status != 0)
                    {
                        pendingR31 = 1;
                        skipped += 1;
                    }

                    skipped += 2;
                    childrenToClear.Add(child);
                    skipped += 7;
                }

                skipped += 2;
                finalCompareLeft = pendingR31;
                if (pendingR31 == 0)
                {
                    skipped += 6;
                    finalR3 = 0;
                }
                else
                {
                    skipped += 7;
                    finalR3 = 1;
                }
            }

            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0))
            {
                return false;
            }

            uint oldLr = state.Lr;
            uint oldR31 = state.Gpr[31];
            bus.Write32(stackPointer + sizeof(uint), oldLr);
            bus.Write32(newStackPointer, stackPointer);
            bus.Write32(newStackPointer + 12, oldR31);
            foreach (uint child in childrenToClear)
            {
                bus.Write16(child + 0x60, 0);
                bus.Write32(child + 0x64, 0);
                bus.Write16(child + 0x68, 0);
                bus.Write16(child + 0x6A, 0);
            }

            state.Gpr[0] = oldLr;
            state.Gpr[1] = stackPointer;
            state.Gpr[3] = finalR3;
            state.Gpr[31] = oldR31;
            state.Pc = oldLr & 0xFFFF_FFFCu;
            SetCr0ForSignedCompareImmediate(state, finalCompareLeft, 0);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool MatchesSonicModeChildStatusPoll(GameCubeBus bus) =>
        bus.Read32(SonicModeChildStatusPollPc + 0x00) == 0x7C08_02A6
        && bus.Read32(SonicModeChildStatusPollPc + 0x04) == 0x9001_0004
        && bus.Read32(SonicModeChildStatusPollPc + 0x08) == 0x9421_FFF0
        && bus.Read32(SonicModeChildStatusPollPc + 0x0C) == 0x93E1_000C
        && bus.Read32(SonicModeChildStatusPollPc + 0x10) == 0x806D_8510
        && bus.Read32(SonicModeChildStatusPollPc + 0x18) == 0x4182_0098
        && bus.Read32(SonicModeChildStatusPollPc + 0x2C) == 0x4805_0C59
        && bus.Read32(SonicModeChildStatusPollPc + 0x44) == 0x4805_0C29
        && bus.Read32(SonicModeChildStatusPollPc + 0x58) == 0x4805_0C2D
        && bus.Read32(SonicModeChildStatusPollPc + 0x70) == 0x4805_0BFD
        && bus.Read32(SonicModeChildStatusPollPc + 0x84) == 0x4805_0C01
        && bus.Read32(SonicModeChildStatusPollPc + 0x9C) == 0x4805_0BD1
        && bus.Read32(SonicModeChildStatusPollPc + 0xB0) == 0x3860_0000
        && bus.Read32(SonicModeChildStatusPollPc + 0xC0) == 0x7C08_03A6
        && bus.Read32(SonicModeChildStatusPollPc + 0xC4) == 0x4E80_0020
        && bus.Read32(SonicModeChildStatusReadPc + 0x00) == 0xA863_0060
        && bus.Read32(SonicModeChildStatusReadPc + 0x04) == 0x4E80_0020
        && bus.Read32(SonicModeChildStatusClearPc + 0x00) == 0x3800_0000
        && bus.Read32(SonicModeChildStatusClearPc + 0x04) == 0xB003_0060
        && bus.Read32(SonicModeChildStatusClearPc + 0x08) == 0x9003_0064
        && bus.Read32(SonicModeChildStatusClearPc + 0x0C) == 0xB003_0068
        && bus.Read32(SonicModeChildStatusClearPc + 0x10) == 0xB003_006A
        && bus.Read32(SonicModeChildStatusClearPc + 0x14) == 0x4E80_0020;

    private static bool TryFastForwardSonicModeStateUpdate(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        try
        {
            if (state.Pc != SonicModeStateUpdatePc || !MatchesSonicModeStateUpdate(bus))
            {
                return false;
            }

            uint originalR3 = state.Gpr[3];
            uint originalCtr = state.Ctr;
            byte originalModeByte = bus.Memory.Read8(SonicModeStateByteAddress);
            int originalMode = unchecked((sbyte)originalModeByte);
            uint r0 = 0;
            uint r3 = originalR3;
            uint r4 = unchecked((uint)originalMode);
            uint r5 = originalModeByte;
            uint r6 = SonicModeStateByteAddress;
            uint ctr = originalCtr;
            uint skipped = 6;
            bool writeSmallDataMode = false;
            uint smallDataModeAddress = unchecked(state.Gpr[13] + 0xFFFF_897C);
            byte modeByte = originalModeByte;
            bool writeModeByte = false;
            bool writeOutputByte = false;
            byte outputByte = 0;

            if (originalMode == 20)
            {
                skipped += 1;
            }
            else
            {
                skipped += 5;
                byte modeMinus21 = unchecked((byte)(originalModeByte - 21));
                if (modeMinus21 > 2)
                {
                    writeSmallDataMode = true;
                    skipped += 1;
                }
            }

            r0 = unchecked(originalR3 + 1);
            skipped += 3;
            if (r0 <= 12)
            {
                uint jumpTarget = SonicModeStateJumpTarget(r0);
                if (jumpTarget == 0)
                {
                    return false;
                }

                r3 = SonicModeStateJumpTableAddress;
                ctr = jumpTarget;
                skipped += 6;
                switch (jumpTarget)
                {
                    case 0x800E_2E74:
                        r0 = 20;
                        modeByte = 20;
                        writeModeByte = true;
                        skipped += 3;
                        break;
                    case 0x800E_2E80:
                    case 0x800E_2E8C:
                        r0 = 21;
                        modeByte = 21;
                        writeModeByte = true;
                        skipped += 3;
                        break;
                    case 0x800E_2E98:
                        r0 = 22;
                        modeByte = 22;
                        writeModeByte = true;
                        skipped += 3;
                        break;
                    case 0x800E_2EA4:
                        r0 = 23;
                        modeByte = 23;
                        writeModeByte = true;
                        skipped += 2;
                        break;
                    case 0x800E_2EAC:
                        r0 = jumpTarget;
                        break;
                    default:
                        return false;
                }
            }

            int mode = unchecked((sbyte)modeByte);
            uint compareLeft = unchecked((uint)mode);
            int compareRight;
            r0 = compareLeft;
            skipped += 3;
            skipped += 1;
            if (mode < 20)
            {
                compareRight = 20;
            }
            else
            {
                skipped += 1;
                skipped += 1;
                if (mode == 21)
                {
                    compareRight = 21;
                    r3 = SonicModeStateBase;
                    r0 = unchecked((uint)-29);
                    outputByte = (byte)r0;
                    writeOutputByte = true;
                    skipped += 5;
                }
                else
                {
                    skipped += 1;
                    if (mode >= 21)
                    {
                        skipped += 1;
                        skipped += 1;
                        compareRight = 24;
                        if (mode >= 24)
                        {
                        }
                        else
                        {
                            skipped += 1;
                            r3 = SonicModeStateBase;
                            r0 = unchecked((uint)-20);
                            outputByte = (byte)r0;
                            writeOutputByte = true;
                            skipped += 5;
                        }
                    }
                    else
                    {
                        skipped += 1;
                        skipped += 1;
                        compareRight = 20;
                        r3 = SonicModeStateBase;
                        r0 = unchecked((uint)-23);
                        outputByte = (byte)r0;
                        writeOutputByte = true;
                        skipped += 5;
                    }
                }
            }

            if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: skipped, extraInstructions: 0)
                || (writeSmallDataMode && !bus.Memory.IsMainRamAddress(smallDataModeAddress, sizeof(uint)))
                || (writeModeByte && !bus.Memory.IsMainRamAddress(SonicModeStateByteAddress, sizeof(byte)))
                || (writeOutputByte && !bus.Memory.IsMainRamAddress(SonicModeStateOutputByteAddress, sizeof(byte))))
            {
                return false;
            }

            if (writeSmallDataMode)
            {
                bus.Write32(smallDataModeAddress, r4);
            }

            if (writeModeByte)
            {
                bus.Write8(SonicModeStateByteAddress, modeByte);
            }

            if (writeOutputByte)
            {
                bus.Write8(SonicModeStateOutputByteAddress, outputByte);
            }

            state.Gpr[0] = r0;
            state.Gpr[3] = r3;
            state.Gpr[4] = r4;
            state.Gpr[5] = r5;
            state.Gpr[6] = r6;
            state.Ctr = ctr;
            state.Pc = state.Lr & 0xFFFF_FFFCu;
            SetCr0ForSignedCompareImmediate(state, compareLeft, compareRight);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static uint SonicModeStateJumpTarget(uint index) => index switch
    {
        0 => 0x800E_2EA4,
        1 or 2 or 3 or 4 or 8 or 9 or 10 or 11 => 0x800E_2EAC,
        5 => 0x800E_2E80,
        6 => 0x800E_2E74,
        7 => 0x800E_2E8C,
        12 => 0x800E_2E98,
        _ => 0,
    };

    private static bool MatchesSonicModeStateUpdate(GameCubeBus bus) =>
        bus.Read32(SonicModeStateUpdatePc + 0x00) == 0x3C80_801D
        && bus.Read32(SonicModeStateUpdatePc + 0x04) == 0x3884_C168
        && bus.Read32(SonicModeStateUpdatePc + 0x08) == 0x38C4_0047
        && bus.Read32(SonicModeStateUpdatePc + 0x0C) == 0x88A4_0047
        && bus.Read32(SonicModeStateUpdatePc + 0x10) == 0x7CA4_0774
        && bus.Read32(SonicModeStateUpdatePc + 0x14) == 0x2C04_0014
        && bus.Read32(SonicModeStateUpdatePc + 0x18) == 0x4182_0018
        && bus.Read32(SonicModeStateUpdatePc + 0x2C) == 0x908D_897C
        && bus.Read32(SonicModeStateUpdatePc + 0x30) == 0x3803_0001
        && bus.Read32(SonicModeStateUpdatePc + 0x34) == 0x2800_000C
        && bus.Read32(SonicModeStateUpdatePc + 0x38) == 0x4181_0054
        && bus.Read32(SonicModeStateUpdatePc + 0x4C) == 0x7C09_03A6
        && bus.Read32(SonicModeStateUpdatePc + 0x50) == 0x4E80_0420
        && bus.Read32(SonicModeStateUpdatePc + 0x8C) == 0x8806_0000
        && bus.Read32(SonicModeStateUpdatePc + 0x98) == 0x4D80_0020
        && bus.Read32(SonicModeStateUpdatePc + 0xB8) == 0x4C80_0020
        && bus.Read32(SonicModeStateUpdatePc + 0xF8) == 0x4E80_0020
        && bus.Read32(SonicModeStateJumpTableAddress + 0x00) == 0x800E_2EA4
        && bus.Read32(SonicModeStateJumpTableAddress + 0x04) == 0x800E_2EAC
        && bus.Read32(SonicModeStateJumpTableAddress + 0x14) == 0x800E_2E80
        && bus.Read32(SonicModeStateJumpTableAddress + 0x18) == 0x800E_2E74
        && bus.Read32(SonicModeStateJumpTableAddress + 0x1C) == 0x800E_2E8C
        && bus.Read32(SonicModeStateJumpTableAddress + 0x30) == 0x800E_2E98;

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

    private static bool MatchesSonicPairedTransform4dIndexedOutputLoop(GameCubeBus bus) =>
        bus.Read32(0x8011_DE54) == 0x1160_321C
        && bus.Read32(0x8011_DE58) == 0xF1E6_0000
        && bus.Read32(0x8011_DE5C) == 0x1181_3A1C
        && bus.Read32(0x8011_DE60) == 0xF206_8008
        && bus.Read32(0x8011_DE64) == 0x11A0_025A
        && bus.Read32(0x8011_DE68) == 0xF226_000C
        && bus.Read32(0x8011_DE6C) == 0x11C1_025A
        && bus.Read32(0x8011_DE70) == 0xF246_8014
        && bus.Read32(0x8011_DE74) == 0x1162_5A1E
        && bus.Read32(0x8011_DE78) == 0x1183_621E
        && bus.Read32(0x8011_DE7C) == 0xE507_0002
        && bus.Read32(0x8011_DE80) == 0x11A2_6A9C
        && bus.Read32(0x8011_DE84) == 0x11C3_729C
        && bus.Read32(0x8011_DE88) == 0xE284_0000
        && bus.Read32(0x8011_DE8C) == 0x1164_5A5C
        && bus.Read32(0x8011_DE90) == 0xE2A4_8008
        && bus.Read32(0x8011_DE94) == 0x1185_625C
        && bus.Read32(0x8011_DE98) == 0xE2C4_000C
        && bus.Read32(0x8011_DE9C) == 0x11A4_6A9E
        && bus.Read32(0x8011_DEA0) == 0xE2E4_8014
        && bus.Read32(0x8011_DEA4) == 0x11C5_729E
        && bus.Read32(0x8011_DEA8) == 0x7C86_2378
        && bus.Read32(0x8011_DEAC) == 0x11EB_A4DC
        && bus.Read32(0x8011_DEB0) == 0x120C_ACDC
        && bus.Read32(0x8011_DEB4) == 0xE527_0008
        && bus.Read32(0x8011_DEB8) == 0x122D_B4DC
        && bus.Read32(0x8011_DEBC) == 0x124E_BCDC
        && bus.Read32(0x8011_DEC0) == 0xE547_0008
        && bus.Read32(0x8011_DEC4) == 0xE667_9008
        && bus.Read32(0x8011_DEC8) == 0xA547_0002
        && bus.Read32(0x8011_DECC) == 0x554A_2834
        && bus.Read32(0x8011_DED0) == 0x7C89_5214
        && bus.Read32(0x8011_DED4) == 0x4200_FF80
        && bus.Read32(0x8011_DED8) == 0xF1E6_0000
        && bus.Read32(0x8011_DEDC) == 0xF206_8008
        && bus.Read32(0x8011_DEE0) == 0xF226_000C
        && bus.Read32(0x8011_DEE4) == 0xF246_8014
        && bus.Read32(0x8011_DEE8) == 0xC9C1_0008
        && bus.Read32(0x8011_DF0C) == 0xCAE1_0050
        && bus.Read32(0x8011_DF10) == 0x3821_0080
        && bus.Read32(0x8011_DF14) == 0x4E80_0020;

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

    private static bool MatchesSonicCoordinatePairFillLoop(GameCubeBus bus, uint pc) =>
        pc == SonicCoordinatePairFillLoopPc
        && bus.Read32(pc + 0x00) == 0x7C04_1800
        && bus.Read32(pc + 0x04) == 0x4180_000C
        && bus.Read32(pc + 0x08) == 0x3880_0000
        && bus.Read32(pc + 0x0C) == 0x38C6_0001
        && bus.Read32(pc + 0x10) == 0x7CC0_0774
        && bus.Read32(pc + 0x14) == 0x9805_0000
        && bus.Read32(pc + 0x18) == 0x7C80_0774
        && bus.Read32(pc + 0x1C) == 0x3884_0001
        && bus.Read32(pc + 0x20) == 0x9805_0001
        && bus.Read32(pc + 0x24) == 0x38A5_0002
        && bus.Read32(pc + 0x28) == 0x4200_FFD8;

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

    private static (double Lane0, double Lane1) ReadPairedSingleFloatSingle(GameCubeBus bus, uint address) =>
        (ReadSingleFloat(bus, address), 1.0d);

    private static (double Lane0, double Lane1) ReadPairedSingleQuantized(GameCubeBus bus, PowerPcState state, uint address, int gqr, bool single)
    {
        int type = (int)((state.Spr[912 + gqr] >> 16) & 0x7);
        int scale = SignExtend6((int)((state.Spr[912 + gqr] >> 24) & 0x3F));
        double lane0 = ReadPairedSingleQuantizedOperand(bus, address, type, scale, out int size);
        double lane1 = single ? 1.0d : ReadPairedSingleQuantizedOperand(bus, address + (uint)size, type, scale, out _);
        return (lane0, lane1);
    }

    private static double ReadPairedSingleQuantizedOperand(GameCubeBus bus, uint address, int type, int scale, out int size)
    {
        double value;
        switch (type)
        {
            case 0:
                size = sizeof(uint);
                return ReadSingleFloat(bus, address);
            case 4:
                size = sizeof(byte);
                value = bus.Memory.Read8(address);
                break;
            case 5:
                size = sizeof(ushort);
                value = bus.Memory.Read16(address);
                break;
            case 6:
                size = sizeof(byte);
                value = unchecked((sbyte)bus.Memory.Read8(address));
                break;
            case 7:
                size = sizeof(ushort);
                value = unchecked((short)bus.Memory.Read16(address));
                break;
            default:
                throw new AddressTranslationException(address);
        }

        return Math.ScaleB(value, -scale);
    }

    private static int SignExtend6(int value) => (value & 0x20) != 0 ? value - 0x40 : value;

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

    private static void WriteSonicPairedTransform4dIndexedOutput(GameCubeBus bus, uint cursor, (double Lane0, double Lane1) first, (double Lane0, double Lane1) second, (double Lane0, double Lane1) third, (double Lane0, double Lane1) fourth)
    {
        WriteSingleFloat(bus, cursor, first.Lane0);
        WriteSingleFloat(bus, cursor + sizeof(uint), first.Lane1);
        WriteSingleFloat(bus, cursor + 8, second.Lane0);
        WriteSingleFloat(bus, cursor + 12, third.Lane0);
        WriteSingleFloat(bus, cursor + 16, third.Lane1);
        WriteSingleFloat(bus, cursor + 20, fourth.Lane0);
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
        SetCr0ForUnsignedCompare(state, left, right);
    }

    private static void SetCr0ForUnsignedCompare(PowerPcState state, uint left, uint right)
    {
        uint field = left == right
            ? 0x2000_0000u
            : left < right ? 0x8000_0000u : 0x4000_0000u;
        state.Cr = (state.Cr & 0x0FFF_FFFF) | field | (state.Xer & 0x8000_0000) >> 3;
    }

    private static bool TryFastForwardMemsetRoutine(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint pc = state.Pc;
        if (MatchesMemsetWrapper(bus, pc))
        {
            uint stackPointer = state.Gpr[1];
            uint newStackPointer = unchecked(stackPointer - 32u);
            uint originalLr = state.Lr;
            uint originalR31 = state.Gpr[31];
            uint destination = state.Gpr[3];
            uint value = state.Gpr[4];
            uint count = state.Gpr[5];
            if (!bus.Memory.IsMainRamAddress(newStackPointer, 40)
                || !TryFastForwardMemsetCore(state, bus, pc + 0x30, destination, value, count, wrapperInstructions: 12, out uint coreInstructions))
            {
                return false;
            }

            bus.Memory.Write32(stackPointer + 4, originalLr);
            bus.Memory.Write32(newStackPointer, stackPointer);
            bus.Memory.Write32(newStackPointer + 28, originalR31);
            state.Gpr[0] = originalLr;
            state.Gpr[1] = stackPointer;
            state.Gpr[3] = destination;
            state.Gpr[31] = originalR31;
            state.Lr = originalLr;
            state.Pc = originalLr & 0xFFFF_FFFCu;

            uint skipped = checked(coreInstructions + 12);
            AdvanceFastForwardedInstructions(state, bus, skipped);
            skippedInstructions = checked((int)skipped);
            return true;
        }

        if (!MatchesMemsetCore(bus, pc)
            || !TryFastForwardMemsetCore(state, bus, pc, state.Gpr[3], state.Gpr[4], state.Gpr[5], wrapperInstructions: 0, out uint directCoreInstructions))
        {
            return false;
        }

        state.Pc = state.Lr & 0xFFFF_FFFCu;
        AdvanceFastForwardedInstructions(state, bus, directCoreInstructions);
        skippedInstructions = checked((int)directCoreInstructions);
        return true;
    }

    private static bool TryFastForwardMemsetCore(
        PowerPcState state,
        GameCubeBus bus,
        uint pc,
        uint destination,
        uint value,
        uint count,
        uint wrapperInstructions,
        out uint coreInstructions)
    {
        coreInstructions = 0;
        if (!MatchesMemsetCore(bus, pc)
            || count > MaxFastForwardMemmoveBytes
            || !CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: checked(wrapperInstructions + EstimateMemsetCoreInstructions(destination, (byte)value, count)), extraInstructions: 0)
            || (count != 0 && !bus.Memory.IsMainRamAddress(destination, checked((int)count))))
        {
            return false;
        }

        byte fillByte = (byte)value;
        if (count != 0)
        {
            if (fillByte == 0)
            {
                bus.Memory.Clear(destination, count);
            }
            else
            {
                for (uint offset = 0; offset < count; offset++)
                {
                    bus.Memory.Write8(destination + offset, fillByte);
                }
            }
        }

        SimulateMemsetCoreRegisters(state, destination, fillByte, count, out coreInstructions);
        return true;
    }

    private static uint EstimateMemsetCoreInstructions(uint destination, byte fillByte, uint count)
    {
        uint instructions = 5;
        if (count >= 32)
        {
            uint cursor = unchecked(destination - 1u);
            uint alignBytes = unchecked(~cursor) & 3u;
            instructions += 4;
            if (alignBytes != 0)
            {
                instructions += 2 + alignBytes * 3;
                count -= alignBytes;
                cursor += alignBytes;
            }

            instructions += 5;
            if (fillByte != 0)
            {
                instructions += 6;
            }

            uint wordBursts = count >> 5;
            if (wordBursts != 0)
            {
                instructions += wordBursts * 10;
            }

            uint remainingWords = (count >> 2) & 7u;
            instructions += 2;
            if (remainingWords != 0)
            {
                instructions += remainingWords * 3;
            }

            uint remainingBytes = count & 3u;
            instructions += 4;
            if (remainingBytes != 0)
            {
                instructions += 1 + remainingBytes * 3 + 1;
            }

            return instructions;
        }

        uint tailBytes = count & 3u;
        instructions += 2;
        if (tailBytes != 0)
        {
            instructions += 1 + tailBytes * 3 + 1;
        }

        return instructions;
    }

    private static void SimulateMemsetCoreRegisters(PowerPcState state, uint destination, byte fillByte, uint count, out uint coreInstructions)
    {
        coreInstructions = 5;
        uint r3 = state.Gpr[3];
        uint r5 = count;
        uint r6 = unchecked(destination - 1u);
        uint r7 = fillByte;
        uint r4 = state.Gpr[4];
        uint r0;

        SetCr0ForUnsignedCompareImmediate(state, r5, 32);
        if (r5 >= 32)
        {
            r0 = unchecked(~r6) & 3u;
            r3 = r0;
            coreInstructions += 4;
            if (r3 != 0)
            {
                r5 -= r3;
                coreInstructions += 2;
                while (true)
                {
                    r3--;
                    r6++;
                    coreInstructions += 3;
                    SetCr0(state, r3);
                    if (r3 == 0)
                    {
                        break;
                    }
                }
            }

            if (r7 != 0)
            {
                r4 = (uint)fillByte << 8;
                r7 |= r7 << 24;
                r7 |= ((uint)fillByte << 16) | ((uint)fillByte << 8);
                coreInstructions += 6;
            }

            r0 = r5 >> 5;
            r3 = unchecked(r6 - 3u);
            coreInstructions += 5;
            while (r0 != 0)
            {
                r0--;
                r3 += 32;
                coreInstructions += 10;
                SetCr0(state, r0);
            }

            r0 = (r5 >> 2) & 7u;
            coreInstructions += 2;
            while (r0 != 0)
            {
                r0--;
                r3 += 4;
                coreInstructions += 3;
                SetCr0(state, r0);
            }

            r6 = r3 + 3;
            r5 &= 3u;
            coreInstructions += 4;
            SetCr0ForUnsignedCompareImmediate(state, r5, 0);
            if (r5 == 0)
            {
                state.Gpr[0] = r0;
                state.Gpr[3] = r3;
                state.Gpr[4] = r4;
                state.Gpr[5] = r5;
                state.Gpr[6] = r6;
                state.Gpr[7] = r7;
                return;
            }
        }
        else
        {
            r5 &= 3u;
            coreInstructions += 2;
            SetCr0ForUnsignedCompareImmediate(state, r5, 0);
            if (r5 == 0)
            {
                state.Gpr[0] = r5;
                state.Gpr[3] = r3;
                state.Gpr[4] = r4;
                state.Gpr[5] = r5;
                state.Gpr[6] = r6;
                state.Gpr[7] = r7;
                return;
            }
        }

        r0 = fillByte;
        coreInstructions++;
        while (true)
        {
            r5--;
            r6++;
            coreInstructions += 3;
            SetCr0(state, r5);
            if (r5 == 0)
            {
                break;
            }
        }

        coreInstructions++;
        state.Gpr[0] = r0;
        state.Gpr[3] = r3;
        state.Gpr[4] = r4;
        state.Gpr[5] = r5;
        state.Gpr[6] = r6;
        state.Gpr[7] = r7;
    }

    private static bool MatchesMemsetWrapper(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x7C08_02A6
        && bus.Read32(pc + 0x04) == 0x9001_0004
        && bus.Read32(pc + 0x08) == 0x9421_FFE0
        && bus.Read32(pc + 0x0C) == 0x93E1_001C
        && bus.Read32(pc + 0x10) == 0x7C7F_1B78
        && bus.Read32(pc + 0x14) == 0x4800_001D
        && bus.Read32(pc + 0x18) == 0x8001_0024
        && bus.Read32(pc + 0x1C) == 0x7FE3_FB78
        && bus.Read32(pc + 0x20) == 0x83E1_001C
        && bus.Read32(pc + 0x24) == 0x3821_0020
        && bus.Read32(pc + 0x28) == 0x7C08_03A6
        && bus.Read32(pc + 0x2C) == 0x4E80_0020
        && MatchesMemsetCore(bus, pc + 0x30);

    private static bool MatchesMemsetCore(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x2805_0020
        && bus.Read32(pc + 0x04) == 0x5480_063E
        && bus.Read32(pc + 0x08) == 0x7C07_0378
        && bus.Read32(pc + 0x0C) == 0x38C3_FFFF
        && bus.Read32(pc + 0x10) == 0x4180_0098
        && bus.Read32(pc + 0x58) == 0x54A0_D97F
        && bus.Read32(pc + 0x8C) == 0x54A0_F77F
        && bus.Read32(pc + 0xA4) == 0x54A5_07BE
        && bus.Read32(pc + 0xAC) == 0x4D82_0020
        && bus.Read32(pc + 0xB4) == 0x34A5_FFFF
        && bus.Read32(pc + 0xB8) == 0x9C06_0001
        && bus.Read32(pc + 0xBC) == 0x4082_FFF8
        && bus.Read32(pc + 0xC0) == 0x4E80_0020;

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

        if (first == 0x8404_FFFC)
        {
            return TryFastForwardOptimizedMemmoveBackwardWordTail(state, bus, pc, out skippedInstructions);
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

    private static bool TryFastForwardOptimizedMemmoveBackwardWordTail(PowerPcState state, GameCubeBus bus, uint pc, out int skippedInstructions)
    {
        skippedInstructions = 0;
        uint wordCount = state.Gpr[3];
        uint residualBytes = state.Gpr[5] & 3u;
        if (!MatchesOptimizedMemmoveBackwardWordTail(bus, pc)
            || wordCount == 0
            || wordCount > MaxFastForwardMemmoveBytes / sizeof(uint))
        {
            return false;
        }

        uint totalBytes = checked(wordCount * sizeof(uint) + residualBytes);
        uint sourceStart = unchecked(state.Gpr[4] - totalBytes);
        uint destinationStart = unchecked(state.Gpr[6] - totalBytes);
        uint instructions = checked(wordCount * 4u + 2u + (residualBytes == 0 ? 0u : residualBytes * 4u + 1u));
        if (!CanFastForwardInstructionCount(state, iterations: 1, instructionsPerIteration: instructions, extraInstructions: 0)
            || !IsMainRamOrLockedCacheRange(bus, sourceStart, checked((int)totalBytes))
            || !IsMainRamOrLockedCacheRange(bus, destinationStart, checked((int)totalBytes)))
        {
            return false;
        }

        uint sourceCursor = state.Gpr[4];
        uint destinationCursor = state.Gpr[6];
        uint r0 = state.Gpr[0];
        uint remainingWords = wordCount;
        while (remainingWords != 0)
        {
            sourceCursor = unchecked(sourceCursor - sizeof(uint));
            destinationCursor = unchecked(destinationCursor - sizeof(uint));
            r0 = bus.Read32(sourceCursor);
            remainingWords--;
            bus.Write32(destinationCursor, r0);
        }

        uint remainingBytes = residualBytes;
        while (remainingBytes != 0)
        {
            sourceCursor--;
            destinationCursor--;
            r0 = bus.Read8(sourceCursor);
            remainingBytes--;
            bus.Write8(destinationCursor, (byte)r0);
        }

        state.Gpr[0] = r0;
        state.Gpr[3] = 0;
        state.Gpr[4] = sourceCursor;
        state.Gpr[5] = 0;
        state.Gpr[6] = destinationCursor;
        state.Pc = state.Lr & 0xFFFF_FFFCu;
        SetCarry(state, carry: true);
        SetCr0(state, 0);

        AdvanceFastForwardedInstructions(state, bus, instructions);
        skippedInstructions = checked((int)instructions);
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

    private static bool MatchesOptimizedMemmoveBackwardWordTail(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x8404_FFFC
        && bus.Read32(pc + 0x04) == 0x3463_FFFF
        && bus.Read32(pc + 0x08) == 0x9406_FFFC
        && bus.Read32(pc + 0x0C) == 0x4082_FFF4
        && bus.Read32(pc + 0x10) == 0x54A5_07BF
        && bus.Read32(pc + 0x14) == 0x4D82_0020
        && bus.Read32(pc + 0x18) == 0x8C04_FFFF
        && bus.Read32(pc + 0x1C) == 0x34A5_FFFF
        && bus.Read32(pc + 0x20) == 0x9C06_FFFF
        && bus.Read32(pc + 0x24) == 0x4082_FFF4
        && bus.Read32(pc + 0x28) == 0x4E80_0020;

    private static bool IsMainRamOrLockedCacheRange(GameCubeBus bus, uint address, int size)
    {
        if (bus.Memory.IsMainRamAddress(address, size))
        {
            return true;
        }

        if (address < LockedCacheStart || size < 0 || (uint)size > LockedCacheSize)
        {
            return false;
        }

        uint normalized = address - LockedCacheStart;
        return normalized <= LockedCacheSize - (uint)size;
    }

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

    private static uint ShiftRightAlgebraicWord(PowerPcState state, uint value, uint shift)
    {
        int amount = (shift & 0x20) != 0 ? 32 : (int)(shift & 0x1F);
        uint result = amount == 0 ? value : unchecked((uint)((int)value >> amount));
        bool carry = amount > 0 && (value & 0x8000_0000) != 0 && (value & ((1u << Math.Min(amount, 31)) - 1)) != 0;
        SetCarry(state, carry);
        return result;
    }

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

    private static string FormatWriteWatchChange(uint address, int width, uint value, bool oldValueValid, uint oldAddress, int oldWidth, uint oldValue)
    {
        if (!oldValueValid || oldAddress != address || oldWidth != width)
        {
            return string.Empty;
        }

        uint mask = width switch
        {
            1 => 0xFF,
            2 => 0xFFFF,
            4 => 0xFFFF_FFFF,
            _ => 0,
        };
        if (mask == 0)
        {
            return string.Empty;
        }

        uint maskedOldValue = oldValue & mask;
        uint maskedNewValue = value & mask;
        uint changedBits = maskedOldValue ^ maskedNewValue;
        int hexDigits = width * 2;
        return $" old=0x{maskedOldValue.ToString($"X{hexDigits}", CultureInfo.InvariantCulture)} xor=0x{changedBits.ToString($"X{hexDigits}", CultureInfo.InvariantCulture)}";
    }

    private static bool TryReadStoreValue(GameCubeMemory memory, uint address, int width, out uint value)
    {
        try
        {
            value = width switch
            {
                1 => memory.Read8(address),
                2 => memory.Read16(address),
                4 => memory.Read32(address),
                _ => 0,
            };
            return width is 1 or 2 or 4;
        }
        catch (AddressTranslationException)
        {
            value = 0;
            return false;
        }
    }

    private sealed record SonicResourceFlagTraceEvent(
        string Operation,
        uint FlagAddress,
        uint OldFlag,
        uint NewFlag,
        uint ChangedBits,
        uint Mask,
        uint Task,
        int TaskSlot,
        uint Selector,
        uint QueueHead,
        uint QueueTail);

    private static bool TryGetSonicResourceFlagTraceEvent(PowerPcState state, GameCubeBus bus, uint pc, out SonicResourceFlagTraceEvent traceEvent)
    {
        traceEvent = default!;
        if (pc is not (SonicResourceFlagSetQueuedPc or SonicResourceFlagSetActivePc or SonicResourceFlagSetListPc or SonicResourceFlagClearTaskPc or SonicResourceFlagClearSelectedPc))
        {
            return false;
        }

        uint flagAddress = unchecked(state.Gpr[13] + SonicResourceFlagOffset);
        if (!TryReadMemory32(bus.Memory, flagAddress, out uint oldFlag))
        {
            return false;
        }

        uint task = pc switch
        {
            SonicResourceFlagSetQueuedPc or SonicResourceFlagSetListPc => state.Gpr[6],
            SonicResourceFlagSetActivePc => state.Gpr[29],
            SonicResourceFlagClearTaskPc => state.Gpr[3],
            SonicResourceFlagClearSelectedPc => state.Gpr[31],
            _ => 0,
        };
        int taskSlot = TryReadMemory32(bus.Memory, task + SonicResourceTaskSlotOffset, out uint slotValue) ? unchecked((int)slotValue) : -1;
        uint selector = pc == SonicResourceFlagClearSelectedPc ? state.Gpr[7] : taskSlot < 0 ? 0xFFFF_FFFF : (uint)taskSlot;
        uint mask = pc switch
        {
            SonicResourceFlagSetQueuedPc or SonicResourceFlagSetActivePc or SonicResourceFlagSetListPc => taskSlot is >= 0 and < 32 ? 1u << (31 - taskSlot) : 0,
            SonicResourceFlagClearTaskPc => taskSlot is >= 0 and < 32 ? (uint)(taskSlot + 1) << (31 - taskSlot) : 0,
            SonicResourceFlagClearSelectedPc => selector < 32 ? unchecked((32u - selector) << (int)(selector ^ 31u)) : 0,
            _ => 0,
        };
        string operation = pc switch
        {
            SonicResourceFlagSetQueuedPc => "set-queued",
            SonicResourceFlagSetActivePc => "set-active",
            SonicResourceFlagSetListPc => "set-list",
            SonicResourceFlagClearTaskPc => "clear-task",
            SonicResourceFlagClearSelectedPc => "clear-selected",
            _ => "unknown",
        };
        uint newFlag = pc is SonicResourceFlagClearTaskPc or SonicResourceFlagClearSelectedPc
            ? oldFlag & ~mask
            : oldFlag | mask;
        if (state.Gpr[0] != newFlag)
        {
            newFlag = state.Gpr[0];
            mask = oldFlag ^ newFlag;
        }

        uint queueHead = TryReadMemory32(bus.Memory, task + 0x2E0, out uint head) ? head : 0;
        uint queueTail = TryReadMemory32(bus.Memory, task + 0x2E4, out uint tail) ? tail : 0;
        traceEvent = new SonicResourceFlagTraceEvent(operation, flagAddress, oldFlag, newFlag, oldFlag ^ newFlag, mask, task, taskSlot, selector, queueHead, queueTail);
        return true;
    }

    private static bool TryReadMemory32(GameCubeMemory memory, uint address, out uint value)
    {
        try
        {
            value = memory.Read32(address);
            return true;
        }
        catch (AddressTranslationException)
        {
            value = 0;
            return false;
        }
    }

    private static bool TryGetStoreEffectiveAddress(PowerPcState state, uint instruction, out uint effectiveAddress, out int byteWidth)
    {
        effectiveAddress = 0;
        byteWidth = 0;
        int opcode = (int)(instruction >> 26);
        int rA = (int)((instruction >> 16) & 0x1F);
        int rB = (int)((instruction >> 11) & 0x1F);
        uint baseAddress = rA == 0 ? 0 : state.Gpr[rA];

        switch (opcode)
        {
            case 36 or 37:
                byteWidth = sizeof(uint);
                effectiveAddress = unchecked(baseAddress + (uint)(short)(instruction & 0xFFFF));
                return true;

            case 38 or 39:
                byteWidth = sizeof(byte);
                effectiveAddress = unchecked(baseAddress + (uint)(short)(instruction & 0xFFFF));
                return true;

            case 44 or 45:
                byteWidth = sizeof(ushort);
                effectiveAddress = unchecked(baseAddress + (uint)(short)(instruction & 0xFFFF));
                return true;

            case 31:
                return TryGetIndexedStoreEffectiveAddress(state, instruction, baseAddress, rB, out effectiveAddress, out byteWidth);

            default:
                return false;
        }
    }

    private static bool TryGetIndexedStoreEffectiveAddress(PowerPcState state, uint instruction, uint baseAddress, int rB, out uint effectiveAddress, out int byteWidth)
    {
        effectiveAddress = 0;
        byteWidth = 0;
        int xo = (int)((instruction >> 1) & 0x3FF);
        byteWidth = xo switch
        {
            151 or 183 => sizeof(uint),
            215 or 247 => sizeof(byte),
            407 or 439 => sizeof(ushort),
            _ => 0,
        };
        if (byteWidth == 0)
        {
            return false;
        }

        effectiveAddress = unchecked(baseAddress + state.Gpr[rB]);
        return true;
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

        output.WriteLine($"Indirect branch-site profile 0x{callSite:X8}: {profile.Count} unique target(s), {total} branch(es)");
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
        return BuildFilteredPcProfileSummary(
            profile,
            topCount,
            executedInstructions,
            profileAfter,
            entry => IsExternalInterruptLeafHelperEntry(bus, entry));
    }

    private static object BuildPcProfileWithoutFastForwardLeavesSummary(IReadOnlyDictionary<uint, ulong> profile, int topCount, int executedInstructions, int profileAfter, GameCubeBus bus)
    {
        return BuildFilteredPcProfileSummary(
            profile,
            topCount,
            executedInstructions,
            profileAfter,
            entry => IsFastForwardLeafHelperEntry(bus, entry));
    }

    private static object BuildFilteredPcProfileSummary(
        IReadOnlyDictionary<uint, ulong> profile,
        int topCount,
        int executedInstructions,
        int profileAfter,
        Func<uint, bool> exclude)
    {
        List<KeyValuePair<uint, ulong>> included = [];
        ulong totalSamples = 0;
        ulong excludedSamples = 0;
        int profiledInstructions = Math.Max(0, executedInstructions - profileAfter);

        foreach (KeyValuePair<uint, ulong> entry in profile)
        {
            if (exclude(entry.Key))
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

    private static bool IsFastForwardLeafHelperEntry(GameCubeBus bus, uint pc)
    {
        if (IsKnownSonicFastForwardEntry(bus, pc))
        {
            return true;
        }

        uint first = bus.Read32(pc);
        if (first == 0x4E80_0020)
        {
            return true;
        }

        if (first == 0x3860_0000)
        {
            return bus.Read32(pc + 4) == 0x4E80_0020;
        }

        if (IsSmallDataWordLoadLeafEntry(bus, pc, first))
        {
            return true;
        }

        if (first == 0x9421_FF90)
        {
            return bus.Read32(pc + 0x04) == 0x4086_0024
                && bus.Read32(pc + 0x08) == 0xD821_0028
                && bus.Read32(pc + 0x24) == 0xD901_0060
                && bus.Read32(pc + 0x28) == 0x9061_0008
                && bus.Read32(pc + 0x44) == 0x9141_0024
                && bus.Read32(pc + 0x48) == 0x3821_0070
                && bus.Read32(pc + 0x4C) == 0x4E80_0020;
        }

        if (first is 0x7C6D_42E6 or 0x7C6C_42E6)
        {
            return IsTimeBaseReadLeafEntry(bus, pc);
        }

        return IsExternalInterruptLeafHelperEntry(bus, pc);
    }

    private static bool IsSmallDataWordLoadLeafEntry(GameCubeBus bus, uint pc, uint first)
    {
        return TryDecodeDForm(first, primaryOpcode: 32, out int targetRegister, out int baseRegister, out _)
            && targetRegister == 3
            && baseRegister == 13
            && bus.Read32(pc + 4) == 0x4E80_0020;
    }

    private static bool IsKnownSonicFastForwardEntry(GameCubeBus bus, uint pc)
    {
        return pc switch
        {
            SonicResourceFlagWaitLoopPc or SonicResourceFlagWaitLoopPc + 4 or SonicResourceFlagWaitLoopPc + 8 => MatchesSonicResourceFlagWaitLoop(bus),
            SonicDvdStatusWaitLoopPc => MatchesSonicDvdStatusWaitLoop(bus),
            SonicModeCoordinatorProloguePc => MatchesSonicModeCoordinatorPrologue(bus),
            SonicModeCoordinatorBodyPc => MatchesSonicModeCoordinatorBody(bus),
            SonicModeCoordinatorZeroTailPc => MatchesSonicModeCoordinatorZeroTail(bus),
            SonicStatusCallerLoopPc or SonicStatusCallerPostQueryPc or SonicStatusCallerDispatchPc or SonicStatusCallerLoopBackPc => MatchesSonicStatusCallerLoop(bus),
            SonicTableByteBuildLoopPc or SonicTableByteBuildPostCallPc => MatchesSonicTableByteBuildDispatch(bus),
            SonicLineCopyLoadPc or SonicLineCopyLoopPc => MatchesSonicLineCopy(bus),
            SonicLineSkipLoopPc => MatchesSonicLineSkip(bus),
            SonicStringAppendScanLoopPc => MatchesSonicStringAppendScan(bus),
            SonicFreeBlockScanLoopPc => MatchesSonicFreeBlockScan(bus),
            SonicCacheStoreSweepLoopPc => MatchesSonicCacheStoreSweep(bus),
            SonicStateZeroFillLoopPc => MatchesSonicStateZeroFill(bus),
            SonicManagerSlotScanLoopPc => MatchesSonicManagerSlotScan(bus),
            SonicTaskEntryScanLoopPc => MatchesSonicTaskEntryScan(bus),
            SonicObjectSlotScanLoopPc => MatchesSonicObjectSlotScan(bus),
            SonicHalfwordChecksumLoopPc or SonicHalfwordChecksumSecondLoopPc => MatchesSonicHalfwordChecksumLoop(bus, pc),
            SonicStatusQueryPc => MatchesSonicStatusQuery(bus),
            SonicNullSlotScanLoopPc => MatchesSonicNullSlotScanLoop(bus),
            SonicPoolNullSlotScanLoopPc => MatchesSonicPoolNullSlotScanLoop(bus),
            SonicPoolSentinelSlotScanLoopPc => MatchesSonicPoolSentinelSlotScanLoop(bus),
            SonicTableKeyScanLoopPc => MatchesSonicTableKeyScanLoop(bus),
            SonicModeRefreshCallbackCheckPc or SonicModeRefreshCallbackCheckPc + 4 or SonicModeRefreshCallbackCheckPc + 8 or SonicModeRefreshCounterCheckPc or SonicModeRefreshCounterCheckPc + 4 or SonicModeRefreshCounterCheckPc + 8 or SonicModeRefreshCallPc or SonicModeRefreshPostCallPc => MatchesSonicModeRefreshDispatch(bus),
            0x800F_135C => IsSonicModeWrapperEntry(bus, pc),
            _ => false,
        };
    }

    private static bool IsSonicModeWrapperEntry(GameCubeBus bus, uint pc) =>
        bus.Read32(pc + 0x00) == 0x7C08_02A6
        && bus.Read32(pc + 0x04) == 0x9001_0004
        && bus.Read32(pc + 0x08) == 0x9421_FFE8
        && bus.Read32(pc + 0x10) == 0x7C7F_1B78
        && bus.Read32(pc + 0x14) == 0x4BFF_653D
        && bus.Read32(pc + 0x48) == 0x4E80_0020;

    private static bool IsTimeBaseReadLeafEntry(GameCubeBus bus, uint pc)
    {
        if (bus.Read32(pc) == 0x7C6C_42E6)
        {
            return bus.Read32(pc + 4) == 0x4E80_0020;
        }

        return bus.Read32(pc) == 0x7C6D_42E6
            && bus.Read32(pc + 0x04) == 0x7C8C_42E6
            && bus.Read32(pc + 0x08) == 0x7CAD_42E6
            && bus.Read32(pc + 0x0C) == 0x7C03_2800
            && bus.Read32(pc + 0x10) == 0x4082_FFF0
            && bus.Read32(pc + 0x14) == 0x4E80_0020;
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
        ulong totalBranches = profile.Values.Aggregate(0UL, static (total, count) => total + count);
        return new
        {
            callSite = $"0x{callSite:X8}",
            uniqueTargets = profile.Count,
            totalCalls = totalBranches,
            totalBranches,
            entries = profile
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key)
                .Take(topCount)
                .Select(entry => new
                {
                    target = $"0x{entry.Key:X8}",
                    count = entry.Value,
                    percent = totalBranches == 0 ? 0 : Math.Round((double)entry.Value * 100 / totalBranches, 3),
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

    private static object? BuildGxCopyMarkerSummary(GxFifoSoftwareRenderCopyMarker? marker)
    {
        if (marker is not GxFifoSoftwareRenderCopyMarker copy)
        {
            return null;
        }

        return new
        {
            copyIndex = copy.CopyIndex,
            isDisplayCopy = copy.IsDisplayCopy,
            drawsSeen = copy.DrawsSeen,
            fifoOffset = $"0x{copy.FifoOffset:X}",
            destinationAddress = $"0x{copy.DestinationAddress:X8}",
            width = copy.Width,
            height = copy.Height,
            format = copy.Format.ToString(),
            clearAfterCopy = copy.ClearAfterCopy,
        };
    }

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

    private readonly record struct SonicPrsDecompressTraceEvent(
        uint Pc,
        uint Source,
        uint SourceEnd,
        uint Destination,
        int OutputLength,
        int TargetOutputOffset,
        byte LastFlagByte,
        int BitsRemaining,
        uint SkippedInstructions,
        string SourceBytes,
        string TargetOutputBytes,
        string OutputHeadBytes);

    private readonly record struct SonicPacketInference(bool Found, uint Packet, uint Kind, uint Stream0, uint Stream1);

    private struct GxMemoryCheckpointState(GxMemoryCheckpointRequest request)
    {
        public GxMemoryCheckpointRequest Request { get; } = request;

        public bool Written { get; set; }

        public byte[]? Bytes { get; set; }
    }
}
