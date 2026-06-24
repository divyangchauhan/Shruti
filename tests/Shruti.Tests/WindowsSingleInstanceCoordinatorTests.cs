using Shruti.Platform.Windows;
using Xunit;

namespace Shruti.Tests;

public sealed class WindowsSingleInstanceCoordinatorTests
{
    [Fact]
    public async Task SecondaryStart_SignalsPrimaryAndDoesNotBecomePrimary()
    {
        (string mutexName, string pipeName) = CreateUniqueNames();
        using var primary = new WindowsSingleInstanceCoordinator(mutexName, pipeName);
        using var secondary = new WindowsSingleInstanceCoordinator(mutexName, pipeName);
        var activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.ActivationRequested += (_, _) => activation.TrySetResult();

        Assert.True(await primary.TryStartAsync());

        bool isSecondaryPrimary = await secondary.TryStartAsync();

        Assert.False(isSecondaryPrimary);
        await activation.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task DisposedPrimary_AllowsNewPrimary()
    {
        (string mutexName, string pipeName) = CreateUniqueNames();
        using var primary = new WindowsSingleInstanceCoordinator(mutexName, pipeName);

        Assert.True(await primary.TryStartAsync());
        primary.Dispose();

        using var replacement = new WindowsSingleInstanceCoordinator(mutexName, pipeName);
        Assert.True(await replacement.TryStartAsync());
    }

    private static (string MutexName, string PipeName) CreateUniqueNames()
    {
        string suffix = Guid.NewGuid().ToString("N");
        return (
            $"Local\\Shruti.Tests.SingleInstance.{suffix}",
            $"Shruti.Tests.SingleInstance.{suffix}");
    }
}
