using System.Diagnostics;
using System.Runtime.Versioning;

namespace RebootWhy.Core.Updates;

/// <summary>
/// Update Orchestrator logs in C:\ProgramData\USOShared\Logs\System. Readable without admin rights;
/// decoded with the built-in tracerpt.exe, including the file USO is writing right now.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UsoLogSource(string? directory = null)
{
    private static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(60);

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "USOShared", "Logs", "System");

    private readonly string _directory = directory ?? DefaultDirectory;

    /// <summary>Decoded files: only changed ones are parsed again (usually just the current one).</summary>
    private readonly Dictionary<string, (DateTime LastWrite, long Length, List<UsoEvent> Events)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Events from files modified after <paramref name="sinceLocal"/> (and always from the newest file).</summary>
    public (List<UsoEvent> Events, List<ForecastProblem> Problems) Read(DateTime sinceLocal)
    {
        var problems = new List<ForecastProblem>();
        if (!Directory.Exists(_directory))
        {
            problems.Add(new ForecastProblem(ForecastProblemKind.NoUsoFolder, Subject: _directory));
            return ([], problems);
        }

        var files = new DirectoryInfo(_directory)
            .GetFiles("UpdateSessionOrchestration.*.etl")
            .OrderBy(f => f.LastWriteTime)
            .ToList();
        var recent = files.Where(f => f.LastWriteTime >= sinceLocal).ToList();
        if (recent.Count == 0 && files.Count > 0)
        {
            recent.Add(files[^1]);
        }

        var events = new List<UsoEvent>();
        foreach (var file in recent)
        {
            try
            {
                if (!_cache.TryGetValue(file.FullName, out var cached) || cached.LastWrite != file.LastWriteTimeUtc || cached.Length != file.Length)
                {
                    cached = (file.LastWriteTimeUtc, file.Length, UsoXmlParser.Parse(Decode(file.FullName)));
                    _cache[file.FullName] = cached;
                }

                events.AddRange(cached.Events);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException)
            {
                problems.Add(new ForecastProblem(ForecastProblemKind.CannotRead, Subject: file.Name, Detail: ex.Message));
            }
        }

        foreach (var stale in _cache.Keys.Except(recent.Select(f => f.FullName), StringComparer.OrdinalIgnoreCase).ToList())
        {
            _cache.Remove(stale);
        }

        return (events, problems);
    }

    public static string Decode(string etlPath)
    {
        var output = Path.Combine(Path.GetTempPath(), $"rebootwhy-uso-{Guid.NewGuid():N}.xml");
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tracerpt.exe"))
            {
                ArgumentList = { etlPath, "-o", output, "-of", "XML", "-y", "-lr" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(start) ?? throw new InvalidOperationException("tracerpt did not start");
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(DecodeTimeout))
            {
                process.Kill();
                throw new InvalidOperationException("tracerpt timed out");
            }

            if (process.ExitCode != 0 || !File.Exists(output))
            {
                throw new InvalidOperationException($"tracerpt exited with code {process.ExitCode}");
            }

            return File.ReadAllText(output);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
