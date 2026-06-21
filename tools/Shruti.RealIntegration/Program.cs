using Shruti.Models;
using Shruti.Storage;
using Shruti.Transcription.Abstractions;
using Shruti.Transcription.WhisperCpp;

const string jfkAudioUrl = "https://raw.githubusercontent.com/ggml-org/whisper.cpp/5ed76e9a079962f1c85cfce44edd325c27ef1f97/samples/jfk.wav";

var paths = AppDataPaths.CreateDefault();
paths.EnsureCreated();

using var httpClient = new HttpClient();
var modelManager = new ModelManager(
    paths.ModelsDirectory,
    new HttpModelDownloadClient(httpClient),
    new ModelIntegrityVerifier());
ModelCatalogEntry modelEntry = RecommendedModelCatalog.Create().GetRequiredModel("whisper-tiny-en");
var progress = new ModelDownloadProgressReporter();

ModelInstallResult install = await modelManager.DownloadAsync(modelEntry, progress, CancellationToken.None);
Console.WriteLine();
if (!install.Succeeded || install.Model is null)
{
    throw new InvalidOperationException(install.Message ?? "The verified whisper.cpp model could not be installed.");
}

string fixtureDirectory = Path.Combine(paths.RootPath, "Integration");
string fixturePath = Path.Combine(fixtureDirectory, "jfk.wav");
Directory.CreateDirectory(fixtureDirectory);
if (!File.Exists(fixturePath))
{
    Console.WriteLine("Downloading pinned speech fixture...");
    byte[] audio = await httpClient.GetByteArrayAsync(jfkAudioUrl);
    await File.WriteAllBytesAsync(fixturePath, audio);
}

byte[] pcmAudio = ReadPcm16Mono16KhzWave(fixturePath);
var provider = new WhisperCppTranscriptionProvider(
    new WhisperCppTranscriptionEngine(new WhisperCppNativeApi()));
var options = new TranscriptionSessionOptions(
    install.Model.ToTranscriptionModelDescriptor(),
    ComputeBackend.Cpu,
    "en",
    TranscriptionMode.Balanced);

await using ITranscriptionSession session = await provider.CreateSessionAsync(options, CancellationToken.None);
var partialTranscript = new TaskCompletionSource<TranscriptEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
Task eventReader = ReadEventsAsync(session.Events, partialTranscript);
int initialAudioLength = Math.Min(pcmAudio.Length, 16_000 * sizeof(short) * 3);
await session.PushAudioAsync(pcmAudio.AsMemory(0, initialAudioLength), CancellationToken.None);
TranscriptEvent partial = await partialTranscript.Task.WaitAsync(TimeSpan.FromMinutes(2));
if (initialAudioLength < pcmAudio.Length)
{
    await session.PushAudioAsync(pcmAudio.AsMemory(initialAudioLength), CancellationToken.None);
}

TranscriptResult result = await session.CompleteAsync(CancellationToken.None);
await eventReader;

Console.WriteLine($"Live: {partial.Text}");
Console.WriteLine(result.Text);
if (string.IsNullOrWhiteSpace(partial.Text))
{
    throw new InvalidOperationException("whisper.cpp did not emit a live partial transcript.");
}

if (!result.Text.Contains("ask not", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("whisper.cpp did not produce the expected JFK transcript.");
}

static async Task ReadEventsAsync(
    IAsyncEnumerable<TranscriptEvent> events,
    TaskCompletionSource<TranscriptEvent> partialTranscript)
{
    await foreach (TranscriptEvent transcriptEvent in events)
    {
        if (transcriptEvent.Kind == TranscriptEventKind.PartialText &&
            !string.IsNullOrWhiteSpace(transcriptEvent.Text))
        {
            partialTranscript.TrySetResult(transcriptEvent);
        }
    }
}

static byte[] ReadPcm16Mono16KhzWave(string path)
{
    using var stream = File.OpenRead(path);
    using var reader = new BinaryReader(stream);

    if (ReadFourCc(reader) != "RIFF" || reader.ReadInt32() < 0 || ReadFourCc(reader) != "WAVE")
    {
        throw new InvalidOperationException("The speech fixture is not a RIFF/WAVE file.");
    }

    short audioFormat = 0;
    short channels = 0;
    int sampleRate = 0;
    short bitsPerSample = 0;
    byte[]? data = null;

    while (stream.Position < stream.Length)
    {
        string chunkId = ReadFourCc(reader);
        int chunkLength = reader.ReadInt32();
        if (chunkLength < 0 || stream.Position + chunkLength > stream.Length)
        {
            throw new InvalidOperationException("The speech fixture contains an invalid WAVE chunk.");
        }

        if (chunkId == "fmt ")
        {
            audioFormat = reader.ReadInt16();
            channels = reader.ReadInt16();
            sampleRate = reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt16();
            bitsPerSample = reader.ReadInt16();
            stream.Position += chunkLength - 16;
        }
        else if (chunkId == "data")
        {
            data = reader.ReadBytes(chunkLength);
        }
        else
        {
            stream.Position += chunkLength;
        }

        if (chunkLength % 2 != 0)
        {
            stream.Position++;
        }
    }

    if (audioFormat != 1 || channels != 1 || sampleRate != 16_000 || bitsPerSample != 16 || data is null)
    {
        throw new InvalidOperationException("The speech fixture must be PCM16, mono, and sampled at 16 kHz.");
    }

    return data;
}

static string ReadFourCc(BinaryReader reader)
{
    return new string(reader.ReadChars(4));
}

sealed class ModelDownloadProgressReporter : IProgress<ModelDownloadProgress>
{
    private int _lastPercent = -1;

    public void Report(ModelDownloadProgress value)
    {
        if (value.Fraction is not double fraction)
        {
            return;
        }

        int percent = (int)Math.Floor(fraction * 100);
        if (percent == _lastPercent)
        {
            return;
        }

        _lastPercent = percent;
        Console.Write($"\rModel download: {percent}%");
    }
}
