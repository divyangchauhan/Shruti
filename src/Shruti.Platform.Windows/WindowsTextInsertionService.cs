using Shruti.Core.Platform;

namespace Shruti.Platform.Windows;

public sealed class WindowsTextInsertionService : ITextInsertionService
{
    private readonly IWindowsWindowing _windowing;
    private readonly IWindowsTextInput _textInput;
    private readonly IWindowsClipboard _clipboard;
    private readonly IWindowsFocusedElementInspector _focusedElementInspector;
    private readonly TextInsertionPolicyEvaluator _policyEvaluator;
    private readonly TimeSpan _clipboardPasteSettleDelay;

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
        TextInsertionPolicyEvaluator? policyEvaluator = null)
    {
        _windowing = windowing ?? throw new ArgumentNullException(nameof(windowing));
        _textInput = textInput ?? throw new ArgumentNullException(nameof(textInput));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _focusedElementInspector = focusedElementInspector ??
            throw new ArgumentNullException(nameof(focusedElementInspector));
        _policyEvaluator = policyEvaluator ?? new TextInsertionPolicyEvaluator();
        _clipboardPasteSettleDelay = clipboardPasteSettleDelay ?? TimeSpan.FromMilliseconds(100);
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

        TextInsertionResult? safetyFailure = ValidateTargetForInsertion(target, options);
        if (safetyFailure is not null)
        {
            return safetyFailure;
        }

        if (text.Length == 0)
        {
            return Failure("Shruti will not insert an empty transcript.");
        }

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
                : "Clipboard paste was submitted but cannot be confirmed.");
    }

    private TextInsertionResult? ValidateTargetForInsertion(
        FocusTarget target,
        TextInsertionOptions options)
    {
        CapturedTargetSafetyFailure? safetyFailure = GetCapturedTargetSafetyFailure(target);
        if (safetyFailure is not null)
        {
            return Failure(safetyFailure.Message);
        }

        if (target.HasSelectedText == true && !options.AllowReplacingSelection)
        {
            return Failure(
                "The target has selected text. Enable explicit replacement permission before inserting.");
        }

        FocusedElementSnapshot? currentFocusedElement =
            _focusedElementInspector.CaptureFocusedElement(target.WindowHandle);

        if (currentFocusedElement is null)
        {
            return Failure("Shruti could not re-confirm the focused editable field before insertion.");
        }

        if (currentFocusedElement.IsEditable is not true)
        {
            return Failure(
                currentFocusedElement.IsEditable is null
                    ? "Shruti could not re-confirm that the focused field is editable."
                    : "The currently focused field is not editable.");
        }

        if (currentFocusedElement.HasSelectedText is null)
        {
            return Failure("Shruti could not re-confirm whether the focused field has selected text.");
        }

        if (currentFocusedElement.HasSelectedText == true && !options.AllowReplacingSelection)
        {
            return Failure(
                "The currently focused field has selected text. Enable explicit replacement permission before inserting.");
        }

        return null;
    }

    private CapturedTargetSafetyFailure? GetCapturedTargetSafetyFailure(FocusTarget target)
    {
        if (target.WindowHandle == IntPtr.Zero || !_windowing.IsWindow(target.WindowHandle))
        {
            return new CapturedTargetSafetyFailure(true, "The captured target window is no longer available.");
        }

        if (target.IsElevated)
        {
            return new CapturedTargetSafetyFailure(false, "Shruti cannot safely insert into an elevated target app.");
        }

        if (target.IsEditable is not true)
        {
            return new CapturedTargetSafetyFailure(
                false,
                target.IsEditable is null
                    ? "Shruti could not confirm that the captured target is editable."
                    : "The captured target does not expose an editable field.");
        }

        return target.HasSelectedText is null
            ? new CapturedTargetSafetyFailure(
                false,
                "Shruti could not determine whether the captured target has selected text.")
            : null;
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
