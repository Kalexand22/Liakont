namespace Liakont.Modules.Reference.Contracts;

using Liakont.Modules.Reference.Contracts.DTOs;

/// <summary>
/// Référentiel de correspondance des codes pays (ADR-0038) : normalise un code pays SOURCE non-ISO
/// (ex. « ENG », « JAP », « BEL ») vers son code ISO 3166-1 alpha-2 (« GB », « JP », « BE »). Table
/// cross-instance UNIVERSELLE (aucun <c>tenant_id</c>), en base système, éditable en console et auditée
/// append-only. Consommé au READ-TIME plateforme (CHECK / SEND / affichage) — JAMAIS à l'ingestion, pour
/// ne pas polluer l'empreinte anti-doublon F06 (INV-REF-CTRY-02) — via <see cref="ResolveAsync"/> ; jamais
/// depuis l'agent (qui transporte le code brut, ADR-0004). Frontière : le Pipeline le consomme par ce
/// Contracts uniquement (jamais l'Infrastructure du module).
/// </summary>
public interface ICountryAliasReferential
{
    /// <summary>
    /// Renvoie le code ISO 3166-1 alpha-2 correspondant à <paramref name="rawCountryCode"/> si une
    /// correspondance existe (recherche insensible à la casse et aux espaces), sinon le code BRUT INCHANGÉ
    /// (fail-closed : jamais deviné — la validation Blocking BT-55 bloquera un code non mappé, INV-REF-CTRY-03).
    /// Un code <c>null</c>/vide est renvoyé tel quel.
    /// </summary>
    Task<string?> ResolveAsync(string? rawCountryCode, CancellationToken cancellationToken = default);

    /// <summary>Liste des correspondances du référentiel (lecture console), triées par code source.</summary>
    Task<IReadOnlyList<CountryAliasDto>> GetAliasesAsync(CancellationToken cancellationToken = default);
}
