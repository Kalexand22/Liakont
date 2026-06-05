namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

/// <summary>
/// Autorité d'horodatage RFC 3161 de TEST (« TSA mockée », TRK06). Émet de VRAIES réponses RFC 3161
/// (TimeStampResp DER) signées par un certificat auto-signé porteur de l'EKU id-kp-timeStamping, afin
/// d'exercer le chemin cryptographique RÉEL de <c>Rfc3161TimestampAnchor</c> (construction de requête,
/// <c>ProcessResponse</c>, vérification de signature) sans dépendre d'une TSA externe.
/// </summary>
internal sealed class TestTimestampAuthority : IDisposable
{
    private static readonly Oid TstInfoContentType = new("1.2.840.113549.1.9.16.1.4");
    private static readonly Oid TimeStampingEku = new("1.3.6.1.5.5.7.3.8");
    private static readonly Oid SigningCertificateV2Oid = new("1.2.840.113549.1.9.16.2.47");
    private static readonly Oid Sha256Oid = new("2.16.840.1.101.3.4.2.1");
    private static readonly Oid DefaultPolicy = new("1.3.6.1.4.1.5237.1.1");

    private readonly RSA _rsa;
    private readonly X509Certificate2 _certificate;

    public TestTimestampAuthority(DateTimeOffset? timestamp = null)
    {
        Timestamp = timestamp ?? new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero);
        _rsa = RSA.Create(2048);
        _certificate = CreateCertificate(_rsa, Timestamp);
    }

    /// <summary>Instant attesté par les jetons émis (UTC fixe pour des tests déterministes).</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Certificat de la TSA (DER base64) — pour épingler la TSA dans les tests de confiance.</summary>
    public string CertificateBase64 => Convert.ToBase64String(_certificate.RawData);

    /// <summary>Émet une réponse RFC 3161 (TimeStampResp DER) signée pour la requête fournie.</summary>
    public byte[] IssueResponse(byte[] requestDer)
    {
        if (!Rfc3161TimestampRequest.TryDecode(requestDer, out Rfc3161TimestampRequest? request, out _) || request is null)
        {
            throw new InvalidOperationException("Requête d'horodatage de test illisible.");
        }

        byte[] messageHash = request.GetMessageHash().ToArray();

        var tstInfo = new Rfc3161TimestampTokenInfo(
            request.RequestedPolicyId ?? DefaultPolicy,
            request.HashAlgorithmId,
            messageHash,
            BuildSerial(messageHash),
            Timestamp,
            accuracyInMicroseconds: null,
            isOrdering: false,
            nonce: request.GetNonce());

        var content = new ContentInfo(TstInfoContentType, tstInfo.Encode());
        var cms = new SignedCms(content, detached: false);
        var signer = new CmsSigner(_certificate)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
            DigestAlgorithm = Sha256Oid,
        };
        signer.SignedAttributes.Add(new AsnEncodedData(SigningCertificateV2Oid, BuildSigningCertificateV2()));
        cms.ComputeSignature(signer);

        return BuildResponse(cms.Encode());
    }

    public void Dispose()
    {
        _certificate.Dispose();
        _rsa.Dispose();
    }

    private static byte[] BuildResponse(byte[] tokenDer)
    {
        // TimeStampResp ::= SEQUENCE { status PKIStatusInfo, timeStampToken ContentInfo }
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteInteger(0); // PKIStatus granted
            }

            writer.WriteEncodedValue(tokenDer);
        }

        return writer.Encode();
    }

    private static byte[] BuildSerial(byte[] messageHash)
    {
        var serial = new byte[8];
        Array.Copy(messageHash, serial, 8);
        serial[0] &= 0x7F; // INTEGER positif
        if (serial[0] == 0)
        {
            serial[0] = 0x01;
        }

        return serial;
    }

    private static X509Certificate2 CreateCertificate(RSA rsa, DateTimeOffset tokenTimestamp)
    {
        var request = new CertificateRequest("CN=Liakont Test TSA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { TimeStampingEku }, critical: true));

        // La fenêtre de validité doit ENGLOBER l'instant attesté par le jeton (Timestamp, possiblement FIXE
        // dans le passé) ET l'instant courant. Ancrer NotBefore sur « maintenant - 1 jour » seul était un
        // bug FLAKY selon l'heure : dès que l'heure UTC du jour dépasse celle du Timestamp fixe, NotBefore
        // passait APRÈS le genTime du jeton → ProcessResponse rejetait « response not understood ». On ancre
        // NotBefore sur le PLUS ANCIEN des deux instants (jeton vs maintenant) moins une marge d'un jour.
        DateTimeOffset notBefore = (tokenTimestamp < DateTimeOffset.UtcNow ? tokenTimestamp : DateTimeOffset.UtcNow).AddDays(-1);
        return request.CreateSelfSigned(notBefore, DateTimeOffset.UtcNow.AddYears(2));
    }

    private byte[] BuildSigningCertificateV2()
    {
        // SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2 } ; ESSCertIDv2 hashAlgorithm DEFAULT sha256.
        byte[] certHash = SHA256.HashData(_certificate.RawData);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            writer.WriteOctetString(certHash);
        }

        return writer.Encode();
    }
}
