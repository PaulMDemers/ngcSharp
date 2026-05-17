using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace NgcSharp.App;

public sealed record ImageBounds(int MinX, int MinY, int MaxX, int MaxY)
{
    public override string ToString() => $"{MinX},{MinY}-{MaxX},{MaxY}";
}

public sealed record ImageStatistics(int NonBlackPixels, ImageBounds? NonBlackBounds, double AverageNonBlackR, double AverageNonBlackG, double AverageNonBlackB);

public sealed record ImageComparisonResult(
    string BaselinePath,
    string CandidatePath,
    string? DiffPath,
    int Width,
    int Height,
    ImageStatistics Baseline,
    ImageStatistics Candidate,
    int ChangedPixels,
    double ChangedPercent,
    double AverageDeltaWhenChanged,
    int MaxDelta);

public static class ImageComparison
{
    public static ImageComparisonResult Compare(string baselinePath, string candidatePath, string? diffPath = null)
    {
        PngRgbImage baseline = PngRgbImage.Load(baselinePath);
        PngRgbImage candidate = PngRgbImage.Load(candidatePath);
        if (baseline.Width != candidate.Width || baseline.Height != candidate.Height)
        {
            throw new InvalidOperationException($"Image dimensions differ: baseline is {baseline.Width}x{baseline.Height}, candidate is {candidate.Width}x{candidate.Height}.");
        }

        int changedPixels = 0;
        long deltaTotal = 0;
        int maxDelta = 0;
        byte[]? diffRgb = diffPath is null ? null : new byte[baseline.Rgb.Length];
        for (int offset = 0; offset < baseline.Rgb.Length; offset += 3)
        {
            int dr = Math.Abs(baseline.Rgb[offset] - candidate.Rgb[offset]);
            int dg = Math.Abs(baseline.Rgb[offset + 1] - candidate.Rgb[offset + 1]);
            int db = Math.Abs(baseline.Rgb[offset + 2] - candidate.Rgb[offset + 2]);
            int delta = dr + dg + db;
            if (delta != 0)
            {
                changedPixels++;
                deltaTotal += delta;
                maxDelta = Math.Max(maxDelta, delta);
            }

            if (diffRgb is not null)
            {
                byte value = (byte)Math.Min(255, delta);
                diffRgb[offset] = value;
                diffRgb[offset + 1] = value;
                diffRgb[offset + 2] = value;
            }
        }

        string? fullDiffPath = null;
        if (diffPath is not null && diffRgb is not null)
        {
            fullDiffPath = Path.GetFullPath(diffPath);
            string? directory = Path.GetDirectoryName(fullDiffPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            FramebufferDumper.WriteRgbPng(fullDiffPath, baseline.Width, baseline.Height, diffRgb);
        }

        int pixels = checked(baseline.Width * baseline.Height);
        return new ImageComparisonResult(
            Path.GetFullPath(baselinePath),
            Path.GetFullPath(candidatePath),
            fullDiffPath,
            baseline.Width,
            baseline.Height,
            ComputeStatistics(baseline),
            ComputeStatistics(candidate),
            changedPixels,
            pixels == 0 ? 0 : changedPixels * 100.0 / pixels,
            changedPixels == 0 ? 0 : deltaTotal / (double)changedPixels,
            maxDelta);
    }

    public static void WriteReport(TextWriter writer, ImageComparisonResult result)
    {
        writer.WriteLine($"Baseline:  {result.BaselinePath}");
        writer.WriteLine($"Candidate: {result.CandidatePath}");
        if (result.DiffPath is not null)
        {
            writer.WriteLine($"Diff:      {result.DiffPath}");
        }

        writer.WriteLine($"Size:      {result.Width}x{result.Height}");
        writer.WriteLine($"Changed:   {result.ChangedPixels} pixel(s) ({result.ChangedPercent.ToString("0.###", CultureInfo.InvariantCulture)}%)");
        writer.WriteLine($"Delta:     avg {result.AverageDeltaWhenChanged.ToString("0.###", CultureInfo.InvariantCulture)} when changed, max {result.MaxDelta}");
        WriteStatistics(writer, "Baseline nonblack", result.Baseline, result.Width, result.Height);
        WriteStatistics(writer, "Candidate nonblack", result.Candidate, result.Width, result.Height);
    }

    private static void WriteStatistics(TextWriter writer, string label, ImageStatistics statistics, int width, int height)
    {
        double percent = checked(width * height) == 0 ? 0 : statistics.NonBlackPixels * 100.0 / checked(width * height);
        string bounds = statistics.NonBlackBounds?.ToString() ?? "none";
        writer.WriteLine($"{label}: {statistics.NonBlackPixels} pixel(s) ({percent.ToString("0.###", CultureInfo.InvariantCulture)}%), bounds {bounds}, avg RGB {statistics.AverageNonBlackR.ToString("0.#", CultureInfo.InvariantCulture)}/{statistics.AverageNonBlackG.ToString("0.#", CultureInfo.InvariantCulture)}/{statistics.AverageNonBlackB.ToString("0.#", CultureInfo.InvariantCulture)}");
    }

    private static ImageStatistics ComputeStatistics(PngRgbImage image)
    {
        int nonBlack = 0;
        int minX = image.Width;
        int minY = image.Height;
        int maxX = -1;
        int maxY = -1;
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        for (int y = 0; y < image.Height; y++)
        {
            int rowOffset = y * image.Width * 3;
            for (int x = 0; x < image.Width; x++)
            {
                int offset = rowOffset + x * 3;
                byte r = image.Rgb[offset];
                byte g = image.Rgb[offset + 1];
                byte b = image.Rgb[offset + 2];
                if (r == 0 && g == 0 && b == 0)
                {
                    continue;
                }

                nonBlack++;
                sumR += r;
                sumG += g;
                sumB += b;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return new ImageStatistics(
            nonBlack,
            nonBlack == 0 ? null : new ImageBounds(minX, minY, maxX, maxY),
            nonBlack == 0 ? 0 : sumR / (double)nonBlack,
            nonBlack == 0 ? 0 : sumG / (double)nonBlack,
            nonBlack == 0 ? 0 : sumB / (double)nonBlack);
    }

    private sealed record PngRgbImage(int Width, int Height, byte[] Rgb)
    {
        private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

        public static PngRgbImage Load(string path)
        {
            byte[] png = File.ReadAllBytes(path);
            if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(PngSignature))
            {
                throw new InvalidOperationException($"{path} is not a PNG file.");
            }

            int width = 0;
            int height = 0;
            using MemoryStream compressed = new();
            int offset = 8;
            while (offset < png.Length)
            {
                if (offset > png.Length - 12)
                {
                    throw new InvalidOperationException($"{path} has a truncated PNG chunk.");
                }

                int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, sizeof(int)));
                if (length < 0 || offset + 12 > png.Length - length)
                {
                    throw new InvalidOperationException($"{path} has an invalid PNG chunk length.");
                }

                ReadOnlySpan<byte> type = png.AsSpan(offset + 4, 4);
                ReadOnlySpan<byte> data = png.AsSpan(offset + 8, length);
                if (type.SequenceEqual("IHDR"u8))
                {
                    if (length != 13)
                    {
                        throw new InvalidOperationException($"{path} has an invalid IHDR chunk.");
                    }

                    width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                    height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                    byte bitDepth = data[8];
                    byte colorType = data[9];
                    byte compression = data[10];
                    byte filter = data[11];
                    byte interlace = data[12];
                    if (width <= 0 || height <= 0 || bitDepth != 8 || colorType != 2 || compression != 0 || filter != 0 || interlace != 0)
                    {
                        throw new InvalidOperationException($"{path} must be an 8-bit non-interlaced RGB PNG.");
                    }
                }
                else if (type.SequenceEqual("IDAT"u8))
                {
                    compressed.Write(data);
                }
                else if (type.SequenceEqual("IEND"u8))
                {
                    break;
                }

                offset += checked(12 + length);
            }

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException($"{path} is missing an IHDR chunk.");
            }

            compressed.Position = 0;
            using ZLibStream zlib = new(compressed, CompressionMode.Decompress);
            using MemoryStream rawStream = new();
            zlib.CopyTo(rawStream);
            byte[] raw = rawStream.ToArray();
            int sourceStride = checked(width * 3 + 1);
            if (raw.Length != checked(height * sourceStride))
            {
                throw new InvalidOperationException($"{path} has unexpected decompressed PNG data length.");
            }

            byte[] rgb = new byte[checked(width * height * 3)];
            int rowByteCount = checked(width * 3);
            for (int row = 0; row < height; row++)
            {
                int sourceOffset = row * sourceStride;
                int destinationOffset = row * rowByteCount;
                UnfilterScanline(path, raw[sourceOffset], raw.AsSpan(sourceOffset + 1, rowByteCount), rgb.AsSpan(destinationOffset, rowByteCount), row == 0 ? ReadOnlySpan<byte>.Empty : rgb.AsSpan(destinationOffset - rowByteCount, rowByteCount));
            }

            return new PngRgbImage(width, height, rgb);
        }

        private static void UnfilterScanline(string path, byte filterType, ReadOnlySpan<byte> encoded, Span<byte> decoded, ReadOnlySpan<byte> previous)
        {
            const int bytesPerPixel = 3;
            switch (filterType)
            {
                case 0:
                    encoded.CopyTo(decoded);
                    break;
                case 1:
                    for (int index = 0; index < encoded.Length; index++)
                    {
                        int left = index >= bytesPerPixel ? decoded[index - bytesPerPixel] : 0;
                        decoded[index] = unchecked((byte)(encoded[index] + left));
                    }

                    break;
                case 2:
                    for (int index = 0; index < encoded.Length; index++)
                    {
                        int up = previous.IsEmpty ? 0 : previous[index];
                        decoded[index] = unchecked((byte)(encoded[index] + up));
                    }

                    break;
                case 3:
                    for (int index = 0; index < encoded.Length; index++)
                    {
                        int left = index >= bytesPerPixel ? decoded[index - bytesPerPixel] : 0;
                        int up = previous.IsEmpty ? 0 : previous[index];
                        decoded[index] = unchecked((byte)(encoded[index] + ((left + up) >> 1)));
                    }

                    break;
                case 4:
                    for (int index = 0; index < encoded.Length; index++)
                    {
                        int left = index >= bytesPerPixel ? decoded[index - bytesPerPixel] : 0;
                        int up = previous.IsEmpty ? 0 : previous[index];
                        int upperLeft = index >= bytesPerPixel && !previous.IsEmpty ? previous[index - bytesPerPixel] : 0;
                        decoded[index] = unchecked((byte)(encoded[index] + PaethPredictor(left, up, upperLeft)));
                    }

                    break;
                default:
                    throw new InvalidOperationException($"{path} uses invalid PNG filter type {filterType}.");
            }
        }

        private static int PaethPredictor(int left, int up, int upperLeft)
        {
            int prediction = left + up - upperLeft;
            int leftDistance = Math.Abs(prediction - left);
            int upDistance = Math.Abs(prediction - up);
            int upperLeftDistance = Math.Abs(prediction - upperLeft);
            if (leftDistance <= upDistance && leftDistance <= upperLeftDistance)
            {
                return left;
            }

            return upDistance <= upperLeftDistance ? up : upperLeft;
        }
    }
}
