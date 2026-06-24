using Shruti.Core.Dictation;
using Shruti.Core.Triggers;

namespace Shruti.Storage;

public sealed record ShrutiSettings
{
    public static ShrutiSettings Default { get; } = new();

    public string? AudioInputDeviceId { get; init; }

    public DictationInsertionMode InsertionMode { get; init; } = DictationInsertionMode.AutoInsert;

    public AppThemePreference ThemePreference { get; init; } = AppThemePreference.System;

    public AudioRetentionPolicy AudioRetentionPolicy { get; init; } = AudioRetentionPolicy.DeleteAfterTranscription;

    public TriggerConfiguration TriggerConfiguration { get; init; } = new(
        EnableGlobalHotkey: false,
        EnablePushToTalk: true,
        EnableFloatingButton: true,
        EnableTrayMenu: true,
        HotkeyGesture: "Ctrl+Win+Space",
        PushToTalkKey: "Ctrl+Win+Space",
        EnableFloatingWindowShortcut: true,
        FloatingWindowShortcut: "Ctrl+Alt+M");
}
