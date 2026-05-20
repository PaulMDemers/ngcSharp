namespace NgcSharp.Core;

public sealed class GameCubeBus : IMemoryBus
{
    private const int AramSize = 16 * 1024 * 1024;
    private const int LockedCacheSize = 16 * 1024;
    private const int ExternalInterfaceMemoryCardSize = 2 * 1024 * 1024;
    private const int ExternalInterfaceMemoryCardSectorSize = 0x2000;
    private const int ExternalInterfaceMemoryCardWriteBlockSize = 0x80;
    private const int ExternalInterfaceMemoryCardMetadataBlocks = 5;
    private const ushort ExternalInterfaceMemoryCard251Mbits = 0x10;
    private const ushort ExternalInterfaceMemoryCard251FreeBlocks =
        (ExternalInterfaceMemoryCardSize / ExternalInterfaceMemoryCardSectorSize) - ExternalInterfaceMemoryCardMetadataBlocks;
    private const uint LockedCacheStart = 0xE000_0000;
    private const uint DspMailboxFull = 0x8000;
    private const uint ArqRequestMagic = 0x1234_5678;
    private const ulong ArqRequestLatencyCycles = 512;
    private const ulong DirectAramDmaBaseLatencyCycles = 512;
    private const ulong DiscInterfaceCommandLatencyCycles = 10_000;
    private const uint DiscInterfaceErrorOk = 0x0000_0000;
    private const uint DiscInterfaceErrorNoDisc = 0x0302_3A00;
    private const uint DiscInterfaceErrorMotorStopped = 0x0402_0400;
    private const uint DiscInterfaceErrorInvalidCommand = 0x0505_2000;
    private const uint DiscInterfaceErrorInvalidField = 0x0505_2400;
    private const uint DiscInterfaceErrorAddressOutOfRange = 0x0505_2100;
    private const uint PikminResourceVTable = 0x802A_5D14;
    private const uint PikminResourceId = 0x0002_0000;
    private const uint PikminPlaceholderResourceAddress = 0x8037_0000;
    private const int PikminResourcePointerListOffset = 0x44;
    private const int PikminResourcePointerListCount = 16;

    public const int VideoVisibleLines = 480;
    public const int VideoScanlinesPerFrame = 525;
    public const int VideoCyclesPerScanline = 512;
    public const uint ProcessorInterfaceVideoInterrupt = 0x0000_0100;
    public const uint ProcessorInterfacePixelEngineTokenInterrupt = 0x0000_0200;
    public const uint ProcessorInterfacePixelEngineFinishInterrupt = 0x0000_0400;
    public const uint ProcessorInterfaceDiscInterrupt = 0x0000_0004;
    public const uint ProcessorInterfaceSerialInterrupt = 0x0000_0008;
    public const uint ProcessorInterfaceExternalInterrupt = 0x0000_0010;
    public const uint ProcessorInterfaceAudioInterrupt = 0x0000_0020;
    public const uint ProcessorInterfaceDspInterrupt = 0x0000_0040;
    public const uint ProcessorInterfaceResetSwitchReleased = 0x0001_0000;
    public const ushort VideoInterruptStatus = 0x8000;
    public const ushort VideoInterruptEnable = 0x1000;
    public const ushort VideoInterruptLineMask = 0x07FF;
    public const uint DiscInterfaceBreak = 0x0000_0001;
    public const uint DiscInterfaceDeviceErrorInterruptMask = 0x0000_0002;
    public const uint DiscInterfaceDeviceErrorInterruptStatus = 0x0000_0004;
    public const uint DiscInterfaceInterruptMask = 0x0000_0008;
    public const uint DiscInterfaceTransferComplete = 0x0000_0010;
    public const uint DiscInterfaceBreakInterruptMask = 0x0000_0020;
    public const uint DiscInterfaceBreakInterruptStatus = 0x0000_0040;
    public const uint DiscInterfaceStatusMask = 0x0000_007F;
    public const uint DiscInterfaceInterruptStatusMask = DiscInterfaceDeviceErrorInterruptStatus | DiscInterfaceTransferComplete | DiscInterfaceBreakInterruptStatus;
    public const uint DiscInterfaceInterruptMaskBits = DiscInterfaceDeviceErrorInterruptMask | DiscInterfaceInterruptMask | DiscInterfaceBreakInterruptMask;
    public const uint DiscInterfaceCoverOpened = 0x0000_0001;
    public const uint DiscInterfaceCoverInterruptMask = 0x0000_0002;
    public const uint DiscInterfaceCoverInterruptStatus = 0x0000_0004;
    public const uint DiscInterfaceConfiguration = 0x0000_0001;
    public const uint AudioInterfacePlayStatus = 0x0000_0001;
    public const uint AudioInterfaceInterruptMask = 0x0000_0004;
    public const uint AudioInterfaceInterruptStatus = 0x0000_0008;
    public const uint AudioInterfaceInterruptValid = 0x0000_0010;
    public const uint AudioInterfaceSampleCounterReset = 0x0000_0020;
    public const uint DspControlDmaInterruptMask = 0x0000_0800;
    public const uint DspControlDspInterruptStatus = 0x0000_0400;
    public const uint DspControlAramInterruptMask = 0x0000_0200;
    public const uint DspControlAramInterruptStatus = 0x0000_0100;
    public const uint DspControlAudioDmaInterruptMask = 0x0000_0080;
    public const uint DspControlAudioDmaInterruptStatus = 0x0000_0040;
    public const uint DspControlInterruptStatusMask = DspControlDspInterruptStatus | DspControlAramInterruptStatus | DspControlAudioDmaInterruptStatus;
    public const uint SerialInterfaceStandardControllerType = 0x0900_0000;
    public const uint SerialInterfaceNeutralControllerHigh = 0x0000_8080;
    public const uint SerialInterfaceNeutralControllerLow = 0x8080_0000;
    public const ushort SerialInterfaceControllerButtonLeft = 0x0001;
    public const ushort SerialInterfaceControllerButtonRight = 0x0002;
    public const ushort SerialInterfaceControllerButtonDown = 0x0004;
    public const ushort SerialInterfaceControllerButtonUp = 0x0008;
    public const ushort SerialInterfaceControllerButtonZ = 0x0010;
    public const ushort SerialInterfaceControllerButtonR = 0x0020;
    public const ushort SerialInterfaceControllerButtonL = 0x0040;
    public const ushort SerialInterfaceControllerButtonA = 0x0100;
    public const ushort SerialInterfaceControllerButtonB = 0x0200;
    public const ushort SerialInterfaceControllerButtonX = 0x0400;
    public const ushort SerialInterfaceControllerButtonY = 0x0800;
    public const ushort SerialInterfaceControllerButtonStart = 0x1000;
    public const uint SerialInterfaceTransferCompleteStatus = 0x8000_0000;
    public const uint SerialInterfaceTransferCompleteMask = 0x4000_0000;
    public const uint SerialInterfaceReadStatusInterruptStatus = 0x1000_0000;
    public const uint SerialInterfaceReadStatusInterruptMask = 0x0800_0000;
    public const ushort PixelEngineTokenEnable = 0x0001;
    public const ushort PixelEngineFinishEnable = 0x0002;
    public const ushort PixelEngineTokenStatus = 0x0004;
    public const ushort PixelEngineFinishStatus = 0x0008;
    public const ushort PixelEngineInterruptMask = PixelEngineTokenEnable | PixelEngineFinishEnable;
    public const ushort PixelEngineInterruptStatusMask = PixelEngineTokenStatus | PixelEngineFinishStatus;
    public const uint ExternalInterfaceDeviceConnected = 0x0000_1000;
    public const uint ExternalInterfaceExternalInterruptStatus = 0x0000_0800;
    public const uint ExternalInterfaceExternalInterruptMask = 0x0000_0400;
    public const uint ExternalInterfaceTransferCompleteStatus = 0x0000_0008;
    public const uint ExternalInterfaceTransferCompleteMask = 0x0000_0004;
    public const uint ExternalInterfaceInterruptStatus = 0x0000_0002;
    public const uint ExternalInterfaceInterruptMask = 0x0000_0001;
    public const uint ExternalInterfaceMemoryCard251Id = 0x0000_0010;

    private static readonly (uint Start, uint End, string Name)[] HardwareRegisterBlocks =
    [
        (0xCC00_0000, 0xCC00_0FFF, "CP"),
        (0xCC00_1000, 0xCC00_1FFF, "PE"),
        (0xCC00_2000, 0xCC00_2FFF, "VI"),
        (0xCC00_3000, 0xCC00_3FFF, "PI"),
        (0xCC00_4000, 0xCC00_4FFF, "MI"),
        (0xCC00_5000, 0xCC00_5FFF, "AI"),
        (0xCC00_6000, 0xCC00_63FF, "DI"),
        (0xCC00_6400, 0xCC00_67FF, "SI"),
        (0xCC00_6800, 0xCC00_6BFF, "EXI"),
        (0xCC00_6C00, 0xCC00_6FFF, "Streaming"),
        (0xCC00_8000, 0xCC00_8FFF, "GX FIFO"),
    ];

    private readonly Dictionary<uint, uint> _mmioValues = [];
    private readonly List<MmioAccess> _mmioAccesses = [];
    private readonly byte[] _aram = new byte[AramSize];
    private readonly byte[] _lockedCache = new byte[LockedCacheSize];
    private readonly Queue<uint> _dspMailboxOut = [];
    private readonly HashSet<uint> _completedDspTaskCallbacks = [];
    private readonly Dictionary<uint, uint> _trivialDspTaskCallbackTargets = [];
    private readonly Dictionary<uint, uint> _decrementWordDspTaskCallbackTargets = [];
    private readonly Dictionary<uint, uint> _byteSetDspTaskCallbackTargets = [];
    private readonly Dictionary<uint, GlobalWordSetDspTaskCallback> _globalWordSetDspTaskCallbacks = [];
    private readonly Dictionary<uint, SlotClearDspTaskCallback> _slotClearDspTaskCallbacks = [];
    private readonly byte[] _serialInterfaceIoBuffer = new byte[0x80];
    private readonly List<PendingArqRequest> _pendingArqRequests = [];
    private readonly HashSet<uint> _pendingArqDescriptorAddresses = [];
    private readonly ExternalInterfaceChannelState[] _externalInterfaceChannels =
    [
        new(),
        new(),
        new(),
    ];
    private readonly byte[] _externalInterfaceSram = CreateDefaultExternalInterfaceSram();
    private readonly byte[] _externalInterfaceMemoryCardSlotA = CreateDefaultExternalInterfaceMemoryCard(0);
    private readonly byte[] _externalInterfaceMemoryCardSlotB = CreateDefaultExternalInterfaceMemoryCard(1);
    private readonly DiscImageReader? _disc;
    private bool _dspMailboxInterruptPending;
    private ushort _dspMailboxInHigh;
    private ushort _dspMailboxInLow;
    private bool _dspMailboxInHighValid;
    private bool _dspMailboxInLowValid;
    private ulong _videoCycles;
    private uint _audioSampleCounter;
    private uint _processorInterruptCause;
    private uint _processorInterruptMask;
    private uint _pikminResourceAddress;
    private uint _discInterfaceLastError = DiscInterfaceErrorOk;
    private uint _discInterfacePosition;
    private ulong _serialInterfacePollCycles;
    private bool _discInterfaceMotorStopped;
    private bool _discInterfaceDiscInitialized;
    private bool _discInterfaceAudioEnabled;
    private bool _discInterfaceAudioStreaming;
    private PendingDiscCommand? _pendingDiscCommand;
    private ulong _pendingDiscCommandCycles;
    private PendingDirectAramDma? _pendingDirectAramDma;
    private GxFifoParserState _gxFifoParserState;
    private ulong _gxFifoRecentBytes;
    private int _gxFifoRecentByteCount;
    private int _serialInterfaceCommunicationTransferChannel;

    public GameCubeBus()
        : this(new GameCubeMemory())
    {
    }

    public GameCubeBus(GameCubeMemory memory, DiscImageReader? disc = null)
    {
        Memory = memory;
        _disc = disc;
        _mmioValues[SerialInterfaceStatusAddress] = 0;
        QueueDspBootGreeting();
    }

    public GameCubeBus(DiscImageReader disc)
        : this(new GameCubeMemory(), disc)
    {
    }

    public GameCubeMemory Memory { get; }

    public IReadOnlyList<MmioAccess> MmioAccesses => _mmioAccesses;

    public bool LogMmioAccesses { get; set; } = true;

    public Action<uint, uint>? MainRamWrite32Observer { get; set; }

    public Action<uint, int, uint>? MainRamWriteObserver { get; set; }

    public Action<MmioAccess>? MmioAccessObserver { get; set; }

    public uint SmallDataBaseRegister { get; set; }

    public ushort SerialInterfaceControllerButtons { get; set; }

    public bool SerialInterfaceControllerPort0Connected { get; set; } = true;

    public bool SerialInterfaceControllerPort1Connected { get; set; }

    public bool SerialInterfaceControllerPort2Connected { get; set; }

    public bool SerialInterfaceControllerPort3Connected { get; set; }

    public uint ExternalInterfaceRtcCounter { get; set; } = CreateDefaultExternalInterfaceRtcCounter();

    public bool ExternalInterfaceMemoryCardSlotAInserted { get; set; }

    public bool ExternalInterfaceMemoryCardSlotBInserted { get; set; }

    public ulong VideoCycleCounter => _videoCycles;

    public int CurrentVideoLine => (int)((_videoCycles / VideoCyclesPerScanline) % VideoScanlinesPerFrame);

    public ulong VideoFrameCounter => _videoCycles / ((ulong)VideoCyclesPerScanline * VideoScanlinesPerFrame);

    public bool IsVideoInVBlank => CurrentVideoLine >= VideoVisibleLines;

    public uint ProcessorInterruptCause
    {
        get
        {
            RefreshDspProcessorInterrupt();
            return _processorInterruptCause;
        }
    }

    public uint ProcessorInterruptMask => _processorInterruptMask;

    public uint PendingProcessorInterrupts => ProcessorInterruptCause & ProcessorInterruptMask;

    public bool HasPendingExternalInterrupt
    {
        get
        {
            return PendingProcessorInterrupts != 0;
        }
    }

    public ExternalInterfaceDebugSnapshot GetExternalInterfaceDebugSnapshot()
    {
        ExternalInterfaceChannelDebugSnapshot[] channels = new ExternalInterfaceChannelDebugSnapshot[_externalInterfaceChannels.Length];
        for (int channel = 0; channel < channels.Length; channel++)
        {
            uint baseAddress = ExternalInterfaceChannelBaseAddress(channel);
            uint parameter = NormalizeExternalInterfaceRead(baseAddress, ReadMmioValue(baseAddress));
            uint dmaAddress = NormalizeExternalInterfaceRead(baseAddress + 0x04, ReadMmioValue(baseAddress + 0x04));
            uint dmaLength = NormalizeExternalInterfaceRead(baseAddress + 0x08, ReadMmioValue(baseAddress + 0x08));
            uint control = NormalizeExternalInterfaceRead(baseAddress + 0x0C, ReadMmioValue(baseAddress + 0x0C));
            uint data = ReadMmioValue(baseAddress + 0x10);
            TryDecodeSelectedExternalInterfaceDevice(parameter, out int selectedDevice);
            ExternalInterfaceChannelState state = _externalInterfaceChannels[channel];
            channels[channel] = new ExternalInterfaceChannelDebugSnapshot(
                channel,
                parameter,
                dmaAddress,
                dmaLength,
                control,
                data,
                selectedDevice,
                (parameter & ExternalInterfaceDeviceConnected) != 0,
                (parameter & ExternalInterfaceTransferCompleteStatus) != 0,
                (parameter & ExternalInterfaceTransferCompleteMask) != 0,
                (parameter & ExternalInterfaceInterruptStatus) != 0,
                (parameter & ExternalInterfaceInterruptMask) != 0,
                (parameter & ExternalInterfaceExternalInterruptStatus) != 0,
                (parameter & ExternalInterfaceExternalInterruptMask) != 0,
                state.Command,
                state.HasCommand,
                state.PendingImmediateWrite,
                state.PendingWriteOffset,
                state.MemoryCardCommand.ToString(),
                state.MemoryCardCommandStarted,
                state.MemoryCardCommandByteCount,
                state.MemoryCardAddressBytesReceived,
                state.MemoryCardAddress,
                state.MemoryCardOffset,
                state.MemoryCardDataBytesTransferred,
                state.MemoryCardStatus,
                state.MemoryCardInterruptEnabled);
        }

        return new ExternalInterfaceDebugSnapshot(
            ProcessorInterruptCause,
            ProcessorInterruptMask,
            HasPendingExternalInterrupt,
            channels);
    }

    public DiscInterfaceDebugSnapshot GetDiscInterfaceDebugSnapshot()
    {
        uint status = NormalizeDiscInterfaceRead(0xCC00_6000, ReadMmioValue(0xCC00_6000));
        uint cover = NormalizeDiscInterfaceRead(0xCC00_6004, ReadMmioValue(0xCC00_6004));
        uint control = NormalizeDiscInterfaceRead(0xCC00_601C, ReadMmioValue(0xCC00_601C));
        return new DiscInterfaceDebugSnapshot(
            ProcessorInterruptCause,
            ProcessorInterruptMask,
            HasPendingExternalInterrupt,
            status,
            cover,
            ReadMmioValue(0xCC00_6008),
            ReadMmioValue(0xCC00_600C),
            ReadMmioValue(0xCC00_6010),
            ReadMmioValue(0xCC00_6014),
            NormalizeDiscInterfaceRead(0xCC00_6018, ReadMmioValue(0xCC00_6018)),
            control,
            ReadMmioValue(0xCC00_6020),
            DiscInterfaceConfiguration,
            _pendingDiscCommand is not null,
            _pendingDiscCommandCycles,
            _discInterfaceLastError,
            (status & DiscInterfaceDeviceErrorInterruptStatus) != 0,
            (status & DiscInterfaceDeviceErrorInterruptMask) != 0,
            (status & DiscInterfaceTransferComplete) != 0,
            (status & DiscInterfaceInterruptMask) != 0,
            (status & DiscInterfaceBreakInterruptStatus) != 0,
            (status & DiscInterfaceBreakInterruptMask) != 0);
    }

    public byte Read8(uint address)
    {
        if (Memory.IsMainRamAddress(address, sizeof(byte)))
        {
            return Memory.Read8(address);
        }

        if (TryTranslateLockedCache(address, out int lockedCacheOffset))
        {
            return _lockedCache[lockedCacheOffset];
        }

        return (byte)ReadMmio(address, sizeof(byte));
    }

    public ushort Read16(uint address)
    {
        if (Memory.IsMainRamAddress(address, sizeof(ushort)))
        {
            return Memory.Read16(address);
        }

        if (TryTranslateLockedCache(address, sizeof(ushort), out int lockedCacheOffset))
        {
            return BigEndian.ReadUInt16(_lockedCache.AsSpan(lockedCacheOffset, sizeof(ushort)));
        }

        return (ushort)ReadMmio(address, sizeof(ushort));
    }

