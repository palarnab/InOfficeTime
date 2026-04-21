using System.Globalization;
using InOfficeTime;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("http://localhost:11000");

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.WriteIndented = true;
});

builder.Services.AddSingleton<WorkTimeLogPaths>();
builder.Services.AddSingleton<LocationDetector>();
builder.Services.AddSingleton<WorkTimeLogWriter>();
builder.Services.AddHostedService<SessionMonitorWorker>();

var app = builder.Build();

app.MapGet("/", (HttpRequest req) =>
{
    if (WantsJson(req))
        return Results.Json(new { endpoints = new[] { "/log", "/time", "/week", "/day", "/off" } });

    return Results.Redirect("/time");
});

app.MapGet("/log", (string? month, HttpRequest req, WorkTimeLogPaths paths) =>
{
    if (!TryResolveMonth(month, out var key, out var badRequest))
        return badRequest!;

    var path = paths.GetFilePathForMonth(key);
    if (!File.Exists(path))
        return NotFoundFor(req, "Log not found", $"No log file exists for {key}.");

    var content = File.ReadAllText(path);
    if (WantsJson(req))
        return Results.Text(content, "text/plain; charset=utf-8");

    return Results.Content(HtmlRenderer.RenderLog(key, content), "text/html; charset=utf-8");
});

app.MapGet("/time", (string? month, HttpRequest req, WorkTimeLogPaths paths) =>
{
    if (!TryResolveMonth(month, out var key, out var badRequest))
        return badRequest!;

    var path = paths.GetFilePathForMonth(key);
    if (!File.Exists(path))
        return NotFoundFor(req, "Month not found", $"No log file exists for {key}.");

    var report = WorkTimeAnalytics.BuildFromLogFile(key, path, DateTimeOffset.Now);
    if (WantsJson(req))
        return Results.Json(report);

    return Results.Content(HtmlRenderer.RenderMonthReport(report), "text/html; charset=utf-8");
});

app.MapGet("/week", (string? week, HttpRequest req, WorkTimeLogPaths paths) =>
{
    if (!TryResolveWeek(week, out var key, out var isoYear, out var isoWeek, out var badRequest))
        return badRequest!;

    var report = WorkTimeAnalytics.BuildWeekReport(key, isoYear, isoWeek, paths, DateTimeOffset.Now);
    if (WantsJson(req))
        return Results.Json(report);

    return Results.Content(HtmlRenderer.RenderWeekReport(report), "text/html; charset=utf-8");
});

app.MapGet("/day", (string? date, HttpRequest req, WorkTimeLogPaths paths) =>
{
    if (!TryResolveDate(date, out var dateKey, out var badRequest))
        return badRequest!;

    var monthKey = WorkTimeLogPaths.MonthKeyFromDateKey(dateKey);
    var path = paths.GetFilePathForMonth(monthKey);
    if (!File.Exists(path))
        return NotFoundFor(req, "Day not found", $"No log file exists for {monthKey}.");

    var report = WorkTimeAnalytics.BuildFromLogFile(monthKey, path, DateTimeOffset.Now);
    var day = report.Days.FirstOrDefault(d => d.Date == dateKey);
    if (day is null)
        return NotFoundFor(req, "Day not found", $"No sessions recorded for {dateKey}.");

    if (WantsJson(req))
        return Results.Json(day);

    return Results.Content(HtmlRenderer.RenderDayReport(day), "text/html; charset=utf-8");
});

app.MapPost("/off", async (HttpRequest req, WorkTimeLogWriter writer) =>
{
    string? kind = null;
    string? date = null;

    if (req.HasFormContentType)
    {
        var form = await req.ReadFormAsync();
        kind = form["type"].ToString();
        date = form["date"].ToString();
    }

    if (string.IsNullOrWhiteSpace(kind))
        kind = req.Query["type"].ToString();
    if (string.IsNullOrWhiteSpace(date))
        date = req.Query["date"].ToString();

    kind = string.IsNullOrWhiteSpace(kind)
        ? WorkTimeLogWriter.DayOffFull
        : kind.Trim().ToLowerInvariant();

    if (kind != WorkTimeLogWriter.DayOffFull && kind != WorkTimeLogWriter.DayOffHalf)
        return Results.BadRequest("Invalid type. Expected 'full' or 'half'.");

    DateTime localDate;
    if (string.IsNullOrWhiteSpace(date))
    {
        localDate = DateTime.Now.Date;
    }
    else
    {
        var trimmed = date.Trim();
        if (!WorkTimeLogPaths.IsValidDateKey(trimmed))
            return Results.BadRequest("Invalid date. Expected yyyy-mm-dd.");

        localDate = DateTime.ParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    writer.AppendDayOff(localDate, kind);

    if (WantsJson(req))
    {
        return Results.Json(new
        {
            date = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            type = kind,
            status = "logged"
        });
    }

    var referer = req.Headers.Referer.ToString();
    var redirectTo = string.IsNullOrWhiteSpace(referer) ? "/time" : referer;
    return Results.Redirect(redirectTo);
});

app.Run();

static bool WantsJson(HttpRequest req)
{
    var contentType = req.Headers.ContentType.ToString();
    if (!string.IsNullOrEmpty(contentType) &&
        contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    var accept = req.Headers.Accept.ToString();
    if (!string.IsNullOrEmpty(accept) &&
        accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) &&
        !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return false;
}

static IResult NotFoundFor(HttpRequest req, string title, string message)
{
    if (WantsJson(req))
        return Results.NotFound();

    return Results.Content(
        HtmlRenderer.RenderNotFound(title, message),
        "text/html; charset=utf-8",
        statusCode: StatusCodes.Status404NotFound);
}

static bool TryResolveMonth(string? month, out string key, out IResult? badRequest)
{
    badRequest = null;
    if (string.IsNullOrWhiteSpace(month))
    {
        key = WorkTimeLogPaths.CurrentMonthKey();
        return true;
    }

    month = month.Trim();
    if (!WorkTimeLogPaths.IsValidMonthKey(month))
    {
        key = string.Empty;
        badRequest = Results.BadRequest("Invalid month. Expected yyyy-mm with month 01-12.");
        return false;
    }

    key = month;
    return true;
}

static bool TryResolveWeek(string? week, out string key, out int isoYear, out int isoWeek, out IResult? badRequest)
{
    badRequest = null;
    if (string.IsNullOrWhiteSpace(week))
    {
        key = WorkTimeLogPaths.CurrentWeekKey();
        WorkTimeLogPaths.TryParseWeekKey(key, out isoYear, out isoWeek);
        return true;
    }

    week = week.Trim();
    if (!WorkTimeLogPaths.TryParseWeekKey(week, out isoYear, out isoWeek))
    {
        key = string.Empty;
        badRequest = Results.BadRequest("Invalid week. Expected yyyy-Www (ISO 8601 week, e.g. 2026-W17).");
        return false;
    }

    key = week;
    return true;
}

static bool TryResolveDate(string? date, out string key, out IResult? badRequest)
{
    badRequest = null;
    if (string.IsNullOrWhiteSpace(date))
    {
        key = WorkTimeLogPaths.CurrentDateKey();
        return true;
    }

    date = date.Trim();
    if (!WorkTimeLogPaths.IsValidDateKey(date))
    {
        key = string.Empty;
        badRequest = Results.BadRequest("Invalid date. Expected yyyy-mm-dd.");
        return false;
    }

    key = date;
    return true;
}
