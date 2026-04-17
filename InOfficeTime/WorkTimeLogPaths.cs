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
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out _);

    public static string CurrentMonthKey() =>
        DateTime.Now.ToString("yyyy-MM");

    public static string CurrentDateKey() =>
        DateTime.Now.ToString("yyyy-MM-dd");

    public static string MonthKeyFromDateKey(string dateKey) =>
        dateKey[..7];
}
