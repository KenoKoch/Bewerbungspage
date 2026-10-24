using KenoKoch.BuildingBlocks.Events;
using KenoKoch.BuildingBlocks.Modules;
using KenoKoch.BuildingBlocks.Persistence;
using KenoKoch.Modules.Analytics.Activity;
using KenoKoch.Modules.Analytics.Persistence;
using KenoKoch.Modules.Analytics.Presence;
using KenoKoch.Modules.Analytics.Stats;
using KenoKoch.Modules.Analytics.Tracking;
using KenoKoch.Modules.Guestbook.Contracts;
using KenoKoch.Modules.Plc.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KenoKoch.Modules.Analytics;

/// <summary>Modul "Besucher &amp; Echtzeit": Live-Besucher, Tagesstatistik und Aktivitäts-Feed.</summary>
public sealed class AnalyticsModule : IModule
{
    public string Name => "Analytics";

    public string DisplayName => "Besucher & Echtzeit";

    public string Description => "Live-Besucherzähler über Blazor-Circuits, datensparsame Tagesstatistik ohne Cookies und ein Echtzeit-Aktivitätsfeed.";

    public string? DatabaseSchema => AnalyticsDbContext.SchemaName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AnalyticsDbContext>(configuration, AnalyticsDbContext.SchemaName);

        services.AddSingleton<MetricsBuffer>();
        services.AddSingleton<PresenceTracker>();
        services.AddSingleton<ActivityFeed>();
        services.AddSingleton<StatsService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, PresenceCircuitHandler>());
        services.AddHostedService<MetricsFlushService>();

        services.AddIntegrationEventHandler<GuestbookEntrySubmitted, GuestbookEntrySubmittedHandler>();
        services.AddIntegrationEventHandler<PlcStarted, PlcStartedHandler>();
        services.AddIntegrationEventHandler<PlcAlarmRaised, PlcAlarmRaisedHandler>();
    }

    public void ConfigurePipeline(IApplicationBuilder app) => app.UseMiddleware<PageViewMiddleware>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/stats", GetStatsAsync)
            .WithName("GetStats")
            .WithSummary("Live-Besucherstatistik")
            .WithDescription("Besucher online (Blazor-Circuits), Seitenaufrufe heute und gesamt, Verlauf der letzten 14 Tage.");
    }

    private static async Task<Ok<StatsApiResponse>> GetStatsAsync(
        StatsService stats,
        PresenceTracker presence,
        DatabaseState database,
        CancellationToken cancellationToken)
    {
        VisitorStatistics? statistics = null;
        if (database.IsReady)
        {
            try
            {
                statistics = await stats.GetAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Live-Werte trotzdem ausliefern – die Datenbank ist nur ein Teil der Antwort.
            }
        }

        return TypedResults.Ok(new StatsApiResponse(
            OnlineNow: presence.OnlineCount,
            PeakToday: presence.PeakToday,
            PageViewsToday: statistics?.PageViewsToday,
            PageViewsTotal: statistics?.TotalPageViews,
            Last14Days: statistics?.Last14Days,
            TopPages: statistics?.TopPages,
            PlcStarts: statistics?.PlcStarts,
            GuestbookEntries: statistics?.GuestbookEntries,
            Database: database.Status.ToString(),
            GeneratedAt: DateTimeOffset.UtcNow));
    }
}

public sealed record StatsApiResponse(
    int OnlineNow,
    int PeakToday,
    int? PageViewsToday,
    long? PageViewsTotal,
    IReadOnlyList<DailyCount>? Last14Days,
    IReadOnlyList<PageCount>? TopPages,
    long? PlcStarts,
    long? GuestbookEntries,
    string Database,
    DateTimeOffset GeneratedAt);
