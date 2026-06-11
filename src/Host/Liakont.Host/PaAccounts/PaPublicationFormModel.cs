namespace Liakont.Host.PaAccounts;

using System;

/// <summary>
/// Saisie de l'action « Publier le SIREN / activer la transmission » (FIX201, F05 §2 B.3). Les valeurs
/// sont fournies par l'OPÉRATEUR (qui connaît le réglage attendu par sa Plateforme Agréée) ou par le seed
/// de dev — JAMAIS inventées par le code (CLAUDE.md n°2/7). Le SIREN n'est pas saisi ici : il est lu du
/// profil tenant (clé fonctionnelle immuable) et porté en <c>cin_scheme = « 0002 »</c> (niveau SIREN, F05).
/// </summary>
public sealed class PaPublicationFormModel
{
    /// <summary>
    /// Date de début de publication à déclarer (F05 §2 : une date FUTURE = SIREN non publié, aucun envoi
    /// possible). Pré-remplie à aujourd'hui par la page ; modifiable par l'opérateur.
    /// </summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Type d'opération à déclarer côté PA (valeur attendue par la Plateforme Agréée — F05 §2 B.3).</summary>
    public string TypeOperation { get; set; } = string.Empty;

    /// <summary>Taille d'entreprise à déclarer côté PA (valeur attendue par la Plateforme Agréée — F05 §2 B.3).</summary>
    public string EnterpriseSize { get; set; } = string.Empty;

    /// <summary>Code NAF/INSEE à déclarer (facultatif — F05 §2 : code INSEE), ou vide.</summary>
    public string? NafCode { get; set; }
}
