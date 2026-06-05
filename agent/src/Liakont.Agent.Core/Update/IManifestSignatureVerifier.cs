namespace Liakont.Agent.Core.Update;

/// <summary>
/// Vérifie la signature d'un manifeste de version (décision D6, ADR-0013) contre la clé publique de
/// release provisionnée à l'installation. Couture testable : un test injecte une paire de clés connue.
/// </summary>
public interface IManifestSignatureVerifier
{
    /// <summary>
    /// Vrai si une clé publique exploitable est provisionnée. Faux = aucune confiance possible →
    /// l'agent REFUSE toute mise à jour (fail-closed), il ne « fait pas confiance par défaut ».
    /// </summary>
    bool HasKey { get; }

    /// <summary>
    /// Vérifie que <paramref name="signatureBase64"/> est une signature RSA/SHA-256 valide des octets
    /// <paramref name="content"/> (les octets bruts du manifeste). Ne lève jamais : signature absente,
    /// mal formée ou non valide → <c>false</c>.
    /// </summary>
    /// <param name="content">Octets bruts signés (manifeste tel que téléchargé).</param>
    /// <param name="signatureBase64">Signature encodée en base64 (champ <c>versionManifestSignature</c>).</param>
    /// <returns><c>true</c> si la signature est authentique.</returns>
    bool Verify(byte[] content, string? signatureBase64);
}
