namespace Liakont.Tests.E2E.Support;

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Générateur TOTP (RFC 6238) pour les tests E2E. Calcule le code à 6 chiffres attendu par Keycloak
/// à partir du secret OTP pré-enrôlé d'un utilisateur de test (keycloak-e2e-realm.json, credential
/// <c>type: "otp"</c>, champ <c>secretData.value</c>), de façon à automatiser un login 2FA (RLM01).
/// <para>
/// Keycloak stocke le secret OTP <b>brut</b> : la clé HMAC est l'octet-à-octet ASCII du secret, PAS
/// un décodage base32 (le base32 ne sert qu'à l'affichage QR / saisie manuelle à l'enrôlement). Les
/// paramètres (algorithme HmacSHA1, 6 chiffres, période 30 s) sont alignés sur l'<c>otpPolicy</c> du
/// realm fixture. L'algorithme HmacSHA1 est imposé par RFC 6238 et par l'otpPolicy de Keycloak : il
/// est utilisé ici pour VÉRIFIER un second facteur de test, jamais pour de la confidentialité
/// (cf. <c>NoWarn CA5350</c> du projet de test, justifié).
/// </para>
/// </summary>
internal static class TotpGenerator
{
    /// <summary>
    /// Code TOTP courant pour un secret OTP Keycloak (octets ASCII bruts), à l'instant <paramref name="utcNow"/>.
    /// </summary>
    public static string Generate(string secret, DateTimeOffset utcNow, int digits = 6, int periodSeconds = 30)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(periodSeconds);

        var counter = utcNow.ToUnixTimeSeconds() / periodSeconds;
        return GenerateForCounter(Encoding.ASCII.GetBytes(secret), counter, digits);
    }

    /// <summary>
    /// HOTP (RFC 4226) — la brique de TOTP. Exposé pour la testabilité par les vecteurs de la RFC 6238
    /// (Appendice B), qui valident l'algorithme indépendamment de l'horloge.
    /// </summary>
    public static string GenerateForCounter(byte[] key, long counter, int digits = 6)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (digits is < 1 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(digits));
        }

        var counterBytes = new byte[8];
        var moving = counter;
        for (var i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(moving & 0xFF);
            moving >>= 8;
        }

        // HmacSHA1 : imposé par RFC 6238 (TOTP) et par l'otpPolicyAlgorithm du realm Keycloak.
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);

        // Troncature dynamique (RFC 4226 §5.3).
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);

        var modulo = (int)Math.Pow(10, digits);
        return (binary % modulo).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }
}
