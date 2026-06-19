namespace Liakont.Host.Demo;

using System;

/// <summary>
/// Une déclaration e-reporting B2C (flux 10.3) restituée à la démo Essentiel (B2C04). Sous-ensemble du modèle
/// de lecture des documents, enrichi de la présence du lien reporting↔pièces (B2C03) et de l'URL d'export
/// autoportant (réutilisée telle quelle, secrets PA masqués côté endpoint).
/// </summary>
/// <param name="Id">Identifiant du document (déclaration).</param>
/// <param name="Number">Numéro de la déclaration.</param>
/// <param name="IssueDate">Date d'émission.</param>
/// <param name="TotalGross">Montant total TTC (decimal — jamais float, CLAUDE.md n°1).</param>
/// <param name="State">État du document (<c>Issued</c> = transmis/accusé, <c>Blocked</c> = régime non mappé…).</param>
/// <param name="HasReportingLink">Vrai si le lien reporting↔pièces (B2C03) est gelé pour cette transmission.</param>
/// <param name="AuditExportUrl">URL de l'export contrôle fiscal autoportant d'un document.</param>
public sealed record DemoB2cDeclarationRow(
    Guid Id,
    string Number,
    DateOnly IssueDate,
    decimal TotalGross,
    string State,
    bool HasReportingLink,
    string AuditExportUrl);
