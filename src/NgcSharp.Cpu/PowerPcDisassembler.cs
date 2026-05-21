namespace NgcSharp.Cpu;

public static class PowerPcDisassembler
{
    public static string Disassemble(uint instruction)
    {
        int opcode = (int)(instruction >> 26);
        return opcode switch
        {
            4 => DisassembleOpcode4(instruction),
            7 => $"mulli r{Rd(instruction)}, r{Ra(instruction)}, {SignExtend16(instruction)}",
            8 => $"subfic r{Rd(instruction)}, r{Ra(instruction)}, {SignExtend16(instruction)}",
            10 => $"cmpli cr{(instruction >> 23) & 0x7}, r{Ra(instruction)}, 0x{instruction & 0xFFFF:X4}",
            11 => $"cmpi cr{(instruction >> 23) & 0x7}, r{Ra(instruction)}, {SignExtend16(instruction)}",
            12 => $"addic r{Rd(instruction)}, r{Ra(instruction)}, {SignExtend16(instruction)}",
            13 => $"addic. r{Rd(instruction)}, r{Ra(instruction)}, {SignExtend16(instruction)}",
            14 => $"addi r{Rd(instruction)}, r{Ra(instruction)}, {SignExtend16(instruction)}",
            15 => $"addis r{Rd(instruction)}, r{Ra(instruction)}, {SignExtend16(instruction)}",
            16 => $"bc{LinkSuffix(instruction)} {(instruction >> 21) & 0x1F}, {(instruction >> 16) & 0x1F}, 0x{BranchConditionalOffset(instruction):X8}",
            17 => "sc",
            18 => $"b{LinkSuffix(instruction)} 0x{BranchOffset(instruction):X8}",
            20 => $"rlwimi r{Ra(instruction)}, r{Rs(instruction)}, {(instruction >> 11) & 0x1F}, {(instruction >> 6) & 0x1F}, {(instruction >> 1) & 0x1F}",
            21 => $"rlwinm r{Ra(instruction)}, r{Rs(instruction)}, {(instruction >> 11) & 0x1F}, {(instruction >> 6) & 0x1F}, {(instruction >> 1) & 0x1F}",
            24 => $"ori r{Ra(instruction)}, r{Rs(instruction)}, 0x{instruction & 0xFFFF:X4}",
            25 => $"oris r{Ra(instruction)}, r{Rs(instruction)}, 0x{instruction & 0xFFFF:X4}",
            26 => $"xori r{Ra(instruction)}, r{Rs(instruction)}, 0x{instruction & 0xFFFF:X4}",
            27 => $"xoris r{Ra(instruction)}, r{Rs(instruction)}, 0x{instruction & 0xFFFF:X4}",
            28 => $"andi. r{Ra(instruction)}, r{Rs(instruction)}, 0x{instruction & 0xFFFF:X4}",
            29 => $"andis. r{Ra(instruction)}, r{Rs(instruction)}, 0x{instruction & 0xFFFF:X4}",
            32 => $"lwz r{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            33 => $"lwzu r{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            34 => $"lbz r{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            35 => $"lbzu r{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            36 => $"stw r{Rs(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            37 => $"stwu r{Rs(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            38 => $"stb r{Rs(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            39 => $"stbu r{Rs(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            40 => $"lhz r{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            41 => $"lhzu r{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            42 => $"lha r{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            43 => $"lhau r{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            44 => $"sth r{Rs(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            45 => $"sthu r{Rs(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            46 => $"lmw r{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            47 => $"stmw r{Rs(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            48 => $"lfs f{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            49 => $"lfsu f{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            50 => $"lfd f{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            51 => $"lfdu f{Rd(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            52 => $"stfs f{Rs(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            53 => $"stfsu f{Rs(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            54 => $"stfd f{Rs(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            55 => $"stfdu f{Rs(instruction)}, {SignExtend16(instruction)}(r{Ra(instruction)})",
            56 => DisassemblePairedSingleQuantizedMemory("psq_l", instruction),
            57 => DisassemblePairedSingleQuantizedMemory("psq_lu", instruction),
            31 => DisassembleOpcode31(instruction),
            59 => DisassembleFloatingOpcode59(instruction),
            60 => DisassemblePairedSingleQuantizedMemory("psq_st", instruction),
            61 => DisassemblePairedSingleQuantizedMemory("psq_stu", instruction),
            19 => DisassembleOpcode19(instruction),
            63 => DisassembleFloatingOpcode63(instruction),
            _ => $".long 0x{instruction:X8}",
        };
    }

    private static string DisassembleOpcode4(uint instruction)
    {
        int xo = (int)((instruction >> 1) & 0x3FF);
        int arithmeticXo = xo & 0x1F;
        string? special = xo switch
        {
            32 => $"ps_cmpu cr{(instruction >> 23) & 0x7}, f{Ra(instruction)}, f{Rb(instruction)}",
            40 => $"ps_neg f{Rd(instruction)}, f{Rb(instruction)}",
            72 => $"ps_mr f{Rd(instruction)}, f{Rb(instruction)}",
            136 => $"ps_nabs f{Rd(instruction)}, f{Rb(instruction)}",
            264 => $"ps_abs f{Rd(instruction)}, f{Rb(instruction)}",
            528 => $"ps_merge00 f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            560 => $"ps_merge01 f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            592 => $"ps_merge10 f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            624 => $"ps_merge11 f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            1014 => $"dcbz_l r{Ra(instruction)}, r{Rb(instruction)}",
            _ => null,
        };

        if (special is not null)
        {
            return special;
        }

        return arithmeticXo switch
        {
            10 => $"ps_sum0 f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            11 => $"ps_sum1 f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            12 => $"ps_muls0 f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}",
            13 => $"ps_muls1 f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}",
            14 => $"ps_madds0 f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            15 => $"ps_madds1 f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            23 => $"ps_sel f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            18 => $"ps_div f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            20 => $"ps_sub f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            21 => $"ps_add f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            25 => $"ps_mul f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}",
            28 => $"ps_msub f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            29 => $"ps_madd f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            30 => $"ps_nmsub f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            31 => $"ps_nmadd f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            _ => $".long 0x{instruction:X8}",
        };
    }

    private static string DisassembleOpcode31(uint instruction)
    {
        int xo = (int)((instruction >> 1) & 0x3FF);
        string suffix = (instruction & 1) != 0 ? "." : string.Empty;

        return xo switch
        {
            0 => $"cmp cr{(instruction >> 23) & 0x7}, r{Ra(instruction)}, r{Rb(instruction)}",
            4 => $"tw {(instruction >> 21) & 0x1F}, r{Ra(instruction)}, r{Rb(instruction)}",
            8 => $"subfc{suffix} r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            10 => $"addc{suffix} r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            11 => $"mulhwu{suffix} r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            19 => $"mfcr r{Rd(instruction)}",
            20 => $"lwarx r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            23 => $"lwzx r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            24 => $"slw{suffix} r{Ra(instruction)}, r{Rs(instruction)}, r{Rb(instruction)}",
            26 => $"cntlzw{suffix} r{Ra(instruction)}, r{Rs(instruction)}",
            28 => $"and{suffix} r{Ra(instruction)}, r{Rs(instruction)}, r{Rb(instruction)}",
            32 => $"cmpl cr{(instruction >> 23) & 0x7}, r{Ra(instruction)}, r{Rb(instruction)}",
            40 => $"subf{suffix} r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            55 => $"lwzux r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            54 => $"dcbst r{Ra(instruction)}, r{Rb(instruction)}",
            60 => $"andc{suffix} r{Ra(instruction)}, r{Rs(instruction)}, r{Rb(instruction)}",
            75 => $"mulhw{suffix} r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            83 => $"mfmsr r{Rd(instruction)}",
            86 => $"dcbf r{Ra(instruction)}, r{Rb(instruction)}",
            87 => $"lbzx r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            104 => $"neg{suffix} r{Rd(instruction)}, r{Ra(instruction)}",
            119 => $"lbzux r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            124 => $"nor{suffix} r{Ra(instruction)}, r{Rs(instruction)}, r{Rb(instruction)}",
            136 => $"subfe{suffix} r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            138 => $"adde{suffix} r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            144 => $"mtcrf 0x{(instruction >> 12) & 0xFF:X2}, r{Rs(instruction)}",
            146 => $"mtmsr r{Rs(instruction)}",
            150 => $"stwcx. r{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            151 => $"stwx r{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            183 => $"stwux r{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            200 => $"subfze{suffix} r{Rd(instruction)}, r{Ra(instruction)}",
            202 => $"addze{suffix} r{Rd(instruction)}, r{Ra(instruction)}",
            210 => $"mtsr sr{Sr(instruction)}, r{Rs(instruction)}",
            215 => $"stbx r{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            234 => $"addme{suffix} r{Rd(instruction)}, r{Ra(instruction)}",
            235 => $"mullw{suffix} r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            247 => $"stbux r{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            246 => $"dcbtst r{Ra(instruction)}, r{Rb(instruction)}",
            266 => $"add{suffix} r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            278 => $"dcbt r{Ra(instruction)}, r{Rb(instruction)}",
            279 => $"lhzx r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            284 => $"eqv{suffix} r{Ra(instruction)}, r{Rs(instruction)}, r{Rb(instruction)}",
            310 => $"eciwx r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            311 => $"lhzux r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            316 => $"xor{suffix} r{Ra(instruction)}, r{Rs(instruction)}, r{Rb(instruction)}",
            339 => DisassembleMfspr(instruction),
            371 => DisassembleMftb(instruction),
            343 => $"lhax r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            375 => $"lhaux r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            407 => $"sthx r{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            439 => $"sthux r{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            444 => $"or{suffix} r{Ra(instruction)}, r{Rs(instruction)}, r{Rb(instruction)}",
            459 => $"divwu{suffix} r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            467 => DisassembleMtspr(instruction),
            470 => $"dcbi r{Ra(instruction)}, r{Rb(instruction)}",
            476 => $"nand{suffix} r{Ra(instruction)}, r{Rs(instruction)}, r{Rb(instruction)}",
            491 => $"divw{suffix} r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            533 => $"lswx r{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            535 => $"lfsx f{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            595 => $"mfsr r{Rd(instruction)}, sr{Sr(instruction)}",
            536 => $"srw{suffix} r{Ra(instruction)}, r{Rs(instruction)}, r{Rb(instruction)}",
            567 => $"lfsux f{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            599 => $"lfdx f{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            631 => $"lfdux f{Rd(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            661 => $"stswx r{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            663 => $"stfsx f{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            695 => $"stfsux f{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            725 => $"stswi r{Rs(instruction)}, r{Ra(instruction)}, {StringWordImmediateByteCount(instruction)}",
            727 => $"stfdx f{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            759 => $"stfdux f{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            598 => "sync",
            438 => $"ecowx r{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            792 => $"sraw{suffix} r{Ra(instruction)}, r{Rs(instruction)}, r{Rb(instruction)}",
            824 => $"srawi{suffix} r{Ra(instruction)}, r{Rs(instruction)}, {Rb(instruction)}",
            922 => $"extsh{suffix} r{Ra(instruction)}, r{Rs(instruction)}",
            954 => $"extsb{suffix} r{Ra(instruction)}, r{Rs(instruction)}",
            983 => $"stfiwx f{Rs(instruction)}, r{Ra(instruction)}, r{Rb(instruction)}",
            982 => $"icbi r{Ra(instruction)}, r{Rb(instruction)}",
            1014 => $"dcbz r{Ra(instruction)}, r{Rb(instruction)}",
            _ => $".long 0x{instruction:X8}",
        };
    }

    private static string DisassembleOpcode19(uint instruction)
    {
        int xo = (int)((instruction >> 1) & 0x3FF);
        return xo switch
        {
            0 => $"mcrf cr{(instruction >> 23) & 0x7}, cr{(instruction >> 18) & 0x7}",
            16 => (instruction & 1) != 0 ? "bclrl" : "bclr",
            33 => DisassembleConditionRegisterLogical("crnor", instruction),
            50 => "rfi",
            129 => DisassembleConditionRegisterLogical("crandc", instruction),
            150 => "isync",
            193 => DisassembleConditionRegisterLogical("crxor", instruction),
            225 => DisassembleConditionRegisterLogical("crnand", instruction),
            257 => DisassembleConditionRegisterLogical("crand", instruction),
            289 => DisassembleConditionRegisterLogical("creqv", instruction),
            417 => DisassembleConditionRegisterLogical("crorc", instruction),
            449 => DisassembleConditionRegisterLogical("cror", instruction),
            528 => (instruction & 1) != 0 ? "bcctrl" : "bcctr",
            _ => $".long 0x{instruction:X8}",
        };
    }

    private static string DisassembleConditionRegisterLogical(string mnemonic, uint instruction)
    {
        return $"{mnemonic} {(instruction >> 21) & 0x1F}, {(instruction >> 16) & 0x1F}, {(instruction >> 11) & 0x1F}";
    }

    private static string DisassembleFloatingOpcode59(uint instruction)
    {
        int xo = (int)((instruction >> 1) & 0x1F);
        string suffix = (instruction & 1) != 0 ? "." : string.Empty;

        return xo switch
        {
            18 => $"fdivs{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            20 => $"fsubs{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            21 => $"fadds{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            24 => $"fres{suffix} f{Rd(instruction)}, f{Rb(instruction)}",
            25 => $"fmuls{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}",
            28 => $"fmsubs{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            29 => $"fmadds{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            30 => $"fnmsubs{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            31 => $"fnmadds{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            _ => $".long 0x{instruction:X8}",
        };
    }

    private static string DisassemblePairedSingleQuantizedMemory(string mnemonic, uint instruction)
    {
        return $"{mnemonic} f{Rd(instruction)}, {PairedSingleQuantizedDisplacement(instruction)}(r{Ra(instruction)}), {(instruction >> 15) & 1}, {(instruction >> 12) & 0x7}";
    }

    private static string DisassembleFloatingOpcode63(uint instruction)
    {
        int xo = (int)((instruction >> 1) & 0x3FF);
        int arithmeticXo = xo & 0x1F;
        string suffix = (instruction & 1) != 0 ? "." : string.Empty;

        string? special = xo switch
        {
            0 => $"fcmpu cr{(instruction >> 23) & 0x7}, f{Ra(instruction)}, f{Rb(instruction)}",
            32 => $"fcmpo cr{(instruction >> 23) & 0x7}, f{Ra(instruction)}, f{Rb(instruction)}",
            12 => $"frsp{suffix} f{Rd(instruction)}, f{Rb(instruction)}",
            15 => $"fctiwz{suffix} f{Rd(instruction)}, f{Rb(instruction)}",
            26 => $"frsqrte{suffix} f{Rd(instruction)}, f{Rb(instruction)}",
            38 => $"mtfsb1 {(instruction >> 21) & 0x1F}",
            40 => $"fneg{suffix} f{Rd(instruction)}, f{Rb(instruction)}",
            70 => $"mtfsb0 {(instruction >> 21) & 0x1F}",
            72 => $"fmr{suffix} f{Rd(instruction)}, f{Rb(instruction)}",
            136 => $"fnabs{suffix} f{Rd(instruction)}, f{Rb(instruction)}",
            264 => $"fabs{suffix} f{Rd(instruction)}, f{Rb(instruction)}",
            583 => $"mffs{suffix} f{Rd(instruction)}",
            711 => $"mtfsf 0x{(instruction >> 17) & 0xFF:X2}, f{Rb(instruction)}",
            _ => null,
        };

        if (special is not null)
        {
            return special;
        }

        return arithmeticXo switch
        {
            18 => $"fdiv{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            20 => $"fsub{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            21 => $"fadd{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Rb(instruction)}",
            25 => $"fmul{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}",
            28 => $"fmsub{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            29 => $"fmadd{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            30 => $"fnmsub{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            31 => $"fnmadd{suffix} f{Rd(instruction)}, f{Ra(instruction)}, f{Fc(instruction)}, f{Rb(instruction)}",
            _ => $".long 0x{instruction:X8}",
        };
    }

    private static string DisassembleMfspr(uint instruction)
    {
        int spr = Spr(instruction);
        string name = spr switch
        {
            1 => "xer",
            8 => "lr",
            9 => "ctr",
            _ => spr.ToString(),
        };

        return spr switch
        {
            8 => $"mflr r{Rd(instruction)}",
            9 => $"mfctr r{Rd(instruction)}",
            _ => $"mfspr r{Rd(instruction)}, {name}",
        };
    }

    private static string DisassembleMftb(uint instruction)
    {
        int tbr = Spr(instruction);
        return tbr switch
        {
            268 => $"mftb r{Rd(instruction)}",
            269 => $"mftbu r{Rd(instruction)}",
            _ => $"mftb r{Rd(instruction)}, {tbr}",
        };
    }

    private static string DisassembleMtspr(uint instruction)
    {
        int spr = Spr(instruction);
        string name = spr switch
        {
            1 => "xer",
            8 => "lr",
            9 => "ctr",
            _ => spr.ToString(),
        };

        return spr switch
        {
            8 => $"mtlr r{Rs(instruction)}",
            9 => $"mtctr r{Rs(instruction)}",
            _ => $"mtspr {name}, r{Rs(instruction)}",
        };
    }

    private static int Rd(uint instruction) => (int)((instruction >> 21) & 0x1F);

    private static int Rs(uint instruction) => Rd(instruction);

    private static int Ra(uint instruction) => (int)((instruction >> 16) & 0x1F);

    private static int Rb(uint instruction) => (int)((instruction >> 11) & 0x1F);

    private static int Fc(uint instruction) => (int)((instruction >> 6) & 0x1F);

    private static int StringWordImmediateByteCount(uint instruction)
    {
        int byteCount = Rb(instruction);
        return byteCount == 0 ? 32 : byteCount;
    }

    private static int Spr(uint instruction) => (int)(((instruction >> 16) & 0x1F) | ((instruction >> 6) & 0x3E0));

    private static int Sr(uint instruction) => (int)((instruction >> 16) & 0xF);

    private static int SignExtend16(uint instruction) => (short)(instruction & 0xFFFF);

    private static int PairedSingleQuantizedDisplacement(uint instruction) => (short)((instruction & 0x0FFF) << 4) >> 4;

    private static uint BranchOffset(uint instruction)
    {
        uint offset = instruction & 0x03FF_FFFC;
        return (offset & 0x0200_0000) != 0 ? offset | 0xFC00_0000 : offset;
    }

    private static uint BranchConditionalOffset(uint instruction) => unchecked((uint)(short)(instruction & 0xFFFC));

    private static string LinkSuffix(uint instruction) => (instruction & 1) != 0 ? "l" : string.Empty;
}
