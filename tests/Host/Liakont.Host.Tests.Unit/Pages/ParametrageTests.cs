namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Parametrage;
using Liakont.Host.Security;
using Liakont.Modules.Archive.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Security;
using Xunit;

public sealed class ParametrageTests : BunitContext
{
    public ParametrageTests()
    {
        // RadzenButton (StratumButton) dans la vue imbriquée appelle du JS : mode permissif.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();

        // Permission par défaut : aucune (les tests qui exercent les exports en réenregistrent une).
        Services.AddScoped<IPermissionService>(_ => new StubPermissionService());
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

    [Fact]
    public void Should_Show_Integrity_Error_Banner_When_Verify_Throws()
    {
        Services.AddScoped<IParametrageQueries>(_ => FakeParametrageQueries.SucceedingLoadThrowingVerify(BuildModel()));

        var cut = Render<Parametrage>();
        cut.Find("[data-testid='parametrage-integrite-btn']").Click();

        // L'erreur de vérification doit apparaître et le rapport ne doit pas s'afficher.
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='parametrage-integrite-error']").Should().ContainSingle());
        cut.FindAll("[data-testid='parametrage-integrite-report']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Offer_Both_Archive_Exports_To_A_Settings_Operator()
    {
        Services.AddScoped<IPermissionService>(_ =>
            new StubPermissionService(LiakontPermissions.Read, LiakontPermissions.Settings));
        Services.AddScoped<IParametrageQueries>(_ => FakeParametrageQueries.Succeeding(BuildModel()));

        var cut = Render<Parametrage>();

        cut.FindAll("[data-testid='parametrage-audit-export']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-tenant-export']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-tenant-export-btn']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Offer_Audit_Export_But_Hide_Reversibility_To_A_Reader()
    {
        Services.AddScoped<IPermissionService>(_ => new StubPermissionService(LiakontPermissions.Read));
        Services.AddScoped<IParametrageQueries>(_ => FakeParametrageQueries.Succeeding(BuildModel()));

        var cut = Render<Parametrage>();

        cut.FindAll("[data-testid='parametrage-audit-export']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-tenant-export']").Should().BeEmpty();
    }

    private static ParametrageViewModel BuildModel() => new()
    {
        Profile = null,
        FiscalSettings = null,
        TvaMapping = null,
        PaAccounts = [],
        Agents = [],
    };

    private sealed class StubPermissionService : IPermissionService
    {
        private readonly HashSet<string> _granted;

        public StubPermissionService(params string[] granted) =>
            _granted = new HashSet<string>(granted, StringComparer.Ordinal);

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => _granted.Contains(permission);
    }

    private sealed class FakeParametrageQueries : IParametrageQueries
    {
        private readonly ParametrageViewModel? _model;
        private readonly ArchiveVerificationReport? _report;
        private readonly bool _throws;
        private readonly bool _throwsOnVerify;

        private FakeParametrageQueries(ParametrageViewModel? model, ArchiveVerificationReport? report, bool throws, bool throwsOnVerify = false)
        {
            _model = model;
            _report = report;
            _throws = throws;
            _throwsOnVerify = throwsOnVerify;
        }

        public static FakeParametrageQueries Succeeding(ParametrageViewModel model, ArchiveVerificationReport? report = null) =>
            new(model, report, throws: false);

        public static FakeParametrageQueries Throwing() => new(null, null, throws: true);

        public static FakeParametrageQueries SucceedingLoadThrowingVerify(ParametrageViewModel model) =>
            new(model, report: null, throws: false, throwsOnVerify: true);

        public Task<ParametrageViewModel> GetParametrageAsync(CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé d'assemblage du paramétrage.");
            }

            return Task.FromResult(_model!);
        }

        public Task<ArchiveVerificationReport> VerifyArchiveIntegrityAsync(CancellationToken cancellationToken = default)
        {
            if (_throwsOnVerify)
            {
                throw new InvalidOperationException("Échec simulé de vérification d'intégrité.");
            }

            return Task.FromResult(_report!);
        }
    }
}
