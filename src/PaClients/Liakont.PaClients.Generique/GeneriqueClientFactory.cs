namespace Liakont.PaClients.Generique;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Fabrique du plug-in générique (ajouter-un-plugin-pa §3) : un client = un compte PA de tenant (canal de
/// livraison + cible + secret SMTP éventuel). Le <see cref="IPaClientRegistry"/> du module Transmission
/// l'indexe par <see cref="PaType"/> ; la résolution se fait par la CLÉ, jamais par un
/// <c>if (pa is Generique)</c> (CLAUDE.md n°6/16). Enregistrée en singleton ; elle résout la configuration
/// du compte via <see cref="IGeneriqueAccountResolver"/> (frontière plug-in ↔ coffre du tenant) puis
/// sélectionne le canal de livraison Host correspondant au mode du compte.
/// </summary>
public sealed class GeneriqueClientFactory : IPaClientFactory
{
    private readonly IReadOnlyList<IDocumentDeliveryChannel> _channels;
    private readonly IGeneriqueAccountResolver _accountResolver;

    /// <summary>Construit la fabrique.</summary>
    /// <param name="channels">
    /// Canaux de livraison disponibles (implémentés au Host : email avec pièce jointe, dépôt de fichier).
    /// </param>
    /// <param name="accountResolver">
    /// Résout le descripteur non sensible du tenant vers la configuration de livraison (et déchiffre le
    /// secret SMTP éventuel — voir <see cref="IGeneriqueAccountResolver"/>).
    /// </param>
    public GeneriqueClientFactory(
        IEnumerable<IDocumentDeliveryChannel> channels,
        IGeneriqueAccountResolver accountResolver)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(accountResolver);
        _channels = channels.ToArray();
        _accountResolver = accountResolver;
    }

    /// <inheritdoc />
    public string PaType => GeneriqueDefaults.PaTypeKey;

    /// <inheritdoc />
    public IPaClient Create(PaAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var config = _accountResolver.Resolve(account);

        // Sélection du canal par le MODE déclaré du compte (jamais un if sur le type de PA — CLAUDE.md n°8).
        var channel = _channels.FirstOrDefault(c => c.Method == config.Method)
            ?? throw new InvalidOperationException(
                $"Aucun canal de livraison « {config.Method} » n'est enregistré pour le compte générique du "
                + $"tenant « {account.TenantId} ». Vérifiez le câblage des canaux au Host.");

        return new GeneriqueClient(channel, config);
    }
}
