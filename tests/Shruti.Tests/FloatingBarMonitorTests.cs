using Shruti.Platform.Windows;
using Xunit;

namespace Shruti.Tests;

public sealed class FloatingBarMonitorTests
{
    private static readonly WindowsDisplayMonitor Laptop = new("laptop", "Laptop", 1, true);
    private static readonly WindowsDisplayMonitor External = new("external", "External", 2, false);

    [Fact]
    public void SingleMonitorUsesSavedChoiceInsteadOfPrimary()
    {
        Assert.Equal([External], WindowsDisplayMonitors.Select([Laptop, External], false, "EXTERNAL"));
    }

    [Fact]
    public void MissingMonitorFallsBackAndReturnsWhenReconnectedWithNewHandle()
    {
        const string savedId = "external";
        Assert.Equal([Laptop], WindowsDisplayMonitors.Select([Laptop], false, savedId));
        var reconnected = External with { Handle = 42, Name = "Display 3" };
        Assert.Equal([reconnected], WindowsDisplayMonitors.Select([Laptop, reconnected], false, savedId));
    }

    [Fact]
    public void DefaultFollowsPrimaryMonitorEvenWhenEnumerationOrderChanges()
    {
        Assert.Equal([Laptop], WindowsDisplayMonitors.Select([External, Laptop], false, null));
        var newPrimary = External with { IsPrimary = true };
        Assert.Equal([newPrimary], WindowsDisplayMonitors.Select([Laptop with { IsPrimary = false }, newPrimary], false, null));
    }

    [Fact]
    public void AllMonitorsSelectsEachDisplayOnceAndIgnoresSingleMonitorPreference()
    {
        Assert.Equal([Laptop, External], WindowsDisplayMonitors.Select([Laptop, External, External], true, "missing"));
        Assert.Equal([Laptop], WindowsDisplayMonitors.Select([Laptop], true, "external"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoDisplaysProducesNoWindows(bool allMonitors)
    {
        Assert.Empty(WindowsDisplayMonitors.Select([], allMonitors, "external"));
    }
}
