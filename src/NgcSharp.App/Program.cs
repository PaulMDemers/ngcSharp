using NgcSharp.Core;
using NgcSharp.App;
using NgcSharp.Cpu;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

string command = args[0];

try
{
    return command switch
    {
        "disc-info" => PrintDiscInfo(args),
        "disc-files" => PrintDiscFiles(args),
        "disc-read" => ReadDiscBytes(args),
        "disc-read-bin" => ReadDiscBytesToFile(args),
        "disc-disasm" => DisassembleDiscMemory(args),
        "disc-dump-memory" => DumpDiscMemory(args),
        "disc-find-dform" => FindDiscDFormInstructions(args),
        "disc-find-branch" => FindDiscBranchInstructions(args),
        "disc-find-word" => FindDiscWord(args),
        "diagnose-disc" => DiagnoseDisc(args),
        "dol-info" => PrintDolInfo(args),
        "prs-decode" => DecodePrsFile(args),
        "compare-images" => CompareImages(args),
        "run-dol" => RunDol(args),
        "run-disc" => RunDisc(args),
        "write-test-dol" => WriteTestDol(args),
        _ => UnknownCommand(command),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int PrintDolInfo(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Missing DOL path.");
        return 1;
    }

    DolFile dol = DolFile.Load(args[1]);
    Console.WriteLine($"Entry: 0x{dol.EntryPoint:X8}");
    Console.WriteLine($"BSS:   0x{dol.BssAddress:X8} + 0x{dol.BssSize:X}");

    foreach (DolSection section in dol.Sections)
    {
        Console.WriteLine($"{section.Name,-5} file=0x{section.FileOffset:X8} mem=0x{section.Address:X8} size=0x{section.Size:X}");
    }

    return 0;
}

static int PrintDiscInfo(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Missing disc image path.");
        return 1;
    }

    using DiscImageReader reader = DiscImageReader.Open(args[1]);
    DiscImageInfo info = reader.Info;
    Console.WriteLine($"Path:       {Path.GetFullPath(info.Path)}");
    Console.WriteLine($"Kind:       {info.Kind}");
    Console.WriteLine($"Game ID:    {info.DiscHeader.GameId}");
    Console.WriteLine($"Maker:      {info.DiscHeader.MakerCode}");
    Console.WriteLine($"Disc #:     {info.DiscHeader.DiscNumber}");
    Console.WriteLine($"Version:    {info.DiscHeader.Version}");
    Console.WriteLine($"Magic:      0x{info.DiscHeader.Magic:X8} {(info.DiscHeader.IsGameCubeDisc ? "(GameCube)" : "(unexpected)")}");
    Console.WriteLine($"Title:      {info.DiscHeader.Title}");
    Console.WriteLine($"Disc size:  0x{info.DiscSize:X}");
    Console.WriteLine($"File size:  0x{info.ContainerSize:X}");

    if (info.Kind == DiscImageKind.Rvz)
    {
        Console.WriteLine($"RVZ ver:    {FormatVersion(info.RvzVersion.GetValueOrDefault())}");
        Console.WriteLine($"RVZ compat: {FormatVersion(info.RvzCompatibleVersion.GetValueOrDefault())}");
        Console.WriteLine($"RVZ comp:   {FormatRvzCompression(info.RvzCompression.GetValueOrDefault())}");
        Console.WriteLine($"RVZ level:  {info.RvzCompressionLevel}");
        Console.WriteLine($"RVZ chunk:  0x{info.RvzChunkSize:X}");
        Console.WriteLine($"NKit mark:  {(info.HasNkitMarker ? "yes" : "no")}");
    }

    GameCubeDiscLayout layout = GameCubeDiscLayout.Read(reader);
    Console.WriteLine($"Main DOL:   0x{layout.MainDolOffset:X8}");
    Console.WriteLine($"FST:        0x{layout.FileSystemTableOffset:X8} + 0x{layout.FileSystemTableSize:X}");
    Console.WriteLine($"FST max:    0x{layout.FileSystemTableMaxSize:X}");

    return 0;
}

