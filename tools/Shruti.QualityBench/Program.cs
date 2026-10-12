using System.Text.Json;
using Shruti.QualityBench;

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
string manifest = args.FirstOrDefault() ?? "tests/quality/manifest.json";
QualityCase[] cases = JsonSerializer.Deserialize<QualityCase[]>(await File.ReadAllTextAsync(manifest), jsonOptions)!;
var rows = cases.Select(test =>
{
    string raw = test.SavedRaw ?? test.ExpectedRaw;
    var score = QualityScorer.WordError(test.ExpectedRaw, raw);
    return new { test.Id, Stage = "saved-raw-baseline", Raw = raw, Final = raw, score.Errors,
        score.ReferenceWords, ExactMatch = raw == test.ExpectedFinal, test.AudioSource, test.Tags };
}).ToArray();
Directory.CreateDirectory("artifacts/quality");
await File.WriteAllTextAsync("artifacts/quality/baseline.json", JsonSerializer.Serialize(rows, jsonOptions));
await File.WriteAllTextAsync("artifacts/quality/baseline.md", "# Saved raw baseline\n\nThis evaluates saved text, not audio recognition.\n\n| Case | Formatting exact match |\n| --- | --- |\n" +
    string.Join("\n", rows.Select(row => $"| {row.Id} | {row.ExactMatch} |")) + "\n");
Console.WriteLine($"{rows.Length} cases. Reports in artifacts/quality.");
