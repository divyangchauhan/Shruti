using System.Text;
using Shruti.Core.Platform;
using Shruti.Core.TranscriptQuality;
using Shruti.Platform.Windows;
using Shruti.TranscriptQuality;
using Xunit;

namespace Shruti.Tests;

public sealed class TranscriptQualityTests
{
    [Fact]
    public void PreviewCorrection_OnlySuggestsOneSpellingChangeForReview()
    {
        Assert.Equal(new PreviewCorrectionSuggestion("cloud", "Claude"),
            PreviewCorrectionSuggestion.Create("Ask cloud tomorrow.", "Ask Claude tomorrow."));
        Assert.Null(PreviewCorrectionSuggestion.Create("Um send this.", "Send this."));
        Assert.Null(PreviewCorrectionSuggestion.Create("Send 42 files.", "Send 43 files."));
    }
    [Theory]
    [InlineData("hello comma world full stop new paragraph next line", "Hello, world.\n\nNext line")]
    [InlineData("Let's do coffee at two actually three.", "Let's do coffee at 3.")]
    [InlineData("numbered list one apples two oranges three pears", "1. Apples\n2. Oranges\n3. Pears")]
    [InlineData("numbered list one apples two oranges three pears four bananas", "1. Apples\n2. Oranges\n3. Pears\n4. Bananas")]
    [InlineData("I like this actually.", "I like this actually.")]
    [InlineData("We are at one with nature.", "We are at one with nature.")]
    [InlineData("hello, full stop", "Hello.")]
    public void Formatting_IsConservative(string input, string expected)
    {
        Assert.Equal(expected, DeterministicRefiner.Refine(input, [], null, TranscriptCleanupMode.Light, []));
        Assert.Equal(input, DeterministicRefiner.Refine(input, [], null, TranscriptCleanupMode.Off, []));
    }

    [Fact]
    public void Formatting_PreservesNamesQuotedCommandsAndFileNames()
    {
        Assert.Equal("OpenVINO says \"new line\" in Shruti.cs.",
            DeterministicRefiner.Refine("OpenVINO says \"new line\" in Shruti.cs.", ["OpenVINO"], null, TranscriptCleanupMode.Light, []));
        Assert.Equal("Ask Claude about OpenVINO and a dark cloud.",
            DeterministicRefiner.Refine("ask claude about openvino and a dark cloud.", ["Claude", "OpenVINO"], null, TranscriptCleanupMode.Light, []));
        Assert.Equal("Say \"openvino\" exactly.",
            DeterministicRefiner.Refine("say \"openvino\" exactly.", ["OpenVINO"], null, TranscriptCleanupMode.Light, []));
    }

    [Theory]
    [InlineData(TranscriptAppCategory.Email, "Please send the report.")]
    [InlineData(TranscriptAppCategory.Document, "Please send the report.")]
    [InlineData(TranscriptAppCategory.PersonalMessaging, "Please send the report")]
    public void Style_AddsTerminalPunctuationOnlyForStandaloneFormalText(TranscriptAppCategory category, string expected)
        => Assert.Equal(expected, DeterministicRefiner.Refine("please send the report", [],
            new("", null, "", category), TranscriptCleanupMode.Light, []));

    [Theory]
    [InlineData("We should take ", ".", "The next step.", "the next step")]
    [InlineData("Hello", "world", "Again", " again ")]
    [InlineData("Ask ", ".", "Shruti.", "Shruti")]
    [InlineData("Done. ", "", "Next", "Next")]
    [InlineData("Line\n", "", "Next", "Next")]
    [InlineData("Done", ".", ".", "")]
    public void Joining_UsesCaretBoundaries(string before, string after, string input, string expected)
        => Assert.Equal(expected, DeterministicRefiner.Join(input,
            new(before, null, after, TranscriptAppCategory.Document), ["Shruti"]));

    [Fact]
    public void Prompt_UsesPersonalTermsFirstAndNeverExceedsTokenUpperBound()
    {
        var hints = RecognitionHintBuilder.Build([new(Guid.NewGuid(), "en", "Shruti", DateTimeOffset.UtcNow)],
            new("OpenVINO", null, "Vulkan", TranscriptAppCategory.Document), [], 16);
        Assert.Equal("Shruti", hints.Terms[0]);
        Assert.True(Encoding.UTF8.GetByteCount(hints.InitialPrompt!) <= 16);
        Assert.Null(RecognitionHintBuilder.Build([], null, [], 128).InitialPrompt);
    }

    [Fact]
    public async Task ContextReader_RejectsExcludedFieldsBeforeReadingAndUnavailableAutomation()
    {
        var source = new Source(new("before", "selection", "after", TranscriptAppCategory.Document));
        var reader = new WindowsTextContextReader(source);
        Assert.Null(await reader.ReadAsync(Target("PayPal"), default));
        Assert.Null(await reader.ReadAsync(Target("Shruti.App.WinUI"), default));
        Assert.Equal(0, source.Reads);
        Assert.Equal("selection", (await reader.ReadAsync(Target("notepad"), default))!.SelectedText);
        source.Context = new(new string('a', 513), "", "", TranscriptAppCategory.Other);
        Assert.Null(await reader.ReadAsync(Target("notepad"), default));
        source.Throw = true;
        Assert.Null(await reader.ReadAsync(Target("notepad"), default));
    }

    private static FocusTarget Target(string process) => new(new IntPtr(1), -1, process, null);
    private sealed class Source(TranscriptContext? context) : IWindowsTextContextSource
    {
        public TranscriptContext? Context { get; set; } = context;
        public bool Throw { get; set; }
        public int Reads { get; private set; }
        public TranscriptContext? Read(FocusTarget target) { Reads++; if (Throw) throw new InvalidOperationException(); return Context; }
    }
}
