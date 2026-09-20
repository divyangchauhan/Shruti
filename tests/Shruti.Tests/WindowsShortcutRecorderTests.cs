using Shruti.Platform.Windows;
using Xunit;

namespace Shruti.Tests;

public sealed class WindowsShortcutRecorderTests
{
    [Theory]
    [InlineData(0xA2, 0x5B)]
    [InlineData(0x5B, 0xA2)]
    [InlineData(0xA3, 0x5C)]
    public void CapturesModifierOnlyHoldShortcut(uint first, uint second)
    {
        var recorder = new WindowsShortcutRecorder(true);
        recorder.Update(first, true);
        recorder.Update(second, true);
        recorder.Update(first, false);
        Assert.False(recorder.IsComplete);
        recorder.Update(second, false);
        Assert.Equal("Ctrl+Win", recorder.Gesture);
        Assert.Null(recorder.Error);
    }

    [Fact]
    public void CapturesChordOnlyAfterEveryKeyIsReleased()
    {
        var recorder = new WindowsShortcutRecorder(allowSingleKey: true);
        recorder.Update(0xA2, true);
        recorder.Update(0x5B, true);
        recorder.Update(0x20, true);
        recorder.Update(0x20, true); // Autorepeat must not add another key.
        Assert.Equal("Ctrl+Win+Space", recorder.DisplayText);
        recorder.Update(0x20, false);
        Assert.False(recorder.IsComplete);
        recorder.Update(0x5B, false);
        recorder.Update(0xA2, false);
        Assert.True(recorder.IsComplete);
        Assert.Equal("Ctrl+Win+Space", recorder.Gesture);
        Assert.Null(recorder.Error);
    }

    [Theory]
    [InlineData(0xA4, 0x09)] // Alt+Tab
    [InlineData(0x5B, 0x4C)] // Win+L
    public void RejectsReservedWindowsShortcuts(uint modifier, uint key)
    {
        var recorder = new WindowsShortcutRecorder(true);
        recorder.Update(modifier, true);
        recorder.Update(key, true);
        recorder.Update(key, false);
        recorder.Update(modifier, false);
        Assert.True(recorder.IsComplete);
        Assert.Null(recorder.Gesture);
        Assert.Contains("reserved", recorder.Error);
    }

    [Theory]
    [InlineData(0xA3, true, "RightControl")]
    [InlineData(0x77, true, "F8")]
    [InlineData(0x41, true, null)]
    [InlineData(0x77, false, null)]
    [InlineData(0xA2, false, null)]
    public void ValidatesSingleKeys(uint key, bool allowSingleKey, string? expected)
    {
        var recorder = new WindowsShortcutRecorder(allowSingleKey);
        recorder.Update(key, true);
        recorder.Update(key, false);
        Assert.True(recorder.IsComplete);
        Assert.Equal(expected, recorder.Gesture);
        Assert.Equal(expected is null, recorder.Error is not null);
    }

    [Fact]
    public void EscapeCancelsWithoutCreatingShortcut()
    {
        var recorder = new WindowsShortcutRecorder(true);
        recorder.Update(0x1B, true);
        recorder.Update(0x1B, false);
        Assert.True(recorder.IsCancelled);
        Assert.True(recorder.IsComplete);
        Assert.Null(recorder.Gesture);
    }

    [Fact]
    public void RejectsMultipleMainKeys()
    {
        var recorder = new WindowsShortcutRecorder(true);
        foreach (uint key in new uint[] { 0xA2, 0x41, 0x42 }) recorder.Update(key, true);
        foreach (uint key in new uint[] { 0x41, 0x42, 0xA2 }) recorder.Update(key, false);
        Assert.Null(recorder.Gesture);
        Assert.NotNull(recorder.Error);
    }
}
