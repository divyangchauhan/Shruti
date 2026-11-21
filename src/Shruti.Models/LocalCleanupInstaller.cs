using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Shruti.Models;

public sealed record LocalCleanupInstallation(string ModelPath, string RuntimePath);

public sealed class LocalCleanupInstaller(HttpClient client)
{
    public Task<LocalCleanupInstallation> InstallAsync(string directory, string runtimeDirectory, CancellationToken cancellationToken) =>
        InstallAsync(LocalCleanupModel.Qwen, directory, runtimeDirectory, cancellationToken);

    public async Task<LocalCleanupInstallation> InstallAsync(LocalCleanupModel selection, string directory, string runtimeDirectory, CancellationToken cancellationToken)
    {
        string server = Path.Combine(runtimeDirectory, "llama-server.exe");
        if (!File.Exists(server)) throw new InvalidOperationException("The packaged local cleanup runtime is missing.");
        Directory.CreateDirectory(directory);
        string model = Path.Combine(directory, selection.FileName);
        string license = await client.GetStringAsync(selection.LicenseUri, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory, selection.LicenseFileName), license, cancellationToken).ConfigureAwait(false);
        await DownloadAsync(selection.DownloadUri, model, selection.Sha256, cancellationToken).ConfigureAwait(false);
        return new(model, server);
    }

    private async Task DownloadAsync(Uri uri, string destination, string hash, CancellationToken cancellationToken)
    {
        if (File.Exists(destination) && await MatchesAsync(destination, hash, cancellationToken).ConfigureAwait(false)) return;
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            await using (var output = File.Create(temporary))
                await DownloadWeightsAsync(uri, output, cancellationToken).ConfigureAwait(false);
            if (!await MatchesAsync(temporary, hash, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("Local cleanup download failed its SHA256 check.");
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task DownloadWeightsAsync(Uri uri, FileStream output, CancellationToken cancellationToken)
    {
        const int chunkSize = 8 * 1024 * 1024;
        long? total = null;
        do
        {
            long start = output.Position;
            long end = Math.Min(start + chunkSize, total ?? long.MaxValue) - 1;
            // Separate cache entries for each range, including the redirect to a signed download URL.
            var partUri = new UriBuilder(uri) { Query = $"part={start}" }.Uri;
            using var request = new HttpRequestMessage(HttpMethod.Get, partUri);
            request.Headers.Range = new RangeHeaderValue(start, end);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.StatusCode == HttpStatusCode.OK && start == 0)
            {
                await response.Content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                return;
            }

            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || range?.Unit != "bytes" ||
                range.From != start || range.Length is not long size || size <= start ||
                range.To != Math.Min(end, size - 1) || (total.HasValue && total != size))
                throw new InvalidDataException("The model server returned an unexpected byte range.");
            total = size;
            await response.Content.CopyToAsync(output, timeout.Token).ConfigureAwait(false);
            if (output.Position != range.To + 1) throw new InvalidDataException("The model download ended early.");
        } while (output.Position < total);
    }

    private static async Task<bool> MatchesAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
