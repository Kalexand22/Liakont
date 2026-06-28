namespace Liakont.Host.BillingMentions;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;

/// <summary>
/// Implémentation de <see cref="IBillingMentionsConsoleService"/> (BUG-26, F12-A §3.4). LECTURE : mentions
/// de facturation du tenant via la query TenantSettings (tenant résolu par le handler). MUTATION :
/// <c>SetBillingMentionsCommand</c> (le handler upsert et journalise). Aucune logique métier ni règle fiscale
/// ici (CLAUDE.md n°2/4/19) : seule conversion de présentation = chaîne vide → <c>null</c> (« non renseigné »).
/// Aucun contenu n'est inventé par le produit (CLAUDE.md n°2/7).
/// </summary>
internal sealed class BillingMentionsConsoleService : IBillingMentionsConsoleService
{
    private readonly ISender _sender;

    public BillingMentionsConsoleService(ISender sender)
    {
        _sender = sender;
    }

    public async Task<BillingMentionsViewModel> GetAsync(CancellationToken cancellationToken = default)
    {
        var mentions = await _sender.Send(new GetBillingMentionsQuery(), cancellationToken).ConfigureAwait(false);

        var form = new BillingMentionsFormModel
        {
            // null (mention non renseignée) reste vide : aucun contenu n'est appliqué par défaut.
            PaymentTerms = mentions?.PaymentTerms ?? string.Empty,
            LatePenaltyTerms = mentions?.LatePenaltyTerms ?? string.Empty,
            RecoveryFeeTerms = mentions?.RecoveryFeeTerms ?? string.Empty,
            DiscountTerms = mentions?.DiscountTerms ?? string.Empty,
        };

        return new BillingMentionsViewModel
        {
            Form = form,
        };
    }

    public async Task SaveAsync(BillingMentionsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var command = new SetBillingMentionsCommand
        {
            PaymentTerms = NullIfBlank(input.PaymentTerms),
            LatePenaltyTerms = NullIfBlank(input.LatePenaltyTerms),
            RecoveryFeeTerms = NullIfBlank(input.RecoveryFeeTerms),
            DiscountTerms = NullIfBlank(input.DiscountTerms),
        };

        await _sender.Send(command, cancellationToken).ConfigureAwait(false);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
