using KenoKoch.BuildingBlocks.Modules;
using KenoKoch.Modules.Plc.Contracts;
using KenoKoch.Modules.Plc.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KenoKoch.Modules.Plc;

/// <summary>Modul "SPS-Simulator": eine Soft-SPS pro Besucher mit Tankanlage und WinCC-ähnlichem HMI.</summary>
public sealed class PlcModule : IModule
{
    public string Name => "Plc";

    public string DisplayName => "SPS-Simulator";

    public string Description => "Soft-SPS mit zyklischem Scan (100 ms), Funktionsbaustein nach SCL-Vorbild in C# und HMI mit Meldesystem.";

    public IReadOnlyCollection<Type> PublishedEvents => [typeof(PlcStarted), typeof(PlcAlarmRaised)];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PlcOptions>().Bind(configuration.GetSection(PlcOptions.SectionName));
        services.AddSingleton<PlcSessionRegistry>();
        services.AddSingleton<PlcRuntimeFactory>();
        services.AddSingleton<PlcSourceCode>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/plc/status", Ok<PlcStatusResponse> (PlcSessionRegistry registry) =>
                TypedResults.Ok(new PlcStatusResponse(registry.ActiveSessions, registry.MaxSessions, PlcRuntime.CycleTime.TotalMilliseconds)))
            .WithName("GetPlcStatus")
            .WithSummary("Laufende Besucher-SPSen");

        endpoints.MapGet("/plc/program", (PlcSourceCode source) => TypedResults.Text(source.Scl, "text/plain; charset=utf-8"))
            .WithName("GetPlcProgram")
            .WithSummary("SCL-Quelltext des Funktionsbausteins FB_TankControl");
    }
}

public sealed record PlcStatusResponse(int ActiveSessions, int MaxSessions, double CycleTimeMilliseconds);
