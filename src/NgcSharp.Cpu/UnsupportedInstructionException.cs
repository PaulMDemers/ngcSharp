namespace NgcSharp.Cpu;

public sealed class UnsupportedInstructionException : Exception
{
    public UnsupportedInstructionException(uint address, uint instruction)
        : base($"Unsupported PowerPC instruction 0x{instruction:X8} at 0x{address:X8}.")
    {
        Address = address;
        Instruction = instruction;
    }

    public uint Address { get; }

    public uint Instruction { get; }
}
