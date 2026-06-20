namespace Shruti.Models;

public sealed class HttpModelDownloadClient : IModelDownloadClient
{
    private readonly HttpClient _httpClient;

    public HttpModelDownloadClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task DownloadAsync(
        Uri source,
        string destinationPath,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        using HttpResponseMessage response = await _httpClient
            .GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            useAsync: true);

        var buffer = new byte[81_920];
        long bytesReceived = 0;
        int read;

        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            bytesReceived += read;
            progress?.Report(new ModelDownloadProgress(bytesReceived, totalBytes));
        }
    }
}
