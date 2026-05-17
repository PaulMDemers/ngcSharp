using System.Buffers.Binary;
using System.IO.Compression;
using NgcSharp.Core;

namespace NgcSharp.App;

public enum FramebufferPixelFormat
{
    Rgb565,
    Yuyv,
    Uyvy,
    Xrgb8888,
}

public sealed record FramebufferDumpResult(string Path, uint Address, int Width, int Height, FramebufferPixelFormat Format);

public static class FramebufferPixelFormatParser
{
    public static bool TryParse(string text, out FramebufferPixelFormat format)
    {
        switch (text.ToLowerInvariant())
        {
            case "rgb565":
                format = FramebufferPixelFormat.Rgb565;
                return true;
            case "yuyv":
                format = FramebufferPixelFormat.Yuyv;
                return true;
            case "uyvy":
                format = FramebufferPixelFormat.Uyvy;
                return true;
            case "xrgb8888":
                format = FramebufferPixelFormat.Xrgb8888;
                return true;
            default:
                format = default;
                return false;
        }
    }
}

public static class FramebufferDumper
{
    private const int MaxFrameDumpPixels = 4_194_304;

    private static readonly (uint High, uint Low)[] ViFramebufferRegisterPairs =
    [
        (0xCC00_2020, 0xCC00_2022),
        (0xCC00_201C, 0xCC00_201E),
    ];

    private static readonly uint[] ViFramebufferRegisters =
    [
        0xCC00_201C,
        0xCC00_2020,
        0xCC00_2024,
        0xCC00_2028,
    ];

    public static bool TryDump(GameCubeBus bus, RunDolOptions options, out FramebufferDumpResult? result, out string? error)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(options);

        result = null;
        error = null;

        if (options.FrameDumpPath is null)
        {
            error = "no frame dump path was provided.";
            return false;
        }

        int width = options.FrameWidth ?? 640;
        int height = options.FrameHeight ?? 480;
        if (width <= 0 || height <= 0)
        {
            error = "frame dimensions must be positive.";
            return false;
        }

        if ((long)width * height > MaxFrameDumpPixels)
        {
            error = $"frame dump is too large ({width}x{height}); maximum is {MaxFrameDumpPixels} pixels.";
            return false;
        }

        if (!TryResolveAddress(bus, options.FrameAddress, out uint address))
        {
            error = "no framebuffer address was provided and no VI framebuffer register has been written.";
            return false;
        }

        byte[] rgb;
        try
        {
            rgb = CaptureRgb(bus.Memory, address, width, height, options.FrameFormat);
        }
        catch (AddressTranslationException)
        {
            error = $"framebuffer range starting at 0x{address:X8} is outside emulated main RAM.";
            return false;
        }

        string fullPath = Path.GetFullPath(options.FrameDumpPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        WriteRgbPng(fullPath, width, height, rgb);
        result = new FramebufferDumpResult(fullPath, address, width, height, options.FrameFormat);
        return true;
    }

