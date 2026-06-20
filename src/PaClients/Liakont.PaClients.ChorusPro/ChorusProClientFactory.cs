namespace Liakont.PaClients.ChorusPro;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Fabrique du plug-in Chorus Pro (ajouter-un-plugin-pa §3) : un client = un compte PA de tenant
/// (creds PISTE + compte technique + URLs verrouillées — F18 §2/§3.3). Le <see cref="IPaClientRegistry"/>
/// du module Transmission l'indexe par <see cref="PaType"/> ; la résolution d'un compte se fait par la
/// CLÉ, jamais par un <c>if (pa is ChorusPro)</c> (CLAUDE.md n°6/16). Enregistrée en singleton.
/// <para>
/// SQUELETTE CP02 : la fabrique exerce déjà la frontière <see cref="IChorusProAccountResolver"/> (le Host
/// déchiffre les secrets du coffre du tenant) pour échouer TÔT sur un compte mal configuré (CLAUDE.md n°3),
/// mais le client qu'elle rend ne consomme pas encore le transport HTTP — le dépôt / la relecture arrivent
/// avec CP03+. Le câblage HTTP nommé (TLS 1.2/1.3) est posé par <see cref="ChorusProPaClientRegistration"/>.
/// </para>
/// </summary>
public sealed class ChorusProClientFactory : IPaClientFactory
{
    private readonly IChorusProAccountResolver _accountResolver;

    /// <summary>Construit la fabrique.</summary>
    /// <param name="accountResolver">
    /// Résout le descripteur non sensible du tenant vers les identifiants déchiffrés (frontière
    /// plug-in ↔ coffre du tenant — voir <see cref="IChorusProAccountResolver"/>).
    /// </param>
    public ChorusProClientFactory(IChorusProAccountResolver accountResolver)
    {
        ArgumentNullException.ThrowIfNull(accountResolver);
        _accountResolver = accountResolver;
    }

    /// <inheritdoc />
    public string PaType => ChorusProDefaults.PaTypeKey;

    /// <inheritdoc />
    public PaAuthMode AuthMode => PaAuthMode.OAuth2WithTechnicalAccount;

    /// <inheritdoc />
    public IPaClient Create(PaAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);

        // Résout et VALIDE la configuration du compte via la frontière resolver : la résolution lève si
        // le compte est inconnu ou si un secret / une URL manque (le constructeur de
        // ChorusProAccountConfig valide) — on échoue TÔT sur un compte mal configuré (CLAUDE.md n°3,
        // bloquer plutôt qu'envoyer faux). Le SQUELETTE ne consomme pas encore le transport HTTP (le
        // dépôt deposerFluxFacture et la relecture consulterCR arrivent avec CP03+) ; la frontière est
        // néanmoins exercée dès maintenant pour que le câblage de bout en bout soit éprouvé.
        _ = _accountResolver.Resolve(account);

        return new ChorusProClient(ChorusProCapabilities.Declared);
    }
}
