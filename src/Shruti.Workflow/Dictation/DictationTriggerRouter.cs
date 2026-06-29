using Shruti.Core.Triggers;

namespace Shruti.Workflow.Dictation;

public sealed class DictationTriggerRouter
{
    private static readonly TimeSpan ShortcutSettleDelay = TimeSpan.FromMilliseconds(350);

    private readonly DictationShellController _controller;

    public DictationTriggerRouter(DictationShellController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public event EventHandler? FloatingWindowToggleRequested;

    public async Task HandleAsync(
        DictationTriggerEvent trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        cancellationToken.ThrowIfCancellationRequested();

        switch (trigger.Kind)
        {
            case DictationTriggerKind.PushToTalkPressed:
                break;

            case DictationTriggerKind.PushToTalkReleased:
                await RunShortcutInsertionCompatibilityTestAsync(cancellationToken).ConfigureAwait(false);
                break;

            case DictationTriggerKind.GlobalHotkey:
                await RunShortcutInsertionCompatibilityTestAsync(cancellationToken).ConfigureAwait(false);

                break;

            case DictationTriggerKind.FloatingWindowToggle:
                FloatingWindowToggleRequested?.Invoke(this, EventArgs.Empty);
                break;

            case DictationTriggerKind.AppButton:
            case DictationTriggerKind.FloatingButton:
            case DictationTriggerKind.TrayMenu:
                if (_controller.State.IsRunning)
                {
                    await _controller.StopAsync().ConfigureAwait(false);
                }
                else
                {
                    await _controller
                        .StartAsync(_controller.State.InsertionMode)
                        .ConfigureAwait(false);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(trigger));
        }
    }

    private async Task RunShortcutInsertionCompatibilityTestAsync(CancellationToken cancellationToken)
    {
        if (_controller.State.IsRunning)
        {
            return;
        }

        await Task.Delay(ShortcutSettleDelay, cancellationToken).ConfigureAwait(false);
        if (!_controller.State.IsRunning)
        {
            await _controller.RunInsertionCompatibilityTestAsync().ConfigureAwait(false);
        }
    }
}
