using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace KenoKoch.Web.Hosting;

public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>Öffentliche Basis-URL (für Canonical-Links, Open Graph und Sitemap).</summary>
    public string BaseUrl { get; set; } = "https://kenokoch.de";

    /// <summary>Optional: Host, auf den "www." umgeleitet wird (z. B. "kenokoch.de").</summary>
    public string? CanonicalHost { get; set; }
}

/// <summary>Querschnittliche Plattformdienste des Hosts – unabhängig von den fachlichen Modulen.</summary>
public static class PlatformExtensions
{
    public const string ApiRateLimitPolicy = "api";

    private static readonly JsonSerializerOptions HealthJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static WebApplicationBuilder AddPlatform(this WebApplicationBuilder builder)
    {
        // Deutsche Zahlen- und Datumsformate für Server-Rendering und Blazor-Circuits.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

        var services = builder.Services;
        services.AddOptions<SiteOptions>().Bind(builder.Configuration.GetSection(SiteOptions.SectionName));
        services.AddProblemDetails();
        services.AddHealthChecks();
        services.AddSingleton<RuntimeInfoService>();
        services.AddSingleton<ModuleInfoProvider>();

        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "KenoKoch.de API",
                Version = "v1",
                Description = "Öffentliche Endpunkte der Bewerbungsseite von Keno Koch – ASP.NET Core Minimal APIs. " +
                              "Jedes Modul des modularen Monolithen bringt seine eigenen Endpunkte mit.",
            };
            return Task.CompletedTask;
        }));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(ApiRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        // Application Insights über OpenTelemetry – nur, wenn eine Verbindungszeichenfolge gesetzt ist.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            services.AddOpenTelemetry().UseAzureMonitor();
        }

        return builder;
    }

    public static WebApplication MapPlatformEndpoints(this WebApplication app)
    {
        // Liveness ohne Datenbank: für den App-Service-Health-Check, weckt keine Serverless-Datenbank.
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });

        // Readiness inklusive Datenbank und Migrationsstand jedes Moduls.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = WriteHealthJsonAsync,
        });

        app.MapOpenApi();
        app.MapScalarApiReference(options => options
            .WithTitle("KenoKoch.de API")
            .WithTheme(ScalarTheme.Purple));

        var platform = app.MapGroup("/api")
            .WithTags("Plattform")
            .RequireRateLimiting(ApiRateLimitPolicy);

        platform.MapGet("/modules", Ok<IReadOnlyList<ModuleInfo>> (ModuleInfoProvider modules) => TypedResults.Ok(modules.GetModules()))
            .WithName("GetModules")
            .WithSummary("Module des modularen Monolithen")
            .WithDescription("Alle registrierten Module mit Datenbankschema, Migrationen, Events und Endpunkten – ermittelt zur Laufzeit.");

        platform.MapGet("/runtime", async Task<Ok<RuntimeSnapshot>> (RuntimeInfoService runtime, CancellationToken cancellationToken) =>
                TypedResults.Ok(await runtime.GetSnapshotAsync(includeDatabase: true, cancellationToken)))
            .WithName("GetRuntime")
            .WithSummary("Laufzeit- und Hosting-Informationen")
            .WithDescription("Azure-Region, .NET-Version, Speicher, Datenbank-Latenz und Build-Informationen.");

        return app;
    }

    private static Task WriteHealthJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            totalMilliseconds = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    milliseconds = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, HealthJson));
    }
}
