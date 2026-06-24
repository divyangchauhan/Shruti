namespace Shruti.Core.Triggers;

public sealed record TriggerConfiguration(
    bool EnableGlobalHotkey,
    bool EnablePushToTalk,
    bool EnableFloatingButton,
    bool EnableTrayMenu,
    string? HotkeyGesture,
    string? PushToTalkKey = "Ctrl+Win+Space",
    bool EnableFloatingWindowShortcut = true,
    string? FloatingWindowShortcut = "Ctrl+Alt+M");
