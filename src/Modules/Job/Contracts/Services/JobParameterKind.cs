namespace Stratum.Modules.Job.Contracts.Services;

/// <summary>Nature d'un paramètre de payload, pour choisir le contrôle de saisie adapté. Liakont addition (FIX211).</summary>
public enum JobParameterKind
{
    /// <summary>Texte libre (chaîne, identifiant).</summary>
    Text,

    /// <summary>Booléen (case à cocher).</summary>
    Boolean,

    /// <summary>Valeur numérique (entière ou décimale) — champ numérique.</summary>
    Number,

    /// <summary>Valeur d'énumération — liste de choix.</summary>
    Enum,
}
