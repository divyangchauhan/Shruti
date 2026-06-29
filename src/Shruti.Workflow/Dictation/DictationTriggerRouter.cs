using Shruti.Core.Triggers;

namespace Shruti.Workflow.Dictation;

public sealed class DictationTriggerRouter
{
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
                if (!_controller.State.IsRunning)
                {
                    await _controller
                        .RunInsertionCompatibilityTestAsync()
                        .ConfigureAwait(false);
                }

                break;

            case DictationTriggerKind.PushToTalkReleased:
                break;

            case DictationTriggerKind.GlobalHotkey:
                if (!_controller.State.IsRunning)
                {
                    await _controller
                        .RunInsertionCompatibilityTestAsync()
                        .ConfigureAwait(false);
                }

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
}
