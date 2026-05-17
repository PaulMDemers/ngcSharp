using NgcSharp.Core;

namespace NgcSharp.App;

public sealed class DiscDiagnosticHarness
{
    private static readonly (string Name, uint Address, int Length)[] FixedMemoryDumps =
    [
        ("low-memory", 0x8000_00C0, 0x160),
        ("sonic-small-data-audio", 0x803A_E2F0, 0x100),
        ("sonic-small-data-graphics", 0x803A_E380, 0x100),
        ("sonic-thread-area", 0x8036_D9E0, 0x280),
        ("sonic-gfx-work", 0x8037_0AE0, 0x140),
    ];

    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public DiscDiagnosticHarness(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
    }

    public int Run(DiscDiagnosticOptions options)
    {
        string outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        using DiscImageReader reader = DiscImageReader.Open(options.Path);
        DolFile dol = GameCubeDiscDolLoader.LoadMainDol(reader);
        GameCubeBus bus = new(reader);
        GameCubeDiscBoot.PrepareMemory(reader, bus.Memory);

        string framePath = Path.Combine(outputDirectory, "frame.png");
        string gxFramePath = Path.Combine(outputDirectory, "gx-frame.png");
        string gxDrawPath = Path.Combine(outputDirectory, "gx-draws.txt");
        RunDolOptions runOptions = new(
            options.Path,
            options.MaxInstructions,
            Trace: false,
            TracePath: null,
            DumpRegisters: true,
            DumpMmio: true,
            Quiet: false,
            FrameDumpPath: null,
            GxFrameDumpPath: null,
            TraceTail: 64,
            PcProfileTop: 32,
            FastForwardIdle: true);

        StringWriter runOutput = new();
        StringWriter runError = new();
        int exitCode;
        using (DiscDiagnosticTimelineRecorder timeline = new(outputDirectory, options.SnapshotInterval, options.ExtraMemoryProbes))
        {
            exitCode = new DolRunner(runOutput, runError).Run(dol, runOptions, bus, timeline.Record);
        }

        TryWriteFrameDump(bus, runOptions with { FrameDumpPath = framePath }, outputDirectory);
        TryWriteGxFrameDump(bus, runOptions with { GxFrameDumpPath = gxFramePath }, outputDirectory);
        TryWriteGxDrawDump(bus, gxDrawPath, outputDirectory);

        File.WriteAllText(Path.Combine(outputDirectory, "run-output.txt"), runOutput.ToString());
        File.WriteAllText(Path.Combine(outputDirectory, "run-error.txt"), runError.ToString());
        WriteMetadata(options, reader.Info, exitCode, outputDirectory);
        WriteMemoryDumps(bus, outputDirectory);
        WriteMmioTail(bus, outputDirectory);

        _output.WriteLine($"Diagnostic run wrote {outputDirectory}");
        _output.WriteLine($"Exit code: {exitCode}");
        if (File.Exists(framePath))
        {
            _output.WriteLine($"Frame: {framePath}");
        }

        if (File.Exists(gxFramePath))
        {
            _output.WriteLine($"GX frame: {gxFramePath}");
        }

        if (File.Exists(gxDrawPath))
        {
            _output.WriteLine($"GX draws: {gxDrawPath}");
        }

        if (exitCode != 0)
        {
            _error.Write(runError.ToString());
        }

        return exitCode;
    }

    private static void TryWriteFrameDump(GameCubeBus bus, RunDolOptions runOptions, string outputDirectory)
    {
        if (FramebufferDumper.TryDump(bus, runOptions, out FramebufferDumpResult? frameDump, out string? frameDumpError))
        {
            File.WriteAllText(
                Path.Combine(outputDirectory, "frame.txt"),
                $"Wrote {frameDump!.Width}x{frameDump.Height} {frameDump.Format} frame from 0x{frameDump.Address:X8} to {frameDump.Path}.{Environment.NewLine}");
            return;
        }

        File.WriteAllText(Path.Combine(outputDirectory, "frame-error.txt"), frameDumpError ?? "unknown frame dump error");
    }

