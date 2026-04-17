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

    internal static double ToHours(long seconds) =>
        Math.Round(seconds / 3600d, 2, MidpointRounding.AwayFromZero);
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
        var lines = File.ReadAllLines(filePath);
        var events = new List<(DateTimeOffset Time, string Name, string Location)>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split('\t', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                continue;

            if (!DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t))
                continue;

            var location = parts.Length >= 4 && !string.IsNullOrEmpty(parts[3])
                ? NormalizeLocation(parts[3])
                : Location.Remote;

            events.Add((t, parts[1], location));
        }

        events.Sort((a, b) => a.Time.CompareTo(b.Time));

        var monthStart = MonthStartLocal(monthKey);
        var monthEnd = monthStart.AddMonths(1);

        var intervals = BuildIntervals(events, now);
        var clipped = ClipToMonth(intervals, monthStart, monthEnd);

        var dayTotals = new Dictionary<string, List<SessionReport>>(StringComparer.Ordinal);
        long monthTotal = 0;
        long monthOffice = 0;
        long monthRemote = 0;

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

                    monthTotal += seconds;
                    if (interval.Location == Location.Office) monthOffice += seconds;
                    else monthRemote += seconds;
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

        return new TimeReportResponse
        {
            Month = monthKey,
            Days = days,
            MonthTotalSeconds = monthTotal,
            MonthOfficeSeconds = monthOffice,
            MonthRemoteSeconds = monthRemote
        };
    }

    private static string NormalizeLocation(string raw) =>
        raw.Equals(Location.Office, StringComparison.OrdinalIgnoreCase) ? Location.Office : Location.Remote;

    private readonly record struct Interval(DateTimeOffset Start, DateTimeOffset End, bool Ongoing, string Location);

    private static List<Interval> ClipToMonth(
        List<Interval> intervals,
        DateTimeOffset monthStart,
        DateTimeOffset monthEnd)
    {
        var list = new List<Interval>();
        foreach (var i in intervals)
        {
            var cs = i.Start < monthStart ? monthStart : i.Start;
            var ce = i.End > monthEnd ? monthEnd : i.End;
            if (cs >= ce)
                continue;

            var clippedOngoing = i.Ongoing && ce.Ticks == i.End.Ticks;
            list.Add(new Interval(cs, ce, clippedOngoing, i.Location));
        }

        return list;
    }

    private static List<Interval> BuildIntervals(
        List<(DateTimeOffset Time, string Name, string Location)> events,
        DateTimeOffset now)
    {
        var result = new List<Interval>();
        var active = false;
        DateTimeOffset? sessionStart = null;
        string? sessionLocation = null;

        foreach (var (time, name, location) in events)
        {
            if (ResumeEvents.Contains(name))
            {
                if (!active)
                {
                    active = true;
                    sessionStart = time;
                    sessionLocation = location;
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
