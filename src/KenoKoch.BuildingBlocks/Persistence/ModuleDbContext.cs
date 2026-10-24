using Microsoft.EntityFrameworkCore;

namespace KenoKoch.BuildingBlocks.Persistence;

/// <summary>
/// Basis für die DbContexts der Module. Jedes Modul arbeitet in seinem eigenen Schema
/// und führt eine eigene Migrationshistorie – so bleiben die Datenmodelle entkoppelt,
/// obwohl alle Module dieselbe Datenbank nutzen.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    public abstract string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
