using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KenoKoch.BuildingBlocks.Events;

/// <summary>Begrenzter Puffer zwischen Publishern und Dispatcher.</summary>
internal sealed class IntegrationEventChannel
{
    public Channel<IIntegrationEvent> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<IIntegrationEvent>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
}

/// <summary>
/// In-Process-Event-Bus: Publisher schreiben in einen Channel und kehren sofort zurück,
/// ein Hintergrunddienst verteilt die Events an alle registrierten Handler.
/// </summary>
internal sealed class InMemoryEventBus(IntegrationEventChannel channel, ILogger<InMemoryEventBus> logger) : IEventBus
{
    public ValueTask PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!channel.Channel.Writer.TryWrite(integrationEvent))
        {
            logger.LogWarning("Integration Event {EventType} konnte nicht eingereiht werden.", typeof(TEvent).Name);
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>Liest Events aus dem Channel und ruft die Handler jeweils in einem eigenen DI-Scope auf.</summary>
internal sealed class IntegrationEventDispatcher(
    IntegrationEventChannel channel,
    IServiceScopeFactory scopeFactory,
    ILogger<IntegrationEventDispatcher> logger) : BackgroundService
{
    private delegate Task Dispatch(IServiceProvider services, IIntegrationEvent integrationEvent, ILogger logger, CancellationToken cancellationToken);

    private static readonly MethodInfo DispatchMethod = typeof(IntegrationEventDispatcher)
        .GetMethod(nameof(DispatchAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<Type, Dispatch> Dispatchers = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var integrationEvent in channel.Channel.Reader.ReadAllAsync(stoppingToken))
            {
                var dispatch = Dispatchers.GetOrAdd(
                    integrationEvent.GetType(),
                    static type => DispatchMethod.MakeGenericMethod(type).CreateDelegate<Dispatch>());

                await using var scope = scopeFactory.CreateAsyncScope();
                await dispatch(scope.ServiceProvider, integrationEvent, logger, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Regulärer Shutdown.
        }
    }

    private static async Task DispatchAsync<TEvent>(
        IServiceProvider services,
        IIntegrationEvent integrationEvent,
        ILogger logger,
        CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        foreach (var handler in services.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            try
            {
                await handler.HandleAsync((TEvent)integrationEvent, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Ein fehlerhafter Handler darf weder andere Handler noch den Publisher beeinträchtigen.
                logger.LogError(ex, "Handler {Handler} ist bei {EventType} fehlgeschlagen.", handler.GetType().Name, typeof(TEvent).Name);
            }
        }
    }
}
