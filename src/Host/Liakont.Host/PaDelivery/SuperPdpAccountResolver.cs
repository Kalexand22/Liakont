namespace Liakont.Host.PaDelivery;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.SuperPdp;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation Host de <see cref="ISuperPdpAccountResolver"/> : seul endroit qui DÉCHIFFRE les secrets
/// OAuth2 (<c>client_id</c> / <c>client_secret</c>) du compte Super PDP d'un tenant. Le plug-in ne voit que
/// <c>Transmission.Contracts</c> (frontière §6) ; le Host, lui, voit TenantSettings et fournit les secrets
/// déchiffrés via cette frontière. Bloque (jamais d'envoi sans authentification — CLAUDE.md n°3) si le compte
/// est absent ou un secret manquant.
/// </summary>
/// <remarks>
/// Le descripteur d'envoi (<see cref="PaAccountDescriptor"/>) ne porte que le type et le tenant (jamais de
/// secret — option B1 du plan SuperPDP) : ce résolveur lit donc <c>tenantsettings.pa_accounts</c> via
/// <see cref="IPaAccountSecretStore"/>. Comme la fabrique qui le consomme est un singleton, ce résolveur est
/// aussi un singleton ; il ne peut pas injecter de service scopé, d'où l'ouverture d'un scope tenant dédié
/// (<see cref="ITenantScopeFactory"/>) à la résolution.
/// </remarks>
internal sealed class SuperPdpAccountResolver : ISuperPdpAccountResolver
{
    private readonly ITenantScopeFactory _tenantScopeFactory;
    private readonly ISecretProtector _secretProtector;

    public SuperPdpAccountResolver(ITenantScopeFactory tenantScopeFactory, ISecretProtector secretProtector)
    {
        ArgumentNullException.ThrowIfNull(tenantScopeFactory);
        ArgumentNullException.ThrowIfNull(secretProtector);
        _tenantScopeFactory = tenantScopeFactory;
        _secretProtector = secretProtector;
    }

    public SuperPdpAccountConfig Resolve(PaAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);

        // ISuperPdpAccountResolver.Resolve est SYNCHRONE (contrat IPaClientFactory.Create figé). Le SEND
        // tourne sur le pool de threads (job multi-tenant, aucun SynchronizationContext) → ce sync-over-async
        // est sans risque de deadlock.
        return ResolveAsync(account).GetAwaiter().GetResult();
    }

    private static SuperPdpEnvironment MapEnvironment(PaEnvironment environment) => environment switch
    {
        PaEnvironment.Staging => SuperPdpEnvironment.Sandbox,
        PaEnvironment.Production => SuperPdpEnvironment.Production,
        _ => SuperPdpEnvironment.Sandbox,
    };

    private async Task<SuperPdpAccountConfig> ResolveAsync(PaAccountDescriptor account)
    {
        await using var scope = _tenantScopeFactory.Create(account.TenantId);

        var companyId = await scope.Services.GetRequiredService<ITenantSettingsQueries>()
            .GetCurrentCompanyId()
            .ConfigureAwait(false);
        if (companyId is null)
        {
            throw new InvalidOperationException(
                $"Compte Super PDP introuvable : aucun profil tenant (companyId) pour « {account.TenantId} ». "
                + "Action opérateur : créez le profil du tenant, puis configurez un compte Super PDP actif.");
        }

        var secrets = await scope.Services.GetRequiredService<IPaAccountSecretStore>()
            .GetActiveAsync(companyId.Value, account.PaType)
            .ConfigureAwait(false);
        if (secrets is null)
        {
            throw new InvalidOperationException(
                $"Aucun compte Super PDP actif pour le tenant « {account.TenantId} ». "
                + "Action opérateur : configurez et activez un compte Super PDP (Paramétrage › Plateforme Agréée).");
        }

        if (string.IsNullOrWhiteSpace(secrets.AccountIdentifiers)
            || string.IsNullOrWhiteSpace(secrets.EncryptedClientId)
            || string.IsNullOrWhiteSpace(secrets.EncryptedClientSecret))
        {
            throw new InvalidOperationException(
                $"Compte Super PDP du tenant « {account.TenantId} » incomplet : identifiant de compte et/ou "
                + "client_id / client_secret OAuth2 non renseignés. Action opérateur : complétez le compte "
                + "(Paramétrage › Plateforme Agréée). On bloque plutôt que d'envoyer sans authentification (CLAUDE.md n°3).");
        }

        var clientId = _secretProtector.Unprotect(secrets.EncryptedClientId, PaAccountSecretPurposes.ClientId);
        var clientSecret = _secretProtector.Unprotect(secrets.EncryptedClientSecret, PaAccountSecretPurposes.ClientSecret);

        // L'identifiant de compte (account_identifiers, NON secret) est REQUIS pour OAuth2 (D4 du plan).
        return new SuperPdpAccountConfig(
            MapEnvironment(secrets.Environment),
            secrets.AccountIdentifiers,
            clientId,
            clientSecret);
    }
}
