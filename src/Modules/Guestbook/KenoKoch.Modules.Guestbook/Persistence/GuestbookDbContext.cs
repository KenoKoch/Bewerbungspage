using KenoKoch.BuildingBlocks.Persistence;
using KenoKoch.Modules.Guestbook.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KenoKoch.Modules.Guestbook.Persistence;

public sealed class GuestbookDbContext(DbContextOptions<GuestbookDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "guestbook";

    public override string Schema => SchemaName;

    public DbSet<GuestbookEntry> Entries => Set<GuestbookEntry>();
}

internal sealed class GuestbookEntryConfiguration : IEntityTypeConfiguration<GuestbookEntry>
{
    public void Configure(EntityTypeBuilder<GuestbookEntry> builder)
    {
        builder.ToTable("Entries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(GuestbookEntry.NameMaxLength).IsRequired();
        builder.Property(e => e.Company).HasMaxLength(GuestbookEntry.CompanyMaxLength);
        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Message).HasMaxLength(GuestbookEntry.MessageMaxLength).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasIndex(e => new { e.IsApproved, e.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Entries_IsApproved_CreatedAt");

        builder.HasData(new
        {
            Id = 1,
            Name = "Keno Koch",
            Company = (string?)null,
            Role = VisitorRole.Developer,
            Message = "Willkommen in meinem Gästebuch! Jeder Eintrag wird mit Entity Framework Core in Azure SQL gespeichert – das erzeugte SQL sehen Sie direkt unter dem Formular. Ich freue mich über Ihren Gruß.",
            CreatedAt = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero),
            IsApproved = true,
        });
    }
}

/// <summary>Wird nur von <c>dotnet ef</c> beim Erzeugen von Migrationen verwendet.</summary>
internal sealed class GuestbookDbContextDesignTimeFactory : IDesignTimeDbContextFactory<GuestbookDbContext>
{
    public GuestbookDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GuestbookDbContext>()
            .UseSqlServer(
                @"Server=(localdb)\MSSQLLocalDB;Database=KenoKoch;Trusted_Connection=True;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", GuestbookDbContext.SchemaName))
            .Options;

        return new GuestbookDbContext(options);
    }
}
