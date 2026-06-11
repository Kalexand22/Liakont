namespace Liakont.Host.Fiscal;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;

/// <summary>
/// Implémentation de <see cref="IFiscalConsoleService"/> (FIX301). LECTURE : paramétrage fiscal du tenant via
/// la query TenantSettings (tenant résolu par le handler). MUTATION : <c>SetFiscalSettingsCommand</c> (le
/// handler valide, parse les listes fermées et journalise — append-only). Aucune logique métier ni règle
/// fiscale ici (CLAUDE.md n°2/4/19) : seule conversion de présentation = jeton tri-état → <c>bool?</c> et
/// chaîne vide → <c>null</c> (« non renseigné »). Les listes fermées proviennent de
/// <see cref="FiscalSettingsOptions"/> (énumérations du contrat), jamais d'une liste devinée.
/// </summary>
internal sealed class FiscalConsoleService : IFiscalConsoleService
{
    /// <summary>Jeton de formulaire « TVA sur les débits = Oui ».</summary>
    internal const string VatOnDebitsYesToken = "true";

    /// <summary>Jeton de formulaire « TVA sur les débits = Non ».</summary>
    internal const string VatOnDebitsNoToken = "false";

    private readonly ISender _sender;

    public FiscalConsoleService(ISender sender)
    {
        _sender = sender;
    }

    public async Task<FiscalViewModel> GetAsync(CancellationToken cancellationToken = default)
    {
        var fiscal = await _sender.Send(new GetFiscalSettingsQuery(), cancellationToken).ConfigureAwait(false);

        var form = new FiscalFormModel
        {
            // bool? → jeton tri-état ; null (non défini) reste « non renseigné » (aucun défaut appliqué).
            VatOnDebits = fiscal?.VatOnDebits switch
            {
                true => VatOnDebitsYesToken,
                false => VatOnDebitsNoToken,
                _ => string.Empty,
            },
            OperationCategory = fiscal?.OperationCategory ?? string.Empty,
            FeeImputationMethod = fiscal?.FeeImputationMethod ?? string.Empty,
            ReportingFrequency = fiscal?.ReportingFrequency ?? string.Empty,
        };

        return new FiscalViewModel
        {
            Form = form,
            OperationCategoryOptions = FiscalSettingsOptions.OperationCategories,
            FeeImputationMethodOptions = FiscalSettingsOptions.FeeImputationMethods,
        };
    }

    public async Task SaveAsync(FiscalSettingsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var command = new SetFiscalSettingsCommand
        {
            VatOnDebits = ParseVatOnDebits(input.VatOnDebits),
            OperationCategory = NullIfBlank(input.OperationCategory),
            FeeImputationMethod = NullIfBlank(input.FeeImputationMethod),
            ReportingFrequency = NullIfBlank(input.ReportingFrequency),
        };

        await _sender.Send(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Jeton tri-état → <c>bool?</c>. Vide (ou toute valeur hors des deux jetons fermés) = « non renseigné »
    /// = <c>null</c> = suspension conservée. La liste déroulante n'offre que les deux jetons, donc le
    /// repli sur <c>null</c> ne masque aucune saisie valide.
    /// </summary>
    private static bool? ParseVatOnDebits(string? token) => token switch
    {
        VatOnDebitsYesToken => true,
        VatOnDebitsNoToken => false,
        _ => null,
    };

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
