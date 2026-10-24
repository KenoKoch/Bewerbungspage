using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using KenoKoch.BuildingBlocks.Events;
using KenoKoch.BuildingBlocks.Persistence;
using KenoKoch.Modules.Guestbook.Contracts;
using KenoKoch.Modules.Guestbook.Domain;
using KenoKoch.Modules.Guestbook.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KenoKoch.Modules.Guestbook.Application;

/// <summary>Anwendungsdienst des Gästebuchs: Lesen, Validieren, Speichern, Event veröffentlichen.</summary>
public sealed class GuestbookService(
    IDbContextFactory<GuestbookDbContext> dbFactory,
    IEventBus eventBus,
    IOptions<GuestbookOptions> options,
    SubmissionThrottle throttle,
    TimeProvider timeProvider,
    ILogger<GuestbookService> logger)
{
    public bool RequiresApproval => options.Value.RequireApproval;

    public async Task<GuestbookPage> GetLatestAsync(int take, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 50);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Entries
            .AsNoTracking()
            .Where(e => e.IsApproved)
            .OrderByDescending(e => e.CreatedAt)
            .Take(take);

        var sql = query.ToQueryString();
        var stopwatch = Stopwatch.StartNew();
        var entries = await query.ToListAsync(cancellationToken);
        stopwatch.Stop();

        var total = await db.Entries.CountAsync(e => e.IsApproved, cancellationToken);

        return new GuestbookPage(
            entries.Select(GuestbookEntryDto.From).ToArray(),
            total,
            new QueryInsight(sql, stopwatch.Elapsed, entries.Count));
    }

    public async Task<SubmitResult> SubmitAsync(GuestbookSubmission submission, string visitorKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (!string.IsNullOrEmpty(submission.Website))
        {
            logger.LogInformation("Gästebuch: Honeypot-Feld ausgefüllt, Eintrag verworfen.");
            return SubmitResult.Fail("Ihr Eintrag konnte leider nicht gespeichert werden.");
        }

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(submission, new ValidationContext(submission), validationResults, validateAllProperties: true))
        {
            return SubmitResult.Fail(validationResults[0].ErrorMessage ?? "Bitte prüfen Sie Ihre Eingaben.");
        }

        if (GuestbookEntry.ContainsLink(submission.Message) || GuestbookEntry.ContainsLink(submission.Name) || GuestbookEntry.ContainsLink(submission.Company))
        {
            return SubmitResult.Fail("Links sind im Gästebuch leider nicht erlaubt – das hält Spam fern.");
        }

        if (!throttle.TryAcquire(visitorKey))
        {
            return SubmitResult.Fail("Es wurden gerade sehr viele Einträge geschrieben. Bitte versuchen Sie es in ein paar Minuten erneut.");
        }

        var requireApproval = options.Value.RequireApproval;
        var entry = GuestbookEntry.Create(
            submission.Name,
            submission.Company,
            submission.Role,
            submission.Message,
            timeProvider.GetUtcNow(),
            approved: !requireApproval);

        using var capture = SqlCapture.Begin();
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            db.Entries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
        }

        await eventBus.PublishAsync(new GuestbookEntrySubmitted(entry.Id, entry.Role.Label(), requireApproval), cancellationToken);
        logger.LogInformation("Gästebuch: Eintrag {EntryId} gespeichert (Freigabe erforderlich: {RequiresApproval}).", entry.Id, requireApproval);

        return SubmitResult.Ok(GuestbookEntryDto.From(entry), capture.Commands);
    }

    /// <summary>Beschreibt den Datenbankserver für die Anzeige (ohne Verbindung aufzubauen).</summary>
    public string DescribeServer()
    {
        using var db = dbFactory.CreateDbContext();
        var dataSource = db.Database.GetDbConnection().DataSource ?? string.Empty;

        return dataSource switch
        {
            _ when dataSource.Contains("database.windows.net", StringComparison.OrdinalIgnoreCase) => "Azure SQL Database",
            _ when dataSource.Contains("localdb", StringComparison.OrdinalIgnoreCase) => "SQL Server LocalDB",
            "" => "SQL Server",
            _ => "SQL Server",
        };
    }
}
