namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Clients;
using Liakont.Host.Components.Pages;
using Liakont.Host.Security.Abstractions;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Assistant « Nouveau client » (OPS03 lot C) : validations de FORME de l'étape profil, exécution
/// de la création (sous-opérations idempotentes, ÉCHEC AFFICHÉ + réessayable — jamais de rollback
/// silencieux), skip explicite de l'étape utilisateur, clé d'agent affichée UNE fois, mot de passe
/// temporaire de l'utilisateur affiché UNE fois quand aucune invitation ne part, récapitulatif
/// honnête (les étapes sautées sont DITES sautées). 100 % français.
/// </summary>
public sealed class ClientNouveauTests : BunitContext
{
    private readonly FakeClientConsoleService _service = new();

    public ClientNouveauTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddScoped<IClientConsoleService>(_ => _service);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    }

    [Fact]
    public void An_Invalid_Tenant_Id_Blocks_The_Profil_Step_With_A_French_Message()
    {
        var cut = Render<ClientNouveau>();

        FillProfil(cut, tenantId: "ACME!!");
        cut.Find("[data-testid='client-wizard-profil-continue']").Click();

        cut.Find("[data-testid='client-wizard-profil-error']").TextContent.Should().Contain("minuscules");
        cut.FindAll("[data-testid='client-wizard-seed']").Should().BeEmpty("l'étape ne passe pas");
    }

    [Fact]
    public void An_Invalid_Siren_Form_Blocks_The_Profil_Step()
    {
        var cut = Render<ClientNouveau>();

        FillProfil(cut, siren: "12345");
        cut.Find("[data-testid='client-wizard-profil-continue']").Click();

        cut.Find("[data-testid='client-wizard-profil-error']").TextContent.Should().Contain("9 chiffres");
    }

    [Fact]
    public void The_Nominal_Path_Reaches_The_Recap_With_The_Agent_Key_Shown_Once()
    {
        var cut = Render<ClientNouveau>();

        // Étape 1 : profil valide → étape 2 : sans seed → création exécutée.
        FillProfil(cut);
        cut.Find("[data-testid='client-wizard-profil-continue']").Click();
        cut.Find("[data-testid='client-wizard-seed-continue']").Click();

        // Création : tenant + profil faits → continuer.
        _service.CreateCalls.Should().ContainSingle();
        _service.ProfileCalls.Should().ContainSingle("sans seed, le profil saisi est créé");
        cut.Find("[data-testid='client-wizard-creation-continue']").Click();

        // Étape utilisateur : SAUTÉE explicitement.
        cut.Find("[data-testid='client-wizard-utilisateur-skip']").Click();

        // Étape agent : enregistrement → clé affichée UNE fois.
        cut.Find("[data-testid='client-wizard-agent-name']").Input("poste-compta");
        cut.Find("[data-testid='client-wizard-agent-register']").Click();
        cut.Find("[data-testid='client-wizard-agent-key']").TextContent.Should().Be("pfx.full-key-secret");
        cut.Find("[data-testid='client-wizard-agent-continue']").Click();

        // Récapitulatif HONNÊTE : utilisateur sauté DIT sauté ; agent par son préfixe seulement.
        var recap = cut.Find("[data-testid='client-wizard-recap-text']").TextContent;
        recap.Should().Contain("SAUTÉE").And.Contain("pfx");
        recap.Should().NotContain("full-key-secret", "la clé complète n'apparaît plus après l'étape");
    }

    [Fact]
    public void A_Seed_Failure_Is_Shown_And_Retryable_Without_Replaying_The_Tenant_Creation()
    {
        _service.SeedDirectories = ["client-demo"];
        _service.SeedResult = new ClientSeedResult(ClientActionStatus.Conflict, "Seed invalide : SIREN divergent.");

        var cut = Render<ClientNouveau>();
        FillProfil(cut);
        cut.Find("[data-testid='client-wizard-profil-continue']").Click();

        // Étape 2 : avec seed.
        cut.Find("[data-testid='client-wizard-seed-use']").Change(true);
        cut.Find("[data-testid='client-wizard-seed-continue']").Click();

        // L'échec du seed est AFFICHÉ (message du domaine) — le tenant, lui, est créé.
        cut.Find("[data-testid='client-wizard-creation-error']").TextContent.Should().Contain("SIREN divergent");
        _service.CreateCalls.Should().ContainSingle();

        // Réessai : la création de tenant n'est PAS rejouée, seul le seed l'est.
        _service.SeedResult = new ClientSeedResult(ClientActionStatus.Succeeded);
        cut.Find("[data-testid='client-wizard-creation-retry']").Click();

        _service.CreateCalls.Should().ContainSingle("la sous-opération déjà réussie n'est pas rejouée");
        _service.SeedCalls.Should().HaveCount(2);
        cut.FindAll("[data-testid='client-wizard-creation-error']").Should().BeEmpty();
    }

    [Fact]
    public void The_User_Temporary_Password_Is_Shown_Once_When_No_Invitation_Could_Be_Sent()
    {
        _service.UserResult = new TenantUserProvisionResult
        {
            Success = true,
            UserId = Guid.NewGuid(),
            IdpUserId = "kc-1",
            InvitationEmailSent = false,
            TemporaryPassword = "mdp-temporaire-unique",
        };

        var cut = Render<ClientNouveau>();
        FillProfil(cut);
        cut.Find("[data-testid='client-wizard-profil-continue']").Click();
        cut.Find("[data-testid='client-wizard-seed-continue']").Click();
        cut.Find("[data-testid='client-wizard-creation-continue']").Click();

        cut.Find("[data-testid='client-wizard-utilisateur-username']").Input("jdupont");
        cut.Find("[data-testid='client-wizard-utilisateur-display-name']").Input("Jeanne Dupont");
        cut.Find("[data-testid='client-wizard-utilisateur-create']").Click();

        cut.Find("[data-testid='client-wizard-utilisateur-password-value']").TextContent
            .Should().Be("mdp-temporaire-unique");
    }

    private static void FillProfil(IRenderedComponent<ClientNouveau> cut, string tenantId = "acme", string siren = "123456782")
    {
        cut.Find("[data-testid='client-wizard-tenant-id']").Input(tenantId);
        cut.Find("[data-testid='client-wizard-raison-sociale']").Input("Acme SARL");
        cut.Find("[data-testid='client-wizard-email']").Input("contact@acme.test");
        cut.Find("[data-testid='client-wizard-siren']").Input(siren);
        cut.Find("[data-testid='client-wizard-street']").Input("1 rue de Test");
        cut.Find("[data-testid='client-wizard-postal-code']").Input("35000");
        cut.Find("[data-testid='client-wizard-city']").Input("Rennes");
    }

    private sealed class FakeClientConsoleService : IClientConsoleService
    {
        public IReadOnlyList<string> SeedDirectories { get; set; } = [];

        public ClientSeedResult SeedResult { get; set; } = new(ClientActionStatus.Succeeded);

        public TenantUserProvisionResult UserResult { get; set; } = new() { Success = true, UserId = Guid.NewGuid() };

        public List<string> CreateCalls { get; } = [];

        public List<string> SeedCalls { get; } = [];

        public List<ClientProfileInput> ProfileCalls { get; } = [];

        public Task<IReadOnlyList<ClientConsoleLine>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClientConsoleLine>>([]);

        public IReadOnlyList<string> ListSeedDirectories() => SeedDirectories;

        public Task<ClientCreationResult> CreateTenantAsync(string tenantId, string displayName, string adminEmail, CancellationToken cancellationToken = default)
        {
            CreateCalls.Add(tenantId);
            return Task.FromResult(new ClientCreationResult(ClientActionStatus.Succeeded));
        }

        public Task<ClientSeedResult> ImportSeedAsync(string tenantId, string seedDirectoryName, CancellationToken cancellationToken = default)
        {
            SeedCalls.Add(seedDirectoryName);
            return Task.FromResult(SeedResult);
        }

        public Task<ClientActionResult> SaveProfileAsync(string tenantId, ClientProfileInput profile, CancellationToken cancellationToken = default)
        {
            ProfileCalls.Add(profile);
            return Task.FromResult(new ClientActionResult(ClientActionStatus.Succeeded));
        }

        public Task<TenantUserProvisionResult> ProvisionFirstUserAsync(TenantUserProvisionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(UserResult);

        public Task<ClientAgentKeyResult> RegisterFirstAgentAsync(string tenantId, string agentName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ClientAgentKeyResult(
                ClientActionStatus.Succeeded,
                new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "pfx", FullKey = "pfx.full-key-secret" }));

        public Task<ClientActionResult> SetStatusAsync(string tenantId, bool suspendre, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
