using KenoKoch.BuildingBlocks.Modules;
using KenoKoch.BuildingBlocks.Persistence;
using KenoKoch.Modules.Guestbook.Application;
using KenoKoch.Modules.Guestbook.Contracts;
using KenoKoch.Modules.Guestbook.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KenoKoch.Modules.Guestbook;

/// <summary>Modul "Gästebuch": Besucher hinterlassen Grüße, gespeichert mit EF Core im Schema [guestbook].</summary>
public sealed class GuestbookModule : IModule
{
    public string Name => "Guestbook";

    public string DisplayName => "Gästebuch";

    public string Description => "Einträge von Besuchern: Validierung, Spam-Schutz und Persistenz mit EF Core im eigenen Schema.";

    public string? DatabaseSchema => GuestbookDbContext.SchemaName;

    public IReadOnlyCollection<Type> PublishedEvents => [typeof(GuestbookEntrySubmitted)];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GuestbookOptions>().Bind(configuration.GetSection(GuestbookOptions.SectionName));
        services.AddModuleDbContext<GuestbookDbContext>(configuration, GuestbookDbContext.SchemaName);
        services.AddSingleton<SubmissionThrottle>();
        services.AddScoped<GuestbookService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/guestbook", GetEntriesAsync)
            .WithName("GetGuestbookEntries")
            .WithSummary("Freigegebene Gästebucheinträge")
            .WithDescription("Liefert die neuesten freigegebenen Einträge aus Azure SQL (Parameter take: 1–50).");
    }

    private static async Task<Results<Ok<GuestbookApiResponse>, ProblemHttpResult>> GetEntriesAsync(
        GuestbookService service,
        DatabaseState database,
        int? take,
        CancellationToken cancellationToken)
    {
        if (!database.IsReady)
        {
            return TypedResults.Problem(
                title: "Datenbank derzeit nicht verfügbar",
                detail: database.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var page = await service.GetLatestAsync(take ?? 10, cancellationToken);
        var entries = page.Entries
            .Select(e => new GuestbookApiEntry(e.Name, e.Company, e.RoleLabel, e.Message, e.CreatedAt))
            .ToArray();

        return TypedResults.Ok(new GuestbookApiResponse(page.TotalCount, entries, Math.Round(page.Query.Duration.TotalMilliseconds, 2)));
    }
}
