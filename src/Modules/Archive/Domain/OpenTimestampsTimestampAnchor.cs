namespace Liakont.Modules.Archive.Domain;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Ancrage OpenTimestamps (ancrage blockchain Bitcoin) — PRÉSENT mais NON OPÉRATIONNEL en V1 (ADR-0010).
/// Le protocole .ots complet (sérialisation, upgrade calendar, vérification Merkle/Bitcoin) n'a aucune
/// bibliothèque .NET mûre et licence-compatible ; un sous-ensemble maison serait non vérifiable pour un
/// produit de conformité fiscale. La décision (ADR-0010) le reporte en V1.1, RFC 3161 restant l'ancrage
/// recommandé. Cet ancrage refuse EXPLICITEMENT son usage (jamais un no-op silencieux = faux vert) : la
/// capacité <see cref="TimestampAnchorCapabilities.IsOperational"/> est <c>false</c> et tout appel lève.
/// </summary>
public sealed class OpenTimestampsTimestampAnchor : ITimestampAnchor
{
    internal const string DeferredMessage =
        "L'ancrage OpenTimestamps est reporté en V1.1 (ADR-0010) : choisissez RFC 3161 (recommandé) ou NoAnchor. " +
        "Le plug-in OpenTimestamps sera fourni en fast-follow sans modifier le module Archive.";

    public TimestampAnchorCapabilities Capabilities => new(
        TimestampAnchorMethod.OpenTimestamps,
        IsOperational: false,
        ProducesImmediateProof: false,
        RequiresOutboundInternet: true);

    public Task<TimestampAnchorResult> AnchorAsync(byte[] chainHeadDigest, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(DeferredMessage);

    public Task<TimestampVerification> VerifyAsync(byte[] proof, byte[] chainHeadDigest, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(DeferredMessage);
}
