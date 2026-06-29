using Shruti.Core.Platform;

namespace Shruti.Platform.Windows;

public sealed class WindowsTextInsertionService : ITextInsertionService
{
    private static readonly TimeSpan DefaultFocusedElementSettleDelay = TimeSpan.FromMilliseconds(50);
    private const int DefaultFocusedElementInspectionAttempts = 3;

    private readonly IWindowsWindowing _windowing;
    private readonly IWindowsTextInput _textInput;
    private readonly IWindowsClipboard _clipboard;
    private readonly IWindowsFocusedElementInspector _focusedElementInspector;
    private readonly TextInsertionPolicyEvaluator _policyEvaluator;
    private readonly TimeSpan _clipboardPasteSettleDelay;
    private readonly TimeSpan _focusedElementSettleDelay;
    private readonly int _focusedElementInspectionAttempts;

    public WindowsTextInsertionService()
        : this(
            new Win32Windowing(),
            new WindowsTextInput(),
            new WindowsClipboard(),
            new WindowsFocusedElementInspector(),
            TimeSpan.FromMilliseconds(100))
    {
    }

    public WindowsTextInsertionService(
        IWindowsWindowing windowing,
        IWindowsTextInput textInput,
        IWindowsClipboard clipboard,
        IWindowsFocusedElementInspector focusedElementInspector,
        TimeSpan? clipboardPasteSettleDelay = null,
        TextInsertionPolicyEvaluator? policyEvaluator = null,
        TimeSpan? focusedElementSettleDelay = null,
        int focusedElementInspectionAttempts = DefaultFocusedElementInspectionAttempts)
    {
        _windowing = windowing ?? throw new ArgumentNullException(nameof(windowing));
        _textInput = textInput ?? throw new ArgumentNullException(nameof(textInput));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _focusedElementInspector = focusedElementInspector ??
            throw new ArgumentNullException(nameof(focusedElementInspector));
        _policyEvaluator = policyEvaluator ?? new TextInsertionPolicyEvaluator();
        _clipboardPasteSettleDelay = clipboardPasteSettleDelay ?? TimeSpan.FromMilliseconds(100);
        _focusedElementSettleDelay = focusedElementSettleDelay ?? DefaultFocusedElementSettleDelay;
        _focusedElementInspectionAttempts = Math.Max(1, focusedElementInspectionAttempts);
    }

    public Task<TextInsertionCapability> InspectAsync(
        FocusTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        CapturedTargetSafetyFailure? safetyFailure = GetCapturedTargetSafetyFailure(target);
        if (safetyFailure is not null)
        {
            return Task.FromResult(safetyFailure.IsUnsupported
                ? Unsupported(safetyFailure.Message)
                : PreviewRequired(safetyFailure.Message));
        }

        if (target.HasSelectedText == true)
        {
            return Task.FromResult(new TextInsertionCapability(
                TextInsertionCapabilityOutcome.DirectInputAvailable,
                TextInsertionMethod.DirectInput,
                "The captured target has selected text and requires explicit replacement permission."));
        }

        TextInsertionPolicy policy = _policyEvaluator.Evaluate(target);
        TextInsertionCapability? policyCapability = GetPolicyCapability(policy);
        if (policyCapability is not null)
        {
            return Task.FromResult(policyCapability);
        }

        return Task.FromResult(new TextInsertionCapability(
            TextInsertionCapabilityOutcome.DirectInputAvailable,
            TextInsertionMethod.DirectInput));
    }

