using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KenoKoch.BuildingBlocks.Persistence;

/// <summary>
/// Wendet die Migrationen aller Module im Hintergrund an. Der Start der Anwendung wird dadurch
/// nie blockiert – wichtig für Azure SQL Serverless, das nach Auto-Pause bis zu einer Minute zum Aufwachen braucht.
/// </summary>
internal sealed class DatabaseInitializer(
    IEnumerable<IModuleDatabase> databases,
    DatabaseState state,
    IConfiguration configuration,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseInitializer> logger) : BackgroundService
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(DatabaseOptions.ConnectionStringName)))
        {
            state.Set(DatabaseStatus.NotConfigured, "Keine Verbindungszeichenfolge 'ConnectionStrings:Sql' konfiguriert.");
            logger.LogWarning("Keine Datenbank konfiguriert – datenbankgestützte Funktionen sind deaktiviert.");
            return;
        }

        try
        {
            await InitializeWithRetriesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Regulärer Shutdown während eines Wiederholungsversuchs.
        }
    }

    private async Task InitializeWithRetriesAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;
            try
            {
                state.Set(DatabaseStatus.Initializing, attempt == 1
                    ? "Verbindung wird aufgebaut …"
                    : $"Neuer Verbindungsversuch ({attempt}) …");

                foreach (var database in databases)
                {
                    if (options.Value.MigrateOnStartup)
                    {
                        await database.MigrateAsync(stoppingToken);
                        logger.LogInformation("Schema [{Schema}] ist auf dem aktuellen Stand.", database.Schema);
                    }
                    else
                    {
                        await database.GetPendingMigrationsAsync(stoppingToken);
                    }
                }

                state.Set(DatabaseStatus.Ready, "Verbunden");
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Datenbank nicht erreichbar (Versuch {Attempt}). Nächster Versuch in {Delay}.", attempt, delay);
                state.Set(DatabaseStatus.Unavailable, "Datenbank derzeit nicht erreichbar – neuer Versuch läuft automatisch.");

                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
            }
        }
    }
}
