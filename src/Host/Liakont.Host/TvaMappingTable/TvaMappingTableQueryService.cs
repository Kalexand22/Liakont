namespace Liakont.Host.TvaMappingTable;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using MediatR;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de <see cref="ITvaMappingTableQueries"/> pour la page WEB07a (lecture) + WEB07b
/// (édition). LECTURE via les contracts TVA (<see cref="ITvaMappingQueries"/>) + couverture (TVA03) +
/// listes fermées (TVA05) scopées par le tenant courant ; MUTATIONS via les commandes MediatR TVA05
/// (validation, ajout, modification, suppression) — aucune règle fiscale ni logique métier ici
/// (catégorie, VATEX, taux, invalidation, journal : du ressort des handlers, CLAUDE.md n°2/3/4/19). La
/// société est résolue depuis l'identité authentifiée (<c>IActorContext.CompanyId</c>) — la MÊME source
/// que les commandes (via <c>ICompanyFilter</c>), pour que lecture et écriture portent toujours sur la
/// même table (CLAUDE.md n°9). Le valideur enregistré est l'identité authentifiée de l'opérateur
/// (CLAUDE.md n°12), jamais une valeur fournie par l'UI.
/// </summary>
internal sealed class TvaMappingTableQueryService : ITvaMappingTableQueries
{
    private readonly ITvaMappingQueries _queries;
    private readonly IActorContextAccessor _actorContext;
    private readonly ISender _sender;
    private readonly ITenantSettingsQueries _tenantSettingsQueries;

    public TvaMappingTableQueryService(
        ITvaMappingQueries queries,
        IActorContextAccessor actorContext,
        ISender sender,
        ITenantSettingsQueries tenantSettingsQueries)
    {
        _queries = queries;
        _actorContext = actorContext;
        _sender = sender;
        _tenantSettingsQueries = tenantSettingsQueries;
    }

    public async Task<TvaMappingTableViewModel> GetTableAsync(CancellationToken cancellationToken = default)
    {
        // Listes fermées d'édition : vocabulaire STATIQUE (sans tenant) — toujours disponible, même
        // quand aucune société n'est encore résolue (la page ne propose alors aucune édition de toute façon).
        var editOptions = await _sender.Send(new GetTvaMappingEditOptionsQuery(), cancellationToken).ConfigureAwait(false);

        // Société du contexte authentifié (même source que les commandes, via ICompanyFilter →
        // IActorContext.CompanyId) — lecture et écriture portent ainsi sur la même table (CLAUDE.md n°9).
        // Société non résolue (profil tenant pas encore créé — CFG02) : vue vide (transitoire), jamais
        // une erreur — même contrat que la page Paramétrage (WEB04b).
        var companyId = _actorContext.Current.CompanyId;
        if (companyId is null)
        {
            return new TvaMappingTableViewModel
            {
                Table = null,
                ChangeLog = Array.Empty<MappingChangeLogEntryDto>(),
                CurrentOperatorName = ResolveOperatorIdentity(),
                Coverage = null,
                TenantResolved = false,
                AuctionVerticalEnabled = false,
                Consistency = null,
                EditOptions = editOptions,
            };
        }

        var table = await _queries.GetMappingTable(companyId.Value, cancellationToken).ConfigureAwait(false);
        var changeLog = await _queries.GetChangeLog(companyId.Value, cancellationToken).ConfigureAwait(false);

        // Rapport de couverture (TVA03) : régimes source observés non mappés « à compléter ». Recalculé
        // à la demande (toujours à jour après push d'agent et après chaque mutation de table). Tenant
        // résolu par le handler (CLAUDE.md n°9) — il porte sur la même société que la table ci-dessus.
        var coverage = await _sender.Send(new GetMappingCoverageReportQuery(), cancellationToken).ConfigureAwait(false);

        // Activation du vertical enchères (paramétrage produit D4) : gouverne l'exposition du champ
        // « part » dans l'éditeur (et RIEN d'autre — la cohérence reflète la réalité du pipeline, pas
        // l'activation). Lue une fois ici (défaut OFF si absente).
        var auctionVerticalEnabled = await _tenantSettingsQueries
            .GetAuctionVerticalEnabled(companyId.Value, cancellationToken)
            .ConfigureAwait(false);

        // Rapport de cohérence (lot FIX03) : règles mortes (part non consultée par le pipeline, code
        // jamais observé) signalées avant validation. Recalculé à la demande, comme la couverture.
        var consistency = await _sender
            .Send(new GetMappingConsistencyReportQuery(), cancellationToken)
            .ConfigureAwait(false);

        return new TvaMappingTableViewModel
        {
            Table = table,
            ChangeLog = changeLog,
            CurrentOperatorName = ResolveOperatorIdentity(),
            Coverage = coverage,
            TenantResolved = true,
            AuctionVerticalEnabled = auctionVerticalEnabled,
            Consistency = consistency,
            EditOptions = editOptions,
        };
    }

    public Task SetAuctionVerticalAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        // Mutation du paramétrage produit (D4) : commande TenantSettings journalisée côté handler. La
        // garde de permission (liakont.settings) est appliquée par la page (parité avec la validation).
        return _sender.Send(new SetAuctionVerticalActivationCommand { Enabled = enabled }, cancellationToken);
    }

    public Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        // validatedBy = identité AUTHENTIFIÉE de l'opérateur courant (jamais une valeur de l'UI : un
        // opérateur ne peut pas signer la validation au nom d'un autre — parité avec l'endpoint API04).
        var validatedBy = ResolveOperatorIdentity();
        return _sender.Send(new ValidateMappingTableCommand { ValidatedBy = validatedBy }, cancellationToken);
    }

    public Task AddRuleAsync(TvaRuleFormModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Pass-through pur vers la commande TVA05 : aucune validation fiscale ici (le handler valide la
        // catégorie, le VATEX, le taux et l'absence de doublon, et invalide + journalise atomiquement).
        var command = new AddMappingRuleCommand
        {
            SourceRegimeCode = model.SourceRegimeCode,
            Label = model.Label,
            Part = model.Part,
            SourceFlags = model.SourceFlags,
            Category = model.Category,
            Vatex = model.Vatex,
            Note = model.Note,
            RateMode = model.RateMode,
            RateValue = model.RateValue,
        };

        return _sender.Send(command, cancellationToken);
    }

    public Task UpdateRuleAsync(TvaRuleFormModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Le couple (code régime, part) identifie la règle et reste inchangé (pour changer la clé :
        // supprimer puis ajouter). Les flags source existants sont préservés tels quels (l'édition de
        // flags n'est pas offerte en console V1) — jamais effacés silencieusement.
        var command = new UpdateMappingRuleCommand
        {
            SourceRegimeCode = model.SourceRegimeCode,
            Part = model.Part,
            Label = model.Label,
            SourceFlags = model.SourceFlags,
            Category = model.Category,
            Vatex = model.Vatex,
            Note = model.Note,
            RateMode = model.RateMode,
            RateValue = model.RateValue,
        };

        return _sender.Send(command, cancellationToken);
    }

    public Task RemoveRuleAsync(string sourceRegimeCode, string part, CancellationToken cancellationToken = default)
    {
        return _sender.Send(
            new RemoveMappingRuleCommand { SourceRegimeCode = sourceRegimeCode, Part = part },
            cancellationToken);
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
