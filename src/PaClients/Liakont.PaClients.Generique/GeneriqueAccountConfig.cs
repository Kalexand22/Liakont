namespace Liakont.PaClients.Generique;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Configuration RÉSOLUE d'un compte générique de tenant (F16 §6.2) : le canal choisi et sa cible non
/// sensible (boîte PA / dossier), plus d'éventuels identifiants SMTP par tenant DÉJÀ DÉCHIFFRÉS. Produite
/// par <see cref="IGeneriqueAccountResolver"/> au Host (qui seul voit le coffre du tenant) ; le descripteur
/// de compte ne transporte JAMAIS de secret en clair (CLAUDE.md n°10). Type <c>class</c> (et non
/// <c>record</c>) car elle porte un secret en mémoire : un <c>record</c> l'imprimerait via <c>ToString()</c>
/// (risque de fuite — CLAUDE.md n°18).
/// </summary>
public sealed class GeneriqueAccountConfig
{
    /// <summary>Canal de livraison configuré pour ce compte (email / dépôt de fichier).</summary>
    public required DocumentDeliveryMethod Method { get; init; }

    /// <summary>Cible non sensible : adresse email (boîte PA du tenant) ou chemin de dossier par tenant.</summary>
    public required string Target { get; init; }

    /// <summary>
    /// Identifiants SMTP propres au tenant (email), DÉJÀ DÉCHIFFRÉS Host-side via le coffre. <c>null</c> ⇒
    /// le canal email réutilise le SMTP d'instance (ADR-0018).
    /// </summary>
    public SmtpDeliveryAuthentication? SmtpAuth { get; init; }
}
