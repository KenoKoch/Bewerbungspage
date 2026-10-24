using System.Diagnostics;
using System.Runtime.InteropServices;
using KenoKoch.BuildingBlocks.Persistence;
using Microsoft.Data.SqlClient;

namespace KenoKoch.Web.Hosting;

public sealed record HostingInfo(string Platform, bool IsAzure, string? Region, string? SiteName, string? InstanceId, string? Plan);

public sealed record ProcessInfo(
    string OperatingSystem,
    string Framework,
    string Architecture,
    int ProcessorCount,
    TimeSpan Uptime,
    long WorkingSetBytes,
    long ManagedHeapBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int ThreadPoolThreads,
    double CpuPercent,
    string Environment);

public sealed record DatabaseInfo(
    string Status,
    string? Message,
    double? LatencyMilliseconds,
    string? Engine,
    string? ServiceObjective,
    string? ServerVersion,
    string? DatabaseName,
    string Authentication);

public sealed record BuildDetails(string Version, string? Commit, DateTimeOffset? BuiltAt);

public sealed record RuntimeSnapshot(
    HostingInfo Hosting,
    ProcessInfo Process,
    DatabaseInfo? Database,
    BuildDetails Build,
    bool TelemetryEnabled,
    DateTimeOffset Timestamp);

/// <summary>
/// Liefert Hosting-, Prozess- und Datenbankinformationen für die Live-Anzeige "Unter der Haube".
/// Auf Azure App Service werden die vom Plattformdienst gesetzten Umgebungsvariablen ausgewertet.
/// </summary>
public sealed class RuntimeInfoService(
    IConfiguration configuration,
    IHostEnvironment environment,
    DatabaseState databaseState,
    TimeProvider timeProvider,
    ILogger<RuntimeInfoService> logger)
{
    private static readonly TimeSpan DatabaseProbeInterval = TimeSpan.FromSeconds(5);

    private readonly Lock _cpuGate = new();
    private readonly SemaphoreSlim _databaseGate = new(1, 1);
    private TimeSpan _lastCpuTime;
    private long _lastCpuTimestamp;
    private double _lastCpuPercent;
    private DatabaseInfo? _cachedDatabase;
    private DateTimeOffset _cachedDatabaseAt;

    public HostingInfo Hosting { get; } = DetectHosting();

    public bool TelemetryEnabled => !string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]);

    public async Task<RuntimeSnapshot> GetSnapshotAsync(bool includeDatabase, CancellationToken cancellationToken = default)
    {
        var database = includeDatabase ? await GetDatabaseInfoAsync(cancellationToken) : null;

        return new RuntimeSnapshot(
            Hosting,
            GetProcessInfo(),
            database,
            new BuildDetails(BuildInfo.Version, BuildInfo.Commit, BuildInfo.BuiltAt),
            TelemetryEnabled,
            timeProvider.GetUtcNow());
    }

    public ProcessInfo GetProcessInfo()
    {
        using var process = Process.GetCurrentProcess();

        return new ProcessInfo(
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            DateTime.Now - process.StartTime,
            process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            ThreadPool.ThreadCount,
            MeasureCpu(process),
            environment.EnvironmentName);
    }

    public async Task<DatabaseInfo> GetDatabaseInfoAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
        var authentication = DescribeAuthentication(connectionString);

        if (!databaseState.IsReady || string.IsNullOrWhiteSpace(connectionString))
        {
            return new DatabaseInfo(databaseState.Status.ToString(), databaseState.Message, null, null, null, null, null, authentication);
        }

        if (_cachedDatabase is not null && timeProvider.GetUtcNow() - _cachedDatabaseAt < DatabaseProbeInterval)
        {
            return _cachedDatabase;
        }

        await _databaseGate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedDatabase is not null && timeProvider.GetUtcNow() - _cachedDatabaseAt < DatabaseProbeInterval)
            {
                return _cachedDatabase;
            }

            _cachedDatabase = await ProbeAsync(connectionString, authentication, cancellationToken);
            _cachedDatabaseAt = timeProvider.GetUtcNow();
            return _cachedDatabase;
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    private async Task<DatabaseInfo> ProbeAsync(string connectionString, string authentication, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DB_NAME(),
                   CAST(SERVERPROPERTY('EngineEdition') AS int),
                   CAST(SERVERPROPERTY('Edition') AS nvarchar(128)),
                   CAST(DATABASEPROPERTYEX(DB_NAME(), 'ServiceObjective') AS nvarchar(128)),
                   CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128));
            """;

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);
            var stopwatch = Stopwatch.StartNew();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            stopwatch.Stop();

            var engineEdition = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            var edition = reader.IsDBNull(2) ? null : reader.GetString(2);
            var engine = engineEdition switch
            {
                5 => "Azure SQL Database",
                8 => "Azure SQL Managed Instance",
                _ => edition is null ? "SQL Server" : $"SQL Server ({edition})",
            };

            return new DatabaseInfo(
                "Ready",
                "Verbunden",
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                engine,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(0) ? null : reader.GetString(0),
                authentication);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Datenbank-Probe fehlgeschlagen.");
            return new DatabaseInfo("Unavailable", "Datenbank gerade nicht erreichbar", null, null, null, null, null, authentication);
        }
    }

    private double MeasureCpu(Process process)
    {
        lock (_cpuGate)
        {
            var now = Stopwatch.GetTimestamp();
            var cpu = process.TotalProcessorTime;

            if (_lastCpuTimestamp != 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(_lastCpuTimestamp, now).TotalMilliseconds;
                if (elapsed > 250)
                {
                    var used = (cpu - _lastCpuTime).TotalMilliseconds;
                    _lastCpuPercent = Math.Clamp(used / (elapsed * Environment.ProcessorCount) * 100, 0, 100);
                }
                else
                {
                    return _lastCpuPercent;
                }
            }

            _lastCpuTime = cpu;
            _lastCpuTimestamp = now;
            return _lastCpuPercent;
        }
    }

    private static string DescribeAuthentication(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "–";
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return builder.Authentication switch
            {
                SqlAuthenticationMethod.ActiveDirectoryManagedIdentity or SqlAuthenticationMethod.ActiveDirectoryMSI => "Managed Identity (passwortlos)",
                SqlAuthenticationMethod.ActiveDirectoryDefault => "Microsoft Entra ID (DefaultAzureCredential)",
                _ when builder.IntegratedSecurity => "Windows-Authentifizierung",
                _ => "SQL-Login",
            };
        }
        catch (ArgumentException)
        {
            return "unbekannt";
        }
    }

    private static HostingInfo DetectHosting()
    {
        var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
        if (string.IsNullOrWhiteSpace(siteName))
        {
            return new HostingInfo(
                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ? "Container" : "Lokale Entwicklung",
                IsAzure: false,
                Region: null,
                SiteName: null,
                InstanceId: null,
                Plan: null);
        }

        var instance = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID");
        return new HostingInfo(
            "Azure App Service",
            IsAzure: true,
            Region: Environment.GetEnvironmentVariable("REGION_NAME"),
            SiteName: siteName,
            InstanceId: string.IsNullOrEmpty(instance) ? null : instance[..Math.Min(8, instance.Length)],
            Plan: Environment.GetEnvironmentVariable("WEBSITE_SKU"));
    }
}
