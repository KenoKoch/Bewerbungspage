using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KenoKoch.BuildingBlocks.Modules;

/// <summary>
/// Vertrag eines fachlichen Moduls im modularen Monolithen.
/// Ein Modul kapselt Domäne, Persistenz (eigenes Datenbankschema), API-Endpunkte und UI-Komponenten
/// und meldet sich ausschließlich über diese Schnittstelle beim Host an.
/// </summary>
public interface IModule
{
    /// <summary>Technischer, eindeutiger Name (z. B. "Guestbook").</summary>
    string Name { get; }

    /// <summary>Anzeigename (z. B. "Gästebuch").</summary>
    string DisplayName { get; }

    /// <summary>Fachliche Verantwortung des Moduls in einem Satz.</summary>
    string Description { get; }

    /// <summary>Eigenes Datenbankschema, falls das Modul Daten speichert.</summary>
    string? DatabaseSchema => null;

    /// <summary>Integration Events, die dieses Modul veröffentlicht.</summary>
    IReadOnlyCollection<Type> PublishedEvents => [];

    /// <summary>Registriert die Dienste des Moduls im DI-Container.</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Hängt optionale Middleware des Moduls in die HTTP-Pipeline ein.</summary>
    void ConfigurePipeline(IApplicationBuilder app)
    {
    }

    /// <summary>Registriert HTTP-Endpunkte des Moduls (Präfix <c>/api</c> wird vom Host gesetzt).</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
