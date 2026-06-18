namespace Liakont.Host.Tests.Unit.PaDelivery;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.PaDelivery;
using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.SuperPdp;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Résolution d'un compte Super PDP (slice 3) : le résolveur Host ouvre un scope tenant, résout la société,
/// lit les secrets CHIFFRÉS du compte ACTIF via <see cref="IPaAccountSecretStore"/>, déchiffre client_id /
/// client_secret sous leurs purposes dédiés (via <see cref="ISecretProtector"/> injecté EN SINGLETON dans le
/// résolveur — pas via le scope), et mappe l'environnement PA → Super PDP. On BLOQUE plutôt que d'envoyer sans
/// authentification (CLAUDE.md n°3) : société absente, compte absent ou secret manquant → exception (FR).
/// </summary>
public sealed class SuperPdpAccountResolverTests
{
    private static readonly Guid CompanyId = Guid.Parse("33333333-3333-4333-a333-333333333333");

    private static PaAccountDescriptor Descriptor() => new("SuperPdp", "tenant-1");

    [Fact]
    public void Resolve_Reads_The_Active_Account_And_Decrypts_Client_Id_And_Secret()
    {
        var protector = new FakeSecretProtector();
        var secrets = new PaAccountSecrets(
            PaEnvironment.Staging,
            AccountIdentifiers: "acct-1",
            EncryptedApiKey: null,
            EncryptedClientId: "ENC:cid",
            EncryptedClientSecret: "ENC:csecret");
        var resolver = new SuperPdpAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, secrets)),
            protector);

        var config = resolver.Resolve(Descriptor());

        config.Environment.Should().Be(SuperPdpEnvironment.Sandbox, "Staging → Sandbox");
        config.AccountId.Should().Be("acct-1");
        config.ClientId.Should().Be("cid", "le client_id est déchiffré sous son purpose dédié");
        config.ClientSecret.Should().Be("csecret", "le client_secret est déchiffré sous son purpose dédié");

        // Chaque secret est déchiffré sous SON purpose (isolation cryptographique).
        protector.UnprotectPurposes.Should().Contain(
            (PaAccountSecretPurposes.ClientId, "ENC:cid"));
        protector.UnprotectPurposes.Should().Contain(
            (PaAccountSecretPurposes.ClientSecret, "ENC:csecret"));
    }

    [Fact]
    public void Resolve_Maps_Production_Environment_To_Production()
    {
        var secrets = new PaAccountSecrets(
            PaEnvironment.Production, "acct-1", null, "ENC:cid", "ENC:csecret");
        var resolver = new SuperPdpAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, secrets)),
            new FakeSecretProtector());

        var config = resolver.Resolve(Descriptor());

        config.Environment.Should().Be(SuperPdpEnvironment.Production, "Production → Production");
    }

    [Fact]
    public void Resolve_Throws_When_No_Company_Profile()
    {
        var resolver = new SuperPdpAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(companyId: null, secrets: null)),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage("*tenant*");
    }

    [Fact]
    public void Resolve_Throws_When_No_Active_Account()
    {
        var resolver = new SuperPdpAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, secrets: null)),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage("*compte Super PDP actif*");
    }

    [Fact]
    public void Resolve_Throws_When_Encrypted_Client_Id_Missing()
    {
        var secrets = new PaAccountSecrets(
            PaEnvironment.Staging, "acct-1", null, EncryptedClientId: null, EncryptedClientSecret: "ENC:csecret");
        var resolver = new SuperPdpAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, secrets)),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage("*incomplet*");
    }

    [Fact]
    public void Resolve_Throws_When_Encrypted_Client_Secret_Missing()
    {
        var secrets = new PaAccountSecrets(
            PaEnvironment.Staging, "acct-1", null, EncryptedClientId: "ENC:cid", EncryptedClientSecret: null);
        var resolver = new SuperPdpAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, secrets)),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage("*incomplet*");
    }

    [Fact]
    public void Resolve_Throws_When_Account_Identifiers_Blank()
    {
        var secrets = new PaAccountSecrets(
            PaEnvironment.Staging, AccountIdentifiers: "  ", null, "ENC:cid", "ENC:csecret");
        var resolver = new SuperPdpAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, secrets)),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage("*incomplet*");
    }

    private static ServiceProvider BuildScopeServices(Guid? companyId, PaAccountSecrets? secrets) =>
        new ServiceCollection()
            .AddScoped<ITenantSettingsQueries>(_ => new FakeSettingsQueries(companyId))
            .AddScoped<IPaAccountSecretStore>(_ => new FakeSecretStore(secrets))
            .BuildServiceProvider();

    /// <summary>Coffre FACTICE : « ENC:&lt;clair&gt; » ⇄ « &lt;clair&gt; », enregistre (purpose, chiffré) déprotégé.</summary>
    private sealed class FakeSecretProtector : ISecretProtector
    {
        public List<(string Purpose, string Protected)> UnprotectPurposes { get; } = [];

        public string Protect(string plaintext) => "ENC:" + plaintext;

        public string Protect(string plaintext, string purpose) => "ENC:" + plaintext;

        public string Unprotect(string protectedValue) => Strip(protectedValue);

        public string Unprotect(string protectedValue, string purpose)
        {
            UnprotectPurposes.Add((purpose, protectedValue));
            return Strip(protectedValue);
        }

        private static string Strip(string protectedValue) =>
            protectedValue.StartsWith("ENC:", StringComparison.Ordinal)
                ? protectedValue["ENC:".Length..]
                : protectedValue;
    }

    private sealed class FakeTenantScopeFactory : ITenantScopeFactory
    {
        private readonly IServiceProvider _services;

        public FakeTenantScopeFactory(IServiceProvider services) => _services = services;

        public ITenantScope Create(string tenantId) => new FakeTenantScope(tenantId, _services);
    }

    private sealed class FakeTenantScope : ITenantScope
    {
        public FakeTenantScope(string tenantId, IServiceProvider services)
        {
            TenantId = tenantId;
            Services = services;
        }

        public string TenantId { get; }

        public IServiceProvider Services { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSecretStore : IPaAccountSecretStore
    {
        private readonly PaAccountSecrets? _secrets;

        public FakeSecretStore(PaAccountSecrets? secrets) => _secrets = secrets;

        public Task<PaAccountSecrets?> GetActiveAsync(Guid companyId, string pluginType, CancellationToken ct = default) =>
            Task.FromResult(_secrets);
    }

    private sealed class FakeSettingsQueries : ITenantSettingsQueries
    {
        private readonly Guid? _currentCompanyId;

        public FakeSettingsQueries(Guid? currentCompanyId) => _currentCompanyId = currentCompanyId;

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult(_currentCompanyId);

        public Task<string?> GetCurrentTenantStatut(CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
