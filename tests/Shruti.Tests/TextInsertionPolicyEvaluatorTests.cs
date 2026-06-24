using Shruti.Core.Platform;
using Xunit;

namespace Shruti.Tests;

public sealed class TextInsertionPolicyEvaluatorTests
{
    [Theory]
    [InlineData("cmd")]
    [InlineData("pwsh.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal\WindowsTerminal.exe")]
    public void Evaluate_ReturnsPreviewRequiredForTerminalProcesses(string processName)
    {
        var evaluator = new TextInsertionPolicyEvaluator();

        TextInsertionPolicy policy = evaluator.Evaluate(CreateTarget(processName));

        Assert.Equal("terminal.preview-required", policy.Id);
        Assert.Equal(TextInsertionPolicyMode.PreviewRequired, policy.Mode);
    }

    [Theory]
    [InlineData("WINWORD")]
    [InlineData("excel.exe")]
    public void Evaluate_ReturnsClipboardPreferredForOfficeProcesses(string processName)
    {
        var evaluator = new TextInsertionPolicyEvaluator();

        TextInsertionPolicy policy = evaluator.Evaluate(CreateTarget(processName));

        Assert.Equal("office.clipboard-preferred", policy.Id);
        Assert.Equal(TextInsertionPolicyMode.ClipboardPastePreferred, policy.Mode);
    }

    [Fact]
    public void Evaluate_ReturnsDefaultDirectInputForUnknownProcesses()
    {
        var evaluator = new TextInsertionPolicyEvaluator();

        TextInsertionPolicy policy = evaluator.Evaluate(CreateTarget("notepad"));

        Assert.Equal(TextInsertionPolicy.Default, policy);
        Assert.Equal(TextInsertionPolicyMode.DirectInputPreferred, policy.Mode);
    }

    private static FocusTarget CreateTarget(string processName)
    {
        return new FocusTarget(
            new IntPtr(42),
            ProcessId: 123,
            ProcessName: processName,
            WindowTitle: "Target",
            IsEditable: true,
            HasSelectedText: false,
            ThreadId: 456);
    }
}
