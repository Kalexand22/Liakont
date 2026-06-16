namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Registre de TYPES de plug-ins de signature : résout le compte de signature d'un tenant vers
/// l'implémentation <see cref="ISignatureProvider"/> enregistrée par le Host (ADR-0027 §4). La résolution
/// se fait UNIQUEMENT par le <see cref="SignatureProviderAccount.ProviderType"/> (clé), jamais par un
/// <c>if (type == "Yousign")</c> (CLAUDE.md n°6/16). Demander un type non enregistré est une ERREUR DE
/// CONFIGURATION : <see cref="Resolve"/> lève (on bloque plutôt que d'agir faux — CLAUDE.md n°3), elle ne
/// retourne jamais <c>null</c>. NB : l'ABSENCE de tout fournisseur n'est, elle, jamais une erreur — la
/// signature est optionnelle (le défaut <c>Recorded</c> fonctionne) ; cette optionalité est vérifiée au
/// démarrage par le sélecteur du composition root, pas par ce registre (INV-SIGPROV-6).
/// </summary>
public interface ISignatureProviderRegistry
{
    /// <summary>Types de plug-ins actuellement enregistrés (diagnostic, messages opérateur).</summary>
    IReadOnlyCollection<string> RegisteredTypes { get; }

    /// <summary>
    /// Résout le fournisseur de signature d'un compte de tenant par son type. Lève une
    /// <see cref="InvalidOperationException"/> avec un message opérateur français si le type n'est
    /// enregistré par aucun plug-in.
    /// </summary>
    /// <param name="account">Description du compte de signature du tenant.</param>
    ISignatureProvider Resolve(SignatureProviderAccount account);

    /// <summary>Vrai si un plug-in est enregistré pour ce type (insensible à la casse).</summary>
    /// <param name="providerType">Type de plug-in à tester.</param>
    bool IsRegistered(string providerType);
}