    public uint Read32(uint address)
    {
        if (Memory.IsMainRamAddress(address, sizeof(uint)))
        {
            TryRepairPikminResourceRegistrationOnRead(address);
            return Memory.Read32(address);
        }

        if (TryTranslateLockedCache(address, sizeof(uint), out int lockedCacheOffset))
        {
            return BigEndian.ReadUInt32(_lockedCache.AsSpan(lockedCacheOffset, sizeof(uint)));
        }

        return ReadMmio(address, sizeof(uint));
    }

    public void Write8(uint address, byte value)
    {
        if (Memory.IsMainRamAddress(address, sizeof(byte)))
        {
            Memory.Write8(address, value);
            MainRamWriteObserver?.Invoke(address, sizeof(byte), value);
            return;
        }

        if (TryTranslateLockedCache(address, out int lockedCacheOffset))
        {
            _lockedCache[lockedCacheOffset] = value;
            return;
        }

        WriteMmio(address, sizeof(byte), value);
    }

    public void Write16(uint address, ushort value)
    {
        if (Memory.IsMainRamAddress(address, sizeof(ushort)))
        {
            Memory.Write16(address, value);
            MainRamWriteObserver?.Invoke(address, sizeof(ushort), value);
            return;
        }

        if (TryTranslateLockedCache(address, sizeof(ushort), out int lockedCacheOffset))
        {
            BigEndian.WriteUInt16(_lockedCache.AsSpan(lockedCacheOffset, sizeof(ushort)), value);
            return;
        }

        WriteMmio(address, sizeof(ushort), value);
    }

    public void Write32(uint address, uint value)
    {
        if (Memory.IsMainRamAddress(address, sizeof(uint)))
        {
            Memory.Write32(address, value);
            MainRamWriteObserver?.Invoke(address, sizeof(uint), value);
            MainRamWrite32Observer?.Invoke(address, value);
            try
            {
                RecordDspTaskCallback(value);
                TryExecuteGraphicsCommandCallback(address, value);
                TryScheduleArqRequest(address, value);
                TryScheduleArqManagerActiveRequest(address, value);
                TryAutoRegisterPikminResource(address, value);
            }
            catch (AddressTranslationException)
            {
            }

            if (value == 0 && _trivialDspTaskCallbackTargets.ContainsValue(address))
            {
                CompleteRecordedDspTaskCallbacks();
            }

            return;
        }

        if (TryTranslateLockedCache(address, sizeof(uint), out int lockedCacheOffset))
        {
            BigEndian.WriteUInt32(_lockedCache.AsSpan(lockedCacheOffset, sizeof(uint)), value);
            return;
        }

        WriteMmio(address, sizeof(uint), value);
    }

    public void ClearMmioLog()
    {
        _mmioAccesses.Clear();
    }

    public void Advance(ulong cycles)
    {
        ulong oldScanline = _videoCycles / VideoCyclesPerScanline;
        uint oldAudioSampleCounter = _audioSampleCounter;
        _videoCycles += cycles;
        _audioSampleCounter = unchecked(_audioSampleCounter + (uint)cycles);
        UpdateAudioInterfaceInterrupt(oldAudioSampleCounter, _audioSampleCounter);
        ulong newScanline = _videoCycles / VideoCyclesPerScanline;

        for (ulong scanline = oldScanline + 1; scanline <= newScanline; scanline++)
        {
            UpdateVideoInterruptsForLine((int)(scanline % VideoScanlinesPerFrame));
        }

        AdvanceDiscInterface(cycles);
        AdvanceDirectAramDma(cycles);
        AdvanceArqRequests(cycles);
        AdvanceSerialInterfacePolling(cycles);
    }

    public void RaiseProcessorInterrupt(uint interruptMask)
    {
        _processorInterruptCause |= interruptMask;
    }

    public bool TryGetMmioValue(uint address, out uint value)
    {
        return _mmioValues.TryGetValue(address, out value);
    }

    public byte ReadAram8(uint address)
    {
        return _aram[TranslateAram(address)];
    }

    public void WriteAram8(uint address, byte value)
    {
        _aram[TranslateAram(address)] = value;
    }

    public static string? TryGetHardwareRegisterBlockName(uint address)
    {
        foreach ((uint start, uint end, string name) in HardwareRegisterBlocks)
        {
            if (address >= start && address <= end)
            {
                return name;
            }
        }

        return null;
    }

    private uint ReadMmio(uint address, int width)
    {
        string deviceName = TryGetHardwareRegisterBlockName(address) ?? throw new AddressTranslationException(address);
        uint alignedAddress = AlignAddress(address, width);
        _mmioValues.TryGetValue(alignedAddress, out uint value);
        uint maskedValue = NormalizeMmioRead(alignedAddress, MaskToWidth(value, width), width, deviceName);
        Log(MmioAccessKind.Read, address, width, maskedValue, deviceName);
        return maskedValue;
    }

    private void WriteMmio(uint address, int width, uint value)
    {
        string deviceName = TryGetHardwareRegisterBlockName(address) ?? throw new AddressTranslationException(address);
        uint alignedAddress = AlignAddress(address, width);
        uint maskedValue = NormalizeMmioWrite(alignedAddress, MaskToWidth(value, width), width, deviceName);
        _mmioValues[alignedAddress] = maskedValue;
        Log(MmioAccessKind.Write, address, width, maskedValue, deviceName);
    }

    private uint NormalizeMmioWrite(uint alignedAddress, uint value, int width, string deviceName)
    {
        if (deviceName == "PI" && alignedAddress == 0xCC00_3000)
        {
            _processorInterruptCause &= ~value;
            return _processorInterruptCause;
        }

        if (deviceName == "PI" && alignedAddress == 0xCC00_3004)
        {
            _processorInterruptMask = value;
            return _processorInterruptMask;
        }

        if (deviceName == "VI" && IsVideoInterruptRegister(alignedAddress))
        {
            if ((value & VideoInterruptStatus) == 0)
            {
                ClearVideoInterruptIfAcknowledged(alignedAddress, value);
            }

            return value;
        }

        if (deviceName == "DI")
        {
            return NormalizeDiscInterfaceWrite(alignedAddress, value);
        }

        if (deviceName == "SI")
        {
            return NormalizeSerialInterfaceWrite(alignedAddress, value);
        }

        if (deviceName == "EXI")
        {
            return NormalizeExternalInterfaceWrite(alignedAddress, value);
        }

        if (deviceName == "AI")
        {
            return NormalizeDspInterfaceWrite(alignedAddress, value, width);
        }

        if (deviceName == "Streaming")
        {
            return NormalizeAudioInterfaceWrite(alignedAddress, value);
        }

        if (deviceName == "PE")
        {
            return NormalizePixelEngineWrite(alignedAddress, value);
        }

        if (deviceName == "GX FIFO")
        {
            ObserveGxFifoWrite(value, width);
        }

        return value;
    }

    private uint NormalizeMmioRead(uint alignedAddress, uint value, int width, string deviceName)
    {
        if (deviceName == "PI" && alignedAddress == 0xCC00_3000)
        {
            RefreshDspProcessorInterrupt();
            uint cause = _processorInterruptCause | ProcessorInterfaceResetSwitchReleased;
            if (_dspMailboxInterruptPending)
            {
                _dspMailboxInterruptPending = false;
                ClearDspInterrupt(DspControlDspInterruptStatus);
            }

            return cause;
        }

        if (deviceName == "PI" && alignedAddress == 0xCC00_3004)
        {
            return _processorInterruptMask;
        }

        if (deviceName == "VI" && alignedAddress == 0xCC00_206C)
        {
            return (uint)CurrentVideoLine;
        }

        if (deviceName == "AI" && alignedAddress == 0xCC00_5004)
        {
            return PeekDspMailboxOutHigh();
        }

        if (deviceName == "AI" && alignedAddress == 0xCC00_5006)
        {
            return ReadDspMailboxOutLow();
        }

        if (deviceName == "AI" && alignedAddress == 0xCC00_5000)
        {
            return value & ~0x8000u;
        }

        if (deviceName == "AI" && alignedAddress == 0xCC00_500A)
        {
            return value;
        }

        if (deviceName == "AI" && alignedAddress == 0xCC00_5016)
        {
            return value | 1u;
        }

        if (deviceName == "AI" && width == sizeof(uint) && IsAramDmaRegisterPair(alignedAddress))
        {
            return ReadAramDmaRegisterPair(alignedAddress);
        }

        if (deviceName == "SI")
        {
            return NormalizeSerialInterfaceRead(alignedAddress, value);
        }

        if (deviceName == "DI")
        {
            return NormalizeDiscInterfaceRead(alignedAddress, value);
        }

        if (deviceName == "EXI")
        {
            return NormalizeExternalInterfaceRead(alignedAddress, value);
        }

        if (deviceName == "Streaming")
        {
            return NormalizeAudioInterfaceRead(alignedAddress, value);
        }

        return value;
    }

    private uint NormalizeDspInterfaceWrite(uint alignedAddress, uint value, int width)
    {
        if (IsAramDmaRegister(alignedAddress))
        {
            return WriteAramDmaRegister(alignedAddress, value, width);
        }

        if (alignedAddress == 0xCC00_5036)
        {
            return NormalizeDspAudioDmaControlWrite(value);
        }

        if (alignedAddress == 0xCC00_5000 && (value & 0x8000) != 0)
        {
            WriteDspMailboxInHigh((ushort)value);
            return value;
        }

        if (alignedAddress == 0xCC00_5002)
        {
            WriteDspMailboxInLow((ushort)value);
            return value;
        }

        if (alignedAddress == 0xCC00_500A)
        {
            _mmioValues.TryGetValue(alignedAddress, out uint existing);
            uint nextValue = (existing | value) & ~1u;
            nextValue &= ~(value & DspControlInterruptStatusMask);
            _mmioValues[alignedAddress] = nextValue;
            UpdateDspProcessorInterrupt(nextValue);

            return nextValue;
        }
        return value;
    }

    private uint WriteAramDmaRegister(uint alignedAddress, uint value, int width)
    {
        if (width == sizeof(uint) && IsAramDmaRegisterPair(alignedAddress))
        {
            uint high = (value >> 16) & 0xFFFF;
            uint low = value & 0xFFFF;
            if (alignedAddress is 0xCC00_5020 or 0xCC00_5024 or 0xCC00_5028)
            {
                low &= ~0x1Fu;
            }

            _mmioValues[alignedAddress + 2] = low;
            if (alignedAddress == 0xCC00_5028)
            {
                _mmioValues[alignedAddress] = high;
                ExecuteAramDma();
            }

            return high;
        }

        uint normalizedValue = value & 0xFFFF;
        if (alignedAddress is 0xCC00_5022 or 0xCC00_5026 or 0xCC00_502A)
        {
            normalizedValue &= ~0x1Fu;
        }

        if (alignedAddress == 0xCC00_502A)
        {
            _mmioValues[alignedAddress] = normalizedValue;
            ExecuteAramDma();
            _mmioValues.TryGetValue(alignedAddress, out uint postDmaValue);
            return postDmaValue;
        }

        return normalizedValue;
    }

    private uint NormalizeDspAudioDmaControlWrite(uint value)
    {
        uint normalizedValue = value & 0xFFFF;
        if ((normalizedValue & 0x8000) == 0)
        {
            return normalizedValue;
        }

        _mmioValues[0xCC00_503A] = 0;
        RaiseDspInterrupt(DspControlAudioDmaInterruptStatus);
        return normalizedValue & ~0x8000u;
    }

    private void WriteDspMailboxInHigh(ushort high)
    {
        _dspMailboxInHigh = high;
        _dspMailboxInHighValid = true;
        TryAcceptDspMailboxIn();
    }

    private void WriteDspMailboxInLow(ushort low)
    {
        _dspMailboxInLow = low;
        _dspMailboxInLowValid = true;
        TryAcceptDspMailboxIn();
    }

    private void TryAcceptDspMailboxIn()
    {
        if (!_dspMailboxInHighValid || !_dspMailboxInLowValid)
        {
            return;
        }

        uint message = ((uint)_dspMailboxInHigh << 16) | _dspMailboxInLow;
        _dspMailboxInHighValid = false;
        _dspMailboxInLowValid = false;

        if (SelectDspMailboxResponse(message) is uint response)
        {
            QueueDspMailboxResponse(response, raiseInterrupt: true);
        }
    }

    private static uint? SelectDspMailboxResponse(uint message)
    {
        uint command = message & 0xFFFF_0000;
        return command switch
        {
            0xDCD1_0000 => 0xDCD1_0001,
            0xDCD1_0001 => 0xDCD1_0002,
            0xDCD1_0002 => 0xDCD1_0003,
            0x80F3_0000 => null,
            _ => message,
        };
    }

    private void QueueDspMailboxResponse(uint message, bool raiseInterrupt)
    {
        _dspMailboxOut.Enqueue(message);
        if (raiseInterrupt)
        {
            _dspMailboxInterruptPending = true;
            RaiseDspInterrupt(DspControlDspInterruptStatus);
        }
    }

    private void QueueDspBootGreeting()
    {
        QueueDspMailboxResponse(0x8071_FEED, raiseInterrupt: false);
    }

    private uint PeekDspMailboxOutHigh()
    {
        if (!_dspMailboxOut.TryPeek(out uint message))
        {
            return 0;
        }

        return DspMailboxFull | ((message >> 16) & 0xFFFF);
    }

    private uint ReadDspMailboxOutLow()
    {
        if (!_dspMailboxOut.TryDequeue(out uint message))
        {
            return 0;
        }

        if (_dspMailboxInterruptPending)
        {
            _dspMailboxInterruptPending = false;
            ClearDspInterrupt(DspControlDspInterruptStatus);
        }

        return message & 0xFFFF;
    }

    private void ExecuteAramDma()
    {
        if (_pendingDirectAramDma is not null)
        {
            return;
        }

        uint mainAddress = ReadAramDmaRegisterPair(0xCC00_5020);
        uint aramAddress = ReadAramDmaRegisterPair(0xCC00_5024);
        uint control = ReadAramDmaRegisterPair(0xCC00_5028);

        uint length = control & 0x7FFF_FFE0;
        if (length != 0)
        {
            bool aramToMain = (control & 0x8000_0000) != 0;
            _pendingDirectAramDma = new PendingDirectAramDma(
                mainAddress,
                aramAddress,
                length,
                aramToMain,
                DirectAramDmaLatency(length));
            return;
        }

        if (_dspMailboxOut.Count == 0)
        {
            if (length == 0)
            {
                QueueDspBootGreeting();
            }
            else
            {
                QueueDspMailboxResponse(0xC0FF_0001, raiseInterrupt: false);
            }
        }

        CompleteRecordedDspTaskCallbacks();
        RaiseDspInterrupt(DspControlAramInterruptStatus);
    }

    private static ulong DirectAramDmaLatency(uint length) =>
        DirectAramDmaBaseLatencyCycles + Math.Max(1u, length / 2u);

    private void AdvanceDirectAramDma(ulong cycles)
    {
        if (_pendingDirectAramDma is not PendingDirectAramDma dma)
        {
            return;
        }

        if (cycles < dma.RemainingCycles)
        {
            _pendingDirectAramDma = dma with { RemainingCycles = dma.RemainingCycles - cycles };
            return;
        }

        _pendingDirectAramDma = null;
        CompleteDirectAramDma(dma);
    }

    private void CompleteDirectAramDma(PendingDirectAramDma dma)
    {
        CopyAramDma(dma.MainAddress, dma.AramAddress, dma.Length, dma.AramToMain);
        WriteAramDmaRegisterPair(0xCC00_5020, unchecked(dma.MainAddress + dma.Length));
        WriteAramDmaRegisterPair(0xCC00_5024, unchecked(dma.AramAddress + dma.Length));
        WriteAramDmaRegisterPair(0xCC00_5028, dma.AramToMain ? 0x8000_0000u : 0u);

        if (_dspMailboxOut.Count == 0)
        {
            QueueDspMailboxResponse(0xC0FF_0001, raiseInterrupt: false);
        }

        CompleteRecordedDspTaskCallbacks();
        RaiseDspInterrupt(DspControlAramInterruptStatus);
    }

    private void CompleteRecordedDspTaskCallbacks()
    {
        foreach (uint callbackAddress in _trivialDspTaskCallbackTargets.Keys.ToArray())
        {
            TryExecuteTrivialDspTaskCallback(callbackAddress);
        }

        foreach (SlotClearDspTaskCallback callback in _slotClearDspTaskCallbacks.Values)
        {
            TryExecuteSlotClearDspTaskCallback(callback);
        }

        foreach (uint targetAddress in _decrementWordDspTaskCallbackTargets.Values)
        {
            TryExecuteDecrementWordDspTaskCallback(targetAddress);
        }

        foreach ((uint callbackAddress, uint targetAddress) in _byteSetDspTaskCallbackTargets)
        {
            TryExecuteByteSetDspTaskCallback(callbackAddress, targetAddress);
        }

        foreach (GlobalWordSetDspTaskCallback callback in _globalWordSetDspTaskCallbacks.Values)
        {
            TryExecuteGlobalWordSetDspTaskCallback(callback);
        }
    }

    private void RecordDspTaskCallback(uint callbackAddress)
    {
        RecordTrivialDspTaskCallback(callbackAddress);
        RecordDecrementWordDspTaskCallback(callbackAddress);
        RecordByteSetDspTaskCallback(callbackAddress);
        RecordGlobalWordSetDspTaskCallback(callbackAddress);
        RecordSlotClearDspTaskCallback(callbackAddress);
    }

    private void RecordTrivialDspTaskCallback(uint callbackAddress)
    {
        if (SmallDataBaseRegister == 0
            || callbackAddress == 0
            || _trivialDspTaskCallbackTargets.ContainsKey(callbackAddress)
            || !TryGetTrivialDspTaskCallbackTarget(callbackAddress, out uint targetAddress))
        {
            return;
        }

        _trivialDspTaskCallbackTargets[callbackAddress] = targetAddress;
    }

    private void RecordDecrementWordDspTaskCallback(uint callbackAddress)
    {
        if (SmallDataBaseRegister == 0
            || callbackAddress == 0
            || _decrementWordDspTaskCallbackTargets.ContainsKey(callbackAddress)
            || !TryGetDecrementWordDspTaskCallbackTarget(callbackAddress, out uint targetAddress))
        {
            return;
        }

        _decrementWordDspTaskCallbackTargets[callbackAddress] = targetAddress;
    }

    private void RecordGlobalWordSetDspTaskCallback(uint callbackAddress)
    {
        if (SmallDataBaseRegister == 0
            || callbackAddress == 0
            || _globalWordSetDspTaskCallbacks.ContainsKey(callbackAddress)
            || !TryGetGlobalWordSetDspTaskCallback(callbackAddress, out GlobalWordSetDspTaskCallback callback))
        {
            return;
        }

        _globalWordSetDspTaskCallbacks[callbackAddress] = callback;
    }

    private void RecordByteSetDspTaskCallback(uint callbackAddress)
    {
        if (SmallDataBaseRegister == 0
            || callbackAddress == 0
            || _byteSetDspTaskCallbackTargets.ContainsKey(callbackAddress)
            || !TryGetByteSetDspTaskCallbackTarget(callbackAddress, out uint targetAddress))
        {
            return;
        }

        _byteSetDspTaskCallbackTargets[callbackAddress] = targetAddress;
    }

