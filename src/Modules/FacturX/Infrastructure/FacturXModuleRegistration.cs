namespace Liakont.Modules.FacturX.Infrastructure;

using System;
using Liakont.Modules.FacturX.Application;
using Liakont.Modules.FacturX.Application.Cii;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Enregistrement DI du module FacturX (ADR-0023) : point d'entrée de composition du module et
/// activation de la licence QuestPDF (déclarée par topologie/instance — RDF18, DEC-3) requise avant tout
/// rendu PDF/A-3. QuestPDF est CONFINÉE à cette couche (INV-FX-1) ; aucune autre couche FacturX ne la
/// référence. Le sérialiseur CII (FX03) et le builder de scellement PDF/A-3 (FX04, port
/// <c>IFacturXBuilder</c>) y sont enregistrés.
/// </summary>
public static class FacturXModuleRegistration
{
    /// <summary>
    /// Active la licence QuestPDF déclarée par configuration (<c>QuestPdf:LicenseType</c>, contrôle au
    /// déploiement fermé — RDF18), enregistre le sérialiseur CII et le builder Factur-X, et prépare la
    /// composition du module FacturX.
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    /// <param name="configuration">Configuration de l'application (porte <c>QuestPdf:LicenseType</c>).</param>
    /// <returns>La même collection, pour chaînage.</returns>
    public static IServiceCollection AddFacturXModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Licence QuestPDF (RDF18, DEC-3) : NON codée en dur. Déclarée par topologie/instance via
        // « QuestPdf:LicenseType » ; contrôle au déploiement fermé (valeur explicite et reconnue exigée).
        // Le socle (Stratum.Common.UI, AddCommonUI) pose Community par défaut pour SES exports PDF ;
        // FacturX est enregistré APRÈS (AppBootstrap) sur le chemin d'émission FISCALE et impose la
        // valeur configurée (dernière écriture du statique global QuestPDF.Settings.License).
        QuestPDF.Settings.License = QuestPdfLicenseConfiguration.Resolve(configuration);

        // Sérialiseur CII maison (FX03) — sans état, déterministe du pivot seul (ADR-0023 INV-FX-4).
        services.AddSingleton<ICrossIndustryInvoiceSerializer, CrossIndustryInvoiceSerializer>();

        // Builder de scellement PDF/A-3 (FX04) — sans état, consomme le sérialiseur CII ; QuestPDF
        // (rendu visuel + DocumentOperation) confinée à cette couche (INV-FX-1).
        services.AddSingleton<IFacturXBuilder, FacturXBuilder>();

        return services;
    }
}
