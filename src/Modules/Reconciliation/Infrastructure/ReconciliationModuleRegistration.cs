namespace Liakont.Modules.Reconciliation.Infrastructure;

using Liakont.Modules.Reconciliation.Application;
using Liakont.Modules.Reconciliation.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Enregistrement DI du module Reconciliation (item TRK07, F06 §7) : migrations DbUp (schéma
/// <c>reconciliation</c> + file d'attente), store de file d'attente, extracteur de texte PDF, et le
/// service de réconciliation (action + lecture). Le handler de job SYSTÈME (fan-out multi-tenant) est
/// enregistré par le Host (composition root, via <c>AddJobHandler</c>), comme les autres job handlers.
/// </summary>
public static class ReconciliationModuleRegistration
{
    public static IServiceCollection AddReconciliationModule(this IServiceCollection services)
    {
        services.Configure<MigrationAssembliesOptions>(opts =>
            opts.Add(typeof(ReconciliationModuleRegistration).Assembly));

        // Extraction de texte PDF (PdfPig, ADR-0010) : sans état → singleton.
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();

        // File d'attente (base du tenant) + service de réconciliation. Une seule instance scoped sert les
        // deux surfaces (action IReconciliationService + lecture IReconciliationQueries).
        services.AddScoped<IReconciliationQueueStore, PostgresReconciliationQueueStore>();
        services.AddScoped<ReconciliationService>();
        services.AddScoped<IReconciliationService>(sp => sp.GetRequiredService<ReconciliationService>());
        services.AddScoped<IReconciliationQueries>(sp => sp.GetRequiredService<ReconciliationService>());

        return services;
    }
}
