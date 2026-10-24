using KenoKoch.BuildingBlocks.Events;
using KenoKoch.Modules.Plc.Contracts;
using KenoKoch.Modules.Plc.Runtime;
using KenoKoch.Modules.Plc.Simulation;

namespace KenoKoch.Tests.Plc;

public sealed class PlcRuntimeTests
{
    private sealed class RecordingEventBus : IEventBus
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public ValueTask PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : IIntegrationEvent
        {
            Events.Add(integrationEvent);
            return ValueTask.CompletedTask;
        }
    }

    private static readonly TimeSpan Cycle = PlcRuntime.CycleTime;

    private static void Scan(PlcRuntime runtime, int cycles = 1)
    {
        for (var i = 0; i < cycles; i++)
        {
            runtime.Scan(Cycle);
        }
    }

    [Fact]
    public void Starting_the_cpu_publishes_an_integration_event()
    {
        var bus = new RecordingEventBus();
        var runtime = new PlcRuntime(bus, TimeProvider.System);

        runtime.Send(PlcCommand.CpuRun(true));
        Scan(runtime);

        var started = Assert.IsType<PlcStarted>(Assert.Single(bus.Events));
        Assert.Equal(runtime.SessionTag, started.SessionTag);
        Assert.Equal(CpuMode.Run, runtime.Snapshot.Mode);
    }

    [Fact]
    public void Cpu_stop_switches_off_all_outputs()
    {
        var runtime = new PlcRuntime(null, TimeProvider.System, process: new TankProcess(initialLevel: 15, seed: 1));
        runtime.Send(PlcCommand.CpuRun(true));
        runtime.Send(PlcCommand.PressStart());
        Scan(runtime, 3);
        Assert.True(runtime.Snapshot.Pump);

        runtime.Send(PlcCommand.CpuRun(false));
        Scan(runtime);

        Assert.False(runtime.Snapshot.Pump);
        Assert.False(runtime.Snapshot.Valve);
        Assert.Contains(runtime.Snapshot.Alarms, a => a.Number == AlarmManager.CpuStop.Number);
    }

    [Fact]
    public void Emergency_stop_raises_alarm_that_needs_release_and_acknowledge()
    {
        var bus = new RecordingEventBus();
        var runtime = new PlcRuntime(bus, TimeProvider.System, process: new TankProcess(initialLevel: 50, seed: 2));
        runtime.Send(PlcCommand.CpuRun(true));
        runtime.Send(PlcCommand.PressStart());
        Scan(runtime, 2);

        runtime.Send(PlcCommand.ToggleEmergencyStop());
        Scan(runtime);

        var alarm = Assert.Single(runtime.Snapshot.Alarms, a => a.Number == AlarmManager.EmergencyStop.Number);
        Assert.Equal("K", alarm.State);
        Assert.False(runtime.Snapshot.Running);
        Assert.True(runtime.Snapshot.Horn);
        Assert.Contains(bus.Events, e => e is PlcAlarmRaised { AlarmNumber: 1001 });

        runtime.Send(PlcCommand.ToggleEmergencyStop());
        runtime.Send(PlcCommand.PressAcknowledge());
        Scan(runtime, 2);

        Assert.DoesNotContain(runtime.Snapshot.Alarms, a => a.Number == AlarmManager.EmergencyStop.Number);
        Assert.Contains(runtime.Snapshot.History, a => a.Number == AlarmManager.EmergencyStop.Number);
        Assert.False(runtime.Snapshot.Horn);
    }

    [Fact]
    public void Overfilling_in_manual_mode_is_stopped_by_the_interlock()
    {
        var runtime = new PlcRuntime(null, TimeProvider.System, process: new TankProcess(initialLevel: 70, seed: 3));
        runtime.Send(PlcCommand.CpuRun(true));
        runtime.Send(PlcCommand.Auto(false));
        runtime.Send(PlcCommand.PressStart());
        runtime.Send(PlcCommand.ManualPump(true));

        // 30 s Simulationszeit reichen, um von 70 % über 95 % zu kommen.
        Scan(runtime, 300);

        var snapshot = runtime.Snapshot;
        Assert.True(snapshot.LevelHighHighSwitch || snapshot.Level >= 90);
        Assert.False(snapshot.Pump);
        Assert.Contains(snapshot.Alarms, a => a.Number == AlarmManager.LevelHighHigh.Number);
        Assert.True(snapshot.Level <= 100);
    }

    [Fact]
    public void Automatic_mode_keeps_level_inside_the_hysteresis_band()
    {
        var runtime = new PlcRuntime(null, TimeProvider.System, process: new TankProcess(initialLevel: 40, seed: 4));
        runtime.Send(PlcCommand.CpuRun(true));
        runtime.Send(PlcCommand.PressStart());

        var levels = new List<double>();
        for (var i = 0; i < 1200; i++)
        {
            runtime.Scan(Cycle);
            levels.Add(runtime.Snapshot.Level);
        }

        // Nach dem Einschwingen pendelt der Füllstand zwischen L (20 %) und H (80 %) – mit etwas Überschwingen.
        var settled = levels.Skip(400).ToArray();
        Assert.InRange(settled.Min(), 15, 25);
        Assert.InRange(settled.Max(), 75, 88);
        Assert.Empty(runtime.Snapshot.Alarms);
    }

    [Fact]
    public void Trend_keeps_the_last_sixty_seconds()
    {
        var runtime = new PlcRuntime(null, TimeProvider.System);
        runtime.Send(PlcCommand.CpuRun(true));
        Scan(runtime, 1000);

        Assert.Equal(120, runtime.Snapshot.Trend.Count);
        Assert.True(runtime.Snapshot.Trend[^1].Seconds > runtime.Snapshot.Trend[0].Seconds);
    }

    [Fact]
    public void Showcase_snapshot_shows_a_running_plant()
    {
        var snapshot = PlcRuntime.Showcase(TimeProvider.System);

        Assert.True(snapshot.Pump);
        Assert.True(snapshot.Valve);
        Assert.Equal(CpuMode.Run, snapshot.Mode);
    }
}
