namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.CountryReference;
using Liakont.Modules.Reference.Contracts.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

/// <summary>
/// Tests bUnit de la page « Paramétrage › Référentiel pays » (ADR-0038, Lot 4) : page RÉSERVÉE au paramétrage
/// (accès refusé sans <c>liakont.settings</c>), liste des correspondances bâtie sur DeclaredListPage, ajout /
/// modification (upsert) et suppression déléguées aux commandes MediatR via <c>ISender</c>. Le service console
/// et le dispatcher sont remplacés par des doubles : on prouve le WIRING page ↔ service ↔ commandes ↔ permission.
/// </summary>
public sealed class ReferentielPaysTests : BunitContext
{
    public ReferentielPaysTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Without_settings_permission_shows_denied_and_no_list_or_add_action()
    {
        Use(FakeConsoleService.Returning(Row("BEL", "BE")), canManage: false);

        var cut = Render<ReferentielPays>();

        cut.FindAll("[data-testid='referentiel-pays-denied']").Should().ContainSingle(
            "l'édition du référentiel est réservée au paramétrage (liakont.settings)");
        cut.FindAll("[data-testid='referentiel-pays-add-btn']").Should().BeEmpty();
        cut.Markup.Should().NotContain("BEL", "aucune donnée n'est chargée sans la permission");
    }

    [Fact]
    public void With_settings_renders_the_alias_rows()
    {
        Use(
            FakeConsoleService.Returning(Row("BEL", "BE"), Row("ENG", "GB"), Row("JAP", "JP")),
            canManage: true);

        var cut = Render<ReferentielPays>();

        cut.FindAll("[data-testid='referentiel-pays-denied']").Should().BeEmpty();
        cut.FindAll("[data-testid='referentiel-pays-add-btn']").Should().ContainSingle();

        cut.WaitForAssertion(
            () => cut.Markup.Should().Contain("BEL").And.Contain("ENG").And.Contain("JAP")
                .And.Contain("BE").And.Contain("GB").And.Contain("JP"),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Empty_referential_shows_the_explicit_empty_state()
    {
        Use(FakeConsoleService.Returning(), canManage: true);

        var cut = Render<ReferentielPays>();

        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='referentiel-pays-empty']").Should().ContainSingle(),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Adding_a_correspondence_sends_an_upsert_command_and_reloads()
    {
        var service = FakeConsoleService.Returning(Row("BEL", "BE"));
        var sender = Use(service, canManage: true);

        var cut = Render<ReferentielPays>();
        service.ListCalls.Should().Be(1);

        cut.Find("[data-testid='referentiel-pays-add-btn']").Click();
        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='referentiel-pays-source']").Should().ContainSingle(),
            TimeSpan.FromSeconds(5));

        cut.Find("[data-testid='referentiel-pays-source']").Input("bel");
        cut.Find("[data-testid='referentiel-pays-iso']").Input("BE");
        cut.Find("[data-testid='referentiel-pays-save']").Click();

        cut.WaitForAssertion(
            () =>
            {
                var command = sender.Sent.OfType<UpsertCountryAliasCommand>().Should().ContainSingle().Subject;
                command.SourceCode.Should().Be("bel");
                command.IsoCode.Should().Be("BE");
                service.ListCalls.Should().Be(2, "la liste est rechargée après l'upsert");
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Deleting_a_row_confirms_and_sends_a_remove_command_and_reloads()
    {
        var service = FakeConsoleService.Returning(Row("BEL", "BE"));
        var sender = Use(service, canManage: true);

        var cut = Render<ReferentielPays>();

        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='quick-action-delete']").Should().NotBeEmpty(),
            TimeSpan.FromSeconds(5));
        cut.FindAll("[data-testid='quick-action-delete']")[0].Click();

        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='referentiel-pays-remove-confirm']").Should().NotBeEmpty(),
            TimeSpan.FromSeconds(5));
        cut.Find("[data-testid='referentiel-pays-remove-confirm']").Click();

