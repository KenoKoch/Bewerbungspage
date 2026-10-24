using KenoKoch.BuildingBlocks.Events;
using KenoKoch.Modules.Profile;
using KenoKoch.UI.Code;
using KenoKoch.UI.Formatting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KenoKoch.Tests.Platform;

public sealed class CodeHighlighterTests
{
    [Fact]
    public void CSharp_keywords_types_and_strings_are_tokenized()
    {
        var tokens = CodeHighlighter.Tokenize("""var builder = WebApplication.CreateBuilder("x");""", CodeLanguage.CSharp).Single();

        Assert.Contains(tokens, t => t is { Text: "var", Kind: "k" });
        Assert.Contains(tokens, t => t is { Text: "WebApplication", Kind: "t" });
        Assert.Contains(tokens, t => t is { Text: "CreateBuilder", Kind: "f" });
        Assert.Contains(tokens, t => t is { Text: "\"x\"", Kind: "s" });
    }

    [Fact]
    public void Scl_local_variables_and_functions_are_tokenized()
    {
        var tokens = CodeHighlighter.Tokenize("#qLevel := SCALE_X(MIN := 0.0); // Kommentar", CodeLanguage.Scl).Single();

        Assert.Contains(tokens, t => t is { Text: "#qLevel", Kind: "v" });
        Assert.Contains(tokens, t => t is { Text: "SCALE_X", Kind: "f" });
        Assert.Contains(tokens, t => t is { Text: "// Kommentar", Kind: "c" });
    }

    [Fact]
    public void Html_output_is_encoded()
    {
        var html = CodeHighlighter.ToHtml("""SELECT '<script>' FROM [x]""", CodeLanguage.Sql);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_ranges_are_parsed() =>
        Assert.Equal([3, 4, 5, 9], CodeHighlighter.ParseLineRanges("3-5, 9").Order());

    [Fact]
    public void Common_indentation_is_removed() =>
        Assert.Equal("a\n  b", CodeHighlighter.Normalize("\n    a\n      b\n"));
}

public sealed class FormattingTests
{
    [Fact]
    public void Numbers_use_german_separators()
    {
        Assert.Equal("1.234.567", Fmt.Number(1_234_567));
        Assert.Equal("3,5", Fmt.Number(3.5, 1));
        Assert.Equal("12,5 %", Fmt.Percent(12.5));
    }

    [Theory]
    [InlineData(0.42, "0,42 ms")]
    [InlineData(4.2, "4,2 ms")]
    [InlineData(1234, "1.234 ms")]
    public void Durations_are_readable(double milliseconds, string expected) =>
        Assert.Equal(expected, Fmt.Milliseconds(milliseconds));

    [Fact]
    public void Relative_times_are_german()
    {
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("gerade eben", Fmt.Relative(now.AddSeconds(-3), now));
        Assert.Equal("vor 5 min", Fmt.Relative(now.AddMinutes(-5), now));
        Assert.Equal("gestern", Fmt.Relative(now.AddHours(-30), now));
    }
}

public sealed class ProfileStoreTests
{
    [Fact]
    public void Embedded_profile_is_complete()
    {
        var profile = new ProfileStore().Profile;

        Assert.Equal("Keno Koch", profile.Name);
        Assert.False(string.IsNullOrWhiteSpace(profile.Tagline));
        Assert.Contains(profile.Experience, e => e.Current);
        Assert.Contains(profile.Experience, e => e.Kind == "automation");
        Assert.NotEmpty(profile.SkillGroups);
    }

    [Fact]
    public void Placeholders_are_detected_but_comments_are_ignored()
    {
        const string json = """
            // Hinweis: Platzhalter in [eckigen Klammern] ersetzen
            {
              "name": "Test", "firstName": "T", "headline": "H", "tagline": "T",
              "location": "[Ort], Deutschland", "email": "a@b.de",
              "skillGroups": [ { "name": "Daten", "skills": [ { "name": "SQL" } ] } ]
            }
            """;

        var store = new ProfileStore(json);

        Assert.Equal(["[Ort]"], store.OpenPlaceholders);
    }
}

public sealed class EventBusTests
{
    private sealed record Ping(string Value) : IntegrationEvent;

    private sealed class PingHandler(TaskCompletionSource<string> received) : IIntegrationEventHandler<Ping>
    {
        public Task HandleAsync(Ping integrationEvent, CancellationToken cancellationToken)
        {
            received.TrySetResult(integrationEvent.Value);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingHandler : IIntegrationEventHandler<Ping>
    {
        public Task HandleAsync(Ping integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Absichtlicher Fehler");
    }

    [Fact]
    public async Task Published_events_reach_all_handlers_even_if_one_fails()
    {
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(received)
            .AddIntegrationEvents()
            .AddIntegrationEventHandler<Ping, FailingHandler>()
            .AddIntegrationEventHandler<Ping, PingHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetServices<IHostedService>().Single();
        await dispatcher.StartAsync(CancellationToken.None);

        await provider.GetRequiredService<IEventBus>().PublishAsync(new Ping("hallo"));

        Assert.Equal("hallo", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await dispatcher.StopAsync(CancellationToken.None);
    }
}
