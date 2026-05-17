namespace NgcSharp.Hw;

public sealed class ProcessorInterface
{
    public InterruptSource Cause { get; private set; }

    public InterruptSource Mask { get; private set; }

    public bool HasPendingInterrupt => (Cause & Mask) != InterruptSource.None;

    public void SetMask(InterruptSource mask)
    {
        Mask = mask;
    }

    public void Raise(InterruptSource source)
    {
        Cause |= source;
    }

    public void Acknowledge(InterruptSource source)
    {
        Cause &= ~source;
    }
}
