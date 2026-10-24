using System.Collections.Concurrent;
using System.Diagnostics;
using KenoKoch.UI.Formatting;
using System.Security.Cryptography;
using KenoKoch.BuildingBlocks.Events;
using KenoKoch.Modules.Plc.Contracts;
using KenoKoch.Modules.Plc.Program;
using KenoKoch.Modules.Plc.Simulation;
using Microsoft.Extensions.Logging;

namespace KenoKoch.Modules.Plc.Runtime;

/// <summary>
/// Soft-SPS pro Besucher. Ablauf je Zyklus wie in der S7:
/// Bedienbefehle übernehmen → Prozessabbild der Eingänge lesen → FB ausführen → Ausgänge schreiben → Meldesystem → Trend.
/// Der Zustand wird nach jedem Zyklus als unveränderlicher <see cref="PlcSnapshot"/> veröffentlicht.
/// </summary>
public sealed class PlcRuntime : IAsyncDisposable
{
    public static readonly TimeSpan CycleTime = TimeSpan.FromMilliseconds(100);

    private const int TrendCapacity = 120;
    private const int TrendEveryCycles = 5;
    private static readonly TimeSpan StartEventInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AlarmEventInterval = TimeSpan.FromSeconds(15);

    private readonly TankControlFb _fb = new();
    private readonly TankProcess _process;
    private readonly AlarmManager _alarms = new();
    private readonly ConcurrentQueue<PlcCommand> _commands = new();
    private readonly Queue<TrendSample> _trend = new(TrendCapacity + 1);
    private readonly IEventBus? _eventBus;
    private readonly TimeProvider _time;
    private readonly ILogger? _logger;
    private readonly TimeSpan _idleTimeout;
    private readonly Action? _onDispose;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    private CpuMode _mode = CpuMode.Stop;
    private bool _autoMode = true;
    private bool _manualPump;
    private bool _manualValve;
    private bool _emergencyStopPressed;
    private bool _startPulse;
    private bool _stopPulse;
    private bool _ackPulse;

    private bool _qPump;
    private bool _qValve;
    private bool _qHorn;
    private bool _qFaultLamp;
    private bool _qRunLamp;

    private long _cycleCount;
    private long _scanCounter;
    private double _lastCycleUs;
    private double _maxCycleUs;
    private double _avgCycleUs;
    private double _simulationSeconds;
    private DateTimeOffset _lastInteraction;
    private bool _idleStopped;
    private DateTimeOffset _lastStartEvent = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAlarmEvent = DateTimeOffset.MinValue;
    private int _disposed;

    public PlcRuntime(
        IEventBus? eventBus,
        TimeProvider timeProvider,
        ILogger? logger = null,
        TimeSpan? idleTimeout = null,
        Action? onDispose = null,
        TankProcess? process = null)
    {
        _eventBus = eventBus;
        _time = timeProvider;
        _logger = logger;
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(10);
        _onDispose = onDispose;
        _process = process ?? new TankProcess();
        _lastInteraction = timeProvider.GetUtcNow();
        ReadInputs();
        Snapshot = BuildSnapshot(_lastInteraction);
    }

    /// <summary>Kurze, anonyme Kennung dieser SPS-Instanz, z. B. "4F2A".</summary>
    public string SessionTag { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(2));

    public PlcSnapshot Snapshot { get; private set; }

    /// <summary>Wird nach jedem Zyklus ausgelöst (Thread-Pool-Thread).</summary>
    public event Action? Updated;

    /// <summary>Vorschaubild für das Prerendering – ohne Laufzeit und ohne Ressourcenverbrauch.</summary>
    public static PlcSnapshot Preview(TimeProvider timeProvider) => new PlcRuntime(null, timeProvider).Snapshot;

