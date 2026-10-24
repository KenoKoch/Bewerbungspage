using KenoKoch.BuildingBlocks.Events;
using KenoKoch.BuildingBlocks.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KenoKoch.BuildingBlocks.Modules;

public static class ModuleHostingExtensions
{
    /// <summary>
    /// Registriert die Module samt gemeinsamer Infrastruktur (Event-Bus, Persistenz).
    /// Die Reihenfolge der Module bestimmt die Reihenfolge von Middleware und Migrationen.
    /// </summary>
    public static WebApplicationBuilder AddModules(this WebApplicationBuilder builder, params IModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var catalog = new ModuleCatalog();
        builder.Services.AddSingleton(catalog);
        builder.Services.AddIntegrationEvents();
        builder.Services.AddModulePersistence(builder.Configuration);

        foreach (var module in modules)
        {
            var registrationsBefore = builder.Services.Count;
            module.AddServices(builder.Services, builder.Configuration);

            var handledEvents = builder.Services
                .Skip(registrationsBefore)
                .Where(d => d.ServiceType.IsGenericType &&
                            d.ServiceType.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))
                .Select(d => d.ServiceType.GetGenericArguments()[0])
                .Distinct()
                .ToArray();

            catalog.Add(new ModuleDescriptor(module, handledEvents));
        }

        return builder;
    }

    /// <summary>Lässt jedes Modul seine Middleware in die Pipeline einhängen.</summary>
    public static IApplicationBuilder UseModules(this IApplicationBuilder app)
    {
        foreach (var descriptor in app.ApplicationServices.GetRequiredService<ModuleCatalog>().Modules)
        {
            descriptor.Module.ConfigurePipeline(app);
        }

        return app;
    }

    /// <summary>
    /// Mappt die Endpunkte aller Module unter <c>/api</c>. Jede Gruppe erhält ein OpenAPI-Tag
    /// und <see cref="ModuleEndpointMetadata"/>, damit Endpunkte zur Laufzeit ihrem Modul zugeordnet werden können.
    /// </summary>
    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder endpoints, Action<RouteGroupBuilder>? configureGroup = null)
    {
        var catalog = endpoints.ServiceProvider.GetRequiredService<ModuleCatalog>();

        foreach (var descriptor in catalog.Modules)
        {
            var group = endpoints.MapGroup("/api")
                .WithTags(descriptor.DisplayName)
                .WithMetadata(new ModuleEndpointMetadata(descriptor.Name));

            configureGroup?.Invoke(group);
            descriptor.Module.MapEndpoints(group);
        }

        return endpoints;
    }
}
