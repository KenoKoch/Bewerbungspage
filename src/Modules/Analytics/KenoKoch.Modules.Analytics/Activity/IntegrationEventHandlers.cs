using KenoKoch.BuildingBlocks.Events;
using KenoKoch.Modules.Analytics.Tracking;
using KenoKoch.Modules.Guestbook.Contracts;
using KenoKoch.Modules.Plc.Contracts;

namespace KenoKoch.Modules.Analytics.Activity;

/// <summary>Reagiert auf Events des Gästebuch-Moduls – ohne dessen Implementierung zu kennen.</summary>
internal sealed class GuestbookEntrySubmittedHandler(ActivityFeed feed, MetricsBuffer metrics)
    : IIntegrationEventHandler<GuestbookEntrySubmitted>
{
    public Task HandleAsync(GuestbookEntrySubmitted integrationEvent, CancellationToken cancellationToken)
    {
        metrics.Increment(MetricNames.GuestbookEntry);
        feed.Add(ActivityKind.Guestbook, "message", $"Neuer Gästebucheintrag ({integrationEvent.VisitorRole})");
        return Task.CompletedTask;
    }
}

/// <summary>Reagiert auf Events des SPS-Moduls.</summary>
internal sealed class PlcStartedHandler(ActivityFeed feed, MetricsBuffer metrics)
    : IIntegrationEventHandler<PlcStarted>
{
    public Task HandleAsync(PlcStarted integrationEvent, CancellationToken cancellationToken)
    {
        metrics.Increment(MetricNames.PlcStarted);
        feed.Add(ActivityKind.Plc, "cpu", $"SPS #{integrationEvent.SessionTag} wurde in RUN geschaltet");
        return Task.CompletedTask;
    }
}

internal sealed class PlcAlarmRaisedHandler(ActivityFeed feed, MetricsBuffer metrics)
    : IIntegrationEventHandler<PlcAlarmRaised>
{
    public Task HandleAsync(PlcAlarmRaised integrationEvent, CancellationToken cancellationToken)
    {
        metrics.Increment(MetricNames.PlcAlarm);
        feed.Add(ActivityKind.Alarm, "alert", $"Alarm in SPS #{integrationEvent.SessionTag}: {integrationEvent.AlarmText}");
        return Task.CompletedTask;
    }
}
