using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KenoKoch.BuildingBlocks.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    internal static IServiceCollection AddModulePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.TryAddSingleton<DatabaseState>();
        services.TryAddSingleton<SqlCaptureInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<DatabaseInitializer>();
        return services;
    }

    /// <summary>
    /// Registriert den DbContext eines Moduls: eigenes Schema, eigene Migrationshistorie,
    /// Wiederholungsstrategie für transiente Azure-SQL-Fehler und einen Health Check.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : ModuleDbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        services.AddDbContextFactory<TContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);

            void ConfigureSqlServer(SqlServerDbContextOptionsBuilder sql)
            {
                sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema);
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(30);
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlServer(ConfigureSqlServer);
            }
            else
            {
                options.UseSqlServer(connectionString, ConfigureSqlServer);
            }

            options.AddInterceptors(serviceProvider.GetRequiredService<SqlCaptureInterceptor>());
        });

        services.AddSingleton<IModuleDatabase>(sp =>
            new ModuleDatabase<TContext>(schema, sp.GetRequiredService<IDbContextFactory<TContext>>()));

        services.AddHealthChecks().Add(new HealthCheckRegistration(
            name: $"sql-{schema}",
            factory: sp => new ModuleDatabaseHealthCheck(
                sp.GetServices<IModuleDatabase>().First(d => d.ContextType == typeof(TContext)),
                sp.GetRequiredService<DatabaseState>()),
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready", "database"]));

        return services;
    }
}
