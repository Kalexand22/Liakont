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
