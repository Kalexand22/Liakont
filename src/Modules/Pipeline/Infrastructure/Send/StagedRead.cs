namespace Liakont.Modules.Pipeline.Infrastructure.Send;

using Liakont.Agent.Contracts.Pivot;

/// <summary>Résultat de la relecture du staging : le pivot désérialisé et son JSON canonique (ou un statut d'échec).</summary>
internal sealed record StagedRead(StagedReadStatus Status, PivotDocumentDto? Pivot, string? Json);
