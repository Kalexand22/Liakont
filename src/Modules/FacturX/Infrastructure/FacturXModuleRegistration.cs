namespace Liakont.Modules.FacturX.Infrastructure;

using Liakont.Modules.FacturX.Application;
using Liakont.Modules.FacturX.Application.Cii;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;

/// <summary>
/// Enregistrement DI du module FacturX (ADR-0023) : point d'entrée de composition du module et
/// activation de la licence QuestPDF (Community) requise avant tout rendu PDF/A-3. QuestPDF est
/// CONFINÉE à cette couche (INV-FX-1) ; aucune autre couche FacturX ne la référence. Le sérialiseur CII
/// (FX03) et le builder de scellement PDF/A-3 (FX04, port <c>IFacturXBuilder</c>) y sont enregistrés.
/// </summary>
public static class FacturXModuleRegistration
{
    /// <summary>
    /// Active la licence QuestPDF Community (idempotent), enregistre le sérialiseur CII et le builder
    /// Factur-X, et prépare la composition du module FacturX.
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    /// <returns>La même collection, pour chaînage.</returns>
    public static IServiceCollection AddFacturXModule(this IServiceCollection services)
    {
        // QuestPDF (gratuit < 1 M$ CA — ADR-0023) exige l'activation explicite de la licence avant tout
        // rendu PDF. Idempotent : le socle (Common.UI) la pose déjà ; FacturX ne suppose pas cet ordre.
        QuestPDF.Settings.License = LicenseType.Community;

        // Sérialiseur CII maison (FX03) — sans état, déterministe du pivot seul (ADR-0023 INV-FX-4).
        services.AddSingleton<ICrossIndustryInvoiceSerializer, CrossIndustryInvoiceSerializer>();

        // Builder de scellement PDF/A-3 (FX04) — sans état, consomme le sérialiseur CII ; QuestPDF
        // (rendu visuel + DocumentOperation) confinée à cette couche (INV-FX-1).
        services.AddSingleton<IFacturXBuilder, FacturXBuilder>();

        return services;
    }
}
