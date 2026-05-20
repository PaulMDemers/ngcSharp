using NgcSharp.Core;

namespace NgcSharp.Cpu;

public sealed class PowerPcInterpreter
{
    private const uint MsrRecoverableInterrupt = 0x0000_0002;
    private const uint MsrExternalInterruptEnable = 0x0000_8000;
    private const int DecrementerSpr = 22;
    private const uint ExternalInterruptVector = 0x8000_0500;
    private const uint DecrementerInterruptVector = 0x8000_0900;
    private const uint SystemCallInterruptVector = 0x8000_0C00;
    private bool _decrementerInterruptPending;

    public uint Step(PowerPcState state, IMemoryBus memory)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(memory);

        if (state.Halted)
        {
            return 0;
        }

        if (memory is GameCubeBus pendingBus && TryEnterExternalInterrupt(state, pendingBus))
        {
            TickCounters(state);
            AdvanceBus(memory);
            return 0;
        }

        if (TryEnterDecrementerInterrupt(state))
        {
            TickCounters(state);
            AdvanceBus(memory);
            return 0;
        }

        uint oldPc = state.Pc;
        uint instruction = memory.Read32(oldPc);
        state.Pc = oldPc + sizeof(uint);
        TickCounters(state);
        Execute(state, memory, oldPc, instruction);
        AdvanceBus(memory);

        if (IsTerminalSelfBranch(instruction, oldPc, state.Pc) && !CanWaitForExternalInterrupt(state, memory))
        {
            state.Halted = true;
        }

