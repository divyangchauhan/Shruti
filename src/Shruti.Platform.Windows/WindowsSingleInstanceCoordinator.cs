using System.IO;
using System.IO.Pipes;

namespace Shruti.Platform.Windows;

public sealed class WindowsSingleInstanceCoordinator : IDisposable
{
    public const string DefaultMutexName = "Local\\Shruti.WinUI.SingleInstance";
    public const string DefaultPipeName = "Shruti.WinUI.SingleInstance";

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SignalRetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly string _mutexName;
    private readonly string _pipeName;

    private Mutex? _mutex;
    private CancellationTokenSource? _serverCancellation;
    private Task? _serverTask;
    private bool _isPrimaryInstance;
    private bool _isDisposed;

    public WindowsSingleInstanceCoordinator()
        : this(DefaultMutexName, DefaultPipeName)
    {
    }

    public WindowsSingleInstanceCoordinator(string mutexName, string pipeName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException("A mutex name is required.", nameof(mutexName));
        }

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("A pipe name is required.", nameof(pipeName));
        }

        _mutexName = mutexName;
        _pipeName = pipeName;
    }

    public event EventHandler? ActivationRequested;

    public async Task<bool> TryStartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_isPrimaryInstance)
        {
            return true;
        }

        var mutex = new Mutex(initiallyOwned: false, _mutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            await SignalPrimaryAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        _mutex = mutex;
        _isPrimaryInstance = true;
        _serverCancellation = new CancellationTokenSource();
        _serverTask = RunServerAsync(_serverCancellation.Token);
        return true;
    }

    public async Task<bool> SignalPrimaryAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + SignalTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                await client.ConnectAsync((int)SignalRetryDelay.TotalMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
                await client.WriteAsync(new byte[] { 1 }, cancellationToken).ConfigureAwait(false);
                await client.FlushAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                await Task.Delay(SignalRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                await Task.Delay(SignalRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(SignalRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _serverCancellation?.Cancel();
        _serverCancellation?.Dispose();
        _serverCancellation = null;
        _serverTask = null;

        _mutex?.Dispose();
        _mutex = null;
        _isPrimaryInstance = false;
        GC.SuppressFinalize(this);
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                if (server.IsConnected)
                {
                    ActivationRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // A secondary process may exit immediately after connecting.
            }
        }
    }
}
