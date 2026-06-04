namespace Liakont.Modules.Reconciliation.Contracts.DTOs;

using System;

/// <summary>
/// PDF du pool ORPHELIN — aucune correspondance ou ambiguïté (item TRK07, F06 §7 §3). File d'attente
/// manuelle : l'opérateur peut le rattacher à un document via une réconciliation manuelle (API04/WEB08).
/// </summary>
public sealed record OrphanPdfDto(
    Guid QueueEntryId,
    string PoolPdfId,
    string FileName,
    string Detail,
    DateTimeOffset CreatedUtc);
