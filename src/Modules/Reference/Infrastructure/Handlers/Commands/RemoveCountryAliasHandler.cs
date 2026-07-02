namespace Liakont.Modules.Reference.Infrastructure.Handlers.Commands;

using Liakont.Modules.Reference.Contracts.Commands;
using MediatR;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Applique <see cref="RemoveCountryAliasCommand"/> (ADR-0038) : résout l'auteur (piste d'audit) puis délègue
/// au référentiel, qui normalise la clé et écrit la suppression + le journal append-only dans la même
/// transaction. Aucune logique métier ici — déléguée au store (CLAUDE.md n°19).
/// </summary>
internal sealed class RemoveCountryAliasHandler : IRequestHandler<RemoveCountryAliasCommand>
{
    private readonly PostgresCountryAliasReferential _referential;
    private readonly IActorContextAccessor _actorContextAccessor;

    public RemoveCountryAliasHandler(
        PostgresCountryAliasReferential referential,
        IActorContextAccessor actorContextAccessor)
    {
        _referential = referential;
        _actorContextAccessor = actorContextAccessor;
    }

    public async Task Handle(RemoveCountryAliasCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = _actorContextAccessor.Current;
        await _referential.RemoveAsync(
            request.SourceCode, actor.UserId, actor.DisplayName, cancellationToken)
            .ConfigureAwait(false);
    }
}
