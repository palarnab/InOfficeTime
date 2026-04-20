using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace InOfficeTime;

/// <summary>
/// Renders work-time reports as standalone HTML pages. Styling is inlined so the
/// responses stay self-contained and usable in any browser without extra assets.
/// </summary>
internal static class HtmlRenderer
{
    private const string CssStyles = """
        :root {
            color-scheme: light dark;
            --bg: #0f172a;
            --bg-soft: #111c36;
            --card: #1b2646;
            --card-border: #2a3a66;
            --text: #e6ecff;
            --muted: #9aa7c7;
            --accent: #6aa7ff;
            --accent-soft: #1e3a6b;
            --office: #4ade80;
            --remote: #fbbf24;
            --ongoing: #38bdf8;
        }
        @media (prefers-color-scheme: light) {
            :root {
                --bg: #f4f6fb;
                --bg-soft: #eaeef7;
                --card: #ffffff;
                --card-border: #dbe2ef;
                --text: #1a2238;
                --muted: #5a6684;
                --accent: #2563eb;
                --accent-soft: #dbe7ff;
                --office: #16a34a;
                --remote: #b45309;
                --ongoing: #0284c7;
            }
        }
        * { box-sizing: border-box; }
        body {
            margin: 0;
            padding: 2rem 1.25rem 4rem;
            background: var(--bg);
            color: var(--text);
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto,
                "Helvetica Neue", Arial, sans-serif;
            font-size: 15px;
            line-height: 1.5;
        }
        main {
            max-width: 960px;
            margin: 0 auto;
        }
        header.page-header {
            display: flex;
            align-items: baseline;
            justify-content: space-between;
            flex-wrap: wrap;
            gap: 1rem;
            margin-bottom: 1.5rem;
            padding-bottom: 1rem;
            border-bottom: 1px solid var(--card-border);
        }
        h1 {
            font-size: 1.75rem;
            margin: 0;
            letter-spacing: -0.01em;
        }
        h2 {
            font-size: 1.15rem;
            margin: 0 0 .75rem;
        }
        .subtitle {
            color: var(--muted);
            font-size: .9rem;
        }
        nav.links {
            display: flex;
            gap: .5rem;
            flex-wrap: wrap;
        }
        nav.links a {
            background: var(--accent-soft);
            color: var(--accent);
            padding: .35rem .75rem;
            border-radius: 999px;
            font-size: .85rem;
            text-decoration: none;
            font-weight: 500;
        }
        nav.links a:hover { filter: brightness(1.1); }
        form.filter {
            display: flex;
            align-items: center;
            gap: .5rem;
            flex-wrap: wrap;
            background: var(--card);
            border: 1px solid var(--card-border);
            border-radius: 12px;
            padding: .75rem 1rem;
            margin-bottom: 1.5rem;
        }
        form.filter label {
            color: var(--muted);
            font-size: .85rem;
            font-weight: 500;
        }
        form.filter input[type="week"] {
            background: var(--bg-soft);
            color: var(--text);
            border: 1px solid var(--card-border);
            border-radius: 8px;
            padding: .35rem .6rem;
            font-family: inherit;
            font-size: .9rem;
            color-scheme: light dark;
        }
        form.filter button {
            background: var(--accent);
            color: var(--bg);
            border: none;
            border-radius: 999px;
            padding: .4rem .9rem;
            font-size: .85rem;
            font-weight: 600;
            cursor: pointer;
        }
        form.filter button:hover { filter: brightness(1.1); }
        .summary-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
            gap: .75rem;
            margin-bottom: 1.5rem;
        }
        .card {
            background: var(--card);
            border: 1px solid var(--card-border);
            border-radius: 12px;
            padding: 1rem 1.1rem;
        }
        .stat-label {
            color: var(--muted);
            font-size: .75rem;
            text-transform: uppercase;
            letter-spacing: .06em;
        }
        .stat-value {
            font-size: 1.5rem;
            font-weight: 600;
            margin-top: .25rem;
        }
        .stat-value .unit {
            font-size: .85rem;
            color: var(--muted);
            font-weight: 400;
            margin-left: .25rem;
        }
        .stat-office .stat-value { color: var(--office); }
        .stat-remote .stat-value { color: var(--remote); }
        details.day {
            background: var(--card);
            border: 1px solid var(--card-border);
            border-radius: 12px;
            padding: .75rem 1rem;
            margin-bottom: .6rem;
        }
        details.day[open] { padding-bottom: 1rem; }
        details.day summary {
            cursor: pointer;
            list-style: none;
            display: flex;
            align-items: center;
            justify-content: space-between;
            flex-wrap: wrap;
            gap: .75rem;
            font-weight: 500;
        }
        details.day summary::-webkit-details-marker { display: none; }
        details.day summary::before {
            content: "▸";
            color: var(--muted);
            margin-right: .5rem;
            transition: transform .15s ease;
            display: inline-block;
        }
        details.day[open] summary::before { transform: rotate(90deg); }
        .day-title {
            display: flex;
            align-items: baseline;
            gap: .75rem;
            flex: 1 1 auto;
        }
        .day-date { font-variant-numeric: tabular-nums; }
        .day-weekday { color: var(--muted); font-size: .85rem; }
        .day-stats {
            display: flex;
            gap: 1rem;
            font-size: .85rem;
            color: var(--muted);
            font-variant-numeric: tabular-nums;
        }
        .day-stats strong { color: var(--text); font-weight: 600; }
        .chip-office { color: var(--office); font-weight: 600; }
        .chip-remote { color: var(--remote); font-weight: 600; }
        table.sessions {
            width: 100%;
            border-collapse: collapse;
            margin-top: .75rem;
            font-size: .9rem;
            font-variant-numeric: tabular-nums;
        }
        table.sessions th,
        table.sessions td {
            text-align: left;
            padding: .4rem .5rem;
            border-bottom: 1px solid var(--card-border);
        }
        table.sessions th {
            color: var(--muted);
            font-weight: 500;
            font-size: .78rem;
            text-transform: uppercase;
            letter-spacing: .05em;
        }
        table.sessions tr:last-child td { border-bottom: none; }
        .badge {
            display: inline-block;
            padding: .1rem .55rem;
            border-radius: 999px;
            font-size: .75rem;
            font-weight: 600;
            border: 1px solid currentColor;
        }
        .badge-office { color: var(--office); }
        .badge-remote { color: var(--remote); }
        .badge-ongoing {
            color: var(--ongoing);
            background: color-mix(in srgb, var(--ongoing) 15%, transparent);
            border-color: transparent;
            margin-left: .4rem;
            animation: pulse 1.6s ease-in-out infinite;
        }
        @keyframes pulse {
            0%, 100% { opacity: 1; }
            50% { opacity: .55; }
        }
        pre.log {
            background: var(--card);
            border: 1px solid var(--card-border);
            border-radius: 12px;
            padding: 1rem;
            overflow: auto;
            font-family: ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, monospace;
            font-size: .85rem;
            line-height: 1.45;
            max-height: 75vh;
        }
        .empty {
            color: var(--muted);
            text-align: center;
            padding: 2rem 1rem;
        }
        footer.page-footer {
            margin-top: 2.5rem;
            color: var(--muted);
            font-size: .8rem;
            text-align: center;
        }
        footer.page-footer a {
            color: var(--accent);
            text-decoration: none;
        }
        footer.page-footer a:hover { text-decoration: underline; }
        """;

