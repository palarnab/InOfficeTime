using System.Globalization;

namespace InOfficeTime;

public sealed class WorkTimeLogPaths
{
    private readonly string _root;

    public WorkTimeLogPaths()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "InOfficeTime");
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public string GetFilePathForMonth(string yyyyMm) =>
        Path.Combine(_root, $"{yyyyMm}.txt");

    public static bool IsValidMonthKey(string value) =>
        value.Length == 7
        && value[4] == '-'
        && int.TryParse(value.AsSpan(0, 4), out var y) && y is >= 2000 and <= 2100
        && int.TryParse(value.AsSpan(5, 2), out var m) && m is >= 1 and <= 12;

    public static bool IsValidDateKey(string value) =>
        value.Length == 10
        && value[4] == '-'
        && value[7] == '-'
        && DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);

    public static bool IsValidWeekKey(string value) =>
        TryParseWeekKey(value, out _, out _);

    public static bool TryParseWeekKey(string value, out int isoYear, out int isoWeek)
    {
        isoYear = 0;
        isoWeek = 0;
        if (string.IsNullOrEmpty(value) || value.Length != 8 || value[4] != '-' || value[5] != 'W')
            return false;

        if (!int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            || y is < 2000 or > 2100)
            return false;

        if (!int.TryParse(value.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var w)
            || w is < 1 or > 53)
            return false;

        if (w > ISOWeek.GetWeeksInYear(y))
            return false;

        isoYear = y;
        isoWeek = w;
        return true;
    }

    public static string CurrentMonthKey() =>
        DateTime.Now.ToString("yyyy-MM");

    public static string CurrentDateKey() =>
        DateTime.Now.ToString("yyyy-MM-dd");

    public static string CurrentWeekKey() =>
        FormatWeekKey(ISOWeek.GetYear(DateTime.Now), ISOWeek.GetWeekOfYear(DateTime.Now));

    public static string MonthKeyFromDateKey(string dateKey) =>
        dateKey[..7];

    public static string FormatWeekKey(int isoYear, int isoWeek) =>
        string.Create(CultureInfo.InvariantCulture, $"{isoYear:D4}-W{isoWeek:D2}");
}
