namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Contracts.Queries;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Garde PARTAGÉE des écritures de paramétrage acceptant une société explicite (seed, profil) :
/// un <c>CompanyId</c> explicite est honoré si (a) aucun acteur de tenant n'est présent (amorçage,
/// endpoint d'administration sans claim company), (b) l'acteur porte la MÊME société, ou
/// (c) le tenant cible n'a AUCUN profil — l'état de PROVISIONING console (OPS03 : l'opérateur
/// d'instance agit dans le scope du tenant cible en portant le company_id de SON tenant ; il n'y a
/// rien à corrompre tant qu'aucun profil n'existe). Dès qu'un profil existe, un explicite qui
/// contredit l'acteur est REFUSÉ (garde anti-injection cross-tenant, CLAUDE.md n°9/17).
/// </summary>
internal static class TenantSettingsCompanyOverrideGuard
{
    public static async Task<Guid> ResolveAsync(
        Guid? explicitCompanyId,
        ICompanyFilter companyFilter,
        IActorContextAccessor actorContextAccessor,
        ITenantSettingsQueries settingsQueries,
        CancellationToken cancellationToken)
    {
        if (explicitCompanyId is not { } companyId)
        {
            return companyFilter.GetRequiredCompanyId();
        }

        var actorCompanyId = actorContextAccessor.Current.CompanyId;
        if (actorCompanyId is { } actorId && actorId != companyId)
        {
            // Tenant cible SANS profil = provisioning create-only : l'explicite fait foi (c'est la
            // société que le claim du realm cible présentera). La connexion étant celle du tenant
            // CIBLE (database-per-tenant), aucune donnée d'un autre tenant n'est atteignable.
            if (await settingsQueries.GetCurrentCompanyId(cancellationToken) is null)
            {
                return companyId;
            }

            throw new ConflictException(
                "Le companyId explicite ne peut pas différer de la société du contexte courant quand le tenant "
                + "est déjà paramétré (garde anti-injection cross-tenant). L'override n'est destiné qu'au "
                + "provisioning d'un tenant sans profil.");
        }

        return companyId;
    }
}
