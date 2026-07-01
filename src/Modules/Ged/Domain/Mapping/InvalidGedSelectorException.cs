namespace Liakont.Modules.Ged.Domain.Mapping;

using System;

/// <summary>
/// Le sélecteur JSONPath restreint d'une règle de profil est syntaxiquement invalide (F19 §4.5). Levée à la
/// CONSTRUCTION/validation d'un <see cref="GedMappingProfile"/> — un profil ne peut être validé s'il porte un
/// sélecteur mal formé (jamais deviner une intention, règle 3). N'est PAS levée au mapping : à ce stade les
/// sélecteurs sont déjà validés.
/// </summary>
public sealed class InvalidGedSelectorException : Exception
{
    /// <summary>Initialise l'exception avec le sélecteur fautif et la raison syntaxique.</summary>
    /// <param name="selector">Le sélecteur brut invalide.</param>
    /// <param name="reason">La raison du rejet (français, opposable en console).</param>
    public InvalidGedSelectorException(string selector, string reason)
        : base($"Sélecteur GED invalide « {selector} » : {reason}")
    {
        Selector = selector;
        Reason = reason;
    }

    /// <summary>Le sélecteur brut invalide.</summary>
    public string Selector { get; }

    /// <summary>La raison syntaxique du rejet.</summary>
    public string Reason { get; }
}
