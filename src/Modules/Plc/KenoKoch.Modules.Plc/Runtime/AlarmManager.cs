namespace KenoKoch.Modules.Plc.Runtime;

public enum AlarmClass
{
    Fault,
    Warning,
    System,
}

public sealed record AlarmDefinition(int Number, AlarmClass Class, string Text);

/// <summary>Eine Meldung im HMI mit den klassischen Zuständen Kommend (K), Gegangen (G) und Quittiert (Q).</summary>
public sealed class AlarmEntry
{
    public required AlarmDefinition Definition { get; init; }

    public required DateTimeOffset CameAt { get; init; }

    public DateTimeOffset? WentAt { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public bool IsActive => WentAt is null;

    public bool IsAcknowledged => AcknowledgedAt is not null;

    public string State => (IsActive, IsAcknowledged) switch
    {
        (true, false) => "K",
        (true, true) => "KQ",
        (false, false) => "KG",
        _ => "KGQ",
    };
}

/// <summary>
/// Meldesystem nach WinCC-Vorbild: Störmeldungen müssen quittiert werden,
/// Warnungen und Systemmeldungen verschwinden von selbst, sobald die Ursache weg ist.
/// </summary>
public sealed class AlarmManager
{
    public static readonly AlarmDefinition EmergencyStop = new(1001, AlarmClass.Fault, "NOT-HALT betätigt");
    public static readonly AlarmDefinition MotorProtection = new(1002, AlarmClass.Fault, "Motorschutz Pumpe P1 ausgelöst");
    public static readonly AlarmDefinition LevelHighHigh = new(1003, AlarmClass.Fault, "Überfüllsicherung T1 (LSHH) angesprochen");
    public static readonly AlarmDefinition DryRun = new(2001, AlarmClass.Warning, "Trockenlaufschutz aktiv – Ventil V1 gesperrt");
    public static readonly AlarmDefinition CpuStop = new(9001, AlarmClass.System, "CPU in STOP – alle Ausgänge abgeschaltet");

    private const int HistoryCapacity = 12;

    private readonly List<AlarmEntry> _current = [];
    private readonly LinkedList<AlarmEntry> _history = new();

    public IReadOnlyList<AlarmEntry> Current => _current;

    public IReadOnlyCollection<AlarmEntry> History => _history;

    public bool HasUnacknowledged => _current.Any(a => !a.IsAcknowledged);

    /// <summary>Wertet die Meldebits aus und liefert die neu gekommenen Meldungen.</summary>
    public IReadOnlyList<AlarmDefinition> Evaluate(DateTimeOffset now, IEnumerable<(AlarmDefinition Definition, bool Active)> conditions)
    {
        var raised = new List<AlarmDefinition>();

        foreach (var (definition, active) in conditions)
        {
            var entry = _current.FirstOrDefault(a => a.Definition == definition && a.IsActive);

            if (active && entry is null)
            {
                _current.Insert(0, new AlarmEntry
                {
                    Definition = definition,
                    CameAt = now,
                    AcknowledgedAt = definition.Class == AlarmClass.Fault ? null : now,
                });
                raised.Add(definition);
            }
            else if (!active && entry is not null)
            {
                entry.WentAt = now;
            }
        }

        ArchiveCompleted();
        return raised;
    }

    public void AcknowledgeAll(DateTimeOffset now)
    {
        foreach (var entry in _current.Where(a => !a.IsAcknowledged))
        {
            entry.AcknowledgedAt = now;
        }

        ArchiveCompleted();
    }

    private void ArchiveCompleted()
    {
        foreach (var entry in _current.Where(a => !a.IsActive && a.IsAcknowledged).ToArray())
        {
            _current.Remove(entry);
            _history.AddFirst(entry);
        }

        while (_history.Count > HistoryCapacity)
        {
            _history.RemoveLast();
        }
    }
}
