using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KenoKoch.Tests.Integration;

/// <summary>
/// Startet die komplette Anwendung im Speicher – bewusst OHNE Datenbank.
/// So ist abgesichert, dass die Seite auch dann funktioniert, wenn Azure SQL pausiert oder nicht erreichbar ist.
/// </summary>
public sealed class WebsiteFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Sql", string.Empty);
    }
}

public sealed class WebsiteTests(WebsiteFactory factory) : IClassFixture<WebsiteFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/", "Keno Koch")]
    [InlineData("/demos", "SPS-Simulator mit HMI")]
    [InlineData("/architektur", "So ist diese Website")]
    [InlineData("/impressum", "Impressum")]
    [InlineData("/datenschutz", "Datenschutzerklärung")]
    public async Task Pages_render_without_database(string path, string expectedText)
    {
        var response = await _client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_route_returns_custom_404()
    {
        var response = await _client.GetAsync("/diese-seite-gibt-es-nicht");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Diese Seite gibt es nicht", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Security_headers_include_csp_with_nonce()
    {
        var response = await _client.GetAsync("/");

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self' 'nonce-", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));

        // Die Nonce aus dem Header muss an der Import-Map im HTML stehen.
        var nonce = csp.Split("'nonce-")[1].Split('\'')[0];
        Assert.Contains($"nonce=\"{nonce}\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Liveness_check_does_not_need_the_database()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Profile_api_returns_the_cv()
    {
        var profile = await _client.GetFromJsonAsync<JsonElement>("/api/profile");

        Assert.Equal("Keno Koch", profile.GetProperty("name").GetString());
        Assert.True(profile.GetProperty("experience").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task Modules_api_lists_all_four_modules_with_their_endpoints()
    {
        var modules = await _client.GetFromJsonAsync<JsonElement[]>("/api/modules");

        Assert.NotNull(modules);
        Assert.Equal(["Profile", "Guestbook", "Analytics", "Plc"], modules.Select(m => m.GetProperty("name").GetString()));
        Assert.All(modules, m => Assert.All(
            m.GetProperty("endpoints").EnumerateArray(),
            e => Assert.StartsWith("/api/", e.GetProperty("route").GetString(), StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Stats_api_reports_missing_database_instead_of_failing()
    {
        var stats = await _client.GetFromJsonAsync<JsonElement>("/api/stats");

        Assert.Equal("NotConfigured", stats.GetProperty("database").GetString());
        Assert.Equal(JsonValueKind.Null, stats.GetProperty("pageViewsTotal").ValueKind);
    }

    [Fact]
    public async Task Guestbook_api_returns_503_without_database()
    {
        var response = await _client.GetAsync("/api/guestbook");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_describes_the_module_endpoints()
    {
        var document = await _client.GetStringAsync("/openapi/v1.json");

        Assert.Contains("\"/api/profile\"", document, StringComparison.Ordinal);
        Assert.Contains("\"/api/plc/status\"", document, StringComparison.Ordinal);
        Assert.Contains("KenoKoch.de API", document, StringComparison.Ordinal);
    }
}