        return instruction;
    }

    public int Run(PowerPcState state, IMemoryBus memory, int maxInstructions)
    {
        if (maxInstructions < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInstructions));
        }

        int executed = 0;
        while (!state.Halted && executed < maxInstructions)
        {
            Step(state, memory);
            executed++;
        }

        return executed;
    }

    private static void Execute(PowerPcState state, IMemoryBus memory, uint oldPc, uint instruction)
    {
        int opcode = (int)(instruction >> 26);

        switch (opcode)
        {
            case 7:
                MultiplyLowImmediate(state, instruction);
                return;
            case 8:
                SubtractFromImmediateCarrying(state, instruction);
                return;
            case 10:
                CompareImmediate(state, instruction, unsigned: true);
                return;
            case 12:
                AddImmediateCarrying(state, instruction, record: false);
                return;
            case 13:
                AddImmediateCarrying(state, instruction, record: true);
                return;
            case 11:
                CompareImmediate(state, instruction, unsigned: false);
                return;
            case 14:
                AddImmediate(state, instruction, shifted: false);
                return;
            case 15:
                AddImmediate(state, instruction, shifted: true);
                return;
            case 16:
                BranchConditionalRelative(state, oldPc, instruction);
                return;
            case 17:
                SystemCall(state);
                return;
            case 18:
                Branch(state, oldPc, instruction);
                return;
            case 20:
                RotateLeftWordImmediateThenMaskInsert(state, instruction);
                return;
            case 21:
                RotateLeftWordImmediateThenMask(state, instruction);
                return;
            case 24:
                OrImmediate(state, instruction, shifted: false);
                return;
            case 25:
                OrImmediate(state, instruction, shifted: true);
                return;
            case 26:
                XorImmediate(state, instruction, shifted: false);
                return;
            case 27:
                XorImmediate(state, instruction, shifted: true);
                return;
            case 28:
                AndImmediate(state, instruction, shifted: false);
                return;
            case 29:
                AndImmediate(state, instruction, shifted: true);
                return;
            case 4:
                ExecuteOpcode4(state, memory, instruction);
                return;
            case 32:
                LoadWordAndZero(state, memory, instruction);
                return;
            case 33:
                LoadWordAndZeroUpdate(state, memory, instruction);
                return;
            case 34:
                LoadByteAndZero(state, memory, instruction);
                return;
            case 35:
                LoadByteAndZeroUpdate(state, memory, instruction);
                return;
            case 36:
                StoreWord(state, memory, instruction);
                return;
            case 37:
                StoreWordUpdate(state, memory, instruction);
                return;
            case 38:
                StoreByte(state, memory, instruction);
                return;
            case 39:
                StoreByteUpdate(state, memory, instruction);
                return;
            case 40:
                LoadHalfWordAndZero(state, memory, instruction);
                return;
            case 41:
                LoadHalfWordAndZeroUpdate(state, memory, instruction);
                return;
            case 42:
                LoadHalfWordAlgebraic(state, memory, instruction);
                return;
            case 43:
                LoadHalfWordAlgebraicUpdate(state, memory, instruction);
                return;
            case 44:
                StoreHalfWord(state, memory, instruction);
                return;
            case 45:
                StoreHalfWordUpdate(state, memory, instruction);
                return;
            case 46:
                LoadMultipleWord(state, memory, instruction);
                return;
            case 47:
                StoreMultipleWord(state, memory, instruction);
                return;
            case 48:
                LoadFloatingSingle(state, memory, instruction);
                return;
            case 49:
                LoadFloatingSingleUpdate(state, memory, instruction);
                return;
            case 50:
                LoadFloatingDouble(state, memory, instruction);
                return;
            case 51:
                LoadFloatingDoubleUpdate(state, memory, instruction);
                return;
            case 52:
                StoreFloatingSingle(state, memory, instruction);
                return;
            case 53:
                StoreFloatingSingleUpdate(state, memory, instruction);
                return;
            case 54:
                StoreFloatingDouble(state, memory, instruction);
                return;
            case 55:
                StoreFloatingDoubleUpdate(state, memory, instruction);
                return;
            case 56:
                PairedSingleQuantizedLoad(state, memory, instruction, update: false);
                return;
            case 57:
                PairedSingleQuantizedLoad(state, memory, instruction, update: true);
                return;
            case 60:
                PairedSingleQuantizedStore(state, memory, instruction, update: false);
                return;
            case 61:
                PairedSingleQuantizedStore(state, memory, instruction, update: true);
                return;
            case 59:
                ExecuteFloatingOpcode59(state, instruction);
                return;
            case 31:
                ExecuteOpcode31(state, memory, instruction);
                return;
            case 19:
                ExecuteOpcode19(state, instruction);
                return;
            case 63:
                ExecuteFloatingOpcode63(state, instruction);
                return;
            default:
                throw new UnsupportedInstructionException(oldPc, instruction);
        }
    }

    private static void AddImmediate(PowerPcState state, uint instruction, bool shifted)
    {
        int rD = Rd(instruction);
        int rA = Ra(instruction);
        int imm = SignExtend16(instruction);
        uint value = shifted ? unchecked((uint)(imm << 16)) : unchecked((uint)imm);
        state.Gpr[rD] = unchecked(BaseForImmediate(state, rA) + value);
    }

    private static void AddImmediateCarrying(PowerPcState state, uint instruction, bool record)
    {
        int rD = Rd(instruction);
        int rA = Ra(instruction);
        uint left = state.Gpr[rA];
        uint right = unchecked((uint)SignExtend16(instruction));
        uint result = unchecked(left + right);
        state.Gpr[rD] = result;
        SetCarry(state, result < left);

        if (record)
        {
            SetCr0(state, result);
        }
    }

    private static void MultiplyLowImmediate(PowerPcState state, uint instruction)
    {
        int rD = Rd(instruction);
        int rA = Ra(instruction);
        state.Gpr[rD] = unchecked((uint)((int)state.Gpr[rA] * SignExtend16(instruction)));
    }

    private static void SubtractFromImmediateCarrying(PowerPcState state, uint instruction)
    {
        int rD = Rd(instruction);
        uint subtrahend = state.Gpr[Ra(instruction)];
        uint minuend = unchecked((uint)SignExtend16(instruction));
        state.Gpr[rD] = SubtractFromCarrying(state, subtrahend, minuend);
    }

    private static void OrImmediate(PowerPcState state, uint instruction, bool shifted)
    {
        int rS = Rs(instruction);
        int rA = Ra(instruction);
        uint imm = instruction & 0xFFFF;
        if (shifted)
        {
            imm <<= 16;
        }

        state.Gpr[rA] = state.Gpr[rS] | imm;
    }

    private static void XorImmediate(PowerPcState state, uint instruction, bool shifted)
    {
        int rS = Rs(instruction);
        int rA = Ra(instruction);
        uint imm = instruction & 0xFFFF;
        if (shifted)
        {
            imm <<= 16;
        }

        state.Gpr[rA] = state.Gpr[rS] ^ imm;
    }

    private static void AndImmediate(PowerPcState state, uint instruction, bool shifted)
    {
        int rS = Rs(instruction);
        int rA = Ra(instruction);
        uint imm = instruction & 0xFFFF;
        if (shifted)
        {
            imm <<= 16;
        }

        state.Gpr[rA] = state.Gpr[rS] & imm;
        SetCr0(state, state.Gpr[rA]);
    }

    private static void CompareImmediate(PowerPcState state, uint instruction, bool unsigned)
    {
        int crField = (int)((instruction >> 23) & 0x7);
        int rA = Ra(instruction);

        if (unsigned)
        {
            SetCrField(state, crField, state.Gpr[rA], instruction & 0xFFFF);
        }
        else
        {
            SetCrField(state, crField, unchecked((int)state.Gpr[rA]), SignExtend16(instruction));
        }
    }

    private static void Branch(PowerPcState state, uint oldPc, uint instruction)
    {
        uint offset = instruction & 0x03FF_FFFC;
        if ((offset & 0x0200_0000) != 0)
        {
            offset |= 0xFC00_0000;
        }

        bool absolute = (instruction & 0x2) != 0;
        bool link = (instruction & 0x1) != 0;

        if (link)
        {
            state.Lr = oldPc + sizeof(uint);
        }

        state.Pc = absolute ? offset : unchecked(oldPc + offset);
    }

    private static bool TryEnterExternalInterrupt(PowerPcState state, GameCubeBus bus)
    {
        if (!CanWaitForExternalInterrupt(state, bus) || !bus.HasPendingExternalInterrupt)
        {
            return false;
        }

        EnterInterrupt(state, ExternalInterruptVector);
        return true;
    }

    private bool TryEnterDecrementerInterrupt(PowerPcState state)
    {
        if ((state.Msr & MsrExternalInterruptEnable) == 0 || !_decrementerInterruptPending)
        {
            return false;
        }

        _decrementerInterruptPending = false;
        EnterInterrupt(state, DecrementerInterruptVector);
        return true;
    }

    private static void EnterInterrupt(PowerPcState state, uint vector)
    {
        state.Spr[26] = state.Pc;
        state.Spr[27] = state.Msr;
        state.Msr &= ~(MsrExternalInterruptEnable | MsrRecoverableInterrupt);
        state.Pc = vector;
        state.Halted = false;
    }

    private static void SystemCall(PowerPcState state)
    {
        EnterInterrupt(state, SystemCallInterruptVector);
    }

    private static bool CanWaitForExternalInterrupt(PowerPcState state, IMemoryBus memory)
    {
        return memory is GameCubeBus && (state.Msr & MsrExternalInterruptEnable) != 0;
    }

    private static bool IsTerminalSelfBranch(uint instruction, uint oldPc, uint newPc)
    {
        return (instruction >> 26) == 18 && (instruction & 0x3) == 0 && newPc == oldPc;
    }

    private void TickCounters(PowerPcState state)
    {
        state.TimeBase++;
        uint oldDecrementer = state.Spr[DecrementerSpr];
        state.Spr[DecrementerSpr]--;

        if ((oldDecrementer & 0x8000_0000) == 0 && (state.Spr[DecrementerSpr] & 0x8000_0000) != 0)
        {
            _decrementerInterruptPending = true;
        }
    }

    private static void AdvanceBus(IMemoryBus memory)
    {
        if (memory is GameCubeBus bus)
        {
            bus.Advance(1);
        }
    }

    private static void BranchConditionalRelative(PowerPcState state, uint oldPc, uint instruction)
    {
        bool absolute = (instruction & 0x2) != 0;
        int offset = (short)(instruction & 0xFFFC);
        uint target = absolute ? unchecked((uint)offset) : unchecked(oldPc + (uint)offset);
        BranchConditionalToAddress(state, oldPc, instruction, target);
    }

    private static void BranchConditionalToAddress(PowerPcState state, uint oldPc, uint instruction, uint target)
    {
        bool shouldBranch = ShouldBranch(state, (int)((instruction >> 21) & 0x1F), (int)((instruction >> 16) & 0x1F));
        if ((instruction & 1) != 0)
        {
            state.Lr = oldPc + sizeof(uint);
        }

        if (shouldBranch)
        {
            state.Pc = target;
        }
    }

    private static void RotateLeftWordImmediateThenMask(PowerPcState state, uint instruction)
    {
        int rS = Rs(instruction);
        int rA = Ra(instruction);
        int shift = (int)((instruction >> 11) & 0x1F);
        int maskBegin = (int)((instruction >> 6) & 0x1F);
        int maskEnd = (int)((instruction >> 1) & 0x1F);
        uint value = RotateLeft(state.Gpr[rS], shift) & Mask(maskBegin, maskEnd);
        state.Gpr[rA] = value;

        if ((instruction & 1) != 0)
        {
            SetCr0(state, value);
        }
    }

    private static void RotateLeftWordImmediateThenMaskInsert(PowerPcState state, uint instruction)
    {
        int rS = Rs(instruction);
        int rA = Ra(instruction);
        int shift = (int)((instruction >> 11) & 0x1F);
        int maskBegin = (int)((instruction >> 6) & 0x1F);
        int maskEnd = (int)((instruction >> 1) & 0x1F);
        uint mask = Mask(maskBegin, maskEnd);
        uint value = (state.Gpr[rA] & ~mask) | (RotateLeft(state.Gpr[rS], shift) & mask);
        state.Gpr[rA] = value;

        if ((instruction & 1) != 0)
        {
            SetCr0(state, value);
        }
    }

    private static void LoadWordAndZero(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        state.Gpr[Rd(instruction)] = memory.Read32(EffectiveAddress(state, instruction));
    }

    private static void LoadWordAndZeroUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        state.Gpr[Rd(instruction)] = memory.Read32(address);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void LoadMultipleWord(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        for (int register = Rd(instruction); register < state.Gpr.Length; register++, address += sizeof(uint))
        {
            state.Gpr[register] = memory.Read32(address);
        }
    }

    private static void LoadHalfWordAndZero(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        state.Gpr[Rd(instruction)] = memory.Read16(EffectiveAddress(state, instruction));
    }

    private static void LoadHalfWordAndZeroUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        state.Gpr[Rd(instruction)] = memory.Read16(address);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void LoadHalfWordAlgebraic(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        state.Gpr[Rd(instruction)] = unchecked((uint)(short)memory.Read16(EffectiveAddress(state, instruction)));
    }

    private static void LoadHalfWordAlgebraicUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        state.Gpr[Rd(instruction)] = unchecked((uint)(short)memory.Read16(address));
        state.Gpr[Ra(instruction)] = address;
    }

    private static void LoadByteAndZero(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        state.Gpr[Rd(instruction)] = memory.Read8(EffectiveAddress(state, instruction));
    }

    private static void LoadByteAndZeroUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        state.Gpr[Rd(instruction)] = memory.Read8(address);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void LoadWordAndZeroIndexedUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = IndexedAddress(state, instruction);
        state.Gpr[Rd(instruction)] = memory.Read32(address);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void LoadByteAndZeroIndexedUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = IndexedAddress(state, instruction);
        state.Gpr[Rd(instruction)] = memory.Read8(address);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void LoadHalfWordAndZeroIndexedUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = IndexedAddress(state, instruction);
        state.Gpr[Rd(instruction)] = memory.Read16(address);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void LoadHalfWordAlgebraicIndexedUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = IndexedAddress(state, instruction);
        state.Gpr[Rd(instruction)] = unchecked((uint)(short)memory.Read16(address));
        state.Gpr[Ra(instruction)] = address;
    }

    private static void StoreWord(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        memory.Write32(EffectiveAddress(state, instruction), state.Gpr[Rs(instruction)]);
    }

    private static void StoreWordUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        memory.Write32(address, state.Gpr[Rs(instruction)]);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void StoreWordIndexedUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = IndexedAddress(state, instruction);
        memory.Write32(address, state.Gpr[Rs(instruction)]);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void StoreMultipleWord(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        for (int register = Rs(instruction); register < state.Gpr.Length; register++, address += sizeof(uint))
        {
            memory.Write32(address, state.Gpr[register]);
        }
    }

    private static void StoreHalfWord(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        memory.Write16(EffectiveAddress(state, instruction), (ushort)state.Gpr[Rs(instruction)]);
    }

    private static void StoreHalfWordUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        memory.Write16(address, (ushort)state.Gpr[Rs(instruction)]);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void StoreHalfWordIndexedUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = IndexedAddress(state, instruction);
        memory.Write16(address, (ushort)state.Gpr[Rs(instruction)]);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void StoreByte(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        memory.Write8(EffectiveAddress(state, instruction), (byte)state.Gpr[Rs(instruction)]);
    }

    private static void StoreByteUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        memory.Write8(address, (byte)state.Gpr[Rs(instruction)]);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void StoreByteIndexedUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = IndexedAddress(state, instruction);
        memory.Write8(address, (byte)state.Gpr[Rs(instruction)]);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void LoadFloatingDouble(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        ulong value = ((ulong)memory.Read32(address) << 32) | memory.Read32(address + sizeof(uint));
        SetFloatingScalar(state, Rd(instruction), BitConverter.UInt64BitsToDouble(value));
    }

    private static void LoadFloatingDoubleUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        ulong value = ((ulong)memory.Read32(address) << 32) | memory.Read32(address + sizeof(uint));
        SetFloatingScalar(state, Rd(instruction), BitConverter.UInt64BitsToDouble(value));
        state.Gpr[Ra(instruction)] = address;
    }

    private static void LoadFloatingSingle(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint value = memory.Read32(EffectiveAddress(state, instruction));
        SetFloatingScalar(state, Rd(instruction), BitConverter.Int32BitsToSingle(unchecked((int)value)));
    }

    private static void LoadFloatingSingleUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        uint value = memory.Read32(address);
        SetFloatingScalar(state, Rd(instruction), BitConverter.Int32BitsToSingle(unchecked((int)value)));
        state.Gpr[Ra(instruction)] = address;
    }

    private static void LoadFloatingDoubleIndexed(PowerPcState state, IMemoryBus memory, uint instruction, bool update)
    {
        uint address = IndexedAddress(state, instruction);
        ulong value = ((ulong)memory.Read32(address) << 32) | memory.Read32(address + sizeof(uint));
        SetFloatingScalar(state, Rd(instruction), BitConverter.UInt64BitsToDouble(value));

        if (update)
        {
            state.Gpr[Ra(instruction)] = address;
        }
    }

    private static void LoadFloatingSingleIndexed(PowerPcState state, IMemoryBus memory, uint instruction, bool update)
    {
        uint address = IndexedAddress(state, instruction);
        uint value = memory.Read32(address);
        SetFloatingScalar(state, Rd(instruction), BitConverter.Int32BitsToSingle(unchecked((int)value)));

        if (update)
        {
            state.Gpr[Ra(instruction)] = address;
        }
    }

    private static void StoreFloatingDouble(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        ulong value = (ulong)BitConverter.DoubleToInt64Bits(state.Fpr[Rs(instruction)]);
        memory.Write32(address, (uint)(value >> 32));
        memory.Write32(address + sizeof(uint), (uint)value);
    }

    private static void StoreFloatingDoubleUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        ulong value = (ulong)BitConverter.DoubleToInt64Bits(state.Fpr[Rs(instruction)]);
        memory.Write32(address, (uint)(value >> 32));
        memory.Write32(address + sizeof(uint), (uint)value);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void StoreFloatingSingle(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint value = unchecked((uint)BitConverter.SingleToInt32Bits((float)state.Fpr[Rs(instruction)]));
        memory.Write32(EffectiveAddress(state, instruction), value);
    }

    private static void StoreFloatingSingleUpdate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = EffectiveAddress(state, instruction);
        uint value = unchecked((uint)BitConverter.SingleToInt32Bits((float)state.Fpr[Rs(instruction)]));
        memory.Write32(address, value);
        state.Gpr[Ra(instruction)] = address;
    }

    private static void StoreFloatingDoubleIndexed(PowerPcState state, IMemoryBus memory, uint instruction, bool update)
    {
        uint address = IndexedAddress(state, instruction);
        ulong value = (ulong)BitConverter.DoubleToInt64Bits(state.Fpr[Rs(instruction)]);
        memory.Write32(address, (uint)(value >> 32));
        memory.Write32(address + sizeof(uint), (uint)value);

        if (update)
        {
            state.Gpr[Ra(instruction)] = address;
        }
    }

    private static void StoreFloatingIntegerWordIndexed(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = IndexedAddress(state, instruction);
        uint value = (uint)BitConverter.DoubleToInt64Bits(state.Fpr[Rs(instruction)]);
        memory.Write32(address, value);
    }

    private static void LoadStringWordIndexed(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = IndexedAddress(state, instruction);
        int rD = Rd(instruction);
        int byteCount = (int)(state.Xer & 0x7F);
        if (byteCount == 0)
        {
            byteCount = 32;
        }

        for (int index = 0; index < byteCount; index++)
        {
            int register = (rD + (index / sizeof(uint))) & 0x1F;
            int byteInRegister = index & 0x3;
            if (byteInRegister == 0)
            {
                state.Gpr[register] = 0;
            }

            uint loaded = memory.Read8(address + (uint)index);
            int shift = 24 - byteInRegister * 8;
            state.Gpr[register] |= loaded << shift;
        }
    }

    private static void StoreStringWordIndexed(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = IndexedAddress(state, instruction);
        int rS = Rs(instruction);
        int byteCount = (int)(state.Xer & 0x7F);
        if (byteCount == 0)
        {
            byteCount = 32;
        }

        StoreStringWord(state, memory, address, rS, byteCount);
    }

    private static void StoreStringWordImmediate(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        int byteCount = (int)((instruction >> 11) & 0x1F);
        if (byteCount == 0)
        {
            byteCount = 32;
        }

        StoreStringWord(state, memory, Ra(instruction) == 0 ? 0 : state.Gpr[Ra(instruction)], Rs(instruction), byteCount);
    }

    private static void StoreStringWord(PowerPcState state, IMemoryBus memory, uint address, int sourceRegister, int byteCount)
    {
        for (int index = 0; index < byteCount; index++)
        {
            int register = (sourceRegister + (index / sizeof(uint))) & 0x1F;
            int byteInRegister = index & 0x3;
            int shift = 24 - byteInRegister * 8;
            byte value = (byte)(state.Gpr[register] >> shift);
            memory.Write8(address + (uint)index, value);
        }
    }

    private static void StoreFloatingSingleIndexed(PowerPcState state, IMemoryBus memory, uint instruction, bool update)
    {
        uint address = IndexedAddress(state, instruction);
        uint value = unchecked((uint)BitConverter.SingleToInt32Bits((float)state.Fpr[Rs(instruction)]));
        memory.Write32(address, value);

        if (update)
        {
            state.Gpr[Ra(instruction)] = address;
        }
    }

    private static void PairedSingleQuantizedLoad(PowerPcState state, IMemoryBus memory, uint instruction, bool update)
    {
        uint address = PairedSingleQuantizedAddress(state, instruction);
        int fD = Rd(instruction);
        int gqr = PairedSingleQuantizedRegister(instruction);
        int loadType = GqrLoadType(state, gqr);
        int loadScale = GqrLoadScale(state, gqr);
        uint instructionAddress = state.Pc - sizeof(uint);
        state.Fpr[fD] = ReadPairedSingleQuantizedOperand(memory, address, loadType, loadScale, instructionAddress, instruction, out int operandSize);
        state.FprPair1[fD] = PairedSingleQuantizedSingle(instruction)
            ? 1.0d
            : ReadPairedSingleQuantizedOperand(memory, address + (uint)operandSize, loadType, loadScale, instructionAddress, instruction, out _);

        if (update)
        {
            state.Gpr[Ra(instruction)] = address;
        }
    }

    private static void PairedSingleQuantizedStore(PowerPcState state, IMemoryBus memory, uint instruction, bool update)
    {
        uint address = PairedSingleQuantizedAddress(state, instruction);
        int fS = Rs(instruction);
        int gqr = PairedSingleQuantizedRegister(instruction);
        int storeType = GqrStoreType(state, gqr);
        int storeScale = GqrStoreScale(state, gqr);
        uint instructionAddress = state.Pc - sizeof(uint);
        int operandSize = WritePairedSingleQuantizedOperand(memory, address, state.Fpr[fS], storeType, storeScale, instructionAddress, instruction);
        if (!PairedSingleQuantizedSingle(instruction))
        {
            WritePairedSingleQuantizedOperand(memory, address + (uint)operandSize, state.FprPair1[fS], storeType, storeScale, instructionAddress, instruction);
        }

        if (update)
        {
            state.Gpr[Ra(instruction)] = address;
        }
    }

    private static void ExecuteOpcode31(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        int extendedOpcode = (int)((instruction >> 1) & 0x3FF);
        int rD = Rd(instruction);
        int rS = Rs(instruction);
        int rA = Ra(instruction);
        int rB = Rb(instruction);
        bool record = (instruction & 1) != 0;
        uint value;

        switch (extendedOpcode)
        {
            case 0:
                CompareRegister(state, instruction, unsigned: false);
                return;
            case 4:
                return;
            case 8:
                value = SubtractFromCarrying(state, state.Gpr[rA], state.Gpr[rB]);
                state.Gpr[rD] = value;
                break;
            case 10:
                value = AddCarrying(state, state.Gpr[rA], state.Gpr[rB]);
                state.Gpr[rD] = value;
                break;
            case 11:
                value = (uint)(((ulong)state.Gpr[rA] * state.Gpr[rB]) >> 32);
                state.Gpr[rD] = value;
                break;
            case 28:
                value = state.Gpr[rS] & state.Gpr[rB];
                state.Gpr[rA] = value;
                break;
            case 32:
                CompareRegister(state, instruction, unsigned: true);
                return;
            case 19:
                state.Gpr[rD] = state.Cr;
                return;
            case 20:
                state.ReservationAddress = IndexedAddress(state, instruction);
                state.HasReservation = true;
                state.Gpr[rD] = memory.Read32(state.ReservationAddress);
                return;
            case 23:
                state.Gpr[rD] = memory.Read32(IndexedAddress(state, instruction));
                return;
            case 55:
                LoadWordAndZeroIndexedUpdate(state, memory, instruction);
                return;
            case 24:
                value = ShiftLeftWord(state.Gpr[rS], state.Gpr[rB]);
                state.Gpr[rA] = value;
                break;
            case 26:
                value = (uint)uint.LeadingZeroCount(state.Gpr[rS]);
                state.Gpr[rA] = value;
                break;
            case 83:
                state.Gpr[rD] = state.Msr;
                return;
            case 87:
                state.Gpr[rD] = memory.Read8(IndexedAddress(state, instruction));
                return;
            case 119:
                LoadByteAndZeroIndexedUpdate(state, memory, instruction);
                return;
            case 104:
                value = unchecked(0u - state.Gpr[rA]);
                state.Gpr[rD] = value;
                break;
            case 40:
                value = unchecked(state.Gpr[rB] - state.Gpr[rA]);
                state.Gpr[rD] = value;
                break;
            case 54:
            case 86:
            case 246:
            case 278:
            case 470:
            case 982:
                return;
            case 60:
                value = state.Gpr[rS] & ~state.Gpr[rB];
                state.Gpr[rA] = value;
                break;
            case 75:
                value = (uint)(((long)(int)state.Gpr[rA] * (int)state.Gpr[rB]) >> 32);
                state.Gpr[rD] = value;
                break;
            case 150:
                uint conditionalAddress = IndexedAddress(state, instruction);
                bool storeSucceeded = state.HasReservation && state.ReservationAddress == conditionalAddress;
                state.HasReservation = false;
                if (storeSucceeded)
                {
                    memory.Write32(conditionalAddress, state.Gpr[rS]);
                }

                SetStoreConditionalCr0(state, storeSucceeded);
                return;
            case 151:
                memory.Write32(IndexedAddress(state, instruction), state.Gpr[rS]);
                return;
            case 183:
                StoreWordIndexedUpdate(state, memory, instruction);
                return;
            case 202:
                value = AddExtended(state, state.Gpr[rA], 0);
                state.Gpr[rD] = value;
                break;
            case 234:
                value = AddExtended(state, state.Gpr[rA], uint.MaxValue);
                state.Gpr[rD] = value;
                break;
            case 200:
                value = SubtractFromExtended(state, state.Gpr[rA], 0);
                state.Gpr[rD] = value;
                break;
            case 215:
                memory.Write8(IndexedAddress(state, instruction), (byte)state.Gpr[rS]);
                return;
            case 247:
                StoreByteIndexedUpdate(state, memory, instruction);
                return;
            case 235:
                value = unchecked((uint)((int)state.Gpr[rA] * (int)state.Gpr[rB]));
                state.Gpr[rD] = value;
                break;
            case 266:
                value = unchecked(state.Gpr[rA] + state.Gpr[rB]);
                state.Gpr[rD] = value;
                break;
            case 279:
                state.Gpr[rD] = memory.Read16(IndexedAddress(state, instruction));
                return;
            case 284:
                value = unchecked(~(state.Gpr[rS] ^ state.Gpr[rB]));
                state.Gpr[rA] = value;
                break;
            case 311:
                LoadHalfWordAndZeroIndexedUpdate(state, memory, instruction);
                return;
            case 310:
                state.Gpr[rD] = 0;
                return;
            case 316:
                value = state.Gpr[rS] ^ state.Gpr[rB];
                state.Gpr[rA] = value;
                break;
            case 339:
                state.Gpr[rD] = ReadSpecialPurposeRegister(state, Spr(instruction));
                return;
            case 371:
                state.Gpr[rD] = ReadTimeBase(state, Spr(instruction));
                return;
            case 343:
                state.Gpr[rD] = unchecked((uint)(short)memory.Read16(IndexedAddress(state, instruction)));
                return;
            case 375:
                LoadHalfWordAlgebraicIndexedUpdate(state, memory, instruction);
                return;
            case 407:
                memory.Write16(IndexedAddress(state, instruction), (ushort)state.Gpr[rS]);
                return;
            case 439:
                StoreHalfWordIndexedUpdate(state, memory, instruction);
                return;
            case 1014:
                DataCacheBlockSetToZero(state, memory, instruction);
                return;
            case 444:
                value = state.Gpr[rS] | state.Gpr[rB];
                state.Gpr[rA] = value;
                break;
            case 459:
                value = state.Gpr[rB] == 0 ? 0 : state.Gpr[rA] / state.Gpr[rB];
                state.Gpr[rD] = value;
                break;
            case 467:
                WriteSpecialPurposeRegister(state, Spr(instruction), state.Gpr[rS]);
                return;
            case 476:
                value = unchecked(~(state.Gpr[rS] & state.Gpr[rB]));
                state.Gpr[rA] = value;
                break;
            case 491:
                value = state.Gpr[rB] == 0 ? 0 : unchecked((uint)((int)state.Gpr[rA] / (int)state.Gpr[rB]));
                state.Gpr[rD] = value;
                break;
            case 533:
                LoadStringWordIndexed(state, memory, instruction);
                return;
            case 535:
                LoadFloatingSingleIndexed(state, memory, instruction, update: false);
                return;
            case 536:
                value = ShiftRightWord(state.Gpr[rS], state.Gpr[rB]);
                state.Gpr[rA] = value;
                break;
            case 567:
                LoadFloatingSingleIndexed(state, memory, instruction, update: true);
                return;
            case 599:
                LoadFloatingDoubleIndexed(state, memory, instruction, update: false);
                return;
            case 631:
                LoadFloatingDoubleIndexed(state, memory, instruction, update: true);
                return;
            case 661:
                StoreStringWordIndexed(state, memory, instruction);
                return;
            case 663:
                StoreFloatingSingleIndexed(state, memory, instruction, update: false);
                return;
            case 725:
                StoreStringWordImmediate(state, memory, instruction);
                return;
            case 695:
                StoreFloatingSingleIndexed(state, memory, instruction, update: true);
                return;
            case 727:
                StoreFloatingDoubleIndexed(state, memory, instruction, update: false);
                return;
            case 759:
                StoreFloatingDoubleIndexed(state, memory, instruction, update: true);
                return;
            case 983:
                StoreFloatingIntegerWordIndexed(state, memory, instruction);
                return;
            case 792:
                value = ShiftRightAlgebraicWord(state, state.Gpr[rS], state.Gpr[rB]);
                state.Gpr[rA] = value;
                break;
            case 824:
                value = ShiftRightAlgebraicWord(state, state.Gpr[rS], (uint)rB);
                state.Gpr[rA] = value;
                break;
            case 922:
                value = unchecked((uint)(short)state.Gpr[rS]);
                state.Gpr[rA] = value;
                break;
            case 954:
                value = unchecked((uint)(sbyte)state.Gpr[rS]);
                state.Gpr[rA] = value;
                break;
            case 598:
                return;
            case 438:
                return;
            case 144:
                MoveToConditionRegisterFields(state, instruction);
                return;
            case 146:
                state.Msr = state.Gpr[rS];
                return;
            case 210:
                state.SegmentRegisters[Sr(instruction)] = state.Gpr[rS];
                return;
            case 595:
                state.Gpr[rD] = state.SegmentRegisters[Sr(instruction)];
                return;
            case 124:
                value = unchecked(~(state.Gpr[rS] | state.Gpr[rB]));
                state.Gpr[rA] = value;
                break;
            case 136:
                value = SubtractFromExtended(state, state.Gpr[rA], state.Gpr[rB]);
                state.Gpr[rD] = value;
                break;
            case 138:
                value = AddExtended(state, state.Gpr[rA], state.Gpr[rB]);
                state.Gpr[rD] = value;
                break;
            default:
                throw new UnsupportedInstructionException(state.Pc - sizeof(uint), instruction);
        }

        if (record)
        {
            SetCr0(state, value);
        }
    }

    private static void ExecuteOpcode4(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        int extendedOpcode = (int)((instruction >> 1) & 0x3FF);
        int arithmeticOpcode = extendedOpcode & 0x1F;

        switch (extendedOpcode)
        {
            case 32:
                ComparePairedSingleRegister(state, instruction);
                return;
            case 40:
                PairedSingleUnary(state, instruction, static value => -value);
                return;
            case 72:
                PairedSingleUnary(state, instruction, static value => value);
                return;
            case 136:
                PairedSingleUnary(state, instruction, static value => -Math.Abs(value));
                return;
            case 264:
                PairedSingleUnary(state, instruction, Math.Abs);
                return;
            case 528:
                PairedSingleMerge(state, instruction, takeAFromPair1: false, takeBFromPair1: false);
                return;
            case 560:
                PairedSingleMerge(state, instruction, takeAFromPair1: false, takeBFromPair1: true);
                return;
            case 592:
                PairedSingleMerge(state, instruction, takeAFromPair1: true, takeBFromPair1: false);
                return;
            case 624:
                PairedSingleMerge(state, instruction, takeAFromPair1: true, takeBFromPair1: true);
                return;
            case 1014:
                DataCacheBlockSetToZero(state, memory, instruction);
                return;
        }

        switch (arithmeticOpcode)
        {
            case 10:
                PairedSingleSum0(state, instruction);
                return;
            case 11:
                PairedSingleSum1(state, instruction);
                return;
            case 12:
                PairedSingleMultiplyScalarLane(state, instruction, pair1: false);
                return;
            case 13:
                PairedSingleMultiplyScalarLane(state, instruction, pair1: true);
                return;
            case 14:
                PairedSingleMultiplyAddScalarLane(state, instruction, pair1: false);
                return;
            case 15:
                PairedSingleMultiplyAddScalarLane(state, instruction, pair1: true);
                return;
            case 23:
                PairedSingleSelect(state, instruction);
                return;
            case 18:
            case 20:
            case 21:
            case 25:
            case 28:
            case 29:
            case 30:
            case 31:
                PairedSingleArithmetic(state, instruction, arithmeticOpcode);
                return;
            default:
                throw new UnsupportedInstructionException(state.Pc - sizeof(uint), instruction);
        }
    }

    private static void PairedSingleSelect(PowerPcState state, uint instruction)
    {
        int fD = Rd(instruction);
        int fA = Ra(instruction);
        int fB = Rb(instruction);
        int fC = (int)((instruction >> 6) & 0x1F);
        state.Fpr[fD] = state.Fpr[fA] >= 0.0d ? state.Fpr[fC] : state.Fpr[fB];
        state.FprPair1[fD] = state.FprPair1[fA] >= 0.0d ? state.FprPair1[fC] : state.FprPair1[fB];
    }

    private static void ComparePairedSingleRegister(PowerPcState state, uint instruction)
    {
        int crField = (int)((instruction >> 23) & 0x7);
        double left = state.Fpr[Ra(instruction)];
        double right = state.Fpr[Rb(instruction)];
        uint field = double.IsNaN(left) || double.IsNaN(right)
            ? 0b0001u
            : left < right ? 0b1000u
            : left > right ? 0b0100u
            : 0b0010u;

        SetCrFieldRaw(state, crField, field);
    }

    private static void PairedSingleUnary(PowerPcState state, uint instruction, Func<double, double> operation)
    {
        int fD = Rd(instruction);
        int fB = Rb(instruction);
        state.Fpr[fD] = operation(state.Fpr[fB]);
        state.FprPair1[fD] = operation(state.FprPair1[fB]);
    }

    private static void PairedSingleSum0(PowerPcState state, uint instruction)
    {
        int fD = Rd(instruction);
        int fA = Ra(instruction);
        int fB = Rb(instruction);
        int fC = (int)((instruction >> 6) & 0x1F);
        state.Fpr[fD] = (float)state.Fpr[fA] + (float)state.FprPair1[fB];
        state.FprPair1[fD] = (float)state.FprPair1[fC];
    }

    private static void PairedSingleSum1(PowerPcState state, uint instruction)
    {
        int fD = Rd(instruction);
        int fA = Ra(instruction);
        int fB = Rb(instruction);
        int fC = (int)((instruction >> 6) & 0x1F);
        state.Fpr[fD] = (float)state.Fpr[fC];
        state.FprPair1[fD] = (float)state.FprPair1[fA] + (float)state.Fpr[fB];
    }

    private static void PairedSingleMultiplyScalarLane(PowerPcState state, uint instruction, bool pair1)
    {
        int fD = Rd(instruction);
        int fA = Ra(instruction);
        int fC = (int)((instruction >> 6) & 0x1F);
        float scalar = pair1 ? (float)state.FprPair1[fC] : (float)state.Fpr[fC];
        state.Fpr[fD] = (float)state.Fpr[fA] * scalar;
        state.FprPair1[fD] = (float)state.FprPair1[fA] * scalar;
    }

    private static void PairedSingleMultiplyAddScalarLane(PowerPcState state, uint instruction, bool pair1)
    {
        int fD = Rd(instruction);
        int fA = Ra(instruction);
        int fB = Rb(instruction);
        int fC = (int)((instruction >> 6) & 0x1F);
        float scalar = pair1 ? (float)state.FprPair1[fC] : (float)state.Fpr[fC];
        state.Fpr[fD] = (float)state.Fpr[fA] * scalar + (float)state.Fpr[fB];
        state.FprPair1[fD] = (float)state.FprPair1[fA] * scalar + (float)state.FprPair1[fB];
    }

    private static void PairedSingleArithmetic(PowerPcState state, uint instruction, int extendedOpcode)
    {
        int fD = Rd(instruction);
        int fA = Ra(instruction);
        int fB = Rb(instruction);
        int fC = (int)((instruction >> 6) & 0x1F);

        float a0 = (float)state.Fpr[fA];
        float a1 = (float)state.FprPair1[fA];
        float b0 = (float)state.Fpr[fB];
        float b1 = (float)state.FprPair1[fB];
        float c0 = (float)state.Fpr[fC];
        float c1 = (float)state.FprPair1[fC];

        (float Result0, float Result1) = extendedOpcode switch
        {
            18 => (a0 / b0, a1 / b1),
            20 => (a0 - b0, a1 - b1),
            21 => (a0 + b0, a1 + b1),
            25 => (a0 * c0, a1 * c1),
            28 => (a0 * c0 - b0, a1 * c1 - b1),
            29 => (a0 * c0 + b0, a1 * c1 + b1),
            30 => (-(a0 * c0 - b0), -(a1 * c1 - b1)),
            31 => (-(a0 * c0 + b0), -(a1 * c1 + b1)),
            _ => throw new UnsupportedInstructionException(state.Pc - sizeof(uint), instruction),
        };

        state.Fpr[fD] = Result0;
        state.FprPair1[fD] = Result1;
    }

    private static void PairedSingleMerge(PowerPcState state, uint instruction, bool takeAFromPair1, bool takeBFromPair1)
    {
        int fD = Rd(instruction);
        int fA = Ra(instruction);
        int fB = Rb(instruction);
        state.Fpr[fD] = takeAFromPair1 ? state.FprPair1[fA] : state.Fpr[fA];
        state.FprPair1[fD] = takeBFromPair1 ? state.FprPair1[fB] : state.Fpr[fB];
    }

    private static void ExecuteOpcode19(PowerPcState state, uint instruction)
    {
        int extendedOpcode = (int)((instruction >> 1) & 0x3FF);
        uint oldPc = state.Pc - sizeof(uint);

        switch (extendedOpcode)
        {
            case 0:
                MoveConditionRegisterField(state, instruction);
                return;
            case 16:
                BranchConditionalToAddress(state, oldPc, instruction, state.Lr & 0xFFFF_FFFC);
                return;
            case 33:
                ConditionRegisterLogical(state, instruction, static (left, right) => !(left || right));
                return;
            case 129:
                ConditionRegisterLogical(state, instruction, static (left, right) => left && !right);
                return;
            case 193:
                ConditionRegisterLogical(state, instruction, static (left, right) => left ^ right);
                return;
            case 225:
                ConditionRegisterLogical(state, instruction, static (left, right) => !(left && right));
                return;
            case 257:
                ConditionRegisterLogical(state, instruction, static (left, right) => left && right);
                return;
            case 289:
                ConditionRegisterLogical(state, instruction, static (left, right) => left == right);
                return;
            case 417:
                ConditionRegisterLogical(state, instruction, static (left, right) => left || !right);
                return;
            case 449:
                ConditionRegisterLogical(state, instruction, static (left, right) => left || right);
                return;
            case 50:
                state.Pc = state.Spr[26];
                state.Msr = state.Spr[27];
                return;
            case 150:
                return;
            case 528:
                BranchConditionalToAddress(state, oldPc, instruction, state.Ctr & 0xFFFF_FFFC);
                return;
            default:
                throw new UnsupportedInstructionException(state.Pc - sizeof(uint), instruction);
        }
    }

    private static void MoveConditionRegisterField(PowerPcState state, uint instruction)
    {
        int targetField = (int)((instruction >> 23) & 0x7);
        int sourceField = (int)((instruction >> 18) & 0x7);
        uint field = (state.Cr >> (28 - sourceField * 4)) & 0xF;
        SetCrFieldRaw(state, targetField, field);
    }

    private static void ConditionRegisterLogical(PowerPcState state, uint instruction, Func<bool, bool, bool> operation)
    {
        int targetBit = (int)((instruction >> 21) & 0x1F);
        bool left = GetConditionRegisterBit(state, (int)((instruction >> 16) & 0x1F));
        bool right = GetConditionRegisterBit(state, (int)((instruction >> 11) & 0x1F));
        SetConditionRegisterBit(state, targetBit, operation(left, right));
    }

    private static void ExecuteFloatingOpcode59(PowerPcState state, uint instruction)
    {
        int extendedOpcode = (int)((instruction >> 1) & 0x1F);
        int fD = Rd(instruction);
        int fA = Ra(instruction);
        int fB = Rb(instruction);
        int fC = (int)((instruction >> 6) & 0x1F);

        float result = extendedOpcode switch
        {
            18 => (float)(state.Fpr[fA] / state.Fpr[fB]),
            20 => (float)(state.Fpr[fA] - state.Fpr[fB]),
            21 => (float)(state.Fpr[fA] + state.Fpr[fB]),
            24 => (float)(1.0d / state.Fpr[fB]),
            25 => (float)(state.Fpr[fA] * state.Fpr[fC]),
            28 => (float)(state.Fpr[fA] * state.Fpr[fC] - state.Fpr[fB]),
            29 => (float)(state.Fpr[fA] * state.Fpr[fC] + state.Fpr[fB]),
            30 => (float)(-(state.Fpr[fA] * state.Fpr[fC] - state.Fpr[fB])),
            31 => (float)(-(state.Fpr[fA] * state.Fpr[fC] + state.Fpr[fB])),
            _ => throw new UnsupportedInstructionException(state.Pc - sizeof(uint), instruction),
        };

        SetFloatingScalar(state, fD, result);
    }

    private static void ExecuteFloatingOpcode63(PowerPcState state, uint instruction)
    {
        int extendedOpcode = (int)((instruction >> 1) & 0x3FF);
        int arithmeticOpcode = extendedOpcode & 0x1F;
        int fD = Rd(instruction);
        int fA = Ra(instruction);
        int fB = Rb(instruction);
        int fC = (int)((instruction >> 6) & 0x1F);

        switch (extendedOpcode)
        {
            case 0:
            case 32:
                CompareFloatingRegister(state, instruction);
                return;
            case 12:
                SetFloatingScalar(state, fD, (float)state.Fpr[fB]);
                return;
            case 15:
                SetFloatingScalar(state, fD, BitConverter.Int64BitsToDouble(ConvertFloatingToIntegerWordZero(state.Fpr[fB])));
                return;
            case 26:
                SetFloatingScalar(state, fD, 1.0d / Math.Sqrt(state.Fpr[fB]));
                return;
        }

        switch (arithmeticOpcode)
        {
            case 18:
                SetFloatingScalar(state, fD, state.Fpr[fA] / state.Fpr[fB]);
                return;
            case 20:
                SetFloatingScalar(state, fD, state.Fpr[fA] - state.Fpr[fB]);
                return;
            case 21:
                SetFloatingScalar(state, fD, state.Fpr[fA] + state.Fpr[fB]);
                return;
            case 25:
                SetFloatingScalar(state, fD, state.Fpr[fA] * state.Fpr[fC]);
                return;
            case 28:
                SetFloatingScalar(state, fD, state.Fpr[fA] * state.Fpr[fC] - state.Fpr[fB]);
                return;
            case 29:
                SetFloatingScalar(state, fD, state.Fpr[fA] * state.Fpr[fC] + state.Fpr[fB]);
                return;
            case 30:
                SetFloatingScalar(state, fD, -(state.Fpr[fA] * state.Fpr[fC] - state.Fpr[fB]));
                return;
            case 31:
                SetFloatingScalar(state, fD, -(state.Fpr[fA] * state.Fpr[fC] + state.Fpr[fB]));
                return;
        }

        switch (extendedOpcode)
        {
            case 38:
                MoveToFpscrBit(state, instruction, value: true);
                return;
            case 40:
                SetFloatingScalar(state, fD, -state.Fpr[fB]);
                return;
            case 70:
                MoveToFpscrBit(state, instruction, value: false);
                return;
            case 72:
                SetFloatingScalar(state, fD, state.Fpr[fB]);
                return;
            case 136:
                SetFloatingScalar(state, fD, -Math.Abs(state.Fpr[fB]));
                return;
            case 264:
                SetFloatingScalar(state, fD, Math.Abs(state.Fpr[fB]));
                return;
            case 583:
                SetFloatingScalarBits(state, fD, state.Fpscr);
                return;
            case 711:
                MoveToFloatingPointStatusAndControlRegisterFields(state, instruction);
                return;
            default:
                throw new UnsupportedInstructionException(state.Pc - sizeof(uint), instruction);
        }
    }

    private static void CompareFloatingRegister(PowerPcState state, uint instruction)
    {
        int crField = (int)((instruction >> 23) & 0x7);
        double left = state.Fpr[Ra(instruction)];
        double right = state.Fpr[Rb(instruction)];
        uint field = double.IsNaN(left) || double.IsNaN(right)
            ? 0b0001u
            : left < right ? 0b1000u
            : left > right ? 0b0100u
            : 0b0010u;

        SetCrFieldRaw(state, crField, field);
    }

    private static long ConvertFloatingToIntegerWordZero(double value)
    {
        int result;
        if (double.IsNaN(value))
        {
            result = 0;
        }
        else if (value >= int.MaxValue)
        {
            result = int.MaxValue;
        }
        else if (value <= int.MinValue)
        {
            result = int.MinValue;
        }
        else
        {
            result = (int)Math.Truncate(value);
        }

        return (uint)result;
    }

    private static void MoveToFloatingPointStatusAndControlRegisterFields(PowerPcState state, uint instruction)
    {
        int fieldMask = (int)((instruction >> 17) & 0xFF);
        int fB = Rb(instruction);
        uint source = (uint)BitConverter.DoubleToInt64Bits(state.Fpr[fB]);

        for (int field = 0; field < 8; field++)
        {
            if ((fieldMask & (0x80 >> field)) == 0)
            {
                continue;
            }

            int shift = 28 - field * 4;
            uint mask = 0xFu << shift;
            state.Fpscr = (state.Fpscr & ~mask) | (source & mask);
        }
    }

    private static void MoveToFpscrBit(PowerPcState state, uint instruction, bool value)
    {
        int bit = (int)((instruction >> 21) & 0x1F);
        uint mask = 1u << (31 - bit);

        if (value)
        {
            state.Fpscr |= mask;
        }
        else
        {
            state.Fpscr &= ~mask;
        }
    }

    private static void CompareRegister(PowerPcState state, uint instruction, bool unsigned)
    {
        int crField = (int)((instruction >> 23) & 0x7);
        int rA = Ra(instruction);
        int rB = Rb(instruction);

        if (unsigned)
        {
            SetCrField(state, crField, state.Gpr[rA], state.Gpr[rB]);
        }
        else
        {
            SetCrField(state, crField, unchecked((int)state.Gpr[rA]), unchecked((int)state.Gpr[rB]));
        }
    }

    private static void MoveToConditionRegisterFields(PowerPcState state, uint instruction)
    {
        int mask = (int)((instruction >> 12) & 0xFF);
        uint source = state.Gpr[Rs(instruction)];

        for (int field = 0; field < 8; field++)
        {
            if ((mask & (0x80 >> field)) == 0)
            {
                continue;
            }

            int shift = 28 - field * 4;
            uint fieldMask = 0xFu << shift;
            state.Cr = (state.Cr & ~fieldMask) | (source & fieldMask);
        }
    }

    private static uint ReadSpecialPurposeRegister(PowerPcState state, int spr)
    {
        return spr switch
        {
            1 => state.Xer,
            8 => state.Lr,
            9 => state.Ctr,
            18 => 0,
            19 => 0,
            >= 528 and <= 535 => state.Spr[spr],
            >= 536 and <= 543 => state.Spr[spr],
            >= 560 and <= 567 => state.Spr[spr],
            >= 568 and <= 575 => state.Spr[spr],
            22 or 25 or 26 or 27 => state.Spr[spr],
            _ => state.Spr[spr],
        };
    }

    private static uint ReadTimeBase(PowerPcState state, int tbr)
    {
        return tbr switch
        {
            268 => (uint)state.TimeBase,
            269 => (uint)(state.TimeBase >> 32),
            _ => state.Spr[tbr],
        };
    }

    private static void WriteSpecialPurposeRegister(PowerPcState state, int spr, uint value)
    {
        switch (spr)
        {
            case 1:
                state.Xer = value;
                return;
            case 8:
                state.Lr = value;
                return;
            case 9:
                state.Ctr = value;
                return;
            case >= 528 and <= 535:
            case >= 536 and <= 543:
            case >= 560 and <= 567:
            case >= 568 and <= 575:
            case 18:
            case 19:
            case 22:
            case 25:
            case 26:
            case 27:
                state.Spr[spr] = value;
                return;
            default:
                state.Spr[spr] = value;
                return;
        }
    }

    private static bool ShouldBranch(PowerPcState state, int bo, int bi)
    {
        bool ctrOk = true;
        if ((bo & 0x04) == 0)
        {
            state.Ctr--;
            ctrOk = (state.Ctr != 0) ^ ((bo & 0x02) != 0);
        }

        bool crOk = true;
        if ((bo & 0x10) == 0)
        {
            crOk = GetConditionRegisterBit(state, bi) == ((bo & 0x08) != 0);
        }

        return ctrOk && crOk;
    }

    private static bool GetConditionRegisterBit(PowerPcState state, int bit)
    {
        return ((state.Cr >> (31 - bit)) & 1) != 0;
    }

    private static void SetConditionRegisterBit(PowerPcState state, int bit, bool value)
    {
        uint mask = 1u << (31 - bit);
        state.Cr = value ? state.Cr | mask : state.Cr & ~mask;
    }

    private static uint EffectiveAddress(PowerPcState state, uint instruction)
    {
        int rA = Ra(instruction);
        uint baseAddress = rA == 0 ? 0 : state.Gpr[rA];
        return unchecked(baseAddress + (uint)SignExtend16(instruction));
    }

    private static uint IndexedAddress(PowerPcState state, uint instruction)
    {
        int rA = Ra(instruction);
        uint baseAddress = rA == 0 ? 0 : state.Gpr[rA];
        return unchecked(baseAddress + state.Gpr[Rb(instruction)]);
    }

    private static uint PairedSingleQuantizedAddress(PowerPcState state, uint instruction)
    {
        int rA = Ra(instruction);
        uint baseAddress = rA == 0 ? 0 : state.Gpr[rA];
        int displacement = (short)((instruction & 0x0FFF) << 4) >> 4;
        return unchecked(baseAddress + (uint)displacement);
    }

    private static bool PairedSingleQuantizedSingle(uint instruction) => ((instruction >> 15) & 1) != 0;

    private static int PairedSingleQuantizedRegister(uint instruction) => (int)((instruction >> 12) & 0x7);

    private static int GqrLoadScale(PowerPcState state, int register) => SignExtend6((int)((state.Spr[912 + register] >> 24) & 0x3F));

    private static int GqrLoadType(PowerPcState state, int register) => (int)((state.Spr[912 + register] >> 16) & 0x7);

    private static int GqrStoreScale(PowerPcState state, int register) => SignExtend6((int)((state.Spr[912 + register] >> 8) & 0x3F));

    private static int GqrStoreType(PowerPcState state, int register) => (int)(state.Spr[912 + register] & 0x7);

    private static int SignExtend6(int value) => (value & 0x20) != 0 ? value - 0x40 : value;

    private static double ReadPairedSingleQuantizedOperand(IMemoryBus memory, uint address, int type, int scale, uint instructionAddress, uint instruction, out int size)
    {
        double value;
        switch (type)
        {
            case 0:
                size = sizeof(uint);
                return BitConverter.Int32BitsToSingle(unchecked((int)memory.Read32(address)));
            case 4:
                size = sizeof(byte);
                value = memory.Read8(address);
                break;
            case 5:
                size = sizeof(ushort);
                value = memory.Read16(address);
                break;
            case 6:
                size = sizeof(byte);
                value = unchecked((sbyte)memory.Read8(address));
                break;
            case 7:
                size = sizeof(ushort);
                value = unchecked((short)memory.Read16(address));
                break;
            default:
                throw new UnsupportedInstructionException(instructionAddress, instruction);
        }

        return Math.ScaleB(value, -scale);
    }

    private static int WritePairedSingleQuantizedOperand(IMemoryBus memory, uint address, double value, int type, int scale, uint instructionAddress, uint instruction)
    {
        switch (type)
        {
            case 0:
                memory.Write32(address, unchecked((uint)BitConverter.SingleToInt32Bits((float)value)));
                return sizeof(uint);
            case 4:
                memory.Write8(address, (byte)ClampQuantizedInteger(value, scale, 0, byte.MaxValue));
                return sizeof(byte);
            case 5:
                memory.Write16(address, (ushort)ClampQuantizedInteger(value, scale, 0, ushort.MaxValue));
                return sizeof(ushort);
            case 6:
                memory.Write8(address, unchecked((byte)(sbyte)ClampQuantizedInteger(value, scale, sbyte.MinValue, sbyte.MaxValue)));
                return sizeof(byte);
            case 7:
                memory.Write16(address, unchecked((ushort)(short)ClampQuantizedInteger(value, scale, short.MinValue, short.MaxValue)));
                return sizeof(ushort);
            default:
                throw new UnsupportedInstructionException(instructionAddress, instruction);
        }
    }

    private static int ClampQuantizedInteger(double value, int scale, int min, int max)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        double scaled = Math.Truncate(Math.ScaleB(value, scale));
        if (scaled <= min)
        {
            return min;
        }

        if (scaled >= max)
        {
            return max;
        }

        return (int)scaled;
    }

    private static void SetFloatingScalar(PowerPcState state, int register, double value)
    {
        state.Fpr[register] = value;
        state.FprPair1[register] = value;
    }

    private static void SetFloatingScalarBits(PowerPcState state, int register, ulong value)
    {
        double raw = BitConverter.Int64BitsToDouble(unchecked((long)value));
        state.Fpr[register] = raw;
        state.FprPair1[register] = raw;
    }

    private static void DataCacheBlockSetToZero(PowerPcState state, IMemoryBus memory, uint instruction)
    {
        uint address = IndexedAddress(state, instruction) & ~31u;
        for (uint offset = 0; offset < 32; offset += sizeof(uint))
        {
            memory.Write32(address + offset, 0);
        }
    }

    private static uint BaseForImmediate(PowerPcState state, int rA) => rA == 0 ? 0 : state.Gpr[rA];

    private static int Rd(uint instruction) => (int)((instruction >> 21) & 0x1F);

    private static int Rs(uint instruction) => Rd(instruction);

    private static int Ra(uint instruction) => (int)((instruction >> 16) & 0x1F);

    private static int Rb(uint instruction) => (int)((instruction >> 11) & 0x1F);

    private static int Spr(uint instruction) => (int)(((instruction >> 16) & 0x1F) | ((instruction >> 6) & 0x3E0));

    private static int Sr(uint instruction) => (int)((instruction >> 16) & 0xF);

    private static int SignExtend16(uint instruction) => (short)(instruction & 0xFFFF);

    private static uint RotateLeft(uint value, int shift) => (value << shift) | (value >> (32 - shift));

    private static uint ShiftLeftWord(uint value, uint shift)
    {
        return (shift & 0x20) != 0 ? 0 : value << (int)(shift & 0x1F);
    }

    private static uint ShiftRightWord(uint value, uint shift)
    {
        return (shift & 0x20) != 0 ? 0 : value >> (int)(shift & 0x1F);
    }

    private static uint ShiftRightAlgebraicWord(PowerPcState state, uint value, uint shift)
    {
        int amount = (shift & 0x20) != 0 ? 32 : (int)(shift & 0x1F);
        uint result = amount == 0 ? value : unchecked((uint)((int)value >> amount));
        bool carry = amount > 0 && (value & 0x8000_0000) != 0 && (value & ((1u << Math.Min(amount, 31)) - 1)) != 0;
        SetCarry(state, carry);
        return result;
    }

    private static uint SubtractFromExtended(PowerPcState state, uint subtrahend, uint minuend)
    {
        ulong carry = (state.Xer & 0x2000_0000) != 0 ? 1u : 0u;
        ulong result = (ulong)minuend + (~subtrahend & 0xFFFF_FFFFu) + carry;
        SetCarry(state, result > uint.MaxValue);
        return (uint)result;
    }

    private static uint SubtractFromCarrying(PowerPcState state, uint subtrahend, uint minuend)
    {
        uint result = unchecked(minuend - subtrahend);
        SetCarry(state, minuend >= subtrahend);
        return result;
    }

    private static uint AddCarrying(PowerPcState state, uint left, uint right)
    {
        uint result = unchecked(left + right);
        SetCarry(state, result < left);
        return result;
    }

    private static uint AddExtended(PowerPcState state, uint left, uint right)
    {
        ulong carry = (state.Xer & 0x2000_0000) != 0 ? 1u : 0u;
        ulong result = (ulong)left + right + carry;
        SetCarry(state, result > uint.MaxValue);
        return (uint)result;
    }

    private static uint Mask(int begin, int end)
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

    private static void SetStoreConditionalCr0(PowerPcState state, bool succeeded)
    {
        uint field = succeeded ? 0x2000_0000u : 0;
        state.Cr = (state.Cr & 0x0FFF_FFFF) | field | ((state.Xer & 0x8000_0000) >> 3);
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

    private static void SetCrField(PowerPcState state, int fieldIndex, int left, int right)
    {
        uint field = left < right ? 0b1000u : left > right ? 0b0100u : 0b0010u;
        field |= (state.Xer >> 31) & 1;
        SetCrFieldRaw(state, fieldIndex, field);
    }

    private static void SetCrField(PowerPcState state, int fieldIndex, uint left, uint right)
    {
        uint field = left < right ? 0b1000u : left > right ? 0b0100u : 0b0010u;
        field |= (state.Xer >> 31) & 1;
        SetCrFieldRaw(state, fieldIndex, field);
    }

    private static void SetCrFieldRaw(PowerPcState state, int fieldIndex, uint field)
    {
        int shift = 28 - fieldIndex * 4;
        uint mask = 0xFu << shift;
        state.Cr = (state.Cr & ~mask) | ((field & 0xF) << shift);
    }
}
