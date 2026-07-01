using Shruti.Core.Platform;

namespace Shruti.Platform.Windows;

public sealed class WindowsTextInsertionService : ITextInsertionService
{
    private static readonly TimeSpan DefaultFocusedElementSettleDelay = TimeSpan.FromMilliseconds(50);
    private const int DefaultFocusedElementInspectionAttempts = 3;
    private static readonly IReadOnlyList<WindowsPasteShortcut> StandardPasteShortcuts =
    [
        WindowsPasteShortcut.ControlV
    ];

    private static readonly IReadOnlyList<WindowsPasteShortcut> TerminalPasteShortcuts =
    [
        WindowsPasteShortcut.ControlV,
        WindowsPasteShortcut.ShiftInsert,
        WindowsPasteShortcut.ControlShiftV
    ];

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
            TimeSpan.FromMilliseconds(250))
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
        _clipboardPasteSettleDelay = clipboardPasteSettleDelay ?? TimeSpan.FromMilliseconds(250);
        _focusedElementSettleDelay = focusedElementSettleDelay ?? DefaultFocusedElementSettleDelay;
        _focusedElementInspectionAttempts = Math.Max(1, focusedElementInspectionAttempts);
    }

    public Task<TextInsertionCapability> InspectAsync(
        FocusTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        TextInsertionPolicy policy = _policyEvaluator.Evaluate(target);
        WindowsTargetInsertionProfile profile = WindowsTargetInsertionProfile.FromTarget(target, policy);
        CapturedTargetSafetyFailure? safetyFailure = GetCapturedTargetSafetyFailure(target, profile);
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

        TextInsertionPolicy policy = _policyEvaluator.Evaluate(target);
        WindowsTargetInsertionProfile profile = WindowsTargetInsertionProfile.FromTarget(target, policy);
        FocusedElementSnapshot? currentFocusedElement = await CaptureFocusedElementAsync(
                target.WindowHandle,
                cancellationToken)
            .ConfigureAwait(false);
        TextInsertionResult? safetyFailure = ValidateTargetForInsertion(
                target,
                options,
                profile,
                currentFocusedElement);
        if (safetyFailure is not null)
        {
            return safetyFailure;
        }

        if (text.Length == 0)
        {
            return Failure("Shruti will not insert an empty transcript.");
        }

        if (policy.Mode == TextInsertionPolicyMode.PreviewRequired)
        {
            return Failure(policy.Message);
        }

        if (profile.PreferClipboard)
        {
            if (!options.AllowClipboardFallback)
            {
                return Failure(
                    $"{policy.Message} Clipboard fallback is disabled.");
            }

            TextInsertionResult pasteResult = await PasteViaClipboardAsync(
                    PrepareTextForProfile(text, profile),
                    target,
                    profile,
                    currentFocusedElement,
                    cancellationToken)
                .ConfigureAwait(false);
            if (pasteResult.Completed || !profile.UseSlowUnicodeFallback)
            {
                return pasteResult;
            }

            return InsertWithUnicode(
                target,
                PrepareTextForProfile(text, profile),
                profile,
                currentFocusedElement,
                useSlowInput: true,
                fallbackMessage: pasteResult.Message);
        }

        TextInsertionResult directResult = InsertWithUnicode(
            target,
            text,
            profile,
            currentFocusedElement,
            useSlowInput: false);
        if (directResult.Inserted)
        {
            return directResult;
        }

        if (directResult.OperationalDiagnostics.TryGetValue("sendInputOutcome", out string? directOutcome) &&
            string.Equals(directOutcome, WindowsInputSendOutcome.Partial.ToString(), StringComparison.Ordinal))
        {
            return directResult;
        }

        if (!options.AllowClipboardFallback)
        {
            return Failure("Direct input failed and clipboard fallback is disabled.");
        }

        return await PasteViaClipboardAsync(
                text,
                target,
                profile,
                currentFocusedElement,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TextInsertionResult> PasteViaClipboardAsync(
        string text,
        FocusTarget target,
        WindowsTargetInsertionProfile profile,
        FocusedElementSnapshot? focusedElement,
        CancellationToken cancellationToken)
    {
        IntPtr foregroundBefore = _windowing.GetForegroundWindow();
        WindowsClipboardSnapshot snapshot = _clipboard.Capture();
        if (!snapshot.CanRestore)
        {
            return Failure(
                snapshot.Message ?? "Clipboard fallback could not preserve existing clipboard data.",
                CreateDiagnostics(
                    target,
                    profile,
                    focusedElement,
                    foregroundBefore,
                    foregroundAfter: _windowing.GetForegroundWindow(),
                    clipboardSnapshot: snapshot));
        }

        WindowsClipboardWriteResult write = _clipboard.SetText(text, snapshot.SequenceNumber);
        if (!write.TemporaryTextWritten)
        {
            WindowsClipboardRestoreResult? restoreAfterWriteFailure = write.ClipboardMayNeedRestore
                ? _clipboard.RestoreIfUnchanged(snapshot, write.ExpectedSequenceNumber)
                : null;

            return Failure(
                DescribeClipboardWriteFailure(write, restoreAfterWriteFailure),
                CreateDiagnostics(
                    target,
                    profile,
                    focusedElement,
                    foregroundBefore,
                    foregroundAfter: _windowing.GetForegroundWindow(),
                    clipboardSnapshot: snapshot,
                    clipboardWrite: write,
                    clipboardRestore: restoreAfterWriteFailure));
        }

        WindowsInputSendResult pasteInput = WindowsInputSendResult.FromCounts(0, 0);
        WindowsPasteShortcut pasteShortcut = profile.PasteShortcuts[0];
        cancellationToken.ThrowIfCancellationRequested();
        foreach (WindowsPasteShortcut shortcut in profile.PasteShortcuts)
        {
            pasteShortcut = shortcut;
            pasteInput = _textInput.SendPasteShortcut(shortcut);
            if (pasteInput.Outcome == WindowsInputSendOutcome.Complete)
            {
                break;
            }

            if (pasteInput.Outcome == WindowsInputSendOutcome.Partial)
            {
                _textInput.ReleasePasteShortcutKeys();
            }
        }
        if (pasteInput.SentInputCount > 0 && _clipboardPasteSettleDelay > TimeSpan.Zero)
        {
            await Task.Delay(_clipboardPasteSettleDelay, CancellationToken.None).ConfigureAwait(false);
        }

        if (pasteInput.Outcome != WindowsInputSendOutcome.Complete)
        {
            WindowsClipboardRestoreResult restoreAfterPasteFailure = _clipboard.RestoreIfUnchanged(
                snapshot,
                write.ExpectedSequenceNumber);
            return Failure(
                DescribePasteFailure(pasteInput, restoreAfterPasteFailure),
                CreateDiagnostics(
                    target,
                    profile,
                    focusedElement,
                    foregroundBefore,
                    foregroundAfter: _windowing.GetForegroundWindow(),
                    clipboardSnapshot: snapshot,
                    clipboardWrite: write,
                    clipboardRestore: restoreAfterPasteFailure,
                    input: pasteInput,
                    pasteShortcut: pasteShortcut));
        }

        return new TextInsertionResult(
            Inserted: false,
            TextInsertionMethod.ClipboardPaste,
            profile.PreservesLineSafety
                ? "Terminal paste was submitted but cannot be confirmed. The transcript remains on the clipboard for manual paste; line breaks were replaced with spaces to avoid command submission."
                : "Clipboard paste was submitted but cannot be confirmed. The transcript remains on the clipboard for manual paste.",
            Submitted: true,
            Diagnostics: CreateDiagnostics(
                target,
                profile,
                focusedElement,
                foregroundBefore,
                foregroundAfter: _windowing.GetForegroundWindow(),
                clipboardSnapshot: snapshot,
                clipboardWrite: write,
                input: pasteInput,
                pasteShortcut: pasteShortcut,
                recoveryClipboardTextAvailable: true));
    }

    private TextInsertionResult InsertWithUnicode(
        FocusTarget target,
        string text,
        WindowsTargetInsertionProfile profile,
        FocusedElementSnapshot? focusedElement,
        bool useSlowInput,
        string? fallbackMessage = null)
    {
        IntPtr foregroundBefore = _windowing.GetForegroundWindow();
        WindowsInputSendResult directInput = useSlowInput
            ? _textInput.SendUnicodeTextSlow(text)
            : _textInput.SendUnicodeText(text);
        IntPtr foregroundAfter = _windowing.GetForegroundWindow();
        IReadOnlyDictionary<string, string?> diagnostics = CreateDiagnostics(
            target,
            profile,
            focusedElement,
            foregroundBefore,
            foregroundAfter,
            input: directInput,
            unicodeInputMode: useSlowInput ? "slow" : "direct",
            fallbackMessage: fallbackMessage);

        if (directInput.Outcome == WindowsInputSendOutcome.Complete)
        {
            return new TextInsertionResult(
                Inserted: true,
                TextInsertionMethod.DirectInput,
                useSlowInput && !string.IsNullOrWhiteSpace(fallbackMessage)
                    ? "Inserted with slow Unicode typing after clipboard paste could not be submitted."
                    : null,
                Diagnostics: diagnostics);
        }

        if (directInput.Outcome == WindowsInputSendOutcome.Partial)
        {
            return Failure(
                "Direct input was only partially delivered. Shruti will not retry through the clipboard.",
                diagnostics);
        }

        return Failure(
            string.IsNullOrWhiteSpace(fallbackMessage)
                ? "Direct input failed."
                : $"{fallbackMessage} Slow Unicode fallback also failed.",
            diagnostics);
    }

    private static string PrepareTextForProfile(
        string text,
        WindowsTargetInsertionProfile profile)
    {
        return profile.PreservesLineSafety
            ? ReplaceLineBreaksWithSpaces(text)
            : text;
    }

    private static string ReplaceLineBreaksWithSpaces(string text)
    {
        return text
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    private TextInsertionResult? ValidateTargetForInsertion(
        FocusTarget target,
        TextInsertionOptions options,
        WindowsTargetInsertionProfile profile,
        FocusedElementSnapshot? currentFocusedElement)
    {
        CapturedTargetSafetyFailure? safetyFailure = GetCapturedTargetSafetyFailure(target, profile);
        if (safetyFailure is not null)
        {
            return Failure(safetyFailure.Message);
        }

        if (target.HasSelectedText == true && !options.AllowReplacingSelection)
        {
            return Failure(
                "The target has selected text. Enable explicit replacement permission before inserting.");
        }

        if (currentFocusedElement is null)
        {
            return null;
        }

        if (currentFocusedElement.IsEditable == false && !profile.AllowsAutomationLimitedTarget)
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
        WindowsTargetInsertionProfile profile)
    {
        if (target.WindowHandle == IntPtr.Zero || !_windowing.IsWindow(target.WindowHandle))
        {
            return new CapturedTargetSafetyFailure(true, "The captured target window is no longer available.");
        }

        if (target.IsElevated)
        {
            return new CapturedTargetSafetyFailure(false, "Shruti cannot safely insert into an elevated target app.");
        }

        if (target.IsEditable == false && !profile.AllowsAutomationLimitedTarget)
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
        return Failure(message, diagnostics: null);
    }

    private static TextInsertionResult Failure(
        string message,
        IReadOnlyDictionary<string, string?>? diagnostics)
    {
        return new TextInsertionResult(
            Inserted: false,
            TextInsertionMethod.None,
            message,
            Diagnostics: diagnostics);
    }

    private static IReadOnlyDictionary<string, string?> CreateDiagnostics(
        FocusTarget target,
        WindowsTargetInsertionProfile profile,
        FocusedElementSnapshot? focusedElement,
        IntPtr foregroundBefore,
        IntPtr foregroundAfter,
        WindowsClipboardSnapshot? clipboardSnapshot = null,
        WindowsClipboardWriteResult? clipboardWrite = null,
        WindowsClipboardRestoreResult? clipboardRestore = null,
        WindowsInputSendResult? input = null,
        WindowsPasteShortcut? pasteShortcut = null,
        string? unicodeInputMode = null,
        bool? recoveryClipboardTextAvailable = null,
        string? fallbackMessage = null)
    {
        var diagnostics = new Dictionary<string, string?>
        {
            ["targetWindowHandle"] = FormatWindowHandle(target.WindowHandle),
            ["targetProcessName"] = target.ProcessName,
            ["targetThreadId"] = target.ThreadId.ToString(),
            ["targetProfile"] = profile.Id,
            ["foregroundWindowBefore"] = FormatWindowHandle(foregroundBefore),
            ["foregroundWindowAfter"] = FormatWindowHandle(foregroundAfter),
            ["focusedElementAutomationId"] = focusedElement?.AutomationElementId,
            ["focusedElementIsEditable"] = focusedElement?.IsEditable?.ToString(),
            ["focusedElementHasSelectedText"] = focusedElement?.HasSelectedText?.ToString()
        };

        if (clipboardSnapshot is not null)
        {
            diagnostics["clipboardCanRestore"] = clipboardSnapshot.CanRestore.ToString();
            diagnostics["clipboardSequenceBefore"] = clipboardSnapshot.SequenceNumber.ToString();
        }

        if (clipboardWrite is not null)
        {
            diagnostics["clipboardWriteOutcome"] = clipboardWrite.Outcome.ToString();
            diagnostics["clipboardExpectedSequenceAfterWrite"] = clipboardWrite.ExpectedSequenceNumber.ToString();
        }

        if (clipboardRestore is not null)
        {
            diagnostics["clipboardRestoreOutcome"] = clipboardRestore.Outcome.ToString();
        }

        if (input is not null)
        {
            diagnostics["sendInputOutcome"] = input.Outcome.ToString();
            diagnostics["sendInputSentCount"] = input.SentInputCount.ToString();
            diagnostics["sendInputRequestedCount"] = input.RequestedInputCount.ToString();
            diagnostics["sendInputLastError"] = input.LastError?.ToString();
        }

        if (pasteShortcut is not null)
        {
            diagnostics["pasteShortcut"] = pasteShortcut.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(unicodeInputMode))
        {
            diagnostics["unicodeInputMode"] = unicodeInputMode;
        }

        if (recoveryClipboardTextAvailable is not null)
        {
            diagnostics["recoveryClipboardTextAvailable"] = recoveryClipboardTextAvailable.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(fallbackMessage))
        {
            diagnostics["fallbackMessage"] = fallbackMessage;
        }

        return diagnostics;
    }

    private static string FormatWindowHandle(IntPtr windowHandle)
    {
        return windowHandle == IntPtr.Zero
            ? "0x0"
            : $"0x{windowHandle.ToInt64():X}";
    }

    private sealed record CapturedTargetSafetyFailure(bool IsUnsupported, string Message);

    private sealed record WindowsTargetInsertionProfile(
        string Id,
        bool PreferClipboard,
        bool AllowsAutomationLimitedTarget,
        bool UseSlowUnicodeFallback,
        bool PreservesLineSafety,
        IReadOnlyList<WindowsPasteShortcut> PasteShortcuts)
    {
        private static readonly HashSet<string> TerminalProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "alacritty",
            "bash",
            "cmd",
            "conhost",
            "debian",
            "kali",
            "mintty",
            "openssh",
            "OpenConsole",
            "powershell",
            "pwsh",
            "ssh",
            "ubuntu",
            "wezterm-gui",
            "WindowsTerminal",
            "wsl",
            "wt"
        };

        private static readonly HashSet<string> WebViewProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome",
            "Code",
            "Cursor",
            "Discord",
            "firefox",
            "msedge",
            "Slack",
            "Teams",
            "Windsurf"
        };

        public static WindowsTargetInsertionProfile FromTarget(
            FocusTarget target,
            TextInsertionPolicy policy)
        {
            string processName = TextInsertionPolicyEvaluator.NormalizeProcessName(target.ProcessName);
            if (TerminalProcesses.Contains(processName))
            {
                return new WindowsTargetInsertionProfile(
                    "terminal",
                    PreferClipboard: true,
                    AllowsAutomationLimitedTarget: true,
                    UseSlowUnicodeFallback: false,
                    PreservesLineSafety: true,
                    TerminalPasteShortcuts);
            }

            if (WebViewProcesses.Contains(processName))
            {
                return new WindowsTargetInsertionProfile(
                    "webview",
                    PreferClipboard: true,
                    AllowsAutomationLimitedTarget: true,
                    UseSlowUnicodeFallback: true,
                    PreservesLineSafety: false,
                    StandardPasteShortcuts);
            }

            if (policy.Mode == TextInsertionPolicyMode.ClipboardPastePreferred)
            {
                return new WindowsTargetInsertionProfile(
                    "clipboard-preferred",
                    PreferClipboard: true,
                    AllowsAutomationLimitedTarget: false,
                    UseSlowUnicodeFallback: false,
                    PreservesLineSafety: false,
                    StandardPasteShortcuts);
            }

            return new WindowsTargetInsertionProfile(
                "standard",
                PreferClipboard: false,
                AllowsAutomationLimitedTarget: false,
                UseSlowUnicodeFallback: false,
                PreservesLineSafety: false,
                StandardPasteShortcuts);
        }
    }

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