    public static string RenderMonthReport(TimeReportResponse report)
    {
        var sb = new StringBuilder(8192);
        BeginDocument(sb, $"Work time — {report.Month}");

        sb.Append("<header class=\"page-header\"><div><h1>").Append(HtmlEscape(report.Month))
          .Append("</h1><div class=\"subtitle\">Monthly work-time report</div></div>");
        AppendNav(sb, activeMonth: report.Month);
        sb.Append("</header>");

        sb.Append("<section class=\"summary-grid\">");
        AppendStat(sb, "Total", report.MonthTotalHours, "stat-total");
        AppendStat(sb, "Office", report.MonthOfficeHours, "stat-office");
        AppendStat(sb, "Remote", report.MonthRemoteHours, "stat-remote");
        AppendStatText(sb, "Total office days", report.TotalOfficeDays.ToString(CultureInfo.InvariantCulture), "stat-office-days");
        AppendStatText(sb, "Days logged", report.Days.Count.ToString(CultureInfo.InvariantCulture), "stat-days");
        sb.Append("</section>");

        if (report.Days.Count == 0)
        {
            sb.Append("<div class=\"card empty\">No sessions logged this month yet.</div>");
        }
        else
        {
            sb.Append("<section>");
            foreach (var day in report.Days)
                AppendDay(sb, day, openByDefault: false);
            sb.Append("</section>");
        }

        EndDocument(sb);
        return sb.ToString();
    }