    private void RecordSlotClearDspTaskCallback(uint callbackAddress)
    {
        if (SmallDataBaseRegister == 0
            || callbackAddress == 0
            || _slotClearDspTaskCallbacks.ContainsKey(callbackAddress)
            || !TryGetSlotClearDspTaskCallback(callbackAddress, out SlotClearDspTaskCallback callback))
        {
            return;
        }

        _slotClearDspTaskCallbacks[callbackAddress] = callback;
    }

    private void TryExecuteTrivialDspTaskCallback(uint callbackAddress)
    {
        if (callbackAddress == 0 || _completedDspTaskCallbacks.Contains(callbackAddress))
        {
            return;
        }

        if (!_trivialDspTaskCallbackTargets.TryGetValue(callbackAddress, out uint targetAddress)
            && !TryGetTrivialDspTaskCallbackTarget(callbackAddress, out targetAddress))
        {
            return;
        }

        Memory.Write32(targetAddress, 1);
        _completedDspTaskCallbacks.Add(callbackAddress);
    }

    private void TryExecuteDecrementWordDspTaskCallback(uint targetAddress)
    {
        if (!GameCubeAddress.TryTranslateMainRam(targetAddress, out _))
        {
            return;
        }

        uint value = Memory.Read32(targetAddress);
        if (value != 0)
        {
            Memory.Write32(targetAddress, value - 1);
        }
    }

    private void TryExecuteByteSetDspTaskCallback(uint callbackAddress, uint targetAddress)
    {
        if (callbackAddress == 0 || _completedDspTaskCallbacks.Contains(callbackAddress))
        {
            return;
        }

        if (GameCubeAddress.TryTranslateMainRam(targetAddress, out _))
        {
            Memory.Write8(targetAddress, 1);
            _completedDspTaskCallbacks.Add(callbackAddress);
        }
    }

    private void TryExecuteGlobalWordSetDspTaskCallback(GlobalWordSetDspTaskCallback callback)
    {
        uint globalBase = Memory.Read32(callback.GlobalBasePointerAddress);
        uint targetAddress = unchecked(globalBase + (uint)callback.TargetOffset);
        if (GameCubeAddress.TryTranslateMainRam(targetAddress, out _))
        {
            Memory.Write32(targetAddress, callback.Value);
        }
    }

    private void TryAutoRegisterPikminResource(uint address, uint value)
    {
        if (SmallDataBaseRegister == 0 || value != PikminResourceId || address < 0x3CC)
        {
            return;
        }

        uint resourceAddress = address - 0x3CC;
        try
        {
            if (Memory.Read32(resourceAddress + 0x3B4) != PikminResourceVTable)
            {
                return;
            }

            RegisterPikminResource(GetPikminRegistrationAddress(resourceAddress), value);
            _pikminResourceAddress = resourceAddress;
        }
        catch (AddressTranslationException)
        {
        }
    }

    private void TryRepairPikminResourceRegistrationOnRead(uint address)
    {
        if (_pikminResourceAddress == 0 || SmallDataBaseRegister == 0 || address != SmallDataBaseRegister + 0x2C2C)
        {
            return;
        }

        try
        {
            if ((Memory.Read32(address) == 0 || HasPikminPlaceholderResourceRegistration())
                && Memory.Read32(_pikminResourceAddress + 0x3B4) == PikminResourceVTable
                && Memory.Read32(_pikminResourceAddress + 0x3CC) == PikminResourceId)
            {
                RegisterPikminResource(GetPikminRegistrationAddress(_pikminResourceAddress), PikminResourceId);
            }
        }
        catch (AddressTranslationException)
        {
        }
    }

    private uint GetPikminRegistrationAddress(uint resourceAddress)
    {
        if (HasInvalidPikminResourcePointerSlots(resourceAddress))
        {
            PreparePikminPlaceholderResource(resourceAddress);
            return PikminPlaceholderResourceAddress;
        }

        return resourceAddress;
    }

    private bool HasInvalidPikminResourcePointerSlots(uint resourceAddress)
    {
        for (int index = 0; index < PikminResourcePointerListCount; index++)
        {
            uint slotAddress = resourceAddress + PikminResourcePointerListOffset + (uint)(index * sizeof(uint));
            uint slotValue = Memory.Read32(slotAddress);
            if (slotValue != 0 && (!GameCubeAddress.TryTranslateMainRam(slotValue, out _) || (slotValue & 0x3) != 0))
            {
                return true;
            }
        }

        return false;
    }

    private void PreparePikminPlaceholderResource(uint sourceResourceAddress)
    {
        for (uint offset = 0; offset < 0x400; offset += sizeof(uint))
        {
            uint value = Memory.Read32(sourceResourceAddress + offset);
            Memory.Write32(PikminPlaceholderResourceAddress + offset, value);
        }

        for (int index = 0; index < PikminResourcePointerListCount; index++)
        {
            uint slotOffset = PikminResourcePointerListOffset + (uint)(index * sizeof(uint));
            uint slotValue = Memory.Read32(PikminPlaceholderResourceAddress + slotOffset);
            if (slotValue != 0 && (!GameCubeAddress.TryTranslateMainRam(slotValue, out _) || (slotValue & 0x3) != 0))
            {
                Memory.Write32(PikminPlaceholderResourceAddress + slotOffset, 0);
            }
        }

        Memory.Write32(PikminPlaceholderResourceAddress + 0x3B4, PikminResourceVTable);
        Memory.Write32(PikminPlaceholderResourceAddress + 0x3CC, PikminResourceId);
    }

    private void RegisterPikminResource(uint resourceAddress, uint resourceId)
    {
        uint countAddress = SmallDataBaseRegister + 0x2C2C;
        uint tableAddress = 0x8031_F8E0;
        uint count = Memory.Read32(countAddress);
        uint firstFreeSlot = uint.MaxValue;

        for (uint slot = 0; slot < 32; slot++)
        {
            uint entryAddress = tableAddress + slot * 8;
            uint existingResourceAddress = Memory.Read32(entryAddress);
            uint existingResourceId = Memory.Read32(entryAddress + 4);
            if (existingResourceAddress != 0 && existingResourceId == resourceId)
            {
                if (existingResourceAddress == PikminPlaceholderResourceAddress
                    && resourceAddress != PikminPlaceholderResourceAddress
                    && !HasInvalidPikminResourcePointerSlots(resourceAddress))
                {
                    Memory.Write32(entryAddress, resourceAddress);
                    Memory.Write8(resourceAddress + 0x3E2, 1);
                }

                if (count <= slot)
                {
                    Memory.Write32(countAddress, Math.Min(slot + 1, 32));
                }

                return;
            }

            if (firstFreeSlot == uint.MaxValue && existingResourceAddress == 0)
            {
                firstFreeSlot = slot;
            }
        }

        if (firstFreeSlot == uint.MaxValue)
        {
            return;
        }

        uint targetSlot = count < 32 ? count : firstFreeSlot;
        uint targetAddress = tableAddress + targetSlot * 8;
        Memory.Write32(targetAddress, resourceAddress);
        Memory.Write32(targetAddress + 4, resourceId);
        Memory.Write32(countAddress, Math.Min(count + 1, 32));
        Memory.Write8(resourceAddress + 0x3E2, 1);
    }

