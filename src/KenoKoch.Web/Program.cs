using KenoKoch.BuildingBlocks.Modules;
using KenoKoch.Modules.Analytics;
using KenoKoch.Modules.Guestbook;
using KenoKoch.Modules.Plc;
using KenoKoch.Modules.Profile;
using KenoKoch.Web.Components;
using KenoKoch.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Plattform: Telemetrie, OpenAPI, Health, Rate Limiting
builder.AddPlatform();

// Modularer Monolith: jedes Modul bringt Dienste,
// eigenes DB-Schema, Endpunkte und UI-Komponenten mit.
builder.AddModules(
    new ProfileModule(),
    new GuestbookModule(),
    new AnalyticsModule(),
    new PlcModule());

// Blazor: statisches SSR + interaktive Inseln
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseCanonicalHost();
app.UseHttpsRedirection();
app.UseSecurityHeaders();
app.UseRequestTiming();
app.UseRateLimiter();
app.UseModules();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapPlatformEndpoints();
app.MapModules(api => api.RequireRateLimiting(PlatformExtensions.ApiRateLimitPolicy));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(KenoKoch.Web.Client._Imports).Assembly);

app.Run();

/// <summary>Einstiegspunkt – öffentlich für Integrationstests mit WebApplicationFactory.</summary>
public partial class Program;