    public static string RenderWeekReport(WeekReportResponse report)
    {
        var sb = new StringBuilder(8192);
        BeginDocument(sb, $"Work time — {report.Week}");

        sb.Append("<header class=\"page-header\"><div><h1>").Append(HtmlEscape(report.Week))
          .Append("</h1><div class=\"subtitle\">Weekly work-time report · ")
          .Append(HtmlEscape(FormatDateRange(report.StartDate, report.EndDate)))
          .Append("</div></div>");
        AppendNav(sb, activeMonth: null, activeWeek: report.Week);
        sb.Append("</header>");

        sb.Append("<form class=\"filter\" method=\"get\" action=\"/week\">")
          .Append("<label for=\"week-picker\">Filter by week:</label>")
          .Append("<input type=\"week\" id=\"week-picker\" name=\"week\" value=\"")
          .Append(HtmlEscape(report.Week)).Append("\">")
          .Append("<button type=\"submit\">Go</button>")
          .Append("</form>");

        sb.Append("<section class=\"summary-grid\">");
        AppendStat(sb, "Total", report.WeekTotalHours, "stat-total");
        AppendStat(sb, "Office", report.WeekOfficeHours, "stat-office");
        AppendStat(sb, "Remote", report.WeekRemoteHours, "stat-remote");
        AppendStatText(sb, "Total office days", report.TotalOfficeDays.ToString(CultureInfo.InvariantCulture), "stat-office-days");
        AppendStatText(sb, "Days logged", report.Days.Count.ToString(CultureInfo.InvariantCulture), "stat-days");
        sb.Append("</section>");

        if (report.Days.Count == 0)
        {
            sb.Append("<div class=\"card empty\">No sessions logged this week yet.</div>");
        }
        else
        {
            sb.Append("<section>");
            foreach (var day in report.Days)
                AppendDay(sb, day, openByDefault: false);
            sb.Append("</section>");
        }

        EndDocument(sb);
        return sb.ToString();
    }

    public static string RenderDayReport(DayReport day)
    {
        var sb = new StringBuilder(4096);
        BeginDocument(sb, $"Work time — {day.Date}");

        var monthKey = day.Date.Length >= 7 ? day.Date[..7] : null;

        sb.Append("<header class=\"page-header\"><div><h1>").Append(HtmlEscape(day.Date))
          .Append("</h1><div class=\"subtitle\">")
          .Append(HtmlEscape(FormatWeekday(day.Date)))
          .Append("</div></div>");
        AppendNav(sb, activeMonth: monthKey);
        sb.Append("</header>");

        sb.Append("<section class=\"summary-grid\">");
        AppendStat(sb, "Total", day.DayTotalHours, "stat-total");
        AppendStat(sb, "Office", day.DayOfficeHours, "stat-office");
        AppendStat(sb, "Remote", day.DayRemoteHours, "stat-remote");
        AppendStatText(sb, "Sessions", day.Sessions.Count.ToString(CultureInfo.InvariantCulture), "stat-days");
        sb.Append("</section>");

        AppendDay(sb, day, openByDefault: true);

        EndDocument(sb);
        return sb.ToString();
    }

    public static string RenderLog(string monthKey, string logContent)
    {
        var sb = new StringBuilder(logContent.Length + 2048);
        BeginDocument(sb, $"Log — {monthKey}");

        sb.Append("<header class=\"page-header\"><div><h1>Raw log</h1><div class=\"subtitle\">")
          .Append(HtmlEscape(monthKey)).Append("</div></div>");
        AppendNav(sb, activeMonth: monthKey);
        sb.Append("</header>");

        if (string.IsNullOrWhiteSpace(logContent))
            sb.Append("<div class=\"card empty\">Log file is empty.</div>");
        else
            sb.Append("<pre class=\"log\">").Append(HtmlEscape(logContent)).Append("</pre>");

        EndDocument(sb);
        return sb.ToString();
    }

    public static string RenderNotFound(string title, string message)
    {
        var sb = new StringBuilder(1024);
        BeginDocument(sb, title);
        sb.Append("<header class=\"page-header\"><div><h1>").Append(HtmlEscape(title))
          .Append("</h1></div>");
        AppendNav(sb, activeMonth: null);
        sb.Append("</header>");
        sb.Append("<div class=\"card empty\">").Append(HtmlEscape(message)).Append("</div>");
        EndDocument(sb);
        return sb.ToString();
    }

