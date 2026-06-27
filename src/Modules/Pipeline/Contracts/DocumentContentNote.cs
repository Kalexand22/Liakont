namespace Liakont.Modules.Pipeline.Contracts;

/// <summary>
/// Mention de niveau document EFFECTIVE restituée par le rejeu read-time (BUG-26, <see cref="DocumentContentReplay"/>) :
/// le texte (BT-22 <see cref="Content"/>) et son code sujet (BT-21 <see cref="SubjectCode"/>, codelist UNTDID 4451,
/// ex. <c>PMD</c> / <c>PMT</c> / <c>AAB</c>). Reflet en lecture des mentions légales FR (BR-FR-05) portées par le
/// document : valeur du document si elle existe, sinon défaut TENANT injecté par l'enricher (F12-A §3.4). Type de
/// PRÉSENTATION — aucune règle fiscale, aucun texte inventé (CLAUDE.md n°2) ; le libellé d'affichage du code reste
/// à la charge de la vue (Host).
/// </summary>
/// <param name="Content">Texte de la mention (EN 16931 BT-22).</param>
/// <param name="SubjectCode">Code sujet UNTDID 4451 (EN 16931 BT-21), <c>null</c> pour une note libre sans code.</param>
public sealed record DocumentContentNote(string Content, string? SubjectCode);
