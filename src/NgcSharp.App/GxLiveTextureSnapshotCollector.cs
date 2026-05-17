using NgcSharp.Core;

namespace NgcSharp.App;

public sealed class GxLiveTextureSnapshotCollector
{
    private readonly GxTextureSnapshotCapturePlan _plan;
    private readonly List<byte> _fifo = [];
    private readonly uint[] _vcdLow = new uint[8];
    private readonly uint[] _vcdHigh = new uint[8];
    private readonly uint[] _vatA = new uint[8];
    private readonly uint[] _vatB = new uint[8];
    private readonly uint[] _vatC = new uint[8];
    private readonly GxTextureState[] _textures = Enumerable.Range(0, 8).Select(_ => new GxTextureState()).ToArray();
    private readonly Dictionary<int, GxTlutLoadState> _tlutLoads = [];
    private readonly List<GxMemorySnapshot> _snapshots = [];
    private int _parseOffset;
    private int _draws;
    private uint? _pendingTlutSourceAddress;

    public GxLiveTextureSnapshotCollector(GxTextureSnapshotCapturePlan plan)
    {
        _plan = plan;
    }

    public IReadOnlyList<GxMemorySnapshot> Snapshots => _snapshots;

    public int DrawsSeen => _draws;

    public static GxLiveTextureSnapshotCollector? Create(RunDolOptions options)
    {
        if (options.GxDisableAutoTextureSnapshots)
        {
            return null;
        }

        GxTextureSnapshotCapturePlan plan = GxTextureSnapshotCapturePlan.FromOptions(options);
        return plan.IsEmpty ? null : new GxLiveTextureSnapshotCollector(plan);
    }

    public static GxLiveTextureSnapshotCollector CreateForDrawWindow(int skipDraws, int maxDraws) =>
        new(new GxTextureSnapshotCapturePlan([new GxTextureSnapshotCaptureRange(skipDraws + 1, skipDraws + maxDraws)]));

    public void Feed(MmioAccess access, GameCubeMemory memory)
    {
        if (access.Kind != MmioAccessKind.Write || access.DeviceName != "GX FIFO")
        {
            return;
        }

        for (int shift = (access.Width - 1) * 8; shift >= 0; shift -= 8)
        {
            _fifo.Add((byte)(access.Value >> shift));
        }

        ParseAvailable(memory);
    }

    private void ParseAvailable(GameCubeMemory memory)
    {
        while (_parseOffset < _fifo.Count)
        {
            int commandOffset = _parseOffset;
            byte command = _fifo[_parseOffset++];
            if (command is 0x00 or 0x44 or 0x48)
            {
                continue;
            }

            if (command == 0x08)
            {
                if (!TryReadBigEndian(_fifo, _parseOffset, 1, out uint cpRegister)
                    || !TryReadBigEndian(_fifo, _parseOffset + 1, 4, out uint cpValue))
                {
                    _parseOffset = commandOffset;
                    return;
                }

                WriteCpRegister((byte)cpRegister, cpValue);
                _parseOffset += 5;
                continue;
            }

            if (command == 0x10)
            {
                if (!TryReadBigEndian(_fifo, _parseOffset, 4, out uint xfHeader))
                {
                    _parseOffset = commandOffset;
                    return;
                }

                uint wordCount = ((xfHeader >> 16) & 0xFFFF) + 1;
                int xfPayloadBytes = checked((int)wordCount * sizeof(uint));
                if (_parseOffset + 4 > _fifo.Count - xfPayloadBytes)
                {
                    _parseOffset = commandOffset;
                    return;
                }

                _parseOffset += 4 + xfPayloadBytes;
                continue;
            }

            if (command is 0x20 or 0x28 or 0x30 or 0x38)
            {
                if (_parseOffset > _fifo.Count - sizeof(uint))
                {
                    _parseOffset = commandOffset;
                    return;
                }

                _parseOffset += sizeof(uint);
                continue;
            }

            if (command == 0x40)
            {
                if (_parseOffset > _fifo.Count - sizeof(ulong))
                {
                    _parseOffset = commandOffset;
                    return;
                }

                _parseOffset += sizeof(ulong);
                continue;
            }

            if (command == 0x61)
            {
                if (!TryReadBigEndian(_fifo, _parseOffset, 4, out uint bpValue))
                {
                    _parseOffset = commandOffset;
                    return;
                }

                WriteBpRegister(bpValue);
                _parseOffset += sizeof(uint);
                continue;
            }

            if ((command & 0x80) == 0 || !TryReadBigEndian(_fifo, _parseOffset, 2, out uint vertexCount))
            {
                _parseOffset = commandOffset;
                return;
            }

            int format = command & 7;
            if (!TryGetVertexStride(format, out int stride))
            {
                _parseOffset = commandOffset;
                return;
            }

            long payloadBytesLong = (long)vertexCount * stride;
            if (payloadBytesLong > int.MaxValue)
            {
                _parseOffset = commandOffset;
                return;
            }

            int payloadBytes = (int)payloadBytesLong;
            if (_parseOffset + 2 > _fifo.Count - payloadBytes)
            {
                _parseOffset = commandOffset;
                return;
            }

            _draws++;
            if (_plan.ShouldCaptureDraw(_draws))
            {
                CaptureActiveTextures(memory, commandOffset);
            }

            _parseOffset += 2 + payloadBytes;
        }
    }

