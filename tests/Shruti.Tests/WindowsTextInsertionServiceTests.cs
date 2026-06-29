using Shruti.Core.Platform;
using Shruti.Platform.Windows;
using Xunit;

namespace Shruti.Tests;

public sealed class WindowsTextInsertionServiceTests
{
    [Fact]
    public void WindowsTextInput_UsesNativeInputLayoutForCurrentArchitecture()
    {
        int expectedSize = IntPtr.Size == 8 ? 40 : 28;

        Assert.Equal(expectedSize, WindowsTextInput.NativeInputSize);
    }

    [Fact]
    public async Task InspectAsync_ReturnsUnsupportedWhenTargetWindowNoLongerExists()
    {
        var service = CreateService(new FakeWindowing());

        TextInsertionCapability capability = await service.InspectAsync(CreateTarget(), CancellationToken.None);

        Assert.Equal(TextInsertionCapabilityOutcome.Unsupported, capability.Outcome);
        Assert.Equal(TextInsertionMethod.None, capability.PreferredMethod);
    }

    [Fact]
    public async Task InspectAsync_AllowsDirectInputWhenEditabilityIsUnknown()
    {
        var service = CreateService(new FakeWindowing { IsWindowResult = true });

        TextInsertionCapability capability = await service.InspectAsync(
            CreateTarget(IsEditable: null),
            CancellationToken.None);

        Assert.Equal(TextInsertionCapabilityOutcome.DirectInputAvailable, capability.Outcome);
        Assert.Equal(TextInsertionMethod.DirectInput, capability.PreferredMethod);
        Assert.Null(capability.Message);
    }

    [Fact]
    public async Task InspectAsync_AllowsDirectInputWhenSelectionStateIsUnknown()
    {
        var service = CreateService(new FakeWindowing { IsWindowResult = true });

        TextInsertionCapability capability = await service.InspectAsync(
            CreateTarget(HasSelectedText: null),
            CancellationToken.None);

        Assert.Equal(TextInsertionCapabilityOutcome.DirectInputAvailable, capability.Outcome);
        Assert.Equal(TextInsertionMethod.DirectInput, capability.PreferredMethod);
        Assert.Null(capability.Message);
    }

