namespace Liakont.Modules.TvaMapping.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Crée une table de mapping TVA VIDE et « NON VALIDÉE » pour le tenant courant (item FIX01b) :
/// chemin du bouton « Créer la table » sur l'état vide de la console. La table naît avec la version
/// initiale, le comportement par défaut <c>block</c> (régime non mappé bloqué — sécurité par défaut,
/// F03 §4.1) et AUCUNE règle ; la création est journalisée (append-only, type <c>CreateTable</c>). Le
/// rapport de couverture et l'ajout de règles deviennent alors disponibles. Le tenant est résolu par le
/// contexte (CLAUDE.md n°9). Lève si une table existe déjà pour ce tenant (pas d'écrasement silencieux).
/// </summary>
public sealed record CreateMappingTableCommand : ICommand;
