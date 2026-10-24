using System.Threading.RateLimiting;

namespace KenoKoch.Modules.Analytics.Activity;

public enum ActivityKind
{
    Guestbook,
    Plc,
    Alarm,
    Reaction,
}

public sealed record ActivityItem(long Id, ActivityKind Kind, string Icon, string Text, DateTimeOffset At, string? Emoji = null);

/// <summary>
/// Gemeinsamer Echtzeit-Feed aller Besucher (nur im Arbeitsspeicher, keine Personendaten).
/// Komponenten abonnieren <see cref="Added"/> und bekommen neue Einträge per SignalR auf den Bildschirm.
/// </summary>
public sealed class ActivityFeed(TimeProvider timeProvider) : IDisposable
{
    public static readonly IReadOnlyList<string> AllowedReactions = ["👋", "🚀", "☕", "🎉", "💜"];

    private const int Capacity = 30;
    private readonly Lock _gate = new();
    private readonly LinkedList<ActivityItem> _items = new();
    private readonly FixedWindowRateLimiter _reactionLimiter = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = 40,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    });

    private long _nextId;

    public event Action<ActivityItem>? Added;

    public IReadOnlyList<ActivityItem> Items
    {
        get
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }
    }

    public ActivityItem Add(ActivityKind kind, string icon, string text, string? emoji = null)
    {
        ActivityItem item;
        lock (_gate)
        {
            item = new ActivityItem(++_nextId, kind, icon, text, timeProvider.GetUtcNow(), emoji);
            _items.AddFirst(item);
            while (_items.Count > Capacity)
            {
                _items.RemoveLast();
            }
        }

        foreach (var handler in Added?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<ActivityItem>)handler)(item);
            }
            catch
            {
                // Ein fehlerhafter Abonnent darf die anderen nicht blockieren.
            }
        }

        return item;
    }

    /// <summary>Sendet eine Emoji-Reaktion an alle Besucher (nur erlaubte Emojis, global begrenzt).</summary>
    public bool TryReact(string emoji)
    {
        if (!AllowedReactions.Contains(emoji))
        {
            return false;
        }

        using var lease = _reactionLimiter.AttemptAcquire();
        if (!lease.IsAcquired)
        {
            return false;
        }

        Add(ActivityKind.Reaction, "sparkles", $"Jemand hat {emoji} gesendet", emoji);
        return true;
    }

    public void Dispose() => _reactionLimiter.Dispose();
}
