using NgcSharp.Hw;

namespace NgcSharp.Tests;

public sealed class ProcessorInterfaceTests
{
    [Fact]
    public void ReportsPendingInterruptOnlyWhenRaisedSourceIsMasked()
    {
        ProcessorInterface processorInterface = new();

        processorInterface.Raise(InterruptSource.VideoInterface);
        Assert.False(processorInterface.HasPendingInterrupt);

        processorInterface.SetMask(InterruptSource.VideoInterface);
        Assert.True(processorInterface.HasPendingInterrupt);

        processorInterface.Acknowledge(InterruptSource.VideoInterface);
        Assert.False(processorInterface.HasPendingInterrupt);
    }
}
