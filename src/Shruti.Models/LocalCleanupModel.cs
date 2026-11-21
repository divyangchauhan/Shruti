namespace Shruti.Models;

public sealed class LocalCleanupModel
{
    public static LocalCleanupModel Qwen { get; } = new(
        "Qwen3.5-2B", "1.3 GB · English · Q4_K_M", "qwen3.5-2b-q4_k_m.gguf",
        "zanish-labs/qwen3.5-2b-q4_k_m-gguf", "56c1090cf58b925e3bf23eb8731781e4d9c9029c",
        "92b61f139fbae199718c0c283df25cb619b1cc57e71faaae5a9a6b06a1842dda", "LICENSE", "Qwen-LICENSE.txt");

    public static LocalCleanupModel Utter { get; } = new(
        "Utter Mini 1B", "720 MB · English · Q4_0", "utter-mini-v1-q4_0.gguf",
        "ductai199x/utter", "5c5b6e0e9d3d1eeca81e5f19908d640b1dbb2212",
        "8cd6850731222133a727ef163f1ad9bb8e7e1d4e8949ac84b829ac6eedbb8dc4", "LICENSE.md", "Utter-LICENSE.txt");

    public string Name { get; }
    public string Details { get; }
    public string FileName { get; }
    public Uri DownloadUri { get; }
    public Uri LicenseUri { get; }
    internal string Sha256 { get; }
    internal string LicenseFileName { get; }

    private LocalCleanupModel(string name, string details, string fileName, string repository, string revision,
        string sha256, string license, string licenseFileName)
    {
        Name = name;
        Details = details;
        FileName = fileName;
        Sha256 = sha256;
        LicenseFileName = licenseFileName;
        DownloadUri = new($"https://huggingface.co/{repository}/resolve/{revision}/{fileName}");
        LicenseUri = new($"https://huggingface.co/{repository}/raw/{revision}/{license}");
    }

    // Existing installations store a path. Keep the original Qwen protocol for legacy filenames.
    public static LocalCleanupModel FromPath(string? path) =>
        string.Equals(Path.GetFileName(path), Utter.FileName, StringComparison.OrdinalIgnoreCase) ? Utter : Qwen;
}
