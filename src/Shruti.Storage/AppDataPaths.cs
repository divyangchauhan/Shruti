namespace Shruti.Storage;

public sealed class AppDataPaths
{
    public AppDataPaths(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        RootPath = Path.GetFullPath(rootPath);
        SettingsDirectory = Path.Combine(RootPath, "Settings");
        RecordingsDirectory = Path.Combine(RootPath, "Recordings");
        TranscriptsDirectory = Path.Combine(RootPath, "Transcripts");
        ModelsDirectory = Path.Combine(RootPath, "Models");
        ExportsDirectory = Path.Combine(RootPath, "Exports");
        LogsDirectory = Path.Combine(RootPath, "Logs");
    }

    public string RootPath { get; }

    public string SettingsDirectory { get; }

    public string RecordingsDirectory { get; }

    public string TranscriptsDirectory { get; }

    public string ModelsDirectory { get; }

    public string ExportsDirectory { get; }

    public string LogsDirectory { get; }

    public string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    public string BenchmarkCacheFilePath => Path.Combine(SettingsDirectory, "transcription-benchmarks.json");

    public string DiagnosticLogFilePath => Path.Combine(LogsDirectory, "diagnostics.jsonl");

    public string DatabaseFilePath => Path.Combine(RootPath, "shruti.db");

    public static AppDataPaths CreateDefault()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException("Windows local application data is unavailable.");
        }

        return new AppDataPaths(Path.Combine(localApplicationData, "Shruti"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(RecordingsDirectory);
        Directory.CreateDirectory(TranscriptsDirectory);
        Directory.CreateDirectory(ModelsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
