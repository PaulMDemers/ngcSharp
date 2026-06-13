using NgcSharp.Core;
using NgcSharp.Cpu;

namespace NgcSharp.Tests;

public sealed class PowerPcInterpreterTests
{
    [Fact]
    public void ExecutesSimpleIntegerProgram()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, Addi(3, 0, 1));
        WriteInstruction(memory, pc + 0x04, Addi(4, 3, 2));
        WriteInstruction(memory, pc + 0x08, Stw(4, 0, 0x100));
        WriteInstruction(memory, pc + 0x0C, B(0));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        PowerPcInterpreter interpreter = new();
        interpreter.Run(state, memory, 3);

        Assert.Equal(1u, state.Gpr[3]);
        Assert.Equal(3u, state.Gpr[4]);
        Assert.Equal(3u, memory.Read32(0x100));
        Assert.Equal(pc + 0x0C, state.Pc);
    }

    [Fact]
    public void BranchAndLinkStoresReturnAddressInLinkRegister()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, B(0x20, link: true));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(pc + 4, state.Lr);
        Assert.Equal(pc + 0x20, state.Pc);
    }

    [Fact]
    public void SystemCallEntersExceptionVector()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, 0x4400_0002);

        PowerPcState state = new()
        {
            Pc = pc,
            Msr = 0x0000_8002,
        };

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x8000_0C00u, state.Pc);
        Assert.Equal(pc + 4, state.Spr[26]);
        Assert.Equal(0x0000_8002u, state.Spr[27]);
        Assert.Equal(0u, state.Msr & 0x0000_8002u);
        Assert.Equal("sc", PowerPcDisassembler.Disassemble(0x4400_0002));
    }

    [Fact]
    public void ConditionRegisterLogicalInstructionsUpdateTargetBit()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, XlForm(bt: 6, ba: 6, bb: 6, xo: 193));
        WriteInstruction(memory, pc + 0x04, XlForm(bt: 7, ba: 6, bb: 6, xo: 289));

        PowerPcState state = new()
        {
            Pc = pc,
            Cr = 1u << (31 - 6),
        };

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(0u, state.Cr & (1u << (31 - 6)));
        Assert.NotEqual(0u, state.Cr & (1u << (31 - 7)));
        Assert.Equal("crxor 6, 6, 6", PowerPcDisassembler.Disassemble(XlForm(bt: 6, ba: 6, bb: 6, xo: 193)));
    }

    [Fact]
    public void MoveConditionRegisterFieldCopiesFourBitField()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, Mcrf(targetField: 1, sourceField: 3));

        PowerPcState state = new()
        {
            Pc = pc,
            Cr = 0x1234_5678,
        };

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x1434_5678u, state.Cr);
        Assert.Equal("mcrf cr1, cr3", PowerPcDisassembler.Disassemble(Mcrf(targetField: 1, sourceField: 3)));
    }

    [Fact]
    public void ByteAndHalfwordStoresTruncateRegisterValue()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, Stb(3, 0, 0x120));
        WriteInstruction(memory, pc + 0x04, Sth(3, 0, 0x122));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x1234_ABCD;

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(0xCD, memory.Read8(0x120));
        Assert.Equal(0xABCD, memory.Read16(0x122));
    }

    [Fact]
    public void StoreAndLoadMultipleWordTransferRegisterRange()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, DForm(47, rDOrS: 29, rA: 5, immediate: 0x004C));
        WriteInstruction(memory, pc + 0x04, DForm(46, rDOrS: 29, rA: 5, immediate: 0x004C));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[5] = 0x8000_1000;
        state.Gpr[29] = 0xAAAA_0001;
        state.Gpr[30] = 0xBBBB_0002;
        state.Gpr[31] = 0xCCCC_0003;

        new PowerPcInterpreter().Step(state, memory);
        state.Gpr[29] = 0;
        state.Gpr[30] = 0;
        state.Gpr[31] = 0;
        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xAAAA_0001u, state.Gpr[29]);
        Assert.Equal(0xBBBB_0002u, state.Gpr[30]);
        Assert.Equal(0xCCCC_0003u, state.Gpr[31]);
        Assert.Equal("stmw r29, 76(r5)", PowerPcDisassembler.Disassemble(DForm(47, rDOrS: 29, rA: 5, immediate: 0x004C)));
        Assert.Equal("lmw r29, 76(r5)", PowerPcDisassembler.Disassemble(DForm(46, rDOrS: 29, rA: 5, immediate: 0x004C)));
    }

    [Fact]
    public void IndexedUpdateLoadStoreWriteBackEffectiveAddress()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, XForm(31, rSOrD: 3, rA: 4, rB: 5, xo: 183));
        WriteInstruction(memory, pc + 0x04, XForm(31, rSOrD: 6, rA: 7, rB: 5, xo: 55));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xCAFE_BABE;
        state.Gpr[4] = 0x8000_1000;
        state.Gpr[5] = 0x20;
        state.Gpr[7] = 0x8000_1000;

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(0x8000_1020u, state.Gpr[4]);
        Assert.Equal(0x8000_1020u, state.Gpr[7]);
        Assert.Equal(0xCAFE_BABEu, state.Gpr[6]);
        Assert.Equal("stwux r3, r4, r5", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 4, rB: 5, xo: 183)));
        Assert.Equal("lwzux r6, r7, r5", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 6, rA: 7, rB: 5, xo: 55)));
    }

    [Fact]
    public void LogicalXFormWritesRaDestination()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 3, rA: 5, rB: 4, xo: 28));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xF0;
        state.Gpr[4] = 0xCC;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xF0u, state.Gpr[3]);
        Assert.Equal(0xC0u, state.Gpr[5]);
    }

    [Fact]
    public void NandWritesComplementOfAndToRa()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 3, rA: 5, rB: 4, xo: 476));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xF0;
        state.Gpr[4] = 0xCC;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xFFFF_FF3Fu, state.Gpr[5]);
        Assert.Equal("nand r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 5, rB: 4, xo: 476)));
    }

    [Fact]
    public void EqvWritesComplementOfXorToRa()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 3, rA: 5, rB: 4, xo: 284));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xF0F0_00FF;
        state.Gpr[4] = 0xFF00_00F0;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xF00F_FFF0u, state.Gpr[5]);
        Assert.Equal("eqv r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 5, rB: 4, xo: 284)));
    }

    [Fact]
    public void AndWithComplementClearsMaskedBits()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 3, rA: 5, rB: 4, xo: 60));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xFF00_FF00;
        state.Gpr[4] = 0x0F0F_0000;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xF000_FF00u, state.Gpr[5]);
        Assert.Equal("andc r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 5, rB: 4, xo: 60)));
    }

    [Fact]
    public void MultiplyLowWordStoresLowThirtyTwoBits()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 235));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xFFFF_FFFE;
        state.Gpr[4] = 3;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xFFFF_FFFAu, state.Gpr[5]);
        Assert.Equal("mullw r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 235)));
    }

    [Fact]
    public void MultiplyHighWordSupportsSignedAndUnsignedForms()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 11));
        WriteInstruction(memory, pc + 0x04, XForm(31, rSOrD: 8, rA: 6, rB: 7, xo: 75));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xFFFF_FFFF;
        state.Gpr[4] = 2;
        state.Gpr[6] = unchecked((uint)-2);
        state.Gpr[7] = 2;

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(1u, state.Gpr[5]);
        Assert.Equal(0xFFFF_FFFFu, state.Gpr[8]);
        Assert.Equal("mulhwu r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 11)));
        Assert.Equal("mulhw r8, r6, r7", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 8, rA: 6, rB: 7, xo: 75)));
    }

    [Fact]
    public void WordDivisionHandlesSignedAndUnsignedForms()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 459));
        WriteInstruction(memory, pc + 0x04, XForm(31, rSOrD: 8, rA: 6, rB: 7, xo: 491));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 10;
        state.Gpr[4] = 3;
        state.Gpr[6] = unchecked((uint)-9);
        state.Gpr[7] = 2;

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(3u, state.Gpr[5]);
        Assert.Equal(unchecked((uint)-4), state.Gpr[8]);
        Assert.Equal("divwu r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 459)));
        Assert.Equal("divw r8, r6, r7", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 8, rA: 6, rB: 7, xo: 491)));
    }

    [Fact]
    public void SubtractFromExtendedUsesAndUpdatesCarry()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 136));

        PowerPcState state = new()
        {
            Pc = pc,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = 3;
        state.Gpr[4] = 10;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(7u, state.Gpr[5]);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        Assert.Equal("subfe r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 136)));
    }

    [Fact]
    public void SubtractFromZeroExtendedUsesCarryIn()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 5, rA: 3, rB: 0, xo: 200));

        PowerPcState state = new()
        {
            Pc = pc,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = 3;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(unchecked((uint)-3), state.Gpr[5]);
        Assert.Equal(0u, state.Xer & 0x2000_0000);
        Assert.Equal("subfze r5, r3", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 3, rB: 0, xo: 200)));
    }

    [Fact]
    public void SubtractFromCarryingUpdatesCarry()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 8));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 3;
        state.Gpr[4] = 10;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(7u, state.Gpr[5]);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        Assert.Equal("subfc r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 8)));
    }

    [Fact]
    public void SubtractFromImmediateCarryingUpdatesCarry()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, DForm(8, rDOrS: 5, rA: 3, immediate: 10));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 3;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(7u, state.Gpr[5]);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        Assert.Equal("subfic r5, r3, 10", PowerPcDisassembler.Disassemble(DForm(8, rDOrS: 5, rA: 3, immediate: 10)));
    }

    [Fact]
    public void AddCarryingUpdatesCarry()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 10));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xFFFF_FFFF;
        state.Gpr[4] = 2;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(1u, state.Gpr[5]);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        Assert.Equal("addc r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 10)));
    }

    [Fact]
    public void AddExtendedUsesCarryInAndUpdatesCarry()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 138));

        PowerPcState state = new()
        {
            Pc = pc,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = 0xFFFF_FFFF;
        state.Gpr[4] = 0;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0u, state.Gpr[5]);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        Assert.Equal("adde r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 3, rB: 4, xo: 138)));
    }

    [Fact]
    public void NegateStoresTwosComplement()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 4, rA: 9, rB: 0, xo: 104));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[9] = 17;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xFFFF_FFEFu, state.Gpr[4]);
        Assert.Equal("neg r4, r9", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 4, rA: 9, rB: 0, xo: 104)));
    }

    [Fact]
    public void AddZeroExtendedUsesCarryIn()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 5, rA: 3, rB: 0, xo: 202));

        PowerPcState state = new()
        {
            Pc = pc,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = 41;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(42u, state.Gpr[5]);
        Assert.Equal(0u, state.Xer & 0x2000_0000);
        Assert.Equal("addze r5, r3", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 3, rB: 0, xo: 202)));
    }

    [Fact]
    public void AddMinusOneExtendedUsesCarryIn()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 5, rA: 3, rB: 0, xo: 234));

        PowerPcState state = new()
        {
            Pc = pc,
            Xer = 0x2000_0000,
        };
        state.Gpr[3] = 0xFFFF_FFFF;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xFFFF_FFFFu, state.Gpr[5]);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        Assert.Equal("addme r5, r3", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 3, rB: 0, xo: 234)));
    }

    [Fact]
    public void VariableWordShiftsUseLowFiveBitsAndClearForLargeShifts()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, XForm(31, rSOrD: 3, rA: 5, rB: 4, xo: 24));
        WriteInstruction(memory, pc + 0x04, XForm(31, rSOrD: 6, rA: 8, rB: 7, xo: 536));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x10;
        state.Gpr[4] = 3;
        state.Gpr[6] = 0x8000_0000;
        state.Gpr[7] = 0x20;

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(0x80u, state.Gpr[5]);
        Assert.Equal(0u, state.Gpr[8]);
        Assert.Equal("slw r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 5, rB: 4, xo: 24)));
        Assert.Equal("srw r8, r6, r7", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 6, rA: 8, rB: 7, xo: 536)));
    }

    [Fact]
    public void ArithmeticVariableRightShiftSignExtends()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 3, rA: 5, rB: 4, xo: 792));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xFFFF_FFF1;
        state.Gpr[4] = 2;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xFFFF_FFFCu, state.Gpr[5]);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        Assert.Equal("sraw r5, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 5, rB: 4, xo: 792)));
    }

    [Fact]
    public void ArithmeticImmediateRightShiftSignExtendsAndUpdatesCarry()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 3, rA: 9, rB: 2, xo: 824));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xFFFF_FFF1;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xFFFF_FFFCu, state.Gpr[9]);
        Assert.Equal(0x2000_0000u, state.Xer & 0x2000_0000);
        Assert.Equal("srawi r9, r3, 2", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 9, rB: 2, xo: 824)));
    }

    [Fact]
    public void CountLeadingZerosWritesCountToRa()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 3, rA: 4, rB: 0, xo: 26));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x0000_8000;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(16u, state.Gpr[4]);
        Assert.Equal("cntlzw r4, r3", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 4, rB: 0, xo: 26)));
    }

    [Fact]
    public void ExtendSignInstructionsWriteSignedWordToRa()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, XForm(31, rSOrD: 3, rA: 4, rB: 0, xo: 922));
        WriteInstruction(memory, pc + 0x04, XForm(31, rSOrD: 5, rA: 6, rB: 0, xo: 954));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x0000_8001;
        state.Gpr[5] = 0x0000_00F1;

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(0xFFFF_8001u, state.Gpr[4]);
        Assert.Equal(0xFFFF_FFF1u, state.Gpr[6]);
        Assert.Equal("extsh r4, r3", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 4, rB: 0, xo: 922)));
        Assert.Equal("extsb r6, r5", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 6, rB: 0, xo: 954)));
    }

    [Fact]
    public void RotateLeftWordImmediateThenMaskInsertPreservesUnmaskedBits()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, MForm(opcode: 20, rS: 3, rA: 4, shift: 8, maskBegin: 8, maskEnd: 15));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x0000_1200;
        state.Gpr[4] = 0xABCD_EF01;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xAB12_EF01u, state.Gpr[4]);
        Assert.Equal("rlwimi r4, r3, 8, 8, 15", PowerPcDisassembler.Disassemble(MForm(opcode: 20, rS: 3, rA: 4, shift: 8, maskBegin: 8, maskEnd: 15)));
    }

    [Fact]
    public void DspMailboxReadSequenceCombinesHighAndLowWords()
    {
        GameCubeBus bus = new();
        uint pc = 0x8000_3100;
        WriteInstruction(bus.Memory, pc + 0x00, DForm(15, rDOrS: 3, rA: 0, immediate: 0xCC00));
        WriteInstruction(bus.Memory, pc + 0x04, DForm(14, rDOrS: 3, rA: 3, immediate: 0x5000));
        WriteInstruction(bus.Memory, pc + 0x08, DForm(40, rDOrS: 0, rA: 3, immediate: 0x0004));
        WriteInstruction(bus.Memory, pc + 0x0C, DForm(40, rDOrS: 3, rA: 3, immediate: 0x0006));
        WriteInstruction(bus.Memory, pc + 0x10, MForm(opcode: 20, rS: 0, rA: 3, shift: 16, maskBegin: 0, maskEnd: 15));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, bus, 5);

        Assert.Equal(0x8071_FEEDu, state.Gpr[3]);
    }

    [Fact]
    public void CompareImmediateAndConditionalBranchCanSkipInstruction()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, Addi(3, 0, 5));
        WriteInstruction(memory, pc + 0x04, Cmpi(crField: 0, rA: 3, immediate: 5));
        WriteInstruction(memory, pc + 0x08, Bc(bo: 12, bi: 2, offset: 8));
        WriteInstruction(memory, pc + 0x0C, Addi(4, 0, 1));
        WriteInstruction(memory, pc + 0x10, Addi(4, 0, 2));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, memory, 4);

        Assert.Equal(2u, state.Gpr[4]);
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
    }

    [Fact]
    public void CountRegisterBranchCanDriveSmallLoop()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, Addi(3, 0, 0));
        WriteInstruction(memory, pc + 0x04, Addi(4, 0, 3));
        WriteInstruction(memory, pc + 0x08, MtSpr(rS: 4, spr: 9));
        WriteInstruction(memory, pc + 0x0C, Addi(3, 3, 1));
        WriteInstruction(memory, pc + 0x10, Bc(bo: 16, bi: 0, offset: -4));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, memory, 9);

        Assert.Equal(3u, state.Gpr[3]);
        Assert.Equal(0u, state.Ctr);
        Assert.Equal(pc + 0x14, state.Pc);
    }

    [Fact]
    public void LinkRegisterCanBeMovedAndBranchedThrough()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, Addi(6, 0, 0x3120));
        WriteInstruction(memory, pc + 0x04, Oris(6, 6, 0x8000));
        WriteInstruction(memory, pc + 0x08, MtSpr(rS: 6, spr: 8));
        WriteInstruction(memory, pc + 0x0C, Mfspr(rD: 7, spr: 8));
        WriteInstruction(memory, pc + 0x10, Bclr(bo: 20, bi: 0));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, memory, 5);

        Assert.Equal(0x8000_3120u, state.Gpr[7]);
        Assert.Equal(0x8000_3120u, state.Pc);
    }

    [Fact]
    public void MachineStateRegisterCanBeMovedToAndFromGpr()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, Addi(3, 0, 0x1234));
        WriteInstruction(memory, pc + 0x04, MtMsr(3));
        WriteInstruction(memory, pc + 0x08, MfMsr(4));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, memory, 3);

        Assert.Equal(0x1234u, state.Msr);
        Assert.Equal(0x1234u, state.Gpr[4]);
    }

    [Fact]
    public void UnknownStartupSprsAreStateBacked()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, Addi(3, 0, 0x4567));
        WriteInstruction(memory, pc + 0x04, MtSpr(rS: 3, spr: 26));
        WriteInstruction(memory, pc + 0x08, Mfspr(rD: 4, spr: 26));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, memory, 3);

        Assert.Equal(0x4567u, state.Spr[26]);
        Assert.Equal(0x4567u, state.Gpr[4]);
    }

    [Fact]
    public void TimeBaseIncrementsAndCanBeRead()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, Mftb(rD: 3, tbr: 268));
        WriteInstruction(memory, pc + 0x04, Mftb(rD: 4, tbr: 269));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(1u, state.Gpr[3]);
        Assert.Equal(0u, state.Gpr[4]);
        Assert.Equal(2u, state.TimeBase);
        Assert.Equal("mftb r3", PowerPcDisassembler.Disassemble(Mftb(rD: 3, tbr: 268)));
    }

    [Fact]
    public void DecrementerCountsDownWithInstructions()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, 0x6000_0000);

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Spr[22] = 3;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(2u, state.Spr[22]);
        Assert.Equal(1ul, state.TimeBase);
        Assert.Equal(pc + 4, state.Pc);
    }

    [Fact]
    public void DecrementerVectorsWhenNegativeAndExternalInterruptsEnabled()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, 0x6000_0000);
        WriteInstruction(memory, pc + 4, 0x6000_0000);

        PowerPcState state = new()
        {
            Pc = pc,
            Msr = 0x8002,
        };
        state.Spr[22] = 0;

        PowerPcInterpreter interpreter = new();

        uint firstInstruction = interpreter.Step(state, memory);

        Assert.Equal(0x6000_0000u, firstInstruction);
        Assert.Equal(pc + 4, state.Pc);
        Assert.Equal(0xFFFF_FFFFu, state.Spr[22]);

        uint instruction = interpreter.Step(state, memory);

        Assert.Equal(0u, instruction);
        Assert.Equal(0x8000_0900u, state.Pc);
        Assert.Equal(pc + 4, state.Spr[26]);
        Assert.Equal(0x8002u, state.Spr[27]);
        Assert.Equal(0u, state.Msr & 0x8000);
        Assert.Equal(0u, state.Msr & 0x0002);
        Assert.Equal(0xFFFF_FFFEu, state.Spr[22]);
        Assert.Equal(2ul, state.TimeBase);
    }

    [Fact]
    public void DecrementerDoesNotVectorWhenExternalInterruptsDisabled()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, 0x6000_0000);
        WriteInstruction(memory, pc + 4, 0x6000_0000);

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Spr[22] = 0;

        PowerPcInterpreter interpreter = new();

        uint firstInstruction = interpreter.Step(state, memory);
        uint secondInstruction = interpreter.Step(state, memory);

        Assert.Equal(0x6000_0000u, firstInstruction);
        Assert.Equal(0x6000_0000u, secondInstruction);
        Assert.Equal(pc + 8, state.Pc);
        Assert.Equal(0xFFFF_FFFEu, state.Spr[22]);
    }

    [Fact]
    public void HighNumberedSprWritesAreStateBackedNoOps()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, Addi(3, 0, 0x2222));
        WriteInstruction(memory, pc + 0x04, MtSpr(rS: 3, spr: 1008));
        WriteInstruction(memory, pc + 0x08, Mfspr(rD: 4, spr: 1008));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, memory, 3);

        Assert.Equal(0x2222u, state.Spr[1008]);
        Assert.Equal(0x2222u, state.Gpr[4]);
    }

    [Fact]
    public void ReturnFromInterruptRestoresPcAndMsrFromSrrRegisters()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, Rfi());

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Spr[26] = 0x8000_4000;
        state.Spr[27] = 0x3002;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x8000_4000u, state.Pc);
        Assert.Equal(0x3002u, state.Msr);
    }

    [Fact]
    public void InstructionSyncIsANoopForInterpreter()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, Isync());

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(pc + 4, state.Pc);
        Assert.Equal("isync", PowerPcDisassembler.Disassemble(Isync()));
    }

    [Fact]
    public void SyncIsANoopForInterpreter()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, Sync());

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(pc + 4, state.Pc);
        Assert.Equal("sync", PowerPcDisassembler.Disassemble(Sync()));
    }

    [Fact]
    public void SegmentRegistersAreStateBacked()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, Addi(3, 0, 0x1234));
        WriteInstruction(memory, pc + 0x04, MtSr(rS: 3, sr: 7));
        WriteInstruction(memory, pc + 0x08, MfSr(rD: 4, sr: 7));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, memory, 3);

        Assert.Equal(0x1234u, state.SegmentRegisters[7]);
        Assert.Equal(0x1234u, state.Gpr[4]);
    }

    [Fact]
    public void FloatingDoubleLoadAndStorePreserveRawBits()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write32(0x100, 0x3FF8_0000);
        memory.Write32(0x104, 0x0000_0000);
        WriteInstruction(memory, pc + 0x00, Lfd(fD: 1, rA: 0, displacement: 0x100));
        WriteInstruction(memory, pc + 0x04, Stfd(fS: 1, rA: 0, displacement: 0x108));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(1.5d, state.Fpr[1]);
        Assert.Equal(0x3FF8_0000u, memory.Read32(0x108));
        Assert.Equal(0u, memory.Read32(0x10C));
    }

    [Fact]
    public void FloatingDoubleIntegerConversionIdiomProducesExpectedSingle()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write32(0x100, 0x4330_0000);
        memory.Write32(0x104, 0x8000_0280);
        memory.Write32(0x108, 0x4330_0000);
        memory.Write32(0x10C, 0x8000_0000);
        WriteInstruction(memory, pc + 0x00, Lfd(fD: 0, rA: 0, displacement: 0x100));
        WriteInstruction(memory, pc + 0x04, Lfd(fD: 4, rA: 0, displacement: 0x108));
        WriteInstruction(memory, pc + 0x08, AForm(59, fD: 0, fA: 0, fB: 4, fC: 0, xo: 20));
        WriteInstruction(memory, pc + 0x0C, DForm(52, rDOrS: 0, rA: 0, immediate: 0x110));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, memory, 4);

        Assert.Equal(640.0d, state.Fpr[0]);
        Assert.Equal(0x4420_0000u, memory.Read32(0x110));
    }

    [Fact]
    public void FloatingSingleLoadAndStorePreserveSinglePrecisionBits()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write32(0x100, 0x3FC0_0000);
        WriteInstruction(memory, pc + 0x00, DForm(48, rDOrS: 1, rA: 0, immediate: 0x100));
        WriteInstruction(memory, pc + 0x04, DForm(52, rDOrS: 1, rA: 0, immediate: 0x104));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(1.5d, state.Fpr[1]);
        Assert.Equal(0x3FC0_0000u, memory.Read32(0x104));
        Assert.Equal("lfs f1, 256(r0)", PowerPcDisassembler.Disassemble(DForm(48, rDOrS: 1, rA: 0, immediate: 0x100)));
        Assert.Equal("stfs f1, 260(r0)", PowerPcDisassembler.Disassemble(DForm(52, rDOrS: 1, rA: 0, immediate: 0x104)));
    }

    [Fact]
    public void FloatingDoubleStoreUpdateWritesEffectiveAddress()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, DForm(55, rDOrS: 2, rA: 3, immediate: 8));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x100;
        state.Fpr[2] = 1.5d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x108u, state.Gpr[3]);
        Assert.Equal(0x3FF8_0000u, memory.Read32(0x108));
        Assert.Equal(0u, memory.Read32(0x10C));
        Assert.Equal("stfdu f2, 8(r3)", PowerPcDisassembler.Disassemble(DForm(55, rDOrS: 2, rA: 3, immediate: 8)));
    }

    [Fact]
    public void FloatingDoubleLoadUpdateLoadsRawBitsAndWritesEffectiveAddress()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write32(0x108, 0x3FF8_0000);
        memory.Write32(0x10C, 0x0000_0000);
        WriteInstruction(memory, pc, DForm(51, rDOrS: 2, rA: 3, immediate: 8));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x100;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x108u, state.Gpr[3]);
        Assert.Equal(1.5d, state.Fpr[2]);
        Assert.Equal("lfdu f2, 8(r3)", PowerPcDisassembler.Disassemble(DForm(51, rDOrS: 2, rA: 3, immediate: 8)));
    }

    [Fact]
    public void FloatingIndexedLoadAndStorePreserveSinglePrecisionBits()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write32(0x104, 0x3FC0_0000);
        WriteInstruction(memory, pc + 0x00, XForm(31, rSOrD: 1, rA: 3, rB: 4, xo: 535));
        WriteInstruction(memory, pc + 0x04, XForm(31, rSOrD: 1, rA: 3, rB: 5, xo: 663));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x100;
        state.Gpr[4] = 4;
        state.Gpr[5] = 8;

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(1.5d, state.Fpr[1]);
        Assert.Equal(0x3FC0_0000u, memory.Read32(0x108));
        Assert.Equal("lfsx f1, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 1, rA: 3, rB: 4, xo: 535)));
        Assert.Equal("stfsx f1, r3, r5", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 1, rA: 3, rB: 5, xo: 663)));
    }

    [Fact]
    public void FloatingIndexedUpdateInstructionsWriteEffectiveAddress()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write32(0x104, 0x3FF8_0000);
        memory.Write32(0x108, 0);
        WriteInstruction(memory, pc + 0x00, XForm(31, rSOrD: 2, rA: 3, rB: 4, xo: 631));
        WriteInstruction(memory, pc + 0x04, XForm(31, rSOrD: 2, rA: 5, rB: 6, xo: 759));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x100;
        state.Gpr[4] = 4;
        state.Gpr[5] = 0x110;
        state.Gpr[6] = 8;

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(1.5d, state.Fpr[2]);
        Assert.Equal(0x104u, state.Gpr[3]);
        Assert.Equal(0x118u, state.Gpr[5]);
        Assert.Equal(0x3FF8_0000u, memory.Read32(0x118));
        Assert.Equal(0u, memory.Read32(0x11C));
        Assert.Equal("lfdux f2, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 2, rA: 3, rB: 4, xo: 631)));
        Assert.Equal("stfdux f2, r5, r6", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 2, rA: 5, rB: 6, xo: 759)));
    }

    [Fact]
    public void PairedSingleQuantizedLoadAndStoreUseFirstScalar()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write32(0x100, 0x3FC0_0000);
        WriteInstruction(memory, pc + 0x00, PsqMemory(opcode: 56, fDOrS: 1, rA: 3, displacement: 0x10));
        WriteInstruction(memory, pc + 0x04, PsqMemory(opcode: 60, fDOrS: 1, rA: 3, displacement: 0x14));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xF0;

        new PowerPcInterpreter().Run(state, memory, 2);

        Assert.Equal(1.5d, state.Fpr[1]);
        Assert.Equal(0x3FC0_0000u, memory.Read32(0x104));
        Assert.Equal("psq_l f1, 16(r3), 0, 0", PowerPcDisassembler.Disassemble(PsqMemory(opcode: 56, fDOrS: 1, rA: 3, displacement: 0x10)));
    }

    [Fact]
    public void PairedSingleQuantizedLoadUsesGqrTypeScaleAndStride()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write8(0x100, 0x80);
        memory.Write8(0x101, 0x40);
        WriteInstruction(memory, pc, PsqMemory(opcode: 56, fDOrS: 1, rA: 3, displacement: 0, w: 0, i: 1));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x100;
        state.Spr[913] = (1u << 24) | (6u << 16);

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(-64.0d, state.Fpr[1]);
        Assert.Equal(32.0d, state.FprPair1[1]);
    }

    [Fact]
    public void PairedSingleQuantizedLoadSingleFillsSecondLaneWithOne()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write16(0x100, 0x0040);
        WriteInstruction(memory, pc, PsqMemory(opcode: 56, fDOrS: 1, rA: 3, displacement: 0, w: 1, i: 1));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x100;
        state.Spr[913] = (2u << 24) | (5u << 16);

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(16.0d, state.Fpr[1]);
        Assert.Equal(1.0d, state.FprPair1[1]);
    }

    [Fact]
    public void PairedSingleQuantizedStoreUsesGqrTypeScaleClampAndStride()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, PsqMemory(opcode: 60, fDOrS: 1, rA: 3, displacement: 0, w: 0, i: 1));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x100;
        state.Fpr[1] = 100.0d;
        state.FprPair1[1] = 200.0d;
        state.Spr[913] = (1u << 8) | 4u;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(200, memory.Read8(0x100));
        Assert.Equal(255, memory.Read8(0x101));
    }

    [Fact]
    public void PairedSingleQuantizedStoreSignedIntegerRoundsTowardZero()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, PsqMemory(opcode: 60, fDOrS: 1, rA: 3, displacement: 0, w: 0, i: 1));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x100;
        state.Fpr[1] = -63.9d;
        state.FprPair1[1] = 63.9d;
        state.Spr[913] = (1u << 8) | 6u;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x81, memory.Read8(0x100));
        Assert.Equal(0x7F, memory.Read8(0x101));
    }

    [Fact]
    public void PairedSingleMergeAndStorePreserveBothLanes()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write32(0x100, 0x0000_0000);
        memory.Write32(0x104, 0x3F80_0000);
        WriteInstruction(memory, pc + 0x00, PsqMemory(opcode: 56, fDOrS: 1, rA: 3, displacement: 0));
        WriteInstruction(memory, pc + 0x04, XForm(4, rSOrD: 2, rA: 1, rB: 1, xo: 592));
        WriteInstruction(memory, pc + 0x08, PsqMemory(opcode: 60, fDOrS: 2, rA: 3, displacement: 8));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x100;

        new PowerPcInterpreter().Run(state, memory, 3);

        Assert.Equal(1.0d, state.Fpr[2]);
        Assert.Equal(0.0d, state.FprPair1[2]);
        Assert.Equal(0x3F80_0000u, memory.Read32(0x108));
        Assert.Equal(0u, memory.Read32(0x10C));
        Assert.Equal("ps_merge10 f2, f1, f1", PowerPcDisassembler.Disassemble(XForm(4, rSOrD: 2, rA: 1, rB: 1, xo: 592)));
    }

    [Fact]
    public void PairedSingleMergeReadsSourceBeforeOverwritingDestination()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(4, rSOrD: 4, rA: 2, rB: 4, xo: 528));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Fpr[2] = 1.0d;
        state.FprPair1[2] = 99.0d;
        state.Fpr[4] = -0.0d;
        state.FprPair1[4] = 77.0d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(1.0d, state.Fpr[4]);
        Assert.True(BitConverter.DoubleToInt64Bits(state.FprPair1[4]) < 0);
        Assert.Equal("ps_merge00 f4, f2, f4", PowerPcDisassembler.Disassemble(XForm(4, rSOrD: 4, rA: 2, rB: 4, xo: 528)));
    }

    [Fact]
    public void PairedSingleArithmeticUpdatesBothLanes()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(4, rSOrD: 3, rA: 1, rB: 2, xo: 20));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Fpr[1] = 5.0d;
        state.FprPair1[1] = 7.0d;
        state.Fpr[2] = 2.0d;
        state.FprPair1[2] = 3.0d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(3.0d, state.Fpr[3]);
        Assert.Equal(4.0d, state.FprPair1[3]);
        Assert.Equal("ps_sub f3, f1, f2", PowerPcDisassembler.Disassemble(XForm(4, rSOrD: 3, rA: 1, rB: 2, xo: 20)));
    }

    [Fact]
    public void PairedSingleCompareUpdatesConditionRegisterField()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(4, rSOrD: 0, rA: 1, rB: 2, xo: 32));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Fpr[1] = 1.0d;
        state.Fpr[2] = 2.0d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x8u, state.Cr >> 28);
        Assert.Equal("ps_cmpu cr0, f1, f2", PowerPcDisassembler.Disassemble(XForm(4, rSOrD: 0, rA: 1, rB: 2, xo: 32)));
    }

    [Fact]
    public void PairedSingleSelectChoosesPerLane()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(4, rSOrD: 4, rA: 1, rB: 2, xo: 23) | (3u << 6));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Fpr[1] = -1.0d;
        state.FprPair1[1] = 1.0d;
        state.Fpr[2] = 20.0d;
        state.FprPair1[2] = 21.0d;
        state.Fpr[3] = 30.0d;
        state.FprPair1[3] = 31.0d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(20.0d, state.Fpr[4]);
        Assert.Equal(31.0d, state.FprPair1[4]);
        Assert.Equal("ps_sel f4, f1, f3, f2", PowerPcDisassembler.Disassemble(XForm(4, rSOrD: 4, rA: 1, rB: 2, xo: 23) | (3u << 6)));
    }

    [Fact]
    public void PairedSingleScalarMultiplyAddUsesSelectedMultiplierLane()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(4, rSOrD: 4, rA: 1, rB: 2, xo: 46) | (3u << 6));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Fpr[1] = 2.0d;
        state.FprPair1[1] = 3.0d;
        state.Fpr[2] = 5.0d;
        state.FprPair1[2] = 7.0d;
        state.Fpr[3] = 11.0d;
        state.FprPair1[3] = 13.0d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(27.0d, state.Fpr[4]);
        Assert.Equal(40.0d, state.FprPair1[4]);
        Assert.Equal("ps_madds0 f4, f1, f3, f2", PowerPcDisassembler.Disassemble(XForm(4, rSOrD: 4, rA: 1, rB: 2, xo: 46) | (3u << 6)));
    }

    [Fact]
    public void SonicRootMatrixMultiplyRoutineMatchesAffineReference()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteSonicRootMatrixMultiplyRoutine(memory, pc);

        uint leftAddress = 0x1000;
        uint rightAddress = 0x1100;
        uint outputAddress = 0x1200;
        float[] left =
        [
            0.75f, -0.25f, 0.50f, 10.0f,
            0.10f, 0.90f, -0.30f, -4.0f,
            -0.45f, 0.20f, 0.85f, 6.5f,
        ];
        float[] right =
        [
            0.60f, 0.20f, -0.70f, 3.0f,
            -0.35f, 0.95f, 0.15f, -2.0f,
            0.50f, 0.10f, 0.80f, 8.0f,
        ];

        WriteAffineMatrix(memory, leftAddress, left);
        WriteAffineMatrix(memory, rightAddress, right);
        WriteFloat(memory, 0x803A_D5C8, 0.0f);
        WriteFloat(memory, 0x803A_D5CC, 1.0f);

        PowerPcState state = new()
        {
            Pc = pc,
            Lr = 0x8000_4000,
        };
        state.Gpr[1] = 0x2000;
        state.Gpr[3] = leftAddress;
        state.Gpr[4] = rightAddress;
        state.Gpr[5] = outputAddress;

        new PowerPcInterpreter().Run(state, memory, 51);

        float[] expected = MultiplyAffine3x4(left, right);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.InRange(Math.Abs(ReadFloat(memory, outputAddress + (uint)(index * 4)) - expected[index]), 0.0f, 0.0001f);
        }

        Assert.Equal(state.Lr, state.Pc);
    }

    [Fact]
    public void FloatingSingleArithmeticRoundsToSinglePrecision()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, AForm(opcode: 59, fD: 1, fA: 1, fB: 2, fC: 0, xo: 20));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Fpr[1] = 3.25d;
        state.Fpr[2] = 1.25d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(2.0f, (float)state.Fpr[1]);
        Assert.Equal("fsubs f1, f1, f2", PowerPcDisassembler.Disassemble(0xEC21_1028));
    }

    [Fact]
    public void FloatingSingleMultiplyAddUsesFcAndFbOperands()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, AForm(opcode: 59, fD: 0, fA: 2, fB: 3, fC: 6, xo: 29));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Fpr[2] = 2.0d;
        state.Fpr[3] = 0.5d;
        state.Fpr[6] = 4.0d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(8.5f, (float)state.Fpr[0]);
        Assert.Equal("fmadds f0, f2, f6, f3", PowerPcDisassembler.Disassemble(AForm(opcode: 59, fD: 0, fA: 2, fB: 3, fC: 6, xo: 29)));
    }

    [Fact]
    public void FloatingSingleReciprocalEstimateComputesReciprocal()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, AForm(opcode: 59, fD: 0, fA: 0, fB: 7, fC: 0, xo: 24));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Fpr[7] = 4.0d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0.25f, (float)state.Fpr[0]);
        Assert.Equal("fres f0, f7", PowerPcDisassembler.Disassemble(AForm(opcode: 59, fD: 0, fA: 0, fB: 7, fC: 0, xo: 24)));
    }

    [Fact]
    public void FloatingDoubleMultiplyUsesFiveBitArithmeticOpcode()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, AForm(opcode: 63, fD: 0, fA: 0, fB: 0, fC: 1, xo: 25));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Fpr[0] = 3.0d;
        state.Fpr[1] = 2.0d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(6.0d, state.Fpr[0]);
        Assert.Equal("fmul f0, f0, f1", PowerPcDisassembler.Disassemble(AForm(opcode: 63, fD: 0, fA: 0, fB: 0, fC: 1, xo: 25)));
    }

    [Fact]
    public void FloatingConvertToIntegerWordTruncatesTowardZero()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, AForm(opcode: 63, fD: 0, fA: 0, fB: 1, fC: 0, xo: 15));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Fpr[1] = -12.75d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(unchecked((uint)-12), (uint)BitConverter.DoubleToInt64Bits(state.Fpr[0]));
        Assert.Equal("fctiwz f0, f1", PowerPcDisassembler.Disassemble(AForm(opcode: 63, fD: 0, fA: 0, fB: 1, fC: 0, xo: 15)));
    }

    [Fact]
    public void StoreFloatingIntegerWordIndexedWritesLowWord()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 1, rA: 3, rB: 4, xo: 983));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x100;
        state.Gpr[4] = 0x20;
        state.Fpr[1] = BitConverter.Int64BitsToDouble(unchecked((long)0xFFFF_FFFF_FFFF_FFF4));

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xFFFF_FFF4u, memory.Read32(0x120));
        Assert.Equal("stfiwx f1, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 1, rA: 3, rB: 4, xo: 983)));
    }

    [Fact]
    public void LoadStringWordIndexedUsesXerByteCount()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write32(0x120, 0x1122_3344);
        memory.Write32(0x124, 0x5566_7788);
        WriteInstruction(memory, pc, XForm(31, rSOrD: 31, rA: 3, rB: 4, xo: 533));

        PowerPcState state = new()
        {
            Pc = pc,
            Xer = 6,
        };
        state.Gpr[3] = 0x100;
        state.Gpr[4] = 0x20;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x1122_3344u, state.Gpr[31]);
        Assert.Equal(0x5566_0000u, state.Gpr[0]);
        Assert.Equal("lswx r31, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 31, rA: 3, rB: 4, xo: 533)));
    }

    [Fact]
    public void StoreStringWordIndexedUsesXerByteCount()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 31, rA: 3, rB: 4, xo: 661));

        PowerPcState state = new()
        {
            Pc = pc,
            Xer = 6,
        };
        state.Gpr[3] = 0x100;
        state.Gpr[4] = 0x20;
        state.Gpr[31] = 0x1122_3344;
        state.Gpr[0] = 0x5566_7788;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x1122_3344u, memory.Read32(0x120));
        Assert.Equal(0x5566_0000u, memory.Read32(0x124));
        Assert.Equal("stswx r31, r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 31, rA: 3, rB: 4, xo: 661)));
    }

    [Fact]
    public void StoreStringWordImmediateUsesEncodedByteCount()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 31, rA: 3, rB: 6, xo: 725));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x120;
        state.Gpr[31] = 0x1122_3344;
        state.Gpr[0] = 0x5566_7788;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x1122_3344u, memory.Read32(0x120));
        Assert.Equal(0x5566_0000u, memory.Read32(0x124));
        Assert.Equal("stswi r31, r3, 6", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 31, rA: 3, rB: 6, xo: 725)));
        Assert.Equal("stswi r5, r3, 32", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 5, rA: 3, rB: 0, xo: 725)));
    }

    [Fact]
    public void FloatingCompareUpdatesConditionRegisterField()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, AForm(opcode: 63, fD: 0, fA: 1, fB: 2, fC: 0, xo: 32));

        PowerPcState state = new()
        {
            Pc = pc,
            Cr = 0xFFFF_FFFF,
        };
        state.Fpr[1] = 1.0d;
        state.Fpr[2] = 2.0d;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x8u, state.Cr >> 28);
        Assert.Equal("fcmpo cr0, f1, f2", PowerPcDisassembler.Disassemble(AForm(opcode: 63, fD: 0, fA: 1, fB: 2, fC: 0, xo: 32)));
    }

    [Fact]
    public void FloatingUnaryOperationsUseSourceRegister()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, AForm(opcode: 63, fD: 1, fA: 0, fB: 2, fC: 0, xo: 72));
        WriteInstruction(memory, pc + 0x04, AForm(opcode: 63, fD: 3, fA: 0, fB: 2, fC: 0, xo: 40));
        WriteInstruction(memory, pc + 0x08, AForm(opcode: 63, fD: 4, fA: 0, fB: 2, fC: 0, xo: 264));
        WriteInstruction(memory, pc + 0x0C, AForm(opcode: 63, fD: 5, fA: 0, fB: 2, fC: 0, xo: 12));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Fpr[2] = -3.5d;

        new PowerPcInterpreter().Run(state, memory, 4);

        Assert.Equal(-3.5d, state.Fpr[1]);
        Assert.Equal(3.5d, state.Fpr[3]);
        Assert.Equal(3.5d, state.Fpr[4]);
        Assert.Equal(-3.5f, (float)state.Fpr[5]);
        Assert.Equal("fmr f1, f2", PowerPcDisassembler.Disassemble(AForm(opcode: 63, fD: 1, fA: 0, fB: 2, fC: 0, xo: 72)));
        Assert.Equal("frsp f5, f2", PowerPcDisassembler.Disassemble(AForm(opcode: 63, fD: 5, fA: 0, fB: 2, fC: 0, xo: 12)));
    }

    [Fact]
    public void MoveToFpscrFieldsCopiesSelectedFields()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, Mtfsf(fieldMask: 0xF0, fB: 1));

        PowerPcState state = new()
        {
            Pc = pc,
            Fpscr = 0x1111_2222,
        };
        state.Fpr[1] = BitConverter.Int64BitsToDouble(unchecked((long)0xAAAA_BBBB_CCCC_DDDD));

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xCCCC_2222u, state.Fpscr);
        Assert.Equal("mtfsf 0xF0, f1", PowerPcDisassembler.Disassemble(Mtfsf(fieldMask: 0xF0, fB: 1)));
    }

    [Fact]
    public void MoveFromFpscrCopiesRawBitsToFloatingRegister()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, AForm(opcode: 63, fD: 7, fA: 0, fB: 0, fC: 0, xo: 583));

        PowerPcState state = new()
        {
            Pc = pc,
            Fpscr = 0xF00D_CAFE,
        };

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x0000_0000_F00D_CAFEul, unchecked((ulong)BitConverter.DoubleToInt64Bits(state.Fpr[7])));
        Assert.Equal(0x0000_0000_F00D_CAFEul, unchecked((ulong)BitConverter.DoubleToInt64Bits(state.FprPair1[7])));
        Assert.Equal("mffs f7", PowerPcDisassembler.Disassemble(AForm(opcode: 63, fD: 7, fA: 0, fB: 0, fC: 0, xo: 583)));
    }

    [Fact]
    public void MoveToFpscrBitSetsAndClearsBits()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, Mtfsb1(29));
        WriteInstruction(memory, pc + 0x04, Mtfsb0(29));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Step(state, memory);
        Assert.Equal(1u << 2, state.Fpscr);
        Assert.Equal("mtfsb1 29", PowerPcDisassembler.Disassemble(Mtfsb1(29)));

        new PowerPcInterpreter().Step(state, memory);
        Assert.Equal(0u, state.Fpscr);
    }

    [Fact]
    public void UpdateStoresWriteBackEffectiveAddress()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, Stwu(rS: 3, rA: 1, displacement: -16));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[1] = 0x8000_1000;
        state.Gpr[3] = 0xDEAD_BEEF;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x8000_0FF0u, state.Gpr[1]);
        Assert.Equal(0xDEAD_BEEFu, memory.Read32(0x8000_0FF0));
    }

    [Fact]
    public void IndexedLoadStoreInstructionsUseRaPlusRb()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc + 0x00, XForm(31, rSOrD: 3, rA: 4, rB: 5, xo: 151));
        WriteInstruction(memory, pc + 0x04, XForm(31, rSOrD: 6, rA: 4, rB: 5, xo: 23));
        WriteInstruction(memory, pc + 0x08, XForm(31, rSOrD: 7, rA: 4, rB: 8, xo: 215));
        WriteInstruction(memory, pc + 0x0C, XForm(31, rSOrD: 9, rA: 4, rB: 8, xo: 87));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x1234_5678;
        state.Gpr[4] = 0x8000_1000;
        state.Gpr[5] = 0x20;
        state.Gpr[7] = 0xABCD;
        state.Gpr[8] = 0x30;

        new PowerPcInterpreter().Run(state, memory, 4);

        Assert.Equal(0x1234_5678u, memory.Read32(0x8000_1020));
        Assert.Equal(0x1234_5678u, state.Gpr[6]);
        Assert.Equal(0xCD, memory.Read8(0x8000_1030));
        Assert.Equal(0xCDu, state.Gpr[9]);
        Assert.Equal("stwx r3, r4, r5", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 4, rB: 5, xo: 151)));
    }

    [Fact]
    public void LoadWordAndReserveActsLikeIndexedLoadInSingleThreadedInterpreter()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write32(0x8000_1020, 0xCAFE_BABE);
        WriteInstruction(memory, pc, XForm(31, rSOrD: 3, rA: 4, rB: 5, xo: 20));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[4] = 0x8000_1000;
        state.Gpr[5] = 0x20;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xCAFE_BABEu, state.Gpr[3]);
        Assert.Equal("lwarx r3, r4, r5", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 4, rB: 5, xo: 20)));
    }

    [Fact]
    public void StoreWordConditionalSucceedsWithMatchingReservation()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 3, rA: 4, rB: 5, xo: 150, record: true));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x1234_5678;
        state.Gpr[4] = 0x8000_1000;
        state.Gpr[5] = 0x20;
        state.HasReservation = true;
        state.ReservationAddress = 0x8000_1020;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0x1234_5678u, memory.Read32(0x8000_1020));
        Assert.Equal(0x2000_0000u, state.Cr & 0xF000_0000);
        Assert.False(state.HasReservation);
        Assert.Equal("stwcx. r3, r4, r5", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 3, rA: 4, rB: 5, xo: 150, record: true)));
    }

    [Fact]
    public void StoreWordConditionalFailsWithoutReservation()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write32(0x8000_1020, 0xCAFE_BABE);
        WriteInstruction(memory, pc, XForm(31, rSOrD: 3, rA: 4, rB: 5, xo: 150, record: true));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x1234_5678;
        state.Gpr[4] = 0x8000_1000;
        state.Gpr[5] = 0x20;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xCAFE_BABEu, memory.Read32(0x8000_1020));
        Assert.Equal(0u, state.Cr & 0xF000_0000);
        Assert.False(state.HasReservation);
    }

    [Fact]
    public void DataCacheBlockZeroClearsAlignedThirtyTwoByteBlock()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        for (uint offset = 0; offset < 64; offset += sizeof(uint))
        {
            memory.Write32(0x8000_1000 + offset, 0xFFFF_FFFF);
        }

        WriteInstruction(memory, pc, XForm(31, rSOrD: 0, rA: 3, rB: 4, xo: 1014));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x8000_1000;
        state.Gpr[4] = 0x33;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xFFFF_FFFFu, memory.Read32(0x8000_1000));
        Assert.Equal(0u, memory.Read32(0x8000_1020));
        Assert.Equal(0u, memory.Read32(0x8000_103C));
        Assert.Equal("dcbz r3, r4", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 0, rA: 3, rB: 4, xo: 1014)));
    }

    [Fact]
    public void LockedCacheBlockZeroClearsLockedCacheRegion()
    {
        GameCubeBus bus = new();
        uint pc = 0x8000_3100;
        bus.Memory.Write32(pc, XForm(4, rSOrD: 0, rA: 3, rB: 4, xo: 1014));
        bus.Write32(0xE000_0020, 0xFFFF_FFFF);
        bus.Write32(0xE000_003C, 0xFFFF_FFFF);

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0xE000_0000;
        state.Gpr[4] = 0x33;

        new PowerPcInterpreter().Step(state, bus);

        Assert.Equal(0u, bus.Read32(0xE000_0020));
        Assert.Equal(0u, bus.Read32(0xE000_003C));
        Assert.Equal("dcbz_l r3, r4", PowerPcDisassembler.Disassemble(XForm(4, rSOrD: 0, rA: 3, rB: 4, xo: 1014)));
    }

    [Fact]
    public void CacheFlushInstructionIsNoopForInterpreter()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 0, rA: 0, rB: 3, xo: 86));

        PowerPcState state = new()
        {
            Pc = pc,
        };
        state.Gpr[3] = 0x8000_1000;

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(pc + 4, state.Pc);
        Assert.Equal("dcbf r0, r3", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 0, rA: 0, rB: 3, xo: 86)));
    }

    [Fact]
    public void DataCacheBlockInvalidateIsNoopForInterpreter()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        WriteInstruction(memory, pc, XForm(31, rSOrD: 0, rA: 0, rB: 3, xo: 470));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(pc + 4, state.Pc);
        Assert.Equal("dcbi r0, r3", PowerPcDisassembler.Disassemble(XForm(31, rSOrD: 0, rA: 0, rB: 3, xo: 470)));
    }

    [Fact]
    public void AlgebraicHalfwordLoadSignExtends()
    {
        GameCubeMemory memory = new();
        uint pc = 0x8000_3100;
        memory.Write16(0x8000_0200, 0xFF80);
        WriteInstruction(memory, pc, Lha(rD: 3, rA: 0, displacement: 0x200));

        PowerPcState state = new()
        {
            Pc = pc,
        };

        new PowerPcInterpreter().Step(state, memory);

        Assert.Equal(0xFFFF_FF80u, state.Gpr[3]);
    }

    [Fact]
    public void DisassemblesKnownInstruction()
    {
        Assert.Equal("addi r3, r0, 42", PowerPcDisassembler.Disassemble(Addi(3, 0, 42)));
        Assert.Equal("mflr r7", PowerPcDisassembler.Disassemble(Mfspr(rD: 7, spr: 8)));
        Assert.Equal("bl 0xFFFFECCC", PowerPcDisassembler.Disassemble(0x4BFF_ECCD));
        Assert.Equal("bcl 12, 0, 0xFFFFFE5C", PowerPcDisassembler.Disassemble(0x4180_FE5D));
    }

    private static void WriteInstruction(GameCubeMemory memory, uint address, uint instruction)
    {
        memory.Write32(address, instruction);
    }

    private static uint Addi(int rD, int rA, short immediate)
    {
        return DForm(14, rD, rA, (ushort)immediate);
    }

    private static uint Oris(int rS, int rA, ushort immediate)
    {
        return DForm(25, rS, rA, immediate);
    }

    private static uint Cmpi(int crField, int rA, short immediate)
    {
        return ((uint)11 << 26) | ((uint)crField << 23) | ((uint)rA << 16) | (ushort)immediate;
    }

    private static uint Bc(int bo, int bi, short offset, bool link = false)
    {
        return ((uint)16 << 26) | ((uint)bo << 21) | ((uint)bi << 16) | ((uint)offset & 0xFFFC) | (link ? 1u : 0u);
    }

    private static uint Bclr(int bo, int bi, bool link = false)
    {
        return ((uint)19 << 26) | ((uint)bo << 21) | ((uint)bi << 16) | (16u << 1) | (link ? 1u : 0u);
    }

    private static uint Stw(int rS, int rA, short displacement)
    {
        return DForm(36, rS, rA, (ushort)displacement);
    }

    private static uint Stwu(int rS, int rA, short displacement)
    {
        return DForm(37, rS, rA, (ushort)displacement);
    }

    private static uint Stb(int rS, int rA, short displacement)
    {
        return DForm(38, rS, rA, (ushort)displacement);
    }

    private static uint Sth(int rS, int rA, short displacement)
    {
        return DForm(44, rS, rA, (ushort)displacement);
    }

    private static uint Lha(int rD, int rA, short displacement)
    {
        return DForm(42, rD, rA, (ushort)displacement);
    }

    private static uint Mfspr(int rD, int spr)
    {
        return ((uint)31 << 26) | ((uint)rD << 21) | SprBits(spr) | (339u << 1);
    }

    private static uint Mftb(int rD, int tbr)
    {
        return ((uint)31 << 26) | ((uint)rD << 21) | SprBits(tbr) | (371u << 1);
    }

    private static uint MtSpr(int rS, int spr)
    {
        return ((uint)31 << 26) | ((uint)rS << 21) | SprBits(spr) | (467u << 1);
    }

    private static uint MfMsr(int rD)
    {
        return ((uint)31 << 26) | ((uint)rD << 21) | (83u << 1);
    }

    private static uint MtMsr(int rS)
    {
        return ((uint)31 << 26) | ((uint)rS << 21) | (146u << 1);
    }

    private static uint Rfi()
    {
        return ((uint)19 << 26) | (50u << 1);
    }

    private static uint Isync()
    {
        return ((uint)19 << 26) | (150u << 1);
    }

    private static uint Sync()
    {
        return ((uint)31 << 26) | (598u << 1);
    }

    private static uint MtSr(int rS, int sr)
    {
        return ((uint)31 << 26) | ((uint)rS << 21) | ((uint)sr << 16) | (210u << 1);
    }

    private static uint MfSr(int rD, int sr)
    {
        return ((uint)31 << 26) | ((uint)rD << 21) | ((uint)sr << 16) | (595u << 1);
    }

    private static uint Lfd(int fD, int rA, short displacement)
    {
        return DForm(50, fD, rA, (ushort)displacement);
    }

    private static uint Stfd(int fS, int rA, short displacement)
    {
        return DForm(54, fS, rA, (ushort)displacement);
    }

    private static uint Mtfsf(int fieldMask, int fB)
    {
        return ((uint)63 << 26) | ((uint)fieldMask << 17) | ((uint)fB << 11) | (711u << 1);
    }

    private static uint Mtfsb1(int bit)
    {
        return ((uint)63 << 26) | ((uint)bit << 21) | (38u << 1);
    }

    private static uint Mtfsb0(int bit)
    {
        return ((uint)63 << 26) | ((uint)bit << 21) | (70u << 1);
    }

    private static uint XForm(int opcode, int rSOrD, int rA, int rB, int xo, bool record = false)
    {
        return ((uint)opcode << 26) |
            ((uint)rSOrD << 21) |
            ((uint)rA << 16) |
            ((uint)rB << 11) |
            ((uint)xo << 1) |
            (record ? 1u : 0u);
    }

    private static uint AForm(int opcode, int fD, int fA, int fB, int fC, int xo, bool record = false)
    {
        return ((uint)opcode << 26) |
            ((uint)fD << 21) |
            ((uint)fA << 16) |
            ((uint)fB << 11) |
            ((uint)fC << 6) |
            ((uint)xo << 1) |
            (record ? 1u : 0u);
    }

    private static uint PsqMemory(int opcode, int fDOrS, int rA, short displacement, int w = 0, int i = 0)
    {
        return ((uint)opcode << 26) |
            ((uint)fDOrS << 21) |
            ((uint)rA << 16) |
            ((uint)w << 15) |
            ((uint)i << 12) |
            ((uint)displacement & 0xFFF);
    }

    private static void WriteSonicRootMatrixMultiplyRoutine(GameCubeMemory memory, uint pc)
    {
        uint[] instructions =
        [
            0x9421_FFC0, 0xE003_0000, 0xD9C1_0008, 0xE0C4_0000,
            0xE0E4_0008, 0xD9E1_0010, 0x3CC0_803B, 0x38C6_D5C8,
            0xDBE1_0028, 0xE104_0010, 0x1186_0018, 0xE043_0010,
            0x11A7_0018, 0xE3E6_0000, 0x11C6_0098, 0xE124_0018,
            0x11E7_0098, 0xE023_0008, 0x1188_601E, 0xE063_0018,
            0x11C8_709E, 0xE144_0020, 0x11A9_681E, 0xE164_0028,
            0x11E9_789E, 0xE083_0020, 0xE0A3_0028, 0x118A_605C,
            0x11AB_685C, 0x11CA_70DC, 0x11EB_78DC, 0xF185_0000,
            0x1046_0118, 0x11BF_685E, 0x1007_0118, 0xF1C5_0010,
            0x11FF_78DE, 0xF1A5_0008, 0x1048_111E, 0x1009_011E,
            0x104A_115C, 0xC9C1_0008, 0xF1E5_0018, 0x100B_015C,
            0xF045_0020, 0x101F_015E, 0xC9E1_0010, 0xF005_0028,
            0xCBE1_0028, 0x3821_0040, 0x4E80_0020,
        ];

        for (int index = 0; index < instructions.Length; index++)
        {
            WriteInstruction(memory, pc + (uint)(index * 4), instructions[index]);
        }
    }

    private static void WriteAffineMatrix(GameCubeMemory memory, uint address, float[] values)
    {
        Assert.Equal(12, values.Length);
        for (int index = 0; index < values.Length; index++)
        {
            WriteFloat(memory, address + (uint)(index * 4), values[index]);
        }
    }

    private static float[] MultiplyAffine3x4(float[] left, float[] right)
    {
        float[] result = new float[12];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                float value =
                    left[row * 4 + 0] * right[0 * 4 + column] +
                    left[row * 4 + 1] * right[1 * 4 + column] +
                    left[row * 4 + 2] * right[2 * 4 + column];
                if (column == 3)
                {
                    value += left[row * 4 + 3];
                }

                result[row * 4 + column] = value;
            }
        }

        return result;
    }

    private static void WriteFloat(GameCubeMemory memory, uint address, float value)
    {
        memory.Write32(address, BitConverter.SingleToUInt32Bits(value));
    }

    private static float ReadFloat(GameCubeMemory memory, uint address)
    {
        return BitConverter.UInt32BitsToSingle(memory.Read32(address));
    }

    private static uint XlForm(int bt, int ba, int bb, int xo, bool link = false)
    {
        return (19u << 26) |
            ((uint)bt << 21) |
            ((uint)ba << 16) |
            ((uint)bb << 11) |
            ((uint)xo << 1) |
            (link ? 1u : 0u);
    }

    private static uint Mcrf(int targetField, int sourceField)
    {
        return (19u << 26) | ((uint)targetField << 23) | ((uint)sourceField << 18);
    }

    private static uint SprBits(int spr)
    {
        return ((uint)spr & 0x1F) << 16 | ((uint)spr & 0x3E0) << 6;
    }

    private static uint B(int offset, bool link = false)
    {
        return (18u << 26) | ((uint)offset & 0x03FF_FFFC) | (link ? 1u : 0u);
    }

    private static uint DForm(int opcode, int rDOrS, int rA, ushort immediate)
    {
        return ((uint)opcode << 26) | ((uint)rDOrS << 21) | ((uint)rA << 16) | immediate;
    }

    private static uint MForm(int opcode, int rS, int rA, int shift, int maskBegin, int maskEnd, bool record = false)
    {
        return ((uint)opcode << 26) |
            ((uint)rS << 21) |
            ((uint)rA << 16) |
            ((uint)shift << 11) |
            ((uint)maskBegin << 6) |
            ((uint)maskEnd << 1) |
            (record ? 1u : 0u);
    }
}
