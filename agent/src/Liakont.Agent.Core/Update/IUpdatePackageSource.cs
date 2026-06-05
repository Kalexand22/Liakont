namespace Liakont.Agent.Core.Update;

/// <summary>
/// Récupère le manifeste et le paquet de mise à jour (HTTPS sortant uniquement — F12 §2.6). Surface
/// MINCE et sans politique : elle télécharge, point. Un échec ne lève jamais (renvoie <c>null</c> /
/// <c>false</c>) — le coordinateur décide. Couture testable : un test fournit des octets en mémoire.
/// </summary>
public interface IUpdatePackageSource
{
    /// <summary>
    /// Télécharge les octets bruts du manifeste depuis <paramref name="manifestUrl"/> (ce sont ces
    /// octets-là dont la signature est vérifiée). <c>null</c> en cas d'échec.
    /// </summary>
    /// <param name="manifestUrl">URL HTTPS du manifeste (champ <c>updateUrl</c> de la config).</param>
    /// <returns>Les octets du manifeste, ou <c>null</c>.</returns>
    byte[]? TryDownloadManifest(string manifestUrl);

    /// <summary>
    /// Télécharge le paquet depuis <paramref name="packageUrl"/> vers <paramref name="destinationPath"/>.
    /// <c>false</c> en cas d'échec.
    /// </summary>
    /// <param name="packageUrl">URL HTTPS du paquet (référencée par le manifeste signé).</param>
    /// <param name="destinationPath">Chemin de destination local.</param>
    /// <returns><c>true</c> si le paquet a été intégralement téléchargé.</returns>
    bool TryDownloadPackage(string packageUrl, string destinationPath);
}
