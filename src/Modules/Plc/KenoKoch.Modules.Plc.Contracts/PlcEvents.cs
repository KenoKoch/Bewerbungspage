using KenoKoch.BuildingBlocks.Events;

namespace KenoKoch.Modules.Plc.Contracts;

/// <summary>Ein Besucher hat die CPU seiner simulierten SPS in RUN geschaltet.</summary>
/// <param name="SessionTag">Kurze, anonyme Kennung der SPS-Instanz (z. B. "4F2A").</param>
public sealed record PlcStarted(string SessionTag) : IntegrationEvent;

/// <summary>In einer simulierten SPS ist ein Störalarm aufgetreten.</summary>
public sealed record PlcAlarmRaised(string SessionTag, int AlarmNumber, string AlarmText) : IntegrationEvent;