    private static void AppendDay(StringBuilder sb, DayReport day, bool openByDefault)
    {
        sb.Append("<details class=\"day\"");
        if (openByDefault) sb.Append(" open");
        sb.Append("><summary><span class=\"day-title\"><span class=\"day-date\">")
          .Append(HtmlEscape(day.Date))
          .Append("</span><span class=\"day-weekday\">")
          .Append(HtmlEscape(FormatWeekday(day.Date)))
          .Append("</span></span><span class=\"day-stats\"><span><strong>")
          .Append(FormatHours(day.DayTotalHours))
          .Append("</strong> total</span><span class=\"chip-office\">")
          .Append(FormatHours(day.DayOfficeHours))
          .Append(" office</span><span class=\"chip-remote\">")
          .Append(FormatHours(day.DayRemoteHours))
          .Append(" remote</span></span></summary>");

        if (day.Sessions.Count == 0)
        {
            sb.Append("<div class=\"empty\">No sessions.</div>");
        }
        else
        {
            sb.Append("<table class=\"sessions\"><thead><tr>")
              .Append("<th>Start</th><th>End</th><th>Duration</th><th>Location</th>")
              .Append("</tr></thead><tbody>");
            foreach (var s in day.Sessions)
            {
                sb.Append("<tr><td>").Append(FormatTime(s.Start)).Append("</td>")
                  .Append("<td>").Append(s.End is null ? "—" : FormatTime(s.End.Value)).Append("</td>")
                  .Append("<td>").Append(FormatHours(s.TotalHours)).Append("</td>")
                  .Append("<td><span class=\"badge ")
                  .Append(s.Location == Location.Office ? "badge-office" : "badge-remote")
                  .Append("\">").Append(HtmlEscape(s.Location)).Append("</span>");
                if (s.Ongoing)
                    sb.Append("<span class=\"badge badge-ongoing\">ongoing</span>");
                sb.Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append("</details>");
    }

    private static void AppendStat(StringBuilder sb, string label, double hours, string extraClass)
    {
        sb.Append("<div class=\"card ").Append(extraClass).Append("\"><div class=\"stat-label\">")
          .Append(HtmlEscape(label)).Append("</div><div class=\"stat-value\">")
          .Append(FormatHours(hours)).Append("<span class=\"unit\">h</span></div></div>");
    }

    private static void AppendStatText(StringBuilder sb, string label, string value, string extraClass)
    {
        sb.Append("<div class=\"card ").Append(extraClass).Append("\"><div class=\"stat-label\">")
          .Append(HtmlEscape(label)).Append("</div><div class=\"stat-value\">")
          .Append(HtmlEscape(value)).Append("</div></div>");
    }

    private static void AppendNav(StringBuilder sb, string? activeMonth, string? activeWeek = null)
    {
        sb.Append("<nav class=\"links\">");
        if (activeMonth is not null)
        {
            sb.Append("<a href=\"/time?month=").Append(HtmlEscape(activeMonth)).Append("\">Month</a>");
            sb.Append("<a href=\"/log?month=").Append(HtmlEscape(activeMonth)).Append("\">Log</a>");
        }
        if (activeWeek is not null)
        {
            sb.Append("<a href=\"/week?week=").Append(HtmlEscape(activeWeek)).Append("\">Week</a>");
        }
        sb.Append("<a href=\"/time\">This month</a>");
        sb.Append("<a href=\"/week\">This week</a>");
        sb.Append("<a href=\"/day\">Today</a>");
        sb.Append("</nav>");
    }

    private static void BeginDocument(StringBuilder sb, string title)
    {
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<title>").Append(HtmlEscape(title)).Append("</title>")
          .Append("<style>").Append(CssStyles).Append("</style></head><body><main>");
    }

    private static readonly string AppVersion =
        typeof(HtmlRenderer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private const string ContactEmail = "arnab.i@gmail.com";

    private static void EndDocument(StringBuilder sb)
    {
        sb.Append("<footer class=\"page-footer\">InOfficeTime v")
          .Append(HtmlEscape(AppVersion))
          .Append(" · ")
          .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
          .Append(" · <a href=\"mailto:").Append(ContactEmail).Append("\">")
          .Append(ContactEmail).Append("</a>")
          .Append("</footer></main></body></html>");
    }

    private static string FormatHours(double hours) =>
        hours.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatTime(DateTimeOffset t) =>
        t.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatDateRange(string startDateKey, string endDateKey)
    {
        if (!DateTime.TryParseExact(startDateKey, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start))
            return $"{startDateKey} – {endDateKey}";

        if (!DateTime.TryParseExact(endDateKey, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end))
            return $"{startDateKey} – {endDateKey}";

        var startFormat = start.Year == end.Year && start.Month == end.Month ? "MMM d" : "MMM d";
        return start.Year == end.Year
            ? $"{start.ToString(startFormat, CultureInfo.InvariantCulture)} – {end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}"
            : $"{start.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)} – {end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}";
    }

    private static string FormatWeekday(string dateKey)
    {
        return DateTime.TryParseExact(dateKey, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dt)
            ? dt.ToString("dddd", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string HtmlEscape(string value) => HtmlEncoder.Default.Encode(value);
}
