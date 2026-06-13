namespace Liakont.Host.Clients;

/// <summary>Résultat d'action générique (statut + message opérateur français quand pertinent).</summary>
internal sealed record ClientActionResult(ClientActionStatus Status, string? Message = null);
