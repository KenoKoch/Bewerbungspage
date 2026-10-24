using KenoKoch.BuildingBlocks.Modules;
using KenoKoch.Modules.Profile.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KenoKoch.Modules.Profile;

/// <summary>Modul "Profil": stellt Lebenslauf, Skills und Kontaktdaten bereit.</summary>
public sealed class ProfileModule : IModule
{
    public string Name => "Profile";

    public string DisplayName => "Profil";

    public string Description => "Lebenslauf, Skills und Kontaktdaten – als Datenquelle für die Seite und als JSON-API.";

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<ProfileStore>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/profile", Ok<ProfileData> (ProfileStore store) => TypedResults.Ok(store.Profile))
            .WithName("GetProfile")
            .WithSummary("Lebenslauf als JSON")
            .WithDescription("Liefert den kompletten Lebenslauf inklusive Stationen, Skills und Sprachen.");

        endpoints.MapGet("/profile/skills", Ok<IReadOnlyList<SkillGroup>> (ProfileStore store) => TypedResults.Ok(store.Profile.SkillGroups))
            .WithName("GetSkills")
            .WithSummary("Skills nach Kategorien");
    }
}
