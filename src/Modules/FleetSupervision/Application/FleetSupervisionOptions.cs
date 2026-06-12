namespace Liakont.Modules.FleetSupervision.Application;

using Liakont.Modules.FleetSupervision.Contracts;

/// <summary>
/// Paramétrage de niveau INSTANCE de la méta-supervision de flotte (OPS04, F12 §6), lié depuis la section
/// <c>FleetSupervision</c> des appsettings. Une instance peut tenir le rôle <see cref="Central"/> (instance
/// mutualisée d'IT Innovations : reçoit les heartbeats, expose le dashboard, notifie les mises à jour), le
/// rôle <see cref="Reporting"/> (envoie sa propre télémétrie au central), ou les deux. Tout est désactivé
/// par défaut (aucune télémétrie ne part, aucun endpoint n'accepte de heartbeat) — opt-in par déploiement.
/// </summary>
public sealed class FleetSupervisionOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "FleetSupervision";

    /// <summary>Rôle CENTRAL (instance mutualisée d'IT Innovations).</summary>
    public FleetCentralOptions Central { get; init; } = new();

    /// <summary>Rôle REPORTING (cette instance envoie sa télémétrie au central).</summary>
    public FleetReportingOptions Reporting { get; init; } = new();
}