static int ReadDiscBytes(string[] args)
{
    if (args.Length < 4 || !TryParseUInt32(args[2], out uint offset) || !TryParseInt32(args[3], out int size) || size < 0)
    {
        Console.Error.WriteLine("Usage: ngcsharp disc-read <path-to-iso-gcm-rvz> <offset> <size>");
        return 1;
    }

    using DiscImageReader reader = DiscImageReader.Open(args[1]);
    byte[] bytes = reader.ReadBytes(offset, size);
    for (int row = 0; row < bytes.Length; row += 16)
    {
        int count = Math.Min(16, bytes.Length - row);
        Console.Write($"0x{offset + row:X8}  ");
        for (int i = 0; i < 16; i++)
        {
            Console.Write(i < count ? $"{bytes[row + i]:X2} " : "   ");
        }

        Console.Write(" ");
        for (int i = 0; i < count; i++)
        {
            byte value = bytes[row + i];
            Console.Write(value is >= 0x20 and <= 0x7E ? (char)value : '.');
        }

        Console.WriteLine();
    }

    return 0;
}

static int ReadDiscBytesToFile(string[] args)
{
    if (args.Length < 5 || !TryParseUInt32(args[2], out uint offset) || !TryParseInt32(args[3], out int size) || size < 0)
    {
        Console.Error.WriteLine("Usage: ngcsharp disc-read-bin <path-to-iso-gcm-rvz> <offset> <size> <output-path>");
        return 1;
    }

    using DiscImageReader reader = DiscImageReader.Open(args[1]);
    byte[] bytes = reader.ReadBytes(offset, size);
    string fullPath = Path.GetFullPath(args[4]);
    string? directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    File.WriteAllBytes(fullPath, bytes);
    Console.WriteLine($"Wrote {bytes.Length} byte(s) from disc offset 0x{offset:X8} to {fullPath}.");
    return 0;
}

static int DisassembleDiscMemory(string[] args)
{
    if (args.Length < 4 || !TryParseUInt32(args[2], out uint address) || !TryParseInt32(args[3], out int count) || count < 0)
    {
        Console.Error.WriteLine("Usage: ngcsharp disc-disasm <path-to-iso-gcm-rvz> <address> <instruction-count>");
        return 1;
    }

    using DiscImageReader reader = DiscImageReader.Open(args[1]);
    DolFile dol = GameCubeDiscDolLoader.LoadMainDol(reader);
    GameCubeMemory memory = new();
    GameCubeDiscBoot.PrepareMemory(reader, memory);
    dol.LoadInto(memory);

    for (int index = 0; index < count; index++)
    {
        uint pc = address + (uint)(index * sizeof(uint));
        uint instruction = memory.Read32(pc);
        Console.WriteLine($"0x{pc:X8}: 0x{instruction:X8}  {PowerPcDisassembler.Disassemble(instruction)}");
    }

    return 0;
}

static int DumpDiscMemory(string[] args)
{
    if (args.Length < 4 || !TryParseUInt32(args[2], out uint address) || !TryParseInt32(args[3], out int size) || size < 0)
    {
        Console.Error.WriteLine("Usage: ngcsharp disc-dump-memory <path-to-iso-gcm-rvz> <address> <size>");
        return 1;
    }

    using DiscImageReader reader = DiscImageReader.Open(args[1]);
    DolFile dol = GameCubeDiscDolLoader.LoadMainDol(reader);
    GameCubeMemory memory = new();
    GameCubeDiscBoot.PrepareMemory(reader, memory);
    dol.LoadInto(memory);
    ConsoleFormatting.WriteMemoryDump(Console.Out, memory, address, size);
    return 0;
}

