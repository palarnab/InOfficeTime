using System.Globalization;

namespace InOfficeTime;

public sealed class TimeReportResponse
{
    public required string Month { get; init; }
    public required List<DayReport> Days { get; init; }
    public long MonthTotalSeconds { get; init; }
    public long MonthOfficeSeconds { get; init; }
    public long MonthRemoteSeconds { get; init; }
    public double MonthTotalHours => ToHours(MonthTotalSeconds);
    public double MonthOfficeHours => ToHours(MonthOfficeSeconds);
    public double MonthRemoteHours => ToHours(MonthRemoteSeconds);
    public int TotalOfficeDays => CountOfficeDays(Days);
    public double DaysOff { get; init; }

    internal static double ToHours(long seconds) =>
        Math.Round(seconds / 3600d, 2, MidpointRounding.AwayFromZero);

    internal static int CountOfficeDays(IEnumerable<DayReport> days)
    {
        var count = 0;
        foreach (var d in days)
        {
            if (d.DayOfficeSeconds > 3600)
                count++;
        }

        return count;
    }
}

public sealed class WeekReportResponse
{
    public required string Week { get; init; }
    public required string StartDate { get; init; }
    public required string EndDate { get; init; }
    public required List<DayReport> Days { get; init; }
    public long WeekTotalSeconds { get; init; }
    public long WeekOfficeSeconds { get; init; }
    public long WeekRemoteSeconds { get; init; }
    public double WeekTotalHours => TimeReportResponse.ToHours(WeekTotalSeconds);
    public double WeekOfficeHours => TimeReportResponse.ToHours(WeekOfficeSeconds);
    public double WeekRemoteHours => TimeReportResponse.ToHours(WeekRemoteSeconds);
    public int TotalOfficeDays => TimeReportResponse.CountOfficeDays(Days);
    public double DaysOff { get; init; }
}

public sealed class DayReport
{
    public required string Date { get; init; }
    public required List<SessionReport> Sessions { get; init; }
    public long DayTotalSeconds { get; init; }
    public long DayOfficeSeconds { get; init; }
    public long DayRemoteSeconds { get; init; }
    public double DayTotalHours => TimeReportResponse.ToHours(DayTotalSeconds);
    public double DayOfficeHours => TimeReportResponse.ToHours(DayOfficeSeconds);
    public double DayRemoteHours => TimeReportResponse.ToHours(DayRemoteSeconds);
    /// <summary>"half" or "full" when the user marked this day off; null otherwise.</summary>
    public string? DayOffType { get; init; }
}

public sealed class SessionReport
{
    public required DateTimeOffset Start { get; init; }
    public DateTimeOffset? End { get; init; }
    public long TotalSeconds { get; init; }
    public double TotalHours => TimeReportResponse.ToHours(TotalSeconds);
    public required string Location { get; init; }
    public bool Ongoing { get; init; }
}

public static class WorkTimeAnalytics
{
    private static readonly HashSet<string> ResumeEvents = new(StringComparer.Ordinal)
    {
        "SessionLogon",
        "SessionUnlock"
    };

    private static readonly HashSet<string> PauseEvents = new(StringComparer.Ordinal)
    {
        "SessionLock",
        "SessionLogoff"
    };

    public static TimeReportResponse BuildFromLogFile(string monthKey, string filePath, DateTimeOffset now)
    {
        var events = ReadEvents(new[] { filePath });

        var monthStart = MonthStartLocal(monthKey);
        var monthEnd = monthStart.AddMonths(1);

        var intervals = BuildIntervals(events, now);
        var clipped = ClipToRange(intervals, monthStart, monthEnd);

        var (days, totalSeconds, officeSeconds, remoteSeconds) = AggregateDays(clipped);
        var dayOffs = ExtractDayOffs(events, monthStart, monthEnd);
        var daysOffTotal = ApplyDayOffs(days, dayOffs);

        return new TimeReportResponse
        {
            Month = monthKey,
            Days = days,
            MonthTotalSeconds = totalSeconds,
            MonthOfficeSeconds = officeSeconds,
            MonthRemoteSeconds = remoteSeconds,
            DaysOff = daysOffTotal
        };
    }

