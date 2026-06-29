namespace Shruti.Workflow.Dictation;

public static class InsertionCompatibilityTestTexts
{
    private static readonly string[] Texts =
    [
        "Short insertion test.",
        "Plain English sentence for a basic edit box.",
        "Numbers and symbols: 0123456789 !@#$%^&*()_+-=[]{};':\",./<>?",
        "Quotes test: \"double\", 'single', and `backtick` characters.",
        "Paths test: C:\\Users\\divya\\Documents\\Shruti\\notes.txt",
        "URL test: https://example.test/search?q=shruti+dictation&mode=compat",
        "Email-style text: qa+shruti@example.test replied at 09:42.",
        "Comma list: alpha, beta, gamma, delta, epsilon.",
        "Parentheses and brackets: (one) [two] {three} <four>.",
        "Markdown sample: **bold**, _italic_, `code`, and [link](https://example.test).",
        "JSON-ish sample: {\"app\":\"Shruti\",\"mode\":\"insertion-test\",\"ok\":true}",
        "CSV row: name,city,count,notes",
        "Currency sample: USD 12.34, EUR 56.78, INR \u20B91,234.00.",
        "Math sample: 2 + 2 = 4; pi ~= \u03C0; sqrt(81) = 9.",
        "Checklist sample: [ ] pending item, [x] completed item, [!] blocked item.",
        "Unicode accents: caf\u00E9, r\u00E9sum\u00E9, na\u00EFve, jalape\u00F1o, fa\u00E7ade, co\u00F6perate.",
        "Escaped Unicode sample: \u2713 check, \u2605 star, \u2192 arrow.",
        "Emoji sample: smile \U0001F642 rocket \U0001F680 keyboard \u2328.",
        "Hindi sample: \u0928\u092e\u0938\u094d\u0924\u0947 Shruti test.",
        "CJK sample: \u4F60\u597D, \u3053\u3093\u306B\u3061\u306F, \uC548\uB155.",
        "RTL sample: Arabic \u0645\u0631\u062D\u0628\u0627 and Hebrew \u05E9\u05DC\u05D5\u05DD.",
        "Line separator sample: first line / second line / third line.",
        "Column text sample: left | middle | right.",
        "Prompt-style text: Refactor the selected method, keep behavior unchanged, and list verification steps.",
        "Meeting note: Decision - keep insertion local-first; Owner - Divya; Due - Friday.",
        "Support reply: Thanks for the report. I reproduced the issue and am checking the focus restore path.",
        "Long paragraph: Shruti is testing whether a focused Windows app accepts inserted text after a shortcut-triggered target capture. This sentence is intentionally longer than a normal chat reply so line wrapping, paste fallback, and direct input behavior are easier to inspect.",
        "Very long paragraph: This compatibility payload includes punctuation, numbers like 12345, repeated clauses, and enough length to reveal truncation or partial insertion failures. If the entire paragraph appears in the target app and also remains visible in Shruti's transcript preview, the capture, restore, and insertion path probably completed for this target.",
        "Code-like sample: public string Echo(string value) => value.Trim();",
        "PowerShell-themed sample, not a command to run: Get-Process Shruti.App.WinUI then inspect Id and ProcessName.",
        "SQL-themed sample, not a command to run: select id and transcript_text from dictation_sessions.",
        "Shell-themed sample, not a command to run: git status followed by a test command.",
        "Mixed punctuation: one... two---three; four/five\\six | seven & eight.",
        "Filename sample: 2026-06-29_shruti-insertion-test_final-v2.md",
        "Sentence with ellipsis... then an em dash represented as -- for ASCII source compatibility.",
        "Boundary sample: start<<<<<middle>>>>>end"
    ];

    public static IReadOnlyList<string> All => Texts;

    public static string SelectRandom()
    {
        return Texts[Random.Shared.Next(Texts.Length)];
    }
}
