namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Contexte d'un webhook de complétion reçu d'un fournisseur (ADR-0027 §2 ; détail durable/HMAC en
/// ADR-0029, SIG07). Surface STABLE et indépendante du fournisseur : le corps brut est transmis tel quel
/// (la vérification HMAC se fait sur les OCTETS EXACTS du corps, jamais sur un objet reconstruit), et les
/// en-têtes sont une projection en lecture seule. AUCUN type HTTP ne traverse l'abstraction
/// (INV-SIGPROV-8) — c'est le Host qui adapte la requête HTTP vers ce contexte.
/// </summary>
public sealed record SignatureWebhookContext
{
    /// <summary>Octets exacts du corps de la requête (pour la vérification HMAC à temps constant — SIG07).</summary>
    public required IReadOnlyList<byte> RawBody { get; init; }

    /// <summary>En-têtes pertinents (signature HMAC, type d'événement…), projection en lecture seule.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Handle opaque de tenant extrait de l'URL de webhook (routage hors requête métier — SIG07).</summary>
    public string? TenantHandle { get; init; }
}
