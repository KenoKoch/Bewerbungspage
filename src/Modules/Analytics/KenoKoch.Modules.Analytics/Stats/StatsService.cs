using System.Diagnostics;
using KenoKoch.BuildingBlocks.Persistence;
using KenoKoch.BuildingBlocks.Time;
using KenoKoch.Modules.Analytics.Persistence;
using KenoKoch.Modules.Analytics.Tracking;
using Microsoft.EntityFrameworkCore;

namespace KenoKoch.Modules.Analytics.Stats;

public sealed record DailyCount(DateOnly Date, int Count);

public sealed record PageCount(string Path, long Count);

public sealed record VisitorStatistics(
    long TotalPageViews,
    int PageViewsToday,
    IReadOnlyList<DailyCount> Last14Days,
    IReadOnlyList<PageCount> TopPages,
    long PlcStarts,
    long PlcAlarms,
    long GuestbookEntries,
    long Reactions,
    TimeSpan QueryDuration,
    string QuerySql,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Liefert die Besucherstatistik: Werte aus SQL (30 Sekunden gecacht) plus noch nicht geschriebene Werte aus dem Puffer.
/// So sieht jeder Besucher seinen eigenen Aufruf sofort, obwohl nur alle 20 Sekunden geschrieben wird.
/// </summary>
public sealed class StatsService(
    IDbContextFactory<AnalyticsDbContext> dbFactory,
    MetricsBuffer buffer,
    DatabaseState database,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile DatabaseSnapshot? _cache;

    public void Invalidate() => _cache = null;

    public async Task<VisitorStatistics?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!database.IsReady)
        {
            return null;
        }

        var snapshot = await LoadAsync(cancellationToken);
        return Merge(snapshot);
    }

    private async Task<DatabaseSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var cached = _cache;
        if (cached is not null && timeProvider.GetUtcNow() - cached.LoadedAt < CacheDuration)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            cached = _cache;
            if (cached is not null && timeProvider.GetUtcNow() - cached.LoadedAt < CacheDuration)
            {
                return cached;
            }

            var from = GermanTime.Today(timeProvider).AddDays(-13);
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var dailyQuery = db.DailyMetrics
                .AsNoTracking()
                .Where(m => m.Date >= from && m.Metric.StartsWith(MetricNames.PagePrefix))
                .GroupBy(m => m.Date)
                .Select(g => new { Date = g.Key, Views = g.Sum(m => m.Count) })
                .OrderBy(x => x.Date);

            var totalsQuery = db.DailyMetrics
                .AsNoTracking()
                .GroupBy(m => m.Metric)
                .Select(g => new { Metric = g.Key, Total = g.Sum(m => (long)m.Count) });

            var sql = dailyQuery.ToQueryString();
            var stopwatch = Stopwatch.StartNew();
            var daily = await dailyQuery.ToListAsync(cancellationToken);
            var totals = await totalsQuery.ToListAsync(cancellationToken);
            stopwatch.Stop();

            var snapshot = new DatabaseSnapshot(
                totals.ToDictionary(t => t.Metric, t => t.Total, StringComparer.Ordinal),
                daily.ToDictionary(d => d.Date, d => d.Views),
                from,
                stopwatch.Elapsed,
                sql,
                timeProvider.GetUtcNow());

            _cache = snapshot;
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private VisitorStatistics Merge(DatabaseSnapshot snapshot)
    {
        var totals = new Dictionary<string, long>(snapshot.Totals, StringComparer.Ordinal);
        var daily = new Dictionary<DateOnly, int>(snapshot.Daily);

        foreach (var (key, count) in buffer.Snapshot())
        {
            totals[key.Metric] = totals.GetValueOrDefault(key.Metric) + count;
            if (MetricNames.IsPage(key.Metric) && key.Date >= snapshot.From)
            {
                daily[key.Date] = daily.GetValueOrDefault(key.Date) + count;
            }
        }

        var today = GermanTime.Today(timeProvider);
        var last14Days = Enumerable.Range(0, 14)
            .Select(offset => today.AddDays(offset - 13))
            .Select(date => new DailyCount(date, daily.GetValueOrDefault(date)))
            .ToArray();

        var pages = totals.Where(t => MetricNames.IsPage(t.Key)).ToArray();

        return new VisitorStatistics(
            TotalPageViews: pages.Sum(p => p.Value),
            PageViewsToday: daily.GetValueOrDefault(today),
            Last14Days: last14Days,
            TopPages: pages
                .OrderByDescending(p => p.Value)
                .Take(5)
                .Select(p => new PageCount(p.Key[MetricNames.PagePrefix.Length..], p.Value))
                .ToArray(),
            PlcStarts: totals.GetValueOrDefault(MetricNames.PlcStarted),
            PlcAlarms: totals.GetValueOrDefault(MetricNames.PlcAlarm),
            GuestbookEntries: totals.GetValueOrDefault(MetricNames.GuestbookEntry),
            Reactions: totals.GetValueOrDefault(MetricNames.Reaction),
            QueryDuration: snapshot.Duration,
            QuerySql: snapshot.Sql,
            GeneratedAt: snapshot.LoadedAt);
    }

    private sealed record DatabaseSnapshot(
        IReadOnlyDictionary<string, long> Totals,
        IReadOnlyDictionary<DateOnly, int> Daily,
        DateOnly From,
        TimeSpan Duration,
        string Sql,
        DateTimeOffset LoadedAt);
}
