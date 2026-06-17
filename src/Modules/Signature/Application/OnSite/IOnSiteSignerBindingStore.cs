namespace Liakont.Modules.Signature.Application.OnSite;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Enregistrement et résolution de la liaison VÉRIFIÉE déposant→signataire (ADR-0030 §5). Tenant-scopé par
/// construction (connexion = tenant, database-per-tenant — CLAUDE.md n°9). La capture RÉSOUT le signataire
/// vérifié via ce port (jamais depuis son payload), ce qui ferme l'usurpation (INV-ONSITE-7).
/// </summary>
public interface IOnSiteSignerBindingStore
{
    /// <summary>Enregistre une liaison vérifiée. Lève si l'entrée est incomplète.</summary>
    /// <param name="record">Liaison à enregistrer.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task RegisterAsync(OnSiteSignerBindingRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Résout la liaison vérifiée la plus récente d'un document dans le tenant courant, ou <c>null</c> si
    /// aucune n'existe (le signataire reste alors non prouvé — le niveau ne monte jamais au-delà de SES).
    /// </summary>
    /// <param name="companyId">Tenant (clé <c>company_id</c>).</param>
    /// <param name="documentId">Document recherché.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<OnSiteSignerBindingRecord?> ResolveVerifiedAsync(
        Guid companyId, Guid documentId, CancellationToken cancellationToken = default);
}
