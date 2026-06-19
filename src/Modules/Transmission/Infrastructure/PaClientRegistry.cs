namespace Liakont.Modules.Transmission.Infrastructure;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Registre par défaut des plug-ins PA (PAA01 §5). Indexe les <see cref="IPaClientFactory"/>
/// enregistrés dans le conteneur DI par leur <see cref="IPaClientFactory.PaType"/> (insensible à la
/// casse) et résout le compte PA d'un tenant vers son client — UNIQUEMENT par la clé de type, jamais
/// par un <c>if (type == "B2Brouter")</c> (CLAUDE.md n°6/16 ; même patron que le registre d'IdP du
/// Host, décision D10). Un type inconnu lève (on bloque plutôt que d'envoyer faux — CLAUDE.md n°3).
/// </summary>
public sealed class PaClientRegistry : IPaClientRegistry
{
    private readonly IReadOnlyDictionary<string, IPaClientFactory> _factories;

    /// <summary>
    /// Construit le registre à partir des fabriques enregistrées par les plug-ins. Deux fabriques
    /// déclarant le même type est un bug d'enregistrement → lève au démarrage (jamais de résolution
    /// ambiguë silencieuse).
    /// </summary>
    /// <param name="factories">Fabriques fournies par les plug-ins PA (peut être vide avant tout plug-in).</param>
    public PaClientRegistry(IEnumerable<IPaClientFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);

        var map = new Dictionary<string, IPaClientFactory>(StringComparer.OrdinalIgnoreCase);
        foreach (var factory in factories)
        {
            if (string.IsNullOrWhiteSpace(factory.PaType))
            {
                throw new InvalidOperationException(
                    $"Un plug-in PA ({factory.GetType().FullName}) déclare un type vide. "
                    + "Chaque fabrique IPaClientFactory doit exposer un PaType non vide.");
            }

            if (map.ContainsKey(factory.PaType))
            {
                throw new InvalidOperationException(
                    $"Deux plug-ins PA déclarent le type « {factory.PaType} ». "
                    + "Chaque type de plateforme agréée doit être unique dans le registre.");
            }

            map[factory.PaType] = factory;
        }

        _factories = map;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> RegisteredTypes => (IReadOnlyCollection<string>)_factories.Keys;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PaAuthMode> DescribeAuthModes() =>
        _factories.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.AuthMode,
            StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool IsRegistered(string paType) =>
        !string.IsNullOrWhiteSpace(paType) && _factories.ContainsKey(paType);

    /// <inheritdoc />
    public IPaClient Resolve(PaAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (_factories.TryGetValue(account.PaType, out var factory))
        {
            return factory.Create(account);
        }

        var disponibles = _factories.Count == 0
            ? "aucun"
            : string.Join(", ", _factories.Keys);
        throw new InvalidOperationException(
            $"Aucun plug-in n'est enregistré pour la plateforme agréée « {account.PaType} » "
            + $"(tenant {account.TenantId}). Plug-ins disponibles : {disponibles}. "
            + "Vérifiez le type de compte PA paramétré pour ce tenant (CFG02).");
    }
}
