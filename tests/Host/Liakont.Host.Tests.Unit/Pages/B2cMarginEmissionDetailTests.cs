namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.B2cReporting;
using Liakont.Host.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Stratum.Common.UI.Time;
using Xunit;

/// <summary>
/// Tests bUnit de la fiche de DÉTAIL d'une émission e-reporting B2C (BUG-22, règle de review n°19). Couvre les
/// états observables : synthèse + motif PA lisible + pièces avec lien vers le document, état introuvable, bandeau
/// d'erreur. Aucune logique métier dans la page (déléguée au service) — seule la projection est testée.
/// </summary>
public sealed class B2cMarginEmissionDetailTests : BunitContext
{
    public B2cMarginEmissionDetailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAdminPageStubs();
    }

    [Fact]
    public void Renders_The_Aggregate_Summary_And_Status()
    {
        var batchId = Guid.NewGuid();
        Services.AddScoped<IB2cMarginEmissionsConsoleQueries>(_ => FakeQueries.Returning(
            Detail(batchId, status: "Issued", paEmissionId: "591")));

        var cut = Render<B2cMarginEmissionDetail>(p => p.Add(v => v.EmissionBatchId, batchId));

        cut.Find("[data-testid='emission-detail-title']").TextContent.Should().Contain("23/06/2026");
        cut.Find("[data-testid='emission-detail-pa-id']").TextContent.Should().Contain("591");
        cut.FindAll("[data-testid='emission-detail-status']").Should().NotBeEmpty();
        cut.Find("[data-testid='emission-detail-currency']").TextContent.Should().Contain("EUR");
    }

    [Fact]
    public void Shows_The_Pa_Motif_When_Rejected()
    {
        var batchId = Guid.NewGuid();
        Services.AddScoped<IB2cMarginEmissionsConsoleQueries>(_ => FakeQueries.Returning(
            Detail(
                batchId,
                status: "RejectedByPa",
                paEmissionId: null,
                detail: "Rejet par la plateforme.",
                paLines: ["cannot add transaction at date 2024-01-03"])));

        var cut = Render<B2cMarginEmissionDetail>(p => p.Add(v => v.EmissionBatchId, batchId));

        cut.Find("[data-testid='emission-detail-motif-detail']").TextContent.Should().Contain("Rejet par la plateforme");
        cut.Find("[data-testid='emission-detail-pa-response']").TextContent.Should().Contain("cannot add transaction at date");
    }

    [Fact]
    public void Lists_The_Composing_Documents_With_A_Link_To_Each()
    {
        var batchId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        Services.AddScoped<IB2cMarginEmissionsConsoleQueries>(_ => FakeQueries.Returning(
            Detail(batchId, status: "Issued", paEmissionId: "591", documents:
            [
                new B2cMarginEmissionDetailDocumentRow { DocumentId = docId, SourceReference = "encheresv6:ba:9000004", Family = "Bordereau acheteur" },
            ])));

        var cut = Render<B2cMarginEmissionDetail>(p => p.Add(v => v.EmissionBatchId, batchId));

        cut.FindAll("[data-testid='emission-detail-document']").Should().ContainSingle();
        cut.Find("[data-testid='emission-detail-documents']").TextContent.Should().Contain("Bordereau acheteur").And.Contain("encheresv6:ba:9000004");
        cut.Find("[data-testid='emission-detail-document-link']").GetAttribute("href").Should().Be($"/documents/{docId}");
    }

    [Fact]
    public void Last_Activity_Renders_In_The_Browser_Timezone()
    {
        // BUG-22 (suivi review) : « Dernière activité » au fuseau du NAVIGATEUR (RB6), pas du serveur — cohérent avec
        // la liste sœur. Fuseau résolu (Europe/Paris, UTC+2 en juin) ⇒ 09:30 UTC → 11:30 local, jamais de suffixe UTC.
        var paris = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Paris");
        Services.AddScoped<IBrowserTimeZone>(_ => new ResolvedBrowserTimeZone(paris));
        var batchId = Guid.NewGuid();
        Services.AddScoped<IB2cMarginEmissionsConsoleQueries>(_ => FakeQueries.Returning(
            Detail(batchId, status: "Issued", paEmissionId: "591")));

        var cut = Render<B2cMarginEmissionDetail>(p => p.Add(v => v.EmissionBatchId, batchId));

        var cell = cut.Find("[data-testid='emission-detail-last-activity']").TextContent;
        cell.Should().Contain("23/06/2026 11:30", "09:30 UTC est rendu au fuseau navigateur (UTC+2 en juin)");
        cell.Should().NotContain("UTC", "la dernière activité n'est plus rendue en UTC ni en heure serveur");
    }

    [Fact]
    public void Shows_A_Not_Found_Banner_When_The_Batch_Is_Unknown()
    {
        Services.AddScoped<IB2cMarginEmissionsConsoleQueries>(_ => FakeQueries.Returning(null));

        var cut = Render<B2cMarginEmissionDetail>(p => p.Add(v => v.EmissionBatchId, Guid.NewGuid()));

        cut.FindAll("[data-testid='emission-detail-notfound']").Should().ContainSingle();
        cut.FindAll("[data-testid='emission-detail-recap']").Should().BeEmpty();
    }

    [Fact]
    public void Shows_An_Error_Banner_When_Load_Throws()
    {
        Services.AddScoped<IB2cMarginEmissionsConsoleQueries>(_ => FakeQueries.Throwing());

        var cut = Render<B2cMarginEmissionDetail>(p => p.Add(v => v.EmissionBatchId, Guid.NewGuid()));

        cut.FindAll("[data-testid='emission-detail-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='emission-detail-recap']").Should().BeEmpty();
    }

    private static B2cMarginEmissionDetailViewModel Detail(
        Guid batchId,
        string status,
        string? paEmissionId,
        string? detail = null,
        IReadOnlyList<string>? paLines = null,
        IReadOnlyList<B2cMarginEmissionDetailDocumentRow>? documents = null) =>
        new()
        {
            EmissionBatchId = batchId,
            AggregateDate = new DateOnly(2026, 6, 23),
            Currency = "EUR",
            Category = "TMA1",
            Role = "SE",
            Status = status,
            PaEmissionId = string.IsNullOrWhiteSpace(paEmissionId) ? "—" : paEmissionId!,
            Detail = string.IsNullOrWhiteSpace(detail) ? "—" : detail!,
            PaResponseLines = paLines ?? [],
            LastActivityUtc = new DateTimeOffset(2026, 6, 23, 9, 30, 0, TimeSpan.Zero),
            Documents = documents ?? [],
        };

    // Fuseau navigateur DÉJÀ résolu (LiakontDate rend alors en heure locale, sans suffixe UTC).
    private sealed class ResolvedBrowserTimeZone : IBrowserTimeZone
    {
        public ResolvedBrowserTimeZone(TimeZoneInfo zone) => Zone = zone;

        public event Action? Resolved
        {
            add { }
            remove { }
        }

        public TimeZoneInfo? Zone { get; }

        public bool IsResolved => true;

        public Task EnsureResolvedAsync(IJSRuntime js, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeQueries : IB2cMarginEmissionsConsoleQueries
    {
        private readonly B2cMarginEmissionDetailViewModel? _detail;
        private readonly bool _throws;

        private FakeQueries(B2cMarginEmissionDetailViewModel? detail, bool throws)
        {
            _detail = detail;
            _throws = throws;
        }

        public static FakeQueries Returning(B2cMarginEmissionDetailViewModel? detail) => new(detail, throws: false);

        public static FakeQueries Throwing() => new(null, throws: true);

        public Task<B2cMarginEmissionsViewModel> GetEmissionsAsync(string? period, CancellationToken cancellationToken = default) =>
            Task.FromResult(new B2cMarginEmissionsViewModel { Emissions = [] });

        public Task<B2cMarginEmissionDetailViewModel?> GetEmissionDetailAsync(Guid emissionBatchId, CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de chargement du détail d'émission.");
            }

            return Task.FromResult(_detail);
        }
    }
}
