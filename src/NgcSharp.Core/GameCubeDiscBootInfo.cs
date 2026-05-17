namespace NgcSharp.Core;

public sealed record GameCubeDiscBootInfo(
    uint FileSystemTableAddress,
    uint FileSystemTableSize,
    uint FileSystemTableMaxSize);
