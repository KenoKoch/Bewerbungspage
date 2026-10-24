namespace KenoKoch.BuildingBlocks.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Name der Verbindungszeichenfolge unter <c>ConnectionStrings</c>.</summary>
    public const string ConnectionStringName = "Sql";

    /// <summary>Migrationen aller Module beim Start im Hintergrund anwenden.</summary>
    public bool MigrateOnStartup { get; set; } = true;
}
