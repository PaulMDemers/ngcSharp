using System.Reflection;
using NgcSharp.App;
using NgcSharp.Core;
using NgcSharp.Cpu;

namespace NgcSharp.Tests;

public sealed class DolRunnerTests
{
    [Fact]
    public void WriteWatchRangeMatchesMainRamAliases()
    {
        RunDolOptions options = new("game.dol", 1000, Trace: false, TracePath: null, DumpRegisters: false, DumpMmio: false, Quiet: true, WatchWriteRangeAddress: 0x0060_E4A0, WatchWriteRangeLength: 0x20);

        Assert.True(InvokeMatchesWriteWatchRange(options, 0x8060_E4B0, 4));
        Assert.True(InvokeMatchesWriteWatchRange(options, 0xC060_E4B0, 4));
        Assert.False(InvokeMatchesWriteWatchRange(options, 0x8060_E4C0, 4));
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
        Assert.Equal(48, skippedInstructions);
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

    private static bool InvokeFastForwardStringLengthRoutine(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardStringLengthRoutine", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find string-length fast-forward helper.");
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

    private static bool InvokeFastForwardSonicNormalizedStringScan(PowerPcState state, GameCubeBus bus, out int skippedInstructions)
    {
        MethodInfo method = typeof(DolRunner).GetMethod("TryFastForwardSonicNormalizedStringScan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Sonic normalized string scan fast-forward helper.");
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
