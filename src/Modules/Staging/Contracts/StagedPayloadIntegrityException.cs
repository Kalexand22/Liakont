namespace Liakont.Modules.Staging.Contracts;

using System;

/// <summary>
/// Levée par <see cref="IPayloadStagingStore.ReadAsync"/> quand le contenu relu ne peut pas être restitué
/// fidèlement. Couvre deux cas :
/// <list type="bullet">
/// <item><description><see cref="HashMismatch"/> : le contenu a été déchiffré mais l'empreinte recalculée ≠ empreinte attendue — altération TERMINALE du blob (ADR-0014 §3).</description></item>
/// <item><description><see cref="Undecryptable"/> : le déchiffrement/authentification AEAD a échoué — cause indistinguable entre altération du blob chiffré ET clé Data Protection indisponible (rotation ou éphémère — cf. MODULE.md §Dependencies, prérequis OPS clés DP persistantes).</description></item>
/// </list>
/// Dans les deux cas le contenu est REJETÉ, jamais servi silencieusement. La distinction de routage
/// (re-demander à l'agent vs erreur terminale) pour le cas <see cref="Undecryptable"/> relève de PIP01.
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

    /// <summary>Construit l'exception pour un blob chiffré illisible (déchiffrement/authentification AEAD échoué).</summary>
    /// <param name="key">La clé lue.</param>
    /// <param name="innerException">L'erreur cryptographique sous-jacente.</param>
    /// <returns>
    /// L'exception décrivant le blob illisible. La cause est indistinguable au niveau de la couche de stockage :
    /// il peut s'agir d'une altération du blob chiffré OU d'une clé Data Protection indisponible (cf. MODULE.md
    /// §Dependencies, prérequis OPS clés DP persistantes). La distinction de routage relève de PIP01.
    /// </returns>
    public static StagedPayloadIntegrityException Undecryptable(StagedPayloadKey key, Exception innerException)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new StagedPayloadIntegrityException(
            $"Contenu stagé illisible pour le document {key.DocumentId} (tenant {key.TenantId}) : déchiffrement échoué — blob altéré OU clé de chiffrement indisponible (cf. prérequis OPS clés DP persistantes, MODULE.md).",
            innerException);
    }
}
