using NgcSharp.Core;

namespace NgcSharp.App;

public static class SyntheticDolFactory
{
    public const uint EntryPoint = 0x8000_3100;

    public static byte[] CreateSmokeTestDol()
    {
        byte[] image = new byte[0x120];

        Write32(image, 0x000, 0x100);
        Write32(image, 0x048, EntryPoint);
        Write32(image, 0x090, 0x10);
        Write32(image, 0x0D8, 0x8000_4000);
        Write32(image, 0x0DC, 0x100);
        Write32(image, 0x0E0, EntryPoint);

        Write32(image, 0x100, Addi(rD: 3, rA: 0, immediate: 7));
        Write32(image, 0x104, Stw(rS: 3, rA: 0, displacement: 0x100));
        Write32(image, 0x108, Addi(rD: 4, rA: 3, immediate: 35));
        Write32(image, 0x10C, Branch(offset: 0));

        return image;
    }

    public static void WriteSmokeTestDol(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, CreateSmokeTestDol());
    }

    private static uint Addi(int rD, int rA, short immediate)
    {
        return DForm(14, rD, rA, (ushort)immediate);
    }

    private static uint Stw(int rS, int rA, short displacement)
    {
        return DForm(36, rS, rA, (ushort)displacement);
    }

    private static uint Branch(int offset)
    {
        return (18u << 26) | ((uint)offset & 0x03FF_FFFC);
    }

    private static uint DForm(int opcode, int rDOrS, int rA, ushort immediate)
    {
        return ((uint)opcode << 26) | ((uint)rDOrS << 21) | ((uint)rA << 16) | immediate;
    }

    private static void Write32(byte[] image, int offset, uint value)
    {
        BigEndian.WriteUInt32(image.AsSpan(offset, sizeof(uint)), value);
    }
}
