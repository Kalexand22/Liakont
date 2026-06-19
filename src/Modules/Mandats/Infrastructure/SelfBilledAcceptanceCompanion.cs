namespace Liakont.Modules.Mandats.Infrastructure;

/// <summary>Données fiscales propres au module Mandats portées par la companion self-billing (SIG05).</summary>
/// <param name="AllocatedNumber">BT-1 fiscal alloué par mandant (MND05) ; <c>null</c> tant que non alloué.</param>
/// <param name="PendingSince">Instant (UTC) d'entrée en attente d'acceptation.</param>
internal readonly record struct SelfBilledAcceptanceCompanion(string? AllocatedNumber, DateTimeOffset PendingSince);
