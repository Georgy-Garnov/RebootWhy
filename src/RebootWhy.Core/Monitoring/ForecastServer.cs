using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using RebootWhy.Core.Updates;

namespace RebootWhy.Core.Monitoring;

/// <summary>
/// Service side: accepts program connections, sends the forecast to subscribers and broadcasts changes.
/// Only local users can connect; the service only reads and recalculates.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ForecastServer(ForecastEngine engine, RebootSimulation? simulation = null, string pipeName = ForecastProtocol.PipeName)
{
    private const int MaxClients = 16;

    private readonly Lock _lock = new();
    private readonly List<Subscriber> _subscribers = [];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        engine.Updated += Broadcast;
        try
        {
            await Task.WhenAll(engine.RunAsync(cancellationToken), AcceptAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            engine.Updated -= Broadcast;
        }
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                MaxClients,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                CreateSecurity());
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
            }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }

            _ = ServeAsync(pipe, cancellationToken);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using var _ = pipe;
        using var reader = new StreamReader(pipe);
        var subscriber = new Subscriber(new StreamWriter(pipe) { AutoFlush = true });
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var (command, id) = ForecastProtocol.ReadCommand(line);
                switch (command)
                {
                    case ForecastProtocol.Subscribe:
                        lock (_lock)
                        {
                            _subscribers.Add(subscriber);
                        }

                        if (engine.Latest is { } latest)
                        {
                            subscriber.Send(latest);
                        }

                        break;
                    case ForecastProtocol.Simulate:
                        simulation?.Start(engine);
                        break;
                    case ForecastProtocol.StopSimulation:
                        simulation?.Stop(engine);
                        break;
                    case ForecastProtocol.Analyze:
                        // Always reply to the requester: the user must see that the check happened.
                        subscriber.Send(await engine.RecalculateNowAsync().WaitAsync(cancellationToken), force: true, replyTo: id);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The program closed or the service is stopping.
        }
        finally
        {
            lock (_lock)
            {
                _subscribers.Remove(subscriber);
            }
        }
    }

    private void Broadcast(RebootForecast forecast)
    {
        Subscriber[] subscribers;
        lock (_lock)
        {
            subscribers = [.. _subscribers];
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Send(forecast, force: true);
        }
    }

    private static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();

        // Only accounts with this right can create new pipe instances (one per client), so grant it to our own account.
        using (var self = WindowsIdentity.GetCurrent())
        {
            security.AddAccessRule(new PipeAccessRule(self.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return security;
    }

    /// <summary>
    /// Remembers the last forecast sent: if a subscription coincides with a recalculation,
    /// the same forecast would otherwise be sent twice, as the current one and as a change.
    /// </summary>
    private sealed class Subscriber(StreamWriter writer)
    {
        private string? _lastSentKey;

        public void Send(RebootForecast forecast, bool force = false, string? replyTo = null)
        {
            var key = ForecastProtocol.ContentKey(forecast);
            try
            {
                lock (this)
                {
                    if (key == _lastSentKey && !force)
                    {
                        return;
                    }

                    writer.WriteLine(ForecastProtocol.Forecast(forecast, replyTo));
                    _lastSentKey = key;
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The subscriber disconnected; ServeAsync will remove it.
            }
        }
    }
}
