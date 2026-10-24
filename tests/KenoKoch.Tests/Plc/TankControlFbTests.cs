using KenoKoch.Modules.Plc.Program;

namespace KenoKoch.Tests.Plc;

/// <summary>Prüft den Funktionsbaustein gegen die Spezifikation aus FB_TankControl.scl.</summary>
public sealed class TankControlFbTests
{
    private static short Raw(double percent) => (short)Math.Round(percent / 100 * 27648);

    private static TankControlFb RunningPlant(double level, bool auto = true)
    {
        var fb = new TankControlFb { LevelRaw = Raw(level), AutoMode = auto, Start = true };
        fb.Execute();
        fb.Start = false;
        return fb;
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(13824, 50f)]
    [InlineData(27648, 100f)]
    public void Analog_value_is_scaled_like_norm_x_and_scale_x(short raw, float expected)
    {
        var fb = new TankControlFb { LevelRaw = raw };
        fb.Execute();
        Assert.Equal(expected, fb.Level, precision: 3);
    }

    [Fact]
    public void Start_switches_plant_on_and_run_lamp_follows()
    {
        var fb = RunningPlant(level: 50);

        Assert.True(fb.IsRunning);
        Assert.True(fb.RunLamp);
    }

    [Fact]
    public void Stop_dominates_start()
    {
        var fb = new TankControlFb { LevelRaw = Raw(50), Start = true, Stop = false };
        fb.Execute();

        Assert.False(fb.IsRunning);
    }

    [Fact]
    public void Two_point_controller_uses_hysteresis()
    {
        var fb = RunningPlant(level: 15);
        Assert.True(fb.Pump);

        fb.LevelRaw = Raw(50);
        fb.Execute();
        Assert.True(fb.Pump);

        fb.LevelRaw = Raw(82);
        fb.Execute();
        Assert.False(fb.Pump);

        fb.LevelRaw = Raw(50);
        fb.Execute();
        Assert.False(fb.Pump);
    }

    [Fact]
    public void Emergency_stop_latches_until_released_and_acknowledged()
    {
        var fb = RunningPlant(level: 15);

        fb.EmergencyStopOk = false;
        fb.Execute();
        Assert.False(fb.IsRunning);
        Assert.False(fb.Pump);
        Assert.True(fb.AlarmEmergencyStop);
        Assert.True(fb.Horn);

        // Quittieren bei gedrücktem NOT-HALT hat keine Wirkung.
        fb.Acknowledge = true;
        fb.Execute();
        fb.Acknowledge = false;
        fb.Execute();
        Assert.True(fb.AlarmEmergencyStop);

        // Entriegeln allein reicht nicht – erst die Quittierung setzt zurück.
        fb.EmergencyStopOk = true;
        fb.Execute();
        Assert.True(fb.AlarmEmergencyStop);

        fb.Acknowledge = true;
        fb.Execute();
        Assert.False(fb.AlarmEmergencyStop);
        Assert.False(fb.IsRunning);

        fb.Acknowledge = false;
        fb.Start = true;
        fb.Execute();
        Assert.True(fb.IsRunning);
    }

    [Fact]
    public void Overfill_protection_blocks_pump_even_in_manual_mode()
    {
        var fb = RunningPlant(level: 50, auto: false);
        fb.ManualPump = true;
        fb.Execute();
        Assert.True(fb.Pump);

        fb.LevelHighHigh = true;
        fb.Execute();
        Assert.False(fb.Pump);
        Assert.True(fb.AlarmLevelHighHigh);
    }

    [Fact]
    public void Motor_protection_trip_stops_plant_and_needs_acknowledge()
    {
        var fb = RunningPlant(level: 15);
        fb.MotorProtectionOk = false;
        fb.Execute();

        Assert.False(fb.IsRunning);
        Assert.True(fb.AlarmMotorProtection);
        Assert.True(fb.FaultLamp);

        fb.MotorProtectionOk = true;
        fb.Acknowledge = true;
        fb.Execute();

        Assert.False(fb.AlarmMotorProtection);
        Assert.False(fb.FaultLamp);
    }

    [Fact]
    public void Valve_is_locked_below_dry_run_limit()
    {
        var fb = RunningPlant(level: 30);
        Assert.True(fb.Valve);

        fb.LevelRaw = Raw(5);
        fb.Execute();

        Assert.False(fb.Valve);
        Assert.True(fb.WarningDryRun);
    }

    [Fact]
    public void Horn_switches_off_after_acknowledge_while_fault_is_still_present()
    {
        var fb = RunningPlant(level: 50);
        fb.LevelHighHigh = true;
        fb.Execute();
        Assert.True(fb.Horn);

        fb.Acknowledge = true;
        fb.Execute();

        Assert.False(fb.Horn);
        Assert.True(fb.FaultLamp);
    }
}
