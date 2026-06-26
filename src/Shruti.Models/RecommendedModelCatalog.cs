using Shruti.Transcription.Abstractions;

namespace Shruti.Models;

public static class RecommendedModelCatalog
{
    public static ModelCatalog Create()
    {
        return new ModelCatalog(
            SchemaVersion: 1,
            Revision: "whisper-cpp-ggml-main",
            Models:
            [
                CreateWhisperModel(
                    "whisper-tiny-en",
                    "Whisper tiny.en",
                    "ggml-tiny.en.bin",
                    77_704_715,
                    "c78c86eb1a8faa21b369bcd33207cc90d64ae9df"),
                CreateWhisperModel(
                    "whisper-base-en",
                    "Whisper base.en",
                    "ggml-base.en.bin",
                    147_964_211,
                    "137c40403d78fd54d454da0f9bd998f78703390c"),
                CreateWhisperModel(
                    "whisper-small-en",
                    "Whisper small.en",
                    "ggml-small.en.bin",
                    487_614_201,
                    "db8a495a91d927739e50b3fc1cc4c6b8f6c2d022")
            ]);
    }

    private static ModelCatalogEntry CreateWhisperModel(
        string id,
        string displayName,
        string fileName,
        long sizeBytes,
        string sha1)
    {
        return new ModelCatalogEntry(
            id,
            displayName,
            ProviderId: "whisper.cpp",
            LocalFileName: fileName,
            ModelFileFormat.Ggml,
            LanguageHint: "en",
            sizeBytes,
            new Uri($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{fileName}"),
            new ModelIntegrity(ModelHashAlgorithm.Sha1, sha1),
            [ComputeBackend.Cpu, ComputeBackend.Gpu],
            IsRecommended: true);
    }
}
