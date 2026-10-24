using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RebootWhy.Core.Updates;

/// <summary>
/// Installation history and known but not installed updates via the Windows Update Agent API (no admin rights needed).
/// The search uses the local Windows Update database and does not contact Microsoft servers.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsUpdateCatalogReader
{
    private const int DriverType = 2;

    public static List<InstalledUpdate> ReadHistory(int maxEntries = 2000)
    {
        dynamic searcher = CreateSearcher();
        int total = searcher.GetTotalHistoryCount();
        dynamic history = searcher.QueryHistory(0, Math.Min(total, maxEntries));
        int count = history.Count;

        var entries = new List<InstalledUpdate>(count);
        for (var i = 0; i < count; i++)
        {
            dynamic entry = history[i];
            DateTime date = entry.Date;
            entries.Add(new InstalledUpdate(
                DateTime.SpecifyKind(date, DateTimeKind.Utc).ToLocalTime(),
                (string?)entry.Title ?? Properties.Strings.UntitledUpdate,
                NullIfBlank((string?)entry.Description),
                Enum.IsDefined(typeof(UpdateOperation), (int)entry.Operation) ? (UpdateOperation)(int)entry.Operation : UpdateOperation.Other,
                (UpdateResult)(int)entry.ResultCode,
                (int)entry.HResult,
                NullIfBlank((string?)entry.ClientApplicationID),
                NullIfBlank((string?)entry.SupportUrl),
                (string)entry.UpdateIdentity.UpdateID,
                (int)entry.UpdateIdentity.RevisionNumber,
                Names(entry.Categories)));
        }

        return entries;
    }

    public static List<AvailableUpdate> SearchAvailable()
    {
        dynamic searcher = CreateSearcher();
        searcher.Online = false;
        dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0");
        dynamic updates = result.Updates;
        int count = updates.Count;

        var available = new List<AvailableUpdate>(count);
        for (var i = 0; i < count; i++)
        {
            dynamic update = updates[i];
            List<string> kbArticles = Strings(update.KBArticleIDs);
            available.Add(new AvailableUpdate(
                (string)update.Identity.UpdateID,
                (int)update.Identity.RevisionNumber,
                (string?)update.Title ?? Properties.Strings.UntitledUpdate,
                NullIfBlank((string?)update.Description),
                (int)update.Type == DriverType,
                (bool)update.IsDownloaded,
                (bool)update.IsMandatory,
                (UpdateRebootBehavior)(int)update.InstallationBehavior.RebootBehavior,
                kbArticles.Select(kb => "KB" + kb).ToList(),
                NullIfBlank((string?)update.SupportUrl),
                ReadDate(update),
                NullIfBlank((string?)update.MsrcSeverity),
                Names(update.Categories)));
        }

        return available;
    }

    private static dynamic CreateSearcher()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session")
            ?? throw new COMException("Windows Update Agent is not available");
        dynamic session = Activator.CreateInstance(type)!;
        session.ClientApplicationID = "RebootWhy";
        return session.CreateUpdateSearcher();
    }

    private static DateTime? ReadDate(dynamic update)
    {
        try
        {
            DateTime value = update.LastDeploymentChangeTime;
            return value.Year < 2000 ? null : value;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }

    private static List<string> Names(dynamic categories)
    {
        var names = new List<string>();
        int count = categories.Count;
        for (var i = 0; i < count; i++)
        {
            names.Add((string)categories[i].Name);
        }

        return names;
    }

    private static List<string> Strings(dynamic collection)
    {
        var values = new List<string>();
        int count = collection.Count;
        for (var i = 0; i < count; i++)
        {
            values.Add((string)collection[i]);
        }

        return values;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
