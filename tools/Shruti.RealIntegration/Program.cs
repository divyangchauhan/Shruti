using Shruti.Models;
using Shruti.Storage;
using Shruti.Transcription.Abstractions;
using Shruti.Transcription.WhisperCpp;
using Shruti.Transcription.OpenVino;

const string jfkAudioUrl = "https://raw.githubusercontent.com/ggml-org/whisper.cpp/5ed76e9a079962f1c85cfce44edd325c27ef1f97/samples/jfk.wav";
bool useNpu = args.Contains("--npu", StringComparer.OrdinalIgnoreCase);
bool useGpu = args.Contains("--gpu", StringComparer.OrdinalIgnoreCase);
if (useNpu && useGpu)
{
    throw new ArgumentException("Choose either --gpu or --npu.");
}

var paths = AppDataPaths.CreateDefault();
paths.EnsureCreated();

using var httpClient = new HttpClient();
var modelManager = new ModelManager(
    paths.ModelsDirectory,
    new HttpModelDownloadClient(httpClient),
    new ModelIntegrityVerifier());
int modelArgument = Array.IndexOf(args, "--model");
if (modelArgument >= 0 && modelArgument + 1 >= args.Length)
{
    throw new ArgumentException("--model requires a catalog model ID.");
}
ModelCatalogEntry modelEntry = RecommendedModelCatalog.Create().GetRequiredModel(
    modelArgument >= 0 ? args[modelArgument + 1] :
    useNpu ? "openvino-whisper-base-int8" : "whisper-tiny-en");
var progress = new ModelDownloadProgressReporter();

ModelInstallResult install = await modelManager.DownloadAsync(modelEntry, progress, CancellationToken.None);
Console.WriteLine();
if (!install.Succeeded || install.Model is null)
{
    throw new InvalidOperationException(install.Message ?? "The verified transcription model could not be installed.");
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
ITranscriptionProvider provider = useNpu
    ? new OpenVinoTranscriptionProvider()
    : new WhisperCppTranscriptionProvider(new WhisperCppTranscriptionEngine(new WhisperCppNativeApi()));
var options = new TranscriptionSessionOptions(
    install.Model.ToTranscriptionModelDescriptor(),
    useNpu ? ComputeBackend.Npu : useGpu ? ComputeBackend.Gpu : ComputeBackend.Cpu,
    "en",
    TranscriptionMode.Balanced);
IReadOnlyList<EngineCapability> capabilities = await provider.ProbeAsync(CancellationToken.None);
Console.WriteLine($"Available devices: {string.Join(", ", capabilities.Select(capability => capability.Backend))}");
Console.WriteLine($"Requested backend: {options.Backend}");
if (!capabilities.Any(capability => capability.Backend == options.Backend))
{
    throw new InvalidOperationException($"The runtime does not expose {options.Backend}.");
}

await using ITranscriptionSession session = await provider.CreateSessionAsync(options, CancellationToken.None);
var events = new List<TranscriptEvent>();
Task eventReader = ReadEventsAsync(session.Events, events);
bool silence = args.Contains("--silence", StringComparer.OrdinalIgnoreCase);
if (silence) pcmAudio = new byte[16_000 * sizeof(short) * 3];
await session.PushAudioAsync(pcmAudio, CancellationToken.None);
TranscriptResult result = await session.CompleteAsync(CancellationToken.None);
await eventReader;
if (events.Any(item => item.Kind == TranscriptEventKind.PartialText))
    throw new InvalidOperationException("Unexpected live transcript event.");
Console.WriteLine(silence ? $"Silence result: '{result.Text}'" : result.Text);
if (silence ? !string.IsNullOrWhiteSpace(result.Text) : !result.Text.Contains("ask not", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Unexpected final transcript.");

static async Task ReadEventsAsync(IAsyncEnumerable<TranscriptEvent> source, List<TranscriptEvent> events)
{
    await foreach (TranscriptEvent item in source) events.Add(item);
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
