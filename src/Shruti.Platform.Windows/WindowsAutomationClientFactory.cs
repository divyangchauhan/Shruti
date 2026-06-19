namespace Shruti.Platform.Windows;

public sealed class WindowsAutomationClientFactory : IWindowsAutomationClientFactory
{
    private static readonly Guid AutomationClientClassId = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    public object? CreateAutomationClient()
    {
        Type? automationType = ResolveAutomationClientType();
        return automationType is null ? null : Activator.CreateInstance(automationType);
    }

    public Type? ResolveAutomationClientType()
    {
        return Type.GetTypeFromProgID("UIAutomationClient.CUIAutomation") ??
            Type.GetTypeFromCLSID(AutomationClientClassId);
    }
}
