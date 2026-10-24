namespace KenoKoch.Modules.Guestbook.Domain;

/// <summary>Wer schreibt ins Gästebuch? (Gespeichert als Text, damit die Daten in SQL lesbar bleiben.)</summary>
public enum VisitorRole
{
    Recruiting = 1,
    Developer = 2,
    Lead = 3,
    Curious = 4,
}

public static class VisitorRoleExtensions
{
    public static string Label(this VisitorRole role) => role switch
    {
        VisitorRole.Recruiting => "Recruiting / HR",
        VisitorRole.Developer => "Entwickler:in",
        VisitorRole.Lead => "Führungskraft",
        VisitorRole.Curious => "Neugierig",
        _ => "Besucher:in",
    };

    public static string Icon(this VisitorRole role) => role switch
    {
        VisitorRole.Recruiting => "users",
        VisitorRole.Developer => "code",
        VisitorRole.Lead => "target",
        _ => "sparkles",
    };
}
