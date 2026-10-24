namespace KenoKoch.BuildingBlocks.Time;

/// <summary>Hilfen für deutsche Ortszeit (Tagesgrenzen der Statistik, Anzeige).</summary>
public static class GermanTime
{
    public static TimeZoneInfo Zone { get; } = ResolveZone();

    public static DateTimeOffset Now(TimeProvider timeProvider) => ToGerman(timeProvider.GetUtcNow());

    public static DateTimeOffset ToGerman(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Zone);

    public static DateOnly Today(TimeProvider timeProvider) => DateOnly.FromDateTime(Now(timeProvider).DateTime);

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Europe/Berlin", "W. Europe Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.Utc;
    }
}
