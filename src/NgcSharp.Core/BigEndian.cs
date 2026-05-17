using System.Buffers.Binary;

namespace NgcSharp.Core;

public static class BigEndian
{
    public static ushort ReadUInt16(ReadOnlySpan<byte> source) => BinaryPrimitives.ReadUInt16BigEndian(source);

    public static uint ReadUInt32(ReadOnlySpan<byte> source) => BinaryPrimitives.ReadUInt32BigEndian(source);

    public static ulong ReadUInt64(ReadOnlySpan<byte> source) => BinaryPrimitives.ReadUInt64BigEndian(source);

    public static void WriteUInt16(Span<byte> destination, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(destination, value);

    public static void WriteUInt32(Span<byte> destination, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(destination, value);

    public static void WriteUInt64(Span<byte> destination, ulong value) =>
        BinaryPrimitives.WriteUInt64BigEndian(destination, value);
}
