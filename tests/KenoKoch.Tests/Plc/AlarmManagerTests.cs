using KenoKoch.Modules.Plc.Runtime;

namespace KenoKoch.Tests.Plc;

public sealed class AlarmManagerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fault_goes_through_came_acknowledged_gone()
    {
        var alarms = new AlarmManager();

        var raised = alarms.Evaluate(T0, [(AlarmManager.MotorProtection, true)]);
        Assert.Equal([AlarmManager.MotorProtection], raised);
        Assert.Equal("K", Assert.Single(alarms.Current).State);
        Assert.True(alarms.HasUnacknowledged);

        alarms.AcknowledgeAll(T0.AddSeconds(5));
        Assert.Equal("KQ", Assert.Single(alarms.Current).State);

        alarms.Evaluate(T0.AddSeconds(10), [(AlarmManager.MotorProtection, false)]);
        Assert.Empty(alarms.Current);
        Assert.Single(alarms.History);
    }

    [Fact]
    public void Gone_but_unacknowledged_fault_stays_visible()
    {
        var alarms = new AlarmManager();
        alarms.Evaluate(T0, [(AlarmManager.EmergencyStop, true)]);
        alarms.Evaluate(T0.AddSeconds(2), [(AlarmManager.EmergencyStop, false)]);

        Assert.Equal("KG", Assert.Single(alarms.Current).State);
    }

    [Fact]
    public void Warnings_do_not_need_acknowledgement()
    {
        var alarms = new AlarmManager();
        alarms.Evaluate(T0, [(AlarmManager.DryRun, true)]);

        Assert.False(alarms.HasUnacknowledged);

        alarms.Evaluate(T0.AddSeconds(1), [(AlarmManager.DryRun, false)]);
        Assert.Empty(alarms.Current);
    }

    [Fact]
    public void Active_alarm_is_raised_only_once()
    {
        var alarms = new AlarmManager();

        Assert.Single(alarms.Evaluate(T0, [(AlarmManager.LevelHighHigh, true)]));
        Assert.Empty(alarms.Evaluate(T0.AddSeconds(1), [(AlarmManager.LevelHighHigh, true)]));
        Assert.Single(alarms.Current);
    }
}
