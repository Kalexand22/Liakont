namespace Liakont.Host.PaDelivery;

using System.Text.Json;
using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.ChorusPro;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation Host de <see cref="IChorusProAccountResolver"/> : seul endroit qui DÉCHIFFRE les secrets
/// du compte Chorus Pro d'un tenant (client_id / client_secret OAuth2 PISTE + mot de passe du compte
/// TECHNIQUE Chorus Pro). Le plug-in ne voit que <c>Transmission.Contracts</c> (module-rules §6) ; le Host,
/// lui, voit TenantSettings et fournit les secrets déchiffrés via cette frontière. Bloque (jamais d'envoi
/// sans authentification — CLAUDE.md n°3) si le compte est absent, un secret manquant, ou la configuration
/// non sensible (URLs / login / e-mail / identifiant) incomplète.
/// </summary>
/// <remarks>
/// <para>
/// Modèle technique : <see cref="SuperPdpAccountResolver"/> (résolveur OAuth2 déjà livré). Comme la fabrique
/// qui le consomme est un singleton, ce résolveur est aussi un singleton ; il ne peut pas injecter de service
/// scopé, d'où l'ouverture d'un scope tenant dédié (<see cref="ITenantScopeFactory"/>) à la résolution, et le
/// <c>Task.Run</c> qui isole l'await du <see cref="SynchronizationContext"/> de l'appelant (au SEND comme au
/// RENDU UI Blazor) — voir <see cref="SuperPdpAccountResolver"/> pour le détail du deadlock évité.
/// </para>
/// <para>
/// Les SECRETS (client_id/secret PISTE, mot de passe technique) sont lus CHIFFRÉS via
/// <see cref="IPaAccountSecretStore"/> et déchiffrés PAR PURPOSE (isolation cryptographique). Les valeurs NON
/// SENSIBLES propres à Chorus Pro — URLs verrouillées au raccordement (F18 §3.3 « ne pas hardcoder »), login
/// du compte technique (en-tête <c>cpro-account</c>), e-mail de connexion (résolution
/// <c>idUtilisateurCourant</c>, F18 §3.2) et identifiant de compte — voyagent dans le champ opaque
/// <c>account_identifiers</c> (JSON libre du plug-in, saisi par l'opérateur dans « Identifiant de compte »
/// — CP06). Elles ne sont JAMAIS figées en dur ici (CLAUDE.md n°7 : aucune donnée client dans le code).
/// </para>
/// </remarks>
internal sealed class ChorusProAccountResolver : IChorusProAccountResolver
{
    /// <summary>Clé JSON (dans <c>account_identifiers</c>) : identifiant de compte Chorus Pro (audit/lectures).</summary>
    public const string AccountIdKey = "accountId";

    /// <summary>Clé JSON : login du compte technique Chorus Pro (en-tête <c>cpro-account</c>, F18 §2.2).</summary>
    public const string TechnicalLoginKey = "technicalLogin";

    /// <summary>Clé JSON : e-mail de connexion du compte technique (résolution <c>idUtilisateurCourant</c>, F18 §3.2).</summary>
    public const string ConnectionEmailKey = "connectionEmail";

    /// <summary>Clé JSON : base API Chorus Pro (<c>cpro</c>), verrouillée au raccordement (F18 §3.3, absolue).</summary>
    public const string BaseUrlKey = "baseUrl";

    /// <summary>Clé JSON : endpoint jeton OAuth2 PISTE, verrouillé au raccordement (F18 §2.1, absolu).</summary>
    public const string TokenEndpointKey = "tokenEndpoint";

    private readonly ITenantScopeFactory _tenantScopeFactory;
    private readonly ISecretProtector _secretProtector;

    public ChorusProAccountResolver(ITenantScopeFactory tenantScopeFactory, ISecretProtector secretProtector)
    {
        ArgumentNullException.ThrowIfNull(tenantScopeFactory);
        ArgumentNullException.ThrowIfNull(secretProtector);
        _tenantScopeFactory = tenantScopeFactory;
        _secretProtector = secretProtector;
    }

    public ChorusProAccountConfig Resolve(PaAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);

