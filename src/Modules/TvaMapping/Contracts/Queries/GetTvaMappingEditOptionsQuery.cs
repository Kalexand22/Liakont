namespace Liakont.Modules.TvaMapping.Contracts.Queries;

using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Retourne les listes FERMÉES proposées à l'édition de la table de mapping TVA (item TVA05 / WEB07b) :
/// catégories UNCL5305, parts, modes de taux et codes VATEX admis. Vocabulaire STATIQUE (sans tenant,
/// sans accès base) dérivé des mêmes sources que le moteur d'édition (énumérations du domaine + listes
/// sourcées F03 §2.1/§2.2) — la console n'invente aucune valeur fiscale (CLAUDE.md n°2). Consommée par
/// la page WEB07b pour peupler les sélecteurs (jamais de saisie libre sur catégorie / VATEX).
/// </summary>
public sealed record GetTvaMappingEditOptionsQuery : IQuery<TvaMappingEditOptionsDto>;
