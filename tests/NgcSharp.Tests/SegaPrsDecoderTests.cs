using NgcSharp.App;

namespace NgcSharp.Tests;

public sealed class SegaPrsDecoderTests
{
    [Fact]
    public void DecodesLiteralRunAndTerminator()
    {
        byte[] source = [0x17, (byte)'A', (byte)'B', (byte)'C', 0x00, 0x00];

        bool decoded = SegaPrsDecoder.TryDecode(source, out SegaPrsDecodeResult result, out string? failure);

        Assert.True(decoded, failure);
        Assert.Equal([0x41, 0x42, 0x43], result.Output);
        Assert.Equal(source.Length, result.SourceBytesConsumed);
        Assert.Equal(0, result.LastFlagByte);
        Assert.Equal(3, result.BitsRemaining);
    }

    [Fact]
    public void DecodesCapturedSonicBridgeBufferWhenAvailable()
    {
        string sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "compat-runs",
            "20260529-sonic-prs-replay-bridge",
            "source-80fffe60-wide.bin");

        if (!File.Exists(sourcePath))
        {
            return;
        }

        bool decoded = SegaPrsDecoder.TryDecode(File.ReadAllBytes(sourcePath), out SegaPrsDecodeResult result, out string? failure);

        Assert.True(decoded, failure);
        Assert.Equal(0x108DA5, result.Output.Length);
        Assert.Equal(0xA2821, result.SourceBytesConsumed);
        Assert.Equal("81317DA481317674397FDA40431A4168B97FDA404598B0A000000016813184D0", Convert.ToHexString(result.Output.AsSpan(0xB8670, 0x20)));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