    private void CaptureActiveTextures(GameCubeMemory memory, int fifoOffset)
    {
        HashSet<(uint Address, int Length)> captured = [];
        for (int slot = 0; slot < _textures.Length; slot++)
        {
            GxTextureState texture = _textures[slot];
            if (!texture.HasImage0 || !texture.HasImage3 || texture.Width <= 0 || texture.Height <= 0)
            {
                continue;
            }

            CaptureRange(memory, fifoOffset, texture.SourceAddress, TextureSourceByteLength(texture.Width, texture.Height, texture.Format), captured);
            if (texture.HasTlut && _tlutLoads.TryGetValue(texture.TlutBaseIndex, out GxTlutLoadState tlut))
            {
                CaptureRange(memory, fifoOffset, tlut.SourceAddress, checked(tlut.Entries * sizeof(ushort)), captured);
            }
        }
    }

    private void CaptureRange(GameCubeMemory memory, int fifoOffset, uint address, int length, HashSet<(uint Address, int Length)> captured)
    {
        if (length <= 0 || !captured.Add((address, length)))
        {
            return;
        }

        try
        {
            byte[] bytes = new byte[length];
            for (int offset = 0; offset < bytes.Length; offset++)
            {
                bytes[offset] = memory.Read8(address + (uint)offset);
            }

            _snapshots.Add(new GxMemorySnapshot(fifoOffset, address, bytes));
        }
        catch (AddressTranslationException)
        {
        }
    }

    private void WriteCpRegister(byte register, uint value)
    {
        if (register is >= 0x50 and <= 0x57)
        {
            _vcdLow[register - 0x50] = value;
        }
        else if (register is >= 0x60 and <= 0x67)
        {
            _vcdHigh[register - 0x60] = value;
        }
        else if (register is >= 0x70 and <= 0x77)
        {
            _vatA[register - 0x70] = value;
        }
        else if (register is >= 0x80 and <= 0x87)
        {
            _vatB[register - 0x80] = value;
        }
        else if (register is >= 0x90 and <= 0x97)
        {
            _vatC[register - 0x90] = value;
        }
    }

    private void WriteBpRegister(uint value)
    {
        byte register = (byte)(value >> 24);
        uint payload = value & 0x00FF_FFFF;
        WriteTlutLoadRegister(register, payload);
        WriteTextureRegister(register, payload);
    }

    private void WriteTlutLoadRegister(byte register, uint value)
    {
        if (register == 0x64)
        {
            _pendingTlutSourceAddress = (value & 0x00FF_FFFF) << 5;
            return;
        }

        if (register != 0x65 || _pendingTlutSourceAddress is not { } sourceAddress)
        {
            return;
        }

        int baseIndex = (int)(value & 0x3FF);
        int entries = (int)((value >> 10) & 0x3FF) * 16;
        _tlutLoads[baseIndex] = new GxTlutLoadState(sourceAddress, entries);
        _pendingTlutSourceAddress = null;
    }

    private void WriteTextureRegister(byte register, uint value)
    {
        if (TryGetTextureSlot(register, TextureRegisterKind.Mode0, out int mode0Slot))
        {
            _textures[mode0Slot].HasMode0 = true;
            _textures[mode0Slot].Mode0 = value;
            return;
        }

        if (TryGetTextureSlot(register, TextureRegisterKind.Mode1, out int mode1Slot))
        {
            _textures[mode1Slot].HasMode1 = true;
            _textures[mode1Slot].Mode1 = value;
            return;
        }

        if (TryGetTextureSlot(register, TextureRegisterKind.Image0, out int image0Slot))
        {
            GxTextureState texture = _textures[image0Slot];
            texture.HasImage0 = true;
            texture.Image0 = value;
            texture.Width = (int)(value & 0x3FF) + 1;
            texture.Height = (int)((value >> 10) & 0x3FF) + 1;
            texture.Format = (int)((value >> 20) & 0xF);
            return;
        }

        if (TryGetTextureSlot(register, TextureRegisterKind.Image1, out int image1Slot))
        {
            GxTextureState texture = _textures[image1Slot];
            texture.HasImage1 = true;
            texture.Image1 = value;
            texture.TmemEven = value & 0x7FFF;
            return;
        }

        if (TryGetTextureSlot(register, TextureRegisterKind.Image2, out int image2Slot))
        {
            GxTextureState texture = _textures[image2Slot];
            texture.HasImage2 = true;
            texture.Image2 = value;
            texture.TmemOdd = value & 0x7FFF;
            return;
        }

        if (TryGetTextureSlot(register, TextureRegisterKind.Tlut, out int tlutSlot))
        {
            GxTextureState texture = _textures[tlutSlot];
            texture.HasTlut = true;
            texture.Tlut = value;
            texture.TlutBaseIndex = (int)(value & 0x3FF);
            return;
        }

        if (TryGetTextureSlot(register, TextureRegisterKind.Image3, out int image3Slot))
        {
            GxTextureState texture = _textures[image3Slot];
            texture.HasImage3 = true;
            texture.Image3 = value;
            texture.SourceAddress = (value & 0x00FF_FFFF) << 5;
        }
    }

