namespace Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;

using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Domain.Entities;
using MediatR;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Définit (upsert) les mentions de facturation du tenant courant (F12-A §3.4, BUG-26). Le contenu est
/// SAISI par le client / son expert-comptable (CLAUDE.md n°2/7) ; une chaîne vide vaut « non renseignée »
/// (normalisée par l'entité). Mutation journalisée APRÈS commit (piste d'audit append-only).
/// </summary>
public sealed class SetBillingMentionsHandler : IRequestHandler<SetBillingMentionsCommand>
{
    private readonly ITenantSettingsUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly TenantSettingsJournal _journal;

    public SetBillingMentionsHandler(
        ITenantSettingsUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        TenantSettingsJournal journal)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _journal = journal;
    }

    public async Task Handle(SetBillingMentionsCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();

        Guid mentionsId;
        string activityType;

        await using (var uow = await _uowFactory.BeginAsync(cancellationToken))
        {
            var existing = await uow.GetBillingMentionsByCompanyAsync(companyId, cancellationToken);
            if (existing is null)
            {
                var mentions = BillingMentions.Create(
                    companyId, request.PaymentTerms, request.LatePenaltyTerms, request.RecoveryFeeTerms, request.DiscountTerms);
                await uow.InsertBillingMentionsAsync(mentions, cancellationToken);
                mentionsId = mentions.Id;
                activityType = "created";
            }
            else
            {
                existing.Update(
                    request.PaymentTerms, request.LatePenaltyTerms, request.RecoveryFeeTerms, request.DiscountTerms);
                await uow.UpdateBillingMentionsAsync(existing, cancellationToken);
                mentionsId = existing.Id;
                activityType = "updated";
            }

            await uow.CommitAsync(cancellationToken);
        }

        // Le journal d'audit ne porte que la PRÉSENCE de chaque mention (booléen), jamais le texte saisi :
        // une mention peut contenir des informations contractuelles du client (CLAUDE.md n°12, journal sobre).
        await _journal.RecordAsync(
            "BillingMentions",
            mentionsId,
            activityType,
            $"Mentions de facturation {activityType}.",
            companyId,
            new
            {
                HasPaymentTerms = !string.IsNullOrWhiteSpace(request.PaymentTerms),
                HasLatePenaltyTerms = !string.IsNullOrWhiteSpace(request.LatePenaltyTerms),
                HasRecoveryFeeTerms = !string.IsNullOrWhiteSpace(request.RecoveryFeeTerms),
                HasDiscountTerms = !string.IsNullOrWhiteSpace(request.DiscountTerms),
            },
            cancellationToken);
    }
}
