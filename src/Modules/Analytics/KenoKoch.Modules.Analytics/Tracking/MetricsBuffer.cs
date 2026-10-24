using System.Collections.Concurrent;
using KenoKoch.BuildingBlocks.Time;

namespace KenoKoch.Modules.Analytics.Tracking;

public static class MetricNames
{
    public const string PagePrefix = "page:";
    public const string PlcStarted = "event:plc-started";
    public const string PlcAlarm = "event:plc-alarm";
    public const string GuestbookEntry = "event:guestbook-entry";
    public const string Reaction = "event:reaction";

    public static string Page(string path) => PagePrefix + path;

    public static bool IsPage(string metric) => metric.StartsWith(PagePrefix, StringComparison.Ordinal);
}

public readonly record struct MetricKey(DateOnly Date, string Metric);

/// <summary>
/// Zählt im Arbeitsspeicher und schreibt gebündelt in die Datenbank (siehe <see cref="MetricsFlushService"/>).
/// Kein Request wartet je auf SQL – wichtig für Antwortzeiten und für Azure SQL Serverless.
/// </summary>
public sealed class MetricsBuffer(TimeProvider timeProvider)
{
    private const int MaxPendingKeys = 10_000;
    private readonly ConcurrentDictionary<MetricKey, int> _pending = new();

    public void Increment(string metric, int amount = 1)
    {
        if (_pending.Count >= MaxPendingKeys)
        {
            return;
        }

        var key = new MetricKey(GermanTime.Today(timeProvider), metric);
        _pending.AddOrUpdate(key, amount, (_, current) => current + amount);
    }

    /// <summary>Entnimmt alle offenen Zählerstände atomar pro Schlüssel.</summary>
    public IReadOnlyList<KeyValuePair<MetricKey, int>> Drain()
    {
        var drained = new List<KeyValuePair<MetricKey, int>>();
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var count))
            {
                drained.Add(new KeyValuePair<MetricKey, int>(key, count));
            }
        }

        return drained;
    }

    /// <summary>Legt nicht gespeicherte Werte zurück (z. B. wenn die Datenbank gerade pausiert).</summary>
    public void Restore(IEnumerable<KeyValuePair<MetricKey, int>> items)
    {
        foreach (var (key, count) in items)
        {
            _pending.AddOrUpdate(key, count, (_, current) => current + count);
        }
    }

    public IReadOnlyList<KeyValuePair<MetricKey, int>> Snapshot() => _pending.ToArray();
}
