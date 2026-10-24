namespace KenoKoch.Web.Hosting;

/// <summary>Ausschnitte aus dem echten Quelltext dieser Anwendung (zur Build-Zeit eingebettet).</summary>
public static class SourceSnippets
{
    /// <summary>Der Teil von Program.cs, in dem Plattform und Module zusammengesetzt werden.</summary>
    public static string ProgramComposition { get; } = Extract("Program.cs", "var builder", "AddInteractiveWebAssemblyComponents");

    private static string Extract(string resource, string startMarker, string endMarker)
    {
        using var stream = typeof(SourceSnippets).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return "// Quelltext nicht eingebettet";
        }

        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimStart().StartsWith(startMarker, StringComparison.Ordinal));
        var end = Array.FindIndex(lines, Math.Max(start, 0), l => l.Contains(endMarker, StringComparison.Ordinal));

        return start >= 0 && end >= start
            ? string.Join('\n', lines[start..(end + 1)])
            : string.Join('\n', lines.Take(20));
    }
}
