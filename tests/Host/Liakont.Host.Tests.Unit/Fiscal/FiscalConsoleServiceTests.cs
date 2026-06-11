namespace Liakont.Host.Tests.Unit.Fiscal;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Fiscal;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TenantSettings.Domain.Entities;
using MediatR;
using Xunit;

/// <summary>
/// <see cref="FiscalConsoleService"/> (FIX301) : pré-remplissage depuis la query, mapping du formulaire vers
/// <see cref="SetFiscalSettingsCommand"/> (jeton tri-état → <c>bool?</c>, chaîne vide → <c>null</c> =
/// suspension conservée), et invariant des listes fermées = énumérations du contrat (aucune valeur inventée).
/// </summary>
public sealed class FiscalConsoleServiceTests
{
    [Fact]
    public async Task GetAsync_Prefills_From_The_Current_Settings()
    {
        var sender = new FakeSender
        {
            Fiscal = new FiscalSettingsDto
            {
                Id = Guid.NewGuid(),
                CompanyId = Guid.NewGuid(),
                VatOnDebits = true,
                OperationCategory = "Mixte",
                FeeImputationMethod = "AgregationJourTaux",
                ReportingFrequency = "mensuelle",
                CreatedAt = DateTimeOffset.UnixEpoch,
            },
        };
        var service = new FiscalConsoleService(sender);

        var model = await service.GetAsync();

        model.Form.VatOnDebits.Should().Be("true");
        model.Form.OperationCategory.Should().Be("Mixte");
        model.Form.FeeImputationMethod.Should().Be("AgregationJourTaux");
        model.Form.ReportingFrequency.Should().Be("mensuelle");
        model.OperationCategoryOptions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAsync_When_No_Settings_Yet_Leaves_Everything_Unset()
    {
        var service = new FiscalConsoleService(new FakeSender { Fiscal = null });

        var model = await service.GetAsync();

        // Aucun défaut appliqué : tout reste « non renseigné » (suspension conservée — CLAUDE.md n°2).
        model.Form.VatOnDebits.Should().BeEmpty();
        model.Form.OperationCategory.Should().BeEmpty();
        model.Form.FeeImputationMethod.Should().BeEmpty();
        model.Form.ReportingFrequency.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_Maps_The_Form_And_Sends_The_Command()
    {
        var sender = new FakeSender();
        var service = new FiscalConsoleService(sender);

        await service.SaveAsync(new FiscalSettingsInput
        {
            VatOnDebits = "false",
            OperationCategory = "LivraisonBiens",
            FeeImputationMethod = "Prorata",
            ReportingFrequency = "  mensuelle  ",
        });

        var command = sender.Sent.OfType<SetFiscalSettingsCommand>().Should().ContainSingle().Subject;
        command.VatOnDebits.Should().BeFalse();
        command.OperationCategory.Should().Be("LivraisonBiens");
        command.FeeImputationMethod.Should().Be("Prorata");
        command.ReportingFrequency.Should().Be("mensuelle");
    }

    [Fact]
    public async Task SaveAsync_Vat_Yes_Token_Maps_To_True()
    {
        var sender = new FakeSender();
        var service = new FiscalConsoleService(sender);

        await service.SaveAsync(new FiscalSettingsInput { VatOnDebits = "true" });

        var command = sender.Sent.OfType<SetFiscalSettingsCommand>().Should().ContainSingle().Subject;
        command.VatOnDebits.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_An_Unset_Form_Sends_Nulls_Preserving_Suspension()
    {
        var sender = new FakeSender();
        var service = new FiscalConsoleService(sender);

        await service.SaveAsync(new FiscalSettingsInput
        {
            VatOnDebits = string.Empty,
            OperationCategory = string.Empty,
            FeeImputationMethod = "   ",
            ReportingFrequency = null,
        });

        var command = sender.Sent.OfType<SetFiscalSettingsCommand>().Should().ContainSingle().Subject;
        command.VatOnDebits.Should().BeNull();
        command.OperationCategory.Should().BeNull();
        command.FeeImputationMethod.Should().BeNull();
        command.ReportingFrequency.Should().BeNull();
    }

    [Fact]
    public void Operation_Category_Options_Are_Exactly_The_Contract_Enum()
    {
        // L'écran ne peut offrir que des valeurs que le handler accepte, ni en omettre, ni en inventer :
        // la liste fermée DOIT coïncider avec l'énumération du contrat (source unique).
        FiscalSettingsOptions.OperationCategories.Select(o => o.Value)
            .Should().Equal(Enum.GetNames<OperationCategory>());
        FiscalSettingsOptions.OperationCategories.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.Label));
    }

    [Fact]
    public void Fee_Imputation_Options_Are_Exactly_The_Contract_Enum()
    {
        FiscalSettingsOptions.FeeImputationMethods.Select(o => o.Value)
            .Should().Equal(Enum.GetNames<FeeImputationMethod>());
        FiscalSettingsOptions.FeeImputationMethods.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.Label));
    }

    private sealed class FakeSender : ISender
    {
        public List<object> Sent { get; } = [];

        public FiscalSettingsDto? Fiscal { get; init; }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Sent.Add(request!);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            if (request is GetFiscalSettingsQuery)
            {
                return Task.FromResult((TResponse)(object?)Fiscal!);
            }

            return Task.FromResult(default(TResponse)!);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
