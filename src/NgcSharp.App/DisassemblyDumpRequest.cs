namespace NgcSharp.App;

public readonly record struct DisassemblyDumpRequest(uint Address, int InstructionCount);
