namespace Shruti.Core.Triggers;

public interface IGlobalTriggerService
{
    IAsyncEnumerable<DictationTriggerEvent> Events { get; }

    Task ConfigureAsync(
        TriggerConfiguration configuration,
        CancellationToken cancellationToken);
}
