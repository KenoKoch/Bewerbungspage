using System.Globalization;
using System.Reflection;

namespace KenoKoch.Web.Hosting;

/// <summary>Version, Commit und Build-Zeitpunkt – werden beim Build eingebettet (Source Link / AssemblyMetadata).</summary>
public static class BuildInfo
{
    static BuildInfo()
    {
        var assembly = typeof(BuildInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        Version = plus > 0 ? informational[..plus] : informational;
        Commit = plus > 0 ? informational[(plus + 1)..] : null;

        var timestamp = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildTimestamp")?.Value;

        BuiltAt = DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    public static string Version { get; }

    /// <summary>Vollständiger Git-Commit (nur wenn beim Build ein Git-Repository vorhanden war).</summary>
    public static string? Commit { get; }

    public static string? CommitShort => Commit is { Length: >= 7 } ? Commit[..7] : Commit;

    public static DateTimeOffset? BuiltAt { get; }
}
