namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Issue d'une tentative d'émission d'un agrégat e-reporting B2C de la marge (flux 10.3) vers la Plateforme
/// Agréée. L'API SuperPDP n'expose AUCUNE clé d'idempotence (pas d'<c>external_id</c> ; 2 POST = 2 lignes) :
/// l'anti-doublon est porté UNIQUEMENT par le journal d'émission de la plateforme (append-only, par document),
/// d'où le statut <see cref="Pending"/> écrit AVANT le POST (crash-safe : un document déjà tenté n'est jamais
/// re-tenté en auto, même après un crash). Aucune issue non <see cref="Issued"/> n'est ré-émise automatiquement
/// (signalée à l'opérateur) — bloquer plutôt que doublonner (CLAUDE.md n°3).
/// </summary>
public enum B2cMarginEmissionStatus
{
    /// <summary>Tentative ENGAGÉE (enregistrée AVANT le POST) — issue encore inconnue : ne jamais re-POSTer (doublon possible).</summary>
    Pending = 0,

    /// <summary>Agrégat CRÉÉ côté PA (HTTP 200) — succès terminal, jamais re-POSTé (l'API n'a aucun dédoublonnage).</summary>
    Issued = 1,

    /// <summary>Rejet métier de la PA (4xx) — rien créé ; non ré-émis en auto (correction de données = geste opérateur).</summary>
    RejectedByPa = 2,

    /// <summary>Échec technique/transitoire (timeout, 5xx, réseau, auth) — issue INCERTAINE : non ré-émis en auto, signalé opérateur.</summary>
    Technical = 3,
}