    public async Task<TextInsertionResult> InsertAsync(
        FocusTarget target,
        string text,
        TextInsertionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        TextInsertionResult? safetyFailure = await ValidateTargetForInsertionAsync(
                target,
                options,
                cancellationToken)
            .ConfigureAwait(false);
        if (safetyFailure is not null)
        {
            return safetyFailure;
        }

        if (text.Length == 0)
        {
            return Failure("Shruti will not insert an empty transcript.");
        }

        if (options.PreferredMethodOverride == TextInsertionMethod.ClipboardPaste)
        {
            if (!options.AllowClipboardFallback)
            {
                return Failure("Clipboard paste was requested, but clipboard fallback is disabled.");
            }

            return await PasteViaClipboardAsync(text, cancellationToken).ConfigureAwait(false);
        }

        if (!options.BypassTargetPolicy)
        {
            TextInsertionPolicy policy = _policyEvaluator.Evaluate(target);
            if (policy.Mode == TextInsertionPolicyMode.PreviewRequired)
            {
                return Failure(policy.Message);
            }

            if (policy.Mode == TextInsertionPolicyMode.ClipboardPastePreferred)
            {
                if (!options.AllowClipboardFallback)
                {
                    return Failure(
                        $"{policy.Message} Clipboard fallback is disabled.");
                }

                return await PasteViaClipboardAsync(text, cancellationToken).ConfigureAwait(false);
            }
        }

        WindowsInputSendResult directInput = _textInput.SendUnicodeText(text);
        if (directInput.Outcome == WindowsInputSendOutcome.Complete)
        {
            return new TextInsertionResult(
                Inserted: true,
                TextInsertionMethod.DirectInput);
        }

        if (directInput.Outcome == WindowsInputSendOutcome.Partial)
        {
            return Failure(
                "Direct input was only partially delivered. Shruti will not retry through the clipboard.");
        }

        if (!options.AllowClipboardFallback)
        {
            return Failure("Direct input failed and clipboard fallback is disabled.");
        }

        return await PasteViaClipboardAsync(text, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TextInsertionResult> PasteViaClipboardAsync(
        string text,
        CancellationToken cancellationToken)
    {
        WindowsClipboardSnapshot snapshot = _clipboard.Capture();
        if (!snapshot.CanRestore)
        {
            return Failure(snapshot.Message ?? "Clipboard fallback could not preserve existing clipboard data.");
        }

        WindowsClipboardWriteResult write = _clipboard.SetText(text, snapshot.SequenceNumber);
        if (!write.TemporaryTextWritten)
        {
            WindowsClipboardRestoreResult? restoreAfterWriteFailure = write.ClipboardMayNeedRestore
                ? _clipboard.RestoreIfUnchanged(snapshot, write.ExpectedSequenceNumber)
                : null;

            return Failure(DescribeClipboardWriteFailure(write, restoreAfterWriteFailure));
        }

        WindowsInputSendResult pasteInput;
        WindowsClipboardRestoreResult restoreResult;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            pasteInput = _textInput.SendPasteShortcut();

            if (pasteInput.Outcome == WindowsInputSendOutcome.Partial)
            {
                _textInput.ReleasePasteShortcutKeys();
            }

            if (pasteInput.SentInputCount > 0 && _clipboardPasteSettleDelay > TimeSpan.Zero)
            {
                await Task.Delay(_clipboardPasteSettleDelay, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            restoreResult = _clipboard.RestoreIfUnchanged(snapshot, write.ExpectedSequenceNumber);
        }

        if (pasteInput.Outcome != WindowsInputSendOutcome.Complete)
        {
            return Failure(DescribePasteFailure(pasteInput, restoreResult));
        }

        return new TextInsertionResult(
            Inserted: false,
            TextInsertionMethod.ClipboardPaste,
            restoreResult.Outcome == WindowsClipboardRestoreOutcome.Failed
                ? "Clipboard paste was submitted but cannot be confirmed, and Shruti could not restore the previous clipboard text."
                : "Clipboard paste was submitted but cannot be confirmed.",
            Submitted: true);
    }

    private async Task<TextInsertionResult?> ValidateTargetForInsertionAsync(
        FocusTarget target,
        TextInsertionOptions options,
        CancellationToken cancellationToken)
    {
        CapturedTargetSafetyFailure? safetyFailure = GetCapturedTargetSafetyFailure(
            target,
            options.BypassTargetPolicy);
        if (safetyFailure is not null)
        {
            return Failure(safetyFailure.Message);
        }

        if (!options.BypassTargetPolicy &&
            target.HasSelectedText == true &&
            !options.AllowReplacingSelection)
        {
            return Failure(
                "The target has selected text. Enable explicit replacement permission before inserting.");
        }

        if (options.BypassTargetPolicy)
        {
            return null;
        }

        FocusedElementSnapshot? currentFocusedElement = await CaptureFocusedElementAsync(
                target.WindowHandle,
                cancellationToken)
            .ConfigureAwait(false);

        if (currentFocusedElement is null)
        {
            return null;
        }

        if (currentFocusedElement.IsEditable == false)
        {
            return Failure("The currently focused field is not editable.");
        }

        if (currentFocusedElement.HasSelectedText == true && !options.AllowReplacingSelection)
        {
            return Failure(
                "The currently focused field has selected text. Enable explicit replacement permission before inserting.");
        }

        return null;
    }

    private async Task<FocusedElementSnapshot?> CaptureFocusedElementAsync(
        IntPtr ownerWindowHandle,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= _focusedElementInspectionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FocusedElementSnapshot? snapshot = _focusedElementInspector.CaptureFocusedElement(ownerWindowHandle);
            if (snapshot is not null &&
                snapshot.IsEditable is not null)
            {
                return snapshot;
            }

            if (attempt == _focusedElementInspectionAttempts)
            {
                return snapshot;
            }

            if (_focusedElementSettleDelay > TimeSpan.Zero)
            {
                await Task.Delay(_focusedElementSettleDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private CapturedTargetSafetyFailure? GetCapturedTargetSafetyFailure(
        FocusTarget target,
        bool bypassTargetPolicy = false)
    {
        if (target.WindowHandle == IntPtr.Zero || !_windowing.IsWindow(target.WindowHandle))
        {
            return new CapturedTargetSafetyFailure(true, "The captured target window is no longer available.");
        }

        if (target.IsElevated)
        {
            return new CapturedTargetSafetyFailure(false, "Shruti cannot safely insert into an elevated target app.");
        }

        if (!bypassTargetPolicy && target.IsEditable == false)
        {
            return new CapturedTargetSafetyFailure(
                false,
                "The captured target does not expose an editable field.");
        }

        return null;
    }

    private static TextInsertionCapability Unsupported(string message)
    {
        return new TextInsertionCapability(
            TextInsertionCapabilityOutcome.Unsupported,
            TextInsertionMethod.None,
            message);
    }

    private static TextInsertionCapability PreviewRequired(string message)
    {
        return new TextInsertionCapability(
            TextInsertionCapabilityOutcome.PreviewRecommended,
            TextInsertionMethod.None,
            message);
    }

    private static TextInsertionCapability? GetPolicyCapability(TextInsertionPolicy policy)
    {
        return policy.Mode switch
        {
            TextInsertionPolicyMode.PreviewRequired => PreviewRequired(policy.Message),
            TextInsertionPolicyMode.ClipboardPastePreferred => new TextInsertionCapability(
                TextInsertionCapabilityOutcome.ClipboardFallbackOnly,
                TextInsertionMethod.ClipboardPaste,
                policy.Message),
            _ => null
        };
    }

    private static TextInsertionResult Failure(string message)
    {
        return new TextInsertionResult(
            Inserted: false,
            TextInsertionMethod.None,
            message);
    }

    private sealed record CapturedTargetSafetyFailure(bool IsUnsupported, string Message);

    private static string DescribeClipboardWriteFailure(
        WindowsClipboardWriteResult write,
        WindowsClipboardRestoreResult? restoreResult)
    {
        string message = write.Message ?? "Shruti could not write the transcript to the clipboard.";
        return restoreResult?.Outcome == WindowsClipboardRestoreOutcome.Failed
            ? $"{message} Shruti could not restore the previous clipboard text."
            : message;
    }

    private static string DescribePasteFailure(
        WindowsInputSendResult pasteInput,
        WindowsClipboardRestoreResult restoreResult)
    {
        string message = pasteInput.Outcome == WindowsInputSendOutcome.Partial
            ? "Clipboard paste was only partially delivered."
            : "Clipboard paste could not be sent to the target app.";

        return restoreResult.Outcome == WindowsClipboardRestoreOutcome.Failed
            ? $"{message} Shruti could not restore the previous clipboard text."
            : message;
    }
}
