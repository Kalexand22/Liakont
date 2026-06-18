namespace Liakont.Host.PaAccounts;

using System;

/// <summary>
/// Modèle de saisie d'un compte Plateforme Agréée dans la console (FIX01c) — création ET édition.
/// Muté directement par l'éditeur (instance partagée avec la page hôte), lu par la façade pour émettre
/// la commande TenantSettings (<c>AddPaAccountCommand</c> / <c>UpdatePaAccountCommand</c>).
/// </summary>
/// <remarks>
/// La clé API est SAISIE par l'opérateur (jamais générée ni réaffichée) : en création, vide = aucune clé ;
/// en édition, vide = clé inchangée, une valeur non vide = rotation. Le clair n'est jamais journalisé ni
/// persisté (le handler la chiffre immédiatement — CLAUDE.md n°10).
/// </remarks>
public sealed class PaAccountFormModel
{
    /// <summary>Identifiant du compte en édition ; <c>null</c> en création.</summary>
    public Guid? PaAccountId { get; set; }

    /// <summary>Type de plug-in PA (clé du registre). Figé en édition (la commande de mise à jour ne le change pas).</summary>
    public string PluginType { get; set; } = string.Empty;

    /// <summary>Environnement du compte ∈ { « Staging », « Production » }.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Identifiants de compte opaques (JSON libre du plug-in), facultatifs.</summary>
    public string? AccountIdentifiers { get; set; }

    /// <summary>Clé API EN CLAIR saisie par l'opérateur ; vide = aucune (création) ou inchangée (édition).</summary>
    public string? ApiKey { get; set; }

    /// <summary>« client_id » OAuth2 EN CLAIR (plug-ins en OAuth2ClientCredentials) ; vide = aucun / inchangé.</summary>
    public string? ClientId { get; set; }

    /// <summary>« client_secret » OAuth2 EN CLAIR (plug-ins en OAuth2ClientCredentials) ; vide = aucun / inchangé.</summary>
    public string? ClientSecret { get; set; }
}
