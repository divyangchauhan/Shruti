using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Shruti.Core.Platform;
using Shruti.Core.TranscriptQuality;

namespace Shruti.Platform.Windows;

public interface IWindowsTextContextSource
{
    TranscriptContext? Read(FocusTarget target);
}

public sealed class WindowsTextContextReader(IWindowsTextContextSource? source = null) : ITextContextReader
{
    private readonly IWindowsTextContextSource _source = source ?? new AutomationTextContextSource();
    public async Task<TranscriptContext?> ReadAsync(FocusTarget target, CancellationToken cancellationToken)
    {
        if (IsExcluded(target)) return null;
        try
        {
            var context = await Task.Run(() => _source.Read(target), cancellationToken)
                .WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            return context is null || (context.BeforeCaret?.Length ?? 0) > 512 ||
                (context.AfterCaret?.Length ?? 0) > 512 || (context.SelectedText?.Length ?? 0) > 512 ? null : context;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    public static bool IsExcluded(FocusTarget target)
    {
        if (target.ProcessId == Environment.ProcessId || target.ProcessName.Contains("shruti", StringComparison.OrdinalIgnoreCase)) return true;
        string description = target.ProcessName + " " + target.WindowTitle;
        string[] sensitive = ["bank", "payment", "paypal", "paytm", "phonepe", "gpay", "hdfc", "icici", "netbanking", "stripe", "checkout", "upi", "yono"];
        return sensitive.Any(term => description.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static TranscriptAppCategory Classify(string processName) => processName.ToLowerInvariant().Replace(".exe", "") switch
    {
        "outlook" or "olk" or "gmail" => TranscriptAppCategory.Email,
        "slack" or "teams" or "ms-teams" => TranscriptAppCategory.WorkMessaging,
        "whatsapp" or "telegram" => TranscriptAppCategory.PersonalMessaging,
        "winword" or "word" or "notepad" or "notion" => TranscriptAppCategory.Document,
        _ => TranscriptAppCategory.Other
    };

    private sealed class AutomationTextContextSource : IWindowsTextContextSource
    {
        public TranscriptContext? Read(FocusTarget target)
        {
            try
            {
                AutomationElement element = AutomationElement.FocusedElement;
                if (element.Current.ProcessId != target.ProcessId || !element.Current.HasKeyboardFocus ||
                    element.Current.IsPassword || !element.Current.IsEnabled) return null;
                if (target.AutomationElementId is { Length: > 0 } id && element.Current.AutomationId != id) return null;
                AutomationElement? ancestor = element;
                bool belongs = false;
                for (int depth = 0; ancestor is not null && depth < 32; depth++)
                {
                    if (ancestor.Current.NativeWindowHandle == unchecked((int)target.WindowHandle.ToInt64())) { belongs = true; break; }
                    ancestor = TreeWalker.ControlViewWalker.GetParent(ancestor);
                }
                if (!belongs) return null;
                string identity = (element.Current.AutomationId + " " + element.Current.Name).ToLowerInvariant();
                if (new[] { "address", "omnibox", "urlbar", "url bar", "search or enter", "numeric", "pin", "credit card" }
                    .Any(identity.Contains)) return null;
                if (element.TryGetCurrentPattern(RangeValuePattern.Pattern, out _)) return null;
                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject) || patternObject is not TextPattern pattern) return null;
                TextPatternRange[] selections = pattern.GetSelection();
                if (selections.Length != 1) return null;
                TextPatternRange selection = selections[0];
                string selected = selection.GetText(513);
                if (selected.Length > 512) return null;
                TextPatternRange before = selection.Clone();
                before.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection, TextPatternRangeEndpoint.Start);
                before.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -512);
                TextPatternRange after = selection.Clone();
                after.MoveEndpointByRange(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End);
                after.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 512);
                return new(before.GetText(512), selected, after.GetText(512), Classify(target.ProcessName));
            }
            catch (Exception ex) when (ex is COMException or ElementNotAvailableException or InvalidOperationException or UnauthorizedAccessException)
            { return null; }
        }
    }
}
