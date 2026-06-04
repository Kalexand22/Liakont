namespace Liakont.Modules.Archive.Domain;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Ancrage « néant » (TRK06) : aucune attestation externe. Choix d'INSTANCE pour un déploiement sans accès
/// internet sortant (air-gapped) — l'intégrité du coffre reste entièrement portée par la chaîne de hashes
/// (blueprint §6). C'est le défaut programmatique : une instance non configurée ne tente AUCUN appel sortant.
/// </summary>
public sealed class NoAnchorTimestampAnchor : ITimestampAnchor
{
    private const string NotAnchoredDetail =
        "Aucun ancrage temporel configuré (NoAnchor) : l'intégrité repose sur la chaîne de hashes du coffre.";

    public TimestampAnchorCapabilities Capabilities => new(
        TimestampAnchorMethod.None,
        IsOperational: true,
        ProducesImmediateProof: false,
        RequiresOutboundInternet: false);

    public Task<TimestampAnchorResult> AnchorAsync(byte[] chainHeadDigest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chainHeadDigest);
        return Task.FromResult(TimestampAnchorResult.NotAnchored(TimestampAnchorMethod.None, NotAnchoredDetail));
    }

    public Task<TimestampVerification> VerifyAsync(byte[] proof, byte[] chainHeadDigest, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TimestampVerification(
            IsValid: false,
            AnchoredUtc: null,
            Detail: "NoAnchor ne produit pas de preuve d'ancrage : il n'y a rien à vérifier."));
}
