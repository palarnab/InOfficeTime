namespace InOfficeTime;

public sealed class WorkTimeLogWriter
{
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
}
