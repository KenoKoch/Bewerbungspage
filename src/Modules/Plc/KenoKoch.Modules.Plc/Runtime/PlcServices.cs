using KenoKoch.BuildingBlocks.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KenoKoch.Modules.Plc.Runtime;

public sealed class PlcOptions
{
    public const string SectionName = "Plc";

    /// <summary>Maximal gleichzeitig laufende Besucher-SPSen (Schutz der Server-Ressourcen).</summary>
    public int MaxConcurrentSessions { get; set; } = 50;

    /// <summary>Nach dieser Zeit ohne Bedienung schaltet die CPU automatisch in STOP.</summary>
    public int IdleTimeoutMinutes { get; set; } = 10;
}

/// <summary>Zählt die laufenden SPS-Instanzen und begrenzt sie.</summary>
public sealed class PlcSessionRegistry(IOptions<PlcOptions> options)
{
    private int _active;

    public int ActiveSessions => Volatile.Read(ref _active);

    public int MaxSessions => Math.Max(1, options.Value.MaxConcurrentSessions);

    public event Action? Changed;

    public bool TryEnter()
    {
        while (true)
        {
            var current = Volatile.Read(ref _active);
            if (current >= MaxSessions)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _active, current + 1, current) == current)
            {
                Changed?.Invoke();
                return true;
            }
        }
    }

    public void Leave()
    {
        Interlocked.Decrement(ref _active);
        Changed?.Invoke();
    }
}

/// <summary>Erzeugt SPS-Instanzen für Besucher (null, wenn das Limit erreicht ist).</summary>
public sealed class PlcRuntimeFactory(
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    PlcSessionRegistry registry,
    IOptions<PlcOptions> options)
{
    public PlcRuntime? TryCreate()
    {
        if (!registry.TryEnter())
        {
            return null;
        }

        return new PlcRuntime(
            eventBus,
            timeProvider,
            loggerFactory.CreateLogger<PlcRuntime>(),
            TimeSpan.FromMinutes(Math.Max(1, options.Value.IdleTimeoutMinutes)),
            registry.Leave);
    }
}

/// <summary>Stellt den eingebetteten Quelltext (SCL und C#) für die Anzeige bereit.</summary>
public sealed class PlcSourceCode
{
    public string Scl { get; } = Read("FB_TankControl.scl");

    public string CSharp { get; } = Read("TankControlFb.cs");

    private static string Read(string resourceName)
    {
        using var stream = typeof(PlcSourceCode).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Eingebettete Ressource '{resourceName}' fehlt.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