    [Fact]
    public async Task CapturedTargetSafety_UsesTheSameMessageForInspectionAndInsertion()
    {
        var service = CreateService(new FakeWindowing { IsWindowResult = true });
        FocusTarget target = CreateTarget(IsEditable: false);

        TextInsertionCapability capability = await service.InspectAsync(target, CancellationToken.None);
        TextInsertionResult result = await service.InsertAsync(
            target,
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.Equal(TextInsertionCapabilityOutcome.PreviewRecommended, capability.Outcome);
        Assert.False(result.Inserted);
        Assert.Equal(capability.Message, result.Message);
    }

    [Fact]
    public async Task InspectAsync_AllowsCoordinatorToRespectExplicitSelectionPermission()
    {
        var service = CreateService(new FakeWindowing { IsWindowResult = true });

        TextInsertionCapability capability = await service.InspectAsync(
            CreateTarget(HasSelectedText: true),
            CancellationToken.None);

        Assert.Equal(TextInsertionCapabilityOutcome.DirectInputAvailable, capability.Outcome);
        Assert.Equal(TextInsertionMethod.DirectInput, capability.PreferredMethod);
        Assert.Equal(
            "The captured target has selected text and requires explicit replacement permission.",
            capability.Message);
    }

    [Fact]
    public async Task InspectAsync_RequiresPreviewForTerminalTargets()
    {
        var service = CreateService(new FakeWindowing { IsWindowResult = true });

        TextInsertionCapability capability = await service.InspectAsync(
            CreateTarget(ProcessName: "WindowsTerminal.exe", WindowTitle: "PowerShell"),
            CancellationToken.None);

        Assert.Equal(TextInsertionCapabilityOutcome.PreviewRecommended, capability.Outcome);
        Assert.Equal(TextInsertionMethod.None, capability.PreferredMethod);
        Assert.Equal("Terminal and shell targets require preview before insertion.", capability.Message);
    }

    [Fact]
    public async Task InspectAsync_RequiresPreviewForElevatedTargets()
    {
        var service = CreateService(new FakeWindowing { IsWindowResult = true });

        TextInsertionCapability capability = await service.InspectAsync(
            CreateTarget(IsElevated: true),
            CancellationToken.None);

        Assert.Equal(TextInsertionCapabilityOutcome.PreviewRecommended, capability.Outcome);
        Assert.Equal(TextInsertionMethod.None, capability.PreferredMethod);
        Assert.Equal("Shruti cannot safely insert into an elevated target app.", capability.Message);
    }

    [Fact]
    public async Task InspectAsync_UsesClipboardFallbackOnlyForClipboardPreferredTargets()
    {
        var service = CreateService(new FakeWindowing { IsWindowResult = true });

        TextInsertionCapability capability = await service.InspectAsync(
            CreateTarget(ProcessName: "WINWORD", WindowTitle: "Document1 - Word"),
            CancellationToken.None);

        Assert.Equal(TextInsertionCapabilityOutcome.ClipboardFallbackOnly, capability.Outcome);
        Assert.Equal(TextInsertionMethod.ClipboardPaste, capability.PreferredMethod);
        Assert.Equal(
            "This target is more reliable with clipboard paste than direct text input.",
            capability.Message);
    }

    [Fact]
    public async Task InspectAsync_PrioritizesSelectedTextSafetyBeforeClipboardPreferredPolicy()
    {
        var service = CreateService(new FakeWindowing { IsWindowResult = true });

        TextInsertionCapability capability = await service.InspectAsync(
            CreateTarget(HasSelectedText: true, ProcessName: "winword"),
            CancellationToken.None);

        Assert.Equal(TextInsertionCapabilityOutcome.DirectInputAvailable, capability.Outcome);
        Assert.Equal(TextInsertionMethod.DirectInput, capability.PreferredMethod);
        Assert.Equal(
            "The captured target has selected text and requires explicit replacement permission.",
            capability.Message);
    }

    [Fact]
    public async Task InsertAsync_UsesDirectUnicodeInputWhenAvailable()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = CompleteResult(requestedInputCount: 28)
        };
        var clipboard = new FakeClipboard();
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.True(result.Inserted);
        Assert.True(result.Succeeded);
        Assert.False(result.Submitted);
        Assert.Equal(TextInsertionMethod.DirectInput, result.Method);
        Assert.Equal("Hello, Shruti.", input.LastUnicodeText);
        Assert.Equal(0, input.SendPasteShortcutCount);
        Assert.Equal(0, clipboard.CaptureCount);
    }

    [Fact]
    public async Task InsertAsync_RefusesTerminalTargetsWithoutSendingInput()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = CompleteResult(requestedInputCount: 28),
            PasteResult = CompleteResult(requestedInputCount: 4)
        };
        var clipboard = new FakeClipboard();
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(ProcessName: "pwsh"),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.Equal(TextInsertionMethod.None, result.Method);
        Assert.Equal("Terminal and shell targets require preview before insertion.", result.Message);
        Assert.Equal(0, input.SendUnicodeTextCount);
        Assert.Equal(0, input.SendPasteShortcutCount);
        Assert.Equal(0, clipboard.CaptureCount);
    }

    [Fact]
    public async Task InsertAsync_BypassPolicyCanForceClipboardPasteIntoTerminalTargets()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = CompleteResult(requestedInputCount: 28),
            PasteResult = CompleteResult(requestedInputCount: 4)
        };
        var clipboard = new FakeClipboard(
            new WindowsClipboardSnapshot(CanRestore: true, Text: "previous clipboard text", SequenceNumber: 11));
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(ProcessName: "pwsh", IsEditable: false),
            "Hello, Shruti.",
            new TextInsertionOptions(
                AllowReplacingSelection: true,
                BypassTargetPolicy: true,
                PreferredMethodOverride: TextInsertionMethod.ClipboardPaste),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.True(result.Succeeded);
        Assert.True(result.Submitted);
        Assert.Equal(TextInsertionMethod.ClipboardPaste, result.Method);
        Assert.Equal(0, input.SendUnicodeTextCount);
        Assert.Equal(1, input.SendPasteShortcutCount);
        Assert.Equal(1, clipboard.CaptureCount);
        Assert.Equal("Hello, Shruti.", clipboard.LastSetText);
    }

    [Fact]
    public async Task InsertAsync_RefusesElevatedTargetsWithoutSendingInput()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = CompleteResult(requestedInputCount: 28),
            PasteResult = CompleteResult(requestedInputCount: 4)
        };
        var clipboard = new FakeClipboard();
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(IsElevated: true),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(TextInsertionMethod.None, result.Method);
        Assert.Equal("Shruti cannot safely insert into an elevated target app.", result.Message);
        Assert.Equal(0, input.SendUnicodeTextCount);
        Assert.Equal(0, input.SendPasteShortcutCount);
        Assert.Equal(0, clipboard.CaptureCount);
    }

    [Fact]
    public async Task InsertAsync_BypassPolicyStillRefusesElevatedTargets()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = CompleteResult(requestedInputCount: 28),
            PasteResult = CompleteResult(requestedInputCount: 4)
        };
        var clipboard = new FakeClipboard();
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(IsElevated: true),
            "Hello, Shruti.",
            new TextInsertionOptions(
                AllowReplacingSelection: true,
                BypassTargetPolicy: true,
                PreferredMethodOverride: TextInsertionMethod.ClipboardPaste),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(TextInsertionMethod.None, result.Method);
        Assert.Equal("Shruti cannot safely insert into an elevated target app.", result.Message);
        Assert.Equal(0, input.SendUnicodeTextCount);
        Assert.Equal(0, input.SendPasteShortcutCount);
        Assert.Equal(0, clipboard.CaptureCount);
    }

    [Fact]
    public async Task InsertAsync_ClipboardPreferredTargetSkipsDirectInputAndSubmitsPaste()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = CompleteResult(requestedInputCount: 28),
            PasteResult = CompleteResult(requestedInputCount: 4)
        };
        var clipboard = new FakeClipboard(
            new WindowsClipboardSnapshot(CanRestore: true, Text: "previous clipboard text", SequenceNumber: 11));
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(ProcessName: "winword"),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.True(result.Succeeded);
        Assert.True(result.Submitted);
        Assert.Equal(TextInsertionMethod.ClipboardPaste, result.Method);
        Assert.Equal(0, input.SendUnicodeTextCount);
        Assert.Equal(1, input.SendPasteShortcutCount);
        Assert.Equal(1, clipboard.CaptureCount);
        Assert.Equal("Hello, Shruti.", clipboard.LastSetText);
    }

    [Fact]
    public async Task InsertAsync_ClipboardPreferredTargetRespectsDisabledClipboardFallback()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = CompleteResult(requestedInputCount: 28),
            PasteResult = CompleteResult(requestedInputCount: 4)
        };
        var clipboard = new FakeClipboard();
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(ProcessName: "winword"),
            "Hello, Shruti.",
            new TextInsertionOptions(AllowClipboardFallback: false),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.Equal(TextInsertionMethod.None, result.Method);
        Assert.Equal(
            "This target is more reliable with clipboard paste than direct text input. Clipboard fallback is disabled.",
            result.Message);
        Assert.Equal(0, input.SendUnicodeTextCount);
        Assert.Equal(0, input.SendPasteShortcutCount);
        Assert.Equal(0, clipboard.CaptureCount);
    }

    [Fact]
    public async Task InsertAsync_PartialDirectInputNeverFallsBackToClipboard()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = PartialResult(sentInputCount: 2, requestedInputCount: 28)
        };
        var clipboard = new FakeClipboard();
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.Equal(TextInsertionMethod.None, result.Method);
        Assert.Equal(
            "Direct input was only partially delivered. Shruti will not retry through the clipboard.",
            result.Message);
        Assert.Equal(0, clipboard.CaptureCount);
        Assert.Equal(0, input.SendPasteShortcutCount);
    }

    [Fact]
    public async Task InsertAsync_DirectInputFailureSubmitsClipboardPasteAndRestoresText()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = NoneResult(requestedInputCount: 28),
            PasteResult = CompleteResult(requestedInputCount: 4)
        };
        var clipboard = new FakeClipboard(
            new WindowsClipboardSnapshot(CanRestore: true, Text: "previous clipboard text", SequenceNumber: 11));
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.True(result.Succeeded);
        Assert.True(result.Submitted);
        Assert.Equal(TextInsertionMethod.ClipboardPaste, result.Method);
        Assert.Equal("Clipboard paste was submitted but cannot be confirmed.", result.Message);
        Assert.Equal(1, clipboard.CaptureCount);
        Assert.Equal("Hello, Shruti.", clipboard.LastSetText);
        Assert.Equal(1, clipboard.RestoreCount);
        Assert.Equal("previous clipboard text", clipboard.LastRestoredSnapshot?.Text);
        Assert.Equal(1, input.SendPasteShortcutCount);
    }

    [Fact]
    public async Task InsertAsync_ClipboardFallbackRefusesToOverwriteUnrestorableClipboardData()
    {
        var input = new FakeTextInput { UnicodeResult = NoneResult(requestedInputCount: 28) };
        var clipboard = new FakeClipboard(WindowsClipboardSnapshot.Unavailable("Clipboard has an image."));
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.Equal(TextInsertionMethod.None, result.Method);
        Assert.Equal("Clipboard has an image.", result.Message);
        Assert.Equal(0, clipboard.SetTextCount);
        Assert.Equal(0, input.SendPasteShortcutCount);
    }

    [Fact]
    public async Task InsertAsync_RestoresClipboardWhenTemporaryWriteFails()
    {
        var input = new FakeTextInput { UnicodeResult = NoneResult(requestedInputCount: 28) };
        var clipboard = new FakeClipboard(
            new WindowsClipboardSnapshot(CanRestore: true, Text: "previous", SequenceNumber: 11))
        {
            WriteResult = new WindowsClipboardWriteResult(
                WindowsClipboardWriteOutcome.FailedAfterModification,
                ExpectedSequenceNumber: 12,
                Message: "Shruti could not write the transcript to the clipboard.")
        };
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.Equal(1, clipboard.RestoreCount);
        Assert.Equal(12u, clipboard.LastExpectedSequenceNumber);
        Assert.Equal("Shruti could not write the transcript to the clipboard.", result.Message);
    }

    [Fact]
    public async Task InsertAsync_PasteFailureRestoresClipboard()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = NoneResult(requestedInputCount: 28),
            PasteResult = NoneResult(requestedInputCount: 4)
        };
        var clipboard = new FakeClipboard(
            new WindowsClipboardSnapshot(CanRestore: true, Text: "previous", SequenceNumber: 11));
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.Equal(1, clipboard.RestoreCount);
        Assert.Equal("Clipboard paste could not be sent to the target app.", result.Message);
    }

    [Fact]
    public async Task InsertAsync_DoesNotOverwriteClipboardWhenOwnershipChanges()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = NoneResult(requestedInputCount: 28),
            PasteResult = CompleteResult(requestedInputCount: 4)
        };
        var clipboard = new FakeClipboard(
            new WindowsClipboardSnapshot(CanRestore: true, Text: "previous", SequenceNumber: 11))
        {
            RestoreResult = new WindowsClipboardRestoreResult(
                WindowsClipboardRestoreOutcome.SkippedClipboardChanged)
        };
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.True(result.Succeeded);
        Assert.True(result.Submitted);
        Assert.Equal("Clipboard paste was submitted but cannot be confirmed.", result.Message);
        Assert.Equal(1, clipboard.RestoreCount);
    }

    [Fact]
    public async Task InsertAsync_ReportsClipboardRestorationFailureAfterSubmittedPaste()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = NoneResult(requestedInputCount: 28),
            PasteResult = CompleteResult(requestedInputCount: 4)
        };
        var clipboard = new FakeClipboard(
            new WindowsClipboardSnapshot(CanRestore: true, Text: "previous", SequenceNumber: 11))
        {
            RestoreResult = new WindowsClipboardRestoreResult(
                WindowsClipboardRestoreOutcome.Failed)
        };
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.True(result.Succeeded);
        Assert.True(result.Submitted);
        Assert.Equal(TextInsertionMethod.ClipboardPaste, result.Method);
        Assert.Equal(
            "Clipboard paste was submitted but cannot be confirmed, and Shruti could not restore the previous clipboard text.",
            result.Message);
    }

    [Fact]
    public async Task InsertAsync_DoesNotReplaceSelectedTextWithoutExplicitPermission()
    {
        var input = new FakeTextInput { UnicodeResult = CompleteResult(requestedInputCount: 28) };
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            new FakeClipboard());

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(HasSelectedText: true),
            "Hello, Shruti.",
            new TextInsertionOptions(AllowReplacingSelection: false),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.Equal(0, input.SendUnicodeTextCount);
    }

    [Fact]
    public async Task InsertAsync_ClipboardPreferredTargetDoesNotReplaceSelectedTextWithoutExplicitPermission()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = CompleteResult(requestedInputCount: 28),
            PasteResult = CompleteResult(requestedInputCount: 4)
        };
        var clipboard = new FakeClipboard();
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            clipboard);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(HasSelectedText: true, ProcessName: "winword"),
            "Hello, Shruti.",
            new TextInsertionOptions(AllowReplacingSelection: false),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.Equal(TextInsertionMethod.None, result.Method);
        Assert.Equal(
            "The target has selected text. Enable explicit replacement permission before inserting.",
            result.Message);
        Assert.Equal(0, input.SendUnicodeTextCount);
        Assert.Equal(0, input.SendPasteShortcutCount);
        Assert.Equal(0, clipboard.CaptureCount);
    }

    [Fact]
    public async Task InsertAsync_ReplacesSelectedTextWhenExplicitPermissionIsEnabled()
    {
        var input = new FakeTextInput { UnicodeResult = CompleteResult(requestedInputCount: 28) };
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            new FakeClipboard());

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(HasSelectedText: true),
            "Hello, Shruti.",
            new TextInsertionOptions(AllowReplacingSelection: true),
            CancellationToken.None);

        Assert.True(result.Inserted);
        Assert.Equal(TextInsertionMethod.DirectInput, result.Method);
        Assert.Equal(1, input.SendUnicodeTextCount);
    }

    [Fact]
    public async Task InsertAsync_AllowsWhenTheCurrentFocusedFieldHasUnknownSelectionState()
    {
        var input = new FakeTextInput { UnicodeResult = CompleteResult(requestedInputCount: 28) };
        var focusedElementInspector = new FakeFocusedElementInspector(
            new FocusedElementSnapshot(
                AutomationElementId: "Edit",
                IsEditable: true,
                HasSelectedText: null));
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            new FakeClipboard(),
            focusedElementInspector);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.True(result.Inserted);
        Assert.Equal(1, input.SendUnicodeTextCount);
    }

    [Fact]
    public async Task InsertAsync_RefusesWhenTheCurrentFocusedFieldIsConfirmedNotEditable()
    {
        var input = new FakeTextInput { UnicodeResult = CompleteResult(requestedInputCount: 28) };
        var focusedElementInspector = new FakeFocusedElementInspector(
            new FocusedElementSnapshot(
                AutomationElementId: "ReadOnly",
                IsEditable: false,
                HasSelectedText: false));
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            new FakeClipboard(),
            focusedElementInspector);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.Equal("The currently focused field is not editable.", result.Message);
        Assert.Equal(0, input.SendUnicodeTextCount);
    }

    [Fact]
    public async Task InsertAsync_RetriesFocusedElementInspectionBeforeSendingInput()
    {
        var input = new FakeTextInput { UnicodeResult = CompleteResult(requestedInputCount: 28) };
        var focusedElementInspector = new FakeFocusedElementInspector(
            null,
            new FocusedElementSnapshot(
                AutomationElementId: "Edit",
                IsEditable: true,
                HasSelectedText: false));
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            new FakeClipboard(),
            focusedElementInspector);

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.True(result.Inserted);
        Assert.Equal(2, focusedElementInspector.CaptureCount);
        Assert.Equal(1, input.SendUnicodeTextCount);
    }

    [Fact]
    public async Task InsertAsync_ReleasesPasteKeysAfterPartialPasteSubmission()
    {
        var input = new FakeTextInput
        {
            UnicodeResult = NoneResult(requestedInputCount: 28),
            PasteResult = PartialResult(sentInputCount: 1, requestedInputCount: 4)
        };
        var service = CreateService(
            new FakeWindowing { IsWindowResult = true },
            input,
            new FakeClipboard());

        TextInsertionResult result = await service.InsertAsync(
            CreateTarget(),
            "Hello, Shruti.",
            new TextInsertionOptions(),
            CancellationToken.None);

        Assert.False(result.Inserted);
        Assert.Equal("Clipboard paste was only partially delivered.", result.Message);
        Assert.Equal(1, input.ReleasePasteShortcutKeysCount);
    }

    private static WindowsTextInsertionService CreateService(
        IWindowsWindowing windowing,
        IWindowsTextInput? textInput = null,
        IWindowsClipboard? clipboard = null,
        IWindowsFocusedElementInspector? focusedElementInspector = null)
    {
        return new WindowsTextInsertionService(
            windowing,
            textInput ?? new FakeTextInput(),
            clipboard ?? new FakeClipboard(),
            focusedElementInspector ?? FakeFocusedElementInspector.EditableWithoutSelection,
            clipboardPasteSettleDelay: TimeSpan.Zero,
            focusedElementSettleDelay: TimeSpan.Zero);
    }

    private static FocusTarget CreateTarget(
        bool? IsEditable = true,
        bool? HasSelectedText = false,
        bool IsElevated = false,
        string ProcessName = "notepad",
        string? WindowTitle = "Untitled - Notepad")
    {
        return new FocusTarget(
            new IntPtr(42),
            ProcessId: 123,
            ProcessName: ProcessName,
            WindowTitle: WindowTitle,
            IsEditable: IsEditable,
            HasSelectedText: HasSelectedText,
            IsElevated: IsElevated,
            ThreadId: 456);
    }

    private static WindowsInputSendResult NoneResult(uint requestedInputCount)
    {
        return new WindowsInputSendResult(
            WindowsInputSendOutcome.None,
            SentInputCount: 0,
            requestedInputCount);
    }

    private static WindowsInputSendResult PartialResult(
        uint sentInputCount,
        uint requestedInputCount)
    {
        return new WindowsInputSendResult(
            WindowsInputSendOutcome.Partial,
            sentInputCount,
            requestedInputCount);
    }

    private static WindowsInputSendResult CompleteResult(uint requestedInputCount)
    {
        return new WindowsInputSendResult(
            WindowsInputSendOutcome.Complete,
            requestedInputCount,
            requestedInputCount);
    }

    private sealed class FakeWindowing : IWindowsWindowing
    {
        public bool IsWindowResult { get; init; }

        public IntPtr GetForegroundWindow()
        {
            return IntPtr.Zero;
        }

        public WindowsWindowSnapshot? CaptureWindow(IntPtr windowHandle)
        {
            return null;
        }

        public bool IsWindow(IntPtr windowHandle)
        {
            return IsWindowResult;
        }

        public bool IsMinimized(IntPtr windowHandle)
        {
            return false;
        }

        public bool RestoreWindow(IntPtr windowHandle)
        {
            return true;
        }

        public bool SetForegroundWindow(IntPtr windowHandle)
        {
            return true;
        }
    }

    private sealed class FakeTextInput : IWindowsTextInput
    {
        public WindowsInputSendResult UnicodeResult { get; init; } = NoneResult(0);

        public WindowsInputSendResult PasteResult { get; init; } = NoneResult(0);

        public int SendUnicodeTextCount { get; private set; }

        public int SendPasteShortcutCount { get; private set; }

        public string? LastUnicodeText { get; private set; }

        public WindowsInputSendResult SendUnicodeText(string text)
        {
            SendUnicodeTextCount++;
            LastUnicodeText = text;
            return UnicodeResult;
        }

        public WindowsInputSendResult SendPasteShortcut()
        {
            SendPasteShortcutCount++;
            return PasteResult;
        }

        public int ReleasePasteShortcutKeysCount { get; private set; }

        public WindowsInputSendResult ReleasePasteShortcutKeys()
        {
            ReleasePasteShortcutKeysCount++;
            return CompleteResult(requestedInputCount: 2);
        }
    }

    private sealed class FakeClipboard : IWindowsClipboard
    {
        private readonly WindowsClipboardSnapshot _snapshot;

        public FakeClipboard(WindowsClipboardSnapshot? snapshot = null)
        {
            _snapshot = snapshot ?? new WindowsClipboardSnapshot(
                CanRestore: true,
                Text: null,
                SequenceNumber: 11);
        }

        public WindowsClipboardWriteResult WriteResult { get; init; } = new(
            WindowsClipboardWriteOutcome.TemporaryTextWritten,
            ExpectedSequenceNumber: 12);

        public WindowsClipboardRestoreResult RestoreResult { get; init; } = new(
            WindowsClipboardRestoreOutcome.Restored);

        public int CaptureCount { get; private set; }

        public int SetTextCount { get; private set; }

        public int RestoreCount { get; private set; }

        public uint LastExpectedSequenceNumber { get; private set; }

        public string? LastSetText { get; private set; }

        public WindowsClipboardSnapshot? LastRestoredSnapshot { get; private set; }

        public WindowsClipboardSnapshot Capture()
        {
            CaptureCount++;
            return _snapshot;
        }

        public WindowsClipboardWriteResult SetText(
            string text,
            uint expectedSequenceNumber)
        {
            SetTextCount++;
            LastSetText = text;
            return WriteResult;
        }

        public WindowsClipboardRestoreResult RestoreIfUnchanged(
            WindowsClipboardSnapshot snapshot,
            uint expectedSequenceNumber)
        {
            RestoreCount++;
            LastRestoredSnapshot = snapshot;
            LastExpectedSequenceNumber = expectedSequenceNumber;
            return RestoreResult;
        }
    }

    private sealed class FakeFocusedElementInspector : IWindowsFocusedElementInspector
    {
        public static FakeFocusedElementInspector EditableWithoutSelection { get; } = new(
            new FocusedElementSnapshot(
                AutomationElementId: "Edit",
                IsEditable: true,
                HasSelectedText: false));

        private readonly Queue<FocusedElementSnapshot?> _snapshots;

        public FakeFocusedElementInspector(params FocusedElementSnapshot?[] snapshots)
        {
            _snapshots = new Queue<FocusedElementSnapshot?>(
                snapshots.Length == 0 ? [null] : snapshots);
        }

        public int CaptureCount { get; private set; }

        public FocusedElementSnapshot? CaptureFocusedElement(IntPtr ownerWindowHandle)
        {
            CaptureCount++;
            if (_snapshots.Count > 1)
            {
                return _snapshots.Dequeue();
            }

            return _snapshots.Peek();
        }
    }
}
