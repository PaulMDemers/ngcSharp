namespace NgcSharp.Core;

public sealed record MmioAccess(MmioAccessKind Kind, uint Address, int Width, uint Value, string DeviceName);
