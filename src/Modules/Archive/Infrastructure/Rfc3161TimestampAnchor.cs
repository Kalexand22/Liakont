namespace Liakont.Modules.Archive.Infrastructure;

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Domain;

/// <summary>
/// Ancrage temporel RFC 3161 (TRK06) — API NATIVES <c>System.Security.Cryptography.Pkcs</c>, AUCUNE
/// dépendance externe (le point dur net48 du backlog v5 n'existe plus en .NET 10). Construit une requête
/// d'horodatage sur l'empreinte de tête de chaîne, l'envoie à la TSA via <see cref="ITsaClient"/> (couture
/// testable), et conserve le jeton signé comme preuve. La vérification (<see cref="VerifyAsync"/>) est
/// 100 % hors-ligne : signature de la TSA + correspondance de l'empreinte scellée. La confiance dans la
/// TSA (qualifiée eIDAS) est établie par la configuration d'instance (URL/certificat), pas codée en dur.
/// </summary>
public sealed class Rfc3161TimestampAnchor : ITimestampAnchor
{
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    private readonly ITsaClient _tsaClient;

    public Rfc3161TimestampAnchor(ITsaClient tsaClient)
    {
        _tsaClient = tsaClient;
    }

    public TimestampAnchorCapabilities Capabilities => new(
        TimestampAnchorMethod.Rfc3161,
        IsOperational: true,
        ProducesImmediateProof: true,
        RequiresOutboundInternet: true);

    public async Task<TimestampAnchorResult> AnchorAsync(byte[] chainHeadDigest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chainHeadDigest);

        byte[] nonce = RandomNumberGenerator.GetBytes(16);
        var request = Rfc3161TimestampRequest.CreateFromHash(
            new ReadOnlyMemory<byte>(chainHeadDigest),
            HashAlgorithm,
            requestedPolicyId: null,
            nonce: new ReadOnlyMemory<byte>(nonce),
            requestSignerCertificates: true);

        byte[] requestDer = request.Encode();
        byte[] responseDer = await _tsaClient.RequestTokenAsync(requestDer, cancellationToken);

        Rfc3161TimestampToken token = request.ProcessResponse(new ReadOnlyMemory<byte>(responseDer), out _);
        byte[] proof = token.AsSignedCms().Encode();
        DateTimeOffset anchoredUtc = token.TokenInfo.Timestamp;

        return new TimestampAnchorResult(
            TimestampAnchorMethod.Rfc3161,
            IsAnchored: true,
            Proof: proof,
            ProofContentType: "application/timestamp-token",
            AnchoredUtc: anchoredUtc,
            Detail: $"Tête de chaîne horodatée par la TSA le {anchoredUtc.ToString("O", CultureInfo.InvariantCulture)}.");
    }

    public Task<TimestampVerification> VerifyAsync(byte[] proof, byte[] chainHeadDigest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(chainHeadDigest);

        if (!Rfc3161TimestampToken.TryDecode(new ReadOnlyMemory<byte>(proof), out Rfc3161TimestampToken? token, out _) || token is null)
        {
            return Task.FromResult(new TimestampVerification(IsValid: false, AnchoredUtc: null, "Jeton d'horodatage RFC 3161 illisible."));
        }

        bool valid = token.VerifySignatureForHash(chainHeadDigest, HashAlgorithm, out _);
        return Task.FromResult(valid
            ? new TimestampVerification(IsValid: true, token.TokenInfo.Timestamp, "Jeton RFC 3161 valide pour cette tête de chaîne.")
            : new TimestampVerification(IsValid: false, token.TokenInfo.Timestamp, "Le jeton RFC 3161 n'atteste pas cette empreinte de tête de chaîne (signature ou empreinte invalide)."));
    }
}
