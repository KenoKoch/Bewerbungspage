using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KenoKoch.Web.Hosting;

public static class Middleware
{
    private const string CspNonceKey = "kk:csp-nonce";
    private const string RequestStartKey = "kk:request-start";

    /// <summary>
    /// Security Header inklusive Content-Security-Policy mit Nonce pro Request:
    /// Skripte nur vom eigenen Origin, WebAssembly erlaubt, keine Einbettung in fremde Frames.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        var isDevelopment = app.ApplicationServices.GetRequiredService<IHostEnvironment>().IsDevelopment();

        return app.Use(async (context, next) =>
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Items[CspNonceKey] = nonce;

            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";

                // Die Scalar-API-Referenz bringt eigene Inline-Skripte mit und bekommt daher keine strikte CSP.
                if (!context.Request.Path.StartsWithSegments("/scalar"))
                {
                    headers.ContentSecurityPolicy = BuildContentSecurityPolicy(context, nonce, isDevelopment);
                }

                return Task.CompletedTask;
            });

            await next();
        });
    }

    /// <summary>Misst die Serverzeit pro Request (Server-Timing-Header und Anzeige im Footer).</summary>
    public static IApplicationBuilder UseRequestTiming(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var started = Stopwatch.GetTimestamp();
        context.Items[RequestStartKey] = started;

        context.Response.OnStarting(() =>
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            context.Response.Headers["Server-Timing"] = "app;dur=" + elapsed.ToString("0.0", CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        });

        await next();
    });

    /// <summary>Leitet www.&lt;host&gt; dauerhaft auf den kanonischen Host um (nur wenn konfiguriert).</summary>
    public static IApplicationBuilder UseCanonicalHost(this IApplicationBuilder app)
    {
        var canonicalHost = app.ApplicationServices.GetRequiredService<IOptions<SiteOptions>>().Value.CanonicalHost;
        if (string.IsNullOrWhiteSpace(canonicalHost))
        {
            return app;
        }

        return app.Use(async (context, next) =>
        {
            if (string.Equals(context.Request.Host.Host, "www." + canonicalHost, StringComparison.OrdinalIgnoreCase))
            {
                var target = $"https://{canonicalHost}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
                context.Response.Redirect(target, permanent: true);
                return;
            }

            await next();
        });
    }

    public static string? GetCspNonce(this HttpContext context) => context.Items[CspNonceKey] as string;

    public static TimeSpan? GetElapsedSinceRequestStart(this HttpContext context) =>
        context.Items[RequestStartKey] is long started ? Stopwatch.GetElapsedTime(started) : null;

    private static string BuildContentSecurityPolicy(HttpContext context, string nonce, bool isDevelopment)
    {
        var host = context.Request.Host.Value;
        var socket = isDevelopment ? "ws: wss:" : $"wss://{host}";

        return string.Join("; ",
            "default-src 'self'",
            $"script-src 'self' 'nonce-{nonce}' 'wasm-unsafe-eval'",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data:",
            "font-src 'self'",
            $"connect-src 'self' {socket}",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'");
    }
}
