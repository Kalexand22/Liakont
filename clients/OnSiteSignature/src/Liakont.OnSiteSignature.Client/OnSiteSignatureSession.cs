namespace Liakont.OnSiteSignature.Client;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Orchestration de la signature SUR PLACE (ADR-0030 §3/§4) : capte la signature via le pad, construit le
/// payload immuable (binding hash sur les OCTETS EXACTS de l'artefact scellé, FSS + PNG en Base64) et le
/// POST au proxy plateforme. AUCUNE logique métier ni décision : pur capteur (INV-ONSITE-4). AUCUN gabarit
/// biométrique n'est dérivé de la FSS — elle est transmise telle quelle comme preuve (INV-ONSITE-10).
/// </summary>
internal sealed class OnSiteSignatureSession
{
    private readonly ISignaturePadDevice _device;
    private readonly IOnSiteCaptureTransport _transport;

    /// <summary>Crée une session de capture.</summary>
    /// <param name="device">Pad de signature (abstraction du SDK Wacom).</param>
    /// <param name="transport">Transport HTTPS vers le proxy plateforme.</param>
    public OnSiteSignatureSession(ISignaturePadDevice device, IOnSiteCaptureTransport transport)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>
    /// Construit le payload de capture à partir des octets EXACTS de l'artefact scellé et de la capture
    /// brute. Le binding hash est le SHA-256 de l'artefact (jamais de la FSS) ; la FSS et le PNG sont
    /// transmis en Base64 SANS transformation (aucun gabarit dérivé — INV-ONSITE-10). Pur, sans effet de bord.
    /// </summary>
    /// <param name="documentId">Document signé.</param>
    /// <param name="sealedArtifact">Octets exacts de l'artefact Factur-X scellé reçu.</param>
    /// <param name="capture">Capture brute renvoyée par le pad.</param>
    /// <param name="declaredOperatorIdentity">Identité opérateur déclarée (indicative, non probante), ou <c>null</c>.</param>
    /// <param name="capturedAtUtc">Horodatage de capture (UTC).</param>
    /// <returns>Le payload immuable à transmettre.</returns>
    public static OnSiteCapturePayload BuildPayload(
        Guid documentId,
        byte[] sealedArtifact,
        CapturedSignature capture,
        string? declaredOperatorIdentity,
        DateTimeOffset capturedAtUtc)
    {
        if (capture is null)
        {
            throw new ArgumentNullException(nameof(capture));
        }

        return new OnSiteCapturePayload(
            documentId,
            BindingHasher.ComputeHex(sealedArtifact),
            Convert.ToBase64String(capture.SignatureFormatStorage),
            Convert.ToBase64String(capture.PngImage),
            declaredOperatorIdentity,
            capturedAtUtc);
    }

    /// <summary>
    /// Capte la signature au pad, construit le payload et le POST au proxy. Renvoie le payload transmis
    /// (pour journalisation locale éventuelle).
    /// </summary>
    /// <param name="documentId">Document signé.</param>
    /// <param name="sealedArtifact">Octets exacts de l'artefact Factur-X scellé reçu.</param>
    /// <param name="declaredOperatorIdentity">Identité opérateur déclarée (indicative), ou <c>null</c>.</param>
    /// <param name="capturedAtUtc">Horodatage de capture (UTC).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Le payload transmis.</returns>
    public async Task<OnSiteCapturePayload> CaptureAndSendAsync(
        Guid documentId,
        byte[] sealedArtifact,
        string? declaredOperatorIdentity,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        CapturedSignature capture = _device.Capture();
        OnSiteCapturePayload payload = BuildPayload(
            documentId, sealedArtifact, capture, declaredOperatorIdentity, capturedAtUtc);
        await _transport.SendAsync(payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }
}
