namespace Liakont.PaClients.Fake;

using System.Collections.Concurrent;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Fabrique du plug-in factice (PAA02) — elle s'enregistre dans le conteneur DI EXACTEMENT comme la
/// fabrique de B2Brouter (PAB) ou de Super PDP (PAS) ; le <see cref="IPaClientRegistry"/> l'indexe par
/// <see cref="PaType"/>, sans qu'aucun code produit n'ait à connaître « Fake » (résolution par clé,
/// jamais un <c>if (type == …)</c> — CLAUDE.md n°6/16). Attendue en singleton (capturée par le
/// registre singleton).
/// <para>
/// État PAR COMPTE (FIX201) : contrairement à un vrai plug-in HTTP (sans état — le SIREN / le
/// <c>tax_report_setting</c> vivent côté PA, relus à chaque appel), le plug-in factice SIMULE la PA EN
/// MÉMOIRE. Son état (réglage « assuré » par <see cref="IPaClient.EnsureTaxReportSettingAsync"/>,
/// idempotence d'émission) vit dans l'instance. La fabrique singleton garde donc UNE instance par compte
/// (<see cref="PaAccountDescriptor.PaType"/> + <see cref="PaAccountDescriptor.TenantId"/>) : l'onboarding
/// (publication du SIREN) et l'envoi qui suit — deux résolutions distinctes du registre — partagent ainsi
/// le même réglage, sinon l'activation serait perdue à la résolution suivante (l'envoi resterait
/// « Transport not available » pour toujours).
/// </para>
/// </summary>
public sealed class FakePaClientFactory : IPaClientFactory
{
    /// <summary>Clé de registre du plug-in factice (insensible à la casse côté registre).</summary>
    public const string PaTypeKey = "Fake";

    private readonly FakePaClientOptions _options;

    // Une instance factice par compte (PaType+TenantId). Le registre singleton capture la fabrique ; le
    // cache est donc à durée de vie processus, partagé par toutes les résolutions d'un même compte.
    private readonly ConcurrentDictionary<string, FakePaClient> _clientsByAccount = new(StringComparer.Ordinal);

    /// <summary>Construit la fabrique avec la configuration que recevront les clients qu'elle crée.</summary>
    /// <param name="options">Configuration du plug-in factice, ou <c>null</c> pour les valeurs par défaut.</param>
    public FakePaClientFactory(FakePaClientOptions? options = null)
    {
        _options = options ?? new FakePaClientOptions();
    }

    /// <inheritdoc />
    public string PaType => PaTypeKey;

    /// <inheritdoc />
    public IPaClient Create(PaAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);

        // Le plug-in factice ne lit aucun secret : il ignore la configuration sensible du compte et ne
        // dépend que de ses propres options (le compte est néanmoins exigé non nul, comme un vrai plug-in).
        // MÊME instance par compte (FIX201) : voir la remarque de classe — l'état simulé (réglage publié,
        // idempotence) doit survivre entre l'onboarding et l'envoi (deux résolutions du registre).
        return _clientsByAccount.GetOrAdd(AccountKey(account), _ => new FakePaClient(_options));
    }

    // Clé de cache normalisée : le type de plug-in est mis en majuscules (le registre résout déjà la casse
    // du type) ; le tenant (slug) est comparé tel quel. Le caractère NUL sépare les deux segments — il ne
    // peut apparaître ni dans une clé de registre ni dans un slug de tenant, donc aucune collision possible.
    private static string AccountKey(PaAccountDescriptor account) =>
        string.Concat(account.PaType.ToUpperInvariant(), "\0", account.TenantId);
}
