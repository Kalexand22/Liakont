namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Registre de TYPES de plug-ins PA : résout le compte PA d'un tenant vers l'implémentation
/// <see cref="IPaClient"/> enregistrée par le Host (PAA01 §5). La résolution se fait UNIQUEMENT par
/// le <see cref="PaAccountDescriptor.PaType"/> (clé), jamais par un <c>if (type == "B2Brouter")</c>
/// (CLAUDE.md n°6/16). Un type inconnu est une ERREUR DE CONFIGURATION : la résolution lève (on
/// bloque plutôt que d'envoyer faux — CLAUDE.md n°3), elle ne retourne jamais <c>null</c>.
/// </summary>
public interface IPaClientRegistry
{
    /// <summary>Types de plug-ins actuellement enregistrés (diagnostic, messages opérateur).</summary>
    IReadOnlyCollection<string> RegisteredTypes { get; }

    /// <summary>
    /// Mode d'authentification de CHAQUE type enregistré (clé = type, insensible à la casse) — la console
    /// l'utilise pour présenter les bons champs de creds à la création d'un compte PA, sans instancier de
    /// client (option 1 / PAS). Générique : jamais un <c>if (type == "SuperPdp")</c> (CLAUDE.md n°8/16).
    /// <para>Défaut : <see cref="PaAuthMode.ApiKey"/> pour tous les types enregistrés (le registre réel
    /// surcharge ce membre pour refléter le mode déclaré par chaque fabrique).</para>
    /// </summary>
    IReadOnlyDictionary<string, PaAuthMode> DescribeAuthModes() =>
        RegisteredTypes.ToDictionary(
            paType => paType,
            _ => PaAuthMode.ApiKey,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Résout le client PA d'un compte de tenant par son type. Lève une
    /// <see cref="InvalidOperationException"/> avec un message opérateur français si le type n'est
    /// enregistré par aucun plug-in.
    /// </summary>
    /// <param name="account">Description du compte PA du tenant.</param>
    IPaClient Resolve(PaAccountDescriptor account);

    /// <summary>Vrai si un plug-in est enregistré pour ce type (insensible à la casse).</summary>
    /// <param name="paType">Type de plug-in à tester.</param>
    bool IsRegistered(string paType);
}
