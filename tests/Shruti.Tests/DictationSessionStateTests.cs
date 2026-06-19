using Shruti.Core;
using Xunit;

namespace Shruti.Tests;

public sealed class DictationSessionStateTests
{
    [Fact]
    public void InitialStatusCanRepresentIdleShell()
    {
        var status = new DictationStatus(
            DictationSessionState.Idle,
            "Ready");

        Assert.Equal(DictationSessionState.Idle, status.State);
        Assert.Equal("Ready", status.Message);
    }
}
