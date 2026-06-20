namespace Liakont.Agent.Core.Tests.Update;

using System;
using System.Security.Cryptography;

/// <summary>
/// Simule le POSTE DE RELEASE (clé privée hors plateforme, ADR-0013) : génère une paire RSA et signe
/// les octets d'un manifeste en SHA-256, via le même fournisseur PROV_RSA_AES (type 24) que le
/// vérificateur de l'agent. Prouve la chaîne signature → vérification de bout en bout.
/// <para>
/// La taille de clé est paramétrable (défaut 2048) : un test peut produire une clé 1024 bits pour
/// prouver le rejet du plancher de sécurité (RDF14).
/// </para>
/// </summary>
internal sealed class TestUpdateSigner
{
    private const int ProvRsaAes = 24;

    private readonly string _publicKeyXml;
    private readonly RSAParameters _privateParameters;

    public TestUpdateSigner(int keySizeBits = 2048)
    {
        var cspParameters = new CspParameters(ProvRsaAes);
        using (var rsa = new RSACryptoServiceProvider(keySizeBits, cspParameters) { PersistKeyInCsp = false })
        {
            _publicKeyXml = rsa.ToXmlString(includePrivateParameters: false);
            _privateParameters = rsa.ExportParameters(includePrivateParameters: true);
        }
    }

    public string PublicKeyXml => _publicKeyXml;

    public string SignBase64(byte[] content)
    {
        var cspParameters = new CspParameters(ProvRsaAes);
        using (var rsa = new RSACryptoServiceProvider(cspParameters) { PersistKeyInCsp = false })
        {
            rsa.ImportParameters(_privateParameters);
            byte[] signature = rsa.SignData(content, "SHA256");
            return Convert.ToBase64String(signature);
        }
    }
}