    public static WeekReportResponse BuildWeekReport(
        string weekKey,
        int isoYear,
        int isoWeek,
        WorkTimeLogPaths paths,
        DateTimeOffset now)
    {
        var weekStartLocalDate = ISOWeek.ToDateTime(isoYear, isoWeek, DayOfWeek.Monday);
        var weekStart = new DateTimeOffset(DateTime.SpecifyKind(weekStartLocalDate, DateTimeKind.Local));
        var weekEnd = weekStart.AddDays(7);

        var monthKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var d = weekStart; d < weekEnd; d = d.AddDays(1))
            monthKeys.Add(d.ToString("yyyy-MM", CultureInfo.InvariantCulture));

        var filePaths = monthKeys
            .Select(paths.GetFilePathForMonth)
            .Where(File.Exists)
            .ToArray();

        var events = ReadEvents(filePaths);

        var intervals = BuildIntervals(events, now);
        var clipped = ClipToRange(intervals, weekStart, weekEnd);

        var (days, totalSeconds, officeSeconds, remoteSeconds) = AggregateDays(clipped);
        var dayOffs = ExtractDayOffs(events, weekStart, weekEnd);
        var daysOffTotal = ApplyDayOffs(days, dayOffs);

        return new WeekReportResponse
        {
            Week = weekKey,
            StartDate = weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDate = weekEnd.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Days = days,
            WeekTotalSeconds = totalSeconds,
            WeekOfficeSeconds = officeSeconds,
            WeekRemoteSeconds = remoteSeconds,
            DaysOff = daysOffTotal
        };
    }

    private static List<(DateTimeOffset Time, string Name, string Extra)> ReadEvents(IEnumerable<string> filePaths)
    {
        var events = new List<(DateTimeOffset Time, string Name, string Extra)>();
        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath))
                continue;

            foreach (var line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split('\t', StringSplitOptions.TrimEntries);
                if (parts.Length < 2)
                    continue;

                if (!DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t))
                    continue;

                var extra = parts.Length >= 4 ? parts[3] : string.Empty;
                events.Add((t, parts[1], extra));
            }
        }

        events.Sort((a, b) => a.Time.CompareTo(b.Time));
        return events;
    }

    private static (List<DayReport> Days, long Total, long Office, long Remote) AggregateDays(List<Interval> clipped)
    {
        var dayTotals = new Dictionary<string, List<SessionReport>>(StringComparer.Ordinal);
        long total = 0;
        long office = 0;
        long remote = 0;

        foreach (var interval in clipped)
        {
            var cursor = interval.Start;
            while (cursor < interval.End)
            {
                var sod = StartOfLocalDay(cursor);
                var nextMidnight = sod.AddDays(1);
                var chunkEnd = interval.End < nextMidnight ? interval.End : nextMidnight;
                var seconds = (long)(chunkEnd - cursor).TotalSeconds;
                if (seconds > 0)
                {
                    var dayKey = sod.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (!dayTotals.TryGetValue(dayKey, out var list))
                    {
                        list = [];
                        dayTotals[dayKey] = list;
                    }

                    var segOngoing = interval.Ongoing && chunkEnd.Ticks == interval.End.Ticks;
                    list.Add(new SessionReport
                    {
                        Start = cursor,
                        End = segOngoing ? null : chunkEnd,
                        TotalSeconds = seconds,
                        Location = interval.Location,
                        Ongoing = segOngoing
                    });

                    total += seconds;
                    if (interval.Location == Location.Office) office += seconds;
                    else remote += seconds;
                }

                cursor = chunkEnd;
            }
        }

        var days = dayTotals
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv =>
            {
                long dayOffice = 0, dayRemote = 0;
                foreach (var s in kv.Value)
                {
                    if (s.Location == Location.Office) dayOffice += s.TotalSeconds;
                    else dayRemote += s.TotalSeconds;
                }

                return new DayReport
                {
                    Date = kv.Key,
                    Sessions = kv.Value,
                    DayTotalSeconds = dayOffice + dayRemote,
                    DayOfficeSeconds = dayOffice,
                    DayRemoteSeconds = dayRemote
                };
            })
            .ToList();

        return (days, total, office, remote);
    }

    private static string NormalizeLocation(string raw) =>
        raw.Equals(Location.Office, StringComparison.OrdinalIgnoreCase) ? Location.Office : Location.Remote;

    private readonly record struct Interval(DateTimeOffset Start, DateTimeOffset End, bool Ongoing, string Location);

    private static List<Interval> ClipToRange(
        List<Interval> intervals,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        var list = new List<Interval>();
        foreach (var i in intervals)
        {
            var cs = i.Start < rangeStart ? rangeStart : i.Start;
            var ce = i.End > rangeEnd ? rangeEnd : i.End;
            if (cs >= ce)
                continue;

            var clippedOngoing = i.Ongoing && ce.Ticks == i.End.Ticks;
            list.Add(new Interval(cs, ce, clippedOngoing, i.Location));
        }

        return list;
    }

    private static List<Interval> BuildIntervals(
        List<(DateTimeOffset Time, string Name, string Extra)> events,
        DateTimeOffset now)
    {
        var result = new List<Interval>();
        var active = false;
        DateTimeOffset? sessionStart = null;
        string? sessionLocation = null;

        foreach (var (time, name, extra) in events)
        {
            if (ResumeEvents.Contains(name))
            {
                if (!active)
                {
                    active = true;
                    sessionStart = time;
                    sessionLocation = string.IsNullOrEmpty(extra)
                        ? Location.Remote
                        : NormalizeLocation(extra);
                }

                continue;
            }

            if (PauseEvents.Contains(name))
            {
                if (active && sessionStart is not null && sessionLocation is not null)
                {
                    result.Add(new Interval(sessionStart.Value, time, false, sessionLocation));
                    active = false;
                    sessionStart = null;
                    sessionLocation = null;
                }
            }
        }

        if (active && sessionStart is not null && sessionLocation is not null)
            result.Add(new Interval(sessionStart.Value, now, true, sessionLocation));

        return result;
    }

    private static Dictionary<string, string> ExtractDayOffs(
        List<(DateTimeOffset Time, string Name, string Extra)> events,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (time, name, extra) in events)
        {
            if (!string.Equals(name, WorkTimeLogWriter.DayOffEventName, StringComparison.Ordinal))
                continue;
            if (time < rangeStart || time >= rangeEnd)
                continue;

            var kind = NormalizeDayOffKind(extra);
            if (kind is null)
                continue;

            var dateKey = time.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            // Last write wins: a later log entry for the same date overrides earlier ones.
            result[dateKey] = kind;
        }

        return result;
    }

    private static double ApplyDayOffs(List<DayReport> days, Dictionary<string, string> dayOffs)
    {
        if (dayOffs.Count == 0)
            return 0d;

        double total = 0d;
        foreach (var (_, kind) in dayOffs)
            total += kind == WorkTimeLogWriter.DayOffFull ? 1.0 : 0.5;

        var byDate = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < days.Count; i++)
            byDate[days[i].Date] = i;

        foreach (var (date, kind) in dayOffs)
        {
            if (byDate.TryGetValue(date, out var idx))
            {
                var existing = days[idx];
                days[idx] = new DayReport
                {
                    Date = existing.Date,
                    Sessions = existing.Sessions,
                    DayTotalSeconds = existing.DayTotalSeconds,
                    DayOfficeSeconds = existing.DayOfficeSeconds,
                    DayRemoteSeconds = existing.DayRemoteSeconds,
                    DayOffType = kind
                };
            }
            else
            {
                days.Add(new DayReport
                {
                    Date = date,
                    Sessions = new List<SessionReport>(),
                    DayTotalSeconds = 0,
                    DayOfficeSeconds = 0,
                    DayRemoteSeconds = 0,
                    DayOffType = kind
                });
            }
        }

        days.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));
        return total;
    }

    private static string? NormalizeDayOffKind(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;
        if (raw.Equals(WorkTimeLogWriter.DayOffFull, StringComparison.OrdinalIgnoreCase))
            return WorkTimeLogWriter.DayOffFull;
        if (raw.Equals(WorkTimeLogWriter.DayOffHalf, StringComparison.OrdinalIgnoreCase))
            return WorkTimeLogWriter.DayOffHalf;
        return null;
    }

    private static DateTimeOffset MonthStartLocal(string monthKey)
    {
        if (!DateTime.TryParseExact(monthKey + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
            throw new ArgumentException("Invalid month key.", nameof(monthKey));

        var local = DateTime.SpecifyKind(dt, DateTimeKind.Local);
        return new DateTimeOffset(local);
    }

    private static DateTimeOffset StartOfLocalDay(DateTimeOffset t) =>
        new(t.Year, t.Month, t.Day, 0, 0, 0, t.Offset);
}
