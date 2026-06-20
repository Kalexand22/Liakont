namespace Liakont.Agent.Core.Update;

using System;
using System.IO;
using System.Security.Cryptography;

/// <summary>
/// Vérificateur RSA/SHA-256 (PKCS#1 v1.5) du manifeste de version. La clé publique est fournie au
/// format XML (<see cref="RSA.ToXmlString(bool)"/>) ; elle est PROVISIONNÉE à l'installation, jamais
/// embarquée en dur dans le code (CLAUDE.md n°7, ADR-0013).
/// <para>
/// PARTICULARITÉ net48 : <see cref="RSACryptoServiceProvider"/> par défaut (PROV_RSA_FULL) ne sait pas
/// vérifier une signature SHA-256 (« algorithme non valide »). On force le fournisseur
/// <c>PROV_RSA_AES</c> (type 24), qui supporte SHA-256 — le poste de release SIGNE avec le même type.
/// </para>
/// <para>
/// TAILLE DE CLÉ MINIMALE (RDF14, RL-UPD-1) : une clé dont le module est inférieur à
/// <see cref="MinimumKeyBits"/> bits (2048 ; 3072 recommandé) est REFUSÉE — chargée, elle laisse
/// <see cref="HasKey"/> à <c>false</c> et <see cref="Verify"/> à <c>false</c> (fail-closed), exactement
/// comme une clé absente. Une clé 1024 bits ne donne donc aucune confiance d'auto-update.
/// </para>
/// </summary>
public sealed class RsaManifestSignatureVerifier : IManifestSignatureVerifier
{
    /// <summary>
    /// Taille minimale (en bits) du module RSA acceptée pour la clé de release d'auto-update. En deçà,
    /// la clé est traitée comme absente (fail-closed). 2048 = plancher, 3072 recommandé (ADR-0013).
    /// </summary>
    public const int MinimumKeyBits = 2048;

    // PROV_RSA_AES : fournisseur cryptographique Windows qui supporte SHA-256 (à la différence du
    // PROV_RSA_FULL par défaut). Sign et Verify DOIVENT utiliser le même type.
    private const int ProvRsaAes = 24;

    private readonly string? _publicKeyXml;
    private readonly bool _hasKey;

    /// <summary>Crée un vérificateur à partir d'une clé publique XML (ou sans clé = fail-closed).</summary>
    /// <param name="publicKeyXml">Clé publique RSA au format XML, ou <c>null</c>/vide si non provisionnée.</param>
    public RsaManifestSignatureVerifier(string? publicKeyXml)
    {
        _publicKeyXml = publicKeyXml;
        _hasKey = !string.IsNullOrWhiteSpace(publicKeyXml) && CanLoad(publicKeyXml!);
    }

    /// <inheritdoc/>
    public bool HasKey => _hasKey;

    /// <summary>
    /// Charge la clé publique de signature PROVISIONNÉE (posée par l'installeur OPS05 à
    /// <paramref name="keyFilePath"/>). Fichier absent → vérificateur SANS clé (fail-closed),
    /// jamais une levée : l'agent refusera proprement toute mise à jour.
    /// </summary>
    /// <param name="keyFilePath">Chemin du fichier de clé publique XML.</param>
    /// <returns>Un vérificateur (avec ou sans clé selon la présence du fichier).</returns>
    public static RsaManifestSignatureVerifier FromProvisionedKey(string keyFilePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(keyFilePath) && File.Exists(keyFilePath))
            {
                string xml = File.ReadAllText(keyFilePath);
                return new RsaManifestSignatureVerifier(xml);
            }
        }
        catch (IOException)
        {
            // Lecture best-effort : un fichier illisible est traité comme « clé absente ».
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new RsaManifestSignatureVerifier(null);
    }

    /// <inheritdoc/>
    public bool Verify(byte[] content, string? signatureBase64)
    {
        if (!_hasKey || content == null || content.Length == 0 || string.IsNullOrWhiteSpace(signatureBase64))
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            using (RSACryptoServiceProvider rsa = CreateSha256CapableRsa())
            {
                rsa.FromXmlString(_publicKeyXml!);
                return rsa.VerifyData(content, "SHA256", signature);
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool CanLoad(string publicKeyXml)
    {
        try
        {
            using (RSACryptoServiceProvider rsa = CreateSha256CapableRsa())
            {
                rsa.FromXmlString(publicKeyXml);

                // Plancher de taille de clé (RDF14) : une clé < 2048 bits est traitée comme « pas de
                // clé exploitable » → fail-closed (HasKey false, Verify false), jamais une levée.
                return rsa.KeySize >= MinimumKeyBits;
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static RSACryptoServiceProvider CreateSha256CapableRsa()
    {
        var cspParameters = new CspParameters(ProvRsaAes);
        return new RSACryptoServiceProvider(cspParameters) { PersistKeyInCsp = false };
    }
}
