using System.Text.Json;
using System.Text.RegularExpressions;
using KenoKoch.Modules.Profile.Model;

namespace KenoKoch.Modules.Profile;

/// <summary>Lädt den Lebenslauf einmalig aus der eingebetteten <c>profile.json</c>.</summary>
public sealed partial class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public ProfileStore()
        : this(ReadEmbeddedJson())
    {
    }

    public ProfileStore(string json)
    {
        Profile = JsonSerializer.Deserialize<ProfileData>(json, JsonOptions)
                  ?? throw new InvalidOperationException("profile.json ist leer oder ungültig.");

        var contentWithoutComments = string.Join('\n', json
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        OpenPlaceholders = PlaceholderRegex().Matches(contentWithoutComments)
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public ProfileData Profile { get; }

    /// <summary>Noch nicht ausgefüllte Platzhalter wie "[Unternehmen]" (Hinweis im Development-Modus).</summary>
    public IReadOnlyList<string> OpenPlaceholders { get; }

    internal static string ReadEmbeddedJson()
    {
        using var stream = typeof(ProfileStore).Assembly.GetManifestResourceStream("profile.json")
                           ?? throw new InvalidOperationException("Eingebettete Ressource 'profile.json' nicht gefunden.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex("""\[[A-ZÄÖÜa-zäöü][^\]"\n]{1,120}\]""")]
    private static partial Regex PlaceholderRegex();
}
