namespace Liakont.Agent.Installer.Profiles;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Registre DÉCLARATIF des champs profilables de l'installeur et de leurs métadonnées de schéma
/// (F13 §5.4). La liste <see cref="All"/> N'EST PAS figée : ajouter une option profilable = ajouter
/// une clé ici (et, pour un champ requis/secret, l'inscrire dans <see cref="Required"/> /
/// <see cref="Secret"/>) — SANS toucher au moteur, qui itère sur ces données (F13 §5.4, même principe
/// que les capacités PA, blueprint.md §2). Aucun <c>if(integrateur)</c> : la variabilité est portée
/// par le profil, pas par une branche codée en dur (F13 §5.1).
/// </summary>
internal static class ProfileFieldKeys
{
    /// <summary>Adaptateur source (plug-in embarqué) — masquable si l'intégrateur n'en a qu'un.</summary>
    public const string Adapter = "adapter";

    /// <summary>URL de la plateforme centralisée — requise (F13 §5.3).</summary>
    public const string PlatformUrl = "platformUrl";

    /// <summary>Clé API de l'agent — requise ET secrète : jamais imposée par le profil (F13 §5.3/§6).</summary>
    public const string ApiKey = "apiKey";

    /// <summary>Chaîne/DSN de connexion ODBC à la base source.</summary>
    public const string OdbcConnection = "odbcConnection";

    /// <summary>Paramètres ODBC avancés (repliables).</summary>
    public const string OdbcAdvanced = "odbcAdvanced";

    /// <summary>Planification d'extraction.</summary>
    public const string Schedule = "schedule";

    /// <summary>
    /// Date de début d'extraction — factures à prendre en compte À PARTIR de cette date (borne « extraire
    /// depuis », ADR-0031). VIDE = aucun rattrapage d'historique : uniquement les NOUVEAUX documents (fenêtre
    /// depuis maintenant). Format date/heure (ex. <c>2026-01-01</c>) validé par le chargeur du cœur agent.
    /// </summary>
    public const string ExtractFromUtc = "extractFromUtc";

    /// <summary>Dossier du pool de PDF.</summary>
    public const string PdfPoolPath = "pdfPoolPath";

    /// <summary>Niveau / rétention des journaux.</summary>
    public const string Logging = "logging";

    /// <summary>Activation de l'auto-update de l'agent.</summary>
    public const string AutoUpdate = "autoUpdate";

    /// <summary>
    /// Nom de l'INSTANCE de l'agent (multi-instances, OPS05 pt 5) : un champ profilable comme les
    /// autres (3 états + valeur par défaut « Default »). Sa valeur, quand elle est déclarée, doit être
    /// un nom de service Windows valide — vérifié via <see cref="Liakont.Agent.Core.AgentInstance.TryParse"/>
    /// (réutilisé, jamais redupliqué).
    /// </summary>
    public const string InstanceName = "instanceName";

    /// <summary>
    /// Liste (NON figée) des clés profilables connues. Le moteur itère dessus ; la validation rejette
    /// toute clé absente de cette liste comme « champ inconnu » (F13 §5.3).
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Adapter,
        PlatformUrl,
        ApiKey,
        OdbcConnection,
        OdbcAdvanced,
        Schedule,
        ExtractFromUtc,
        PdfPoolPath,
        Logging,
        AutoUpdate,
        InstanceName,
    };

    /// <summary>
    /// Champs VITAUX toujours requis : l'agent serait non fonctionnel sans eux (F13 §5.3). Ils ne
    /// peuvent pas être « non résolus » (ni affichés pour saisie, ni dotés d'une valeur).
    /// </summary>
    public static readonly IReadOnlyCollection<string> Required = new HashSet<string>(StringComparer.Ordinal)
    {
        PlatformUrl,
        ApiKey,
    };

    /// <summary>
    /// Champs SECRETS : jamais imposés par le profil (saisis au wizard puis chiffrés DPAPI — F13 §6).
    /// Un secret doté d'une valeur dans le profil, ou non « affiché », est une erreur de schéma.
    /// </summary>
    public static readonly IReadOnlyCollection<string> Secret = new HashSet<string>(StringComparer.Ordinal)
    {
        ApiKey,
    };

    /// <summary>Vrai si <paramref name="key"/> est une clé profilable connue.</summary>
    public static bool IsKnown(string key) => All.Contains(key);

    /// <summary>Vrai si <paramref name="key"/> désigne un champ vital requis.</summary>
    public static bool IsRequired(string key) => Required.Contains(key);

    /// <summary>Vrai si <paramref name="key"/> désigne un secret jamais imposable par le profil.</summary>
    public static bool IsSecret(string key) => Secret.Contains(key);
}
