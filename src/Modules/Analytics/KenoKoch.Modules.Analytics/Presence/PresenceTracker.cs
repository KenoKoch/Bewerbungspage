using System.Collections.Concurrent;
using KenoKoch.BuildingBlocks.Time;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace KenoKoch.Modules.Analytics.Presence;

/// <summary>
/// Zählt aktive Blazor-Server-Verbindungen (Circuits). Jede offene Seite mit interaktiven Komponenten
/// hält genau einen Circuit über SignalR – ein ehrlicher "gerade online"-Zähler ganz ohne Tracking.
/// </summary>
public sealed class PresenceTracker(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _connections = new();
    private readonly Lock _peakGate = new();
    private DateOnly _peakDay;
    private int _peakToday;

    public int OnlineCount => _connections.Count;

    public int PeakToday
    {
        get
        {
            lock (_peakGate)
            {
                return _peakDay == GermanTime.Today(timeProvider) ? Math.Max(_peakToday, OnlineCount) : OnlineCount;
            }
        }
    }

    /// <summary>Wird bei jeder Änderung ausgelöst – Abonnenten müssen selbst auf ihren Renderer-Kontext wechseln.</summary>
    public event Action? Changed;

    internal void Connected(string connectionId)
    {
        _connections[connectionId] = timeProvider.GetUtcNow();
        UpdatePeak();
        RaiseChanged();
    }

    internal void Disconnected(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out _))
        {
            RaiseChanged();
        }
    }

    private void UpdatePeak()
    {
        lock (_peakGate)
        {
            var today = GermanTime.Today(timeProvider);
            if (_peakDay != today)
            {
                _peakDay = today;
                _peakToday = 0;
            }

            _peakToday = Math.Max(_peakToday, OnlineCount);
        }
    }

    private void RaiseChanged()
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action)handler)();
            }
            catch
            {
                // Ein fehlerhafter Abonnent darf die anderen nicht blockieren.
            }
        }
    }
}

/// <summary>Meldet Auf- und Abbau von Blazor-Circuits an den <see cref="PresenceTracker"/>.</summary>
internal sealed class PresenceCircuitHandler(PresenceTracker tracker) : CircuitHandler
{
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        tracker.Connected(circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        tracker.Disconnected(circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        tracker.Disconnected(circuit.Id);
        return Task.CompletedTask;
    }
}
