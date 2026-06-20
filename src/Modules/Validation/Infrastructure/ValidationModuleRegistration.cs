namespace Liakont.Modules.Validation.Infrastructure;

using System;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Rules;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Enregistrement DI du module Validation (F04) : expose <see cref="IValidationService"/> à la frontière
/// Contracts (consommé par le pipeline, PIP01b) et enregistre l'ensemble EXPLICITE et AUDITABLE des règles
/// de validation (<see cref="IDocumentRule"/>, VAL02-VAL05). Un validateur de conformité ne doit JAMAIS
/// passer en silence faute de règles (CLAUDE.md n°3) : l'ensemble est non vide et tracé ligne par ligne ici.
/// </summary>
public static class ValidationModuleRegistration
{
    /// <summary>Enregistre le service de validation et l'ensemble des règles métier (F04).</summary>
    /// <param name="services">La collection de services.</param>
    /// <returns>La collection de services, pour chaînage.</returns>
    public static IServiceCollection AddValidationModule(this IServiceCollection services)
    {
        services.AddScoped<IValidationService, ValidationService>();

        // Ensemble EXPLICITE des règles de validation (F04, VAL02-VAL05). Le pipeline (PIP01b, CHECK) résout
        // IValidationService qui les agrège via ValidationPipeline. Les dépendances inter-modules sont fournies
        // au composition root (Host) : IIssuedInvoiceLookup / IIssuedDocumentLookup (TRK03, module Documents),
        // ITenantSettingsQueries (CFG, module TenantSettings). StructureRule reçoit l'horloge système.
        services.AddScoped<IDocumentRule>(_ => new StructureRule(TimeProvider.System));
        services.AddScoped<IDocumentRule, ArithmeticRule>();
        services.AddScoped<IDocumentRule, LineTotalsRule>();
        services.AddScoped<IDocumentRule, SourceTotalsRule>();
        services.AddScoped<IDocumentRule, CategoryRateConsistencyRule>();
        services.AddScoped<IDocumentRule, VatexRequiredRule>();
        services.AddScoped<IDocumentRule, MappingCoverageRule>();
        services.AddScoped<IDocumentRule, SupplierIdentityRule>();
        services.AddScoped<IDocumentRule, BuyerIdentityRule>();
        services.AddScoped<IDocumentRule, BuyerLooksProfessionalRule>();
        services.AddScoped<IDocumentRule, UniquenessRule>();
        services.AddScoped<IDocumentRule, CreditNoteRule>();
        services.AddScoped<IDocumentRule, PartyRoleConsistencyRule>();

        return services;
    }
}
