using NgcSharp.Core;

namespace NgcSharp.App;

public sealed class DiscDiagnosticTimelineRecorder : IDisposable
{
    private static readonly (string Name, uint Address, int Length)[] SnapshotMemoryDumps =
    [
        ("low-memory", 0x8000_00C0, 0x160),
        ("sonic-small-data-audio", 0x803A_E2F0, 0x100),
        ("sonic-small-data-graphics", 0x803A_E380, 0x100),
        ("sonic-thread-area", 0x8036_D9E0, 0x280),
        ("sonic-gfx-work", 0x8037_0AE0, 0x140),
    ];

    private static readonly (string Name, uint Address)[] MmioProbes =
    [
        ("piCause", 0xCC00_3000),
        ("piMask", 0xCC00_3004),
        ("diStatus", 0xCC00_6000),
        ("diDmaLength", 0xCC00_6018),
        ("dspControl", 0xCC00_500A),
        ("streamControl", 0xCC00_6C00),
        ("streamSamples", 0xCC00_6C08),
    ];

    private static readonly DiagnosticMemoryProbe[] DefaultMemoryWordProbes =
    [
        new("threadQueueHead", 0x8000_00C0),
        new("defaultThread", 0x8000_00D8),
        new("currentThread0", 0x8000_00E0),
        new("currentThread4", 0x8000_00E4),
        new("audioF4", 0x803A_E2F4),
        new("audio308", 0x803A_E308),
        new("audio30C", 0x803A_E30C),
        new("audio310", 0x803A_E310),
        new("audio31C", 0x803A_E31C),
        new("audio324", 0x803A_E324),
        new("audio32C", 0x803A_E32C),
        new("audio330", 0x803A_E330),
        new("audio334", 0x803A_E334),
        new("audio340", 0x803A_E340),
        new("audio348", 0x803A_E348),
        new("audio34C", 0x803A_E34C),
        new("graphics380", 0x803A_E380),
        new("graphics384", 0x803A_E384),
        new("gfxWork0", 0x8037_0AE0),
        new("gfxWork20", 0x8037_0B00),
    ];

    private readonly int _snapshotInterval;
    private readonly string _snapshotDirectory;
    private readonly StreamWriter? _timeline;
    private readonly StreamWriter? _changes;
    private readonly Dictionary<string, uint> _lastProbeValues = [];
    private readonly DiagnosticMemoryProbe[] _memoryWordProbes;
    private long _nextSnapshotInstruction;
    private int? _lastSnapshotInstruction;

    public DiscDiagnosticTimelineRecorder(string outputDirectory, int snapshotInterval, IReadOnlyList<DiagnosticMemoryProbe>? extraMemoryProbes)
    {
        _snapshotInterval = snapshotInterval;
        _memoryWordProbes = BuildMemoryProbes(extraMemoryProbes);
        if (_snapshotInterval <= 0)
        {
            _snapshotDirectory = string.Empty;
            return;
        }

        _nextSnapshotInstruction = snapshotInterval;
        _snapshotDirectory = Path.Combine(outputDirectory, "snapshots");
        Directory.CreateDirectory(_snapshotDirectory);

        _timeline = new StreamWriter(Path.Combine(outputDirectory, "timeline.csv"));
        _changes = new StreamWriter(Path.Combine(outputDirectory, "changes.csv"));
        WriteTimelineHeader(_timeline, _memoryWordProbes);
        _changes.WriteLine("instructions,kind,source,name,address,old_value,new_value,pc,lr");
    }

    public void Record(DolRunStep step)
    {
        if (_snapshotInterval <= 0 || !ShouldSnapshot(step, out string kind))
        {
            return;
        }

        _lastSnapshotInstruction = step.ExecutedInstructions;
        WriteTimelineRow(step, kind);
        WriteChanges(step, kind);
        WriteSnapshotFile(step, kind);
    }

    public void Dispose()
    {
        _timeline?.Dispose();
        _changes?.Dispose();
    }

