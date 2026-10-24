using System.IO.Pipes;
using RebootWhy.Core.Updates;

namespace RebootWhy.Core.Monitoring;

/// <summary>Program side: keeps a subscription to the service and reconnects if the service restarts.</summary>
public sealed class ForecastClient(string pipeName = ForecastProtocol.PipeName)
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private StreamWriter? _writer;
    private (string Id, TaskCompletionSource<RebootForecast> Reply)? _pending;

    public event Action<RebootForecast>? ForecastReceived;

    /// <summary>true: connected to the service, false: the service is unavailable. Raised on every change and after the first attempt.</summary>
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _writer is not null;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        bool? connected = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(ConnectTimeout, cancellationToken);
                using var reader = new StreamReader(pipe);
                _writer = new StreamWriter(pipe) { AutoFlush = true };

                // Subscribe first, then raise the event: the handler must not delay the subscription.
                await SendAsync(ForecastProtocol.Subscribe);
                SetConnected(ref connected, true);

                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    var (forecast, replyTo) = ForecastProtocol.ReadForecast(line);
                    if (forecast is null)
                    {
                        continue;
                    }

                    ForecastReceived?.Invoke(forecast);

                    // Only a message with our request id is the reply: a broadcast from a previous check may arrive later.
                    if (replyTo is not null && _pending is { } pending && pending.Id == replyTo)
                    {
                        pending.Reply.TrySetResult(forecast);
                    }
                }
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                // The service is not installed, stopped or restarting.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                _writer = null;
            }

            SetConnected(ref connected, false);
            try
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Ask the service to check now and wait for the result; null if there is no service or it did not reply in time.</summary>
    public async Task<RebootForecast?> AnalyzeAsync(TimeSpan timeout)
    {
        var id = Guid.NewGuid().ToString("N");
        var reply = new TaskCompletionSource<RebootForecast>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = (id, reply);
        if (!await SendAsync(ForecastProtocol.Analyze, id))
        {
            return null;
        }

        try
        {
            return await reply.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    /// <summary>Ask the service to start the reaction test; false if there is no service.</summary>
    public Task<bool> StartSimulationAsync() => SendAsync(ForecastProtocol.Simulate);

    public Task<bool> StopSimulationAsync() => SendAsync(ForecastProtocol.StopSimulation);

    private async Task<bool> SendAsync(string command, string? id = null)
    {
        await _writeGate.WaitAsync();
        try
        {
            if (_writer is not { } writer)
            {
                return false;
            }

            await writer.WriteLineAsync(ForecastProtocol.Command(command, id));
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void SetConnected(ref bool? current, bool value)
    {
        if (current != value)
        {
            current = value;
            ConnectionChanged?.Invoke(value);
        }
    }
}
