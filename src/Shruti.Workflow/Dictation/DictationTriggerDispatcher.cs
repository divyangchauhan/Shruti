using Shruti.Core.Triggers;

namespace Shruti.Workflow.Dictation;

public sealed class DictationTriggerDispatcher
{
    private readonly IGlobalTriggerService _triggerService;
    private readonly DictationTriggerRouter _router;

    public DictationTriggerDispatcher(
        IGlobalTriggerService triggerService,
        DictationTriggerRouter router)
    {
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (DictationTriggerEvent trigger in _triggerService.Events
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                await _router.HandleAsync(trigger, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown cancels the event pump.
        }
    }
}
