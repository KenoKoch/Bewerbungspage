using KenoKoch.Modules.Analytics.Activity;
using KenoKoch.Modules.Analytics.Tracking;
using Microsoft.AspNetCore.Http;

namespace KenoKoch.Tests.Analytics;

public sealed class PageViewMiddlewareTests
{
    private static HttpContext Request(string path, string userAgent = "Mozilla/5.0 Edg/141.0", string? fetchDest = "document", int status = 200, string contentType = "text/html; charset=utf-8")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.Headers.UserAgent = userAgent;
        if (fetchDest is not null)
        {
            context.Request.Headers["Sec-Fetch-Dest"] = fetchDest;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        return context;
    }

    [Fact]
    public void Browser_document_requests_are_counted() =>
        Assert.True(PageViewMiddleware.ShouldCount(Request("/demos")));

    [Fact]
    public void Enhanced_navigation_requests_are_counted()
    {
        var context = Request("/architektur", fetchDest: "empty");
        context.Request.Headers.Accept = "text/html; blazor-enhanced-nav=on";

        Assert.True(PageViewMiddleware.ShouldCount(context));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1)")]
    [InlineData("AlwaysOn")]
    [InlineData("curl/8.4.0")]
    public void Bots_and_probes_are_ignored(string userAgent) =>
        Assert.False(PageViewMiddleware.ShouldCount(Request("/", userAgent)));

    [Fact]
    public void Requests_without_fetch_metadata_are_ignored() =>
        Assert.False(PageViewMiddleware.ShouldCount(Request("/", fetchDest: null)));

    [Fact]
    public void Errors_and_assets_are_ignored()
    {
        Assert.False(PageViewMiddleware.ShouldCount(Request("/gibts-nicht", status: 404)));
        Assert.False(PageViewMiddleware.ShouldCount(Request("/css/app.css", contentType: "text/css")));
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/Demos/", "/demos")]
    [InlineData("/architektur", "/architektur")]
    public void Paths_are_normalized(string path, string expected) =>
        Assert.Equal(expected, PageViewMiddleware.NormalizePath(path));
}

public sealed class MetricsBufferTests
{
    [Fact]
    public void Increments_are_aggregated_and_drained_once()
    {
        var buffer = new MetricsBuffer(TimeProvider.System);
        buffer.Increment(MetricNames.Page("/"));
        buffer.Increment(MetricNames.Page("/"));
        buffer.Increment(MetricNames.PlcStarted);

        var drained = buffer.Drain();

        Assert.Equal(2, drained.Count);
        Assert.Equal(2, drained.Single(d => d.Key.Metric == "page:/").Value);
        Assert.Empty(buffer.Drain());
    }

    [Fact]
    public void Restored_values_are_kept_for_the_next_flush()
    {
        var buffer = new MetricsBuffer(TimeProvider.System);
        buffer.Increment(MetricNames.GuestbookEntry, 3);
        var drained = buffer.Drain();

        buffer.Restore(drained);
        buffer.Increment(MetricNames.GuestbookEntry);

        Assert.Equal(4, Assert.Single(buffer.Drain()).Value);
    }
}

public sealed class ActivityFeedTests
{
    [Fact]
    public void Feed_keeps_newest_items_first_and_notifies_subscribers()
    {
        using var feed = new ActivityFeed(TimeProvider.System);
        ActivityItem? received = null;
        feed.Added += item => received = item;

        feed.Add(ActivityKind.Plc, "cpu", "erstes");
        feed.Add(ActivityKind.Guestbook, "message", "zweites");

        Assert.Equal("zweites", feed.Items[0].Text);
        Assert.Equal("zweites", received?.Text);
    }

    [Fact]
    public void Feed_is_capped()
    {
        using var feed = new ActivityFeed(TimeProvider.System);
        for (var i = 0; i < 50; i++)
        {
            feed.Add(ActivityKind.Plc, "cpu", $"#{i}");
        }

        Assert.Equal(30, feed.Items.Count);
    }

    [Fact]
    public void Only_allowed_reactions_are_broadcast()
    {
        using var feed = new ActivityFeed(TimeProvider.System);

        Assert.True(feed.TryReact("🚀"));
        Assert.False(feed.TryReact("<script>"));
        Assert.Single(feed.Items);
    }
}
