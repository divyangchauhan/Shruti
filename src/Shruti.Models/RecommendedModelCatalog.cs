using Shruti.Transcription.Abstractions;

namespace Shruti.Models;

public static class RecommendedModelCatalog
{
    public static ModelCatalog Create()
    {
        return new ModelCatalog(
            SchemaVersion: 1,
            Revision: "whisper-cpp-and-openvino-2026.3",
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
                    "db8a495a91d927739e50b3fc1cc4c6b8f6c2d022"),
                CreateOpenVinoWhisperBaseModel()
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

    private static ModelCatalogEntry CreateOpenVinoWhisperBaseModel()
    {
        const string repository = "https://huggingface.co/OpenVINO/whisper-base-int8-ov/resolve/main";
        return new ModelCatalogEntry(
            "openvino-whisper-base-int8",
            "Whisper base INT8",
            ProviderId: "openvino-genai",
            LocalFileName: "whisper-base-int8-ov",
            ModelFileFormat.OpenVinoIr,
            LanguageHint: "en",
            SizeBytes: 84_707_540,
            DownloadUri: null,
            Integrity: null,
            [ComputeBackend.Npu, ComputeBackend.Gpu, ComputeBackend.Cpu],
            IsRecommended: true,
            Artifacts:
            [
                Artifact(repository, "added_tokens.json", 34_604, "9715fd2243b6f06a5858b5e32950d2853f73dd5bc201aafcf76f5082a2d8acd1"),
                Artifact(repository, "config.json", 1_320, "7580357cd33e60d3c453b90b6ab4d78606cbc5de32cc2da8df873468dcfac237"),
                Artifact(repository, "generation_config.json", 3_802, "a24ccb25a8638fb23d3acfa0b234982497dd30bb3c577d145619525629f38e97"),
                Artifact(repository, "merges.txt", 493_869, "2df2990a395e35e8dfbc7511e08c12d56018d8d04691e0133e5d63b21e154dc6"),
                Artifact(repository, "normalizer.json", 52_666, "bf1c507dc8724ca9cf9903640dacfb69dae2f00edee4f21ceba106a7392f26dd"),
                Artifact(repository, "openvino_config.json", 443, "62a8f3b61e52493b7b60ca1b2f42bd1aa34e2c95df4230945507d45537cafd5e"),
                Artifact(repository, "openvino_decoder_model.bin", 52_439_987, "1e373d629c4a1a5c7a1964188f77c2b52b0bb488f27015fd9dc35bcb1e7eef42"),
                Artifact(repository, "openvino_decoder_model.xml", 564_428, "901c8584bd9c7721b879d617d2941fb9c318150cfec5d04056bc8faaa9745285"),
                Artifact(repository, "openvino_detokenizer.bin", 736_181, "2542d1fe6c4c5d838e7b7c61b24996b94ae0cb54e64ff6dbaab2b9dd5ddd7ca0"),
                Artifact(repository, "openvino_detokenizer.xml", 9_699, "2ef46e0d325a858753784b4f96172bd0e627cd9ff1fd0c9db5ac8bc1785b8c7f"),
                Artifact(repository, "openvino_encoder_model.bin", 23_097_456, "a0aa4518850411dfadc7799b426d7c08e966a85367be96588432fbadf40789d8"),
                Artifact(repository, "openvino_encoder_model.xml", 295_834, "79dc09241718475ca14277bb16766cfb688b279f412cae8972b1c1857863ae3a"),
                Artifact(repository, "openvino_tokenizer.bin", 1_898_933, "846f3c65f7a71f120fce7aaaf41b342f0767eb13e3c4e7c2a54f1d49b5c38fda"),
                Artifact(repository, "openvino_tokenizer.xml", 27_011, "3e4ddd6e2031c307d5db367630298ae77d6f9f5675b689cb536e0da617a0e38d"),
                Artifact(repository, "preprocessor_config.json", 356, "994838f1fa6462c8b9b3c90edada831f11f3dd8b4664634e18f4694d005c9dbf"),
                Artifact(repository, "special_tokens_map.json", 2_194, "e67ae3a0aaa99abcd9f187138e12db1f65c16a14761c50ef10eef2c174a7a691"),
                Artifact(repository, "tokenizer.json", 3_930_494, "7b469ff15eb7816315aa45eec391f5943d639b9d73d110f5c003df5192fd54e3"),
                Artifact(repository, "tokenizer_config.json", 282_713, "21a4fc0483c14b87f4e0bbc177a9a357479bfa7c95aaf21ad53d71e9c5afafb9"),
                Artifact(repository, "vocab.json", 835_550, "8f680bba319e01a653d2e8a5dbc17a9157179e0576e6ce74ce0c06356c6e24f9")
            ]);
    }

    private static ModelArtifact Artifact(string repository, string relativePath, long sizeBytes, string sha256)
    {
        return new ModelArtifact(
            relativePath,
            new Uri($"{repository}/{relativePath}?download=true"),
            sizeBytes,
            new ModelIntegrity(ModelHashAlgorithm.Sha256, sha256));
    }
}
