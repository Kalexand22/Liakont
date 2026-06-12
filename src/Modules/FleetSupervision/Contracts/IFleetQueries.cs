namespace Liakont.Modules.FleetSupervision.Contracts;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Contracts.DTOs;

/// <summary>
/// Lecture de la flotte pour le dashboard d'IT Innovations (OPS04). Consommée par la page « Flotte » du Host
/// (côté instance mutualisée / central). Lecture seule — aucune écriture, aucun secret exposé.
/// </summary>
public interface IFleetQueries
{
    /// <summary>
    /// Vue d'ensemble de la flotte : instances connues + alertes calculées + dernière version publiée.
    /// </summary>
    Task<FleetOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
}
