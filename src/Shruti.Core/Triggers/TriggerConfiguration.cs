namespace Shruti.Core.Triggers;

public sealed record TriggerConfiguration(
    bool EnableGlobalHotkey,
    bool EnablePushToTalk,
    bool EnableFloatingButton,
    bool EnableTrayMenu,
    string? HotkeyGesture);
