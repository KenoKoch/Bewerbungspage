using System.Globalization;

namespace KenoKoch.UI.Formatting;

/// <summary>
/// Deutsche Formatierung ohne Abhängigkeit von ICU-Kulturdaten.
/// Dadurch kann der WebAssembly-Client im Invariant-Globalization-Modus laufen (kleinerer Download).
/// </summary>
public static class Fmt
{
    private static readonly NumberFormatInfo GermanNumbers = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        PercentDecimalSeparator = ",",
        PercentGroupSeparator = ".",
    };

    private static readonly string[] Months =
        ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

    private static readonly string[] WeekdaysShort = ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"];

    public static string Number(long value) => value.ToString("N0", GermanNumbers);

    public static string Number(double value, int decimals = 0) => value.ToString("N" + decimals, GermanNumbers);

    public static string Percent(double value, int decimals = 1) => value.ToString("N" + decimals, GermanNumbers) + " %";

    public static string Milliseconds(TimeSpan duration) => Milliseconds(duration.TotalMilliseconds);

    public static string Milliseconds(double milliseconds) => milliseconds switch
    {
        < 1 => milliseconds.ToString("0.00", GermanNumbers) + " ms",
        < 10 => milliseconds.ToString("0.0", GermanNumbers) + " ms",
        _ => milliseconds.ToString("N0", GermanNumbers) + " ms",
    };

    public static string Microseconds(double microseconds) => microseconds < 1000
        ? microseconds.ToString("N1", GermanNumbers) + " µs"
        : Milliseconds(microseconds / 1000d);

    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "N0" : "N1", GermanNumbers) + " " + units[unit];
    }

    public static string Uptime(TimeSpan duration) => duration switch
    {
        { TotalMinutes: < 1 } => $"{(int)duration.TotalSeconds} s",
        { TotalHours: < 1 } => $"{(int)duration.TotalMinutes} min {duration.Seconds} s",
        { TotalDays: < 1 } => $"{(int)duration.TotalHours} h {duration.Minutes} min",
        _ => $"{(int)duration.TotalDays} T {duration.Hours} h",
    };

    public static string Relative(DateTimeOffset when, DateTimeOffset now)
    {
        var delta = now - when;
        return delta switch
        {
            { TotalSeconds: < 10 } => "gerade eben",
            { TotalSeconds: < 60 } => $"vor {(int)delta.TotalSeconds} s",
            { TotalMinutes: < 60 } => $"vor {(int)delta.TotalMinutes} min",
            { TotalHours: < 24 } => $"vor {(int)delta.TotalHours} h",
            { TotalDays: < 2 } => "gestern",
            { TotalDays: < 30 } => $"vor {(int)delta.TotalDays} Tagen",
            _ => Date(when),
        };
    }

    public static string Date(DateTimeOffset value) => value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string DateLong(DateTimeOffset value) => $"{value.Day}. {Months[value.Month - 1]} {value.Year}";

    public static string DateTime(DateTimeOffset value) => value.ToString("dd.MM.yyyy, HH:mm", CultureInfo.InvariantCulture) + " Uhr";

    public static string Time(DateTimeOffset value, bool seconds = true) =>
        value.ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);

    public static string TimePrecise(DateTimeOffset value) => value.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public static string DayShort(DateOnly value) => $"{WeekdaysShort[(int)value.DayOfWeek]} {value.Day:00}.{value.Month:00}.";
}
