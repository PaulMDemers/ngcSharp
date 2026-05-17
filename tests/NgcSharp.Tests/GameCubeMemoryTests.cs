using NgcSharp.Core;

namespace NgcSharp.Tests;

public sealed class GameCubeMemoryTests
{
    [Fact]
    public void MapsPhysicalCachedAndUncachedMainRamAliases()
    {
        GameCubeMemory memory = new();

        memory.Write32(0x8000_0100, 0xCAFE_BABE);

        Assert.Equal(0xCAFE_BABEu, memory.Read32(0x0000_0100));
        Assert.Equal(0xCAFE_BABEu, memory.Read32(0xC000_0100));
    }

    [Fact]
    public void RejectsUnmappedAddresses()
    {
        GameCubeMemory memory = new();

        Assert.Throws<AddressTranslationException>(() => memory.Read32(0xCC00_0000));
    }
}
