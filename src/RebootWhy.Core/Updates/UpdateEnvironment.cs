namespace RebootWhy.Core.Updates;

/// <summary>Windows Update active hours: Windows does not restart for updates during this time.</summary>
public sealed record ActiveHours(int StartHour, int EndHour)
{
    public bool IsActive(DateTime time) => StartHour <= EndHour
        ? time.Hour >= StartHour && time.Hour < EndHour
        : time.Hour >= StartHour || time.Hour < EndHour;

    /// <summary>The nearest moment outside active hours, starting from <paramref name="time"/>.</summary>
    public DateTime NextOutside(DateTime time)
    {
        if (!IsActive(time))
        {
            return time;
        }

        var end = time.Date.AddHours(EndHour);
        return end > time ? end : end.AddDays(1);
    }

    public string Describe() => $"{StartHour}:00–{EndHour}:00";
}

/// <summary>System state the analyzer needs in addition to the USO log.</summary>
public sealed record UpdateEnvironment
{
    public ActiveHours? ActiveHours { get; init; }

    /// <summary>Last boot time (local): USO events before it do not describe the current state.</summary>
    public DateTime? LastBootLocal { get; init; }

    /// <summary>Microsoft.Update.SystemInfo.RebootRequired.</summary>
    public bool? WindowsUpdateRebootRequired { get; init; }

    /// <summary>The Component Based Servicing\RebootPending key.</summary>
    public bool ComponentServicingRebootPending { get; init; }

    public bool NoAutoRebootWithLoggedOnUsers { get; init; }

    /// <summary>UpdateOrchestrator task schedule from Task Scheduler (available with admin rights only).</summary>
    public IReadOnlyDictionary<string, DateTime?>? ScheduledTasks { get; init; }
}
