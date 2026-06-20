using Shruti.Workflow.Dictation;
using Shruti.Core;
using Shruti.Core.Dictation;
using Shruti.Core.Triggers;
using Xunit;

namespace Shruti.Tests;

public sealed class DictationTriggerRouterTests
{
    [Theory]
    [InlineData(DictationTriggerKind.AppButton)]
    [InlineData(DictationTriggerKind.GlobalHotkey)]
    [InlineData(DictationTriggerKind.FloatingButton)]
    [InlineData(DictationTriggerKind.TrayMenu)]
    public async Task ToggleTrigger_StartsAndStopsMockDictation(DictationTriggerKind kind)
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();
        var router = new DictationTriggerRouter(controller);

        await router.HandleAsync(CreateTrigger(kind));
        await router.HandleAsync(CreateTrigger(kind));

        Assert.Equal(DictationRunOutcome.Inserted, controller.LastResult?.Outcome);
        Assert.Equal(1, services.AudioCapture.StartCount);
        Assert.Equal(1, services.TextInsertion.InsertCount);
    }

    [Fact]
    public async Task PushToTalk_StartsOnPressAndStopsOnRelease()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();
        var router = new DictationTriggerRouter(controller);

        await router.HandleAsync(CreateTrigger(DictationTriggerKind.PushToTalkPressed));

        Assert.True(controller.State.IsRunning);

        await router.HandleAsync(CreateTrigger(DictationTriggerKind.PushToTalkReleased));

        Assert.Equal(DictationSessionState.Complete, controller.State.SessionState);
        Assert.Equal(DictationRunOutcome.Inserted, controller.LastResult?.Outcome);
    }

    [Fact]
    public async Task MockGlobalTriggerService_ConfigurationGatesDisabledTrigger()
    {
        var triggerService = new MockGlobalTriggerService();
        await triggerService.ConfigureAsync(
            new TriggerConfiguration(
                EnableGlobalHotkey: false,
                EnablePushToTalk: true,
                EnableFloatingButton: true,
                EnableTrayMenu: true,
                HotkeyGesture: "Ctrl+Alt+Space"),
            CancellationToken.None);

        bool published = triggerService.Publish(DictationTriggerKind.GlobalHotkey);

        Assert.False(published);
    }

    [Fact]
    public async Task MockGlobalTriggerService_PublishesEnabledTrigger()
    {
        var triggerService = new MockGlobalTriggerService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        bool published = triggerService.Publish(DictationTriggerKind.FloatingButton);
        DictationTriggerEvent? received = await ReadFirstAsync(triggerService.Events, cancellation.Token);

        Assert.True(published);
        Assert.Equal(DictationTriggerKind.FloatingButton, received?.Kind);
        Assert.Equal("mock", received?.SourceId);
    }

    [Fact]
    public async Task TriggerDispatcher_RoutesMockGlobalHotkeyThroughDictationWorkflow()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();
        var router = new DictationTriggerRouter(controller);
        var triggerService = new MockGlobalTriggerService();
        var dispatcher = new DictationTriggerDispatcher(triggerService, router);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        Task dispatchTask = dispatcher.RunAsync(cancellation.Token);

        Assert.True(triggerService.Publish(DictationTriggerKind.GlobalHotkey));
        await WaitUntilAsync(() => controller.State.IsRunning, cancellation.Token);

        Assert.True(triggerService.Publish(DictationTriggerKind.GlobalHotkey));
        await WaitUntilAsync(
            () => controller.LastResult?.Outcome == DictationRunOutcome.Inserted,
            cancellation.Token);

        cancellation.Cancel();
        await dispatchTask;

        Assert.Equal(1, services.AudioCapture.StartCount);
        Assert.Equal(1, services.TextInsertion.InsertCount);
    }

    private static DictationTriggerEvent CreateTrigger(DictationTriggerKind kind)
    {
        return new DictationTriggerEvent(kind, DateTimeOffset.UtcNow, "test");
    }

    private static async Task<DictationTriggerEvent?> ReadFirstAsync(
        IAsyncEnumerable<DictationTriggerEvent> events,
        CancellationToken cancellationToken)
    {
        await foreach (DictationTriggerEvent trigger in events.WithCancellation(cancellationToken))
        {
            return trigger;
        }

        return null;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        while (!predicate())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
