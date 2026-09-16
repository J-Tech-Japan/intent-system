using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class NotifyRoleCollectWaitClockDefaultTests
{
    [Fact]
    public void DefaultWaitClockFactoryProducesTheStopwatchClock()
    {
        Assert.IsType<StopwatchNotifyRoleCollectWaitClock>(NotifyCommand.DefaultRoleCollectWaitClockFactory());
    }

    [Fact]
    public void StopwatchWaitClockObservesRealTime()
    {
        var clock = new StopwatchNotifyRoleCollectWaitClock();
        var first = clock.ElapsedMilliseconds;
        Assert.True(first >= 0);

        var second = clock.ElapsedMilliseconds;
        Assert.True(second >= first);

        clock.Sleep(TimeSpan.FromMilliseconds(15));
        var third = clock.ElapsedMilliseconds;
        Assert.True(third > second);
    }
}