static int FindDiscDFormInstructions(string[] args)
{
    if (args.Length < 5
        || !TryParseOpcodeSelector(args[2], out HashSet<int>? opcodes)
        || !TryParseInt32(args[3], out int ra)
        || ra is < 0 or > 31
        || !TryParseSigned16(args[4], out short displacement))
    {
        Console.Error.WriteLine("Usage: ngcsharp disc-find-dform <path-to-iso-gcm-rvz> <opcode|load|store|any> <ra> <signed-displacement>");
        return 1;
    }

    using DiscImageReader reader = DiscImageReader.Open(args[1]);
    DolFile dol = GameCubeDiscDolLoader.LoadMainDol(reader);
    int matches = 0;

    foreach (DolSection section in dol.Sections.OrderBy(section => section.Address))
    {
        ReadOnlySpan<byte> data = section.Data.Span;
        for (int offset = 0; offset <= data.Length - sizeof(uint); offset += sizeof(uint))
        {
            uint instruction = BigEndian.ReadUInt32(data.Slice(offset, sizeof(uint)));
            int opcode = (int)(instruction >> 26);
            int instructionRa = (int)((instruction >> 16) & 0x1F);
            short instructionDisplacement = unchecked((short)(instruction & 0xFFFF));

            if (instructionRa != ra || instructionDisplacement != displacement)
            {
                continue;
            }

            if (opcodes is not null && !opcodes.Contains(opcode))
            {
                continue;
            }

            uint address = section.Address + checked((uint)offset);
            Console.WriteLine($"0x{address:X8}: 0x{instruction:X8}  {PowerPcDisassembler.Disassemble(instruction)}  ({section.Name}+0x{offset:X})");
            matches++;
        }
    }

    Console.WriteLine($"Found {matches} matching instruction(s).");
    return 0;
}

static int FindDiscBranchInstructions(string[] args)
{
    if (args.Length < 3 || !TryParseUInt32(args[2], out uint targetAddress))
    {
        Console.Error.WriteLine("Usage: ngcsharp disc-find-branch <path-to-iso-gcm-rvz> <target-address>");
        return 1;
    }

    using DiscImageReader reader = DiscImageReader.Open(args[1]);
    DolFile dol = GameCubeDiscDolLoader.LoadMainDol(reader);
    int matches = 0;

    foreach (DolSection section in dol.Sections.OrderBy(section => section.Address))
    {
        ReadOnlySpan<byte> data = section.Data.Span;
        for (int offset = 0; offset <= data.Length - sizeof(uint); offset += sizeof(uint))
        {
            uint address = section.Address + checked((uint)offset);
            uint instruction = BigEndian.ReadUInt32(data.Slice(offset, sizeof(uint)));
            if (!TryGetBranchTarget(address, instruction, out uint branchTarget) || branchTarget != targetAddress)
            {
                continue;
            }

            Console.WriteLine($"0x{address:X8}: 0x{instruction:X8}  {PowerPcDisassembler.Disassemble(instruction)}  ({section.Name}+0x{offset:X})");
            matches++;
        }
    }

    Console.WriteLine($"Found {matches} matching branch instruction(s).");
    return 0;
}

static int FindDiscWord(string[] args)
{
    if (args.Length < 3 || !TryParseUInt32(args[2], out uint value))
    {
        Console.Error.WriteLine("Usage: ngcsharp disc-find-word <path-to-iso-gcm-rvz> <value>");
        return 1;
    }

    using DiscImageReader reader = DiscImageReader.Open(args[1]);
    DolFile dol = GameCubeDiscDolLoader.LoadMainDol(reader);
    int matches = 0;

    foreach (DolSection section in dol.Sections.OrderBy(section => section.Address))
    {
        ReadOnlySpan<byte> data = section.Data.Span;
        for (int offset = 0; offset <= data.Length - sizeof(uint); offset += sizeof(uint))
        {
            uint word = BigEndian.ReadUInt32(data.Slice(offset, sizeof(uint)));
            if (word != value)
            {
                continue;
            }

            uint address = section.Address + checked((uint)offset);
            Console.WriteLine($"0x{address:X8}: 0x{word:X8}  ({section.Name}+0x{offset:X})");
            matches++;
        }
    }

    Console.WriteLine($"Found {matches} matching word(s).");
    return 0;
}

static bool TryGetBranchTarget(uint address, uint instruction, out uint target)
{
    target = 0;
    int opcode = (int)(instruction >> 26);
    switch (opcode)
    {
        case 16:
            {
                int displacement = (short)(instruction & 0xFFFC);
                bool absolute = (instruction & 0x2) != 0;
                target = absolute ? unchecked((uint)displacement) : unchecked(address + (uint)displacement);
                return true;
            }

        case 18:
            {
                int displacement = SignExtend26(instruction & 0x03FF_FFFC);
                bool absolute = (instruction & 0x2) != 0;
                target = absolute ? unchecked((uint)displacement) : unchecked(address + (uint)displacement);
                return true;
            }

        default:
            return false;
    }
}

