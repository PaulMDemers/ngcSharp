using NgcSharp.App;
using System.Buffers.Binary;
using System.IO.Compression;

namespace NgcSharp.Tests;

public sealed class ImageComparisonTests
{
    [Fact]
    public void ComparesRgbPngsAndWritesDiff()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string baselinePath = Path.Combine(directory, "baseline.png");
        string candidatePath = Path.Combine(directory, "candidate.png");
        string diffPath = Path.Combine(directory, "diff.png");
        try
        {
            byte[] baseline =
            [
                0, 0, 0,
                255, 0, 0,
                0, 0, 0,
                0, 0, 255,
            ];
            byte[] candidate =
            [
                0, 0, 0,
                255, 0, 0,
                0, 255, 0,
                0, 0, 128,
            ];
            FramebufferDumper.WriteRgbPng(baselinePath, width: 2, height: 2, baseline);
            FramebufferDumper.WriteRgbPng(candidatePath, width: 2, height: 2, candidate);

            ImageComparisonResult result = ImageComparison.Compare(baselinePath, candidatePath, diffPath);

            Assert.Equal(2, result.Width);
            Assert.Equal(2, result.Height);
            Assert.Equal(2, result.ChangedPixels);
            Assert.Equal(50, result.ChangedPercent);
            Assert.Equal(255 + 127, result.AverageDeltaWhenChanged * result.ChangedPixels, precision: 6);
            Assert.Equal(255, result.MaxDelta);
            Assert.Equal(2, result.Baseline.NonBlackPixels);
            Assert.Equal(new ImageBounds(1, 0, 1, 1), result.Baseline.NonBlackBounds);
            Assert.Equal(3, result.Candidate.NonBlackPixels);
            Assert.Equal(new ImageBounds(0, 0, 1, 1), result.Candidate.NonBlackBounds);
            Assert.True(File.Exists(diffPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsDifferentDimensions()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string baselinePath = Path.Combine(directory, "baseline.png");
        string candidatePath = Path.Combine(directory, "candidate.png");
        try
        {
            FramebufferDumper.WriteRgbPng(baselinePath, width: 1, height: 1, [0, 0, 0]);
            FramebufferDumper.WriteRgbPng(candidatePath, width: 2, height: 1, [0, 0, 0, 0, 0, 0]);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => ImageComparison.Compare(baselinePath, candidatePath));

            Assert.Contains("dimensions differ", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ReadsStandardPngFilters(byte filterType)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string baselinePath = Path.Combine(directory, "baseline.png");
        string candidatePath = Path.Combine(directory, "candidate.png");
        try
        {
            byte[] rgb =
            [
                10, 20, 30,
                20, 40, 60,
                7, 9, 11,
                70, 80, 90,
            ];
            WriteFilteredRgbPng(baselinePath, width: 2, height: 2, rgb, filterType);
            FramebufferDumper.WriteRgbPng(candidatePath, width: 2, height: 2, rgb);

            ImageComparisonResult result = ImageComparison.Compare(baselinePath, candidatePath);

            Assert.Equal(0, result.ChangedPixels);
            Assert.Equal(4, result.Baseline.NonBlackPixels);
            Assert.Equal(new ImageBounds(0, 0, 1, 1), result.Baseline.NonBlackBounds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteFilteredRgbPng(string path, int width, int height, byte[] rgb, byte filterType)
    {
        const int bytesPerPixel = 3;
        int rowBytes = checked(width * bytesPerPixel);
        byte[] raw = new byte[checked(height * (rowBytes + 1))];
        for (int row = 0; row < height; row++)
        {
            int rawOffset = row * (rowBytes + 1);
            int rgbOffset = row * rowBytes;
            raw[rawOffset] = filterType;
            for (int index = 0; index < rowBytes; index++)
            {
                int value = rgb[rgbOffset + index];
                int left = index >= bytesPerPixel ? rgb[rgbOffset + index - bytesPerPixel] : 0;
                int up = row == 0 ? 0 : rgb[rgbOffset - rowBytes + index];
                int upperLeft = row != 0 && index >= bytesPerPixel ? rgb[rgbOffset - rowBytes + index - bytesPerPixel] : 0;
                int predictor = filterType switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => PaethPredictor(left, up, upperLeft),
                    _ => throw new ArgumentOutOfRangeException(nameof(filterType)),
                };
                raw[rawOffset + 1 + index] = unchecked((byte)(value - predictor));
            }
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using FileStream file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        WritePngChunk(file, "IHDR"u8, ihdr);
        WritePngChunk(file, "IDAT"u8, compressed.ToArray());
        WritePngChunk(file, "IEND"u8, []);
    }

    private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);
        stream.Write(stackalloc byte[4]);
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