    private static void TryWriteGxFrameDump(GameCubeBus bus, RunDolOptions runOptions, string outputDirectory)
    {
        if (runOptions.GxFrameDumpPath is null)
        {
            return;
        }

        int width = runOptions.FrameWidth ?? 640;
        int height = runOptions.FrameHeight ?? 480;
        if (GxFifoSoftwareRenderer.TryRender(bus.MmioAccesses, bus.Memory, runOptions.GxFrameDumpPath, width, height, out GxFifoSoftwareRenderResult? frame, out string? error))
        {
            File.WriteAllText(
                Path.Combine(outputDirectory, "gx-frame.txt"),
                $"Wrote {frame!.Width}x{frame.Height} GX diagnostic frame to {frame.Path} ({frame.Draws} draw(s), {frame.RenderedQuads} rendered quad(s), {frame.RenderedTriangles} rendered triangle(s), {frame.DegenerateQuads} degenerate quad(s), {frame.DegenerateTriangles} degenerate triangle(s)).{Environment.NewLine}");
            return;
        }

        File.WriteAllText(Path.Combine(outputDirectory, "gx-frame-error.txt"), error ?? "unknown GX frame dump error");
    }

    private static void TryWriteGxDrawDump(GameCubeBus bus, string path, string outputDirectory)
    {
        if (GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(bus.MmioAccesses, bus.Memory, path, maxDraws: 10, out GxFifoDrawDiagnosticResult? result, out string? error))
        {
            File.WriteAllText(
                Path.Combine(outputDirectory, "gx-draws-summary.txt"),
                $"Wrote GX draw diagnostics for {result!.DrawsWritten} of {result.TotalDraws} draw(s) to {result.Path}.{Environment.NewLine}");
            return;
        }

        File.WriteAllText(Path.Combine(outputDirectory, "gx-draws-error.txt"), error ?? "unknown GX draw dump error");
    }

    private static void WriteMetadata(DiscDiagnosticOptions options, DiscImageInfo info, int exitCode, string outputDirectory)
    {
        using StreamWriter writer = new(Path.Combine(outputDirectory, "metadata.txt"));
        writer.WriteLine($"Name: {options.Name ?? Path.GetFileNameWithoutExtension(options.Path)}");
        writer.WriteLine($"Path: {Path.GetFullPath(options.Path)}");
        writer.WriteLine($"Game ID: {info.DiscHeader.GameId}");
        writer.WriteLine($"Title: {info.DiscHeader.Title}");
        writer.WriteLine($"Kind: {info.Kind}");
        writer.WriteLine($"Max instructions: {options.MaxInstructions}");
        writer.WriteLine($"Snapshot interval: {options.SnapshotInterval}");
        if (options.ExtraMemoryProbes is { Count: > 0 })
        {
            foreach (DiagnosticMemoryProbe probe in options.ExtraMemoryProbes)
            {
                writer.WriteLine($"Probe: {probe.Name} 0x{probe.Address:X8}");
            }
        }

        writer.WriteLine($"Exit code: {exitCode}");
        writer.WriteLine($"UTC: {DateTimeOffset.UtcNow:O}");
    }

    private static void WriteMemoryDumps(GameCubeBus bus, string outputDirectory)
    {
        string memoryDirectory = Path.Combine(outputDirectory, "memory");
        Directory.CreateDirectory(memoryDirectory);

        foreach ((string name, uint address, int length) in FixedMemoryDumps)
        {
            WriteMemoryDumpFile(bus.Memory, Path.Combine(memoryDirectory, $"{name}.txt"), address, length);
        }

        TryWritePointedDump(bus.Memory, memoryDirectory, "current-thread", 0x8000_00E4, 0x320);
        TryWritePointedDump(bus.Memory, memoryDirectory, "default-thread", 0x8000_00D8, 0x320);
        TryWritePointedDump(bus.Memory, memoryDirectory, "thread-queue-head", 0x8000_00C0, 0x320);
    }

    private static void TryWritePointedDump(GameCubeMemory memory, string memoryDirectory, string name, uint pointerAddress, int length)
    {
        uint target = memory.Read32(pointerAddress);
        if (target == 0 || !GameCubeAddress.TryTranslateMainRam(target, out _))
        {
            return;
        }

        WriteMemoryDumpFile(memory, Path.Combine(memoryDirectory, $"{name}-0x{target:X8}.txt"), target, length);
    }

    private static void WriteMemoryDumpFile(GameCubeMemory memory, string path, uint address, int length)
    {
        using StreamWriter writer = new(path);
        ConsoleFormatting.WriteMemoryDump(writer, memory, address, length);
    }

    private static void WriteMmioTail(GameCubeBus bus, string outputDirectory)
    {
        using StreamWriter writer = new(Path.Combine(outputDirectory, "mmio-summary.txt"));
        ConsoleFormatting.WriteMmioSummary(writer, bus.MmioAccesses);
    }
}
