namespace NgcSharp.App;

public readonly record struct MemoryBinaryDumpRequest(uint Address, int Length, string Path, int? Instruction = null);
