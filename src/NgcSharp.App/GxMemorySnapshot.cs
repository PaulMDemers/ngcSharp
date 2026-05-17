namespace NgcSharp.App;

public sealed record GxMemorySnapshot(int FifoOffset, uint Address, byte[] Bytes, string? Path = null);

public sealed class GxMemorySnapshotSet
{
    private readonly GxMemorySnapshot[] _snapshots;

    public GxMemorySnapshotSet(IEnumerable<GxMemorySnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        _snapshots = snapshots
            .Where(snapshot => snapshot.Bytes.Length != 0)
            .OrderBy(snapshot => snapshot.FifoOffset)
            .ToArray();
    }

    public bool IsEmpty => _snapshots.Length == 0;

    public bool TryRead8(int fifoOffset, uint address, out byte value)
    {
        if (TryFindSnapshot(fifoOffset, address, length: 1, out GxMemorySnapshot? snapshot, out int offset))
        {
            value = snapshot.Bytes[offset];
            return true;
        }

        value = 0;
        return false;
    }

    public bool TryRead16(int fifoOffset, uint address, out ushort value)
    {
        if (TryFindSnapshot(fifoOffset, address, length: sizeof(ushort), out GxMemorySnapshot? snapshot, out int offset))
        {
            value = (ushort)((snapshot.Bytes[offset] << 8) | snapshot.Bytes[offset + 1]);
            return true;
        }

        value = 0;
        return false;
    }

    public bool TryRead32(int fifoOffset, uint address, out uint value)
    {
        if (TryFindSnapshot(fifoOffset, address, length: sizeof(uint), out GxMemorySnapshot? snapshot, out int offset))
        {
            value = ((uint)snapshot.Bytes[offset] << 24)
                | ((uint)snapshot.Bytes[offset + 1] << 16)
                | ((uint)snapshot.Bytes[offset + 2] << 8)
                | snapshot.Bytes[offset + 3];
            return true;
        }

        value = 0;
        return false;
    }

    public bool TryHashRange(int fifoOffset, uint address, int length, out uint hash)
    {
        if (length > 0 && TryFindSnapshot(fifoOffset, address, length, out GxMemorySnapshot? snapshot, out int offset))
        {
            hash = HashBytes(snapshot.Bytes.AsSpan(offset, length));
            return true;
        }

        hash = 0;
        return false;
    }

    public string DescribeSource(int fifoOffset, uint address, int length)
    {
        return length > 0 && TryFindSnapshot(fifoOffset, address, length, out GxMemorySnapshot? snapshot, out _)
            ? $"checkpoint+0x{snapshot.FifoOffset:X}"
            : "main-ram";
    }

    private bool TryFindSnapshot(int fifoOffset, uint address, int length, out GxMemorySnapshot snapshot, out int offset)
    {
        uint normalizedAddress = NormalizeAddress(address);
        for (int index = _snapshots.Length - 1; index >= 0; index--)
        {
            GxMemorySnapshot candidate = _snapshots[index];
            if (candidate.FifoOffset > fifoOffset)
            {
                continue;
            }

            uint candidateAddress = NormalizeAddress(candidate.Address);
            uint delta = normalizedAddress - candidateAddress;
            if (normalizedAddress < candidateAddress
                || delta > int.MaxValue
                || (long)delta + length > candidate.Bytes.Length)
            {
                continue;
            }

            snapshot = candidate;
            offset = (int)delta;
            return true;
        }

        snapshot = null!;
        offset = 0;
        return false;
    }

    private static uint NormalizeAddress(uint address)
    {
        return address switch
        {
            >= 0xC000_0000 and < 0xC180_0000 => address - 0xC000_0000,
            >= 0x8000_0000 and < 0x8180_0000 => address - 0x8000_0000,
            _ => address,
        };
    }

    private static uint HashBytes(ReadOnlySpan<byte> bytes)
    {
        uint hash = 2166136261u;
        for (int index = 0; index < bytes.Length; index++)
        {
            hash ^= bytes[index];
            hash *= 16777619u;
        }

        return hash;
    }
}