    public static byte[] CaptureRgb(GameCubeMemory memory, uint address, int width, int height, FramebufferPixelFormat format)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((long)width * height > MaxFrameDumpPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"frame dump is too large ({width}x{height}); maximum is {MaxFrameDumpPixels} pixels.");
        }

        int bytesPerRow = BytesPerSourceRow(width, format);
        int sourceLength = checked(bytesPerRow * height);
        int sourceOffset = memory.TranslateMainRam(address, sourceLength);
        ReadOnlySpan<byte> source = memory.MainRam.Span.Slice(sourceOffset, sourceLength);
        byte[] rgb = new byte[checked(width * height * 3)];

        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> sourceRow = source.Slice(y * bytesPerRow, bytesPerRow);
            Span<byte> rgbRow = rgb.AsSpan(y * width * 3, width * 3);
            DecodeRow(sourceRow, rgbRow, width, format);
        }

        return rgb;
    }

    private static bool TryResolveAddress(GameCubeBus bus, uint? explicitAddress, out uint address)
    {
        if (explicitAddress is uint provided)
        {
            address = provided;
            return true;
        }

        uint bestSplitAddress = 0;
        foreach ((uint highRegister, uint lowRegister) in ViFramebufferRegisterPairs)
        {
            if (bus.TryGetMmioValue(highRegister, out uint highValue)
                && bus.TryGetMmioValue(lowRegister, out uint lowValue)
                && TryNormalizeVideoInterfaceAddress(CombineVideoInterfaceAddress(highValue, lowValue), preferShifted: false, out address))
            {
                bestSplitAddress = Math.Max(bestSplitAddress, address);
            }
        }

        if (bestSplitAddress != 0)
        {
            address = bestSplitAddress;
            return true;
        }

        foreach (uint register in ViFramebufferRegisters)
        {
            if (bus.TryGetMmioValue(register, out uint value) && TryNormalizeVideoInterfaceAddress(value, preferShifted: true, out address))
            {
                return true;
            }
        }

        address = 0;
        return false;
    }

    private static uint CombineVideoInterfaceAddress(uint highValue, uint lowValue)
    {
        return ((highValue & 0xFF) << 16) | (lowValue & 0xFFFF);
    }

    private static bool TryNormalizeVideoInterfaceAddress(uint value, bool preferShifted, out uint address)
    {
        uint shifted = (value & 0x00FF_FFFF) << 5;
        if (preferShifted && shifted != value && GameCubeAddress.TryTranslateMainRam(shifted, out _))
        {
            address = shifted;
            return shifted != 0;
        }

        if (GameCubeAddress.TryTranslateMainRam(value, out _))
        {
            address = value;
            return value != 0;
        }

        if (!preferShifted && shifted != value && GameCubeAddress.TryTranslateMainRam(shifted, out _))
        {
            address = shifted;
            return shifted != 0;
        }

        address = 0;
        return false;
    }

    private static int BytesPerSourceRow(int width, FramebufferPixelFormat format)
    {
        return format switch
        {
            FramebufferPixelFormat.Rgb565 => checked(width * 2),
            FramebufferPixelFormat.Yuyv or FramebufferPixelFormat.Uyvy => checked(((width + 1) / 2) * 4),
            FramebufferPixelFormat.Xrgb8888 => checked(width * 4),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static void DecodeRow(ReadOnlySpan<byte> source, Span<byte> rgb, int width, FramebufferPixelFormat format)
    {
        switch (format)
        {
            case FramebufferPixelFormat.Rgb565:
                DecodeRgb565Row(source, rgb, width);
                break;
            case FramebufferPixelFormat.Yuyv:
                DecodeYuv422Row(source, rgb, width, yFirst: true);
                break;
            case FramebufferPixelFormat.Uyvy:
                DecodeYuv422Row(source, rgb, width, yFirst: false);
                break;
            case FramebufferPixelFormat.Xrgb8888:
                DecodeXrgb8888Row(source, rgb, width);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void DecodeRgb565Row(ReadOnlySpan<byte> source, Span<byte> rgb, int width)
    {
        for (int x = 0; x < width; x++)
        {
            ushort pixel = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(x * 2, 2));
            rgb[x * 3] = Expand5((pixel >> 11) & 0x1F);
            rgb[x * 3 + 1] = Expand6((pixel >> 5) & 0x3F);
            rgb[x * 3 + 2] = Expand5(pixel & 0x1F);
        }
    }

    private static void DecodeXrgb8888Row(ReadOnlySpan<byte> source, Span<byte> rgb, int width)
    {
        for (int x = 0; x < width; x++)
        {
            int sourceIndex = x * 4;
            int rgbIndex = x * 3;
            rgb[rgbIndex] = source[sourceIndex + 1];
            rgb[rgbIndex + 1] = source[sourceIndex + 2];
            rgb[rgbIndex + 2] = source[sourceIndex + 3];
        }
    }

    private static void DecodeYuv422Row(ReadOnlySpan<byte> source, Span<byte> rgb, int width, bool yFirst)
    {
        for (int x = 0; x < width; x += 2)
        {
            int sourceIndex = (x / 2) * 4;
            byte y0;
            byte y1;
            byte cb;
            byte cr;

            if (yFirst)
            {
                y0 = source[sourceIndex];
                cb = source[sourceIndex + 1];
                y1 = source[sourceIndex + 2];
                cr = source[sourceIndex + 3];
            }
            else
            {
                cb = source[sourceIndex];
                y0 = source[sourceIndex + 1];
                cr = source[sourceIndex + 2];
                y1 = source[sourceIndex + 3];
            }

            WriteYcbcrPixel(rgb, x, y0, cb, cr);
            if (x + 1 < width)
            {
                WriteYcbcrPixel(rgb, x + 1, y1, cb, cr);
            }
        }
    }

    private static void WriteYcbcrPixel(Span<byte> rgb, int x, byte y, byte cb, byte cr)
    {
        int c = y - 16;
        int d = cb - 128;
        int e = cr - 128;
        int index = x * 3;
        rgb[index] = Clamp8((298 * c + 409 * e + 128) >> 8);
        rgb[index + 1] = Clamp8((298 * c - 100 * d - 208 * e + 128) >> 8);
        rgb[index + 2] = Clamp8((298 * c + 516 * d + 128) >> 8);
    }

    public static void WriteRgbPng(string path, int width, int height, ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length != checked(width * height * 3))
        {
            throw new ArgumentException("RGB buffer length does not match the requested dimensions.", nameof(rgb));
        }

        using FileStream file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        WriteChunk(file, "IHDR"u8, ihdr);

        using MemoryStream idat = new();
        using (ZLibStream zlib = new(idat, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgb.Slice(y * width * 3, width * 3));
            }
        }

        WriteChunk(file, "IDAT"u8, idat.ToArray());
        WriteChunk(file, "IEND"u8, []);
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], data.Length);
        type.CopyTo(header[4..]);
        stream.Write(header);
        stream.Write(data);

        Span<byte> crcBytes = stackalloc byte[4];
        uint crc = Crc32(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFF_FFFF;
        crc = UpdateCrc32(crc, type);
        crc = UpdateCrc32(crc, data);
        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB8_8320 : crc >> 1;
            }
        }

        return crc;
    }

    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));

    private static byte Expand6(int value) => (byte)((value << 2) | (value >> 4));

    private static byte Clamp8(int value) => (byte)Math.Clamp(value, 0, 255);
}
