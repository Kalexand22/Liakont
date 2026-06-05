namespace Liakont.Modules.TvaMapping.Contracts.Services;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Surface inter-modules du module TvaMapping (frontière Contracts-only, module-rules §3) : applique la
/// table de mapping VALIDÉE du tenant à des requêtes de ligne EXPLICITES (moteur <c>TvaMapper</c>) et
/// remonte l'état de validation de la table. Consommé par le pipeline (PIP01b, CHECK).
///
/// <para>Le service N'INVENTE AUCUNE règle fiscale (CLAUDE.md n°2) : il n'expose que le moteur de domaine
/// existant à la frontière. La résolution part/code depuis une ligne pivot est une décision fiscale
/// OUVERTE (aucune règle sourcée — ADR-0004 / F03 §2.3) ; elle appartient à l'appelant (PIP01b), qui
/// fournit la <see cref="TvaLineMappingRequest.Part"/>. Ce service ne touche jamais un <c>PivotDocumentDto</c>
/// directement : il opère sur des requêtes que l'appelant a construites depuis le pivot.</para>
/// </summary>
public interface ITvaMappingService
{
    /// <summary>
    /// Mappe chaque requête de ligne contre la table du tenant (<paramref name="companyId"/>) et remonte
    /// l'état de validation de la table. Une table absente → résultat avec <c>TableExists=false</c> et
    /// chaque ligne bloquée (jamais de mapping deviné).
    /// </summary>
    /// <param name="companyId">Tenant propriétaire de la table (clé d'isolation, CLAUDE.md n°9).</param>
    /// <param name="lines">Requêtes de ligne (code régime + part + flags), construites par l'appelant.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Le résultat agrégé (validation de la table + résultat par ligne).</returns>
    Task<DocumentTvaMappingResult> MapAsync(
        Guid companyId,
        IReadOnlyList<TvaLineMappingRequest> lines,
        CancellationToken cancellationToken = default);
}