    private bool HasPikminPlaceholderResourceRegistration()
    {
        uint tableAddress = 0x8031_F8E0;
        for (uint slot = 0; slot < 32; slot++)
        {
            uint entryAddress = tableAddress + slot * 8;
            if (Memory.Read32(entryAddress) == PikminPlaceholderResourceAddress
                && Memory.Read32(entryAddress + 4) == PikminResourceId)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetTrivialDspTaskCallbackTarget(uint callbackAddress, out uint targetAddress)
    {
        targetAddress = 0;
        if (!GameCubeAddress.TryTranslateMainRam(callbackAddress, out _))
        {
            return false;
        }

        uint loadOne = Memory.Read32(callbackAddress);
        uint store = Memory.Read32(callbackAddress + 4);
        uint ret = Memory.Read32(callbackAddress + 8);

        if (loadOne != 0x3800_0001 || ret != 0x4E80_0020)
        {
            return false;
        }

        int opcode = (int)(store >> 26);
        int rS = (int)((store >> 21) & 0x1F);
        int rA = (int)((store >> 16) & 0x1F);
        if (opcode != 36 || rS != 0 || rA != 13)
        {
            return false;
        }

        targetAddress = unchecked(SmallDataBaseRegister + (uint)(short)(store & 0xFFFF));
        return GameCubeAddress.TryTranslateMainRam(targetAddress, out _);
    }

    private bool TryGetDecrementWordDspTaskCallbackTarget(uint callbackAddress, out uint targetAddress)
    {
        targetAddress = 0;
        if (!GameCubeAddress.TryTranslateMainRam(callbackAddress, out _))
        {
            return false;
        }

        uint load = Memory.Read32(callbackAddress);
        uint decrement = Memory.Read32(callbackAddress + 4);
        uint store = Memory.Read32(callbackAddress + 8);
        uint ret = Memory.Read32(callbackAddress + 12);

        if (!IsLwz(load, rD: 3, rA: 13)
            || !IsAddi(decrement, rD: 0, rA: 3)
            || (short)(decrement & 0xFFFF) != -1
            || !IsStw(store, rS: 0, rA: 13)
            || (load & 0xFFFF) != (store & 0xFFFF)
            || ret != 0x4E80_0020)
        {
            return false;
        }

        targetAddress = unchecked(SmallDataBaseRegister + (uint)(short)(load & 0xFFFF));
        return GameCubeAddress.TryTranslateMainRam(targetAddress, out _);
    }

    private bool TryGetByteSetDspTaskCallbackTarget(uint callbackAddress, out uint targetAddress)
    {
        targetAddress = 0;
        if (!GameCubeAddress.TryTranslateMainRam(callbackAddress, out _))
        {
            return false;
        }

        for (uint offset = 0; offset <= 0x100; offset += sizeof(uint))
        {
            uint branch = Memory.Read32(callbackAddress + offset);
            if (!TryGetRelativeBranchTarget(branch, callbackAddress + offset, out uint setterAddress)
                || !GameCubeAddress.TryTranslateMainRam(setterAddress, out _))
            {
                continue;
            }

            uint loadOne = Memory.Read32(setterAddress);
            uint storeByte = Memory.Read32(setterAddress + 4);
            uint ret = Memory.Read32(setterAddress + 8);
            if (!IsAddi(loadOne, rD: 0, rA: 0)
                || (short)(loadOne & 0xFFFF) != 1
                || !IsStb(storeByte, rS: 0, rA: 13)
                || ret != 0x4E80_0020)
            {
                continue;
            }

            targetAddress = unchecked(SmallDataBaseRegister + (uint)(short)(storeByte & 0xFFFF));
            return GameCubeAddress.TryTranslateMainRam(targetAddress, out _);
        }

        return false;
    }

    private bool TryGetGlobalWordSetDspTaskCallback(uint callbackAddress, out GlobalWordSetDspTaskCallback callback)
    {
        callback = default;
        if (!GameCubeAddress.TryTranslateMainRam(callbackAddress, out _))
        {
            return false;
        }

        for (uint loadValueOffset = 0; loadValueOffset <= 0x80; loadValueOffset += sizeof(uint))
        {
            uint loadValue = Memory.Read32(callbackAddress + loadValueOffset);
            if (!IsAddi(loadValue, rD: 0, rA: 0))
            {
                continue;
            }

            uint value = (uint)(short)(loadValue & 0xFFFF);
            for (uint loadGlobalOffset = loadValueOffset + sizeof(uint); loadGlobalOffset <= 0x80; loadGlobalOffset += sizeof(uint))
            {
                uint loadGlobal = Memory.Read32(callbackAddress + loadGlobalOffset);
                if (!IsLwzWithBase(loadGlobal, rA: 13))
                {
                    continue;
                }

                int globalRegister = (int)((loadGlobal >> 21) & 0x1F);
                uint globalBasePointerAddress = unchecked(SmallDataBaseRegister + (uint)(short)(loadGlobal & 0xFFFF));
                if (!GameCubeAddress.TryTranslateMainRam(globalBasePointerAddress, out _))
                {
                    continue;
                }

                for (uint storeOffset = loadGlobalOffset + sizeof(uint); storeOffset <= 0x90; storeOffset += sizeof(uint))
                {
                    uint store = Memory.Read32(callbackAddress + storeOffset);
                    if (!IsStw(store, rS: 0, rA: globalRegister))
                    {
                        continue;
                    }

                    callback = new GlobalWordSetDspTaskCallback(callbackAddress, globalBasePointerAddress, (short)(store & 0xFFFF), value);
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryGetSlotClearDspTaskCallback(uint callbackAddress, out SlotClearDspTaskCallback callback)
    {
        callback = default;
        if (!GameCubeAddress.TryTranslateMainRam(callbackAddress, out _))
        {
            return false;
        }

        uint loadGlobalBase = Memory.Read32(callbackAddress + 0x18);
        uint slotBase = Memory.Read32(callbackAddress + 0x20);
        if (!IsLwz(loadGlobalBase, rD: 5, rA: 13) || !IsAddi(slotBase, rD: 0, rA: 4))
        {
            return false;
        }

        for (uint offset = 0; offset <= 0x120; offset += sizeof(uint))
        {
            uint addSlotIndexToGlobalBase = Memory.Read32(callbackAddress + offset);
            uint loadZero = Memory.Read32(callbackAddress + offset + 4);
            uint storeFlag = Memory.Read32(callbackAddress + offset + 8);

            if (addSlotIndexToGlobalBase != 0x7C65_3214 || loadZero != 0x3800_0000 || !IsStb(storeFlag, rS: 0, rA: 3))
            {
                continue;
            }

            int globalBasePointerDisplacement = (short)(loadGlobalBase & 0xFFFF);
            ushort flagOffset = (ushort)(storeFlag & 0xFFFF);
            callback = new SlotClearDspTaskCallback(callbackAddress, unchecked(SmallDataBaseRegister + (uint)globalBasePointerDisplacement), flagOffset, Slots: 8);
            return true;
        }

        return false;
    }

    private void TryExecuteSlotClearDspTaskCallback(SlotClearDspTaskCallback callback)
    {
        uint globalBase = Memory.Read32(callback.GlobalBasePointerAddress);
        if (!GameCubeAddress.TryTranslateMainRam(globalBase, out _))
        {
            return;
        }

        for (uint slot = 0; slot < callback.Slots; slot++)
        {
            uint flagAddress = globalBase + callback.FlagOffset + slot;
            if (GameCubeAddress.TryTranslateMainRam(flagAddress, out _))
            {
                Memory.Write8(flagAddress, 0);
            }
        }
    }

    private void TryExecuteGraphicsCommandCallback(uint callbackSlotAddress, uint callbackAddress)
    {
        const uint callbackFieldOffset = 0x1C;
        const uint commandArrayOffset = 0x4858;
        const uint commandStride = 0x20;
        const uint commandSlots = 8;
        const uint completionFlagArrayOffset = 0x5058;

        if (SmallDataBaseRegister == 0
            || callbackAddress == 0
            || callbackSlotAddress < callbackFieldOffset
            || !GameCubeAddress.TryTranslateMainRam(callbackAddress, out _))
        {
            return;
        }

        uint loadGlobalBase = Memory.Read32(callbackAddress + 0x18);
        if (!IsLwz(loadGlobalBase, rD: 5, rA: 13))
        {
            return;
        }

        uint globalBasePointerAddress = unchecked(SmallDataBaseRegister + (uint)(short)(loadGlobalBase & 0xFFFF));
        if (!GameCubeAddress.TryTranslateMainRam(globalBasePointerAddress, out _))
        {
            return;
        }

        uint globalBase = Memory.Read32(globalBasePointerAddress);
        uint commandAddress = callbackSlotAddress - callbackFieldOffset;
        uint commandArrayAddress = globalBase + commandArrayOffset;
        uint commandOffset = commandAddress - commandArrayAddress;
        if (!GameCubeAddress.TryTranslateMainRam(globalBase, out _)
            || commandAddress < commandArrayAddress
            || commandOffset >= commandStride * commandSlots
            || commandOffset % commandStride != 0)
        {
            return;
        }

        uint slot = commandOffset / commandStride;
        uint flagAddress = globalBase + completionFlagArrayOffset + slot;
        if (GameCubeAddress.TryTranslateMainRam(flagAddress, out _))
        {
            Memory.Write8(flagAddress, 0);
        }
    }

    private void TryScheduleArqRequest(uint callbackFieldAddress, uint callbackAddress)
    {
        if (callbackAddress == 0
            || !GameCubeAddress.TryTranslateMainRam(callbackAddress, out _)
            || !IsRecordedDspTaskCallback(callbackAddress))
        {
            return;
        }

        if (!TryReadArqDescriptorForCallbackField(callbackFieldAddress, out ArqDescriptor descriptor))
        {
            return;
        }

        if (descriptor.IsLinkedQueueDescriptor)
        {
            return;
        }

        ScheduleArqRequest(descriptor);
    }

    private void TryScheduleArqManagerActiveRequest(uint activeDescriptorAddress, uint descriptorAddress)
    {
        if (SmallDataBaseRegister == 0 || descriptorAddress == 0)
        {
            return;
        }

        uint primaryActiveAddress = SmallDataBaseRegister + 0x33E8;
        uint secondaryActiveAddress = SmallDataBaseRegister + 0x33EC;
        if (activeDescriptorAddress != primaryActiveAddress && activeDescriptorAddress != secondaryActiveAddress)
        {
            return;
        }

        if (TryReadLinkedArqDescriptor(descriptorAddress, out ArqDescriptor descriptor))
        {
            ScheduleArqRequest(descriptor);
        }
    }

    private bool TryReadArqDescriptorForCallbackField(uint callbackFieldAddress, out ArqDescriptor descriptor)
    {
        descriptor = default;
        if (callbackFieldAddress >= 0x18)
        {
            uint directAddress = callbackFieldAddress - 0x18;
            if (TryReadDirectArqDescriptor(directAddress, out descriptor))
            {
                return true;
            }
        }

        if (callbackFieldAddress >= 0x1C)
        {
            uint linkedAddress = callbackFieldAddress - 0x1C;
            return TryReadLinkedArqDescriptor(linkedAddress, out descriptor);
        }

        return false;
    }

    private bool TryReadDirectArqDescriptor(uint descriptorAddress, out ArqDescriptor descriptor)
    {
        descriptor = default;
        if (!GameCubeAddress.TryTranslateMainRam(descriptorAddress, out _)
            || Memory.Read32(descriptorAddress) != ArqRequestMagic)
        {
            return false;
        }

        descriptor = new ArqDescriptor(
            descriptorAddress,
            Memory.Read32(descriptorAddress + 0x0C),
            Memory.Read32(descriptorAddress + 0x10),
            Memory.Read32(descriptorAddress + 0x14),
            Memory.Read32(descriptorAddress + 0x18),
            AramToMain: false,
            IsLinkedQueueDescriptor: false);
        return true;
    }

    private bool TryReadLinkedArqDescriptor(uint descriptorAddress, out ArqDescriptor descriptor)
    {
        descriptor = default;
        if (!GameCubeAddress.TryTranslateMainRam(descriptorAddress, out _))
        {
            return false;
        }

        if (Memory.Read32(descriptorAddress + 4) != ArqRequestMagic)
        {
            return TryReadCallbackAnchoredArqDescriptor(descriptorAddress, out descriptor);
        }

        bool aramToMain = Memory.Read32(descriptorAddress + 8) != 0;
        uint firstAddress = Memory.Read32(descriptorAddress + 0x10);
        uint secondAddress = Memory.Read32(descriptorAddress + 0x14);
        descriptor = new ArqDescriptor(
            descriptorAddress,
            aramToMain ? secondAddress : firstAddress,
            aramToMain ? firstAddress : secondAddress,
            Memory.Read32(descriptorAddress + 0x18),
            Memory.Read32(descriptorAddress + 0x1C),
            aramToMain,
            IsLinkedQueueDescriptor: true);
        return true;
    }

    private bool TryReadCallbackAnchoredArqDescriptor(uint descriptorAddress, out ArqDescriptor descriptor)
    {
        descriptor = default;
        uint callbackAddress = Memory.Read32(descriptorAddress + 0x1C);
        uint length = Memory.Read32(descriptorAddress + 0x18);
        bool aramToMain = Memory.Read32(descriptorAddress + 8) != 0;
        uint firstAddress = Memory.Read32(descriptorAddress + 0x10);
        uint secondAddress = Memory.Read32(descriptorAddress + 0x14);
        uint mainAddress = aramToMain ? secondAddress : firstAddress;
        if (callbackAddress == 0
            || length == 0
            || !IsRecordedDspTaskCallback(callbackAddress)
            || !IsMainRamRange(mainAddress, length)
            || length > AramSize)
        {
            return false;
        }

        descriptor = new ArqDescriptor(
            descriptorAddress,
            mainAddress,
            aramToMain ? firstAddress : secondAddress,
            length,
            callbackAddress,
            aramToMain,
            IsLinkedQueueDescriptor: true);
        return true;
    }

    private void ScheduleArqRequest(ArqDescriptor descriptor)
    {
        if (descriptor.Length == 0
            || descriptor.CallbackAddress == 0
            || !IsMainRamRange(descriptor.MainAddress, descriptor.Length)
            || descriptor.Length > AramSize
            || !_pendingArqDescriptorAddresses.Add(descriptor.Address))
        {
            return;
        }

        _pendingArqRequests.Add(new PendingArqRequest(descriptor.Address, descriptor.MainAddress, descriptor.AramAddress, descriptor.Length, descriptor.CallbackAddress, descriptor.AramToMain, ArqRequestLatencyCycles));
    }

    private bool IsRecordedDspTaskCallback(uint callbackAddress) =>
        _trivialDspTaskCallbackTargets.ContainsKey(callbackAddress)
        || _decrementWordDspTaskCallbackTargets.ContainsKey(callbackAddress)
        || _byteSetDspTaskCallbackTargets.ContainsKey(callbackAddress)
        || _globalWordSetDspTaskCallbacks.ContainsKey(callbackAddress)
        || _slotClearDspTaskCallbacks.ContainsKey(callbackAddress);

    private void AdvanceArqRequests(ulong cycles)
    {
        for (int index = _pendingArqRequests.Count - 1; index >= 0; index--)
        {
            PendingArqRequest request = _pendingArqRequests[index];
            if (cycles < request.RemainingCycles)
            {
                _pendingArqRequests[index] = request with { RemainingCycles = request.RemainingCycles - cycles };
                continue;
            }

            _pendingArqRequests.RemoveAt(index);
            _pendingArqDescriptorAddresses.Remove(request.DescriptorAddress);
            CompleteArqRequest(request);
        }
    }

    private void CompleteArqRequest(PendingArqRequest request)
    {
        CopyAramDma(request.MainAddress, request.AramAddress, request.Length, request.AramToMain);

        if (_trivialDspTaskCallbackTargets.ContainsKey(request.CallbackAddress))
        {
            TryExecuteTrivialDspTaskCallback(request.CallbackAddress);
        }

        if (_decrementWordDspTaskCallbackTargets.TryGetValue(request.CallbackAddress, out uint targetAddress))
        {
            TryExecuteDecrementWordDspTaskCallback(targetAddress);
        }

        if (_byteSetDspTaskCallbackTargets.TryGetValue(request.CallbackAddress, out uint byteSetTargetAddress))
        {
            TryExecuteByteSetDspTaskCallback(request.CallbackAddress, byteSetTargetAddress);
        }

        if (_globalWordSetDspTaskCallbacks.TryGetValue(request.CallbackAddress, out GlobalWordSetDspTaskCallback globalWordSetCallback))
        {
            TryExecuteGlobalWordSetDspTaskCallback(globalWordSetCallback);
        }

        if (_slotClearDspTaskCallbacks.TryGetValue(request.CallbackAddress, out SlotClearDspTaskCallback slotClearCallback))
        {
            TryExecuteSlotClearDspTaskCallback(slotClearCallback);
        }

        TryCompleteArqManagerRequest(request);
    }

    private void TryCompleteArqManagerRequest(PendingArqRequest request)
    {
        if (SmallDataBaseRegister == 0)
        {
            return;
        }

        uint queuedHeadAddress = SmallDataBaseRegister + 0x33D8;
        uint queuedTailAddress = SmallDataBaseRegister + 0x33DC;
        uint activeDescriptorAddress = SmallDataBaseRegister + 0x33E8;
        uint activeCallbackAddress = SmallDataBaseRegister + 0x33F0;
        if (Memory.Read32(activeDescriptorAddress) != request.DescriptorAddress
            || Memory.Read32(activeCallbackAddress) != request.CallbackAddress)
        {
            return;
        }

        Memory.Write32(activeDescriptorAddress, 0);
        Memory.Write32(activeCallbackAddress, 0);

        uint nextDescriptorAddress = Memory.Read32(queuedHeadAddress);
        if (nextDescriptorAddress == 0 || !TryReadLinkedArqDescriptor(nextDescriptorAddress, out ArqDescriptor descriptor))
        {
            return;
        }

        uint followingDescriptorAddress = Memory.Read32(nextDescriptorAddress);
        Memory.Write32(activeDescriptorAddress, nextDescriptorAddress);
        Memory.Write32(activeCallbackAddress, descriptor.CallbackAddress);
        Memory.Write32(queuedHeadAddress, followingDescriptorAddress);
        if (followingDescriptorAddress == 0)
        {
            Memory.Write32(queuedTailAddress, 0);
        }

        ScheduleArqRequest(descriptor);
    }

    private uint ReadAramDmaRegisterPair(uint highAddress)
    {
        _mmioValues.TryGetValue(highAddress, out uint high);
        _mmioValues.TryGetValue(highAddress + 2, out uint low);
        return ((high & 0xFFFF) << 16) | (low & 0xFFFF);
    }

    private void WriteAramDmaRegisterPair(uint highAddress, uint value)
    {
        _mmioValues[highAddress] = (value >> 16) & 0xFFFF;
        _mmioValues[highAddress + 2] = value & 0xFFFF;
    }

    private void CopyAramDma(uint mainAddress, uint aramAddress, uint length, bool aramToMain)
    {
        int mainOffset = Memory.TranslateMainRam(mainAddress, checked((int)length));
        int aramOffset = TranslateAram(aramAddress, checked((int)length));

        ReadOnlySpan<byte> main = Memory.MainRam.Span.Slice(mainOffset, checked((int)length));
        Span<byte> aram = _aram.AsSpan(aramOffset, checked((int)length));

        if (aramToMain)
        {
            Memory.Load(mainAddress, aram);
        }
        else
        {
            main.CopyTo(aram);
        }
    }

    private void RaiseDspInterrupt(uint status)
    {
        _mmioValues.TryGetValue(0xCC00_500A, out uint control);
        control = (control | status) & ~1u;
        _mmioValues[0xCC00_500A] = control;
        UpdateDspProcessorInterrupt(control);
    }

    private void ClearDspInterrupt(uint status)
    {
        _mmioValues.TryGetValue(0xCC00_500A, out uint control);
        control &= ~status;
        _mmioValues[0xCC00_500A] = control;
        UpdateDspProcessorInterrupt(control);
    }

    private void UpdateDspProcessorInterrupt(uint control)
    {
        SetDspProcessorInterrupt(DspProcessorInterruptPending(control));
    }

    private void RefreshDspProcessorInterrupt()
    {
        _mmioValues.TryGetValue(0xCC00_500A, out uint control);
        SetDspProcessorInterrupt(DspProcessorInterruptPending(control));
    }

    private void SetDspProcessorInterrupt(bool pending)
    {
        if (pending)
        {
            _processorInterruptCause |= ProcessorInterfaceDspInterrupt;
        }
        else
        {
            _processorInterruptCause &= ~ProcessorInterfaceDspInterrupt;
        }
    }

    private static bool DspProcessorInterruptPending(uint control)
    {
        return
            ((control & DspControlDspInterruptStatus) != 0 && (control & DspControlDmaInterruptMask) != 0) ||
            ((control & DspControlAramInterruptStatus) != 0 && (control & DspControlAramInterruptMask) != 0) ||
            ((control & DspControlAudioDmaInterruptStatus) != 0 && (control & DspControlAudioDmaInterruptMask) != 0);
    }

    private uint NormalizeAudioInterfaceWrite(uint alignedAddress, uint value)
    {
        if (alignedAddress == 0xCC00_6C00)
        {
            return WriteAudioInterfaceControl(value);
        }

        return value;
    }

    private uint NormalizePixelEngineWrite(uint alignedAddress, uint value)
    {
        if (alignedAddress != 0xCC00_100A)
        {
            return value;
        }

        _mmioValues.TryGetValue(alignedAddress, out uint existing);
        uint nextValue = (existing & PixelEngineInterruptStatusMask) | (value & PixelEngineInterruptMask);
        nextValue &= ~(value & PixelEngineInterruptStatusMask);
        SetPixelEngineProcessorInterrupts(nextValue);
        return nextValue;
    }

    private void ObserveGxFifoWrite(uint value, int width)
    {
        if (width == sizeof(uint))
        {
            byte command = (byte)(value >> 24);
            if (command is 0x47 or 0x48)
            {
                RaisePixelEngineTokenInterrupt((ushort)value);
            }
        }

        for (int shift = (width - 1) * 8; shift >= 0; shift -= 8)
        {
            ObserveGxFifoByte((byte)(value >> shift));
        }
    }

    private void ObserveGxFifoByte(byte value)
    {
        ObserveGxFifoBytePattern(value);

        if (_gxFifoParserState.SkipBytes > 0)
        {
            _gxFifoParserState.SkipBytes--;
            return;
        }

        if (_gxFifoParserState.PayloadBytesRemaining > 0)
        {
            _gxFifoParserState.Payload = (_gxFifoParserState.Payload << 8) | value;
            _gxFifoParserState.PayloadBytesRemaining--;
            if (_gxFifoParserState.PayloadBytesRemaining == 0)
            {
                CompleteGxFifoPayload();
            }

            return;
        }

        switch (value)
        {
            case 0x00:
            case 0x44:
            case 0x48:
                break;
            case 0x08:
                _gxFifoParserState.SkipBytes = 5;
                break;
            case 0x10:
                _gxFifoParserState.PendingPayloadKind = GxFifoPayloadKind.XfHeader;
                _gxFifoParserState.PayloadBytesRemaining = 4;
                _gxFifoParserState.Payload = 0;
                break;
            case 0x20:
            case 0x28:
            case 0x30:
            case 0x38:
                _gxFifoParserState.SkipBytes = 4;
                break;
            case 0x40:
                _gxFifoParserState.SkipBytes = 8;
                break;
            case 0x61:
                _gxFifoParserState.PendingPayloadKind = GxFifoPayloadKind.BpRegister;
                _gxFifoParserState.PayloadBytesRemaining = 4;
                _gxFifoParserState.Payload = 0;
                break;
            default:
                _gxFifoParserState = default;
                if ((value & 0x80) != 0)
                {
                    _gxFifoParserState.SkipBytes = 2;
                }

                break;
        }
    }

    private void ObserveGxFifoBytePattern(byte value)
    {
        const ulong peFinishPattern = 0x61_45_00_00_02;

        _gxFifoRecentBytes = ((_gxFifoRecentBytes << 8) | value) & 0xFF_FF_FF_FF_FFul;
        _gxFifoRecentByteCount = Math.Min(_gxFifoRecentByteCount + 1, 5);
        if (_gxFifoRecentByteCount == 5 && _gxFifoRecentBytes == peFinishPattern)
        {
            RaisePixelEngineFinishInterrupt();
        }
    }

    private void CompleteGxFifoPayload()
    {
        uint payload = _gxFifoParserState.Payload;
        GxFifoPayloadKind kind = _gxFifoParserState.PendingPayloadKind;
        _gxFifoParserState.PendingPayloadKind = GxFifoPayloadKind.None;
        _gxFifoParserState.Payload = 0;

        if (kind == GxFifoPayloadKind.XfHeader)
        {
            uint wordCount = ((payload >> 16) & 0xFFFF) + 1;
            _gxFifoParserState.SkipBytes = checked((int)Math.Min(wordCount, int.MaxValue / 4u) * 4);
            return;
        }

        if (kind != GxFifoPayloadKind.BpRegister)
        {
            return;
        }

        uint register = payload >> 24;
        uint registerValue = payload & 0x00FF_FFFF;
        if (register == 0x45 && registerValue == 0x000002)
        {
            RaisePixelEngineFinishInterrupt();
        }
    }

    private void RaisePixelEngineTokenInterrupt(ushort token)
    {
        _mmioValues[0xCC00_100E] = token;
        RaisePixelEngineInterrupt(PixelEngineTokenStatus);
    }

    private void RaisePixelEngineFinishInterrupt()
    {
        RaisePixelEngineInterrupt(PixelEngineFinishStatus);
    }

    private void RaisePixelEngineInterrupt(ushort status)
    {
        _mmioValues.TryGetValue(0xCC00_100A, out uint existing);
        uint nextValue = existing | status;
        _mmioValues[0xCC00_100A] = nextValue;
        SetPixelEngineProcessorInterrupts(nextValue);
    }

    private void SetPixelEngineProcessorInterrupts(uint control)
    {
        SetProcessorInterrupt(ProcessorInterfacePixelEngineTokenInterrupt, (control & (PixelEngineTokenEnable | PixelEngineTokenStatus)) == (PixelEngineTokenEnable | PixelEngineTokenStatus));
        SetProcessorInterrupt(ProcessorInterfacePixelEngineFinishInterrupt, (control & (PixelEngineFinishEnable | PixelEngineFinishStatus)) == (PixelEngineFinishEnable | PixelEngineFinishStatus));
    }

    private void SetProcessorInterrupt(uint interrupt, bool pending)
    {
        if (pending)
        {
            _processorInterruptCause |= interrupt;
        }
        else
        {
            _processorInterruptCause &= ~interrupt;
        }
    }

    private uint NormalizeAudioInterfaceRead(uint alignedAddress, uint value)
    {
        return alignedAddress switch
        {
            0xCC00_6C00 => value,
            0xCC00_6C08 => _audioSampleCounter,
            _ => value,
        };
    }

    private uint WriteAudioInterfaceControl(uint value)
    {
        _mmioValues.TryGetValue(0xCC00_6C00, out uint existing);
        if ((value & AudioInterfaceSampleCounterReset) != 0)
        {
            _audioSampleCounter = 0;
        }

        uint status = existing & AudioInterfaceInterruptStatus;
        if ((value & AudioInterfaceInterruptStatus) != 0)
        {
            status = 0;
            _processorInterruptCause &= ~ProcessorInterfaceAudioInterrupt;
        }

        uint nextValue = (value & ~(AudioInterfaceSampleCounterReset | AudioInterfaceInterruptStatus)) | status;
        if ((nextValue & (AudioInterfaceInterruptMask | AudioInterfaceInterruptStatus)) == (AudioInterfaceInterruptMask | AudioInterfaceInterruptStatus))
        {
            RaiseProcessorInterrupt(ProcessorInterfaceAudioInterrupt);
        }

        return nextValue;
    }

    private void UpdateAudioInterfaceInterrupt(uint oldSampleCounter, uint newSampleCounter)
    {
        _mmioValues.TryGetValue(0xCC00_6C00, out uint control);
        if ((control & AudioInterfaceInterruptStatus) != 0
            || (control & AudioInterfaceInterruptMask) == 0
            || (control & AudioInterfaceInterruptValid) != 0)
        {
            return;
        }

        _mmioValues.TryGetValue(0xCC00_6C0C, out uint interruptSample);
        if (!SampleCounterReached(oldSampleCounter, newSampleCounter, interruptSample))
        {
            return;
        }

        control |= AudioInterfaceInterruptStatus;
        _mmioValues[0xCC00_6C00] = control;
        RaiseProcessorInterrupt(ProcessorInterfaceAudioInterrupt);
    }

    private static bool SampleCounterReached(uint oldSampleCounter, uint newSampleCounter, uint interruptSample)
    {
        if (oldSampleCounter <= newSampleCounter)
        {
            return oldSampleCounter < interruptSample && interruptSample <= newSampleCounter;
        }

        return interruptSample >= oldSampleCounter || interruptSample < newSampleCounter;
    }

    private uint NormalizeDiscInterfaceWrite(uint alignedAddress, uint value)
    {
        if (alignedAddress == 0xCC00_6000)
        {
            _mmioValues.TryGetValue(alignedAddress, out uint existing);
            uint interruptStatus = existing & DiscInterfaceInterruptStatusMask;
            interruptStatus &= ~(value & DiscInterfaceInterruptStatusMask);

            if ((value & DiscInterfaceBreak) != 0)
            {
                CancelPendingDiscInterfaceCommand();
                interruptStatus |= DiscInterfaceBreakInterruptStatus;
            }

            uint nextValue = interruptStatus | (value & DiscInterfaceInterruptMaskBits);
            RefreshDiscProcessorInterrupt(nextValue);
            return nextValue;
        }

        if (alignedAddress == 0xCC00_6004)
        {
            uint existing = DiscInterfaceCoverRegister();
            uint interruptStatus = existing & DiscInterfaceCoverInterruptStatus;
            interruptStatus &= ~(value & DiscInterfaceCoverInterruptStatus);
            uint nextValue = interruptStatus | (value & DiscInterfaceCoverInterruptMask);
            RefreshDiscProcessorInterrupt(coverStatus: nextValue);
            return nextValue;
        }

        if (alignedAddress == 0xCC00_601C)
        {
            if ((value & 1) != 0)
            {
                ScheduleDiscInterfaceCommand();
                return value | 1u;
            }

            return value & ~1u;
        }

        if (alignedAddress == 0xCC00_6024)
        {
            return DiscInterfaceConfiguration;
        }

        return value;
    }

    private uint NormalizeDiscInterfaceRead(uint alignedAddress, uint value)
    {
        if (alignedAddress == 0xCC00_6000)
        {
            return value & DiscInterfaceStatusMask;
        }

        if (alignedAddress == 0xCC00_6004)
        {
            return DiscInterfaceCoverRegister();
        }

        if (alignedAddress == 0xCC00_601C)
        {
            return _pendingDiscCommand is null ? value & ~1u : value | 1u;
        }

        if (alignedAddress == 0xCC00_6024)
        {
            return DiscInterfaceConfiguration;
        }

        return value;
    }

    private uint DiscInterfaceCoverRegister()
    {
        _mmioValues.TryGetValue(0xCC00_6004, out uint value);
        return value & (DiscInterfaceCoverInterruptMask | DiscInterfaceCoverInterruptStatus);
    }

    private void ScheduleDiscInterfaceCommand()
    {
        if (_pendingDiscCommand is not null)
        {
            CompleteDiscInterfaceCommand();
        }

        _mmioValues.TryGetValue(0xCC00_6008, out uint command0);
        _mmioValues.TryGetValue(0xCC00_600C, out uint command1);
        _mmioValues.TryGetValue(0xCC00_6010, out uint command2);
        _mmioValues.TryGetValue(0xCC00_6014, out uint dmaAddress);
        _mmioValues.TryGetValue(0xCC00_6018, out uint dmaLength);

        _pendingDiscCommand = new PendingDiscCommand(command0, command1, command2, dmaAddress, dmaLength);
        _pendingDiscCommandCycles = DiscInterfaceCommandLatencyCycles;
    }

    private void CancelPendingDiscInterfaceCommand()
    {
        _pendingDiscCommand = null;
        _pendingDiscCommandCycles = 0;
        _mmioValues[0xCC00_601C] = ReadMmioValue(0xCC00_601C) & ~1u;
    }

    private void AdvanceDiscInterface(ulong cycles)
    {
        if (_pendingDiscCommand is null)
        {
            return;
        }

        if (cycles < _pendingDiscCommandCycles)
        {
            _pendingDiscCommandCycles -= cycles;
            return;
        }

        CompleteDiscInterfaceCommand();
    }

    private void CompleteDiscInterfaceCommand()
    {
        PendingDiscCommand commandRegisters = _pendingDiscCommand ?? new PendingDiscCommand(
            ReadMmioValue(0xCC00_6008),
            ReadMmioValue(0xCC00_600C),
            ReadMmioValue(0xCC00_6010),
            ReadMmioValue(0xCC00_6014),
            ReadMmioValue(0xCC00_6018));

        _pendingDiscCommand = null;
        _pendingDiscCommandCycles = 0;
        _mmioValues[0xCC00_601C] = ReadMmioValue(0xCC00_601C) & ~1u;

        ExecuteDiscInterfaceCommand(commandRegisters);
    }

    private void ExecuteDiscInterfaceCommand(PendingDiscCommand commandRegisters)
    {
        uint command0 = commandRegisters.Command0;
        uint command1 = commandRegisters.Command1;
        uint command2 = commandRegisters.Command2;
        uint dmaAddress = commandRegisters.DmaAddress;
        uint dmaLength = commandRegisters.DmaLength;

        byte command = (byte)(command0 >> 24);
        switch (command)
        {
            case 0x12:
                WriteDiscInquiry(dmaAddress, dmaLength);
                break;
            case 0xA8:
                if ((command0 & 0xFF) == 0x40)
                {
                    ReadDiscId(dmaAddress, dmaLength);
                }
                else
                {
                    ReadDiscBytes(command1 << 2, command2 == 0 ? dmaLength : command2, dmaAddress, dmaLength);
                }

                break;
            case 0xAB:
                SeekDisc(command1 << 2);
                break;
            case 0xE0:
                _mmioValues[0xCC00_6020] = _discInterfaceLastError;
                _discInterfaceLastError = DiscInterfaceErrorOk;
                break;
            case 0xE1:
                StartDiscAudioStream(command1 << 2, command2);
                break;
            case 0xE2:
                _mmioValues[0xCC00_6020] = DiscInterfaceAudioStatus();
                break;
            case 0xE3:
                _discInterfaceMotorStopped = true;
                _discInterfaceAudioStreaming = false;
                break;
            case 0xE4:
                _discInterfaceAudioEnabled = ((command0 >> 16) & 0xFF) != 0;
                if (!_discInterfaceAudioEnabled)
                {
                    _discInterfaceAudioStreaming = false;
                }

                break;
            default:
                SignalDiscInterfaceDeviceError(DiscInterfaceErrorInvalidCommand);
                break;
        }

        SignalDiscInterfaceCommandComplete();
    }

    private void ReadDiscBytes(uint discOffset, uint commandLength, uint dmaAddress, uint dmaLength)
    {
        if (!EnsureDiscAvailable())
        {
            return;
        }

        if (!IsDiscReadRangeValid(discOffset, commandLength))
        {
            SignalDiscInterfaceDeviceError(DiscInterfaceErrorAddressOutOfRange);
            return;
        }

        if (commandLength == 0 || dmaLength == 0)
        {
            return;
        }

        int size = checked((int)Math.Min(commandLength, dmaLength));
        byte[] bytes = _disc!.ReadBytes(discOffset, size);
        Memory.Load(dmaAddress, bytes);
        _discInterfacePosition = unchecked(discOffset + (uint)size);
        _discInterfaceDiscInitialized = true;
        _discInterfaceMotorStopped = false;
        _discInterfaceLastError = DiscInterfaceErrorOk;
    }

    private void ReadDiscId(uint dmaAddress, uint dmaLength)
    {
        if (!EnsureDiscAvailable() || dmaLength == 0)
        {
            return;
        }

        int size = checked((int)Math.Min(dmaLength, 0x20));
        byte[] bytes = _disc!.ReadBytes(0, size);
        Memory.Load(dmaAddress, bytes);
        _discInterfacePosition = 0;
        _discInterfaceDiscInitialized = true;
        _discInterfaceMotorStopped = false;
        _discInterfaceLastError = DiscInterfaceErrorOk;
    }

    private void SeekDisc(uint discOffset)
    {
        if (!EnsureDiscAvailable())
        {
            return;
        }

        if (!IsDiscReadRangeValid(discOffset, 0))
        {
            SignalDiscInterfaceDeviceError(DiscInterfaceErrorAddressOutOfRange);
            return;
        }

        _discInterfacePosition = discOffset;
        _discInterfaceMotorStopped = false;
        _discInterfaceLastError = DiscInterfaceErrorOk;
    }

    private void StartDiscAudioStream(uint discOffset, uint length)
    {
        if (!_discInterfaceAudioEnabled)
        {
            SignalDiscInterfaceDeviceError(DiscInterfaceErrorInvalidField);
            return;
        }

        if (!EnsureDiscAvailable())
        {
            return;
        }

        if (!IsDiscReadRangeValid(discOffset, length))
        {
            SignalDiscInterfaceDeviceError(DiscInterfaceErrorAddressOutOfRange);
            return;
        }

        _discInterfaceAudioStreaming = length != 0;
        _discInterfacePosition = discOffset;
        _discInterfaceLastError = DiscInterfaceErrorOk;
    }

    private void WriteDiscInquiry(uint dmaAddress, uint dmaLength)
    {
        if (dmaLength == 0)
        {
            return;
        }

        byte[] inquiry = new byte[Math.Min(checked((int)dmaLength), 0x20)];
        inquiry[0] = 0x00;
        inquiry[1] = 0x00;
        inquiry[2] = 0x00;
        inquiry[3] = 0x02;
        inquiry[4] = 0x20;
        inquiry[5] = 0x01;
        inquiry[6] = 0x06;
        inquiry[7] = 0x08;
        Memory.Load(dmaAddress, inquiry);
        _discInterfaceLastError = DiscInterfaceErrorOk;
    }

    private void SignalDiscInterfaceCommandComplete()
    {
        _mmioValues.TryGetValue(0xCC00_6000, out uint status);
        _mmioValues[0xCC00_6018] = 0;
        status = (status | DiscInterfaceTransferComplete) & DiscInterfaceStatusMask;
        _mmioValues[0xCC00_6000] = status;

        RefreshDiscProcessorInterrupt(status);
    }

    private void SignalDiscInterfaceDeviceError(uint error)
    {
        _discInterfaceLastError = error;
        _mmioValues[0xCC00_6020] = error;
        _mmioValues.TryGetValue(0xCC00_6000, out uint status);
        status = (status | DiscInterfaceDeviceErrorInterruptStatus) & DiscInterfaceStatusMask;
        _mmioValues[0xCC00_6000] = status;
        RefreshDiscProcessorInterrupt(status);
    }

    private bool EnsureDiscAvailable()
    {
        if (_disc is null)
        {
            SignalDiscInterfaceDeviceError(DiscInterfaceErrorNoDisc);
            return false;
        }

        if (_discInterfaceMotorStopped)
        {
            SignalDiscInterfaceDeviceError(DiscInterfaceErrorMotorStopped);
            return false;
        }

        return true;
    }

    private bool IsDiscReadRangeValid(uint offset, uint length)
    {
        if (_disc is null)
        {
            return false;
        }

        ulong end = (ulong)offset + length;
        return end <= _disc.Info.DiscSize;
    }

    private uint DiscInterfaceAudioStatus()
    {
        uint status = _discInterfaceAudioEnabled ? 0x0000_0001u : 0u;
        if (_discInterfaceAudioStreaming)
        {
            status |= 0x0000_0002u;
        }

        if (_discInterfaceDiscInitialized)
        {
            status |= 0x0000_0004u;
        }

        return status;
    }

    private void SetDiscProcessorInterrupt(bool pending)
    {
        if (pending)
        {
            RaiseProcessorInterrupt(ProcessorInterfaceDiscInterrupt);
        }
        else
        {
            _processorInterruptCause &= ~ProcessorInterfaceDiscInterrupt;
        }
    }

    private void RefreshDiscProcessorInterrupt(uint? commandStatus = null, uint? coverStatus = null)
    {
        uint status = commandStatus ?? ReadMmioValue(0xCC00_6000);
        uint cover = coverStatus ?? DiscInterfaceCoverRegister();
        SetDiscProcessorInterrupt(DiscProcessorInterruptPending(status) || DiscCoverProcessorInterruptPending(cover));
    }

    private static bool DiscProcessorInterruptPending(uint status) =>
        ((status & DiscInterfaceDeviceErrorInterruptStatus) != 0 && (status & DiscInterfaceDeviceErrorInterruptMask) != 0) ||
        ((status & DiscInterfaceTransferComplete) != 0 && (status & DiscInterfaceInterruptMask) != 0) ||
        ((status & DiscInterfaceBreakInterruptStatus) != 0 && (status & DiscInterfaceBreakInterruptMask) != 0);

    private static bool DiscCoverProcessorInterruptPending(uint status) =>
        (status & DiscInterfaceCoverInterruptStatus) != 0 && (status & DiscInterfaceCoverInterruptMask) != 0;

    private uint ReadMmioValue(uint address)
    {
        _mmioValues.TryGetValue(address, out uint value);
        return value;
    }

    private uint NormalizeExternalInterfaceWrite(uint alignedAddress, uint value)
    {
        if (!TryGetExternalInterfaceRegister(alignedAddress, out int channel, out ExternalInterfaceRegister register))
        {
            return value;
        }

        uint baseAddress = ExternalInterfaceChannelBaseAddress(channel);
        switch (register)
        {
            case ExternalInterfaceRegister.Parameter:
                return WriteExternalInterfaceParameter(baseAddress, channel, value);
            case ExternalInterfaceRegister.DmaAddress:
                return value & ~0x1Fu;
            case ExternalInterfaceRegister.DmaLength:
                return value & ~0x1Fu;
            case ExternalInterfaceRegister.Control:
                return WriteExternalInterfaceControl(baseAddress, channel, value);
            case ExternalInterfaceRegister.Data:
                return value;
            default:
                return value;
        }
    }

    private uint NormalizeExternalInterfaceRead(uint alignedAddress, uint value)
    {
        if (!TryGetExternalInterfaceRegister(alignedAddress, out int channel, out ExternalInterfaceRegister register))
        {
            return value;
        }

        return register switch
        {
            ExternalInterfaceRegister.Parameter => value | ExternalInterfaceConnectedBit(channel),
            ExternalInterfaceRegister.DmaAddress => value & ~0x1Fu,
            ExternalInterfaceRegister.DmaLength => value & ~0x1Fu,
            ExternalInterfaceRegister.Control => value & ~1u,
            _ => value,
        };
    }

    private uint WriteExternalInterfaceParameter(uint parameterAddress, int channel, uint value)
    {
        _mmioValues.TryGetValue(parameterAddress, out uint existing);
        TryDecodeSelectedExternalInterfaceDevice(existing, out int existingDevice);

        uint status = existing & ExternalInterfaceStatusMask;
        status &= ~(value & ExternalInterfaceStatusMask);

        uint next = value & ExternalInterfaceParameterWriteMask;
        if ((existing & ExternalInterfaceRomDisable) != 0)
        {
            next |= ExternalInterfaceRomDisable;
        }

        next |= status;
        _mmioValues[parameterAddress] = next;
        TryDecodeSelectedExternalInterfaceDevice(next, out int nextDevice);
        if (existingDevice != nextDevice && IsExternalInterfaceMemoryCardDevice(channel, existingDevice))
        {
            ResetExternalInterfaceMemoryCardCommand(_externalInterfaceChannels[channel]);
        }

        UpdateExternalInterfaceProcessorInterrupt();
        return next;
    }

    private uint WriteExternalInterfaceControl(uint controlAddress, int channel, uint value)
    {
        uint control = value & ExternalInterfaceControlMask;
        if ((control & ExternalInterfaceTransferStart) == 0)
        {
            return control;
        }

        ExecuteExternalInterfaceTransfer(channel, control);
        return control & ~ExternalInterfaceTransferStart;
    }

    private void ExecuteExternalInterfaceTransfer(int channel, uint control)
    {
        uint baseAddress = ExternalInterfaceChannelBaseAddress(channel);
        uint dataAddress = baseAddress + 0x10;
        int length = ExternalInterfaceImmediateLength(control);
        ExternalInterfaceTransferKind kind = ExternalInterfaceTransferType(control);

        if ((control & ExternalInterfaceDmaTransfer) != 0)
        {
            uint dmaAddress = ReadMmioValue(baseAddress + 0x04);
            uint dmaLength = ReadMmioValue(baseAddress + 0x08) & ~0x1Fu;
            ExecuteExternalInterfaceDma(channel, dmaAddress, dmaLength, kind);
        }
        else
        {
            uint data = ReadMmioValue(dataAddress);
            uint result = ExecuteExternalInterfaceImmediate(channel, data, length, kind);
            if (kind is ExternalInterfaceTransferKind.Read or ExternalInterfaceTransferKind.ReadWrite)
            {
                _mmioValues[dataAddress] = result;
            }
        }

        _mmioValues[baseAddress + 0x0C] = control & ~ExternalInterfaceTransferStart;
        SignalExternalInterfaceTransferComplete(baseAddress);
    }

    private uint ExecuteExternalInterfaceImmediate(int channel, uint data, int length, ExternalInterfaceTransferKind kind)
    {
        if (!TryGetSelectedExternalInterfaceDevice(channel, out int device))
        {
            return 0xFFFF_FFFF;
        }

        if (kind is ExternalInterfaceTransferKind.Write or ExternalInterfaceTransferKind.ReadWrite)
        {
            WriteExternalInterfaceDevice(channel, device, data, length);
        }

        if (kind is ExternalInterfaceTransferKind.Read or ExternalInterfaceTransferKind.ReadWrite)
        {
            return ReadExternalInterfaceDevice(channel, device, length);
        }

        return data;
    }

    private void ExecuteExternalInterfaceDma(int channel, uint dmaAddress, uint length, ExternalInterfaceTransferKind kind)
    {
        if (length == 0 || !TryGetSelectedExternalInterfaceDevice(channel, out int device))
        {
            return;
        }

        if (kind == ExternalInterfaceTransferKind.Read)
        {
            byte[] bytes = ReadExternalInterfaceDeviceBytes(channel, device, length);
            Memory.Load(dmaAddress, bytes);
            return;
        }

        if (kind == ExternalInterfaceTransferKind.Write)
        {
            byte[] bytes = new byte[checked((int)Math.Min(length, int.MaxValue))];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Memory.Read8(unchecked(dmaAddress + (uint)index));
            }

            WriteExternalInterfaceDeviceBytes(channel, device, bytes);
        }
    }

    private void WriteExternalInterfaceDevice(int channel, int device, uint data, int length)
    {
        ExternalInterfaceChannelState state = _externalInterfaceChannels[channel];
        if (IsExternalInterfaceInternalDevice(channel, device))
        {
            if (state.PendingImmediateWrite)
            {
                WriteExternalInterfaceInternalWord(state.PendingWriteOffset, data, length);
                state.PendingWriteOffset = unchecked(state.PendingWriteOffset + (uint)length);
                return;
            }

            uint command = AlignExternalInterfaceImmediateData(data, length);
            state.Command = command;
            state.HasCommand = true;
            state.PendingImmediateWrite = (command & ExternalInterfaceDeviceWriteFlag) != 0;
            state.PendingWriteOffset = ExternalInterfaceInternalDataOffset(command);
            return;
        }

        if (IsExternalInterfaceMemoryCardDevice(channel, device))
        {
            WriteExternalInterfaceMemoryCardImmediate(channel, state, data, length);
            return;
        }

        state.Command = AlignExternalInterfaceImmediateData(data, length);
        state.HasCommand = true;
    }

    private uint ReadExternalInterfaceDevice(int channel, int device, int length)
    {
        ExternalInterfaceChannelState state = _externalInterfaceChannels[channel];
        uint value;
        if (IsExternalInterfaceInternalDevice(channel, device))
        {
            value = state.HasCommand
                ? ReadExternalInterfaceInternalWord(state.Command, length)
                : 0xFFFF_FFFF;
        }
        else
        {
            value = ReadExternalInterfaceMemoryCardWord(channel, state, length);
        }

        return MaskExternalInterfaceImmediateData(value, length);
    }

    private byte[] ReadExternalInterfaceDeviceBytes(int channel, int device, uint length)
    {
        int byteCount = checked((int)Math.Min(length, int.MaxValue));
        byte[] bytes = new byte[byteCount];
        ExternalInterfaceChannelState state = _externalInterfaceChannels[channel];

        if (IsExternalInterfaceInternalDevice(channel, device) && state.HasCommand)
        {
            uint offset = ExternalInterfaceInternalDataOffset(state.Command);
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = ReadExternalInterfaceInternalByte(unchecked(offset + (uint)index));
            }
        }
        else if (IsExternalInterfaceMemoryCardDevice(channel, device) && IsExternalInterfaceMemoryCardInserted(channel))
        {
            ReadExternalInterfaceMemoryCardBytes(channel, state, bytes);
        }

        return bytes;
    }

    private void WriteExternalInterfaceDeviceBytes(int channel, int device, ReadOnlySpan<byte> bytes)
    {
        ExternalInterfaceChannelState state = _externalInterfaceChannels[channel];
        if (IsExternalInterfaceMemoryCardDevice(channel, device))
        {
            WriteExternalInterfaceMemoryCardBytes(channel, state, bytes);
            return;
        }

        if (!IsExternalInterfaceInternalDevice(channel, device) || !state.HasCommand || (state.Command & ExternalInterfaceDeviceWriteFlag) == 0)
        {
            return;
        }

        uint offset = ExternalInterfaceInternalDataOffset(state.Command);
        for (int index = 0; index < bytes.Length; index++)
        {
            WriteExternalInterfaceInternalByte(unchecked(offset + (uint)index), bytes[index]);
        }
    }

    private uint ReadExternalInterfaceInternalWord(uint command, int length)
    {
        uint offset = command & ~ExternalInterfaceDeviceWriteFlag;
        if (offset == ExternalInterfaceRtcCounterOffset)
        {
            return ExternalInterfaceRtcCounter;
        }

        uint dataOffset = ExternalInterfaceInternalDataOffset(command);
        uint value = 0;
        for (int index = 0; index < length; index++)
        {
            value = (value << 8) | ReadExternalInterfaceInternalByte(unchecked(dataOffset + (uint)index));
        }

        return value << ((sizeof(uint) - length) * 8);
    }

    private void WriteExternalInterfaceInternalWord(uint offset, uint data, int length)
    {
        if (offset == ExternalInterfaceRtcCounterOffset)
        {
            ExternalInterfaceRtcCounter = data;
            return;
        }

        for (int index = 0; index < length; index++)
        {
            int shift = (sizeof(uint) - 1 - index) * 8;
            WriteExternalInterfaceInternalByte(unchecked(offset + (uint)index), (byte)(data >> shift));
        }
    }

    private byte ReadExternalInterfaceInternalByte(uint offset)
    {
        if (offset < _externalInterfaceSram.Length)
        {
            return _externalInterfaceSram[offset];
        }

        return 0;
    }

    private void WriteExternalInterfaceInternalByte(uint offset, byte value)
    {
        if (offset < _externalInterfaceSram.Length)
        {
            _externalInterfaceSram[offset] = value;
        }
    }

    private void WriteExternalInterfaceMemoryCardImmediate(int channel, ExternalInterfaceChannelState state, uint data, int length)
    {
        if (!IsExternalInterfaceMemoryCardInserted(channel))
        {
            state.MemoryCardCommand = ExternalInterfaceMemoryCardCommand.None;
            return;
        }

        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BigEndian.WriteUInt32(bytes, data);
        for (int index = 0; index < length; index++)
        {
            WriteExternalInterfaceMemoryCardCommandByte(channel, state, bytes[index]);
        }
    }

    private void WriteExternalInterfaceMemoryCardCommandByte(int channel, ExternalInterfaceChannelState state, byte value)
    {
        if (ShouldStartNewExternalInterfaceMemoryCardCommand(state))
        {
            state.MemoryCardCommandStarted = false;
        }

        if (state.MemoryCardCommandStarted
            && state.MemoryCardCommand == ExternalInterfaceMemoryCardCommand.WriteBlock
            && state.MemoryCardAddressBytesReceived >= 4)
        {
            WriteExternalInterfaceMemoryCardByte(channel, state.MemoryCardOffset, value);
            state.MemoryCardOffset++;
            state.MemoryCardDataBytesTransferred++;
            if (state.MemoryCardDataBytesTransferred >= ExternalInterfaceMemoryCardWriteBlockSize)
            {
                state.MemoryCardCommandStarted = false;
            }

            return;
        }

        if (!state.MemoryCardCommandStarted)
        {
            state.MemoryCardCommandStarted = true;
            state.MemoryCardCommandByteCount = 1;
            state.MemoryCardAddress = 0;
            state.MemoryCardAddressBytesReceived = 0;
            state.MemoryCardDataBytesTransferred = 0;
            state.MemoryCardCommand = value switch
            {
                0x00 => ExternalInterfaceMemoryCardCommand.GetDeviceId,
                0x52 => ExternalInterfaceMemoryCardCommand.ReadBlock,
                0x53 => ExternalInterfaceMemoryCardCommand.ArrayToBuffer,
                0x81 => ExternalInterfaceMemoryCardCommand.SetInterrupt,
                0x82 => ExternalInterfaceMemoryCardCommand.WriteBuffer,
                0x83 => ExternalInterfaceMemoryCardCommand.GetStatus,
                0x85 => ExternalInterfaceMemoryCardCommand.GetId,
                0x86 => ExternalInterfaceMemoryCardCommand.ReadErrorBuffer,
                0x87 => ExternalInterfaceMemoryCardCommand.WakeUp,
                0x88 => ExternalInterfaceMemoryCardCommand.Sleep,
                0x89 => ExternalInterfaceMemoryCardCommand.ClearStatus,
                0xF1 => ExternalInterfaceMemoryCardCommand.EraseSector,
                0xF2 => ExternalInterfaceMemoryCardCommand.WriteBlock,
                0xF3 => ExternalInterfaceMemoryCardCommand.ExtraByteProgram,
                0xF4 => ExternalInterfaceMemoryCardCommand.EraseCard,
                _ => ExternalInterfaceMemoryCardCommand.None,
            };

            if (state.MemoryCardCommand == ExternalInterfaceMemoryCardCommand.ClearStatus)
            {
                state.MemoryCardStatus = ExternalInterfaceMemoryCardDefaultStatus;
                state.MemoryCardCommandStarted = false;
            }

            if (state.MemoryCardCommand == ExternalInterfaceMemoryCardCommand.WakeUp)
            {
                state.MemoryCardStatus = ExternalInterfaceMemoryCardDefaultStatus;
                state.MemoryCardCommandStarted = false;
            }

            if (state.MemoryCardCommand == ExternalInterfaceMemoryCardCommand.Sleep)
            {
                state.MemoryCardStatus = ExternalInterfaceMemoryCardSleepStatus;
                state.MemoryCardCommandStarted = false;
            }

            return;
        }

        state.MemoryCardCommandByteCount++;
        if (state.MemoryCardCommand == ExternalInterfaceMemoryCardCommand.GetId && state.MemoryCardCommandByteCount >= 2)
        {
            return;
        }

        if (state.MemoryCardCommand == ExternalInterfaceMemoryCardCommand.GetStatus && state.MemoryCardCommandByteCount >= 2)
        {
            return;
        }

        if (state.MemoryCardCommand == ExternalInterfaceMemoryCardCommand.SetInterrupt)
        {
            if (state.MemoryCardCommandByteCount >= 2)
            {
                state.MemoryCardInterruptEnabled = value != 0;
                state.MemoryCardCommandStarted = false;
            }

            return;
        }

        if (state.MemoryCardCommand is ExternalInterfaceMemoryCardCommand.ArrayToBuffer
            or ExternalInterfaceMemoryCardCommand.WriteBuffer
            or ExternalInterfaceMemoryCardCommand.ExtraByteProgram)
        {
            state.MemoryCardCommandStarted = false;
            return;
        }

        if (state.MemoryCardCommand == ExternalInterfaceMemoryCardCommand.EraseCard)
        {
            if (state.MemoryCardCommandByteCount >= 3)
            {
                EraseExternalInterfaceMemoryCard(GetExternalInterfaceMemoryCard(channel));
                state.MemoryCardCommandStarted = false;
            }

            return;
        }

        int requiredAddressBytes = state.MemoryCardCommand == ExternalInterfaceMemoryCardCommand.EraseSector ? 2 : 4;
        if (state.MemoryCardAddressBytesReceived < requiredAddressBytes)
        {
            state.MemoryCardAddress = (state.MemoryCardAddress << 8) | value;
            state.MemoryCardAddressBytesReceived++;
            if (state.MemoryCardAddressBytesReceived == requiredAddressBytes)
            {
                state.MemoryCardOffset = DecodeExternalInterfaceMemoryCardOffset(state.MemoryCardCommand, state.MemoryCardAddress);
                if (state.MemoryCardCommand == ExternalInterfaceMemoryCardCommand.EraseSector)
                {
                    EraseExternalInterfaceMemoryCardSector(channel, state.MemoryCardOffset);
                    state.MemoryCardCommandStarted = false;
                }
            }
        }
    }

    private uint ReadExternalInterfaceMemoryCardWord(int channel, ExternalInterfaceChannelState state, int length)
    {
        if (!IsExternalInterfaceMemoryCardInserted(channel))
        {
            return 0xFFFF_FFFF;
        }

        uint value = 0;
        for (int index = 0; index < length; index++)
        {
            value = (value << 8) | ReadExternalInterfaceMemoryCardByte(channel, state);
        }

        return value << ((sizeof(uint) - length) * 8);
    }

    private void ReadExternalInterfaceMemoryCardBytes(int channel, ExternalInterfaceChannelState state, Span<byte> destination)
    {
        if (!IsExternalInterfaceMemoryCardInserted(channel))
        {
            destination.Fill(0xFF);
            return;
        }

        for (int index = 0; index < destination.Length; index++)
        {
            destination[index] = ReadExternalInterfaceMemoryCardByte(channel, state);
        }
    }

    private void WriteExternalInterfaceMemoryCardBytes(int channel, ExternalInterfaceChannelState state, ReadOnlySpan<byte> bytes)
    {
        if (!IsExternalInterfaceMemoryCardInserted(channel))
        {
            return;
        }

        foreach (byte value in bytes)
        {
            WriteExternalInterfaceMemoryCardCommandByte(channel, state, value);
        }
    }

    private static bool ShouldStartNewExternalInterfaceMemoryCardCommand(ExternalInterfaceChannelState state)
    {
        if (!state.MemoryCardCommandStarted)
        {
            return false;
        }

        return state.MemoryCardCommand switch
        {
            ExternalInterfaceMemoryCardCommand.GetDeviceId => state.MemoryCardDataBytesTransferred >= sizeof(uint),
            ExternalInterfaceMemoryCardCommand.GetId => state.MemoryCardDataBytesTransferred >= sizeof(ushort),
            ExternalInterfaceMemoryCardCommand.GetStatus => state.MemoryCardDataBytesTransferred >= 1,
            ExternalInterfaceMemoryCardCommand.ReadErrorBuffer => state.MemoryCardDataBytesTransferred >= 1,
            ExternalInterfaceMemoryCardCommand.ReadBlock => state.MemoryCardDataBytesTransferred >= ExternalInterfaceMemoryCardWriteBlockSize,
            ExternalInterfaceMemoryCardCommand.WriteBlock => state.MemoryCardDataBytesTransferred >= ExternalInterfaceMemoryCardWriteBlockSize,
            ExternalInterfaceMemoryCardCommand.None => true,
            _ => false,
        };
    }

    private static void ResetExternalInterfaceMemoryCardCommand(ExternalInterfaceChannelState state)
    {
        state.MemoryCardCommand = ExternalInterfaceMemoryCardCommand.None;
        state.MemoryCardCommandStarted = false;
        state.MemoryCardCommandByteCount = 0;
        state.MemoryCardAddressBytesReceived = 0;
        state.MemoryCardAddress = 0;
        state.MemoryCardOffset = 0;
        state.MemoryCardDataBytesTransferred = 0;
    }

    private byte ReadExternalInterfaceMemoryCardByte(int channel, ExternalInterfaceChannelState state)
    {
        return state.MemoryCardCommand switch
        {
            ExternalInterfaceMemoryCardCommand.GetDeviceId => ReadExternalInterfaceMemoryCardDeviceIdByte(state),
            ExternalInterfaceMemoryCardCommand.GetId => ReadExternalInterfaceMemoryCardIdByte(state),
            ExternalInterfaceMemoryCardCommand.GetStatus => ReadExternalInterfaceMemoryCardStatusByte(state),
            ExternalInterfaceMemoryCardCommand.ReadErrorBuffer => ReadExternalInterfaceMemoryCardErrorByte(state),
            ExternalInterfaceMemoryCardCommand.ReadBlock => ReadExternalInterfaceMemoryCardDataByte(channel, state),
            _ => 0xFF,
        };
    }

    private static byte ReadExternalInterfaceMemoryCardDeviceIdByte(ExternalInterfaceChannelState state)
    {
        Span<byte> id = stackalloc byte[sizeof(uint)];
        BigEndian.WriteUInt32(id, ExternalInterfaceMemoryCard251Id);
        byte value = state.MemoryCardDataBytesTransferred < id.Length
            ? id[state.MemoryCardDataBytesTransferred]
            : (byte)0xFF;
        state.MemoryCardDataBytesTransferred++;
        return value;
    }

    private static byte ReadExternalInterfaceMemoryCardErrorByte(ExternalInterfaceChannelState state)
    {
        byte value = state.MemoryCardDataBytesTransferred == 0 ? (byte)0 : (byte)0xFF;
        state.MemoryCardDataBytesTransferred++;
        return value;
    }

    private static byte ReadExternalInterfaceMemoryCardIdByte(ExternalInterfaceChannelState state)
    {
        Span<byte> id = stackalloc byte[sizeof(uint)];
        BigEndian.WriteUInt32(id, ExternalInterfaceMemoryCard251Id);
        byte value = state.MemoryCardDataBytesTransferred < sizeof(ushort)
            ? id[sizeof(ushort) + state.MemoryCardDataBytesTransferred]
            : (byte)0xFF;
        state.MemoryCardDataBytesTransferred++;
        return value;
    }

    private static byte ReadExternalInterfaceMemoryCardStatusByte(ExternalInterfaceChannelState state)
    {
        byte value = state.MemoryCardDataBytesTransferred == 0
            ? state.MemoryCardStatus
            : (byte)0xFF;
        state.MemoryCardDataBytesTransferred++;
        return value;
    }

    private byte ReadExternalInterfaceMemoryCardDataByte(int channel, ExternalInterfaceChannelState state)
    {
        if (state.MemoryCardAddressBytesReceived < 4)
        {
            return 0xFF;
        }

        byte value = ReadExternalInterfaceMemoryCardByte(channel, state.MemoryCardOffset);
        state.MemoryCardOffset++;
        state.MemoryCardDataBytesTransferred++;
        return value;
    }

    private byte[] GetExternalInterfaceMemoryCard(int channel) =>
        channel == 0 ? _externalInterfaceMemoryCardSlotA : _externalInterfaceMemoryCardSlotB;

    private byte ReadExternalInterfaceMemoryCardByte(int channel, uint offset)
    {
        byte[] card = GetExternalInterfaceMemoryCard(channel);
        return offset < card.Length ? card[offset] : (byte)0xFF;
    }

    private void WriteExternalInterfaceMemoryCardByte(int channel, uint offset, byte value)
    {
        byte[] card = GetExternalInterfaceMemoryCard(channel);
        if (offset < card.Length)
        {
            card[offset] = value;
        }
    }

    private void EraseExternalInterfaceMemoryCardSector(int channel, uint offset)
    {
        byte[] card = GetExternalInterfaceMemoryCard(channel);
        uint sectorOffset = offset & ~(ExternalInterfaceMemoryCardSectorSize - 1u);
        if (sectorOffset >= card.Length)
        {
            return;
        }

        int count = Math.Min(ExternalInterfaceMemoryCardSectorSize, card.Length - checked((int)sectorOffset));
        card.AsSpan(checked((int)sectorOffset), count).Fill(0xFF);
    }

    private static void EraseExternalInterfaceMemoryCard(Span<byte> card) => card.Fill(0xFF);

    private static uint DecodeExternalInterfaceMemoryCardOffset(ExternalInterfaceMemoryCardCommand command, uint encoded)
    {
        return command == ExternalInterfaceMemoryCardCommand.EraseSector
            ? ((encoded >> 8) & 0x7Fu) << 17 | (encoded & 0xFFu) << 9
            : ((encoded >> 24) & 0x7Fu) << 17 | ((encoded >> 16) & 0xFFu) << 9 | ((encoded >> 8) & 0x3u) << 7 | (encoded & 0x7Fu);
    }

    private static byte[] CreateDefaultExternalInterfaceMemoryCard(ushort deviceId)
    {
        byte[] card = new byte[ExternalInterfaceMemoryCardSize];
        card.AsSpan().Fill(0xFF);
        WriteExternalInterfaceMemoryCardHeader(card.AsSpan(0, ExternalInterfaceMemoryCardSectorSize), deviceId);
        WriteExternalInterfaceMemoryCardDirectory(card.AsSpan(ExternalInterfaceMemoryCardSectorSize, ExternalInterfaceMemoryCardSectorSize));
        WriteExternalInterfaceMemoryCardDirectory(card.AsSpan(2 * ExternalInterfaceMemoryCardSectorSize, ExternalInterfaceMemoryCardSectorSize));
        WriteExternalInterfaceMemoryCardBlockAllocationTable(card.AsSpan(3 * ExternalInterfaceMemoryCardSectorSize, ExternalInterfaceMemoryCardSectorSize));
        WriteExternalInterfaceMemoryCardBlockAllocationTable(card.AsSpan(4 * ExternalInterfaceMemoryCardSectorSize, ExternalInterfaceMemoryCardSectorSize));
        return card;
    }

    private static void WriteExternalInterfaceMemoryCardHeader(Span<byte> block, ushort deviceId)
    {
        block.Fill(0xFF);
        ulong formatTime = 0x0000_0000_4E67_2000UL;
        Span<byte> flashId = stackalloc byte[12]
        {
            0x00, 0x17, 0x5E, 0xA1, 0x23, 0x42, 0x7C, 0x09, 0x10, 0x33, 0x56, 0xC7,
        };

        ulong random = formatTime;
        for (int index = 0; index < flashId.Length; index++)
        {
            random = ((random * 0x41C6_4E6DUL) + 0x3039UL) >> 16;
            block[index] = unchecked((byte)(flashId[index] + (uint)random));
            random = ((random * 0x41C6_4E6DUL) + 0x3039UL) >> 16;
            random &= 0x7FFFUL;
        }

        BigEndian.WriteUInt64(block.Slice(0x0C, sizeof(ulong)), formatTime);
        BigEndian.WriteUInt32(block.Slice(0x14, sizeof(uint)), 0);
        BigEndian.WriteUInt32(block.Slice(0x18, sizeof(uint)), 0);
        BigEndian.WriteUInt32(block.Slice(0x1C, sizeof(uint)), 0);
        BigEndian.WriteUInt16(block.Slice(0x20, sizeof(ushort)), deviceId);
        BigEndian.WriteUInt16(block.Slice(0x22, sizeof(ushort)), ExternalInterfaceMemoryCard251Mbits);
        BigEndian.WriteUInt16(block.Slice(0x24, sizeof(ushort)), 0);
        WriteExternalInterfaceMemoryCardChecksum(block, 0x0000, 0x01FC, 0x01FC);
    }

    private static void WriteExternalInterfaceMemoryCardDirectory(Span<byte> block)
    {
        block.Fill(0xFF);
        BigEndian.WriteUInt16(block.Slice(0x1FFA, sizeof(ushort)), 0);
        WriteExternalInterfaceMemoryCardChecksum(block, 0x0000, 0x1FFC, 0x1FFC);
    }

    private static void WriteExternalInterfaceMemoryCardBlockAllocationTable(Span<byte> block)
    {
        block.Clear();
        BigEndian.WriteUInt16(block.Slice(0x0004, sizeof(ushort)), 0);
        BigEndian.WriteUInt16(block.Slice(0x0006, sizeof(ushort)), ExternalInterfaceMemoryCard251FreeBlocks);
        BigEndian.WriteUInt16(block.Slice(0x0008, sizeof(ushort)), ExternalInterfaceMemoryCardMetadataBlocks - 1);
        WriteExternalInterfaceMemoryCardChecksum(block, 0x0004, ExternalInterfaceMemoryCardSectorSize - 0x0004, 0x0000);
    }

    private static void WriteExternalInterfaceMemoryCardChecksum(Span<byte> block, int offset, int count, int checksumOffset)
    {
        (ushort checksum, ushort inverseChecksum) =
            CalculateExternalInterfaceMemoryCardChecksum(block.Slice(offset, count));
        BigEndian.WriteUInt16(block.Slice(checksumOffset, sizeof(ushort)), checksum);
        BigEndian.WriteUInt16(block.Slice(checksumOffset + sizeof(ushort), sizeof(ushort)), inverseChecksum);
    }

    private static (ushort Checksum, ushort InverseChecksum) CalculateExternalInterfaceMemoryCardChecksum(ReadOnlySpan<byte> data)
    {
        ushort checksum = 0;
        ushort inverseChecksum = 0;
        for (int offset = 0; offset < data.Length; offset += sizeof(ushort))
        {
            ushort word = BigEndian.ReadUInt16(data.Slice(offset, sizeof(ushort)));
            checksum = unchecked((ushort)(checksum + word));
            inverseChecksum = unchecked((ushort)(inverseChecksum + (word ^ 0xFFFF)));
        }

        if (checksum == 0xFFFF)
        {
            checksum = 0;
        }

        if (inverseChecksum == 0xFFFF)
        {
            inverseChecksum = 0;
        }

        return (checksum, inverseChecksum);
    }

    private void SignalExternalInterfaceTransferComplete(uint baseAddress)
    {
        uint parameterAddress = baseAddress;
        _mmioValues.TryGetValue(parameterAddress, out uint parameter);
        parameter |= ExternalInterfaceTransferCompleteStatus;
        _mmioValues[parameterAddress] = parameter;
        UpdateExternalInterfaceProcessorInterrupt();
    }

    private void UpdateExternalInterfaceProcessorInterrupt()
    {
        bool pending = false;
        for (int channel = 0; channel < _externalInterfaceChannels.Length; channel++)
        {
            uint parameter = ReadMmioValue(ExternalInterfaceChannelBaseAddress(channel));
            pending |= (parameter & (ExternalInterfaceTransferCompleteStatus | ExternalInterfaceTransferCompleteMask))
                == (ExternalInterfaceTransferCompleteStatus | ExternalInterfaceTransferCompleteMask);
            pending |= (parameter & (ExternalInterfaceInterruptStatus | ExternalInterfaceInterruptMask))
                == (ExternalInterfaceInterruptStatus | ExternalInterfaceInterruptMask);
            pending |= (parameter & (ExternalInterfaceExternalInterruptStatus | ExternalInterfaceExternalInterruptMask))
                == (ExternalInterfaceExternalInterruptStatus | ExternalInterfaceExternalInterruptMask);
        }

        if (pending)
        {
            RaiseProcessorInterrupt(ProcessorInterfaceExternalInterrupt);
        }
        else
        {
            _processorInterruptCause &= ~ProcessorInterfaceExternalInterrupt;
        }
    }

    private bool TryGetSelectedExternalInterfaceDevice(int channel, out int device)
    {
        uint parameter = ReadMmioValue(ExternalInterfaceChannelBaseAddress(channel));
        return TryDecodeSelectedExternalInterfaceDevice(parameter, out device);
    }

    private static bool TryDecodeSelectedExternalInterfaceDevice(uint parameter, out int device)
    {
        uint selected = (parameter >> 7) & 0x7;
        if (selected == 0 || (selected & (selected - 1)) != 0)
        {
            device = -1;
            return false;
        }

        device = selected switch
        {
            0x1 => 0,
            0x2 => 1,
            0x4 => 2,
            _ => -1,
        };
        return device >= 0;
    }

    private uint ExternalInterfaceConnectedBit(int channel)
    {
        if (channel == 0 && ExternalInterfaceMemoryCardSlotAInserted)
        {
            return ExternalInterfaceDeviceConnected;
        }

        if (channel == 1 && ExternalInterfaceMemoryCardSlotBInserted)
        {
            return ExternalInterfaceDeviceConnected;
        }

        return 0;
    }

    private static bool TryGetExternalInterfaceRegister(uint alignedAddress, out int channel, out ExternalInterfaceRegister register)
    {
        if (alignedAddress < 0xCC00_6800 || alignedAddress > 0xCC00_6838)
        {
            channel = -1;
            register = default;
            return false;
        }

        uint offset = alignedAddress - 0xCC00_6800;
        channel = (int)(offset / 0x14);
        uint registerOffset = offset % 0x14;
        register = registerOffset switch
        {
            0x00 => ExternalInterfaceRegister.Parameter,
            0x04 => ExternalInterfaceRegister.DmaAddress,
            0x08 => ExternalInterfaceRegister.DmaLength,
            0x0C => ExternalInterfaceRegister.Control,
            0x10 => ExternalInterfaceRegister.Data,
            _ => default,
        };

        return channel < 3 && registerOffset is 0x00 or 0x04 or 0x08 or 0x0C or 0x10;
    }

    private static uint ExternalInterfaceChannelBaseAddress(int channel) => 0xCC00_6800u + (uint)channel * 0x14u;

    private static int ExternalInterfaceImmediateLength(uint control) => (int)((control >> 4) & 0x3) + 1;

    private static ExternalInterfaceTransferKind ExternalInterfaceTransferType(uint control) => (ExternalInterfaceTransferKind)((control >> 2) & 0x3);

    private static bool IsExternalInterfaceInternalDevice(int channel, int device) => channel == 0 && device == 1;

    private static bool IsExternalInterfaceMemoryCardDevice(int channel, int device) => channel is 0 or 1 && device == 0;

    private bool IsExternalInterfaceMemoryCardInserted(int channel) =>
        channel == 0 ? ExternalInterfaceMemoryCardSlotAInserted :
        channel == 1 && ExternalInterfaceMemoryCardSlotBInserted;

    private static uint AlignExternalInterfaceImmediateData(uint data, int length) => data & (0xFFFF_FFFFu << ((sizeof(uint) - length) * 8));

    private static uint MaskExternalInterfaceImmediateData(uint data, int length) => AlignExternalInterfaceImmediateData(data, length);

    private static uint ExternalInterfaceInternalDataOffset(uint command)
    {
        uint offset = command & ~ExternalInterfaceDeviceWriteFlag;
        if (offset >= ExternalInterfaceSramBaseOffset && offset < ExternalInterfaceSramBaseOffset + 0x1000)
        {
            return ((offset - ExternalInterfaceSramBaseOffset) >> 8) * sizeof(uint);
        }

        return offset;
    }

    private static uint CreateDefaultExternalInterfaceRtcCounter()
    {
        System.DateTimeOffset epoch = new(2000, 1, 1, 0, 0, 0, System.TimeSpan.Zero);
        long seconds = (long)(System.DateTimeOffset.UtcNow - epoch).TotalSeconds;
        return seconds <= 0 ? 0 : unchecked((uint)seconds);
    }

    private static byte[] CreateDefaultExternalInterfaceSram()
    {
        byte[] sram = new byte[64];
        sram[0x12] = 0;
        sram[0x13] = 0x2C;
        WriteExternalInterfaceSramMemoryCardFlashId(sram.AsSpan(0x14, 12));
        WriteExternalInterfaceSramMemoryCardFlashId(sram.AsSpan(0x20, 12));
        sram[0x3A] = CalculateExternalInterfaceSramMemoryCardFlashIdChecksum(sram.AsSpan(0x14, 12));
        sram[0x3B] = CalculateExternalInterfaceSramMemoryCardFlashIdChecksum(sram.AsSpan(0x20, 12));
        RefreshExternalInterfaceSramChecksum(sram);
        return sram;
    }

    private static void WriteExternalInterfaceSramMemoryCardFlashId(Span<byte> destination)
    {
        Span<byte> flashId = stackalloc byte[12]
        {
            0x00, 0x17, 0x5E, 0xA1, 0x23, 0x42, 0x7C, 0x09, 0x10, 0x33, 0x56, 0xC7,
        };
        flashId.CopyTo(destination);
    }

    private static byte CalculateExternalInterfaceSramMemoryCardFlashIdChecksum(ReadOnlySpan<byte> flashId)
    {
        byte checksum = 0;
        foreach (byte value in flashId)
        {
            checksum = unchecked((byte)(checksum + value));
        }

        return unchecked((byte)(checksum ^ 0xFF));
    }

    private static void RefreshExternalInterfaceSramChecksum(Span<byte> sram)
    {
        uint sum = 0;
        for (int offset = 4; offset < sram.Length; offset += sizeof(ushort))
        {
            sum = unchecked(sum + BigEndian.ReadUInt16(sram.Slice(offset, sizeof(ushort))));
        }

        ushort checksum = (ushort)sum;
        BigEndian.WriteUInt16(sram[..sizeof(ushort)], checksum);
        BigEndian.WriteUInt16(sram.Slice(sizeof(ushort), sizeof(ushort)), (ushort)~checksum);
    }

    private const uint ExternalInterfaceRomDisable = 0x0000_2000;
    private const uint ExternalInterfaceStatusMask =
        ExternalInterfaceExternalInterruptStatus |
        ExternalInterfaceTransferCompleteStatus |
        ExternalInterfaceInterruptStatus;
    private const uint ExternalInterfaceParameterWriteMask =
        ExternalInterfaceRomDisable |
        ExternalInterfaceExternalInterruptMask |
        0x0000_0380 |
        0x0000_0070 |
        ExternalInterfaceTransferCompleteMask |
        ExternalInterfaceInterruptMask;
    private const uint ExternalInterfaceControlMask = 0x0000_003F;
    private const uint ExternalInterfaceTransferStart = 0x0000_0001;
    private const uint ExternalInterfaceDmaTransfer = 0x0000_0002;
    private const uint ExternalInterfaceDeviceWriteFlag = 0x8000_0000;
    private const uint ExternalInterfaceRtcCounterOffset = 0x2000_0000;
    private const uint ExternalInterfaceSramBaseOffset = 0x2000_0100;
    private const byte ExternalInterfaceMemoryCardReadyStatus = 0x01;
    private const byte ExternalInterfaceMemoryCardSleepStatus = 0x20;
    private const byte ExternalInterfaceMemoryCardUnlockedStatus = 0x40;
    private const byte ExternalInterfaceMemoryCardBusyStatus = 0x80;
    private const byte ExternalInterfaceMemoryCardDefaultStatus =
        ExternalInterfaceMemoryCardBusyStatus | ExternalInterfaceMemoryCardReadyStatus | ExternalInterfaceMemoryCardUnlockedStatus;

    private uint NormalizeSerialInterfaceWrite(uint alignedAddress, uint value)
    {
        if (TryGetSerialInterfaceChannel(alignedAddress, out int channel))
        {
            SetSerialInterfaceResponse(channel, value);
            return value;
        }

        if (alignedAddress == SerialInterfacePollRegisterAddress)
        {
            uint normalizedValue = value & SerialInterfacePollMask;
            ExecuteSerialInterfacePolling(normalizedValue);
            _serialInterfacePollCycles = 0;
            return normalizedValue;
        }

        if (alignedAddress == 0xCC00_6434)
        {
            return WriteSerialInterfaceCommunicationControl(value);
        }

        if (alignedAddress == 0xCC00_6438)
        {
            return WriteSerialInterfaceStatus(value);
        }

        if (alignedAddress == 0xCC00_643C)
        {
            return value & 0x8000_0000u;
        }

        if (IsSerialInterfaceIoBuffer(alignedAddress))
        {
            WriteSerialInterfaceIoBuffer(alignedAddress, value);
            return value;
        }

        return value;
    }

    private uint WriteSerialInterfaceCommunicationControl(uint value)
    {
        _mmioValues.TryGetValue(SerialInterfaceCommunicationControlAddress, out uint existing);
        uint status = existing & (SerialInterfaceTransferCompleteStatus | SerialInterfaceReadStatusInterruptStatus);
        if ((value & SerialInterfaceTransferCompleteStatus) != 0)
        {
            status &= ~SerialInterfaceTransferCompleteStatus;
        }

        if ((value & SerialInterfaceReadStatusInterruptStatus) != 0)
        {
            status &= ~SerialInterfaceReadStatusInterruptStatus;
        }

        uint nextValue = (value & SerialInterfaceCommunicationControlWriteMask) | status;
        if (ShouldStartSerialInterfaceCommunicationTransfer(value))
        {
            ExecuteSerialInterfaceCommunicationTransfer(value);
            nextValue |= SerialInterfaceTransferCompleteStatus;
        }

        _mmioValues[SerialInterfaceCommunicationControlAddress] = nextValue;
        UpdateSerialInterfaceProcessorInterrupt(nextValue);
        return nextValue;
    }

    private uint WriteSerialInterfaceStatus(uint value)
    {
        _mmioValues.TryGetValue(SerialInterfaceStatusAddress, out uint existing);
        uint nextValue = existing;
        nextValue &= ~(value & SerialInterfaceStatusClearMask);
        if ((value & SerialInterfaceStatusWriteAllBuffers) != 0)
        {
            nextValue &= ~SerialInterfaceWriteStatusMask;
        }

        _mmioValues[SerialInterfaceStatusAddress] = nextValue;
        RefreshSerialInterfaceErrorLatches();
        UpdateSerialInterfaceReadStatusInterrupt();
        return nextValue;
    }

    private uint NormalizeSerialInterfaceRead(uint alignedAddress, uint value)
    {
        if (IsSerialInterfaceInputBufferLow(alignedAddress))
        {
            MarkSerialInterfaceChannelDataRead(SerialInterfaceInputBufferChannel(alignedAddress));
            return value;
        }

        if (IsSerialInterfaceInputBufferHigh(alignedAddress))
        {
            MarkSerialInterfaceChannelDataRead(SerialInterfaceInputBufferChannel(alignedAddress));
            return value;
        }

        if (alignedAddress == 0xCC00_6434)
        {
            return value & ~1u;
        }

        if (alignedAddress == 0xCC00_6438)
        {
            return value;
        }

        if (alignedAddress == 0xCC00_643C)
        {
            return value & 0x8000_0000u;
        }

        if (IsSerialInterfaceIoBuffer(alignedAddress))
        {
            if (alignedAddress == SerialInterfaceIoBufferAddress)
            {
                MarkSerialInterfaceChannelDataRead(_serialInterfaceCommunicationTransferChannel);
            }

            return ReadSerialInterfaceIoBuffer(alignedAddress);
        }

        return value;
    }

    private void ExecuteSerialInterfaceCommunicationTransfer(uint control)
    {
        int transferChannel = SerialInterfaceCommunicationTransferChannel(control);
        _serialInterfaceCommunicationTransferChannel = transferChannel;
        if (!IsSerialInterfaceControllerConnected(transferChannel))
        {
            SetSerialInterfaceNoResponse(transferChannel);
            SetSerialInterfaceNoControllerInput(transferChannel);
            return;
        }

        _mmioValues.TryGetValue(SerialInterfaceOutputBufferAddress(transferChannel), out uint outputBuffer);
        byte command = (control & SerialInterfaceTransferStart) != 0 && _serialInterfaceIoBuffer[0] == SerialInterfaceControllerCommandId
            ? SerialInterfaceOutputBufferCommand(outputBuffer)
            : _serialInterfaceIoBuffer[0];
        SetSerialInterfaceResponse(transferChannel, command);
        SetSerialInterfaceIoBufferResponse(command);
        _mmioValues[SerialInterfaceStatusAddress] = ReadMmioValue(SerialInterfaceStatusAddress) & ~SerialInterfaceWriteStatusBit(transferChannel);
    }

    private void ExecuteSerialInterfacePolling(uint poll)
    {
        uint enabled = (poll >> 4) & 0xF;
        if (enabled == 0)
        {
            return;
        }

        uint status = ReadMmioValue(SerialInterfaceStatusAddress);
        for (int channel = 0; channel < 4; channel++)
        {
            if ((enabled & (1u << channel)) == 0)
            {
                continue;
            }

            if (!IsSerialInterfaceControllerConnected(channel))
            {
                SetSerialInterfaceNoControllerInput(channel);
                status &= ~SerialInterfaceReadStatusBit(channel);
                continue;
            }

            _mmioValues.TryGetValue(SerialInterfaceOutputBufferAddress(channel), out uint outputBuffer);
            SetSerialInterfaceResponse(channel, SerialInterfaceOutputBufferCommand(outputBuffer));
            status |= SerialInterfaceReadStatusBit(channel);
        }

        _mmioValues[SerialInterfaceStatusAddress] = status;
        UpdateSerialInterfaceReadStatusInterrupt();
    }

    private void AdvanceSerialInterfacePolling(ulong cycles)
    {
        uint poll = ReadMmioValue(SerialInterfacePollRegisterAddress);
        if (((poll >> 4) & 0xF) == 0)
        {
            _serialInterfacePollCycles = 0;
            return;
        }

        _serialInterfacePollCycles += cycles;
        while (_serialInterfacePollCycles >= SerialInterfaceAutoPollCycles)
        {
            _serialInterfacePollCycles -= SerialInterfaceAutoPollCycles;
            ExecuteSerialInterfacePolling(poll);
        }
    }

    private void SetSerialInterfaceResponse(int channel, uint outputBuffer)
    {
        SetSerialInterfaceResponse(channel, SerialInterfaceOutputBufferCommand(outputBuffer));
    }

    private void SetSerialInterfaceResponse(int channel, byte command)
    {
        uint high = command == SerialInterfaceControllerCommandId
            ? SerialInterfaceStandardControllerType
            : SerialInterfaceControllerHigh();

        _mmioValues[SerialInterfaceInputBufferHighAddress(channel)] = high;
        _mmioValues[SerialInterfaceInputBufferLowAddress(channel)] = SerialInterfaceNeutralControllerLow;
    }

    private void SetSerialInterfaceNoResponse(int channel)
    {
        if (channel is < 0 or > 3)
        {
            return;
        }

        _mmioValues[SerialInterfaceStatusAddress] =
            ReadMmioValue(SerialInterfaceStatusAddress) | SerialInterfaceNoResponseBit(channel);
    }

    private void SetSerialInterfaceNoControllerInput(int channel)
    {
        if (channel is < 0 or > 3)
        {
            return;
        }

        _mmioValues[SerialInterfaceInputBufferHighAddress(channel)] =
            SerialInterfaceInputErrorStatus | SerialInterfaceInputErrorLatch;
        _mmioValues[SerialInterfaceInputBufferLowAddress(channel)] = 0;
    }

    private uint SerialInterfaceControllerHigh() =>
        SerialInterfaceNeutralControllerHigh | ((uint)SerialInterfaceControllerButtons << 16);

    private void SetSerialInterfaceIoBufferResponse(byte command)
    {
        Array.Clear(_serialInterfaceIoBuffer);

        if (command == SerialInterfaceControllerCommandId)
        {
            BigEndian.WriteUInt32(_serialInterfaceIoBuffer.AsSpan(0, sizeof(uint)), SerialInterfaceStandardControllerType);
            return;
        }

        BigEndian.WriteUInt32(_serialInterfaceIoBuffer.AsSpan(0, sizeof(uint)), SerialInterfaceControllerHigh());
        BigEndian.WriteUInt32(_serialInterfaceIoBuffer.AsSpan(sizeof(uint), sizeof(uint)), SerialInterfaceNeutralControllerLow);
        if (command is SerialInterfaceControllerCommandOrigin or SerialInterfaceControllerCommandRecalibrate or SerialInterfaceControllerCommandLongStatus)
        {
            BigEndian.WriteUInt32(_serialInterfaceIoBuffer.AsSpan(2 * sizeof(uint), sizeof(uint)), 0);
        }
    }

    private void UpdateSerialInterfaceReadStatusInterrupt()
    {
        uint status = ReadMmioValue(SerialInterfaceStatusAddress);
        uint control = ReadMmioValue(SerialInterfaceCommunicationControlAddress);
        bool newDataAvailable = (status & SerialInterfaceReadStatusMask) != 0;
        if (newDataAvailable)
        {
            control |= SerialInterfaceReadStatusInterruptStatus;
        }
        else
        {
            control &= ~SerialInterfaceReadStatusInterruptStatus;
        }

        _mmioValues[SerialInterfaceCommunicationControlAddress] = control;
        UpdateSerialInterfaceProcessorInterrupt(control);
    }

    private void MarkSerialInterfaceChannelDataRead(int channel)
    {
        if (channel is < 0 or > 3)
        {
            return;
        }

        uint status = ReadMmioValue(SerialInterfaceStatusAddress) & ~SerialInterfaceReadStatusBit(channel);
        _mmioValues[SerialInterfaceStatusAddress] = status;
        UpdateSerialInterfaceReadStatusInterrupt();
    }

    private void RefreshSerialInterfaceErrorLatches()
    {
        for (int channel = 0; channel < 4; channel++)
        {
            uint inputHigh = ReadMmioValue(SerialInterfaceInputBufferHighAddress(channel));
            if ((ReadMmioValue(SerialInterfaceStatusAddress) & SerialInterfaceChannelErrorMask(channel)) != 0)
            {
                inputHigh |= SerialInterfaceInputErrorStatus | SerialInterfaceInputErrorLatch;
            }
            else
            {
                inputHigh &= ~(SerialInterfaceInputErrorStatus | SerialInterfaceInputErrorLatch);
            }

            _mmioValues[SerialInterfaceInputBufferHighAddress(channel)] = inputHigh;
        }
    }

    private void UpdateSerialInterfaceProcessorInterrupt(uint control)
    {
        bool pending =
            (control & (SerialInterfaceTransferCompleteStatus | SerialInterfaceTransferCompleteMask)) == (SerialInterfaceTransferCompleteStatus | SerialInterfaceTransferCompleteMask) ||
            (control & (SerialInterfaceReadStatusInterruptStatus | SerialInterfaceReadStatusInterruptMask)) == (SerialInterfaceReadStatusInterruptStatus | SerialInterfaceReadStatusInterruptMask);

        if (pending)
        {
            RaiseProcessorInterrupt(ProcessorInterfaceSerialInterrupt);
        }
        else
        {
            _processorInterruptCause &= ~ProcessorInterfaceSerialInterrupt;
        }
    }

    private void UpdateVideoInterruptsForLine(int line)
    {
        foreach (uint address in VideoInterruptRegisters())
        {
            _mmioValues.TryGetValue(address, out uint value);
            if ((value & VideoInterruptLineMask) == line)
            {
                RaiseVideoInterrupt(address);
            }
        }
    }

    private void RaiseVideoInterrupt(uint address)
    {
        _mmioValues.TryGetValue(address, out uint value);
        _mmioValues[address] = value | VideoInterruptStatus;
        UpdateVideoProcessorInterrupt();
    }

    private void ClearVideoInterruptIfAcknowledged(uint acknowledgedAddress, uint acknowledgedValue)
    {
        UpdateVideoProcessorInterrupt(acknowledgedAddress, acknowledgedValue);
    }

    private void UpdateVideoProcessorInterrupt(uint? overrideAddress = null, uint overrideValue = 0)
    {
        foreach (uint address in VideoInterruptRegisters())
        {
            uint value = address == overrideAddress
                ? overrideValue
                : _mmioValues.TryGetValue(address, out uint storedValue) ? storedValue : 0;

            if ((value & (VideoInterruptStatus | VideoInterruptEnable)) == (VideoInterruptStatus | VideoInterruptEnable))
            {
                RaiseProcessorInterrupt(ProcessorInterfaceVideoInterrupt);
                return;
            }
        }

        _processorInterruptCause &= ~ProcessorInterfaceVideoInterrupt;
    }

    private static bool IsVideoInterruptRegister(uint alignedAddress)
    {
        foreach (uint address in VideoInterruptRegisters())
        {
            if (alignedAddress == address)
            {
                return true;
            }
        }

        return false;
    }

    private static ReadOnlySpan<uint> VideoInterruptRegisters()
    {
        return
        [
            0xCC00_2030,
            0xCC00_2034,
            0xCC00_2038,
            0xCC00_203C,
        ];
    }

    private static bool TryGetSerialInterfaceChannel(uint alignedAddress, out int channel)
    {
        if (alignedAddress >= 0xCC00_6400 && alignedAddress <= 0xCC00_6424 && (alignedAddress - 0xCC00_6400) % 0xC == 0)
        {
            channel = (int)((alignedAddress - 0xCC00_6400) / 0xC);
            return true;
        }

        channel = -1;
        return false;
    }

    private static bool IsSerialInterfaceInputBufferHigh(uint alignedAddress)
    {
        return alignedAddress >= 0xCC00_6404 && alignedAddress <= 0xCC00_6428 && (alignedAddress - 0xCC00_6404) % 0xC == 0;
    }

    private static bool IsSerialInterfaceInputBufferLow(uint alignedAddress)
    {
        return alignedAddress >= 0xCC00_6408 && alignedAddress <= 0xCC00_642C && (alignedAddress - 0xCC00_6408) % 0xC == 0;
    }

    private static int SerialInterfaceInputBufferChannel(uint alignedAddress)
    {
        uint baseAddress = IsSerialInterfaceInputBufferHigh(alignedAddress) ? 0xCC00_6404u : 0xCC00_6408u;
        return (int)((alignedAddress - baseAddress) / 0xC);
    }

    private static bool IsSerialInterfaceIoBuffer(uint alignedAddress) =>
        alignedAddress is >= 0xCC00_6480 and <= 0xCC00_64FC && (alignedAddress & 0x3) == 0;

    private void WriteSerialInterfaceIoBuffer(uint alignedAddress, uint value)
    {
        int offset = checked((int)(alignedAddress - SerialInterfaceIoBufferAddress));
        BigEndian.WriteUInt32(_serialInterfaceIoBuffer.AsSpan(offset, sizeof(uint)), value);
    }

    private uint ReadSerialInterfaceIoBuffer(uint alignedAddress)
    {
        int offset = checked((int)(alignedAddress - SerialInterfaceIoBufferAddress));
        return BigEndian.ReadUInt32(_serialInterfaceIoBuffer.AsSpan(offset, sizeof(uint)));
    }

    private static uint SerialInterfaceReadStatusBit(int channel) => 0x2000_0000u >> (channel * 8);

    private static uint SerialInterfaceWriteStatusBit(int channel) => 0x1000_0000u >> (channel * 8);

    private static uint SerialInterfaceNoResponseBit(int channel) => 0x0800_0000u >> (channel * 8);

    private static uint SerialInterfaceChannelErrorMask(int channel) => 0x0F00_0000u >> (channel * 8);

    private static byte SerialInterfaceOutputBufferCommand(uint outputBuffer) => (byte)((outputBuffer >> 16) & 0xFF);

    private static bool ShouldStartSerialInterfaceCommunicationTransfer(uint control)
    {
        return (control & SerialInterfaceTransferStart) != 0;
    }

    private static int SerialInterfaceCommunicationTransferChannel(uint control)
    {
        int lowChannel = (int)((control >> 1) & 0x3);
        if (lowChannel != 0)
        {
            return lowChannel;
        }

        return (int)((control >> 25) & 0x3);
    }

    private bool IsSerialInterfaceControllerConnected(int channel) =>
        channel switch
        {
            0 => SerialInterfaceControllerPort0Connected,
            1 => SerialInterfaceControllerPort1Connected,
            2 => SerialInterfaceControllerPort2Connected,
            3 => SerialInterfaceControllerPort3Connected,
            _ => false,
        };

    private static bool IsAramDmaRegister(uint alignedAddress)
    {
        return alignedAddress is >= 0xCC00_5020 and <= 0xCC00_502A;
    }

    private static bool IsAramDmaRegisterPair(uint alignedAddress)
    {
        return alignedAddress is 0xCC00_5020 or 0xCC00_5024 or 0xCC00_5028;
    }

    private static bool IsMainRamRange(uint address, uint length)
    {
        if (length > int.MaxValue || !GameCubeAddress.TryTranslateMainRam(address, out int offset))
        {
            return false;
        }

        return offset <= GameCubeAddress.MainRamSize - (int)length;
    }

    private static bool IsLwz(uint instruction, int rD, int rA) =>
        (instruction >> 26) == 32 && ((instruction >> 21) & 0x1F) == rD && ((instruction >> 16) & 0x1F) == rA;

    private static bool IsLwzWithBase(uint instruction, int rA) =>
        (instruction >> 26) == 32 && ((instruction >> 16) & 0x1F) == rA;

    private static bool IsAddi(uint instruction, int rD, int rA) =>
        (instruction >> 26) == 14 && ((instruction >> 21) & 0x1F) == rD && ((instruction >> 16) & 0x1F) == rA;

    private static bool IsStb(uint instruction, int rS, int rA) =>
        (instruction >> 26) == 38 && ((instruction >> 21) & 0x1F) == rS && ((instruction >> 16) & 0x1F) == rA;

    private static bool IsStw(uint instruction, int rS, int rA) =>
        (instruction >> 26) == 36 && ((instruction >> 21) & 0x1F) == rS && ((instruction >> 16) & 0x1F) == rA;

    private static bool TryGetRelativeBranchTarget(uint instruction, uint pc, out uint target)
    {
        target = 0;
        if ((instruction >> 26) != 18 || (instruction & 0x2) != 0)
        {
            return false;
        }

        int displacement = (int)(instruction & 0x03FF_FFFCu);
        if ((displacement & 0x0200_0000) != 0)
        {
            displacement |= unchecked((int)0xFC00_0000);
        }

        target = unchecked(pc + (uint)displacement);
        return true;
    }

    private static uint SerialInterfaceOutputBufferAddress(int channel) => 0xCC00_6400u + (uint)channel * 0xC;

    private static uint SerialInterfaceInputBufferHighAddress(int channel) => SerialInterfaceOutputBufferAddress(channel) + 4;

    private static uint SerialInterfaceInputBufferLowAddress(int channel) => SerialInterfaceOutputBufferAddress(channel) + 8;

    private const uint SerialInterfacePollRegisterAddress = 0xCC00_6430;
    private const uint SerialInterfaceCommunicationControlAddress = 0xCC00_6434;
    private const uint SerialInterfaceStatusAddress = 0xCC00_6438;
    private const uint SerialInterfaceIoBufferAddress = 0xCC00_6480;
    private const uint SerialInterfaceTransferStart = 0x0000_0001;
    private const byte SerialInterfaceControllerCommandId = 0x00;
    private const byte SerialInterfaceControllerCommandStatus = 0x40;
    private const byte SerialInterfaceControllerCommandOrigin = 0x41;
    private const byte SerialInterfaceControllerCommandRecalibrate = 0x42;
    private const byte SerialInterfaceControllerCommandLongStatus = 0x43;
    private const uint SerialInterfaceStatusWriteAllBuffers = 0x8000_0000;
    private const uint SerialInterfaceReadStatusMask = 0x2020_2020;
    private const uint SerialInterfaceWriteStatusMask = 0x1010_1010;
    private const uint SerialInterfaceStatusClearMask = 0x2F2F_2F2F;
    private const uint SerialInterfacePollMask = 0x03FF_FFF0;
    private const uint SerialInterfaceInputErrorStatus = 0x8000_0000;
    private const uint SerialInterfaceInputErrorLatch = 0x4000_0000;
    private const ulong SerialInterfaceAutoPollCycles = 4096;
    private const uint SerialInterfaceCommunicationControlWriteMask =
        SerialInterfaceTransferCompleteMask |
        SerialInterfaceReadStatusInterruptMask |
        0x0700_0000 |
        0x007F_0000 |
        0x0000_7F00 |
        0x0000_00C6;

    private static int TranslateAram(uint address, int size = 1)
    {
        uint normalized = address & (AramSize - 1);
        if (size < 0 || normalized > AramSize - size)
        {
            throw new AddressTranslationException(address);
        }

        return checked((int)normalized);
    }

    private static bool TryTranslateLockedCache(uint address, out int offset) =>
        TryTranslateLockedCache(address, size: 1, out offset);

    private static bool TryTranslateLockedCache(uint address, int size, out int offset)
    {
        uint normalized = address - LockedCacheStart;
        if (address < LockedCacheStart || size < 0 || normalized > LockedCacheSize - size)
        {
            offset = 0;
            return false;
        }

        offset = checked((int)normalized);
        return true;
    }

    private void Log(MmioAccessKind kind, uint address, int width, uint value, string deviceName)
    {
        MmioAccess access = new(kind, address, width, value, deviceName);
        if (LogMmioAccesses)
        {
            _mmioAccesses.Add(access);
        }

        MmioAccessObserver?.Invoke(access);
    }

    private static uint AlignAddress(uint address, int width)
    {
        return width switch
        {
            sizeof(byte) => address,
            sizeof(ushort) => address & ~1u,
            sizeof(uint) => address & ~3u,
            _ => throw new ArgumentOutOfRangeException(nameof(width)),
        };
    }

    private static uint MaskToWidth(uint value, int width)
    {
        return width switch
        {
            sizeof(byte) => value & 0xFF,
            sizeof(ushort) => value & 0xFFFF,
            sizeof(uint) => value,
            _ => throw new ArgumentOutOfRangeException(nameof(width)),
        };
    }

    private readonly record struct PendingDiscCommand(uint Command0, uint Command1, uint Command2, uint DmaAddress, uint DmaLength);

    private readonly record struct ArqDescriptor(uint Address, uint MainAddress, uint AramAddress, uint Length, uint CallbackAddress, bool AramToMain, bool IsLinkedQueueDescriptor);

    private readonly record struct PendingDirectAramDma(uint MainAddress, uint AramAddress, uint Length, bool AramToMain, ulong RemainingCycles);

    private readonly record struct PendingArqRequest(uint DescriptorAddress, uint MainAddress, uint AramAddress, uint Length, uint CallbackAddress, bool AramToMain, ulong RemainingCycles);

    private readonly record struct GlobalWordSetDspTaskCallback(uint Address, uint GlobalBasePointerAddress, short TargetOffset, uint Value);

    private readonly record struct SlotClearDspTaskCallback(uint Address, uint GlobalBasePointerAddress, ushort FlagOffset, uint Slots);

    private enum ExternalInterfaceRegister
    {
        Parameter,
        DmaAddress,
        DmaLength,
        Control,
        Data,
    }

    private enum ExternalInterfaceTransferKind
    {
        Read,
        Write,
        ReadWrite,
        Reserved,
    }

    private enum ExternalInterfaceMemoryCardCommand
    {
        None,
        GetDeviceId,
        GetId,
        GetStatus,
        ClearStatus,
        SetInterrupt,
        ReadErrorBuffer,
        WakeUp,
        Sleep,
        ArrayToBuffer,
        WriteBuffer,
        ReadBlock,
        WriteBlock,
        EraseSector,
        ExtraByteProgram,
        EraseCard,
    }

    private sealed class ExternalInterfaceChannelState
    {
        public uint Command;
        public bool HasCommand;
        public bool PendingImmediateWrite;
        public uint PendingWriteOffset;
        public ExternalInterfaceMemoryCardCommand MemoryCardCommand;
        public bool MemoryCardCommandStarted;
        public int MemoryCardCommandByteCount;
        public int MemoryCardAddressBytesReceived;
        public uint MemoryCardAddress;
        public uint MemoryCardOffset;
        public int MemoryCardDataBytesTransferred;
        public byte MemoryCardStatus = ExternalInterfaceMemoryCardDefaultStatus;
        public bool MemoryCardInterruptEnabled;
    }

    private enum GxFifoPayloadKind
    {
        None,
        BpRegister,
        XfHeader,
    }

    private struct GxFifoParserState
    {
        public int SkipBytes;
        public int PayloadBytesRemaining;
        public uint Payload;
        public GxFifoPayloadKind PendingPayloadKind;
    }
}

public sealed record ExternalInterfaceDebugSnapshot(
    uint ProcessorInterruptCause,
    uint ProcessorInterruptMask,
    bool HasPendingExternalInterrupt,
    ExternalInterfaceChannelDebugSnapshot[] Channels);

public sealed record ExternalInterfaceChannelDebugSnapshot(
    int Channel,
    uint Parameter,
    uint DmaAddress,
    uint DmaLength,
    uint Control,
    uint Data,
    int SelectedDevice,
    bool DeviceConnected,
    bool TransferCompleteStatus,
    bool TransferCompleteMask,
    bool InterruptStatus,
    bool InterruptMask,
    bool ExternalInterruptStatus,
    bool ExternalInterruptMask,
    uint Command,
    bool HasCommand,
    bool PendingImmediateWrite,
    uint PendingWriteOffset,
    string MemoryCardCommand,
    bool MemoryCardCommandStarted,
    int MemoryCardCommandByteCount,
    int MemoryCardAddressBytesReceived,
    uint MemoryCardAddress,
    uint MemoryCardOffset,
    int MemoryCardDataBytesTransferred,
    byte MemoryCardStatus,
    bool MemoryCardInterruptEnabled);

public sealed record DiscInterfaceDebugSnapshot(
    uint ProcessorInterruptCause,
    uint ProcessorInterruptMask,
    bool HasPendingExternalInterrupt,
    uint Status,
    uint Cover,
    uint Command0,
    uint Command1,
    uint Command2,
    uint DmaAddress,
    uint DmaLength,
    uint Control,
    uint ImmediateData,
    uint Configuration,
    bool HasPendingCommand,
    ulong PendingCommandCycles,
    uint LastError,
    bool DeviceErrorStatus,
    bool DeviceErrorMask,
    bool TransferCompleteStatus,
    bool TransferCompleteMask,
    bool BreakStatus,
    bool BreakMask);
