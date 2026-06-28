namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Profil;
using Liakont.Host.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests bUnit de la PAGE « Paramétrage › Profil légal » (BUG-15 ; règle de review n°19 : une page Blazor
/// sans test est un P1). Couvre l'ORCHESTRATION de la page (la vue pure est couverte par
/// <see cref="Components.ProfilViewTests"/>) : garde de permission, échec de chargement, profil absent, et
/// surtout le SWALLOW du rechargement après un enregistrement réussi (leçon WEB09 : un échec de reload ne
/// doit JAMAIS masquer une réussite). Aucune logique métier dans la page (déléguée au service, CLAUDE.md n°19).
/// </summary>
public sealed class ProfilPageTests : BunitContext
{
    [Fact]
    public void Denies_Access_Without_The_Settings_Permission_And_Loads_Nothing()
    {
        // Sans liakont.settings : la page n'instancie rien et ne charge AUCUNE donnée (la garde serveur reste
        // l'unique contrôle du chemin console).
        var service = new FakeProfilService(_ => Model());
        Services.AddAdminPageStubs();
        Services.AddScoped<IProfilConsoleService>(_ => service);

        var cut = Render<Profil>();

        cut.FindAll("[data-testid='profil-denied']").Should().ContainSingle();
        cut.FindAll("[data-testid='profil']").Should().BeEmpty();
        service.GetCalls.Should().Be(0, "aucune donnée n'est chargée sans la permission");
    }

    [Fact]
    public void Shows_An_Error_Banner_When_Loading_Throws()
    {
        var service = new FakeProfilService(_ => throw new InvalidOperationException("Échec simulé de chargement."));
        AddPageWithPermission(service);

        var cut = Render<Profil>();

        cut.FindAll("[data-testid='profil-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='profil']").Should().BeEmpty("l'échec n'expose aucune donnée");
    }

    [Fact]
    public void Shows_An_Absent_Notice_When_The_Tenant_Has_No_Profile_Yet()
    {
        var service = new FakeProfilService(_ => null);
        AddPageWithPermission(service);

        var cut = Render<Profil>();

        cut.FindAll("[data-testid='profil-absent']").Should().ContainSingle();
    }

    [Fact]
    public void A_Successful_Save_Keeps_The_Success_Message_Even_If_The_Reload_Throws()
    {
        // WEB09 : 1er GetAsync (chargement) OK, SaveAsync OK, 2e GetAsync (reload) LÈVE. Le reload est avalé
        // (loggé) — la réussite de l'enregistrement NE DOIT PAS être masquée par l'échec du rechargement.
        var service = new FakeProfilService(
            get: call => call == 0 ? Model() : throw new InvalidOperationException("Échec simulé du rechargement."),
            save: () => Task.CompletedTask);
        AddPageWithPermission(service);

        var cut = Render<Profil>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='profil-save-btn']").Should().ContainSingle());

        cut.Find("[data-testid='profil-save-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            var feedback = cut.Find("[data-testid='profil-feedback']");
            feedback.TextContent.Should().Contain("enregistré");
            feedback.GetAttribute("role").Should().Be("status", "une réussite n'est jamais rendue en alerte");
        });
        service.SaveCalls.Should().Be(1);
        service.GetCalls.Should().Be(2, "le reload a bien été tenté (puis avalé)");
    }

    [Fact]
    public void A_Successful_Save_Shows_The_Success_Message()
    {
        var service = new FakeProfilService(get: _ => Model(), save: () => Task.CompletedTask);
        AddPageWithPermission(service);

        var cut = Render<Profil>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='profil-save-btn']").Should().ContainSingle());

        cut.Find("[data-testid='profil-save-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            var feedback = cut.Find("[data-testid='profil-feedback']");
            feedback.TextContent.Should().Contain("enregistré");
            feedback.GetAttribute("role").Should().Be("status");
        });
    }

    private static ProfilViewModel Model() => new()
    {
        Siren = "123456782",
        Form = new ProfilFormModel
        {
            RaisonSociale = "Étude des Enchères",
            Street = "1 rue de l'Exemple",
            PostalCode = "35000",
            City = "Rennes",
            Country = "FR",
            ContactEmailAlerte = "alerte@exemple.fr",
        },
    };

    private void AddPageWithPermission(IProfilConsoleService service)
    {
        Services.AddAdminPageStubs(permissions: LiakontPermissions.Settings);
        Services.AddScoped(_ => service);
    }

    private sealed class FakeProfilService : IProfilConsoleService
    {
        private readonly Func<int, ProfilViewModel?> _get;
        private readonly Func<Task>? _save;

        public FakeProfilService(Func<int, ProfilViewModel?> get, Func<Task>? save = null)
        {
            _get = get;
            _save = save;
        }

        public int GetCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public Task<ProfilViewModel?> GetAsync(CancellationToken cancellationToken = default)
        {
            var call = GetCalls;
            GetCalls++;
            return Task.FromResult(_get(call));
        }

        public Task SaveAsync(ProfilInput input, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return _save?.Invoke() ?? Task.CompletedTask;
        }
    }
}
