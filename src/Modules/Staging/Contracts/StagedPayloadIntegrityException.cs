namespace Liakont.Modules.Staging.Contracts;

using System;

/// <summary>
/// Levée par <see cref="IPayloadStagingStore.ReadAsync"/> quand le contenu relu ne correspond pas à
/// l'empreinte attendue (ADR-0014 §3 : « re-vérifie le payload_hash à la lecture — toute altération est
/// détectée par l'empreinte existante »). Couvre aussi un déchiffrement échoué (altération du blob chiffré
/// détectée par l'authentification AEAD du chiffrement au repos). Un contenu altéré est REJETÉ, jamais
/// servi silencieusement.
/// </summary>
public sealed class StagedPayloadIntegrityException : Exception
{
    private StagedPayloadIntegrityException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Construit l'exception pour une empreinte recalculée différente de l'empreinte attendue.</summary>
    /// <param name="key">La clé lue.</param>
    /// <param name="actualHash">L'empreinte recalculée sur le contenu relu.</param>
    /// <returns>L'exception décrivant la rupture d'intégrité.</returns>
    public static StagedPayloadIntegrityException HashMismatch(StagedPayloadKey key, string actualHash)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new StagedPayloadIntegrityException(
            $"Contenu stagé altéré pour le document {key.DocumentId} (tenant {key.TenantId}) : empreinte recalculée « {actualHash} » ≠ empreinte attendue « {key.PayloadHash} ».",
            innerException: null);
    }

    /// <summary>Construit l'exception pour un blob chiffré illisible (déchiffrement/authentification échoué).</summary>
    /// <param name="key">La clé lue.</param>
    /// <param name="innerException">L'erreur cryptographique sous-jacente.</param>
    /// <returns>L'exception décrivant le blob illisible.</returns>
    public static StagedPayloadIntegrityException Undecryptable(StagedPayloadKey key, Exception innerException)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new StagedPayloadIntegrityException(
            $"Contenu stagé illisible pour le document {key.DocumentId} (tenant {key.TenantId}) : déchiffrement échoué (blob altéré).",
            innerException);
    }
}