    private bool TryGetVertexStride(int format, out int stride)
    {
        stride = 0;
        uint vcdLow = _vcdLow[format];
        uint vcdHigh = _vcdHigh[format];
        uint vatA = _vatA[format];
        uint vatB = _vatB[format];
        uint vatC = _vatC[format];
        for (int bit = 0; bit < 9; bit++)
        {
            if (((vcdLow >> bit) & 1) != 0)
            {
                stride++;
            }
        }

        return AddAttribute((vcdLow >> 9) & 3, ComponentBytes((vatA >> 1) & 7, (vatA & 1) == 0 ? 2 : 3), ref stride)
            && AddAttribute((vcdLow >> 11) & 3, ComponentBytes((vatA >> 10) & 7, ((vatA >> 9) & 1) == 0 ? 3 : 9), ref stride)
            && AddAttribute((vcdLow >> 13) & 3, ColorBytes((vatA >> 14) & 7), ref stride)
            && AddAttribute((vcdLow >> 15) & 3, ColorBytes((vatA >> 18) & 7), ref stride)
            && AddTex((vcdHigh >> 0) & 3, vatA, 21, 22, ref stride)
            && AddTex((vcdHigh >> 2) & 3, vatB, 0, 1, ref stride)
            && AddTex((vcdHigh >> 4) & 3, vatB, 9, 10, ref stride)
            && AddTex((vcdHigh >> 6) & 3, vatB, 18, 19, ref stride)
            && AddTex((vcdHigh >> 8) & 3, vatB, 27, 28, ref stride)
            && AddTex((vcdHigh >> 10) & 3, vatC, 5, 6, ref stride)
            && AddTex((vcdHigh >> 12) & 3, vatC, 14, 15, ref stride)
            && AddTex((vcdHigh >> 14) & 3, vatC, 23, 24, ref stride);
    }

    private static bool AddTex(uint descriptor, uint vat, int countBit, int formatBit, ref int stride) =>
        AddAttribute(descriptor, ComponentBytes((vat >> formatBit) & 7, ((vat >> countBit) & 1) == 0 ? 1 : 2), ref stride);

    private static bool AddAttribute(uint descriptor, int directBytes, ref int stride)
    {
        switch (descriptor)
        {
            case 0:
                return true;
            case 1 when directBytes > 0:
                stride += directBytes;
                return true;
            case 2:
                stride++;
                return true;
            case 3:
                stride += 2;
                return true;
            default:
                return false;
        }
    }

    private static int TextureSourceByteLength(int width, int height, int format)
    {
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        return format switch
        {
            0 or 8 => checked(((width + 7) / 8) * ((height + 7) / 8) * 32),
            1 or 2 or 9 => checked(((width + 7) / 8) * ((height + 3) / 4) * 32),
            3 or 4 or 5 => checked(((width + 3) / 4) * ((height + 3) / 4) * 32),
            6 => checked(((width + 3) / 4) * ((height + 3) / 4) * 64),
            14 => checked(((width + 7) / 8) * ((height + 7) / 8) * 32),
            _ => 0,
        };
    }

    private static int ComponentBytes(uint format, int components) =>
        format switch
        {
            0 or 1 => components,
            2 or 3 => components * 2,
            4 => components * 4,
            _ => 0,
        };

    private static int ColorBytes(uint format) =>
        format switch
        {
            0 => 2,
            1 => 3,
            2 => 4,
            3 => 2,
            4 => 3,
            5 => 4,
            _ => 0,
        };

