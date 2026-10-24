using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KenoKoch.BuildingBlocks.Events;

public static class EventServiceCollectionExtensions
{
    public static IServiceCollection AddIntegrationEvents(this IServiceCollection services)
    {
        services.TryAddSingleton<IntegrationEventChannel>();
        services.TryAddSingleton<IEventBus, InMemoryEventBus>();
        services.AddHostedService<IntegrationEventDispatcher>();
        return services;
    }

    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        return services;
    }
}
