namespace NgcSharp.Core;

public sealed record GameCubeFileSystemEntry(
    string Path,
    bool IsDirectory,
    uint DiscOffset,
    uint Size,
    int ParentIndex,
    int NextIndex);