static int SignExtend26(uint value)
{
    const uint signBit = 0x0200_0000;
    const uint mask = 0x03FF_FFFF;
    uint normalized = value & mask;
    if ((normalized & signBit) != 0)
    {
        normalized |= ~mask;
    }

    return unchecked((int)normalized);
}

static int PrintDiscFiles(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Missing disc image path.");
        return 1;
    }

    using DiscImageReader reader = DiscImageReader.Open(args[1]);
    GameCubeFileSystemTable fst = GameCubeFileSystemTable.Load(reader);
    foreach (GameCubeFileSystemEntry entry in fst.Entries)
    {
        if (entry.IsDirectory)
        {
            Console.WriteLine($"<dir>  {entry.Path}");
        }
        else
        {
            Console.WriteLine($"0x{entry.DiscOffset:X8} 0x{entry.Size:X8} {entry.Path}");
        }
    }

    return 0;
}

static int RunDol(string[] args)
{
    if (!RunDolOptions.TryParse(args, out RunDolOptions options, Console.Error))
    {
        return 1;
    }

    return new DolRunner(Console.Out, Console.Error).Run(options);
}

static int RunDisc(string[] args)
{
    if (!RunDolOptions.TryParse(args, out RunDolOptions options, Console.Error))
    {
        return 1;
    }

    using DiscImageReader reader = DiscImageReader.Open(options.Path);
    DolFile dol = GameCubeDiscDolLoader.LoadMainDol(reader);
    GameCubeBus bus = new(reader);
    GameCubeDiscBoot.PrepareMemory(reader, bus.Memory);
    return new DolRunner(Console.Out, Console.Error).Run(dol, options, bus);
}

static int DiagnoseDisc(string[] args)
{
    if (!DiscDiagnosticOptions.TryParse(args, out DiscDiagnosticOptions options, Console.Error))
    {
        return 1;
    }

    return new DiscDiagnosticHarness(Console.Out, Console.Error).Run(options);
}

static int CompareImages(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: ngcsharp compare-images <baseline-png> <candidate-png> [--diff <png-path>]");
        return 1;
    }

    string? diffPath = null;
    for (int index = 3; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--diff":
                if (index + 1 >= args.Length)
                {
                    Console.Error.WriteLine("--diff requires a PNG output path.");
                    return 1;
                }

                diffPath = args[++index];
                break;
            default:
                Console.Error.WriteLine($"Unknown compare-images option '{args[index]}'.");
                return 1;
        }
    }

    ImageComparisonResult result = ImageComparison.Compare(args[1], args[2], diffPath);
    ImageComparison.WriteReport(Console.Out, result);
    return 0;
}

