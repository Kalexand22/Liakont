namespace Liakont.Host.Tests.Unit.InstanceEmail;

using System;
using Liakont.Modules.TenantSettings.Application;

/// <summary>
/// Faux <see cref="ISecretProtector"/> déterministe qui ENCODE le purpose dans le ciphertext : un
/// <c>Unprotect</c> avec un purpose différent LÈVE — ce qui vérifie NATURELLEMENT la symétrie de purpose
/// entre le chiffrement (save) et le déchiffrement (send). Aucun vrai chiffrement (test unitaire).
/// </summary>
internal sealed class FakeSecretProtector : ISecretProtector
{
    private const string DefaultPurpose = "default";

    public string Protect(string plaintext) => Protect(plaintext, DefaultPurpose);

    public string Unprotect(string protectedValue) => Unprotect(protectedValue, DefaultPurpose);

    public string Protect(string plaintext, string purpose) => $"ENC({purpose}):{plaintext}";

    public string Unprotect(string protectedValue, string purpose)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        var prefix = $"ENC({purpose}):";
        if (!protectedValue.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Purpose mismatch au déchiffrement : ciphertext « {protectedValue} » attendu sous « {purpose} ».");
        }

        return protectedValue[prefix.Length..];
    }
}
