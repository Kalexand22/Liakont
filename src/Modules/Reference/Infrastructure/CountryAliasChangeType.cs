namespace Liakont.Modules.Reference.Infrastructure;

/// <summary>
/// Nature d'une mutation du référentiel de correspondance pays, persistée dans le journal append-only
/// (ADR-0038). Les valeurs numériques sont STABLES (stockées en base) : ne jamais les réordonner.
/// </summary>
internal enum CountryAliasChangeType
{
    /// <summary>Ajout d'une correspondance qui n'existait pas.</summary>
    Create = 0,

    /// <summary>Modification de la cible ISO d'une correspondance existante.</summary>
    Update = 1,

    /// <summary>Suppression d'une correspondance existante.</summary>
    Remove = 2,
}
