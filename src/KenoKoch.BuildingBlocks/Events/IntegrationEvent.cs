namespace KenoKoch.BuildingBlocks.Events;

/// <summary>Ereignis, über das Module lose gekoppelt miteinander kommunizieren.</summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}

/// <summary>Basis für Integration Events als unveränderliche Records.</summary>
public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Veröffentlicht Integration Events, ohne die Empfänger zu kennen.</summary>
public interface IEventBus
{
    ValueTask PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}

/// <summary>Verarbeitet ein Integration Event in einem eigenen DI-Scope.</summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
