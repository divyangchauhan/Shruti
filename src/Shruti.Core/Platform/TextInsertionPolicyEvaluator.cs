namespace Shruti.Core.Platform;

public sealed class TextInsertionPolicyEvaluator
{
    private static readonly TextInsertionPolicy TerminalPreviewPolicy = new(
        "terminal.preview-required",
        TextInsertionPolicyMode.PreviewRequired,
        "Terminal and shell targets require preview before insertion.");

    private static readonly TextInsertionPolicy OfficeClipboardPolicy = new(
        "office.clipboard-preferred",
        TextInsertionPolicyMode.ClipboardPastePreferred,
        "This target is more reliable with clipboard paste than direct text input.");

    public static IReadOnlyList<TextInsertionPolicyRule> DefaultRules { get; } =
    [
        new TextInsertionPolicyRule(
            "terminal-processes",
            [
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
            ],
            TerminalPreviewPolicy),
        new TextInsertionPolicyRule(
            "office-document-processes",
            [
                "excel",
                "onenote",
                "outlook",
                "powerpnt",
                "winword"
            ],
            OfficeClipboardPolicy)
    ];

    private readonly IReadOnlyList<TextInsertionPolicyRule> _rules;

    public TextInsertionPolicyEvaluator()
        : this(DefaultRules)
    {
    }

    public TextInsertionPolicyEvaluator(IEnumerable<TextInsertionPolicyRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.ToArray();
    }

    public TextInsertionPolicy Evaluate(FocusTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        foreach (TextInsertionPolicyRule rule in _rules)
        {
            if (rule.Matches(target))
            {
                return rule.Policy;
            }
        }

        return TextInsertionPolicy.Default;
    }

    public static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        string trimmedProcessName = processName.Trim();
        string fileName = Path.GetFileName(trimmedProcessName);
        return Path.GetFileNameWithoutExtension(fileName);
    }
}
