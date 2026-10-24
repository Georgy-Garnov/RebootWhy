using RebootWhy.Core.Monitoring;
using RebootWhy.Core.Properties;
using RebootWhy.Core.Updates;

namespace RebootWhy.Core.Text;

/// <summary>Texts of a Windows Update forecast in the program's language (the forecast itself is language-neutral data).</summary>
public static class ForecastText
{
    public static string Summary(RebootForecast forecast)
    {
        if (forecast.IsSample)
        {
            return Strings.Forecast_Sample;
        }

        if (forecast.AnalysisFailed)
        {
            return Strings.Forecast_Failed;
        }

        if (forecast.State == ForecastState.None || forecast.EarliestReboot is not { } earliest)
        {
            return Strings.Forecast_None;
        }

        var pending = forecast.PendingUpdates;
        var what = pending.Count switch
        {
            0 => Strings.Forecast_What_None,
            1 => Display.Format(Strings.Forecast_What_One, UpdateTitle(pending[0])),
            _ => Display.Format(Strings.Forecast_What_Many, UpdateTitle(pending[0]), pending.Count - 1),
        };

        var summary = forecast.State == ForecastState.Imminent
            ? Display.Format(Strings.Forecast_Imminent, Display.ShortDateTime(earliest), what)
            : Display.Format(Strings.Forecast_Planned, what, Display.ShortDateTime(earliest));
        return forecast.IsSimulation ? Strings.Forecast_SimulationPrefix + summary : summary;
    }

    public static string UpdateTitle(PendingUpdate update) =>
        update.Id == RebootSimulation.UpdateId ? Strings.Simulation_UpdateTitle
        : update.Title ?? Display.Format(Strings.UpdateWithId, update.Id);

    public static string UpdateType(PendingUpdate update) => UpdateText.UpdateType(update.Type);

    public static IReadOnlyList<string> Notes(RebootForecast forecast) =>
        forecast.Notes.Select(n => Note(forecast, n)).ToList();

    public static string Note(RebootForecast forecast, ForecastNote note) => note.Kind switch
    {
        ForecastNoteKind.RebootRequired => Strings.ForecastNote_RebootRequired,
        ForecastNoteKind.ServicingPending => Strings.ForecastNote_ServicingPending,
        ForecastNoteKind.ApprovedEarlier => Display.Format(Strings.ForecastNote_ApprovedEarlier, Subject(forecast, note), Time(note)),
        ForecastNoteKind.Overdue => Display.Format(Strings.ForecastNote_Overdue, Subject(forecast, note)),
        ForecastNoteKind.ActiveHours => Display.Format(Strings.ForecastNote_ActiveHours, note.From, note.To),
        ForecastNoteKind.DeferredWhileActive => Display.Format(Strings.ForecastNote_DeferredWhileActive, Time(note)),
        ForecastNoteKind.NoAutoRebootPolicy => Strings.ForecastNote_NoAutoRebootPolicy,
        ForecastNoteKind.Simulation => Display.Format(Strings.ForecastNote_Simulation, note.Time is { } end ? Display.ShortTime(end) : string.Empty),
        ForecastNoteKind.SampleReason => Strings.ForecastNote_SampleReason,
        ForecastNoteKind.SampleNotice => Strings.ForecastNote_SampleNotice,
        _ => note.Kind.ToString(),
    };

    /// <summary>The log facts the forecast is based on.</summary>
    public static IReadOnlyList<string> Evidence(RebootForecast forecast)
    {
        if (forecast.State == ForecastState.None)
        {
            return [];
        }

        var lines = forecast.PendingUpdates
            .Select(u => u.PendingSince is { } since
                ? Display.Format(Strings.ForecastEvidence_Waiting, Display.ShortDateTime(since), UpdateTitle(u))
                : Display.Format(Strings.ForecastEvidence_WaitingNoTime, UpdateTitle(u)))
            .ToList();
        lines.AddRange(forecast.Schedule.Select(s => Display.Format(Strings.ForecastEvidence_Task, s.Task, Display.ShortDateTime(s.NextRun))));
        return lines;
    }

    public static IReadOnlyList<string> Problems(RebootForecast forecast) => forecast.Problems.Select(Problem).ToList();

    public static string Problem(ForecastProblem problem) => problem.Kind switch
    {
        ForecastProblemKind.NoUsoFolder => Display.Format(Strings.Problem_NoUsoFolder, problem.Subject),
        ForecastProblemKind.CannotRead => Display.Format(Strings.Problem_CannotRead, problem.Subject, problem.Detail),
        ForecastProblemKind.WindowsUpdateSilent => Display.Format(Strings.Problem_WindowsUpdateSilent, problem.Detail),
        _ => problem.Detail ?? problem.Kind.ToString(),
    };

    /// <summary>Notes refer to updates by id so that the title is rendered in one place.</summary>
    private static string Subject(RebootForecast forecast, ForecastNote note) =>
        forecast.PendingUpdates.FirstOrDefault(u => u.Id == note.Subject) is { } update
            ? UpdateTitle(update)
            : note.Subject ?? string.Empty;

    private static string Time(ForecastNote note) => note.Time is { } time ? Display.ShortDateTime(time) : string.Empty;
}
