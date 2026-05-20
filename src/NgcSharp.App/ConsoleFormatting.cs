using NgcSharp.Core;
using NgcSharp.Cpu;

namespace NgcSharp.App;

public static class ConsoleFormatting
{
    private const int MaxMmioAccessesToPrint = 200;
    private const int MaxMmioTailAccessesToPrint = 40;
    private const int MaxMmioFinalValuesToPrint = 160;
    private static readonly (string Label, uint High, uint Low)[] ViFramebufferRegisterPairs =
    [
        ("top-left", 0xCC00_201C, 0xCC00_201E),
        ("top-right", 0xCC00_2020, 0xCC00_2022),
        ("bottom-left", 0xCC00_2024, 0xCC00_2026),
        ("bottom-right", 0xCC00_2028, 0xCC00_202A),
    ];

    private static readonly uint[] ViFramebufferRegisters =
    [
        0xCC00_201C,
        0xCC00_2020,
        0xCC00_2024,
        0xCC00_2028,
    ];

    public static void WriteRegisters(TextWriter writer, PowerPcState state)
    {
        writer.WriteLine($"PC=0x{state.Pc:X8} LR=0x{state.Lr:X8} CTR=0x{state.Ctr:X8} CR=0x{state.Cr:X8} XER=0x{state.Xer:X8} MSR=0x{state.Msr:X8} DEC=0x{state.Spr[22]:X8} SRR0=0x{state.Spr[26]:X8} SRR1=0x{state.Spr[27]:X8}");

        for (int row = 0; row < 8; row++)
        {
            int baseRegister = row * 4;
            writer.WriteLine(
                $"r{baseRegister:D2}=0x{state.Gpr[baseRegister]:X8} " +
                $"r{baseRegister + 1:D2}=0x{state.Gpr[baseRegister + 1]:X8} " +
                $"r{baseRegister + 2:D2}=0x{state.Gpr[baseRegister + 2]:X8} " +
                $"r{baseRegister + 3:D2}=0x{state.Gpr[baseRegister + 3]:X8}");
        }
    }

    public static void WriteMmioSummary(TextWriter writer, IReadOnlyList<MmioAccess> accesses)
    {
        writer.WriteLine($"MMIO accesses: {accesses.Count}");
        if (accesses.Count != 0)
        {
            WriteMmioDeviceSummary(writer, accesses);
            WriteMmioAddressSummary(writer, accesses);
            WriteMmioFinalValues(writer, accesses);
            WriteViFramebufferCandidates(writer, accesses);
            WriteGxFifoPreview(writer, accesses);
        }

        foreach (MmioAccess access in accesses.Take(MaxMmioAccessesToPrint))
        {
            WriteMmioAccess(writer, access);
        }

        if (accesses.Count > MaxMmioAccessesToPrint)
        {
            writer.WriteLine($"... omitted {accesses.Count - MaxMmioAccessesToPrint} additional MMIO access(es)");
            writer.WriteLine($"Last {Math.Min(MaxMmioTailAccessesToPrint, accesses.Count - MaxMmioAccessesToPrint)} MMIO access(es):");
            foreach (MmioAccess access in accesses.Skip(Math.Max(MaxMmioAccessesToPrint, accesses.Count - MaxMmioTailAccessesToPrint)))
            {
                WriteMmioAccess(writer, access);
            }
        }
    }

