using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KenoKoch.BuildingBlocks.Persistence;

/// <summary>Health Check pro Modul: Datenbank erreichbar und Schema auf dem aktuellen Migrationsstand?</summary>
internal sealed class ModuleDatabaseHealthCheck(IModuleDatabase database, DatabaseState state) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (state.Status == DatabaseStatus.NotConfigured)
        {
            return HealthCheckResult.Degraded("Keine Datenbank konfiguriert.");
        }

        try
        {
            var pending = await database.GetPendingMigrationsAsync(cancellationToken);
            return pending.Count == 0
                ? HealthCheckResult.Healthy($"Schema [{database.Schema}] ist aktuell.")
                : HealthCheckResult.Degraded($"Schema [{database.Schema}]: {pending.Count} ausstehende Migration(en).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"Schema [{database.Schema}]: Datenbank nicht erreichbar.", ex);
        }
    }
}
