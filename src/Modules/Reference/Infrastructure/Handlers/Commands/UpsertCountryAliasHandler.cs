namespace Liakont.Modules.Reference.Infrastructure.Handlers.Commands;

using Liakont.Modules.Reference.Contracts.Commands;
using MediatR;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Applique <see cref="UpsertCountryAliasCommand"/> (ADR-0038) : résout l'auteur (piste d'audit) puis délègue
/// au référentiel, qui normalise la clé, VALIDE la cible ISO et écrit table + journal append-only dans la même
/// transaction. Aucune logique métier ici — déléguée au store (CLAUDE.md n°19).
/// </summary>
internal sealed class UpsertCountryAliasHandler : IRequestHandler<UpsertCountryAliasCommand>
{
    private readonly PostgresCountryAliasReferential _referential;
    private readonly IActorContextAccessor _actorContextAccessor;

    public UpsertCountryAliasHandler(
        PostgresCountryAliasReferential referential,
        IActorContextAccessor actorContextAccessor)
    {
        _referential = referential;
        _actorContextAccessor = actorContextAccessor;
    }

    public async Task Handle(UpsertCountryAliasCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = _actorContextAccessor.Current;
        await _referential.UpsertAsync(
            request.SourceCode, request.IsoCode, actor.UserId, actor.DisplayName, cancellationToken)
            .ConfigureAwait(false);
    }
}
