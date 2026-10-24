using KenoKoch.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KenoKoch.Modules.Analytics.Persistence;

/// <summary>
/// Tageszähler pro Kennzahl (z. B. "page:/demos" oder "event:plc-started").
/// Bewusst datensparsam: keine IP-Adressen, keine Cookies, keine einzelnen Besuche – nur Summen pro Tag.
/// </summary>
public sealed class DailyMetric
{
    public DateOnly Date { get; set; }

    public string Metric { get; set; } = string.Empty;

    public int Count { get; set; }
}

public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "analytics";

    public override string Schema => SchemaName;

    public DbSet<DailyMetric> DailyMetrics => Set<DailyMetric>();
}

internal sealed class DailyMetricConfiguration : IEntityTypeConfiguration<DailyMetric>
{
    public void Configure(EntityTypeBuilder<DailyMetric> builder)
    {
        builder.ToTable("DailyMetrics");
        builder.HasKey(m => new { m.Date, m.Metric });
        builder.Property(m => m.Metric).HasMaxLength(120).IsRequired();
        builder.Property(m => m.Count).IsRequired();
        builder.HasIndex(m => m.Metric).HasDatabaseName("IX_DailyMetrics_Metric");
    }
}

/// <summary>Wird nur von <c>dotnet ef</c> beim Erzeugen von Migrationen verwendet.</summary>
internal sealed class AnalyticsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlServer(
                @"Server=(localdb)\MSSQLLocalDB;Database=KenoKoch;Trusted_Connection=True;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AnalyticsDbContext.SchemaName))
            .Options;

        return new AnalyticsDbContext(options);
    }
}
