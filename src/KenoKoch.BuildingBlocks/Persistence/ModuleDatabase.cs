using Microsoft.EntityFrameworkCore;

namespace KenoKoch.BuildingBlocks.Persistence;

/// <summary>Zugriff auf Migrationen und Erreichbarkeit der Datenbank eines Moduls.</summary>
public interface IModuleDatabase
{
    string Schema { get; }

    Type ContextType { get; }

    /// <summary>Im Modul-Assembly definierte Migrationen (ohne Datenbankzugriff ermittelbar).</summary>
    IReadOnlyList<string> DefinedMigrations { get; }

    Task MigrateAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken);
}

internal sealed class ModuleDatabase<TContext>(string schema, IDbContextFactory<TContext> factory) : IModuleDatabase
    where TContext : DbContext
{
    private readonly Lazy<IReadOnlyList<string>> _definedMigrations = new(() =>
    {
        using var db = factory.CreateDbContext();
        return db.Database.GetMigrations().ToArray();
    });

    public string Schema => schema;

    public Type ContextType => typeof(TContext);

    public IReadOnlyList<string> DefinedMigrations => _definedMigrations.Value;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
    }
}
