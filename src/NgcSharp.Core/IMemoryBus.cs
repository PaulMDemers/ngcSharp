namespace NgcSharp.Core;

public interface IMemoryBus
{
    byte Read8(uint address);

    ushort Read16(uint address);

    uint Read32(uint address);

    void Write8(uint address, byte value);

    void Write16(uint address, ushort value);

    void Write32(uint address, uint value);
}
