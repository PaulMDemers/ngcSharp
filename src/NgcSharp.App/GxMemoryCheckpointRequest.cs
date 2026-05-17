namespace NgcSharp.App;

public readonly record struct GxMemoryCheckpointRequest(int FifoOffset, uint Address, int Length, string Path);
