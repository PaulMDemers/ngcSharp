namespace NgcSharp.App;

public sealed record DiscDiagnosticOptions(
    string Path,
    string OutputDirectory,
    int MaxInstructions,
    string? Name = null,
    int SnapshotInterval = 5_000_000,
    IReadOnlyList<DiagnosticMemoryProbe>? ExtraMemoryProbes = null)
{
    public static bool TryParse(string[] args, out DiscDiagnosticOptions options, TextWriter error)
    {
        options = new DiscDiagnosticOptions(string.Empty, string.Empty, 60_000_000);

        if (args.Length < 2)
        {
            error.WriteLine("Missing disc image path.");
            return false;
        }

        string path = args[1];
        string? outputDirectory = null;
        string? name = null;
        int maxInstructions = 60_000_000;
        int snapshotInterval = 5_000_000;
        List<DiagnosticMemoryProbe> extraMemoryProbes = [];

        for (int index = 2; index < args.Length; index++)
        {
            string arg = args[index];
            switch (arg)
            {
                case "--max-instructions":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out maxInstructions))
                    {
                        error.WriteLine("--max-instructions requires an integer value.");
                        return false;
                    }

                    break;
                case "--out":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--out requires a directory path.");
                        return false;
                    }

                    outputDirectory = args[++index];
                    break;
                case "--name":
                    if (index + 1 >= args.Length)
                    {
                        error.WriteLine("--name requires a short label.");
                        return false;
                    }

                    name = args[++index];
                    break;
                case "--probe-word":
                    if (index + 2 >= args.Length || !TryParseUInt32(args[index + 2], out uint probeAddress))
                    {
                        error.WriteLine("--probe-word requires a name and decimal or 0x-prefixed address.");
                        return false;
                    }

                    extraMemoryProbes.Add(new DiagnosticMemoryProbe(args[++index], probeAddress));
                    index++;
                    break;
                case "--snapshot-interval":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out snapshotInterval))
                    {
                        error.WriteLine("--snapshot-interval requires an integer value.");
                        return false;
                    }

                    break;
                default:
                    error.WriteLine($"Unknown diagnose-disc option '{arg}'.");
                    return false;
            }
        }

        if (maxInstructions < 0)
        {
            error.WriteLine("--max-instructions must be non-negative.");
            return false;
        }

        if (snapshotInterval < 0)
        {
            error.WriteLine("--snapshot-interval must be non-negative.");
            return false;
        }

        outputDirectory ??= System.IO.Path.Combine("artifacts", "diagnostics", BuildDefaultName(path, maxInstructions));
        options = new DiscDiagnosticOptions(path, outputDirectory, maxInstructions, name, snapshotInterval, extraMemoryProbes);
        return true;
    }

    private static string BuildDefaultName(string path, int maxInstructions)
    {
        string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        string safeName = new(fileName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        return $"{safeName}-{maxInstructions}";
    }

    private static bool TryParseUInt32(string text, out uint value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, provider: null, out value);
        }

        return uint.TryParse(text, out value);
    }
}
