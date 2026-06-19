// Liakont addition (FIX211/FIX210 §4.20/§4.21 catalogue et executions de jobs) - not part of the original Stratum vendoring.
namespace Stratum.Modules.Job.Contracts.Services;

/// <summary>
/// Description d'un type de job pour l'UI : clé technique (stockée), libellé français (affiché) et les
/// paramètres typés de son payload (vide pour les déclencheurs sans paramètre — cas majoritaire).
/// Liakont addition (FIX211).
/// </summary>
public sealed record JobTypeDescriptor(
    string TechnicalKey,
    string DisplayName,
    IReadOnlyList<JobParameterDescriptor> Parameters);