    private bool ShouldSnapshot(DolRunStep step, out string kind)
    {
        if (step.IsInitial)
        {
            kind = "initial";
            return true;
        }

        if (step.IsFinal)
        {
            kind = "final";
            return _lastSnapshotInstruction != step.ExecutedInstructions;
        }

        if (step.ExecutedInstructions >= _nextSnapshotInstruction)
        {
            kind = "interval";
            while (_nextSnapshotInstruction <= step.ExecutedInstructions)
            {
                _nextSnapshotInstruction += _snapshotInterval;
            }

            return true;
        }

        kind = string.Empty;
        return false;
    }

    private static DiagnosticMemoryProbe[] BuildMemoryProbes(IReadOnlyList<DiagnosticMemoryProbe>? extraMemoryProbes)
    {
        if (extraMemoryProbes is not { Count: > 0 })
        {
            return DefaultMemoryWordProbes;
        }

        return DefaultMemoryWordProbes.Concat(extraMemoryProbes).ToArray();
    }

    private static void WriteTimelineHeader(TextWriter writer, IReadOnlyList<DiagnosticMemoryProbe> memoryWordProbes)
    {
        writer.Write("instructions,kind,pc,lr,ctr,cr,msr,r1,r2,r3,r4,r5,r6,r10,r13,video_frame,video_line,vblank,irq_cause,irq_mask,irq_pending");
        foreach ((string name, _) in MmioProbes)
        {
            writer.Write($",mmio_{name}");
        }

        foreach (DiagnosticMemoryProbe probe in memoryWordProbes)
        {
            writer.Write($",mem_{probe.Name}");
        }

        writer.WriteLine();
    }

    private void WriteTimelineRow(DolRunStep step, string kind)
    {
        GameCubeBus bus = step.Bus;
        _timeline!.Write($"{step.ExecutedInstructions},{kind},{Hex(step.Pc)},{Hex(step.State.Lr)},{Hex(step.State.Ctr)},{Hex(step.State.Cr)},{Hex(step.State.Msr)}");
        _timeline.Write($",{Hex(step.State.Gpr[1])},{Hex(step.State.Gpr[2])},{Hex(step.State.Gpr[3])},{Hex(step.State.Gpr[4])},{Hex(step.State.Gpr[5])},{Hex(step.State.Gpr[6])},{Hex(step.State.Gpr[10])},{Hex(step.State.Gpr[13])}");
        _timeline.Write($",{bus.VideoFrameCounter},{bus.CurrentVideoLine},{(bus.IsVideoInVBlank ? 1 : 0)}");
        _timeline.Write($",{Hex(bus.ProcessorInterruptCause)},{Hex(bus.ProcessorInterruptMask)},{Hex(bus.PendingProcessorInterrupts)}");

        foreach ((_, uint address) in MmioProbes)
        {
            bus.TryGetMmioValue(address, out uint value);
            _timeline.Write($",{Hex(value)}");
        }

        foreach (DiagnosticMemoryProbe probe in _memoryWordProbes)
        {
            _timeline.Write($",{Hex(ReadMemoryWord(step.Bus.Memory, probe.Address))}");
        }

        _timeline.WriteLine();
        _timeline.Flush();
    }

    private void WriteChanges(DolRunStep step, string kind)
    {
        foreach ((string name, uint address) in MmioProbes)
        {
            step.Bus.TryGetMmioValue(address, out uint value);
            WriteChangeIfNeeded(step, kind, "mmio", name, address, value);
        }

        foreach (DiagnosticMemoryProbe probe in _memoryWordProbes)
        {
            WriteChangeIfNeeded(step, kind, "memory", probe.Name, probe.Address, ReadMemoryWord(step.Bus.Memory, probe.Address));
        }

        _changes!.Flush();
    }

    private void WriteChangeIfNeeded(DolRunStep step, string kind, string source, string name, uint address, uint value)
    {
        string key = $"{source}:{name}";
        if (!_lastProbeValues.TryGetValue(key, out uint oldValue))
        {
            _changes!.WriteLine($"{step.ExecutedInstructions},{kind},{source},{name},{Hex(address)},,{Hex(value)},{Hex(step.Pc)},{Hex(step.State.Lr)}");
            _lastProbeValues[key] = value;
            return;
        }

        if (oldValue == value)
        {
            return;
        }

        _changes!.WriteLine($"{step.ExecutedInstructions},{kind},{source},{name},{Hex(address)},{Hex(oldValue)},{Hex(value)},{Hex(step.Pc)},{Hex(step.State.Lr)}");
        _lastProbeValues[key] = value;
    }

