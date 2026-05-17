using NgcSharp.App;
using NgcSharp.Core;

namespace NgcSharp.Tests;

public sealed class ConsoleFormattingTests
{
    [Fact]
    public void ThreadSummaryPrintsQueuedMessagePayloads()
    {
        GameCubeMemory memory = new();
        const uint threadAddress = 0x8030_0000;
        const uint queueAddress = 0x8031_0000;
        const uint bufferAddress = 0x8031_1000;
        const uint payloadAddress = 0x8031_2000;

        memory.Write32(0x8000_00E0, threadAddress);
        memory.Write16(threadAddress + 0x2C8, 4);
        memory.Write32(threadAddress + 0x2DC, queueAddress + 8);
        memory.Write32(queueAddress + 8, threadAddress);
        memory.Write32(queueAddress + 12, threadAddress);
        memory.Write32(queueAddress + 0x10, bufferAddress);
        memory.Write32(queueAddress + 0x14, 2);
        memory.Write32(queueAddress + 0x18, 1);
        memory.Write32(queueAddress + 0x1C, 2);
        memory.Write32(bufferAddress, 0x0000_0007);
        memory.Write32(bufferAddress + 4, payloadAddress);
        memory.Write32(payloadAddress, 0x1234_5678);
        memory.Write32(payloadAddress + 4, 0x9ABC_DEF0);
        using StringWriter writer = new();

        ConsoleFormatting.WriteThreadSummary(writer, memory);

        string output = writer.ToString();
        Assert.Contains("msgq@0x80310000", output);
        Assert.Contains("messages=[0x80312000->0x12345678/0x9ABCDEF0,0x00000007]", output);
    }

    [Fact]
    public void MessageQueueSummaryPrintsActiveQueues()
    {
        GameCubeMemory memory = new();
        const uint queueAddress = 0x8031_0000;
        const uint bufferAddress = 0x8031_1000;

        memory.Write32(queueAddress + 0x10, bufferAddress);
        memory.Write32(queueAddress + 0x14, 1);
        memory.Write32(queueAddress + 0x1C, 1);
        memory.Write32(bufferAddress, 0x434F_4E54);
        using StringWriter writer = new();

        ConsoleFormatting.WriteMessageQueueSummary(writer, memory);

        string output = writer.ToString();
        Assert.Contains("Active message queues:", output);
        Assert.Contains("msgq@0x80310000", output);
        Assert.Contains("messages=[0x434F4E54]", output);
    }

    [Fact]
    public void GxFifoSummarySkipsDirectVertexPayloads()
    {
        List<byte> bytes =
        [
            0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
            0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
            0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
            0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
            0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
            0x80, 0x00, 0x04,
        ];

        bytes.AddRange(Enumerable.Repeat((byte)0, 64));
        bytes.AddRange([0x61, 0x45, 0x00, 0x00, 0x02]);

        List<MmioAccess> accesses = bytes
            .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
            .ToList();
        using StringWriter writer = new();

        ConsoleFormatting.WriteMmioSummary(writer, accesses);

        string output = writer.ToString();
        Assert.Contains("draws=1", output);
        Assert.Contains("draw primitives: quads=1", output);
        Assert.Contains("first draw layout: pos:direct/12, col0:direct/4", output);
        Assert.Contains("BP register 0x45 <= 0x000002", output);
    }

    [Fact]
    public void MmioSummaryPrintsFinalNonFifoRegisterValues()
    {
        MmioAccess[] accesses =
        [
            new(MmioAccessKind.Write, 0xCC00_201C, 2, 0x1002, "VI"),
            new(MmioAccessKind.Write, 0xCC00_201C, 2, 0x1009, "VI"),
            new(MmioAccessKind.Write, 0xCC00_8000, 4, 0xDEAD_BEEF, "GX FIFO"),
        ];
        using StringWriter writer = new();

        ConsoleFormatting.WriteMmioSummary(writer, accesses);

        string output = writer.ToString();
        Assert.Contains("MMIO final written values:", output);
        Assert.Contains("VI        0xCC00201C/2 = 0x00001009", output);
        Assert.DoesNotContain("GX FIFO   0xCC008000/4 = 0xDEADBEEF", output);
    }

    [Fact]
    public void MmioSummaryPrintsViFramebufferCandidates()
    {
        MmioAccess[] accesses =
        [
            new(MmioAccessKind.Write, 0xCC00_201C, 2, 0x1002, "VI"),
            new(MmioAccessKind.Write, 0xCC00_201E, 2, 0x0000, "VI"),
            new(MmioAccessKind.Write, 0xCC00_2020, 2, 0x1009, "VI"),
            new(MmioAccessKind.Write, 0xCC00_2022, 2, 0xF000, "VI"),
            new(MmioAccessKind.Write, 0xCC00_2024, 2, 0x000A, "VI"),
            new(MmioAccessKind.Write, 0xCC00_2026, 2, 0x1000, "VI"),
            new(MmioAccessKind.Write, 0xCC00_2024, 4, 0x0000_9000, "VI"),
        ];
        using StringWriter writer = new();

        ConsoleFormatting.WriteMmioSummary(writer, accesses);

        string output = writer.ToString();
        Assert.Contains("VI framebuffer candidates:", output);
        Assert.Contains("top-right split 0xCC002020/0xCC002022 raw=0x0009F000 shifted=0x013E0000 normalized=0x0009F000", output);
        Assert.Contains("top-left split 0xCC00201C/0xCC00201E raw=0x00020000 shifted=0x00400000 normalized=0x00020000", output);
        Assert.Contains("direct 0xCC002024 raw=0x00009000 shifted=0x00120000 normalized=0x00120000", output);
    }

    [Fact]
    public void MmioSummaryLabelsViBottomFramebufferPairAndSkipsHalfRegisterDirectCandidate()
    {
        MmioAccess[] accesses =
        [
            new(MmioAccessKind.Write, 0xCC00_2024, 2, 0x000A, "VI"),
            new(MmioAccessKind.Write, 0xCC00_2026, 2, 0x1000, "VI"),
        ];
        using StringWriter writer = new();

        ConsoleFormatting.WriteMmioSummary(writer, accesses);

        string output = writer.ToString();
        Assert.Contains("bottom-left split 0xCC002024/0xCC002026 raw=0x000A1000 shifted=0x01420000 normalized=0x000A1000", output);
        Assert.DoesNotContain("direct 0xCC002024", output);
    }
}