    private static void WriteMmioDeviceSummary(TextWriter writer, IReadOnlyList<MmioAccess> accesses)
    {
        writer.WriteLine("MMIO by device:");
        foreach (var device in accesses
            .GroupBy(access => access.DeviceName)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key))
        {
            int reads = device.Count(access => access.Kind == MmioAccessKind.Read);
            int writes = device.Count(access => access.Kind == MmioAccessKind.Write);
            writer.WriteLine($"{device.Key,-9} {device.Count(),10}  read={reads,8} write={writes,8}");
        }
    }

    private static void WriteMmioAddressSummary(TextWriter writer, IReadOnlyList<MmioAccess> accesses)
    {
        writer.WriteLine("MMIO hot addresses:");
        foreach (var address in accesses
            .GroupBy(access => (access.DeviceName, access.Address))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.DeviceName)
            .ThenBy(group => group.Key.Address)
            .Take(16))
        {
            int reads = address.Count(access => access.Kind == MmioAccessKind.Read);
            int writes = address.Count(access => access.Kind == MmioAccessKind.Write);
            writer.WriteLine($"{address.Key.DeviceName,-9} 0x{address.Key.Address:X8} {address.Count(),10}  read={reads,8} write={writes,8}");
        }
    }

    private static void WriteMmioFinalValues(TextWriter writer, IReadOnlyList<MmioAccess> accesses)
    {
        List<MmioAccess> finalWrites = accesses
            .Where(access => access.Kind == MmioAccessKind.Write && access.DeviceName != "GX FIFO")
            .GroupBy(access => (access.DeviceName, access.Address))
            .Select(group => group.Last())
            .OrderBy(access => access.DeviceName)
            .ThenBy(access => access.Address)
            .Take(MaxMmioFinalValuesToPrint + 1)
            .ToList();

        if (finalWrites.Count == 0)
        {
            return;
        }

        writer.WriteLine("MMIO final written values:");
        foreach (MmioAccess access in finalWrites.Take(MaxMmioFinalValuesToPrint))
        {
            writer.WriteLine($"{access.DeviceName,-9} 0x{access.Address:X8}/{access.Width} = 0x{access.Value:X8}");
        }

        if (finalWrites.Count > MaxMmioFinalValuesToPrint)
        {
            writer.WriteLine($"... omitted additional MMIO final value(s)");
        }
    }

    private static void WriteViFramebufferCandidates(TextWriter writer, IReadOnlyList<MmioAccess> accesses)
    {
        Dictionary<uint, MmioAccess> finalViWrites = accesses
            .Where(access => access.Kind == MmioAccessKind.Write && access.DeviceName == "VI")
            .GroupBy(access => access.Address)
            .ToDictionary(group => group.Key, group => group.Last());

        bool wroteHeader = false;
        foreach ((string label, uint highRegister, uint lowRegister) in ViFramebufferRegisterPairs)
        {
            if (!finalViWrites.TryGetValue(highRegister, out MmioAccess? high)
                || !finalViWrites.TryGetValue(lowRegister, out MmioAccess? low))
            {
                continue;
            }

            uint raw = CombineVideoInterfaceAddress(high.Value, low.Value);
            WriteViFramebufferHeader(writer, ref wroteHeader);
            writer.WriteLine($"  {label} split 0x{highRegister:X8}/0x{lowRegister:X8} raw=0x{raw:X8} shifted=0x{ShiftVideoInterfaceAddress(raw):X8} normalized={FormatVideoInterfaceAddress(raw, preferShifted: false)}");
        }

        foreach (uint register in ViFramebufferRegisters)
        {
            if (!finalViWrites.TryGetValue(register, out MmioAccess? access))
            {
                continue;
            }

            if (access.Width != sizeof(uint))
            {
                continue;
            }

            uint raw = access.Value;
            WriteViFramebufferHeader(writer, ref wroteHeader);
            writer.WriteLine($"  direct 0x{register:X8} raw=0x{raw:X8} shifted=0x{ShiftVideoInterfaceAddress(raw):X8} normalized={FormatVideoInterfaceAddress(raw, preferShifted: true)}");
        }
    }

    private static void WriteViFramebufferHeader(TextWriter writer, ref bool wroteHeader)
    {
        if (wroteHeader)
        {
            return;
        }

        writer.WriteLine("VI framebuffer candidates:");
        wroteHeader = true;
    }

    private static uint CombineVideoInterfaceAddress(uint highValue, uint lowValue) =>
        ((highValue & 0xFF) << 16) | (lowValue & 0xFFFF);

    private static uint ShiftVideoInterfaceAddress(uint value) =>
        (value & 0x00FF_FFFF) << 5;

    private static string FormatVideoInterfaceAddress(uint value, bool preferShifted) =>
        TryNormalizeVideoInterfaceAddress(value, preferShifted, out uint address)
            ? $"0x{address:X8}"
            : "<unmapped>";

    private static bool TryNormalizeVideoInterfaceAddress(uint value, bool preferShifted, out uint address)
    {
        uint shifted = ShiftVideoInterfaceAddress(value);
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

    private static void WriteGxFifoPreview(TextWriter writer, IReadOnlyList<MmioAccess> accesses)
    {
        byte[] bytes = accesses
            .Where(access => access.DeviceName == "GX FIFO" && access.Kind == MmioAccessKind.Write)
            .SelectMany(ExpandMmioWriteBytes)
            .ToArray();

        if (bytes.Length == 0)
        {
            return;
        }

        WriteGxFifoCommandSummary(writer, bytes);

        int previewLength = Math.Min(bytes.Length, 512);
        writer.WriteLine($"GX FIFO byte preview: first {previewLength} of {bytes.Length} captured byte(s)");
        for (int row = 0; row < previewLength; row += 16)
        {
            int count = Math.Min(16, previewLength - row);
            writer.Write($"  +0x{row:X4} ");
            for (int index = 0; index < 16; index++)
            {
                writer.Write(index < count ? $"{bytes[row + index]:X2} " : "   ");
            }

            writer.WriteLine();
        }

        writer.WriteLine("GX FIFO command preview:");
        int offset = 0;
        int commands = 0;
        GxFifoDecodeState state = new();
        while (offset < bytes.Length && commands < 256)
        {
            int start = offset;
            byte command = bytes[offset++];
            commands++;

            if (command == 0x00)
            {
                writer.WriteLine($"  +0x{start:X4} NOP");
                continue;
            }

            if (command == 0x08 && TryReadBigEndian(bytes, offset, 1, out uint cpRegister) && TryReadBigEndian(bytes, offset + 1, 4, out uint cpValue))
            {
                state.WriteCpRegister((byte)cpRegister, cpValue);
                writer.WriteLine($"  +0x{start:X4} CP register 0x{cpRegister:X2} <= 0x{cpValue:X8}");
                offset += 5;
                continue;
            }

            if (command == 0x10 && TryReadBigEndian(bytes, offset, 4, out uint xfHeader))
            {
                uint xfRegister = xfHeader & 0xFFFF;
                uint xfWordCount = ((xfHeader >> 16) & 0xFFFF) + 1;
                writer.WriteLine($"  +0x{start:X4} XF register 0x{xfRegister:X4}, {xfWordCount} word(s)");
                int payloadBytes = checked((int)Math.Min(xfWordCount, (uint)((bytes.Length - offset - 4) / 4)) * 4);
                offset += 4 + payloadBytes;
                continue;
            }

            if (command is 0x20 or 0x28 or 0x30 or 0x38 && TryReadBigEndian(bytes, offset, 4, out uint indexedValue))
            {
                uint index = indexedValue >> 16;
                uint xfRegister = indexedValue & 0xFFF;
                uint xfWordCount = ((indexedValue >> 12) & 0xF) + 1;
                char array = (char)('A' + ((command - 0x20) / 8));
                writer.WriteLine($"  +0x{start:X4} Indexed XF {array}: index=0x{index:X4}, register=0x{xfRegister:X3}, {xfWordCount} word(s)");
                offset += 4;
                continue;
            }

            if (command == 0x40 && TryReadBigEndian(bytes, offset, 4, out uint displayListAddress) && TryReadBigEndian(bytes, offset + 4, 4, out uint displayListSize))
            {
                writer.WriteLine($"  +0x{start:X4} Display list 0x{displayListAddress & ~31u:X8} + 0x{displayListSize & ~31u:X}");
                offset += 8;
                continue;
            }

            if (command == 0x44)
            {
                writer.WriteLine($"  +0x{start:X4} Metrics command");
                continue;
            }

            if (command == 0x48)
            {
                writer.WriteLine($"  +0x{start:X4} Invalidate vertex cache");
                continue;
            }

            if (command == 0x61 && TryReadBigEndian(bytes, offset, 4, out uint bpValue))
            {
                writer.WriteLine($"  +0x{start:X4} BP register 0x{bpValue >> 24:X2} <= 0x{bpValue & 0x00FF_FFFF:X6}");
                offset += 4;
                continue;
            }

            if ((command & 0x80) != 0 && TryReadBigEndian(bytes, offset, 2, out uint vertexCount))
            {
                int format = command & 7;
                if (!state.TryGetVertexStride(format, out int vertexStride, out string vertexLayout))
                {
                    writer.WriteLine($"  +0x{start:X4} Draw command 0x{command:X2}, {vertexCount} vertices; vertex stride unknown ({vertexLayout}), stopping preview");
                    break;
                }

                long payloadBytes = (long)vertexCount * vertexStride;
                writer.WriteLine($"  +0x{start:X4} Draw {GxPrimitiveName(command)}, fmt={format}, {vertexCount} vertices, stride={vertexStride} ({vertexLayout})");
                if (payloadBytes > bytes.Length - offset - 2)
                {
                    writer.WriteLine($"  +0x{start:X4} Draw payload truncated, stopping preview");
                    break;
                }

                offset += 2 + (int)payloadBytes;
                continue;
            }

            writer.WriteLine($"  +0x{start:X4} Command 0x{command:X2}; decoder incomplete, stopping preview");
            break;
        }
    }

    private static void WriteGxFifoCommandSummary(TextWriter writer, byte[] bytes)
    {
        int offset = 0;
        int commands = 0;
        int nops = 0;
        int bpCommands = 0;
        int cpCommands = 0;
        int xfCommands = 0;
        int indexedXfCommands = 0;
        int displayLists = 0;
        int metricsCommands = 0;
        int invalidateCommands = 0;
        int drawCommands = 0;
        int drawVertices = 0;
        Dictionary<byte, int> drawCommandsByPrimitive = [];
        Dictionary<int, int> drawCommandsByFormat = [];
        int? firstDisplayListOffset = null;
        string? firstDisplayList = null;
        int? firstDrawOffset = null;
        string? firstDraw = null;
        string? firstDrawLayout = null;
        int? unknownOffset = null;
        byte unknownCommand = 0;
        Queue<string> recentCommands = [];
        string[] commandsBeforeFirstDraw = [];
        List<string> firstDraws = [];
        GxFifoDecodeState state = new();

        while (offset < bytes.Length)
        {
            int start = offset;
            byte command = bytes[offset++];
            commands++;

            if (command == 0x00)
            {
                nops++;
                EnqueueRecentGxCommand(recentCommands, $"+0x{start:X4} NOP");
                continue;
            }

            if (command == 0x08 && TryReadBigEndian(bytes, offset, 1, out uint cpRegister) && TryReadBigEndian(bytes, offset + 1, 4, out uint cpValue))
            {
                cpCommands++;
                state.WriteCpRegister((byte)cpRegister, cpValue);
                EnqueueRecentGxCommand(recentCommands, $"+0x{start:X4} CP register 0x{cpRegister:X2} <= 0x{cpValue:X8}");
                offset += 5;
                continue;
            }

            if (command == 0x10 && TryReadBigEndian(bytes, offset, 4, out uint xfHeader))
            {
                xfCommands++;
                uint xfRegister = xfHeader & 0xFFFF;
                uint xfWordCount = ((xfHeader >> 16) & 0xFFFF) + 1;
                EnqueueRecentGxCommand(recentCommands, $"+0x{start:X4} XF register 0x{xfRegister:X4}, {xfWordCount} word(s)");
                offset += 4 + AvailablePayloadBytes(bytes, offset + 4, xfWordCount);
                continue;
            }

            if (command is 0x20 or 0x28 or 0x30 or 0x38 && TryReadBigEndian(bytes, offset, 4, out uint indexedValue))
            {
                indexedXfCommands++;
                uint index = indexedValue >> 16;
                uint xfRegister = indexedValue & 0xFFF;
                uint xfWordCount = ((indexedValue >> 12) & 0xF) + 1;
                char array = (char)('A' + ((command - 0x20) / 8));
                EnqueueRecentGxCommand(recentCommands, $"+0x{start:X4} Indexed XF {array}: index=0x{index:X4}, register=0x{xfRegister:X3}, {xfWordCount} word(s)");
                offset += 4;
                continue;
            }

            if (command == 0x40 && TryReadBigEndian(bytes, offset, 4, out uint displayListAddress) && TryReadBigEndian(bytes, offset + 4, 4, out uint displayListSize))
            {
                displayLists++;
                firstDisplayListOffset ??= start;
                firstDisplayList ??= $"0x{displayListAddress & ~31u:X8} + 0x{displayListSize & ~31u:X}";
                EnqueueRecentGxCommand(recentCommands, $"+0x{start:X4} Display list {firstDisplayList}");
                offset += 8;
                continue;
            }

            if (command == 0x44)
            {
                metricsCommands++;
                EnqueueRecentGxCommand(recentCommands, $"+0x{start:X4} Metrics command");
                continue;
            }

            if (command == 0x48)
            {
                invalidateCommands++;
                EnqueueRecentGxCommand(recentCommands, $"+0x{start:X4} Invalidate vertex cache");
                continue;
            }

            if (command == 0x61 && TryReadBigEndian(bytes, offset, 4, out uint bpValue))
            {
                bpCommands++;
                EnqueueRecentGxCommand(recentCommands, $"+0x{start:X4} BP register 0x{bpValue >> 24:X2} <= 0x{bpValue & 0x00FF_FFFF:X6}");
                offset += 4;
                continue;
            }

            if ((command & 0x80) != 0 && TryReadBigEndian(bytes, offset, 2, out uint vertexCount))
            {
                int format = command & 7;
                if (!state.TryGetVertexStride(format, out int vertexStride, out string vertexLayout))
                {
                    firstDrawOffset ??= start;
                    firstDraw ??= $"0x{command:X2}, {vertexCount} vertices; vertex stride unknown ({vertexLayout})";
                    commandsBeforeFirstDraw = commandsBeforeFirstDraw.Length == 0 ? recentCommands.ToArray() : commandsBeforeFirstDraw;
                    unknownOffset = start;
                    unknownCommand = command;
                    break;
                }

                long payloadBytes = (long)vertexCount * vertexStride;
                if (payloadBytes > bytes.Length - offset - 2)
                {
                    firstDrawOffset ??= start;
                    firstDraw ??= $"0x{command:X2}, {vertexCount} vertices; payload truncated";
                    commandsBeforeFirstDraw = commandsBeforeFirstDraw.Length == 0 ? recentCommands.ToArray() : commandsBeforeFirstDraw;
                    unknownOffset = start;
                    unknownCommand = command;
                    break;
                }

                drawCommands++;
                drawVertices += (int)Math.Min(vertexCount, int.MaxValue - drawVertices);
                byte primitive = (byte)(command & 0xF8);
                drawCommandsByPrimitive[primitive] = drawCommandsByPrimitive.GetValueOrDefault(primitive) + 1;
                drawCommandsByFormat[format] = drawCommandsByFormat.GetValueOrDefault(format) + 1;
                string drawSummary = $"+0x{start:X4} Draw {GxPrimitiveName(command)}, fmt={format}, {vertexCount} vertices, stride={vertexStride}";
                if (firstDraws.Count < 12)
                {
                    firstDraws.Add(drawSummary);
                }

                if (firstDrawOffset is null)
                {
                    firstDrawOffset = start;
                    firstDraw = $"0x{command:X2} {GxPrimitiveName(command)}, fmt={format}, {vertexCount} vertices, stride={vertexStride}";
                    firstDrawLayout = vertexLayout;
                    commandsBeforeFirstDraw = recentCommands.ToArray();
                }

                EnqueueRecentGxCommand(recentCommands, drawSummary);
                offset += 2 + (int)payloadBytes;
                continue;
            }

            unknownOffset = start;
            unknownCommand = command;
            break;
        }

        writer.WriteLine("GX FIFO command summary:");
        writer.WriteLine($"  captured bytes: {bytes.Length}");
        writer.WriteLine($"  recognized commands before stop/end: {commands}");
        writer.WriteLine($"  BP={bpCommands} CP={cpCommands} XF={xfCommands} indexedXF={indexedXfCommands} displayList={displayLists} metrics={metricsCommands} invalidate={invalidateCommands} nop={nops} draws={drawCommands}");
        if (drawCommands != 0)
        {
            writer.WriteLine($"  draw vertices: {drawVertices}");
            writer.WriteLine($"  draw primitives: {FormatCountMap(drawCommandsByPrimitive, key => GxPrimitiveName(key))}");
            writer.WriteLine($"  draw formats: {FormatCountMap(drawCommandsByFormat, key => key.ToString())}");
        }

        if (firstDisplayListOffset is not null)
        {
            writer.WriteLine($"  first display list: +0x{firstDisplayListOffset.Value:X} {firstDisplayList}");
        }

        if (firstDrawOffset is not null)
        {
            writer.WriteLine($"  first draw: +0x{firstDrawOffset.Value:X} {firstDraw}");
            if (!string.IsNullOrEmpty(firstDrawLayout))
            {
                writer.WriteLine($"  first draw layout: {firstDrawLayout}");
            }

            if (firstDraws.Count != 0)
            {
                writer.WriteLine("  first parsed draws:");
                foreach (string draw in firstDraws)
                {
                    writer.WriteLine($"    {draw}");
                }
            }

            if (commandsBeforeFirstDraw.Length != 0)
            {
                writer.WriteLine("  commands before first draw:");
                foreach (string command in commandsBeforeFirstDraw)
                {
                    writer.WriteLine($"    {command}");
                }
            }

            WriteGxFifoFirstDrawBytes(writer, bytes, firstDrawOffset.Value);
        }
        else if (unknownOffset is not null)
        {
            writer.WriteLine($"  stopped on unknown command: +0x{unknownOffset.Value:X} 0x{unknownCommand:X2}");
        }
        else
        {
            writer.WriteLine(drawCommands == 0
                ? "  no draw command encountered in captured stream"
                : "  parsed complete captured stream");
        }
    }

    private static void WriteGxFifoFirstDrawBytes(TextWriter writer, byte[] bytes, int drawOffset)
    {
        int start = drawOffset;
        int count = Math.Min(96, bytes.Length - start);
        if (count <= 0)
        {
            return;
        }

        writer.WriteLine($"  first draw bytes: first {count} byte(s) from +0x{start:X}");
        for (int row = 0; row < count; row += 16)
        {
            int rowCount = Math.Min(16, count - row);
            writer.Write($"    +0x{start + row:X4} ");
            for (int index = 0; index < 16; index++)
            {
                writer.Write(index < rowCount ? $"{bytes[start + row + index]:X2} " : "   ");
            }

            writer.WriteLine();
        }
    }

    private static void EnqueueRecentGxCommand(Queue<string> recentCommands, string command)
    {
        recentCommands.Enqueue(command);
        while (recentCommands.Count > 24)
        {
            recentCommands.Dequeue();
        }
    }

    private static string GxPrimitiveName(int command) =>
        (command & 0xF8) switch
        {
            0x80 => "quads",
            0x90 => "triangles",
            0x98 => "triangle-strip",
            0xA0 => "triangle-fan",
            0xA8 => "lines",
            0xB0 => "line-strip",
            0xB8 => "points",
            _ => $"primitive-0x{command & 0xF8:X2}",
        };

    private static string FormatCountMap<TKey>(IReadOnlyDictionary<TKey, int> values, Func<TKey, string> formatKey)
        where TKey : notnull =>
        string.Join(", ", values.OrderBy(entry => formatKey(entry.Key), StringComparer.Ordinal).Select(entry => $"{formatKey(entry.Key)}={entry.Value}"));

    private sealed class GxFifoDecodeState
    {
        private readonly uint[] _vcdLow = new uint[8];
        private readonly uint[] _vcdHigh = new uint[8];
        private readonly uint[] _vatA = new uint[8];
        private readonly uint[] _vatB = new uint[8];
        private readonly uint[] _vatC = new uint[8];

        public void WriteCpRegister(byte register, uint value)
        {
            if (register is >= 0x50 and <= 0x57)
            {
                _vcdLow[register - 0x50] = value;
                return;
            }

            if (register is >= 0x60 and <= 0x67)
            {
                _vcdHigh[register - 0x60] = value;
                return;
            }

            if (register is >= 0x70 and <= 0x77)
            {
                _vatA[register - 0x70] = value;
                return;
            }

            if (register is >= 0x80 and <= 0x87)
            {
                _vatB[register - 0x80] = value;
                return;
            }

            if (register is >= 0x90 and <= 0x97)
            {
                _vatC[register - 0x90] = value;
            }
        }

        public bool TryGetVertexStride(int format, out int stride, out string layout)
        {
            stride = 0;
            layout = string.Empty;
            if (format is < 0 or >= 8)
            {
                layout = "invalid vertex format";
                return false;
            }

            uint vcdLow = _vcdLow[format];
            uint vcdHigh = _vcdHigh[format];
            uint vatA = _vatA[format];
            uint vatB = _vatB[format];
            uint vatC = _vatC[format];
            List<string> attributes = [];

            AddMatrixIndex(vcdLow, 0, "pnmtx", ref stride, attributes);
            for (int index = 0; index < 8; index++)
            {
                AddMatrixIndex(vcdLow, index + 1, $"tex{index}mtx", ref stride, attributes);
            }

            if (!AddAttribute((vcdLow >> 9) & 3, DirectComponentBytes(((vatA >> 1) & 7), ((vatA & 1) == 0 ? 2 : 3)), "pos", ref stride, attributes))
            {
                layout = "unsupported position format";
                return false;
            }

            int normalComponents = ((vatA >> 9) & 1) == 0 ? 3 : 9;
            if (!AddAttribute((vcdLow >> 11) & 3, DirectComponentBytes(((vatA >> 10) & 7), normalComponents), "nrm", ref stride, attributes, indexedMultiplier: ((vatA >> 31) & 1) == 0 ? 1 : 3))
            {
                layout = "unsupported normal format";
                return false;
            }

            if (!AddAttribute((vcdLow >> 13) & 3, ColorBytes((vatA >> 14) & 7), "col0", ref stride, attributes))
            {
                layout = "unsupported color0 format";
                return false;
            }

            if (!AddAttribute((vcdLow >> 15) & 3, ColorBytes((vatA >> 18) & 7), "col1", ref stride, attributes))
            {
                layout = "unsupported color1 format";
                return false;
            }

            if (!AddTexAttribute((vcdHigh >> 0) & 3, vatA, 21, 22, "tex0", ref stride, attributes)
                || !AddTexAttribute((vcdHigh >> 2) & 3, vatB, 0, 1, "tex1", ref stride, attributes)
                || !AddTexAttribute((vcdHigh >> 4) & 3, vatB, 9, 10, "tex2", ref stride, attributes)
                || !AddTexAttribute((vcdHigh >> 6) & 3, vatB, 18, 19, "tex3", ref stride, attributes)
                || !AddTexAttribute((vcdHigh >> 8) & 3, vatB, 27, 28, "tex4", ref stride, attributes)
                || !AddTexAttribute((vcdHigh >> 10) & 3, vatC, 5, 6, "tex5", ref stride, attributes)
                || !AddTexAttribute((vcdHigh >> 12) & 3, vatC, 14, 15, "tex6", ref stride, attributes)
                || !AddTexAttribute((vcdHigh >> 14) & 3, vatC, 23, 24, "tex7", ref stride, attributes))
            {
                layout = "unsupported texture coordinate format";
                return false;
            }

            layout = attributes.Count == 0 ? "no vertex attributes" : string.Join(", ", attributes);
            return true;
        }

        private static void AddMatrixIndex(uint vcdLow, int bit, string name, ref int stride, List<string> attributes)
        {
            if (((vcdLow >> bit) & 1) == 0)
            {
                return;
            }

            stride++;
            attributes.Add($"{name}:u8");
        }

        private static bool AddTexAttribute(uint descriptor, uint vat, int countBit, int formatBit, string name, ref int stride, List<string> attributes)
        {
            int components = ((vat >> countBit) & 1) == 0 ? 1 : 2;
            int directBytes = DirectComponentBytes((vat >> formatBit) & 7, components);
            return AddAttribute(descriptor, directBytes, name, ref stride, attributes);
        }

        private static bool AddAttribute(uint descriptor, int directBytes, string name, ref int stride, List<string> attributes, int indexedMultiplier = 1)
        {
            switch (descriptor)
            {
                case 0:
                    return true;
                case 1 when directBytes > 0:
                    stride += directBytes;
                    attributes.Add($"{name}:direct/{directBytes}");
                    return true;
                case 2:
                    stride += indexedMultiplier;
                    attributes.Add($"{name}:idx8/{indexedMultiplier}");
                    return true;
                case 3:
                    stride += indexedMultiplier * 2;
                    attributes.Add($"{name}:idx16/{indexedMultiplier * 2}");
                    return true;
                default:
                    return false;
            }
        }

        private static int DirectComponentBytes(uint format, int components) =>
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
    }

    private static int AvailablePayloadBytes(byte[] bytes, int offset, uint wordCount)
    {
        int availableWords = Math.Max(0, bytes.Length - offset) / sizeof(uint);
        int consumedWords = (int)Math.Min(wordCount, (uint)availableWords);
        return checked(consumedWords * sizeof(uint));
    }

    private static IEnumerable<byte> ExpandMmioWriteBytes(MmioAccess access)
    {
        for (int shift = (access.Width - 1) * 8; shift >= 0; shift -= 8)
        {
            yield return (byte)(access.Value >> shift);
        }
    }

    private static bool TryReadBigEndian(byte[] bytes, int offset, int width, out uint value)
    {
        value = 0;
        if (offset < 0 || width < 0 || offset > bytes.Length - width)
        {
            return false;
        }

        for (int index = 0; index < width; index++)
        {
            value = (value << 8) | bytes[offset + index];
        }

        return true;
    }

    public static void WritePcProfile(TextWriter writer, IReadOnlyDictionary<uint, ulong> profile, int topCount, int executedInstructions, int profileAfter = 0)
    {
        int profiledInstructions = Math.Max(0, executedInstructions - profileAfter);
        string windowSuffix = profileAfter == 0 ? string.Empty : $" after {profileAfter} instruction(s)";
        writer.WriteLine($"PC profile{windowSuffix}: {profile.Count} unique address(es)");

        foreach (KeyValuePair<uint, ulong> entry in profile.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).Take(topCount))
        {
            double percent = profiledInstructions == 0 ? 0 : (double)entry.Value * 100 / profiledInstructions;
            writer.WriteLine($"0x{entry.Key:X8}  {entry.Value,10}  {percent,6:F2}%");
        }
    }

    public static void WriteMemoryDump(TextWriter writer, GameCubeMemory memory, uint address, int length)
    {
        writer.WriteLine($"Memory dump 0x{address:X8} + 0x{length:X}:");
        for (int row = 0; row < length; row += 16)
        {
            int count = Math.Min(16, length - row);
            writer.Write($"0x{address + (uint)row:X8}  ");
            for (int i = 0; i < 16; i++)
            {
                writer.Write(i < count ? $"{memory.Read8(address + (uint)row + (uint)i):X2} " : "   ");
            }

            writer.Write(" ");
            for (int i = 0; i < count; i++)
            {
                byte value = memory.Read8(address + (uint)row + (uint)i);
                writer.Write(value is >= 0x20 and <= 0x7E ? (char)value : '.');
            }

            writer.WriteLine();
        }
    }

    public static void WritePointerTableDump(TextWriter writer, GameCubeMemory memory, PointerTableDumpRequest request)
    {
        writer.WriteLine($"Pointer table dump 0x{request.Address:X8}: count={request.Count} stride=0x{request.Stride:X} pointerOffset=0x{request.PointerOffset:X} targetWords={request.TargetWords}");

        int printed = 0;
        int active = 0;
        int nullPointers = 0;
        int invalidPointers = 0;
        int entryWords = Math.Min(request.Stride / sizeof(uint), 4);
        for (int index = 0; index < request.Count; index++)
        {
            uint entryAddress = request.Address + checked((uint)(index * request.Stride));
            uint pointer;
            try
            {
                pointer = memory.Read32(entryAddress + (uint)request.PointerOffset);
            }
            catch (AddressTranslationException)
            {
                writer.WriteLine($"  [{index:D4}] 0x{entryAddress:X8}: entry outside main RAM");
                invalidPointers++;
                continue;
            }

            if (pointer == 0)
            {
                nullPointers++;
                continue;
            }

            active++;
            if (!TryNormalizeMainRamAddress(pointer, out uint targetAddress))
            {
                invalidPointers++;
                writer.WriteLine($"  [{index:D4}] 0x{entryAddress:X8}: ptr=0x{pointer:X8} outside main RAM entry={FormatEntryWords(memory, entryAddress, entryWords)}");
                printed++;
                continue;
            }

            writer.WriteLine($"  [{index:D4}] 0x{entryAddress:X8}: ptr=0x{targetAddress:X8}{FormatRawPointerAlias(pointer, targetAddress)} entry={FormatEntryWords(memory, entryAddress, entryWords)} target={FormatTargetWords(memory, targetAddress, request.TargetWords)}");
            printed++;
        }

        writer.WriteLine($"Pointer table summary: active={active} null={nullPointers} invalid={invalidPointers} printed={printed}");
    }

    private static string FormatEntryWords(GameCubeMemory memory, uint address, int words)
    {
        if (words <= 0)
        {
            return "[]";
        }

        List<string> values = new(words);
        for (int index = 0; index < words; index++)
        {
            uint wordAddress = address + (uint)(index * sizeof(uint));
            try
            {
                values.Add($"0x{memory.Read32(wordAddress):X8}");
            }
            catch (AddressTranslationException)
            {
                values.Add("<truncated>");
                break;
            }
        }

        return $"[{string.Join(",", values)}]";
    }

    private static string FormatTargetWords(GameCubeMemory memory, uint address, int words)
    {
        if (words <= 0)
        {
            return "[]";
        }

        List<string> values = new(words);
        for (int index = 0; index < words; index++)
        {
            uint wordAddress = address + (uint)(index * sizeof(uint));
            try
            {
                values.Add($"0x{memory.Read32(wordAddress):X8}");
            }
            catch (AddressTranslationException)
            {
                values.Add("<truncated>");
                break;
            }
        }

        return $"[{string.Join(",", values)}]";
    }

    private static string FormatRawPointerAlias(uint pointer, uint targetAddress) =>
        pointer == targetAddress ? string.Empty : $" (raw 0x{pointer:X8})";

    public static void WriteThreadSummary(TextWriter writer, GameCubeMemory memory)
    {
        writer.WriteLine("Thread summary:");
        WriteThreadPointer(writer, memory, "threadQueueHead", 0x8000_00C0);
        WriteThreadPointer(writer, memory, "defaultThread", 0x8000_00D8);
        WriteThreadPointer(writer, memory, "currentThread0", 0x8000_00E0);
        WriteThreadPointer(writer, memory, "currentThread4", 0x8000_00E4);
        WriteThreadChain(writer, memory, "currentThread0", memory.Read32(0x8000_00E0));
        WriteThreadChain(writer, memory, "threadQueueHead", memory.Read32(0x8000_00C0));
        WriteThreadScan(writer, memory);
    }

    public static void WriteMessageQueueSummary(TextWriter writer, GameCubeMemory memory)
    {
        const int maxQueuesToPrint = 128;
        int printed = 0;
        int matches = 0;

        writer.WriteLine("Active message queues:");
        for (int offset = 0; offset <= memory.MainRamSize - 0x20; offset += sizeof(uint))
        {
            uint queueAddress = GameCubeAddress.MainRamCachedStart + (uint)offset;
            if (!TryDescribeActiveMessageQueue(memory, queueAddress, out string? description))
            {
                continue;
            }

            matches++;
            if (printed < maxQueuesToPrint)
            {
                writer.WriteLine($"  {description}");
                printed++;
            }
        }

        if (matches == 0)
        {
            writer.WriteLine("  none");
        }
        else if (matches > printed)
        {
            writer.WriteLine($"  ... omitted {matches - printed} additional queue(s)");
        }
    }

    private static void WriteThreadPointer(TextWriter writer, GameCubeMemory memory, string name, uint pointerAddress)
    {
        uint rawThreadAddress = memory.Read32(pointerAddress);
        if (rawThreadAddress == 0)
        {
            writer.WriteLine($"{name,-16} 0x{pointerAddress:X8} -> 0x00000000");
            return;
        }

        if (!TryNormalizeMainRamAddress(rawThreadAddress, out uint threadAddress))
        {
            writer.WriteLine($"{name,-16} 0x{pointerAddress:X8} -> 0x{rawThreadAddress:X8} (outside main RAM)");
            return;
        }

        try
        {
            uint stackPointer = memory.Read32(threadAddress + 0x04);
            uint savedLr = memory.Read32(threadAddress + 0x84);
            uint savedSrr0 = memory.Read32(threadAddress + 0x198);
            uint savedSrr1 = memory.Read32(threadAddress + 0x19C);
            ushort flags = memory.Read16(threadAddress + 0x1A2);
            ushort state = memory.Read16(threadAddress + 0x2C8);
            uint waitQueue = memory.Read32(threadAddress + 0x2DC);
            uint queueNext = memory.Read32(threadAddress + 0x2E0);
            uint queuePrev = memory.Read32(threadAddress + 0x2E4);
            string alias = rawThreadAddress == threadAddress ? string.Empty : $" (raw 0x{rawThreadAddress:X8})";
            writer.WriteLine($"{name,-16} 0x{pointerAddress:X8} -> 0x{threadAddress:X8}{alias} state=0x{state:X4} flags=0x{flags:X4} sp=0x{stackPointer:X8} lr=0x{savedLr:X8} srr0=0x{savedSrr0:X8} srr1=0x{savedSrr1:X8} waitq=0x{waitQueue:X8} qnext=0x{queueNext:X8} qprev=0x{queuePrev:X8}{FormatThreadWaitObject(memory, state, waitQueue)}");
        }
        catch (AddressTranslationException)
        {
            writer.WriteLine($"{name,-16} 0x{pointerAddress:X8} -> 0x{threadAddress:X8} (thread record truncated)");
        }
    }

    private static void WriteThreadChain(TextWriter writer, GameCubeMemory memory, string name, uint startAddress)
    {
        if (startAddress == 0 || !TryNormalizeMainRamAddress(startAddress, out startAddress))
        {
            return;
        }

        writer.WriteLine($"{name} chain:");
        HashSet<uint> seen = [];
        uint threadAddress = startAddress;
        for (int index = 0; index < 12 && threadAddress != 0 && seen.Add(threadAddress); index++)
        {
            if (!TryNormalizeMainRamAddress(threadAddress, out threadAddress))
            {
                writer.WriteLine($"  [{index}] 0x{threadAddress:X8} outside main RAM");
                break;
            }

            try
            {
                ushort state = memory.Read16(threadAddress + 0x2C8);
                ushort flags = memory.Read16(threadAddress + 0x1A2);
                uint stackPointer = memory.Read32(threadAddress + 0x04);
                uint savedLr = memory.Read32(threadAddress + 0x84);
                uint savedSrr0 = memory.Read32(threadAddress + 0x198);
                uint waitQueue = memory.Read32(threadAddress + 0x2DC);
                uint queueNext = memory.Read32(threadAddress + 0x2E0);
                uint queuePrev = memory.Read32(threadAddress + 0x2E4);
                writer.WriteLine($"  [{index}] 0x{threadAddress:X8} state=0x{state:X4} flags=0x{flags:X4} sp=0x{stackPointer:X8} lr=0x{savedLr:X8} srr0=0x{savedSrr0:X8} waitq=0x{waitQueue:X8} qnext=0x{queueNext:X8} qprev=0x{queuePrev:X8}{FormatThreadWaitObject(memory, state, waitQueue)}");
                threadAddress = queueNext;
            }
            catch (AddressTranslationException)
            {
                writer.WriteLine($"  [{index}] 0x{threadAddress:X8} truncated");
                break;
            }
        }
    }

    private static void WriteThreadScan(TextWriter writer, GameCubeMemory memory)
    {
        List<uint> threadAddresses = [];
        ReadOnlySpan<byte> ram = memory.MainRam.Span;
        for (int offset = 0; offset <= ram.Length - 0x320; offset += 4)
        {
            uint address = GameCubeAddress.MainRamCachedStart + (uint)offset;
            uint stackPointer = BigEndian.ReadUInt32(ram.Slice(offset + 0x04, sizeof(uint)));
            uint savedSrr0 = BigEndian.ReadUInt32(ram.Slice(offset + 0x198, sizeof(uint)));
            uint savedSrr1 = BigEndian.ReadUInt32(ram.Slice(offset + 0x19C, sizeof(uint)));
            ushort flags = BigEndian.ReadUInt16(ram.Slice(offset + 0x1A2, sizeof(ushort)));
            ushort state = BigEndian.ReadUInt16(ram.Slice(offset + 0x2C8, sizeof(ushort)));
            uint next = BigEndian.ReadUInt32(ram.Slice(offset + 0x2DC, sizeof(uint)));
            uint prev = BigEndian.ReadUInt32(ram.Slice(offset + 0x2E0, sizeof(uint)));

            if (state is 0 or > 8
                || flags > 0xFF
                || !IsMainRamStack(stackPointer)
                || !IsMainRamCode(savedSrr0)
                || !IsZeroOrReasonableMsr(savedSrr1)
                || !IsZeroOrMainRam(next)
                || !IsZeroOrMainRam(prev))
            {
                continue;
            }

            uint savedLr = BigEndian.ReadUInt32(ram.Slice(offset + 0x84, sizeof(uint)));
            if (!IsMainRamCode(savedLr))
            {
                continue;
            }

            threadAddresses.Add(address);
        }

        if (threadAddresses.Count == 0)
        {
            return;
        }

        writer.WriteLine($"Plausible thread records: {threadAddresses.Count}");
        foreach (uint threadAddress in threadAddresses.Take(32))
        {
            WriteThreadRecord(writer, memory, threadAddress);
        }

        if (threadAddresses.Count > 32)
        {
            writer.WriteLine($"  ... omitted {threadAddresses.Count - 32} additional candidate(s)");
        }
    }

    private static void WriteThreadRecord(TextWriter writer, GameCubeMemory memory, uint threadAddress)
    {
        ushort state = memory.Read16(threadAddress + 0x2C8);
        ushort flags = memory.Read16(threadAddress + 0x1A2);
        uint stackPointer = memory.Read32(threadAddress + 0x04);
        uint savedLr = memory.Read32(threadAddress + 0x84);
        uint savedSrr0 = memory.Read32(threadAddress + 0x198);
        uint savedSrr1 = memory.Read32(threadAddress + 0x19C);
        uint waitQueue = memory.Read32(threadAddress + 0x2DC);
        uint queueNext = memory.Read32(threadAddress + 0x2E0);
        uint queuePrev = memory.Read32(threadAddress + 0x2E4);
        writer.WriteLine($"  0x{threadAddress:X8} state=0x{state:X4} flags=0x{flags:X4} sp=0x{stackPointer:X8} lr=0x{savedLr:X8} srr0=0x{savedSrr0:X8} srr1=0x{savedSrr1:X8} waitq=0x{waitQueue:X8} qnext=0x{queueNext:X8} qprev=0x{queuePrev:X8}{FormatThreadWaitObject(memory, state, waitQueue)}");
        WriteStackBacktrace(writer, memory, stackPointer, "    ");
    }

    private static string FormatThreadWaitObject(GameCubeMemory memory, ushort threadState, uint waitQueue)
    {
        if (threadState != 4 || waitQueue == 0 || !TryNormalizeMainRamAddress(waitQueue, out uint normalizedWaitQueue))
        {
            return string.Empty;
        }

        string waitQueueText = $" wait={DescribeWaitQueue(memory, normalizedWaitQueue)}";
        if (normalizedWaitQueue >= 8)
        {
            string receiveQueue = DescribeMessageQueue(memory, normalizedWaitQueue - 8, receiveQueueAddress: normalizedWaitQueue);
            if (receiveQueue.Length != 0)
            {
                return $"{waitQueueText} {receiveQueue}";
            }
        }

        string sendQueue = DescribeMessageQueue(memory, normalizedWaitQueue, receiveQueueAddress: normalizedWaitQueue + 8);
        return sendQueue.Length == 0 ? waitQueueText : $"{waitQueueText} {sendQueue}";
    }

    private static string DescribeWaitQueue(GameCubeMemory memory, uint queueAddress)
    {
        try
        {
            uint head = memory.Read32(queueAddress);
            uint tail = memory.Read32(queueAddress + 4);
            return $"queue@0x{queueAddress:X8}(head=0x{head:X8},tail=0x{tail:X8})";
        }
        catch (AddressTranslationException)
        {
            return $"queue@0x{queueAddress:X8}(truncated)";
        }
    }

    private static string DescribeMessageQueue(GameCubeMemory memory, uint queueAddress, uint receiveQueueAddress)
    {
        try
        {
            uint sendHead = memory.Read32(queueAddress);
            uint sendTail = memory.Read32(queueAddress + 4);
            uint receiveHead = memory.Read32(queueAddress + 8);
            uint receiveTail = memory.Read32(queueAddress + 12);
            uint buffer = memory.Read32(queueAddress + 0x10);
            uint capacity = memory.Read32(queueAddress + 0x14);
            uint readIndex = memory.Read32(queueAddress + 0x18);
            uint used = memory.Read32(queueAddress + 0x1C);
            if (capacity == 0
                || capacity > 0x10000
                || readIndex >= capacity
                || used > capacity
                || (used == 0 ? !IsZeroOrMainRam(buffer) : !IsEffectiveMainRamAddress(buffer, out _))
                || !IsZeroOrMainRam(sendHead)
                || !IsZeroOrMainRam(sendTail)
                || !IsZeroOrMainRam(receiveHead)
                || !IsZeroOrMainRam(receiveTail)
                || receiveQueueAddress != queueAddress + 8)
            {
                return string.Empty;
            }

            string side = receiveHead != 0 || receiveTail != 0 ? "recv" : "send";
            string contents = FormatMessageQueueContents(memory, buffer, capacity, readIndex, used);
            return $"msgq@0x{queueAddress:X8}({side},buffer=0x{buffer:X8},capacity={capacity},read={readIndex},used={used}{contents},sendHead=0x{sendHead:X8},recvHead=0x{receiveHead:X8})";
        }
        catch (AddressTranslationException)
        {
            return string.Empty;
        }
    }

    private static bool TryDescribeActiveMessageQueue(GameCubeMemory memory, uint queueAddress, out string? description)
    {
        description = null;
        try
        {
            if (queueAddress < 0x802E_0000)
            {
                return false;
            }

            uint sendHead = memory.Read32(queueAddress);
            uint sendTail = memory.Read32(queueAddress + 4);
            uint receiveHead = memory.Read32(queueAddress + 8);
            uint receiveTail = memory.Read32(queueAddress + 12);
            uint buffer = memory.Read32(queueAddress + 0x10);
            uint capacity = memory.Read32(queueAddress + 0x14);
            uint used = memory.Read32(queueAddress + 0x1C);
            if (sendHead == 0 && sendTail == 0 && receiveHead == 0 && receiveTail == 0 && used == 0)
            {
                return false;
            }

            if (capacity > 1024 || !IsEffectiveMainRamAddress(buffer, out _))
            {
                return false;
            }

            description = DescribeMessageQueue(memory, queueAddress, receiveQueueAddress: queueAddress + 8);
            return description.Length != 0;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static string FormatMessageQueueContents(GameCubeMemory memory, uint buffer, uint capacity, uint readIndex, uint used)
    {
        if (used == 0)
        {
            return string.Empty;
        }

        const uint maxMessages = 8;
        uint messagesToPrint = Math.Min(used, maxMessages);
        List<string> messages = new(checked((int)messagesToPrint));
        try
        {
            for (uint i = 0; i < messagesToPrint; i++)
            {
                uint index = (readIndex + i) % capacity;
                uint messageAddress = buffer + index * sizeof(uint);
                uint message = memory.Read32(messageAddress);
                messages.Add(FormatMessageQueueWord(memory, message));
            }
        }
        catch (AddressTranslationException)
        {
            return ",messages=<truncated>";
        }

        string omitted = used > maxMessages ? ",..." : string.Empty;
        return $",messages=[{string.Join(",", messages)}{omitted}]";
    }

    private static string FormatMessageQueueWord(GameCubeMemory memory, uint word)
    {
        if (!IsEffectiveMainRamAddress(word, out uint normalized))
        {
            return $"0x{word:X8}";
        }

        try
        {
            uint first = memory.Read32(normalized);
            uint second = memory.Read32(normalized + sizeof(uint));
            return $"0x{normalized:X8}->0x{first:X8}/0x{second:X8}";
        }
        catch (AddressTranslationException)
        {
            return $"0x{normalized:X8}-><truncated>";
        }
    }

    private static bool IsZeroOrMainRam(uint value) =>
        value == 0 || GameCubeAddress.TryTranslateMainRam(value, out _);

    private static bool IsMainRamCode(uint value) =>
        IsEffectiveMainRamAddress(value, out uint normalized) && normalized < 0x8030_0000;

    private static bool IsMainRamStack(uint value) =>
        IsEffectiveMainRamAddress(value, out uint normalized) && normalized >= 0x8030_0000;

    private static bool IsZeroOrReasonableMsr(uint value) =>
        value == 0 || (value & 0xFFFF_0000) == 0;

    private static void WriteStackBacktrace(TextWriter writer, GameCubeMemory memory, uint stackPointer, string indent)
    {
        if (!IsEffectiveMainRamAddress(stackPointer, out uint framePointer))
        {
            return;
        }

        List<string> frames = [];
        HashSet<uint> seen = [];
        for (int index = 0; index < 8 && seen.Add(framePointer); index++)
        {
            uint nextFrame;
            uint returnAddress;
            try
            {
                nextFrame = memory.Read32(framePointer);
                returnAddress = memory.Read32(framePointer + 4);
            }
            catch (AddressTranslationException)
            {
                break;
            }

            if (returnAddress != 0)
            {
                frames.Add($"[{index}] sp=0x{framePointer:X8} lr=0x{returnAddress:X8}");
            }

            if (!IsEffectiveMainRamAddress(nextFrame, out uint normalizedNextFrame) || normalizedNextFrame <= framePointer)
            {
                break;
            }

            framePointer = normalizedNextFrame;
        }

        if (frames.Count == 0)
        {
            return;
        }

        writer.WriteLine($"{indent}stack:");
        foreach (string frame in frames)
        {
            writer.WriteLine($"{indent}  {frame}");
        }
    }

    private static bool IsEffectiveMainRamAddress(uint address, out uint normalized)
    {
        if (address < GameCubeAddress.MainRamCachedStart)
        {
            normalized = 0;
            return false;
        }

        return TryNormalizeMainRamAddress(address, out normalized);
    }

    private static bool TryNormalizeMainRamAddress(uint address, out uint normalized)
    {
        if (!GameCubeAddress.TryTranslateMainRam(address, out int offset))
        {
            normalized = 0;
            return false;
        }

        normalized = GameCubeAddress.MainRamCachedStart + (uint)offset;
        return true;
    }

    private static void WriteMmioAccess(TextWriter writer, MmioAccess access)
    {
        string arrow = access.Kind == MmioAccessKind.Read ? "<-" : "->";
        writer.WriteLine($"{access.DeviceName,-9} {access.Kind,-5} {access.Width * 8,2}-bit 0x{access.Address:X8} {arrow} 0x{access.Value:X8}");
    }
}
