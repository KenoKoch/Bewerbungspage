namespace KenoKoch.BuildingBlocks.Persistence;

public enum DatabaseStatus
{
    NotConfigured,
    Initializing,
    Ready,
    Unavailable,
}

/// <summary>
/// Zentraler Zustand der Datenbankverbindung. Die Oberfläche bleibt dadurch bedienbar,
/// auch wenn Azure SQL (Serverless) gerade pausiert oder noch hochfährt.
/// </summary>
public sealed class DatabaseState
{
    private readonly Lock _gate = new();

    public DatabaseStatus Status { get; private set; } = DatabaseStatus.Initializing;

    public string? Message { get; private set; } = "Verbindung wird aufgebaut …";

    public DateTimeOffset ChangedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsReady => Status == DatabaseStatus.Ready;

    public event Action? Changed;

    public void Set(DatabaseStatus status, string? message = null)
    {
        lock (_gate)
        {
            if (Status == status && Message == message)
            {
                return;
            }

            Status = status;
            Message = message;
            ChangedAt = DateTimeOffset.UtcNow;
        }

        Changed?.Invoke();
    }
}
