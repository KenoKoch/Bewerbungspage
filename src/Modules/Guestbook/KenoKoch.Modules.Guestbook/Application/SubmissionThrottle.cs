using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace KenoKoch.Modules.Guestbook.Application;

/// <summary>
/// Begrenzt Einträge pro Besucher und insgesamt – mit den Rate-Limiting-Primitiven aus .NET.
/// Formulare in Blazor Server laufen über die SignalR-Verbindung, nicht über HTTP-Requests;
/// deshalb greift hier die Middleware von ASP.NET Core nicht und das Limit sitzt im Anwendungsdienst.
/// </summary>
public sealed class SubmissionThrottle : IDisposable
{
    private readonly PartitionedRateLimiter<string> _perVisitor;
    private readonly FixedWindowRateLimiter _global;

    public SubmissionThrottle(IOptions<GuestbookOptions> options)
    {
        var settings = options.Value;

        _perVisitor = PartitionedRateLimiter.Create<string, string>(visitorKey =>
            RateLimitPartition.GetFixedWindowLimiter(visitorKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, settings.MaxEntriesPerVisitor),
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
            }));

        _global = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, settings.MaxEntriesPerHour),
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
        });
    }

    public bool TryAcquire(string visitorKey)
    {
        using var visitorLease = _perVisitor.AttemptAcquire(visitorKey);
        if (!visitorLease.IsAcquired)
        {
            return false;
        }

        using var globalLease = _global.AttemptAcquire();
        return globalLease.IsAcquired;
    }

    public void Dispose()
    {
        _perVisitor.Dispose();
        _global.Dispose();
    }
}