static int DecodePrsFile(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: ngcsharp prs-decode <input-bin> <output-bin> [--offset <n>] [--slice <offset> <size> <output-bin>]");
        return 1;
    }

    int sourceOffset = 0;
    List<(int Offset, int Size, string Path)> slices = [];
    for (int index = 3; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--offset":
                if (index + 1 >= args.Length || !TryParseInt32(args[++index], out sourceOffset) || sourceOffset < 0)
                {
                    Console.Error.WriteLine("--offset requires a non-negative integer.");
                    return 1;
                }

                break;
            case "--slice":
                if (index + 3 >= args.Length
                    || !TryParseInt32(args[++index], out int sliceOffset)
                    || !TryParseInt32(args[++index], out int sliceSize)
                    || sliceOffset < 0
                    || sliceSize < 0)
                {
                    Console.Error.WriteLine("--slice requires a non-negative offset, size, and output path.");
                    return 1;
                }

                slices.Add((sliceOffset, sliceSize, args[++index]));
                break;
            default:
                Console.Error.WriteLine($"Unknown prs-decode option '{args[index]}'.");
                return 1;
        }
    }

    byte[] input = File.ReadAllBytes(args[1]);
    if (sourceOffset > input.Length)
    {
        Console.Error.WriteLine($"--offset 0x{sourceOffset:X} is beyond the input length 0x{input.Length:X}.");
        return 1;
    }

    if (!SegaPrsDecoder.TryDecode(input.AsMemory(sourceOffset), out SegaPrsDecodeResult result, out string? failure))
    {
        Console.Error.WriteLine($"PRS decode failed: {failure}");
        return 1;
    }

    WriteBinaryFile(args[2], result.Output);
    Console.WriteLine($"Decoded 0x{result.Output.Length:X} byte(s) from 0x{result.SourceBytesConsumed:X} source byte(s) to {Path.GetFullPath(args[2])}.");

    foreach ((int offset, int size, string path) in slices)
    {
        if (offset > result.Output.Length || size > result.Output.Length - offset)
        {
            Console.Error.WriteLine($"Slice 0x{offset:X}+0x{size:X} is outside decoded output length 0x{result.Output.Length:X}.");
            return 1;
        }

        WriteBinaryFile(path, result.Output.AsSpan(offset, size));
        Console.WriteLine($"Wrote decoded slice 0x{offset:X}+0x{size:X} to {Path.GetFullPath(path)}.");
    }

    Console.WriteLine($"Final flag byte: 0x{result.LastFlagByte:X2}; bits remaining: {result.BitsRemaining}.");
    return 0;
}

static void WriteBinaryFile(string path, ReadOnlySpan<byte> bytes)
{
    string fullPath = Path.GetFullPath(path);
    string? directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    File.WriteAllBytes(fullPath, bytes);
}

