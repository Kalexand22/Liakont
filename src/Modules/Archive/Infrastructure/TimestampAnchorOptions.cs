namespace Liakont.Modules.Archive.Infrastructure;

/// <summary>
/// Configuration d'INSTANCE de l'ancrage temporel du coffre (TRK06, section <c>Archive:Anchor</c>). Le
/// choix de la méthode et de la TSA est du paramétrage d'instance — aucune URL ni aucun secret n'est
/// versionné dans le code (CLAUDE.md n°7/n°10). Défaut : <c>None</c> (NoAnchor) pour qu'une instance non
/// configurée ne tente aucun appel sortant ; les instances hébergées activent RFC 3161 (recommandé).
/// </summary>
public sealed class TimestampAnchorOptions
{
    /// <summary>Section de configuration.</summary>
    public const string SectionName = "Archive:Anchor";

    /// <summary>Méthode d'ancrage : <c>None</c> (défaut), <c>Rfc3161</c> (recommandé) ou <c>OpenTimestamps</c> (V1.1).</summary>
    public string Method { get; set; } = "None";

    /// <summary>Paramètres de l'ancrage RFC 3161.</summary>
    public Rfc3161Options Rfc3161 { get; set; } = new();

    /// <summary>Paramètres de l'autorité d'horodatage (TSA) RFC 3161.</summary>
    public sealed class Rfc3161Options
    {
        /// <summary>URL de la TSA qualifiée (eIDAS). Obligatoire quand <see cref="Method"/> vaut <c>Rfc3161</c>.</summary>
        public string? TsaUrl { get; set; }

        /// <summary>Délai d'attente de l'appel TSA, en secondes.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Certificat de la TSA qualifiée À ÉPINGLER (DER encodé en base64), paramétrage d'INSTANCE. Quand
        /// il est renseigné, la vérification exige que le certificat signataire du jeton ait CETTE empreinte
        /// (authentifie la TSA, ferme la forge d'un jeton auto-signé). Quand il est absent, la vérification
        /// in-produit confirme signature + empreinte mais NON l'identité de la TSA (caveat reporté à
        /// l'opérateur ; vérification autoritaire par contrôle externe). Jamais de certificat client en dur.
        /// </summary>
        public string? TrustedCertificateBase64 { get; set; }
    }
}
