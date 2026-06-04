namespace Liakont.Modules.Archive.Infrastructure;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Couture d'appel à l'autorité d'horodatage RFC 3161 (TRK06) : envoie une requête d'horodatage
/// (TimeStampReq, DER) et renvoie la réponse brute (TimeStampResp, DER). Isole l'appel réseau pour rendre
/// <c>Rfc3161TimestampAnchor</c> testable sans TSA réelle (TSA mockée).
/// </summary>
public interface ITsaClient
{
    /// <summary>Poste la requête d'horodatage DER et renvoie la réponse DER de la TSA.</summary>
    Task<byte[]> RequestTokenAsync(byte[] requestDer, CancellationToken cancellationToken = default);
}