static int WriteTestDol(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Missing output path.");
        return 1;
    }

    SyntheticDolFactory.WriteSmokeTestDol(args[1]);
    Console.WriteLine($"Wrote synthetic smoke-test DOL: {Path.GetFullPath(args[1])}");
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("ngcsharp");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  ngcsharp disc-info <path-to-iso-gcm-rvz>");
    Console.WriteLine("  ngcsharp disc-files <path-to-iso-gcm-rvz>");
    Console.WriteLine("  ngcsharp disc-read <path-to-iso-gcm-rvz> <offset> <size>");
    Console.WriteLine("  ngcsharp disc-read-bin <path-to-iso-gcm-rvz> <offset> <size> <output-path>");
    Console.WriteLine("  ngcsharp disc-disasm <path-to-iso-gcm-rvz> <address> <instruction-count>");
    Console.WriteLine("  ngcsharp disc-dump-memory <path-to-iso-gcm-rvz> <address> <size>");
    Console.WriteLine("  ngcsharp disc-find-dform <path-to-iso-gcm-rvz> <opcode|load|store|any> <ra> <signed-displacement>");
    Console.WriteLine("  ngcsharp disc-find-branch <path-to-iso-gcm-rvz> <target-address>");
    Console.WriteLine("  ngcsharp disc-find-word <path-to-iso-gcm-rvz> <value>");
    Console.WriteLine("  ngcsharp diagnose-disc <path-to-iso-gcm-rvz> [--max-instructions <n>] [--snapshot-interval <n>] [--probe-word <name> <addr>] [--out <directory>] [--name <label>]");
    Console.WriteLine("  ngcsharp dol-info <path-to-dol>");
    Console.WriteLine("  ngcsharp prs-decode <input-bin> <output-bin> [--offset <n>] [--slice <offset> <size> <output-bin>]");
    Console.WriteLine("  ngcsharp compare-images <baseline-png> <candidate-png> [--diff <png-path>]");
    Console.WriteLine($"  ngcsharp run-dol <path-to-dol> [--max-instructions <n>] [--trace] [--trace-file <path>] [--trace-tail <n>] [--dump-mmio] [--dump-threads] [--dump-message-queues] [--dump-memory <addr> <len>] [--dump-memory-bin <addr> <len> <path>] [--dump-memory-bin-at <instruction> <addr> <len> <path>] [--dump-disasm <addr> <count>] [--dump-pointer-table <addr> <count> <stride> <ptr-offset> <target-words>] [--dump-frame <png-path>] [--dump-gx-frame <png-path>] [--dump-gx-frame-sweep <dir> <start-skip> <step> <count>] [--gx-frame-source <efb|auto|last-display-copy|last-nonblack-display-copy|largest-display-copy|last-nonblack-efb|vi-framebuffer|last-nonblack-vi-framebuffer|copy-index|copy-source-index>] [--gx-frame-copy-index <n>] [--gx-frame-max-draws <n, default {RunDolOptions.DefaultGxFrameMaxDraws}>] [--gx-frame-skip-draws <n>] [--gx-frame-max-raster-pixels <n, default {RunDolOptions.DefaultGxFrameMaxRasterPixels}>] [--gx-frame-ignore-efb-copy-clear] [--gx-frame-skip-copy-memory-writes] [--dump-gx-draws <txt-path>] [--dump-gx-copies <csv-path>] [--dump-gx-copy-events <csv-path>] [--dump-gx-coverage <csv-path>] [--dump-gx-triangle-coverage <csv-path>] [--dump-gx-tev-samples <csv-path>] [--dump-gx-transforms <csv-path>] [--dump-gx-state-timeline <csv-path>] [--dump-gx-vertices <csv-path>] [--dump-gx-textures <dir>] [--gx-draw-skip-draws <n>] [--gx-draw-max-draws <n, default {RunDolOptions.DefaultGxDrawMaxDraws}>] [--trace-gx-fifo-writes <csv-path>] [--trace-gx-fifo-window <csv-path> <start> <len>] [--gx-memory-checkpoint <fifo-offset> <addr> <len> <path>] [--gx-disable-auto-texture-snapshots] [--trace-exi <csv-path>] [--trace-si <csv-path>] [--trace-mmio <csv-path>] [--trace-scheduler <csv-path>] [--run-summary <json-path>] [--memory-card-a] [--memory-card-b] [--frame-address <addr>] [--frame-width <n>] [--frame-height <n>] [--frame-format <rgb565|yuyv|uyvy|xrgb8888>] [--watch-address <addr>] [--watch-write-value <value>] [--watch-write-range <addr> <len>] [--watch-write-after <n>] [--watch-load-range <addr> <len>] [--stop-after-write-watch <n>] [--watch-call-target <addr>] [--watch-call-range <addr> <len>] [--find-memory-word <value>] [--watch-gpr <r0-r31>] [--watch-gpr-after <n>] [--fast-forward-idle] [--fast-forward-write-watch] [--disable-sonic-bit-unpack-fast-forward] [--disable-sonic-paired-transform-fast-forward] [--disable-sonic-gx-fast-forward] [--disable-sonic-geometry-fast-forward] [--disable-sonic-resource-fast-forward] [--disable-sonic-resource-lookup-fast-forward] [--enable-sonic-resource-lookup-fast-forward] [--disable-sonic-resource-mode-query-fast-forward] [--enable-sonic-resource-mode-query-fast-forward] [--disable-sonic-resource-state-poll-fast-forward] [--enable-sonic-resource-state-poll-fast-forward] [--disable-sonic-resource-fixup-fast-forward] [--enable-sonic-resource-fixup-fast-forward] [--trace-prs-decompress] [--trace-sonic-path-lookup <csv-path>] [--trace-sonic-resource-flags <csv-path>] [--trace-sonic-matrix-stack <csv-path>] [--trace-sonic-matrix-writer <csv-path>] [--trace-sonic-root-matrix <csv-path>] [--trace-sonic-scene-state <csv-path>] [--trace-sonic-packet-selection <csv-path>] [--trace-sonic-traversal-source <csv-path>] [--trace-sonic-draw-packets <csv-path>] [--trace-sonic-gx-emitters <csv-path>] [--trace-sonic-texture-binds <csv-path>] [--trace-sonic-vertex-provenance <csv-path> <fifo-start> <fifo-len>] [--trace-sonic-transform-inputs <csv-path>] [--trace-sonic-transform-output-range <addr> <len>] [--trace-sonic-input-writes <csv-path> <addr> <len>] [--trace-locked-cache-writes <csv-path> <addr> <len>] [--di-command-latency-cycles <n>] [--controller-button <name|mask>] [--controller-buttons <mask>] [--controller-button-window <name|mask> <start> <end>] [--watch-limit <n>] [--profile-pc <n>] [--profile-after <n>] [--profile-indirect-call-site <addr> <n>] [--profile-branch-site <addr> <n>] [--profile-pc-lr <addr> <n>] [--stop-on-pc <addr>] [--stop-on-pc-after <n>] [--trace-pc <addr>] [--trace-pc-after <n>] [--stop-on-gx-fifo-offset <n>] [--stop-on-hot-pc <n>] [--stop-on-hot-pc-after <n>] [--no-registers] [--quiet]");
    Console.WriteLine("  ngcsharp run-disc <path-to-iso-gcm-rvz> [--max-instructions <n>] [--trace] [--trace-file <path>] [--trace-tail <n>] [--dump-mmio] [--dump-threads] [--dump-message-queues] [--dump-memory <addr> <len>] [--dump-disasm <addr> <count>] [--dump-pointer-table <addr> <count> <stride> <ptr-offset> <target-words>] [--dump-gx-frame <png-path>] [--dump-gx-frame-sweep <dir> <start-skip> <step> <count>] [--gx-frame-source <efb|auto|last-display-copy|last-nonblack-display-copy|largest-display-copy|last-nonblack-efb|vi-framebuffer|last-nonblack-vi-framebuffer|copy-index|copy-source-index>] [--gx-frame-copy-index <n>] [--gx-frame-max-draws <n>] [--gx-frame-skip-draws <n>] [--gx-frame-max-raster-pixels <n>] [--gx-frame-ignore-efb-copy-clear] [--gx-frame-skip-copy-memory-writes] [--dump-gx-draws <txt-path>] [--dump-gx-copies <csv-path>] [--dump-gx-copy-events <csv-path>] [--dump-gx-coverage <csv-path>] [--dump-gx-triangle-coverage <csv-path>] [--dump-gx-tev-samples <csv-path>] [--dump-gx-transforms <csv-path>] [--dump-gx-state-timeline <csv-path>] [--dump-gx-vertices <csv-path>] [--dump-gx-textures <dir>] [--gx-draw-skip-draws <n>] [--gx-draw-max-draws <n>] [--trace-gx-fifo-writes <csv-path>] [--trace-gx-fifo-window <csv-path> <start> <len>] [--gx-memory-checkpoint <fifo-offset> <addr> <len> <path>] [--gx-disable-auto-texture-snapshots] [--trace-exi <csv-path>] [--trace-si <csv-path>] [--trace-mmio <csv-path>] [--trace-scheduler <csv-path>] [--run-summary <json-path>] [--memory-card-a] [--memory-card-b] [--watch-address <addr>] [--watch-write-value <value>] [--watch-write-range <addr> <len>] [--watch-write-after <n>] [--watch-load-range <addr> <len>] [--stop-after-write-watch <n>] [--watch-call-target <addr>] [--watch-call-range <addr> <len>] [--find-memory-word <value>] [--watch-gpr <r0-r31>] [--watch-gpr-after <n>] [--fast-forward-idle] [--fast-forward-write-watch] [--disable-sonic-bit-unpack-fast-forward] [--disable-sonic-paired-transform-fast-forward] [--disable-sonic-gx-fast-forward] [--disable-sonic-geometry-fast-forward] [--disable-sonic-resource-fast-forward] [--disable-sonic-resource-lookup-fast-forward] [--enable-sonic-resource-lookup-fast-forward] [--disable-sonic-resource-mode-query-fast-forward] [--enable-sonic-resource-mode-query-fast-forward] [--disable-sonic-resource-state-poll-fast-forward] [--enable-sonic-resource-state-poll-fast-forward] [--disable-sonic-resource-fixup-fast-forward] [--enable-sonic-resource-fixup-fast-forward] [--trace-prs-decompress] [--trace-sonic-path-lookup <csv-path>] [--trace-sonic-resource-flags <csv-path>] [--trace-sonic-matrix-stack <csv-path>] [--trace-sonic-matrix-writer <csv-path>] [--trace-sonic-root-matrix <csv-path>] [--trace-sonic-scene-state <csv-path>] [--trace-sonic-packet-selection <csv-path>] [--trace-sonic-traversal-source <csv-path>] [--trace-sonic-draw-packets <csv-path>] [--trace-sonic-gx-emitters <csv-path>] [--trace-sonic-texture-binds <csv-path>] [--trace-sonic-vertex-provenance <csv-path> <fifo-start> <fifo-len>] [--trace-sonic-transform-inputs <csv-path>] [--trace-sonic-transform-output-range <addr> <len>] [--trace-sonic-input-writes <csv-path> <addr> <len>] [--trace-locked-cache-writes <csv-path> <addr> <len>] [--di-command-latency-cycles <n>] [--controller-button <name|mask>] [--controller-buttons <mask>] [--controller-button-window <name|mask> <start> <end>] [--watch-limit <n>] [--profile-pc <n>] [--profile-after <n>] [--profile-indirect-call-site <addr> <n>] [--profile-branch-site <addr> <n>] [--profile-pc-lr <addr> <n>] [--stop-on-pc <addr>] [--stop-on-pc-after <n>] [--trace-pc <addr>] [--trace-pc-after <n>] [--stop-on-gx-fifo-offset <n>] [--stop-on-hot-pc <n>] [--stop-on-hot-pc-after <n>] [--no-registers] [--quiet]");
    Console.WriteLine("  ngcsharp write-test-dol <output-path>");
    Console.WriteLine("  run-dol/run-disc also accept --dump-memory-bin <addr> <len> <path> for final raw RAM snapshots and --dump-memory-bin-at <instruction> <addr> <len> <path> for pre-instruction snapshots.");
}

