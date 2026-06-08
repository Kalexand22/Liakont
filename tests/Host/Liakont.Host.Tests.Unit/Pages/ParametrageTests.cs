namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Parametrage;
using Liakont.Modules.Archive.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class ParametrageTests : BunitContext
{
    public ParametrageTests()
    {
        // RadzenButton (StratumButton) dans la vue imbriquée appelle du JS : mode permissif.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
    }

    [Fact]
    public void Should_Render_View_When_Load_Succeeds()
    {
        Services.AddScoped<IParametrageQueries>(_ => FakeParametrageQueries.Succeeding(BuildModel()));

        var cut = Render<Parametrage>();

        cut.FindAll("[data-testid='liakont-parametrage']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-error']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Error_Banner_When_Load_Throws()
    {
        Services.AddScoped<IParametrageQueries>(_ => FakeParametrageQueries.Throwing());

        var cut = Render<Parametrage>();

        // L'échec d'assemblage reste VISIBLE (bandeau) et n'expose pas la page (anti faux-vert).
        cut.FindAll("[data-testid='parametrage-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='liakont-parametrage']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Render_Integrity_Report_After_Verify_Click()
    {
        var report = new ArchiveVerificationReport(
            new ArchiveIntegrityReport(IsIntact: true, EntryCount: 3, Entries: [], FirstBreakDetail: null),
            Anchors: [],
            IsChainAnchored: true,
            IsFullyVerified: true,
            Summary: "Coffre vérifié.");
        Services.AddScoped<IParametrageQueries>(_ => FakeParametrageQueries.Succeeding(BuildModel(), report));

        var cut = Render<Parametrage>();
        cut.Find("[data-testid='parametrage-integrite-btn']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='parametrage-integrite-report']").Should().ContainSingle());
        cut.Find("[data-testid='parametrage-integrite-summary']").TextContent.Should().Contain("Coffre vérifié.");
    }

    private static ParametrageViewModel BuildModel() => new()
    {
        Profile = null,
        FiscalSettings = null,
        TvaMapping = null,
        PaAccounts = [],
        Agents = [],
    };

    private sealed class FakeParametrageQueries : IParametrageQueries
    {
        private readonly ParametrageViewModel? _model;
        private readonly ArchiveVerificationReport? _report;
        private readonly bool _throws;

        private FakeParametrageQueries(ParametrageViewModel? model, ArchiveVerificationReport? report, bool throws)
        {
            _model = model;
            _report = report;
            _throws = throws;
        }

        public static FakeParametrageQueries Succeeding(ParametrageViewModel model, ArchiveVerificationReport? report = null) =>
            new(model, report, throws: false);

        public static FakeParametrageQueries Throwing() => new(null, null, throws: true);

        public Task<ParametrageViewModel> GetParametrageAsync(CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé d'assemblage du paramétrage.");
            }

            return Task.FromResult(_model!);
        }

        public Task<ArchiveVerificationReport> VerifyArchiveIntegrityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_report!);
    }
}
