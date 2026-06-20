using System.Threading.Channels;
using Shruti.Core.Triggers;

namespace Shruti.App.WinUI.Dictation;

public sealed class MockGlobalTriggerService : IGlobalTriggerService
{
    private readonly Channel<DictationTriggerEvent> _events = Channel.CreateUnbounded<DictationTriggerEvent>();

    public TriggerConfiguration Configuration { get; private set; } = new(
        EnableGlobalHotkey: true,
        EnablePushToTalk: true,
        EnableFloatingButton: true,
        EnableTrayMenu: true,
        HotkeyGesture: "Ctrl+Alt+Space");

    public IAsyncEnumerable<DictationTriggerEvent> Events => _events.Reader.ReadAllAsync();

    public Task ConfigureAsync(
        TriggerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        Configuration = configuration;
        return Task.CompletedTask;
    }

    public bool Publish(DictationTriggerKind kind)
    {
        if (!IsEnabled(kind))
        {
            return false;
        }

        return _events.Writer.TryWrite(new DictationTriggerEvent(
            kind,
            DateTimeOffset.UtcNow,
            SourceId: "mock"));
    }

    private bool IsEnabled(DictationTriggerKind kind)
    {
        return kind switch
        {
            DictationTriggerKind.AppButton => true,
            DictationTriggerKind.GlobalHotkey => Configuration.EnableGlobalHotkey,
            DictationTriggerKind.PushToTalkPressed or DictationTriggerKind.PushToTalkReleased =>
                Configuration.EnablePushToTalk,
            DictationTriggerKind.FloatingButton => Configuration.EnableFloatingButton,
            DictationTriggerKind.TrayMenu => Configuration.EnableTrayMenu,
            _ => false
        };
    }
}
