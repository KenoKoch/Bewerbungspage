using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KenoKoch.BuildingBlocks.Persistence;

public sealed record CapturedSqlParameter(string Name, string Value, string DbType);

/// <summary>Ein von EF Core tatsächlich an SQL Server gesendeter Befehl.</summary>
public sealed record CapturedSqlCommand(
    string CommandText,
    IReadOnlyList<CapturedSqlParameter> Parameters,
    TimeSpan Duration,
    DateTimeOffset ExecutedAt);

/// <summary>Sammelt alle SQL-Befehle, die innerhalb des Scopes (async-Fluss) ausgeführt werden.</summary>
public sealed class SqlCaptureScope : IDisposable
{
    private readonly List<CapturedSqlCommand> _commands = [];
    private readonly SqlCaptureScope? _parent;

    internal SqlCaptureScope(SqlCaptureScope? parent) => _parent = parent;

    public IReadOnlyList<CapturedSqlCommand> Commands
    {
        get
        {
            lock (_commands)
            {
                return _commands.ToArray();
            }
        }
    }

    internal void Add(CapturedSqlCommand command)
    {
        lock (_commands)
        {
            _commands.Add(command);
        }
    }

    public void Dispose() => SqlCapture.End(this, _parent);
}

/// <summary>
/// Macht sichtbar, welches SQL EF Core erzeugt – für die Live-Demo auf der Website.
/// Die Erfassung ist an den aktuellen async-Fluss gebunden und betrifft keine anderen Anfragen.
/// </summary>
public static class SqlCapture
{
    private static readonly AsyncLocal<SqlCaptureScope?> CurrentScope = new();

    internal static SqlCaptureScope? Current => CurrentScope.Value;

    public static SqlCaptureScope Begin()
    {
        var scope = new SqlCaptureScope(CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    internal static void End(SqlCaptureScope scope, SqlCaptureScope? parent)
    {
        if (ReferenceEquals(CurrentScope.Value, scope))
        {
            CurrentScope.Value = parent;
        }
    }
}

/// <summary>EF-Core-Interceptor, der ausgeführte Befehle an den aktiven <see cref="SqlCaptureScope"/> meldet.</summary>
public sealed class SqlCaptureInterceptor : DbCommandInterceptor
{
    private const int MaxValueLength = 120;

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Capture(command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Capture(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Capture(command, eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Capture(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Capture(command, eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Capture(command, eventData);
        return ValueTask.FromResult(result);
    }

    private static void Capture(DbCommand command, CommandExecutedEventData eventData)
    {
        var scope = SqlCapture.Current;
        if (scope is null)
        {
            return;
        }

        var parameters = command.Parameters
            .Cast<DbParameter>()
            .Select(p => new CapturedSqlParameter(p.ParameterName, Format(p.Value), p.DbType.ToString()))
            .ToArray();

        scope.Add(new CapturedSqlCommand(command.CommandText, parameters, eventData.Duration, DateTimeOffset.UtcNow));
    }

    private static string Format(object? value) => value switch
    {
        null or DBNull => "NULL",
        string s => "N'" + Truncate(s).Replace("'", "''", StringComparison.Ordinal) + "'",
        bool b => b ? "1" : "0",
        DateTimeOffset dto => "'" + dto.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture) + "'",
        DateTime dt => "'" + dt.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "'",
        DateOnly d => "'" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Truncate(value.ToString() ?? string.Empty),
    };

    private static string Truncate(string value) =>
        value.Length <= MaxValueLength ? value : string.Concat(value.AsSpan(0, MaxValueLength), "…");
}
