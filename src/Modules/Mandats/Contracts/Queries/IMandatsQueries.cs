namespace Liakont.Modules.Mandats.Contracts.Queries;

using Liakont.Modules.Mandats.Contracts.DTOs;

/// <summary>
/// Lectures (seules) du module Mandats, exposées à la frontière Contracts (module-rules §3,
/// INV-MANDATS-2). Toutes les méthodes sont scopées par <paramref name="companyId"/> (jamais de lecture
/// cross-tenant — CLAUDE.md n°9/17, INV-MANDATS-1) ; le <c>company_id</c> est résolu par l'appelant via
/// <c>ICompanyFilter.GetRequiredCompanyId</c>, jamais fourni par le client.
/// </summary>
public interface IMandatsQueries
{
    /// <summary>Charge un mandant par sa référence métier ; <c>null</c> si absent pour ce tenant.</summary>
    Task<MandantDto?> GetMandant(Guid companyId, string reference, CancellationToken ct = default);

    /// <summary>Liste les mandants du tenant (registre, F15 §2.2).</summary>
    Task<IReadOnlyList<MandantDto>> ListMandants(Guid companyId, CancellationToken ct = default);

    /// <summary>Charge un mandat par sa clé (mandant + référence) ; <c>null</c> si absent pour ce tenant.</summary>
    Task<MandatDto?> GetMandat(Guid companyId, Guid mandantId, string reference, CancellationToken ct = default);

    /// <summary>Liste les mandats d'un mandant du tenant.</summary>
    Task<IReadOnlyList<MandatDto>> ListMandats(Guid companyId, Guid mandantId, CancellationToken ct = default);

    /// <summary>Lit le journal append-only des modifications du tenant, du plus récent au plus ancien (audit).</summary>
    Task<IReadOnlyList<MandatChangeLogEntryDto>> GetChangeLog(Guid companyId, CancellationToken ct = default);
}
