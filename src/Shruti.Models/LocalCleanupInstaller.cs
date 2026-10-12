using System.Security.Cryptography;

namespace Shruti.Models;

public sealed record LocalCleanupInstallation(string ModelPath, string RuntimePath);

public sealed class LocalCleanupInstaller(HttpClient client)
{
    public async Task<LocalCleanupInstallation> InstallAsync(string directory, string runtimeDirectory, CancellationToken cancellationToken)
    {
        string server = Path.Combine(runtimeDirectory, "llama-server.exe");
        if (!File.Exists(server)) throw new InvalidOperationException("The packaged local cleanup runtime is missing.");
        Directory.CreateDirectory(directory);
        string model = Path.Combine(directory, "qwen3.5-2b-q4_k_m.gguf");
        await DownloadAsync(new("https://huggingface.co/zanish-labs/qwen3.5-2b-q4_k_m-gguf/resolve/56c1090cf58b925e3bf23eb8731781e4d9c9029c/qwen3.5-2b-q4_k_m.gguf"),
            model, "92b61f139fbae199718c0c283df25cb619b1cc57e71faaae5a9a6b06a1842dda", cancellationToken).ConfigureAwait(false);
        string license = await client.GetStringAsync(new Uri("https://huggingface.co/zanish-labs/qwen3.5-2b-q4_k_m-gguf/raw/56c1090cf58b925e3bf23eb8731781e4d9c9029c/LICENSE"), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory, "Qwen-LICENSE.txt"), license, cancellationToken).ConfigureAwait(false);
        return new(model, server);
    }

    private async Task DownloadAsync(Uri uri, string destination, string hash, CancellationToken cancellationToken)
    {
        if (File.Exists(destination) && await MatchesAsync(destination, hash, cancellationToken).ConfigureAwait(false)) return;
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var output = File.Create(temporary))
                await response.Content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            if (!await MatchesAsync(temporary, hash, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("Local cleanup download failed its SHA256 check.");
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<bool> MatchesAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
