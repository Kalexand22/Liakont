namespace Liakont.Modules.DocumentApproval.Domain;

using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Échelle d'<b>assurance</b> eIDAS (<c>Recorded</c> &lt; <c>SES</c> &lt; <c>AES</c> &lt; <c>QES</c>) appliquée à
/// une PREUVE UNIQUE attachée à un document. À NE PAS confondre avec l'<b>ENSEMBLE</b> de capacités
/// <c>[Flags]</c> d'ADR-0027 (où un fournisseur DÉCLARE un ensemble de niveaux et où l'on teste l'appartenance
/// avec <c>HasFlag</c>, jamais un max ordonné). Ici, la Règle de gate (ADR-0028 §5 condition 2) compare le
/// niveau de la preuve attachée au niveau requis par le tenant : « <c>SignatureProof.Level</c> ≥ niveau requis ».
/// Un <c>Recorded</c> nu ne franchit donc PAS une exigence <c>SES</c>/<c>AES</c>/<c>QES</c>.
/// </summary>
internal static class SignatureLevelAssurance
{
    /// <summary>
    /// Rang d'assurance d'un niveau UNIQUE. Lève si <paramref name="level"/> n'est pas un niveau atomique
    /// (une preuve porte un niveau unique, jamais un ensemble combiné — garde anti-faux-vert).
    /// </summary>
    public static int Rank(SignatureLevel level) => level switch
    {
        SignatureLevel.None => 0,
        SignatureLevel.Recorded => 1,
        SignatureLevel.SES => 2,
        SignatureLevel.AES => 3,
        SignatureLevel.QES => 4,
        _ => throw new ArgumentOutOfRangeException(
            nameof(level),
            level,
            "Une preuve porte un niveau d'assurance UNIQUE (None/Recorded/SES/AES/QES), jamais un ensemble combiné."),
    };

    /// <summary>Une preuve de niveau <paramref name="proof"/> satisfait l'exigence <paramref name="required"/> ssi son assurance est ≥.</summary>
    public static bool Satisfies(SignatureLevel proof, SignatureLevel required)
        => Rank(proof) >= Rank(required);

    /// <summary>Garde : <paramref name="level"/> doit être un niveau atomique (pas un ensemble combiné). Lève sinon.</summary>
    public static void EnsureSingleLevel(SignatureLevel level, string paramName)
    {
        if (level is SignatureLevel.None or SignatureLevel.Recorded or SignatureLevel.SES
            or SignatureLevel.AES or SignatureLevel.QES)
        {
            return;
        }

        throw new ArgumentException(
            $"Une preuve porte un niveau UNIQUE (Recorded/SES/AES/QES), pas un ensemble combiné : « {level} ».",
            paramName);
    }
}
