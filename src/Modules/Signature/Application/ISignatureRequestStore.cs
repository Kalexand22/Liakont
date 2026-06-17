namespace Liakont.Modules.Signature.Application;

using Liakont.Modules.Signature.Domain.Entities;

/// <summary>
/// Persistance tenant-scopée de la liaison <c>référence fournisseur → document</c> (ADR-0029 §5). Écrite à
/// l'émission d'une demande de signature ; relue par le drain pour rapatrier la preuve en WORM (le webhook
/// ne porte que la référence fournisseur). Scopée par <c>company_id</c> (CLAUDE.md n°9).
/// </summary>
public interface ISignatureRequestStore
{
    /// <summary>Enregistre (ou remplace) la liaison d'une demande émise.</summary>
    /// <param name="link">Liaison à persister.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task RecordAsync(SignatureRequestLink link, CancellationToken cancellationToken = default);

    /// <summary>Relit la liaison d'une référence fournisseur, ou <c>null</c> si inconnue (événement orphelin).</summary>
    /// <param name="companyId">Tenant (clé <c>company_id</c>).</param>
    /// <param name="providerType">Type de fournisseur.</param>
    /// <param name="providerReference">Référence côté fournisseur.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<SignatureRequestLink?> GetByProviderReferenceAsync(
        Guid companyId, string providerType, string providerReference, CancellationToken cancellationToken = default);
}
