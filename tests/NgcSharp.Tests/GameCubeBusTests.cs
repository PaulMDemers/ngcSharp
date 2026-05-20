using NgcSharp.Core;
using NgcSharp.Cpu;

namespace NgcSharp.Tests;

public sealed class GameCubeBusTests
{
    [Fact]
    public void RoutesMainRamAccessesToMemory()
    {
        GameCubeBus bus = new();

        bus.Write32(0x8000_0100, 0x1234_5678);

        Assert.Equal(0x1234_5678u, bus.Read32(0x0000_0100));
        Assert.Empty(bus.MmioAccesses);
    }

    [Fact]
    public void MainRamPointerHeuristicsDoNotTurnNearTopPointersIntoFaults()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x803B_1AE8,
        };
        bus.Memory.EnableMainRamTopGuard = true;

        bus.Write32(0x8051_AAF0, 0x817F_FFF8);

        Assert.Equal(0x817F_FFF8u, bus.Read32(0x8051_AAF0));
    }

    [Fact]
    public void StubsAndLogsHardwareRegisterAccesses()
    {
        GameCubeBus bus = new();

        bus.Write16(0xCC00_3008, 0xBEEF);
        ushort value = bus.Read16(0xCC00_3008);

        Assert.Equal(0xBEEF, value);
        Assert.Equal(2, bus.MmioAccesses.Count);
        Assert.Equal(MmioAccessKind.Write, bus.MmioAccesses[0].Kind);
        Assert.Equal("PI", bus.MmioAccesses[0].DeviceName);
        Assert.Equal(MmioAccessKind.Read, bus.MmioAccesses[1].Kind);
    }

    [Fact]
    public void ExiTransferControlWritesCompleteImmediately()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_680C, 0x15);

        Assert.Equal(0x14u, bus.Read32(0xCC00_680C));
        Assert.Equal(0x14u, bus.MmioAccesses[0].Value);
        Assert.Equal(GameCubeBus.ExternalInterfaceTransferCompleteStatus, bus.Read32(0xCC00_6800) & GameCubeBus.ExternalInterfaceTransferCompleteStatus);
    }

    [Fact]
    public void ExiImmediateTransferRaisesAndClearsMaskedProcessorInterrupt()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceExternalInterrupt);
        bus.Write32(0xCC00_6800, 0x080 | GameCubeBus.ExternalInterfaceTransferCompleteMask);
        bus.Write32(0xCC00_6810, 0x2000_0000);
        bus.Write32(0xCC00_680C, 0x39);

        Assert.True(bus.HasPendingExternalInterrupt);
        Assert.Equal(GameCubeBus.ProcessorInterfaceExternalInterrupt, bus.Read32(0xCC00_3000) & GameCubeBus.ProcessorInterfaceExternalInterrupt);

        bus.Write32(0xCC00_6800, 0x100 | GameCubeBus.ExternalInterfaceTransferCompleteMask | GameCubeBus.ExternalInterfaceTransferCompleteStatus);

        Assert.False(bus.HasPendingExternalInterrupt);
        Assert.Equal(0u, bus.Read32(0xCC00_6800) & GameCubeBus.ExternalInterfaceTransferCompleteStatus);
    }

    [Fact]
    public void ExiDebugSnapshotReportsChannelState()
    {
        GameCubeBus bus = new()
        {
            ExternalInterfaceMemoryCardSlotAInserted = true,
        };

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceExternalInterrupt);
        bus.Write32(0xCC00_6800, 0x080 | GameCubeBus.ExternalInterfaceTransferCompleteMask);
        bus.Write32(0xCC00_6810, 0x5200_0000);
        bus.Write32(0xCC00_680C, 0x35);

        ExternalInterfaceDebugSnapshot snapshot = bus.GetExternalInterfaceDebugSnapshot();
        ExternalInterfaceChannelDebugSnapshot channel = snapshot.Channels[0];

        Assert.True(snapshot.HasPendingExternalInterrupt);
        Assert.Equal(GameCubeBus.ProcessorInterfaceExternalInterrupt, snapshot.ProcessorInterruptCause & GameCubeBus.ProcessorInterfaceExternalInterrupt);
        Assert.Equal(GameCubeBus.ProcessorInterfaceExternalInterrupt, snapshot.ProcessorInterruptMask & GameCubeBus.ProcessorInterfaceExternalInterrupt);
        Assert.True(channel.DeviceConnected);
        Assert.True(channel.TransferCompleteStatus);
        Assert.True(channel.TransferCompleteMask);
        Assert.Equal(0, channel.SelectedDevice);
        Assert.Equal("ReadBlock", channel.MemoryCardCommand);
        Assert.True(channel.MemoryCardCommandStarted);
        Assert.Equal(4, channel.MemoryCardCommandByteCount);
        Assert.Equal(3, channel.MemoryCardAddressBytesReceived);
    }

    [Fact]
    public void DiscDebugSnapshotReportsPendingCommandState()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_6000, GameCubeBus.DiscInterfaceInterruptMask);
        bus.Write32(0xCC00_6008, 0xA800_0000);
        bus.Write32(0xCC00_600C, 0x0000_4000);
        bus.Write32(0xCC00_6010, 0x0000_0200);
        bus.Write32(0xCC00_6014, 0x8000_1000);
        bus.Write32(0xCC00_6018, 0x0000_0200);
        bus.Write32(0xCC00_601C, 0x0000_0001);

        DiscInterfaceDebugSnapshot snapshot = bus.GetDiscInterfaceDebugSnapshot();

        Assert.Equal(GameCubeBus.DiscInterfaceInterruptMask, snapshot.Status & GameCubeBus.DiscInterfaceInterruptMask);
        Assert.True(snapshot.HasPendingCommand);
        Assert.True(snapshot.PendingCommandCycles > 0);
        Assert.Equal(0xA800_0000u, snapshot.Command0);
        Assert.Equal(0x8000_1000u, snapshot.DmaAddress);
        Assert.Equal(0x0000_0200u, snapshot.DmaLength);
        Assert.Equal(1u, snapshot.Control & 1u);
    }

    [Fact]
    public void ExiInternalRtcCommandReturnsConfiguredCounter()
    {
        GameCubeBus bus = new()
        {
            ExternalInterfaceRtcCounter = 0x1234_5678,
        };

        bus.Write32(0xCC00_6800, 0x100);
        bus.Write32(0xCC00_6810, 0x2000_0000);
        bus.Write32(0xCC00_680C, 0x35);
        bus.Write32(0xCC00_680C, 0x31);

        Assert.Equal(0x1234_5678u, bus.Read32(0xCC00_6810));
    }

    [Fact]
    public void ExiInternalSramDmaReadCopiesDefaultSram()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_6800, 0x100);
        bus.Write32(0xCC00_6810, 0x2000_0100);
        bus.Write32(0xCC00_680C, 0x35);
        bus.Write32(0xCC00_6804, 0x8000_1000);
        bus.Write32(0xCC00_6808, 0x40);
        bus.Write32(0xCC00_680C, 0x03);

        ushort checksum = bus.Memory.Read16(0x8000_1000);
        ushort inverseChecksum = bus.Memory.Read16(0x8000_1002);
        Assert.Equal((ushort)~checksum, inverseChecksum);
        Assert.Equal(0x2C, bus.Memory.Read8(0x8000_1013));
        Assert.Equal(0x00, bus.Memory.Read8(0x8000_1014));
        Assert.Equal(0x17, bus.Memory.Read8(0x8000_1015));
        Assert.Equal(0x9F, bus.Memory.Read8(0x8000_103A));
        Assert.Equal(0x9F, bus.Memory.Read8(0x8000_103B));
    }

    [Fact]
    public void ExiInternalSramImmediateWriteCanBeReadBack()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_6800, 0x100);
        bus.Write32(0xCC00_6810, 0xA000_0100);
        bus.Write32(0xCC00_680C, 0x35);
        bus.Write32(0xCC00_6810, 0x1122_3344);
        bus.Write32(0xCC00_680C, 0x35);
        bus.Write32(0xCC00_6810, 0x2000_0100);
        bus.Write32(0xCC00_680C, 0x35);
        bus.Write32(0xCC00_680C, 0x31);

        Assert.Equal(0x1122_3344u, bus.Read32(0xCC00_6810));
    }

    [Fact]
    public void ExiMemoryCardIdentityIsReportedOnlyWhenInserted()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_6800, 0x80);
        bus.Write32(0xCC00_6810, 0);
        bus.Write32(0xCC00_680C, 0x35);
        bus.Write32(0xCC00_680C, 0x31);
        Assert.Equal(0xFFFF_FFFFu, bus.Read32(0xCC00_6810));

        bus.ExternalInterfaceMemoryCardSlotAInserted = true;
        bus.Write32(0xCC00_6810, 0);
        bus.Write32(0xCC00_680C, 0x35);
        bus.Write32(0xCC00_680C, 0x31);

        Assert.Equal(GameCubeBus.ExternalInterfaceDeviceConnected, bus.Read32(0xCC00_6800) & GameCubeBus.ExternalInterfaceDeviceConnected);
        Assert.Equal(GameCubeBus.ExternalInterfaceMemoryCard251Id, bus.Read32(0xCC00_6810));
    }

    [Fact]
    public void ExiMemoryCardCommandIdAndStatusReportReadyCard()
    {
        GameCubeBus bus = new()
        {
            ExternalInterfaceMemoryCardSlotAInserted = true,
        };

        bus.Write32(0xCC00_6800, 0x80);
        bus.Write32(0xCC00_6810, 0x8500_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_680C, 0x11);

        Assert.Equal(0x0010_0000u, bus.Read32(0xCC00_6810));

        bus.Write32(0xCC00_6810, 0x8300_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_680C, 0x01);

        Assert.Equal(0xC100_0000u, bus.Read32(0xCC00_6810));
    }

    [Fact]
    public void ExiMemoryCardStatusReportsUnlockedAndSupportsSleepWake()
    {
        GameCubeBus bus = new()
        {
            ExternalInterfaceMemoryCardSlotAInserted = true,
        };

        bus.Write32(0xCC00_6800, 0x80);
        bus.Write32(0xCC00_6810, 0x8300_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_680C, 0x01);
        Assert.Equal(0xC100_0000u, bus.Read32(0xCC00_6810));

        bus.Write32(0xCC00_6810, 0x8800_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_6810, 0x8300_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_680C, 0x01);
        Assert.Equal(0x2000_0000u, bus.Read32(0xCC00_6810));

        bus.Write32(0xCC00_6810, 0x8700_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_6810, 0x8300_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_680C, 0x01);
        Assert.Equal(0xC100_0000u, bus.Read32(0xCC00_6810));
    }

    [Fact]
    public void ExiMemoryCardInsertedSlotStartsFormattedAs251BlockCard()
    {
        GameCubeBus bus = new()
        {
            ExternalInterfaceMemoryCardSlotAInserted = true,
        };

        byte[] header = ReadExiMemoryCardBytes(bus, 0, 0x80);
        byte[] directoryTail = ReadExiMemoryCardBytes(bus, 0x2000 + 0x1F80, 0x80);
        byte[] bat = ReadExiMemoryCardBytes(bus, 0x6000, 0x80);

        Assert.NotEqual(Enumerable.Repeat((byte)0xFF, header.Length), header);
        Assert.Equal(0, BigEndian.ReadUInt16(header.AsSpan(0x20, sizeof(ushort))));
        Assert.Equal(0x10, BigEndian.ReadUInt16(header.AsSpan(0x22, sizeof(ushort))));
        Assert.Equal(0, BigEndian.ReadUInt16(header.AsSpan(0x24, sizeof(ushort))));
        Assert.Equal(0xF003, BigEndian.ReadUInt16(directoryTail.AsSpan(0x7C, sizeof(ushort))));
        Assert.Equal(0, BigEndian.ReadUInt16(directoryTail.AsSpan(0x7E, sizeof(ushort))));
        Assert.Equal(251, BigEndian.ReadUInt16(bat.AsSpan(0x0006, sizeof(ushort))));
        Assert.Equal(4, BigEndian.ReadUInt16(bat.AsSpan(0x0008, sizeof(ushort))));
    }

    [Fact]
    public void ExiMemoryCardDeselectResetsShortReadCommand()
    {
        GameCubeBus bus = new()
        {
            ExternalInterfaceMemoryCardSlotAInserted = true,
        };

        bus.Write32(0xCC00_6800, 0x80);
        bus.Write32(0xCC00_6810, 0x5203_FF01);
        bus.Write32(0xCC00_680C, 0x35);
        bus.Write32(0xCC00_6810, 0x4B00_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_680C, 0x31);
        Assert.Equal(0xFFFF_FFFFu, bus.Read32(0xCC00_6810));

        bus.Write32(0xCC00_6800, 0);
        byte[] header = ReadExiMemoryCardBytes(bus, 0, 4);

        Assert.NotEqual(0xFFFF_FFFFu, BigEndian.ReadUInt32(header));
    }

    [Fact]
    public void ExiMemoryCardLibogcStyleStatusSequenceReportsReady()
    {
        GameCubeBus bus = new()
        {
            ExternalInterfaceMemoryCardSlotAInserted = true,
        };

        bus.Write32(0xCC00_6800, 0x80);
        bus.Write32(0xCC00_6810, 0x8300_0000);
        bus.Write32(0xCC00_680C, 0x15);
        bus.Write32(0xCC00_680C, 0x01);

        Assert.Equal(0xC100_0000u, bus.Read32(0xCC00_6810));
    }

    [Fact]
    public void ExiMemoryCardWriteReadAndEraseSectorRoundTripsData()
    {
        GameCubeBus bus = new()
        {
            ExternalInterfaceMemoryCardSlotAInserted = true,
        };

        for (int index = 0; index < 0x80; index++)
        {
            bus.Memory.Write8(0x8000_2000u + (uint)index, (byte)(index ^ 0x5A));
        }

        bus.Write32(0xCC00_6800, 0x80);
        bus.Write32(0xCC00_6810, 0xF200_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_6810, 0x0000_0000);
        bus.Write32(0xCC00_680C, 0x35);
        bus.Write32(0xCC00_6804, 0x8000_2000);
        bus.Write32(0xCC00_6808, 0x80);
        bus.Write32(0xCC00_680C, 0x07);

        bus.Write32(0xCC00_6810, 0x5200_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_6810, 0x0000_0000);
        bus.Write32(0xCC00_680C, 0x35);
        bus.Write32(0xCC00_6804, 0x8000_2100);
        bus.Write32(0xCC00_6808, 0x80);
        bus.Write32(0xCC00_680C, 0x03);

        for (int index = 0; index < 0x80; index++)
        {
            Assert.Equal((byte)(index ^ 0x5A), bus.Memory.Read8(0x8000_2100u + (uint)index));
        }

        bus.Write32(0xCC00_6810, 0xF100_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_6810, 0x0000_0000);
        bus.Write32(0xCC00_680C, 0x15);
        bus.Write32(0xCC00_6810, 0x5200_0000);
        bus.Write32(0xCC00_680C, 0x05);
        bus.Write32(0xCC00_6810, 0x0000_0000);
        bus.Write32(0xCC00_680C, 0x35);
        bus.Write32(0xCC00_6804, 0x8000_2200);
        bus.Write32(0xCC00_6808, 0x80);
        bus.Write32(0xCC00_680C, 0x03);

        for (int index = 0; index < 0x80; index++)
        {
            Assert.Equal(0xFF, bus.Memory.Read8(0x8000_2200u + (uint)index));
        }
    }

    private static byte[] ReadExiMemoryCardBytes(GameCubeBus bus, uint offset, int length)
    {
        byte[] result = new byte[length];
        uint destinationAddress = 0x8000_3000;
        bus.Write32(0xCC00_6800, 0x80);

        for (int copied = 0; copied < result.Length; copied += 0x80)
        {
            int chunkLength = Math.Min(0x80, result.Length - copied);
            bus.Write32(0xCC00_6810, 0x5200_0000);
            bus.Write32(0xCC00_680C, 0x05);
            bus.Write32(0xCC00_6810, EncodeExiMemoryCardOffset(offset + (uint)copied));
            bus.Write32(0xCC00_680C, 0x35);
            bus.Write32(0xCC00_6804, destinationAddress);
            bus.Write32(0xCC00_6808, (uint)chunkLength);
            bus.Write32(0xCC00_680C, 0x03);

            for (int index = 0; index < chunkLength; index++)
            {
                result[copied + index] = bus.Memory.Read8(destinationAddress + (uint)index);
            }
        }

        return result;
    }

    private static uint EncodeExiMemoryCardOffset(uint offset) =>
        ((offset >> 17) & 0x7Fu) << 24 |
        ((offset >> 9) & 0xFFu) << 16 |
        ((offset >> 7) & 0x03u) << 8 |
        (offset & 0x7Fu);

    [Fact]
    public void DspControlWritesClearResetAndAcknowledgedInterruptBits()
    {
        GameCubeBus bus = new();

        bus.Write16(
            0xCC00_500A,
            (ushort)(GameCubeBus.DspControlDmaInterruptMask
                | GameCubeBus.DspControlDspInterruptStatus
                | GameCubeBus.DspControlAramInterruptStatus
                | GameCubeBus.DspControlAudioDmaInterruptStatus
                | 0x0005));

        Assert.Equal(0x0804, bus.Read16(0xCC00_500A));
    }

    [Fact]
    public void DspMailboxInputReportsAcceptedAfterWrite()
    {
        GameCubeBus bus = new();
        Assert.Equal(0x8071, bus.Read16(0xCC00_5004) & 0xFFFF);
        Assert.Equal(0xFEED, bus.Read16(0xCC00_5006));

        bus.Write16(0xCC00_5002, 0x1234);
        bus.Write16(0xCC00_5000, 0x9234);

        Assert.Equal(0x1234, bus.Read16(0xCC00_5000));
        Assert.Equal(0x9234, bus.Read16(0xCC00_5004));
        Assert.Equal(0x1234, bus.Read16(0xCC00_5006));
        Assert.Equal(0, bus.Read16(0xCC00_5004));
        Assert.False(bus.HasPendingExternalInterrupt);
        Assert.Equal(0u, bus.Read16(0xCC00_500A) & GameCubeBus.DspControlDspInterruptStatus);
    }

    [Fact]
    public void AramDmaSizeWriteReportsTransferComplete()
    {
        GameCubeBus bus = new();
        Assert.Equal(0x8071, bus.Read16(0xCC00_5004) & 0xFFFF);
        Assert.Equal(0xFEED, bus.Read16(0xCC00_5006));

        bus.Write32(0xCC00_5028, 0x0000_0020);

        Assert.Equal(0u, bus.Read16(0xCC00_500A) & GameCubeBus.DspControlAramInterruptStatus);
        bus.Advance(0x1000);

        Assert.Equal(GameCubeBus.DspControlAramInterruptStatus, bus.Read16(0xCC00_500A) & GameCubeBus.DspControlAramInterruptStatus);
        Assert.False(bus.HasPendingExternalInterrupt);
        Assert.Equal(0xC0FF, bus.Read16(0xCC00_5004));
        Assert.Equal(1, bus.Read16(0xCC00_5006));
    }

    [Fact]
    public void ZeroLengthAramDmaReissuesDspBootGreeting()
    {
        GameCubeBus bus = new();
        Assert.Equal(0x8071, bus.Read16(0xCC00_5004) & 0xFFFF);
        Assert.Equal(0xFEED, bus.Read16(0xCC00_5006));

        bus.Write32(0xCC00_5028, 0);

        Assert.Equal(0x8071, bus.Read16(0xCC00_5004) & 0xFFFF);
        Assert.Equal(0xFEED, bus.Read16(0xCC00_5006));
    }

    [Fact]
    public void DspProcessorInterruptIsGatedByControlMasks()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceDspInterrupt);
        bus.Write32(0xCC00_5028, 0x0000_0020);

        Assert.False(bus.HasPendingExternalInterrupt);

        bus.Write16(0xCC00_500A, (ushort)GameCubeBus.DspControlAramInterruptMask);

        Assert.False(bus.HasPendingExternalInterrupt);
        bus.Advance(0x1000);

        Assert.True(bus.HasPendingExternalInterrupt);

        bus.Write16(0xCC00_500A, (ushort)GameCubeBus.DspControlAramInterruptStatus);

        Assert.False(bus.HasPendingExternalInterrupt);
    }

    [Fact]
    public void PixelEngineFinishCommandRaisesMaskedProcessorInterrupt()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfacePixelEngineFinishInterrupt);
        bus.Write16(0xCC00_100A, GameCubeBus.PixelEngineFinishEnable);
        bus.Write8(0xCC00_8000, 0x61);
        bus.Write32(0xCC00_8000, 0x4500_0002);

        Assert.Equal(GameCubeBus.PixelEngineFinishStatus, bus.Read16(0xCC00_100A) & GameCubeBus.PixelEngineFinishStatus);
        Assert.True(bus.HasPendingExternalInterrupt);
        Assert.Equal(GameCubeBus.ProcessorInterfacePixelEngineFinishInterrupt, bus.PendingProcessorInterrupts & GameCubeBus.ProcessorInterfacePixelEngineFinishInterrupt);

        bus.Write16(0xCC00_100A, GameCubeBus.PixelEngineFinishEnable | GameCubeBus.PixelEngineFinishStatus);

        Assert.Equal(0, bus.Read16(0xCC00_100A) & GameCubeBus.PixelEngineFinishStatus);
        Assert.False(bus.HasPendingExternalInterrupt);
    }

    [Fact]
    public void PixelEngineFinishCommandIsDetectedAfterFifoParserDesync()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfacePixelEngineFinishInterrupt);
        bus.Write16(0xCC00_100A, GameCubeBus.PixelEngineFinishEnable);
        bus.Write8(0xCC00_8000, 0x80);
        bus.Write16(0xCC00_8000, 0x0004);
        bus.Write8(0xCC00_8000, 0x10);
        bus.Write8(0xCC00_8000, 0x61);
        bus.Write32(0xCC00_8000, 0x4500_0002);

        Assert.Equal(GameCubeBus.PixelEngineFinishStatus, bus.Read16(0xCC00_100A) & GameCubeBus.PixelEngineFinishStatus);
        Assert.True(bus.HasPendingExternalInterrupt);
    }

    [Fact]
    public void PixelEngineTokenCommandStoresTokenAndRaisesMaskedProcessorInterrupt()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfacePixelEngineTokenInterrupt);
        bus.Write16(0xCC00_100A, GameCubeBus.PixelEngineTokenEnable);
        bus.Write32(0xCC00_8000, 0x4800_BEEF);

        Assert.Equal(0xBEEF, bus.Read16(0xCC00_100E));
        Assert.Equal(GameCubeBus.PixelEngineTokenStatus, bus.Read16(0xCC00_100A) & GameCubeBus.PixelEngineTokenStatus);
        Assert.True(bus.HasPendingExternalInterrupt);

        bus.Write16(0xCC00_100A, GameCubeBus.PixelEngineTokenEnable | GameCubeBus.PixelEngineTokenStatus);

        Assert.Equal(0, bus.Read16(0xCC00_100A) & GameCubeBus.PixelEngineTokenStatus);
        Assert.False(bus.HasPendingExternalInterrupt);
    }

    [Fact]
    public void DspAudioDmaStartRaisesMaskedProcessorInterrupt()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceDspInterrupt);
        bus.Write16(0xCC00_500A, (ushort)GameCubeBus.DspControlAudioDmaInterruptMask);

        bus.Write16(0xCC00_5036, 0x8014);

        Assert.Equal(0x0014, bus.Read16(0xCC00_5036));
        Assert.Equal(0, bus.Read16(0xCC00_503A));
        Assert.Equal(GameCubeBus.DspControlAudioDmaInterruptStatus, bus.Read16(0xCC00_500A) & GameCubeBus.DspControlAudioDmaInterruptStatus);
        Assert.True(bus.HasPendingExternalInterrupt);

        bus.Write16(0xCC00_500A, (ushort)GameCubeBus.DspControlAudioDmaInterruptStatus);

        Assert.False(bus.HasPendingExternalInterrupt);
    }

    [Fact]
    public void AramDmaCompletesTrivialDspTaskCallbacks()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x8000_3000,
        };

        uint taskAddress = 0x8000_1000;
        uint callbackAddress = 0x8000_2000;
        uint flagAddress = 0x8000_2FFC;

        bus.Memory.Write32(callbackAddress, 0x3800_0001);
        bus.Memory.Write32(callbackAddress + 4, 0x900D_FFFC);
        bus.Memory.Write32(callbackAddress + 8, 0x4E80_0020);
        bus.Write32(taskAddress + 0x28, callbackAddress);

        bus.Write32(0xCC00_5028, 0);

        Assert.Equal(1u, bus.Memory.Read32(flagAddress));
    }

    [Fact]
    public void AramDmaCompletesDecrementWordDspTaskCallbacks()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x8000_3000,
        };

        uint taskAddress = 0x8000_1000;
        uint callbackAddress = 0x8000_2000;
        uint counterAddress = 0x8000_2FFC;

        bus.Memory.Write32(callbackAddress, 0x806D_FFFC);
        bus.Memory.Write32(callbackAddress + 4, 0x3803_FFFF);
        bus.Memory.Write32(callbackAddress + 8, 0x900D_FFFC);
        bus.Memory.Write32(callbackAddress + 12, 0x4E80_0020);
        bus.Memory.Write32(counterAddress, 2);
        bus.Write32(taskAddress + 0x28, callbackAddress);

        bus.Write32(0xCC00_5028, 0);

        Assert.Equal(1u, bus.Memory.Read32(counterAddress));

        bus.Write32(0xCC00_5028, 0);

        Assert.Equal(0u, bus.Memory.Read32(counterAddress));
    }

    [Fact]
    public void AramDmaCompletesByteSetDspTaskCallbacks()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x8000_3000,
        };

        uint taskAddress = 0x8000_1000;
        uint callbackAddress = 0x8000_2000;
        uint setterAddress = 0x8000_2800;
        uint flagAddress = 0x8000_2FFC;

        bus.Memory.Write32(callbackAddress + 0x20, 0x4800_07E1);
        bus.Memory.Write32(setterAddress, 0x3800_0001);
        bus.Memory.Write32(setterAddress + 4, 0x980D_FFFC);
        bus.Memory.Write32(setterAddress + 8, 0x4E80_0020);
        bus.Write32(taskAddress + 0x28, callbackAddress);

        bus.Write32(0xCC00_5028, 0);

        Assert.Equal(1, bus.Memory.Read8(flagAddress));
    }

    [Fact]
    public void ArqDescriptorCompletionCopiesToAramAndRunsCallbackAfterLatency()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x8000_3000,
        };

        uint descriptorAddress = 0x8000_1000;
        uint callbackAddress = 0x8000_2000;
        uint counterAddress = 0x8000_2FFC;
        uint sourceAddress = 0x8000_4000;
        uint aramAddress = 0x0001_0000;

        bus.Memory.Write32(callbackAddress, 0x806D_FFFC);
        bus.Memory.Write32(callbackAddress + 4, 0x3803_FFFF);
        bus.Memory.Write32(callbackAddress + 8, 0x900D_FFFC);
        bus.Memory.Write32(callbackAddress + 12, 0x4E80_0020);
        bus.Memory.Write32(sourceAddress, 0xAABB_CCDD);

        bus.Write32(descriptorAddress, 0x1234_5678);
        bus.Write32(descriptorAddress + 0x0C, sourceAddress);
        bus.Write32(descriptorAddress + 0x10, aramAddress);
        bus.Write32(descriptorAddress + 0x14, 0x20);
        bus.Write32(descriptorAddress + 0x18, callbackAddress);
        bus.Write32(counterAddress, 1);

        bus.Advance(511);

        Assert.Equal(1u, bus.Memory.Read32(counterAddress));
        Assert.Equal(0, bus.ReadAram8(aramAddress));

        bus.Advance(1);

        Assert.Equal(0u, bus.Memory.Read32(counterAddress));
        Assert.Equal(0xAA, bus.ReadAram8(aramAddress));
        Assert.Equal(0xBB, bus.ReadAram8(aramAddress + 1));
        Assert.Equal(0xCC, bus.ReadAram8(aramAddress + 2));
        Assert.Equal(0xDD, bus.ReadAram8(aramAddress + 3));
    }

    [Fact]
    public void ArqDescriptorCompletionRunsGlobalWordSetCallback()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x8000_3000,
        };

        uint descriptorAddress = 0x8000_1000;
        uint callbackAddress = 0x8000_2000;
        uint globalAddress = 0x8000_5000;
        uint globalPointerAddress = bus.SmallDataBaseRegister - 4;
        uint sourceAddress = 0x8000_4000;
        uint aramAddress = 0x0001_0000;

        bus.Memory.Write32(callbackAddress, 0x3800_0001);
        bus.Memory.Write32(callbackAddress + 4, 0x806D_FFFC);
        bus.Memory.Write32(callbackAddress + 8, 0x9003_032C);
        bus.Memory.Write32(callbackAddress + 12, 0x4E80_0020);
        bus.Memory.Write32(globalPointerAddress, globalAddress);
        bus.Memory.Write32(sourceAddress, 0xAABB_CCDD);

        bus.Write32(descriptorAddress, 0x1234_5678);
        bus.Write32(descriptorAddress + 0x0C, sourceAddress);
        bus.Write32(descriptorAddress + 0x10, aramAddress);
        bus.Write32(descriptorAddress + 0x14, 0x20);
        bus.Write32(descriptorAddress + 0x18, callbackAddress);

        bus.Advance(512);

        Assert.Equal(1u, bus.Memory.Read32(globalAddress + 0x32C));
        Assert.Equal(0xAA, bus.ReadAram8(aramAddress));
    }

    [Fact]
    public void ArqManagerCompletionPromotesNextLinkedDescriptor()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x8000_3000,
        };

        uint firstDescriptorAddress = 0x8000_1000;
        uint secondDescriptorAddress = 0x8000_1020;
        uint callbackAddress = 0x8000_2000;
        uint counterAddress = 0x8000_2FFC;
        uint firstSourceAddress = 0x8000_4000;
        uint secondSourceAddress = 0x8000_5000;
        uint firstAramAddress = 0x0001_0000;
        uint secondAramAddress = 0x0001_0020;
        uint queueHeadAddress = bus.SmallDataBaseRegister + 0x33D8;
        uint activeDescriptorAddress = bus.SmallDataBaseRegister + 0x33E8;
        uint activeCallbackAddress = bus.SmallDataBaseRegister + 0x33F0;

        bus.Memory.Write32(callbackAddress, 0x806D_FFFC);
        bus.Memory.Write32(callbackAddress + 4, 0x3803_FFFF);
        bus.Memory.Write32(callbackAddress + 8, 0x900D_FFFC);
        bus.Memory.Write32(callbackAddress + 12, 0x4E80_0020);
        bus.Memory.Write32(counterAddress, 1);
        bus.Memory.Write32(firstSourceAddress, 0xAABB_CCDD);
        bus.Memory.Write32(secondSourceAddress, 0x1122_3344);

        WriteLinkedArqDescriptor(bus, firstDescriptorAddress, nextDescriptorAddress: 0, firstSourceAddress, firstAramAddress, length: 4, callbackAddress);
        WriteLinkedArqDescriptor(bus, secondDescriptorAddress, nextDescriptorAddress: 0, secondSourceAddress, secondAramAddress, length: 4, callbackAddress);

        bus.Memory.Write32(queueHeadAddress, secondDescriptorAddress);
        bus.Write32(activeDescriptorAddress, firstDescriptorAddress);
        bus.Write32(activeCallbackAddress, callbackAddress);

        bus.Advance(512);

        Assert.Equal(0u, bus.Memory.Read32(counterAddress));
        Assert.Equal(secondDescriptorAddress, bus.Memory.Read32(activeDescriptorAddress));
        Assert.Equal(callbackAddress, bus.Memory.Read32(activeCallbackAddress));
        Assert.Equal(0xAA, bus.ReadAram8(firstAramAddress));
        Assert.Equal(0xBB, bus.ReadAram8(firstAramAddress + 1));
        Assert.Equal(0xCC, bus.ReadAram8(firstAramAddress + 2));
        Assert.Equal(0xDD, bus.ReadAram8(firstAramAddress + 3));

        bus.Advance(512);

        Assert.Equal(0u, bus.Memory.Read32(activeDescriptorAddress));
        Assert.Equal(0u, bus.Memory.Read32(activeCallbackAddress));
        Assert.Equal(0x11, bus.ReadAram8(secondAramAddress));
        Assert.Equal(0x22, bus.ReadAram8(secondAramAddress + 1));
        Assert.Equal(0x33, bus.ReadAram8(secondAramAddress + 2));
        Assert.Equal(0x44, bus.ReadAram8(secondAramAddress + 3));
    }

    [Fact]
    public void ArqManagerSchedulesCallbackAnchoredDescriptorWithoutMagic()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x8000_3000,
        };

        uint descriptorAddress = 0x8000_1000;
        uint callbackAddress = 0x8000_2000;
        uint globalAddress = 0x8000_5000;
        uint globalPointerAddress = bus.SmallDataBaseRegister - 4;
        uint sourceAddress = 0x8000_4000;
        uint aramAddress = 0x0001_0000;
        uint activeDescriptorAddress = bus.SmallDataBaseRegister + 0x33E8;

        bus.Memory.Write32(callbackAddress, 0x3800_0001);
        bus.Memory.Write32(callbackAddress + 4, 0x806D_FFFC);
        bus.Memory.Write32(callbackAddress + 8, 0x9003_032C);
        bus.Memory.Write32(callbackAddress + 12, 0x4E80_0020);
        bus.Memory.Write32(globalPointerAddress, globalAddress);
        bus.Memory.Write32(sourceAddress, 0xAABB_CCDD);

        bus.Write32(descriptorAddress + 0x10, sourceAddress);
        bus.Write32(descriptorAddress + 0x14, aramAddress);
        bus.Write32(descriptorAddress + 0x18, 0x20);
        bus.Write32(descriptorAddress + 0x1C, callbackAddress);
        bus.Write32(activeDescriptorAddress, descriptorAddress);

        bus.Advance(512);

        Assert.Equal(1u, bus.Memory.Read32(globalAddress + 0x32C));
        Assert.Equal(0xAA, bus.ReadAram8(aramAddress));
    }

    [Fact]
    public void PikminResourceConstructionRegistersResourceTableEntry()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x803E_4C80,
        };

        uint resourceAddress = 0x8172_9C40;
        uint countAddress = bus.SmallDataBaseRegister + 0x2C2C;
        uint tableAddress = 0x8031_F8E0;

        bus.Memory.Write32(resourceAddress + 0x3B4, 0x802A_5D14);

        bus.Write32(resourceAddress + 0x3CC, 0x0002_0000);

        Assert.Equal(1u, bus.Memory.Read32(countAddress));
        Assert.Equal(resourceAddress, bus.Memory.Read32(tableAddress));
        Assert.Equal(0x0002_0000u, bus.Memory.Read32(tableAddress + 4));
        Assert.Equal(1, bus.Memory.Read8(resourceAddress + 0x3E2));

        bus.Write32(resourceAddress + 0x3CC, 0x0002_0000);

        Assert.Equal(1u, bus.Memory.Read32(countAddress));

        bus.Memory.Write32(countAddress, 0);
        bus.Memory.Write32(tableAddress, 0);
        bus.Memory.Write32(tableAddress + 4, 0);

        Assert.Equal(1u, bus.Read32(countAddress));
        Assert.Equal(resourceAddress, bus.Memory.Read32(tableAddress));
        Assert.Equal(0x0002_0000u, bus.Memory.Read32(tableAddress + 4));

        bus.Memory.Write32(countAddress, 0);
        bus.Write32(resourceAddress + 0x3CC, 0x0002_0000);

        Assert.Equal(1u, bus.Memory.Read32(countAddress));
    }

    [Fact]
    public void PikminResourceRepairUsesPlaceholderWhenPointerSlotsAreNotReady()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x803E_4C80,
        };

        uint resourceAddress = 0x8172_9C40;
        uint placeholderAddress = 0x8037_0000;
        uint countAddress = bus.SmallDataBaseRegister + 0x2C2C;
        uint tableAddress = 0x8031_F8E0;

        bus.Memory.Write32(resourceAddress + 0x3B4, 0x802A_5D14);
        bus.Write32(resourceAddress + 0x3CC, 0x0002_0000);
        bus.Memory.Write32(countAddress, 0);
        bus.Memory.Write32(tableAddress, 0);
        bus.Memory.Write32(tableAddress + 4, 0);
        bus.Memory.Write32(resourceAddress + 0x20, 0x1234_5678);
        bus.Memory.Write32(resourceAddress + 0x44, 0x3F80_0000);

        Assert.Equal(1u, bus.Read32(countAddress));
        Assert.Equal(placeholderAddress, bus.Memory.Read32(tableAddress));
        Assert.Equal(0x0002_0000u, bus.Memory.Read32(tableAddress + 4));
        Assert.Equal(0x1234_5678u, bus.Memory.Read32(placeholderAddress + 0x20));
        Assert.Equal(0u, bus.Memory.Read32(placeholderAddress + 0x44));
        Assert.Equal(0x802A_5D14u, bus.Memory.Read32(placeholderAddress + 0x3B4));
        Assert.Equal(0x0002_0000u, bus.Memory.Read32(placeholderAddress + 0x3CC));
    }

    [Fact]
    public void PikminResourceRepairUpgradesPlaceholderWhenRealResourceBecomesValid()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x803E_4C80,
        };

        uint resourceAddress = 0x8172_9C40;
        uint placeholderAddress = 0x8037_0000;
        uint countAddress = bus.SmallDataBaseRegister + 0x2C2C;
        uint tableAddress = 0x8031_F8E0;

        bus.Memory.Write32(resourceAddress + 0x3B4, 0x802A_5D14);
        bus.Memory.Write32(resourceAddress + 0x44, 0x3F80_0000);
        bus.Write32(resourceAddress + 0x3CC, 0x0002_0000);

        Assert.Equal(placeholderAddress, bus.Memory.Read32(tableAddress));

        bus.Memory.Write32(resourceAddress + 0x44, 0);

        Assert.Equal(1u, bus.Read32(countAddress));
        Assert.Equal(resourceAddress, bus.Memory.Read32(tableAddress));
        Assert.Equal(0x0002_0000u, bus.Memory.Read32(tableAddress + 4));
        Assert.Equal(1, bus.Memory.Read8(resourceAddress + 0x3E2));
    }

    [Fact]
    public void AramDmaCompletesSlotClearDspTaskCallbacks()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x8000_3000,
        };

        uint callbackAddress = 0x8000_2000;
        uint globalBase = 0x8001_0000;
        uint globalBasePointerAddress = 0x8000_2FFC;
        uint flagAddress = globalBase + 0x505A;

        bus.Memory.Write32(globalBasePointerAddress, globalBase);
        bus.Memory.Write32(callbackAddress + 0x18, 0x80AD_FFFC);
        bus.Memory.Write32(callbackAddress + 0x20, 0x3804_4858);
        bus.Memory.Write32(callbackAddress + 0x40, 0x7C65_3214);
        bus.Memory.Write32(callbackAddress + 0x44, 0x3800_0000);
        bus.Memory.Write32(callbackAddress + 0x48, 0x9803_5058);
        bus.Memory.Write8(flagAddress, 1);

        bus.Write32(0x8000_1000, callbackAddress);
        bus.Write32(0xCC00_5028, 0);

        Assert.Equal(0, bus.Memory.Read8(flagAddress));
    }

    private static void WriteLinkedArqDescriptor(GameCubeBus bus, uint descriptorAddress, uint nextDescriptorAddress, uint mainAddress, uint aramAddress, uint length, uint callbackAddress)
    {
        bus.Memory.Write32(descriptorAddress, nextDescriptorAddress);
        bus.Memory.Write32(descriptorAddress + 4, 0x1234_5678);
        bus.Memory.Write32(descriptorAddress + 8, 0);
        bus.Memory.Write32(descriptorAddress + 0x10, mainAddress);
        bus.Memory.Write32(descriptorAddress + 0x14, aramAddress);
        bus.Memory.Write32(descriptorAddress + 0x18, length);
        bus.Memory.Write32(descriptorAddress + 0x1C, callbackAddress);
    }

    [Fact]
    public void GraphicsCommandCallbackClearsCompletionFlag()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x8000_3000,
        };

        uint callbackAddress = 0x8000_2000;
        uint globalBasePointerAddress = 0x8000_2FFC;
        uint globalBase = 0x8001_0000;
        uint commandAddress = globalBase + 0x4858 + 2 * 0x20;
        uint flagAddress = globalBase + 0x5058 + 2;

        bus.Memory.Write32(globalBasePointerAddress, globalBase);
        bus.Memory.Write32(callbackAddress + 0x18, 0x80AD_FFFC);
        bus.Memory.Write8(flagAddress, 1);

        bus.Write32(commandAddress + 0x1C, callbackAddress);

        Assert.Equal(0, bus.Memory.Read8(flagAddress));
    }

    [Fact]
    public void DspMailboxSdkHandshakeCommandsReceiveProgressReplies()
    {
        GameCubeBus bus = new();
        Assert.Equal(0x8071, bus.Read16(0xCC00_5004) & 0xFFFF);
        Assert.Equal(0xFEED, bus.Read16(0xCC00_5006));

        bus.Write16(0xCC00_5002, 0x0000);
        bus.Write16(0xCC00_5000, 0xDCD1);

        Assert.Equal(0xDCD1, bus.Read16(0xCC00_5004));
        Assert.Equal(0x0001, bus.Read16(0xCC00_5006));
    }

    [Fact]
    public void AudioInterfaceSampleCounterAdvancesWithBusTime()
    {
        GameCubeBus bus = new();

        bus.Advance(7);

        Assert.Equal(7u, bus.Read32(0xCC00_6C08));

        bus.Write32(0xCC00_6C00, GameCubeBus.AudioInterfaceSampleCounterReset);

        Assert.Equal(0u, bus.Read32(0xCC00_6C08));
    }

    [Fact]
    public void AudioInterfaceSampleCounterMatchRaisesAndClearsInterrupt()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceAudioInterrupt);
        bus.Write32(0xCC00_6C0C, 5);
        bus.Write32(0xCC00_6C00, GameCubeBus.AudioInterfaceInterruptMask);

        bus.Advance(5);

        Assert.Equal(GameCubeBus.AudioInterfaceInterruptStatus, bus.Read32(0xCC00_6C00) & GameCubeBus.AudioInterfaceInterruptStatus);
        Assert.True(bus.HasPendingExternalInterrupt);

        bus.Write32(0xCC00_6C00, GameCubeBus.AudioInterfaceInterruptMask | GameCubeBus.AudioInterfaceInterruptStatus);

        Assert.Equal(0u, bus.Read32(0xCC00_6C00) & GameCubeBus.AudioInterfaceInterruptStatus);
        Assert.False(bus.HasPendingExternalInterrupt);
    }

    [Fact]
    public void AramDmaCopiesMainMemoryToAramAndBack()
    {
        GameCubeBus bus = new();
        byte[] source = Enumerable.Range(0, 0x20).Select(value => (byte)(value + 1)).ToArray();
        bus.Memory.Load(0x8000_2000, source);

        bus.Write32(0xCC00_5020, 0x8000_2000);
        bus.Write32(0xCC00_5024, 0x0000_0040);
        bus.Write32(0xCC00_5028, 0x0000_0020);
        bus.Advance(0x1000);

        for (uint index = 0; index < source.Length; index++)
        {
            Assert.Equal(source[index], bus.ReadAram8(0x40 + index));
        }

        bus.WriteAram8(0x40, 0xFE);
        bus.Write32(0xCC00_5020, 0x8000_2400);
        bus.Write32(0xCC00_5024, 0x0000_0040);
        bus.Write32(0xCC00_5028, 0x8000_0020);
        bus.Advance(0x1000);

        Assert.Equal(0xFE, bus.Memory.Read8(0x8000_2400));
        Assert.Equal(source[1], bus.Memory.Read8(0x8000_2401));
        Assert.Equal(0x8000_2420u, bus.Read32(0xCC00_5020));
        Assert.Equal(0x0000_0060u, bus.Read32(0xCC00_5024));
        Assert.Equal(0x8000_0000u, bus.Read32(0xCC00_5028));

        bus.WriteAram8(0x60, 0xAB);
        bus.Write16(0xCC00_502A, 0x0020);
        bus.Advance(0x1000);

        Assert.Equal(0xFE, bus.Memory.Read8(0x8000_2400));
        Assert.Equal(0xAB, bus.Memory.Read8(0x8000_2420));
        Assert.Equal(0x8000_2440u, bus.Read32(0xCC00_5020));
        Assert.Equal(0x0000_0080u, bus.Read32(0xCC00_5024));
        Assert.Equal(0x8000_0000u, bus.Read32(0xCC00_5028));
        Assert.Equal(GameCubeBus.DspControlAramInterruptStatus, bus.Read16(0xCC00_500A) & GameCubeBus.DspControlAramInterruptStatus);
    }

    [Fact]
    public void AramDmaIgnoresRepeatedStartWritesWhileBusy()
    {
        GameCubeBus bus = new();
        bus.Memory.Write32(0x8000_2400, 0);
        bus.WriteAram8(0x40, 0xFE);
        bus.WriteAram8(0x60, 0xAB);

        bus.Write32(0xCC00_5020, 0x8000_2400);
        bus.Write32(0xCC00_5024, 0x0000_0040);
        bus.Write32(0xCC00_5028, 0x8000_0020);
        bus.Write16(0xCC00_502A, 0x0020);

        Assert.Equal(0, bus.Memory.Read8(0x8000_2400));

        bus.Advance(0x1000);

        Assert.Equal(0xFE, bus.Memory.Read8(0x8000_2400));
        Assert.Equal(0, bus.Memory.Read8(0x8000_2420));
        Assert.Equal(0x8000_2420u, bus.Read32(0xCC00_5020));
        Assert.Equal(0x0000_0060u, bus.Read32(0xCC00_5024));
    }

    [Fact]
    public void AramModeReportsNormalAfterInitialization()
    {
        GameCubeBus bus = new();

        Assert.Equal(1u, bus.Read16(0xCC00_5016) & 1u);
    }

    [Fact]
    public void DiscInterfaceReadCommandCopiesDiscBytesAndRaisesInterrupt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.iso");
        try
        {
            byte[] image = new byte[0x300];
            image[0x200] = 0xDE;
            image[0x201] = 0xAD;
            image[0x202] = 0xBE;
            image[0x203] = 0xEF;
            File.WriteAllBytes(path, image);

            using DiscImageReader disc = DiscImageReader.Open(path);
            GameCubeBus bus = new(disc);
            bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceDiscInterrupt);
            bus.Write32(0xCC00_6000, GameCubeBus.DiscInterfaceInterruptMask);
            bus.Write32(0xCC00_6008, 0xA800_0000);
            bus.Write32(0xCC00_600C, 0x0000_0080);
            bus.Write32(0xCC00_6010, 0x20);
            bus.Write32(0xCC00_6014, 0x8000_1000);
            bus.Write32(0xCC00_6018, 0x20);

            bus.Write32(0xCC00_601C, 1);

            Assert.Equal(0u, bus.Memory.Read32(0x8000_1000));
            Assert.Equal(0u, bus.Read32(0xCC00_6000) & GameCubeBus.DiscInterfaceTransferComplete);
            Assert.Equal(1u, bus.Read32(0xCC00_601C) & 1u);
            Assert.False(bus.HasPendingExternalInterrupt);

            bus.Advance(10_000);

            Assert.Equal(0xDEAD_BEEFu, bus.Memory.Read32(0x8000_1000));
            Assert.Equal(GameCubeBus.DiscInterfaceTransferComplete, bus.Read32(0xCC00_6000) & GameCubeBus.DiscInterfaceTransferComplete);
            Assert.Equal(0u, bus.Read32(0xCC00_6018));
            Assert.True(bus.HasPendingExternalInterrupt);

            bus.Write32(0xCC00_6000, GameCubeBus.DiscInterfaceTransferComplete);

            Assert.False(bus.HasPendingExternalInterrupt);
            Assert.Equal(0u, bus.Read32(0xCC00_6000) & GameCubeBus.DiscInterfaceTransferComplete);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DiscInterfaceCoverRegisterReportsClosedCoverAndPreservesMask()
    {
        GameCubeBus bus = new();

        Assert.Equal(0u, bus.Read32(0xCC00_6004) & GameCubeBus.DiscInterfaceCoverOpened);

        bus.Write32(
            0xCC00_6004,
            GameCubeBus.DiscInterfaceCoverOpened |
            GameCubeBus.DiscInterfaceCoverInterruptMask |
            GameCubeBus.DiscInterfaceCoverInterruptStatus);

        Assert.Equal(GameCubeBus.DiscInterfaceCoverInterruptMask, bus.Read32(0xCC00_6004));

        bus.Write32(0xCC00_6004, 0);

        Assert.Equal(0u, bus.Read32(0xCC00_6004));
    }

    [Fact]
    public void DiscInterfaceConfigurationRegisterIsReadOnly()
    {
        GameCubeBus bus = new();

        Assert.Equal(GameCubeBus.DiscInterfaceConfiguration, bus.Read32(0xCC00_6024));

        bus.Write32(0xCC00_6024, 0xFFFF_FFFF);

        Assert.Equal(GameCubeBus.DiscInterfaceConfiguration, bus.Read32(0xCC00_6024));
    }

    [Fact]
    public void DiscInterfaceInquiryWritesBasicDriveDescriptor()
    {
        GameCubeBus bus = new();
        bus.Write32(0xCC00_6008, 0x1200_0000);
        bus.Write32(0xCC00_6014, 0x8000_1200);
        bus.Write32(0xCC00_6018, 0x20);

        bus.Write32(0xCC00_601C, 1);
        bus.Advance(10_000);

        Assert.Equal(2u, bus.Memory.Read32(0x8000_1200));
        Assert.Equal(GameCubeBus.DiscInterfaceTransferComplete, bus.Read32(0xCC00_6000) & GameCubeBus.DiscInterfaceTransferComplete);
    }

    [Fact]
    public void DiscInterfaceReadDiscIdCopiesHeaderAndInitializesDrive()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.iso");
        try
        {
            byte[] image = new byte[0x100];
            "GPIE01"u8.CopyTo(image);
            image[0x1C] = 0xC2;
            image[0x1D] = 0x33;
            image[0x1E] = 0x9F;
            image[0x1F] = 0x3D;
            File.WriteAllBytes(path, image);

            using DiscImageReader disc = DiscImageReader.Open(path);
            GameCubeBus bus = new(disc);
            bus.Write32(0xCC00_6008, 0xA800_0040);
            bus.Write32(0xCC00_6014, 0x8000_1400);
            bus.Write32(0xCC00_6018, 0x20);

            bus.Write32(0xCC00_601C, 1);
            bus.Advance(10_000);

            Assert.Equal(0x4750_4945u, bus.Memory.Read32(0x8000_1400));
            Assert.Equal(0xC233_9F3Du, bus.Memory.Read32(0x8000_141C));
            Assert.Equal(GameCubeBus.DiscInterfaceTransferComplete, bus.Read32(0xCC00_6000) & GameCubeBus.DiscInterfaceTransferComplete);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DiscInterfaceRequestErrorReportsAndClearsLastError()
    {
        GameCubeBus bus = new();
        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceDiscInterrupt);
        bus.Write32(0xCC00_6000, GameCubeBus.DiscInterfaceDeviceErrorInterruptMask);
        bus.Write32(0xCC00_6008, 0xA800_0040);
        bus.Write32(0xCC00_6014, 0x8000_1600);
        bus.Write32(0xCC00_6018, 0x20);

        bus.Write32(0xCC00_601C, 1);
        bus.Advance(10_000);

        Assert.Equal(GameCubeBus.DiscInterfaceDeviceErrorInterruptStatus, bus.Read32(0xCC00_6000) & GameCubeBus.DiscInterfaceDeviceErrorInterruptStatus);
        Assert.True(bus.HasPendingExternalInterrupt);

        bus.Write32(0xCC00_6008, 0xE000_0000);
        bus.Write32(0xCC00_601C, 1);
        bus.Advance(10_000);

        Assert.NotEqual(0u, bus.Read32(0xCC00_6020));

        bus.Write32(0xCC00_6008, 0xE000_0000);
        bus.Write32(0xCC00_601C, 1);
        bus.Advance(10_000);

        Assert.Equal(0u, bus.Read32(0xCC00_6020));
    }

    [Fact]
    public void DiscInterfaceSeekStopMotorAndAudioStatusUseImmediateBuffer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.iso");
        try
        {
            byte[] image = new byte[0x1000];
            image[0x1C] = 0xC2;
            image[0x1D] = 0x33;
            image[0x1E] = 0x9F;
            image[0x1F] = 0x3D;
            File.WriteAllBytes(path, image);

            using DiscImageReader disc = DiscImageReader.Open(path);
            GameCubeBus bus = new(disc);

            bus.Write32(0xCC00_6008, 0xE401_0000);
            bus.Write32(0xCC00_601C, 1);
            bus.Advance(10_000);
            bus.Write32(0xCC00_6008, 0xE100_0000);
            bus.Write32(0xCC00_600C, 0x20);
            bus.Write32(0xCC00_6010, 0x40);
            bus.Write32(0xCC00_601C, 1);
            bus.Advance(10_000);
            bus.Write32(0xCC00_6008, 0xE200_0000);
            bus.Write32(0xCC00_601C, 1);
            bus.Advance(10_000);

            Assert.Equal(0x3u, bus.Read32(0xCC00_6020) & 0x7u);

            bus.Write32(0xCC00_6008, 0xAB00_0000);
            bus.Write32(0xCC00_600C, 0x20);
            bus.Write32(0xCC00_601C, 1);
            bus.Advance(10_000);
            bus.Write32(0xCC00_6008, 0xE300_0000);
            bus.Write32(0xCC00_601C, 1);
            bus.Advance(10_000);

            Assert.Equal(GameCubeBus.DiscInterfaceTransferComplete, bus.Read32(0xCC00_6000) & GameCubeBus.DiscInterfaceTransferComplete);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VideoScanlineAdvancesWithBusTime()
    {
        GameCubeBus bus = new();

        Assert.Equal(0, bus.Read16(0xCC00_206C));

        bus.Advance((ulong)GameCubeBus.VideoCyclesPerScanline * 42);

        Assert.Equal(42, bus.Read16(0xCC00_206C));
        Assert.False(bus.IsVideoInVBlank);

        bus.Advance((ulong)GameCubeBus.VideoCyclesPerScanline * (GameCubeBus.VideoVisibleLines - 42));

        Assert.Equal(GameCubeBus.VideoVisibleLines, bus.Read16(0xCC00_206C));
        Assert.True(bus.IsVideoInVBlank);
    }

    [Fact]
    public void VideoDisplayInterruptRaisesProcessorInterfaceInterruptOnProgrammedLine()
    {
        GameCubeBus bus = new();

        bus.Write16(0xCC00_2030, (ushort)(GameCubeBus.VideoInterruptEnable | GameCubeBus.VideoVisibleLines));
        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceVideoInterrupt);
        bus.Advance((ulong)GameCubeBus.VideoCyclesPerScanline * GameCubeBus.VideoVisibleLines);

        Assert.True(bus.HasPendingExternalInterrupt);
        Assert.Equal(GameCubeBus.ProcessorInterfaceVideoInterrupt, bus.PendingProcessorInterrupts);
        Assert.Equal(GameCubeBus.ProcessorInterfaceVideoInterrupt, bus.ProcessorInterruptCause & GameCubeBus.ProcessorInterfaceVideoInterrupt);
        Assert.Equal(GameCubeBus.ProcessorInterfaceVideoInterrupt, bus.ProcessorInterruptMask);
        Assert.Equal(GameCubeBus.ProcessorInterfaceVideoInterrupt, bus.Read32(0xCC00_3000) & GameCubeBus.ProcessorInterfaceVideoInterrupt);
        Assert.Equal(GameCubeBus.VideoInterruptStatus, bus.Read16(0xCC00_2030) & GameCubeBus.VideoInterruptStatus);

        bus.Write16(0xCC00_2030, 0);

        Assert.False(bus.HasPendingExternalInterrupt);
        Assert.Equal(0u, bus.PendingProcessorInterrupts);
        Assert.Equal(0u, bus.Read32(0xCC00_3000) & GameCubeBus.ProcessorInterfaceVideoInterrupt);
        Assert.Equal(0, bus.Read16(0xCC00_2030) & GameCubeBus.VideoInterruptStatus);
    }

    [Fact]
    public void VideoDisplayInterruptStatusLatchesWhenInterruptIsDisabled()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceVideoInterrupt);
        bus.Write16(0xCC00_2038, 42);

        bus.Advance((ulong)GameCubeBus.VideoCyclesPerScanline * 42);

        Assert.Equal(GameCubeBus.VideoInterruptStatus, bus.Read16(0xCC00_2038) & GameCubeBus.VideoInterruptStatus);
        Assert.False(bus.HasPendingExternalInterrupt);
        Assert.Equal(0u, bus.Read32(0xCC00_3000) & GameCubeBus.ProcessorInterfaceVideoInterrupt);
    }

    [Fact]
    public void VideoProcessorInterruptClearsWhenOnlyDisabledStatusRemains()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceVideoInterrupt);
        bus.Write16(0xCC00_2030, (ushort)(GameCubeBus.VideoInterruptEnable | 42));
        bus.Write16(0xCC00_2038, 42);

        bus.Advance((ulong)GameCubeBus.VideoCyclesPerScanline * 42);

        Assert.True(bus.HasPendingExternalInterrupt);
        Assert.Equal(GameCubeBus.VideoInterruptStatus, bus.Read16(0xCC00_2038) & GameCubeBus.VideoInterruptStatus);

        bus.Write16(0xCC00_2030, (ushort)(GameCubeBus.VideoInterruptEnable | 42));

        Assert.Equal(GameCubeBus.VideoInterruptStatus, bus.Read16(0xCC00_2038) & GameCubeBus.VideoInterruptStatus);
        Assert.False(bus.HasPendingExternalInterrupt);
        Assert.Equal(0u, bus.Read32(0xCC00_3000) & GameCubeBus.ProcessorInterfaceVideoInterrupt);
    }

    [Fact]
    public void ProcessorInterfaceReportsResetSwitchReleasedWithoutPendingInterrupt()
    {
        GameCubeBus bus = new();

        Assert.False(bus.HasPendingExternalInterrupt);
        Assert.Equal(
            GameCubeBus.ProcessorInterfaceResetSwitchReleased,
            bus.Read32(0xCC00_3000) & GameCubeBus.ProcessorInterfaceResetSwitchReleased);
    }

    [Fact]
    public void SerialInterfaceTypeCommandReportsStandardController()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_6434, 0x8000_0000);
        bus.Write32(0xCC00_6400, 0x0000_0000);
        bus.Write32(0xCC00_6434, 0x0000_0001);

        Assert.Equal(GameCubeBus.SerialInterfaceStandardControllerType, bus.Read32(0xCC00_6404));
        Assert.Equal(GameCubeBus.SerialInterfaceNeutralControllerLow, bus.Read32(0xCC00_6408));
        Assert.Equal(0u, bus.Read32(0xCC00_6434) & 1);
        Assert.Equal(0u, bus.Read32(0xCC00_6438) & 0x2000_0000u);
    }

    [Fact]
    public void SerialInterfaceImmediateTransferRaisesAndClearsMaskedProcessorInterrupt()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceSerialInterrupt);
        bus.Write32(0xCC00_6400, 0x0000_0000);
        bus.Write32(0xCC00_6434, GameCubeBus.SerialInterfaceTransferCompleteMask | 1);

        Assert.True(bus.HasPendingExternalInterrupt);
        Assert.Equal(GameCubeBus.ProcessorInterfaceSerialInterrupt, bus.Read32(0xCC00_3000) & GameCubeBus.ProcessorInterfaceSerialInterrupt);
        Assert.Equal(GameCubeBus.SerialInterfaceTransferCompleteStatus, bus.Read32(0xCC00_6434) & GameCubeBus.SerialInterfaceTransferCompleteStatus);

        bus.Write32(0xCC00_6434, GameCubeBus.SerialInterfaceTransferCompleteStatus);

        Assert.False(bus.HasPendingExternalInterrupt);
        Assert.Equal(0u, bus.Read32(0xCC00_3000) & GameCubeBus.ProcessorInterfaceSerialInterrupt);
        Assert.Equal(0u, bus.Read32(0xCC00_6434) & GameCubeBus.SerialInterfaceTransferCompleteStatus);
    }

    [Fact]
    public void SerialInterfacePollingCommandReturnsNeutralControllerState()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_6400, 0x0040_0300);

        Assert.Equal(GameCubeBus.SerialInterfaceNeutralControllerHigh, bus.Read32(0xCC00_6404));
        Assert.Equal(GameCubeBus.SerialInterfaceNeutralControllerLow, bus.Read32(0xCC00_6408));
    }

    [Fact]
    public void SerialInterfacePollingCommandReturnsConfiguredControllerButtons()
    {
        GameCubeBus bus = new()
        {
            SerialInterfaceControllerButtons = GameCubeBus.SerialInterfaceControllerButtonStart | GameCubeBus.SerialInterfaceControllerButtonX,
        };

        bus.Write32(0xCC00_6400, 0x0040_0300);

        Assert.Equal(0x1400_8080u, bus.Read32(0xCC00_6404));
        Assert.Equal(GameCubeBus.SerialInterfaceNeutralControllerLow, bus.Read32(0xCC00_6408));
    }

    [Fact]
    public void SerialInterfacePollRegisterCapturesEnabledControllerAndRaisesReadStatusInterrupt()
    {
        GameCubeBus bus = new()
        {
            SerialInterfaceControllerButtons = GameCubeBus.SerialInterfaceControllerButtonA,
        };

        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceSerialInterrupt);
        bus.Write32(0xCC00_6434, GameCubeBus.SerialInterfaceReadStatusInterruptMask);
        bus.Write32(0xCC00_6400, 0x0040_0300);
        bus.Write32(0xCC00_6430, 0x00F7_0100 | 0x10);

        Assert.Equal(GameCubeBus.SerialInterfaceReadStatusInterruptStatus, bus.Read32(0xCC00_6434) & GameCubeBus.SerialInterfaceReadStatusInterruptStatus);
        Assert.True(bus.HasPendingExternalInterrupt);
        Assert.Equal(0x2000_0000u, bus.Read32(0xCC00_6438) & 0x2000_0000u);
        Assert.Equal(0x0100_8080u, bus.Read32(0xCC00_6404));
        Assert.Equal(0u, bus.Read32(0xCC00_6438) & 0x2000_0000u);
        Assert.Equal(0u, bus.Read32(0xCC00_6434) & GameCubeBus.SerialInterfaceReadStatusInterruptStatus);
        Assert.False(bus.HasPendingExternalInterrupt);

        bus.Write32(0xCC00_6430, 0x00F7_0100 | 0x10);
        Assert.Equal(GameCubeBus.SerialInterfaceReadStatusInterruptStatus, bus.Read32(0xCC00_6434) & GameCubeBus.SerialInterfaceReadStatusInterruptStatus);
        bus.Write32(0xCC00_6434, GameCubeBus.SerialInterfaceReadStatusInterruptMask | GameCubeBus.SerialInterfaceReadStatusInterruptStatus);

        Assert.False(bus.HasPendingExternalInterrupt);
        Assert.Equal(0u, bus.Read32(0xCC00_6434) & GameCubeBus.SerialInterfaceReadStatusInterruptStatus);
    }

    [Fact]
    public void SerialInterfacePollRegisterRepeatsWhileEnabled()
    {
        GameCubeBus bus = new()
        {
            SerialInterfaceControllerButtons = GameCubeBus.SerialInterfaceControllerButtonA,
        };

        bus.Write32(0xCC00_6400, 0x0040_0300);
        bus.Write32(0xCC00_6430, 0x00F7_0100 | 0x10);
        Assert.Equal(0x2000_0000u, bus.Read32(0xCC00_6438) & 0x2000_0000u);
        Assert.Equal(0x0100_8080u, bus.Read32(0xCC00_6404));
        Assert.Equal(0u, bus.Read32(0xCC00_6438) & 0x2000_0000u);

        bus.Advance(4095);
        Assert.Equal(0u, bus.Read32(0xCC00_6438) & 0x2000_0000u);
        bus.Advance(1);

        Assert.Equal(0x2000_0000u, bus.Read32(0xCC00_6438) & 0x2000_0000u);
        Assert.Equal(0x0100_8080u, bus.Read32(0xCC00_6404));
    }

    [Fact]
    public void SerialInterfacePollRegisterSkipsDisconnectedPorts()
    {
        GameCubeBus bus = new()
        {
            SerialInterfaceControllerButtons = GameCubeBus.SerialInterfaceControllerButtonA,
        };

        bus.Write32(0xCC00_6400, 0x0040_0300);
        bus.Write32(0xCC00_640C, 0x0040_0300);
        bus.Write32(0xCC00_6418, 0x0040_0300);
        bus.Write32(0xCC00_6424, 0x0040_0300);
        bus.Write32(0xCC00_6430, 0x00F7_0100 | 0xF0);

        uint status = bus.Read32(0xCC00_6438);
        Assert.Equal(0x2000_0000u, status & 0x2000_0000u);
        Assert.Equal(0u, status & 0x0020_2020u);
        Assert.Equal(0u, status & 0x0008_0808u);
        Assert.Equal(0x0100_8080u, bus.Read32(0xCC00_6404));
        Assert.Equal(0xC000_0000u, bus.Read32(0xCC00_6410) & 0xC000_0000u);
    }

    [Fact]
    public void SerialInterfaceCommunicationTransferReportsNoResponseForDisconnectedPort()
    {
        GameCubeBus bus = new();

        bus.Write32(0xCC00_6434, 0xC001_0303);

        Assert.Equal(0x0008_0000u, bus.Read32(0xCC00_6438) & 0x0008_0000u);
        Assert.Equal(0xC000_0000u, bus.Read32(0xCC00_6410) & 0xC000_0000u);

        bus.Write32(0xCC00_6438, 0x0008_0000);

        Assert.Equal(0u, bus.Read32(0xCC00_6438) & 0x0008_0000u);
        Assert.Equal(0u, bus.Read32(0xCC00_6410) & 0xC000_0000u);
    }

    [Fact]
    public void SerialInterfaceCanConnectAdditionalControllerPorts()
    {
        GameCubeBus bus = new()
        {
            SerialInterfaceControllerPort1Connected = true,
            SerialInterfaceControllerButtons = GameCubeBus.SerialInterfaceControllerButtonA,
        };

        bus.Write32(0xCC00_640C, 0x0040_0300);
        bus.Write32(0xCC00_6430, 0x00F7_0100 | 0x20);

        Assert.Equal(0x0020_0000u, bus.Read32(0xCC00_6438) & 0x0020_0000u);
        Assert.Equal(0x0100_8080u, bus.Read32(0xCC00_6410));
        Assert.Equal(GameCubeBus.SerialInterfaceNeutralControllerLow, bus.Read32(0xCC00_6414));
    }

    [Fact]
    public void SerialInterfaceCommunicationTransferPopulatesIoBuffer()
    {
        GameCubeBus bus = new()
        {
            SerialInterfaceControllerButtons = GameCubeBus.SerialInterfaceControllerButtonStart,
        };

        bus.Write32(0xCC00_6400, 0x0040_0300);
        bus.Write32(0xCC00_6434, 0x0001_0301);

        Assert.Equal(0x1000_8080u, bus.Read32(0xCC00_6480));
        Assert.Equal(GameCubeBus.SerialInterfaceNeutralControllerLow, bus.Read32(0xCC00_6484));
        Assert.Equal(GameCubeBus.SerialInterfaceTransferCompleteStatus, bus.Read32(0xCC00_6434) & GameCubeBus.SerialInterfaceTransferCompleteStatus);
        Assert.Equal(0u, bus.Read32(0xCC00_6434) & 1);
    }

    [Fact]
    public void SerialInterfaceCommunicationTransferUsesIoBufferCommand()
    {
        GameCubeBus bus = new()
        {
            SerialInterfaceControllerButtons = GameCubeBus.SerialInterfaceControllerButtonA,
        };

        bus.Write32(0xCC00_6480, 0x4100_0000);
        bus.Write32(0xCC00_6434, 0xC001_0A01);

        Assert.Equal(0x0100_8080u, bus.Read32(0xCC00_6480));
        Assert.Equal(GameCubeBus.SerialInterfaceNeutralControllerLow, bus.Read32(0xCC00_6484));
        Assert.Equal(0u, bus.Read32(0xCC00_6488));
        Assert.Equal(GameCubeBus.SerialInterfaceTransferCompleteStatus, bus.Read32(0xCC00_6434) & GameCubeBus.SerialInterfaceTransferCompleteStatus);
    }

    [Fact]
    public void InterpreterTicksConcreteGameCubeBus()
    {
        GameCubeBus bus = new();
        uint pc = 0x8000_3100;
        bus.Write32(pc, 0x4800_0000);

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Step(state, bus);

        Assert.Equal(1ul, state.TimeBase);
        Assert.Equal(1ul, bus.VideoCycleCounter);
    }

    [Fact]
    public void InterpreterVectorsToExternalInterruptWhenPiInterruptIsPending()
    {
        GameCubeBus bus = new();
        uint pc = 0x8000_3100;
        bus.Write32(pc, 0x6000_0000);
        bus.Write16(0xCC00_2030, (ushort)(GameCubeBus.VideoInterruptEnable | GameCubeBus.VideoVisibleLines));
        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceVideoInterrupt);
        bus.Advance((ulong)GameCubeBus.VideoCyclesPerScanline * GameCubeBus.VideoVisibleLines);

        PowerPcState state = new()
        {
            Pc = pc,
            Msr = 0x8002,
        };

        uint instruction = new PowerPcInterpreter().Step(state, bus);

        Assert.Equal(0u, instruction);
        Assert.Equal(0x8000_0500u, state.Pc);
        Assert.Equal(pc, state.Spr[26]);
        Assert.Equal(0x8002u, state.Spr[27]);
        Assert.Equal(0u, state.Msr & 0x8000);
        Assert.Equal(0u, state.Msr & 0x0002);
    }

    [Fact]
    public void RejectsAddressesOutsideRamAndKnownMmio()
    {
        GameCubeBus bus = new();

        Assert.Throws<AddressTranslationException>(() => bus.Read32(0xD000_0000));
    }
}
