using System.Runtime.InteropServices;
using Shruti.Transcription.Abstractions;

namespace Shruti.Transcription.WhisperCpp;

public sealed class WhisperCppNativeApi : IWhisperCppNativeApi
{
    private static readonly NativeAbortCallback AbortWhenCancellationRequested = IsCancellationRequested;
    private const int BackendFlagCpu = 1 << 0;
    private const int BackendFlagGpu = 1 << 1;

    public WhisperCppBackendCapabilities GetCapabilities()
    {
        try
        {
            int flags = NativeMethods.GetAvailableBackends();
            string systemInfo = Marshal.PtrToStringUTF8(NativeMethods.SystemInfo()) ?? string.Empty;
            return new WhisperCppBackendCapabilities(
                SupportsCpu: (flags & BackendFlagCpu) != 0,
                SupportsGpu: (flags & BackendFlagGpu) != 0,
                SupportsNpu: false,
                systemInfo);
        }
        catch (DllNotFoundException)
        {
            return new WhisperCppBackendCapabilities(
                SupportsCpu: false,
                SupportsGpu: false,
                SupportsNpu: false,
                SystemInfo: "shruti_whisper native library is unavailable.");
        }
        catch (EntryPointNotFoundException)
        {
            return new WhisperCppBackendCapabilities(
                SupportsCpu: false,
                SupportsGpu: false,
                SupportsNpu: false,
                SystemInfo: "shruti_whisper native library does not expose backend capability metadata.");
        }
    }

    public IWhisperCppNativeContext LoadModel(string modelPath, ComputeBackend backend, int gpuDevice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The whisper.cpp model file was not found.", modelPath);
        }

        try
        {
            IntPtr handle = NativeMethods.CreateWithBackend(modelPath, ToNativeBackend(backend), gpuDevice);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"whisper.cpp could not load the model file on {backend}.");
            }

            return new WhisperCppNativeContext(handle);
        }
        catch (DllNotFoundException exception)
        {
            throw new InvalidOperationException(
                "The shruti_whisper native library is unavailable. Build the whisper.cpp native shim before starting transcription.",
                exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new InvalidOperationException("The shruti_whisper native library does not match the managed adapter.", exception);
        }
    }

    private static int ToNativeBackend(ComputeBackend backend)
    {
        return backend switch
        {
            ComputeBackend.Cpu => 0,
            ComputeBackend.Gpu => 1,
            _ => throw new NotSupportedException($"whisper.cpp does not support the {backend} backend.")
        };
    }

    private sealed class WhisperCppNativeContext : IWhisperCppNativeContext
    {
        private IntPtr _handle;

        public WhisperCppNativeContext(IntPtr handle)
        {
            _handle = handle;
        }

        public int Transcribe(float[] samples, string language, int threadCount, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(samples);
            ArgumentException.ThrowIfNullOrWhiteSpace(language);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threadCount);
            cancellationToken.ThrowIfCancellationRequested();

            GCHandle cancellationState = GCHandle.Alloc(new NativeCancellationState(cancellationToken));
            try
            {
                return NativeMethods.Transcribe(
                    _handle,
                    samples,
                    samples.Length,
                    language,
                    threadCount,
                    AbortWhenCancellationRequested,
                    GCHandle.ToIntPtr(cancellationState));
            }
            finally
            {
                cancellationState.Free();
            }
        }

        public int GetSegmentCount()
        {
            ThrowIfDisposed();
            return NativeMethods.GetSegmentCount(_handle);
        }

        public WhisperCppSegment GetSegment(int index)
        {
            ThrowIfDisposed();

            int status = NativeMethods.GetSegment(
                _handle,
                index,
                out long startMilliseconds,
                out long endMilliseconds,
                out IntPtr text);
            if (status != 0)
            {
                throw new InvalidOperationException($"whisper.cpp could not retrieve segment {index}.");
            }

            return new WhisperCppSegment(
                TimeSpan.FromMilliseconds(startMilliseconds),
                TimeSpan.FromMilliseconds(endMilliseconds),
                Marshal.PtrToStringUTF8(text) ?? string.Empty);
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.Free(_handle);
            _handle = IntPtr.Zero;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeAbortCallback(IntPtr userData);

    private sealed class NativeCancellationState
    {
        public NativeCancellationState(CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
        }

        public CancellationToken CancellationToken { get; }
    }

    private static int IsCancellationRequested(IntPtr userData)
    {
        var cancellationState = (NativeCancellationState?)GCHandle.FromIntPtr(userData).Target;
        return cancellationState?.CancellationToken.IsCancellationRequested == true ? 1 : 0;
    }

    private static class NativeMethods
    {
        private const string LibraryName = "shruti_whisper";

        [DllImport(LibraryName, EntryPoint = "shruti_whisper_available_backends", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetAvailableBackends();

        [DllImport(LibraryName, EntryPoint = "shruti_whisper_create_with_backend", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr CreateWithBackend(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath,
            int backend,
            int gpuDevice);

        [DllImport(LibraryName, EntryPoint = "shruti_whisper_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Free(IntPtr context);

        [DllImport(LibraryName, EntryPoint = "shruti_whisper_transcribe", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Transcribe(
            IntPtr context,
            [In] float[] samples,
            int sampleCount,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string language,
            int threadCount,
            NativeAbortCallback abortCallback,
            IntPtr abortCallbackUserData);

        [DllImport(LibraryName, EntryPoint = "shruti_whisper_get_segment_count", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetSegmentCount(IntPtr context);

        [DllImport(LibraryName, EntryPoint = "shruti_whisper_get_segment", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetSegment(
            IntPtr context,
            int index,
            out long startMilliseconds,
            out long endMilliseconds,
            out IntPtr text);

        [DllImport(LibraryName, EntryPoint = "shruti_whisper_system_info", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SystemInfo();
    }
}