        cut.WaitForAssertion(
            () =>
            {
                var command = sender.Sent.OfType<RemoveCountryAliasCommand>().Should().ContainSingle().Subject;
                command.SourceCode.Should().Be("BEL");
                service.ListCalls.Should().Be(2, "la liste est rechargée après la suppression");
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Rejected_upsert_surfaces_the_iso_error_and_keeps_the_dialog_open()
    {
        var service = FakeConsoleService.Returning(Row("BEL", "BE"));
        var sender = Use(service, canManage: true);

        // Toast RÉEL capturé (instance partagée avec la page) : on prouve que le message opérateur FR est affiché.
        var toasts = new RecordingToastService();
        Services.AddScoped<IToastService>(_ => toasts);

        // Le handler d'upsert rejette une cible non-ISO avec un message opérateur FR (ADR-0038 §5) : la page doit
        // l'AFFICHER verbatim (toast d'erreur), LAISSER le dialogue ouvert pour correction, sans recharger la liste.
        const string isoError =
            "Le code pays cible « QQ » n'est pas un code ISO 3166-1 alpha-2 valide : saisissez un code officiel à 2 lettres (ex. « BE » pour la Belgique).";
        sender.ThrowOnSend = new InvalidOperationException(isoError);

        var cut = Render<ReferentielPays>();
        service.ListCalls.Should().Be(1);

        cut.Find("[data-testid='referentiel-pays-add-btn']").Click();
        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='referentiel-pays-source']").Should().ContainSingle(),
            TimeSpan.FromSeconds(5));

        cut.Find("[data-testid='referentiel-pays-source']").Input("ENG");
        cut.Find("[data-testid='referentiel-pays-iso']").Input("QQ");
        cut.Find("[data-testid='referentiel-pays-save']").Click();

        cut.WaitForAssertion(
            () =>
            {
                sender.Sent.OfType<UpsertCountryAliasCommand>().Should().ContainSingle();
                toasts.Shown.Should().Contain(
                    t => t.Severity == Severity.Error && t.Message == isoError,
                    "le message ISO du handler est affiché verbatim (message opérateur FR, CLAUDE.md n°12)");
                cut.FindAll("[data-testid='referentiel-pays-source']").Should().ContainSingle(
                    "le dialogue reste ouvert pour permettre la correction");
                service.ListCalls.Should().Be(1, "un upsert rejeté ne recharge pas la liste");
            },
            TimeSpan.FromSeconds(5));
    }

    private static CountryAliasRow Row(string source, string iso) => new()
    {
        SourceCode = source,
        IsoCode = iso,
        UpdatedAtUtc = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero),
    };

    private RecordingSender Use(FakeConsoleService service, bool canManage)
    {
        // Infra de test partagée des pages console (DeclaredListPage réel, localisation / acteur / préférences
        // stubbés). La permission est portée par le stub partagé ; on remplace juste le dispatcher par un double
        // qui enregistre les commandes envoyées, et on branche le service console de lecture.
        Services.AddAdminPageStubs(permissions: canManage ? ["liakont.settings"] : []);

        var sender = new RecordingSender();
        Services.AddScoped<ISender>(_ => sender);
        Services.AddScoped<ICountryAliasConsoleService>(_ => service);
        return sender;
    }

    private sealed class FakeConsoleService : ICountryAliasConsoleService
    {
        private readonly IReadOnlyList<CountryAliasRow> _rows;

        private FakeConsoleService(IReadOnlyList<CountryAliasRow> rows) => _rows = rows;

        public int ListCalls { get; private set; }

        public static FakeConsoleService Returning(params CountryAliasRow[] rows) => new(rows);

        public Task<IReadOnlyList<CountryAliasRow>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult(_rows);
        }
    }

    private sealed class RecordingSender : ISender
    {
        public List<object> Sent { get; } = [];

        /// <summary>Si non nul, le dispatch d'une commande (IRequest) lève — simule un rejet côté handler.</summary>
        public Exception? ThrowOnSend { get; set; }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Sent.Add(request!);
            if (ThrowOnSend is not null)
            {
                throw ThrowOnSend;
            }

            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult<TResponse>(default!);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Toast factice qui ENREGISTRE les notifications affichées (instance partagée avec la page).</summary>
    private sealed class RecordingToastService : IToastService
    {
        public event Action? OnToastsChanged;

        public List<ToastMessage> Shown { get; } = [];

        public IReadOnlyList<ToastMessage> GetActiveToasts() => Shown;

        public void Show(string message, Severity severity, int duration = 5000, bool dismissible = true)
        {
            Shown.Add(new ToastMessage(Guid.NewGuid(), message, severity, duration, dismissible));
            OnToastsChanged?.Invoke();
        }

        public void Dismiss(Guid id) => Shown.RemoveAll(t => t.Id == id);
    }
}
