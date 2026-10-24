using System.Text.RegularExpressions;

namespace KenoKoch.Modules.Guestbook.Domain;

/// <summary>Ein Eintrag im Gästebuch. Invarianten werden in der Fabrikmethode geprüft.</summary>
public sealed partial class GuestbookEntry
{
    public const int NameMaxLength = 60;
    public const int CompanyMaxLength = 80;
    public const int MessageMaxLength = 500;

    private GuestbookEntry()
    {
        // Für EF Core.
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Company { get; private set; }

    public VisitorRole Role { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsApproved { get; private set; }

    public static GuestbookEntry Create(string name, string? company, VisitorRole role, string message, DateTimeOffset createdAt, bool approved)
    {
        var cleanName = CollapseWhitespace(name);
        var cleanCompany = string.IsNullOrWhiteSpace(company) ? null : CollapseWhitespace(company);
        var cleanMessage = message?.Trim() ?? string.Empty;

        if (cleanName.Length is < 2 or > NameMaxLength)
        {
            throw new ArgumentException($"Der Name muss zwischen 2 und {NameMaxLength} Zeichen lang sein.", nameof(name));
        }

        if (cleanCompany is { Length: > CompanyMaxLength })
        {
            throw new ArgumentException($"Das Unternehmen darf höchstens {CompanyMaxLength} Zeichen lang sein.", nameof(company));
        }

        if (cleanMessage.Length is < 3 or > MessageMaxLength)
        {
            throw new ArgumentException($"Die Nachricht muss zwischen 3 und {MessageMaxLength} Zeichen lang sein.", nameof(message));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unbekannte Besucherrolle.");
        }

        return new GuestbookEntry
        {
            Name = cleanName,
            Company = cleanCompany,
            Role = role,
            Message = cleanMessage,
            CreatedAt = createdAt,
            IsApproved = approved,
        };
    }

    /// <summary>Enthält der Text Links? Links sind im Gästebuch nicht erlaubt (Spam-Schutz).</summary>
    public static bool ContainsLink(string? text) => text is not null && LinkRegex().IsMatch(text);

    public void Approve() => IsApproved = true;

    private static string CollapseWhitespace(string value) => WhitespaceRegex().Replace(value ?? string.Empty, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(https?://|www\.|\b[a-z0-9-]+\.(com|de|org|io|ru|cn|xyz)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();
}
