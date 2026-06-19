namespace Shruti.Core.Platform;

public interface ITargetFocusService
{
    Task<FocusTarget?> CaptureCurrentTargetAsync(
        CancellationToken cancellationToken);

    Task<FocusRestoreResult> RestoreAsync(
        FocusTarget target,
        CancellationToken cancellationToken);
}
