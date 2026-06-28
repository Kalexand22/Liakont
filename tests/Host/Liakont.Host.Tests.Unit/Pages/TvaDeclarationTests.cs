namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.TvaDeclaration;
using Liakont.Modules.Pipeline.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests bUnit de la page « TVA / Déclaration » (aide à la déclaration de TVA sous le régime de la marge, L2 ;
/// règle de review n°19 : une page Blazor sans test est un P1). Couvre les états observables : récap (total TVA
/// sur marge) + détail par taux, état vide explicite, bandeau d'erreur (sans fuite + filtre toujours
/// disponible), rechargement par période, format français des montants. Aucune logique métier dans la page
/// (déléguée au service) — seule la projection est testée.
/// </summary>
public sealed class TvaDeclarationTests : BunitContext
{
    public TvaDeclarationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs();
    }

    [Fact]
    public void Renders_The_Total_Vat_To_Report_And_The_Rate_Detail()
    {
        Services.AddScoped<ITvaDeclarationConsoleQueries>(_ => FakeQueries.Returning(
            Model([Line(ratePercent: 20m, baseHt: 8.33m, vat: 1.67m)])));

        var cut = Render<TvaDeclaration>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='tva-declaration-recap']").Should().NotBeNull();

            // Total TVA sur marge à reporter (somme des lignes), formaté en français.
            cut.Find("[data-testid='tva-declaration-total-vat']").TextContent.Should().Contain("1,67");

            // Détail par taux : la ligne 20 % apparaît.
            cut.Markup.Should().Contain("20 %");
        });
    }

    [Fact]
    public void Sums_The_Total_Across_Rate_Lines()
    {
        Services.AddScoped<ITvaDeclarationConsoleQueries>(_ => FakeQueries.Returning(
            Model([
                Line(ratePercent: 20m, baseHt: 100m, vat: 20m),
                Line(ratePercent: 5.5m, baseHt: 50m, vat: 2.75m),
            ])));

        var cut = Render<TvaDeclaration>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='tva-declaration-total-base']").TextContent.Should().Contain("150,00");
            cut.Find("[data-testid='tva-declaration-total-vat']").TextContent.Should().Contain("22,75");
        });
    }

    [Fact]
    public void Shows_An_Explicit_Empty_State_When_There_Is_No_Margin()
    {
        Services.AddScoped<ITvaDeclarationConsoleQueries>(_ => FakeQueries.Returning(TvaDeclarationViewModel.Empty));

        var cut = Render<TvaDeclaration>();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='tva-declaration-empty']").Should().ContainSingle());
    }

    [Fact]
    public void Shows_Error_Banner_When_Load_Throws()
    {
        Services.AddScoped<ITvaDeclarationConsoleQueries>(_ => FakeQueries.Throwing());

        var cut = Render<TvaDeclaration>();

        // L'échec reste VISIBLE (bandeau) et n'expose AUCUNE donnée (ni récap, ni état vide), mais le filtre
        // période RESTE disponible pour réessayer (anti faux-vert + ergonomie).
        cut.FindAll("[data-testid='tva-declaration-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='tva-declaration-filters']").Should().ContainSingle();
        cut.FindAll("[data-testid='tva-declaration-recap']").Should().BeEmpty();
        cut.FindAll("[data-testid='tva-declaration-empty']").Should().BeEmpty();
    }

    [Fact]
    public void Changing_The_Period_Reloads_The_Declaration_For_That_Month()
    {
        var spy = new SpyQueries(
            Model([Line(ratePercent: 20m, baseHt: 8.33m, vat: 1.67m)]),
            Model([Line(ratePercent: 5.5m, baseHt: 50m, vat: 2.75m)]));
        Services.AddScoped<ITvaDeclarationConsoleQueries>(_ => spy);

        var cut = Render<TvaDeclaration>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("20 %"));

        cut.Find("[data-testid='tva-declaration-filter-period']").Change("2026-01");

        cut.WaitForAssertion(() =>
        {
            spy.RequestedPeriods.Should().Contain("2026-01");
            cut.Markup.Should().Contain("5,5 %");
        });
    }

    private static TvaDeclarationViewModel Model(IReadOnlyList<TvaDeclarationRow> lines)
    {
        decimal totalBase = 0m, totalVat = 0m;
        foreach (var line in lines)
        {
            totalBase += line.MarginBaseHt;
            totalVat += line.MarginVat;
        }

        return new TvaDeclarationViewModel { Lines = lines, TotalBaseHt = totalBase, TotalVat = totalVat };
    }

    private static TvaDeclarationRow Line(decimal ratePercent, decimal baseHt, decimal vat, int documentCount = 1) =>
        TvaDeclarationRow.FromDto(new MarginRegistryMonthlyDto
        {
            Period = "2026-06",
            CurrencyCode = "EUR",
            RatePercent = ratePercent,
            MarginBaseHt = baseHt,
            MarginVat = vat,
            DocumentCount = documentCount,
        });

    private sealed class FakeQueries : ITvaDeclarationConsoleQueries
    {
        private readonly TvaDeclarationViewModel? _model;
        private readonly bool _throws;

        private FakeQueries(TvaDeclarationViewModel? model, bool throws)
        {
            _model = model;
            _throws = throws;
        }

        public static FakeQueries Returning(TvaDeclarationViewModel model) => new(model, throws: false);

        public static FakeQueries Throwing() => new(null, throws: true);

        public Task<TvaDeclarationViewModel> GetDeclarationAsync(string? period, CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de chargement de l'aide à la déclaration.");
            }

            return Task.FromResult(_model!);
        }
    }

    private sealed class SpyQueries : ITvaDeclarationConsoleQueries
    {
        private readonly TvaDeclarationViewModel _firstLoad;
        private readonly TvaDeclarationViewModel _afterPeriodChange;
        private int _calls;

        public SpyQueries(TvaDeclarationViewModel firstLoad, TvaDeclarationViewModel afterPeriodChange)
        {
            _firstLoad = firstLoad;
            _afterPeriodChange = afterPeriodChange;
        }

        public List<string?> RequestedPeriods { get; } = [];

        public Task<TvaDeclarationViewModel> GetDeclarationAsync(string? period, CancellationToken cancellationToken = default)
        {
            RequestedPeriods.Add(period);
            var model = _calls == 0 ? _firstLoad : _afterPeriodChange;
            _calls++;
            return Task.FromResult(model);
        }
    }
}
