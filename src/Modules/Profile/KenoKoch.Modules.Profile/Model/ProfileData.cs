namespace KenoKoch.Modules.Profile.Model;

/// <summary>Kompletter Lebenslauf – wird aus <c>Data/profile.json</c> geladen und unter <c>/api/profile</c> ausgeliefert.</summary>
public sealed record ProfileData
{
    public required string Name { get; init; }

    public required string FirstName { get; init; }

    public required string Headline { get; init; }

    public required string Tagline { get; init; }

    public string? Availability { get; init; }

    public required string Location { get; init; }

    public required string Email { get; init; }

    public string? Phone { get; init; }

    public ProfileLinks Links { get; init; } = new();

    public string? CvUrl { get; init; }

    public IReadOnlyList<string> About { get; init; } = [];

    public IReadOnlyList<ProfileFact> Facts { get; init; } = [];

    public IReadOnlyList<ProfileHighlight> Highlights { get; init; } = [];

    public IReadOnlyList<ExperienceEntry> Experience { get; init; } = [];

    public IReadOnlyList<EducationEntry> Education { get; init; } = [];

    public IReadOnlyList<SkillGroup> SkillGroups { get; init; } = [];

    public IReadOnlyList<LanguageSkill> Languages { get; init; } = [];
}

public sealed record ProfileLinks
{
    public string? GitHub { get; init; }

    public string? LinkedIn { get; init; }

    public string? Xing { get; init; }

    /// <summary>Öffentliches Repository dieser Website (für "Code ansehen"-Links).</summary>
    public string? Repository { get; init; }
}

public sealed record ProfileFact(string Icon, string Label, string Value);

public sealed record ProfileHighlight(string Icon, string Title, string Text);

public sealed record ExperienceEntry
{
    public required string Period { get; init; }

    public required string Role { get; init; }

    public required string Company { get; init; }

    public string? Location { get; init; }

    /// <summary>"software" oder "automation" – steuert Icon und Farbe in der Zeitleiste.</summary>
    public string Kind { get; init; } = "software";

    public bool Current { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<string> Achievements { get; init; } = [];

    public IReadOnlyList<string> Tech { get; init; } = [];
}

public sealed record EducationEntry(string Period, string Title, string Institution, string? Details = null);

public sealed record SkillGroup
{
    public required string Name { get; init; }

    public string Icon { get; init; } = "code";

    public string? Description { get; init; }

    public IReadOnlyList<SkillItem> Skills { get; init; } = [];
}

public sealed record SkillItem(string Name, bool Focus = false);

public sealed record LanguageSkill(string Name, string Level);
