namespace Liakont.Modules.Validation.Infrastructure;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain;

/// <summary>
/// Implémentation de <see cref="IValidationService"/> : expose, à la frontière Contracts, le
/// <see cref="ValidationPipeline"/> du domaine — construit sur l'ensemble des règles
/// (<see cref="IDocumentRule"/>) enregistrées par les items VAL02-VAL05. Aucune logique propre : la
/// garantie « jamais de règle silencieuse » (une règle qui lève → anomalie bloquante) vit dans le pipeline.
/// </summary>
internal sealed class ValidationService : IValidationService
{
    private readonly ValidationPipeline _pipeline;

    public ValidationService(IEnumerable<IDocumentRule> rules)
    {
        _pipeline = new ValidationPipeline(rules);
    }

    public Task<ValidationResult> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default) =>
        _pipeline.ValidateAsync(context, cancellationToken);
}
