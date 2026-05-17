using NgcSharp.Core;

namespace NgcSharp.Tests;

public sealed class GameCubeStandaloneBootTests
{
    [Fact]
    public void PreparesLowMemoryForHomebrewDol()
    {
        GameCubeMemory memory = new();

        GameCubeStandaloneBoot.PrepareMemory(memory);

        Assert.True(memory.EnableMainRamTopGuard);
        Assert.Equal(GameCubeDiscHeader.ExpectedMagic, memory.Read32(0x8000_001C));
        Assert.Equal(GameCubeBootMemory.NormalBootMagic, memory.Read32(0x8000_0020));
        Assert.Equal((uint)GameCubeAddress.MainRamSize, memory.Read32(0x8000_0028));
        Assert.Equal(GameCubeBootMemory.RetailConsoleType, memory.Read32(0x8000_002C));
        Assert.Equal(0u, memory.Read32(0x8000_0030));
        Assert.Equal(GameCubeBootMemory.StandaloneBi2Address, memory.Read32(0x8000_0034));
        Assert.Equal(0u, memory.Read32(0x8000_0038));
        Assert.Equal(GameCubeBootMemory.AramSize, memory.Read32(0x8000_00D0));
        Assert.Equal(GameCubeBootMemory.DefaultThreadAddress, memory.Read32(0x8000_00D8));
        Assert.Equal(GameCubeBootMemory.MemoryTop, memory.Read32(0x8000_00EC));
        Assert.Equal((uint)GameCubeAddress.MainRamSize, memory.Read32(0x8000_00F0));
        Assert.Equal(GameCubeBootMemory.StandaloneBi2Address, memory.Read32(0x8000_00F4));
        Assert.Equal(GameCubeBootMemory.BusClock, memory.Read32(0x8000_00F8));
        Assert.Equal(GameCubeBootMemory.CpuClock, memory.Read32(0x8000_00FC));

        Assert.Equal((uint)GameCubeAddress.MainRamSize, memory.Read32(GameCubeBootMemory.StandaloneBi2Address));
        Assert.Equal(GameCubeBootMemory.BusClock, memory.Read32(GameCubeBootMemory.StandaloneBi2Address + 0x08));
        Assert.Equal(GameCubeBootMemory.CpuClock, memory.Read32(GameCubeBootMemory.StandaloneBi2Address + 0x0C));
    }

    [Fact]
    public void MainRamTopGuardIsOptInAndAliased()
    {
        GameCubeMemory memory = new();
        Assert.Throws<AddressTranslationException>(() => memory.Write32(0x8180_0000, 0x1234_5678));

        memory.EnableMainRamTopGuard = true;
        memory.Write32(0x8180_0000, 0x1234_5678);

        Assert.Equal(0x1234_5678u, memory.Read32(0x8180_0000));
        Assert.Equal(0x1234_5678u, memory.Read32(0x0180_0000));
        Assert.Throws<AddressTranslationException>(() => memory.Write32(0x8180_0040, 0));
    }
}
