using KenoKoch.BuildingBlocks.Persistence;
using KenoKoch.Modules.Analytics.Persistence;
using KenoKoch.Modules.Analytics.Stats;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KenoKoch.Modules.Analytics.Tracking;

/// <summary>Schreibt die gepufferten Zählerstände alle 20 Sekunden gebündelt nach SQL (Upsert per ExecuteUpdate).</summary>
internal sealed class MetricsFlushService(
    MetricsBuffer buffer,
    IDbContextFactory<AnalyticsDbContext> dbFactory,
    DatabaseState database,
    StatsService stats,
    ILogger<MetricsFlushService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Regulärer Shutdown.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // Letzte Werte beim Herunterfahren (z. B. Deployment) nicht verlieren.
        await FlushAsync(cancellationToken);
    }

    internal async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!database.IsReady)
        {
            return;
        }

        var items = buffer.Drain();
        if (items.Count == 0)
        {
            return;
        }

        var processed = 0;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            foreach (var (key, count) in items)
            {
                await UpsertAsync(db, key, count, cancellationToken);
                processed++;
            }

            stats.Invalidate();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            buffer.Restore(items.Skip(processed));
            logger.LogWarning(ex, "Statistik konnte nicht gespeichert werden – {Count} Werte bleiben im Puffer.", items.Count - processed);
        }
    }

    private static async Task UpsertAsync(AnalyticsDbContext db, MetricKey key, int count, CancellationToken cancellationToken)
    {
        var updated = await db.DailyMetrics
            .Where(m => m.Date == key.Date && m.Metric == key.Metric)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Count, m => m.Count + count), cancellationToken);

        if (updated > 0)
        {
            return;
        }

        db.DailyMetrics.Add(new DailyMetric { Date = key.Date, Metric = key.Metric, Count = count });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Parallel von einer anderen Instanz angelegt (Scale-out) – dann eben addieren.
            db.ChangeTracker.Clear();
            await db.DailyMetrics
                .Where(m => m.Date == key.Date && m.Metric == key.Metric)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Count, m => m.Count + count), cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
