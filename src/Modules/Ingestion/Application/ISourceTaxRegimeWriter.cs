namespace Liakont.Modules.Ingestion.Application;

using System.Collections.Generic;

/// <summary>
/// Persiste (upsert) les régimes de TVA source observés, par tenant, dans la base SYSTÈME (schéma
/// <c>ingestion</c>) — F12 / PIV04. Métadonnée de push idempotente : un même code ré-observé cumule
/// ses occurrences et rafraîchit son libellé/horodatage. Valeur BRUTE, jamais interprétée
/// (CLAUDE.md n°2). Consommé par TVA03 (détection de couverture) via <c>ISourceTaxRegimeQueries</c>.
/// </summary>
public interface ISourceTaxRegimeWriter
{
    Task UpsertAsync(string tenantId, IReadOnlyList<SourceTaxRegimeObservation> regimes, CancellationToken cancellationToken = default);
}

/// <summary>Une observation de régime de TVA source à persister (code brut, libellé, occurrences du push).</summary>
public sealed record SourceTaxRegimeObservation
{
    public required string Code { get; init; }

    public string? Label { get; init; }

    /// <summary>Occurrences observées sur ce push (cumulées à l'existant côté plateforme).</summary>
    public required int Occurrences { get; init; }
}
