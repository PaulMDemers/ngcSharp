using System.Reflection;
using System.Text.Json;
using NgcSharp.App;
using NgcSharp.Core;
using NgcSharp.Cpu;

namespace NgcSharp.Tests;

public sealed class DolRunnerTests
{
    [Fact]
    public void RunCanStopBeforeFirstInstructionWhenCancelled()
    {
        string summaryPath = Path.Combine(Path.GetTempPath(), $"ngcsharp-cancel-{Guid.NewGuid():N}.json");
        try
        {
            DolFile dol = DolFile.Parse(SyntheticDolFactory.CreateSmokeTestDol());
            RunDolOptions options = new("game.dol", 1000, Trace: false, TracePath: null, DumpRegisters: false, DumpMmio: false, Quiet: true, RunSummaryPath: summaryPath);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            int executed = new DolRunner(TextWriter.Null, TextWriter.Null).Run(dol, options, new GameCubeBus(), stepObserver: null, cancellation.Token);

            Assert.Equal(0, executed);
            using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.Equal("cancelled", summary.RootElement.GetProperty("stopReason").GetString());
        }
        finally
        {
            File.Delete(summaryPath);
        }
    }

    [Fact]
    public void WriteWatchRangeMatchesMainRamAliases()
    {
        RunDolOptions options = new("game.dol", 1000, Trace: false, TracePath: null, DumpRegisters: false, DumpMmio: false, Quiet: true, WatchWriteRangeAddress: 0x0060_E4A0, WatchWriteRangeLength: 0x20);

        Assert.True(InvokeMatchesWriteWatchRange(options, 0x8060_E4B0, 4));
        Assert.True(InvokeMatchesWriteWatchRange(options, 0xC060_E4B0, 4));
        Assert.False(InvokeMatchesWriteWatchRange(options, 0x8060_E4C0, 4));
    }

    [Fact]
    public void AddressRangeOverlapMatchesMainRamAliases()
    {
        Assert.True(InvokeOverlapsAddressRange(0x0072_C700, 0x40, 0x8072_C600, 0x800));
        Assert.True(InvokeOverlapsAddressRange(0xC072_C700, 0x40, 0x0072_C600, 0x800));
        Assert.False(InvokeOverlapsAddressRange(0x8072_D000, 0x40, 0x0072_C600, 0x800));
    }

    [Fact]
    public void CtrDelayLoopFastForwardFinishesSelfBranch()
    {
        const uint pc = 0x8000_3000;
        GameCubeBus bus = new();
        WriteInstruction(bus.Memory, pc, 0x4200_0000);
        PowerPcState state = new()
        {
            Pc = pc,
            Ctr = 5,
        };
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardCtrDelayLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(5, skippedInstructions);
        Assert.Equal(pc + 4, state.Pc);
        Assert.Equal(0u, state.Ctr);
        Assert.Equal(5ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 5u, state.Spr[22]);
    }

    [Fact]
    public void CtrDelayLoopFastForwardStopsAtPositiveDecrementerEdge()
    {
        const uint pc = 0x8000_3000;
        GameCubeBus bus = new();
        WriteInstruction(bus.Memory, pc, 0x4200_0000);
        PowerPcState state = new()
        {
            Pc = pc,
            Ctr = 5,
        };
        state.Spr[22] = 2;

        bool skipped = InvokeFastForwardCtrDelayLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(2, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(3u, state.Ctr);
        Assert.Equal(2ul, state.TimeBase);
        Assert.Equal(0u, state.Spr[22]);
    }

    [Fact]
    public void CtrByteCopyLoopFastForwardCopiesUnrolledBlocks()
    {
        const uint pc = 0x8000_3000;
        const uint source = 0x8001_0000;
        const uint destination = 0x8001_1000;
        GameCubeBus bus = new();
        WriteCtrByteCopyLoop(bus.Memory, pc);
        for (uint offset = 0; offset < 24; offset++)
        {
            bus.Memory.Write8(source + offset, (byte)(0x40 + offset));
        }

        PowerPcState state = new()
        {
            Pc = pc,
            Ctr = 3,
        };
        state.Gpr[5] = source;
        state.Gpr[7] = destination;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardCtrByteCopyLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(57, skippedInstructions);
        Assert.Equal(pc + 0x4C, state.Pc);
        Assert.Equal(0u, state.Ctr);
        Assert.Equal(source + 24, state.Gpr[5]);
        Assert.Equal(destination + 24, state.Gpr[7]);
        Assert.Equal(0x57u, state.Gpr[0]);
        Assert.Equal(57ul, state.TimeBase);
        for (uint offset = 0; offset < 24; offset++)
        {
            Assert.Equal((byte)(0x40 + offset), bus.Memory.Read8(destination + offset));
        }
    }

    [Fact]
    public void CtrSingleByteCopyLoopFastForwardCopiesRemainder()
    {
        const uint pc = 0x8000_3000;
        const uint source = 0x8001_0000;
        const uint destination = 0x8001_1000;
        GameCubeBus bus = new();
        WriteCtrSingleByteCopyLoop(bus.Memory, pc);
        for (uint offset = 0; offset < 5; offset++)
        {
            bus.Memory.Write8(source + offset, (byte)(0x90 + offset));
        }

        PowerPcState state = new()
        {
            Pc = pc,
            Ctr = 5,
        };
        state.Gpr[5] = source;
        state.Gpr[7] = destination;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardCtrSingleByteCopyLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(25, skippedInstructions);
        Assert.Equal(pc + 0x14, state.Pc);
        Assert.Equal(0u, state.Ctr);
        Assert.Equal(source + 5, state.Gpr[5]);
        Assert.Equal(destination + 5, state.Gpr[7]);
        Assert.Equal(0x94u, state.Gpr[0]);
        for (uint offset = 0; offset < 5; offset++)
        {
            Assert.Equal((byte)(0x90 + offset), bus.Memory.Read8(destination + offset));
        }
    }

    [Fact]
    public void WordCopyLoopFastForwardCopiesUnrolledBlocks()
    {
        const uint pc = 0x8000_3000;
        const uint sourceBase = 0x8001_0000;
        const uint destinationBase = 0x8001_1000;
        GameCubeBus bus = new();
        WriteWordCopyLoop(bus.Memory, pc);
        for (uint offset = 0; offset < 64; offset += 4)
        {
            bus.Memory.Write32(sourceBase + 4 + offset, 0xA000_0000u + offset);
        }

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = destinationBase;
        state.Gpr[4] = 2;
        state.Gpr[6] = sourceBase;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardWordCopyLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(36, skippedInstructions);
        Assert.Equal(pc + 0x4C, state.Pc);
        Assert.Equal(destinationBase + 64, state.Gpr[3]);
        Assert.Equal(0u, state.Gpr[4]);
        Assert.Equal(sourceBase + 64, state.Gpr[6]);
        Assert.Equal(0xA000_003Cu, state.Gpr[0]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        for (uint offset = 0; offset < 64; offset += 4)
        {
            Assert.Equal(0xA000_0000u + offset, bus.Memory.Read32(destinationBase + 4 + offset));
        }
    }

    [Fact]
    public void ZeroStoreLoopFastForwardClearsMemoryAndFinishesLoop()
    {
        const uint pc = 0x8000_3100;
        const uint baseAddress = 0x8001_0000;
        GameCubeBus bus = new();
        WriteZeroStoreLoop(bus.Memory, pc);
        for (uint offset = 4; offset <= 96; offset += 4)
        {
            bus.Memory.Write32(baseAddress + offset, 0xDEAD_BEEF);
        }

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[0] = 3;
        state.Gpr[3] = baseAddress;
        state.Gpr[7] = 0;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardZeroStoreLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(30, skippedInstructions);
        Assert.Equal(pc + 40, state.Pc);
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal(baseAddress + 96, state.Gpr[3]);
        Assert.Equal(30ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 30u, state.Spr[22]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        for (uint offset = 4; offset <= 96; offset += 4)
        {
            Assert.Equal(0u, bus.Memory.Read32(baseAddress + offset));
        }
    }

    [Fact]
    public void ZeroStoreLoopFastForwardDoesNotCrossPositiveDecrementer()
    {
        const uint pc = 0x8000_3100;
        GameCubeBus bus = new();
        WriteZeroStoreLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[0] = 3;
        state.Gpr[3] = 0x8001_0000;
        state.Gpr[7] = 0;
        state.Spr[22] = 100;

        bool skipped = InvokeFastForwardZeroStoreLoop(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
    }

    [Fact]
    public void WordFillLoopFastForwardWritesRepeatedWordAndFinishesLoop()
    {
        const uint pc = 0x8000_3180;
        const uint baseAddress = 0x8001_0000;
        GameCubeBus bus = new();
        WriteZeroStoreLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[0] = 2;
        state.Gpr[3] = baseAddress;
        state.Gpr[7] = 0xAABB_CCDD;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardWordFillLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(20, skippedInstructions);
        Assert.Equal(pc + 40, state.Pc);
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal(baseAddress + 64, state.Gpr[3]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        for (uint offset = 4; offset <= 64; offset += 4)
        {
            Assert.Equal(0xAABB_CCDDu, bus.Memory.Read32(baseAddress + offset));
        }
    }

    [Fact]
    public void WordFillLoopFastForwardStopsAtPositiveDecrementerEdge()
    {
        const uint pc = 0x8000_3180;
        const uint baseAddress = 0x8001_0000;
        GameCubeBus bus = new();
        WriteZeroStoreLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[0] = 3;
        state.Gpr[3] = baseAddress;
        state.Gpr[7] = 0x1122_3344;
        state.Spr[22] = 15;

        bool skipped = InvokeFastForwardWordFillLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(10, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(2u, state.Gpr[0]);
        Assert.Equal(baseAddress + 32, state.Gpr[3]);
        Assert.Equal(5u, state.Spr[22]);
        for (uint offset = 4; offset <= 32; offset += 4)
        {
            Assert.Equal(0x1122_3344u, bus.Memory.Read32(baseAddress + offset));
        }
    }

    [Fact]
    public void ReverseWordFillLoopFastForwardWritesRepeatedWordAndFinishesLoop()
    {
        const uint pc = 0x8000_3300;
        const uint endAddress = 0x8001_0080;
        GameCubeBus bus = new();
        WriteReverseWordFillLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[0] = 2;
        state.Gpr[4] = 0xA5A5_5A5A;
        state.Gpr[7] = endAddress;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardWordFillLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(36, skippedInstructions);
        Assert.Equal(pc + 0x48, state.Pc);
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal(endAddress - 128, state.Gpr[7]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        for (uint address = endAddress - 128; address < endAddress; address += 4)
        {
            Assert.Equal(0xA5A5_5A5Au, bus.Memory.Read32(address));
        }
    }

    [Fact]
    public void ReverseWordFillLoopFastForwardStopsAtPositiveDecrementerEdge()
    {
        const uint pc = 0x8000_3300;
        const uint endAddress = 0x8001_00C0;
        GameCubeBus bus = new();
        WriteReverseWordFillLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[0] = 3;
        state.Gpr[4] = 0xCAFE_BABE;
        state.Gpr[7] = endAddress;
        state.Spr[22] = 20;

        bool skipped = InvokeFastForwardWordFillLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(18, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(2u, state.Gpr[0]);
        Assert.Equal(endAddress - 64, state.Gpr[7]);
        Assert.Equal(2u, state.Spr[22]);
        Assert.Equal(0x4000_0000u, state.Cr & 0xF000_0000);
        for (uint address = endAddress - 64; address < endAddress; address += 4)
        {
            Assert.Equal(0xCAFE_BABEu, bus.Memory.Read32(address));
        }
    }

    [Fact]
    public void SignedLongDivisionLeafFastForwardReturnsQuotient()
    {
        const uint pc = 0x8000_3300;
        GameCubeBus bus = new();
        WriteSignedLongDivisionLeafSignature(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_4400,
        };
        state.Gpr[1] = 0x8030_0000;
        SetLongOperand(state, 3, unchecked((ulong)-90L));
        SetLongOperand(state, 5, 7);
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardLongDivisionLeaf(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(160, skippedInstructions);
        Assert.Equal(0x8000_4400u, state.Pc);
        Assert.Equal(0x8030_0010u, state.Gpr[1]);
        Assert.Equal(unchecked((ulong)-12L), GetLongOperand(state, 3));
    }

    [Fact]
    public void SonicSignedLongDivisionLeafFastForwardMatchesInterpreterResult()
    {
        const uint pc = 0x8010_B11C;
        const uint stack = 0x8030_1000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicSignedLongDivisionLeaf(expectedBus.Memory, pc);
        WriteSonicSignedLongDivisionLeaf(actualBus.Memory, pc);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Lr = 0x8000_4400,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[1] = stack;
        actualState.Gpr[1] = stack;
        SetLongOperand(expectedState, 3, unchecked((ulong)-90L));
        SetLongOperand(expectedState, 5, 7);
        SetLongOperand(actualState, 3, unchecked((ulong)-90L));
        SetLongOperand(actualState, 5, 7);
        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != expectedState.Lr && expectedInstructions < 512)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        bool skipped = InvokeFastForwardLongDivisionLeaf(actualState, actualBus, out int skippedInstructions);

        Assert.Equal(expectedState.Lr, expectedState.Pc);
        Assert.True(skipped);
        Assert.True(skippedInstructions > 0);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Gpr[1], actualState.Gpr[1]);
        Assert.Equal(GetLongOperand(expectedState, 3), GetLongOperand(actualState, 3));
    }

    [Fact]
    public void UnsignedLongDivisionLeafFastForwardReturnsQuotient()
    {
        const uint pc = 0x8000_3500;
        GameCubeBus bus = new();
        WriteUnsignedLongDivisionLeafSignature(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_4600,
        };
        state.Gpr[1] = 0x8030_0000;
        SetLongOperand(state, 3, 0x0000_0002_0000_0000ul);
        SetLongOperand(state, 5, 4);
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardLongDivisionLeaf(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(120, skippedInstructions);
        Assert.Equal(0x8000_4600u, state.Pc);
        Assert.Equal(0x8030_0000u, state.Gpr[1]);
        Assert.Equal(0x0000_0000_8000_0000ul, GetLongOperand(state, 3));
    }

    [Fact]
    public void SignedLongDivisionLoopFastForwardFinishesCurrentDivision()
    {
        const uint pc = 0x8000_3600;
        const uint stack = 0x8030_0000;
        GameCubeBus bus = new();
        WriteSignedLongDivisionLoopSignature(bus.Memory, pc);
        bus.Memory.Write32(stack + 8, 0);
        bus.Memory.Write32(stack + 12, 0);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_4700,
            Ctr = 10,
        };
        state.Gpr[1] = stack;
        state.Gpr[3] = 0x7240_0000;
        state.Gpr[4] = 0;
        state.Gpr[5] = 0;
        state.Gpr[6] = 0x0000_9E34;
        state.Gpr[7] = 0;
        state.Gpr[8] = 0x0000_752A;
        state.Gpr[9] = 0x16;
        state.Gpr[10] = 0xFFFF_FFFF;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardLongDivisionLeaf(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(108, skippedInstructions);
        Assert.Equal(0x8000_4700u, state.Pc);
        Assert.Equal(0x8030_0010u, state.Gpr[1]);
        Assert.Equal(0x0000_0000_0000_03FFul, GetLongOperand(state, 3));
    }

    [Fact]
    public void LongDivisionLeafFastForwardDoesNotCrossPositiveDecrementer()
    {
        const uint pc = 0x8000_3700;
        GameCubeBus bus = new();
        WriteSignedLongDivisionLeafSignature(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_4800,
        };
        SetLongOperand(state, 3, 90);
        SetLongOperand(state, 5, 7);
        state.Spr[22] = 32;

        bool skipped = InvokeFastForwardLongDivisionLeaf(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
    }

    [Fact]
    public void ByteCopyLoopFastForwardCopiesMemoryAndFinishesLoop()
    {
        const uint pc = 0x8000_3200;
        const uint sourceBase = 0x8002_0000;
        const uint destinationBase = 0x8003_0000;
        byte[] sourceBytes = [0x12, 0x34, 0x56, 0x78, 0x9A];
        GameCubeBus bus = new();
        WriteByteCopyLoop(bus.Memory, pc);
        bus.Memory.Load(sourceBase + 1, sourceBytes);

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[4] = sourceBase;
        state.Gpr[5] = (uint)sourceBytes.Length;
        state.Gpr[6] = destinationBase;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardByteCopyLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(20, skippedInstructions);
        Assert.Equal(pc + 16, state.Pc);
        Assert.Equal(sourceBase + sourceBytes.Length, state.Gpr[4]);
        Assert.Equal(0u, state.Gpr[5]);
        Assert.Equal(destinationBase + sourceBytes.Length, state.Gpr[6]);
        Assert.Equal(0x9Au, state.Gpr[0]);
        Assert.Equal(20ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 20u, state.Spr[22]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        for (int index = 0; index < sourceBytes.Length; index++)
        {
            Assert.Equal(sourceBytes[index], bus.Memory.Read8(destinationBase + 1u + (uint)index));
        }
    }

    [Fact]
    public void ByteCopyLoopFastForwardDoesNotCrossPositiveDecrementer()
    {
        const uint pc = 0x8000_3200;
        GameCubeBus bus = new();
        WriteByteCopyLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[4] = 0x8002_0000;
        state.Gpr[5] = 5;
        state.Gpr[6] = 0x8003_0000;
        state.Spr[22] = 3;

        bool skipped = InvokeFastForwardByteCopyLoop(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
    }

    [Fact]
    public void ByteCopyLoopFastForwardStopsAtPositiveDecrementerEdge()
    {
        const uint pc = 0x8000_3200;
        const uint sourceBase = 0x8002_0000;
        const uint destinationBase = 0x8003_0000;
        byte[] sourceBytes = [0x12, 0x34, 0x56, 0x78, 0x9A];
        GameCubeBus bus = new();
        WriteByteCopyLoop(bus.Memory, pc);
        bus.Memory.Load(sourceBase + 1, sourceBytes);

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[4] = sourceBase;
        state.Gpr[5] = (uint)sourceBytes.Length;
        state.Gpr[6] = destinationBase;
        state.Spr[22] = 9;

        bool skipped = InvokeFastForwardByteCopyLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(8, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(sourceBase + 2, state.Gpr[4]);
        Assert.Equal(3u, state.Gpr[5]);
        Assert.Equal(destinationBase + 2, state.Gpr[6]);
        Assert.Equal(0x34u, state.Gpr[0]);
        Assert.Equal(8ul, state.TimeBase);
        Assert.Equal(1u, state.Spr[22]);
        Assert.Equal(0x4000_0000u, state.Cr & 0xF000_0000);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        Assert.Equal(0x12, bus.Memory.Read8(destinationBase + 1));
        Assert.Equal(0x34, bus.Memory.Read8(destinationBase + 2));
        Assert.Equal(0x00, bus.Memory.Read8(destinationBase + 3));
    }

    [Fact]
    public void NullTerminatedByteCopyLoopFastForwardCopiesThroughTerminator()
    {
        const uint pc = 0x8000_3280;
        const uint sourceBase = 0x8002_1000;
        const uint destinationBase = 0x8003_1000;
        byte[] sourceBytes = [(byte)'P', (byte)'i', (byte)'k', 0];
        GameCubeBus bus = new();
        WriteNullTerminatedByteCopyLoop(bus.Memory, pc);
        bus.Memory.Load(sourceBase + 1, sourceBytes);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_32C0,
        };
        state.Gpr[4] = sourceBase;
        state.Gpr[5] = destinationBase;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardNullTerminatedByteCopyLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(17, skippedInstructions);
        Assert.Equal(0x8000_32C0u, state.Pc);
        Assert.Equal(sourceBase + 4, state.Gpr[4]);
        Assert.Equal(destinationBase + 4, state.Gpr[5]);
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal(17ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 17u, state.Spr[22]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        for (int index = 0; index < sourceBytes.Length; index++)
        {
            Assert.Equal(sourceBytes[index], bus.Memory.Read8(destinationBase + 1u + (uint)index));
        }
    }

    [Fact]
    public void NullTerminatedByteCopyLoopFastForwardDoesNotCrossPositiveDecrementer()
    {
        const uint pc = 0x8000_3280;
        GameCubeBus bus = new();
        WriteNullTerminatedByteCopyLoop(bus.Memory, pc);
        bus.Memory.Load(0x8002_1001, [(byte)'A', 0]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_32C0,
        };
        state.Gpr[4] = 0x8002_1000;
        state.Gpr[5] = 0x8003_1000;
        state.Spr[22] = 8;

        bool skipped = InvokeFastForwardNullTerminatedByteCopyLoop(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
    }

    [Fact]
    public void StringCompareRoutineFastForwardReturnsZeroForEqualStrings()
    {
        const uint pc = 0x8000_32C0;
        const uint left = 0x8002_2000;
        const uint right = 0x8003_2000;
        GameCubeBus bus = new();
        WriteStringCompareRoutine(bus.Memory, pc);
        bus.Memory.Load(left, [(byte)'t', (byte)'e', (byte)'s', (byte)'t', 0]);
        bus.Memory.Load(right, [(byte)'t', (byte)'e', (byte)'s', (byte)'t', 0]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3300,
        };
        state.Gpr[3] = left;
        state.Gpr[4] = right;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardStringCompareRoutine(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(84, skippedInstructions);
        Assert.Equal(0x8000_3300u, state.Pc);
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal(0u, state.Gpr[3]);
        Assert.Equal(0u, state.Gpr[5]);
        Assert.Equal(0u, state.Gpr[6]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void StringCompareRoutineFastForwardReturnsSignedDifferenceForMismatch()
    {
        const uint pc = 0x8000_32C0;
        const uint left = 0x8002_2000;
        const uint right = 0x8003_2000;
        GameCubeBus bus = new();
        WriteStringCompareRoutine(bus.Memory, pc);
        bus.Memory.Load(left, [(byte)'a', (byte)'b', 0]);
        bus.Memory.Load(right, [(byte)'a', (byte)'c', 0]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3300,
        };
        state.Gpr[3] = left;
        state.Gpr[4] = right;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardStringCompareRoutine(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(48, skippedInstructions);
        Assert.Equal(0x8000_3300u, state.Pc);
        Assert.Equal(0xFFFF_FFFFu, state.Gpr[0]);
        Assert.Equal(0xFFFF_FFFFu, state.Gpr[3]);
        Assert.Equal((uint)'c', state.Gpr[5]);
        Assert.Equal((uint)'b', state.Gpr[6]);
        Assert.Equal(0x8000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void StringCompareRoutineFastForwardDoesNotCrossPositiveDecrementer()
    {
        const uint pc = 0x8000_32C0;
        GameCubeBus bus = new();
        WriteStringCompareRoutine(bus.Memory, pc);
        bus.Memory.Load(0x8002_2000, [(byte)'a', 0]);
        bus.Memory.Load(0x8003_2000, [(byte)'a', 0]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3300,
        };
        state.Gpr[3] = 0x8002_2000;
        state.Gpr[4] = 0x8003_2000;
        state.Spr[22] = 40;

        bool skipped = InvokeFastForwardStringCompareRoutine(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
    }

    [Fact]
    public void AsciiCaseInsensitiveStringCompareLoopFastForwardReturnsOneForEqualStrings()
    {
        const uint pc = 0x8000_3300;
        const uint left = 0x8002_3000;
        const uint right = 0x8003_3000;
        GameCubeBus bus = new();
        WriteAsciiCaseInsensitiveStringCompareLoop(bus.Memory, pc);
        bus.Memory.Load(left, [(byte)'T', (byte)'e', (byte)'S', (byte)'t', 0]);
        bus.Memory.Load(right, [(byte)'t', (byte)'E', (byte)'s', (byte)'T', 0]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3380,
        };
        state.Gpr[3] = left;
        state.Gpr[4] = right;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardAsciiCaseInsensitiveStringCompareLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(102, skippedInstructions);
        Assert.Equal(0x8000_3380u, state.Pc);
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal(1u, state.Gpr[3]);
        Assert.Equal(right + 5, state.Gpr[4]);
        Assert.Equal(0u, state.Gpr[5]);
        Assert.Equal(0u, state.Gpr[6]);
        Assert.Equal(0u, state.Gpr[7]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void AsciiCaseInsensitiveStringCompareLoopFastForwardReturnsZeroForMismatch()
    {
        const uint pc = 0x8000_3300;
        const uint left = 0x8002_3000;
        const uint right = 0x8003_3000;
        GameCubeBus bus = new();
        WriteAsciiCaseInsensitiveStringCompareLoop(bus.Memory, pc);
        bus.Memory.Load(left, [(byte)'a', (byte)'b', 0]);
        bus.Memory.Load(right, [(byte)'A', (byte)'c', 0]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3380,
        };
        state.Gpr[3] = left;
        state.Gpr[4] = right;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardAsciiCaseInsensitiveStringCompareLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(41, skippedInstructions);
        Assert.Equal(0x8000_3380u, state.Pc);
        Assert.Equal((uint)'c', state.Gpr[0]);
        Assert.Equal(0u, state.Gpr[3]);
        Assert.Equal(right + 2, state.Gpr[4]);
        Assert.Equal((uint)'b', state.Gpr[5]);
        Assert.Equal((uint)'b', state.Gpr[6]);
        Assert.Equal((uint)'c', state.Gpr[7]);
        Assert.Equal(0x8000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void AsciiCaseInsensitiveStringCompareLoopFastForwardDoesNotCrossPositiveDecrementer()
    {
        const uint pc = 0x8000_3300;
        GameCubeBus bus = new();
        WriteAsciiCaseInsensitiveStringCompareLoop(bus.Memory, pc);
        bus.Memory.Load(0x8002_3000, [(byte)'a', 0]);
        bus.Memory.Load(0x8003_3000, [(byte)'A', 0]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3380,
        };
        state.Gpr[3] = 0x8002_3000;
        state.Gpr[4] = 0x8003_3000;
        state.Spr[22] = 20;

        bool skipped = InvokeFastForwardAsciiCaseInsensitiveStringCompareLoop(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
    }

    [Fact]
    public void StringLengthRoutineFastForwardReturnsTerminatorOffset()
    {
        const uint pc = 0x8000_3400;
        const uint text = 0x8002_4000;
        GameCubeBus bus = new();
        WriteStringLengthRoutine(bus.Memory, pc);
        bus.Memory.Load(text, [(byte)'d', (byte)'e', (byte)'b', (byte)'u', (byte)'g', 0]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3440,
        };
        state.Gpr[3] = text;
        state.Gpr[30] = 0xCAFE_BABE;
        state.Gpr[31] = 0xFACE_FEED;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardStringLengthRoutine(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(34, skippedInstructions);
        Assert.Equal(0x8000_3440u, state.Pc);
        Assert.Equal(5u, state.Gpr[3]);
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal(0xCAFE_BABEu, state.Gpr[30]);
        Assert.Equal(0xFACE_FEEDu, state.Gpr[31]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void StringLengthRoutineFastForwardDoesNotCrossPositiveDecrementer()
    {
        const uint pc = 0x8000_3400;
        const uint text = 0x8002_4000;
        GameCubeBus bus = new();
        WriteStringLengthRoutine(bus.Memory, pc);
        bus.Memory.Load(text, [(byte)'a', (byte)'b', 0]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3440,
        };
        state.Gpr[3] = text;
        state.Spr[22] = 8;

        bool skipped = InvokeFastForwardStringLengthRoutine(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
    }

    [Fact]
    public void ReturnZeroLeafFastForwardReturnsThroughLinkRegister()
    {
        const uint pc = 0x8000_3480;
        GameCubeBus bus = new();
        bus.Memory.Write32(pc, 0x3860_0000);
        bus.Memory.Write32(pc + 4, 0x4E80_0020);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3490,
        };
        state.Gpr[3] = 0xFFFF_FFFF;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(2, skippedInstructions);
        Assert.Equal(0x8000_3490u, state.Pc);
        Assert.Equal(0u, state.Gpr[3]);
    }

    [Fact]
    public void SmallDataWordLoadLeafFastForwardReturnsLoadedWordThroughLinkRegister()
    {
        const uint pc = 0x8000_34A0;
        const uint smallDataBase = 0x803B_F000;
        const int offset = -0x7500;
        uint valueAddress = unchecked(smallDataBase + (uint)offset);
        GameCubeBus bus = new();
        bus.Memory.Write32(pc, 0x806D_8B00);
        bus.Memory.Write32(pc + 4, 0x4E80_0020);
        bus.Memory.Write32(valueAddress, 0x8123_4567);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_34B0,
        };
        state.Gpr[3] = 0xFFFF_FFFF;
        state.Gpr[13] = smallDataBase;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(2, skippedInstructions);
        Assert.Equal(0x8000_34B0u, state.Pc);
        Assert.Equal(0x8123_4567u, state.Gpr[3]);
        Assert.Equal(2ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 2u, state.Spr[22]);
    }

    [Fact]
    public void VariadicRegisterSaveStubFastForwardReturnsWithoutTouchingStack()
    {
        const uint pc = 0x8000_3500;
        GameCubeBus bus = new();
        WriteVariadicRegisterSaveStub(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3560,
        };
        state.Gpr[1] = 0x8004_0000;
        state.Gpr[3] = 0x1234_5678;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(20, skippedInstructions);
        Assert.Equal(0x8000_3560u, state.Pc);
        Assert.Equal(0x8004_0000u, state.Gpr[1]);
        Assert.Equal(0x1234_5678u, state.Gpr[3]);
    }

    [Fact]
    public void FastForwardLeafProfileFilterRecognizesOnlyKnownLeafShapes()
    {
        const uint blrPc = 0x8000_34C0;
        const uint returnZeroPc = 0x8000_34E0;
        const uint variadicPc = 0x8000_3500;
        const uint timeBasePc = 0x8000_3580;
        const uint smallDataLoadPc = 0x8000_35C0;
        const uint ordinaryPc = 0x8000_3600;
        GameCubeBus bus = new();

        bus.Memory.Write32(blrPc, 0x4E80_0020);
        bus.Memory.Write32(returnZeroPc, 0x3860_0000);
        bus.Memory.Write32(returnZeroPc + 4, 0x4E80_0020);
        WriteVariadicRegisterSaveStub(bus.Memory, variadicPc);
        WriteTimeBaseReadLeaf(bus.Memory, timeBasePc);
        bus.Memory.Write32(smallDataLoadPc, 0x806D_8B00);
        bus.Memory.Write32(smallDataLoadPc + 4, 0x4E80_0020);
        bus.Memory.Write32(ordinaryPc, 0x9421_FFF0);
        bus.Memory.Write32(ordinaryPc + 4, 0x7C08_02A6);

        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, blrPc));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, returnZeroPc));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, variadicPc));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, timeBasePc));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, smallDataLoadPc));
        Assert.False(InvokeIsFastForwardLeafHelperEntry(bus, ordinaryPc));
    }

    [Fact]
    public void FastForwardLeafProfileFilterRecognizesKnownSonicFastForwardRoots()
    {
        const uint ordinaryPc = 0x8012_8000;
        GameCubeBus bus = new();
        WriteSonicResourceFlagWaitLoop(bus.Memory);
        WriteSonicDvdStatusWaitLoop(bus.Memory);
        WriteSonicNullSlotScanLoop(bus.Memory);
        WriteSonicPoolNullSlotScanLoop(bus.Memory);
        WriteSonicPoolSentinelSlotScanLoop(bus.Memory);
        WriteSonicTableKeyScanLoop(bus.Memory);
        WriteSonicModeRefreshDispatch(bus.Memory);
        bus.Memory.Write32(ordinaryPc, 0x9421_FFF0);
        bus.Memory.Write32(ordinaryPc + 4, 0x7C08_02A6);

        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x800E_BEA0));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x800E_BEA4));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x800E_BEA8));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x800D_B9AC));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x8011_6BBC));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x8011_6C18));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x8011_6C8C));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x8011_90B8));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x8012_33E0));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x8012_33E4));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x8012_33E8));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x8012_3478));
        Assert.True(InvokeIsFastForwardLeafHelperEntry(bus, 0x8012_3484));
        Assert.False(InvokeIsFastForwardLeafHelperEntry(bus, ordinaryPc));
    }

    [Fact]
    public void TimeBaseReadLeafFastForwardReturnsStableTimeBase()
    {
        const uint pc = 0x8000_3580;
        GameCubeBus bus = new();
        WriteTimeBaseReadLeaf(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_35A0,
            TimeBase = 0x0000_0001_0000_0000,
        };
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(6, skippedInstructions);
        Assert.Equal(0x8000_35A0u, state.Pc);
        Assert.Equal(1u, state.Gpr[3]);
        Assert.Equal(2u, state.Gpr[4]);
        Assert.Equal(1u, state.Gpr[5]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        Assert.Equal(0x0000_0001_0000_0006ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 6u, state.Spr[22]);
    }

    [Fact]
    public void TimeBaseReadLeafFastForwardFallsBackAcrossUpperChange()
    {
        const uint pc = 0x8000_3580;
        GameCubeBus bus = new();
        WriteTimeBaseReadLeaf(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_35A0,
            TimeBase = 0x0000_0000_FFFF_FFFE,
        };
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(0x0000_0000_FFFF_FFFEul, state.TimeBase);
    }

    [Fact]
    public void LowerTimeBaseReadLeafFastForwardReturnsLowWord()
    {
        const uint pc = 0x8000_35C0;
        GameCubeBus bus = new();
        WriteLowerTimeBaseReadLeaf(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_35D0,
            TimeBase = 0x0000_0002_1234_5678,
        };
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(2, skippedInstructions);
        Assert.Equal(0x8000_35D0u, state.Pc);
        Assert.Equal(0x1234_5679u, state.Gpr[3]);
        Assert.Equal(0x0000_0002_1234_567Aul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 2u, state.Spr[22]);
    }

    [Fact]
    public void CtrZeroStoreLoopFastForwardClearsMemoryAndFinishesLoop()
    {
        const uint pc = 0x8000_3300;
        const uint baseAddress = 0x8004_0000;
        GameCubeBus bus = new();
        WriteCtrZeroStoreLoop(bus.Memory, pc);
        for (uint offset = 0; offset < 96; offset += 4)
        {
            bus.Memory.Write32(baseAddress + offset, 0xDEAD_BEEF);
        }

        PowerPcState state = new()
        {
            Pc = pc,
            Ctr = 3,
        };
        state.Gpr[0] = 0;
        state.Gpr[4] = baseAddress;
        state.Gpr[6] = 0x8005_0000;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardCtrZeroStoreLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(33, skippedInstructions);
        Assert.Equal(pc + 44, state.Pc);
        Assert.Equal(0u, state.Ctr);
        Assert.Equal(baseAddress + 96, state.Gpr[4]);
        Assert.Equal(0x8005_0000u + 24u, state.Gpr[6]);
        Assert.Equal(33ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 33u, state.Spr[22]);
        for (uint offset = 0; offset < 96; offset += 4)
        {
            Assert.Equal(0u, bus.Memory.Read32(baseAddress + offset));
        }
    }

    [Fact]
    public void CtrZeroStoreLoopFastForwardStopsAtPositiveDecrementerEdge()
    {
        const uint pc = 0x8000_3300;
        const uint baseAddress = 0x8004_0000;
        GameCubeBus bus = new();
        WriteCtrZeroStoreLoop(bus.Memory, pc);
        for (uint offset = 0; offset < 96; offset += 4)
        {
            bus.Memory.Write32(baseAddress + offset, 0xDEAD_BEEF);
        }

        PowerPcState state = new()
        {
            Pc = pc,
            Ctr = 3,
        };
        state.Gpr[0] = 0;
        state.Gpr[4] = baseAddress;
        state.Gpr[6] = 0x8005_0000;
        state.Spr[22] = 23;

        bool skipped = InvokeFastForwardCtrZeroStoreLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(22, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(1u, state.Ctr);
        Assert.Equal(baseAddress + 64, state.Gpr[4]);
        Assert.Equal(0x8005_0000u + 16u, state.Gpr[6]);
        Assert.Equal(1u, state.Spr[22]);
        for (uint offset = 0; offset < 64; offset += 4)
        {
            Assert.Equal(0u, bus.Memory.Read32(baseAddress + offset));
        }

        Assert.Equal(0xDEAD_BEEFu, bus.Memory.Read32(baseAddress + 64));
    }

    [Fact]
    public void CtrCacheBlockLoopFastForwardFinishesLoop()
    {
        const uint pc = 0x8000_3400;
        GameCubeBus bus = new();
        WriteCtrCacheBlockLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Ctr = 5,
        };
        state.Gpr[3] = 0x817B_F100;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardCtrCacheBlockLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(15, skippedInstructions);
        Assert.Equal(pc + 12, state.Pc);
        Assert.Equal(0u, state.Ctr);
        Assert.Equal(0x817B_F100u + 160u, state.Gpr[3]);
        Assert.Equal(15ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 15u, state.Spr[22]);
    }

    [Fact]
    public void CtrCacheBlockLoopFastForwardStopsAtPositiveDecrementerEdge()
    {
        const uint pc = 0x8000_3400;
        GameCubeBus bus = new();
        WriteCtrCacheBlockLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Ctr = 5,
        };
        state.Gpr[3] = 0x817B_F100;
        state.Spr[22] = 8;

        bool skipped = InvokeFastForwardCtrCacheBlockLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(6, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(3u, state.Ctr);
        Assert.Equal(0x817B_F100u + 64u, state.Gpr[3]);
        Assert.Equal(2u, state.Spr[22]);
    }

    [Fact]
    public void FourShortStoreLeafFastForwardStoresSignedHalfwordsAndReturns()
    {
        const uint pc = 0x8000_3500;
        const uint address = 0x8006_0000;
        GameCubeBus bus = new();
        WriteFourShortStoreLeaf(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3600,
        };
        state.Gpr[3] = address;
        state.Gpr[4] = 0x0000_8001;
        state.Gpr[5] = 0x0000_7FFE;
        state.Gpr[6] = 0xFFFF_FF80;
        state.Gpr[7] = 0x0000_1234;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(9, skippedInstructions);
        Assert.Equal(0x8000_3600u, state.Pc);
        Assert.Equal(0x0000_1234u, state.Gpr[0]);
        Assert.Equal(0xFFFF_FF80u, state.Gpr[4]);
        Assert.Equal(0x8001, bus.Memory.Read16(address));
        Assert.Equal(0x7FFE, bus.Memory.Read16(address + 2));
        Assert.Equal(0xFF80, bus.Memory.Read16(address + 4));
        Assert.Equal(0x1234, bus.Memory.Read16(address + 6));
        Assert.Equal(9ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 9u, state.Spr[22]);
    }

    [Fact]
    public void ZeroThreeWordsLeafFastForwardClearsWordsAndReturns()
    {
        const uint pc = 0x8000_3600;
        const uint address = 0x8006_1000;
        GameCubeBus bus = new();
        WriteZeroThreeWordsLeaf(bus.Memory, pc);
        bus.Memory.Write32(address, 0xDEAD_BEEF);
        bus.Memory.Write32(address + 4, 0xCAFE_BABE);
        bus.Memory.Write32(address + 8, 0xFEED_FACE);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3700,
        };
        state.Gpr[3] = address;
        state.Gpr[0] = 0xFFFF_FFFF;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(5, skippedInstructions);
        Assert.Equal(0x8000_3700u, state.Pc);
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal(0u, bus.Memory.Read32(address));
        Assert.Equal(0u, bus.Memory.Read32(address + 4));
        Assert.Equal(0u, bus.Memory.Read32(address + 8));
    }

    [Fact]
    public void PointerNodeLeafFastForwardInitializesPointerNodeAndReturns()
    {
        const uint pc = 0x8000_3700;
        const uint address = 0x8006_2000;
        GameCubeBus bus = new();
        WritePointerNodeLeaf(bus.Memory, pc);
        for (uint offset = 0; offset < 16; offset += 4)
        {
            bus.Memory.Write32(address + offset, 0xDEAD_BEEF);
        }

        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3800,
        };
        state.Gpr[3] = address;
        state.Gpr[4] = 0x8049_AE28;
        state.Gpr[0] = 0xFFFF_FFFF;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(6, skippedInstructions);
        Assert.Equal(0x8000_3800u, state.Pc);
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal(0x8049_AE28u, bus.Memory.Read32(address));
        Assert.Equal(0u, bus.Memory.Read32(address + 4));
        Assert.Equal(0u, bus.Memory.Read32(address + 8));
        Assert.Equal(0u, bus.Memory.Read32(address + 12));
    }

    [Fact]
    public void WordEqualsLeafFastForwardReturnsOneWhenWordMatches()
    {
        const uint pc = 0x8000_3780;
        const uint address = 0x8006_3000;
        GameCubeBus bus = new();
        WriteWordEqualsLeaf(bus.Memory, pc);
        bus.Memory.Write32(address, 0x7A31_3272);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3800,
        };
        state.Gpr[3] = address;
        state.Gpr[4] = 0x7A31_3272;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(5, skippedInstructions);
        Assert.Equal(0x8000_3800u, state.Pc);
        Assert.Equal(32u, state.Gpr[0]);
        Assert.Equal(1u, state.Gpr[3]);
        Assert.Equal(5ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 5u, state.Spr[22]);
    }

    [Fact]
    public void WordEqualsLeafFastForwardReturnsZeroWhenWordDiffers()
    {
        const uint pc = 0x8000_3780;
        const uint address = 0x8006_3000;
        GameCubeBus bus = new();
        WriteWordEqualsLeaf(bus.Memory, pc);
        bus.Memory.Write32(address, 0x7A31_3272);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3800,
        };
        state.Gpr[3] = address;
        state.Gpr[4] = 0x7A31_3273;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(5, skippedInstructions);
        Assert.Equal(0x8000_3800u, state.Pc);
        Assert.Equal(31u, state.Gpr[0]);
        Assert.Equal(0u, state.Gpr[3]);
    }

    [Fact]
    public void DisableExternalInterruptLeafFastForwardClearsMsrEeAndReturnsPreviousState()
    {
        const uint pc = 0x800E_78AC;
        GameCubeBus bus = new();
        WriteDisableExternalInterruptLeaf(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x800E_7900,
            Msr = 0x0000_8000,
        };
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(5, skippedInstructions);
        Assert.Equal(1u, state.Gpr[3]);
        Assert.Equal(0u, state.Msr & 0x8000);
        Assert.Equal(state.Lr, state.Pc);
    }

    [Fact]
    public void EnableExternalInterruptLeafFastForwardSetsMsrEeAndReturnsPreviousState()
    {
        const uint pc = 0x800E_78C0;
        GameCubeBus bus = new();
        WriteEnableExternalInterruptLeaf(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x800E_7900,
            Msr = 0,
        };
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(5, skippedInstructions);
        Assert.Equal(0u, state.Gpr[3]);
        Assert.Equal(0x8000u, state.Msr & 0x8000);
        Assert.Equal(state.Lr, state.Pc);
    }

    [Fact]
    public void RestoreExternalInterruptLeafFastForwardRestoresRequestedMsrEe()
    {
        const uint pc = 0x800E_78D4;
        GameCubeBus bus = new();
        WriteRestoreExternalInterruptLeaf(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x800E_7900,
            Msr = 0,
        };
        state.Gpr[3] = 1;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSmallLeafHelper(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(9, skippedInstructions);
        Assert.Equal(0x8000u, state.Msr & 0x8000);
        Assert.Equal(0u, state.Gpr[4]);
        Assert.Equal(0x4000_0000u, state.Cr & 0xF000_0000);
        Assert.Equal(state.Lr, state.Pc);
    }

    [Theory]
    [InlineData(0u, 0x5Au)]
    [InlineData(3u, 0x5Au)]
    [InlineData(37u, 0x00u)]
    [InlineData(43u, 0xA5u)]
    public void MemsetRoutineFastForwardMatchesInterpreterWrapper(uint count, uint value)
    {
        const uint pc = 0x8000_532C;
        const uint destination = 0x8007_0003;
        const uint stack = 0x817F_F000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteMemsetRoutine(expectedBus.Memory, pc);
        WriteMemsetRoutine(actualBus.Memory, pc);
        PowerPcState expectedState = CreateMemsetRoutineState(pc, destination, value, count, stack);
        PowerPcState actualState = CreateMemsetRoutineState(pc, destination, value, count, stack);

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        uint returnAddress = expectedState.Lr;
        while (expectedState.Pc != returnAddress && expectedInstructions < 512)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        Assert.Equal(returnAddress, expectedState.Pc);
        bool skipped = InvokeFastForwardMemsetRoutine(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Xer, actualState.Xer);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 4, 5, 6, 7, 31 })
        {
            Assert.True(
                expectedState.Gpr[register] == actualState.Gpr[register],
                $"r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
        }

        for (uint offset = 0; offset < Math.Max(count, 1); offset++)
        {
            Assert.Equal(expectedBus.Memory.Read8(destination + offset), actualBus.Memory.Read8(destination + offset));
        }

        for (uint offset = 0; offset <= 0x24; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 32 + offset), actualBus.Memory.Read32(stack - 32 + offset));
        }
    }

    [Fact]
    public void MemsetRoutineFastForwardMatchesInterpreterCore()
    {
        const uint pc = 0x8000_535C;
        const uint destination = 0x8007_0101;
        const uint count = 35;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteMemsetCore(expectedBus.Memory, pc);
        WriteMemsetCore(actualBus.Memory, pc);
        PowerPcState expectedState = CreateMemsetRoutineState(pc, destination, 0x3C, count, stack: 0x817F_F000);
        PowerPcState actualState = CreateMemsetRoutineState(pc, destination, 0x3C, count, stack: 0x817F_F000);

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        uint returnAddress = expectedState.Lr;
        while (expectedState.Pc != returnAddress && expectedInstructions < 512)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        Assert.Equal(returnAddress, expectedState.Pc);
        bool skipped = InvokeFastForwardMemsetRoutine(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Xer, actualState.Xer);
        foreach (int register in new[] { 0, 3, 4, 5, 6, 7 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (uint offset = 0; offset < count; offset++)
        {
            Assert.Equal(expectedBus.Memory.Read8(destination + offset), actualBus.Memory.Read8(destination + offset));
        }
    }

    [Fact]
    public void MemmoveRoutineFastForwardCopiesForwardFromEntry()
    {
        const uint pc = 0x8000_3800;
        const uint source = 0x8007_0000;
        const uint destination = 0x8006_F000;
        byte[] sourceBytes = [0x10, 0x21, 0x32, 0x43, 0x54];
        GameCubeBus bus = new();
        WriteMemmoveRoutine(bus.Memory, pc);
        bus.Memory.Load(source, sourceBytes);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3900,
        };
        state.Gpr[3] = destination;
        state.Gpr[4] = source;
        state.Gpr[5] = (uint)sourceBytes.Length;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardMemmoveRoutine(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(29, skippedInstructions);
        Assert.Equal(0x8000_3900u, state.Pc);
        Assert.Equal(source + 4, state.Gpr[4]);
        Assert.Equal(0u, state.Gpr[5]);
        Assert.Equal(destination + 4, state.Gpr[6]);
        Assert.Equal(0x54u, state.Gpr[0]);
        Assert.Equal(29ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 29u, state.Spr[22]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        for (int index = 0; index < sourceBytes.Length; index++)
        {
            Assert.Equal(sourceBytes[index], bus.Memory.Read8(destination + (uint)index));
        }
    }

    [Fact]
    public void MemmoveRoutineFastForwardCopiesBackwardForOverlap()
    {
        const uint pc = 0x8000_3800;
        const uint buffer = 0x8007_2000;
        byte[] initialBytes = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88];
        GameCubeBus bus = new();
        WriteMemmoveRoutine(bus.Memory, pc);
        bus.Memory.Load(buffer, initialBytes);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3900,
        };
        state.Gpr[3] = buffer + 2;
        state.Gpr[4] = buffer;
        state.Gpr[5] = 5;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardMemmoveRoutine(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(29, skippedInstructions);
        Assert.Equal(0x8000_3900u, state.Pc);
        Assert.Equal(buffer, state.Gpr[4]);
        Assert.Equal(0u, state.Gpr[5]);
        Assert.Equal(buffer + 2, state.Gpr[6]);
        Assert.Equal(0x11u, state.Gpr[0]);
        byte[] expectedBytes = [0x11, 0x22, 0x11, 0x22, 0x33, 0x44, 0x55, 0x88];
        for (int index = 0; index < expectedBytes.Length; index++)
        {
            Assert.Equal(expectedBytes[index], bus.Memory.Read8(buffer + (uint)index));
        }
    }

    [Fact]
    public void MemmoveRoutineFastForwardCopiesFromForwardSetup()
    {
        const uint pc = 0x8000_3800;
        const uint source = 0x8007_3000;
        const uint destination = 0x8007_4000;
        byte[] sourceBytes = [0xA0, 0xB1, 0xC2];
        GameCubeBus bus = new();
        WriteMemmoveRoutine(bus.Memory, pc);
        bus.Memory.Load(source, sourceBytes);
        PowerPcState state = new()
        {
            Pc = pc + 0x10,
            Lr = 0x8000_3900,
        };
        state.Gpr[4] = source - 1;
        state.Gpr[5] = (uint)sourceBytes.Length;
        state.Gpr[6] = destination - 1;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardMemmoveRoutine(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(17, skippedInstructions);
        Assert.Equal(0x8000_3900u, state.Pc);
        Assert.Equal(source + 2, state.Gpr[4]);
        Assert.Equal(0u, state.Gpr[5]);
        Assert.Equal(destination + 2, state.Gpr[6]);
        Assert.Equal(0xC2u, state.Gpr[0]);
        for (int index = 0; index < sourceBytes.Length; index++)
        {
            Assert.Equal(sourceBytes[index], bus.Memory.Read8(destination + (uint)index));
        }
    }

    [Theory]
    [InlineData(2u, 3u)]
    [InlineData(3u, 0u)]
    public void MemmoveRoutineFastForwardCopiesOptimizedBackwardWordTail(uint wordCount, uint residualBytes)
    {
        const uint pc = 0x8010_C0C4;
        const uint source = 0x8007_5000;
        const uint destination = 0x8007_6000;
        uint totalBytes = wordCount * sizeof(uint) + residualBytes;
        byte[] sourceBytes = Enumerable.Range(0, (int)totalBytes).Select(index => (byte)(0x31 + index)).ToArray();
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteOptimizedMemmoveBackwardWordTail(expectedBus.Memory, pc);
        WriteOptimizedMemmoveBackwardWordTail(actualBus.Memory, pc);
        expectedBus.Memory.Load(source, sourceBytes);
        actualBus.Memory.Load(source, sourceBytes);
        PowerPcState expectedState = CreateOptimizedMemmoveBackwardWordTailState(pc, source + totalBytes, destination + totalBytes, wordCount, residualBytes);
        PowerPcState actualState = CreateOptimizedMemmoveBackwardWordTailState(pc, source + totalBytes, destination + totalBytes, wordCount, residualBytes);

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != expectedState.Lr && expectedInstructions < 64)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        Assert.Equal(expectedState.Lr, expectedState.Pc);
        bool skipped = InvokeFastForwardMemmoveRoutine(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Xer, actualState.Xer);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 4, 5, 6 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (uint offset = 0; offset < totalBytes; offset++)
        {
            Assert.Equal(expectedBus.Memory.Read8(destination + offset), actualBus.Memory.Read8(destination + offset));
        }
    }

    [Fact]
    public void MemmoveRoutineFastForwardCopiesOptimizedBackwardWordTailInLockedCache()
    {
        const uint pc = 0x8010_C0C4;
        const uint source = 0xE000_0010;
        const uint destination = 0xE000_0040;
        const uint wordCount = 4;
        const uint residualBytes = 0;
        uint totalBytes = wordCount * sizeof(uint) + residualBytes;
        byte[] sourceBytes = Enumerable.Range(0, (int)totalBytes).Select(index => (byte)(0x70 + index)).ToArray();
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteOptimizedMemmoveBackwardWordTail(expectedBus.Memory, pc);
        WriteOptimizedMemmoveBackwardWordTail(actualBus.Memory, pc);
        for (uint offset = 0; offset < totalBytes; offset++)
        {
            expectedBus.Write8(source + offset, sourceBytes[offset]);
            actualBus.Write8(source + offset, sourceBytes[offset]);
        }

        PowerPcState expectedState = CreateOptimizedMemmoveBackwardWordTailState(pc, source + totalBytes, destination + totalBytes, wordCount, residualBytes);
        PowerPcState actualState = CreateOptimizedMemmoveBackwardWordTailState(pc, source + totalBytes, destination + totalBytes, wordCount, residualBytes);

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != expectedState.Lr && expectedInstructions < 64)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        Assert.Equal(expectedState.Lr, expectedState.Pc);
        bool skipped = InvokeFastForwardMemmoveRoutine(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Xer, actualState.Xer);
        foreach (int register in new[] { 0, 3, 4, 5, 6 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (uint offset = 0; offset < totalBytes; offset++)
        {
            Assert.Equal(expectedBus.Read8(destination + offset), actualBus.Read8(destination + offset));
        }
    }

    [Fact]
    public void MemmoveRoutineFastForwardDoesNotCrossPositiveDecrementer()
    {
        const uint pc = 0x8000_3800;
        GameCubeBus bus = new();
        WriteMemmoveRoutine(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3900,
        };
        state.Gpr[3] = 0x8007_1000;
        state.Gpr[4] = 0x8007_0000;
        state.Gpr[5] = 5;
        state.Spr[22] = 12;

        bool skipped = InvokeFastForwardMemmoveRoutine(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
    }

    [Fact]
    public void TextureSampleLeafFastForwardReturnsHighNibbleSample()
    {
        const uint pc = 0x8000_3900;
        const uint descriptor = 0x8008_0000;
        const uint textureData = 0x8008_1000;
        GameCubeBus bus = new();
        WriteTextureSampleLeaf(bus.Memory, pc);
        bus.Memory.Write16(descriptor + 4, 5);
        bus.Memory.Write16(descriptor + 8, 8);
        bus.Memory.Write32(descriptor + 0x0C, 4);
        bus.Memory.Write32(descriptor + 0x10, 4);
        bus.Memory.Write32(descriptor + 0x14, textureData);
        bus.Memory.Write8(textureData + 57, 0xAB);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3A00,
        };
        state.Gpr[3] = descriptor;
        state.Gpr[4] = 5;
        state.Gpr[5] = 6;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardTextureSampleLeaf(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(25, skippedInstructions);
        Assert.Equal(0x8000_3A00u, state.Pc);
        Assert.Equal(0xABu, state.Gpr[0]);
        Assert.Equal(0xA0u, state.Gpr[3]);
        Assert.Equal(57u, state.Gpr[4]);
        Assert.Equal(32u, state.Gpr[5]);
        Assert.Equal(4u, state.Gpr[6]);
        Assert.Equal(32u, state.Gpr[7]);
        Assert.Equal(1u, state.Gpr[8]);
        Assert.Equal(1u, state.Gpr[9]);
        Assert.Equal(4u, state.Gpr[10]);
        Assert.Equal(16u, state.Gpr[11]);
        Assert.Equal(25ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 25u, state.Spr[22]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void TextureSampleLeafFastForwardOnlyHandlesObservedKind()
    {
        const uint pc = 0x8000_3900;
        const uint descriptor = 0x8008_0000;
        GameCubeBus bus = new();
        WriteTextureSampleLeaf(bus.Memory, pc);
        bus.Memory.Write16(descriptor + 4, 4);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_3A00,
        };
        state.Gpr[3] = descriptor;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardTextureSampleLeaf(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
    }

    [Fact]
    public void SonicPrsDecompressFastForwardCopiesLiteralBytes()
    {
        const uint pc = 0x8000_3000;
        const uint source = 0x8001_0000;
        const uint destination = 0x8001_1000;
        GameCubeBus bus = new();
        WriteSonicPrsDecompressRoutine(bus.Memory, pc);
        bus.Memory.Load(source, [0x17, (byte)'A', (byte)'B', (byte)'C', 0x00, 0x00]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_4000,
        };
        state.Gpr[3] = source;
        state.Gpr[4] = destination;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicPrsDecompress(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.True(skippedInstructions > 0);
        Assert.Equal(0x8000_4000u, state.Pc);
        Assert.Equal(3u, state.Gpr[3]);
        Assert.Equal((byte)'A', bus.Memory.Read8(destination));
        Assert.Equal((byte)'B', bus.Memory.Read8(destination + 1));
        Assert.Equal((byte)'C', bus.Memory.Read8(destination + 2));
    }

    [Fact]
    public void SonicPrsDecompressFastForwardHandlesLongBackReference()
    {
        const uint pc = 0x8000_3000;
        const uint source = 0x8001_0000;
        const uint destination = 0x8001_1000;
        GameCubeBus bus = new();
        WriteSonicPrsDecompressRoutine(bus.Memory, pc);
        bus.Memory.Load(source, [0x57, (byte)'A', (byte)'B', (byte)'C', 0xE9, 0xFF, 0x00, 0x00]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_4000,
        };
        state.Gpr[3] = source;
        state.Gpr[4] = destination;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicPrsDecompress(state, bus, out _);

        Assert.True(skipped);
        Assert.Equal(6u, state.Gpr[3]);
        Assert.Equal("ABCABC"u8.ToArray(), Enumerable.Range(0, 6).Select(offset => bus.Memory.Read8(destination + (uint)offset)).ToArray());
    }

    [Fact]
    public void SonicPrsDecompressFastForwardHandlesOverlappingLongBackReference()
    {
        const uint pc = 0x8000_3000;
        const uint source = 0x8001_0000;
        const uint destination = 0x8001_1000;
        GameCubeBus bus = new();
        WriteSonicPrsDecompressRoutine(bus.Memory, pc);
        bus.Memory.Load(source, [0x15, (byte)'A', 0xFE, 0xFF, 0x00, 0x00]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_4000,
        };
        state.Gpr[3] = source;
        state.Gpr[4] = destination;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicPrsDecompress(state, bus, out _);

        Assert.True(skipped);
        Assert.Equal(9u, state.Gpr[3]);
        Assert.Equal("AAAAAAAAA"u8.ToArray(), Enumerable.Range(0, 9).Select(offset => bus.Memory.Read8(destination + (uint)offset)).ToArray());
    }

    [Fact]
    public void SonicPrsDecompressFastForwardHandlesShortBackReference()
    {
        const uint pc = 0x8000_3000;
        const uint source = 0x8001_0000;
        const uint destination = 0x8001_1000;
        GameCubeBus bus = new();
        WriteSonicPrsDecompressRoutine(bus.Memory, pc);
        bus.Memory.Load(source, [0x47, (byte)'A', (byte)'B', (byte)'C', 0xFD, 0x01, 0x00, 0x00]);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_4000,
        };
        state.Gpr[3] = source;
        state.Gpr[4] = destination;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicPrsDecompress(state, bus, out _);

        Assert.True(skipped);
        Assert.Equal(6u, state.Gpr[3]);
        Assert.Equal("ABCABC"u8.ToArray(), Enumerable.Range(0, 6).Select(offset => bus.Memory.Read8(destination + (uint)offset)).ToArray());
    }

    [Fact]
    public void SonicPrsDecompressFastForwardHandlesUnsignedShortBackReferenceOffset()
    {
        const uint pc = 0x8000_3000;
        const uint source = 0x8001_0000;
        const uint destination = 0x8001_2000;
        GameCubeBus bus = new();
        WriteSonicPrsDecompressRoutine(bus.Memory, pc);
        List<byte> compressed = [];
        for (int block = 0; block < 32; block++)
        {
            compressed.Add(0xFF);
            for (int index = 0; index < 8; index++)
            {
                compressed.Add((byte)(block * 8 + index));
            }
        }

        compressed.Add(0x20);
        compressed.Add(0x00);
        compressed.Add(0x00);
        compressed.Add(0x00);
        bus.Memory.Load(source, compressed.ToArray());
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_4000,
        };
        state.Gpr[3] = source;
        state.Gpr[4] = destination;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicPrsDecompress(state, bus, out _);

        Assert.True(skipped);
        Assert.Equal(258u, state.Gpr[3]);
        Assert.Equal(0, bus.Memory.Read8(destination + 256));
        Assert.Equal(1, bus.Memory.Read8(destination + 257));
    }

    [Fact]
    public void SonicTrigTableInitFastForwardWritesSinCosPairsAndFinishesLoop()
    {
        const uint pc = 0x8011_8300;
        const uint destination = 0x8009_0000;
        GameCubeBus bus = new();
        WriteSonicTrigTableInitLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc - 4,
        };
        state.Gpr[27] = destination;
        state.Gpr[28] = 0;
        state.Gpr[29] = 0;
        state.Gpr[31] = 4;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicTrigTableInit(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(722, skippedInstructions);
        Assert.Equal(pc + 0x4C, state.Pc);
        Assert.Equal(destination + 32, state.Gpr[27]);
        Assert.Equal(4u, state.Gpr[28]);
        Assert.Equal(8u, state.Gpr[29]);
        Assert.Equal(722ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 722u, state.Spr[22]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        AssertSingleNear(0.0f, ReadSingle(bus.Memory, destination + 0));
        AssertSingleNear(1.0f, ReadSingle(bus.Memory, destination + 4));
        AssertSingleNear(MathF.Sin(2.0f * MathF.Tau / 65_536.0f), ReadSingle(bus.Memory, destination + 8));
        AssertSingleNear(MathF.Cos(2.0f * MathF.Tau / 65_536.0f), ReadSingle(bus.Memory, destination + 12));
    }

    [Fact]
    public void SonicTrigTableInitFastForwardResumesFromMidLoopBody()
    {
        const uint pc = 0x8011_8300;
        const uint destination = 0x8009_1000;
        GameCubeBus bus = new();
        WriteSonicTrigTableInitLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[27] = destination;
        state.Gpr[28] = 2;
        state.Gpr[29] = 4;
        state.Gpr[31] = 4;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicTrigTableInit(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(362, skippedInstructions);
        Assert.Equal(pc + 0x4C, state.Pc);
        Assert.Equal(destination + 16, state.Gpr[27]);
        Assert.Equal(4u, state.Gpr[28]);
        Assert.Equal(8u, state.Gpr[29]);
        AssertSingleNear(MathF.Sin(4.0f * MathF.Tau / 65_536.0f), ReadSingle(bus.Memory, destination + 0));
        AssertSingleNear(MathF.Cos(4.0f * MathF.Tau / 65_536.0f), ReadSingle(bus.Memory, destination + 4));
        AssertSingleNear(MathF.Sin(6.0f * MathF.Tau / 65_536.0f), ReadSingle(bus.Memory, destination + 8));
        AssertSingleNear(MathF.Cos(6.0f * MathF.Tau / 65_536.0f), ReadSingle(bus.Memory, destination + 12));
    }

    [Fact]
    public void SonicTrigTableInitFastForwardDoesNotCrossPositiveDecrementer()
    {
        const uint pc = 0x8011_8300;
        GameCubeBus bus = new();
        WriteSonicTrigTableInitLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[27] = 0x8009_0000;
        state.Gpr[28] = 0;
        state.Gpr[29] = 0;
        state.Gpr[31] = 4;
        state.Spr[22] = 100;

        bool skipped = InvokeFastForwardSonicTrigTableInit(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
    }

    [Fact]
    public void SonicBitUnpackRowsFastForwardProcessesRemainingRows()
    {
        const uint pc = 0x800E_201C;
        const uint address = 0x800A_0000;
        GameCubeBus bus = new();
        WriteSonicBitUnpackRows(bus.Memory, pc);
        for (uint offset = 0; offset < 7; offset++)
        {
            bus.Memory.Write8(address + offset, (byte)(0x10 + offset));
        }

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = address;
        state.Gpr[7] = 22;
        state.Gpr[9] = 0;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicBitUnpackRows(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(800, skippedInstructions);
        Assert.Equal(pc + 0x15C, state.Pc);
        Assert.Equal(address + 6, state.Gpr[3]);
        Assert.Equal(address + 6, state.Gpr[6]);
        Assert.Equal(24u, state.Gpr[7]);
        Assert.Equal(3u, state.Gpr[8]);
        Assert.Equal(8u, state.Gpr[11]);
        Assert.Equal(24u, state.Gpr[12]);
        Assert.Equal(0x15u, state.Gpr[31]);
        Assert.Equal(0u, state.Ctr);
        Assert.Equal(800ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 800u, state.Spr[22]);
        for (uint offset = 0; offset < 6; offset++)
        {
            Assert.Equal((byte)(0x10 + offset), bus.Memory.Read8(address + offset));
        }
    }

    [Fact]
    public void SonicBitUnpackRowsFastForwardHonorsBitOffset()
    {
        const uint pc = 0x800E_201C;
        const uint address = 0x800A_1000;
        GameCubeBus bus = new();
        WriteSonicBitUnpackRows(bus.Memory, pc);
        bus.Memory.Write8(address + 0, 0b_1010_1100);
        bus.Memory.Write8(address + 1, 0b_0110_0011);
        bus.Memory.Write8(address + 2, 0b_1111_0000);
        bus.Memory.Write8(address + 3, 0b_0000_1111);

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = address;
        state.Gpr[7] = 23;
        state.Gpr[9] = 4;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicBitUnpackRows(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(400, skippedInstructions);
        Assert.Equal(GatherExpectedByte([0b_1010_1100, 0b_0110_0011, 0b_1111_0000, 0b_0000_1111], 4), bus.Memory.Read8(address + 0));
        Assert.Equal(GatherExpectedByte([0b_1010_1100, 0b_0110_0011, 0b_1111_0000, 0b_0000_1111], 12), bus.Memory.Read8(address + 1));
        Assert.Equal(GatherExpectedByte([0b_1010_1100, 0b_0110_0011, 0b_1111_0000, 0b_0000_1111], 20), bus.Memory.Read8(address + 2));
        Assert.Equal(28u, state.Gpr[5]);
        Assert.Equal(28u, state.Gpr[12]);
    }

    [Fact]
    public void SonicBitUnpackRowsFastForwardDoesNotCrossPositiveDecrementer()
    {
        const uint pc = 0x800E_201C;
        GameCubeBus bus = new();
        WriteSonicBitUnpackRows(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x800A_0000;
        state.Gpr[7] = 22;
        state.Gpr[9] = 0;
        state.Spr[22] = 100;

        bool skipped = InvokeFastForwardSonicBitUnpackRows(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(pc, state.Pc);
    }

    [Fact]
    public void SonicBitUnpackByteFastForwardStoresOneByteAndContinuesRow()
    {
        const uint pc = 0x800E_203C;
        const uint address = 0x800A_2000;
        GameCubeBus bus = new();
        WriteSonicBitUnpackByte(bus.Memory, pc);
        bus.Memory.Write8(address + 0, 0b_1010_1100);
        bus.Memory.Write8(address + 1, 0b_0110_0011);
        bus.Memory.Write8(address + 2, 0b_1111_0000);

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = address;
        state.Gpr[5] = 0;
        state.Gpr[6] = address;
        state.Gpr[7] = 3;
        state.Gpr[8] = 0;
        state.Gpr[11] = 0;
        state.Gpr[12] = 0;
        state.Gpr[31] = 0;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicBitUnpackRows(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(130, skippedInstructions);
        Assert.Equal(pc - 0x14, state.Pc);
        Assert.Equal(1u, state.Gpr[8]);
        Assert.Equal(8u, state.Gpr[5]);
        Assert.Equal(address + 1, state.Gpr[6]);
        Assert.Equal(8u, state.Gpr[11]);
        Assert.Equal(8u, state.Gpr[12]);
        Assert.Equal(0b_1010_1100u, state.Gpr[31]);
        Assert.Equal(0b_1010_1100, bus.Memory.Read8(address));
    }

    [Fact]
    public void SonicBitUnpackByteFastForwardFinishesFinalRow()
    {
        const uint pc = 0x800E_203C;
        const uint address = 0x800A_3000;
        GameCubeBus bus = new();
        WriteSonicBitUnpackByte(bus.Memory, pc);
        bus.Memory.Write8(address + 0, 0x12);
        bus.Memory.Write8(address + 1, 0x34);
        bus.Memory.Write8(address + 2, 0x56);

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = address;
        state.Gpr[5] = 16;
        state.Gpr[6] = address + 2;
        state.Gpr[7] = 23;
        state.Gpr[8] = 2;
        state.Gpr[11] = 0;
        state.Gpr[12] = 16;
        state.Gpr[31] = 0;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicBitUnpackRows(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(130, skippedInstructions);
        Assert.Equal(pc + 0x13C, state.Pc);
        Assert.Equal(24u, state.Gpr[7]);
        Assert.Equal(address + 3, state.Gpr[3]);
        Assert.Equal(address + 3, state.Gpr[6]);
        Assert.Equal(0x56u, state.Gpr[31]);
        Assert.Equal(0x56, bus.Memory.Read8(address + 2));
    }

    [Fact]
    public void SonicBitScanSetupFastForwardFindsBestLeadingBitCount()
    {
        const uint pc = 0x800E_1FA0;
        const uint address = 0x800A_4000;
        GameCubeBus bus = new();
        WriteSonicBitScanSetup(bus.Memory, pc);
        for (uint row = 0; row < 24; row++)
        {
            bus.Memory.Write8(address + row * 3 + 0, 0x00);
            bus.Memory.Write8(address + row * 3 + 1, 0x00);
            bus.Memory.Write8(address + row * 3 + 2, 0x01);
        }

        bus.Memory.Write8(address + 5 * 3 + 2, 0x20);
        bus.Memory.Write8(address + 11 * 3 + 1, 0x40);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = address;
        state.Gpr[10] = 24;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicBitUnpackRows(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(677, skippedInstructions);
        Assert.Equal(pc + 0x78, state.Pc);
        Assert.Equal(74u, state.Gpr[4]);
        Assert.Equal(0u, state.Ctr);
        Assert.Equal(2u, state.Gpr[10]);
        Assert.Equal(677ul, state.TimeBase);
        Assert.Equal(0xFFFF_F000u - 677u, state.Spr[22]);
    }

    [Fact]
    public void SonicBitScanRowFastForwardProcessesSingleScanRow()
    {
        const uint pc = 0x800E_1FAC;
        const uint address = 0x800A_5000;
        GameCubeBus bus = new();
        WriteSonicBitScanRow(bus.Memory, pc);
        bus.Memory.Write8(address + 0, 0x00);
        bus.Memory.Write8(address + 1, 0x00);
        bus.Memory.Write8(address + 2, 0x20);
        PowerPcState state = new()
        {
            Pc = pc,
            Ctr = 3,
        };
        state.Gpr[3] = address;
        state.Gpr[4] = 2;
        state.Gpr[10] = 24;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicBitUnpackRows(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(28, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(5u, state.Gpr[4]);
        Assert.Equal(2u, state.Gpr[6]);
        Assert.Equal(0x80u, state.Gpr[7]);
        Assert.Equal(2u, state.Gpr[10]);
        Assert.Equal(2u, state.Ctr);
    }

    [Fact]
    public void SonicBitPlaneInnerExpandFastForwardMatchesInterpreterPaths()
    {
        (uint Ctr, byte[] Bytes, string Name)[] cases =
        [
            (2, [0x81, 0x06], "exhausts"),
            (5, [0x81, 0x06, 0xF0], "partial"),
        ];

        foreach (var testCase in cases)
        {
            const uint pc = 0x800E_0DF0;
            const uint source = 0x800A_6000;
            const uint destination = 0x800A_7000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicBitPlaneInnerExpand(expectedBus.Memory, pc);
            WriteSonicBitPlaneInnerExpand(actualBus.Memory, pc);
            for (uint index = 0; index < testCase.Bytes.Length; index++)
            {
                expectedBus.Memory.Write8(source + index, testCase.Bytes[index]);
                actualBus.Memory.Write8(source + index, testCase.Bytes[index]);
            }

            PowerPcState expectedState = CreateSonicBitPlaneInnerExpandState(pc, source, destination, testCase.Ctr);
            PowerPcState actualState = CreateSonicBitPlaneInnerExpandState(pc, source, destination, testCase.Ctr);
            uint expectedSource = source + Math.Min(testCase.Ctr, 3u);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            do
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }
            while (((testCase.Ctr <= 3 ? expectedState.Pc != pc + 0x130 : expectedState.Pc != pc || expectedState.Gpr[31] != expectedSource)) && expectedInstructions < 512);

            bool skipped = InvokeFastForwardSonicBitUnpackRows(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Ctr, actualState.Ctr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 7, 10, 11, 31 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{testCase.Name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }

            for (uint offset = 0; offset < 0x100; offset += sizeof(ushort))
            {
                Assert.Equal(expectedBus.Memory.Read16(destination + offset), actualBus.Memory.Read16(destination + offset));
            }
        }
    }

    [Fact]
    public void SonicTickWaitLoopFastForwardAdvancesTickPastThreshold()
    {
        const uint pc = 0x8011_7E0C;
        const uint smallDataBase = 0x803B_52C0;
        GameCubeBus bus = new();
        WriteSonicTickWaitLoop(bus.Memory, pc);
        uint tickAddress = unchecked(smallDataBase + (uint)(short)0x8B00);
        uint baseAddress = unchecked(smallDataBase + (uint)(short)0x8DC0);
        uint delayAddress = unchecked(smallDataBase + (uint)(short)0x8DC4);
        uint callbackAddress = unchecked(smallDataBase + (uint)(short)0x8B14);
        uint activeFlagAddress = unchecked(smallDataBase + (uint)(short)0x85E8);
        bus.Memory.Write32(tickAddress, 0x190);
        bus.Memory.Write32(baseAddress, 0x190);
        bus.Memory.Write32(delayAddress, 1);
        bus.Memory.Write32(callbackAddress, 0x8002_2F5C);
        bus.Memory.Write32(activeFlagAddress, 1);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[13] = smallDataBase;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicTickWaitLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(8, skippedInstructions);
        Assert.Equal(pc + 0x1C, state.Pc);
        Assert.Equal(0x191u, bus.Memory.Read32(tickAddress));
        Assert.Equal(0u, bus.Memory.Read32(activeFlagAddress));
        Assert.Equal(0x191u, state.Gpr[3]);
        Assert.Equal(0x190u, state.Gpr[0]);
        Assert.Equal(0x4000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void SonicCallbackWaitLoopFastForwardCompletesInstalledCallback()
    {
        const uint pc = 0x8002_2EF0;
        const uint smallDataBase = 0x803B_52C0;
        GameCubeBus bus = new();
        WriteSonicCallbackWaitLoop(bus.Memory, pc);
        uint callbackAddress = unchecked(smallDataBase + (uint)(short)0x8B14);
        uint activeFlagAddress = unchecked(smallDataBase + (uint)(short)0x85E8);
        bus.Memory.Write32(callbackAddress, 0x8002_2F5C);
        bus.Memory.Write32(activeFlagAddress, 2);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[13] = smallDataBase;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicCallbackWaitLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(6, skippedInstructions);
        Assert.Equal(pc + 0x0C, state.Pc);
        Assert.Equal(0u, bus.Memory.Read32(activeFlagAddress));
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void SonicDotProductLoopFastForwardWritesRemainingDotProducts()
    {
        const uint pc = 0x8012_8DB8;
        const uint smallDataBase2 = 0x803B_6520;
        const uint fixedVector = 0x800B_0000;
        const uint input = 0x800B_1000;
        const uint destination = 0x800B_3000;
        GameCubeBus bus = new();
        WriteSonicDotProductLoop(bus.Memory, pc);
        WriteSingle(bus.Memory, unchecked(smallDataBase2 + (uint)(short)0xAB68), 1.0f);
        for (uint index = 0; index < 32; index++)
        {
            WriteSingle(bus.Memory, fixedVector + index * 4, index + 1);
            WriteSingle(bus.Memory, input + index * 4, 1.0f);
            WriteSingle(bus.Memory, input + 0x80 + index * 4, 2.0f);
        }

        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8012_A75C,
        };
        state.Gpr[0] = 2;
        state.Gpr[2] = smallDataBase2;
        state.Gpr[4] = input;
        state.Gpr[5] = destination;
        state.Gpr[6] = fixedVector;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicDotProductLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(211, skippedInstructions);
        Assert.Equal(state.Lr, state.Pc);
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal(input + 0x100, state.Gpr[4]);
        Assert.Equal(destination + 8, state.Gpr[5]);
        AssertSingleNear(529.0f, ReadSingle(bus.Memory, destination));
        AssertSingleNear(1057.0f, ReadSingle(bus.Memory, destination + 4));
    }

    [Fact]
    public void SonicResourceTableLookupFastForwardReturnsMatchingIndex()
    {
        const uint pc = 0x8011_6B9C;
        const uint smallDataBase = 0x803B_52C0;
        const uint table = 0x800B_0000;
        const uint targetObject = 0x800C_0000;
        GameCubeBus bus = new();
        WriteSonicResourceTableLookup(bus.Memory, pc);
        bus.Memory.Write32(unchecked(smallDataBase - 29196u), 4);
        bus.Memory.Write32(unchecked(smallDataBase - 29192u), table);
        bus.Memory.Write32(table + 0x0C, 0);
        bus.Memory.Write32(table + 0x18 + 0x0C, 0x800C_0100);
        bus.Memory.Write32(0x800C_0100, 0x1111_2222);
        bus.Memory.Write32(table + 0x30 + 0x0C, targetObject);
        bus.Memory.Write32(targetObject, 0x007A_1201);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8011_7000,
        };
        state.Gpr[3] = 0x007A_1201;
        state.Gpr[13] = smallDataBase;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicResourceTableLookup(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.True(skippedInstructions > 0);
        Assert.Equal(2u, state.Gpr[3]);
        Assert.Equal(table + 0x30, state.Gpr[4]);
        Assert.Equal(targetObject, state.Gpr[5]);
        Assert.Equal(2u, state.Gpr[6]);
        Assert.Equal(2u, state.Ctr);
        Assert.Equal(0x4000_0000u, state.Cr & 0xF000_0000);
        Assert.Equal(state.Lr, state.Pc);
    }

    [Fact]
    public void SonicResourceTableLookupFastForwardReturnsMinusOneWhenMissing()
    {
        const uint pc = 0x8011_6B9C;
        const uint smallDataBase = 0x803B_52C0;
        const uint table = 0x800B_0000;
        GameCubeBus bus = new();
        WriteSonicResourceTableLookup(bus.Memory, pc);
        bus.Memory.Write32(unchecked(smallDataBase - 29196u), 2);
        bus.Memory.Write32(unchecked(smallDataBase - 29192u), table);
        bus.Memory.Write32(table + 0x0C, 0x800C_0000);
        bus.Memory.Write32(0x800C_0000, 0x1111_2222);
        bus.Memory.Write32(table + 0x18 + 0x0C, 0);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8011_7000,
        };
        state.Gpr[3] = 0x007A_1201;
        state.Gpr[13] = smallDataBase;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicResourceTableLookup(state, bus, out _);

        Assert.True(skipped);
        Assert.Equal(0xFFFF_FFFFu, state.Gpr[3]);
        Assert.Equal(table + 0x30, state.Gpr[4]);
        Assert.Equal(0u, state.Gpr[5]);
        Assert.Equal(2u, state.Gpr[6]);
        Assert.Equal(0u, state.Ctr);
        Assert.Equal(0x8000_0000u, state.Cr & 0xF000_0000);
        Assert.Equal(state.Lr, state.Pc);
    }

    [Fact]
    public void SonicResourceFlagWaitLoopFastForwardMatchesInterpreter()
    {
        const uint smallDataBase = 0x803B_52C0;
        uint flagAddress = unchecked(smallDataBase + 0xFFFF_8A20u);
        (uint Pc, uint Decrementer, uint R0, uint Cr, int ExpectedInstructions, string Name)[] cases =
        [
            (0x800E_BEA0, 99, 0xFFFF_FFFF, 0x1234_5678, 99, "loop-entry"),
            (0x800E_BEA4, 101, 0, 0x1234_5678, 101, "compare-tail"),
            (0x800E_BEA8, 100, 0, 0x2000_0000, 100, "branch-tail"),
        ];

        foreach (var testCase in cases)
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicResourceFlagWaitLoop(expectedBus.Memory);
            WriteSonicResourceFlagWaitLoop(actualBus.Memory);
            expectedBus.Memory.Write32(flagAddress, 0);
            actualBus.Memory.Write32(flagAddress, 0);
            PowerPcState expectedState = new()
            {
                Pc = testCase.Pc,
                Cr = testCase.Cr,
            };
            PowerPcState actualState = expectedState.Clone();
            expectedState.Gpr[0] = testCase.R0;
            expectedState.Gpr[13] = smallDataBase;
            expectedState.Spr[22] = testCase.Decrementer;
            actualState.Gpr[0] = expectedState.Gpr[0];
            actualState.Gpr[13] = smallDataBase;
            actualState.Spr[22] = expectedState.Spr[22];

            bool skipped = InvokeFastForwardSonicResourceFlagWaitLoop(actualState, actualBus, out int skippedInstructions);
            new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.Equal(testCase.ExpectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            Assert.Equal(expectedState.Gpr[0], actualState.Gpr[0]);
            Assert.Equal(expectedState.Gpr[13], actualState.Gpr[13]);
            Assert.Equal(0u, actualBus.Memory.Read32(flagAddress));
        }
    }

    [Fact]
    public void SonicDvdStatusWaitLoopFastForwardStopsBeforeDiscCompletion()
    {
        const uint pc = 0x800D_B9AC;
        GameCubeBus bus = new();
        WriteSonicDvdStatusWaitLoop(bus.Memory);
        bus.DiscInterfaceCommandLatencyCycles = 0x2000;
        bus.Write32(0xCC00_3004, GameCubeBus.ProcessorInterfaceDiscInterrupt);
        bus.Write32(0xCC00_6000, GameCubeBus.DiscInterfaceInterruptMask);
        bus.Write32(0xCC00_6008, 0xA800_0000);
        bus.Write32(0xCC00_600C, 0x0000_0080);
        bus.Write32(0xCC00_6010, 0x20);
        bus.Write32(0xCC00_6014, 0x8000_1000);
        bus.Write32(0xCC00_6018, 0x20);
        bus.Write32(0xCC00_601C, 1);
        PowerPcState state = new()
        {
            Pc = pc,
            TimeBase = 0x1000,
        };
        state.Gpr[27] = 1;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicDvdStatusWaitLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(0x2000 - 64, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(0x1000ul + (ulong)skippedInstructions, state.TimeBase);
        Assert.False(bus.HasPendingExternalInterrupt);
        DiscInterfaceDebugSnapshot snapshot = bus.GetDiscInterfaceDebugSnapshot();
        Assert.True(snapshot.HasPendingCommand);
        Assert.Equal(64ul, snapshot.PendingCommandCycles);
        Assert.Empty(snapshot.CommandHistory);
    }

    [Fact]
    public void SonicDvdStatusWaitLoopFastForwardEmptySlotStopsAtDecrementer()
    {
        const uint pc = 0x800D_B9AC;
        GameCubeBus bus = new();
        WriteSonicDvdStatusWaitLoop(bus.Memory);
        bus.Memory.Write8(0x8000_30E3, 0);
        bus.Memory.Write32(0x803B_D080, 0);
        PowerPcState state = new()
        {
            Pc = pc,
            TimeBase = 0x2000,
        };
        state.Gpr[27] = 0;
        state.Spr[22] = 300;

        bool skipped = InvokeFastForwardSonicDvdStatusWaitLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(300, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(0x2000ul + 300, state.TimeBase);
        Assert.Equal(0u, state.Spr[22]);
        Assert.False(bus.HasPendingExternalInterrupt);
    }

    [Fact]
    public void SonicInitTableLoopTailFastForwardMatchesInterpreterPaths()
    {
        const uint pc = 0x8006_90D4;
        (uint InitialR29, uint ExpectedPc)[] cases =
        [
            (41, 0x8006_8164),
            (42, 0x8006_90F0),
            (0xFFFF_FFFE, 0x8006_8164),
        ];

        foreach ((uint initialR29, uint expectedPc) in cases)
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicInitTableLoopTail(expectedBus.Memory);
            WriteSonicInitTableLoopTail(actualBus.Memory);
            PowerPcState expectedState = new()
            {
                Pc = pc,
                Cr = 0x1234_5678,
                TimeBase = 0x1000,
            };
            PowerPcState actualState = expectedState.Clone();
            expectedState.Gpr[25] = 0x8020_1000;
            expectedState.Gpr[26] = 0x8020_2000;
            expectedState.Gpr[28] = 0x8020_3000;
            expectedState.Gpr[29] = initialR29;
            actualState.Gpr[25] = expectedState.Gpr[25];
            actualState.Gpr[26] = expectedState.Gpr[26];
            actualState.Gpr[28] = expectedState.Gpr[28];
            actualState.Gpr[29] = expectedState.Gpr[29];
            expectedState.Spr[22] = 0xFFFF_F000;
            actualState.Spr[22] = expectedState.Spr[22];

            bool skipped = InvokeFastForwardSonicInitTableLoopTail(actualState, actualBus, out int skippedInstructions);
            new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(7, skippedInstructions);
            Assert.Equal(expectedPc, actualState.Pc);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            Assert.Equal(expectedState.Gpr[0], actualState.Gpr[0]);
            Assert.Equal(expectedState.Gpr[25], actualState.Gpr[25]);
            Assert.Equal(expectedState.Gpr[26], actualState.Gpr[26]);
            Assert.Equal(expectedState.Gpr[28], actualState.Gpr[28]);
            Assert.Equal(expectedState.Gpr[29], actualState.Gpr[29]);
        }
    }

    [Fact]
    public void SonicInitTableNullEntryLoopFastForwardStopsBeforeRealEntry()
    {
        const uint pc = 0x8006_8164;
        const uint table = 0x8030_1000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicInitTableNullEntryLoop(expectedBus.Memory);
        WriteSonicInitTableNullEntryLoop(actualBus.Memory);
        for (uint slot = 0; slot < 2; slot++)
        {
            expectedBus.Memory.Write16(table + slot * 0x30 + 0x08, (ushort)(0x1200 + slot));
            expectedBus.Memory.Write32(table + slot * 0x30 + 0x14, 0xFFFF_FFFF);
            actualBus.Memory.Write16(table + slot * 0x30 + 0x08, (ushort)(0x1200 + slot));
            actualBus.Memory.Write32(table + slot * 0x30 + 0x14, 0xFFFF_FFFF);
        }

        expectedBus.Memory.Write32(table + 2 * 0x30 + 0x14, 0x0000_1000);
        actualBus.Memory.Write32(table + 2 * 0x30 + 0x14, 0x0000_1000);
        PowerPcState expectedState = CreateSonicInitTableNullEntryState(pc, table, 0);
        PowerPcState actualState = CreateSonicInitTableNullEntryState(pc, table, 0);

        bool skipped = InvokeFastForwardSonicInitTableNullEntryLoop(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(26, skippedInstructions);
        Assert.Equal(pc, actualState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 21, 25, 26, 28, 29, 31 })
        {
            Assert.True(
                expectedState.Gpr[register] == actualState.Gpr[register],
                $"r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
        }
    }

    [Fact]
    public void SonicInitTableNullEntryLoopFastForwardMatchesInterpreterExit()
    {
        const uint pc = 0x8006_8164;
        const uint table = 0x8030_2000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicInitTableNullEntryLoop(expectedBus.Memory);
        WriteSonicInitTableNullEntryLoop(actualBus.Memory);
        for (uint slot = 41; slot < 43; slot++)
        {
            expectedBus.Memory.Write16(table + slot * 0x30 + 0x08, (ushort)(0x2200 + slot));
            expectedBus.Memory.Write32(table + slot * 0x30 + 0x14, 0xFFFF_FFFF);
            actualBus.Memory.Write16(table + slot * 0x30 + 0x08, (ushort)(0x2200 + slot));
            actualBus.Memory.Write32(table + slot * 0x30 + 0x14, 0xFFFF_FFFF);
        }

        PowerPcState expectedState = CreateSonicInitTableNullEntryState(pc, table + 41 * 0x30, 41);
        PowerPcState actualState = CreateSonicInitTableNullEntryState(pc, table + 41 * 0x30, 41);

        bool skipped = InvokeFastForwardSonicInitTableNullEntryLoop(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(26, skippedInstructions);
        Assert.Equal(0x8006_90F0u, actualState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 21, 25, 26, 28, 29, 31 })
        {
            Assert.True(
                expectedState.Gpr[register] == actualState.Gpr[register],
                $"r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
        }
    }

    [Fact]
    public void SonicRecordHeaderScanLoopFastForwardStopsBeforeCallableRecord()
    {
        const uint pc = 0x8013_2AD8;
        const uint records = 0x8030_2000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicRecordHeaderScanLoop(expectedBus.Memory);
        WriteSonicRecordHeaderScanLoop(actualBus.Memory);
        expectedBus.Memory.Write8(records + 0x00, 0);
        actualBus.Memory.Write8(records + 0x00, 0);
        expectedBus.Memory.Write8(records + 0x40, 1);
        expectedBus.Memory.Write8(records + 0x41, 3);
        actualBus.Memory.Write8(records + 0x40, 1);
        actualBus.Memory.Write8(records + 0x41, 3);
        expectedBus.Memory.Write8(records + 0x80, 1);
        expectedBus.Memory.Write8(records + 0x81, 2);
        actualBus.Memory.Write8(records + 0x80, 1);
        actualBus.Memory.Write8(records + 0x81, 2);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[30] = 0;
        expectedState.Gpr[31] = records;
        actualState.Gpr[30] = expectedState.Gpr[30];
        actualState.Gpr[31] = expectedState.Gpr[31];
        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        bool skipped = InvokeFastForwardSonicRecordHeaderScanLoop(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(19, skippedInstructions);
        Assert.Equal(pc, actualState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        Assert.Equal(expectedState.Gpr[0], actualState.Gpr[0]);
        Assert.Equal(expectedState.Gpr[3], actualState.Gpr[3]);
        Assert.Equal(expectedState.Gpr[30], actualState.Gpr[30]);
        Assert.Equal(expectedState.Gpr[31], actualState.Gpr[31]);
    }

    [Fact]
    public void SonicRecordHeaderScanLoopFastForwardMatchesInterpreterExit()
    {
        const uint pc = 0x8013_2AD8;
        const uint records = 0x8030_3000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicRecordHeaderScanLoop(expectedBus.Memory);
        WriteSonicRecordHeaderScanLoop(actualBus.Memory);
        expectedBus.Memory.Write8(records + 38 * 0x40, 0);
        expectedBus.Memory.Write8(records + 39 * 0x40, 0);
        actualBus.Memory.Write8(records + 38 * 0x40, 0);
        actualBus.Memory.Write8(records + 39 * 0x40, 0);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
            TimeBase = 0x2000,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[30] = 38;
        expectedState.Gpr[31] = records + 38 * 0x40;
        actualState.Gpr[30] = expectedState.Gpr[30];
        actualState.Gpr[31] = expectedState.Gpr[31];
        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        bool skipped = InvokeFastForwardSonicRecordHeaderScanLoop(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(16, skippedInstructions);
        Assert.Equal(0x8013_2B08u, actualState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        Assert.Equal(expectedState.Gpr[0], actualState.Gpr[0]);
        Assert.Equal(expectedState.Gpr[3], actualState.Gpr[3]);
        Assert.Equal(expectedState.Gpr[30], actualState.Gpr[30]);
        Assert.Equal(expectedState.Gpr[31], actualState.Gpr[31]);
    }

    [Fact]
    public void SonicFlagRecordScanLoopFastForwardStopsBeforeCallableRecord()
    {
        const uint pc = 0x8013_7E58;
        const uint records = 0x8030_4000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicFlagRecordScanLoop(expectedBus.Memory);
        WriteSonicFlagRecordScanLoop(actualBus.Memory);
        expectedBus.Memory.Write8(records + 0x00, 0);
        expectedBus.Memory.Write8(records + 0x64, 2);
        expectedBus.Memory.Write8(records + 0xC8, 1);
        actualBus.Memory.Write8(records + 0x00, 0);
        actualBus.Memory.Write8(records + 0x64, 2);
        actualBus.Memory.Write8(records + 0xC8, 1);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[3] = 0xAAAA_BBBB;
        expectedState.Gpr[30] = 0;
        expectedState.Gpr[31] = records;
        actualState.Gpr[3] = expectedState.Gpr[3];
        actualState.Gpr[30] = expectedState.Gpr[30];
        actualState.Gpr[31] = expectedState.Gpr[31];
        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        bool skipped = InvokeFastForwardSonicFlagRecordScanLoop(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(14, skippedInstructions);
        Assert.Equal(pc, actualState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        Assert.Equal(expectedState.Gpr[0], actualState.Gpr[0]);
        Assert.Equal(0xAAAA_BBBBu, actualState.Gpr[3]);
        Assert.Equal(expectedState.Gpr[3], actualState.Gpr[3]);
        Assert.Equal(expectedState.Gpr[30], actualState.Gpr[30]);
        Assert.Equal(expectedState.Gpr[31], actualState.Gpr[31]);
    }

    [Fact]
    public void SonicFlagRecordScanLoopFastForwardMatchesInterpreterExit()
    {
        const uint pc = 0x8013_7E58;
        const uint records = 0x8030_5000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicFlagRecordScanLoop(expectedBus.Memory);
        WriteSonicFlagRecordScanLoop(actualBus.Memory);
        expectedBus.Memory.Write8(records + 38 * 0x64, 0);
        expectedBus.Memory.Write8(records + 39 * 0x64, 0);
        actualBus.Memory.Write8(records + 38 * 0x64, 0);
        actualBus.Memory.Write8(records + 39 * 0x64, 0);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
            TimeBase = 0x2000,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[3] = 0x1234_ABCD;
        expectedState.Gpr[30] = 38;
        expectedState.Gpr[31] = records + 38 * 0x64;
        actualState.Gpr[3] = expectedState.Gpr[3];
        actualState.Gpr[30] = expectedState.Gpr[30];
        actualState.Gpr[31] = expectedState.Gpr[31];
        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        bool skipped = InvokeFastForwardSonicFlagRecordScanLoop(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(14, skippedInstructions);
        Assert.Equal(0x8013_7E7Cu, actualState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        Assert.Equal(expectedState.Gpr[0], actualState.Gpr[0]);
        Assert.Equal(expectedState.Gpr[3], actualState.Gpr[3]);
        Assert.Equal(expectedState.Gpr[30], actualState.Gpr[30]);
        Assert.Equal(expectedState.Gpr[31], actualState.Gpr[31]);
    }

    [Fact]
    public void SonicTaskSlotCallbackScanLoopFastForwardStopsBeforeNonNullSlot()
    {
        const uint pc = 0x8012_5DEC;
        const uint slots = 0x8030_6000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicTaskSlotCallbackScanLoop(expectedBus.Memory);
        WriteSonicTaskSlotCallbackScanLoop(actualBus.Memory);
        expectedBus.Memory.Write32(slots + 0x20, 0x8030_7000);
        actualBus.Memory.Write32(slots + 0x20, 0x8030_7000);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[30] = 0;
        expectedState.Gpr[31] = slots;
        actualState.Gpr[30] = expectedState.Gpr[30];
        actualState.Gpr[31] = expectedState.Gpr[31];
        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        bool skipped = InvokeFastForwardSonicTaskSlotCallbackScanLoop(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(14, skippedInstructions);
        Assert.Equal(pc, actualState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        Assert.Equal(expectedState.Gpr[3], actualState.Gpr[3]);
        Assert.Equal(expectedState.Gpr[30], actualState.Gpr[30]);
        Assert.Equal(expectedState.Gpr[31], actualState.Gpr[31]);
    }

    [Fact]
    public void SonicTaskSlotCallbackScanLoopFastForwardMatchesInterpreterExit()
    {
        const uint pc = 0x8012_5DEC;
        const uint slots = 0x8030_8000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicTaskSlotCallbackScanLoop(expectedBus.Memory);
        WriteSonicTaskSlotCallbackScanLoop(actualBus.Memory);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
            TimeBase = 0x2000,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[30] = 30;
        expectedState.Gpr[31] = slots + 30 * 0x10;
        actualState.Gpr[30] = expectedState.Gpr[30];
        actualState.Gpr[31] = expectedState.Gpr[31];
        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        bool skipped = InvokeFastForwardSonicTaskSlotCallbackScanLoop(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(14, skippedInstructions);
        Assert.Equal(0x8012_5E1Cu, actualState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        Assert.Equal(expectedState.Gpr[3], actualState.Gpr[3]);
        Assert.Equal(expectedState.Gpr[30], actualState.Gpr[30]);
        Assert.Equal(expectedState.Gpr[31], actualState.Gpr[31]);
    }

    [Fact]
    public void SonicBitmaskDispatchScanLoopFastForwardStopsBeforeActiveEntry()
    {
        const uint pc = 0x800E_8024;
        const uint table = 0x8030_9000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicBitmaskDispatchScanLoop(expectedBus.Memory);
        WriteSonicBitmaskDispatchScanLoop(actualBus.Memory);
        expectedBus.Memory.Write32(table + 0x00, 0x0000_0001);
        expectedBus.Memory.Write32(table + 0x04, 0x0000_0002);
        expectedBus.Memory.Write32(table + 0x08, 0x0000_0100);
        actualBus.Memory.Write32(table + 0x00, 0x0000_0001);
        actualBus.Memory.Write32(table + 0x04, 0x0000_0002);
        actualBus.Memory.Write32(table + 0x08, 0x0000_0100);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[3] = table;
        expectedState.Gpr[4] = 0x0000_0100;
        actualState.Gpr[3] = expectedState.Gpr[3];
        actualState.Gpr[4] = expectedState.Gpr[4];
        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        bool skipped = InvokeFastForwardSonicBitmaskDispatchScanLoop(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(12, skippedInstructions);
        Assert.Equal(pc, actualState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        Assert.Equal(expectedState.Gpr[0], actualState.Gpr[0]);
        Assert.Equal(expectedState.Gpr[3], actualState.Gpr[3]);
        Assert.Equal(expectedState.Gpr[4], actualState.Gpr[4]);
    }

    [Fact]
    public void SonicBitmaskDispatchScanLoopFastForwardHonorsDecrementer()
    {
        const uint pc = 0x800E_8024;
        const uint table = 0x8030_A000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicBitmaskDispatchScanLoop(expectedBus.Memory);
        WriteSonicBitmaskDispatchScanLoop(actualBus.Memory);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
            TimeBase = 0x2000,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[3] = table;
        expectedState.Gpr[4] = 0x0000_0100;
        actualState.Gpr[3] = expectedState.Gpr[3];
        actualState.Gpr[4] = expectedState.Gpr[4];
        expectedState.Spr[22] = 12;
        actualState.Spr[22] = expectedState.Spr[22];

        bool skipped = InvokeFastForwardSonicBitmaskDispatchScanLoop(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(12, skippedInstructions);
        Assert.Equal(pc, actualState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(0u, actualState.Spr[22]);
        Assert.Equal(expectedState.Gpr[0], actualState.Gpr[0]);
        Assert.Equal(expectedState.Gpr[3], actualState.Gpr[3]);
        Assert.Equal(expectedState.Gpr[4], actualState.Gpr[4]);
    }

    [Fact]
    public void SonicInterruptStatusPrologueFastForwardMatchesInterpreter()
    {
        const uint pc = 0x800E_6760;
        const uint stack = 0x8030_2000;
        const uint tablePointer = 0x802B_A8C0;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicInterruptStatusPrologue(expectedBus.Memory);
        WriteSonicInterruptStatusPrologue(actualBus.Memory);
        expectedBus.Memory.Write32(tablePointer + 0x0C, 0x0040_0010);
        actualBus.Memory.Write32(tablePointer + 0x0C, 0x0040_0010);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Lr = 0x800E_7000,
            Msr = 0x8000,
            Cr = 0x1234_5678,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[1] = stack;
        expectedState.Gpr[3] = 1;
        actualState.Gpr[1] = stack;
        actualState.Gpr[3] = 1;
        for (int register = 27; register <= 31; register++)
        {
            expectedState.Gpr[register] = 0xAAAA_0000u + (uint)register;
            actualState.Gpr[register] = expectedState.Gpr[register];
        }

        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != 0x800E_67AC && expectedInstructions < 64)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        bool skipped = InvokeFastForwardSonicInterruptStatusPrologue(actualState, actualBus, out int skippedInstructions);

        Assert.Equal(0x800E_67ACu, expectedState.Pc);
        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Msr, actualState.Msr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 4, 5, 6, 28, 29, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (uint offset = 0; offset <= 0x24; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 40 + offset), actualBus.Memory.Read32(stack - 40 + offset));
        }
    }

    [Fact]
    public void SonicInterruptStatusTailFastForwardMatchesInterpreter()
    {
        const uint pc = 0x800E_68B4;
        const uint frame = 0x8030_3000;
        const uint returnAddress = 0x800E_7000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicInterruptStatusTail(expectedBus.Memory);
        WriteSonicInterruptStatusTail(actualBus.Memory);
        expectedBus.Memory.Write32(frame + 44, returnAddress);
        actualBus.Memory.Write32(frame + 44, returnAddress);
        for (int register = 27; register <= 31; register++)
        {
            uint value = 0xBBBB_0000u + (uint)register;
            expectedBus.Memory.Write32(frame + 20u + (uint)(register - 27) * sizeof(uint), value);
            actualBus.Memory.Write32(frame + 20u + (uint)(register - 27) * sizeof(uint), value);
        }

        PowerPcState expectedState = new()
        {
            Pc = pc,
            Lr = 0x800E_68BC,
            Msr = 0,
            Cr = 0x1234_5678,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[1] = frame;
        expectedState.Gpr[29] = 1;
        expectedState.Gpr[30] = 1;
        actualState.Gpr[1] = frame;
        actualState.Gpr[29] = 1;
        actualState.Gpr[30] = 1;
        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != returnAddress && expectedInstructions < 64)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        bool skipped = InvokeFastForwardSonicInterruptStatusTail(actualState, actualBus, out int skippedInstructions);

        Assert.Equal(returnAddress, expectedState.Pc);
        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Msr, actualState.Msr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 4, 5, 27, 28, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Fact]
    public void SonicInterruptStatusPollFastForwardMatchesInterpreterNoFirstBit()
    {
        const uint pc = 0x800E_67AC;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicInterruptStatusPoll(expectedBus.Memory);
        WriteSonicInterruptStatusPoll(actualBus.Memory);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[0] = 0;
        expectedState.Gpr[3] = 1;
        expectedState.Gpr[5] = 20;
        expectedState.Gpr[6] = 0xCC00_6800;
        actualState.Gpr[0] = expectedState.Gpr[0];
        actualState.Gpr[3] = expectedState.Gpr[3];
        actualState.Gpr[5] = expectedState.Gpr[5];
        actualState.Gpr[6] = expectedState.Gpr[6];
        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != 0x800E_67F0 && expectedInstructions < 16)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        bool skipped = InvokeFastForwardSonicInterruptStatusPoll(actualState, actualBus, out int skippedInstructions);

        Assert.Equal(0x800E_67F0u, expectedState.Pc);
        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 5, 6, 7, 30 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Fact]
    public void SonicInterruptStatusTimerSetupFastForwardMatchesInterpreter()
    {
        const uint pc = 0x800E_67F0;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicInterruptStatusTimerSetup(expectedBus.Memory);
        WriteSonicInterruptStatusTimerSetup(actualBus.Memory);
        expectedBus.Memory.Write32(0x8000_00F8, 0x09A7_EC80);
        actualBus.Memory.Write32(0x8000_00F8, 0x09A7_EC80);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
        };
        PowerPcState actualState = expectedState.Clone();
        expectedState.Gpr[7] = 0x1000;
        actualState.Gpr[7] = expectedState.Gpr[7];
        expectedState.Spr[22] = 0xFFFF_F000;
        actualState.Spr[22] = expectedState.Spr[22];

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != 0x800E_6814 && expectedInstructions < 16)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        bool skipped = InvokeFastForwardSonicInterruptStatusTimerSetup(actualState, actualBus, out int skippedInstructions);

        Assert.Equal(0x800E_6814u, expectedState.Pc);
        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 7, 27, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Fact]
    public void SonicInterruptStatusTimestampFastForwardMatchesInterpreter()
    {
        const uint pc = 0x800E_6814;
        const uint stack = 0x8030_4000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicInterruptStatusTimestamp(expectedBus.Memory);
        WriteSonicInterruptStatusTimestamp(actualBus.Memory);
        expectedBus.Memory.Write32(0x8000_30C4, 0x1234_5678);
        actualBus.Memory.Write32(0x8000_30C4, 0x1234_5678);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Lr = 0x8123_4568,
            Cr = 0x8765_4321,
            TimeBase = 0x0012_3456_789A_BCDE,
        };
        PowerPcState actualState = expectedState.Clone();
        foreach (PowerPcState state in new[] { expectedState, actualState })
        {
            state.Gpr[1] = stack;
            state.Gpr[27] = 2_592_000;
            state.Gpr[28] = 1;
            state.Gpr[31] = 0x8000_0000;
            state.Spr[22] = 0xFFFF_F000;
        }

        int expectedInstructions = RunUntilPc(expectedState, expectedBus, 0x800E_6844, maxInstructions: 1024);

        bool skipped = InvokeFastForwardSonicInterruptStatusTimestamp(actualState, actualBus, out int skippedInstructions);

        Assert.Equal(0x800E_6844u, expectedState.Pc);
        Assert.True(skipped);
        Assert.True(skippedInstructions > 0);
        Assert.True(skippedInstructions <= expectedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        foreach (int register in new[] { 0, 1, 3, 4, 5, 6, 7, 27, 28, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Theory]
    [InlineData(0u, 70u, 100u)]
    [InlineData(5u, 99u, 100u)]
    [InlineData(5u, 70u, 100u)]
    public void SonicInterruptStatusCompareFastForwardMatchesInterpreter(uint initialR0, uint tableValue, uint timestamp)
    {
        const uint pc = 0x800E_6844;
        const uint tableAddress = 0x8000_30C4;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicInterruptStatusCompare(expectedBus.Memory);
        WriteSonicInterruptStatusCompare(actualBus.Memory);
        expectedBus.Memory.Write32(tableAddress, tableValue);
        actualBus.Memory.Write32(tableAddress, tableValue);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
        };
        PowerPcState actualState = expectedState.Clone();
        foreach (PowerPcState state in new[] { expectedState, actualState })
        {
            state.Gpr[0] = initialR0;
            state.Gpr[3] = tableAddress;
            state.Gpr[4] = timestamp;
            state.Gpr[29] = 0xCAFE_BABE;
            state.Spr[22] = 0xFFFF_F000;
        }

        int expectedInstructions = RunUntilPc(expectedState, expectedBus, 0x800E_68B4, maxInstructions: 16);

        bool skipped = InvokeFastForwardSonicInterruptStatusCompare(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 4, 29 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        Assert.Equal(expectedBus.Memory.Read32(tableAddress), actualBus.Memory.Read32(tableAddress));
    }

    [Fact]
    public void SonicInterruptStatusQueryPrologueFastForwardMatchesInterpreter()
    {
        const uint pc = 0x800E_6954;
        const uint stack = 0x8030_5000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicInterruptStatusQueryPrologue(expectedBus.Memory);
        WriteSonicInterruptStatusQueryPrologue(actualBus.Memory);
        PowerPcState expectedState = new()
        {
            Pc = pc,
            Lr = 0x8123_4568,
            Cr = 0x1234_5678,
        };
        PowerPcState actualState = expectedState.Clone();
        foreach (PowerPcState state in new[] { expectedState, actualState })
        {
            state.Gpr[1] = stack;
            state.Gpr[3] = 1;
            state.Gpr[30] = 0xAAAA_0030;
            state.Gpr[31] = 0xAAAA_0031;
            state.Spr[22] = 0xFFFF_F000;
        }

        int expectedInstructions = RunUntilPc(expectedState, expectedBus, 0x800E_6760, maxInstructions: 16);

        bool skipped = InvokeFastForwardSonicInterruptStatusQueryPrologue(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 4, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (uint offset = 0; offset < 24; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 24 + offset), actualBus.Memory.Read32(stack - 24 + offset));
        }

        Assert.Equal(expectedBus.Memory.Read32(stack + 4), actualBus.Memory.Read32(stack + 4));
    }

    [Theory]
    [InlineData(0u, 0u, 0u)]
    [InlineData(0u, 7u, 0u)]
    [InlineData(1u, 0u, 5u)]
    public void SonicInterruptStatusQueryPostCallFastForwardMatchesInterpreter(uint statusResult, uint tableValue, uint flagValue)
    {
        const uint pc = 0x800E_6984;
        const uint stack = 0x8030_6000;
        const uint returnPc = 0x8123_4568;
        const uint slot = 1;
        uint tableAddress = 0x8000_30C0 + slot * sizeof(uint);
        uint statusTable = 0x802B_A880 + slot * 0x40;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicInterruptStatusQueryPostCall(expectedBus.Memory);
        WriteSonicInterruptStatusQueryPostCall(actualBus.Memory);
        foreach (GameCubeBus bus in new[] { expectedBus, actualBus })
        {
            bus.Memory.Write32(stack + 16, 0xAAAA_0030);
            bus.Memory.Write32(stack + 20, 0xAAAA_0031);
            bus.Memory.Write32(stack + 28, returnPc);
            bus.Memory.Write32(tableAddress, tableValue);
            bus.Memory.Write32(statusTable + 0x20, flagValue);
        }

        PowerPcState expectedState = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
        };
        PowerPcState actualState = expectedState.Clone();
        foreach (PowerPcState state in new[] { expectedState, actualState })
        {
            state.Gpr[1] = stack;
            state.Gpr[3] = statusResult;
            state.Gpr[30] = slot;
            state.Gpr[31] = statusTable;
            state.Spr[22] = 0xFFFF_F000;
        }

        int expectedInstructions = RunUntilPc(expectedState, expectedBus, returnPc, maxInstructions: 32);

        bool skipped = InvokeFastForwardSonicInterruptStatusQueryPostCall(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Fact]
    public void SonicResourceFixupFastForwardRefusesCaRecordWithPreviousResource()
    {
        const uint record = 0x800B_0000;
        const uint baseAddress = 0x800C_0000;
        GameCubeBus bus = new();
        WriteSonicResourceFixupLoop(bus.Memory);
        bus.Memory.Write16(record, 0x20);
        bus.Memory.Write8(record + 2, 0xCA);
        bus.Memory.Write8(record + 3, 1);
        bus.Memory.Write32(record + 4, 0);

        PowerPcState state = new()
        {
            Pc = 0x800E_81A8,
        };
        state.Gpr[27] = 0x800D_0000;
        state.Gpr[28] = baseAddress;
        state.Gpr[29] = 0x800E_0000;
        state.Gpr[30] = record;
        state.Gpr[31] = 0;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicResourceFixupRecord(state, bus, out int skippedInstructions);

        Assert.False(skipped);
        Assert.Equal(0, skippedInstructions);
        Assert.Equal(0x800E_81A8u, state.Pc);
        Assert.Equal(baseAddress, state.Gpr[28]);
        Assert.Equal(0x800E_0000u, state.Gpr[29]);
        Assert.Equal(record, state.Gpr[30]);
    }

    [Fact]
    public void SonicResourceFixupFastForwardPreloadsNextOpcodeAtLoopTail()
    {
        const uint record = 0x800B_0000;
        const uint baseAddress = 0x800C_0000;
        GameCubeBus bus = new();
        WriteSonicResourceFixupLoop(bus.Memory);
        bus.Memory.Write16(record, 0x20);
        bus.Memory.Write8(record + 2, 0x00);
        bus.Memory.Write8(record + 8 + 2, 0xCB);

        PowerPcState state = new()
        {
            Pc = 0x800E_81A8,
        };
        state.Gpr[28] = baseAddress;
        state.Gpr[30] = record;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicResourceFixupRecord(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.True(skippedInstructions > 0);
        Assert.Equal(0x800E_835Cu, state.Pc);
        Assert.Equal(0xCBu, state.Gpr[4]);
        Assert.Equal(record + 8, state.Gpr[30]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
    }

    [Theory]
    [InlineData(0x00, false)]
    [InlineData(0x00, true)]
    [InlineData(0x01, false)]
    [InlineData(0x01, true)]
    [InlineData(0x02, true)]
    [InlineData(0x03, true)]
    [InlineData(0x04, true)]
    [InlineData(0x05, true)]
    [InlineData(0x06, true)]
    [InlineData(0x0A, true)]
    [InlineData(0xC9, true)]
    public void SonicResourceFixupFastForwardAppliesSafeRecordOpcodes(int opcodeRaw, bool hasBaseTable)
    {
        const uint record = 0x800B_0000;
        const uint baseAddress = 0x800C_0000;
        const uint baseTableHolder = 0x800D_0000;
        const uint baseTable = 0x800D_1000;
        const uint destinationOffset = 0x20;
        const uint destination = baseAddress + destinationOffset;
        const byte tableIndex = 2;
        const uint tableValue = 0x8123_4567;
        const uint baseValue = tableValue & 0xFFFF_FFFEu;
        const uint addend = 0x0000_8123;
        byte opcode = (byte)opcodeRaw;

        GameCubeBus bus = new();
        WriteSonicResourceFixupLoop(bus.Memory);
        bus.Memory.Write16(record, (ushort)destinationOffset);
        bus.Memory.Write8(record + 2, opcode);
        bus.Memory.Write8(record + 3, tableIndex);
        bus.Memory.Write32(record + 4, addend);
        bus.Memory.Write8(record + 8 + 2, 0xCB);
        bus.Memory.Write32(baseTableHolder + 0x10, baseTable);
        bus.Memory.Write32(baseTable + tableIndex * 8u, tableValue);
        bus.Memory.Write32(destination, 0xFC00_0003);

        PowerPcState state = new()
        {
            Pc = 0x800E_81A8,
        };
        state.Gpr[26] = baseTableHolder;
        state.Gpr[28] = baseAddress;
        state.Gpr[30] = record;
        state.Gpr[31] = hasBaseTable ? 1u : 0u;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicResourceFixupRecord(state, bus, out int skippedInstructions);

        uint expectedBaseValue = hasBaseTable ? baseValue : 0u;
        uint expectedValue = unchecked(expectedBaseValue + addend);
        Assert.True(skipped);
        Assert.True(skippedInstructions > 0);
        Assert.Equal(0x800E_835Cu, state.Pc);
        Assert.Equal(0xCBu, state.Gpr[4]);
        Assert.Equal(expectedBaseValue, state.Gpr[5]);
        Assert.Equal(destination, state.Gpr[28]);
        Assert.Equal(record + 8, state.Gpr[30]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);

        switch (opcode)
        {
            case 0x01:
                Assert.Equal(expectedValue, bus.Memory.Read32(destination));
                break;
            case 0x02:
                Assert.Equal(0xFC00_0003u | (expectedValue & 0x03FF_FFFCu), bus.Memory.Read32(destination));
                break;
            case 0x03:
            case 0x04:
                Assert.Equal((ushort)expectedValue, bus.Memory.Read16(destination));
                break;
            case 0x05:
                Assert.Equal((ushort)(expectedValue >> 16), bus.Memory.Read16(destination));
                break;
            case 0x06:
                uint high = expectedValue >> 16;
                if ((expectedValue & 0x8000) != 0)
                {
                    high++;
                }

                Assert.Equal((ushort)high, bus.Memory.Read16(destination));
                break;
            case 0x0A:
                uint relative = unchecked(expectedValue - destination);
                Assert.Equal(0xFC00_0003u | (relative & 0x03FF_FFFCu), bus.Memory.Read32(destination));
                break;
            default:
                Assert.Equal(0xFC00_0003u, bus.Memory.Read32(destination));
                break;
        }
    }

    [Fact]
    public void SonicResourceFixupFastForwardAppliesCaRecordWithoutPreviousResource()
    {
        const uint record = 0x800B_0000;
        const uint baseAddress = 0x800C_0000;
        const uint resourceTableHolder = 0x800D_0000;
        const uint resourceTable = 0x800D_2000;
        const byte tableIndex = 3;
        uint resourceEntry = resourceTable + tableIndex * 8u;
        GameCubeBus bus = new();
        WriteSonicResourceFixupLoop(bus.Memory);
        bus.Memory.Write16(record, 0x20);
        bus.Memory.Write8(record + 2, 0xCA);
        bus.Memory.Write8(record + 3, tableIndex);
        bus.Memory.Write8(record + 8 + 2, 0xCB);
        bus.Memory.Write32(resourceTableHolder + 0x10, resourceTable);
        bus.Memory.Write32(resourceEntry, 0x800E_4001);

        PowerPcState state = new()
        {
            Pc = 0x800E_81A8,
        };
        state.Gpr[27] = resourceTableHolder;
        state.Gpr[28] = baseAddress;
        state.Gpr[30] = record;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicResourceFixupRecord(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.True(skippedInstructions > 0);
        Assert.Equal(0x800E_835Cu, state.Pc);
        Assert.Equal(resourceEntry, state.Gpr[23]);
        Assert.Equal(0x800E_4000u, state.Gpr[28]);
        Assert.Equal(resourceEntry, state.Gpr[29]);
        Assert.Equal(record + 8, state.Gpr[30]);
        Assert.Equal(0xCBu, state.Gpr[4]);
    }

    [Fact]
    public void SonicOverlayInactiveSlotScanFastForwardStopsBeforeActiveSlot()
    {
        const uint pc = 0x80BC_8110;
        const uint slotTable = 0x80BE_D490;
        GameCubeBus bus = new();
        WriteSonicOverlayInactiveSlotScan(bus.Memory);
        bus.Memory.Write32(slotTable + 3 * 0x10, 0);
        bus.Memory.Write32(slotTable + 4 * 0x10, 2);
        bus.Memory.Write32(slotTable + 5 * 0x10, 1);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[31] = 3;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicOverlayInactiveSlotScan(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(20, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(5u, state.Gpr[31]);
        Assert.Equal(2u, state.Gpr[0]);
        Assert.Equal(0x40u, state.Gpr[4]);
        Assert.Equal(slotTable + 0x40, state.Gpr[3]);
        Assert.Equal(0x8000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void SonicOverlayInactiveSlotScanFastForwardCompletesInnerLoop()
    {
        const uint slotTable = 0x80BE_D490;
        GameCubeBus bus = new();
        WriteSonicOverlayInactiveSlotScan(bus.Memory);
        bus.Memory.Write32(slotTable + 62 * 0x10, 0);
        bus.Memory.Write32(slotTable + 63 * 0x10, 0);
        PowerPcState state = new()
        {
            Pc = 0x80BC_8110,
        };
        state.Gpr[31] = 62;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicOverlayInactiveSlotScan(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(20, skippedInstructions);
        Assert.Equal(0x80BC_82B8u, state.Pc);
        Assert.Equal(64u, state.Gpr[31]);
        Assert.Equal(0x3F0u, state.Gpr[4]);
        Assert.Equal(slotTable + 0x3F0, state.Gpr[3]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void SonicGxAttributeFlushFastForwardEmitsFifoPacketsAndClearsFlags()
    {
        const uint pc = 0x8010_10A0;
        const uint smallDataBase = 0x803B_52C0;
        const uint stateBlock = 0x800B_0000;
        GameCubeBus bus = new();
        List<MmioAccess> writes = [];
        bus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                writes.Add(access);
            }
        };

        WriteSonicGxAttributeFlush(bus.Memory, pc);
        bus.Memory.Write32(unchecked(smallDataBase - 31872u), stateBlock);
        bus.Memory.Write8(stateBlock + 0x4EE, 0b0000_0101);
        bus.Memory.Write32(stateBlock + 0x1C, 0x1111_0000);
        bus.Memory.Write32(stateBlock + 0x3C, 0x2222_0000);
        bus.Memory.Write32(stateBlock + 0x5C, 0x3333_0000);
        bus.Memory.Write32(stateBlock + 0x1C + 8, 0x1111_0002);
        bus.Memory.Write32(stateBlock + 0x3C + 8, 0x2222_0002);
        bus.Memory.Write32(stateBlock + 0x5C + 8, 0x3333_0002);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8010_2000,
        };
        state.Gpr[13] = smallDataBase;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicGxAttributeFlush(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.True(skippedInstructions > 0);
        Assert.Equal(0, bus.Memory.Read8(stateBlock + 0x4EE));
        Assert.Equal(state.Lr, state.Pc);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        Assert.Equal(
            [
                (1, 0x08u), (1, 0x70u), (4, 0x1111_0000u), (1, 0x08u), (1, 0x80u), (4, 0x2222_0000u), (1, 0x08u), (1, 0x90u), (4, 0x3333_0000u),
                (1, 0x08u), (1, 0x72u), (4, 0x1111_0002u), (1, 0x08u), (1, 0x82u), (4, 0x2222_0002u), (1, 0x08u), (1, 0x92u), (4, 0x3333_0002u),
            ],
            writes.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxAttributeFlushFastForwardCanResumeFromLoopBody()
    {
        const uint pc = 0x8010_10A0;
        const uint stateBlock = 0x800B_0000;
        GameCubeBus bus = new();
        List<MmioAccess> writes = [];
        bus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                writes.Add(access);
            }
        };

        WriteSonicGxAttributeFlush(bus.Memory, pc);
        bus.Memory.Write8(stateBlock + 0x4EE, 0b0000_0101);
        bus.Memory.Write32(stateBlock + 0x1C + 8, 0x1111_0002);
        bus.Memory.Write32(stateBlock + 0x3C + 8, 0x2222_0002);
        bus.Memory.Write32(stateBlock + 0x5C + 8, 0x3333_0002);
        PowerPcState state = new()
        {
            Pc = pc + 0x14,
            Lr = 0x8010_2000,
        };
        state.Gpr[10] = stateBlock;
        state.Gpr[11] = 8;
        state.Gpr[12] = 2;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicGxAttributeFlush(state, bus, out _);

        Assert.True(skipped);
        Assert.Equal(0, bus.Memory.Read8(stateBlock + 0x4EE));
        Assert.Equal(state.Lr, state.Pc);
        Assert.Equal([(1, 0x08u), (1, 0x72u), (4, 0x1111_0002u), (1, 0x08u), (1, 0x82u), (4, 0x2222_0002u), (1, 0x08u), (1, 0x92u), (4, 0x3333_0002u)], writes.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxVertexEmitFastForwardEmitsRemainingVerticesToFifo()
    {
        const uint pc = 0x8012_00A8;
        const uint stream = 0x8132_A728;
        const uint vertexBase = 0x80B2_8500;
        GameCubeBus bus = new();
        List<MmioAccess> writes = [];
        bus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                writes.Add(access);
            }
        };

        WriteSonicGxVertexEmitLoop(bus.Memory, pc);
        bus.Memory.Write16(stream + 0x00, 0);
        bus.Memory.Write16(stream + 0x02, 0x1234);
        bus.Memory.Write16(stream + 0x04, 0x5678);
        bus.Memory.Write16(stream + 0x06, 1);
        bus.Memory.Write16(stream + 0x08, 0x9ABC);
        bus.Memory.Write16(stream + 0x0A, 0xDEF0);
        bus.Memory.Write32(vertexBase + 0x00, 0x3F80_0000);
        bus.Memory.Write32(vertexBase + 0x04, 0x4000_0000);
        bus.Memory.Write32(vertexBase + 0x08, 0x4040_0000);
        bus.Memory.Write32(vertexBase + 0x18, 0x1122_3344);
        bus.Memory.Write32(vertexBase + 0x20, 0x4080_0000);
        bus.Memory.Write32(vertexBase + 0x24, 0x40A0_0000);
        bus.Memory.Write32(vertexBase + 0x28, 0x40C0_0000);
        bus.Memory.Write32(vertexBase + 0x38, 0x5566_7788);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[24] = stream;
        state.Gpr[25] = vertexBase;
        state.Gpr[30] = 2;
        state.Gpr[31] = 0;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicGxVertexEmitLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(66, skippedInstructions);
        Assert.Equal(pc + 0x54, state.Pc);
        Assert.Equal(stream + 0x0C, state.Gpr[24]);
        Assert.Equal(vertexBase + 0x20, state.Gpr[27]);
        Assert.Equal(0u, state.Gpr[30]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        Assert.Equal(
            [
                (4, 0x3F80_0000u), (4, 0x4000_0000u), (4, 0x4040_0000u), (4, 0x1122_3344u), (2, 0x1234u), (2, 0x5678u),
                (4, 0x4080_0000u), (4, 0x40A0_0000u), (4, 0x40C0_0000u), (4, 0x5566_7788u), (2, 0x9ABCu), (2, 0xDEF0u),
            ],
            writes.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxTexObjLoadNoCallbackFastForwardMatchesInterpreterPath()
    {
        const uint pc = 0x8010_37A4;
        const uint stack = 0x817F_F000;
        const uint textureObject = 0x8035_7FB0;
        const uint samplerObject = 0x802D_4C38;
        const uint stateBlock = 0x803C_0000;
        const uint textureMap = 2;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        PowerPcState expectedState = CreateSonicGxTexObjLoadNoCallbackState(expectedBus, pc, stack, textureObject, samplerObject, stateBlock, textureMap);
        PowerPcState actualState = CreateSonicGxTexObjLoadNoCallbackState(actualBus, pc, stack, textureObject, samplerObject, stateBlock, textureMap);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 92);
        bool skipped = InvokeFastForwardSonicGxTexObjLoadNoCallback(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(92, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        for (int register = 0; register < 32; register++)
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (uint offset = 0; offset < 0x20; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(textureObject + offset), actualBus.Memory.Read32(textureObject + offset));
        }

        for (uint offset = 0; offset < 8; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(samplerObject + offset), actualBus.Memory.Read32(samplerObject + offset));
        }

        for (uint offset = 0; offset <= 0x30; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 40 + offset), actualBus.Memory.Read32(stack - 40 + offset));
        }

        foreach (uint offset in new[] { 0x45Cu + textureMap * 4, 0x47Cu + textureMap * 4, 0x4F0u })
        {
            Assert.Equal(expectedBus.Memory.Read32(stateBlock + offset), actualBus.Memory.Read32(stateBlock + offset));
        }

        Assert.Equal(expectedBus.Memory.Read16(stateBlock + 2), actualBus.Memory.Read16(stateBlock + 2));
        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxPackedStateSetterFastForwardMatchesInterpreterPaths()
    {
        const uint pc = 0x8010_4FF8;
        const uint stateBlockPointerAddress = 0x803A_D640;
        const uint stateBlock = 0x803C_0000;
        foreach ((uint mode, int expectedInstructions) in new[] { (0u, 65), (1u, 62), (3u, 64) })
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            List<MmioAccess> expectedWrites = [];
            List<MmioAccess> actualWrites = [];
            expectedBus.MmioAccessObserver = access =>
            {
                if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
                {
                    expectedWrites.Add(access);
                }
            };
            actualBus.MmioAccessObserver = access =>
            {
                if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
                {
                    actualWrites.Add(access);
                }
            };

            WriteSonicGxPackedStateSetter(expectedBus.Memory, pc);
            WriteSonicGxPackedStateSetter(actualBus.Memory, pc);
            expectedBus.Memory.Write32(stateBlockPointerAddress, stateBlock);
            actualBus.Memory.Write32(stateBlockPointerAddress, stateBlock);
            expectedBus.Memory.Write32(stateBlock + 0x1D0, 0xDEAD_BEEF);
            actualBus.Memory.Write32(stateBlock + 0x1D0, 0xDEAD_BEEF);
            expectedBus.Memory.Write16(stateBlock + 2, 0x1234);
            actualBus.Memory.Write16(stateBlock + 2, 0x1234);

            PowerPcState expectedState = CreateSonicGxPackedStateSetterState(pc, mode);
            PowerPcState actualState = CreateSonicGxPackedStateSetterState(pc, mode);

            new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
            bool skipped = InvokeFastForwardSonicGxPackedStateSetter(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            for (int register = 0; register < 32; register++)
            {
                Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
            }

            Assert.Equal(expectedBus.Memory.Read32(stateBlock + 0x1D0), actualBus.Memory.Read32(stateBlock + 0x1D0));
            Assert.Equal(expectedBus.Memory.Read16(stateBlock + 2), actualBus.Memory.Read16(stateBlock + 2));
            Assert.Equal(
                expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
                actualWrites.Select(access => (access.Width, access.Value)).ToArray());
        }
    }

    [Fact]
    public void SonicGxCpStateSetterFastForwardMatchesInterpreter()
    {
        const uint pc = 0x8010_14A8;
        const uint stateBlock = 0x8035_7000;
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        WriteSonicGxCpStateSetter(expectedBus.Memory, pc);
        WriteSonicGxCpStateSetter(actualBus.Memory, pc);
        PowerPcState expectedState = CreateSonicGxCpStateSetterState(expectedBus, pc, stateBlock);
        PowerPcState actualState = CreateSonicGxCpStateSetterState(actualBus, pc, stateBlock);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 18);
        bool skipped = InvokeFastForwardSonicGxCpStateSetter(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(18, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 4, 5, 6, 7, 13 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        Assert.Equal(expectedBus.Memory.Read32(stateBlock + 0x204), actualBus.Memory.Read32(stateBlock + 0x204));
        Assert.Equal(expectedBus.Memory.Read32(stateBlock + 0x4F0), actualBus.Memory.Read32(stateBlock + 0x4F0));
        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxTevColorEnvSetterFastForwardMatchesInterpreter()
    {
        const uint pc = 0x8010_464C;
        const uint stateBlock = 0x8035_7000;
        const uint tevStage = 3;
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        WriteSonicGxTevColorEnvSetter(expectedBus.Memory, pc);
        WriteSonicGxTevColorEnvSetter(actualBus.Memory, pc);
        PowerPcState expectedState = CreateSonicGxTevEnvSetterState(expectedBus, pc, stateBlock, tevStage, stateBlock + 0x130 + tevStage * 4);
        PowerPcState actualState = CreateSonicGxTevEnvSetterState(actualBus, pc, stateBlock, tevStage, stateBlock + 0x130 + tevStage * 4);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 32);
        bool skipped = InvokeFastForwardSonicGxTevColorEnvSetter(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(32, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Xer, actualState.Xer);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        for (int register = 0; register < 32; register++)
        {
            Assert.True(expectedState.Gpr[register] == actualState.Gpr[register], $"r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
        }

        Assert.Equal(expectedBus.Memory.Read32(stateBlock + 0x130 + tevStage * 4), actualBus.Memory.Read32(stateBlock + 0x130 + tevStage * 4));
        Assert.Equal(expectedBus.Memory.Read16(stateBlock + 2), actualBus.Memory.Read16(stateBlock + 2));
        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxTevColorEnvSetterTailFastForwardMatchesInterpreter()
    {
        const uint pc = 0x8010_4698;
        const uint stateBlock = 0x8035_7000;
        const uint cachedAddress = stateBlock + 0x130;
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        WriteSonicGxTevColorEnvSetterTail(expectedBus.Memory, pc);
        WriteSonicGxTevColorEnvSetterTail(actualBus.Memory, pc);
        PowerPcState expectedState = CreateSonicGxTevColorEnvSetterTailState(expectedBus, pc, stateBlock, cachedAddress);
        PowerPcState actualState = CreateSonicGxTevColorEnvSetterTailState(actualBus, pc, stateBlock, cachedAddress);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 13);
        bool skipped = InvokeFastForwardSonicGxTevColorEnvSetter(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(13, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Xer, actualState.Xer);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        for (int register = 0; register < 32; register++)
        {
            Assert.True(expectedState.Gpr[register] == actualState.Gpr[register], $"r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
        }

        Assert.Equal(expectedBus.Memory.Read32(cachedAddress), actualBus.Memory.Read32(cachedAddress));
        Assert.Equal(expectedBus.Memory.Read16(stateBlock + 2), actualBus.Memory.Read16(stateBlock + 2));
        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxTevAlphaEnvSetterFastForwardMatchesInterpreter()
    {
        const uint pc = 0x8010_46CC;
        const uint stateBlock = 0x8035_7000;
        const uint tevStage = 2;
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        WriteSonicGxTevAlphaEnvSetter(expectedBus.Memory, pc);
        WriteSonicGxTevAlphaEnvSetter(actualBus.Memory, pc);
        PowerPcState expectedState = CreateSonicGxTevEnvSetterState(expectedBus, pc, stateBlock, tevStage, stateBlock + 0x170 + tevStage * 4);
        PowerPcState actualState = CreateSonicGxTevEnvSetterState(actualBus, pc, stateBlock, tevStage, stateBlock + 0x170 + tevStage * 4);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 33);
        bool skipped = InvokeFastForwardSonicGxTevAlphaEnvSetter(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(33, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Xer, actualState.Xer);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        for (int register = 0; register < 32; register++)
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        Assert.Equal(expectedBus.Memory.Read32(stateBlock + 0x170 + tevStage * 4), actualBus.Memory.Read32(stateBlock + 0x170 + tevStage * 4));
        Assert.Equal(expectedBus.Memory.Read16(stateBlock + 2), actualBus.Memory.Read16(stateBlock + 2));
        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Theory]
    [InlineData(0x8010_4750u, 0x130u, 0u, true)]
    [InlineData(0x8010_4750u, 0x130u, 2u, true)]
    [InlineData(0x8010_4810u, 0x170u, 0u, false)]
    [InlineData(0x8010_4810u, 0x170u, 2u, false)]
    public void SonicGxTevOpSetterFastForwardMatchesInterpreter(uint pc, uint cacheOffset, uint mode, bool color)
    {
        const uint stateBlock = 0x8035_7000;
        const uint tevStage = 2;
        uint cachedAddress = stateBlock + cacheOffset + tevStage * 4;
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        WriteSonicGxTevOpSetter(expectedBus.Memory, pc, cacheOffset);
        WriteSonicGxTevOpSetter(actualBus.Memory, pc, cacheOffset);
        PowerPcState expectedState = CreateSonicGxTevOpSetterState(expectedBus, pc, stateBlock, tevStage, cachedAddress, mode);
        PowerPcState actualState = CreateSonicGxTevOpSetterState(actualBus, pc, stateBlock, tevStage, cachedAddress, mode);

        int expectedInstructions = 0;
        while (expectedState.Pc != expectedState.Lr && expectedInstructions < 64)
        {
            new PowerPcInterpreter().Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        int skippedInstructions;
        bool skipped = color
            ? InvokeFastForwardSonicGxTevColorOpSetter(actualState, actualBus, out skippedInstructions)
            : InvokeFastForwardSonicGxTevAlphaOpSetter(actualState, actualBus, out skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Xer, actualState.Xer);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        for (int register = 0; register < 32; register++)
        {
            Assert.True(expectedState.Gpr[register] == actualState.Gpr[register], $"r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
        }

        Assert.Equal(expectedBus.Memory.Read32(cachedAddress), actualBus.Memory.Read32(cachedAddress));
        Assert.Equal(expectedBus.Memory.Read16(stateBlock + 2), actualBus.Memory.Read16(stateBlock + 2));
        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxTevDefaultWrapperFastForwardMatchesInterpreter()
    {
        const uint pc = 0x8010_44A8;
        const uint stack = 0x803C_1250;
        const uint stateBlock = 0x8035_7000;
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        WriteSonicGxTevDefaultWrapper(expectedBus.Memory);
        WriteSonicGxTevDefaultWrapper(actualBus.Memory);
        PowerPcState expectedState = CreateSonicGxTevDefaultWrapperState(expectedBus, pc, stack, stateBlock);
        PowerPcState actualState = CreateSonicGxTevDefaultWrapperState(actualBus, pc, stack, stateBlock);

        uint returnAddress = expectedState.Lr;
        int expectedInstructions = 0;
        while (expectedState.Pc != returnAddress && expectedInstructions < 256)
        {
            new PowerPcInterpreter().Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        bool skipped = InvokeFastForwardSonicGxTevDefaultWrapper(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Xer, actualState.Xer);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        for (int register = 0; register < 32; register++)
        {
            Assert.True(expectedState.Gpr[register] == actualState.Gpr[register], $"r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
        }

        for (uint offset = 0; offset < 0x30; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 24 + offset), actualBus.Memory.Read32(stack - 24 + offset));
        }

        foreach (uint offset in new[] { 0x130u, 0x170u })
        {
            Assert.Equal(expectedBus.Memory.Read32(stateBlock + offset), actualBus.Memory.Read32(stateBlock + offset));
        }

        Assert.Equal(expectedBus.Memory.Read16(stateBlock + 2), actualBus.Memory.Read16(stateBlock + 2));
        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxDrawBeginFastForwardMatchesCleanInterpreterPath()
    {
        const uint pc = 0x8010_1948;
        const uint stack = 0x817F_F000;
        const uint stateBlock = 0x803C_0000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        PowerPcState expectedState = CreateSonicGxDrawBeginState(expectedBus, pc, stack, stateBlock);
        PowerPcState actualState = CreateSonicGxDrawBeginState(actualBus, pc, stack, stateBlock);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 28);
        bool skipped = InvokeFastForwardSonicGxDrawBegin(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(28, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 6, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (uint offset = 0; offset <= 0x30; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 40 + offset), actualBus.Memory.Read32(stack - 40 + offset));
        }

        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxBeginDirectFastForwardMatchesInterpreterPaths()
    {
        const uint pc = 0x8010_06D8;
        const uint stateBlock = 0x803C_0000;
        foreach ((uint word14, uint word18, byte vertexMatrix, byte normalMatrix) in new[]
        {
            (0x0000_0000u, 0x0000_0000u, (byte)0, (byte)0),
            (0x0001_4000u, 0xFFFF_FFFFu, (byte)1, (byte)0),
            (0x0000_8000u, 0x0003_0000u, (byte)0, (byte)1),
        })
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            List<MmioAccess> expectedWrites = [];
            List<MmioAccess> actualWrites = [];
            expectedBus.MmioAccessObserver = access =>
            {
                if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
                {
                    expectedWrites.Add(access);
                }
            };
            actualBus.MmioAccessObserver = access =>
            {
                if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
                {
                    actualWrites.Add(access);
                }
            };
            PowerPcState expectedState = CreateSonicGxBeginDirectState(expectedBus, pc, stateBlock, word14, word18, vertexMatrix, normalMatrix);
            PowerPcState actualState = CreateSonicGxBeginDirectState(actualBus, pc, stateBlock, word14, word18, vertexMatrix, normalMatrix);

            int expectedInstructions = RunUntilPc(expectedState, expectedBus, expectedState.Lr, maxInstructions: 96);
            bool skipped = InvokeFastForwardSonicGxBeginDirect(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 4, 5, 6, 7, 8, 13 })
            {
                Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
            }

            Assert.Equal(expectedBus.Memory.Read16(stateBlock + 2), actualBus.Memory.Read16(stateBlock + 2));
            Assert.Equal(
                expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
                actualWrites.Select(access => (access.Width, access.Value)).ToArray());
        }
    }

    [Fact]
    public void SonicGxIndexedStripDrawBeginFastForwardMatchesInterpreterPath()
    {
        const uint pc = 0x8012_0078;
        const uint gxBeginPc = 0x8010_1948;
        const uint stack = 0x817F_F000;
        const uint stream = 0x8132_A728;
        const uint stateBlock = 0x803C_0000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        PowerPcState expectedState = CreateSonicGxIndexedStripDrawBeginState(expectedBus, pc, gxBeginPc, stack, stream, stateBlock, stripCount: 3);
        PowerPcState actualState = CreateSonicGxIndexedStripDrawBeginState(actualBus, pc, gxBeginPc, stack, stream, stateBlock, stripCount: 3);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 39);
        bool skipped = InvokeFastForwardSonicGxIndexedStripDrawBegin(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(39, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 4, 5, 6, 24, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (uint offset = 0; offset <= 0x30; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 40 + offset), actualBus.Memory.Read32(stack - 40 + offset));
        }

        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxVertexDescriptorSetterFastForwardMatchesInterpreterPaths()
    {
        const uint pc = 0x8010_0830;
        const uint stateBlock = 0x803C_0000;
        foreach ((uint descriptor, byte vertexMatrixFlag, byte normalMatrixFlag) in new[]
        {
            (9u, (byte)0, (byte)0),
            (11u, (byte)1, (byte)0),
            (13u, (byte)0, (byte)1),
        })
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicGxVertexDescriptorSetter(expectedBus.Memory, pc);
            WriteSonicGxVertexDescriptorSetter(actualBus.Memory, pc);
            PowerPcState expectedState = CreateSonicGxVertexDescriptorSetterState(expectedBus, pc, stateBlock, descriptor, vertexMatrixFlag, normalMatrixFlag);
            PowerPcState actualState = CreateSonicGxVertexDescriptorSetterState(actualBus, pc, stateBlock, descriptor, vertexMatrixFlag, normalMatrixFlag);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != expectedState.Lr && expectedInstructions < 128)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(expectedState.Lr, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicGxVertexDescriptorSetter(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Ctr, actualState.Ctr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 4, 5, 6, 7, 8, 9, 10 })
            {
                Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
            }

            foreach (uint offset in new[] { 0x14u, 0x18u, 0x4F0u })
            {
                Assert.True(
                    expectedBus.Memory.Read32(stateBlock + offset) == actualBus.Memory.Read32(stateBlock + offset),
                    $"offset 0x{offset:X}: expected 0x{expectedBus.Memory.Read32(stateBlock + offset):X8}, actual 0x{actualBus.Memory.Read32(stateBlock + offset):X8}");
            }
        }
    }

    [Fact]
    public void SonicGxVertexAttributeFlushFastForwardMatchesInterpreterPath()
    {
        const uint pc = 0x8010_3D28;
        const uint stateBlock = 0x803C_0000;
        const uint stack = 0x817F_F000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        WriteSonicGxVertexAttributeFlush(expectedBus.Memory, pc);
        WriteSonicGxVertexAttributeFlush(actualBus.Memory, pc);
        PowerPcState expectedState = CreateSonicGxVertexAttributeFlushState(expectedBus, pc, stack, stateBlock);
        PowerPcState actualState = CreateSonicGxVertexAttributeFlushState(actualBus, pc, stack, stateBlock);

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        uint returnAddress = expectedState.Lr;
        while (expectedState.Pc != returnAddress && expectedInstructions < 256)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        Assert.Equal(expectedState.Lr, expectedState.Pc);
        bool skipped = InvokeFastForwardSonicGxVertexAttributeFlush(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped, $"vertex attribute flush fast path rejected after {skippedInstructions} instruction(s)");
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 4, 5, 6, 7, 8, 9, 10, 27, 28, 29, 30, 31 })
        {
            Assert.True(
                expectedState.Gpr[register] == actualState.Gpr[register],
                $"r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
        }

        foreach (uint offset in new[] { 0x02u, 0xB8u, 0xD8u, 0x45Cu, 0x47Cu, 0x49Cu, 0x4DCu })
        {
            if (offset == 0x02)
            {
                Assert.Equal(expectedBus.Memory.Read16(stateBlock + offset), actualBus.Memory.Read16(stateBlock + offset));
            }
            else
            {
                Assert.True(
                    expectedBus.Memory.Read32(stateBlock + offset) == actualBus.Memory.Read32(stateBlock + offset),
                    $"offset 0x{offset:X}: expected 0x{expectedBus.Memory.Read32(stateBlock + offset):X8}, actual 0x{actualBus.Memory.Read32(stateBlock + offset):X8}");
            }
        }

        for (uint offset = 0; offset <= 0x2C; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 40 + offset), actualBus.Memory.Read32(stack - 40 + offset));
        }

        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxIndexedStripBatchFastForwardMatchesInterpreterPath()
    {
        const uint pc = 0x8012_0078;
        const uint gxBeginPc = 0x8010_1948;
        const uint stack = 0x817F_F000;
        const uint stream = 0x8132_A728;
        const uint vertexBase = 0x80B2_8500;
        const uint stateBlock = 0x803C_0000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        PowerPcState expectedState = CreateSonicGxIndexedStripBatchState(expectedBus, pc, gxBeginPc, stack, stream, vertexBase, stateBlock);
        PowerPcState actualState = CreateSonicGxIndexedStripBatchState(actualBus, pc, gxBeginPc, stack, stream, vertexBase, stateBlock);

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != pc + 0x94 && expectedInstructions < 512)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        Assert.Equal(pc + 0x94, expectedState.Pc);
        bool skipped = InvokeFastForwardSonicGxIndexedStripBatch(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 4, 5, 6, 24, 26, 27, 28, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        foreach (int register in new[] { 1, 2, 3 })
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[register]), BitConverter.DoubleToInt64Bits(actualState.Fpr[register]));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.FprPair1[register]), BitConverter.DoubleToInt64Bits(actualState.FprPair1[register]));
        }

        for (uint offset = 0; offset <= 0x30; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 40 + offset), actualBus.Memory.Read32(stack - 40 + offset));
        }

        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxIndexedStripTailFastForwardMatchesInterpreterBranch()
    {
        const uint pc = 0x8012_00FC;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicGxIndexedStripTail(expectedBus.Memory, pc);
        WriteSonicGxIndexedStripTail(actualBus.Memory, pc);
        PowerPcState expectedState = CreateSonicGxIndexedStripTailState(pc, remainingStrips: 2);
        PowerPcState actualState = CreateSonicGxIndexedStripTailState(pc, remainingStrips: 2);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 5);
        bool skipped = InvokeFastForwardSonicGxIndexedStripTail(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(5, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Gpr[26], actualState.Gpr[26]);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
    }

    [Fact]
    public void SonicGxIndexedStripEpilogueFastForwardMatchesInterpreterPath()
    {
        const uint pc = 0x8012_010C;
        const uint stack = 0x817F_F000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicGxIndexedStripEpilogue(expectedBus.Memory, pc);
        WriteSonicGxIndexedStripEpilogue(actualBus.Memory, pc);
        PowerPcState expectedState = CreateSonicGxIndexedStripEpilogueState(expectedBus, pc, stack);
        PowerPcState actualState = CreateSonicGxIndexedStripEpilogueState(actualBus, pc, stack);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 15);
        bool skipped = InvokeFastForwardSonicGxIndexedStripEpilogue(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(15, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 11, 24, 25, 26, 27, 28, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Fact]
    public void SonicGxCommandListTerminalFastForwardMatchesInterpreterEpilogue()
    {
        const uint pc = 0x8011_D184;
        const uint stack = 0x817F_F000;
        const uint stream = 0x8132_A728;
        const uint returnAddress = 0x8012_3454;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicGxCommandListTerminal(expectedBus.Memory, pc);
        WriteSonicGxCommandListTerminal(actualBus.Memory, pc);
        PowerPcState expectedState = CreateSonicGxCommandListTerminalState(expectedBus, pc, stack, stream, returnAddress);
        PowerPcState actualState = CreateSonicGxCommandListTerminalState(actualBus, pc, stack, stream, returnAddress);

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != returnAddress && expectedInstructions < 64)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        Assert.Equal(returnAddress, expectedState.Pc);
        bool skipped = InvokeFastForwardSonicGxCommandListTerminal(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 11, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Theory]
    [InlineData(0x0004, 8)]
    [InlineData(0x000F, 10)]
    [InlineData(0x0011, 12)]
    [InlineData(0x0041, 12)]
    public void SonicGxCommandListFetchFastForwardMatchesInterpreterBranch(ushort command, int expectedInstructions)
    {
        const uint pc = 0x8011_D184;
        const uint stream = 0x8132_A728;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicGxCommandListTerminal(expectedBus.Memory, pc);
        WriteSonicGxCommandDispatch(expectedBus.Memory);
        WriteSonicGxCommandListTerminal(actualBus.Memory, pc);
        WriteSonicGxCommandDispatch(actualBus.Memory);
        PowerPcState expectedState = CreateSonicGxCommandListFetchState(expectedBus, pc, stream, unchecked((short)command));
        PowerPcState actualState = CreateSonicGxCommandListFetchState(actualBus, pc, stream, unchecked((short)command));

        new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
        bool skipped = InvokeFastForwardSonicGxCommandListFetch(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 20, 25, 28 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Fact]
    public void SonicGxCommandDispatchFastForwardMatchesInterpreterBranches()
    {
        const uint headerPc = 0x8011_CD40;
        const uint highRangePc = 0x8011_CDE8;
        const uint extendedRangePc = 0x8011_CE20;
        const uint metadataHeaderPc = 0x8011_CE60;
        const uint activeBatchRecordPc = 0x8011_CE80;
        foreach (uint command in new[] { 0x04u, 0x11u })
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicGxCommandDispatch(expectedBus.Memory);
            WriteSonicGxCommandDispatch(actualBus.Memory);
            PowerPcState expectedState = CreateSonicGxCommandDispatchState(headerPc, command);
            PowerPcState actualState = CreateSonicGxCommandDispatchState(headerPc, command);

            new PowerPcInterpreter().Run(expectedState, expectedBus, 4);
            bool skipped = InvokeFastForwardSonicGxCommandDispatch(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(4, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            foreach (int register in new[] { 0, 25, 28 })
            {
                Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
            }
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        }

        foreach (uint command in new[] { 0x0Fu, 0x11u })
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicGxCommandDispatch(expectedBus.Memory);
            WriteSonicGxCommandDispatch(actualBus.Memory);
            PowerPcState expectedState = CreateSonicGxCommandDispatchState(highRangePc, command);
            PowerPcState actualState = CreateSonicGxCommandDispatchState(highRangePc, command);

            new PowerPcInterpreter().Run(expectedState, expectedBus, 2);
            bool skipped = InvokeFastForwardSonicGxCommandDispatch(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(2, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.Gpr[25], actualState.Gpr[25]);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        }

        foreach (uint command in new[] { 0x3Fu, 0x40u })
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicGxCommandDispatch(expectedBus.Memory);
            WriteSonicGxCommandDispatch(actualBus.Memory);
            PowerPcState expectedState = CreateSonicGxCommandDispatchState(extendedRangePc, command);
            PowerPcState actualState = CreateSonicGxCommandDispatchState(extendedRangePc, command);

            new PowerPcInterpreter().Run(expectedState, expectedBus, 2);
            bool skipped = InvokeFastForwardSonicGxCommandDispatch(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(2, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.Gpr[25], actualState.Gpr[25]);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        }

        foreach ((uint activeBatchDepth, ushort count, ushort payload) in new[]
        {
            (0u, (ushort)0x0241, (ushort)0x0009),
            (1u, (ushort)0x0003, (ushort)0xFFF8),
        })
        {
            const uint stream = 0x8131_930A;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicGxCommandDispatch(expectedBus.Memory);
            WriteSonicGxCommandDispatch(actualBus.Memory);
            expectedBus.Memory.Write16(stream, count);
            expectedBus.Memory.Write16(stream + 2, payload);
            actualBus.Memory.Write16(stream, count);
            actualBus.Memory.Write16(stream + 2, payload);
            PowerPcState expectedState = CreateSonicGxCommandDispatchState(metadataHeaderPc, 0x41);
            PowerPcState actualState = CreateSonicGxCommandDispatchState(metadataHeaderPc, 0x41);
            expectedState.Gpr[20] = stream;
            expectedState.Gpr[24] = activeBatchDepth;
            actualState.Gpr[20] = stream;
            actualState.Gpr[24] = activeBatchDepth;

            new PowerPcInterpreter().Run(expectedState, expectedBus, 8);
            bool skipped = InvokeFastForwardSonicGxCommandDispatch(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(8, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            foreach (int register in new[] { 0, 3, 20, 24, 26, 27 })
            {
                Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
            }
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        }

        foreach ((uint currentSlot, uint activeBatchDepth, uint command, uint count, int expectedInstructions) in new[]
        {
            (0u, 2u, 66u, 3u, 14),
            (1u, 1u, 65u, 7u, 10),
        })
        {
            const uint stream = 0x8131_930E;
            const uint streamStack = 0x803C_1288;
            const uint flagStack = 0x803C_1268;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicGxCommandDispatch(expectedBus.Memory);
            WriteSonicGxCommandDispatch(actualBus.Memory);
            PowerPcState expectedState = CreateSonicGxCommandDispatchState(activeBatchRecordPc, command);
            PowerPcState actualState = CreateSonicGxCommandDispatchState(activeBatchRecordPc, command);
            foreach (PowerPcState state in new[] { expectedState, actualState })
            {
                state.Gpr[20] = stream;
                state.Gpr[23] = currentSlot;
                state.Gpr[24] = activeBatchDepth;
                state.Gpr[27] = count;
                state.Gpr[30] = streamStack;
                state.Gpr[31] = flagStack;
            }

            new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
            bool skipped = InvokeFastForwardSonicGxCommandDispatch(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            foreach (int register in new[] { 0, 3, 20, 23, 24, 25, 27, 30, 31 })
            {
                Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
            }

            uint stackOffset = currentSlot * sizeof(uint);
            Assert.Equal(expectedBus.Memory.Read32(streamStack + stackOffset), actualBus.Memory.Read32(streamStack + stackOffset));
            Assert.Equal(expectedBus.Memory.Read32(flagStack + stackOffset), actualBus.Memory.Read32(flagStack + stackOffset));
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        }

        foreach ((uint control, uint mask, uint overlay, uint initialValue, uint cachedValue, uint dirtyValue, int expectedInstructions) in new[]
        {
            (0u, 0xFFFF_0000u, 0x0000_4000u, 0x0000_8123u, 0x0000_1200u, 0x0000_0004u, 16),
            (0x0000_0800u, 0x0000_FF00u, 0x0000_8100u, 0x1234_00F0u, 0x0000_8100u, 0x0000_0000u, 20),
        })
        {
            const uint maskUpdatePc = 0x8011_CEC0;
            const uint r13 = 0x803B_52C0;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicGxCommandDispatch(expectedBus.Memory);
            WriteSonicGxCommandDispatch(actualBus.Memory);
            PowerPcState expectedState = CreateSonicGxCommandDispatchState(maskUpdatePc, 0x41);
            PowerPcState actualState = CreateSonicGxCommandDispatchState(maskUpdatePc, 0x41);
            foreach ((GameCubeBus bus, PowerPcState state) in new[] { (expectedBus, expectedState), (actualBus, actualState) })
            {
                state.Gpr[13] = r13;
                state.Gpr[28] = initialValue;
                bus.Memory.Write32(r13 - 29144u, control);
                bus.Memory.Write32(r13 - 29148u, mask);
                bus.Memory.Write32(r13 - 29152u, overlay);
                bus.Memory.Write32(r13 - 29056u, cachedValue);
                bus.Memory.Write32(r13 - 29052u, dirtyValue);
            }

            new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
            bool skipped = InvokeFastForwardSonicGxCommandDispatch(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.Xer, actualState.Xer);
            foreach (int register in new[] { 0, 3, 4, 13, 28 })
            {
                Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
            }

            Assert.Equal(expectedBus.Memory.Read32(r13 - 29056u), actualBus.Memory.Read32(r13 - 29056u));
            Assert.Equal(expectedBus.Memory.Read32(r13 - 29052u), actualBus.Memory.Read32(r13 - 29052u));
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        }
    }

    [Fact]
    public void SonicGprSaveRestoreTailFastForwardMatchesInterpreter()
    {
        const uint baseAddress = 0x803C_1200;
        foreach ((uint pc, int firstRegister) in new[]
        {
            (0x8010_AFC0u, 24),
            (0x8010_AFC4u, 25),
            (0x8010_AFC8u, 26),
            (0x8010_AFCCu, 27),
            (0x8010_B00Cu, 24),
            (0x8010_B010u, 25),
            (0x8010_B014u, 26),
            (0x8010_B018u, 27),
        })
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicGprSaveRestoreTail(expectedBus.Memory);
            WriteSonicGprSaveRestoreTail(actualBus.Memory);
            PowerPcState expectedState = CreateSonicGprSaveRestoreTailState(expectedBus, pc, baseAddress, firstRegister);
            PowerPcState actualState = CreateSonicGprSaveRestoreTailState(actualBus, pc, baseAddress, firstRegister);

            int expectedInstructions = 32 - firstRegister + 1;
            new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
            bool skipped = InvokeFastForwardSonicGprSaveRestoreTail(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in Enumerable.Range(firstRegister, 32 - firstRegister).Prepend(11))
            {
                Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
            }

            for (uint offset = 0; offset < (32u - (uint)firstRegister) * sizeof(uint); offset += sizeof(uint))
            {
                Assert.Equal(expectedBus.Memory.Read32(baseAddress + offset), actualBus.Memory.Read32(baseAddress + offset));
            }
        }
    }

    [Fact]
    public void SonicGxAttributeStateSetterFastForwardMatchesInterpreterCases()
    {
        const uint pc = 0x8010_0D44;
        const uint stateBlock = 0x803C_0000;
        foreach (uint parameter in new[] { 9u, 13u })
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicGxAttributeStateSetter(expectedBus.Memory, pc);
            WriteSonicGxAttributeStateSetter(actualBus.Memory, pc);
            PowerPcState expectedState = CreateSonicGxAttributeStateSetterState(expectedBus, pc, stateBlock, parameter);
            PowerPcState actualState = CreateSonicGxAttributeStateSetterState(actualBus, pc, stateBlock, parameter);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != expectedState.Lr && expectedInstructions < 96)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(expectedState.Lr, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicGxAttributeStateSetter(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Ctr, actualState.Ctr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 4, 5, 6, 8, 9, 10 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"parameter {parameter}, r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }

            foreach (uint offset in new[] { 0x1Cu, 0x4EEu, 0x4F0u })
            {
                if (offset == 0x4EE)
                {
                    Assert.Equal(expectedBus.Memory.Read8(stateBlock + offset), actualBus.Memory.Read8(stateBlock + offset));
                }
                else
                {
                    Assert.Equal(expectedBus.Memory.Read32(stateBlock + offset), actualBus.Memory.Read32(stateBlock + offset));
                }
            }
        }
    }

    [Fact]
    public void SonicGxFloatStripEmitFastForwardMatchesInterpreterLoop()
    {
        const uint pc = 0x8011_D610;
        const uint stream = 0x8132_A728;
        const uint vertexBase = 0x80B2_8500;
        const uint iterations = 3;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        PowerPcState expectedState = CreateSonicGxFloatStripEmitState(expectedBus, pc, stream, vertexBase, iterations);
        PowerPcState actualState = CreateSonicGxFloatStripEmitState(actualBus, pc, stream, vertexBase, iterations);
        int expectedInstructions = checked((int)(iterations * 26 + 2));

        new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
        bool skipped = InvokeFastForwardSonicGxFloatStripEmitLoop(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 26, 29, 30 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (int register = 1; register <= 3; register++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[register]), BitConverter.DoubleToInt64Bits(actualState.Fpr[register]));
        }

        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxFloatAttributeStripEmitFastForwardMatchesInterpreterLoop()
    {
        const uint pc = 0x8011_FF70;
        const uint stream = 0x8132_A728;
        const uint vertexBase = 0x80B2_8500;
        const uint iterations = 3;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        PowerPcState expectedState = CreateSonicGxFloatAttributeStripEmitState(expectedBus, pc, stream, vertexBase, iterations);
        PowerPcState actualState = CreateSonicGxFloatAttributeStripEmitState(actualBus, pc, stream, vertexBase, iterations);
        int expectedInstructions = checked((int)(iterations * 22 + 2));

        new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
        bool skipped = InvokeFastForwardSonicGxFloatAttributeStripEmitLoop(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 4, 26, 29, 30 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (int register = 1; register <= 3; register++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[register]), BitConverter.DoubleToInt64Bits(actualState.Fpr[register]));
        }

        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxFloatTexcoordStripEmitFastForwardMatchesInterpreterLoop()
    {
        const uint pc = 0x8011_D860;
        const uint stream = 0x8132_A728;
        const uint vertexBase = 0x80B2_8500;
        const uint iterations = 3;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        PowerPcState expectedState = CreateSonicGxFloatTexcoordStripEmitState(expectedBus, pc, stream, vertexBase, iterations);
        PowerPcState actualState = CreateSonicGxFloatTexcoordStripEmitState(actualBus, pc, stream, vertexBase, iterations);
        int expectedInstructions = checked((int)(iterations * 36 + 2));

        new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
        bool skipped = InvokeFastForwardSonicGxFloatTexcoordStripEmitLoop(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 4, 5, 25, 28, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (int register = 1; register <= 3; register++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[register]), BitConverter.DoubleToInt64Bits(actualState.Fpr[register]));
        }

        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxFloatTexcoordStripEmitFastForwardMatchesInterpreterHeader()
    {
        const uint pc = 0x8011_D830;
        const uint stream = 0x8132_A728;
        const uint vertexBase = 0x80B2_8500;
        const uint stack = 0x817F_F000;
        const uint stateBlock = 0x803C_0000;
        const uint iterations = 3;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        List<MmioAccess> expectedWrites = [];
        List<MmioAccess> actualWrites = [];
        expectedBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                expectedWrites.Add(access);
            }
        };
        actualBus.MmioAccessObserver = access =>
        {
            if (access.Kind == MmioAccessKind.Write && access.Address == 0xCC00_8000)
            {
                actualWrites.Add(access);
            }
        };
        PowerPcState expectedState = CreateSonicGxFloatTexcoordStripEmitHeaderState(expectedBus, pc, stream, vertexBase, stack, stateBlock, iterations);
        PowerPcState actualState = CreateSonicGxFloatTexcoordStripEmitHeaderState(actualBus, pc, stream, vertexBase, stack, stateBlock, iterations);
        int expectedInstructions = checked((int)(11 + 28 + iterations * 36 + 2));

        new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
        bool skipped = InvokeFastForwardSonicGxFloatTexcoordStripEmitLoop(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 4, 5, 6, 13, 25, 28, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (int register = 1; register <= 3; register++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[register]), BitConverter.DoubleToInt64Bits(actualState.Fpr[register]));
        }

        for (uint offset = 0; offset <= 0x30; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 40 + offset), actualBus.Memory.Read32(stack - 40 + offset));
        }

        Assert.Equal(
            expectedWrites.Select(access => (access.Width, access.Value)).ToArray(),
            actualWrites.Select(access => (access.Width, access.Value)).ToArray());
    }

    [Fact]
    public void SonicGxFloatTexcoordStripEmitFastForwardMatchesInterpreterTail()
    {
        const uint loopPc = 0x8011_D860;
        const uint pc = loopPc + 0x5C;
        foreach ((uint remaining, uint expectedPc) in new[]
        {
            (2u, 0x8011_D830u),
            (1u, 0x8011_D8C8u),
        })
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicGxFloatTexcoordStripEmitLoop(expectedBus.Memory, loopPc);
            WriteSonicGxFloatTexcoordStripEmitLoop(actualBus.Memory, loopPc);
            PowerPcState expectedState = CreateSonicGxFloatTexcoordStripEmitTailState(pc, remaining);
            PowerPcState actualState = CreateSonicGxFloatTexcoordStripEmitTailState(pc, remaining);

            new PowerPcInterpreter().Run(expectedState, expectedBus, 3);
            bool skipped = InvokeFastForwardSonicGxFloatTexcoordStripEmitLoop(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(3, skippedInstructions);
            Assert.Equal(expectedPc, actualState.Pc);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.Gpr[27], actualState.Gpr[27]);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        }
    }

    [Fact]
    public void SonicPairedTransform2dFastForwardMatchesInterpreterLoop()
    {
        const uint pc = 0x8011_DAA8;
        const uint input = 0x800C_0000;
        const uint output = 0x800D_0000;
        const uint iterations = 3;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicPairedTransform2dState(expectedBus, pc, input, output, iterations);
        PowerPcState actualState = CreateSonicPairedTransform2dState(actualBus, pc, input, output, iterations);
        int expectedInstructions = checked((int)(iterations * 14 + 4));

        new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
        bool skipped = InvokeFastForwardSonicPairedTransform2d(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Ctr, actualState.Ctr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 5, 6, 8, 10 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (int register = 8; register <= 13; register++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[register]), BitConverter.DoubleToInt64Bits(actualState.Fpr[register]));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.FprPair1[register]), BitConverter.DoubleToInt64Bits(actualState.FprPair1[register]));
        }

        for (uint offset = 0; offset <= 0x90; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(output + offset), actualBus.Memory.Read32(output + offset));
        }
    }

    [Fact]
    public void SonicPairedTransform4dFastForwardMatchesInterpreterLoop()
    {
        const uint pc = 0x8011_DB94;
        const uint input = 0x800C_0000;
        const uint output = 0x800D_0000;
        const uint stack = 0x817F_F000;
        const uint iterations = 3;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicPairedTransform4dState(expectedBus, pc, input, output, stack, iterations);
        PowerPcState actualState = CreateSonicPairedTransform4dState(actualBus, pc, input, output, stack, iterations);
        int expectedInstructions = checked((int)(iterations * 20 + 11));

        new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
        bool skipped = InvokeFastForwardSonicPairedTransform4d(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Ctr, actualState.Ctr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 1, 5, 6 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (int register = 8; register <= 18; register++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[register]), BitConverter.DoubleToInt64Bits(actualState.Fpr[register]));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.FprPair1[register]), BitConverter.DoubleToInt64Bits(actualState.FprPair1[register]));
        }

        for (uint offset = 0; offset <= 0x90; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(output + offset), actualBus.Memory.Read32(output + offset));
        }
    }

    [Fact]
    public void SonicPairedTransform4dIndexedOutputFastForwardMatchesInterpreterLoop()
    {
        const uint input = 0x800C_0000;
        const uint outputBase = 0x800D_0000;
        const uint stack = 0x817F_F000;
        const uint iterations = 3;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicPairedTransform4dIndexedOutputState(expectedBus, input, outputBase, stack, iterations);
        PowerPcState actualState = CreateSonicPairedTransform4dIndexedOutputState(actualBus, input, outputBase, stack, iterations);
        int expectedInstructions = checked((int)(iterations * 33 + 16));

        new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
        bool skipped = InvokeFastForwardSonicPairedTransform4dIndexedOutput(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Ctr, actualState.Ctr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 1, 4, 6, 7, 8, 9, 10 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (int register = 8; register <= 23; register++)
        {
            long expectedLane0 = BitConverter.DoubleToInt64Bits(expectedState.Fpr[register]);
            long actualLane0 = BitConverter.DoubleToInt64Bits(actualState.Fpr[register]);
            long expectedLane1 = BitConverter.DoubleToInt64Bits(expectedState.FprPair1[register]);
            long actualLane1 = BitConverter.DoubleToInt64Bits(actualState.FprPair1[register]);
            Assert.True(expectedLane0 == actualLane0, $"f{register} lane0 expected={expectedState.Fpr[register]} actual={actualState.Fpr[register]}");
            Assert.True(expectedLane1 == actualLane1, $"f{register} lane1 expected={expectedState.FprPair1[register]} actual={actualState.FprPair1[register]}");
        }

        for (uint offset = 0; offset <= 0xA0; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(outputBase + offset), actualBus.Memory.Read32(outputBase + offset));
        }
    }

    [Fact]
    public void SonicVectorBlendCopyFastForwardMatchesInterpreterLoop()
    {
        const uint pc = 0x8012_0D98;
        const uint input = 0x800C_0000;
        const uint output = 0x800D_0000;
        const uint blendA = 0x800E_0000;
        const uint blendB = 0x800F_0000;
        const uint stack = 0x817F_F000;
        const uint iterations = 3;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicVectorBlendCopyState(expectedBus, pc, input, output, blendA, blendB, stack, iterations);
        PowerPcState actualState = CreateSonicVectorBlendCopyState(actualBus, pc, input, output, blendA, blendB, stack, iterations);
        int expectedInstructions = checked((int)(iterations * 47));

        new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
        bool skipped = InvokeFastForwardSonicVectorBlendCopy(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Ctr, actualState.Ctr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 4, 7, 29, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (int register = 0; register <= 2; register++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[register]), BitConverter.DoubleToInt64Bits(actualState.Fpr[register]));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.FprPair1[register]), BitConverter.DoubleToInt64Bits(actualState.FprPair1[register]));
        }

        for (uint offset = 0; offset < iterations * 24; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(output + offset), actualBus.Memory.Read32(output + offset));
        }
    }

    [Fact]
    public void SonicCoordinatePairFillFastForwardMatchesInterpreterLoop()
    {
        const uint pc = 0x8014_B260;
        const uint output = 0x8039_9000;
        const uint iterations = 8;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicCoordinatePairFillState(expectedBus, pc, output, iterations, columnLimit: 3, column: 2, row: 4);
        PowerPcState actualState = CreateSonicCoordinatePairFillState(actualBus, pc, output, iterations, columnLimit: 3, column: 2, row: 4);

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != pc + 0x2C && expectedInstructions < 128)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        Assert.Equal(pc + 0x2C, expectedState.Pc);
        bool skipped = InvokeFastForwardSonicCoordinatePairFillLoop(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Ctr, actualState.Ctr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 4, 5, 6 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (uint offset = 0; offset < iterations * 2; offset++)
        {
            Assert.Equal(expectedBus.Memory.Read8(output + offset), actualBus.Memory.Read8(output + offset));
        }
    }

    [Fact]
    public void SonicBufferFillFastForwardMatchesInterpreterLoops()
    {
        (uint Pc, uint CountOffset, int IndexRegister, int CursorRegister, int ValueRegister, uint ExitPc, uint Count)[] cases =
        [
            (0x800F_C598, 0, 3, 4, 31, 0x800F_C5B4, 2),
            (0x800F_C5C0, 4, 4, 5, 3, 0x800F_C5DC, 3),
            (0x800F_C5E8, 8, 4, 6, 3, 0x800F_C604, 1),
        ];

        for (int caseIndex = 0; caseIndex < cases.Length; caseIndex++)
        {
            var testCase = cases[caseIndex];
            const uint descriptor = 0x800A_0000;
            uint output = 0x800B_0000 + (uint)caseIndex * 0x1000;
            uint limit = testCase.Count * 160;
            uint startIndex = limit - 3;
            uint fillValue = 0xCAFE_0000u + (uint)caseIndex;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicBufferFillLoops(expectedBus.Memory);
            WriteSonicBufferFillLoops(actualBus.Memory);
            expectedBus.Memory.Write32(descriptor + testCase.CountOffset, testCase.Count);
            actualBus.Memory.Write32(descriptor + testCase.CountOffset, testCase.Count);
            for (uint offset = 0; offset < 0x20; offset += sizeof(uint))
            {
                expectedBus.Memory.Write32(output + offset, 0xA5A5_0000u + offset);
                actualBus.Memory.Write32(output + offset, 0xA5A5_0000u + offset);
            }

            PowerPcState expectedState = CreateSonicBufferFillState(testCase.Pc, descriptor, output, startIndex, fillValue, testCase.IndexRegister, testCase.CursorRegister, testCase.ValueRegister);
            PowerPcState actualState = CreateSonicBufferFillState(testCase.Pc, descriptor, output, startIndex, fillValue, testCase.IndexRegister, testCase.CursorRegister, testCase.ValueRegister);
            int expectedInstructions = checked((int)((limit - startIndex) * 7));

            new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
            bool skipped = InvokeFastForwardSonicBufferFillLoop(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(testCase.ExitPc, expectedState.Pc);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            Assert.Equal(expectedState.Gpr[0], actualState.Gpr[0]);
            Assert.Equal(expectedState.Gpr[testCase.IndexRegister], actualState.Gpr[testCase.IndexRegister]);
            Assert.Equal(expectedState.Gpr[testCase.CursorRegister], actualState.Gpr[testCase.CursorRegister]);
            Assert.Equal(expectedState.Gpr[testCase.ValueRegister], actualState.Gpr[testCase.ValueRegister]);
            for (uint offset = 0; offset < 0x20; offset += sizeof(uint))
            {
                Assert.Equal(expectedBus.Memory.Read32(output + offset), actualBus.Memory.Read32(output + offset));
            }
        }
    }

    [Fact]
    public void SonicModeStateUpdateFastForwardMatchesInterpreterPaths()
    {
        (byte Mode, uint Input, string Name)[] cases =
        [
            (19, 20, "below-range-no-table"),
            (20, 5, "set-20"),
            (20, 11, "set-22-from-20"),
            (21, 4, "set-21"),
            (22, 11, "set-22"),
            (23, 0xFFFF_FFFF, "set-23"),
            (24, 20, "above-range-no-table"),
            (21, 0, "no-set-21"),
            (22, 0, "no-set-22"),
        ];

        foreach (var testCase in cases)
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicModeStateUpdate(expectedBus.Memory);
            WriteSonicModeStateUpdate(actualBus.Memory);
            expectedBus.Memory.Write8(0x801C_C1AF, testCase.Mode);
            actualBus.Memory.Write8(0x801C_C1AF, testCase.Mode);
            expectedBus.Memory.Write8(0x801C_C17B, 0xAA);
            actualBus.Memory.Write8(0x801C_C17B, 0xAA);
            expectedBus.Memory.Write32(0x8010_0000, 0xDEAD_BEEF);
            actualBus.Memory.Write32(0x8010_0000, 0xDEAD_BEEF);
            PowerPcState expectedState = CreateSonicModeStateUpdateState(testCase.Input);
            PowerPcState actualState = CreateSonicModeStateUpdateState(testCase.Input);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != expectedState.Lr && expectedInstructions < 96)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(expectedState.Lr, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicModeStateUpdate(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Ctr, actualState.Ctr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 4, 5, 6, 13 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{testCase.Name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }

            Assert.Equal(expectedBus.Memory.Read8(0x801C_C1AF), actualBus.Memory.Read8(0x801C_C1AF));
            Assert.Equal(expectedBus.Memory.Read8(0x801C_C17B), actualBus.Memory.Read8(0x801C_C17B));
            Assert.Equal(expectedBus.Memory.Read32(0x8010_0000), actualBus.Memory.Read32(0x8010_0000));
        }
    }

    [Fact]
    public void SonicModeCoordinatorPrologueFastForwardMatchesInterpreter()
    {
        const uint pc = 0x800E_30CC;
        const uint stack = 0x817F_F000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicModeCoordinatorPrologue(expectedBus.Memory);
        WriteSonicModeCoordinatorPrologue(actualBus.Memory);
        PowerPcState expectedState = CreateSonicModeCoordinatorPrologueState(stack);
        PowerPcState actualState = CreateSonicModeCoordinatorPrologueState(stack);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 9);
        bool skipped = InvokeFastForwardSonicModeCoordinatorPrologue(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(9, skippedInstructions);
        Assert.Equal(pc + 0x24, expectedState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 26, 27, 28, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        Assert.Equal(expectedBus.Memory.Read32(stack + 4), actualBus.Memory.Read32(stack + 4));
        for (uint offset = 0; offset < 40; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 40 + offset), actualBus.Memory.Read32(stack - 40 + offset));
        }
    }

    [Fact]
    public void SonicModeCoordinatorBodyFastForwardMatchesInterpreterPaths()
    {
        (uint Pc, uint StateWord, uint BusyFlag, uint FallbackFlag, uint QueryPointer, uint QueryMode, short ChildStatus, string Name)[] cases =
        [
            (0x800E_30F0, 0, 0, 0, 0x8010_4000, 5, 0, "state-branch-direct-mode"),
            (0x800E_3124, 0, 1, 0, 0, 0, 0, "busy-negative-one"),
            (0x800E_3124, 0, 0, 1, 0, 0, 3, "fallback-child-status-forces-11"),
            (0x800E_3124, 4, 0, 1, 0, 0, 0, "fallback-state-word-does-not-force-11"),
        ];

        foreach (var testCase in cases)
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicModeCoordinatorBodyFixture(expectedBus.Memory, testCase.StateWord, testCase.BusyFlag, testCase.FallbackFlag, testCase.QueryPointer, testCase.QueryMode, testCase.ChildStatus);
            WriteSonicModeCoordinatorBodyFixture(actualBus.Memory, testCase.StateWord, testCase.BusyFlag, testCase.FallbackFlag, testCase.QueryPointer, testCase.QueryMode, testCase.ChildStatus);
            PowerPcState expectedState = CreateSonicModeCoordinatorBodyState(testCase.Pc, testCase.StateWord);
            PowerPcState actualState = CreateSonicModeCoordinatorBodyState(testCase.Pc, testCase.StateWord);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != 0x800E_3170 && expectedInstructions < 256)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(0x800E_3170u, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicModeCoordinatorBody(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.True(
                expectedInstructions == skippedInstructions,
                $"{testCase.Name} instructions: expected {expectedInstructions}, actual {skippedInstructions}");
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Ctr, actualState.Ctr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.Msr, actualState.Msr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 1, 3, 4, 5, 6, 13, 26, 27, 28, 29, 30, 31 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{testCase.Name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }

            Assert.Equal(expectedBus.Memory.Read8(0x801C_C1AF), actualBus.Memory.Read8(0x801C_C1AF));
            Assert.Equal(expectedBus.Memory.Read8(0x801C_C17B), actualBus.Memory.Read8(0x801C_C17B));
            Assert.Equal(expectedBus.Memory.Read32(0x800F_897C), actualBus.Memory.Read32(0x800F_897C));
            Assert.Equal(expectedBus.Memory.Read16(0x8010_3060), actualBus.Memory.Read16(0x8010_3060));
            Assert.Equal(expectedBus.Memory.Read32(0x8010_3064), actualBus.Memory.Read32(0x8010_3064));
            Assert.Equal(expectedBus.Memory.Read16(0x8010_3068), actualBus.Memory.Read16(0x8010_3068));
            Assert.Equal(expectedBus.Memory.Read16(0x8010_306A), actualBus.Memory.Read16(0x8010_306A));
        }
    }

    [Fact]
    public void SonicModeCoordinatorZeroTailFastForwardMatchesInterpreter()
    {
        const uint stack = 0x817F_EFD8;
        const uint returnPc = 0x8000_88A0;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicModeCoordinatorZeroTail(expectedBus.Memory);
        WriteSonicModeCoordinatorZeroTail(actualBus.Memory);
        expectedBus.Memory.Write8(0x801C_C17B, 0);
        actualBus.Memory.Write8(0x801C_C17B, 0);
        expectedBus.Memory.Write32(stack + 44, returnPc);
        actualBus.Memory.Write32(stack + 44, returnPc);
        for (int register = 26; register <= 31; register++)
        {
            uint value = 0xCAFE_0000u + (uint)register;
            expectedBus.Memory.Write32(stack + 16u + (uint)(register - 26) * sizeof(uint), value);
            actualBus.Memory.Write32(stack + 16u + (uint)(register - 26) * sizeof(uint), value);
        }

        PowerPcState expectedState = CreateSonicModeCoordinatorZeroTailState(stack);
        PowerPcState actualState = CreateSonicModeCoordinatorZeroTailState(stack);
        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != returnPc && expectedInstructions < 32)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        Assert.Equal(returnPc, expectedState.Pc);
        bool skipped = InvokeFastForwardSonicModeCoordinatorZeroTail(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 26, 27, 28, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Fact]
    public void SonicStatusQueryFastForwardMatchesInterpreterEarlyReturnPaths()
    {
        (sbyte Status, string Name)[] cases =
        [
            (0, "zero"),
            (4, "four"),
            (-1, "negative-one"),
        ];

        foreach (var testCase in cases)
        {
            const uint stack = 0x817F_F000;
            const uint returnPc = 0x8000_9A40;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicStatusQuery(expectedBus.Memory);
            WriteSonicStatusQuery(actualBus.Memory);
            SetupSonicStatusQueryData(expectedBus.Memory, 7, 0x8010_5000, testCase.Status);
            SetupSonicStatusQueryData(actualBus.Memory, 7, 0x8010_5000, testCase.Status);
            PowerPcState expectedState = CreateSonicStatusQueryState(stack, returnPc, 7);
            PowerPcState actualState = CreateSonicStatusQueryState(stack, returnPc, 7);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != returnPc && expectedInstructions < 64)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(returnPc, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicStatusQuery(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 1, 3, 13, 26, 27, 28, 29, 30, 31 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{testCase.Name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }

            Assert.Equal(expectedBus.Memory.Read32(stack + 4), actualBus.Memory.Read32(stack + 4));
            for (uint offset = 0; offset < 40; offset += sizeof(uint))
            {
                Assert.Equal(expectedBus.Memory.Read32(stack - 40 + offset), actualBus.Memory.Read32(stack - 40 + offset));
            }
        }
    }

    [Fact]
    public void SonicStatusCallerLoopFastForwardMatchesInterpreterCommonPath()
    {
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicStatusCallerLoopFixture(expectedBus.Memory);
        WriteSonicStatusCallerLoopFixture(actualBus.Memory);
        PowerPcState expectedState = CreateSonicStatusCallerLoopState();
        PowerPcState actualState = CreateSonicStatusCallerLoopState();

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        do
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }
        while (expectedState.Pc != 0x8001_2B24 && expectedInstructions < 512);

        Assert.Equal(0x8001_2B24u, expectedState.Pc);
        bool skipped = InvokeFastForwardSonicStatusCallerLoop(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Ctr, actualState.Ctr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.Msr, actualState.Msr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 4, 5, 6, 13, 26, 27, 28, 29, 30, 31 })
        {
            Assert.True(
                expectedState.Gpr[register] == actualState.Gpr[register],
                $"r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
        }

        for (uint offset = 0; offset < 80; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(0x817F_F000 - 80 + offset), actualBus.Memory.Read32(0x817F_F000 - 80 + offset));
        }

        Assert.Equal(expectedBus.Memory.Read8(0x801C_C1AF), actualBus.Memory.Read8(0x801C_C1AF));
        Assert.Equal(expectedBus.Memory.Read8(0x801C_C17B), actualBus.Memory.Read8(0x801C_C17B));
    }

    [Fact]
    public void SonicStatusCallerDispatchFastForwardMatchesInterpreterPaths()
    {
        (uint Pc, uint R3, uint ExpectedPc, int ExpectedInstructions, string Name)[] cases =
        [
            (0x8001_2B24, 0xFFFF_FFFF, 0x8013_6CEC, 2, "status-query-call"),
            (0x8001_2B2C, 0, 0x8001_2B54, 4, "common-result"),
            (0x8001_2B2C, 3, 0x8001_2B5C, 5, "status-three-store"),
            (0x8001_2B2C, 4, 0x8001_2B5C, 7, "status-four-store"),
            (0x8001_2B54, 0, 0x800E_30CC, 1, "dispatch-call"),
            (0x8001_2B58, 0, 0x8001_2B24, 1, "loop-back"),
        ];

        foreach ((uint pc, uint r3, uint expectedPc, int expectedInstructions, string name) in cases)
        {
            const uint smallDataBase = 0x803B_52C0;
            uint storeAddress = unchecked(smallDataBase + 0xFFFF_8084u);
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicStatusCallerLoop(expectedBus.Memory);
            WriteSonicStatusCallerLoop(actualBus.Memory);
            expectedBus.Memory.Write8(0x8030_1000, 0x5A);
            actualBus.Memory.Write8(0x8030_1000, 0x5A);
            PowerPcState expectedState = new()
            {
                Pc = pc,
                Cr = 0x1234_5678,
                Lr = 0x8123_4568,
                TimeBase = 0x1000,
            };
            PowerPcState actualState = expectedState.Clone();
            expectedState.Gpr[3] = r3;
            expectedState.Gpr[13] = smallDataBase;
            expectedState.Gpr[29] = 0x8030_1000;
            actualState.Gpr[3] = expectedState.Gpr[3];
            actualState.Gpr[13] = expectedState.Gpr[13];
            actualState.Gpr[29] = expectedState.Gpr[29];
            expectedState.Spr[22] = 0xFFFF_F000;
            actualState.Spr[22] = expectedState.Spr[22];

            new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
            bool skipped = InvokeFastForwardSonicStatusCallerDispatch(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedPc, actualState.Pc);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 13, 29 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }

            Assert.Equal(expectedBus.Memory.Read8(storeAddress), actualBus.Memory.Read8(storeAddress));
        }
    }

    [Fact]
    public void SonicTableByteBuildDispatchFastForwardMatchesInterpreterPaths()
    {
        (uint Pc, uint R3, uint R29, uint ExpectedPc, int ExpectedInstructions, string Name)[] cases =
        [
            (0x800E_1158, 0xDEAD_BEEF, 7, 0x800E_1F14, 2, "classifier-call"),
            (0x800E_1160, 0xFFFF_FF80, 7, 0x800E_1158, 7, "post-call-loop"),
            (0x800E_1160, 0x0000_007F, 3422, 0x800E_117C, 7, "post-call-exit"),
        ];

        foreach ((uint pc, uint r3, uint r29, uint expectedPc, int expectedInstructions, string name) in cases)
        {
            const uint sourceRecord = 0x8010_2000;
            const uint output = 0x8010_5000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicTableByteBuildDispatch(expectedBus.Memory);
            WriteSonicTableByteBuildDispatch(actualBus.Memory);
            PowerPcState expectedState = CreateSonicTableByteBuildDispatchState(pc, sourceRecord, output, r3, r29);
            PowerPcState actualState = CreateSonicTableByteBuildDispatchState(pc, sourceRecord, output, r3, r29);

            new PowerPcInterpreter().Run(expectedState, expectedBus, expectedInstructions);
            bool skipped = InvokeFastForwardSonicTableByteBuildDispatch(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedPc, actualState.Pc);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            Assert.Equal(expectedBus.Memory.Read8(output), actualBus.Memory.Read8(output));
            foreach (int register in new[] { 0, 3, 28, 29, 30 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicLineCopyFastForwardMatchesInterpreterPaths()
    {
        (uint Pc, byte[] Source, byte CurrentByte, string Name)[] cases =
        [
            (0x8013_B454, [(byte)'A', (byte)'B', (byte)'C', 10, (byte)'Z'], 0, "load-entry-lf"),
            (0x8013_B430, [(byte)'X', (byte)'Y', (byte)'Z', 0, (byte)'Q'], (byte)'X', "loop-entry-nul"),
            (0x8013_B430, [13, (byte)'Q'], 13, "loop-entry-cr"),
        ];

        foreach ((uint pc, byte[] sourceBytes, byte currentByte, string name) in cases)
        {
            const uint source = 0x8010_6000;
            const uint output = 0x8010_7000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicLineCopy(expectedBus.Memory);
            WriteSonicLineCopy(actualBus.Memory);
            for (uint i = 0; i < sourceBytes.Length; i++)
            {
                expectedBus.Memory.Write8(source + i, sourceBytes[i]);
                actualBus.Memory.Write8(source + i, sourceBytes[i]);
            }

            PowerPcState expectedState = CreateSonicLineCopyState(pc, source, output, currentByte);
            PowerPcState actualState = CreateSonicLineCopyState(pc, source, output, currentByte);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != 0x8013_B460 && expectedInstructions < 256)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(0x8013_B460u, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicLineCopy(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            for (uint offset = 0; offset < 8; offset++)
            {
                Assert.Equal(expectedBus.Memory.Read8(output + offset), actualBus.Memory.Read8(output + offset));
            }

            foreach (int register in new[] { 3, 4, 5, 6 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicLineSkipFastForwardMatchesInterpreterPaths()
    {
        (byte[] Source, uint ExpectedPc, string Name)[] cases =
        [
            ([(byte)'A', (byte)'B', (byte)'C', 10, (byte)'Z'], 0x8013_A3E0, "lf"),
            ([(byte)'A', (byte)'B', (byte)'C', 13, 10, (byte)'Z'], 0x8013_A3E0, "crlf"),
            ([(byte)'A', (byte)'B', (byte)'C', 13, (byte)'X', (byte)'Z'], 0x8013_A3E0, "cr-only"),
            ([(byte)'A', (byte)'B', (byte)'C', 0, (byte)'Z'], 0x8013_A3E8, "nul"),
        ];

        foreach ((byte[] sourceBytes, uint expectedPc, string name) in cases)
        {
            const uint source = 0x8010_6000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicLineSkip(expectedBus.Memory);
            WriteSonicLineSkip(actualBus.Memory);
            for (uint i = 0; i < sourceBytes.Length; i++)
            {
                expectedBus.Memory.Write8(source + i, sourceBytes[i]);
                actualBus.Memory.Write8(source + i, sourceBytes[i]);
            }

            PowerPcState expectedState = CreateSonicLineSkipState(source);
            PowerPcState actualState = CreateSonicLineSkipState(source);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != 0x8013_A3E0 && expectedState.Pc != 0x8013_A3E8 && expectedInstructions < 256)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(expectedPc, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicLineSkip(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 26 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicStringAppendScanFastForwardMatchesInterpreterLoop()
    {
        (byte[] Destination, string Name)[] cases =
        [
            ([(byte)'A', 0], "one-byte"),
            ([(byte)'A', (byte)'B', (byte)'C', 0], "three-bytes"),
            ([0], "already-empty"),
        ];

        foreach ((byte[] destinationBytes, string name) in cases)
        {
            const uint destination = 0x8010_6400;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicStringAppendScan(expectedBus.Memory);
            WriteSonicStringAppendScan(actualBus.Memory);
            for (uint i = 0; i < destinationBytes.Length; i++)
            {
                expectedBus.Memory.Write8(destination + i, destinationBytes[i]);
                actualBus.Memory.Write8(destination + i, destinationBytes[i]);
            }

            PowerPcState expectedState = CreateSonicStringAppendScanState(destination);
            PowerPcState actualState = CreateSonicStringAppendScanState(destination);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != 0x8010_DF9C && expectedInstructions < 256)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(0x8010_DF9Cu, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicStringAppendScan(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 5 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicFreeBlockScanFastForwardMatchesInterpreterPaths()
    {
        (ushort FirstMagic, uint RequestSize, bool Found, string Name)[] cases =
        [
            (0x4D46, 0x40, true, "first-block-fits"),
            (0x4D42, 0x40, true, "skip-non-magic"),
            (0x4D46, 0x200, false, "exhausted-after-small-block"),
        ];

        foreach ((ushort firstMagic, uint requestSize, bool found, string name) in cases)
        {
            const uint firstBlock = 0x8010_8000;
            const uint secondBlock = 0x8010_8100;
            const uint thirdBlock = 0x8010_8200;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicFreeBlockScan(expectedBus.Memory);
            WriteSonicFreeBlockScan(actualBus.Memory);
            SetupSonicFreeBlock(expectedBus.Memory, firstBlock, firstMagic, secondBlock);
            SetupSonicFreeBlock(actualBus.Memory, firstBlock, firstMagic, secondBlock);
            uint secondNext = found ? thirdBlock : 0;
            SetupSonicFreeBlock(expectedBus.Memory, secondBlock, 0x4D46, secondNext);
            SetupSonicFreeBlock(actualBus.Memory, secondBlock, 0x4D46, secondNext);
            SetupSonicFreeBlock(expectedBus.Memory, thirdBlock, 0x4D46, 0);
            SetupSonicFreeBlock(actualBus.Memory, thirdBlock, 0x4D46, 0);

            PowerPcState expectedState = CreateSonicFreeBlockScanState(firstBlock, secondBlock, requestSize);
            PowerPcState actualState = CreateSonicFreeBlockScanState(firstBlock, secondBlock, requestSize);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != 0x8013_9D38 && expectedState.Pc != 0x8013_9CC0 && expectedInstructions < 256)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(found ? 0x8013_9D38u : 0x8013_9CC0u, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicFreeBlockScan(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 4, 5, 29 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicCacheStoreSweepFastForwardMatchesInterpreterLoop()
    {
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicCacheStoreSweep(expectedBus.Memory);
        WriteSonicCacheStoreSweep(actualBus.Memory);
        PowerPcState expectedState = CreateSonicCacheStoreSweepState(iterations: 5);
        PowerPcState actualState = CreateSonicCacheStoreSweepState(iterations: 5);

        PowerPcInterpreter interpreter = new();
        int expectedInstructions = 0;
        while (expectedState.Pc != 0x800E_4F98 && expectedInstructions < 64)
        {
            interpreter.Step(expectedState, expectedBus);
            expectedInstructions++;
        }

        Assert.Equal(0x800E_4F98u, expectedState.Pc);
        bool skipped = InvokeFastForwardSonicCacheStoreSweep(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(expectedInstructions, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Ctr, actualState.Ctr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        Assert.Equal(expectedState.Gpr[3], actualState.Gpr[3]);
    }

    [Fact]
    public void SonicStateZeroFillFastForwardMatchesInterpreterLoop()
    {
        const uint destination = 0x8010_9000;
        const uint iterations = 3;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        WriteSonicStateZeroFill(expectedBus.Memory);
        WriteSonicStateZeroFill(actualBus.Memory);
        for (uint offset = 0; offset < iterations * 36 + 16; offset++)
        {
            expectedBus.Memory.Write8(destination + offset, 0xA5);
            actualBus.Memory.Write8(destination + offset, 0xA5);
        }

        PowerPcState expectedState = CreateSonicStateZeroFillState(destination, iterations);
        PowerPcState actualState = CreateSonicStateZeroFillState(destination, iterations);

        new PowerPcInterpreter().Run(expectedState, expectedBus, checked((int)(iterations * 11)));
        bool skipped = InvokeFastForwardSonicStateZeroFill(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(checked((int)(iterations * 11)), skippedInstructions);
        Assert.Equal(0x800F_9858u, actualState.Pc);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Ctr, actualState.Ctr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (uint offset = 0; offset < iterations * 36 + 16; offset++)
        {
            Assert.Equal(expectedBus.Memory.Read8(destination + offset), actualBus.Memory.Read8(destination + offset));
        }
    }

    [Fact]
    public void SonicManagerSlotScanFastForwardMatchesInterpreterPaths()
    {
        (int StartSlot, int? ActiveSlot, uint ExpectedPc, string Name)[] cases =
        [
            (0, null, 0x8012_4F74, "all-inactive"),
            (0, 3, 0x8012_4F5C, "stops-at-active"),
            (14, null, 0x8012_4F74, "tail-exit"),
        ];

        foreach ((int startSlot, int? activeSlot, uint expectedPc, string name) in cases)
        {
            const uint table = 0x8010_A000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicManagerSlotScan(expectedBus.Memory);
            WriteSonicManagerSlotScan(actualBus.Memory);
            for (uint slot = 0; slot < 16; slot++)
            {
                byte value = activeSlot == slot ? (byte)1 : (byte)0;
                expectedBus.Memory.Write8(table + slot * 1080, value);
                actualBus.Memory.Write8(table + slot * 1080, value);
            }

            PowerPcState expectedState = CreateSonicManagerSlotScanState(table, startSlot);
            PowerPcState actualState = CreateSonicManagerSlotScanState(table, startSlot);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != expectedPc && expectedInstructions < 256)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(expectedPc, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicManagerSlotScan(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 30, 31 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicTaskEntryScanFastForwardMatchesInterpreterPaths()
    {
        (int StartEntry, int? ActiveEntry, uint ExpectedPc, string Name)[] cases =
        [
            (0, null, 0x8013_03F4, "all-inactive"),
            (0, 3, 0x8013_03C0, "stops-at-active"),
            (14, null, 0x8013_03F4, "tail-exit"),
        ];

        foreach ((int startEntry, int? activeEntry, uint expectedPc, string name) in cases)
        {
            const uint table = 0x8010_C000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicTaskEntryScan(expectedBus.Memory);
            WriteSonicTaskEntryScan(actualBus.Memory);
            for (uint entry = 0; entry < 16; entry++)
            {
                byte value = activeEntry == entry ? (byte)1 : (byte)0;
                expectedBus.Memory.Write8(table + entry * 156, value);
                actualBus.Memory.Write8(table + entry * 156, value);
            }

            PowerPcState expectedState = CreateSonicTaskEntryScanState(table, startEntry);
            PowerPcState actualState = CreateSonicTaskEntryScanState(table, startEntry);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != expectedPc && expectedInstructions < 256)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(expectedPc, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicTaskEntryScan(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 30, 31 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicObjectSlotScanFastForwardMatchesInterpreterPaths()
    {
        (int StartSlot, int? ActiveSlot, uint ExpectedPc, string Name)[] cases =
        [
            (0, null, 0x8013_3900, "all-inactive"),
            (0, 3, 0x8013_38EC, "stops-at-active"),
            (14, null, 0x8013_3900, "tail-exit"),
        ];

        foreach ((int startSlot, int? activeSlot, uint expectedPc, string name) in cases)
        {
            const uint table = 0x8010_D000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicObjectSlotScan(expectedBus.Memory);
            WriteSonicObjectSlotScan(actualBus.Memory);
            for (uint slot = 0; slot < 16; slot++)
            {
                byte value = activeSlot == slot ? (byte)1 : (byte)0;
                expectedBus.Memory.Write8(table + slot * 164, value);
                actualBus.Memory.Write8(table + slot * 164, value);
            }

            PowerPcState expectedState = CreateSonicObjectSlotScanState(table, startSlot);
            PowerPcState actualState = CreateSonicObjectSlotScanState(table, startSlot);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            while (expectedState.Pc != expectedPc && expectedInstructions < 256)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(expectedPc, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicObjectSlotScan(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 30, 31 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicHalfwordChecksumFastForwardMatchesInterpreterLoop()
    {
        (uint Pc, uint TailPc, int SourceRegister, string Name)[] cases =
        [
            (0x8016_F978, 0x8016_FA00, 5, "first-loop"),
            (0x8016_FBA8, 0x8016_FC30, 6, "second-loop"),
        ];

        foreach ((uint pc, uint tailPc, int sourceRegister, string name) in cases)
        {
            const uint source = 0x8010_8000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicHalfwordChecksumLoop(expectedBus.Memory);
            WriteSonicHalfwordChecksumLoop(actualBus.Memory);
            for (uint offset = 0; offset < 64; offset += sizeof(ushort))
            {
                ushort value = (ushort)(0x1020 + offset * 7);
                expectedBus.Memory.Write16(source + offset, value);
                actualBus.Memory.Write16(source + offset, value);
            }

            PowerPcState expectedState = CreateSonicHalfwordChecksumState(pc, source, sourceRegister, iterations: 3);
            PowerPcState actualState = CreateSonicHalfwordChecksumState(pc, source, sourceRegister, iterations: 3);

            new PowerPcInterpreter().Run(expectedState, expectedBus, 3 * 34);
            bool skipped = InvokeFastForwardSonicHalfwordChecksumLoop(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, name);
            Assert.Equal(3 * 34, skippedInstructions);
            Assert.Equal(tailPc, actualState.Pc);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Ctr, actualState.Ctr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, sourceRegister, 9, 10, 11 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicNullSlotScanFastForwardMatchesInterpreterPaths()
    {
        (uint[] SlotPointers, uint Count, bool Exhausts, int ExpectedSkippedSlots, string Name)[] cases =
        [
            ([0, 0, 0x8010_7000], 4, false, 2, "stops-before-non-null"),
            ([0x8010_7100, 0, 0x8010_7000], 4, false, 2, "stops-before-match-after-mismatch"),
            ([0, 0, 0], 3, true, 3, "exhausts"),
        ];

        foreach (var testCase in cases)
        {
            const uint table = 0x8010_6000;
            const uint returnPc = 0x8011_7000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicNullSlotScanLoop(expectedBus.Memory);
            WriteSonicNullSlotScanLoop(actualBus.Memory);
            SetupSonicNullSlotScanData(expectedBus.Memory, table, testCase.SlotPointers);
            SetupSonicNullSlotScanData(actualBus.Memory, table, testCase.SlotPointers);
            if (testCase.Name == "stops-before-match-after-mismatch")
            {
                expectedBus.Memory.Write32(0x8010_7100, 0x8123_4561);
                actualBus.Memory.Write32(0x8010_7100, 0x8123_4561);
            }

            PowerPcState expectedState = CreateSonicNullSlotScanState(table, returnPc, testCase.Count);
            PowerPcState actualState = CreateSonicNullSlotScanState(table, returnPc, testCase.Count);
            int expectedSkippedSlots = testCase.ExpectedSkippedSlots;
            uint expectedLoopCursor = table + (uint)expectedSkippedSlots * 0x18;

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            do
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }
            while ((testCase.Exhausts ? expectedState.Pc != returnPc : expectedState.Pc != 0x8011_6BBC || expectedState.Gpr[4] != expectedLoopCursor) && expectedInstructions < 128);

            Assert.Equal(testCase.Exhausts ? returnPc : 0x8011_6BBCu, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicNullSlotScanLoop(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Ctr, actualState.Ctr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 4, 5, 6, 13 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{testCase.Name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicPoolNullSlotScanFastForwardMatchesInterpreterPaths()
    {
        (uint[] SlotPointers, uint Count, bool Exhausts, int ExpectedSkippedSlots, string Name)[] cases =
        [
            ([0x8123_0000, 0x8123_1000, 0], 4, false, 2, "stops-before-free"),
            ([0x8123_0000, 0x8123_1000, 0x8123_2000], 3, true, 3, "exhausts"),
        ];

        foreach (var testCase in cases)
        {
            const uint table = 0x8010_8000;
            const uint output = 0x8010_9000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicPoolNullSlotScanLoop(expectedBus.Memory);
            WriteSonicPoolNullSlotScanLoop(actualBus.Memory);
            SetupSonicPoolNullSlotScanData(expectedBus.Memory, table, testCase.SlotPointers);
            SetupSonicPoolNullSlotScanData(actualBus.Memory, table, testCase.SlotPointers);
            PowerPcState expectedState = CreateSonicPoolNullSlotScanState(table, output, testCase.Count);
            PowerPcState actualState = CreateSonicPoolNullSlotScanState(table, output, testCase.Count);
            uint expectedCursor = table + (uint)testCase.ExpectedSkippedSlots * 0x18;

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            do
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }
            while ((testCase.Exhausts ? expectedState.Pc != 0x8011_6C3C : expectedState.Pc != 0x8011_6C18 || expectedState.Gpr[6] != expectedCursor) && expectedInstructions < 128);

            Assert.Equal(testCase.Exhausts ? 0x8011_6C3Cu : 0x8011_6C18u, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicPoolSlotScanLoop(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Ctr, actualState.Ctr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 5, 6, 13 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{testCase.Name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicPoolSentinelSlotScanFastForwardMatchesInterpreterPaths()
    {
        (uint[] Values, uint Count, bool Exhausts, int ExpectedSkippedSlots, string Name)[] cases =
        [
            ([0x1234_5678, 0x89AB_CDEF, 0xFFFF_FFFF], 4, false, 2, "stops-before-sentinel"),
            ([0x1234_5678, 0x89AB_CDEF], 2, true, 2, "exhausts"),
        ];

        foreach (var testCase in cases)
        {
            const uint table = 0x8010_A000;
            const uint output = 0x8010_B000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicPoolSentinelSlotScanLoop(expectedBus.Memory);
            WriteSonicPoolSentinelSlotScanLoop(actualBus.Memory);
            SetupSonicPoolSentinelSlotScanData(expectedBus.Memory, table, testCase.Values);
            SetupSonicPoolSentinelSlotScanData(actualBus.Memory, table, testCase.Values);
            PowerPcState expectedState = CreateSonicPoolSentinelSlotScanState(table, output, testCase.Count);
            PowerPcState actualState = CreateSonicPoolSentinelSlotScanState(table, output, testCase.Count);
            uint expectedCursor = table + (uint)testCase.ExpectedSkippedSlots * 0x28;

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            do
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }
            while ((testCase.Exhausts ? expectedState.Pc != 0x8011_6CAC : expectedState.Pc != 0x8011_6C8C || expectedState.Gpr[5] != expectedCursor) && expectedInstructions < 128);

            Assert.Equal(testCase.Exhausts ? 0x8011_6CACu : 0x8011_6C8Cu, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicPoolSlotScanLoop(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Ctr, actualState.Ctr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 4, 5, 13 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{testCase.Name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicTableKeyScanFastForwardMatchesInterpreterPaths()
    {
        (ushort[] Keys, uint Target, uint Count, bool Exhausts, int ExpectedMisses, string Name)[] cases =
        [
            ([3, 7, 12], 12, 4, false, 2, "stops-before-match"),
            ([3, 7, 12], 20, 3, true, 3, "exhausts"),
        ];

        foreach (var testCase in cases)
        {
            const uint tableHolder = 0x8010_C000;
            const uint table = 0x8010_D000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicTableKeyScanLoop(expectedBus.Memory);
            WriteSonicTableKeyScanLoop(actualBus.Memory);
            SetupSonicTableKeyScanData(expectedBus.Memory, tableHolder, table, testCase.Keys);
            SetupSonicTableKeyScanData(actualBus.Memory, tableHolder, table, testCase.Keys);
            PowerPcState expectedState = CreateSonicTableKeyScanState(tableHolder, testCase.Target, testCase.Count);
            PowerPcState actualState = CreateSonicTableKeyScanState(tableHolder, testCase.Target, testCase.Count);
            uint expectedOffset = (uint)testCase.ExpectedMisses * 12;

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            do
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }
            while ((testCase.Exhausts ? expectedState.Pc != 0x8011_90DC : expectedState.Pc != 0x8011_90B8 || expectedState.Gpr[4] != expectedOffset) && expectedInstructions < 128);

            Assert.Equal(testCase.Exhausts ? 0x8011_90DCu : 0x8011_90B8u, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicTableKeyScanLoop(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.Equal(expectedInstructions, skippedInstructions);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Ctr, actualState.Ctr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 4, 13, 28, 29, 31 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{testCase.Name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicModeRefreshDispatchFastForwardMatchesInterpreterPaths()
    {
        (uint Pc, uint R3, uint R0, uint R27, uint R28, uint Cr, uint ExpectedPc, int ExpectedInstructions, string Name)[] cases =
        [
            (0x8012_33E0, 0xCAFE_0003, 0xCAFE_0000, 0, 0xCAFE_001C, 0x1234_5678, 0x8012_340C, 3, "callback-null"),
            (0x8012_33E4, 0xCAFE_0003, 0, 0, 0xCAFE_001C, 0x1234_5678, 0x8012_340C, 2, "callback-compare-tail"),
            (0x8012_33E8, 0xCAFE_0003, 0, 0, 0xCAFE_001C, 0x2000_0000, 0x8012_340C, 1, "callback-branch-tail"),
            (0x8012_33EC, 0xCAFE_0003, 0xCAFE_0000, 7, 0xCAFE_001C, 0x1234_5678, 0x8012_340C, 5, "counter-check-branch"),
            (0x8012_33F0, 7, 0xCAFE_0000, 7, 0xCAFE_001C, 0x1234_5678, 0x8012_340C, 2, "counter-compare-tail"),
            (0x8012_33F4, 7, 0xCAFE_0000, 0, 0xCAFE_001C, 0x4000_0000, 0x8012_340C, 1, "counter-branch-tail"),
            (0x8012_3478, 0xCAFE_0003, 0xCAFE_0000, 0, 0xCAFE_001C, 0x1234_5678, 0x800F_135C, 3, "call-wrapper"),
            (0x8012_3484, 5, 0xCAFE_0000, 0, 0xCAFE_001C, 0x1234_5678, 0x8012_33E0, 3, "post-call-nonzero"),
            (0x8012_3484, 0, 0xCAFE_0000, 0, 0xCAFE_001C, 0x1234_5678, 0x8012_3490, 3, "post-call-zero"),
        ];

        foreach (var testCase in cases)
        {
            const uint smallDataBase = 0x803B_52C0;
            const uint objectPointerHolder = 0x8030_5000;
            const uint objectPointer = 0x8123_5000;
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicModeRefreshDispatch(expectedBus.Memory);
            WriteSonicModeRefreshDispatch(actualBus.Memory);
            SetupSonicModeRefreshDispatchData(expectedBus.Memory, smallDataBase, objectPointerHolder, objectPointer);
            SetupSonicModeRefreshDispatchData(actualBus.Memory, smallDataBase, objectPointerHolder, objectPointer);
            PowerPcState expectedState = CreateSonicModeRefreshDispatchState(testCase.Pc, smallDataBase, testCase.R3);
            PowerPcState actualState = CreateSonicModeRefreshDispatchState(testCase.Pc, smallDataBase, testCase.R3);
            expectedState.Gpr[0] = testCase.R0;
            expectedState.Gpr[27] = testCase.R27;
            expectedState.Gpr[28] = testCase.R28;
            expectedState.Cr = testCase.Cr;
            actualState.Gpr[0] = testCase.R0;
            actualState.Gpr[27] = testCase.R27;
            actualState.Gpr[28] = testCase.R28;
            actualState.Cr = testCase.Cr;

            new PowerPcInterpreter().Run(expectedState, expectedBus, testCase.ExpectedInstructions);
            bool skipped = InvokeFastForwardSonicModeRefreshDispatch(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.Equal(testCase.ExpectedInstructions, skippedInstructions);
            Assert.Equal(testCase.ExpectedPc, actualState.Pc);
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 3, 13, 27, 28 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{testCase.Name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }
        }
    }

    [Fact]
    public void SonicModeQueryFastForwardMatchesInterpreterPaths()
    {
        (uint BusyFlag, uint FallbackFlag, uint Pointer, uint Mode, uint Msr, string Name)[] cases =
        [
            (1, 0, 0, 0, 0x0000_8000, "busy"),
            (0, 1, 0, 0, 0x0000_0000, "fallback"),
            (0, 0, 0, 0, 0x0000_8000, "null-pointer"),
            (0, 0, 0x802B_B700, 0, 0x0000_0000, "sentinel"),
            (0, 0, 0x8010_2000, 3, 0x0000_8000, "mode-three"),
            (0, 0, 0x8010_2000, 5, 0x0000_9000, "mode-pass"),
        ];

        foreach (var testCase in cases)
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicModeQuery(expectedBus.Memory);
            WriteSonicModeQuery(actualBus.Memory);
            SetupSonicModeQueryData(expectedBus.Memory, testCase.BusyFlag, testCase.FallbackFlag, testCase.Pointer, testCase.Mode);
            SetupSonicModeQueryData(actualBus.Memory, testCase.BusyFlag, testCase.FallbackFlag, testCase.Pointer, testCase.Mode);
            PowerPcState expectedState = CreateSonicModeQueryState(testCase.Msr);
            PowerPcState actualState = CreateSonicModeQueryState(testCase.Msr);

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            uint returnPc = expectedState.Lr;
            while (expectedState.Pc != returnPc && expectedInstructions < 128)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(returnPc, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicModeQuery(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.True(
                expectedInstructions == skippedInstructions,
                $"{testCase.Name} instructions: expected {expectedInstructions}, actual {skippedInstructions}");
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.Msr, actualState.Msr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 1, 3, 4, 5, 13, 30, 31 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{testCase.Name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }

            uint stack = 0x817F_F000;
            Assert.Equal(expectedBus.Memory.Read32(stack + 4), actualBus.Memory.Read32(stack + 4));
            for (uint offset = 0; offset < 24; offset += sizeof(uint))
            {
                Assert.Equal(expectedBus.Memory.Read32(stack - 24 + offset), actualBus.Memory.Read32(stack - 24 + offset));
            }
        }
    }

    [Fact]
    public void SonicModeChildStatusPollFastForwardMatchesInterpreterPaths()
    {
        (uint[] Children, short[] Statuses, string Name)[] cases =
        [
            ([], [], "root-null"),
            ([0, 0, 0], [0, 0, 0], "children-null"),
            ([0x8010_2000, 0x8010_3000, 0x8010_4000], [0, 0, 0], "children-zero"),
            ([0x8010_2000, 0x8010_3000, 0x8010_4000], [0, 3, 0], "middle-nonzero"),
            ([0x8010_2000, 0, 0x8010_4000], [2, 0, -1], "mixed-nonzero"),
        ];

        foreach (var testCase in cases)
        {
            GameCubeBus expectedBus = new();
            GameCubeBus actualBus = new();
            WriteSonicModeChildStatusPoll(expectedBus.Memory);
            WriteSonicModeChildStatusPoll(actualBus.Memory);
            SetupSonicModeChildStatusPollData(expectedBus.Memory, testCase.Children, testCase.Statuses);
            SetupSonicModeChildStatusPollData(actualBus.Memory, testCase.Children, testCase.Statuses);
            PowerPcState expectedState = CreateSonicModeChildStatusPollState();
            PowerPcState actualState = CreateSonicModeChildStatusPollState();

            PowerPcInterpreter interpreter = new();
            int expectedInstructions = 0;
            uint returnPc = expectedState.Lr;
            while (expectedState.Pc != returnPc && expectedInstructions < 256)
            {
                interpreter.Step(expectedState, expectedBus);
                expectedInstructions++;
            }

            Assert.Equal(returnPc, expectedState.Pc);
            bool skipped = InvokeFastForwardSonicModeChildStatusPoll(actualState, actualBus, out int skippedInstructions);

            Assert.True(skipped, testCase.Name);
            Assert.True(
                expectedInstructions == skippedInstructions,
                $"{testCase.Name} instructions: expected {expectedInstructions}, actual {skippedInstructions}");
            Assert.Equal(expectedState.Pc, actualState.Pc);
            Assert.Equal(expectedState.Lr, actualState.Lr);
            Assert.Equal(expectedState.Cr, actualState.Cr);
            Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
            Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
            foreach (int register in new[] { 0, 1, 3, 13, 31 })
            {
                Assert.True(
                    expectedState.Gpr[register] == actualState.Gpr[register],
                    $"{testCase.Name} r{register}: expected 0x{expectedState.Gpr[register]:X8}, actual 0x{actualState.Gpr[register]:X8}");
            }

            foreach (uint child in testCase.Children.Where(child => child != 0))
            {
                for (uint offset = 0x60; offset <= 0x6C; offset += sizeof(uint))
                {
                    Assert.Equal(expectedBus.Memory.Read32(child + offset), actualBus.Memory.Read32(child + offset));
                }
            }
        }
    }

    [Fact]
    public void SonicGeneratedRangeScanFastForwardMatchesInterpreterNoHitLoop()
    {
        const uint pc = 0x80BC_BFBC;
        const uint table = 0x8110_0000;
        const uint stack = 0x817F_F000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicGeneratedRangeScanState(expectedBus, pc, table, stack, index: 2046, group: 0);
        PowerPcState actualState = CreateSonicGeneratedRangeScanState(actualBus, pc, table, stack, index: 2046, group: 0);
        WriteSonicGeneratedRangeScanLoop(expectedBus.Memory, pc);
        WriteSonicGeneratedRangeScanLoop(actualBus.Memory, pc);
        WriteSonicGeneratedRangeValue(expectedBus.Memory, table, index: 2046, stride: 56, group: 0, groupStride: 0, baseOffset: 0x0001_A800, value: 0);
        WriteSonicGeneratedRangeValue(expectedBus.Memory, table, index: 2047, stride: 56, group: 0, groupStride: 0, baseOffset: 0x0001_A800, value: 0);
        WriteSonicGeneratedRangeValue(actualBus.Memory, table, index: 2046, stride: 56, group: 0, groupStride: 0, baseOffset: 0x0001_A800, value: 0);
        WriteSonicGeneratedRangeValue(actualBus.Memory, table, index: 2047, stride: 56, group: 0, groupStride: 0, baseOffset: 0x0001_A800, value: 0);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 92);
        bool skipped = InvokeFastForwardSonicGeneratedRangeScan(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(92, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 3, 4, 5, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[0]), BitConverter.DoubleToInt64Bits(actualState.Fpr[0]));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[1]), BitConverter.DoubleToInt64Bits(actualState.Fpr[1]));
        Assert.Equal(expectedBus.Memory.Read32(stack + 0xC8), actualBus.Memory.Read32(stack + 0xC8));
        Assert.Equal(expectedBus.Memory.Read32(stack + 0xCC), actualBus.Memory.Read32(stack + 0xCC));
    }

    [Fact]
    public void SonicGeneratedModelPointerScanFastForwardMatchesInterpreterNoHitPath()
    {
        const uint pc = 0x80BC_B1EC;
        const uint tableContainer = 0x8125_FE60;
        const uint pointerTable = 0x8136_7AA8;
        const uint stack = 0x817F_F000;
        const uint inputPointer = 0x8134_F1BC;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicGeneratedModelPointerScanState(expectedBus, pc, tableContainer, pointerTable, stack, inputPointer);
        PowerPcState actualState = CreateSonicGeneratedModelPointerScanState(actualBus, pc, tableContainer, pointerTable, stack, inputPointer);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 214);
        bool skipped = InvokeFastForwardSonicGeneratedModelPointerScan(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(214, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Lr, actualState.Lr);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 1, 3, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        for (uint offset = 0; offset <= 0x20; offset += sizeof(uint))
        {
            Assert.Equal(expectedBus.Memory.Read32(stack - 24 + offset), actualBus.Memory.Read32(stack - 24 + offset));
        }
    }

    [Fact]
    public void SonicGeneratedTileRangeScanFastForwardMatchesInterpreterNoHitLoop()
    {
        const uint pc = 0x80BC_C0A4;
        const uint table = 0x8110_0000;
        const uint stack = 0x817F_F000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicGeneratedRangeScanState(expectedBus, pc, table, stack, index: 254, group: 2);
        PowerPcState actualState = CreateSonicGeneratedRangeScanState(actualBus, pc, table, stack, index: 254, group: 2);
        WriteSonicGeneratedTileRangeScanLoop(expectedBus.Memory, pc);
        WriteSonicGeneratedTileRangeScanLoop(actualBus.Memory, pc);
        WriteSonicGeneratedRangeValue(expectedBus.Memory, table, index: 254, stride: 68, group: 2, groupStride: 17408, baseOffset: 0x0002_6800, value: 0);
        WriteSonicGeneratedRangeValue(expectedBus.Memory, table, index: 255, stride: 68, group: 2, groupStride: 17408, baseOffset: 0x0002_6800, value: 0);
        WriteSonicGeneratedRangeValue(actualBus.Memory, table, index: 254, stride: 68, group: 2, groupStride: 17408, baseOffset: 0x0002_6800, value: 0);
        WriteSonicGeneratedRangeValue(actualBus.Memory, table, index: 255, stride: 68, group: 2, groupStride: 17408, baseOffset: 0x0002_6800, value: 0);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 100);
        bool skipped = InvokeFastForwardSonicGeneratedRangeScan(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(100, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 3, 4, 5, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }

        Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[0]), BitConverter.DoubleToInt64Bits(actualState.Fpr[0]));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expectedState.Fpr[1]), BitConverter.DoubleToInt64Bits(actualState.Fpr[1]));
        Assert.Equal(expectedBus.Memory.Read32(stack + 0xC8), actualBus.Memory.Read32(stack + 0xC8));
        Assert.Equal(expectedBus.Memory.Read32(stack + 0xCC), actualBus.Memory.Read32(stack + 0xCC));
    }

    [Fact]
    public void SonicGeneratedSlotMismatchScanFastForwardMatchesInterpreterBeforeHit()
    {
        const uint tableBase = 0x8130_0000;
        const uint groupBase = 0x8131_0000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicGeneratedSlotMismatchScanState(expectedBus, tableBase, groupBase, slot: 1, count: 5, target: 0x22);
        PowerPcState actualState = CreateSonicGeneratedSlotMismatchScanState(actualBus, tableBase, groupBase, slot: 1, count: 5, target: 0x22);
        WriteSonicGeneratedSlotMismatchScan(expectedBus.Memory);
        WriteSonicGeneratedSlotMismatchScan(actualBus.Memory);
        WriteSonicGeneratedSlotCompareValue(expectedBus.Memory, groupBase, slot: 1, value: 0x11);
        WriteSonicGeneratedSlotCompareValue(expectedBus.Memory, groupBase, slot: 2, value: 0x33);
        WriteSonicGeneratedSlotCompareValue(expectedBus.Memory, groupBase, slot: 3, value: 0x22);
        WriteSonicGeneratedSlotCompareValue(actualBus.Memory, groupBase, slot: 1, value: 0x11);
        WriteSonicGeneratedSlotCompareValue(actualBus.Memory, groupBase, slot: 2, value: 0x33);
        WriteSonicGeneratedSlotCompareValue(actualBus.Memory, groupBase, slot: 3, value: 0x22);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 38);
        bool skipped = InvokeFastForwardSonicGeneratedSlotMismatchScan(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(38, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 4, 5, 6, 26, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Fact]
    public void SonicGeneratedSlotMismatchScanFastForwardMatchesInterpreterAtExit()
    {
        const uint tableBase = 0x8130_0000;
        const uint groupBase = 0x8131_0000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicGeneratedSlotMismatchScanState(expectedBus, tableBase, groupBase, slot: 1, count: 3, target: 0x22);
        PowerPcState actualState = CreateSonicGeneratedSlotMismatchScanState(actualBus, tableBase, groupBase, slot: 1, count: 3, target: 0x22);
        WriteSonicGeneratedSlotMismatchScan(expectedBus.Memory);
        WriteSonicGeneratedSlotMismatchScan(actualBus.Memory);
        WriteSonicGeneratedSlotCompareValue(expectedBus.Memory, groupBase, slot: 1, value: 0x11);
        WriteSonicGeneratedSlotCompareValue(expectedBus.Memory, groupBase, slot: 2, value: 0x33);
        WriteSonicGeneratedSlotCompareValue(actualBus.Memory, groupBase, slot: 1, value: 0x11);
        WriteSonicGeneratedSlotCompareValue(actualBus.Memory, groupBase, slot: 2, value: 0x33);

        new PowerPcInterpreter().Run(expectedState, expectedBus, 38);
        bool skipped = InvokeFastForwardSonicGeneratedSlotMismatchScan(actualState, actualBus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(38, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 4, 5, 6, 26, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Fact]
    public void SonicStartCodeScanFastForwardMatchesInterpreterBeforeClassifier()
    {
        const uint input = 0x8134_0000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicStartCodeScanState(expectedBus, input, length: 8);
        PowerPcState actualState = CreateSonicStartCodeScanState(actualBus, input, length: 8);
        byte[] bytes = [0x22, 0x00, 0x00, 0x00, 0x01, 0xB3, 0x44, 0x55];
        expectedBus.Memory.Load(input, bytes);
        actualBus.Memory.Load(input, bytes);

        bool skipped = InvokeFastForwardSonicStartCodeScan(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(60, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 4, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Fact]
    public void SonicStartCodeScanFastForwardMatchesInterpreterAtEnd()
    {
        const uint input = 0x8134_0000;
        GameCubeBus expectedBus = new();
        GameCubeBus actualBus = new();
        PowerPcState expectedState = CreateSonicStartCodeScanState(expectedBus, input, length: 4);
        PowerPcState actualState = CreateSonicStartCodeScanState(actualBus, input, length: 4);
        byte[] bytes = [0x22, 0x00, 0x00, 0x02];
        expectedBus.Memory.Load(input, bytes);
        actualBus.Memory.Load(input, bytes);

        bool skipped = InvokeFastForwardSonicStartCodeScan(actualState, actualBus, out int skippedInstructions);
        new PowerPcInterpreter().Run(expectedState, expectedBus, skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(51, skippedInstructions);
        Assert.Equal(expectedState.Pc, actualState.Pc);
        Assert.Equal(expectedState.Cr, actualState.Cr);
        Assert.Equal(expectedState.TimeBase, actualState.TimeBase);
        Assert.Equal(expectedState.Spr[22], actualState.Spr[22]);
        foreach (int register in new[] { 0, 3, 4, 29, 30, 31 })
        {
            Assert.Equal(expectedState.Gpr[register], actualState.Gpr[register]);
        }
    }

    [Fact]
    public void SonicBitPlaneCropFastForwardCropsRowsInPlace()
    {
        const uint pc = 0x800E_1F14;
        const uint source = 0x800B_0000;
        GameCubeBus bus = new();
        WriteSonicBitPlaneCrop(bus.Memory, pc);
        bus.Memory.Write8(source + 0, 0xF0);
        bus.Memory.Write8(source + 1, 0x0F);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x800E_3000,
        };
        state.Gpr[3] = source;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicBitPlaneCrop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.True(skippedInstructions > 0);
        Assert.Equal(8u, state.Gpr[3]);
        Assert.Equal(state.Lr, state.Pc);
        Assert.Equal(0xFF, bus.Memory.Read8(source));
        Assert.Equal(0, bus.Memory.Read8(source + 1));
        Assert.Equal(0, bus.Memory.Read8(source + 2));
        Assert.Equal(0x4000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void SonicBitPlaneCropFastForwardReturnsFallbackWidthForEmptyRows()
    {
        const uint pc = 0x800E_1F14;
        const uint source = 0x800B_0000;
        GameCubeBus bus = new();
        WriteSonicBitPlaneCrop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x800E_3000,
        };
        state.Gpr[3] = source;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicBitPlaneCrop(state, bus, out _);

        Assert.True(skipped);
        Assert.Equal(18u, state.Gpr[3]);
        Assert.Equal(state.Lr, state.Pc);
        Assert.Equal(0x8000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void SonicByteTableLookupFastForwardReturnsTableByte()
    {
        const uint pc = 0x8010_BA44;
        const uint table = 0x8017_64B8;
        GameCubeBus bus = new();
        WriteSonicByteTableLookup(bus.Memory, pc, table);
        bus.Memory.Write8(table + 0x34, 0x9A);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8010_C000,
        };
        state.Gpr[3] = 0x1234;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicByteTableLookup(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(9, skippedInstructions);
        Assert.Equal(0x9Au, state.Gpr[3]);
        Assert.Equal(state.Lr, state.Pc);
        Assert.Equal(0x4000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void SonicByteTableLookupFastForwardReturnsMinusOneSentinel()
    {
        const uint pc = 0x8010_BA44;
        const uint table = 0x8017_64B8;
        GameCubeBus bus = new();
        WriteSonicByteTableLookup(bus.Memory, pc, table);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8010_C000,
        };
        state.Gpr[3] = 0xFFFF_FFFF;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicByteTableLookup(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(3, skippedInstructions);
        Assert.Equal(0xFFFF_FFFFu, state.Gpr[3]);
        Assert.Equal(state.Lr, state.Pc);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void SonicNormalizedStringScanFastForwardStopsAtFirstStringTerminator()
    {
        const uint pc = 0x800E_EF04;
        const uint table = 0x8017_64B8;
        const uint first = 0x800B_0000;
        const uint second = 0x800B_0100;
        GameCubeBus bus = new();
        WriteSonicNormalizedStringScan(bus.Memory, pc);
        WriteSonicByteTableLookup(bus.Memory, pc + 0x1CB40, table);
        WriteIdentityByteTable(bus.Memory, table);
        bus.Memory.Write8(table + 'B', (byte)'b');
        bus.Memory.Load(first, "abc\0"u8);
        bus.Memory.Load(second, "aBc/"u8);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[20] = first;
        state.Gpr[21] = second;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicNormalizedStringScan(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(96, skippedInstructions);
        Assert.Equal(pc + 0x3C, state.Pc);
        Assert.Equal(first + 3, state.Gpr[20]);
        Assert.Equal(second + 3, state.Gpr[21]);
        Assert.Equal(0u, state.Gpr[0]);
        Assert.Equal((uint)'c', state.Gpr[3]);
        Assert.Equal((uint)'c', state.Gpr[22]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void SonicNormalizedStringScanFastForwardStopsAtMappedMismatch()
    {
        const uint pc = 0x800E_EF04;
        const uint table = 0x8017_64B8;
        const uint first = 0x800B_0000;
        const uint second = 0x800B_0100;
        GameCubeBus bus = new();
        WriteSonicNormalizedStringScan(bus.Memory, pc);
        WriteSonicByteTableLookup(bus.Memory, pc + 0x1CB40, table);
        WriteIdentityByteTable(bus.Memory, table);
        bus.Memory.Load(first, "abc"u8);
        bus.Memory.Load(second, "aBc"u8);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[20] = first;
        state.Gpr[21] = second;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicNormalizedStringScan(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(64, skippedInstructions);
        Assert.Equal(pc + 0x28, state.Pc);
        Assert.Equal(first + 2, state.Gpr[20]);
        Assert.Equal(second + 2, state.Gpr[21]);
        Assert.Equal((uint)'B', state.Gpr[3]);
        Assert.Equal((uint)'b', state.Gpr[22]);
        Assert.Equal(0x8000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void SonicPathRecordScanFastForwardStopsBeforeMatchingCandidate()
    {
        const uint pc = 0x800E_EEC4;
        const uint table = 0x8017_64B8;
        const uint entryTable = 0x8018_0000;
        const uint nameTable = 0x8019_0000;
        const uint path = 0x801A_0000;
        GameCubeBus bus = new();
        WriteSonicPathRecordScan(bus.Memory, pc, table);
        WriteIdentityByteTable(bus.Memory, table);
        bus.Memory.Write32(0x803B_52C0u - 30068u, entryTable);
        bus.Memory.Write32(0x803B_52C0u - 30064u, nameTable);
        WriteSonicPathEntry(bus.Memory, entryTable, 0, 0, 0, 4);
        WriteSonicPathEntry(bus.Memory, entryTable, 1, 0x0000_0100, 0, 2);
        WriteSonicPathEntry(bus.Memory, entryTable, 2, 0x0000_0200, 0, 3);
        WriteSonicPathEntry(bus.Memory, entryTable, 3, 0x0000_0300, 0, 4);
        bus.Memory.Load(nameTable + 0x100, "apple\0"u8);
        bus.Memory.Load(nameTable + 0x200, "banana\0"u8);
        bus.Memory.Load(nameTable + 0x300, "target\0"u8);
        bus.Memory.Load(path, "target\0"u8);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[13] = 0x803B_52C0;
        state.Gpr[23] = path;
        state.Gpr[26] = 1;
        state.Gpr[27] = 6;
        state.Gpr[29] = 0;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicPathRecordScan(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(144, skippedInstructions);
        Assert.Equal(pc, state.Pc);
        Assert.Equal(3u, state.Gpr[26]);
        Assert.Equal(4u, state.Gpr[0]);
        Assert.Equal(entryTable, state.Gpr[3]);
        Assert.Equal(24u, state.Gpr[28]);
        Assert.Equal(0x8000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void SonicPathRecordScanFastForwardExitsWhenNoCandidateMatches()
    {
        const uint pc = 0x800E_EEC4;
        const uint table = 0x8017_64B8;
        const uint entryTable = 0x8018_0000;
        const uint nameTable = 0x8019_0000;
        const uint path = 0x801A_0000;
        GameCubeBus bus = new();
        WriteSonicPathRecordScan(bus.Memory, pc, table);
        WriteIdentityByteTable(bus.Memory, table);
        bus.Memory.Write32(0x803B_52C0u - 30068u, entryTable);
        bus.Memory.Write32(0x803B_52C0u - 30064u, nameTable);
        WriteSonicPathEntry(bus.Memory, entryTable, 0, 0, 0, 3);
        WriteSonicPathEntry(bus.Memory, entryTable, 1, 0x0000_0100, 0, 2);
        WriteSonicPathEntry(bus.Memory, entryTable, 2, 0x0000_0200, 0, 3);
        bus.Memory.Load(nameTable + 0x100, "apple\0"u8);
        bus.Memory.Load(nameTable + 0x200, "banana\0"u8);
        bus.Memory.Load(path, "target\0"u8);
        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[13] = 0x803B_52C0;
        state.Gpr[23] = path;
        state.Gpr[26] = 1;
        state.Gpr[27] = 6;
        state.Gpr[29] = 0;
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardSonicPathRecordScan(state, bus, out _);

        Assert.True(skipped);
        Assert.Equal(pc + 0xF4, state.Pc);
        Assert.Equal(3u, state.Gpr[26]);
        Assert.Equal(3u, state.Gpr[0]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void IdleFastForwardStopsAtDecrementerInterruptEdge()
    {
        PowerPcState state = new()
        {
            Pc = 0x801F_BEE8,
            Msr = 0x0000_8000,
        };
        state.Spr[22] = 3;

        GameCubeBus bus = CreateIdleFastForwardBus();

        bool skipped = InvokeFastForwardIdle(state, bus, out ulong skippedCycles);

        Assert.True(skipped);
        Assert.Equal(3ul, skippedCycles);
        Assert.Equal(0u, state.Spr[22]);
        Assert.Equal(3ul, state.TimeBase);
    }

    [Fact]
    public void IdleFastForwardLetsInterpreterCrossZeroDecrementer()
    {
        PowerPcState state = new()
        {
            Pc = 0x801F_BEE8,
            Msr = 0x0000_8000,
        };
        state.Spr[22] = 0;

        GameCubeBus bus = CreateIdleFastForwardBus();

        bool skipped = InvokeFastForwardIdle(state, bus, out ulong skippedCycles);

        Assert.False(skipped);
        Assert.Equal(0ul, skippedCycles);
        Assert.Equal(0u, state.Spr[22]);
    }

    [Fact]
    public void PikminHeapWaitFastForwardAdvancesHardwareTime()
    {
        const uint waitPc = 0x8004_66C0;
        const uint heapManager = 0x8039_8000;
        PowerPcState state = new()
        {
            Pc = waitPc,
            Msr = 0x0000_8000,
        };
        state.Gpr[3] = heapManager;
        state.Spr[22] = 1000;

        GameCubeBus bus = new();
        bus.Memory.Write32(waitPc, 0x8003_032C);
        bus.Memory.Write32(waitPc + 4, 0x2800_0000);
        bus.Memory.Write32(waitPc + 8, 0x4182_FFF8);
        bus.Memory.Write32(heapManager + 0x32C, 0);
        bus.Write16(0xCC00_2030, GameCubeBus.VideoInterruptEnable);

        bool skipped = InvokeFastForwardPikminHeapWait(state, bus, out ulong skippedCycles);

        Assert.True(skipped);
        Assert.Equal((ulong)GameCubeBus.VideoCyclesPerScanline, skippedCycles);
        Assert.Equal(waitPc, state.Pc);
        Assert.Equal(1000u - (uint)GameCubeBus.VideoCyclesPerScanline, state.Spr[22]);
        Assert.Equal((ulong)GameCubeBus.VideoCyclesPerScanline, state.TimeBase);
    }

    [Fact]
    public void MetroTrkEventLoopFastForwardContinuesDebugBuildWithoutDebugger()
    {
        const uint pc = 0x8011_2F5C;
        GameCubeBus bus = new();
        bus.Memory.Write32(pc - 0x20, 0x9421_FFE0);
        bus.Memory.Write32(pc - 0x1C, 0x7C08_02A6);
        bus.Memory.Write32(pc - 0x08, 0x3BC0_0000);
        bus.Memory.Write32(pc + 0x00, 0x3861_0008);
        bus.Memory.Write32(pc + 0x04, 0x4800_01F1);
        bus.Memory.Write32(pc + 0xB8, 0x2C1F_0000);
        bus.Memory.Write32(pc + 0xBC, 0x4182_FF44);
        bus.Memory.Write32(pc + 0xC0, 0x8001_0024);
        bus.Memory.Write32(pc + 0xCC, 0x7C08_03A6);

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Spr[22] = 0xFFFF_F000;

        bool skipped = InvokeFastForwardMetroTrkEventLoop(state, bus, out int skippedInstructions);

        Assert.True(skipped);
        Assert.Equal(48, skippedInstructions);
        Assert.Equal(pc + 0xC0, state.Pc);
        Assert.Equal(1u, state.Gpr[31]);
        Assert.Equal(0u, state.Gpr[30]);
        Assert.Equal(0x4000_0000u, state.Cr & 0xF000_0000);
    }

    private static GameCubeBus CreateIdleFastForwardBus()
    {
        GameCubeBus bus = new()
        {
            SmallDataBaseRegister = 0x8000_3000,
        };
        bus.Memory.Write32(0x8000_6230, 0);
        bus.Write16(0xCC00_2030, GameCubeBus.VideoInterruptEnable);
        return bus;
    }

    private static bool InvokeFastForwardIdle(PowerPcState state, GameCubeBus bus, out ulong skippedCycles)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardKnownIdleLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find idle fast-forward helper.");
        object?[] args = [state, bus, 0ul];
        bool result = (bool)method.Invoke(null, args)!;
        skippedCycles = (ulong)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardPikminHeapWait(PowerPcState state, GameCubeBus bus, out ulong skippedCycles)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardPikminHeapWaitLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Pikmin heap wait fast-forward helper.");
        object?[] args = [state, bus, 0ul];
        bool result = (bool)method.Invoke(null, args)!;
        skippedCycles = (ulong)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardMetroTrkEventLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardMetroTrkEventLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find MetroTRK event-loop fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeMatchesWriteWatchRange(RunDolOptions options, uint address, int width)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("MatchesWriteWatchRange", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find write-watch range matcher.");
        return (bool)method.Invoke(null, [options, address, width])!;
    }

    private static bool InvokeOverlapsAddressRange(uint start, ulong length, uint rangeStart, int rangeLength)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("OverlapsAddressRange", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find address range overlap helper.");
        return (bool)method.Invoke(null, [start, length, rangeStart, rangeLength])!;
    }

    private static bool InvokeFastForwardZeroStoreLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardZeroStoreLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find zero-store fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardWordFillLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardWordFillLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find word-fill fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardCtrDelayLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardCtrDelayLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find CTR delay-loop fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardCtrByteCopyLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardCtrByteCopyLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find CTR byte-copy fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardCtrSingleByteCopyLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardCtrSingleByteCopyLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find CTR single-byte copy fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardWordCopyLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardWordCopyLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find word-copy fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardCtrZeroStoreLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardCtrZeroStoreLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find CTR zero-store fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardByteCopyLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardByteCopyLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find byte-copy fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardNullTerminatedByteCopyLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardNullTerminatedByteCopyLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find null-terminated byte-copy fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardStringCompareRoutine(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardStringCompareRoutine", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find string-compare fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardAsciiCaseInsensitiveStringCompareLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardAsciiCaseInsensitiveStringCompareLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find ASCII case-insensitive string-compare fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardStringLengthRoutine(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardStringLengthRoutine", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find string-length fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardMemsetRoutine(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardMemsetRoutine", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find memset routine fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardCtrCacheBlockLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardCtrCacheBlockLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find CTR cache-block fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSmallLeafHelper(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSmallLeafHelper", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find small leaf helper fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeIsFastForwardLeafHelperEntry(GameCubeBus bus, uint pc)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("IsFastForwardLeafHelperEntry", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find fast-forward leaf profile filter helper.");
        return (bool)method.Invoke(null, [bus, pc])!;
    }

    private static bool InvokeFastForwardLongDivisionLeaf(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardLongDivisionLeaf", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find long division leaf fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardMemmoveRoutine(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardMemmoveRoutine", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find memmove fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardTextureSampleLeaf(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardTextureSampleLeaf", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find texture sample fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicPrsDecompress(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicPrsDecompress", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic PRS decompression fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicTrigTableInit(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicTrigTableInit", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic trig table fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicBitUnpackRows(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicBitUnpackRows", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic bit unpack fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicTickWaitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicTickWaitLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic tick wait fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicCallbackWaitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicCallbackWaitLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic callback wait fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicDotProductLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicDotProductLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic dot product fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicResourceTableLookup(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicResourceTableLookup", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic resource table lookup fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicResourceFlagWaitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicResourceFlagWaitLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic resource flag wait fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicDvdStatusWaitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicDvdStatusWaitLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic DVD status wait fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicInitTableLoopTail(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicInitTableLoopTail", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic init table loop tail fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicInitTableNullEntryLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicInitTableNullEntryLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic init table null-entry fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicRecordHeaderScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicRecordHeaderScanLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic record header scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicFlagRecordScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicFlagRecordScanLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic flag record scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicTaskSlotCallbackScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicTaskSlotCallbackScanLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic task slot callback scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicBitmaskDispatchScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicBitmaskDispatchScanLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic bitmask dispatch scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicInterruptStatusPrologue(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicInterruptStatusPrologue", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic interrupt status prologue fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicInterruptStatusPoll(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicInterruptStatusPoll", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic interrupt status poll fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicInterruptStatusTimerSetup(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicInterruptStatusTimerSetup", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic interrupt status timer setup fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicInterruptStatusTimestamp(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicInterruptStatusTimestamp", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic interrupt status timestamp fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicInterruptStatusCompare(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicInterruptStatusCompare", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic interrupt status compare fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicInterruptStatusTail(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicInterruptStatusTail", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic interrupt status tail fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicInterruptStatusQueryPrologue(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicInterruptStatusQueryPrologue", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic interrupt status query prologue fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicInterruptStatusQueryPostCall(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicInterruptStatusQueryPostCall", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic interrupt status query post-call fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicResourceFixupRecord(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicResourceFixupRecord", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic resource fixup fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxAttributeFlush(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxAttributeFlush", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX attribute flush fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicBitPlaneCrop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicBitPlaneCrop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic bit-plane crop fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicByteTableLookup(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicByteTableLookup", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic byte table lookup fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxVertexEmitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxVertexEmitLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX vertex emit fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxTexObjLoadNoCallback(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxTexObjLoadNoCallback", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX texture object load fast-forward helper.");
        object?[] args = [state, bus, null, 0, 0L, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[5]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxPackedStateSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxPackedStateSetter", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX packed state setter fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxCpStateSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxCpStateSetter", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX CP state setter fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxTevDefaultWrapper(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxTevDefaultWrapper", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX TEV default wrapper fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxTevColorEnvSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxTevColorEnvSetter", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX TEV color env setter fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxTevAlphaEnvSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxTevAlphaEnvSetter", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX TEV alpha env setter fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxTevColorOpSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxTevColorOpSetter", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX TEV color op setter fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxTevAlphaOpSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxTevAlphaOpSetter", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX TEV alpha op setter fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxDrawBegin(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxDrawBegin", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX draw begin fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static int RunUntilPc(PowerPcState state, GameCubeBus bus, uint pc, int maxInstructions)
    {
        PowerPcInterpreter interpreter = new();
        for (int instructions = 1; instructions <= maxInstructions; instructions++)
        {
            interpreter.Run(state, bus, 1);
            if (state.Pc == pc)
            {
                return instructions;
            }
        }

        throw new InvalidOperationException($"Interpreter did not reach 0x{pc:X8} within {maxInstructions} instruction(s).");
    }

    private static bool InvokeFastForwardSonicGxBeginDirect(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxBeginDirect", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX begin direct fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxVertexDescriptorSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxVertexDescriptorSetter", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX vertex descriptor setter fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxVertexAttributeFlush(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxVertexAttributeFlush", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX vertex attribute flush fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxIndexedStripDrawBegin(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxIndexedStripDrawBegin", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX indexed strip draw begin fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxIndexedStripBatch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxIndexedStripBatch", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX indexed strip batch fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxIndexedStripTail(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxIndexedStripTail", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX indexed strip tail fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxIndexedStripEpilogue(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxIndexedStripEpilogue", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX indexed strip epilogue fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxCommandListTerminal(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxCommandListTerminal", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX command-list terminal fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxCommandListFetch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxCommandListFetch", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX command-list fetch fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxCommandDispatch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxCommandDispatch", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX command dispatch fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGprSaveRestoreTail(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGprSaveRestoreTail", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GPR save/restore tail fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxAttributeStateSetter(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxAttributeStateSetter", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX attribute state setter fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicPairedTransform2d(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicPairedTransform2d", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic paired transform 2D fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxFloatStripEmitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxFloatStripEmitLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX float strip emit fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxFloatAttributeStripEmitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxFloatAttributeStripEmitLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX float/attribute strip emit fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicPairedTransform4d(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicPairedTransform4d", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic paired transform 4D fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicVectorBlendCopy(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicVectorBlendCopyLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic vector blend/copy fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicCoordinatePairFillLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicCoordinatePairFillLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic coordinate pair fill fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicBufferFillLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicBufferFillLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic buffer fill fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicModeStateUpdate(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicModeStateUpdate", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic mode state update fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicModeCoordinatorPrologue(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicModeCoordinatorPrologue", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic mode coordinator prologue fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicModeCoordinatorBody(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicModeCoordinatorBody", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic mode coordinator body fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicModeCoordinatorZeroTail(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicModeCoordinatorZeroTail", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic mode coordinator zero-tail fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicStatusQuery(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicStatusQuery", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic status query fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicStatusCallerLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicStatusCallerLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic status caller-loop fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicStatusCallerDispatch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicStatusCallerDispatch", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic status caller dispatch fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicTableByteBuildDispatch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicTableByteBuildDispatch", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic table byte-build dispatch fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicLineCopy(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicLineCopy", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic line-copy fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicLineSkip(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicLineSkip", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic line-skip fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicStringAppendScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicStringAppendScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic string-append scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicFreeBlockScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicFreeBlockScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic free-block scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicCacheStoreSweep(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicCacheStoreSweep", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic cache store sweep fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicStateZeroFill(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicStateZeroFill", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic state zero-fill fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicManagerSlotScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicManagerSlotScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic manager-slot scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicTaskEntryScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicTaskEntryScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic task-entry scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicObjectSlotScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicObjectSlotScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic object-slot scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicHalfwordChecksumLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicHalfwordChecksumLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic halfword checksum fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicNullSlotScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicNullSlotScanLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic null-slot scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicPoolSlotScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicPoolSlotScanLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic pool-slot scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicTableKeyScanLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicTableKeyScanLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic table-key scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicModeRefreshDispatch(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicModeRefreshDispatch", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic mode refresh dispatch fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicModeQuery(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicModeQuery", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic mode query fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicModeChildStatusPoll(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicModeChildStatusPoll", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic mode child status poll fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGeneratedRangeScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGeneratedRangeScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic generated range-scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGeneratedModelPointerScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGeneratedModelPointerScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic generated model pointer scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGeneratedSlotMismatchScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGeneratedSlotMismatchScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic generated slot mismatch scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicPairedTransform4dIndexedOutput(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicPairedTransform4dIndexedOutput", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic paired transform 4D indexed-output fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicGxFloatTexcoordStripEmitLoop(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicGxFloatTexcoordStripEmitLoop", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic GX float/texcoord strip emit fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicNormalizedStringScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicNormalizedStringScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic normalized string scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicStartCodeScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicStartCodeScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic start-code scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicPathRecordScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicPathRecordScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic path record scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static bool InvokeFastForwardSonicOverlayInactiveSlotScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicOverlayInactiveSlotScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic overlay inactive slot scan fast-forward helper.");
        object?[] args = [state, bus, 0];
        bool result = (bool)method.Invoke(null, args)!;
        skippedInstructions = (int)args[2]!;
        return result;
    }

    private static void WriteZeroStoreLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x90E3_0004);
        WriteInstruction(memory, pc + 0x04, 0x3400_FFFF);
        WriteInstruction(memory, pc + 0x08, 0x90E3_0008);
        WriteInstruction(memory, pc + 0x0C, 0x90E3_000C);
        WriteInstruction(memory, pc + 0x10, 0x90E3_0010);
        WriteInstruction(memory, pc + 0x14, 0x90E3_0014);
        WriteInstruction(memory, pc + 0x18, 0x90E3_0018);
        WriteInstruction(memory, pc + 0x1C, 0x90E3_001C);
        WriteInstruction(memory, pc + 0x20, 0x94E3_0020);
        WriteInstruction(memory, pc + 0x24, 0x4082_FFDC);
    }

    private static void WriteReverseWordFillLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x9087_FFFC);
        WriteInstruction(memory, pc + 0x04, 0x9087_FFF8);
        WriteInstruction(memory, pc + 0x08, 0x9087_FFF4);
        WriteInstruction(memory, pc + 0x0C, 0x9087_FFF0);
        WriteInstruction(memory, pc + 0x10, 0x9087_FFEC);
        WriteInstruction(memory, pc + 0x14, 0x9087_FFE8);
        WriteInstruction(memory, pc + 0x18, 0x9087_FFE4);
        WriteInstruction(memory, pc + 0x1C, 0x9087_FFE0);
        WriteInstruction(memory, pc + 0x20, 0x9087_FFDC);
        WriteInstruction(memory, pc + 0x24, 0x9087_FFD8);
        WriteInstruction(memory, pc + 0x28, 0x9087_FFD4);
        WriteInstruction(memory, pc + 0x2C, 0x9087_FFD0);
        WriteInstruction(memory, pc + 0x30, 0x9087_FFCC);
        WriteInstruction(memory, pc + 0x34, 0x9087_FFC8);
        WriteInstruction(memory, pc + 0x38, 0x9087_FFC4);
        WriteInstruction(memory, pc + 0x3C, 0x9487_FFC0);
        WriteInstruction(memory, pc + 0x40, 0x3400_FFFF);
        WriteInstruction(memory, pc + 0x44, 0x4082_FFBC);
    }

    private static void WriteSignedLongDivisionLeafSignature(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x000, 0x9421_FFF0);
        WriteInstruction(memory, pc + 0x004, 0x5469_0001);
        WriteInstruction(memory, pc + 0x014, 0x54A9_0001);
        WriteInstruction(memory, pc + 0x02C, 0x7C60_0034);
        WriteInstruction(memory, pc + 0x034, 0x7C89_0034);
        WriteInstruction(memory, pc + 0x0D4, 0x7C84_2114);
        WriteInstruction(memory, pc + 0x0FC, 0x4200_FFD8);
        WriteInstruction(memory, pc + 0x120, 0x7C63_0190);
        WriteInstruction(memory, pc + 0x130, 0x3821_0010);
        WriteInstruction(memory, pc + 0x134, 0x4E80_0020);
    }

    private static void WriteSonicSignedLongDivisionLeaf(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x000, 0x9421_FFF0);
        WriteInstruction(memory, pc + 0x004, 0x5469_0001);
        WriteInstruction(memory, pc + 0x008, 0x4182_000C);
        WriteInstruction(memory, pc + 0x00C, 0x2084_0000);
        WriteInstruction(memory, pc + 0x010, 0x7C63_0190);
        WriteInstruction(memory, pc + 0x014, 0x9121_0008);
        WriteInstruction(memory, pc + 0x018, 0x54A9_0001);
        WriteInstruction(memory, pc + 0x01C, 0x4182_000C);
        WriteInstruction(memory, pc + 0x020, 0x20C6_0000);
        WriteInstruction(memory, pc + 0x024, 0x7CA5_0190);
        WriteInstruction(memory, pc + 0x028, 0x9121_000C);
        WriteInstruction(memory, pc + 0x02C, 0x2C03_0000);
        WriteInstruction(memory, pc + 0x030, 0x7C60_0034);
        WriteInstruction(memory, pc + 0x034, 0x7C89_0034);
        WriteInstruction(memory, pc + 0x038, 0x4082_0008);
        WriteInstruction(memory, pc + 0x03C, 0x3809_0020);
        WriteInstruction(memory, pc + 0x040, 0x2C05_0000);
        WriteInstruction(memory, pc + 0x044, 0x7CA9_0034);
        WriteInstruction(memory, pc + 0x048, 0x7CCA_0034);
        WriteInstruction(memory, pc + 0x04C, 0x4082_0008);
        WriteInstruction(memory, pc + 0x050, 0x392A_0020);
        WriteInstruction(memory, pc + 0x054, 0x7C00_4800);
        WriteInstruction(memory, pc + 0x058, 0x2140_0040);
        WriteInstruction(memory, pc + 0x05C, 0x4181_00CC);
        WriteInstruction(memory, pc + 0x060, 0x3929_0001);
        WriteInstruction(memory, pc + 0x064, 0x2129_0040);
        WriteInstruction(memory, pc + 0x068, 0x7C00_4A14);
        WriteInstruction(memory, pc + 0x06C, 0x7D29_5050);
        WriteInstruction(memory, pc + 0x070, 0x7D29_03A6);
        WriteInstruction(memory, pc + 0x074, 0x2C09_0020);
        WriteInstruction(memory, pc + 0x078, 0x38E9_FFE0);
        WriteInstruction(memory, pc + 0x07C, 0x4180_0010);
        WriteInstruction(memory, pc + 0x080, 0x7C68_3C30);
        WriteInstruction(memory, pc + 0x084, 0x38E0_0000);
        WriteInstruction(memory, pc + 0x088, 0x4800_0018);
        WriteInstruction(memory, pc + 0x08C, 0x7C88_4C30);
        WriteInstruction(memory, pc + 0x090, 0x20E9_0020);
        WriteInstruction(memory, pc + 0x094, 0x7C67_3830);
        WriteInstruction(memory, pc + 0x098, 0x7D08_3B78);
        WriteInstruction(memory, pc + 0x09C, 0x7C67_4C30);
        WriteInstruction(memory, pc + 0x0A0, 0x2C00_0020);
        WriteInstruction(memory, pc + 0x0A4, 0x3120_FFE0);
        WriteInstruction(memory, pc + 0x0A8, 0x4180_0010);
        WriteInstruction(memory, pc + 0x0AC, 0x7C83_4830);
        WriteInstruction(memory, pc + 0x0B0, 0x3880_0000);
        WriteInstruction(memory, pc + 0x0B4, 0x4800_0018);
        WriteInstruction(memory, pc + 0x0B8, 0x7C63_0030);
        WriteInstruction(memory, pc + 0x0BC, 0x2120_0020);
        WriteInstruction(memory, pc + 0x0C0, 0x7C89_4C30);
        WriteInstruction(memory, pc + 0x0C4, 0x7C63_4B78);
        WriteInstruction(memory, pc + 0x0C8, 0x7C84_0030);
        WriteInstruction(memory, pc + 0x0CC, 0x3940_FFFF);
        WriteInstruction(memory, pc + 0x0D0, 0x30E7_0000);
        WriteInstruction(memory, pc + 0x0D4, 0x7C84_2114);
        WriteInstruction(memory, pc + 0x0D8, 0x7C63_1914);
        WriteInstruction(memory, pc + 0x0DC, 0x7D08_4114);
        WriteInstruction(memory, pc + 0x0E0, 0x7CE7_3914);
        WriteInstruction(memory, pc + 0x0E4, 0x7C06_4010);
        WriteInstruction(memory, pc + 0x0E8, 0x7D25_3911);
        WriteInstruction(memory, pc + 0x0EC, 0x4180_0010);
        WriteInstruction(memory, pc + 0x0F0, 0x7C08_0378);
        WriteInstruction(memory, pc + 0x0F4, 0x7D27_4B78);
        WriteInstruction(memory, pc + 0x0F8, 0x300A_0001);
        WriteInstruction(memory, pc + 0x0FC, 0x4200_FFD8);
        WriteInstruction(memory, pc + 0x100, 0x7C84_2114);
        WriteInstruction(memory, pc + 0x104, 0x7C63_1914);
        WriteInstruction(memory, pc + 0x108, 0x8121_0008);
        WriteInstruction(memory, pc + 0x10C, 0x8141_000C);
        WriteInstruction(memory, pc + 0x110, 0x7D27_5279);
        WriteInstruction(memory, pc + 0x114, 0x4182_0010);
        WriteInstruction(memory, pc + 0x118, 0x2C09_0000);
        WriteInstruction(memory, pc + 0x11C, 0x2084_0000);
        WriteInstruction(memory, pc + 0x120, 0x7C63_0190);
        WriteInstruction(memory, pc + 0x124, 0x4800_000C);
        WriteInstruction(memory, pc + 0x128, 0x3880_0000);
        WriteInstruction(memory, pc + 0x12C, 0x3860_0000);
        WriteInstruction(memory, pc + 0x130, 0x3821_0010);
        WriteInstruction(memory, pc + 0x134, 0x4E80_0020);
    }

    private static void WriteUnsignedLongDivisionLeafSignature(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x000, 0x2C03_0000);
        WriteInstruction(memory, pc + 0x004, 0x7C60_0034);
        WriteInstruction(memory, pc + 0x014, 0x7CCA_0034);
        WriteInstruction(memory, pc + 0x0A8, 0x7C84_2114);
        WriteInstruction(memory, pc + 0x0D4, 0x7D04_4378);
        WriteInstruction(memory, pc + 0x0D8, 0x7CE3_3B78);
        WriteInstruction(memory, pc + 0x0DC, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x0E0, 0x4E80_0020);
    }

    private static void WriteSignedLongDivisionLoopSignature(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C84_2114);
        WriteInstruction(memory, pc + 0x04, 0x7C63_1914);
        WriteInstruction(memory, pc + 0x08, 0x7D08_4114);
        WriteInstruction(memory, pc + 0x0C, 0x7CE7_3914);
        WriteInstruction(memory, pc + 0x10, 0x7C06_4010);
        WriteInstruction(memory, pc + 0x14, 0x7D25_3911);
        WriteInstruction(memory, pc + 0x28, 0x4200_FFD8);
        WriteInstruction(memory, pc + 0x2C, 0x7C84_2114);
        WriteInstruction(memory, pc + 0x30, 0x7C63_1914);
        WriteInstruction(memory, pc + 0x4C, 0x7C63_0190);
        WriteInstruction(memory, pc + 0x50, 0x4800_000C);
    }

    private static void SetLongOperand(PowerPcState state, int highRegister, ulong value)
    {
        state.Gpr[highRegister] = (uint)(value >> 32);
        state.Gpr[highRegister + 1] = (uint)value;
    }

    private static ulong GetLongOperand(PowerPcState state, int highRegister) =>
        ((ulong)state.Gpr[highRegister] << 32) | state.Gpr[highRegister + 1];

    private static void WriteCtrZeroStoreLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x9004_0000);
        WriteInstruction(memory, pc + 0x04, 0x38C6_0008);
        WriteInstruction(memory, pc + 0x08, 0x9004_0004);
        WriteInstruction(memory, pc + 0x0C, 0x9004_0008);
        WriteInstruction(memory, pc + 0x10, 0x9004_000C);
        WriteInstruction(memory, pc + 0x14, 0x9004_0010);
        WriteInstruction(memory, pc + 0x18, 0x9004_0014);
        WriteInstruction(memory, pc + 0x1C, 0x9004_0018);
        WriteInstruction(memory, pc + 0x20, 0x9004_001C);
        WriteInstruction(memory, pc + 0x24, 0x3884_0020);
        WriteInstruction(memory, pc + 0x28, 0x4200_FFD8);
    }

    private static void WriteByteCopyLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x8C04_0001);
        WriteInstruction(memory, pc + 0x04, 0x9C06_0001);
        WriteInstruction(memory, pc + 0x08, 0x34A5_FFFF);
        WriteInstruction(memory, pc + 0x0C, 0x4082_FFF4);
    }

    private static void WriteNullTerminatedByteCopyLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x8C04_0001);
        WriteInstruction(memory, pc + 0x04, 0x2800_0000);
        WriteInstruction(memory, pc + 0x08, 0x9C05_0001);
        WriteInstruction(memory, pc + 0x0C, 0x4082_FFF4);
        WriteInstruction(memory, pc + 0x10, 0x4E80_0020);
    }

    private static void WriteStringCompareRoutine(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x88C3_0000);
        WriteInstruction(memory, pc + 0x04, 0x88A4_0000);
        WriteInstruction(memory, pc + 0x08, 0x7C05_3051);
        WriteInstruction(memory, pc + 0x0C, 0x4182_000C);
        WriteInstruction(memory, pc + 0x10, 0x7C65_3050);
        WriteInstruction(memory, pc + 0x14, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x18, 0x5480_07BE);
        WriteInstruction(memory, pc + 0x1C, 0x5465_07BE);
        WriteInstruction(memory, pc + 0x20, 0x7C00_2840);
        WriteInstruction(memory, pc + 0x24, 0x4082_00C8);
        WriteInstruction(memory, pc + 0x28, 0x2805_0000);
        WriteInstruction(memory, pc + 0x2C, 0x4182_0058);
        WriteInstruction(memory, pc + 0x30, 0x2806_0000);
        WriteInstruction(memory, pc + 0x50, 0x8CA3_0001);
        WriteInstruction(memory, pc + 0x54, 0x8C04_0001);
        WriteInstruction(memory, pc + 0x84, 0x80E3_0000);
        WriteInstruction(memory, pc + 0x88, 0x80CD_2AE4);
        WriteInstruction(memory, pc + 0x8C, 0x80AD_2AE0);
        WriteInstruction(memory, pc + 0xA4, 0x84E3_0004);
        WriteInstruction(memory, pc + 0xA8, 0x8504_0004);
        WriteInstruction(memory, pc + 0xD4, 0x88C3_0000);
        WriteInstruction(memory, pc + 0xD8, 0x88A4_0000);
        WriteInstruction(memory, pc + 0xFC, 0x8CA3_0001);
        WriteInstruction(memory, pc + 0x100, 0x8C04_0001);
        WriteInstruction(memory, pc + 0x104, 0x7C00_2851);
        WriteInstruction(memory, pc + 0x114, 0x2805_0000);
        WriteInstruction(memory, pc + 0x118, 0x4082_FFE4);
        WriteInstruction(memory, pc + 0x11C, 0x3860_0000);
        WriteInstruction(memory, pc + 0x120, 0x4E80_0020);
    }

    private static void WriteAsciiCaseInsensitiveStringCompareLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x88C3_0000);
        WriteInstruction(memory, pc + 0x04, 0x3863_0001);
        WriteInstruction(memory, pc + 0x08, 0x88E4_0000);
        WriteInstruction(memory, pc + 0x0C, 0x3884_0001);
        WriteInstruction(memory, pc + 0x10, 0x7CC0_0774);
        WriteInstruction(memory, pc + 0x14, 0x2C00_0041);
        WriteInstruction(memory, pc + 0x18, 0x4180_0010);
        WriteInstruction(memory, pc + 0x1C, 0x2C00_005A);
        WriteInstruction(memory, pc + 0x20, 0x4181_0008);
        WriteInstruction(memory, pc + 0x24, 0x38C6_0020);
        WriteInstruction(memory, pc + 0x28, 0x7CE0_0774);
        WriteInstruction(memory, pc + 0x2C, 0x2C00_0041);
        WriteInstruction(memory, pc + 0x30, 0x4180_0010);
        WriteInstruction(memory, pc + 0x34, 0x2C00_005A);
        WriteInstruction(memory, pc + 0x38, 0x4181_0008);
        WriteInstruction(memory, pc + 0x3C, 0x38E7_0020);
        WriteInstruction(memory, pc + 0x40, 0x7CC5_0774);
        WriteInstruction(memory, pc + 0x44, 0x7CE0_0774);
        WriteInstruction(memory, pc + 0x48, 0x7C05_0000);
        WriteInstruction(memory, pc + 0x4C, 0x4082_0014);
        WriteInstruction(memory, pc + 0x50, 0x7CC0_0775);
        WriteInstruction(memory, pc + 0x54, 0x4082_FFAC);
        WriteInstruction(memory, pc + 0x58, 0x3860_0001);
        WriteInstruction(memory, pc + 0x5C, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x60, 0x3860_0000);
        WriteInstruction(memory, pc + 0x64, 0x4E80_0020);
    }

    private static void WriteStringLengthRoutine(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x9421_FFF0);
        WriteInstruction(memory, pc + 0x04, 0x93E1_000C);
        WriteInstruction(memory, pc + 0x08, 0x3BE0_FFFF);
        WriteInstruction(memory, pc + 0x0C, 0x93C1_0008);
        WriteInstruction(memory, pc + 0x10, 0x3BC3_FFFF);
        WriteInstruction(memory, pc + 0x14, 0x8C1E_0001);
        WriteInstruction(memory, pc + 0x18, 0x3BFF_0001);
        WriteInstruction(memory, pc + 0x1C, 0x2800_0000);
        WriteInstruction(memory, pc + 0x20, 0x4082_FFF4);
        WriteInstruction(memory, pc + 0x24, 0x7FE3_FB78);
        WriteInstruction(memory, pc + 0x28, 0x83E1_000C);
        WriteInstruction(memory, pc + 0x2C, 0x83C1_0008);
        WriteInstruction(memory, pc + 0x30, 0x3821_0010);
        WriteInstruction(memory, pc + 0x34, 0x4E80_0020);
    }

    private static void WriteVariadicRegisterSaveStub(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x9421_FF90);
        WriteInstruction(memory, pc + 0x04, 0x4086_0024);
        WriteInstruction(memory, pc + 0x08, 0xD821_0028);
        WriteInstruction(memory, pc + 0x0C, 0xD841_0030);
        WriteInstruction(memory, pc + 0x10, 0xD861_0038);
        WriteInstruction(memory, pc + 0x14, 0xD881_0040);
        WriteInstruction(memory, pc + 0x18, 0xD8A1_0048);
        WriteInstruction(memory, pc + 0x1C, 0xD8C1_0050);
        WriteInstruction(memory, pc + 0x20, 0xD8E1_0058);
        WriteInstruction(memory, pc + 0x24, 0xD901_0060);
        WriteInstruction(memory, pc + 0x28, 0x9061_0008);
        WriteInstruction(memory, pc + 0x2C, 0x9081_000C);
        WriteInstruction(memory, pc + 0x30, 0x90A1_0010);
        WriteInstruction(memory, pc + 0x34, 0x90C1_0014);
        WriteInstruction(memory, pc + 0x38, 0x90E1_0018);
        WriteInstruction(memory, pc + 0x3C, 0x9101_001C);
        WriteInstruction(memory, pc + 0x40, 0x9121_0020);
        WriteInstruction(memory, pc + 0x44, 0x9141_0024);
        WriteInstruction(memory, pc + 0x48, 0x3821_0070);
        WriteInstruction(memory, pc + 0x4C, 0x4E80_0020);
    }

    private static void WriteTimeBaseReadLeaf(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C6D_42E6);
        WriteInstruction(memory, pc + 0x04, 0x7C8C_42E6);
        WriteInstruction(memory, pc + 0x08, 0x7CAD_42E6);
        WriteInstruction(memory, pc + 0x0C, 0x7C03_2800);
        WriteInstruction(memory, pc + 0x10, 0x4082_FFF0);
        WriteInstruction(memory, pc + 0x14, 0x4E80_0020);
    }

    private static void WriteLowerTimeBaseReadLeaf(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C6C_42E6);
        WriteInstruction(memory, pc + 0x04, 0x4E80_0020);
    }

    private static void WriteCtrCacheBlockLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C00_1BAC);
        WriteInstruction(memory, pc + 0x04, 0x3863_0020);
        WriteInstruction(memory, pc + 0x08, 0x4200_FFF8);
    }

    private static void WriteFourShortStoreLeaf(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C80_0734);
        WriteInstruction(memory, pc + 0x04, 0xB003_0000);
        WriteInstruction(memory, pc + 0x08, 0x7CA0_0734);
        WriteInstruction(memory, pc + 0x0C, 0x7CC4_0734);
        WriteInstruction(memory, pc + 0x10, 0xB003_0002);
        WriteInstruction(memory, pc + 0x14, 0x7CE0_0734);
        WriteInstruction(memory, pc + 0x18, 0xB083_0004);
        WriteInstruction(memory, pc + 0x1C, 0xB003_0006);
        WriteInstruction(memory, pc + 0x20, 0x4E80_0020);
    }

    private static void WriteZeroThreeWordsLeaf(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x3800_0000);
        WriteInstruction(memory, pc + 0x04, 0x9003_0000);
        WriteInstruction(memory, pc + 0x08, 0x9003_0004);
        WriteInstruction(memory, pc + 0x0C, 0x9003_0008);
        WriteInstruction(memory, pc + 0x10, 0x4E80_0020);
    }

    private static void WritePointerNodeLeaf(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x3800_0000);
        WriteInstruction(memory, pc + 0x04, 0x9003_0004);
        WriteInstruction(memory, pc + 0x08, 0x9083_0000);
        WriteInstruction(memory, pc + 0x0C, 0x9003_0008);
        WriteInstruction(memory, pc + 0x10, 0x9003_000C);
        WriteInstruction(memory, pc + 0x14, 0x4E80_0020);
    }

    private static void WriteWordEqualsLeaf(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x8003_0000);
        WriteInstruction(memory, pc + 0x04, 0x7C00_2050);
        WriteInstruction(memory, pc + 0x08, 0x7C00_0034);
        WriteInstruction(memory, pc + 0x0C, 0x5403_D97E);
        WriteInstruction(memory, pc + 0x10, 0x4E80_0020);
    }

    private static void WriteDisableExternalInterruptLeaf(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C60_00A6);
        WriteInstruction(memory, pc + 0x04, 0x5464_045E);
        WriteInstruction(memory, pc + 0x08, 0x7C80_0124);
        WriteInstruction(memory, pc + 0x0C, 0x5463_8FFE);
        WriteInstruction(memory, pc + 0x10, 0x4E80_0020);
    }

    private static void WriteEnableExternalInterruptLeaf(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C60_00A6);
        WriteInstruction(memory, pc + 0x04, 0x6064_8000);
        WriteInstruction(memory, pc + 0x08, 0x7C80_0124);
        WriteInstruction(memory, pc + 0x0C, 0x5463_8FFE);
        WriteInstruction(memory, pc + 0x10, 0x4E80_0020);
    }

    private static void WriteRestoreExternalInterruptLeaf(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x2C03_0000);
        WriteInstruction(memory, pc + 0x04, 0x7C80_00A6);
        WriteInstruction(memory, pc + 0x08, 0x4182_000C);
        WriteInstruction(memory, pc + 0x0C, 0x6085_8000);
        WriteInstruction(memory, pc + 0x10, 0x4800_0008);
        WriteInstruction(memory, pc + 0x14, 0x5485_045E);
        WriteInstruction(memory, pc + 0x18, 0x7CA0_0124);
        WriteInstruction(memory, pc + 0x1C, 0x5484_8FFE);
        WriteInstruction(memory, pc + 0x20, 0x4E80_0020);
    }

    private static void WriteMemsetRoutine(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C08_02A6);
        WriteInstruction(memory, pc + 0x04, 0x9001_0004);
        WriteInstruction(memory, pc + 0x08, 0x9421_FFE0);
        WriteInstruction(memory, pc + 0x0C, 0x93E1_001C);
        WriteInstruction(memory, pc + 0x10, 0x7C7F_1B78);
        WriteInstruction(memory, pc + 0x14, 0x4800_001D);
        WriteInstruction(memory, pc + 0x18, 0x8001_0024);
        WriteInstruction(memory, pc + 0x1C, 0x7FE3_FB78);
        WriteInstruction(memory, pc + 0x20, 0x83E1_001C);
        WriteInstruction(memory, pc + 0x24, 0x3821_0020);
        WriteInstruction(memory, pc + 0x28, 0x7C08_03A6);
        WriteInstruction(memory, pc + 0x2C, 0x4E80_0020);
        WriteMemsetCore(memory, pc + 0x30);
    }

    private static void WriteMemsetCore(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x2805_0020);
        WriteInstruction(memory, pc + 0x04, 0x5480_063E);
        WriteInstruction(memory, pc + 0x08, 0x7C07_0378);
        WriteInstruction(memory, pc + 0x0C, 0x38C3_FFFF);
        WriteInstruction(memory, pc + 0x10, 0x4180_0098);
        WriteInstruction(memory, pc + 0x14, 0x7CC0_30F8);
        WriteInstruction(memory, pc + 0x18, 0x5400_07BF);
        WriteInstruction(memory, pc + 0x1C, 0x7C03_0378);
        WriteInstruction(memory, pc + 0x20, 0x4182_0018);
        WriteInstruction(memory, pc + 0x24, 0x7CA3_2850);
        WriteInstruction(memory, pc + 0x28, 0x54E0_063E);
        WriteInstruction(memory, pc + 0x2C, 0x3463_FFFF);
        WriteInstruction(memory, pc + 0x30, 0x9C06_0001);
        WriteInstruction(memory, pc + 0x34, 0x4082_FFF8);
        WriteInstruction(memory, pc + 0x38, 0x2807_0000);
        WriteInstruction(memory, pc + 0x3C, 0x4182_001C);
        WriteInstruction(memory, pc + 0x40, 0x54E3_C00E);
        WriteInstruction(memory, pc + 0x44, 0x54E0_801E);
        WriteInstruction(memory, pc + 0x48, 0x54E4_402E);
        WriteInstruction(memory, pc + 0x4C, 0x7C60_0378);
        WriteInstruction(memory, pc + 0x50, 0x7C80_0378);
        WriteInstruction(memory, pc + 0x54, 0x7CE7_0378);
        WriteInstruction(memory, pc + 0x58, 0x54A0_D97F);
        WriteInstruction(memory, pc + 0x5C, 0x3866_FFFD);
        WriteInstruction(memory, pc + 0x60, 0x4182_002C);
        WriteInstruction(memory, pc + 0x64, 0x90E3_0004);
        WriteInstruction(memory, pc + 0x68, 0x3400_FFFF);
        WriteInstruction(memory, pc + 0x6C, 0x90E3_0008);
        WriteInstruction(memory, pc + 0x70, 0x90E3_000C);
        WriteInstruction(memory, pc + 0x74, 0x90E3_0010);
        WriteInstruction(memory, pc + 0x78, 0x90E3_0014);
        WriteInstruction(memory, pc + 0x7C, 0x90E3_0018);
        WriteInstruction(memory, pc + 0x80, 0x90E3_001C);
        WriteInstruction(memory, pc + 0x84, 0x94E3_0020);
        WriteInstruction(memory, pc + 0x88, 0x4082_FFDC);
        WriteInstruction(memory, pc + 0x8C, 0x54A0_F77F);
        WriteInstruction(memory, pc + 0x90, 0x4182_0010);
        WriteInstruction(memory, pc + 0x94, 0x3400_FFFF);
        WriteInstruction(memory, pc + 0x98, 0x94E3_0004);
        WriteInstruction(memory, pc + 0x9C, 0x4082_FFF8);
        WriteInstruction(memory, pc + 0xA0, 0x38C3_0003);
        WriteInstruction(memory, pc + 0xA4, 0x54A5_07BE);
        WriteInstruction(memory, pc + 0xA8, 0x2805_0000);
        WriteInstruction(memory, pc + 0xAC, 0x4D82_0020);
        WriteInstruction(memory, pc + 0xB0, 0x54E0_063E);
        WriteInstruction(memory, pc + 0xB4, 0x34A5_FFFF);
        WriteInstruction(memory, pc + 0xB8, 0x9C06_0001);
        WriteInstruction(memory, pc + 0xBC, 0x4082_FFF8);
        WriteInstruction(memory, pc + 0xC0, 0x4E80_0020);
    }

    private static PowerPcState CreateMemsetRoutineState(uint pc, uint destination, uint value, uint count, uint stack)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_7000,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[3] = destination;
        state.Gpr[4] = value;
        state.Gpr[5] = count;
        state.Gpr[6] = 0xAAAA_0006;
        state.Gpr[7] = 0xBBBB_0007;
        state.Gpr[31] = 0xCCCC_001F;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static void WriteMemmoveRoutine(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C04_1840);
        WriteInstruction(memory, pc + 0x04, 0x4180_0028);
        WriteInstruction(memory, pc + 0x08, 0x3884_FFFF);
        WriteInstruction(memory, pc + 0x0C, 0x38C3_FFFF);
        WriteInstruction(memory, pc + 0x10, 0x38A5_0001);
        WriteInstruction(memory, pc + 0x14, 0x4800_000C);
        WriteInstruction(memory, pc + 0x18, 0x8C04_0001);
        WriteInstruction(memory, pc + 0x1C, 0x9C06_0001);
        WriteInstruction(memory, pc + 0x20, 0x34A5_FFFF);
        WriteInstruction(memory, pc + 0x24, 0x4082_FFF4);
        WriteInstruction(memory, pc + 0x28, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x2C, 0x7C84_2A14);
        WriteInstruction(memory, pc + 0x30, 0x7CC3_2A14);
        WriteInstruction(memory, pc + 0x34, 0x38A5_0001);
        WriteInstruction(memory, pc + 0x38, 0x4800_000C);
        WriteInstruction(memory, pc + 0x3C, 0x8C04_FFFF);
        WriteInstruction(memory, pc + 0x40, 0x9C06_FFFF);
        WriteInstruction(memory, pc + 0x44, 0x34A5_FFFF);
        WriteInstruction(memory, pc + 0x48, 0x4082_FFF4);
        WriteInstruction(memory, pc + 0x4C, 0x4E80_0020);
    }

    private static void WriteOptimizedMemmoveBackwardWordTail(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x8404_FFFC);
        WriteInstruction(memory, pc + 0x04, 0x3463_FFFF);
        WriteInstruction(memory, pc + 0x08, 0x9406_FFFC);
        WriteInstruction(memory, pc + 0x0C, 0x4082_FFF4);
        WriteInstruction(memory, pc + 0x10, 0x54A5_07BF);
        WriteInstruction(memory, pc + 0x14, 0x4D82_0020);
        WriteInstruction(memory, pc + 0x18, 0x8C04_FFFF);
        WriteInstruction(memory, pc + 0x1C, 0x34A5_FFFF);
        WriteInstruction(memory, pc + 0x20, 0x9C06_FFFF);
        WriteInstruction(memory, pc + 0x24, 0x4082_FFF4);
        WriteInstruction(memory, pc + 0x28, 0x4E80_0020);
    }

    private static PowerPcState CreateOptimizedMemmoveBackwardWordTailState(uint pc, uint sourceEnd, uint destinationEnd, uint wordCount, uint residualBytes)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8010_C1AC,
            Cr = 0x2200_0088,
            Xer = 0x0000_0000,
        };
        state.Gpr[0] = 0xAAAA_0000;
        state.Gpr[3] = wordCount;
        state.Gpr[4] = sourceEnd;
        state.Gpr[5] = 0x1200_0000 | residualBytes;
        state.Gpr[6] = destinationEnd;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static void WriteTextureSampleLeaf(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0xA003_0004);
        WriteInstruction(memory, pc + 0x04, 0x2C00_0005);
        WriteInstruction(memory, pc + 0x08, 0x4182_0008);
        WriteInstruction(memory, pc + 0x0C, 0x4800_005C);
        WriteInstruction(memory, pc + 0x10, 0x80E3_0010);
        WriteInstruction(memory, pc + 0x14, 0x8143_000C);
        WriteInstruction(memory, pc + 0x18, 0x7D05_3B96);
        WriteInstruction(memory, pc + 0x1C, 0xA003_0008);
        WriteInstruction(memory, pc + 0x20, 0x8063_0014);
        WriteInstruction(memory, pc + 0x24, 0x7D24_5396);
        WriteInstruction(memory, pc + 0x28, 0x7D6A_39D6);
        WriteInstruction(memory, pc + 0x2C, 0x7CC0_5396);
        WriteInstruction(memory, pc + 0x30, 0x7C08_39D6);
        WriteInstruction(memory, pc + 0x34, 0x7CE6_59D6);
        WriteInstruction(memory, pc + 0x38, 0x7CC9_51D6);
        WriteInstruction(memory, pc + 0x3C, 0x7C00_2850);
        WriteInstruction(memory, pc + 0x40, 0x7C0A_01D6);
        WriteInstruction(memory, pc + 0x44, 0x7C86_2050);
        WriteInstruction(memory, pc + 0x48, 0x7CA8_39D6);
        WriteInstruction(memory, pc + 0x4C, 0x7C84_0214);
        WriteInstruction(memory, pc + 0x50, 0x7C09_59D6);
        WriteInstruction(memory, pc + 0x54, 0x7C84_2A14);
        WriteInstruction(memory, pc + 0x58, 0x7C80_2214);
        WriteInstruction(memory, pc + 0x5C, 0x7C03_20AE);
        WriteInstruction(memory, pc + 0x60, 0x5403_0636);
        WriteInstruction(memory, pc + 0x64, 0x4E80_0020);
    }

    private static void WriteSonicPrsDecompressRoutine(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x000, 0x8903_0000);
        WriteInstruction(memory, pc + 0x004, 0x38E4_0000);
        WriteInstruction(memory, pc + 0x008, 0x38C3_0001);
        WriteInstruction(memory, pc + 0x00C, 0x3920_0009);
        WriteInstruction(memory, pc + 0x010, 0x3529_FFFF);
        WriteInstruction(memory, pc + 0x014, 0x4082_0010);
        WriteInstruction(memory, pc + 0x024, 0x5503_07FE);
        WriteInstruction(memory, pc + 0x028, 0x2803_0001);
        WriteInstruction(memory, pc + 0x030, 0x4082_0014);
        WriteInstruction(memory, pc + 0x048, 0x4182_FFC8);
        WriteInstruction(memory, pc + 0x084, 0x4082_000C);
        WriteInstruction(memory, pc + 0x088, 0x7C64_3850);
        WriteInstruction(memory, pc + 0x08C, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x120, 0x2803_0000);
        WriteInstruction(memory, pc + 0x124, 0x4182_FEEC);
        WriteInstruction(memory, pc + 0x1A0, 0x4BFF_FE70);
        WriteInstruction(memory, pc + 0x1A4, 0x4E80_0020);
    }

    private static void WriteSonicTrigTableInitLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc - 0x04, 0x4800_0048);
        WriteInstruction(memory, pc + 0x00, 0x6FA0_8000);
        WriteInstruction(memory, pc + 0x04, 0x9001_002C);
        WriteInstruction(memory, pc + 0x08, 0x93C1_0028);
        WriteInstruction(memory, pc + 0x0C, 0xC801_0028);
        WriteInstruction(memory, pc + 0x10, 0xEC00_F028);
        WriteInstruction(memory, pc + 0x14, 0xEC1D_0032);
        WriteInstruction(memory, pc + 0x18, 0xEF80_F824);
        WriteInstruction(memory, pc + 0x1C, 0xFC20_E090);
        WriteInstruction(memory, pc + 0x20, 0x4BFF_90D5);
        WriteInstruction(memory, pc + 0x24, 0xD03B_0000);
        WriteInstruction(memory, pc + 0x28, 0x3B7B_0004);
        WriteInstruction(memory, pc + 0x2C, 0xFC20_E090);
        WriteInstruction(memory, pc + 0x30, 0x4BFF_90A5);
        WriteInstruction(memory, pc + 0x34, 0xD03B_0000);
        WriteInstruction(memory, pc + 0x38, 0x3B7B_0004);
        WriteInstruction(memory, pc + 0x3C, 0x3BBD_0002);
        WriteInstruction(memory, pc + 0x40, 0x3B9C_0001);
        WriteInstruction(memory, pc + 0x44, 0x7C1C_F800);
        WriteInstruction(memory, pc + 0x48, 0x4180_FFB8);
    }

    private static void WriteSonicBitUnpackRows(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x38C3_0000);
        WriteInstruction(memory, pc + 0x04, 0x3900_0000);
        WriteInstruction(memory, pc + 0x08, 0x3800_0002);
        WriteInstruction(memory, pc + 0x0C, 0x7C09_03A6);
        WriteInstruction(memory, pc + 0x10, 0x3985_0000);
        WriteInstruction(memory, pc + 0x14, 0x3BE0_0000);
        WriteInstruction(memory, pc + 0x18, 0x3960_0000);
        WriteInstruction(memory, pc + 0x1C, 0x2C0C_0018);
        WriteInstruction(memory, pc + 0x20, 0x4080_0030);
        WriteInstruction(memory, pc + 0x40, 0x7C03_00AE);
        WriteInstruction(memory, pc + 0x4C, 0x7C00_5830);
        WriteInstruction(memory, pc + 0x58, 0x398C_0001);
        WriteInstruction(memory, pc + 0x134, 0x3908_0001);
        WriteInstruction(memory, pc + 0x138, 0x9BE6_0000);
        WriteInstruction(memory, pc + 0x13C, 0x2C08_0003);
        WriteInstruction(memory, pc + 0x140, 0x38A5_0008);
        WriteInstruction(memory, pc + 0x144, 0x38C6_0001);
        WriteInstruction(memory, pc + 0x148, 0x4180_FEC4);
        WriteInstruction(memory, pc + 0x14C, 0x38E7_0001);
        WriteInstruction(memory, pc + 0x150, 0x2C07_0018);
        WriteInstruction(memory, pc + 0x154, 0x3863_0003);
        WriteInstruction(memory, pc + 0x158, 0x4180_FEA8);
    }

    private static void WriteSonicBitUnpackByte(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x2C0C_0018);
        WriteInstruction(memory, pc + 0x04, 0x4080_0030);
        WriteInstruction(memory, pc + 0x20, 0x7C03_00AE);
        WriteInstruction(memory, pc + 0x2C, 0x7C00_5830);
        WriteInstruction(memory, pc + 0x38, 0x398C_0001);
        WriteInstruction(memory, pc + 0x110, 0x4200_FEF0);
        WriteInstruction(memory, pc + 0x114, 0x3908_0001);
        WriteInstruction(memory, pc + 0x118, 0x9BE6_0000);
        WriteInstruction(memory, pc + 0x120, 0x38A5_0008);
        WriteInstruction(memory, pc + 0x124, 0x38C6_0001);
        WriteInstruction(memory, pc + 0x128, 0x4180_FEC4);
    }

    private static void WriteSonicBitScanSetup(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x3800_0018);
        WriteInstruction(memory, pc + 0x04, 0x7C09_03A6);
        WriteInstruction(memory, pc + 0x08, 0x3880_0002);
        WriteInstruction(memory, pc + 0x0C, 0x7CA3_2214);
        WriteInstruction(memory, pc + 0x10, 0x88E5_0000);
        WriteInstruction(memory, pc + 0x14, 0x38C0_0000);
        WriteInstruction(memory, pc + 0x1C, 0x4082_0028);
        WriteInstruction(memory, pc + 0x4C, 0x38C6_0001);
        WriteInstruction(memory, pc + 0x5C, 0x7C06_5000);
        WriteInstruction(memory, pc + 0x68, 0x7CCA_3378);
        WriteInstruction(memory, pc + 0x70, 0x3884_0003);
        WriteInstruction(memory, pc + 0x74, 0x4200_FF98);
    }

    private static void WriteSonicBitScanRow(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7CA3_2214);
        WriteInstruction(memory, pc + 0x04, 0x88E5_0000);
        WriteInstruction(memory, pc + 0x08, 0x38C0_0000);
        WriteInstruction(memory, pc + 0x10, 0x4082_0028);
        WriteInstruction(memory, pc + 0x48, 0x38C6_0001);
        WriteInstruction(memory, pc + 0x58, 0x7C06_5000);
        WriteInstruction(memory, pc + 0x60, 0x7CCA_3378);
        WriteInstruction(memory, pc + 0x64, 0x3884_0003);
        WriteInstruction(memory, pc + 0x68, 0x4200_FF98);
    }

    private static void WriteSonicTickWaitLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x4BFD_B905);
        WriteInstruction(memory, pc + 0x04, 0x80AD_8DC0);
        WriteInstruction(memory, pc + 0x08, 0x808D_8DC4);
        WriteInstruction(memory, pc + 0x0C, 0x3804_FFFF);
        WriteInstruction(memory, pc + 0x10, 0x7C05_0214);
        WriteInstruction(memory, pc + 0x14, 0x7C03_0040);
        WriteInstruction(memory, pc + 0x18, 0x4081_FFE8);
        WriteInstruction(memory, pc - 0x246FC, 0x806D_8B00);
        WriteInstruction(memory, pc - 0x246F8, 0x4E80_0020);
    }

    private static void WriteSonicCallbackWaitLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x800D_85E8);
        WriteInstruction(memory, pc + 0x04, 0x2C00_0000);
        WriteInstruction(memory, pc + 0x08, 0x4181_FFF8);
        WriteInstruction(memory, pc + 0x0C, 0x800D_85F0);
        WriteInstruction(memory, pc + 0x10, 0x900D_85E8);
        WriteInstruction(memory, pc + 0x14, 0x4E80_0020);
    }

    private static void WriteSonicDotProductLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc - 0x08, 0x38C3_0000);
        WriteInstruction(memory, pc - 0x04, 0x3800_0040);
        WriteInstruction(memory, pc + 0x00, 0xC064_0000);
        WriteInstruction(memory, pc + 0x04, 0x3866_0004);
        WriteInstruction(memory, pc + 0x08, 0xC002_AB68);
        WriteInstruction(memory, pc + 0x10, 0xC086_0000);
        WriteInstruction(memory, pc + 0x0EC, 0xC024_004C);
        WriteInstruction(memory, pc + 0x0F0, 0xC046_004C);
        WriteInstruction(memory, pc + 0x0F4, 0x3884_0050);
        WriteInstruction(memory, pc + 0x0F8, 0xEC04_00FA);
        WriteInstruction(memory, pc + 0x0FC, 0xEC02_007A);
        WriteInstruction(memory, pc + 0x100, 0xC083_0000);
        WriteInstruction(memory, pc + 0x104, 0x3400_FFFF);
        WriteInstruction(memory, pc + 0x190, 0xEC04_00FA);
        WriteInstruction(memory, pc + 0x194, 0xEC02_007A);
        WriteInstruction(memory, pc + 0x198, 0xD005_0000);
        WriteInstruction(memory, pc + 0x19C, 0x38A5_0004);
        WriteInstruction(memory, pc + 0x1A0, 0x4082_FE60);
        WriteInstruction(memory, pc + 0x1A4, 0x4E80_0020);
    }

    private static void WriteSonicResourceTableLookup(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x38C0_0000);
        WriteInstruction(memory, pc + 0x04, 0x808D_8DF8);
        WriteInstruction(memory, pc + 0x08, 0x800D_8DF4);
        WriteInstruction(memory, pc + 0x10, 0x7C09_03A6);
        WriteInstruction(memory, pc + 0x14, 0x2C00_0000);
        WriteInstruction(memory, pc + 0x20, 0x80A4_000C);
        WriteInstruction(memory, pc + 0x24, 0x2805_0000);
        WriteInstruction(memory, pc + 0x2C, 0x8005_0000);
        WriteInstruction(memory, pc + 0x30, 0x7C03_0040);
        WriteInstruction(memory, pc + 0x38, 0x7CC3_3378);
        WriteInstruction(memory, pc + 0x40, 0x3884_0018);
        WriteInstruction(memory, pc + 0x44, 0x38C6_0001);
        WriteInstruction(memory, pc + 0x48, 0x4200_FFD8);
        WriteInstruction(memory, pc + 0x4C, 0x3860_FFFF);
        WriteInstruction(memory, pc + 0x50, 0x4E80_0020);
    }

    private static void WriteSonicResourceFlagWaitLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_BEA0, 0x800D_8A20);
        WriteInstruction(memory, 0x800E_BEA4, 0x2800_0000);
        WriteInstruction(memory, 0x800E_BEA8, 0x4182_FFF8);
    }

    private static void WriteSonicDvdStatusWaitLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800D_B9AC, 0x387B_0000);
        WriteInstruction(memory, 0x800D_B9B0, 0x3881_0084);
        WriteInstruction(memory, 0x800D_B9B4, 0x38A1_0080);
        WriteInstruction(memory, 0x800D_B9B8, 0x4809_4A7D);
        WriteInstruction(memory, 0x800D_B9BC, 0x2C03_FFFF);
        WriteInstruction(memory, 0x800D_B9C0, 0x4182_FFEC);
    }

    private static void WriteSonicInitTableLoopTail(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8006_90D4, 0x3B5A_0030);
        WriteInstruction(memory, 0x8006_90D8, 0x3B9C_0004);
        WriteInstruction(memory, 0x8006_90DC, 0x3B39_0004);
        WriteInstruction(memory, 0x8006_90E0, 0x3BBD_0001);
        WriteInstruction(memory, 0x8006_90E4, 0x7FA0_0774);
        WriteInstruction(memory, 0x8006_90E8, 0x2C00_002B);
        WriteInstruction(memory, 0x8006_90EC, 0x4180_F078);
    }

    private static void WriteSonicInitTableNullEntryLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8006_8164, 0x807F_0014);
        WriteInstruction(memory, 0x8006_8168, 0xA2BF_0008);
        WriteInstruction(memory, 0x8006_816C, 0x2C03_FFFF);
        WriteInstruction(memory, 0x8006_8170, 0x4082_000C);
        WriteInstruction(memory, 0x8006_8174, 0x3BFF_0030);
        WriteInstruction(memory, 0x8006_8178, 0x4800_0F5C);
        WriteSonicInitTableLoopTail(memory);
    }

    private static void WriteSonicRecordHeaderScanLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8013_2AD8, 0x881F_0000);
        WriteInstruction(memory, 0x8013_2ADC, 0x387F_0000);
        WriteInstruction(memory, 0x8013_2AE0, 0x2C00_0001);
        WriteInstruction(memory, 0x8013_2AE4, 0x4082_0014);
        WriteInstruction(memory, 0x8013_2AE8, 0x881F_0001);
        WriteInstruction(memory, 0x8013_2AEC, 0x2C00_0002);
        WriteInstruction(memory, 0x8013_2AF0, 0x4082_0008);
        WriteInstruction(memory, 0x8013_2AF4, 0x4800_002D);
        WriteInstruction(memory, 0x8013_2AF8, 0x3BDE_0001);
        WriteInstruction(memory, 0x8013_2AFC, 0x2C1E_0028);
        WriteInstruction(memory, 0x8013_2B00, 0x3BFF_0040);
        WriteInstruction(memory, 0x8013_2B04, 0x4180_FFD4);
    }

    private static void WriteSonicFlagRecordScanLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8013_7E58, 0x881F_0000);
        WriteInstruction(memory, 0x8013_7E5C, 0x2C00_0001);
        WriteInstruction(memory, 0x8013_7E60, 0x4082_000C);
        WriteInstruction(memory, 0x8013_7E64, 0x7FE3_FB78);
        WriteInstruction(memory, 0x8013_7E68, 0x4800_0031);
        WriteInstruction(memory, 0x8013_7E6C, 0x3BDE_0001);
        WriteInstruction(memory, 0x8013_7E70, 0x2C1E_0028);
        WriteInstruction(memory, 0x8013_7E74, 0x3BFF_0064);
        WriteInstruction(memory, 0x8013_7E78, 0x4180_FFE0);
    }

    private static void WriteSonicTaskSlotCallbackScanLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8012_5DEC, 0x807F_0000);
        WriteInstruction(memory, 0x8012_5DF0, 0x2803_0000);
        WriteInstruction(memory, 0x8012_5DF4, 0x4182_0018);
        WriteInstruction(memory, 0x8012_5DF8, 0x8183_0000);
        WriteInstruction(memory, 0x8012_5DFC, 0x280C_0000);
        WriteInstruction(memory, 0x8012_5E00, 0x4182_000C);
        WriteInstruction(memory, 0x8012_5E04, 0x7D88_03A6);
        WriteInstruction(memory, 0x8012_5E08, 0x4E80_0021);
        WriteInstruction(memory, 0x8012_5E0C, 0x3BDE_0001);
        WriteInstruction(memory, 0x8012_5E10, 0x2C1E_0020);
        WriteInstruction(memory, 0x8012_5E14, 0x3BFF_0010);
        WriteInstruction(memory, 0x8012_5E18, 0x4180_FFD4);
    }

    private static void WriteSonicBitmaskDispatchScanLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_8024, 0x8003_0000);
        WriteInstruction(memory, 0x800E_8028, 0x7C80_0038);
        WriteInstruction(memory, 0x800E_802C, 0x2800_0000);
        WriteInstruction(memory, 0x800E_8030, 0x4182_0010);
        WriteInstruction(memory, 0x800E_8034, 0x7C00_0034);
        WriteInstruction(memory, 0x800E_8038, 0x7C1D_0734);
        WriteInstruction(memory, 0x800E_803C, 0x4800_000C);
        WriteInstruction(memory, 0x800E_8040, 0x3863_0004);
        WriteInstruction(memory, 0x800E_8044, 0x4BFF_FFE0);
    }

    private static void WriteSonicInterruptStatusPrologue(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_6760, 0x7C08_02A6);
        WriteInstruction(memory, 0x800E_6764, 0x9001_0004);
        WriteInstruction(memory, 0x800E_6768, 0x9421_FFD8);
        WriteInstruction(memory, 0x800E_676C, 0xBF61_0014);
        WriteInstruction(memory, 0x800E_6770, 0x3B83_0000);
        WriteInstruction(memory, 0x800E_6774, 0x3C60_802C);
        WriteInstruction(memory, 0x800E_6778, 0x2C1C_0002);
        WriteInstruction(memory, 0x800E_677C, 0x5784_3032);
        WriteInstruction(memory, 0x800E_6780, 0x3803_A880);
        WriteInstruction(memory, 0x800E_6784, 0x7FE0_2214);
        WriteInstruction(memory, 0x800E_6788, 0x4082_000C);
        WriteInstruction(memory, 0x800E_6794, 0x3BA0_0001);
        WriteInstruction(memory, 0x800E_6798, 0x4800_1115);
        WriteInstruction(memory, 0x800E_679C, 0x1CBC_0014);
        WriteInstruction(memory, 0x800E_67A0, 0x801F_000C);
        WriteInstruction(memory, 0x800E_67A4, 0x3C80_CC00);
        WriteInstruction(memory, 0x800E_67A8, 0x38C4_6800);
        WriteDisableExternalInterruptLeaf(memory, 0x800E_78AC);
    }

    private static void WriteSonicInterruptStatusPoll(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_67AC, 0x7CC6_2A14);
        WriteInstruction(memory, 0x800E_67B0, 0x5400_0739);
        WriteInstruction(memory, 0x800E_67B4, 0x80E6_0000);
        WriteInstruction(memory, 0x800E_67B8, 0x7C7E_1B78);
        WriteInstruction(memory, 0x800E_67BC, 0x4082_00CC);
        WriteInstruction(memory, 0x800E_67C0, 0x54E0_0529);
        WriteInstruction(memory, 0x800E_67C4, 0x4182_002C);
    }

    private static void WriteSonicInterruptStatusTimerSetup(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_67F0, 0x54E0_04E7);
        WriteInstruction(memory, 0x800E_67F4, 0x4182_0074);
        WriteInstruction(memory, 0x800E_67F8, 0x3FE0_8000);
        WriteInstruction(memory, 0x800E_67FC, 0x801F_00F8);
        WriteInstruction(memory, 0x800E_6800, 0x3C60_1062);
        WriteInstruction(memory, 0x800E_6804, 0x3863_4DD3);
        WriteInstruction(memory, 0x800E_6808, 0x5400_F0BE);
        WriteInstruction(memory, 0x800E_680C, 0x7C03_0016);
        WriteInstruction(memory, 0x800E_6810, 0x541B_D1BE);
    }

    private static void WriteSonicInterruptStatusTimestamp(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_6814, 0x4800_6335);
        WriteInstruction(memory, 0x800E_6818, 0x38DB_0000);
        WriteInstruction(memory, 0x800E_681C, 0x38A0_0000);
        WriteInstruction(memory, 0x800E_6820, 0x4802_48FD);
        WriteInstruction(memory, 0x800E_6824, 0x38A0_0000);
        WriteInstruction(memory, 0x800E_6828, 0x38C0_0064);
        WriteInstruction(memory, 0x800E_682C, 0x4802_48F1);
        WriteInstruction(memory, 0x800E_6830, 0x5780_103A);
        WriteInstruction(memory, 0x800E_6834, 0x387F_30C0);
        WriteInstruction(memory, 0x800E_6838, 0x7C63_0214);
        WriteInstruction(memory, 0x800E_683C, 0x8003_0000);
        WriteInstruction(memory, 0x800E_6840, 0x3884_0001);
        WriteTimeBaseReadLeaf(memory, 0x800E_CB48);
        WriteSonicSignedLongDivisionLeaf(memory, 0x8010_B11C);
    }

    private static void WriteSonicInterruptStatusCompare(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_6844, 0x2C00_0000);
        WriteInstruction(memory, 0x800E_6848, 0x4082_0008);
        WriteInstruction(memory, 0x800E_684C, 0x9083_0000);
        WriteInstruction(memory, 0x800E_6850, 0x8003_0000);
        WriteInstruction(memory, 0x800E_6854, 0x7C00_2050);
        WriteInstruction(memory, 0x800E_6858, 0x2C00_0003);
        WriteInstruction(memory, 0x800E_685C, 0x4080_0058);
        WriteInstruction(memory, 0x800E_6860, 0x3BA0_0000);
        WriteInstruction(memory, 0x800E_6864, 0x4800_0050);
    }

    private static void WriteSonicInterruptStatusTail(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_68B4, 0x7FC3_F378);
        WriteInstruction(memory, 0x800E_68B8, 0x4800_101D);
        WriteInstruction(memory, 0x800E_68BC, 0x7FA3_EB78);
        WriteInstruction(memory, 0x800E_68C0, 0xBB61_0014);
        WriteInstruction(memory, 0x800E_68C4, 0x8001_002C);
        WriteInstruction(memory, 0x800E_68C8, 0x3821_0028);
        WriteInstruction(memory, 0x800E_68CC, 0x7C08_03A6);
        WriteInstruction(memory, 0x800E_68D0, 0x4E80_0020);
        WriteRestoreExternalInterruptLeaf(memory, 0x800E_78D4);
    }

    private static void WriteSonicInterruptStatusQueryPrologue(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_6954, 0x7C08_02A6);
        WriteInstruction(memory, 0x800E_6958, 0x9001_0004);
        WriteInstruction(memory, 0x800E_695C, 0x9421_FFE8);
        WriteInstruction(memory, 0x800E_6960, 0x93E1_0014);
        WriteInstruction(memory, 0x800E_6964, 0x93C1_0010);
        WriteInstruction(memory, 0x800E_6968, 0x3BC3_0000);
        WriteInstruction(memory, 0x800E_696C, 0x3C60_802C);
        WriteInstruction(memory, 0x800E_6970, 0x3803_A880);
        WriteInstruction(memory, 0x800E_6974, 0x57C4_3032);
        WriteInstruction(memory, 0x800E_6978, 0x387E_0000);
        WriteInstruction(memory, 0x800E_697C, 0x7FE0_2214);
        WriteInstruction(memory, 0x800E_6980, 0x4BFF_FDE1);
    }

    private static void WriteSonicInterruptStatusQueryPostCall(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_6984, 0x2C03_0000);
        WriteInstruction(memory, 0x800E_6988, 0x4182_0034);
        WriteInstruction(memory, 0x800E_698C, 0x801F_0020);
        WriteInstruction(memory, 0x800E_6990, 0x2C00_0000);
        WriteInstruction(memory, 0x800E_6994, 0x4082_0028);
        WriteInstruction(memory, 0x800E_6998, 0x387E_0000);
        WriteInstruction(memory, 0x800E_699C, 0x38A1_000C);
        WriteInstruction(memory, 0x800E_69A0, 0x3880_0000);
        WriteInstruction(memory, 0x800E_69A4, 0x4800_0B35);
        WriteInstruction(memory, 0x800E_69A8, 0x2C03_0000);
        WriteInstruction(memory, 0x800E_69AC, 0x4182_000C);
        WriteInstruction(memory, 0x800E_69B0, 0x3860_0001);
        WriteInstruction(memory, 0x800E_69B4, 0x4800_0008);
        WriteInstruction(memory, 0x800E_69B8, 0x3860_0000);
        WriteInstruction(memory, 0x800E_69BC, 0x2C03_0000);
        WriteInstruction(memory, 0x800E_69C0, 0x4182_000C);
        WriteInstruction(memory, 0x800E_69C4, 0x3860_0001);
        WriteInstruction(memory, 0x800E_69C8, 0x4800_0028);
        WriteInstruction(memory, 0x800E_69CC, 0x3C60_8000);
        WriteInstruction(memory, 0x800E_69D0, 0x57C0_103A);
        WriteInstruction(memory, 0x800E_69D4, 0x3863_30C0);
        WriteInstruction(memory, 0x800E_69D8, 0x7C03_002E);
        WriteInstruction(memory, 0x800E_69DC, 0x2C00_0000);
        WriteInstruction(memory, 0x800E_69E0, 0x4182_000C);
        WriteInstruction(memory, 0x800E_69E4, 0x3860_0000);
        WriteInstruction(memory, 0x800E_69E8, 0x4800_0008);
        WriteInstruction(memory, 0x800E_69EC, 0x3860_FFFF);
        WriteInstruction(memory, 0x800E_69F0, 0x8001_001C);
        WriteInstruction(memory, 0x800E_69F4, 0x83E1_0014);
        WriteInstruction(memory, 0x800E_69F8, 0x83C1_0010);
        WriteInstruction(memory, 0x800E_69FC, 0x7C08_03A6);
        WriteInstruction(memory, 0x800E_6A00, 0x3821_0018);
        WriteInstruction(memory, 0x800E_6A04, 0x4E80_0020);
    }

    private static void WriteSonicResourceFixupLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_81A8, 0xA01E_0000);
        WriteInstruction(memory, 0x800E_81AC, 0x281F_0000);
        WriteInstruction(memory, 0x800E_81B0, 0x7F9C_0214);
        WriteInstruction(memory, 0x800E_8350, 0x889E_0002);
        WriteInstruction(memory, 0x800E_8354, 0x2804_00CB);
        WriteInstruction(memory, 0x800E_8358, 0x4082_FE50);
    }

    private static void WriteSonicOverlayInactiveSlotScan(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x80BC_8110, 0x57E4_2036);
        WriteInstruction(memory, 0x80BC_8114, 0x3C60_80BF);
        WriteInstruction(memory, 0x80BC_8118, 0x3803_D490);
        WriteInstruction(memory, 0x80BC_811C, 0x7C60_2214);
        WriteInstruction(memory, 0x80BC_8120, 0x8003_0000);
        WriteInstruction(memory, 0x80BC_8124, 0x2C00_0001);
        WriteInstruction(memory, 0x80BC_8128, 0x4082_0184);
        WriteInstruction(memory, 0x80BC_82AC, 0x3BFF_0001);
        WriteInstruction(memory, 0x80BC_82B0, 0x2C1F_0040);
        WriteInstruction(memory, 0x80BC_82B4, 0x4180_FE5C);
    }

    private static void WriteSonicGeneratedSlotMismatchScan(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x80BC_7288, 0x3C60_80BF);
        WriteInstruction(memory, 0x80BC_728C, 0x3863_CA1C);
        WriteInstruction(memory, 0x80BC_7290, 0x8063_0000);
        WriteInstruction(memory, 0x80BC_7294, 0x57C0_2834);
        WriteInstruction(memory, 0x80BC_7298, 0x7C83_002E);
        WriteInstruction(memory, 0x80BC_729C, 0x1C7F_002C);
        WriteInstruction(memory, 0x80BC_72A0, 0x3863_0028);
        WriteInstruction(memory, 0x80BC_72A4, 0x7C64_182E);
        WriteInstruction(memory, 0x80BC_72A8, 0x7C1A_1840);
        WriteInstruction(memory, 0x80BC_72AC, 0x4082_0BF8);
        WriteInstruction(memory, 0x80BC_7EA4, 0x3BFF_0001);
        WriteInstruction(memory, 0x80BC_7EA8, 0x3C80_80BF);
        WriteInstruction(memory, 0x80BC_7EAC, 0x3884_CA1C);
        WriteInstruction(memory, 0x80BC_7EB0, 0x80C4_0000);
        WriteInstruction(memory, 0x80BC_7EB4, 0x57C5_2834);
        WriteInstruction(memory, 0x80BC_7EB8, 0x3885_0004);
        WriteInstruction(memory, 0x80BC_7EBC, 0x7C86_202E);
        WriteInstruction(memory, 0x80BC_7EC0, 0x7C1F_2000);
        WriteInstruction(memory, 0x80BC_7EC4, 0x4180_F3C4);
    }

    private static void WriteSonicStartCodeScan(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8014_93B8, 0x2C04_0002);
        WriteInstruction(memory, 0x8014_93BC, 0x887D_0000);
        WriteInstruction(memory, 0x8014_93C0, 0x3BBD_0001);
        WriteInstruction(memory, 0x8014_93C4, 0x4182_004C);
        WriteInstruction(memory, 0x8014_93C8, 0x4080_0014);
        WriteInstruction(memory, 0x8014_93CC, 0x2C04_0000);
        WriteInstruction(memory, 0x8014_93D0, 0x4182_0018);
        WriteInstruction(memory, 0x8014_93D4, 0x4080_0024);
        WriteInstruction(memory, 0x8014_93D8, 0x4800_0078);
        WriteInstruction(memory, 0x8014_93DC, 0x2C04_0004);
        WriteInstruction(memory, 0x8014_93E0, 0x4080_0070);
        WriteInstruction(memory, 0x8014_93E4, 0x4800_0050);
        WriteInstruction(memory, 0x8014_93E8, 0x7C60_0775);
        WriteInstruction(memory, 0x8014_93EC, 0x4082_0064);
        WriteInstruction(memory, 0x8014_93F0, 0x3880_0001);
        WriteInstruction(memory, 0x8014_93F4, 0x4800_005C);
        WriteInstruction(memory, 0x8014_93F8, 0x7C60_0775);
        WriteInstruction(memory, 0x8014_93FC, 0x4082_000C);
        WriteInstruction(memory, 0x8014_9400, 0x3880_0002);
        WriteInstruction(memory, 0x8014_9404, 0x4800_004C);
        WriteInstruction(memory, 0x8014_9408, 0x3880_0000);
        WriteInstruction(memory, 0x8014_940C, 0x4800_0044);
        WriteInstruction(memory, 0x8014_9410, 0x7C60_0774);
        WriteInstruction(memory, 0x8014_9414, 0x2C00_0001);
        WriteInstruction(memory, 0x8014_9418, 0x4082_000C);
        WriteInstruction(memory, 0x8014_941C, 0x3880_0003);
        WriteInstruction(memory, 0x8014_9420, 0x4800_0030);
        WriteInstruction(memory, 0x8014_9424, 0x7C60_0775);
        WriteInstruction(memory, 0x8014_9428, 0x4182_0028);
        WriteInstruction(memory, 0x8014_942C, 0x3880_0000);
        WriteInstruction(memory, 0x8014_9430, 0x4800_0020);
        WriteInstruction(memory, 0x8014_9434, 0x387D_FFFC);
        WriteInstruction(memory, 0x8014_9438, 0x4800_0141);
        WriteInstruction(memory, 0x8014_943C, 0x7FC0_1839);
        WriteInstruction(memory, 0x8014_9440, 0x4182_000C);
        WriteInstruction(memory, 0x8014_9444, 0x387D_FFFC);
        WriteInstruction(memory, 0x8014_9448, 0x4800_0014);
        WriteInstruction(memory, 0x8014_944C, 0x3880_0000);
        WriteInstruction(memory, 0x8014_9450, 0x7C1D_F840);
        WriteInstruction(memory, 0x8014_9454, 0x4180_FF64);
        WriteInstruction(memory, 0x8014_9458, 0x3860_0000);
    }

    private static void WriteSonicGxAttributeFlush(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x814D_8380);
        WriteInstruction(memory, pc + 0x04, 0x3980_0000);
        WriteInstruction(memory, pc + 0x08, 0x3960_0000);
        WriteInstruction(memory, pc + 0x0C, 0x3CE0_CC01);
        WriteInstruction(memory, pc + 0x10, 0x4800_0070);
        WriteInstruction(memory, pc + 0x14, 0x5589_063E);
        WriteInstruction(memory, pc + 0x18, 0x886A_04EE);
        WriteInstruction(memory, pc + 0x24, 0x7C60_0039);
        WriteInstruction(memory, pc + 0x28, 0x4182_0050);
        WriteInstruction(memory, pc + 0x3C, 0x9867_8000);
        WriteInstruction(memory, pc + 0x70, 0x7C0A_002E);
        WriteInstruction(memory, pc + 0x7C, 0x398C_0001);
        WriteInstruction(memory, pc + 0x84, 0x2800_0008);
        WriteInstruction(memory, pc + 0x88, 0x4180_FF8C);
        WriteInstruction(memory, pc + 0x8C, 0x806D_8380);
        WriteInstruction(memory, pc + 0x90, 0x3800_0000);
        WriteInstruction(memory, pc + 0x94, 0x9803_04EE);
        WriteInstruction(memory, pc + 0x98, 0x4E80_0020);
    }

    private static void WriteSonicGxTexObjLoadNoCallback(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x000, 0x7C08_02A6);
        WriteInstruction(memory, pc + 0x004, 0x38ED_83A8);
        WriteInstruction(memory, pc + 0x008, 0x9001_0004);
        WriteInstruction(memory, pc + 0x00C, 0x9421_FFD8);
        WriteInstruction(memory, pc + 0x010, 0x93E1_0024);
        WriteInstruction(memory, pc + 0x014, 0x3FE0_CC01);
        WriteInstruction(memory, pc + 0x018, 0x93C1_0020);
        WriteInstruction(memory, pc + 0x01C, 0x3BC0_0061);
        WriteInstruction(memory, pc + 0x020, 0x93A1_001C);
        WriteInstruction(memory, pc + 0x024, 0x3BA5_0000);
        WriteInstruction(memory, pc + 0x028, 0x38AD_83B8);
        WriteInstruction(memory, pc + 0x02C, 0x9381_0018);
        WriteInstruction(memory, pc + 0x030, 0x7C7C_1B78);
        WriteInstruction(memory, pc + 0x034, 0x80C3_0000);
        WriteInstruction(memory, pc + 0x038, 0x386D_8398);
        WriteInstruction(memory, pc + 0x03C, 0x7C03_E8AE);
        WriteInstruction(memory, pc + 0x040, 0x386D_83A0);
        WriteInstruction(memory, pc + 0x044, 0x5400_C00E);
        WriteInstruction(memory, pc + 0x048, 0x50C0_023E);
        WriteInstruction(memory, pc + 0x04C, 0x901C_0000);
        WriteInstruction(memory, pc + 0x050, 0x38CD_83B0);
        WriteInstruction(memory, pc + 0x054, 0x7C03_E8AE);
        WriteInstruction(memory, pc + 0x058, 0x386D_83C0);
        WriteInstruction(memory, pc + 0x05C, 0x811C_0004);
        WriteInstruction(memory, pc + 0x060, 0x5400_C00E);
        WriteInstruction(memory, pc + 0x064, 0x5100_023E);
        WriteInstruction(memory, pc + 0x068, 0x901C_0004);
        WriteInstruction(memory, pc + 0x06C, 0x7C07_E8AE);
        WriteInstruction(memory, pc + 0x070, 0x811C_0008);
        WriteInstruction(memory, pc + 0x074, 0x5400_C00E);
        WriteInstruction(memory, pc + 0x078, 0x5100_023E);
        WriteInstruction(memory, pc + 0x07C, 0x901C_0008);
        WriteInstruction(memory, pc + 0x080, 0x7C06_E8AE);
        WriteInstruction(memory, pc + 0x084, 0x80E4_0000);
        WriteInstruction(memory, pc + 0x088, 0x5400_C00E);
        WriteInstruction(memory, pc + 0x08C, 0x50E0_023E);
        WriteInstruction(memory, pc + 0x090, 0x9004_0000);
        WriteInstruction(memory, pc + 0x094, 0x7C05_E8AE);
        WriteInstruction(memory, pc + 0x098, 0x80C4_0004);
        WriteInstruction(memory, pc + 0x09C, 0x5400_C00E);
        WriteInstruction(memory, pc + 0x0A0, 0x50C0_023E);
        WriteInstruction(memory, pc + 0x0A4, 0x9004_0004);
        WriteInstruction(memory, pc + 0x0A8, 0x7C03_E8AE);
        WriteInstruction(memory, pc + 0x0AC, 0x80BC_000C);
        WriteInstruction(memory, pc + 0x0B0, 0x5400_C00E);
        WriteInstruction(memory, pc + 0x0B4, 0x50A0_023E);
        WriteInstruction(memory, pc + 0x0B8, 0x901C_000C);
        WriteInstruction(memory, pc + 0x0BC, 0x9BDF_8000);
        WriteInstruction(memory, pc + 0x0C0, 0x801C_0000);
        WriteInstruction(memory, pc + 0x0C4, 0x901F_8000);
        WriteInstruction(memory, pc + 0x0C8, 0x9BDF_8000);
        WriteInstruction(memory, pc + 0x0CC, 0x801C_0004);
        WriteInstruction(memory, pc + 0x0D0, 0x901F_8000);
        WriteInstruction(memory, pc + 0x0D4, 0x9BDF_8000);
        WriteInstruction(memory, pc + 0x0D8, 0x801C_0008);
        WriteInstruction(memory, pc + 0x0DC, 0x901F_8000);
        WriteInstruction(memory, pc + 0x0E0, 0x9BDF_8000);
        WriteInstruction(memory, pc + 0x0E4, 0x8004_0000);
        WriteInstruction(memory, pc + 0x0E8, 0x901F_8000);
        WriteInstruction(memory, pc + 0x0EC, 0x9BDF_8000);
        WriteInstruction(memory, pc + 0x0F0, 0x8004_0004);
        WriteInstruction(memory, pc + 0x0F4, 0x901F_8000);
        WriteInstruction(memory, pc + 0x0F8, 0x9BDF_8000);
        WriteInstruction(memory, pc + 0x0FC, 0x801C_000C);
        WriteInstruction(memory, pc + 0x100, 0x901F_8000);
        WriteInstruction(memory, pc + 0x104, 0x881C_001F);
        WriteInstruction(memory, pc + 0x108, 0x5400_07BD);
        WriteInstruction(memory, pc + 0x10C, 0x4082_003C);
        WriteInstruction(memory, pc + 0x148, 0x806D_8380);
        WriteInstruction(memory, pc + 0x14C, 0x57A5_103A);
        WriteInstruction(memory, pc + 0x150, 0x809C_0008);
        WriteInstruction(memory, pc + 0x154, 0x3800_0000);
        WriteInstruction(memory, pc + 0x158, 0x7C63_2A14);
        WriteInstruction(memory, pc + 0x15C, 0x9083_045C);
        WriteInstruction(memory, pc + 0x160, 0x806D_8380);
        WriteInstruction(memory, pc + 0x164, 0x809C_0000);
        WriteInstruction(memory, pc + 0x168, 0x7C63_2A14);
        WriteInstruction(memory, pc + 0x16C, 0x9083_047C);
        WriteInstruction(memory, pc + 0x170, 0x808D_8380);
        WriteInstruction(memory, pc + 0x174, 0x8064_04F0);
        WriteInstruction(memory, pc + 0x178, 0x6063_0001);
        WriteInstruction(memory, pc + 0x17C, 0x9064_04F0);
        WriteInstruction(memory, pc + 0x180, 0x806D_8380);
        WriteInstruction(memory, pc + 0x184, 0xB003_0002);
        WriteInstruction(memory, pc + 0x188, 0x8001_002C);
        WriteInstruction(memory, pc + 0x18C, 0x83E1_0024);
        WriteInstruction(memory, pc + 0x190, 0x83C1_0020);
        WriteInstruction(memory, pc + 0x194, 0x83A1_001C);
        WriteInstruction(memory, pc + 0x198, 0x8381_0018);
        WriteInstruction(memory, pc + 0x19C, 0x3821_0028);
        WriteInstruction(memory, pc + 0x1A0, 0x7C08_03A6);
        WriteInstruction(memory, pc + 0x1A4, 0x4E80_0020);
    }

    private static void WriteSonicGxPackedStateSetter(GameCubeMemory memory, uint pc)
    {
        uint[] instructions =
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
            WriteInstruction(memory, pc + (uint)(index * sizeof(uint)), instructions[index]);
        }
    }

    private static void WriteSonicGxCpStateSetter(GameCubeMemory memory, uint pc)
    {
        uint[] instructions =
        [
            0x808D_8380, 0x5467_063E, 0x3860_0010, 0x38C4_0204,
            0x80A4_0204, 0x3C80_CC01, 0x3800_103F, 0x54A5_0036,
            0x7CA5_3B78, 0x90A6_0000, 0x9864_8000, 0x806D_8380,
            0x9004_8000, 0x90E4_8000, 0x8003_04F0, 0x6000_0004,
            0x9003_04F0, 0x4E80_0020,
        ];
        for (int index = 0; index < instructions.Length; index++)
        {
            WriteInstruction(memory, pc + (uint)(index * sizeof(uint)), instructions[index]);
        }
    }

    private static void WriteSonicGxTevDefaultWrapper(GameCubeMemory memory)
    {
        const uint pc = 0x8010_44A8;
        uint[] instructions =
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
            WriteInstruction(memory, pc + (uint)(index * sizeof(uint)), instructions[index]);
        }

        WriteSonicGxTevColorEnvSetter(memory, 0x8010_464C);
        WriteSonicGxTevAlphaEnvSetter(memory, 0x8010_46CC);
        WriteSonicGxTevOpSetter(memory, 0x8010_4750, 0x130);
        WriteSonicGxTevOpSetter(memory, 0x8010_4810, 0x170);
    }

    private static void WriteSonicGxTevColorEnvSetter(GameCubeMemory memory, uint pc)
    {
        uint[] instructions =
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
            WriteInstruction(memory, pc + (uint)(index * sizeof(uint)), instructions[index]);
        }
    }

    private static void WriteSonicGxTevColorEnvSetterTail(GameCubeMemory memory, uint pc)
    {
        uint[] instructions =
        [
            0x54C6_072E, 0x7CC4_2378, 0x9089_0000, 0x8089_0000,
            0x5484_0036, 0x7C84_3B78, 0x9089_0000, 0x9865_8000,
            0x806D_8380, 0x8089_0000, 0x9085_8000, 0xB003_0002,
            0x4E80_0020,
        ];
        for (int index = 0; index < instructions.Length; index++)
        {
            WriteInstruction(memory, pc + (uint)(index * sizeof(uint)), instructions[index]);
        }
    }

    private static void WriteSonicGxTevAlphaEnvSetter(GameCubeMemory memory, uint pc)
    {
        uint[] instructions =
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
            WriteInstruction(memory, pc + (uint)(index * sizeof(uint)), instructions[index]);
        }
    }

    private static void WriteSonicGxTevOpSetter(GameCubeMemory memory, uint pc, uint cacheOffset)
    {
        uint cacheOffsetInstruction = cacheOffset switch
        {
            0x130 => 0x3863_0130,
            0x170 => 0x3863_0170,
            _ => throw new ArgumentOutOfRangeException(nameof(cacheOffset)),
        };
        uint[] instructions =
        [
            0x5463_103A, 0x800D_8380, cacheOffsetInstruction, 0x7C60_1A14,
            0x8003_0000, 0x2C04_0001, 0x5400_0398, 0x5080_935A,
            0x9003_0000, 0x4181_0030, 0x8123_0000, 0x54C4_A016,
            0x54A0_801E, 0x5525_0312, 0x7CA4_2378, 0x9083_0000,
            0x8083_0000, 0x5484_041A, 0x7C80_0378, 0x9003_0000,
            0x4800_0024, 0x8003_0000, 0x5400_0312, 0x5080_9A96,
            0x9003_0000, 0x8003_0000, 0x5400_041A, 0x6400_0003,
            0x9003_0000, 0x8083_0000, 0x54E0_9958, 0x5506_B012,
            0x5484_0356, 0x7C80_0378, 0x9003_0000, 0x3880_0061,
            0x3CA0_CC01, 0x80E3_0000, 0x3800_0000, 0x54E7_028E,
            0x7CE6_3378, 0x90C3_0000, 0x9885_8000, 0x808D_8380,
            0x8063_0000, 0x9065_8000, 0xB004_0002, 0x4E80_0020,
        ];
        for (int index = 0; index < instructions.Length; index++)
        {
            WriteInstruction(memory, pc + (uint)(index * sizeof(uint)), instructions[index]);
        }
    }

    private static void WriteSonicGxVertexEmitLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0xA818_0000);
        WriteInstruction(memory, pc + 0x04, 0x3B18_0002);
        WriteInstruction(memory, pc + 0x08, 0x5400_2834);
        WriteInstruction(memory, pc + 0x0C, 0x7F79_0214);
        WriteInstruction(memory, pc + 0x10, 0xABB8_0000);
        WriteInstruction(memory, pc + 0x14, 0x3B18_0002);
        WriteInstruction(memory, pc + 0x18, 0xAB98_0000);
        WriteInstruction(memory, pc + 0x1C, 0x3B18_0002);
        WriteInstruction(memory, pc + 0x20, 0xC03B_0000);
        WriteInstruction(memory, pc + 0x24, 0xC05B_0004);
        WriteInstruction(memory, pc + 0x28, 0xC07B_0008);
        WriteInstruction(memory, pc + 0x2C, 0x4800_0071);
        WriteInstruction(memory, pc + 0x30, 0x807B_0018);
        WriteInstruction(memory, pc + 0x34, 0x4800_005D);
        WriteInstruction(memory, pc + 0x38, 0x7FA3_EB78);
        WriteInstruction(memory, pc + 0x3C, 0x7F84_E378);
        WriteInstruction(memory, pc + 0x40, 0x4800_0041);
        WriteInstruction(memory, pc + 0x44, 0x7F18_FA14);
        WriteInstruction(memory, pc + 0x48, 0x3BDE_FFFF);
        WriteInstruction(memory, pc + 0x4C, 0x2C1E_0000);
        WriteInstruction(memory, pc + 0x50, 0x4082_FFB0);
        WriteInstruction(memory, pc + 0x80, 0x3CA0_CC01);
        WriteInstruction(memory, pc + 0x84, 0xB065_8000);
        WriteInstruction(memory, pc + 0x88, 0xB085_8000);
        WriteInstruction(memory, pc + 0x8C, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x90, 0x3C80_CC01);
        WriteInstruction(memory, pc + 0x94, 0x9064_8000);
        WriteInstruction(memory, pc + 0x98, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x9C, 0x3C60_CC01);
        WriteInstruction(memory, pc + 0xA0, 0xD023_8000);
        WriteInstruction(memory, pc + 0xA4, 0xD043_8000);
        WriteInstruction(memory, pc + 0xA8, 0xD063_8000);
        WriteInstruction(memory, pc + 0xAC, 0x4E80_0020);
    }

    private static void WriteSonicGxDrawBegin(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C08_02A6);
        WriteInstruction(memory, pc + 0x04, 0x9001_0004);
        WriteInstruction(memory, pc + 0x08, 0x9421_FFD8);
        WriteInstruction(memory, pc + 0x0C, 0x93E1_0024);
        WriteInstruction(memory, pc + 0x10, 0x3BE5_0000);
        WriteInstruction(memory, pc + 0x14, 0x93C1_0020);
        WriteInstruction(memory, pc + 0x18, 0x3BC4_0000);
        WriteInstruction(memory, pc + 0x1C, 0x93A1_001C);
        WriteInstruction(memory, pc + 0x20, 0x3BA3_0000);
        WriteInstruction(memory, pc + 0x24, 0x80CD_8380);
        WriteInstruction(memory, pc + 0x28, 0x8006_04F0);
        WriteInstruction(memory, pc + 0x2C, 0x2800_0000);
        WriteInstruction(memory, pc + 0x30, 0x4182_006C);
        WriteInstruction(memory, pc + 0x9C, 0x806D_8380);
        WriteInstruction(memory, pc + 0xA0, 0x8003_0000);
        WriteInstruction(memory, pc + 0xA4, 0x2800_0000);
        WriteInstruction(memory, pc + 0xA8, 0x4082_0008);
        WriteInstruction(memory, pc + 0xB0, 0x7FC0_EB78);
        WriteInstruction(memory, pc + 0xB4, 0x3C60_CC01);
        WriteInstruction(memory, pc + 0xB8, 0x9803_8000);
        WriteInstruction(memory, pc + 0xBC, 0xB3E3_8000);
        WriteInstruction(memory, pc + 0xC0, 0x8001_002C);
        WriteInstruction(memory, pc + 0xC4, 0x83E1_0024);
        WriteInstruction(memory, pc + 0xC8, 0x83C1_0020);
        WriteInstruction(memory, pc + 0xCC, 0x83A1_001C);
        WriteInstruction(memory, pc + 0xD0, 0x3821_0028);
        WriteInstruction(memory, pc + 0xD4, 0x7C08_03A6);
        WriteInstruction(memory, pc + 0xD8, 0x4E80_0020);
    }

    private static void WriteSonicGxBeginDirect(GameCubeMemory memory, uint pc)
    {
        uint[] instructions =
        [
            0x80AD_8380, 0x8085_0014, 0x5480_9FBF, 0x4182_000C,
            0x3860_0001, 0x4800_0008, 0x3860_0000, 0x5480_8FBF,
            0x4182_000C, 0x3880_0001, 0x4800_0008, 0x3880_0000,
            0x8805_041D, 0x7CE3_2214, 0x2800_0000, 0x4182_000C,
            0x3880_0002, 0x4800_001C, 0x8805_041C, 0x2800_0000,
            0x4182_000C, 0x3880_0001, 0x4800_0008, 0x3880_0000,
            0x80C5_0018, 0x54C0_07BF, 0x4182_000C, 0x3860_0001,
            0x4800_0008, 0x3860_0000, 0x54C0_F7BF, 0x4182_000C,
            0x38A0_0001, 0x4800_0008, 0x38A0_0000, 0x54C0_E7BF,
            0x7D03_2A14, 0x4182_000C, 0x3860_0001, 0x4800_0008,
            0x3860_0000, 0x54C0_D7BF, 0x7D08_1A14, 0x4182_000C,
            0x3860_0001, 0x4800_0008, 0x3860_0000, 0x54C0_C7BF,
            0x7D08_1A14, 0x4182_000C, 0x3860_0001, 0x4800_0008,
            0x3860_0000, 0x54C0_B7BF, 0x7D08_1A14, 0x4182_000C,
            0x3860_0001, 0x4800_0008, 0x3860_0000, 0x54C0_A7BF,
            0x7D08_1A14, 0x4182_000C, 0x3860_0001, 0x4800_0008,
            0x3860_0000, 0x54C0_97BF, 0x7D08_1A14, 0x4182_000C,
            0x38C0_0001, 0x4800_0008, 0x38C0_0000, 0x3800_0010,
            0x806D_8380, 0x3CA0_CC01, 0x7D08_3214, 0x9805_8000,
            0x5480_103A, 0x3880_1008, 0x9085_8000, 0x5504_2036,
            0x7CE0_0378, 0x7C80_0378, 0x9005_8000, 0x3800_0001,
            0xB003_0002, 0x4E80_0020,
        ];

        for (int index = 0; index < instructions.Length; index++)
        {
            WriteInstruction(memory, pc + (uint)(index * sizeof(uint)), instructions[index]);
        }
    }

    private static void WriteSonicGxIndexedStripDrawBegin(GameCubeMemory memory, uint pc, uint gxBeginPc)
    {
        WriteInstruction(memory, pc + 0x00, 0xA818_0000);
        WriteInstruction(memory, pc + 0x04, 0x3B18_0002);
        WriteInstruction(memory, pc + 0x08, 0x7C1E_0378);
        WriteInstruction(memory, pc + 0x0C, 0x2C1E_0000);
        WriteInstruction(memory, pc + 0x10, 0x4080_0008);
        WriteInstruction(memory, pc + 0x14, 0x7FDE_00D0);
        WriteInstruction(memory, pc + 0x18, 0x3860_0098);
        WriteInstruction(memory, pc + 0x1C, 0x3880_0000);
        WriteInstruction(memory, pc + 0x20, 0x57C5_043E);
        WriteInstruction(memory, pc + 0x24, 0x4BFE_18AD);
        WriteInstruction(memory, pc + 0x28, 0x4800_0004);
        WriteInstruction(memory, pc + 0x2C, 0x4800_0004);
        WriteSonicGxDrawBegin(memory, gxBeginPc);
        WriteSonicGxVertexEmitLoop(memory, pc + 0x30);
    }

    private static void WriteSonicGxVertexDescriptorSetter(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x000, 0x2803_0019);
        WriteInstruction(memory, pc + 0x004, 0x4181_0300);
        WriteInstruction(memory, pc + 0x008, 0x3CA0_801D);
        WriteInstruction(memory, pc + 0x00C, 0x38A5_2668);
        WriteInstruction(memory, pc + 0x010, 0x5460_103A);
        WriteInstruction(memory, pc + 0x014, 0x7C05_002E);
        WriteInstruction(memory, pc + 0x018, 0x7C09_03A6);
        WriteInstruction(memory, pc + 0x01C, 0x4E80_0420);

        WriteInstruction(memory, pc + 0x138, 0x806D_8380);
        WriteInstruction(memory, pc + 0x13C, 0x5480_482C);
        WriteInstruction(memory, pc + 0x140, 0x3883_0014);
        WriteInstruction(memory, pc + 0x144, 0x8063_0014);
        WriteInstruction(memory, pc + 0x148, 0x5463_05E8);
        WriteInstruction(memory, pc + 0x14C, 0x7C60_0378);
        WriteInstruction(memory, pc + 0x150, 0x9004_0000);
        WriteInstruction(memory, pc + 0x154, 0x4800_01B0);

        WriteInstruction(memory, pc + 0x1D0, 0x806D_8380);
        WriteInstruction(memory, pc + 0x1D4, 0x5480_6824);
        WriteInstruction(memory, pc + 0x1D8, 0x3883_0014);
        WriteInstruction(memory, pc + 0x1DC, 0x8063_0014);
        WriteInstruction(memory, pc + 0x1E0, 0x5463_04E0);
        WriteInstruction(memory, pc + 0x1E4, 0x7C60_0378);
        WriteInstruction(memory, pc + 0x1E8, 0x9004_0000);
        WriteInstruction(memory, pc + 0x1EC, 0x4800_0118);

        WriteInstruction(memory, pc + 0x210, 0x806D_8380);
        WriteInstruction(memory, pc + 0x214, 0x8403_0018);
        WriteInstruction(memory, pc + 0x218, 0x5400_003A);
        WriteInstruction(memory, pc + 0x21C, 0x7C00_2378);
        WriteInstruction(memory, pc + 0x220, 0x9003_0000);
        WriteInstruction(memory, pc + 0x224, 0x4800_00E0);

        WriteInstruction(memory, pc + 0x304, 0x806D_8380);
        WriteInstruction(memory, pc + 0x308, 0x8803_041C);
        WriteInstruction(memory, pc + 0x30C, 0x2800_0000);
        WriteInstruction(memory, pc + 0x310, 0x4082_0010);
        WriteInstruction(memory, pc + 0x314, 0x8803_041D);
        WriteInstruction(memory, pc + 0x318, 0x2800_0000);
        WriteInstruction(memory, pc + 0x31C, 0x4182_0024);
        WriteInstruction(memory, pc + 0x320, 0x3883_0014);
        WriteInstruction(memory, pc + 0x324, 0x8003_0418);
        WriteInstruction(memory, pc + 0x328, 0x8063_0014);
        WriteInstruction(memory, pc + 0x32C, 0x5400_5828);
        WriteInstruction(memory, pc + 0x330, 0x5463_0564);
        WriteInstruction(memory, pc + 0x334, 0x7C60_0378);
        WriteInstruction(memory, pc + 0x338, 0x9004_0000);
        WriteInstruction(memory, pc + 0x33C, 0x4800_0010);
        WriteInstruction(memory, pc + 0x340, 0x8403_0014);
        WriteInstruction(memory, pc + 0x344, 0x5400_0564);
        WriteInstruction(memory, pc + 0x348, 0x9003_0000);
        WriteInstruction(memory, pc + 0x34C, 0x806D_8380);
        WriteInstruction(memory, pc + 0x350, 0x8003_04F0);
        WriteInstruction(memory, pc + 0x354, 0x6000_0008);
        WriteInstruction(memory, pc + 0x358, 0x9003_04F0);
        WriteInstruction(memory, pc + 0x35C, 0x4E80_0020);

        memory.Write32(0x801D_2668 + 9 * sizeof(uint), pc + 0x138);
        memory.Write32(0x801D_2668 + 11 * sizeof(uint), pc + 0x1D0);
        memory.Write32(0x801D_2668 + 13 * sizeof(uint), pc + 0x210);
    }

    private static void WriteSonicGxVertexAttributeFlush(GameCubeMemory memory, uint pc)
    {
        WriteInstructions(
            memory,
            0x8010_3C5C,
            [
                0x80AD_8380, 0x5480_103A, 0x5469_103A, 0x7C85_0214,
                0x7C65_4A14, 0x80A3_045C, 0x38E0_0061, 0x8064_00B8,
                0x3CC0_CC01, 0x5463_001E, 0x50A3_05BE, 0x9064_00B8,
                0x3860_0000, 0x808D_8380, 0x7D04_0214, 0x8088_00D8,
                0x5484_001E, 0x50A4_B5BE, 0x9088_00D8, 0x80AD_8380,
                0x7C85_4A14, 0x8124_047C, 0x7D45_0214, 0x80AA_00B8,
                0x5524_F7BE, 0x2104_0001, 0x5524_07BE, 0x2084_0001,
                0x7C84_0034, 0x54A5_041C, 0x5484_5A1E, 0x7CA4_2378,
                0x908A_00B8, 0x7D04_0034, 0x5484_5A1E, 0x80AD_8380,
                0x7D05_0214, 0x80A8_00D8, 0x54A5_041C, 0x7CA4_2378,
                0x9088_00D8, 0x80AD_8380, 0x98E6_8000, 0x7C85_0214,
                0x8004_00B8, 0x9006_8000, 0x98E6_8000, 0x8004_00D8,
                0x9006_8000, 0xB065_0002, 0x4E80_0020,
            ]);

        WriteInstructions(
            memory,
            pc,
            [
                0x7C08_02A6, 0x9001_0004, 0x9421_FFD8, 0xBF61_0014,
                0x806D_8380, 0x8003_04DC, 0x2800_00FF, 0x4182_013C,
                0x8003_0204, 0x3BC0_0000, 0x5403_B73E, 0x3BE3_0001,
                0x541B_877E, 0x4800_00A0, 0x2C1E_0002, 0x4182_004C,
                0x4080_0014, 0x2C1E_0000, 0x4182_0018, 0x4080_0028,
                0x4800_005C, 0x2C1E_0004, 0x4080_0054, 0x4800_0040,
                0x806D_8380, 0x8003_0120, 0x541D_077E, 0x541C_EF7E,
                0x4800_003C, 0x806D_8380, 0x8003_0120, 0x541D_D77E,
                0x541C_BF7E, 0x4800_0028, 0x806D_8380, 0x8003_0120,
                0x541D_A77E, 0x541C_8F7E, 0x4800_0014, 0x806D_8380,
                0x8003_0120, 0x541D_777E, 0x541C_5F7E, 0x806D_8380,
                0x3800_0001, 0x7C00_E030, 0x8063_04DC, 0x7C60_0039,
                0x4082_0010, 0x387D_0000, 0x389C_0000, 0x4BFF_FE69,
                0x3BDE_0001, 0x7C1E_D840, 0x4180_FF60, 0x3B60_0000,
                0x3BDB_0000, 0x4800_006C, 0x80AD_8380, 0x387E_049C,
                0x5764_083A, 0x7C65_182E, 0x5760_07FF, 0x3884_0100,
                0x7C85_2214, 0x547D_062C, 0x4182_0010, 0x8004_0000,
                0x541C_8F7E, 0x4800_000C, 0x8004_0000, 0x541C_EF7E,
                0x281D_00FF, 0x4182_0024, 0x3800_0001, 0x8065_04DC,
                0x7C00_E030, 0x7C60_0039, 0x4082_0010, 0x387D_0000,
                0x389C_0000, 0x4BFF_FDF1, 0x3BDE_0004, 0x3B7B_0001,
                0x7C1B_F840, 0x4180_FF94, 0xBB61_0014, 0x8001_002C,
                0x3821_0028, 0x7C08_03A6, 0x4E80_0020,
            ]);
    }

    private static void WriteInstructions(GameCubeMemory memory, uint pc, uint[] instructions)
    {
        for (int index = 0; index < instructions.Length; index++)
        {
            WriteInstruction(memory, pc + (uint)(index * sizeof(uint)), instructions[index]);
        }
    }

    private static void WriteSonicGxIndexedStripTail(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x4800_0029);
        WriteInstruction(memory, pc + 0x04, 0x3B5A_FFFF);
        WriteInstruction(memory, pc + 0x08, 0x281A_0000);
        WriteInstruction(memory, pc + 0x0C, 0x4082_FF70);
        WriteInstruction(memory, pc + 0x28, 0x4E80_0020);
    }

    private static void WriteSonicGxIndexedStripEpilogue(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x8001_003C);
        WriteInstruction(memory, pc + 0x04, 0x3961_0038);
        WriteInstruction(memory, pc + 0x08, 0x4BFE_AEF9);
        WriteInstruction(memory, pc + 0x0C, 0x3821_0038);
        WriteInstruction(memory, pc + 0x10, 0x7C08_03A6);
        WriteInstruction(memory, pc + 0x14, 0x4E80_0020);
        WriteInstruction(memory, 0x8010_B00C, 0x830B_FFE0);
        WriteInstruction(memory, 0x8010_B010, 0x832B_FFE4);
        WriteInstruction(memory, 0x8010_B014, 0x834B_FFE8);
        WriteInstruction(memory, 0x8010_B018, 0x836B_FFEC);
        WriteInstruction(memory, 0x8010_B01C, 0x838B_FFF0);
        WriteInstruction(memory, 0x8010_B020, 0x83AB_FFF4);
        WriteInstruction(memory, 0x8010_B024, 0x83CB_FFF8);
        WriteInstruction(memory, 0x8010_B028, 0x83EB_FFFC);
        WriteInstruction(memory, 0x8010_B02C, 0x4E80_0020);
    }

    private static void WriteSonicGxCommandListTerminal(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0xAB94_0000);
        WriteInstruction(memory, pc + 0x04, 0x3A94_0002);
        WriteInstruction(memory, pc + 0x08, 0x2C1C_00FF);
        WriteInstruction(memory, pc + 0x0C, 0x4082_FBB0);
        WriteInstruction(memory, pc + 0x10, 0x8001_0084);
        WriteInstruction(memory, pc + 0x14, 0x3961_0080);
        WriteInstruction(memory, pc + 0x18, 0x4BFE_DE61);
        WriteInstruction(memory, pc + 0x1C, 0x3821_0080);
        WriteInstruction(memory, pc + 0x20, 0x7C08_03A6);
        WriteInstruction(memory, pc + 0x24, 0x4E80_0020);

        WriteInstruction(memory, 0x8010_AFFC, 0x828B_FFD0);
        WriteInstruction(memory, 0x8010_B000, 0x82AB_FFD4);
        WriteInstruction(memory, 0x8010_B004, 0x82CB_FFD8);
        WriteInstruction(memory, 0x8010_B008, 0x82EB_FFDC);
        WriteInstruction(memory, 0x8010_B00C, 0x830B_FFE0);
        WriteInstruction(memory, 0x8010_B010, 0x832B_FFE4);
        WriteInstruction(memory, 0x8010_B014, 0x834B_FFE8);
        WriteInstruction(memory, 0x8010_B018, 0x836B_FFEC);
        WriteInstruction(memory, 0x8010_B01C, 0x838B_FFF0);
        WriteInstruction(memory, 0x8010_B020, 0x83AB_FFF4);
        WriteInstruction(memory, 0x8010_B024, 0x83CB_FFF8);
        WriteInstruction(memory, 0x8010_B028, 0x83EB_FFFC);
        WriteInstruction(memory, 0x8010_B02C, 0x4E80_0020);
    }

    private static void WriteSonicGxCommandDispatch(GameCubeMemory memory)
    {
        const uint headerPc = 0x8011_CD40;
        WriteInstruction(memory, headerPc + 0x00, 0x5780_063E);
        WriteInstruction(memory, headerPc + 0x04, 0x7C19_0734);
        WriteInstruction(memory, headerPc + 0x08, 0x2C19_0008);
        WriteInstruction(memory, headerPc + 0x0C, 0x4080_009C);
        WriteInstruction(memory, headerPc + 0x10, 0x2C19_0004);

        const uint highRangePc = 0x8011_CDE8;
        WriteInstruction(memory, highRangePc + 0x00, 0x2C19_0010);
        WriteInstruction(memory, highRangePc + 0x04, 0x4080_0034);
        WriteInstruction(memory, highRangePc + 0x08, 0x2C18_0000);
        WriteInstruction(memory, highRangePc + 0x38, 0x2C19_0040);

        const uint extendedRangePc = 0x8011_CE20;
        WriteInstruction(memory, extendedRangePc + 0x00, 0x2C19_0040);
        WriteInstruction(memory, extendedRangePc + 0x04, 0x4080_003C);
        WriteInstruction(memory, extendedRangePc + 0x08, 0xAB34_0000);
        WriteInstruction(memory, extendedRangePc + 0x40, 0x7E83_A378);

        const uint metadataHeaderPc = 0x8011_CE60;
        WriteInstruction(memory, metadataHeaderPc + 0x00, 0x7E83_A378);
        WriteInstruction(memory, metadataHeaderPc + 0x04, 0x3A94_0002);
        WriteInstruction(memory, metadataHeaderPc + 0x08, 0xA363_0000);
        WriteInstruction(memory, metadataHeaderPc + 0x0C, 0xA814_0000);
        WriteInstruction(memory, metadataHeaderPc + 0x10, 0x3A94_0002);
        WriteInstruction(memory, metadataHeaderPc + 0x14, 0x7C1A_0378);
        WriteInstruction(memory, metadataHeaderPc + 0x18, 0x2C18_0000);
        WriteInstruction(memory, metadataHeaderPc + 0x1C, 0x4081_0044);

        const uint activeBatchRecordPc = 0x8011_CE80;
        WriteInstruction(memory, activeBatchRecordPc + 0x00, 0x56E3_103A);
        WriteInstruction(memory, activeBatchRecordPc + 0x04, 0x7E9E_192E);
        WriteInstruction(memory, activeBatchRecordPc + 0x08, 0x2019_0042);
        WriteInstruction(memory, activeBatchRecordPc + 0x0C, 0x7C00_0034);
        WriteInstruction(memory, activeBatchRecordPc + 0x10, 0x5400_D97E);
        WriteInstruction(memory, activeBatchRecordPc + 0x14, 0x7C1F_192E);
        WriteInstruction(memory, activeBatchRecordPc + 0x18, 0x7EE0_BB78);
        WriteInstruction(memory, activeBatchRecordPc + 0x1C, 0x3AF7_0001);
        WriteInstruction(memory, activeBatchRecordPc + 0x20, 0x7C00_C000);
        WriteInstruction(memory, activeBatchRecordPc + 0x24, 0x4080_0014);
        WriteInstruction(memory, activeBatchRecordPc + 0x28, 0x381B_FFFF);
        WriteInstruction(memory, activeBatchRecordPc + 0x2C, 0x5400_083C);
        WriteInstruction(memory, activeBatchRecordPc + 0x30, 0x7E94_0214);
        WriteInstruction(memory, activeBatchRecordPc + 0x34, 0x4800_02D0);
        WriteInstruction(memory, activeBatchRecordPc + 0x38, 0x7EDC_B378);

        const uint maskUpdatePc = 0x8011_CEC0;
        WriteInstruction(memory, maskUpdatePc + 0x00, 0x800D_8E28);
        WriteInstruction(memory, maskUpdatePc + 0x04, 0x5400_0528);
        WriteInstruction(memory, maskUpdatePc + 0x08, 0x2800_0000);
        WriteInstruction(memory, maskUpdatePc + 0x0C, 0x4182_0014);
        WriteInstruction(memory, maskUpdatePc + 0x10, 0x800D_8E24);
        WriteInstruction(memory, maskUpdatePc + 0x14, 0x7F9C_0038);
        WriteInstruction(memory, maskUpdatePc + 0x18, 0x800D_8E20);
        WriteInstruction(memory, maskUpdatePc + 0x1C, 0x7F9C_0378);
        WriteInstruction(memory, maskUpdatePc + 0x20, 0x7F83_0734);
        WriteInstruction(memory, maskUpdatePc + 0x24, 0x3800_0008);
        WriteInstruction(memory, maskUpdatePc + 0x28, 0x7C60_0630);
        WriteInstruction(memory, maskUpdatePc + 0x2C, 0x7C1C_0734);
        WriteInstruction(memory, maskUpdatePc + 0x30, 0x800D_8E80);
        WriteInstruction(memory, maskUpdatePc + 0x34, 0x7F84_0278);
        WriteInstruction(memory, maskUpdatePc + 0x38, 0x800D_8E84);
        WriteInstruction(memory, maskUpdatePc + 0x3C, 0x7C84_0378);
        WriteInstruction(memory, maskUpdatePc + 0x40, 0x7F83_E378);
        WriteInstruction(memory, maskUpdatePc + 0x44, 0x906D_8E80);
        WriteInstruction(memory, maskUpdatePc + 0x48, 0x3800_0000);
        WriteInstruction(memory, maskUpdatePc + 0x4C, 0x900D_8E84);
    }

    private static void WriteSonicGprSaveRestoreTail(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8010_AFC0, 0x930B_FFE0);
        WriteInstruction(memory, 0x8010_AFC4, 0x932B_FFE4);
        WriteInstruction(memory, 0x8010_AFC8, 0x934B_FFE8);
        WriteInstruction(memory, 0x8010_AFCC, 0x936B_FFEC);
        WriteInstruction(memory, 0x8010_AFD0, 0x938B_FFF0);
        WriteInstruction(memory, 0x8010_AFD4, 0x93AB_FFF4);
        WriteInstruction(memory, 0x8010_AFD8, 0x93CB_FFF8);
        WriteInstruction(memory, 0x8010_AFDC, 0x93EB_FFFC);
        WriteInstruction(memory, 0x8010_AFE0, 0x4E80_0020);

        WriteInstruction(memory, 0x8010_B00C, 0x830B_FFE0);
        WriteInstruction(memory, 0x8010_B010, 0x832B_FFE4);
        WriteInstruction(memory, 0x8010_B014, 0x834B_FFE8);
        WriteInstruction(memory, 0x8010_B018, 0x836B_FFEC);
        WriteInstruction(memory, 0x8010_B01C, 0x838B_FFF0);
        WriteInstruction(memory, 0x8010_B020, 0x83AB_FFF4);
        WriteInstruction(memory, 0x8010_B024, 0x83CB_FFF8);
        WriteInstruction(memory, 0x8010_B028, 0x83EB_FFFC);
        WriteInstruction(memory, 0x8010_B02C, 0x4E80_0020);
    }

    private static void WriteSonicGxAttributeStateSetter(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x000, 0x3804_FFF7);
        WriteInstruction(memory, pc + 0x004, 0x810D_8380);
        WriteInstruction(memory, pc + 0x008, 0x5464_103A);
        WriteInstruction(memory, pc + 0x00C, 0x7D28_2214);
        WriteInstruction(memory, pc + 0x010, 0x2800_0010);
        WriteInstruction(memory, pc + 0x014, 0x3889_001C);
        WriteInstruction(memory, pc + 0x018, 0x3909_003C);
        WriteInstruction(memory, pc + 0x01C, 0x3929_005C);
        WriteInstruction(memory, pc + 0x020, 0x4181_0308);
        WriteInstruction(memory, pc + 0x024, 0x3D40_801D);
        WriteInstruction(memory, pc + 0x028, 0x394A_26D0);
        WriteInstruction(memory, pc + 0x02C, 0x5400_103A);
        WriteInstruction(memory, pc + 0x030, 0x7C0A_002E);
        WriteInstruction(memory, pc + 0x034, 0x7C09_03A6);
        WriteInstruction(memory, pc + 0x038, 0x4E80_0420);

        WriteInstruction(memory, pc + 0x03C, 0x8004_0000);
        WriteInstruction(memory, pc + 0x040, 0x54C6_083C);
        WriteInstruction(memory, pc + 0x044, 0x5400_003C);
        WriteInstruction(memory, pc + 0x048, 0x7C00_2B78);
        WriteInstruction(memory, pc + 0x04C, 0x9004_0000);
        WriteInstruction(memory, pc + 0x050, 0x54E0_2536);
        WriteInstruction(memory, pc + 0x054, 0x80A4_0000);
        WriteInstruction(memory, pc + 0x058, 0x54A5_07F6);
        WriteInstruction(memory, pc + 0x05C, 0x7CA5_3378);
        WriteInstruction(memory, pc + 0x060, 0x90A4_0000);
        WriteInstruction(memory, pc + 0x064, 0x80A4_0000);
        WriteInstruction(memory, pc + 0x068, 0x54A5_072C);
        WriteInstruction(memory, pc + 0x06C, 0x7CA0_0378);
        WriteInstruction(memory, pc + 0x070, 0x9004_0000);
        WriteInstruction(memory, pc + 0x074, 0x4800_02B4);

        WriteInstruction(memory, pc + 0x134, 0x8104_0000);
        WriteInstruction(memory, pc + 0x138, 0x54A0_A814);
        WriteInstruction(memory, pc + 0x13C, 0x5505_02D2);
        WriteInstruction(memory, pc + 0x140, 0x7CA0_0378);
        WriteInstruction(memory, pc + 0x144, 0x9004_0000);
        WriteInstruction(memory, pc + 0x148, 0x54C5_B012);
        WriteInstruction(memory, pc + 0x14C, 0x54E0_C80C);
        WriteInstruction(memory, pc + 0x150, 0x80C4_0000);
        WriteInstruction(memory, pc + 0x154, 0x54C6_028C);
        WriteInstruction(memory, pc + 0x158, 0x7CC5_2B78);
        WriteInstruction(memory, pc + 0x15C, 0x90A4_0000);
        WriteInstruction(memory, pc + 0x160, 0x80A4_0000);
        WriteInstruction(memory, pc + 0x164, 0x54A5_01C2);
        WriteInstruction(memory, pc + 0x168, 0x7CA0_0378);
        WriteInstruction(memory, pc + 0x16C, 0x9004_0000);
        WriteInstruction(memory, pc + 0x170, 0x4800_01B8);

        WriteInstruction(memory, pc + 0x328, 0x80AD_8380);
        WriteInstruction(memory, pc + 0x32C, 0x5460_063E);
        WriteInstruction(memory, pc + 0x330, 0x3860_0001);
        WriteInstruction(memory, pc + 0x334, 0x8085_04F0);
        WriteInstruction(memory, pc + 0x338, 0x7C60_0030);
        WriteInstruction(memory, pc + 0x33C, 0x5400_063E);
        WriteInstruction(memory, pc + 0x340, 0x6083_0010);
        WriteInstruction(memory, pc + 0x344, 0x9065_04F0);
        WriteInstruction(memory, pc + 0x348, 0x808D_8380);
        WriteInstruction(memory, pc + 0x34C, 0x8864_04EE);
        WriteInstruction(memory, pc + 0x350, 0x7C60_0378);
        WriteInstruction(memory, pc + 0x354, 0x9804_04EE);
        WriteInstruction(memory, pc + 0x358, 0x4E80_0020);

        memory.Write32(0x801D_26D0, pc + 0x03C);
        memory.Write32(0x801D_26D0 + 4 * sizeof(uint), pc + 0x134);
    }

    private static void WriteSonicGxFloatStripEmitLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc - 0x30, 0xA81A_0000);
        WriteInstruction(memory, pc - 0x2C, 0x3B5A_0002);
        WriteInstruction(memory, pc - 0x28, 0x7C1E_0378);
        WriteInstruction(memory, pc - 0x24, 0x2C1E_0000);
        WriteInstruction(memory, pc - 0x20, 0x4080_0008);
        WriteInstruction(memory, pc - 0x1C, 0x7FDE_00D0);
        WriteInstruction(memory, pc - 0x18, 0x3860_0098);
        WriteInstruction(memory, pc - 0x14, 0x3880_0000);
        WriteInstruction(memory, pc - 0x10, 0x57C5_043E);
        WriteInstruction(memory, pc - 0x0C, 0x4BFE_4345);
        WriteInstruction(memory, pc - 0x08, 0x4800_0004);
        WriteInstruction(memory, pc - 0x04, 0x4800_0004);
        WriteInstruction(memory, pc + 0x00, 0xA81A_0000);
        WriteInstruction(memory, pc + 0x04, 0x3B5A_0002);
        WriteInstruction(memory, pc + 0x08, 0x5400_2834);
        WriteInstruction(memory, pc + 0x0C, 0x7FBB_0214);
        WriteInstruction(memory, pc + 0x10, 0xC03D_0000);
        WriteInstruction(memory, pc + 0x14, 0xC05D_0004);
        WriteInstruction(memory, pc + 0x18, 0xC07D_0008);
        WriteInstruction(memory, pc + 0x1C, 0x4800_0065);
        WriteInstruction(memory, pc + 0x20, 0xC03D_000C);
        WriteInstruction(memory, pc + 0x24, 0xC05D_0010);
        WriteInstruction(memory, pc + 0x28, 0xC07D_0014);
        WriteInstruction(memory, pc + 0x2C, 0x4800_0041);
        WriteInstruction(memory, pc + 0x30, 0x7F5A_FA14);
        WriteInstruction(memory, pc + 0x34, 0x3BDE_FFFF);
        WriteInstruction(memory, pc + 0x38, 0x2C1E_0000);
        WriteInstruction(memory, pc + 0x3C, 0x4082_FFC4);
        WriteInstruction(memory, pc + 0x40, 0x4800_0029);
        WriteInstruction(memory, pc + 0x44, 0x3B9C_FFFF);
        WriteInstruction(memory, pc + 0x48, 0x281C_0000);
        WriteInstruction(memory, pc + 0x4C, 0x4082_FF84);
        WriteInstruction(memory, pc + 0x68, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x6C, 0x3C60_CC01);
        WriteInstruction(memory, pc + 0x70, 0xD023_8000);
        WriteInstruction(memory, pc + 0x74, 0xD043_8000);
        WriteInstruction(memory, pc + 0x78, 0xD063_8000);
        WriteInstruction(memory, pc + 0x7C, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x80, 0x3C60_CC01);
        WriteInstruction(memory, pc + 0x84, 0xD023_8000);
        WriteInstruction(memory, pc + 0x88, 0xD043_8000);
        WriteInstruction(memory, pc + 0x8C, 0xD063_8000);
        WriteInstruction(memory, pc + 0x90, 0x4E80_0020);
    }

    private static void WriteSonicGxFloatAttributeStripEmitLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc - 0x30, 0xA81A_0000);
        WriteInstruction(memory, pc - 0x2C, 0x3B5A_0002);
        WriteInstruction(memory, pc - 0x28, 0x7C1E_0378);
        WriteInstruction(memory, pc - 0x24, 0x2C1E_0000);
        WriteInstruction(memory, pc - 0x20, 0x4080_0008);
        WriteInstruction(memory, pc - 0x1C, 0x7FDE_00D0);
        WriteInstruction(memory, pc - 0x18, 0x3860_0098);
        WriteInstruction(memory, pc - 0x14, 0x3880_0000);
        WriteInstruction(memory, pc - 0x10, 0x57C5_043E);
        WriteInstruction(memory, pc - 0x0C, 0x4BFE_19E5);
        WriteInstruction(memory, pc - 0x08, 0x4800_0004);
        WriteInstruction(memory, pc - 0x04, 0x4800_0004);
        WriteInstruction(memory, pc + 0x00, 0xA81A_0000);
        WriteInstruction(memory, pc + 0x04, 0x3B5A_0002);
        WriteInstruction(memory, pc + 0x08, 0x5400_2834);
        WriteInstruction(memory, pc + 0x0C, 0x7FBB_0214);
        WriteInstruction(memory, pc + 0x10, 0xC03D_0000);
        WriteInstruction(memory, pc + 0x14, 0xC05D_0004);
        WriteInstruction(memory, pc + 0x18, 0xC07D_0008);
        WriteInstruction(memory, pc + 0x1C, 0x4800_0055);
        WriteInstruction(memory, pc + 0x20, 0x807D_0018);
        WriteInstruction(memory, pc + 0x24, 0x4800_0041);
        WriteInstruction(memory, pc + 0x28, 0x7F5A_FA14);
        WriteInstruction(memory, pc + 0x2C, 0x3BDE_FFFF);
        WriteInstruction(memory, pc + 0x30, 0x2C1E_0000);
        WriteInstruction(memory, pc + 0x34, 0x4082_FFCC);
        WriteInstruction(memory, pc + 0x38, 0x4800_0029);
        WriteInstruction(memory, pc + 0x60, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x64, 0x3C80_CC01);
        WriteInstruction(memory, pc + 0x68, 0x9064_8000);
        WriteInstruction(memory, pc + 0x6C, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x70, 0x3C60_CC01);
        WriteInstruction(memory, pc + 0x74, 0xD023_8000);
        WriteInstruction(memory, pc + 0x78, 0xD043_8000);
        WriteInstruction(memory, pc + 0x7C, 0xD063_8000);
        WriteInstruction(memory, pc + 0x80, 0x4E80_0020);
    }

    private static void WriteSonicGxFloatTexcoordStripEmitLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc - 0x30, 0xA819_0000);
        WriteInstruction(memory, pc - 0x2C, 0x3B39_0002);
        WriteInstruction(memory, pc - 0x28, 0x7C1F_0378);
        WriteInstruction(memory, pc - 0x24, 0x2C1F_0000);
        WriteInstruction(memory, pc - 0x20, 0x4080_0008);
        WriteInstruction(memory, pc - 0x1C, 0x7FFF_00D0);
        WriteInstruction(memory, pc - 0x18, 0x3860_0098);
        WriteInstruction(memory, pc - 0x14, 0x3880_0000);
        WriteInstruction(memory, pc - 0x10, 0x57E5_043E);
        WriteInstruction(memory, pc - 0x0C, 0x4BFE_40F5);
        WriteInstruction(memory, pc - 0x08, 0x4800_0004);
        WriteInstruction(memory, pc - 0x04, 0x4800_0004);
        WriteInstruction(memory, pc + 0x00, 0xA819_0000);
        WriteInstruction(memory, pc + 0x04, 0x3B39_0002);
        WriteInstruction(memory, pc + 0x08, 0x5400_2834);
        WriteInstruction(memory, pc + 0x0C, 0x7F9A_0214);
        WriteInstruction(memory, pc + 0x10, 0xABD9_0000);
        WriteInstruction(memory, pc + 0x14, 0x3B39_0002);
        WriteInstruction(memory, pc + 0x18, 0xABB9_0000);
        WriteInstruction(memory, pc + 0x1C, 0x3B39_0002);
        WriteInstruction(memory, pc + 0x20, 0xC03C_0000);
        WriteInstruction(memory, pc + 0x24, 0xC05C_0004);
        WriteInstruction(memory, pc + 0x28, 0xC07C_0008);
        WriteInstruction(memory, pc + 0x2C, 0x4800_007D);
        WriteInstruction(memory, pc + 0x30, 0xC03C_000C);
        WriteInstruction(memory, pc + 0x34, 0xC05C_0010);
        WriteInstruction(memory, pc + 0x38, 0xC07C_0014);
        WriteInstruction(memory, pc + 0x3C, 0x4800_0059);
        WriteInstruction(memory, pc + 0x40, 0x7FC3_F378);
        WriteInstruction(memory, pc + 0x44, 0x7FA4_EB78);
        WriteInstruction(memory, pc + 0x48, 0x4800_003D);
        WriteInstruction(memory, pc + 0x4C, 0x3BFF_FFFF);
        WriteInstruction(memory, pc + 0x50, 0x2C1F_0000);
        WriteInstruction(memory, pc + 0x54, 0x4082_FFAC);
        WriteInstruction(memory, pc + 0x58, 0x4800_0029);
        WriteInstruction(memory, pc + 0x5C, 0x3B7B_FFFF);
        WriteInstruction(memory, pc + 0x60, 0x281B_0000);
        WriteInstruction(memory, pc + 0x64, 0x4082_FF6C);
        WriteInstruction(memory, pc + 0x80, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x84, 0x3CA0_CC01);
        WriteInstruction(memory, pc + 0x88, 0xB065_8000);
        WriteInstruction(memory, pc + 0x8C, 0xB085_8000);
        WriteInstruction(memory, pc + 0x90, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x94, 0x3C60_CC01);
        WriteInstruction(memory, pc + 0x98, 0xD023_8000);
        WriteInstruction(memory, pc + 0x9C, 0xD043_8000);
        WriteInstruction(memory, pc + 0xA0, 0xD063_8000);
        WriteInstruction(memory, pc + 0xA4, 0x4E80_0020);
        WriteInstruction(memory, pc + 0xA8, 0x3C60_CC01);
        WriteInstruction(memory, pc + 0xAC, 0xD023_8000);
        WriteInstruction(memory, pc + 0xB0, 0xD043_8000);
        WriteInstruction(memory, pc + 0xB4, 0xD063_8000);
        WriteInstruction(memory, pc + 0xB8, 0x4E80_0020);
    }

    private static void WriteSonicBitPlaneCrop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x000, 0x9421_FFD8);
        WriteInstruction(memory, pc + 0x004, 0x3800_0018);
        WriteInstruction(memory, pc + 0x008, 0x7C09_03A6);
        WriteInstruction(memory, pc + 0x014, 0x3920_0019);
        WriteInstruction(memory, pc + 0x018, 0x3940_0019);
        WriteInstruction(memory, pc + 0x074, 0x4182_FFEC);
        WriteInstruction(memory, pc + 0x104, 0x38E0_0000);
        WriteInstruction(memory, pc + 0x108, 0x38A9_0000);
        WriteInstruction(memory, pc + 0x110, 0x3900_0000);
        WriteInstruction(memory, pc + 0x118, 0x7C09_03A6);
        WriteInstruction(memory, pc + 0x240, 0x9BE6_0000);
        WriteInstruction(memory, pc + 0x264, 0x2009_0018);
        WriteInstruction(memory, pc + 0x268, 0x7C6A_0051);
        WriteInstruction(memory, pc + 0x274, 0x83E1_0024);
        WriteInstruction(memory, pc + 0x278, 0x3821_0028);
        WriteInstruction(memory, pc + 0x27C, 0x4E80_0020);
    }

    private static void WriteSonicByteTableLookup(GameCubeMemory memory, uint pc, uint table)
    {
        WriteInstruction(memory, pc + 0x00, 0x2C03_FFFF);
        WriteInstruction(memory, pc + 0x04, 0x4082_000C);
        WriteInstruction(memory, pc + 0x08, 0x3860_FFFF);
        WriteInstruction(memory, pc + 0x0C, 0x4E80_0020);
        WriteInstruction(memory, pc + 0x10, 0x3C80_8017);
        WriteInstruction(memory, pc + 0x14, 0x5463_063E);
        WriteInstruction(memory, pc + 0x18, 0x3804_0000 | (table & 0xFFFF));
        WriteInstruction(memory, pc + 0x1C, 0x7C60_1A14);
        WriteInstruction(memory, pc + 0x20, 0x8863_0000);
        WriteInstruction(memory, pc + 0x24, 0x4E80_0020);
    }

    private static void WriteSonicNormalizedStringScan(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x8814_0000);
        WriteInstruction(memory, pc + 0x04, 0x3A94_0001);
        WriteInstruction(memory, pc + 0x08, 0x7C03_0774);
        WriteInstruction(memory, pc + 0x0C, 0x4801_CB35);
        WriteInstruction(memory, pc + 0x10, 0x8815_0000);
        WriteInstruction(memory, pc + 0x14, 0x3AC3_0000);
        WriteInstruction(memory, pc + 0x18, 0x3AB5_0001);
        WriteInstruction(memory, pc + 0x1C, 0x7C03_0774);
        WriteInstruction(memory, pc + 0x20, 0x4801_CB21);
        WriteInstruction(memory, pc + 0x24, 0x7C03_B000);
        WriteInstruction(memory, pc + 0x28, 0x4182_000C);
        WriteInstruction(memory, pc + 0x2C, 0x3800_0000);
        WriteInstruction(memory, pc + 0x30, 0x4800_0030);
        WriteInstruction(memory, pc + 0x34, 0x8814_0000);
        WriteInstruction(memory, pc + 0x38, 0x7C00_0775);
        WriteInstruction(memory, pc + 0x3C, 0x4082_FFC4);
    }

    private static void WriteSonicPathRecordScan(GameCubeMemory memory, uint pc, uint table)
    {
        WriteInstruction(memory, pc + 0x00, 0x1F9A_000C);
        WriteInstruction(memory, pc + 0x04, 0x7C83_E02E);
        WriteInstruction(memory, pc + 0x08, 0x5480_000F);
        WriteInstruction(memory, pc + 0x1C, 0x2C00_0000);
        WriteInstruction(memory, pc + 0x20, 0x4082_000C);
        WriteInstruction(memory, pc + 0x24, 0x2C1E_0001);
        WriteInstruction(memory, pc + 0x28, 0x4182_0080);
        WriteInstruction(memory, pc + 0x2C, 0x806D_8A90);
        WriteInstruction(memory, pc + 0x30, 0x5480_023E);
        WriteInstruction(memory, pc + 0x34, 0x3AB7_0000);
        WriteInstruction(memory, pc + 0x38, 0x7E83_0214);
        WriteSonicNormalizedStringScan(memory, pc + 0x40);
        WriteInstruction(memory, pc + 0xA8, 0x800D_8A8C);
        WriteInstruction(memory, pc + 0xAC, 0x7C60_E214);
        WriteInstruction(memory, pc + 0xE0, 0x806D_8A8C);
        WriteInstruction(memory, pc + 0xE4, 0x3803_0008);
        WriteInstruction(memory, pc + 0xE8, 0x7C1D_002E);
        WriteInstruction(memory, pc + 0xEC, 0x7C1A_0040);
        WriteInstruction(memory, pc + 0xF0, 0x4180_FF10);
        WriteInstruction(memory, pc + 0xF4, 0x3860_FFFF);
        WriteSonicByteTableLookup(memory, pc + 0x1CB80, table);
    }

    private static void WriteSonicPathEntry(GameCubeMemory memory, uint entryTable, uint index, uint word0, uint parentIndex, uint endIndex)
    {
        uint address = entryTable + index * 12;
        memory.Write32(address, word0);
        memory.Write32(address + 4, parentIndex);
        memory.Write32(address + 8, endIndex);
    }

    private static PowerPcState CreateSonicGxDrawBeginState(GameCubeBus bus, uint pc, uint stack, uint stateBlock)
    {
        WriteSonicGxDrawBegin(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8011_D608,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[3] = 0x98;
        state.Gpr[4] = 0;
        state.Gpr[5] = 0x0123;
        state.Gpr[13] = 0x803B_52C0;
        state.Gpr[29] = 0xAAAA_0001;
        state.Gpr[30] = 0xBBBB_0002;
        state.Gpr[31] = 0xCCCC_0003;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(stateBlock, 1);
        bus.Memory.Write32(stateBlock + 0x4F0, 0);
        return state;
    }

    private static PowerPcState CreateSonicGxTexObjLoadNoCallbackState(
        GameCubeBus bus,
        uint pc,
        uint stack,
        uint textureObject,
        uint samplerObject,
        uint stateBlock,
        uint textureMap)
    {
        WriteSonicGxTexObjLoadNoCallback(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8010_3988,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[3] = textureObject;
        state.Gpr[4] = samplerObject;
        state.Gpr[5] = textureMap;
        state.Gpr[13] = 0x803B_52C0;
        state.Gpr[28] = 0xAAAA_0001;
        state.Gpr[29] = 0xBBBB_0002;
        state.Gpr[30] = 0xCCCC_0003;
        state.Gpr[31] = 0xDDDD_0004;
        state.Spr[22] = 0xFFFF_F000;

        uint[] tableBases =
        [
            state.Gpr[13] - 31848u,
            state.Gpr[13] - 31840u,
            state.Gpr[13] - 31832u,
            state.Gpr[13] - 31824u,
            state.Gpr[13] - 31816u,
            state.Gpr[13] - 31808u,
        ];
        for (int table = 0; table < tableBases.Length; table++)
        {
            bus.Memory.Write8(tableBases[table] + textureMap, (byte)(0x90 + table));
        }

        bus.Memory.Write32(textureObject + 0x00, 0x0011_2233);
        bus.Memory.Write32(textureObject + 0x04, 0x0044_5566);
        bus.Memory.Write32(textureObject + 0x08, 0x0077_8899);
        bus.Memory.Write32(textureObject + 0x0C, 0x00AA_BBCC);
        bus.Memory.Write32(textureObject + 0x18, 0x1234_5678);
        bus.Memory.Write8(textureObject + 0x1F, 0x02);
        bus.Memory.Write32(samplerObject + 0x00, 0x00DD_EEFF);
        bus.Memory.Write32(samplerObject + 0x04, 0x0001_0203);
        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(stateBlock + 0x4F0, 0x40);
        bus.Memory.Write16(stateBlock + 2, 0x1234);
        return state;
    }

    private static PowerPcState CreateSonicGxPackedStateSetterState(uint pc, uint mode)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8011_CC74,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = mode;
        state.Gpr[4] = 4;
        state.Gpr[5] = 5;
        state.Gpr[6] = 2;
        state.Gpr[13] = 0x803B_52C0;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicGxCpStateSetterState(GameCubeBus bus, uint pc, uint stateBlock)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8012_3454,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = 0x1234_56A5;
        state.Gpr[4] = 0xBBBB_0004;
        state.Gpr[5] = 0xCCCC_0005;
        state.Gpr[6] = 0xDDDD_0006;
        state.Gpr[7] = 0xEEEE_0007;
        state.Gpr[13] = 0x803B_52C0;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(stateBlock + 0x204, 0xF234_5670);
        bus.Memory.Write32(stateBlock + 0x4F0, 0x40);
        return state;
    }

    private static PowerPcState CreateSonicGxTevDefaultWrapperState(GameCubeBus bus, uint pc, uint stack, uint stateBlock)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8011_C9D4,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[3] = 0;
        state.Gpr[4] = 0;
        state.Gpr[5] = 0x1111_0005;
        state.Gpr[6] = 0x2222_0006;
        state.Gpr[7] = 0x3333_0007;
        state.Gpr[8] = 0x4444_0008;
        state.Gpr[9] = 0x5555_0009;
        state.Gpr[13] = 0x803B_52C0;
        state.Gpr[30] = 0xAAAA_0001;
        state.Gpr[31] = 0xBBBB_0002;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(stateBlock + 0x130, 0xDEAD_BEEF);
        bus.Memory.Write32(stateBlock + 0x170, 0xABCD_EF01);
        bus.Memory.Write16(stateBlock + 2, 0x1234);
        return state;
    }

    private static PowerPcState CreateSonicGxTevEnvSetterState(GameCubeBus bus, uint pc, uint stateBlock, uint tevStage, uint cachedAddress)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8012_3454,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = tevStage;
        state.Gpr[4] = 0x0000_000F;
        state.Gpr[5] = 0x0000_0008;
        state.Gpr[6] = 0x0000_000A;
        state.Gpr[7] = 0x0000_0007;
        state.Gpr[8] = 0x8888_0008;
        state.Gpr[9] = 0x9999_0009;
        state.Gpr[13] = 0x803B_52C0;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(cachedAddress, 0xDEAD_BEEF);
        bus.Memory.Write16(stateBlock + 2, 0x1234);
        return state;
    }

    private static PowerPcState CreateSonicGxTevColorEnvSetterTailState(GameCubeBus bus, uint pc, uint stateBlock, uint cachedAddress)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8010_4514,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[0] = 0;
        state.Gpr[3] = 0x61;
        state.Gpr[4] = 0x0000_00A0;
        state.Gpr[5] = 0xCC01_0000;
        state.Gpr[6] = 0xDEAD_F8EF;
        state.Gpr[7] = 0x0000_000F;
        state.Gpr[8] = 0x0000_0800;
        state.Gpr[9] = cachedAddress;
        state.Gpr[13] = 0x803B_52C0;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(cachedAddress, state.Gpr[6]);
        bus.Memory.Write16(stateBlock + 2, 0x1234);
        return state;
    }

    private static PowerPcState CreateSonicGxTevOpSetterState(GameCubeBus bus, uint pc, uint stateBlock, uint tevStage, uint cachedAddress, uint mode)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8010_4514,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = tevStage;
        state.Gpr[4] = mode;
        state.Gpr[5] = 0x0000_0003;
        state.Gpr[6] = 0x0000_0001;
        state.Gpr[7] = 0x0000_0001;
        state.Gpr[8] = 0x0000_0000;
        state.Gpr[9] = 0x9999_0009;
        state.Gpr[13] = 0x803B_52C0;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(cachedAddress, 0xDEAD_BEEF);
        bus.Memory.Write16(stateBlock + 2, 0x1234);
        return state;
    }

    private static PowerPcState CreateSonicGxIndexedStripDrawBeginState(
        GameCubeBus bus,
        uint pc,
        uint gxBeginPc,
        uint stack,
        uint stream,
        uint stateBlock,
        short stripCount)
    {
        WriteSonicGxIndexedStripDrawBegin(bus.Memory, pc, gxBeginPc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8123_4568,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[13] = 0x803B_52C0;
        state.Gpr[24] = stream;
        state.Gpr[29] = 0xAAAA_0001;
        state.Gpr[30] = 0xBBBB_0002;
        state.Gpr[31] = 0;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write16(stream, unchecked((ushort)stripCount));
        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(stateBlock, 1);
        bus.Memory.Write32(stateBlock + 0x4F0, 0);
        return state;
    }

    private static PowerPcState CreateSonicGxBeginDirectState(
        GameCubeBus bus,
        uint pc,
        uint stateBlock,
        uint word14,
        uint word18,
        byte vertexMatrix,
        byte normalMatrix)
    {
        WriteSonicGxBeginDirect(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8123_4568,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[0] = 0xAAAA_0000;
        state.Gpr[3] = 0xBBBB_0003;
        state.Gpr[4] = 0xCCCC_0004;
        state.Gpr[5] = 0xDDDD_0005;
        state.Gpr[6] = 0xEEEE_0006;
        state.Gpr[7] = 0xFFFF_0007;
        state.Gpr[8] = 0x9999_0008;
        state.Gpr[13] = 0x803B_52C0;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(stateBlock + 0x14, word14);
        bus.Memory.Write32(stateBlock + 0x18, word18);
        bus.Memory.Write8(stateBlock + 0x41C, vertexMatrix);
        bus.Memory.Write8(stateBlock + 0x41D, normalMatrix);
        bus.Memory.Write16(stateBlock + 2, 0x1234);
        return state;
    }

    private static PowerPcState CreateSonicGxVertexDescriptorSetterState(GameCubeBus bus, uint pc, uint stateBlock, uint descriptor, byte vertexMatrixFlag, byte normalMatrixFlag)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8012_0054,
            Cr = 0x8200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[0] = 0x1234_5678;
        state.Gpr[3] = descriptor;
        state.Gpr[4] = 1;
        state.Gpr[6] = 0xAAAA_0006;
        state.Gpr[7] = 0xBBBB_0007;
        state.Gpr[8] = 0xCCCC_0008;
        state.Gpr[9] = 0xDDDD_0009;
        state.Gpr[10] = 0xEEEE_000A;
        state.Gpr[13] = 0x803B_52C0;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(stateBlock + 0x14, 0xDEAD_BEEF);
        bus.Memory.Write32(stateBlock + 0x18, 0x89AB_CDEF);
        bus.Memory.Write32(stateBlock + 0x418, 0x0000_0007);
        bus.Memory.Write8(stateBlock + 0x41C, vertexMatrixFlag);
        bus.Memory.Write8(stateBlock + 0x41D, normalMatrixFlag);
        bus.Memory.Write32(stateBlock + 0x4F0, 0x40);
        return state;
    }

    private static PowerPcState CreateSonicGxVertexAttributeFlushState(GameCubeBus bus, uint pc, uint stack, uint stateBlock)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8010_1988,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[3] = 0xAAAA_0003;
        state.Gpr[4] = 0xBBBB_0004;
        state.Gpr[5] = 0xCCCC_0005;
        state.Gpr[6] = 0xDDDD_0006;
        state.Gpr[7] = 0xEEEE_0007;
        state.Gpr[8] = 0x1111_0008;
        state.Gpr[9] = 0x2222_0009;
        state.Gpr[10] = 0x3333_000A;
        state.Gpr[13] = 0x803B_52C0;
        state.Gpr[27] = 0xAAAA_0027;
        state.Gpr[28] = 0xBBBB_0028;
        state.Gpr[29] = 0xCCCC_0029;
        state.Gpr[30] = 0xDDDD_0030;
        state.Gpr[31] = 0xEEEE_0031;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(stateBlock + 0x02, 0x1234_5678);
        bus.Memory.Write32(stateBlock + 0x100, 0);
        bus.Memory.Write32(stateBlock + 0x120, 0);
        bus.Memory.Write32(stateBlock + 0x204, 0);
        bus.Memory.Write32(stateBlock + 0x0B8, 0xAAAA_5555);
        bus.Memory.Write32(stateBlock + 0x0D8, 0x1357_9BDF);
        bus.Memory.Write32(stateBlock + 0x45C, 0x0000_03C0);
        bus.Memory.Write32(stateBlock + 0x47C, 0x0000_0001);
        bus.Memory.Write32(stateBlock + 0x49C, 0);
        bus.Memory.Write32(stateBlock + 0x4DC, 0);
        return state;
    }

    private static PowerPcState CreateSonicGxIndexedStripBatchState(
        GameCubeBus bus,
        uint pc,
        uint gxBeginPc,
        uint stack,
        uint stream,
        uint vertexBase,
        uint stateBlock)
    {
        WriteSonicGxIndexedStripDrawBegin(bus.Memory, pc, gxBeginPc);
        WriteSonicGxIndexedStripTail(bus.Memory, pc + 0x84);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8123_4568,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[13] = 0x803B_52C0;
        state.Gpr[24] = stream;
        state.Gpr[25] = vertexBase;
        state.Gpr[26] = 2;
        state.Gpr[27] = 0xAAAA_0000;
        state.Gpr[28] = 0xBBBB_0000;
        state.Gpr[29] = 0xCCCC_0000;
        state.Gpr[30] = 0xDDDD_0000;
        state.Gpr[31] = 0;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write16(stream + 0x00, 2);
        bus.Memory.Write16(stream + 0x02, 1);
        bus.Memory.Write16(stream + 0x04, 0x1111);
        bus.Memory.Write16(stream + 0x06, 0x2222);
        bus.Memory.Write16(stream + 0x08, 2);
        bus.Memory.Write16(stream + 0x0A, 0x3333);
        bus.Memory.Write16(stream + 0x0C, 0x4444);
        bus.Memory.Write16(stream + 0x0E, 0xFFFF);
        bus.Memory.Write16(stream + 0x10, 3);
        bus.Memory.Write16(stream + 0x12, 0x5555);
        bus.Memory.Write16(stream + 0x14, 0x6666);
        WriteIndexedVertex(bus.Memory, vertexBase + 0x20, 1, 2, 3, 0x1122_3344);
        WriteIndexedVertex(bus.Memory, vertexBase + 0x40, 4, 5, 6, 0x5566_7788);
        WriteIndexedVertex(bus.Memory, vertexBase + 0x60, 7, 8, 9, 0x99AA_BBCC);
        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(stateBlock, 1);
        bus.Memory.Write32(stateBlock + 0x4F0, 0);
        return state;
    }

    private static void WriteIndexedVertex(GameCubeMemory memory, uint address, float x, float y, float z, uint color)
    {
        WriteSingle(memory, address + 0x00, x);
        WriteSingle(memory, address + 0x04, y);
        WriteSingle(memory, address + 0x08, z);
        memory.Write32(address + 0x18, color);
    }

    private static PowerPcState CreateSonicGxIndexedStripTailState(uint pc, uint remainingStrips)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8123_4568,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[26] = remainingStrips;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicGxIndexedStripEpilogueState(GameCubeBus bus, uint pc, uint stack)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0xDEAD_0000,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[11] = 0xAAAA_000B;
        state.Spr[22] = 0xFFFF_F000;

        for (int register = 24; register <= 31; register++)
        {
            state.Gpr[register] = 0xABCD_0000u + (uint)register;
            bus.Memory.Write32(stack + (uint)(24 + (register - 24) * sizeof(uint)), 0xCAFE_0000u + (uint)register);
        }

        bus.Memory.Write32(stack + 60, 0x8123_4568);
        return state;
    }

    private static PowerPcState CreateSonicGxCommandListTerminalState(GameCubeBus bus, uint pc, uint stack, uint stream, uint returnAddress)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8123_4568,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[11] = 0xABCD_000B;
        state.Gpr[20] = stream;
        state.Gpr[28] = 0xABCD_001C;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write16(stream, 0x00FF);
        bus.Memory.Write32(stack + 132, returnAddress);
        for (int register = 20; register <= 31; register++)
        {
            bus.Memory.Write32(stack + (uint)(80 + (register - 20) * sizeof(uint)), 0xCAFE_0000u + (uint)register);
        }

        return state;
    }

    private static PowerPcState CreateSonicGxCommandListFetchState(GameCubeBus bus, uint pc, uint stream, short command)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8011_D174,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[20] = stream;
        state.Gpr[28] = 0xABCD_001C;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write16(stream, unchecked((ushort)command));
        return state;
    }

    private static PowerPcState CreateSonicGxCommandDispatchState(uint pc, uint command)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[0] = 0xAAAA_0000;
        state.Gpr[24] = 0xBBBB_0018;
        state.Gpr[25] = command;
        state.Gpr[28] = 0x2500u | command;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicGprSaveRestoreTailState(GameCubeBus bus, uint pc, uint baseAddress, int firstRegister)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8012_3454,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[11] = baseAddress + (uint)((32 - firstRegister) * sizeof(uint));
        for (int register = firstRegister; register <= 31; register++)
        {
            state.Gpr[register] = 0xABCD_0000u + (uint)register;
            bus.Memory.Write32(baseAddress + (uint)((register - firstRegister) * sizeof(uint)), 0xCAFE_0000u + (uint)register);
        }

        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicGxAttributeStateSetterState(GameCubeBus bus, uint pc, uint stateBlock, uint parameter)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8012_0044,
            Cr = 0x2200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = 0;
        state.Gpr[4] = parameter;
        state.Gpr[5] = 1;
        state.Gpr[6] = 3;
        state.Gpr[7] = 8;
        state.Gpr[8] = 0xAAAA_0008;
        state.Gpr[9] = 0xBBBB_0009;
        state.Gpr[10] = 0xCCCC_000A;
        state.Gpr[13] = 0x803B_52C0;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(stateBlock + 0x1C, 0xCAFE_BABE);
        bus.Memory.Write8(stateBlock + 0x4EE, 0x02);
        bus.Memory.Write32(stateBlock + 0x4F0, 0x40);
        return state;
    }

    private static PowerPcState CreateSonicGxFloatStripEmitState(GameCubeBus bus, uint pc, uint stream, uint vertexBase, uint iterations)
    {
        WriteSonicGxFloatStripEmitLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8011_D608,
            Cr = 0x4400_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[26] = stream;
        state.Gpr[27] = vertexBase;
        state.Gpr[28] = 0x6E;
        state.Gpr[29] = vertexBase;
        state.Gpr[30] = iterations;
        state.Gpr[31] = 2;
        state.Fpr[1] = -10.0f;
        state.Fpr[2] = -11.0f;
        state.Fpr[3] = -12.0f;
        state.Spr[22] = 0xFFFF_F000;

        for (uint iteration = 0; iteration < iterations; iteration++)
        {
            uint streamCursor = stream + iteration * 4;
            bus.Memory.Write16(streamCursor, (ushort)iteration);
            bus.Memory.Write16(streamCursor + 2, (ushort)(0x8000u + iteration));

            uint vertex = vertexBase + iteration * 0x20;
            WriteSingle(bus.Memory, vertex + 0x00, 1.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x04, 2.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x08, 3.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x0C, 4.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x10, 5.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x14, 6.0f + iteration);
        }

        return state;
    }

    private static PowerPcState CreateSonicGxFloatAttributeStripEmitState(GameCubeBus bus, uint pc, uint stream, uint vertexBase, uint iterations)
    {
        WriteSonicGxFloatAttributeStripEmitLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8011_FF68,
            Cr = 0x4400_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[26] = stream;
        state.Gpr[27] = vertexBase;
        state.Gpr[28] = 0x6E;
        state.Gpr[29] = vertexBase;
        state.Gpr[30] = iterations;
        state.Gpr[31] = 2;
        state.Fpr[1] = -10.0f;
        state.Fpr[2] = -11.0f;
        state.Fpr[3] = -12.0f;
        state.Spr[22] = 0xFFFF_F000;

        for (uint iteration = 0; iteration < iterations; iteration++)
        {
            uint streamCursor = stream + iteration * 4;
            bus.Memory.Write16(streamCursor, (ushort)iteration);
            bus.Memory.Write16(streamCursor + 2, 0xCAFE);

            uint vertex = vertexBase + iteration * 0x20;
            WriteSingle(bus.Memory, vertex + 0x00, 1.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x04, 2.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x08, 3.0f + iteration);
            bus.Memory.Write32(vertex + 0x18, 0x9000_0000u + iteration);
        }

        return state;
    }

    private static PowerPcState CreateSonicGxFloatTexcoordStripEmitState(GameCubeBus bus, uint pc, uint stream, uint vertexBase, uint iterations)
    {
        WriteSonicGxFloatTexcoordStripEmitLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8011_D858,
            Cr = 0x4400_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[25] = stream;
        state.Gpr[26] = vertexBase;
        state.Gpr[27] = 0x6E;
        state.Gpr[28] = vertexBase;
        state.Gpr[29] = 0xEEEE_0004;
        state.Gpr[30] = 0xDDDD_0003;
        state.Gpr[31] = iterations;
        state.Fpr[1] = -10.0f;
        state.Fpr[2] = -11.0f;
        state.Fpr[3] = -12.0f;
        state.Spr[22] = 0xFFFF_F000;

        for (uint iteration = 0; iteration < iterations; iteration++)
        {
            uint streamCursor = stream + iteration * 6;
            bus.Memory.Write16(streamCursor, (ushort)iteration);
            bus.Memory.Write16(streamCursor + 2, (ushort)(0x1000u + iteration));
            bus.Memory.Write16(streamCursor + 4, (ushort)(0x2000u + iteration));

            uint vertex = vertexBase + iteration * 0x20;
            WriteSingle(bus.Memory, vertex + 0x00, 1.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x04, 2.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x08, 3.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x0C, 4.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x10, 5.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x14, 6.0f + iteration);
        }

        return state;
    }

    private static PowerPcState CreateSonicGxFloatTexcoordStripEmitHeaderState(
        GameCubeBus bus,
        uint pc,
        uint stream,
        uint vertexBase,
        uint stack,
        uint stateBlock,
        uint iterations)
    {
        const uint loopPc = 0x8011_D860;
        WriteSonicGxFloatTexcoordStripEmitLoop(bus.Memory, loopPc);
        WriteSonicGxDrawBegin(bus.Memory, 0x8010_1948);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8011_D858,
            Cr = 0x4400_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[13] = 0x803B_52C0;
        state.Gpr[25] = stream;
        state.Gpr[26] = vertexBase;
        state.Gpr[27] = 0x6E;
        state.Gpr[28] = vertexBase;
        state.Gpr[29] = 0xEEEE_0004;
        state.Gpr[30] = 0xDDDD_0003;
        state.Gpr[31] = 0xCCCC_0002;
        state.Fpr[1] = -10.0f;
        state.Fpr[2] = -11.0f;
        state.Fpr[3] = -12.0f;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(state.Gpr[13] - 31872u, stateBlock);
        bus.Memory.Write32(stateBlock, 1);
        bus.Memory.Write32(stateBlock + 0x4F0, 0);
        bus.Memory.Write16(stream, (ushort)iterations);
        for (uint iteration = 0; iteration < iterations; iteration++)
        {
            uint streamCursor = stream + sizeof(ushort) + iteration * 6;
            bus.Memory.Write16(streamCursor, (ushort)iteration);
            bus.Memory.Write16(streamCursor + 2, (ushort)(0x1000u + iteration));
            bus.Memory.Write16(streamCursor + 4, (ushort)(0x2000u + iteration));

            uint vertex = vertexBase + iteration * 0x20;
            WriteSingle(bus.Memory, vertex + 0x00, 1.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x04, 2.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x08, 3.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x0C, 4.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x10, 5.0f + iteration);
            WriteSingle(bus.Memory, vertex + 0x14, 6.0f + iteration);
        }

        return state;
    }

    private static PowerPcState CreateSonicGxFloatTexcoordStripEmitTailState(uint pc, uint remaining)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = pc,
            Cr = 0x4400_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[27] = remaining;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicPairedTransform2dState(GameCubeBus bus, uint pc, uint input, uint output, uint iterations)
    {
        WriteSonicPairedTransform2dLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8012_3456,
            Ctr = iterations,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[5] = output;
        state.Gpr[6] = input;
        state.Gpr[7] = iterations;
        state.Gpr[8] = 0x1122_3344;
        state.Gpr[10] = 0xAABB_CCDD;
        state.Spr[22] = 0xFFFF_F000;

        for (int register = 0; register <= 13; register++)
        {
            state.Fpr[register] = (float)(register * 0.25f - 1.5f);
            state.FprPair1[register] = (float)(register * -0.125f + 2.0f);
        }

        state.Fpr[8] = -0.75f;
        state.FprPair1[8] = 1.25f;
        state.Fpr[9] = 0.5f;
        state.FprPair1[9] = 1.0f;
        state.Fpr[12] = 9.5f;
        state.FprPair1[12] = -2.25f;
        state.Fpr[13] = 3.75f;
        state.FprPair1[13] = 1.0f;

        for (uint iteration = 0; iteration < iterations; iteration++)
        {
            uint cursor = input + iteration * 16;
            WriteSingle(bus.Memory, cursor + 4, 0.125f + iteration);
            WriteSingle(bus.Memory, cursor + 8, -0.25f - iteration);
            WriteSingle(bus.Memory, cursor + 12, 2.5f + iteration);
            bus.Memory.Write32(cursor + 16, 0x0102_0304u + iteration * 0x1111_1111u);
        }

        return state;
    }

    private static PowerPcState CreateSonicPairedTransform4dState(GameCubeBus bus, uint pc, uint input, uint output, uint stack, uint iterations)
    {
        WriteSonicPairedTransform4dLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8012_3456,
            Ctr = iterations,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[5] = output;
        state.Gpr[6] = input;
        state.Gpr[7] = iterations;
        state.Spr[22] = 0xFFFF_F000;

        for (int register = 0; register <= 18; register++)
        {
            state.Fpr[register] = (float)(register * 0.25f - 1.5f);
            state.FprPair1[register] = (float)(register * -0.125f + 2.0f);
        }

        state.Fpr[8] = -0.75f;
        state.FprPair1[8] = 1.25f;
        state.Fpr[9] = 0.5f;
        state.FprPair1[9] = 1.0f;
        state.Fpr[10] = 0.375f;
        state.FprPair1[10] = -0.625f;
        state.Fpr[15] = 9.5f;
        state.FprPair1[15] = -2.25f;
        state.Fpr[16] = 3.75f;
        state.FprPair1[16] = 1.0f;
        state.Fpr[17] = -4.25f;
        state.FprPair1[17] = 0.75f;
        state.Fpr[18] = 6.5f;
        state.FprPair1[18] = -1.5f;

        WriteDouble(bus.Memory, stack + 8, 100.25);
        WriteDouble(bus.Memory, stack + 16, 101.25);
        WriteDouble(bus.Memory, stack + 24, 102.25);
        WriteDouble(bus.Memory, stack + 32, 103.25);
        WriteDouble(bus.Memory, stack + 40, 104.25);

        for (uint iteration = 0; iteration <= iterations; iteration++)
        {
            uint cursor = input + iteration * 24;
            WriteSingle(bus.Memory, cursor + 8, 0.125f + iteration);
            WriteSingle(bus.Memory, cursor + 12, -0.25f - iteration);
            WriteSingle(bus.Memory, cursor + 16, 2.5f + iteration);
            WriteSingle(bus.Memory, cursor + 20, -3.5f - iteration);
            WriteSingle(bus.Memory, cursor + 24, 4.5f + iteration);
            WriteSingle(bus.Memory, cursor + 28, -5.5f - iteration);
        }

        return state;
    }

    private static PowerPcState CreateSonicPairedTransform4dIndexedOutputState(GameCubeBus bus, uint input, uint outputBase, uint stack, uint iterations)
    {
        WriteSonicPairedTransform4dIndexedOutputLoop(bus.Memory);
        PowerPcState state = new()
        {
            Pc = 0x8011_DE54,
            Lr = 0x8012_3456,
            Ctr = iterations,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[4] = outputBase + 0x20;
        state.Gpr[6] = outputBase;
        state.Gpr[7] = input;
        state.Gpr[8] = iterations * 4;
        state.Gpr[9] = outputBase;
        state.Gpr[10] = 0x20;
        state.Spr[22] = 0xFFFF_F000;
        state.Spr[913] = 0x0807_0807;

        for (int register = 0; register <= 23; register++)
        {
            state.Fpr[register] = (float)(register * 0.1875f - 1.25f);
            state.FprPair1[register] = (float)(register * -0.15625f + 1.75f);
        }

        state.Fpr[8] = -0.75f;
        state.FprPair1[8] = 1.25f;
        state.Fpr[9] = 0.5f;
        state.FprPair1[9] = 1.0f;
        state.Fpr[10] = 0.375f;
        state.FprPair1[10] = -0.625f;
        state.Fpr[15] = 9.5f;
        state.FprPair1[15] = -2.25f;
        state.Fpr[16] = 3.75f;
        state.FprPair1[16] = 1.0f;
        state.Fpr[17] = -4.25f;
        state.FprPair1[17] = 0.75f;
        state.Fpr[18] = 6.5f;
        state.FprPair1[18] = -1.5f;
        state.Fpr[19] = 0.75f;
        state.FprPair1[19] = 1.0f;

        for (int register = 14; register <= 23; register++)
        {
            WriteDouble(bus.Memory, stack + 8u + (uint)(register - 14) * sizeof(double), 100.25 + register);
        }

        for (uint slot = 0; slot <= 6; slot++)
        {
            uint cursor = outputBase + slot * 0x20;
            WriteSingle(bus.Memory, cursor + 0, 0.125f + slot);
            WriteSingle(bus.Memory, cursor + 4, -0.25f - slot);
            WriteSingle(bus.Memory, cursor + 8, 1.5f + slot);
            WriteSingle(bus.Memory, cursor + 12, -2.5f - slot);
            WriteSingle(bus.Memory, cursor + 16, 3.5f + slot);
            WriteSingle(bus.Memory, cursor + 20, -4.5f - slot);
        }

        for (uint iteration = 0; iteration < iterations; iteration++)
        {
            uint cursor = input + iteration * 28;
            WriteSingle(bus.Memory, cursor + 2, 0.125f + iteration);
            WriteSingle(bus.Memory, cursor + 6, -0.25f - iteration);
            WriteSingle(bus.Memory, cursor + 10, 0.5f + iteration);
            WriteSingle(bus.Memory, cursor + 14, -0.75f - iteration);
            WriteSingle(bus.Memory, cursor + 18, 1.0f + iteration);
            WriteSingle(bus.Memory, cursor + 22, -1.25f - iteration);
            WriteQuantizedS16(bus.Memory, cursor + 26, 0.75f);
            bus.Memory.Write16(cursor + 28, (ushort)((iteration + 2) * 0x20 / 0x20));
        }

        return state;
    }

    private static PowerPcState CreateSonicVectorBlendCopyState(GameCubeBus bus, uint pc, uint input, uint output, uint blendA, uint blendB, uint stack, uint iterations)
    {
        WriteSonicVectorBlendCopyLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8012_3456,
            Ctr = iterations,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[4] = input;
        state.Gpr[7] = output;
        state.Gpr[9] = iterations;
        state.Gpr[10] = 1;
        state.Gpr[11] = 1;
        state.Gpr[12] = 0;
        state.Gpr[25] = 0;
        state.Gpr[26] = 0x8123_4568;
        state.Gpr[27] = 0;
        state.Gpr[28] = 0x8010_0000;
        state.Gpr[29] = blendB;
        state.Gpr[30] = 0x8010_1000;
        state.Gpr[31] = blendA;
        state.Spr[22] = 0xFFFF_F000;

        for (int register = 0; register < state.Fpr.Length; register++)
        {
            state.Fpr[register] = (float)(register * 0.25f - 1.5f);
            state.FprPair1[register] = (float)(register * -0.125f + 2.0f);
        }

        state.Fpr[31] = 0.625f;
        state.FprPair1[31] = 0.625f;
        WriteSingle(bus.Memory, stack + 0x38, -0.375f);

        for (uint iteration = 0; iteration < iterations; iteration++)
        {
            for (uint lane = 0; lane < 3; lane++)
            {
                uint offset = iteration * 12 + lane * sizeof(uint);
                WriteSingle(bus.Memory, blendA + offset, 0.25f + iteration * 0.5f + lane * 0.125f);
                WriteSingle(bus.Memory, blendB + offset, -1.5f + iteration * 0.25f - lane * 0.25f);
                WriteSingle(bus.Memory, input + iteration * 24 + 12 + lane * sizeof(uint), 4.0f + iteration + lane * 0.5f);
            }
        }

        return state;
    }

    private static PowerPcState CreateSonicCoordinatePairFillState(
        GameCubeBus bus,
        uint pc,
        uint output,
        uint iterations,
        int columnLimit,
        int column,
        int row)
    {
        WriteSonicCoordinatePairFillLoop(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Ctr = iterations,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = unchecked((uint)columnLimit);
        state.Gpr[4] = unchecked((uint)column);
        state.Gpr[5] = output;
        state.Gpr[6] = unchecked((uint)row);
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicBufferFillState(
        uint pc,
        uint descriptor,
        uint output,
        uint index,
        uint fillValue,
        int indexRegister,
        int cursorRegister,
        int valueRegister)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[29] = descriptor;
        state.Gpr[indexRegister] = index;
        state.Gpr[cursorRegister] = output;
        state.Gpr[valueRegister] = fillValue;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicModeStateUpdateState(uint input)
    {
        PowerPcState state = new()
        {
            Pc = 0x800E_2E20,
            Lr = 0x8000_1234,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = input;
        state.Gpr[13] = 0x8010_7684;
        state.Ctr = 0x1234_5678;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicModeCoordinatorPrologueState(uint stack)
    {
        PowerPcState state = new()
        {
            Pc = 0x800E_30CC,
            Lr = 0x8000_4568,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        for (int register = 26; register <= 31; register++)
        {
            state.Gpr[register] = 0xCAFE_0000u + (uint)register;
        }

        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicModeCoordinatorBodyState(uint pc, uint stateWord)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_4568,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
            Msr = 0x0000_8000,
        };
        state.Gpr[0] = stateWord;
        state.Gpr[1] = 0x817F_EFD8;
        state.Gpr[3] = 0x801C_C168;
        state.Gpr[13] = 0x8010_0000;
        state.Gpr[26] = 0xCAFE_001A;
        state.Gpr[27] = 0xCAFE_001B;
        state.Gpr[28] = 0;
        state.Gpr[29] = 0;
        state.Gpr[30] = 0xCAFE_001E;
        state.Gpr[31] = 0x801C_C1E8;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicModeCoordinatorZeroTailState(uint stack)
    {
        PowerPcState state = new()
        {
            Pc = 0x800E_3170,
            Lr = 0x800E_3170,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[0] = 0xCAFE_0000;
        state.Gpr[1] = stack;
        state.Gpr[3] = 0xDEAD_0003;
        for (int register = 26; register <= 31; register++)
        {
            state.Gpr[register] = 0xBEEF_0000u + (uint)register;
        }

        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicStatusQueryState(uint stack, uint returnPc, uint queryId)
    {
        PowerPcState state = new()
        {
            Pc = 0x8013_6CEC,
            Lr = returnPc,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[3] = queryId;
        state.Gpr[13] = 0x8010_0000;
        for (int register = 26; register <= 31; register++)
        {
            state.Gpr[register] = 0xCAFE_0000u + (uint)register;
        }

        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static void SetupSonicStatusQueryData(GameCubeMemory memory, uint currentId, uint currentPointer, sbyte status)
    {
        memory.Write32(0x800F_9074, currentId);
        memory.Write32(0x800F_9078, currentPointer);
        memory.Write8(currentPointer + 1, unchecked((byte)status));
    }

    private static PowerPcState CreateSonicStatusCallerLoopState()
    {
        PowerPcState state = new()
        {
            Pc = 0x8001_2B24,
            Lr = 0x8000_2220,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
            Msr = 0x0000_8000,
        };
        state.Gpr[1] = 0x817F_F000;
        state.Gpr[13] = 0x8010_0000;
        state.Gpr[29] = 0x8010_7000;
        for (int register = 26; register <= 31; register++)
        {
            state.Gpr[register] = 0xCAFE_0000u + (uint)register;
        }

        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicTableByteBuildDispatchState(uint pc, uint sourceRecord, uint output, uint r3, uint r29)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8123_4568,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[3] = r3;
        state.Gpr[28] = sourceRecord;
        state.Gpr[29] = r29;
        state.Gpr[30] = output;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicLineCopyState(uint pc, uint source, uint output, byte currentByte)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[3] = source;
        state.Gpr[5] = currentByte;
        state.Gpr[6] = output;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicLineSkipState(uint source)
    {
        PowerPcState state = new()
        {
            Pc = 0x8013_A39C,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[26] = source;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicStringAppendScanState(uint destination)
    {
        PowerPcState state = new()
        {
            Pc = 0x8010_DF90,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[5] = unchecked(destination - 1);
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicFreeBlockScanState(uint currentBlock, uint nextBlock, uint requestSize)
    {
        PowerPcState state = new()
        {
            Pc = 0x8013_9C94,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[3] = 0xCAFE_3000;
        state.Gpr[4] = nextBlock;
        state.Gpr[5] = requestSize;
        state.Gpr[29] = currentBlock;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicCacheStoreSweepState(uint iterations)
    {
        PowerPcState state = new()
        {
            Pc = 0x800E_4F88,
            Ctr = iterations,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[3] = 0x8000_0000;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicStateZeroFillState(uint destination, uint iterations)
    {
        PowerPcState state = new()
        {
            Pc = 0x800F_982C,
            Ctr = iterations,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[0] = 0;
        state.Gpr[3] = destination;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicManagerSlotScanState(uint table, int startSlot)
    {
        PowerPcState state = new()
        {
            Pc = 0x8012_4F50,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[30] = (uint)startSlot;
        state.Gpr[31] = table + (uint)startSlot * 1080;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicTaskEntryScanState(uint table, int startEntry)
    {
        PowerPcState state = new()
        {
            Pc = 0x8013_03B0,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[30] = (uint)startEntry;
        state.Gpr[31] = table + (uint)startEntry * 156;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicObjectSlotScanState(uint table, int startSlot)
    {
        PowerPcState state = new()
        {
            Pc = 0x8013_38DC,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[30] = (uint)startSlot;
        state.Gpr[31] = table + (uint)startSlot * 164;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicHalfwordChecksumState(uint pc, uint source, int sourceRegister, uint iterations)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Ctr = iterations,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[sourceRegister] = source;
        state.Gpr[10] = 0x1000_0000;
        state.Gpr[11] = 0x2000_0000;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicInitTableNullEntryState(uint pc, uint cursor, uint index)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[25] = 0x8020_1000;
        state.Gpr[26] = 0x8020_2000;
        state.Gpr[28] = 0x8020_3000;
        state.Gpr[29] = index;
        state.Gpr[31] = cursor;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicNullSlotScanState(uint table, uint returnPc, uint count)
    {
        PowerPcState state = new()
        {
            Pc = 0x8011_6BBC,
            Lr = returnPc,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
            Ctr = count,
        };
        state.Gpr[3] = 0x8123_4560;
        state.Gpr[4] = table;
        state.Gpr[5] = 0xCAFE_0005;
        state.Gpr[6] = 0;
        state.Gpr[13] = 0x8010_0000;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static void SetupSonicNullSlotScanData(GameCubeMemory memory, uint table, uint[] slotPointers)
    {
        for (int index = 0; index < slotPointers.Length; index++)
        {
            uint slot = table + (uint)index * 0x18;
            memory.Write32(slot + 0x0C, slotPointers[index]);
            if (slotPointers[index] != 0)
            {
                memory.Write32(slotPointers[index], 0x8123_4560);
            }
        }
    }

    private static PowerPcState CreateSonicPoolNullSlotScanState(uint table, uint output, uint count)
    {
        PowerPcState state = new()
        {
            Pc = 0x8011_6C18,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
            Ctr = count,
        };
        state.Gpr[0] = 0xCAFE_0000;
        state.Gpr[3] = output;
        state.Gpr[5] = 0;
        state.Gpr[6] = table;
        state.Gpr[13] = 0x8010_0000;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static void SetupSonicPoolNullSlotScanData(GameCubeMemory memory, uint table, uint[] slotPointers)
    {
        for (int index = 0; index < slotPointers.Length; index++)
        {
            uint slot = table + (uint)index * 0x18;
            memory.Write32(slot + 0x0C, slotPointers[index]);
        }
    }

    private static PowerPcState CreateSonicPoolSentinelSlotScanState(uint table, uint output, uint count)
    {
        PowerPcState state = new()
        {
            Pc = 0x8011_6C8C,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
            Ctr = count,
        };
        state.Gpr[0] = 0xCAFE_0000;
        state.Gpr[3] = 0xCAFE_0003;
        state.Gpr[4] = output;
        state.Gpr[5] = table;
        state.Gpr[13] = 0x8010_0000;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static void SetupSonicPoolSentinelSlotScanData(GameCubeMemory memory, uint table, uint[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            uint slot = table + (uint)index * 0x28;
            memory.Write32(slot, values[index]);
        }
    }

    private static PowerPcState CreateSonicTableKeyScanState(uint tableHolder, uint target, uint count)
    {
        PowerPcState state = new()
        {
            Pc = 0x8011_90B8,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
            Ctr = count,
        };
        state.Gpr[0] = 0xCAFE_0000;
        state.Gpr[3] = 0xCAFE_0003;
        state.Gpr[4] = 0;
        state.Gpr[13] = 0x8010_0000;
        state.Gpr[28] = 0;
        state.Gpr[29] = target;
        state.Gpr[31] = tableHolder;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static void SetupSonicTableKeyScanData(GameCubeMemory memory, uint tableHolder, uint table, ushort[] keys)
    {
        memory.Write32(tableHolder, table);
        for (int index = 0; index < keys.Length; index++)
        {
            uint entry = table + (uint)index * 12;
            memory.Write32(entry + 4, 0xCAFE_0000u | keys[index]);
        }
    }

    private static PowerPcState CreateSonicModeRefreshDispatchState(uint pc, uint smallDataBase, uint r3)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8123_0000,
            Cr = 0x1234_5678,
            TimeBase = 0x1000,
        };
        state.Gpr[0] = 0xCAFE_0000;
        state.Gpr[3] = r3;
        state.Gpr[13] = smallDataBase;
        state.Gpr[28] = 0xCAFE_001C;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static void SetupSonicModeRefreshDispatchData(GameCubeMemory memory, uint smallDataBase, uint objectPointerHolder, uint objectPointer)
    {
        memory.Write32(unchecked(smallDataBase + 0xFFFF_8F04u), 0);
        memory.Write32(unchecked(smallDataBase + 0xFFFF_8B00u), 7);
        memory.Write32(unchecked(smallDataBase + 0xFFFF_8F0Cu), objectPointerHolder);
        memory.Write32(objectPointerHolder, objectPointer);
    }

    private static PowerPcState CreateSonicBitPlaneInnerExpandState(uint pc, uint source, uint destination, uint ctr)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
            Ctr = ctr,
        };
        state.Gpr[0] = 0;
        state.Gpr[7] = destination;
        state.Gpr[10] = 0xCAFE_000A;
        state.Gpr[11] = 0xCAFE_000B;
        state.Gpr[31] = source;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicModeQueryState(uint msr)
    {
        PowerPcState state = new()
        {
            Pc = 0x800F_13A8,
            Lr = 0x8000_5678,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
            Msr = msr,
        };
        state.Gpr[1] = 0x817F_F000;
        state.Gpr[13] = 0x8010_0000;
        state.Gpr[30] = 0xAAAA_0030;
        state.Gpr[31] = 0xBBBB_0031;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static void SetupSonicModeQueryData(GameCubeMemory memory, uint busyFlag, uint fallbackFlag, uint pointer, uint mode)
    {
        memory.Write32(0x800F_8AC0, busyFlag);
        memory.Write32(0x800F_8AB8, fallbackFlag);
        memory.Write32(0x800F_8AA8, pointer);
        if (pointer != 0 && pointer != 0x802B_B700)
        {
            memory.Write32(pointer + 0x0C, mode);
        }
    }

    private static PowerPcState CreateSonicModeChildStatusPollState()
    {
        PowerPcState state = new()
        {
            Pc = 0x800E_2C0C,
            Lr = 0x8000_6788,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = 0x817F_F000;
        state.Gpr[13] = 0x8010_7AF0;
        state.Gpr[31] = 0xCAFE_BABE;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static void SetupSonicModeChildStatusPollData(GameCubeMemory memory, uint[] children, short[] statuses)
    {
        const uint statusPointerAddress = 0x8010_0000;
        const uint root = 0x8010_1000;
        if (children.Length == 0)
        {
            memory.Write32(statusPointerAddress, 0);
            return;
        }

        memory.Write32(statusPointerAddress, root);
        uint[] offsets = [0x10, 0x34, 0x50];
        for (int index = 0; index < offsets.Length; index++)
        {
            uint child = children[index];
            memory.Write32(root + offsets[index], child);
            if (child == 0)
            {
                continue;
            }

            memory.Write16(child + 0x60, unchecked((ushort)statuses[index]));
            memory.Write32(child + 0x64, 0xAAAA_0000u + (uint)index);
            memory.Write16(child + 0x68, 0xBBBB);
            memory.Write16(child + 0x6A, 0xCCCC);
            memory.Write32(child + 0x6C, 0xDDDD_0000u + (uint)index);
        }
    }

    private static PowerPcState CreateSonicGeneratedRangeScanState(GameCubeBus bus, uint pc, uint table, uint stack, int index, int group)
    {
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x80BC_B7FC,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[30] = unchecked((uint)group);
        state.Gpr[31] = unchecked((uint)index);
        state.Spr[22] = 0xFFFF_F000;
        state.Fpr[0] = 123.0;
        state.FprPair1[0] = 123.0;
        state.Fpr[1] = -456.0;
        state.FprPair1[1] = -456.0;
        bus.Memory.Write32(0x80BD_4F58, table);
        WriteDouble(bus.Memory, 0x80BD_3120, BitConverter.UInt64BitsToDouble(0x4330_0000_8000_0000));
        WriteSingle(bus.Memory, 0x80BE_CA08, 0.0f);
        WriteSingle(bus.Memory, 0x80BE_CA10, 0.0f);
        return state;
    }

    private static PowerPcState CreateSonicGeneratedSlotMismatchScanState(
        GameCubeBus bus,
        uint tableBase,
        uint groupBase,
        uint slot,
        uint count,
        uint target)
    {
        const uint group = 1;
        const uint groupOffset = group << 5;
        PowerPcState state = new()
        {
            Pc = 0x80BC_7288,
            Lr = 0x80BC_7EA4,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[26] = target;
        state.Gpr[30] = group;
        state.Gpr[31] = slot;
        state.Spr[22] = 0xFFFF_F000;
        bus.Memory.Write32(0x80BE_CA1C, tableBase);
        bus.Memory.Write32(tableBase + groupOffset, groupBase);
        bus.Memory.Write32(tableBase + groupOffset + sizeof(uint), count);
        return state;
    }

    private static PowerPcState CreateSonicStartCodeScanState(GameCubeBus bus, uint input, uint length)
    {
        WriteSonicStartCodeScan(bus.Memory);
        PowerPcState state = new()
        {
            Pc = 0x8014_93B8,
            Lr = 0x8014_A414,
            Cr = 0x4200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = input;
        state.Gpr[4] = 0;
        state.Gpr[29] = input;
        state.Gpr[30] = 0xFFFF_FFFF;
        state.Gpr[31] = input + length;
        state.Spr[22] = 0xFFFF_F000;
        return state;
    }

    private static PowerPcState CreateSonicGeneratedModelPointerScanState(
        GameCubeBus bus,
        uint pc,
        uint tableContainer,
        uint pointerTable,
        uint stack,
        uint inputPointer)
    {
        WriteSonicGeneratedModelPointerScan(bus.Memory, pc);
        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x80BC_7650,
            Cr = 0x8200_0088,
            Xer = 0x2000_0000,
        };
        state.Gpr[1] = stack;
        state.Gpr[3] = inputPointer;
        state.Gpr[30] = 0xAAAA_0001;
        state.Gpr[31] = 0xBBBB_0002;
        state.Spr[22] = 0xFFFF_F000;

        bus.Memory.Write32(0x80BD_3668, tableContainer);
        bus.Memory.Write32(tableContainer + 0x20, pointerTable);
        for (uint index = 0; index < 18; index++)
        {
            bus.Memory.Write32(pointerTable + index * 20, 0x8136_0000u + index * 0x100);
        }

        return state;
    }

    private static void WriteSonicGeneratedRangeValue(GameCubeMemory memory, uint table, int index, int stride, int group, int groupStride, uint baseOffset, int value)
    {
        uint offset = unchecked((uint)(group * groupStride + index * stride) + baseOffset);
        memory.Write32(table + offset, unchecked((uint)value));
    }

    private static void WriteSonicGeneratedSlotCompareValue(GameCubeMemory memory, uint groupBase, uint slot, uint value)
    {
        memory.Write32(groupBase + slot * 44 + 40, value);
    }

    private static void WriteSonicPairedTransform2dLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc - 0x78, 0x80A3_0000);
        WriteInstruction(memory, pc - 0x74, 0x80C3_0004);
        WriteInstruction(memory, pc - 0x70, 0x80E3_0008);
        WriteInstruction(memory, pc - 0x6C, 0x38E7_FFFF);
        WriteInstruction(memory, pc - 0x68, 0x7CE9_03A6);
        WriteInstruction(memory, pc - 0x64, 0xE004_0000);
        WriteInstruction(memory, pc - 0x60, 0x38C6_FFFC);
        WriteInstruction(memory, pc - 0x5C, 0xE024_8008);
        WriteInstruction(memory, pc - 0x58, 0x38A5_FFF8);
        WriteInstruction(memory, pc - 0x54, 0xE0C4_0024);
        WriteInstruction(memory, pc - 0x50, 0xE0E4_802C);
        WriteInstruction(memory, pc - 0x4C, 0xE506_0004);
        WriteInstruction(memory, pc - 0x48, 0xE526_8008);
        WriteInstruction(memory, pc - 0x44, 0x8506_0004);
        WriteInstruction(memory, pc - 0x40, 0x1140_321C);
        WriteInstruction(memory, pc - 0x3C, 0xE044_000C);
        WriteInstruction(memory, pc - 0x38, 0x1161_3A1C);
        WriteInstruction(memory, pc - 0x34, 0xE064_8014);
        WriteInstruction(memory, pc - 0x30, 0x550A_403E);
        WriteInstruction(memory, pc - 0x2C, 0xE0A4_8020);
        WriteInstruction(memory, pc - 0x28, 0xE084_0018);
        WriteInstruction(memory, pc - 0x24, 0x1142_521E);
        WriteInstruction(memory, pc - 0x20, 0x1163_5A1E);
        WriteInstruction(memory, pc - 0x1C, 0x1184_525C);
        WriteInstruction(memory, pc - 0x18, 0x11A5_5A5C);
        WriteInstruction(memory, pc - 0x14, 0xE506_0004);
        WriteInstruction(memory, pc - 0x10, 0xE526_8008);
        WriteInstruction(memory, pc - 0x0C, 0x8506_0004);
        WriteInstruction(memory, pc - 0x08, 0x2C07_0000);
        WriteInstruction(memory, pc - 0x04, 0x4182_003C);
        WriteInstruction(memory, pc + 0x00, 0x1140_321C);
        WriteInstruction(memory, pc + 0x04, 0xF585_0008);
        WriteInstruction(memory, pc + 0x08, 0x1161_3A1C);
        WriteInstruction(memory, pc + 0x0C, 0xF5A5_8008);
        WriteInstruction(memory, pc + 0x10, 0x1142_521E);
        WriteInstruction(memory, pc + 0x14, 0x9545_0010);
        WriteInstruction(memory, pc + 0x18, 0x1163_5A1E);
        WriteInstruction(memory, pc + 0x1C, 0xE506_0004);
        WriteInstruction(memory, pc + 0x20, 0x1184_525C);
        WriteInstruction(memory, pc + 0x24, 0x11A5_5A5C);
        WriteInstruction(memory, pc + 0x28, 0xE526_8008);
        WriteInstruction(memory, pc + 0x2C, 0x550A_403E);
        WriteInstruction(memory, pc + 0x30, 0x8506_0004);
        WriteInstruction(memory, pc + 0x34, 0x4200_FFCC);
        WriteInstruction(memory, pc + 0x38, 0xF585_0008);
        WriteInstruction(memory, pc + 0x3C, 0xF5A5_8008);
        WriteInstruction(memory, pc + 0x40, 0x9545_0010);
        WriteInstruction(memory, pc + 0x44, 0x4E80_0020);
    }

    private static void WriteSonicPairedTransform4dLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc - 0x78, 0xE004_0000);
        WriteInstruction(memory, pc - 0x74, 0x38C6_FFF8);
        WriteInstruction(memory, pc - 0x70, 0xE024_8008);
        WriteInstruction(memory, pc - 0x6C, 0x38A5_FFF4);
        WriteInstruction(memory, pc - 0x68, 0xE0C4_0024);
        WriteInstruction(memory, pc - 0x64, 0xE506_0008);
        WriteInstruction(memory, pc - 0x60, 0xE0E4_802C);
        WriteInstruction(memory, pc - 0x5C, 0xE526_0008);
        WriteInstruction(memory, pc - 0x58, 0x1160_321C);
        WriteInstruction(memory, pc - 0x54, 0xE044_000C);
        WriteInstruction(memory, pc - 0x50, 0x1181_3A1C);
        WriteInstruction(memory, pc - 0x4C, 0xE064_8014);
        WriteInstruction(memory, pc - 0x48, 0x11A0_025A);
        WriteInstruction(memory, pc - 0x44, 0xE546_0008);
        WriteInstruction(memory, pc - 0x40, 0x11C1_025A);
        WriteInstruction(memory, pc - 0x3C, 0xE0A4_8020);
        WriteInstruction(memory, pc - 0x38, 0x1162_5A1E);
        WriteInstruction(memory, pc - 0x34, 0x1183_621E);
        WriteInstruction(memory, pc - 0x30, 0xE084_0018);
        WriteInstruction(memory, pc - 0x2C, 0x11A2_6A9C);
        WriteInstruction(memory, pc - 0x28, 0xE506_0008);
        WriteInstruction(memory, pc - 0x24, 0x11C3_729C);
        WriteInstruction(memory, pc - 0x20, 0x11E4_5A5C);
        WriteInstruction(memory, pc - 0x1C, 0x1205_625C);
        WriteInstruction(memory, pc - 0x18, 0xE526_0008);
        WriteInstruction(memory, pc - 0x14, 0x1224_6A9E);
        WriteInstruction(memory, pc - 0x10, 0x1245_729E);
        WriteInstruction(memory, pc - 0x0C, 0xE546_0008);
        WriteInstruction(memory, pc - 0x08, 0x2C07_0000);
        WriteInstruction(memory, pc - 0x04, 0x4182_0054);
        WriteInstruction(memory, pc + 0x00, 0x1160_321C);
        WriteInstruction(memory, pc + 0x04, 0xF5E5_000C);
        WriteInstruction(memory, pc + 0x08, 0x1181_3A1C);
        WriteInstruction(memory, pc + 0x0C, 0xF605_8008);
        WriteInstruction(memory, pc + 0x10, 0x11A0_025A);
        WriteInstruction(memory, pc + 0x14, 0xF625_0004);
        WriteInstruction(memory, pc + 0x18, 0x11C1_025A);
        WriteInstruction(memory, pc + 0x1C, 0xF645_8008);
        WriteInstruction(memory, pc + 0x20, 0x1162_5A1E);
        WriteInstruction(memory, pc + 0x24, 0x1183_621E);
        WriteInstruction(memory, pc + 0x28, 0xE506_0008);
        WriteInstruction(memory, pc + 0x2C, 0x11A2_6A9C);
        WriteInstruction(memory, pc + 0x30, 0x11C3_729C);
        WriteInstruction(memory, pc + 0x34, 0x11E4_5A5C);
        WriteInstruction(memory, pc + 0x38, 0x1205_625C);
        WriteInstruction(memory, pc + 0x3C, 0xE526_0008);
        WriteInstruction(memory, pc + 0x40, 0x1224_6A9E);
        WriteInstruction(memory, pc + 0x44, 0x1245_729E);
        WriteInstruction(memory, pc + 0x48, 0xE546_0008);
        WriteInstruction(memory, pc + 0x4C, 0x4200_FFB4);
        WriteInstruction(memory, pc + 0x50, 0xF5E5_000C);
        WriteInstruction(memory, pc + 0x54, 0xF605_8008);
        WriteInstruction(memory, pc + 0x58, 0xF625_0004);
        WriteInstruction(memory, pc + 0x5C, 0xF645_8008);
        WriteInstruction(memory, pc + 0x60, 0xC9C1_0008);
        WriteInstruction(memory, pc + 0x64, 0xC9E1_0010);
        WriteInstruction(memory, pc + 0x68, 0xCA01_0018);
        WriteInstruction(memory, pc + 0x6C, 0xCA21_0020);
        WriteInstruction(memory, pc + 0x70, 0xCA41_0028);
        WriteInstruction(memory, pc + 0x74, 0x3821_0040);
        WriteInstruction(memory, pc + 0x78, 0x4E80_0020);
    }

    private static void WriteSonicPairedTransform4dIndexedOutputLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8011_DE54, 0x1160_321C);
        WriteInstruction(memory, 0x8011_DE58, 0xF1E6_0000);
        WriteInstruction(memory, 0x8011_DE5C, 0x1181_3A1C);
        WriteInstruction(memory, 0x8011_DE60, 0xF206_8008);
        WriteInstruction(memory, 0x8011_DE64, 0x11A0_025A);
        WriteInstruction(memory, 0x8011_DE68, 0xF226_000C);
        WriteInstruction(memory, 0x8011_DE6C, 0x11C1_025A);
        WriteInstruction(memory, 0x8011_DE70, 0xF246_8014);
        WriteInstruction(memory, 0x8011_DE74, 0x1162_5A1E);
        WriteInstruction(memory, 0x8011_DE78, 0x1183_621E);
        WriteInstruction(memory, 0x8011_DE7C, 0xE507_0002);
        WriteInstruction(memory, 0x8011_DE80, 0x11A2_6A9C);
        WriteInstruction(memory, 0x8011_DE84, 0x11C3_729C);
        WriteInstruction(memory, 0x8011_DE88, 0xE284_0000);
        WriteInstruction(memory, 0x8011_DE8C, 0x1164_5A5C);
        WriteInstruction(memory, 0x8011_DE90, 0xE2A4_8008);
        WriteInstruction(memory, 0x8011_DE94, 0x1185_625C);
        WriteInstruction(memory, 0x8011_DE98, 0xE2C4_000C);
        WriteInstruction(memory, 0x8011_DE9C, 0x11A4_6A9E);
        WriteInstruction(memory, 0x8011_DEA0, 0xE2E4_8014);
        WriteInstruction(memory, 0x8011_DEA4, 0x11C5_729E);
        WriteInstruction(memory, 0x8011_DEA8, 0x7C86_2378);
        WriteInstruction(memory, 0x8011_DEAC, 0x11EB_A4DC);
        WriteInstruction(memory, 0x8011_DEB0, 0x120C_ACDC);
        WriteInstruction(memory, 0x8011_DEB4, 0xE527_0008);
        WriteInstruction(memory, 0x8011_DEB8, 0x122D_B4DC);
        WriteInstruction(memory, 0x8011_DEBC, 0x124E_BCDC);
        WriteInstruction(memory, 0x8011_DEC0, 0xE547_0008);
        WriteInstruction(memory, 0x8011_DEC4, 0xE667_9008);
        WriteInstruction(memory, 0x8011_DEC8, 0xA547_0002);
        WriteInstruction(memory, 0x8011_DECC, 0x554A_2834);
        WriteInstruction(memory, 0x8011_DED0, 0x7C89_5214);
        WriteInstruction(memory, 0x8011_DED4, 0x4200_FF80);
        WriteInstruction(memory, 0x8011_DED8, 0xF1E6_0000);
        WriteInstruction(memory, 0x8011_DEDC, 0xF206_8008);
        WriteInstruction(memory, 0x8011_DEE0, 0xF226_000C);
        WriteInstruction(memory, 0x8011_DEE4, 0xF246_8014);
        WriteInstruction(memory, 0x8011_DEE8, 0xC9C1_0008);
        WriteInstruction(memory, 0x8011_DEEC, 0xC9E1_0010);
        WriteInstruction(memory, 0x8011_DEF0, 0xCA01_0018);
        WriteInstruction(memory, 0x8011_DEF4, 0xCA21_0020);
        WriteInstruction(memory, 0x8011_DEF8, 0xCA41_0028);
        WriteInstruction(memory, 0x8011_DEFC, 0xCA61_0030);
        WriteInstruction(memory, 0x8011_DF00, 0xCA81_0038);
        WriteInstruction(memory, 0x8011_DF04, 0xCAA1_0040);
        WriteInstruction(memory, 0x8011_DF08, 0xCAC1_0048);
        WriteInstruction(memory, 0x8011_DF0C, 0xCAE1_0050);
        WriteInstruction(memory, 0x8011_DF10, 0x3821_0080);
        WriteInstruction(memory, 0x8011_DF14, 0x4E80_0020);
    }

    private static void WriteSonicVectorBlendCopyLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc - 0x10, 0x7D29_03A6);
        WriteInstruction(memory, pc - 0x0C, 0x2C09_0000);
        WriteInstruction(memory, pc - 0x08, 0x4081_015C);
        WriteInstruction(memory, pc + 0x000, 0x2C0A_0000);
        WriteInstruction(memory, pc + 0x004, 0x4182_008C);
        WriteInstruction(memory, pc + 0x008, 0x281A_0000);
        WriteInstruction(memory, pc + 0x00C, 0x4182_0064);
        WriteInstruction(memory, pc + 0x010, 0xC01F_0000);
        WriteInstruction(memory, pc + 0x014, 0xEC40_07F2);
        WriteInstruction(memory, pc + 0x018, 0xC03D_0000);
        WriteInstruction(memory, pc + 0x01C, 0xC001_0038);
        WriteInstruction(memory, pc + 0x020, 0xEC01_0032);
        WriteInstruction(memory, pc + 0x024, 0xEC02_002A);
        WriteInstruction(memory, pc + 0x028, 0xD007_0000);
        WriteInstruction(memory, pc + 0x02C, 0xC01F_0004);
        WriteInstruction(memory, pc + 0x030, 0xEC40_07F2);
        WriteInstruction(memory, pc + 0x034, 0xC03D_0004);
        WriteInstruction(memory, pc + 0x038, 0xC001_0038);
        WriteInstruction(memory, pc + 0x03C, 0xEC01_0032);
        WriteInstruction(memory, pc + 0x040, 0xEC02_002A);
        WriteInstruction(memory, pc + 0x044, 0xD007_0004);
        WriteInstruction(memory, pc + 0x048, 0xC01F_0008);
        WriteInstruction(memory, pc + 0x04C, 0xEC40_07F2);
        WriteInstruction(memory, pc + 0x050, 0xC03D_0008);
        WriteInstruction(memory, pc + 0x054, 0xC001_0038);
        WriteInstruction(memory, pc + 0x058, 0xEC01_0032);
        WriteInstruction(memory, pc + 0x05C, 0xEC02_002A);
        WriteInstruction(memory, pc + 0x060, 0xD007_0008);
        WriteInstruction(memory, pc + 0x064, 0x3BFF_000C);
        WriteInstruction(memory, pc + 0x068, 0x3BBD_000C);
        WriteInstruction(memory, pc + 0x06C, 0x4800_001C);
        WriteInstruction(memory, pc + 0x070, 0xC004_0000);
        WriteInstruction(memory, pc + 0x074, 0xD007_0000);
        WriteInstruction(memory, pc + 0x078, 0xC004_0004);
        WriteInstruction(memory, pc + 0x07C, 0xD007_0004);
        WriteInstruction(memory, pc + 0x080, 0xC004_0008);
        WriteInstruction(memory, pc + 0x084, 0xD007_0008);
        WriteInstruction(memory, pc + 0x088, 0x38E7_000C);
        WriteInstruction(memory, pc + 0x08C, 0x3884_000C);
        WriteInstruction(memory, pc + 0x090, 0x2C0B_0000);
        WriteInstruction(memory, pc + 0x094, 0x4182_008C);
        WriteInstruction(memory, pc + 0x098, 0x281B_0000);
        WriteInstruction(memory, pc + 0x09C, 0x4182_0064);
        WriteInstruction(memory, pc + 0x0A0, 0xC01E_0000);
        WriteInstruction(memory, pc + 0x0A4, 0xEC40_07B2);
        WriteInstruction(memory, pc + 0x0A8, 0xC03C_0000);
        WriteInstruction(memory, pc + 0x0AC, 0xC001_002C);
        WriteInstruction(memory, pc + 0x0B0, 0xEC01_0032);
        WriteInstruction(memory, pc + 0x0B4, 0xEC02_002A);
        WriteInstruction(memory, pc + 0x0B8, 0xD007_0000);
        WriteInstruction(memory, pc + 0x0BC, 0xC01E_0004);
        WriteInstruction(memory, pc + 0x0C0, 0xEC40_07B2);
        WriteInstruction(memory, pc + 0x0C4, 0xC03C_0004);
        WriteInstruction(memory, pc + 0x0C8, 0xC001_002C);
        WriteInstruction(memory, pc + 0x0CC, 0xEC01_0032);
        WriteInstruction(memory, pc + 0x0D0, 0xEC02_002A);
        WriteInstruction(memory, pc + 0x0D4, 0xD007_0004);
        WriteInstruction(memory, pc + 0x0D8, 0xC01E_0008);
        WriteInstruction(memory, pc + 0x0DC, 0xEC40_07B2);
        WriteInstruction(memory, pc + 0x0E0, 0xC03C_0008);
        WriteInstruction(memory, pc + 0x0E4, 0xC001_002C);
        WriteInstruction(memory, pc + 0x0E8, 0xEC01_0032);
        WriteInstruction(memory, pc + 0x0EC, 0xEC02_002A);
        WriteInstruction(memory, pc + 0x0F0, 0xD007_0008);
        WriteInstruction(memory, pc + 0x0F4, 0x3BDE_000C);
        WriteInstruction(memory, pc + 0x0F8, 0x3B9C_000C);
        WriteInstruction(memory, pc + 0x0FC, 0x4800_001C);
        WriteInstruction(memory, pc + 0x100, 0xC004_0000);
        WriteInstruction(memory, pc + 0x104, 0xD007_0000);
        WriteInstruction(memory, pc + 0x108, 0xC004_0004);
        WriteInstruction(memory, pc + 0x10C, 0xD007_0004);
        WriteInstruction(memory, pc + 0x110, 0xC004_0008);
        WriteInstruction(memory, pc + 0x114, 0xD007_0008);
        WriteInstruction(memory, pc + 0x118, 0x38E7_000C);
        WriteInstruction(memory, pc + 0x11C, 0x3884_000C);
        WriteInstruction(memory, pc + 0x120, 0x2C0C_0000);
        WriteInstruction(memory, pc + 0x124, 0x4182_0014);
        WriteInstruction(memory, pc + 0x128, 0x8004_0000);
        WriteInstruction(memory, pc + 0x12C, 0x3884_0004);
        WriteInstruction(memory, pc + 0x130, 0x9007_0000);
        WriteInstruction(memory, pc + 0x134, 0x38E7_0004);
        WriteInstruction(memory, pc + 0x138, 0x2C19_0000);
        WriteInstruction(memory, pc + 0x13C, 0x4182_0014);
        WriteInstruction(memory, pc + 0x140, 0x8004_0000);
        WriteInstruction(memory, pc + 0x144, 0x3884_0004);
        WriteInstruction(memory, pc + 0x148, 0x9007_0000);
        WriteInstruction(memory, pc + 0x14C, 0x38E7_0004);
        WriteInstruction(memory, pc + 0x150, 0x4200_FEB0);
    }

    private static void WriteSonicCoordinatePairFillLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C04_1800);
        WriteInstruction(memory, pc + 0x04, 0x4180_000C);
        WriteInstruction(memory, pc + 0x08, 0x3880_0000);
        WriteInstruction(memory, pc + 0x0C, 0x38C6_0001);
        WriteInstruction(memory, pc + 0x10, 0x7CC0_0774);
        WriteInstruction(memory, pc + 0x14, 0x9805_0000);
        WriteInstruction(memory, pc + 0x18, 0x7C80_0774);
        WriteInstruction(memory, pc + 0x1C, 0x3884_0001);
        WriteInstruction(memory, pc + 0x20, 0x9805_0001);
        WriteInstruction(memory, pc + 0x24, 0x38A5_0002);
        WriteInstruction(memory, pc + 0x28, 0x4200_FFD8);
    }

    private static void WriteSonicBufferFillLoops(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800F_C598, 0x93E4_0000);
        WriteInstruction(memory, 0x800F_C59C, 0x3884_0004);
        WriteInstruction(memory, 0x800F_C5A0, 0x3863_0001);
        WriteInstruction(memory, 0x800F_C5A4, 0x801D_0000);
        WriteInstruction(memory, 0x800F_C5A8, 0x1C00_00A0);
        WriteInstruction(memory, 0x800F_C5AC, 0x7C03_0040);
        WriteInstruction(memory, 0x800F_C5B0, 0x4180_FFE8);

        WriteInstruction(memory, 0x800F_C5C0, 0x9065_0000);
        WriteInstruction(memory, 0x800F_C5C4, 0x38A5_0004);
        WriteInstruction(memory, 0x800F_C5C8, 0x3884_0001);
        WriteInstruction(memory, 0x800F_C5CC, 0x801D_0004);
        WriteInstruction(memory, 0x800F_C5D0, 0x1C00_00A0);
        WriteInstruction(memory, 0x800F_C5D4, 0x7C04_0040);
        WriteInstruction(memory, 0x800F_C5D8, 0x4180_FFE8);

        WriteInstruction(memory, 0x800F_C5E8, 0x9066_0000);
        WriteInstruction(memory, 0x800F_C5EC, 0x38C6_0004);
        WriteInstruction(memory, 0x800F_C5F0, 0x3884_0001);
        WriteInstruction(memory, 0x800F_C5F4, 0x801D_0008);
        WriteInstruction(memory, 0x800F_C5F8, 0x1C00_00A0);
        WriteInstruction(memory, 0x800F_C5FC, 0x7C04_0040);
        WriteInstruction(memory, 0x800F_C600, 0x4180_FFE8);
    }

    private static void WriteSonicModeCoordinatorPrologue(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_30CC, 0x7C08_02A6);
        WriteInstruction(memory, 0x800E_30D0, 0x3C60_801D);
        WriteInstruction(memory, 0x800E_30D4, 0x9001_0004);
        WriteInstruction(memory, 0x800E_30D8, 0x3863_C168);
        WriteInstruction(memory, 0x800E_30DC, 0x9421_FFD8);
        WriteInstruction(memory, 0x800E_30E0, 0xBF41_0010);
        WriteInstruction(memory, 0x800E_30E4, 0x3BE3_0080);
        WriteInstruction(memory, 0x800E_30E8, 0x3BA0_0000);
        WriteInstruction(memory, 0x800E_30EC, 0x3B80_0000);
    }

    private static void WriteSonicModeCoordinatorBody(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_30F0, 0x8003_0080);
        WriteInstruction(memory, 0x800E_30F4, 0x2800_0008);
        WriteInstruction(memory, 0x800E_30F8, 0x4082_002C);
        WriteInstruction(memory, 0x800E_3124, 0x3BC0_0000);
        WriteInstruction(memory, 0x800E_3128, 0x4BFF_FAE5);
        WriteInstruction(memory, 0x800E_312C, 0x7C7B_1B78);
        WriteInstruction(memory, 0x800E_3130, 0x4800_E279);
        WriteInstruction(memory, 0x800E_3134, 0x3B43_0000);
        WriteInstruction(memory, 0x800E_3138, 0x381A_FFFC);
        WriteInstruction(memory, 0x800E_313C, 0x2800_0002);
        WriteInstruction(memory, 0x800E_3140, 0x4081_0028);
        WriteInstruction(memory, 0x800E_3144, 0x2C1A_FFFF);
        WriteInstruction(memory, 0x800E_3148, 0x4182_0020);
        WriteInstruction(memory, 0x800E_314C, 0x2C1A_000B);
        WriteInstruction(memory, 0x800E_3150, 0x4182_0018);
        WriteInstruction(memory, 0x800E_3154, 0x2C1B_0000);
        WriteInstruction(memory, 0x800E_3158, 0x4082_000C);
        WriteInstruction(memory, 0x800E_315C, 0x2C1E_0004);
        WriteInstruction(memory, 0x800E_3160, 0x4082_0008);
        WriteInstruction(memory, 0x800E_3164, 0x3B40_000B);
        WriteInstruction(memory, 0x800E_3168, 0x7F43_D378);
        WriteInstruction(memory, 0x800E_316C, 0x4BFF_FCB5);
        WriteInstruction(memory, 0x800E_3170, 0x3C60_801D);
    }

    private static void WriteSonicModeCoordinatorZeroTail(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_3170, 0x3C60_801D);
        WriteInstruction(memory, 0x800E_3174, 0x3863_C168);
        WriteInstruction(memory, 0x800E_3178, 0x3BC3_0013);
        WriteInstruction(memory, 0x800E_317C, 0x8803_0013);
        WriteInstruction(memory, 0x800E_3180, 0x7C00_0775);
        WriteInstruction(memory, 0x800E_3184, 0x4182_01F0);
        WriteInstruction(memory, 0x800E_3374, 0xBB41_0010);
        WriteInstruction(memory, 0x800E_3378, 0x8001_002C);
        WriteInstruction(memory, 0x800E_337C, 0x3821_0028);
        WriteInstruction(memory, 0x800E_3380, 0x7C08_03A6);
        WriteInstruction(memory, 0x800E_3384, 0x4E80_0020);
    }

    private static void WriteSonicStatusQuery(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8013_6CEC, 0x7C08_02A6);
        WriteInstruction(memory, 0x8013_6CF0, 0x9001_0004);
        WriteInstruction(memory, 0x8013_6CF4, 0x9421_FFD8);
        WriteInstruction(memory, 0x8013_6CF8, 0xBF41_0010);
        WriteInstruction(memory, 0x8013_6CFC, 0x3B43_0000);
        WriteInstruction(memory, 0x8013_6D00, 0x3C60_8018);
        WriteInstruction(memory, 0x8013_6D04, 0x3BE3_8978);
        WriteInstruction(memory, 0x8013_6D08, 0x800D_9074);
        WriteInstruction(memory, 0x8013_6D0C, 0x7C1A_0000);
        WriteInstruction(memory, 0x8013_6D10, 0x4182_0014);
        WriteInstruction(memory, 0x8013_6D24, 0x806D_9078);
        WriteInstruction(memory, 0x8013_6D28, 0x2803_0000);
        WriteInstruction(memory, 0x8013_6D2C, 0x4082_0014);
        WriteInstruction(memory, 0x8013_6D40, 0x8803_0001);
        WriteInstruction(memory, 0x8013_6D44, 0x7C00_0774);
        WriteInstruction(memory, 0x8013_6D48, 0x7C1B_0378);
        WriteInstruction(memory, 0x8013_6D4C, 0x2C1B_0003);
        WriteInstruction(memory, 0x8013_6D50, 0x4182_000C);
        WriteInstruction(memory, 0x8013_6D54, 0x7F63_DB78);
        WriteInstruction(memory, 0x8013_6D58, 0x4800_0228);
        WriteInstruction(memory, 0x8013_6F80, 0xBB41_0010);
        WriteInstruction(memory, 0x8013_6F84, 0x8001_002C);
        WriteInstruction(memory, 0x8013_6F88, 0x3821_0028);
        WriteInstruction(memory, 0x8013_6F8C, 0x7C08_03A6);
        WriteInstruction(memory, 0x8013_6F90, 0x4E80_0020);
    }

    private static void WriteSonicStatusCallerLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8001_2B24, 0x3860_0000);
        WriteInstruction(memory, 0x8001_2B28, 0x4812_41C5);
        WriteInstruction(memory, 0x8001_2B2C, 0x2C03_0003);
        WriteInstruction(memory, 0x8001_2B30, 0x4082_0010);
        WriteInstruction(memory, 0x8001_2B34, 0x881D_0000);
        WriteInstruction(memory, 0x8001_2B38, 0x980D_8084);
        WriteInstruction(memory, 0x8001_2B3C, 0x4800_0020);
        WriteInstruction(memory, 0x8001_2B40, 0x2C03_0004);
        WriteInstruction(memory, 0x8001_2B44, 0x4082_0010);
        WriteInstruction(memory, 0x8001_2B48, 0x3800_FFFF);
        WriteInstruction(memory, 0x8001_2B4C, 0x980D_8084);
        WriteInstruction(memory, 0x8001_2B50, 0x4800_000C);
        WriteInstruction(memory, 0x8001_2B54, 0x480D_0579);
        WriteInstruction(memory, 0x8001_2B58, 0x4BFF_FFCC);
    }

    private static void WriteSonicTableByteBuildDispatch(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_1158, 0x7F83_E378);
        WriteInstruction(memory, 0x800E_115C, 0x4800_0DB9);
        WriteInstruction(memory, 0x800E_1160, 0x3BBD_0001);
        WriteInstruction(memory, 0x800E_1164, 0x7C60_0774);
        WriteInstruction(memory, 0x800E_1168, 0x981E_0000);
        WriteInstruction(memory, 0x800E_116C, 0x2C1D_0D5F);
        WriteInstruction(memory, 0x800E_1170, 0x3BDE_0001);
        WriteInstruction(memory, 0x800E_1174, 0x3B9C_0048);
        WriteInstruction(memory, 0x800E_1178, 0x4180_FFE0);
        WriteInstruction(memory, 0x800E_1F14, 0x9421_FFD8);
        WriteInstruction(memory, 0x800E_1F18, 0x3800_0018);
    }

    private static void WriteSonicLineCopy(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8013_B430, 0x7CA4_0774);
        WriteInstruction(memory, 0x8013_B434, 0x2C04_000D);
        WriteInstruction(memory, 0x8013_B438, 0x4182_0028);
        WriteInstruction(memory, 0x8013_B43C, 0x2C04_000A);
        WriteInstruction(memory, 0x8013_B440, 0x4182_0020);
        WriteInstruction(memory, 0x8013_B444, 0x8883_0000);
        WriteInstruction(memory, 0x8013_B448, 0x3863_0001);
        WriteInstruction(memory, 0x8013_B44C, 0x9886_0000);
        WriteInstruction(memory, 0x8013_B450, 0x38C6_0001);
        WriteInstruction(memory, 0x8013_B454, 0x88A3_0000);
        WriteInstruction(memory, 0x8013_B458, 0x7CA4_0775);
        WriteInstruction(memory, 0x8013_B45C, 0x4082_FFD4);
        WriteInstruction(memory, 0x8013_B460, 0x3860_000A);
    }

    private static void WriteSonicLineSkip(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8013_A39C, 0x887A_0000);
        WriteInstruction(memory, 0x8013_A3A0, 0x7C60_0775);
        WriteInstruction(memory, 0x8013_A3A4, 0x4182_0044);
        WriteInstruction(memory, 0x8013_A3A8, 0x7C60_0774);
        WriteInstruction(memory, 0x8013_A3AC, 0x2C00_000A);
        WriteInstruction(memory, 0x8013_A3B0, 0x4082_000C);
        WriteInstruction(memory, 0x8013_A3B4, 0x3B5A_0001);
        WriteInstruction(memory, 0x8013_A3B8, 0x4800_0028);
        WriteInstruction(memory, 0x8013_A3BC, 0x2C00_000D);
        WriteInstruction(memory, 0x8013_A3C0, 0x4082_0018);
        WriteInstruction(memory, 0x8013_A3C4, 0x8C1A_0001);
        WriteInstruction(memory, 0x8013_A3C8, 0x2C00_000A);
        WriteInstruction(memory, 0x8013_A3CC, 0x4082_0014);
        WriteInstruction(memory, 0x8013_A3D0, 0x3B5A_0001);
        WriteInstruction(memory, 0x8013_A3D4, 0x4800_000C);
        WriteInstruction(memory, 0x8013_A3D8, 0x3B5A_0001);
        WriteInstruction(memory, 0x8013_A3DC, 0x4BFF_FFC0);
        WriteInstruction(memory, 0x8013_A3E0, 0x2C18_0000);
        WriteInstruction(memory, 0x8013_A3E8, 0x7F63_DB78);
    }

    private static void WriteSonicStringAppendScan(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8010_DF88, 0x3884_FFFF);
        WriteInstruction(memory, 0x8010_DF8C, 0x38A3_FFFF);
        WriteInstruction(memory, 0x8010_DF90, 0x8C05_0001);
        WriteInstruction(memory, 0x8010_DF94, 0x2800_0000);
        WriteInstruction(memory, 0x8010_DF98, 0x4082_FFF8);
        WriteInstruction(memory, 0x8010_DF9C, 0x38A5_FFFF);
        WriteInstruction(memory, 0x8010_DFA0, 0x8C04_0001);
        WriteInstruction(memory, 0x8010_DFA4, 0x2800_0000);
        WriteInstruction(memory, 0x8010_DFA8, 0x9C05_0001);
        WriteInstruction(memory, 0x8010_DFAC, 0x4082_FFF4);
        WriteInstruction(memory, 0x8010_DFB0, 0x4E80_0020);
    }

    private static void WriteSonicFreeBlockScan(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8013_9C94, 0xA01D_0000);
        WriteInstruction(memory, 0x8013_9C98, 0x2800_4D46);
        WriteInstruction(memory, 0x8013_9C9C, 0x4082_0014);
        WriteInstruction(memory, 0x8013_9CA0, 0x7C7D_2050);
        WriteInstruction(memory, 0x8013_9CA4, 0x3863_FFE0);
        WriteInstruction(memory, 0x8013_9CA8, 0x7C03_2840);
        WriteInstruction(memory, 0x8013_9CAC, 0x4080_008C);
        WriteInstruction(memory, 0x8013_9CB0, 0x7C9D_2378);
        WriteInstruction(memory, 0x8013_9CB4, 0x809D_0004);
        WriteInstruction(memory, 0x8013_9CB8, 0x2804_0000);
        WriteInstruction(memory, 0x8013_9CBC, 0x4082_FFD8);
        WriteInstruction(memory, 0x8013_9CC0, 0x3BA0_0000);
        WriteInstruction(memory, 0x8013_9D38, 0x3805_0020);
    }

    private static void SetupSonicFreeBlock(GameCubeMemory memory, uint address, ushort magic, uint next)
    {
        memory.Write16(address, magic);
        memory.Write32(address + 4, next);
    }

    private static void WriteSonicCacheStoreSweep(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_4F70, 0x7CA0_00A6);
        WriteInstruction(memory, 0x800E_4F74, 0x60A5_1000);
        WriteInstruction(memory, 0x800E_4F78, 0x7CA0_0124);
        WriteInstruction(memory, 0x800E_4F7C, 0x3C60_8000);
        WriteInstruction(memory, 0x800E_4F80, 0x3880_0400);
        WriteInstruction(memory, 0x800E_4F84, 0x7C89_03A6);
        WriteInstruction(memory, 0x800E_4F88, 0x7C00_1A2C);
        WriteInstruction(memory, 0x800E_4F8C, 0x7C00_186C);
        WriteInstruction(memory, 0x800E_4F90, 0x3863_0020);
        WriteInstruction(memory, 0x800E_4F94, 0x4200_FFF4);
        WriteInstruction(memory, 0x800E_4F98, 0x7C98_E2A6);
    }

    private static void WriteSonicStateZeroFill(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800F_9818, 0x3800_0380);
        WriteInstruction(memory, 0x800F_981C, 0x3C7F_0001);
        WriteInstruction(memory, 0x800F_9820, 0x7C09_03A6);
        WriteInstruction(memory, 0x800F_9824, 0x3800_0000);
        WriteInstruction(memory, 0x800F_9828, 0x3863_8000);
        WriteInstruction(memory, 0x800F_982C, 0x9003_0000);
        WriteInstruction(memory, 0x800F_9830, 0x9003_0004);
        WriteInstruction(memory, 0x800F_9834, 0x9003_0008);
        WriteInstruction(memory, 0x800F_9838, 0x9003_000C);
        WriteInstruction(memory, 0x800F_983C, 0x9003_0010);
        WriteInstruction(memory, 0x800F_9840, 0x9003_0014);
        WriteInstruction(memory, 0x800F_9844, 0x9003_0018);
        WriteInstruction(memory, 0x800F_9848, 0x9003_001C);
        WriteInstruction(memory, 0x800F_984C, 0x9003_0020);
        WriteInstruction(memory, 0x800F_9850, 0x3863_0024);
        WriteInstruction(memory, 0x800F_9854, 0x4200_FFD8);
        WriteInstruction(memory, 0x800F_9858, 0x3F7F_0001);
    }

    private static void WriteSonicManagerSlotScan(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8012_4F44, 0x3C60_8036);
        WriteInstruction(memory, 0x8012_4F48, 0x3BE3_EB20);
        WriteInstruction(memory, 0x8012_4F4C, 0x3BC0_0000);
        WriteInstruction(memory, 0x8012_4F50, 0x881F_0000);
        WriteInstruction(memory, 0x8012_4F54, 0x2C00_0001);
        WriteInstruction(memory, 0x8012_4F58, 0x4082_000C);
        WriteInstruction(memory, 0x8012_4F5C, 0x7FE3_FB78);
        WriteInstruction(memory, 0x8012_4F60, 0x4800_0509);
        WriteInstruction(memory, 0x8012_4F64, 0x3BDE_0001);
        WriteInstruction(memory, 0x8012_4F68, 0x2C1E_0010);
        WriteInstruction(memory, 0x8012_4F6C, 0x3BFF_0438);
        WriteInstruction(memory, 0x8012_4F70, 0x4180_FFE0);
        WriteInstruction(memory, 0x8012_4F74, 0x3861_0008);
    }

    private static void WriteSonicTaskEntryScan(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8013_03A4, 0x3BE3_5230);
        WriteInstruction(memory, 0x8013_03AC, 0x3BC0_0000);
        WriteInstruction(memory, 0x8013_03B0, 0x881F_0000);
        WriteInstruction(memory, 0x8013_03B4, 0x387F_0000);
        WriteInstruction(memory, 0x8013_03B8, 0x2C00_0001);
        WriteInstruction(memory, 0x8013_03BC, 0x4082_0028);
        WriteInstruction(memory, 0x8013_03C0, 0x881F_0001);
        WriteInstruction(memory, 0x8013_03C4, 0x7C00_0774);
        WriteInstruction(memory, 0x8013_03C8, 0x2C00_0002);
        WriteInstruction(memory, 0x8013_03CC, 0x4082_000C);
        WriteInstruction(memory, 0x8013_03E0, 0x4800_06A9);
        WriteInstruction(memory, 0x8013_03E4, 0x3BDE_0001);
        WriteInstruction(memory, 0x8013_03E8, 0x2C1E_0010);
        WriteInstruction(memory, 0x8013_03EC, 0x3BFF_009C);
        WriteInstruction(memory, 0x8013_03F0, 0x4180_FFC0);
        WriteInstruction(memory, 0x8013_03F4, 0x8001_0014);
    }

    private static void WriteSonicObjectSlotScan(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8013_38CC, 0x3C60_8036);
        WriteInstruction(memory, 0x8013_38D4, 0x3BE3_43B0);
        WriteInstruction(memory, 0x8013_38D8, 0x3BC0_0000);
        WriteInstruction(memory, 0x8013_38DC, 0x881F_0000);
        WriteInstruction(memory, 0x8013_38E0, 0x387F_0000);
        WriteInstruction(memory, 0x8013_38E4, 0x2C00_0001);
        WriteInstruction(memory, 0x8013_38E8, 0x4082_0008);
        WriteInstruction(memory, 0x8013_38EC, 0x4800_0F69);
        WriteInstruction(memory, 0x8013_38F0, 0x3BDE_0001);
        WriteInstruction(memory, 0x8013_38F4, 0x2C1E_0010);
        WriteInstruction(memory, 0x8013_38F8, 0x3BFF_00A4);
        WriteInstruction(memory, 0x8013_38FC, 0x4180_FFE0);
        WriteInstruction(memory, 0x8013_3900, 0x4800_1EA9);
    }

    private static void WriteSonicHalfwordChecksumLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8016_F978, 0xA125_0000);
        WriteInstruction(memory, 0x8016_F97C, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_F980, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_F984, 0xA125_0002);
        WriteInstruction(memory, 0x8016_F988, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_F98C, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_F990, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_F994, 0xA125_0004);
        WriteInstruction(memory, 0x8016_F998, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_F99C, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_F9A0, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_F9A4, 0xA125_0006);
        WriteInstruction(memory, 0x8016_F9A8, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_F9AC, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_F9B0, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_F9B4, 0xA125_0008);
        WriteInstruction(memory, 0x8016_F9B8, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_F9BC, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_F9C0, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_F9C4, 0xA125_000A);
        WriteInstruction(memory, 0x8016_F9C8, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_F9CC, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_F9D0, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_F9D4, 0xA125_000C);
        WriteInstruction(memory, 0x8016_F9D8, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_F9DC, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_F9E0, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_F9E4, 0xA125_000E);
        WriteInstruction(memory, 0x8016_F9E8, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_F9EC, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_F9F0, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_F9F4, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_F9F8, 0x38A5_0010);
        WriteInstruction(memory, 0x8016_F9FC, 0x4200_FF7C);
        WriteInstruction(memory, 0x8016_FA00, 0x70C6_0007);

        WriteInstruction(memory, 0x8016_FBA8, 0xA126_0000);
        WriteInstruction(memory, 0x8016_FBAC, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_FBB0, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_FBB4, 0xA126_0002);
        WriteInstruction(memory, 0x8016_FBB8, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_FBBC, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_FBC0, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_FBC4, 0xA126_0004);
        WriteInstruction(memory, 0x8016_FBC8, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_FBCC, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_FBD0, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_FBD4, 0xA126_0006);
        WriteInstruction(memory, 0x8016_FBD8, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_FBDC, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_FBE0, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_FBE4, 0xA126_0008);
        WriteInstruction(memory, 0x8016_FBE8, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_FBEC, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_FBF0, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_FBF4, 0xA126_000A);
        WriteInstruction(memory, 0x8016_FBF8, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_FBFC, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_FC00, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_FC04, 0xA126_000C);
        WriteInstruction(memory, 0x8016_FC08, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_FC0C, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_FC10, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_FC14, 0xA126_000E);
        WriteInstruction(memory, 0x8016_FC18, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_FC1C, 0x7D20_48F8);
        WriteInstruction(memory, 0x8016_FC20, 0x7D4A_4A14);
        WriteInstruction(memory, 0x8016_FC24, 0x7D6B_0214);
        WriteInstruction(memory, 0x8016_FC28, 0x38C6_0010);
        WriteInstruction(memory, 0x8016_FC2C, 0x4200_FF7C);
        WriteInstruction(memory, 0x8016_FC30, 0x7108_0007);
    }

    private static void WriteSonicStatusCallerLoopFixture(GameCubeMemory memory)
    {
        WriteSonicStatusCallerLoop(memory);
        WriteSonicStatusQuery(memory);
        WriteSonicModeCoordinatorPrologue(memory);
        WriteSonicModeCoordinatorBodyFixture(memory, stateWord: 0, busyFlag: 0, fallbackFlag: 0, queryPointer: 0x8010_4000, queryMode: 0, childStatus: 0);
        WriteSonicModeCoordinatorZeroTail(memory);
        memory.Write8(0x801C_C1AF, 19);
        memory.Write8(0x801C_C17B, 0);
        SetupSonicStatusQueryData(memory, currentId: 0, currentPointer: 0x8010_5000, status: 0);
    }

    private static void WriteSonicNullSlotScanLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8011_6BBC, 0x80A4_000C);
        WriteInstruction(memory, 0x8011_6BC0, 0x2805_0000);
        WriteInstruction(memory, 0x8011_6BC4, 0x4182_0018);
        WriteInstruction(memory, 0x8011_6BC8, 0x8005_0000);
        WriteInstruction(memory, 0x8011_6BCC, 0x7C03_0040);
        WriteInstruction(memory, 0x8011_6BD0, 0x4082_000C);
        WriteInstruction(memory, 0x8011_6BD4, 0x7CC3_3378);
        WriteInstruction(memory, 0x8011_6BD8, 0x4800_0014);
        WriteInstruction(memory, 0x8011_6BDC, 0x3884_0018);
        WriteInstruction(memory, 0x8011_6BE0, 0x38C6_0001);
        WriteInstruction(memory, 0x8011_6BE4, 0x4200_FFD8);
        WriteInstruction(memory, 0x8011_6BE8, 0x3860_FFFF);
        WriteInstruction(memory, 0x8011_6BEC, 0x4E80_0020);
    }

    private static void WriteSonicPoolNullSlotScanLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8011_6C18, 0x8006_000C);
        WriteInstruction(memory, 0x8011_6C1C, 0x2800_0000);
        WriteInstruction(memory, 0x8011_6C20, 0x4082_0010);
        WriteInstruction(memory, 0x8011_6C24, 0x90C3_0000);
        WriteInstruction(memory, 0x8011_6C28, 0x7CA7_2B78);
        WriteInstruction(memory, 0x8011_6C2C, 0x4800_0010);
        WriteInstruction(memory, 0x8011_6C30, 0x38C6_0018);
        WriteInstruction(memory, 0x8011_6C34, 0x38A5_0001);
        WriteInstruction(memory, 0x8011_6C38, 0x4200_FFE0);
        WriteInstruction(memory, 0x8011_6C3C, 0x8003_0000);
    }

    private static void WriteSonicPoolSentinelSlotScanLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8011_6C8C, 0x8065_0000);
        WriteInstruction(memory, 0x8011_6C90, 0x3C03_0001);
        WriteInstruction(memory, 0x8011_6C94, 0x2800_FFFF);
        WriteInstruction(memory, 0x8011_6C98, 0x4082_000C);
        WriteInstruction(memory, 0x8011_6C9C, 0x90A4_0000);
        WriteInstruction(memory, 0x8011_6CA0, 0x4800_000C);
        WriteInstruction(memory, 0x8011_6CA4, 0x38A5_0028);
        WriteInstruction(memory, 0x8011_6CA8, 0x4200_FFE4);
        WriteInstruction(memory, 0x8011_6CAC, 0x8004_0000);
    }

    private static void WriteSonicTableKeyScanLoop(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8011_90B8, 0x807F_0000);
        WriteInstruction(memory, 0x8011_90BC, 0x3804_0004);
        WriteInstruction(memory, 0x8011_90C0, 0x7C03_002E);
        WriteInstruction(memory, 0x8011_90C4, 0x5400_043E);
        WriteInstruction(memory, 0x8011_90C8, 0x7C1D_0040);
        WriteInstruction(memory, 0x8011_90CC, 0x4182_0010);
        WriteInstruction(memory, 0x8011_90D0, 0x3884_000C);
        WriteInstruction(memory, 0x8011_90D4, 0x3B9C_0001);
        WriteInstruction(memory, 0x8011_90D8, 0x4200_FFE0);
        WriteInstruction(memory, 0x8011_90DC, 0x7FE3_FB78);
    }

    private static void WriteSonicModeRefreshDispatch(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x8012_33E0, 0x800D_8F04);
        WriteInstruction(memory, 0x8012_33E4, 0x2800_0000);
        WriteInstruction(memory, 0x8012_33E8, 0x4182_0024);
        WriteInstruction(memory, 0x8012_33EC, 0x4BFD_0325);
        WriteInstruction(memory, 0x8012_33F0, 0x7C1B_1840);
        WriteInstruction(memory, 0x8012_33F4, 0x4080_0018);
        WriteInstruction(memory, 0x800F_3710, 0x806D_8B00);
        WriteInstruction(memory, 0x800F_3714, 0x4E80_0020);
        WriteInstruction(memory, 0x8012_340C, 0x381C_FFFC);
        WriteInstruction(memory, 0x8012_3410, 0x2800_0002);
        WriteInstruction(memory, 0x8012_3414, 0x4081_0014);
        WriteInstruction(memory, 0x8012_3418, 0x2C1C_FFFF);
        WriteInstruction(memory, 0x8012_341C, 0x4182_000C);
        WriteInstruction(memory, 0x8012_3420, 0x2C1C_000B);
        WriteInstruction(memory, 0x8012_3424, 0x4082_0054);
        WriteInstruction(memory, 0x8012_3478, 0x806D_8F0C);
        WriteInstruction(memory, 0x8012_347C, 0x8063_0000);
        WriteInstruction(memory, 0x8012_3480, 0x4BFC_DEDD);
        WriteInstruction(memory, 0x8012_3484, 0x7C7C_1B78);
        WriteInstruction(memory, 0x8012_3488, 0x2C1C_0000);
        WriteInstruction(memory, 0x8012_348C, 0x4082_FF54);
        WriteInstruction(memory, 0x8012_3490, 0x2C1F_0000);
    }

    private static void WriteSonicBitPlaneInnerExpand(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x000, 0x895F_0000);
        WriteInstruction(memory, pc + 0x004, 0x3BFF_0001);
        WriteInstruction(memory, pc + 0x008, 0x7D4B_0774);
        WriteInstruction(memory, pc + 0x00C, 0x556A_07FF);
        WriteInstruction(memory, pc + 0x010, 0x4182_0008);
        WriteInstruction(memory, pc + 0x014, 0xB007_0000);
        WriteInstruction(memory, pc + 0x018, 0x54EA_077C);
        WriteInstruction(memory, pc + 0x01C, 0x280A_0006);
        WriteInstruction(memory, pc + 0x020, 0x4082_000C);
        WriteInstruction(memory, pc + 0x024, 0x38E7_001A);
        WriteInstruction(memory, pc + 0x028, 0x4800_0008);
        WriteInstruction(memory, pc + 0x02C, 0x38E7_0002);
        WriteInstruction(memory, pc + 0x030, 0x556A_07BD);
        WriteInstruction(memory, pc + 0x034, 0x4182_0008);
        WriteInstruction(memory, pc + 0x038, 0xB007_0000);
        WriteInstruction(memory, pc + 0x03C, 0x54EA_077C);
        WriteInstruction(memory, pc + 0x040, 0x280A_0006);
        WriteInstruction(memory, pc + 0x044, 0x4082_000C);
        WriteInstruction(memory, pc + 0x048, 0x38E7_001A);
        WriteInstruction(memory, pc + 0x04C, 0x4800_0008);
        WriteInstruction(memory, pc + 0x050, 0x38E7_0002);
        WriteInstruction(memory, pc + 0x054, 0x556A_077B);
        WriteInstruction(memory, pc + 0x058, 0x4182_0008);
        WriteInstruction(memory, pc + 0x05C, 0xB007_0000);
        WriteInstruction(memory, pc + 0x060, 0x54EA_077C);
        WriteInstruction(memory, pc + 0x064, 0x280A_0006);
        WriteInstruction(memory, pc + 0x068, 0x4082_000C);
        WriteInstruction(memory, pc + 0x06C, 0x38E7_001A);
        WriteInstruction(memory, pc + 0x070, 0x4800_0008);
        WriteInstruction(memory, pc + 0x074, 0x38E7_0002);
        WriteInstruction(memory, pc + 0x078, 0x556A_0739);
        WriteInstruction(memory, pc + 0x07C, 0x4182_0008);
        WriteInstruction(memory, pc + 0x080, 0xB007_0000);
        WriteInstruction(memory, pc + 0x084, 0x54EA_077C);
        WriteInstruction(memory, pc + 0x088, 0x280A_0006);
        WriteInstruction(memory, pc + 0x08C, 0x4082_000C);
        WriteInstruction(memory, pc + 0x090, 0x38E7_001A);
        WriteInstruction(memory, pc + 0x094, 0x4800_0008);
        WriteInstruction(memory, pc + 0x098, 0x38E7_0002);
        WriteInstruction(memory, pc + 0x09C, 0x556A_06F7);
        WriteInstruction(memory, pc + 0x0A0, 0x4182_0008);
        WriteInstruction(memory, pc + 0x0A4, 0xB007_0000);
        WriteInstruction(memory, pc + 0x0A8, 0x54EA_077C);
        WriteInstruction(memory, pc + 0x0AC, 0x280A_0006);
        WriteInstruction(memory, pc + 0x0B0, 0x4082_000C);
        WriteInstruction(memory, pc + 0x0B4, 0x38E7_001A);
        WriteInstruction(memory, pc + 0x0B8, 0x4800_0008);
        WriteInstruction(memory, pc + 0x0BC, 0x38E7_0002);
        WriteInstruction(memory, pc + 0x0C0, 0x556A_06B5);
        WriteInstruction(memory, pc + 0x0C4, 0x4182_0008);
        WriteInstruction(memory, pc + 0x0C8, 0xB007_0000);
        WriteInstruction(memory, pc + 0x0CC, 0x54EA_077C);
        WriteInstruction(memory, pc + 0x0D0, 0x280A_0006);
        WriteInstruction(memory, pc + 0x0D4, 0x4082_000C);
        WriteInstruction(memory, pc + 0x0D8, 0x38E7_001A);
        WriteInstruction(memory, pc + 0x0DC, 0x4800_0008);
        WriteInstruction(memory, pc + 0x0E0, 0x38E7_0002);
        WriteInstruction(memory, pc + 0x0E4, 0x556A_0673);
        WriteInstruction(memory, pc + 0x0E8, 0x4182_0008);
        WriteInstruction(memory, pc + 0x0EC, 0xB007_0000);
        WriteInstruction(memory, pc + 0x0F0, 0x54EA_077C);
        WriteInstruction(memory, pc + 0x0F4, 0x280A_0006);
        WriteInstruction(memory, pc + 0x0F8, 0x4082_000C);
        WriteInstruction(memory, pc + 0x0FC, 0x38E7_001A);
        WriteInstruction(memory, pc + 0x100, 0x4800_0008);
        WriteInstruction(memory, pc + 0x104, 0x38E7_0002);
        WriteInstruction(memory, pc + 0x108, 0x556A_0631);
        WriteInstruction(memory, pc + 0x10C, 0x4182_0008);
        WriteInstruction(memory, pc + 0x110, 0xB007_0000);
        WriteInstruction(memory, pc + 0x114, 0x54EA_077C);
        WriteInstruction(memory, pc + 0x118, 0x280A_0006);
        WriteInstruction(memory, pc + 0x11C, 0x4082_000C);
        WriteInstruction(memory, pc + 0x120, 0x38E7_001A);
        WriteInstruction(memory, pc + 0x124, 0x4800_0008);
        WriteInstruction(memory, pc + 0x128, 0x38E7_0002);
        WriteInstruction(memory, pc + 0x12C, 0x4200_FED4);
        WriteInstruction(memory, pc + 0x130, 0x54C7_06F8);
    }

    private static void WriteSonicModeCoordinatorBodyFixture(
        GameCubeMemory memory,
        uint stateWord,
        uint busyFlag,
        uint fallbackFlag,
        uint queryPointer,
        uint queryMode,
        short childStatus)
    {
        WriteSonicModeCoordinatorBody(memory);
        WriteSonicModeChildStatusPoll(memory);
        WriteSonicModeQuery(memory);
        WriteSonicModeStateUpdate(memory);

        memory.Write32(0x801C_C1E8, stateWord);
        memory.Write8(0x801C_C1AF, 20);
        memory.Write8(0x801C_C17B, 0);
        memory.Write32(0x800F_897C, 0);

        memory.Write32(0x800F_8510, 0);
        if (childStatus != 0)
        {
            memory.Write32(0x800F_8510, 0x8010_2000);
            memory.Write32(0x8010_2010, 0x8010_3000);
            memory.Write16(0x8010_3060, unchecked((ushort)childStatus));
            memory.Write32(0x8010_3064, 0xAAAA_0001);
            memory.Write16(0x8010_3068, 0xBBBB);
            memory.Write16(0x8010_306A, 0xCCCC);
        }

        memory.Write32(0x800F_8AC0, busyFlag);
        memory.Write32(0x800F_8AB8, fallbackFlag);
        memory.Write32(0x800F_8AA8, queryPointer);
        if (queryPointer != 0 && queryPointer != 0x802B_B700)
        {
            memory.Write32(queryPointer + 0x0C, queryMode);
        }
    }

    private static void WriteSonicModeQuery(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800F_13A8, 0x7C08_02A6);
        WriteInstruction(memory, 0x800F_13AC, 0x9001_0004);
        WriteInstruction(memory, 0x800F_13B0, 0x9421_FFE8);
        WriteInstruction(memory, 0x800F_13B4, 0x93E1_0014);
        WriteInstruction(memory, 0x800F_13B8, 0x93C1_0010);
        WriteInstruction(memory, 0x800F_13BC, 0x4BFF_64F1);
        WriteInstruction(memory, 0x800F_13C0, 0x800D_8AC0);
        WriteInstruction(memory, 0x800F_13C4, 0x3BC3_0000);
        WriteInstruction(memory, 0x800F_13C8, 0x2C00_0000);
        WriteInstruction(memory, 0x800F_13CC, 0x4182_000C);
        WriteInstruction(memory, 0x800F_13D0, 0x3BE0_FFFF);
        WriteInstruction(memory, 0x800F_13D4, 0x4800_005C);
        WriteInstruction(memory, 0x800F_13D8, 0x800D_8AB8);
        WriteInstruction(memory, 0x800F_13DC, 0x2C00_0000);
        WriteInstruction(memory, 0x800F_13E0, 0x4182_000C);
        WriteInstruction(memory, 0x800F_13E4, 0x3BE0_0008);
        WriteInstruction(memory, 0x800F_13E8, 0x4800_0048);
        WriteInstruction(memory, 0x800F_13EC, 0x83ED_8AA8);
        WriteInstruction(memory, 0x800F_13F0, 0x281F_0000);
        WriteInstruction(memory, 0x800F_13F4, 0x4082_000C);
        WriteInstruction(memory, 0x800F_13F8, 0x3BE0_0000);
        WriteInstruction(memory, 0x800F_13FC, 0x4800_0034);
        WriteInstruction(memory, 0x800F_1400, 0x3C60_802C);
        WriteInstruction(memory, 0x800F_1404, 0x3803_B700);
        WriteInstruction(memory, 0x800F_1408, 0x7C1F_0040);
        WriteInstruction(memory, 0x800F_140C, 0x4082_000C);
        WriteInstruction(memory, 0x800F_1410, 0x3BE0_0000);
        WriteInstruction(memory, 0x800F_1414, 0x4800_001C);
        WriteInstruction(memory, 0x800F_1418, 0x4BFF_6495);
        WriteInstruction(memory, 0x800F_141C, 0x83FF_000C);
        WriteInstruction(memory, 0x800F_1420, 0x2C1F_0003);
        WriteInstruction(memory, 0x800F_1424, 0x4082_0008);
        WriteInstruction(memory, 0x800F_1428, 0x3BE0_0001);
        WriteInstruction(memory, 0x800F_142C, 0x4BFF_64A9);
        WriteInstruction(memory, 0x800F_1430, 0x7FC3_F378);
        WriteInstruction(memory, 0x800F_1434, 0x4BFF_64A1);
        WriteInstruction(memory, 0x800F_1438, 0x8001_001C);
        WriteInstruction(memory, 0x800F_143C, 0x7FE3_FB78);
        WriteInstruction(memory, 0x800F_1440, 0x83E1_0014);
        WriteInstruction(memory, 0x800F_1444, 0x83C1_0010);
        WriteInstruction(memory, 0x800F_1448, 0x7C08_03A6);
        WriteInstruction(memory, 0x800F_144C, 0x3821_0018);
        WriteInstruction(memory, 0x800F_1450, 0x4E80_0020);
        WriteDisableExternalInterruptLeaf(memory, 0x800E_78AC);
        WriteRestoreExternalInterruptLeaf(memory, 0x800E_78D4);
    }

    private static void WriteSonicModeChildStatusPoll(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_2C0C, 0x7C08_02A6);
        WriteInstruction(memory, 0x800E_2C10, 0x9001_0004);
        WriteInstruction(memory, 0x800E_2C14, 0x9421_FFF0);
        WriteInstruction(memory, 0x800E_2C18, 0x93E1_000C);
        WriteInstruction(memory, 0x800E_2C1C, 0x806D_8510);
        WriteInstruction(memory, 0x800E_2C20, 0x2803_0000);
        WriteInstruction(memory, 0x800E_2C24, 0x4182_0098);
        WriteInstruction(memory, 0x800E_2C28, 0x8063_0010);
        WriteInstruction(memory, 0x800E_2C2C, 0x3BE0_0000);
        WriteInstruction(memory, 0x800E_2C30, 0x2803_0000);
        WriteInstruction(memory, 0x800E_2C34, 0x4182_0020);
        WriteInstruction(memory, 0x800E_2C38, 0x4805_0C59);
        WriteInstruction(memory, 0x800E_2C3C, 0x2C03_0000);
        WriteInstruction(memory, 0x800E_2C40, 0x4182_0008);
        WriteInstruction(memory, 0x800E_2C44, 0x3BE0_0001);
        WriteInstruction(memory, 0x800E_2C48, 0x806D_8510);
        WriteInstruction(memory, 0x800E_2C4C, 0x8063_0010);
        WriteInstruction(memory, 0x800E_2C50, 0x4805_0C29);
        WriteInstruction(memory, 0x800E_2C54, 0x806D_8510);
        WriteInstruction(memory, 0x800E_2C58, 0x8063_0034);
        WriteInstruction(memory, 0x800E_2C5C, 0x2803_0000);
        WriteInstruction(memory, 0x800E_2C60, 0x4182_0020);
        WriteInstruction(memory, 0x800E_2C64, 0x4805_0C2D);
        WriteInstruction(memory, 0x800E_2C68, 0x2C03_0000);
        WriteInstruction(memory, 0x800E_2C6C, 0x4182_0008);
        WriteInstruction(memory, 0x800E_2C70, 0x3BE0_0001);
        WriteInstruction(memory, 0x800E_2C74, 0x806D_8510);
        WriteInstruction(memory, 0x800E_2C78, 0x8063_0034);
        WriteInstruction(memory, 0x800E_2C7C, 0x4805_0BFD);
        WriteInstruction(memory, 0x800E_2C80, 0x806D_8510);
        WriteInstruction(memory, 0x800E_2C84, 0x8063_0050);
        WriteInstruction(memory, 0x800E_2C88, 0x2803_0000);
        WriteInstruction(memory, 0x800E_2C8C, 0x4182_0020);
        WriteInstruction(memory, 0x800E_2C90, 0x4805_0C01);
        WriteInstruction(memory, 0x800E_2C94, 0x2C03_0000);
        WriteInstruction(memory, 0x800E_2C98, 0x4182_0008);
        WriteInstruction(memory, 0x800E_2C9C, 0x3BE0_0001);
        WriteInstruction(memory, 0x800E_2CA0, 0x806D_8510);
        WriteInstruction(memory, 0x800E_2CA4, 0x8063_0050);
        WriteInstruction(memory, 0x800E_2CA8, 0x4805_0BD1);
        WriteInstruction(memory, 0x800E_2CAC, 0x2C1F_0000);
        WriteInstruction(memory, 0x800E_2CB0, 0x4182_000C);
        WriteInstruction(memory, 0x800E_2CB4, 0x3860_0001);
        WriteInstruction(memory, 0x800E_2CB8, 0x4800_0008);
        WriteInstruction(memory, 0x800E_2CBC, 0x3860_0000);
        WriteInstruction(memory, 0x800E_2CC0, 0x8001_0014);
        WriteInstruction(memory, 0x800E_2CC4, 0x83E1_000C);
        WriteInstruction(memory, 0x800E_2CC8, 0x3821_0010);
        WriteInstruction(memory, 0x800E_2CCC, 0x7C08_03A6);
        WriteInstruction(memory, 0x800E_2CD0, 0x4E80_0020);
        WriteInstruction(memory, 0x8013_3890, 0xA863_0060);
        WriteInstruction(memory, 0x8013_3894, 0x4E80_0020);
        WriteInstruction(memory, 0x8013_3878, 0x3800_0000);
        WriteInstruction(memory, 0x8013_387C, 0xB003_0060);
        WriteInstruction(memory, 0x8013_3880, 0x9003_0064);
        WriteInstruction(memory, 0x8013_3884, 0xB003_0068);
        WriteInstruction(memory, 0x8013_3888, 0xB003_006A);
        WriteInstruction(memory, 0x8013_388C, 0x4E80_0020);
    }

    private static void WriteSonicModeStateUpdate(GameCubeMemory memory)
    {
        WriteInstruction(memory, 0x800E_2E20, 0x3C80_801D);
        WriteInstruction(memory, 0x800E_2E24, 0x3884_C168);
        WriteInstruction(memory, 0x800E_2E28, 0x38C4_0047);
        WriteInstruction(memory, 0x800E_2E2C, 0x88A4_0047);
        WriteInstruction(memory, 0x800E_2E30, 0x7CA4_0774);
        WriteInstruction(memory, 0x800E_2E34, 0x2C04_0014);
        WriteInstruction(memory, 0x800E_2E38, 0x4182_0018);
        WriteInstruction(memory, 0x800E_2E3C, 0x3805_FFEB);
        WriteInstruction(memory, 0x800E_2E40, 0x5400_063E);
        WriteInstruction(memory, 0x800E_2E44, 0x2800_0002);
        WriteInstruction(memory, 0x800E_2E48, 0x4081_0008);
        WriteInstruction(memory, 0x800E_2E4C, 0x908D_897C);
        WriteInstruction(memory, 0x800E_2E50, 0x3803_0001);
        WriteInstruction(memory, 0x800E_2E54, 0x2800_000C);
        WriteInstruction(memory, 0x800E_2E58, 0x4181_0054);
        WriteInstruction(memory, 0x800E_2E5C, 0x3C60_801D);
        WriteInstruction(memory, 0x800E_2E60, 0x3863_DC70);
        WriteInstruction(memory, 0x800E_2E64, 0x5400_103A);
        WriteInstruction(memory, 0x800E_2E68, 0x7C03_002E);
        WriteInstruction(memory, 0x800E_2E6C, 0x7C09_03A6);
        WriteInstruction(memory, 0x800E_2E70, 0x4E80_0420);
        WriteInstruction(memory, 0x800E_2E74, 0x3800_0014);
        WriteInstruction(memory, 0x800E_2E78, 0x9806_0000);
        WriteInstruction(memory, 0x800E_2E7C, 0x4800_0030);
        WriteInstruction(memory, 0x800E_2E80, 0x3800_0015);
        WriteInstruction(memory, 0x800E_2E84, 0x9806_0000);
        WriteInstruction(memory, 0x800E_2E88, 0x4800_0024);
        WriteInstruction(memory, 0x800E_2E8C, 0x3800_0015);
        WriteInstruction(memory, 0x800E_2E90, 0x9806_0000);
        WriteInstruction(memory, 0x800E_2E94, 0x4800_0018);
        WriteInstruction(memory, 0x800E_2E98, 0x3800_0016);
        WriteInstruction(memory, 0x800E_2E9C, 0x9806_0000);
        WriteInstruction(memory, 0x800E_2EA0, 0x4800_000C);
        WriteInstruction(memory, 0x800E_2EA4, 0x3800_0017);
        WriteInstruction(memory, 0x800E_2EA8, 0x9806_0000);
        WriteInstruction(memory, 0x800E_2EAC, 0x8806_0000);
        WriteInstruction(memory, 0x800E_2EB0, 0x7C00_0774);
        WriteInstruction(memory, 0x800E_2EB4, 0x2C00_0014);
        WriteInstruction(memory, 0x800E_2EB8, 0x4D80_0020);
        WriteInstruction(memory, 0x800E_2EBC, 0x2C00_0015);
        WriteInstruction(memory, 0x800E_2EC0, 0x4182_0048);
        WriteInstruction(memory, 0x800E_2EC4, 0x4080_0010);
        WriteInstruction(memory, 0x800E_2EC8, 0x2C00_0014);
        WriteInstruction(memory, 0x800E_2ECC, 0x4080_0014);
        WriteInstruction(memory, 0x800E_2ED0, 0x4E80_0020);
        WriteInstruction(memory, 0x800E_2ED4, 0x2C00_0018);
        WriteInstruction(memory, 0x800E_2ED8, 0x4C80_0020);
        WriteInstruction(memory, 0x800E_2EDC, 0x4800_0018);
        WriteInstruction(memory, 0x800E_2EE0, 0x3C60_801D);
        WriteInstruction(memory, 0x800E_2EE4, 0x3863_C168);
        WriteInstruction(memory, 0x800E_2EE8, 0x3800_FFE9);
        WriteInstruction(memory, 0x800E_2EEC, 0x9803_0013);
        WriteInstruction(memory, 0x800E_2EF0, 0x4E80_0020);
        WriteInstruction(memory, 0x800E_2EF4, 0x3C60_801D);
        WriteInstruction(memory, 0x800E_2EF8, 0x3863_C168);
        WriteInstruction(memory, 0x800E_2EFC, 0x3800_FFEC);
        WriteInstruction(memory, 0x800E_2F00, 0x9803_0013);
        WriteInstruction(memory, 0x800E_2F04, 0x4E80_0020);
        WriteInstruction(memory, 0x800E_2F08, 0x3C60_801D);
        WriteInstruction(memory, 0x800E_2F0C, 0x3863_C168);
        WriteInstruction(memory, 0x800E_2F10, 0x3800_FFE3);
        WriteInstruction(memory, 0x800E_2F14, 0x9803_0013);
        WriteInstruction(memory, 0x800E_2F18, 0x4E80_0020);
        WriteInstruction(memory, 0x801C_DC70, 0x800E_2EA4);
        WriteInstruction(memory, 0x801C_DC74, 0x800E_2EAC);
        WriteInstruction(memory, 0x801C_DC78, 0x800E_2EAC);
        WriteInstruction(memory, 0x801C_DC7C, 0x800E_2EAC);
        WriteInstruction(memory, 0x801C_DC80, 0x800E_2EAC);
        WriteInstruction(memory, 0x801C_DC84, 0x800E_2E80);
        WriteInstruction(memory, 0x801C_DC88, 0x800E_2E74);
        WriteInstruction(memory, 0x801C_DC8C, 0x800E_2E8C);
        WriteInstruction(memory, 0x801C_DC90, 0x800E_2EAC);
        WriteInstruction(memory, 0x801C_DC94, 0x800E_2EAC);
        WriteInstruction(memory, 0x801C_DC98, 0x800E_2EAC);
        WriteInstruction(memory, 0x801C_DC9C, 0x800E_2EAC);
        WriteInstruction(memory, 0x801C_DCA0, 0x800E_2E98);
    }

    private static void WriteSonicGeneratedRangeScanLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x3C80_80BD);
        WriteInstruction(memory, pc + 0x04, 0x3884_4F58);
        WriteInstruction(memory, pc + 0x08, 0x8084_0000);
        WriteInstruction(memory, pc + 0x0C, 0x1C7F_0038);
        WriteInstruction(memory, pc + 0x10, 0x3CA3_0001);
        WriteInstruction(memory, pc + 0x14, 0x38A5_A800);
        WriteInstruction(memory, pc + 0x18, 0x7C84_282E);
        WriteInstruction(memory, pc + 0x1C, 0x3C60_80BD);
        WriteInstruction(memory, pc + 0x20, 0x3863_3120);
        WriteInstruction(memory, pc + 0x24, 0xC823_0000);
        WriteInstruction(memory, pc + 0x28, 0x6C83_8000);
        WriteInstruction(memory, pc + 0x2C, 0x9061_00CC);
        WriteInstruction(memory, pc + 0x30, 0x3C60_4330);
        WriteInstruction(memory, pc + 0x34, 0x9061_00C8);
        WriteInstruction(memory, pc + 0x38, 0xC801_00C8);
        WriteInstruction(memory, pc + 0x3C, 0xEC20_0828);
        WriteInstruction(memory, pc + 0x40, 0x3C80_80BF);
        WriteInstruction(memory, pc + 0x44, 0x3864_CA10);
        WriteInstruction(memory, pc + 0x48, 0xC003_0000);
        WriteInstruction(memory, pc + 0x4C, 0xFC01_0040);
        WriteInstruction(memory, pc + 0x50, 0x4C40_1382);
        WriteInstruction(memory, pc + 0x54, 0x4082_0078);
        WriteInstruction(memory, pc + 0x58, 0x3C60_80BD);
        WriteInstruction(memory, pc + 0x5C, 0x3863_4F58);
        WriteInstruction(memory, pc + 0x60, 0x8083_0000);
        WriteInstruction(memory, pc + 0x64, 0x1CBF_0038);
        WriteInstruction(memory, pc + 0x68, 0x3C65_0001);
        WriteInstruction(memory, pc + 0x6C, 0x3863_A800);
        WriteInstruction(memory, pc + 0x70, 0x7C84_182E);
        WriteInstruction(memory, pc + 0x74, 0x3C60_80BD);
        WriteInstruction(memory, pc + 0x78, 0x3863_3120);
        WriteInstruction(memory, pc + 0x7C, 0xC823_0000);
        WriteInstruction(memory, pc + 0x80, 0x6C83_8000);
        WriteInstruction(memory, pc + 0x84, 0x9061_00CC);
        WriteInstruction(memory, pc + 0x88, 0x3C60_4330);
        WriteInstruction(memory, pc + 0x8C, 0x9061_00C8);
        WriteInstruction(memory, pc + 0x90, 0xC801_00C8);
        WriteInstruction(memory, pc + 0x94, 0xEC20_0828);
        WriteInstruction(memory, pc + 0x98, 0x3C80_80BF);
        WriteInstruction(memory, pc + 0x9C, 0x3864_CA08);
        WriteInstruction(memory, pc + 0xA0, 0xC003_0000);
        WriteInstruction(memory, pc + 0xA4, 0xFC01_0040);
        WriteInstruction(memory, pc + 0xA8, 0x4081_0024);
        WriteInstruction(memory, pc + 0xC8, 0x4BFF_8549);
        WriteInstruction(memory, pc + 0xCC, 0x3BFF_0001);
        WriteInstruction(memory, pc + 0xD0, 0x2C1F_0800);
        WriteInstruction(memory, pc + 0xD4, 0x4180_FF2C);
    }

    private static void WriteSonicGeneratedModelPointerScan(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x7C08_02A6);
        WriteInstruction(memory, pc + 0x04, 0x9001_0004);
        WriteInstruction(memory, pc + 0x08, 0x9421_FFE8);
        WriteInstruction(memory, pc + 0x0C, 0x93E1_0014);
        WriteInstruction(memory, pc + 0x10, 0x93C1_0010);
        WriteInstruction(memory, pc + 0x14, 0x7C7E_1B78);
        WriteInstruction(memory, pc + 0x18, 0x3BE0_0000);
        WriteInstruction(memory, pc + 0x1C, 0x4800_0034);
        WriteInstruction(memory, pc + 0x20, 0x3C60_80BD);
        WriteInstruction(memory, pc + 0x24, 0x3863_3668);
        WriteInstruction(memory, pc + 0x28, 0x8063_0000);
        WriteInstruction(memory, pc + 0x2C, 0x8063_0020);
        WriteInstruction(memory, pc + 0x30, 0x1C1F_0014);
        WriteInstruction(memory, pc + 0x34, 0x7C03_002E);
        WriteInstruction(memory, pc + 0x38, 0x7C00_F040);
        WriteInstruction(memory, pc + 0x3C, 0x4082_0010);
        WriteInstruction(memory, pc + 0x40, 0x3C60_80BD);
        WriteInstruction(memory, pc + 0x44, 0x3863_B5CC);
        WriteInstruction(memory, pc + 0x48, 0x4B55_116D);
        WriteInstruction(memory, pc + 0x4C, 0x3BFF_0001);
        WriteInstruction(memory, pc + 0x50, 0x2C1F_0012);
        WriteInstruction(memory, pc + 0x54, 0x4180_FFCC);
        WriteInstruction(memory, pc + 0x58, 0x8001_001C);
        WriteInstruction(memory, pc + 0x5C, 0x83E1_0014);
        WriteInstruction(memory, pc + 0x60, 0x83C1_0010);
        WriteInstruction(memory, pc + 0x64, 0x3821_0018);
        WriteInstruction(memory, pc + 0x68, 0x7C08_03A6);
        WriteInstruction(memory, pc + 0x6C, 0x4E80_0020);
    }

    private static void WriteSonicGeneratedTileRangeScanLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x3C60_80BD);
        WriteInstruction(memory, pc + 0x04, 0x3883_4F58);
        WriteInstruction(memory, pc + 0x08, 0x80A4_0000);
        WriteInstruction(memory, pc + 0x0C, 0x1C7E_4400);
        WriteInstruction(memory, pc + 0x10, 0x1C9F_0044);
        WriteInstruction(memory, pc + 0x14, 0x7C63_2214);
        WriteInstruction(memory, pc + 0x18, 0x3C63_0002);
        WriteInstruction(memory, pc + 0x1C, 0x3863_6800);
        WriteInstruction(memory, pc + 0x20, 0x7C65_182E);
        WriteInstruction(memory, pc + 0x24, 0x3CA0_80BD);
        WriteInstruction(memory, pc + 0x28, 0x38A5_3120);
        WriteInstruction(memory, pc + 0x2C, 0xC825_0000);
        WriteInstruction(memory, pc + 0x30, 0x6C63_8000);
        WriteInstruction(memory, pc + 0x34, 0x9061_00CC);
        WriteInstruction(memory, pc + 0x38, 0x3C80_4330);
        WriteInstruction(memory, pc + 0x3C, 0x9081_00C8);
        WriteInstruction(memory, pc + 0x40, 0xC801_00C8);
        WriteInstruction(memory, pc + 0x44, 0xEC20_0828);
        WriteInstruction(memory, pc + 0x48, 0x3C60_80BF);
        WriteInstruction(memory, pc + 0x4C, 0x3863_CA10);
        WriteInstruction(memory, pc + 0x50, 0xC003_0000);
        WriteInstruction(memory, pc + 0x54, 0xFC01_0040);
        WriteInstruction(memory, pc + 0x58, 0x4C40_1382);
        WriteInstruction(memory, pc + 0x5C, 0x4082_011C);
        WriteInstruction(memory, pc + 0x60, 0x3C60_80BD);
        WriteInstruction(memory, pc + 0x64, 0x3863_4F58);
        WriteInstruction(memory, pc + 0x68, 0x80A3_0000);
        WriteInstruction(memory, pc + 0x6C, 0x1C9E_4400);
        WriteInstruction(memory, pc + 0x70, 0x1C7F_0044);
        WriteInstruction(memory, pc + 0x74, 0x7C64_1A14);
        WriteInstruction(memory, pc + 0x78, 0x3C63_0002);
        WriteInstruction(memory, pc + 0x7C, 0x3863_6800);
        WriteInstruction(memory, pc + 0x80, 0x7C85_182E);
        WriteInstruction(memory, pc + 0x84, 0x3C60_80BD);
        WriteInstruction(memory, pc + 0x88, 0x3863_3120);
        WriteInstruction(memory, pc + 0x8C, 0xC823_0000);
        WriteInstruction(memory, pc + 0x90, 0x6C84_8000);
        WriteInstruction(memory, pc + 0x94, 0x9081_00CC);
        WriteInstruction(memory, pc + 0x98, 0x3C60_4330);
        WriteInstruction(memory, pc + 0x9C, 0x9061_00C8);
        WriteInstruction(memory, pc + 0xA0, 0xC801_00C8);
        WriteInstruction(memory, pc + 0xA4, 0xEC20_0828);
        WriteInstruction(memory, pc + 0xA8, 0x3C60_80BF);
        WriteInstruction(memory, pc + 0xAC, 0x3863_CA08);
        WriteInstruction(memory, pc + 0xB0, 0xC003_0000);
        WriteInstruction(memory, pc + 0xB4, 0xFC01_0040);
        WriteInstruction(memory, pc + 0xB8, 0x4081_00C0);
        WriteInstruction(memory, pc + 0x178, 0x3BFF_0001);
        WriteInstruction(memory, pc + 0x17C, 0x2C1F_0100);
        WriteInstruction(memory, pc + 0x180, 0x4180_FE80);
    }

    private static void WriteIdentityByteTable(GameCubeMemory memory, uint table)
    {
        for (uint index = 0; index <= 0xFF; index++)
        {
            memory.Write8(table + index, (byte)index);
        }
    }

    private static float ReadSingle(GameCubeMemory memory, uint address) =>
        BitConverter.Int32BitsToSingle(unchecked((int)memory.Read32(address)));

    private static void WriteSingle(GameCubeMemory memory, uint address, float value) =>
        memory.Write32(address, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    private static void WriteQuantizedS16(GameCubeMemory memory, uint address, float value) =>
        memory.Write16(address, unchecked((ushort)(short)Math.Clamp((int)Math.Truncate(value * 256.0f), short.MinValue, short.MaxValue)));

    private static void WriteDouble(GameCubeMemory memory, uint address, double value)
    {
        ulong bits = BitConverter.DoubleToUInt64Bits(value);
        memory.Write32(address, (uint)(bits >> 32));
        memory.Write32(address + sizeof(uint), (uint)bits);
    }

    private static void AssertSingleNear(float expected, float actual) =>
        Assert.InRange(actual, expected - 0.00001f, expected + 0.00001f);

    private static byte GatherExpectedByte(ReadOnlySpan<byte> source, int firstBit)
    {
        int result = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            int bitIndex = firstBit + bit;
            result |= ((source[bitIndex / 8] >> (bitIndex & 7)) & 1) << bit;
        }

        return (byte)result;
    }

    private static void WriteInstruction(GameCubeMemory memory, uint address, uint instruction)
    {
        memory.Write32(address, instruction);
    }

    private static void WriteCtrByteCopyLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x8805_0000);
        WriteInstruction(memory, pc + 0x04, 0x9807_0000);
        WriteInstruction(memory, pc + 0x08, 0x8805_0001);
        WriteInstruction(memory, pc + 0x0C, 0x9807_0001);
        WriteInstruction(memory, pc + 0x10, 0x8805_0002);
        WriteInstruction(memory, pc + 0x14, 0x9807_0002);
        WriteInstruction(memory, pc + 0x18, 0x8805_0003);
        WriteInstruction(memory, pc + 0x1C, 0x9807_0003);
        WriteInstruction(memory, pc + 0x20, 0x8805_0004);
        WriteInstruction(memory, pc + 0x24, 0x9807_0004);
        WriteInstruction(memory, pc + 0x28, 0x8805_0005);
        WriteInstruction(memory, pc + 0x2C, 0x9807_0005);
        WriteInstruction(memory, pc + 0x30, 0x8805_0006);
        WriteInstruction(memory, pc + 0x34, 0x9807_0006);
        WriteInstruction(memory, pc + 0x38, 0x8805_0007);
        WriteInstruction(memory, pc + 0x3C, 0x38A5_0008);
        WriteInstruction(memory, pc + 0x40, 0x9807_0007);
        WriteInstruction(memory, pc + 0x44, 0x38E7_0008);
        WriteInstruction(memory, pc + 0x48, 0x4200_FFB8);
    }

    private static void WriteCtrSingleByteCopyLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x8805_0000);
        WriteInstruction(memory, pc + 0x04, 0x38A5_0001);
        WriteInstruction(memory, pc + 0x08, 0x9807_0000);
        WriteInstruction(memory, pc + 0x0C, 0x38E7_0001);
        WriteInstruction(memory, pc + 0x10, 0x4200_FFF0);
    }

    private static void WriteWordCopyLoop(GameCubeMemory memory, uint pc)
    {
        WriteInstruction(memory, pc + 0x00, 0x8006_0004);
        WriteInstruction(memory, pc + 0x04, 0x3484_FFFF);
        WriteInstruction(memory, pc + 0x08, 0x9003_0004);
        WriteInstruction(memory, pc + 0x0C, 0x8006_0008);
        WriteInstruction(memory, pc + 0x10, 0x9003_0008);
        WriteInstruction(memory, pc + 0x14, 0x8006_000C);
        WriteInstruction(memory, pc + 0x18, 0x9003_000C);
        WriteInstruction(memory, pc + 0x1C, 0x8006_0010);
        WriteInstruction(memory, pc + 0x20, 0x9003_0010);
        WriteInstruction(memory, pc + 0x24, 0x8006_0014);
        WriteInstruction(memory, pc + 0x28, 0x9003_0014);
        WriteInstruction(memory, pc + 0x2C, 0x8006_0018);
        WriteInstruction(memory, pc + 0x30, 0x9003_0018);
        WriteInstruction(memory, pc + 0x34, 0x8006_001C);
        WriteInstruction(memory, pc + 0x38, 0x9003_001C);
        WriteInstruction(memory, pc + 0x3C, 0x8406_0020);
        WriteInstruction(memory, pc + 0x40, 0x9403_0020);
        WriteInstruction(memory, pc + 0x44, 0x4082_FFBC);
    }
}
