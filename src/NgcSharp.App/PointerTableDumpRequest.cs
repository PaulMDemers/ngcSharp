namespace NgcSharp.App;

public readonly record struct PointerTableDumpRequest(uint Address, int Count, int Stride, int PointerOffset, int TargetWords);
