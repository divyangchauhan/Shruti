using System.Runtime.InteropServices;

namespace Shruti.Transcription.WhisperCpp;

public sealed class WhisperCppNativeApi : IWhisperCppNativeApi
{
    private static readonly NativeAbortCallback AbortWhenCancellationRequested = IsCancellationRequested;

    public IWhisperCppNativeContext LoadModel(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The whisper.cpp model file was not found.", modelPath);
        }

        try
        {
            IntPtr handle = NativeMethods.Create(modelPath);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("whisper.cpp could not load the model file.");
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

        [DllImport(LibraryName, EntryPoint = "shruti_whisper_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Create([MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath);

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
    }
}
