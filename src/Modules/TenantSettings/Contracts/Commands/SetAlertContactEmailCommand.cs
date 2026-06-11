namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Met à jour le SEUL e-mail de contact d'alerte du tenant courant (F12-A §2 / F12 §5.3) — destinataire des
/// alertes critiques quand l'option d'envoi au contact du tenant est activée (seuils, CFG02). N'altère pas
/// le reste du profil (SIREN, raison sociale, adresse). Vide ⇒ aucun contact.
/// </summary>
public record SetAlertContactEmailCommand : ICommand
{
    /// <summary>E-mail de contact d'alerte, ou <c>null</c>/vide pour le retirer.</summary>
    public string? ContactEmailAlerte { get; init; }
}
