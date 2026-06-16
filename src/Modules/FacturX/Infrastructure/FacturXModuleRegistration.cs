namespace Liakont.Modules.FacturX.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;

/// <summary>
/// Enregistrement DI du module FacturX (ADR-0023) : point d'entrée de composition du module et
/// activation de la licence QuestPDF (Community) requise avant tout rendu PDF/A-3. QuestPDF est
/// CONFINÉE à cette couche (INV-FX-1) ; aucune autre couche FacturX ne la référence. L'implémentation
/// du port <c>IFacturXBuilder</c> (sérialiseur CII FX03 + scellement PDF/A-3 FX04) sera enregistrée
/// ici par les items suivants.
/// </summary>
public static class FacturXModuleRegistration
{
    /// <summary>
    /// Active la licence QuestPDF Community (idempotent) et prépare la composition du module FacturX.
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    /// <returns>La même collection, pour chaînage.</returns>
    public static IServiceCollection AddFacturXModule(this IServiceCollection services)
    {
        // QuestPDF (gratuit < 1 M$ CA — ADR-0023) exige l'activation explicite de la licence avant tout
        // rendu PDF. Idempotent : le socle (Common.UI) la pose déjà ; FacturX ne suppose pas cet ordre.
        QuestPDF.Settings.License = LicenseType.Community;

        return services;
    }
}
