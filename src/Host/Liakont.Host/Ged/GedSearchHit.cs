namespace Liakont.Host.Ged;

using System;

/// <summary>
/// Un résultat de recherche documentaire GED (projeté depuis <c>DocumentSearchHit</c>, GED08). Ne porte QUE des
/// méta-données d'index déjà masquées côté serveur (§6.5) — aucun contenu ni recalcul. La drill-down ouvre la
/// fiche <c>/ged/document/{id}</c> (GED09b).
/// </summary>
/// <param name="DocumentId">Identité GED du document (<c>managed_document_id</c>).</param>
/// <param name="Title">Titre source du document.</param>
/// <param name="Kind">Libellé métier libre du document (<c>doc_kind</c>) ; <see langword="null"/> si absent.</param>
/// <param name="Status">Statut d'index brut (<c>indexed|archived|deferred|draft</c>) — libellé FR calculé à l'affichage.</param>
public sealed record GedSearchHit(Guid DocumentId, string Title, string? Kind, string Status);
