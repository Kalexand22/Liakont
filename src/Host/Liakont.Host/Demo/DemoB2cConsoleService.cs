namespace Liakont.Host.Demo;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Documents;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Implémentation de <see cref="IDemoB2cConsoleService"/> (B2C04). PURE COMPOSITION de lecture, AUCUNE logique
/// métier ni second chemin d'envoi :
/// <list type="bullet">
/// <item>les documents du tenant via <see cref="IDocumentConsoleQueries"/> (tenant-scopé), filtrés sur le type
/// brut <c>DECLARATION</c> (le marqueur 10.3 du pivot projette ce type — <c>SourceDocumentKind</c>) ;</item>
/// <item>la présence du lien reporting↔pièces (B2C03) via <see cref="IReportingPieceLinkStore"/>, avec le
/// <c>companyId</c> du tenant courant (jamais cross-tenant, CLAUDE.md n°9).</item>
/// </list>
/// La période est NON bornée (déclarations de démo datées librement) ; la projection reste tenant-scopée par la
/// connexion. Page de DÉMONSTRATION : ce filtrage par type brut est volontairement simple (un document de
/// déclaration porte <c>SourceDocumentKind = "DECLARATION"</c>).
/// </summary>
internal sealed class DemoB2cConsoleService : IDemoB2cConsoleService
{
    /// <summary>Type BRUT (<c>SourceDocumentKind</c>) d'une déclaration e-reporting B2C (flux 10.3).</summary>
    internal const string DeclarationDocumentType = "DECLARATION";

    /// <summary>État « transmis/accusé » d'une transmission émise.</summary>
    internal const string IssuedState = "Issued";

    private readonly IDocumentConsoleQueries _documents;
    private readonly IReportingPieceLinkStore _linkStore;
    private readonly ITenantSettingsQueries _tenantSettings;

    public DemoB2cConsoleService(
        IDocumentConsoleQueries documents,
        IReportingPieceLinkStore linkStore,
        ITenantSettingsQueries tenantSettings)
    {
        _documents = documents;
        _linkStore = linkStore;
        _tenantSettings = tenantSettings;
    }

    public async Task<DemoB2cViewModel> GetAsync(CancellationToken cancellationToken = default)
    {
        // Filtrage CÔTÉ SERVEUR sur le type brut DECLARATION : le plafond de chargement s'applique au seul type
        // demandé (jamais une troncature silencieuse d'un sous-ensemble filtré après coup — anti faux-vert).
        var declarations = await _documents
            .GetDocumentsInPeriodAsync(from: null, to: null, documentType: DeclarationDocumentType, cancellationToken)
            .ConfigureAwait(false);

        // companyId du tenant courant (défense en profondeur pour la lecture tenant-scopée du store de liens).
        // Absent (profil non créé) → aucun lien restituable : la démo reste lisible (états/export), sans inventer.
        var companyId = await _tenantSettings.GetCurrentCompanyId(cancellationToken).ConfigureAwait(false);

        var rows = new List<DemoB2cDeclarationRow>(declarations.Count);
        foreach (var declaration in declarations)
        {
            var hasLink = false;
            if (companyId is not null && string.Equals(declaration.State, IssuedState, StringComparison.Ordinal))
            {
                // Le lien n'est gelé qu'à l'émission (B2C04) : on ne l'interroge que pour une transmission émise.
                var links = await _linkStore.GetByDocumentAsync(companyId.Value, declaration.Id, cancellationToken).ConfigureAwait(false);
                hasLink = links.Count > 0;
            }

            rows.Add(new DemoB2cDeclarationRow(
                declaration.Id,
                declaration.DocumentNumber,
                declaration.IssueDate,
                declaration.TotalGross,
                declaration.State,
                hasLink,
                AuditExportUrl(declaration.Id)));
        }

        return new DemoB2cViewModel { Declarations = rows };
    }

    /// <summary>URL de l'export contrôle fiscal autoportant d'un document (réutilise l'endpoint API03/B2C03).</summary>
    private static string AuditExportUrl(Guid documentId) =>
        string.Create(CultureInfo.InvariantCulture, $"/api/v1/documents/{documentId}/audit-export");
}
