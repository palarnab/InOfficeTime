namespace InOfficeTime;

public sealed class WorkTimeLogWriter
{
    public const string DayOffEventName = "OutOfOffice";
    public const string DayOffFull = "full";
    public const string DayOffHalf = "half";

    private readonly WorkTimeLogPaths _paths;
    private readonly LocationDetector _locationDetector;
    private readonly ILogger<WorkTimeLogWriter> _logger;
    private readonly object _gate = new();

    public WorkTimeLogWriter(
        WorkTimeLogPaths paths,
        LocationDetector locationDetector,
        ILogger<WorkTimeLogWriter> logger)
    {
        _paths = paths;
        _locationDetector = locationDetector;
        _logger = logger;
    }

    public void Append(string eventName, int sessionId)
    {
        var now = DateTimeOffset.Now;
        var monthKey = now.ToString("yyyy-MM");
        var path = _paths.GetFilePathForMonth(monthKey);
        var location = _locationDetector.Detect();
        var line = $"{now:o}\t{eventName}\t{sessionId}\t{location}";
        lock (_gate)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }

        _logger.LogDebug("Logged {Event} session {SessionId} ({Location}) to {Path}",
            eventName, sessionId, location, path);
    }

    /// <summary>
    /// Append a day-off marker. Reuses the tab-separated log format: the event name
    /// is <see cref="DayOffEventName"/> and the fourth column carries the kind
    /// (<see cref="DayOffFull"/> or <see cref="DayOffHalf"/>). The timestamp is
    /// pinned to local noon of the target date so it sorts naturally inside the day.
    /// </summary>
    public void AppendDayOff(DateTime localDate, string kind)
    {
        var dateOnly = localDate.Date;
        var monthKey = dateOnly.ToString("yyyy-MM");
        var path = _paths.GetFilePathForMonth(monthKey);
        var ts = new DateTimeOffset(DateTime.SpecifyKind(dateOnly.AddHours(12), DateTimeKind.Local));
        var line = $"{ts:o}\t{DayOffEventName}\t0\t{kind}";
        lock (_gate)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }

        _logger.LogInformation("Logged {Event} ({Kind}) for {Date} to {Path}",
            DayOffEventName, kind, dateOnly.ToString("yyyy-MM-dd"), path);
    }
}
