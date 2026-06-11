namespace Stratum.Modules.Job.Contracts.Services;

/// <summary>
/// Un paramètre du payload d'un type de job, dérivé par réflexion des propriétés / paramètres de constructeur
/// du type de payload. Permet à l'UI de générer un champ typé au lieu d'un JSON brut. Liakont addition (FIX211).
/// </summary>
public sealed record JobParameterDescriptor(
    string Name,
    string Label,
    JobParameterKind Kind,
    bool Required,
    string? DefaultValue,
    IReadOnlyList<string> EnumOptions);
