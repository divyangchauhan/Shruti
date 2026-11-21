using System.Globalization;
using System.Text.RegularExpressions;

namespace Shruti.TranscriptQuality;

internal static class ExplicitNumericCorrection
{
    private const string Ones = "one|two|three|four|five|six|seven|eight|nine";
    private const string Tens = "twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety";
    private const string Small = "zero|" + Ones + "|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen";
    private const string SpokenInteger = "(?:(?:" + Tens + ")(?:[ -](?:" + Ones + "))?|" + Small + ")";
    private const string DigitWord = "(?:zero|" + Ones + ")";
    private const string Number = "(?:[0-9]+(?:\\.[0-9]+)?|" + SpokenInteger + "(?: point " + DigitWord + "(?: " + DigitWord + ")*)?)";

    // Start at the word that introduces a quantity. Otherwise a match can start
    // in the middle of "one hundred and twenty" or after a minus sign.
    private static readonly Regex Correction = new(
        @"\b(?:to|at|by|for|from|of|is|was|build|version|room|order|send|need) +(?<old>" + Number +
        @"), *(?:no|sorry|I mean|wait,? no), *(?<final>" + Number + @")(?=$|[\s,;!?]|\.(?!\w))",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SingleQuotedText = new(@"(?<![\p{L}\p{N}])['‘].*?['’](?![\p{L}\p{N}])",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, int> Values = Small.Split('|')
        .Select((word, i) => KeyValuePair.Create(word, i))
        .Concat(Tens.Split('|').Select((word, i) => KeyValuePair.Create(word, 20 + 10 * i)))
        .ToDictionary(StringComparer.OrdinalIgnoreCase);

    public static bool Matches(string input, string output)
    {
        foreach (Match match in Correction.Matches(input))
        {
            Group old = match.Groups["old"], final = match.Groups["final"];
            var prefix = input.AsSpan(0, old.Index);
            var suffix = input.AsSpan(final.Index + final.Length);
            if (output.Length <= prefix.Length + suffix.Length ||
                !output.AsSpan().StartsWith(prefix, StringComparison.Ordinal) ||
                !output.AsSpan().EndsWith(suffix, StringComparison.Ordinal)) continue;

            if (SingleQuotedText.Matches(input).Any(quote => quote.Index <= old.Index && old.Index < quote.Index + quote.Length))
                continue;
            string value = output.Substring(prefix.Length, output.Length - prefix.Length - suffix.Length);
            if (value.Equals(final.Value, StringComparison.OrdinalIgnoreCase) || value == InDigits(final.Value)) return true;
            if (Regex.IsMatch(final.Value, "^(?:" + Tens + ")[ -](?:" + Ones + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                value.Replace('-', ' ').Equals(final.Value.Replace('-', ' '), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // A retained suffix alone cannot prove that the speaker abandoned everything
    // preceding an inline numeric correction.
    public static bool DiscardsCorrection(string input, int removedLength) =>
        Correction.Match(input) is { Success: true } match && match.Groups["old"].Index < removedLength;

    private static string InDigits(string number)
    {
        if (char.IsAsciiDigit(number[0])) return number;
        string[] parts = number.ToLowerInvariant().Split(" point ", StringSplitOptions.None);
        int whole = parts[0].Split([' ', '-']).Sum(word => Values[word]);
        string digits = whole.ToString(CultureInfo.InvariantCulture);
        return parts.Length == 1 ? digits : digits + "." + string.Concat(parts[1].Split(' ').Select(word => (char)('0' + Values[word])));
    }
}
