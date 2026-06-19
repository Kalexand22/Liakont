namespace Liakont.Modules.Archive.Contracts;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Traçabilité reporting↔pièces (item B2C03, F06 §3 / F09 §10.3). Lie de façon IMMUABLE une transmission
/// d'e-reporting B2C (déclaration 10.3) à ses pièces source, dans les DEUX SENS, et tenant-scopé.
/// <para>
/// APPEND-ONLY (CLAUDE.md n°4) : ce port n'expose QUE de l'ajout (idempotent) et de la lecture — aucun
/// chemin d'update/delete (la table porte en plus des triggers anti UPDATE/DELETE/TRUNCATE, V011).
/// TENANT-SCOPÉ (n°9) : chaque méthode prend le <c>companyId</c> explicitement (jamais cross-tenant) — toute
/// requête filtre sur <c>company_id</c>. Le store ne résout PAS le tenant lui-même : l'appelant fournit le
/// <c>companyId</c> du tenant courant (l'export contrôle fiscal le résout via la société du tenant
/// sélectionné, ce qui reste correct y compris pour un export OPÉRATEUR cross-tenant — le filtre acteur
/// <c>ICompanyFilter</c> serait nul dans ce cas).
/// </para>
/// <para>
/// Producteur du lien : la voie d'envoi d'une déclaration 10.3 (câblée par la démo B2C04 puis le calcul de
/// marge B2C09b « au grain lot »). Consommateur : <see cref="IFiscalControlExportService"/> ajoute, pour
/// chaque document, les liens gelés au dossier d'export autoportant.
/// </para>
/// </summary>
public interface IReportingPieceLinkStore
{
    /// <summary>
    /// Gèle, pour le tenant <paramref name="companyId"/>, le lien entre la transmission
    /// <paramref name="documentId"/> et chacune des pièces <paramref name="sourceReferences"/>. Idempotent :
    /// un lien déjà présent (même tenant, même transmission, même pièce) n'est PAS ré-inséré ni modifié
    /// (no-op, append-only préservé). Retourne les liens existants pour cette transmission après l'ajout.
    /// </summary>
    Task<IReadOnlyList<ReportingPieceLink>> AppendAsync(
        Guid companyId,
        Guid documentId,
        IReadOnlyCollection<string> sourceReferences,
        CancellationToken cancellationToken = default);

    /// <summary>Sens transmission → pièces : les liens gelés d'une transmission, pour le tenant <paramref name="companyId"/>.</summary>
    Task<IReadOnlyList<ReportingPieceLink>> GetByDocumentAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Sens pièce → transmissions : les liens d'une pièce source, pour le tenant <paramref name="companyId"/>.</summary>
    Task<IReadOnlyList<ReportingPieceLink>> GetBySourceReferenceAsync(Guid companyId, string sourceReference, CancellationToken cancellationToken = default);
}