    private static bool TryGetTextureSlot(byte register, TextureRegisterKind kind, out int slot)
    {
        ReadOnlySpan<byte> registers = kind switch
        {
            TextureRegisterKind.Mode0 => [0x80, 0x81, 0x82, 0x83, 0xA0, 0xA1, 0xA2, 0xA3],
            TextureRegisterKind.Mode1 => [0x84, 0x85, 0x86, 0x87, 0xA4, 0xA5, 0xA6, 0xA7],
            TextureRegisterKind.Image0 => [0x88, 0x89, 0x8A, 0x8B, 0xA8, 0xA9, 0xAA, 0xAB],
            TextureRegisterKind.Image1 => [0x8C, 0x8D, 0x8E, 0x8F, 0xAC, 0xAD, 0xAE, 0xAF],
            TextureRegisterKind.Image2 => [0x90, 0x91, 0x92, 0x93, 0xB0, 0xB1, 0xB2, 0xB3],
            TextureRegisterKind.Image3 => [0x94, 0x95, 0x96, 0x97, 0xB4, 0xB5, 0xB6, 0xB7],
            TextureRegisterKind.Tlut => [0x98, 0x99, 0x9A, 0x9B, 0xB8, 0xB9, 0xBA, 0xBB],
            _ => [],
        };

        slot = registers.IndexOf(register);
        return slot >= 0;
    }

    private static bool TryReadBigEndian(List<byte> bytes, int offset, int width, out uint value)
    {
        value = 0;
        if (offset < 0 || width < 0 || offset > bytes.Count - width)
        {
            return false;
        }

        for (int index = 0; index < width; index++)
        {
            value = (value << 8) | bytes[offset + index];
        }

        return true;
    }

    private enum TextureRegisterKind
    {
        Mode0,
        Mode1,
        Image0,
        Image1,
        Image2,
        Image3,
        Tlut,
    }

    private readonly record struct GxTlutLoadState(uint SourceAddress, int Entries);

    private sealed class GxTextureState
    {
        public bool HasMode0 { get; set; }
        public bool HasMode1 { get; set; }
        public bool HasImage0 { get; set; }
        public bool HasImage1 { get; set; }
        public bool HasImage2 { get; set; }
        public bool HasImage3 { get; set; }
        public bool HasTlut { get; set; }
        public uint Mode0 { get; set; }
        public uint Mode1 { get; set; }
        public uint Image0 { get; set; }
        public uint Image1 { get; set; }
        public uint Image2 { get; set; }
        public uint Image3 { get; set; }
        public uint Tlut { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Format { get; set; }
        public uint SourceAddress { get; set; }
        public uint TmemEven { get; set; }
        public uint TmemOdd { get; set; }
        public int TlutBaseIndex { get; set; }
    }
}

public sealed record GxTextureSnapshotCaptureRange(int FirstDraw, int LastDraw)
{
    public bool Contains(int draw) => draw >= FirstDraw && draw <= LastDraw;
}

public sealed class GxTextureSnapshotCapturePlan
{
    private readonly GxTextureSnapshotCaptureRange[] _ranges;

    public GxTextureSnapshotCapturePlan(IEnumerable<GxTextureSnapshotCaptureRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        _ranges = ranges
            .Where(range => range.FirstDraw > 0 && range.LastDraw >= range.FirstDraw)
            .OrderBy(range => range.FirstDraw)
            .ToArray();
    }

    public bool IsEmpty => _ranges.Length == 0;

    public static GxTextureSnapshotCapturePlan FromOptions(RunDolOptions options)
    {
        List<GxTextureSnapshotCaptureRange> ranges = [];
        if (options.GxFrameDumpPath is not null)
        {
            int maxDraws = options.GxFrameMaxDraws ?? RunDolOptions.DefaultGxFrameMaxDraws;
            ranges.Add(new GxTextureSnapshotCaptureRange(options.GxFrameSkipDraws + 1, options.GxFrameSkipDraws + maxDraws));
        }

        if (options.GxTextureDumpPath is not null)
        {
            ranges.Add(new GxTextureSnapshotCaptureRange(options.GxDrawSkipDraws + 1, options.GxDrawSkipDraws + options.GxDrawMaxDraws));
        }

        if (options.GxFrameSweep is GxFrameSweepOptions sweep)
        {
            int maxDraws = options.GxFrameMaxDraws ?? RunDolOptions.DefaultGxFrameMaxDraws;
            for (int index = 0; index < sweep.Count; index++)
            {
                long skipDraws = (long)sweep.StartSkipDraws + (long)index * sweep.StepDraws;
                long lastDraw = skipDraws + maxDraws;
                if (skipDraws > int.MaxValue || lastDraw > int.MaxValue)
                {
                    continue;
                }

                ranges.Add(new GxTextureSnapshotCaptureRange((int)skipDraws + 1, (int)lastDraw));
            }
        }

        return new GxTextureSnapshotCapturePlan(ranges);
    }

    public bool ShouldCaptureDraw(int draw)
    {
        for (int index = 0; index < _ranges.Length; index++)
        {
            if (_ranges[index].Contains(draw))
            {
                return true;
            }
        }

        return false;
    }
}