    /// <summary>
    /// Statisches Schaubild mit laufender Anlage (Pumpe fördert, Ventil offen) für die Startseite.
    /// Die Zyklen werden synchron simuliert – ohne Timer, ohne Besucher-Instanz.
    /// </summary>
    public static PlcSnapshot Showcase(TimeProvider timeProvider)
    {
        var runtime = new PlcRuntime(null, timeProvider, process: new TankProcess(initialLevel: 24, seed: 7));
        runtime.Send(PlcCommand.CpuRun(true));
        runtime.Scan(CycleTime);
        runtime.Send(PlcCommand.PressStart());

        for (var cycle = 0; cycle < 600; cycle++)
        {
            runtime.Scan(CycleTime);
            var snapshot = runtime.Snapshot;
            if (snapshot is { Pump: true, Valve: true, PumpSpeed: >= 1, Level: > 40 })
            {
                break;
            }
        }

        return runtime.Snapshot;
    }

    public void Start() => _loop ??= RunAsync(_cts.Token);

    public void Send(PlcCommand command)
    {
        _commands.Enqueue(command);
        _lastInteraction = _time.GetUtcNow();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(CycleTime, _time);
        var last = _time.GetTimestamp();

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var now = _time.GetTimestamp();
                var elapsed = _time.GetElapsedTime(last, now);
                last = now;

                // Nach einem Hänger (z. B. GC-Pause) nicht "springen".
                Scan(elapsed > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : elapsed);
                RaiseUpdated();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Regulär beendet.
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SPS-Laufzeit {SessionTag} wurde unerwartet beendet.", SessionTag);
        }
    }

    /// <summary>Ein kompletter SPS-Zyklus (für Tests direkt aufrufbar).</summary>
    internal void Scan(TimeSpan elapsed)
    {
        var now = _time.GetUtcNow();
        ApplyCommands(now);

        if (_mode == CpuMode.Run && now - _lastInteraction > _idleTimeout)
        {
            _mode = CpuMode.Stop;
            _idleStopped = true;
        }

        // 1) Prozessabbild der Eingänge (PAE)
        ReadInputs();
        _startPulse = _stopPulse = _ackPulse = false;

        // 2) Programm ausführen – nur in RUN; in STOP sind alle Ausgänge abgeschaltet
        if (_mode == CpuMode.Run)
        {
            var started = Stopwatch.GetTimestamp();
            _fb.Execute();
            var microseconds = Stopwatch.GetElapsedTime(started).TotalMicroseconds;

            _cycleCount++;
            _lastCycleUs = microseconds;
            _maxCycleUs = Math.Max(_maxCycleUs, microseconds);
            _avgCycleUs = _avgCycleUs == 0 ? microseconds : _avgCycleUs * 0.95 + microseconds * 0.05;

            (_qPump, _qValve, _qHorn, _qFaultLamp, _qRunLamp) = (_fb.Pump, _fb.Valve, _fb.Horn, _fb.FaultLamp, _fb.RunLamp);
        }
        else
        {
            (_qPump, _qValve, _qHorn, _qFaultLamp, _qRunLamp) = (false, false, false, false, false);
        }

        // 3) Prozessabbild der Ausgänge (PAA) → Anlage
        _process.Step(elapsed, _qPump, _qValve);
        _simulationSeconds += elapsed.TotalSeconds;

        // 4) Meldesystem (HMI)
        var running = _mode == CpuMode.Run;
        var raised = _alarms.Evaluate(now,
        [
            (AlarmManager.EmergencyStop, running && _fb.AlarmEmergencyStop),
            (AlarmManager.MotorProtection, running && _fb.AlarmMotorProtection),
            (AlarmManager.LevelHighHigh, running && _fb.AlarmLevelHighHigh),
            (AlarmManager.DryRun, running && _fb.WarningDryRun),
            (AlarmManager.CpuStop, !running),
        ]);

        var fault = raised.FirstOrDefault(a => a.Class == AlarmClass.Fault);
        if (fault is not null && now - _lastAlarmEvent > AlarmEventInterval)
        {
            _lastAlarmEvent = now;
            Publish(new PlcAlarmRaised(SessionTag, fault.Number, fault.Text));
        }

        // 5) Trend alle 500 ms
        if (++_scanCounter % TrendEveryCycles == 0)
        {
            _trend.Enqueue(new TrendSample(_simulationSeconds, _process.LevelPercent, _qPump, _qValve));
            while (_trend.Count > TrendCapacity)
            {
                _trend.Dequeue();
            }
        }

        Snapshot = BuildSnapshot(now);
    }

    private void ApplyCommands(DateTimeOffset now)
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command.Kind)
            {
                case PlcCommandKind.SetCpuMode when command.Flag && _mode == CpuMode.Stop:
                    _mode = CpuMode.Run;
                    _idleStopped = false;
                    if (now - _lastStartEvent > StartEventInterval)
                    {
                        _lastStartEvent = now;
                        Publish(new PlcStarted(SessionTag));
                    }

                    break;
                case PlcCommandKind.SetCpuMode when !command.Flag:
                    _mode = CpuMode.Stop;
                    break;
                case PlcCommandKind.Start:
                    _startPulse = true;
                    break;
                case PlcCommandKind.Stop:
                    _stopPulse = true;
                    break;
                case PlcCommandKind.Acknowledge:
                    _ackPulse = true;
                    _alarms.AcknowledgeAll(now);
                    break;
                case PlcCommandKind.SetAutoMode:
                    _autoMode = command.Flag;
                    break;
                case PlcCommandKind.SetManualPump:
                    _manualPump = command.Flag;
                    break;
                case PlcCommandKind.SetManualValve:
                    _manualValve = command.Flag;
                    break;
                case PlcCommandKind.ToggleEmergencyStop:
                    _emergencyStopPressed = !_emergencyStopPressed;
                    break;
                case PlcCommandKind.SetMotorProtectionTripped:
                    _process.MotorProtectionTripped = command.Flag;
                    break;
                case PlcCommandKind.SetConsumerDemand:
                    _process.ConsumerDemand = Math.Clamp(command.Value, 0, 1);
                    break;
            }
        }
    }

    private void ReadInputs()
    {
        _fb.Start = _startPulse;
        _fb.Stop = !_stopPulse;
        _fb.EmergencyStopOk = !_emergencyStopPressed;
        _fb.Acknowledge = _ackPulse;
        _fb.AutoMode = _autoMode;
        _fb.ManualPump = _manualPump;
        _fb.ManualValve = _manualValve;
        _fb.LevelHighHigh = _process.LevelHighHigh;
        _fb.MotorProtectionOk = !_process.MotorProtectionTripped;
        _fb.LevelRaw = _process.LevelRaw;
    }

    private PlcSnapshot BuildSnapshot(DateTimeOffset now)
    {
        var level = _mode == CpuMode.Run ? _fb.Level : S7Math.ScaleX(S7Math.NormX(_fb.LevelRaw, 0, 27648), 0, 100);

        return new PlcSnapshot
        {
            SessionTag = SessionTag,
            Mode = _mode,
            CycleCount = _cycleCount,
            LastCycleMicroseconds = _lastCycleUs,
            MaxCycleMicroseconds = _maxCycleUs,
            AverageCycleMicroseconds = _avgCycleUs,
            Running = _mode == CpuMode.Run && _fb.IsRunning,
            AutoMode = _autoMode,
            ManualPump = _manualPump,
            ManualValve = _manualValve,
            EmergencyStopPressed = _emergencyStopPressed,
            MotorProtectionTripped = _process.MotorProtectionTripped,
            ConsumerDemand = _process.ConsumerDemand,
            Level = _process.LevelPercent,
            LevelRaw = _fb.LevelRaw,
            Liters = _process.Liters,
            LevelHighHighSwitch = _process.LevelHighHigh,
            Pump = _qPump,
            Valve = _qValve,
            Horn = _qHorn,
            FaultLamp = _qFaultLamp,
            RunLamp = _qRunLamp,
            DryRunWarning = _mode == CpuMode.Run && _fb.WarningDryRun,
            PumpSpeed = _process.PumpSpeed,
            InflowLitersPerMinute = _process.InflowLitersPerMinute,
            OutflowLitersPerMinute = _process.OutflowLitersPerMinute,
            Alarms = _alarms.Current.Select(AlarmView.From).ToArray(),
            History = _alarms.History.Select(AlarmView.From).ToArray(),
            Trend = _trend.ToArray(),
            Inputs = BuildInputs(),
            Outputs = BuildOutputs(level),
            Timestamp = now,
            SimulationSeconds = _simulationSeconds,
            IdleStopped = _idleStopped,
        };
    }

    private IoPoint[] BuildInputs() =>
    [
        Bit("%I0.0", "Start", "Taster Start (Schließer)", _fb.Start),
        Bit("%I0.1", "Stop", "Taster Stop (Öffner)", _fb.Stop),
        Bit("%I0.2", "NotHalt_OK", "NOT-HALT-Kreis geschlossen", _fb.EmergencyStopOk),
        Bit("%I0.3", "Quittieren", "Taster Quittieren", _fb.Acknowledge),
        Bit("%I0.4", "Auto", "Wahlschalter AUTO/HAND", _fb.AutoMode),
        Bit("%I0.5", "Hand_P1", "Handbefehl Pumpe P1", _fb.ManualPump),
        Bit("%I0.6", "Hand_V1", "Handbefehl Ventil V1", _fb.ManualValve),
        Bit("%I1.0", "LSHH_T1", "Überfüllsicherung T1", _fb.LevelHighHigh),
        Bit("%I1.1", "MS_P1_OK", "Motorschutz P1 (Öffner)", _fb.MotorProtectionOk),
        new IoPoint("%IW64", "LT101_Roh", "Füllstand analog (0 … 27648)", Fmt.Number(_fb.LevelRaw), null),
    ];

    private IoPoint[] BuildOutputs(double level) =>
    [
        Bit("%Q0.0", "P1_Schuetz", "Schütz Pumpe P1", _qPump),
        Bit("%Q0.1", "V1_Ventil", "Magnetventil V1", _qValve),
        Bit("%Q0.2", "Hupe", "Hupe", _qHorn),
        Bit("%Q0.3", "LM_Stoerung", "Leuchtmelder Störung", _qFaultLamp),
        Bit("%Q0.4", "LM_Anlage_Ein", "Leuchtmelder Anlage EIN", _qRunLamp),
        new IoPoint("%MD10", "Fuellstand", "Füllstand skaliert [%] (Real)", Fmt.Number(level, 2), null),
    ];

    private static IoPoint Bit(string address, string symbol, string comment, bool value) =>
        new(address, symbol, comment, value ? "1" : "0", value);

    private void Publish<TEvent>(TEvent integrationEvent)
        where TEvent : IIntegrationEvent
    {
        if (_eventBus is null)
        {
            return;
        }

        _ = PublishAsync(_eventBus, integrationEvent, _logger);

        static async Task PublishAsync(IEventBus bus, TEvent integrationEvent, ILogger? logger)
        {
            try
            {
                await bus.PublishAsync(integrationEvent);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Integration Event {EventType} konnte nicht veröffentlicht werden.", typeof(TEvent).Name);
            }
        }
    }

    private void RaiseUpdated()
    {
        try
        {
            Updated?.Invoke();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Fehler in einem Abonnenten der SPS {SessionTag}.", SessionTag);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _cts.CancelAsync();
        if (_loop is not null)
        {
            await _loop;
        }

        _cts.Dispose();
        _onDispose?.Invoke();
    }
}
