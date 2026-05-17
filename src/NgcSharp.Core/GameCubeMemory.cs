namespace NgcSharp.Core;

public sealed class GameCubeMemory : IMemoryBus
{
    private const int MainRamTopGuardSize = 64;

    private readonly byte[] _mainRam;
    private readonly byte[] _mainRamTopGuard = new byte[MainRamTopGuardSize];

    public GameCubeMemory()
        : this(new byte[GameCubeAddress.MainRamSize])
    {
    }

    public GameCubeMemory(byte[] mainRam)
    {
        if (mainRam.Length != GameCubeAddress.MainRamSize)
        {
            throw new ArgumentException($"Main RAM must be exactly {GameCubeAddress.MainRamSize} bytes.", nameof(mainRam));
        }

        _mainRam = mainRam;
    }

    public int MainRamSize => _mainRam.Length;

    public ReadOnlyMemory<byte> MainRam => _mainRam;

    public bool EnableMainRamTopGuard { get; set; }

    public Action<uint, int>? MainRamBulkWriteObserver { get; set; }

    public Action<uint, int, uint>? MainRamStoreObserver { get; set; }

    public byte Read8(uint address)
    {
        if (TryTranslateMainRam(address, sizeof(byte), out int offset))
        {
            return _mainRam[offset];
        }

        if (TryTranslateMainRamTopGuard(address, sizeof(byte), out int guardOffset))
        {
            return _mainRamTopGuard[guardOffset];
        }

        throw new AddressTranslationException(address);
    }

    public ushort Read16(uint address)
    {
        if (TryTranslateMainRam(address, sizeof(ushort), out int offset))
        {
            return BigEndian.ReadUInt16(_mainRam.AsSpan(offset, sizeof(ushort)));
        }

        if (TryTranslateMainRamTopGuard(address, sizeof(ushort), out int guardOffset))
        {
            return BigEndian.ReadUInt16(_mainRamTopGuard.AsSpan(guardOffset, sizeof(ushort)));
        }

        throw new AddressTranslationException(address);
    }

    public uint Read32(uint address)
    {
        if (TryTranslateMainRam(address, sizeof(uint), out int offset))
        {
            return BigEndian.ReadUInt32(_mainRam.AsSpan(offset, sizeof(uint)));
        }

        if (TryTranslateMainRamTopGuard(address, sizeof(uint), out int guardOffset))
        {
            return BigEndian.ReadUInt32(_mainRamTopGuard.AsSpan(guardOffset, sizeof(uint)));
        }

        throw new AddressTranslationException(address);
    }

    public void Write8(uint address, byte value)
    {
        if (TryTranslateMainRam(address, sizeof(byte), out int offset))
        {
            _mainRam[offset] = value;
            MainRamStoreObserver?.Invoke(address, sizeof(byte), value);
            return;
        }

        if (TryTranslateMainRamTopGuard(address, sizeof(byte), out int guardOffset))
        {
            _mainRamTopGuard[guardOffset] = value;
            return;
        }

        throw new AddressTranslationException(address);
    }

    public void Write16(uint address, ushort value)
    {
        if (TryTranslateMainRam(address, sizeof(ushort), out int offset))
        {
            BigEndian.WriteUInt16(_mainRam.AsSpan(offset, sizeof(ushort)), value);
            MainRamStoreObserver?.Invoke(address, sizeof(ushort), value);
            return;
        }

        if (TryTranslateMainRamTopGuard(address, sizeof(ushort), out int guardOffset))
        {
            BigEndian.WriteUInt16(_mainRamTopGuard.AsSpan(guardOffset, sizeof(ushort)), value);
            return;
        }

        throw new AddressTranslationException(address);
    }

    public void Write32(uint address, uint value)
    {
        if (TryTranslateMainRam(address, sizeof(uint), out int offset))
        {
            BigEndian.WriteUInt32(_mainRam.AsSpan(offset, sizeof(uint)), value);
            MainRamStoreObserver?.Invoke(address, sizeof(uint), value);
            return;
        }

        if (TryTranslateMainRamTopGuard(address, sizeof(uint), out int guardOffset))
        {
            BigEndian.WriteUInt32(_mainRamTopGuard.AsSpan(guardOffset, sizeof(uint)), value);
            return;
        }

        throw new AddressTranslationException(address);
    }

    public void Load(uint address, ReadOnlySpan<byte> data)
    {
        if (TryTranslateMainRam(address, data.Length, out int offset))
        {
            data.CopyTo(_mainRam.AsSpan(offset, data.Length));
            MainRamBulkWriteObserver?.Invoke(address, data.Length);
            return;
        }

        if (TryTranslateMainRamTopGuard(address, data.Length, out int guardOffset))
        {
            data.CopyTo(_mainRamTopGuard.AsSpan(guardOffset, data.Length));
            return;
        }

        throw new AddressTranslationException(address);
    }

    public void Clear(uint address, uint size)
    {
        if (size == 0)
        {
            return;
        }

        if (size > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Clear size is too large for the current memory backend.");
        }

        int checkedSize = checked((int)size);
        if (TryTranslateMainRam(address, checkedSize, out int offset))
        {
            _mainRam.AsSpan(offset, checkedSize).Clear();
            MainRamBulkWriteObserver?.Invoke(address, checkedSize);
            return;
        }

        if (TryTranslateMainRamTopGuard(address, checkedSize, out int guardOffset))
        {
            _mainRamTopGuard.AsSpan(guardOffset, checkedSize).Clear();
            return;
        }

        throw new AddressTranslationException(address);
    }

    public bool IsMainRamAddress(uint address, int size = 1)
    {
        return TryTranslateMainRam(address, size, out _) || TryTranslateMainRamTopGuard(address, size, out _);
    }

    public int TranslateMainRam(uint address, int size = 1)
    {
        if (!TryTranslateMainRam(address, size, out int offset))
        {
            throw new AddressTranslationException(address);
        }

        return offset;
    }

    private bool TryTranslateMainRam(uint address, int size, out int offset)
    {
        if (!GameCubeAddress.TryTranslateMainRam(address, out offset))
        {
            return false;
        }

        return size >= 0 && offset <= _mainRam.Length - size;
    }

    private bool TryTranslateMainRamTopGuard(uint address, int size, out int offset)
    {
        offset = 0;
        if (!EnableMainRamTopGuard || size < 0)
        {
            return false;
        }

        uint? guardStart = address switch
        {
            >= GameCubeAddress.MainRamPhysicalEnd + 1u and < GameCubeAddress.MainRamPhysicalEnd + 1u + MainRamTopGuardSize => GameCubeAddress.MainRamPhysicalEnd + 1u,
            >= GameCubeAddress.MainRamCachedEnd + 1u and < GameCubeAddress.MainRamCachedEnd + 1u + MainRamTopGuardSize => GameCubeAddress.MainRamCachedEnd + 1u,
            >= GameCubeAddress.MainRamUncachedEnd + 1u and < GameCubeAddress.MainRamUncachedEnd + 1u + MainRamTopGuardSize => GameCubeAddress.MainRamUncachedEnd + 1u,
            _ => null,
        };

        if (guardStart is not uint start)
        {
            return false;
        }

        uint relative = address - start;
        if ((ulong)relative + (uint)size > MainRamTopGuardSize)
        {
            return false;
        }

        offset = checked((int)relative);
        return true;
    }
}
