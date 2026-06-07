namespace Liakont.Modules.Staging.Contracts;

using System;

/// <summary>
/// Localise le paquet d'archive WORM d'un document émis, pour subordonner la purge du staging à sa présence
/// EFFECTIVE (ADR-0014 §4). Les composants (année/mois d'émission + numéro de document) sont ceux qui
/// déterminent le chemin du paquet dans le coffre ; le tenant courant est résolu côté implémentation
/// (tenant-scopé, blueprint §7). Volontairement minimal : aucune donnée fiscale, juste de quoi sonder la
/// présence.
/// </summary>
/// <param name="DocumentId">L'identifiant du document émis (corrélation).</param>
/// <param name="IssueYear">L'année de la date d'émission (segment de chemin du paquet).</param>
/// <param name="IssueMonth">Le mois de la date d'émission (segment de chemin du paquet).</param>
/// <param name="DocumentNumber">Le numéro de document (EN 16931 BT-1 ; segment de chemin du paquet).</param>
public sealed record ArchivedDocumentLocator(Guid DocumentId, int IssueYear, int IssueMonth, string DocumentNumber);
