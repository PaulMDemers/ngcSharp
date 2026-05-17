using NgcSharp.Core;

namespace NgcSharp.Tests;

public sealed class DolFileTests
{
    [Fact]
    public void ParsesAndLoadsTextDataAndBss()
    {
        byte[] image = new byte[0x140];
        Write32(image, 0x000, 0x100);
        Write32(image, 0x01C, 0x110);
        Write32(image, 0x048, 0x8000_3100);
        Write32(image, 0x064, 0x8000_4000);
        Write32(image, 0x090, 4);
        Write32(image, 0x0AC, 4);
        Write32(image, 0x0D8, 0x8000_5000);
        Write32(image, 0x0DC, 8);
        Write32(image, 0x0E0, 0x8000_3100);

        image[0x100] = 0x12;
        image[0x101] = 0x34;
        image[0x102] = 0x56;
        image[0x103] = 0x78;
        image[0x110] = 0x9A;
        image[0x111] = 0xBC;
        image[0x112] = 0xDE;
        image[0x113] = 0xF0;

        DolFile dol = DolFile.Parse(image);
        GameCubeMemory memory = new();
        memory.Write32(0x8000_5000, 0xFFFF_FFFF);
        memory.Write32(0x8000_5004, 0xFFFF_FFFF);

        dol.LoadInto(memory);

        Assert.Equal(0x8000_3100u, dol.EntryPoint);
        Assert.Equal(0x1234_5678u, memory.Read32(0x8000_3100));
        Assert.Equal(0x9ABC_DEF0u, memory.Read32(0x8000_4000));
        Assert.Equal(0u, memory.Read32(0x8000_5000));
        Assert.Equal(0u, memory.Read32(0x8000_5004));
    }

    [Fact]
    public void InitializedSectionsWinWhenBssRangeOverlaps()
    {
        byte[] image = new byte[0x110];
        Write32(image, 0x01C, 0x100);
        Write32(image, 0x064, 0x8000_4000);
        Write32(image, 0x0AC, 4);
        Write32(image, 0x0D8, 0x8000_3FFC);
        Write32(image, 0x0DC, 8);

        image[0x100] = 0x9A;
        image[0x101] = 0xBC;
        image[0x102] = 0xDE;
        image[0x103] = 0xF0;

        DolFile dol = DolFile.Parse(image);
        GameCubeMemory memory = new();
        memory.Write32(0x8000_3FFC, 0xFFFF_FFFF);
        memory.Write32(0x8000_4000, 0xFFFF_FFFF);

        dol.LoadInto(memory);

        Assert.Equal(0u, memory.Read32(0x8000_3FFC));
        Assert.Equal(0x9ABC_DEF0u, memory.Read32(0x8000_4000));
    }

    private static void Write32(byte[] image, int offset, uint value)
    {
        BigEndian.WriteUInt32(image.AsSpan(offset, sizeof(uint)), value);
    }
}
