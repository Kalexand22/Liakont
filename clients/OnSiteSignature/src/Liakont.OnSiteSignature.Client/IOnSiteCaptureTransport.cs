namespace Liakont.OnSiteSignature.Client;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Transport du payload de capture vers le proxy plateforme <c>OnSiteCapture</c> (HTTPS). Abstrait pour
/// que l'orchestration de capture (<see cref="OnSiteSignatureSession"/>) soit testable sans réseau réel.
/// </summary>
internal interface IOnSiteCaptureTransport
{
    /// <summary>POST l'objet de capture au proxy plateforme (HTTPS, authentifié). Lève en cas d'échec.</summary>
    /// <param name="payload">Objet immuable de capture.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task SendAsync(OnSiteCapturePayload payload, CancellationToken cancellationToken);
}