static string FormatVersion(uint value)
{
    return $"{value >> 24}.{(value >> 16) & 0xFF:00}";
}

static string FormatRvzCompression(uint value)
{
    return value switch
    {
        0 => "none",
        2 => "bzip2",
        3 => "lzma",
        4 => "lzma2",
        5 => "zstd",
        _ => $"unknown ({value})",
    };
}

static bool TryParseUInt32(string text, out uint value)
{
    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
        return uint.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, provider: null, out value);
    }

    return uint.TryParse(text, out value);
}

static bool TryParseInt32(string text, out int value)
{
    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
        return int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, provider: null, out value);
    }

    return int.TryParse(text, out value);
}

static bool TryParseSigned16(string text, out short value)
{
    if (TryParseInt32(text, out int parsed) && parsed is >= short.MinValue and <= ushort.MaxValue)
    {
        value = unchecked((short)parsed);
        return true;
    }

    value = 0;
    return false;
}
static bool TryParseOpcodeSelector(string text, out HashSet<int>? opcodes)
{
    opcodes = text.ToLowerInvariant() switch
    {
        "any" => null,
        "load" => [32, 33, 34, 35, 40, 41, 42, 43, 46, 48, 49, 50, 51, 56, 57],
        "store" => [36, 37, 38, 39, 44, 45, 47, 52, 53, 54, 55, 60, 61],
        "lwz" => [32],
        "lwzu" => [33],
        "lbz" => [34],
        "lbzu" => [35],
        "stw" => [36],
        "stwu" => [37],
        "stb" => [38],
        "stbu" => [39],
        "lhz" => [40],
        "lhzu" => [41],
        "lha" => [42],
        "lhau" => [43],
        "sth" => [44],
        "sthu" => [45],
        "lmw" => [46],
        "stmw" => [47],
        "lfs" => [48],
        "lfsu" => [49],
        "lfd" => [50],
        "lfdu" => [51],
        "stfs" => [52],
        "stfsu" => [53],
        "stfd" => [54],
        "stfdu" => [55],
        "psq_l" => [56],
        "psq_lu" => [57],
        "psq_st" => [60],
        "psq_stu" => [61],
        _ => null,
    };

    if (opcodes is not null || text.Equals("any", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (TryParseInt32(text, out int parsedOpcode) && parsedOpcode is >= 0 and <= 63)
    {
        opcodes = [parsedOpcode];
        return true;
    }

    return false;
}
