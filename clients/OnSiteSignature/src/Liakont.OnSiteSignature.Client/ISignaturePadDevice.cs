namespace Liakont.OnSiteSignature.Client;

/// <summary>
/// Abstraction du PAD de signature (Wacom STU). L'implémentation concrète intègre le <b>SDK Wacom Ink
/// natif</b> et est fournie par l'hôte de DÉPLOIEMENT (jamais référencée dans le source buildé — README.md ;
/// INV-ONSITE-2/3). Découpler le pad derrière cette interface garde le projet buildable et le test de
/// pureté exécutable sans le SDK natif.
/// </summary>
internal interface ISignaturePadDevice
{
    /// <summary>
    /// Affiche le contexte au signataire et capte sa signature manuscrite (FSS + rendu PNG). L'implémentation
    /// concrète parle au pad via le SDK Wacom ; le canal pad→hôte est déjà chiffré par le SDK (RSA+AES).
    /// </summary>
    /// <returns>La capture brute (FSS + PNG).</returns>
    CapturedSignature Capture();
}
