namespace Liakont.Modules.Signature.Infrastructure;

using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Registre par défaut des plug-ins de signature (ADR-0027 §4). Indexe les
/// <see cref="ISignatureProviderFactory"/> enregistrés dans le conteneur DI par leur
/// <see cref="ISignatureProviderFactory.ProviderType"/> (insensible à la casse) et résout le compte de
/// signature d'un tenant vers son fournisseur — UNIQUEMENT par la clé de type, jamais par un
/// <c>if (type == "Yousign")</c> (CLAUDE.md n°6/16 ; même patron que <c>PaClientRegistry</c> et que le
/// registre d'IdP du Host). Un type DEMANDÉ mais inconnu lève (on bloque plutôt que d'agir faux —
/// CLAUDE.md n°3) ; l'absence de tout plug-in, elle, n'est pas une erreur (signature optionnelle).
/// </summary>
public sealed class SignatureProviderRegistry : ISignatureProviderRegistry
{
    private readonly IReadOnlyDictionary<string, ISignatureProviderFactory> _factories;

    /// <summary>
    /// Construit le registre à partir des fabriques enregistrées par les plug-ins. Deux fabriques
    /// déclarant le même type est un bug d'enregistrement → lève au démarrage (jamais de résolution
    /// ambiguë silencieuse). Un ensemble vide est valide (aucun fournisseur configuré).
    /// </summary>
    /// <param name="factories">Fabriques fournies par les plug-ins de signature (peut être vide).</param>
    public SignatureProviderRegistry(IEnumerable<ISignatureProviderFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);

        var map = new Dictionary<string, ISignatureProviderFactory>(StringComparer.OrdinalIgnoreCase);
        foreach (var factory in factories)
        {
            if (string.IsNullOrWhiteSpace(factory.ProviderType))
            {
                throw new InvalidOperationException(
                    $"Un plug-in de signature ({factory.GetType().FullName}) déclare un type vide. "
                    + "Chaque fabrique ISignatureProviderFactory doit exposer un ProviderType non vide.");
            }

            if (map.ContainsKey(factory.ProviderType))
            {
                throw new InvalidOperationException(
                    $"Deux plug-ins de signature déclarent le type « {factory.ProviderType} ». "
                    + "Chaque type de fournisseur de signature doit être unique dans le registre.");
            }

            map[factory.ProviderType] = factory;
        }

        _factories = map;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> RegisteredTypes => (IReadOnlyCollection<string>)_factories.Keys;

    /// <inheritdoc />
    public bool IsRegistered(string providerType) =>
        !string.IsNullOrWhiteSpace(providerType) && _factories.ContainsKey(providerType);

    /// <inheritdoc />
    public ISignatureProvider Resolve(SignatureProviderAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (_factories.TryGetValue(account.ProviderType, out var factory))
        {
            return factory.Create(account);
        }

        var disponibles = _factories.Count == 0
            ? "aucun"
            : string.Join(", ", _factories.Keys);
        throw new InvalidOperationException(
            $"Aucun plug-in n'est enregistré pour le fournisseur de signature « {account.ProviderType} » "
            + $"(tenant {account.CompanyId}). Plug-ins disponibles : {disponibles}. "
            + "Vérifiez le type de compte de signature paramétré pour ce tenant.");
    }
}
