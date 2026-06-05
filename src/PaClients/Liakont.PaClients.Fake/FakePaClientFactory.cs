namespace Liakont.PaClients.Fake;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Fabrique du plug-in factice (PAA02) — elle s'enregistre dans le conteneur DI EXACTEMENT comme la
/// fabrique de B2Brouter (PAB) ou de Super PDP (PAS) ; le <see cref="IPaClientRegistry"/> l'indexe par
/// <see cref="PaType"/>, sans qu'aucun code produit n'ait à connaître « Fake » (résolution par clé,
/// jamais un <c>if (type == …)</c> — CLAUDE.md n°6/16). Attendue en singleton (capturée par le
/// registre singleton).
/// </summary>
public sealed class FakePaClientFactory : IPaClientFactory
{
    /// <summary>Clé de registre du plug-in factice (insensible à la casse côté registre).</summary>
    public const string PaTypeKey = "Fake";

    private readonly FakePaClientOptions _options;

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
        return new FakePaClient(_options);
    }
}
