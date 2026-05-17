namespace NgcSharp.Core;

public sealed record DolSection(string Name, uint FileOffset, uint Address, ReadOnlyMemory<byte> Data)
{
    public uint Size => checked((uint)Data.Length);
}
