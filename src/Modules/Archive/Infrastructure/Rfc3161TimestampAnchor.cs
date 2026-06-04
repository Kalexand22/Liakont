namespace Liakont.Modules.Archive.Infrastructure;

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Domain;
using Microsoft.Extensions.Options;

/// <summary>
/// Ancrage temporel RFC 3161 (TRK06) — API NATIVES <c>System.Security.Cryptography.Pkcs</c>, AUCUNE
/// dépendance tierce. Construit une requête d'horodatage sur l'empreinte de tête de chaîne, l'envoie à la
/// TSA via <see cref="ITsaClient"/> (couture testable), et conserve le jeton signé comme preuve. La
/// vérification (<see cref="VerifyAsync"/>) est 100 % hors-ligne : signature de la TSA + correspondance de
/// l'empreinte scellée. La CONFIANCE dans la TSA (qualifiée eIDAS) est établie par la configuration
/// d'instance : si un certificat est épinglé (<see cref="TimestampAnchorOptions.Rfc3161Options.TrustedCertificateBase64"/>),
/// le signataire du jeton doit l'égaler (ferme la forge d'un jeton auto-signé) ; sinon, la vérification
/// in-produit signale explicitement que l'identité de la TSA n'est pas épinglée (jamais une garantie surévaluée).
/// </summary>
public sealed class Rfc3161TimestampAnchor : ITimestampAnchor
{
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    private readonly ITsaClient _tsaClient;
    private readonly string? _trustedThumbprint;

    public Rfc3161TimestampAnchor(ITsaClient tsaClient, IOptions<TimestampAnchorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _tsaClient = tsaClient;
        _trustedThumbprint = ResolveTrustedThumbprint(options.Value.Rfc3161.TrustedCertificateBase64);
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
            return Task.FromResult(new TimestampVerification(IsValid: false, IsAuthorityAuthenticated: false, AnchoredUtc: null, "Jeton d'horodatage RFC 3161 illisible."));
        }

        bool signatureValid = token.VerifySignatureForHash(chainHeadDigest, HashAlgorithm, out X509Certificate2? signerCertificate);
        DateTimeOffset anchoredUtc = token.TokenInfo.Timestamp;

        // `signerCertificate` est IDisposable : on le libère sur TOUS les chemins de sortie (handle natif).
        using (signerCertificate)
        {
            if (!signatureValid)
            {
                return Task.FromResult(new TimestampVerification(
                    IsValid: false,
                    IsAuthorityAuthenticated: false,
                    anchoredUtc,
                    "Le jeton RFC 3161 n'atteste pas cette empreinte de tête de chaîne (signature ou empreinte invalide)."));
            }

            // La signature est valide pour cette empreinte ; reste à AUTHENTIFIER la TSA selon la config d'instance.
            if (_trustedThumbprint is null)
            {
                const string unpinnedCaveat = "Jeton RFC 3161 valide (signature + empreinte). Identité de la TSA NON épinglée (Archive:Anchor:Rfc3161:TrustedCertificateBase64 absent) : authentification autoritaire par contrôle externe.";
                return Task.FromResult(new TimestampVerification(IsValid: true, IsAuthorityAuthenticated: false, anchoredUtc, unpinnedCaveat));
            }

            bool pinned = signerCertificate is not null
                && string.Equals(signerCertificate.Thumbprint, _trustedThumbprint, StringComparison.OrdinalIgnoreCase);

            return Task.FromResult(pinned
                ? new TimestampVerification(IsValid: true, IsAuthorityAuthenticated: true, anchoredUtc, "Jeton RFC 3161 valide ; TSA épinglée authentifiée.")
                : new TimestampVerification(
                    IsValid: false,
                    IsAuthorityAuthenticated: false,
                    anchoredUtc,
                    "Le certificat signataire du jeton ne correspond pas à la TSA épinglée (jeton potentiellement forgé)."));
        }
    }

    private static string? ResolveTrustedThumbprint(string? trustedCertificateBase64)
    {
        if (string.IsNullOrWhiteSpace(trustedCertificateBase64))
        {
            return null;
        }

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(trustedCertificateBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Archive:Anchor:Rfc3161:TrustedCertificateBase64 n'est pas un base64 valide (certificat DER attendu).", ex);
        }

        using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(raw);
        return certificate.Thumbprint;
    }
}
