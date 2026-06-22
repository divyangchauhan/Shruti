using Shruti.Audio.Windows;
using Shruti.Core.Audio;
using Shruti.Transcription.Abstractions;
using Xunit;

namespace Shruti.Tests;

public sealed class WindowsAudioCaptureServiceTests
{
    [Fact]
    public async Task ListInputDevicesAsync_MapsWindowsCaptureDevices()
    {
        var catalog = new FakeDeviceCatalog(
        [
            new WindowsAudioCaptureDevice("default", "Primary microphone", IsDefault: true),
            new WindowsAudioCaptureDevice("usb", "USB microphone", IsDefault: false)
        ]);
        using var service = new WindowsAudioCaptureService(catalog, new FakeCaptureFactory());

        IReadOnlyList<AudioInputDevice> devices = await service.ListInputDevicesAsync(CancellationToken.None);

        Assert.Collection(
            devices,
            first =>
            {
                Assert.Equal("default", first.Id);
                Assert.True(first.IsDefault);
            },
            second => Assert.Equal("USB microphone", second.DisplayName));
    }

    [Fact]
    public async Task StartAsync_UsesSelectedDeviceAndEmitsNormalizedPcmAndLevel()
    {
        var catalog = new FakeDeviceCatalog(
        [
            new WindowsAudioCaptureDevice("default", "Primary microphone", IsDefault: true),
            new WindowsAudioCaptureDevice("usb", "USB microphone", IsDefault: false)
        ]);
        var source = new FakeCaptureSource(new WindowsAudioStreamFormat(
            SampleRateHz: 16_000,
            ChannelCount: 1,
            SampleFormat: AudioSampleFormat.Int16,
            BitsPerSample: 16));
        var factory = new FakeCaptureFactory(source);
        using var service = new WindowsAudioCaptureService(catalog, factory);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await using IAudioCaptureSession session = await service.StartAsync(
            new AudioCaptureOptions(DeviceId: "usb", EnableLevelMeter: true),
            AudioFormat.Speech16KhzMono,
            CancellationToken.None);
        Task<AudioFrame> frameTask = ReadFirstAsync(session.Frames, cancellation.Token);
        Task<AudioLevelFrame> levelTask = ReadFirstAsync(session.Levels, cancellation.Token);

        source.EmitPcm16(16_384, -16_384);

        AudioFrame frame = await frameTask;
        AudioLevelFrame level = await levelTask;

        Assert.Equal("usb", factory.LastDevice?.Id);
        Assert.True(source.Started);
        Assert.Equal([0, 64, 0, 192], frame.PcmAudio.ToArray());
        Assert.InRange(level.Peak, 0.49f, 0.51f);
        Assert.InRange(level.Rms, 0.49f, 0.51f);
    }

    [Fact]
    public async Task StartAsync_DownsamplesDeviceAudioToRequestedSpeechFormat()
    {
        var source = new FakeCaptureSource(new WindowsAudioStreamFormat(
            SampleRateHz: 48_000,
            ChannelCount: 1,
            SampleFormat: AudioSampleFormat.Int16,
            BitsPerSample: 16));
        using var service = new WindowsAudioCaptureService(
            new FakeDeviceCatalog([new WindowsAudioCaptureDevice("default", "Primary microphone", true)]),
            new FakeCaptureFactory(source));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await using IAudioCaptureSession session = await service.StartAsync(
            new AudioCaptureOptions(),
            AudioFormat.Speech16KhzMono,
            CancellationToken.None);
        Task<AudioFrame> frameTask = ReadFirstAsync(session.Frames, cancellation.Token);

        source.EmitPcm16(1_000, 2_000, 3_000, 4_000, 5_000, 6_000);

        AudioFrame frame = await frameTask;

        Assert.Equal(2 * sizeof(short), frame.PcmAudio.Length);
        Assert.NotEqual(new byte[frame.PcmAudio.Length], frame.PcmAudio.ToArray());
    }

    [Fact]
    public async Task ActiveCaptureControl_PausesResumesAndStopsSession()
    {
        var source = new FakeCaptureSource(new WindowsAudioStreamFormat(
            SampleRateHz: 16_000,
            ChannelCount: 1,
            SampleFormat: AudioSampleFormat.Int16,
            BitsPerSample: 16));
        using var service = new WindowsAudioCaptureService(
            new FakeDeviceCatalog([new WindowsAudioCaptureDevice("default", "Primary microphone", true)]),
            new FakeCaptureFactory(source));

        await using IAudioCaptureSession session = await service.StartAsync(
            new AudioCaptureOptions(),
            AudioFormat.Speech16KhzMono,
            CancellationToken.None);

        await service.PauseActiveCaptureAsync();
        await service.ResumeActiveCaptureAsync();
        await service.StopActiveCaptureAsync();

        Assert.Equal(1, source.StopCount);
    }

    [Fact]
    public async Task StartAsync_FailsWhenThereIsNoAvailableMicrophone()
    {
        using var service = new WindowsAudioCaptureService(
            new FakeDeviceCatalog([]),
            new FakeCaptureFactory());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(
            new AudioCaptureOptions(),
            AudioFormat.Speech16KhzMono,
            CancellationToken.None));

        Assert.Contains("No active Windows microphone", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<T> ReadFirstAsync<T>(
        IAsyncEnumerable<T> values,
        CancellationToken cancellationToken)
    {
        await foreach (T value in values.WithCancellation(cancellationToken))
        {
            return value;
        }

        throw new InvalidOperationException("The audio stream completed before producing a value.");
    }

    private sealed class FakeDeviceCatalog : IWindowsAudioDeviceCatalog
    {
        private readonly IReadOnlyList<WindowsAudioCaptureDevice> _devices;

        public FakeDeviceCatalog(IReadOnlyList<WindowsAudioCaptureDevice> devices)
        {
            _devices = devices;
        }

        public IReadOnlyList<WindowsAudioCaptureDevice> ListCaptureDevices()
        {
            return _devices;
        }
    }

    private sealed class FakeCaptureFactory : IWindowsAudioCaptureFactory
    {
        private readonly FakeCaptureSource? _source;

        public FakeCaptureFactory(FakeCaptureSource? source = null)
        {
            _source = source;
        }

        public WindowsAudioCaptureDevice? LastDevice { get; private set; }

        public IWindowsAudioCaptureSource Create(WindowsAudioCaptureDevice device)
        {
            LastDevice = device;
            return _source ?? throw new InvalidOperationException("No fake capture source was configured.");
        }
    }

    private sealed class FakeCaptureSource : IWindowsAudioCaptureSource
    {
        public FakeCaptureSource(WindowsAudioStreamFormat streamFormat)
        {
            StreamFormat = streamFormat;
        }

        public WindowsAudioStreamFormat StreamFormat { get; }

        public bool Started { get; private set; }

        public int StopCount { get; private set; }

        public event EventHandler<WindowsAudioDataAvailableEventArgs>? DataAvailable;

        public event EventHandler<WindowsAudioCaptureStoppedEventArgs>? CaptureStopped;

        public void StartCapture()
        {
            Started = true;
        }

        public void StopCapture()
        {
            StopCount++;
            CaptureStopped?.Invoke(this, new WindowsAudioCaptureStoppedEventArgs(exception: null));
        }

        public void EmitPcm16(params short[] samples)
        {
            var buffer = new byte[samples.Length * sizeof(short)];
            Buffer.BlockCopy(samples, 0, buffer, 0, buffer.Length);
            DataAvailable?.Invoke(this, new WindowsAudioDataAvailableEventArgs(buffer, buffer.Length));
        }

        public void Dispose()
        {
        }
    }
}
