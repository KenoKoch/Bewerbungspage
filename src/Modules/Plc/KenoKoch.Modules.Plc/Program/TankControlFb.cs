namespace KenoKoch.Modules.Plc.Program;

/// <summary>
/// FB "TankControl" – das C#-Gegenstück zum SCL-Baustein FB_TankControl.scl.
/// Die Laufzeit ruft <see cref="Execute"/> zyklisch alle 100 ms auf (wie OB1 in der S7).
/// Eingänge werden vor dem Aufruf aus dem Prozessabbild gesetzt, Ausgänge danach gelesen.
/// </summary>
public sealed class TankControlFb
{
    // ── VAR_INPUT ────────────────────────────────────────────────
    public bool Start;                    // %I0.0  Taster Start (Schließer)
    public bool Stop = true;              // %I0.1  Taster Stop (Öffner: TRUE = nicht betätigt)
    public bool EmergencyStopOk = true;   // %I0.2  NOT-HALT-Kreis geschlossen (Öffner)
    public bool Acknowledge;              // %I0.3  Taster Quittieren
    public bool AutoMode = true;          // %I0.4  Wahlschalter AUTO (TRUE) / HAND (FALSE)
    public bool ManualPump;               // %I0.5  Handbefehl Pumpe P1
    public bool ManualValve;              // %I0.6  Handbefehl Ventil V1
    public bool LevelHighHigh;            // %I1.0  Überfüllsicherung LSHH
    public bool MotorProtectionOk = true; // %I1.1  Motorschutzschalter P1 (Öffner)
    public short LevelRaw;                // %IW64  Füllstand analog, 0 … 27648

    // ── Parameter (im Instanz-DB) ────────────────────────────────
    public float LevelLow = 20.0f;        // Pumpe EIN unterhalb [%]
    public float LevelHigh = 80.0f;       // Pumpe AUS oberhalb [%]
    public float DryRunLimit = 8.0f;      // Ventil gesperrt unterhalb [%]

    // ── VAR_OUTPUT ───────────────────────────────────────────────
    public bool Pump;                     // %Q0.0  Schütz Pumpe P1
    public bool Valve;                    // %Q0.1  Magnetventil V1
    public bool Horn;                     // %Q0.2  Hupe
    public bool FaultLamp;                // %Q0.3  Leuchtmelder Störung
    public bool RunLamp;                  // %Q0.4  Leuchtmelder Anlage EIN
    public float Level;                   // Füllstand [%]
    public bool AlarmEmergencyStop;       // Meldebit NOT-HALT
    public bool AlarmMotorProtection;     // Meldebit Motorschutz P1
    public bool AlarmLevelHighHigh;       // Meldebit Überfüllsicherung
    public bool WarningDryRun;            // Meldebit Trockenlaufschutz

    // ── VAR (statisch) ───────────────────────────────────────────
    private bool _running;
    private bool _pumpDemand;
    private bool _eStopLatched;
    private bool _motorLatched;
    private bool _levelHhLatched;
    private bool _ackPrevious;
    private bool _faultPrevious;
    private bool _hornLatched;

    public bool IsRunning => _running;

    public void Execute()
    {
        // Analogwert skalieren: 0 … 27648 → 0,0 … 100,0 %
        Level = S7Math.ScaleX(S7Math.NormX(LevelRaw, 0, 27648), 0.0f, 100.0f);

        // Quittierung (positive Flanke, R_TRIG)
        var ackEdge = Acknowledge && !_ackPrevious;
        _ackPrevious = Acknowledge;

        // Störungen speichern – Rücksetzen nur per Quittierung und nur, wenn die Ursache weg ist
        if (!EmergencyStopOk)
        {
            _eStopLatched = true;
        }
        else if (ackEdge)
        {
            _eStopLatched = false;
        }

        if (!MotorProtectionOk)
        {
            _motorLatched = true;
        }
        else if (ackEdge)
        {
            _motorLatched = false;
        }

        if (LevelHighHigh)
        {
            _levelHhLatched = true;
        }
        else if (ackEdge)
        {
            _levelHhLatched = false;
        }

        var shutdown = _eStopLatched || _motorLatched;

        // Anlage EIN/AUS – Stop und Störungen dominieren (rücksetzdominant)
        if (!Stop || shutdown)
        {
            _running = false;
        }
        else if (Start)
        {
            _running = true;
        }

        // Pumpe: Zweipunktregelung im Automatik, Handbefehl im Handbetrieb
        if (_running && AutoMode)
        {
            if (Level <= LevelLow)
            {
                _pumpDemand = true;
            }
            else if (Level >= LevelHigh)
            {
                _pumpDemand = false;
            }
        }
        else
        {
            _pumpDemand = _running && ManualPump;
        }

        // Verriegelungen – gelten auch im Handbetrieb
        Pump = _pumpDemand && !LevelHighHigh && !_motorLatched;
        Valve = _running && (AutoMode || ManualValve) && Level > DryRunLimit;

        // Meldungen und Hupe
        AlarmEmergencyStop = _eStopLatched;
        AlarmMotorProtection = _motorLatched;
        AlarmLevelHighHigh = _levelHhLatched;
        WarningDryRun = _running && Level <= DryRunLimit;

        var fault = _eStopLatched || _motorLatched || _levelHhLatched;
        if (fault && !_faultPrevious)
        {
            _hornLatched = true;
        }

        if (ackEdge || !fault)
        {
            _hornLatched = false;
        }

        _faultPrevious = fault;

        Horn = _hornLatched;
        FaultLamp = fault;
        RunLamp = _running;
    }
}
