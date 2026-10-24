using KenoKoch.BuildingBlocks.Modules;
using KenoKoch.BuildingBlocks.Persistence;
using Microsoft.AspNetCore.Components;

namespace KenoKoch.Web.Hosting;

public sealed record ModuleEndpointInfo(string Method, string Route, string? Summary);

public sealed record ModuleInfo(
    string Name,
    string DisplayName,
    string Description,
    string Assembly,
    string Version,
    string? DatabaseSchema,
    IReadOnlyList<string> Migrations,
    IReadOnlyList<string> PublishedEvents,
    IReadOnlyList<string> HandledEvents,
    IReadOnlyList<ModuleEndpointInfo> Endpoints,
    int ComponentCount,
    IReadOnlyList<string> Dependencies);

/// <summary>Liest zur Laufzeit aus, wie der modulare Monolith zusammengesetzt ist (Modul-Explorer und /api/modules).</summary>
public sealed class ModuleInfoProvider(ModuleCatalog catalog, EndpointDataSource endpointDataSource, IEnumerable<IModuleDatabase> databases)
{
    private IReadOnlyList<ModuleInfo>? _modules;

    public IReadOnlyList<ModuleInfo> GetModules() => _modules ??= Build();

    private IReadOnlyList<ModuleInfo> Build()
    {
        var endpoints = endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Endpoint: e, Module: e.Metadata.GetMetadata<ModuleEndpointMetadata>()?.ModuleName))
            .Where(e => e.Module is not null)
            .ToArray();

        var databaseList = databases.ToArray();

        return catalog.Modules.Select(module =>
        {
            var moduleEndpoints = endpoints
                .Where(e => e.Module == module.Name)
                .Select(e => new ModuleEndpointInfo(
                    string.Join(", ", e.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]),
                    "/" + (e.Endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'),
                    e.Endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IEndpointSummaryMetadata>()?.Summary))
                .OrderBy(e => e.Route, StringComparer.Ordinal)
                .ToArray();

            var migrations = module.DatabaseSchema is null
                ? []
                : databaseList.FirstOrDefault(d => d.Schema == module.DatabaseSchema)?.DefinedMigrations ?? [];

            var componentCount = module.Assembly.GetTypes()
                .Count(t => typeof(IComponent).IsAssignableFrom(t) && t is { IsAbstract: false, IsPublic: true });

            var dependencies = module.Assembly.GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .Where(name => name.StartsWith("KenoKoch.", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray();

            return new ModuleInfo(
                module.Name,
                module.DisplayName,
                module.Description,
                module.AssemblyName,
                module.Version,
                module.DatabaseSchema,
                migrations.Select(FormatMigration).ToArray(),
                module.PublishedEvents.Select(t => t.Name).ToArray(),
                module.HandledEvents.Select(t => t.Name).ToArray(),
                moduleEndpoints,
                componentCount,
                dependencies);
        }).ToArray();
    }

    // "20261024120000_InitialCreate" → "InitialCreate (24.10.2026)"
    private static string FormatMigration(string id)
    {
        var underscore = id.IndexOf('_', StringComparison.Ordinal);
        if (underscore != 14 || !DateTime.TryParseExact(id[..14], "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var date))
        {
            return id;
        }

        return $"{id[(underscore + 1)..]} ({date:dd.MM.yyyy})";
    }
}
