using System.Text.Json;
using System.Text.Json.Serialization;
using RebootWhy.Core.Updates;

namespace RebootWhy.Core.Monitoring;

/// <summary>
/// Exchange between the service and the program over a named pipe: one JSON line per message.
/// The program sends commands; the service sends the forecast right after subscribing and on every change.
/// </summary>
public static class ForecastProtocol
{
    public const string PipeName = "RebootWhy.Forecast";

    public const string Subscribe = "subscribe";

    public const string Analyze = "analyze";

    public const string Simulate = "simulate";

    public const string StopSimulation = "stop-simulation";

    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <param name="id">Id of a "check now" request; the service returns it in the reply.</param>
    public static string Command(string command, string? id = null) => JsonSerializer.Serialize(new CommandMessage(command, id), Options);

    public static (string? Command, string? Id) ReadCommand(string line)
    {
        try
        {
            var message = JsonSerializer.Deserialize<CommandMessage>(line, Options);
            return (message?.Command, message?.Id);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <param name="replyTo">Id of the request this message answers; null for a regular broadcast.</param>
    public static string Forecast(RebootForecast forecast, string? replyTo = null) =>
        JsonSerializer.Serialize(new ForecastMessage(forecast, replyTo), Options);

    public static (RebootForecast? Forecast, string? ReplyTo) ReadForecast(string line)
    {
        try
        {
            var message = JsonSerializer.Deserialize<ForecastMessage>(line, Options);
            return (message?.Forecast, message?.ReplyTo);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>Key for comparing forecasts regardless of the analysis time.</summary>
    public static string ContentKey(RebootForecast forecast) =>
        JsonSerializer.Serialize(forecast with { AnalyzedAt = default }, Options);

    private sealed record CommandMessage(string Command, string? Id);

    private sealed record ForecastMessage(RebootForecast Forecast, string? ReplyTo);
}
