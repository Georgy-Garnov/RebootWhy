using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Text;
using RebootWhy.Core.Analysis;
using RebootWhy.Core.Sources;
using RebootWhy.Core.Text;
using RebootWhy.Core.Updates;

// Console version for debugging the analyzer and examining .evtx files from other computers.
// The tool itself speaks English; analysis texts follow --lang (en by default).
Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");

var days = 30;
string? file = null;
var showEvidence = false;
var updates = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--days" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed) && parsed > 0:
            days = parsed;
            i++;
            break;
        case "--file" when i + 1 < args.Length:
            file = args[++i];
            break;
        case "--evidence":
            showEvidence = true;
            break;
        case "--updates":
            updates = true;
            break;
        case "--lang" when i + 1 < args.Length:
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(args[++i]);
            break;
        default:
            Console.Error.WriteLine("Usage: rebootwhy-cli [--days N] [--file path.evtx] [--evidence] [--lang en|ru|uk|hy] | --updates");
            return 1;
    }
}

if (updates)
{
    var forecast = new UpdateRebootMonitor().AnalyzeNow();
    Console.WriteLine($"[{forecast.State}] {ForecastText.Summary(forecast)}");
    foreach (var update in forecast.PendingUpdates)
    {
        Console.WriteLine($"    Waiting for a restart: {ForecastText.UpdateTitle(update)} ({ForecastText.UpdateType(update)}){(update.PendingSince is { } since ? $", since {Display.ShortDateTime(since)}" : string.Empty)}");
    }

    var scheduleSource = forecast.ScheduleFromTaskScheduler ? "Task Scheduler" : "USO log";
    foreach (var run in forecast.Schedule)
    {
        Console.WriteLine($"    Task \"{run.Task}\": {Display.LocalDateTime(new DateTimeOffset(run.NextRun))} (source: {scheduleSource})");
    }

    foreach (var line in ForecastText.Notes(forecast).Concat(ForecastText.Problems(forecast)))
    {
        Console.WriteLine($"    • {line}");
    }

    return 0;
}

var source = file is null ? WindowsEventLogSource.ForLiveSystemLog() : WindowsEventLogSource.ForFile(file);
RebootReport report;
try
{
    report = RebootHistory.Load(source, file is null ? DateTimeOffset.UtcNow.AddDays(-days) : null);
}
catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or IOException)
{
    Console.Error.WriteLine($"Could not read the log: {ex.Message}");
    return 2;
}

if (file is null)
{
    var uptime = UptimeCalculator.Calculate(
        report.Records,
        DateTimeOffset.UtcNow,
        TimeSpan.FromMilliseconds(Environment.TickCount64),
        WindowsSystemInfo.GetActiveTimeSinceKernelStart());

    Console.WriteLine($"Uptime: {Display.Duration(uptime.SinceLastStart)} (since {Display.LocalDateTime(uptime.LastStartUtc)}, {Display.BootType(uptime.LastStartType).ToLowerInvariant()})");
    Console.WriteLine($"Last full boot: {Display.LocalDateTime(uptime.LastFullBootUtc)} ({Display.Duration(uptime.SinceLastFullBoot)} ago)");
    if (uptime.ActiveSinceFullBoot is { } active)
    {
        Console.WriteLine($"Active time excluding sleep: {Display.Duration(active)}");
    }

    if (WindowsSystemInfo.IsFastStartupEnabled() == true)
    {
        Console.WriteLine("Fast Startup is enabled: \"Shut down\" does not restart the Windows kernel.");
    }

    Console.WriteLine();
}

Console.WriteLine($"Boots: {report.Records.Count}, events analyzed: {report.EventCount}");
foreach (var record in report.Records)
{
    Console.WriteLine();
    Console.WriteLine($"{Display.LocalDateTime(record.BootTimeUtc)}  {record.Title}");
    foreach (var fact in RecordFacts.Build(record).Skip(1))
    {
        Console.WriteLine($"    {fact.Label}: {fact.Value}");
    }

    Console.WriteLine($"    Boot: {Display.BootType(record.BootType)}");
    foreach (var update in record.Updates)
    {
        Console.WriteLine($"    Update: {update}");
    }

    foreach (var note in record.Notes)
    {
        Console.WriteLine($"    • {note}");
    }

    if (showEvidence)
    {
        foreach (var e in record.Evidence)
        {
            Console.WriteLine($"      {Display.LocalDateTime(e.TimeUtc, withSeconds: true)}  {EventDescriber.Source(e),-28} {EventDescriber.Describe(e)}");
        }
    }
}

return 0;