        // IChorusProAccountResolver.Resolve est SYNCHRONE (contrat IPaClientFactory.Create figé). Task.Run
        // offload la résolution sur un thread du pool (sans SynchronizationContext capturé) → aucun deadlock,
        // que l'appelant soit le SEND (pool de threads) ou le RENDU UI (circuit Blazor Server mono-thread).
        // Voir SuperPdpAccountResolver pour l'analyse complète du deadlock évité.
        return Task.Run(() => ResolveAsync(account)).GetAwaiter().GetResult();
    }

    private static ChorusProEnvironment MapEnvironment(PaEnvironment environment) => environment switch
    {
        PaEnvironment.Staging => ChorusProEnvironment.Qualification,
        PaEnvironment.Production => ChorusProEnvironment.Production,
        _ => ChorusProEnvironment.Qualification,
    };

    private static Dictionary<string, string> ParseAccountIdentifiers(string accountIdentifiers, string tenantId)
    {
        try
        {
            using var document = JsonDocument.Parse(accountIdentifiers);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(BlockedMessage(tenantId, "le champ « Identifiant de compte » n'est pas un objet JSON"));
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    values[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }

            return values;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                BlockedMessage(tenantId, "le champ « Identifiant de compte » n'est pas un JSON valide"), ex);
        }
    }

    private static string RequiredString(IReadOnlyDictionary<string, string> identifiers, string key, string tenantId)
    {
        if (!identifiers.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(BlockedMessage(tenantId, $"la valeur « {key} » est absente"));
        }

        return value;
    }

    private static Uri ToAbsoluteUri(IReadOnlyDictionary<string, string> identifiers, string key, string tenantId)
    {
        var raw = RequiredString(identifiers, key, tenantId);

        // Exige un schéma http(s) absolu, pas seulement UriKind.Absolute : sur un hôte Unix (conteneur),
        // Uri.TryCreate accepterait un chemin relatif comme « /cpro/ » en file-URI absolu (file:///cpro/),
        // laissant passer une URL invalide pour un appel HTTP Chorus Pro. On bloque (CLAUDE.md n°3).
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(BlockedMessage(tenantId, $"l'URL « {key} » n'est pas une URL http(s) absolue"));
        }

        return uri;
    }

    private static string BlockedMessage(string tenantId, string reason) =>
        $"Compte Chorus Pro du tenant « {tenantId} » incomplet : {reason}. Action opérateur : renseignez le "
        + "champ « Identifiant de compte » au format JSON attendu (accountId, technicalLogin, connectionEmail, "
        + "baseUrl, tokenEndpoint — URLs *.piste.gouv.fr verrouillées au raccordement, F18 §3.3). On bloque "
        + "plutôt que d'envoyer faux (CLAUDE.md n°3).";

    private async Task<ChorusProAccountConfig> ResolveAsync(PaAccountDescriptor account)
    {
        await using var scope = _tenantScopeFactory.Create(account.TenantId);

        var companyId = await scope.Services.GetRequiredService<ITenantSettingsQueries>()
            .GetCurrentCompanyId()
            .ConfigureAwait(false);
        if (companyId is null)
        {
            throw new InvalidOperationException(
                $"Compte Chorus Pro introuvable : aucun profil tenant (companyId) pour « {account.TenantId} ». "
                + "Action opérateur : créez le profil du tenant, puis configurez un compte Chorus Pro actif.");
        }

        var secrets = await scope.Services.GetRequiredService<IPaAccountSecretStore>()
            .GetActiveAsync(companyId.Value, account.PaType)
            .ConfigureAwait(false);
        if (secrets is null)
        {
            throw new InvalidOperationException(
                $"Aucun compte Chorus Pro actif pour le tenant « {account.TenantId} ». "
                + "Action opérateur : configurez et activez un compte Chorus Pro (Paramétrage › Plateforme Agréée).");
        }

        if (string.IsNullOrWhiteSpace(secrets.AccountIdentifiers)
            || string.IsNullOrWhiteSpace(secrets.EncryptedClientId)
            || string.IsNullOrWhiteSpace(secrets.EncryptedClientSecret)
            || string.IsNullOrWhiteSpace(secrets.EncryptedTechnicalPassword))
        {
            throw new InvalidOperationException(
                $"Compte Chorus Pro du tenant « {account.TenantId} » incomplet : identifiant de compte et/ou "
                + "client_id / client_secret OAuth2 PISTE et/ou mot de passe du compte technique non renseignés. "
                + "Action opérateur : complétez le compte (Paramétrage › Plateforme Agréée). On bloque plutôt que "
                + "d'envoyer sans authentification (CLAUDE.md n°3).");
        }

        // Secrets : déchiffrés sous LEUR purpose dédié (isolation cryptographique). En mémoire uniquement,
        // jamais journalisés (CLAUDE.md n°10).
        var pisteClientId = _secretProtector.Unprotect(secrets.EncryptedClientId, PaAccountSecretPurposes.ClientId);
        var pisteClientSecret = _secretProtector.Unprotect(secrets.EncryptedClientSecret, PaAccountSecretPurposes.ClientSecret);
        var technicalPassword = _secretProtector.Unprotect(secrets.EncryptedTechnicalPassword, PaAccountSecretPurposes.TechnicalPassword);

        // Configuration NON SENSIBLE (URLs verrouillées au raccordement, login/e-mail technique, identifiant) :
        // JSON libre du plug-in (F18 §3.3 / CLAUDE.md n°7 — jamais en dur). On bloque si le JSON est invalide ou
        // une valeur obligatoire absente (le constructeur de ChorusProAccountConfig valide aussi format/absoluité).
        var identifiers = ParseAccountIdentifiers(secrets.AccountIdentifiers, account.TenantId);

        return new ChorusProAccountConfig(
            MapEnvironment(secrets.Environment),
            baseUrl: ToAbsoluteUri(identifiers, BaseUrlKey, account.TenantId),
            tokenEndpoint: ToAbsoluteUri(identifiers, TokenEndpointKey, account.TenantId),
            accountId: RequiredString(identifiers, AccountIdKey, account.TenantId),
            pisteClientId: pisteClientId,
            pisteClientSecret: pisteClientSecret,
            technicalLogin: RequiredString(identifiers, TechnicalLoginKey, account.TenantId),
            technicalPassword: technicalPassword,
            connectionEmail: RequiredString(identifiers, ConnectionEmailKey, account.TenantId));
    }
}
