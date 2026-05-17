using NgcSharp.Core;

namespace NgcSharp.Tests;

public sealed class DiscImageInfoTests
{
    [Fact]
    public void ParsesIsoGameCubeHeader()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.iso");
        try
        {
            byte[] image = new byte[0x440];
            WriteDiscHeader(image, "TEST01", "Unit Test Disc");
            BigEndian.WriteUInt32(image.AsSpan(0x420), 0x1000);
            BigEndian.WriteUInt32(image.AsSpan(0x424), 0x2000);
            BigEndian.WriteUInt32(image.AsSpan(0x428), 0x300);
            BigEndian.WriteUInt32(image.AsSpan(0x42C), 0x400);
            File.WriteAllBytes(path, image);

            DiscImageInfo info = DiscImageInfo.Load(path);

            Assert.Equal(DiscImageKind.Iso, info.Kind);
            Assert.Equal("TEST01", info.DiscHeader.GameId);
            Assert.Equal("Unit Test Disc", info.DiscHeader.Title);
            Assert.True(info.DiscHeader.IsGameCubeDisc);
            Assert.Equal((ulong)image.Length, info.DiscSize);

            using DiscImageReader reader = DiscImageReader.Open(path);
            GameCubeDiscLayout layout = GameCubeDiscLayout.Read(reader);
            Assert.Equal(0x1000u, layout.MainDolOffset);
            Assert.Equal(0x2000u, layout.FileSystemTableOffset);
            Assert.Equal(0x300u, layout.FileSystemTableSize);
            Assert.Equal(0x400u, layout.FileSystemTableMaxSize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParsesRvzHeaderAndEmbeddedDiscHeader()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.rvz");
        try
        {
            byte[] image = new byte[0x48 + 0xDC];
            image[0] = (byte)'R';
            image[1] = (byte)'V';
            image[2] = (byte)'Z';
            image[3] = 1;
            BigEndian.WriteUInt32(image.AsSpan(0x04), 0x0100_0000);
            BigEndian.WriteUInt32(image.AsSpan(0x08), 0x0003_0000);
            BigEndian.WriteUInt32(image.AsSpan(0x0C), 0xDC);
            BigEndian.WriteUInt64(image.AsSpan(0x24), 0x5705_8000);
            BigEndian.WriteUInt64(image.AsSpan(0x2C), (ulong)image.Length);

            int header2 = 0x48;
            BigEndian.WriteUInt32(image.AsSpan(header2 + 0x00), 1);
            BigEndian.WriteUInt32(image.AsSpan(header2 + 0x04), 5);
            BigEndian.WriteUInt32(image.AsSpan(header2 + 0x08), 19);
            BigEndian.WriteUInt32(image.AsSpan(header2 + 0x0C), 0x20000);
            WriteDiscHeader(image.AsSpan(header2 + 0x10, GameCubeDiscHeader.Size), "GAME01", "RVZ Test Disc");
            "NKIT"u8.CopyTo(image.AsSpan(header2 + 0xD8));
            File.WriteAllBytes(path, image);

            DiscImageInfo info = DiscImageInfo.Load(path);

            Assert.Equal(DiscImageKind.Rvz, info.Kind);
            Assert.Equal("GAME01", info.DiscHeader.GameId);
            Assert.Equal("RVZ Test Disc", info.DiscHeader.Title);
            Assert.Equal(0x5705_8000ul, info.DiscSize);
            Assert.Equal(5u, info.RvzCompression);
            Assert.Equal(19, info.RvzCompressionLevel);
            Assert.Equal(0x20000u, info.RvzChunkSize);
            Assert.True(info.HasNkitMarker);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadsMainDolFromIsoDiscOffset()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.iso");
        try
        {
            byte[] image = new byte[0x904];
            WriteDiscHeader(image, "DOL001", "DOL Test Disc");
            BigEndian.WriteUInt32(image.AsSpan(0x420), 0x800);

            int dolOffset = 0x800;
            BigEndian.WriteUInt32(image.AsSpan(dolOffset + 0x000), 0x100);
            BigEndian.WriteUInt32(image.AsSpan(dolOffset + 0x048), 0x8000_3100);
            BigEndian.WriteUInt32(image.AsSpan(dolOffset + 0x090), 4);
            BigEndian.WriteUInt32(image.AsSpan(dolOffset + 0x0E0), 0x8000_3100);
            image[dolOffset + 0x100] = 0x12;
            image[dolOffset + 0x101] = 0x34;
            image[dolOffset + 0x102] = 0x56;
            image[dolOffset + 0x103] = 0x78;
            File.WriteAllBytes(path, image);

            using DiscImageReader reader = DiscImageReader.Open(path);
            DolFile dol = GameCubeDiscDolLoader.LoadMainDol(reader);

            Assert.Equal(0x8000_3100u, dol.EntryPoint);
            DolSection section = Assert.Single(dol.TextSections);
            Assert.Equal(0x1234_5678u, BigEndian.ReadUInt32(section.Data.Span));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PreparesGameDiscBootMemory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.iso");
        try
        {
            byte[] image = new byte[0xA20];
            WriteDiscHeader(image, "BOOT01", "Boot Test Disc");
            BigEndian.WriteUInt32(image.AsSpan(0x420), 0x800);
            BigEndian.WriteUInt32(image.AsSpan(0x424), 0xA00);
            BigEndian.WriteUInt32(image.AsSpan(0x428), 0x20);
            BigEndian.WriteUInt32(image.AsSpan(0x42C), 0x20);

            int dolOffset = 0x800;
            BigEndian.WriteUInt32(image.AsSpan(dolOffset + 0x000), 0x100);
            BigEndian.WriteUInt32(image.AsSpan(dolOffset + 0x048), 0x8000_3100);
            BigEndian.WriteUInt32(image.AsSpan(dolOffset + 0x090), 4);
            BigEndian.WriteUInt32(image.AsSpan(dolOffset + 0x0E0), 0x8000_3100);

            image[0xA00] = 0x00;
            image[0xA01] = 0x00;
            image[0xA02] = 0x00;
            image[0xA03] = 0x01;
            File.WriteAllBytes(path, image);

            using DiscImageReader reader = DiscImageReader.Open(path);
            GameCubeMemory memory = new();
            GameCubeDiscBootInfo bootInfo = GameCubeDiscBoot.PrepareMemory(reader, memory);

            Assert.Equal(0x424F4F54u, memory.Read32(0x8000_0000));
            Assert.Equal(GameCubeDiscHeader.ExpectedMagic, memory.Read32(0x8000_001C));
            Assert.Equal(0x0D15_EA5Eu, memory.Read32(0x8000_0020));
            Assert.Equal((uint)GameCubeAddress.MainRamSize, memory.Read32(0x8000_0028));
            Assert.Equal(bootInfo.FileSystemTableAddress, memory.Read32(0x8000_0034));
            Assert.Equal(bootInfo.FileSystemTableAddress, memory.Read32(0x8000_0038));
            Assert.Equal(0x0100_0000u, memory.Read32(0x8000_00D0));
            Assert.Equal(0x803F_0000u, memory.Read32(0x8000_00D8));
            Assert.Equal(0x803F_0000u, memory.Read32(0x8000_00E4));
            Assert.Equal(0x8180_0000u, memory.Read32(0x8000_00EC));
            Assert.Equal(0x0000_0001u, memory.Read32(bootInfo.FileSystemTableAddress));
            Assert.Equal(0x104u, memory.Read32(0x8000_30D4));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParsesGameCubeFileSystemTable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.iso");
        try
        {
            byte[] image = new byte[0x1400];
            WriteDiscHeader(image, "FST001", "FST Test Disc");
            BigEndian.WriteUInt32(image.AsSpan(0x424), 0xA00);
            BigEndian.WriteUInt32(image.AsSpan(0x428), 0x49);
            BigEndian.WriteUInt32(image.AsSpan(0x42C), 0x49);

            int fstOffset = 0xA00;
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x00), 0x0100_0000);
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x04), 0);
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x08), 4);
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x0C), 0);
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x10), 0x1200);
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x14), 4);
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x18), 0x0100_0009);
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x1C), 0);
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x20), 4);
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x24), 0x0000_000F);
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x28), 0x1300);
            BigEndian.WriteUInt32(image.AsSpan(fstOffset + 0x2C), 8);
            "boot.bin\0level\0stage.dat\0"u8.CopyTo(image.AsSpan(fstOffset + 0x30));
            File.WriteAllBytes(path, image);

            using DiscImageReader reader = DiscImageReader.Open(path);
            GameCubeFileSystemTable fst = GameCubeFileSystemTable.Load(reader);

            Assert.Contains(fst.Entries, static entry => entry is { Path: "boot.bin", IsDirectory: false, DiscOffset: 0x1200, Size: 4 });
            Assert.Contains(fst.Entries, static entry => entry is { Path: "level", IsDirectory: true });
            Assert.Contains(fst.Entries, static entry => entry is { Path: "level/stage.dat", IsDirectory: false, DiscOffset: 0x1300, Size: 8 });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteDiscHeader(Span<byte> destination, string gameId, string title)
    {
        System.Text.Encoding.ASCII.GetBytes(gameId, destination[..gameId.Length]);
        BigEndian.WriteUInt32(destination.Slice(0x1C), GameCubeDiscHeader.ExpectedMagic);
        System.Text.Encoding.ASCII.GetBytes(title, destination.Slice(0x20, title.Length));
    }
}
