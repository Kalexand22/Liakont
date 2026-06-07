namespace Liakont.Modules.TvaMapping.Contracts.Services;

using System.Collections.Generic;

/// <summary>
/// Résultat agrégé du mapping des lignes d'un document : présence et état de VALIDATION de la table du
/// tenant (remonté tel quel — garde-fou production PIP01b) + le résultat par requête de ligne. Si la table
/// n'existe pas, <see cref="TableExists"/> est <c>false</c> et chaque ligne est bloquée avec un motif.
/// </summary>
public sealed record DocumentTvaMappingResult
{
    /// <summary><c>true</c> si une table de mapping existe pour le tenant.</summary>
    public required bool TableExists { get; init; }

    /// <summary>État de validation humaine de la table (expert-comptable) ; <c>false</c> si table absente.</summary>
    public required bool IsValidated { get; init; }

    /// <summary>Version de la table appliquée ; <c>null</c> si table absente.</summary>
    public string? MappingVersion { get; init; }

    /// <summary>Résultat par requête de ligne (même ordre que les requêtes).</summary>
    public required IReadOnlyList<TvaLineMappingResult> Lines { get; init; }
}