    private void WriteSnapshotFile(DolRunStep step, string kind)
    {
        string path = Path.Combine(_snapshotDirectory, $"{step.ExecutedInstructions:D12}-{kind}.txt");
        using StreamWriter writer = new(path);
        GameCubeBus bus = step.Bus;

        writer.WriteLine($"Instructions: {step.ExecutedInstructions}");
        writer.WriteLine($"Kind: {kind}");
        writer.WriteLine($"PC: {Hex(step.Pc)}");
        writer.WriteLine($"LR: {Hex(step.State.Lr)}");
        writer.WriteLine($"CTR: {Hex(step.State.Ctr)}");
        writer.WriteLine($"CR: {Hex(step.State.Cr)}");
        writer.WriteLine($"MSR: {Hex(step.State.Msr)}");
        writer.WriteLine($"GPR1: {Hex(step.State.Gpr[1])}");
        writer.WriteLine($"GPR2: {Hex(step.State.Gpr[2])}");
        writer.WriteLine($"GPR13: {Hex(step.State.Gpr[13])}");
        writer.WriteLine($"Video frame: {bus.VideoFrameCounter}");
        writer.WriteLine($"Video line: {bus.CurrentVideoLine}");
        writer.WriteLine($"VBlank: {bus.IsVideoInVBlank}");
        writer.WriteLine($"IRQ cause: {Hex(bus.ProcessorInterruptCause)}");
        writer.WriteLine($"IRQ mask: {Hex(bus.ProcessorInterruptMask)}");
        writer.WriteLine($"IRQ pending: {Hex(bus.PendingProcessorInterrupts)}");
        writer.WriteLine();

        writer.WriteLine("MMIO probes:");
        foreach ((string name, uint address) in MmioProbes)
        {
            bus.TryGetMmioValue(address, out uint value);
            writer.WriteLine($"  {name,-14} {Hex(address)} = {Hex(value)}");
        }

        writer.WriteLine();
        writer.WriteLine("Memory probes:");
        foreach (DiagnosticMemoryProbe probe in _memoryWordProbes)
        {
            writer.WriteLine($"  {probe.Name,-16} {Hex(probe.Address)} = {Hex(ReadMemoryWord(bus.Memory, probe.Address))}");
        }

        foreach ((string name, uint address, int length) in SnapshotMemoryDumps)
        {
            writer.WriteLine();
            writer.WriteLine($"--- {name} {Hex(address)} + 0x{length:X} ---");
            ConsoleFormatting.WriteMemoryDump(writer, bus.Memory, address, length);
        }

        WriteRangeDump(writer, bus.Memory, "stack", step.State.Gpr[1], 0x240);

        WritePointedDump(writer, bus.Memory, "thread-queue-head", 0x8000_00C0, 0x320);
        WritePointedDump(writer, bus.Memory, "default-thread", 0x8000_00D8, 0x320);
        WritePointedDump(writer, bus.Memory, "current-thread-0", 0x8000_00E0, 0x320);
        WritePointedDump(writer, bus.Memory, "current-thread-4", 0x8000_00E4, 0x320);
    }

    private static void WriteRangeDump(TextWriter writer, GameCubeMemory memory, string name, uint address, int length)
    {
        if (address == 0 || !GameCubeAddress.TryTranslateMainRam(address, out _))
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine($"--- {name} {Hex(address)} + 0x{length:X} ---");
        ConsoleFormatting.WriteMemoryDump(writer, memory, address, length);
    }

    private static void WritePointedDump(TextWriter writer, GameCubeMemory memory, string name, uint pointerAddress, int length)
    {
        uint target = memory.Read32(pointerAddress);
        if (target == 0 || !GameCubeAddress.TryTranslateMainRam(target, out _))
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine($"--- {name} pointer {Hex(pointerAddress)} -> {Hex(target)} + 0x{length:X} ---");
        ConsoleFormatting.WriteMemoryDump(writer, memory, target, length);
    }

    private static uint ReadMemoryWord(GameCubeMemory memory, uint address)
    {
        try
        {
            return memory.Read32(address);
        }
        catch (AddressTranslationException)
        {
            return 0;
        }
    }

    private static string Hex(uint value) => $"0x{value:X8}";
}
