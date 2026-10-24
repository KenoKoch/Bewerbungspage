using KenoKoch.Modules.Guestbook.Application;
using KenoKoch.Modules.Guestbook.Domain;
using Microsoft.Extensions.Options;

namespace KenoKoch.Tests.Guestbook;

public sealed class GuestbookEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_whitespace()
    {
        var entry = GuestbookEntry.Create("  Maria   Muster ", "  ", VisitorRole.Recruiting, "  Hallo!  ", Now, approved: false);

        Assert.Equal("Maria Muster", entry.Name);
        Assert.Null(entry.Company);
        Assert.Equal("Hallo!", entry.Message);
        Assert.False(entry.IsApproved);
    }

    [Theory]
    [InlineData("A", "Hallo Welt")]
    [InlineData("Maria", "Hi")]
    public void Create_rejects_too_short_values(string name, string message) =>
        Assert.Throws<ArgumentException>(() => GuestbookEntry.Create(name, null, VisitorRole.Curious, message, Now, approved: true));

    [Fact]
    public void Create_rejects_too_long_message() =>
        Assert.Throws<ArgumentException>(() =>
            GuestbookEntry.Create("Maria", null, VisitorRole.Curious, new string('x', GuestbookEntry.MessageMaxLength + 1), Now, approved: true));

    [Fact]
    public void Create_rejects_unknown_role() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GuestbookEntry.Create("Maria", null, (VisitorRole)99, "Hallo Welt", Now, approved: true));

    [Theory]
    [InlineData("Besuchen Sie https://spam.example", true)]
    [InlineData("Mehr auf www.irgendwas.de", true)]
    [InlineData("Günstig bei shop-xyz.com kaufen", true)]
    [InlineData("Starke Seite – ASP.NET Core und Blazor gefallen mir!", false)]
    [InlineData("Viele Grüße aus Hamburg", false)]
    public void Links_are_detected(string text, bool expected) =>
        Assert.Equal(expected, GuestbookEntry.ContainsLink(text));

    [Fact]
    public void Role_labels_are_german() =>
        Assert.Equal("Recruiting / HR", VisitorRole.Recruiting.Label());
}

public sealed class SubmissionThrottleTests
{
    [Fact]
    public void Visitor_is_limited_but_others_are_not()
    {
        using var throttle = new SubmissionThrottle(Options.Create(new GuestbookOptions { MaxEntriesPerVisitor = 2, MaxEntriesPerHour = 10 }));

        Assert.True(throttle.TryAcquire("visitor-a"));
        Assert.True(throttle.TryAcquire("visitor-a"));
        Assert.False(throttle.TryAcquire("visitor-a"));
        Assert.True(throttle.TryAcquire("visitor-b"));
    }

    [Fact]
    public void Global_limit_protects_against_spam_waves()
    {
        using var throttle = new SubmissionThrottle(Options.Create(new GuestbookOptions { MaxEntriesPerVisitor = 5, MaxEntriesPerHour = 3 }));

        Assert.True(throttle.TryAcquire("a"));
        Assert.True(throttle.TryAcquire("b"));
        Assert.True(throttle.TryAcquire("c"));
        Assert.False(throttle.TryAcquire("d"));
    }
}
