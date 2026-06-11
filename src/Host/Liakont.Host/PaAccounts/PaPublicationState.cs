namespace Liakont.Host.PaAccounts;

using System;

/// <summary>
/// État de la publication du SIREN / du <c>tax_report_setting</c> du compte Plateforme Agréée ACTIF du
/// tenant (FIX201). Lecture défensive : <see cref="StateAvailable"/> est <c>false</c> quand l'état n'a pas
/// pu être relu auprès de la PA (plug-in non câblé, clé absente, PA injoignable) — l'écran reste utilisable
/// au lieu d'échouer (précédent API01c : résoudre un client vivant peut lever). Ne porte aucun secret.
/// </summary>
public sealed class PaPublicationState
{
    /// <summary>Un compte PA actif existe pour ce tenant (sinon : rien à publier, configurez d'abord un compte).</summary>
    public required bool HasActiveAccount { get; init; }

    /// <summary>Type de plug-in du compte actif (ex. « Fake », « B2Brouter »), ou <c>null</c> si aucun compte actif.</summary>
    public string? PluginType { get; init; }

    /// <summary>Environnement du compte actif (« Staging »/« Production »), ou <c>null</c>.</summary>
    public string? Environment { get; init; }

    /// <summary>SIREN du tenant (profil CFG02), affiché et publié en <c>cin_scheme « 0002 »</c> ; <c>null</c> si profil incomplet.</summary>
    public string? Siren { get; init; }

    /// <summary>L'état du réglage a pu être relu auprès de la PA. <c>false</c> = état indisponible (affichage dégradé).</summary>
    public required bool StateAvailable { get; init; }

    /// <summary>Le réglage est publié (une date de début est déclarée côté PA). Pertinent seulement si <see cref="StateAvailable"/>.</summary>
    public bool IsPublished { get; init; }

    /// <summary>Date de début de publication déclarée côté PA, ou <c>null</c> si non publié.</summary>
    public DateOnly? StartDate { get; init; }
}
