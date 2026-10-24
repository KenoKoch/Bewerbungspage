using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace KenoKoch.UI.Code;

public enum CodeLanguage
{
    Plain,
    CSharp,
    Scl,
    Sql,
    Json,
    Bicep,
    Yaml,
    Shell,
}

public readonly record struct CodeToken(string Text, string? Kind);

/// <summary>
/// Schlanker Syntax-Highlighter auf Basis von Source-generierten Regex-Ausdrücken.
/// Läuft identisch auf dem Server und in WebAssembly – ohne JavaScript-Bibliothek.
/// </summary>
public static partial class CodeHighlighter
{
    private static readonly string[] Kinds = ["c", "pg", "s", "y", "g", "v", "i", "p", "d", "k", "t", "f", "n"];

    public static string LanguageLabel(CodeLanguage language) => language switch
    {
        CodeLanguage.CSharp => "C#",
        CodeLanguage.Scl => "SCL",
        CodeLanguage.Sql => "T-SQL",
        CodeLanguage.Json => "JSON",
        CodeLanguage.Bicep => "Bicep",
        CodeLanguage.Yaml => "YAML",
        CodeLanguage.Shell => "Shell",
        _ => "Text",
    };

    /// <summary>Zerlegt den Code in Zeilen aus farbigen Tokens.</summary>
    public static IReadOnlyList<IReadOnlyList<CodeToken>> Tokenize(string code, CodeLanguage language)
    {
        code = Normalize(code);
        var tokens = new List<CodeToken>();
        var regex = GetRegex(language);

        if (regex is null)
        {
            tokens.Add(new CodeToken(code, null));
        }
        else
        {
            var position = 0;
            foreach (Match match in regex.Matches(code))
            {
                if (match.Index > position)
                {
                    tokens.Add(new CodeToken(code[position..match.Index], null));
                }

                tokens.Add(new CodeToken(match.Value, KindOf(match)));
                position = match.Index + match.Length;
            }

            if (position < code.Length)
            {
                tokens.Add(new CodeToken(code[position..], null));
            }
        }

        return SplitLines(tokens);
    }

    /// <summary>Erzeugt HTML: eine Zeile pro <c>span.ln</c>, Tokens als <c>span.tok-*</c>.</summary>
    public static string ToHtml(string code, CodeLanguage language, ISet<int>? highlightedLines = null)
    {
        var lines = Tokenize(code, language);
        var html = new StringBuilder(code.Length * 2);

        for (var i = 0; i < lines.Count; i++)
        {
            var lineNumber = i + 1;
            html.Append("<span class=\"ln");
            if (highlightedLines?.Contains(lineNumber) == true)
            {
                html.Append(" ln--hl");
            }

            html.Append("\" style=\"--i:").Append(i).Append("\">");

            foreach (var token in lines[i])
            {
                var text = WebUtility.HtmlEncode(token.Text);
                if (token.Kind is null)
                {
                    html.Append(text);
                }
                else
                {
                    html.Append("<span class=\"tok-").Append(token.Kind).Append("\">").Append(text).Append("</span>");
                }
            }

            html.Append("</span>\n");
        }

        return html.ToString();
    }

