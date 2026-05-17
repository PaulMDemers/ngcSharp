namespace NgcSharp.Core;

public sealed class AddressTranslationException : Exception
{
    public AddressTranslationException(uint address)
        : base($"Address 0x{address:X8} is not mapped by the current GameCube memory bus.")
    {
        Address = address;
    }

    public uint Address { get; }
}
