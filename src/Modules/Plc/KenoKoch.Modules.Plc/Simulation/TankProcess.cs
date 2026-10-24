namespace KenoKoch.Modules.Plc.Simulation;

/// <summary>
/// Physik der Tankanlage im Zeitraffer (Faktor 20): Pumpe mit Hochlauf, Abfluss nach Torricelli (∝ √Füllhöhe),
/// verrauschter Analogwert wie bei einem echten Messumformer.
/// </summary>
public sealed class TankProcess
{
    public const double CapacityLiters = 2000;
    public const double TimeLapseFactor = 20;
    public const double HighHighLevel = 95;

    private const double PumpRatePercentPerSecond = 6.5;
    private const double OutflowRatePercentPerSecond = 4.5;
    private const double PumpRunUpSeconds = 0.6;

    private readonly Random _noise;

    public TankProcess(double initialLevel = 42, int? seed = null)
    {
        LevelPercent = initialLevel;
        _noise = seed is null ? new Random() : new Random(seed.Value);
    }

    public double LevelPercent { get; private set; }

    /// <summary>Abnahme durch den Verbraucher, 0 … 1.</summary>
    public double ConsumerDemand { get; set; } = 0.6;

    /// <summary>Simulierter Fehler: Motorschutzschalter der Pumpe hat ausgelöst.</summary>
    public bool MotorProtectionTripped { get; set; }

    /// <summary>Drehzahl der Pumpe 0 … 1 (Hochlauf/Auslauf).</summary>
    public double PumpSpeed { get; private set; }

    /// <summary>Zufluss in l/min (Echtzeit-Äquivalent).</summary>
    public double InflowLitersPerMinute { get; private set; }

    /// <summary>Abfluss in l/min (Echtzeit-Äquivalent).</summary>
    public double OutflowLitersPerMinute { get; private set; }

    public double Liters => LevelPercent / 100 * CapacityLiters;

    public bool LevelHighHigh => LevelPercent >= HighHighLevel;

    /// <summary>Analogwert wie von einer S7-Analogeingabe: 0 … 27648 (+ Messrauschen).</summary>
    public short LevelRaw =>
        (short)Math.Clamp(Math.Round(LevelPercent / 100 * 27648 + _noise.Next(-12, 13)), 0, 32767);

    public void Step(TimeSpan elapsed, bool pumpContactor, bool valveOpen)
    {
        var seconds = elapsed.TotalSeconds;
        var motorOn = pumpContactor && !MotorProtectionTripped;

        PumpSpeed = Math.Clamp(PumpSpeed + (motorOn ? 1 : -1) * seconds / PumpRunUpSeconds, 0, 1);

        var inflow = PumpSpeed * PumpRatePercentPerSecond;
        var outflow = valveOpen && LevelPercent > 0
            ? ConsumerDemand * OutflowRatePercentPerSecond * Math.Sqrt(Math.Clamp(LevelPercent / 60, 0, 2))
            : 0;

        LevelPercent = Math.Clamp(LevelPercent + (inflow - outflow) * seconds, 0, 100);

        // %/s im Zeitraffer → l/min in Echtzeit: 1 % = 20 l, Zeitraffer 20 ⇒ Faktor 60
        InflowLitersPerMinute = inflow * CapacityLiters / 100 * 60 / TimeLapseFactor;
        OutflowLitersPerMinute = outflow * CapacityLiters / 100 * 60 / TimeLapseFactor;
    }
}
