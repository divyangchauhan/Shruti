using System.Runtime.InteropServices;

namespace Shruti.Transcription.OpenVino;

internal static class OpenVinoNativeApi
{
    private const string OpenVinoLibrary = "openvino_c.dll";
    private const string GenAiLibrary = "openvino_genai_c.dll";

    [StructLayout(LayoutKind.Sequential)]
    private struct AvailableDevices
    {
        public IntPtr Devices;
        public nuint Size;
    }

    public static IReadOnlyList<string> GetAvailableDevices()
    {
        CheckStatus(ov_core_create(out IntPtr core), "create the OpenVINO runtime");
        try
        {
            CheckStatus(ov_core_get_available_devices(core, out AvailableDevices devices), "probe OpenVINO devices");
            try
            {
                var result = new List<string>(checked((int)devices.Size));
                for (nuint index = 0; index < devices.Size; index++)
                {
                    IntPtr devicePointer = Marshal.ReadIntPtr(devices.Devices, checked((int)(index * (nuint)IntPtr.Size)));
                    string? device = Marshal.PtrToStringUTF8(devicePointer);
                    if (!string.IsNullOrWhiteSpace(device))
                    {
                        result.Add(device);
                    }
                }

                return result;
            }
            finally
            {
                ov_available_devices_free(ref devices);
            }
        }
        finally
        {
            ov_core_free(core);
        }
    }

    public static IntPtr CreateWhisperPipeline(string modelPath, string device)
    {
        int status = string.Equals(device, "NPU", StringComparison.Ordinal)
            ? ov_genai_whisper_pipeline_create_with_property(
                modelPath,
                device,
                2,
                out IntPtr pipeline,
                "STATIC_PIPELINE",
                "YES")
            : ov_genai_whisper_pipeline_create(modelPath, device, 0, out pipeline);
        CheckStatus(status, $"load the Whisper model on {device}");
        return pipeline;
    }

    public static string Transcribe(IntPtr pipeline, float[] audio)
    {
        CheckStatus(
            ov_genai_whisper_pipeline_generate(pipeline, audio, (nuint)audio.Length, IntPtr.Zero, out IntPtr results),
            "transcribe audio");
        try
        {
            CheckStatus(ov_genai_whisper_decoded_results_get_texts_count(results, out nuint count), "read transcription result");
            if (count == 0)
            {
                return string.Empty;
            }

            nuint size = 0;
            CheckStatus(
                ov_genai_whisper_decoded_results_get_text_at(results, 0, IntPtr.Zero, ref size),
                "measure transcription text");
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                CheckStatus(
                    ov_genai_whisper_decoded_results_get_text_at(results, 0, buffer, ref size),
                    "read transcription text");
                return Marshal.PtrToStringUTF8(buffer) ?? string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ov_genai_whisper_decoded_results_free(results);
        }
    }

    public static void FreeWhisperPipeline(IntPtr pipeline)
    {
        if (pipeline != IntPtr.Zero)
        {
            ov_genai_whisper_pipeline_free(pipeline);
        }
    }

    private static void CheckStatus(int status, string operation)
    {
        if (status == 0)
        {
            return;
        }

        string? details = Marshal.PtrToStringUTF8(ov_get_last_err_msg());
        string? statusText = Marshal.PtrToStringUTF8(ov_get_error_info(status));
        throw new InvalidOperationException(
            $"OpenVINO could not {operation}: {details ?? statusText ?? $"status {status}"}");
    }

    [DllImport(OpenVinoLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ov_core_create(out IntPtr core);

    [DllImport(OpenVinoLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ov_core_free(IntPtr core);

    [DllImport(OpenVinoLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ov_core_get_available_devices(IntPtr core, out AvailableDevices devices);

    [DllImport(OpenVinoLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ov_available_devices_free(ref AvailableDevices devices);

    [DllImport(OpenVinoLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ov_get_error_info(int status);

    [DllImport(OpenVinoLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ov_get_last_err_msg();

    [DllImport(GenAiLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int ov_genai_whisper_pipeline_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelsPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string device,
        nuint propertyArgsSize,
        out IntPtr pipeline);

    [DllImport(GenAiLibrary, EntryPoint = "ov_genai_whisper_pipeline_create", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int ov_genai_whisper_pipeline_create_with_property(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelsPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string device,
        nuint propertyArgsSize,
        out IntPtr pipeline,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string propertyName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string propertyValue);

    [DllImport(GenAiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ov_genai_whisper_pipeline_free(IntPtr pipeline);

    [DllImport(GenAiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ov_genai_whisper_pipeline_generate(
        IntPtr pipeline,
        [In] float[] rawSpeech,
        nuint rawSpeechSize,
        IntPtr config,
        out IntPtr results);

    [DllImport(GenAiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ov_genai_whisper_decoded_results_free(IntPtr results);

    [DllImport(GenAiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ov_genai_whisper_decoded_results_get_texts_count(IntPtr results, out nuint count);

    [DllImport(GenAiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ov_genai_whisper_decoded_results_get_text_at(
        IntPtr results,
        nuint index,
        IntPtr text,
        ref nuint textSize);
}
