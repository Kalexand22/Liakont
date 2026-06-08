namespace Liakont.Host.TvaMappingTable;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using MediatR;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de <see cref="ITvaMappingTableQueries"/> pour la page WEB07a. LECTURE via les
/// contracts TVA (<see cref="ITvaMappingQueries"/>) scopée par le tenant courant ; VALIDATION via la
/// commande MediatR TVA05 (<see cref="ValidateMappingTableCommand"/>) — aucune règle fiscale ni logique
/// métier ici (catégorie, VATEX, taux, invalidation, journal : du ressort des handlers, CLAUDE.md
/// n°2/3/4/19). La société est résolue depuis l'identité authentifiée (<c>IActorContext.CompanyId</c>) —
/// la MÊME source que la commande de validation (via <c>ICompanyFilter</c>), pour que lecture et écriture
/// portent toujours sur la même table (CLAUDE.md n°9). Le valideur enregistré est l'identité authentifiée
/// de l'opérateur (CLAUDE.md n°12), jamais une valeur fournie par l'UI.
/// </summary>
internal sealed class TvaMappingTableQueryService : ITvaMappingTableQueries
{
    private readonly ITvaMappingQueries _queries;
    private readonly IActorContextAccessor _actorContext;
    private readonly ISender _sender;

    public TvaMappingTableQueryService(
        ITvaMappingQueries queries,
        IActorContextAccessor actorContext,
        ISender sender)
    {
        _queries = queries;
        _actorContext = actorContext;
        _sender = sender;
    }

    public async Task<TvaMappingTableViewModel> GetTableAsync(CancellationToken cancellationToken = default)
    {
        // Société du contexte authentifié (même source que la commande de validation, via ICompanyFilter →
        // IActorContext.CompanyId) — lecture et écriture portent ainsi sur la même table (CLAUDE.md n°9).
        // Société non résolue (profil tenant pas encore créé — CFG02) : vue vide (transitoire), jamais une
        // erreur — même contrat que la page Paramétrage (WEB04b).
        var companyId = _actorContext.Current.CompanyId;
        if (companyId is null)
        {
            return new TvaMappingTableViewModel
            {
                Table = null,
                ChangeLog = Array.Empty<MappingChangeLogEntryDto>(),
                CurrentOperatorName = ResolveOperatorIdentity(),
            };
        }

        var table = await _queries.GetMappingTable(companyId.Value, cancellationToken).ConfigureAwait(false);
        var changeLog = await _queries.GetChangeLog(companyId.Value, cancellationToken).ConfigureAwait(false);

        return new TvaMappingTableViewModel
        {
            Table = table,
            ChangeLog = changeLog,
            CurrentOperatorName = ResolveOperatorIdentity(),
        };
    }

    public Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        // validatedBy = identité AUTHENTIFIÉE de l'opérateur courant (jamais une valeur de l'UI : un
        // opérateur ne peut pas signer la validation au nom d'un autre — parité avec l'endpoint API04).
        var validatedBy = ResolveOperatorIdentity();
        return _sender.Send(new ValidateMappingTableCommand { ValidatedBy = validatedBy }, cancellationToken);
    }

    /// <summary>
    /// Identité lisible de l'opérateur courant : nom affiché, à défaut e-mail, à défaut identifiant.
    /// Lève si aucune identité n'est résolue plutôt que d'enregistrer une validation anonyme (mêmes
    /// règles que <c>TvaMappingEndpointMapping.ResolveOperatorIdentity</c>, CLAUDE.md n°12).
    /// </summary>
    private string ResolveOperatorIdentity()
    {
        var actor = _actorContext.Current;

        if (!string.IsNullOrWhiteSpace(actor.DisplayName))
        {
            return actor.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(actor.Email))
        {
            return actor.Email;
        }

        if (actor.UserId != Guid.Empty)
        {
            return actor.UserId.ToString();
        }

        throw new InvalidOperationException(
            "Identité de l'opérateur introuvable : impossible de valider la table TVA sans valideur (CLAUDE.md n°12).");
    }
}
