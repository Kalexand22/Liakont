namespace Liakont.Modules.Signature.Application.OnSite;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Écriture (APPEND-ONLY) et relecture du journal de preuve de signature sur place
/// (<c>signature.onsite_signature_proofs</c>, ADR-0030 §3 ; INV-ONSITE-6). Tenant-scopé par construction (la
/// connexion EST le tenant, database-per-tenant — CLAUDE.md n°9) : aucune requête cross-tenant. Le journal est
/// immuable (double trigger base) ; aucun chemin update/delete (CLAUDE.md n°4).
/// </summary>
public interface IOnSiteSignatureProofStore
{
    /// <summary>Consigne une preuve en append-only. Lève si l'entrée est incomplète.</summary>
    /// <param name="record">Entrée de preuve à consigner.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task AppendAsync(OnSiteSignatureProofRecord record, CancellationToken cancellationToken = default);

    /// <summary>Relit la preuve la plus récente d'un document dans le tenant courant, ou <c>null</c> si aucune.</summary>
    /// <param name="companyId">Tenant (clé <c>company_id</c>).</param>
    /// <param name="documentId">Document recherché.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<OnSiteSignatureProofRecord?> FindLatestAsync(
        Guid companyId, Guid documentId, CancellationToken cancellationToken = default);
}
