using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace KenoKoch.Modules.Analytics.Tracking;

/// <summary>
/// Zählt echte Seitenaufrufe – ohne Cookies, ohne IP-Speicherung.
/// Gezählt werden nur HTML-Dokumente, die ein Browser als Seite lädt (Sec-Fetch-Dest: document)
/// oder per Blazor Enhanced Navigation nachlädt. Bots, Health-Probes und Always-On-Pings fallen heraus.
/// </summary>
internal sealed partial class PageViewMiddleware(RequestDelegate next, MetricsBuffer buffer)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (ShouldCount(context))
        {
            buffer.Increment(MetricNames.Page(NormalizePath(context.Request.Path)));
        }
    }

    internal static bool ShouldCount(HttpContext context)
    {
        var request = context.Request;

        if (!HttpMethods.IsGet(request.Method) || context.Response.StatusCode != StatusCodes.Status200OK)
        {
            return false;
        }

        if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        var isDocument = string.Equals(request.Headers["Sec-Fetch-Dest"], "document", StringComparison.OrdinalIgnoreCase);
        var isEnhancedNavigation = request.Headers.Accept.ToString().Contains("blazor-enhanced-nav", StringComparison.OrdinalIgnoreCase);

        return (isDocument || isEnhancedNavigation) && !BotRegex().IsMatch(request.Headers.UserAgent.ToString());
    }

    internal static string NormalizePath(PathString path)
    {
        var value = (path.Value ?? "/").TrimEnd('/').ToLowerInvariant();
        if (value.Length == 0)
        {
            value = "/";
        }

        return value.Length > 100 ? value[..100] : value;
    }

    [GeneratedRegex("bot|crawl|spider|slurp|preview|monitor|alwayson|healthcheck|lighthouse|pingdom|curl|wget|python|httpclient|java/", RegexOptions.IgnoreCase)]
    private static partial Regex BotRegex();
}
