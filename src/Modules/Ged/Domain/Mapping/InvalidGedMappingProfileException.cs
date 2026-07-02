namespace Liakont.Modules.Ged.Domain.Mapping;

using System;

/// <summary>
/// La structure d'un <see cref="GedMappingProfile"/> est invalide (F19 §4.5) : type de document ou version
/// vide, code d'axe dupliqué, sélecteur mal formé, règle incomplète. Levée à <see cref="GedMappingProfile.Create"/>
/// et <see cref="GedMappingProfile.Reconstitute"/> (miroir de <c>InvalidMappingTableException</c> du domaine TVA) :
/// un profil corrompu n'est jamais chargé ni appliqué en silence.
/// </summary>
public sealed class InvalidGedMappingProfileException : Exception
{
    /// <summary>Initialise l'exception avec la raison de l'invalidité.</summary>
    /// <param name="reason">La raison du rejet (français).</param>
    public InvalidGedMappingProfileException(string reason)
        : base($"Profil de mapping GED invalide : {reason}")
    {
        Reason = reason;
    }

    /// <summary>Initialise l'exception à partir d'un sélecteur invalide.</summary>
    /// <param name="reason">La raison du rejet (français).</param>
    /// <param name="innerException">L'exception de sélecteur d'origine.</param>
    public InvalidGedMappingProfileException(string reason, Exception innerException)
        : base($"Profil de mapping GED invalide : {reason}", innerException)
    {
        Reason = reason;
    }

    /// <summary>La raison de l'invalidité.</summary>
    public string Reason { get; }
}
