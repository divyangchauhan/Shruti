using Shruti.Workflow.Dictation;
using Shruti.Core;
using Shruti.Core.Dictation;
using Xunit;

namespace Shruti.Tests;

public sealed class DictationShellControllerTests
{
    [Fact]
    public async Task StartAsync_CompletesAfterAudioCaptureSessionStarts()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();

        await controller.StartAsync(DictationInsertionMode.AutoInsert);

        Assert.Equal(1, services.AudioCapture.StartCount);
        Assert.True(controller.State.IsRunning);

        await controller.StopAsync();
    }

    [Fact]
    public async Task AutoInsert_StopCompletesAndInsertsMockTranscript()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();

        await controller.StartAsync(DictationInsertionMode.AutoInsert);
        await controller.StopAsync();

        Assert.Equal(DictationSessionState.Complete, controller.State.SessionState);
        Assert.Equal(DictationRunOutcome.Inserted, controller.LastResult?.Outcome);
        Assert.Equal("hello from the Shruti mock dictation loop", controller.State.TranscriptPreview);
        Assert.Equal(controller.State.TranscriptPreview, services.TextInsertion.LastInsertedText);
        Assert.Equal(1, services.TargetFocus.CaptureCount);
        Assert.Equal(1, services.TargetFocus.RestoreCount);
        Assert.Equal(1, services.TextInsertion.InsertCount);
        Assert.False(controller.State.IsRunning);
        Assert.True(controller.State.CanCopy);
    }

    [Fact]
    public async Task PreviewFirst_StopLeavesTranscriptWithoutInsertion()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();

        await controller.StartAsync(DictationInsertionMode.PreviewFirst);
        await controller.StopAsync();

        Assert.Equal(DictationRunOutcome.PreviewRequired, controller.LastResult?.Outcome);
        Assert.Equal("hello from the Shruti mock dictation loop", controller.State.TranscriptPreview);
        Assert.Equal(0, services.TextInsertion.InsertCount);
        Assert.Equal(0, services.TargetFocus.RestoreCount);

        bool copied = await controller.CopyTranscriptAsync();

        Assert.True(copied);
        Assert.Equal(controller.State.TranscriptPreview, services.Clipboard.LastCopiedText);
    }

    [Fact]
    public async Task PreviewFirst_InsertPreviewInsertsEditedTranscript()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();

        await controller.StartAsync(DictationInsertionMode.PreviewFirst);
        await controller.StopAsync();

        Assert.True(controller.State.CanInsertPreview);

        await controller.InsertPreviewAsync("edited preview text", allowReplacingSelection: true);

        Assert.Equal(DictationRunOutcome.Inserted, controller.LastResult?.Outcome);
        Assert.Equal("edited preview text", controller.State.TranscriptPreview);
        Assert.Equal("edited preview text", services.TextInsertion.LastInsertedText);
        Assert.True(services.TextInsertion.LastOptions?.AllowReplacingSelection);
        Assert.False(controller.State.CanInsertPreview);
    }

    [Fact]
    public async Task PreviewFirst_EmptyPreviewIsNotInserted()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();

        await controller.StartAsync(DictationInsertionMode.PreviewFirst);
        await controller.StopAsync();
        await controller.InsertPreviewAsync("   ", allowReplacingSelection: false);

        Assert.Equal(DictationRunOutcome.PreviewRequired, controller.LastResult?.Outcome);
        Assert.Equal(0, services.TextInsertion.InsertCount);
        Assert.True(controller.State.CanInsertPreview);
        Assert.Equal("Transcript is empty", controller.State.StatusText);
    }

    [Fact]
    public async Task PreviewFirst_InsertPreviewDoesNotEnableCancellationWithoutAnActiveRun()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();
        var insertionCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        services.TextInsertion.InsertCompletion = insertionCompletion;

        await controller.StartAsync(DictationInsertionMode.PreviewFirst);
        await controller.StopAsync();
        Task insertion = controller.InsertPreviewAsync("edited preview text", allowReplacingSelection: false);
        await WaitForStateAsync(
            controller,
            state => state.SessionState == DictationSessionState.InsertingText);

        Assert.False(controller.State.CanCancel);

        insertionCompletion.SetResult();
        await insertion;
    }

    [Fact]
    public async Task CopyOnly_StopCopiesTranscriptWithoutInsertion()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();

        await controller.StartAsync(DictationInsertionMode.CopyOnly);
        await controller.StopAsync();

        Assert.Equal(DictationRunOutcome.CopyOnly, controller.LastResult?.Outcome);
        Assert.Equal("hello from the Shruti mock dictation loop", services.Clipboard.LastCopiedText);
        Assert.Equal(0, services.TextInsertion.InsertCount);
        Assert.Equal(0, services.TargetFocus.RestoreCount);
        Assert.True(controller.State.CanCopy);
    }

    [Fact]
    public async Task Cancel_DoesNotInsertOrCopyTranscript()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();

        await controller.StartAsync(DictationInsertionMode.AutoInsert);
        await controller.CancelAsync();

        Assert.Equal(DictationRunOutcome.Cancelled, controller.LastResult?.Outcome);
        Assert.Equal(DictationSessionState.Cancelled, controller.State.SessionState);
        Assert.Equal(string.Empty, controller.State.TranscriptPreview);
        Assert.Null(services.Clipboard.LastCopiedText);
        Assert.Equal(0, services.TextInsertion.InsertCount);
        Assert.False(controller.State.IsRunning);
    }

    [Fact]
    public async Task Retry_StartsAnotherMockRunWithSelectedMode()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();

        controller.SetInsertionMode(DictationInsertionMode.PreviewFirst);
        await controller.StartAsync(DictationInsertionMode.PreviewFirst);
        await controller.StopAsync();
        await controller.RetryAsync();
        await controller.StopAsync();

        Assert.Equal(2, services.AudioCapture.StartCount);
        Assert.Equal(DictationInsertionMode.PreviewFirst, controller.State.InsertionMode);
        Assert.Equal(0, services.TextInsertion.InsertCount);
    }

    [Fact]
    public async Task Pause_TogglesMockRecordingStateAndStillCompletes()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();

        await controller.StartAsync(DictationInsertionMode.AutoInsert);
        await WaitForStateAsync(controller, state => state.SessionState == DictationSessionState.Recording);

        await controller.PauseAsync();

        Assert.Equal(DictationSessionState.Paused, controller.State.SessionState);
        Assert.True(controller.State.IsPaused);
        Assert.True(controller.State.CanPause);
        Assert.Equal(1, services.AudioCapture.PauseCount);

        await controller.PauseAsync();

        Assert.Equal(DictationSessionState.Recording, controller.State.SessionState);
        Assert.False(controller.State.IsPaused);
        Assert.True(controller.State.CanPause);
        Assert.Equal(1, services.AudioCapture.ResumeCount);

        await controller.StopAsync();

        Assert.Equal(DictationRunOutcome.Inserted, controller.LastResult?.Outcome);
        Assert.Equal("hello from the Shruti mock dictation loop", services.TextInsertion.LastInsertedText);
    }

    [Fact]
    public async Task SetInsertionMode_WhileRunningKeepsActiveRunMode()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();

        await controller.StartAsync(DictationInsertionMode.CopyOnly);
        controller.SetInsertionMode(DictationInsertionMode.AutoInsert);
        await controller.StopAsync();

        Assert.Equal(DictationInsertionMode.CopyOnly, controller.State.InsertionMode);
        Assert.Equal(DictationRunOutcome.CopyOnly, controller.LastResult?.Outcome);
        Assert.Equal(0, services.TextInsertion.InsertCount);
    }

    [Fact]
    public async Task SetAudioInputDevice_UsesSelectedMicrophoneForTheNextRun()
    {
        var services = MockDictationAppServices.Create();
        var controller = services.CreateShellController();

        controller.SetAudioInputDevice("mock-external");
        await controller.StartAsync(DictationInsertionMode.AutoInsert);
        await controller.StopAsync();

        Assert.Equal("mock-external", services.AudioCapture.LastOptions?.DeviceId);
    }

    private static async Task WaitForStateAsync(
        DictationShellController controller,
        Func<DictationShellState, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);

        while (!predicate(controller.State))
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for state. Last state was {controller.State.SessionState}.");
            }

            await Task.Delay(25);
        }
    }
}
