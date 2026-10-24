using System.ComponentModel.DataAnnotations;
using KenoKoch.BuildingBlocks.Persistence;
using KenoKoch.Modules.Guestbook.Domain;

namespace KenoKoch.Modules.Guestbook.Application;

public sealed class GuestbookOptions
{
    public const string SectionName = "Guestbook";

    /// <summary>Neue Einträge erst nach Freigabe öffentlich anzeigen.</summary>
    public bool RequireApproval { get; set; } = true;

    /// <summary>Maximale Einträge pro Besucher innerhalb von 10 Minuten.</summary>
    public int MaxEntriesPerVisitor { get; set; } = 3;

    /// <summary>Maximale Einträge insgesamt pro Stunde (Schutz vor Spam-Wellen).</summary>
    public int MaxEntriesPerHour { get; set; } = 30;
}

/// <summary>Formularmodell inklusive Validierungsregeln (DataAnnotations).</summary>
public sealed class GuestbookSubmission
{
    [Required(ErrorMessage = "Bitte geben Sie Ihren Namen an.")]
    [StringLength(GuestbookEntry.NameMaxLength, MinimumLength = 2, ErrorMessage = "Der Name muss zwischen {2} und {1} Zeichen lang sein.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(GuestbookEntry.CompanyMaxLength, ErrorMessage = "Bitte höchstens {1} Zeichen.")]
    public string? Company { get; set; }

    [EnumDataType(typeof(VisitorRole), ErrorMessage = "Bitte wählen Sie aus, wer Sie sind.")]
    public VisitorRole Role { get; set; } = VisitorRole.Recruiting;

    [Required(ErrorMessage = "Bitte schreiben Sie eine kurze Nachricht.")]
    [StringLength(GuestbookEntry.MessageMaxLength, MinimumLength = 3, ErrorMessage = "Die Nachricht muss zwischen {2} und {1} Zeichen lang sein.")]
    public string Message { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "Bitte bestätigen Sie, dass Ihr Eintrag veröffentlicht werden darf.")]
    public bool ConsentToPublish { get; set; }

    /// <summary>Honeypot: für Menschen unsichtbar – wird es ausgefüllt, war es sehr wahrscheinlich ein Bot.</summary>
    public string? Website { get; set; }
}

public sealed record GuestbookEntryDto(
    int Id,
    string Name,
    string? Company,
    string Role,
    string RoleLabel,
    string RoleIcon,
    string Message,
    DateTimeOffset CreatedAt,
    bool IsApproved)
{
    internal static GuestbookEntryDto From(GuestbookEntry entry) => new(
        entry.Id,
        entry.Name,
        entry.Company,
        entry.Role.ToString(),
        entry.Role.Label(),
        entry.Role.Icon(),
        entry.Message,
        entry.CreatedAt,
        entry.IsApproved);
}

/// <summary>Einblick in die ausgeführte Abfrage – für den SQL-Inspector der Demo.</summary>
public sealed record QueryInsight(string Sql, TimeSpan Duration, int Rows);

public sealed record GuestbookPage(IReadOnlyList<GuestbookEntryDto> Entries, int TotalCount, QueryInsight Query);

public sealed record SubmitResult(bool Succeeded, string? Error, GuestbookEntryDto? Entry, IReadOnlyList<CapturedSqlCommand> Commands)
{
    public static SubmitResult Fail(string error) => new(false, error, null, []);

    public static SubmitResult Ok(GuestbookEntryDto entry, IReadOnlyList<CapturedSqlCommand> commands) => new(true, null, entry, commands);
}

public sealed record GuestbookApiResponse(int Total, IReadOnlyList<GuestbookApiEntry> Entries, double QueryMilliseconds);

public sealed record GuestbookApiEntry(string Name, string? Company, string Role, string Message, DateTimeOffset CreatedAt);