    /// <summary>Parst Angaben wie "3-5,9" zu einer Menge von Zeilennummern.</summary>
    public static HashSet<int> ParseLineRanges(string? ranges)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(ranges))
        {
            return result;
        }

        foreach (var part in ranges.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split('-', 2, StringSplitOptions.TrimEntries);
            if (int.TryParse(bounds[0], out var from))
            {
                var to = bounds.Length == 2 && int.TryParse(bounds[1], out var end) ? end : from;
                for (var line = from; line <= to; line++)
                {
                    result.Add(line);
                }
            }
        }

        return result;
    }

    /// <summary>Entfernt gemeinsame Einrückung und Leerzeilen am Anfang/Ende.</summary>
    public static string Normalize(string code)
    {
        var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\t', ' ').Split('\n').ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var indent = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Length - l.TrimStart(' ').Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart(' ')));
    }

    private static string? KindOf(Match match)
    {
        foreach (var kind in Kinds)
        {
            if (match.Groups[kind].Success)
            {
                return kind;
            }
        }

        return null;
    }

    private static List<IReadOnlyList<CodeToken>> SplitLines(List<CodeToken> tokens)
    {
        var lines = new List<IReadOnlyList<CodeToken>>();
        var current = new List<CodeToken>();

        foreach (var token in tokens)
        {
            var parts = token.Text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    lines.Add(current);
                    current = [];
                }

                if (parts[i].Length > 0)
                {
                    current.Add(token with { Text = parts[i] });
                }
            }
        }

        lines.Add(current);
        return lines;
    }

    private static Regex? GetRegex(CodeLanguage language) => language switch
    {
        CodeLanguage.CSharp => CSharpRegex(),
        CodeLanguage.Scl => SclRegex(),
        CodeLanguage.Sql => SqlRegex(),
        CodeLanguage.Json => JsonRegex(),
        CodeLanguage.Bicep => BicepRegex(),
        CodeLanguage.Yaml => YamlRegex(),
        CodeLanguage.Shell => ShellRegex(),
        _ => null,
    };

    [GeneratedRegex("""(?<c>//[^\n]*|/\*[\s\S]*?\*/)|(?<s>\$?@?"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)')|(?<k>\b(?:abstract|as|async|await|base|bool|break|byte|case|catch|char|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|false|file|finally|float|for|foreach|get|global|if|implicit|in|init|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|partial|private|protected|public|readonly|record|ref|required|return|scoped|sealed|set|short|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|using|var|virtual|void|volatile|when|where|while|with|yield|not|and|or|nameof|value)\b)|(?<n>\b(?:0x[0-9A-Fa-f_]+|\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?[fFdDmMuUlL]?)\b)|(?<f>\b[A-Za-z_][A-Za-z0-9_]*(?=\s*(?:<[A-Za-z0-9_, <>?]*>)?\())|(?<t>\b[A-Z][A-Za-z0-9_]*\b)""")]
    private static partial Regex CSharpRegex();

    [GeneratedRegex("""(?<c>//[^\n]*|\(\*[\s\S]*?\*\))|(?<pg>\{[^}\n]*\})|(?<s>'(?:[^'\n]|\$')*')|(?<g>"[^"\n]*")|(?<v>#[A-Za-z_][A-Za-z0-9_]*)|(?<k>\b(?:FUNCTION_BLOCK|END_FUNCTION_BLOCK|FUNCTION|END_FUNCTION|ORGANIZATION_BLOCK|END_ORGANIZATION_BLOCK|DATA_BLOCK|END_DATA_BLOCK|VAR_INPUT|VAR_OUTPUT|VAR_IN_OUT|VAR_TEMP|VAR|END_VAR|CONSTANT|RETAIN|BEGIN|IF|THEN|ELSIF|ELSE|END_IF|CASE|OF|END_CASE|FOR|TO|BY|DO|END_FOR|WHILE|END_WHILE|REPEAT|UNTIL|END_REPEAT|RETURN|EXIT|CONTINUE|AND|OR|XOR|NOT|MOD|TRUE|FALSE|TITLE|AUTHOR|VERSION|REGION|END_REGION)\b)|(?<t>\b(?:Bool|Byte|Word|DWord|Int|DInt|UInt|UDInt|SInt|USInt|Real|LReal|Time|Date|String|Char)\b)|(?<f>\b[A-Za-z_][A-Za-z0-9_]*(?=\s*\())|(?<n>\bT#[0-9A-Za-z_]+|\b\d+(?:\.\d+)?\b)""")]
    private static partial Regex SclRegex();

    [GeneratedRegex("""(?<c>--[^\n]*|/\*[\s\S]*?\*/)|(?<s>N?'(?:[^']|'')*')|(?<i>\[[^\]\n]+\])|(?<p>@[A-Za-z_][A-Za-z0-9_]*)|(?<k>\b(?:SELECT|FROM|WHERE|AND|OR|NOT|ORDER|BY|GROUP|HAVING|TOP|DESC|ASC|AS|INSERT|INTO|VALUES|OUTPUT|INSERTED|UPDATE|SET|DELETE|MERGE|USING|ON|OFF|WHEN|MATCHED|THEN|JOIN|LEFT|RIGHT|INNER|OUTER|CROSS|APPLY|DECLARE|NOCOUNT|IMPLICIT_TRANSACTIONS|COUNT|COUNT_BIG|SUM|AVG|MIN|MAX|CAST|CONVERT|COALESCE|IS|NULL|IN|LIKE|BETWEEN|OFFSET|ROWS|ROW|FETCH|NEXT|ONLY|EXISTS|CASE|ELSE|END|WITH|DISTINCT|CREATE|ALTER|DROP|TABLE|INDEX|SCHEMA|PRIMARY|KEY|DEFAULT|CONSTRAINT|INT|BIGINT|NVARCHAR|VARCHAR|BIT|DATE|DATETIME2|DATETIMEOFFSET|ROLE|USER|ADD|MEMBER|EXTERNAL|PROVIDER|GO|BEGIN|COMMIT|ROLLBACK|TRANSACTION|EXEC|GRANT|TO|FOR)\b)|(?<n>\b\d+(?:\.\d+)?\b)""", RegexOptions.IgnoreCase)]
    private static partial Regex SqlRegex();

    [GeneratedRegex("""(?<y>"(?:[^"\\\n]|\\.)*"(?=\s*:))|(?<s>"(?:[^"\\\n]|\\.)*")|(?<n>-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)|(?<k>\b(?:true|false|null)\b)""")]
    private static partial Regex JsonRegex();

    [GeneratedRegex("""(?<c>//[^\n]*|/\*[\s\S]*?\*/)|(?<s>'(?:[^'\\\n]|\\.)*')|(?<d>@[A-Za-z]+)|(?<k>\b(?:param|var|resource|module|output|existing|targetScope|if|for|in|true|false|null|import|using|type|func|metadata)\b)|(?<t>\b(?:string|int|bool|object|array)\b)|(?<f>\b[A-Za-z_][A-Za-z0-9_]*(?=\())|(?<n>\b\d+\b)""")]
    private static partial Regex BicepRegex();

    [GeneratedRegex("""(?<c>(?<=^|\s)#[^\n]*)|(?<v>\$\{\{[^}\n]*\}\})|(?<s>'[^'\n]*'|"[^"\n]*")|(?<y>^[ \t]*-?[ \t]*[A-Za-z0-9_.\-/]+(?=:))|(?<k>\b(?:true|false|null)\b)""", RegexOptions.Multiline)]
    private static partial Regex YamlRegex();

    [GeneratedRegex("""(?<c>(?<=^|\s)#[^\n]*)|(?<s>'[^'\n]*'|"[^"\n]*")|(?<k>^(?:dotnet|az|git|sqllocaldb|sqlcmd|cd|curl|winget)\b)|(?<v>\$[A-Za-z_][A-Za-z0-9_]*|(?<=\s)--?[a-z][a-z0-9\-]*)""", RegexOptions.Multiline)]
    private static partial Regex ShellRegex();
}
