namespace KenoKoch.Modules.Plc.Runtime;

public enum CpuMode
{
    Stop,
    Run,
}

public enum PlcCommandKind
{
    SetCpuMode,
    Start,
    Stop,
    Acknowledge,
    SetAutoMode,
    SetManualPump,
    SetManualValve,
    ToggleEmergencyStop,
    SetMotorProtectionTripped,
    SetConsumerDemand,
}

/// <summary>Bedienhandlung von HMI, Feldgerät oder CPU-Schalter. Wird zu Beginn des nächsten Zyklus verarbeitet.</summary>
public readonly record struct PlcCommand(PlcCommandKind Kind, double Value = 0)
{
    public bool Flag => Value != 0;

    public static PlcCommand CpuRun(bool run) => new(PlcCommandKind.SetCpuMode, run ? 1 : 0);

    public static PlcCommand PressStart() => new(PlcCommandKind.Start);

    public static PlcCommand PressStop() => new(PlcCommandKind.Stop);

    public static PlcCommand PressAcknowledge() => new(PlcCommandKind.Acknowledge);

    public static PlcCommand Auto(bool auto) => new(PlcCommandKind.SetAutoMode, auto ? 1 : 0);

    public static PlcCommand ManualPump(bool on) => new(PlcCommandKind.SetManualPump, on ? 1 : 0);

    public static PlcCommand ManualValve(bool open) => new(PlcCommandKind.SetManualValve, open ? 1 : 0);

    public static PlcCommand ToggleEmergencyStop() => new(PlcCommandKind.ToggleEmergencyStop);

    public static PlcCommand MotorProtection(bool tripped) => new(PlcCommandKind.SetMotorProtectionTripped, tripped ? 1 : 0);

    public static PlcCommand Demand(double demand) => new(PlcCommandKind.SetConsumerDemand, demand);
}

public sealed record AlarmView(int Number, AlarmClass Class, string Text, string State, DateTimeOffset CameAt, bool Active, bool Acknowledged)
{
    internal static AlarmView From(AlarmEntry entry) => new(
        entry.Definition.Number,
        entry.Definition.Class,
        entry.Definition.Text,
        entry.State,
        entry.CameAt,
        entry.IsActive,
        entry.IsAcknowledged);
}

/// <summary>Ein Punkt im Prozessabbild, z. B. %I0.0 "Start".</summary>
public sealed record IoPoint(string Address, string Symbol, string Comment, string Value, bool? Bit);

/// <summary>Ein Trendwert alle 500 ms.</summary>
public readonly record struct TrendSample(double Seconds, double Level, bool Pump, bool Valve);

/// <summary>Unveränderlicher Zustand nach einem SPS-Zyklus – Grundlage für das Rendering.</summary>
public sealed record PlcSnapshot
{
    public required string SessionTag { get; init; }

    public required CpuMode Mode { get; init; }

    public long CycleCount { get; init; }

    public double LastCycleMicroseconds { get; init; }

    public double MaxCycleMicroseconds { get; init; }

    public double AverageCycleMicroseconds { get; init; }

    public bool Running { get; init; }

    public bool AutoMode { get; init; }

    public bool ManualPump { get; init; }

    public bool ManualValve { get; init; }

    public bool EmergencyStopPressed { get; init; }

    public bool MotorProtectionTripped { get; init; }

    public double ConsumerDemand { get; init; }

    public double Level { get; init; }

    public short LevelRaw { get; init; }

    public double Liters { get; init; }

    public bool LevelHighHighSwitch { get; init; }

    public bool Pump { get; init; }

    public bool Valve { get; init; }

    public bool Horn { get; init; }

    public bool FaultLamp { get; init; }

    public bool RunLamp { get; init; }

    public bool DryRunWarning { get; init; }

    public double PumpSpeed { get; init; }

    public double InflowLitersPerMinute { get; init; }

    public double OutflowLitersPerMinute { get; init; }

    public IReadOnlyList<AlarmView> Alarms { get; init; } = [];

    public IReadOnlyList<AlarmView> History { get; init; } = [];

    public IReadOnlyList<TrendSample> Trend { get; init; } = [];

    public IReadOnlyList<IoPoint> Inputs { get; init; } = [];

    public IReadOnlyList<IoPoint> Outputs { get; init; } = [];

    public DateTimeOffset Timestamp { get; init; }

    public double SimulationSeconds { get; init; }

    public bool IdleStopped { get; init; }
}
