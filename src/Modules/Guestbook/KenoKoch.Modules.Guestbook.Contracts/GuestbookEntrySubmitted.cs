using KenoKoch.BuildingBlocks.Events;

namespace KenoKoch.Modules.Guestbook.Contracts;

/// <summary>Ein Besucher hat einen Gästebucheintrag abgeschickt.</summary>
/// <param name="EntryId">Technische Id des Eintrags.</param>
/// <param name="VisitorRole">Anzeigename der gewählten Besucherrolle (z. B. "Recruiting / HR").</param>
/// <param name="RequiresApproval">Wird der Eintrag erst nach Freigabe öffentlich sichtbar?</param>
public sealed record GuestbookEntrySubmitted(int EntryId, string VisitorRole, bool RequiresApproval) : IntegrationEvent;
