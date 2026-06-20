namespace Liakont.Host.Tests.Unit.PaDelivery;

using System;
using System.Collections.Concurrent;
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
using Liakont.PaClients.ChorusPro;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Résolution d'un compte Chorus Pro (CP07) : le résolveur Host ouvre un scope tenant, résout la société, lit
/// les secrets CHIFFRÉS du compte ACTIF via <see cref="IPaAccountSecretStore"/>, déchiffre client_id /
/// client_secret PISTE + mot de passe du compte technique sous LEUR purpose dédié (via
/// <see cref="ISecretProtector"/> injecté EN SINGLETON), parse la configuration NON SENSIBLE
/// (URLs / login / e-mail / identifiant) depuis le champ opaque <c>account_identifiers</c>, et mappe
/// l'environnement PA → Chorus Pro. On BLOQUE plutôt que d'envoyer sans authentification (CLAUDE.md n°3) :
/// société absente, compte absent, secret manquant ou JSON incomplet/invalide → exception (FR).
/// </summary>
public sealed class ChorusProAccountResolverTests
{
    private const string ValidIdentifiers =
        """
        {
          "accountId": "ACC-1",
          "technicalLogin": "tech-login",
          "connectionEmail": "tech@example.test",
          "baseUrl": "https://sandbox-api.piste.gouv.fr/cpro/",
          "tokenEndpoint": "https://sandbox-oauth.piste.gouv.fr/api/oauth/token"
        }
        """;

    private static readonly Guid CompanyId = Guid.Parse("44444444-4444-4444-a444-444444444444");

    private static PaAccountDescriptor Descriptor() => new("ChorusPro", "tenant-1");

    private static PaAccountSecrets SecretsWith(
        PaEnvironment environment = PaEnvironment.Staging,
        string accountIdentifiers = ValidIdentifiers,
        string? clientId = "ENC:cid",
        string? clientSecret = "ENC:csecret",
        string? technicalPassword = "ENC:cpwd") =>
        new(environment, accountIdentifiers, EncryptedApiKey: null, clientId, clientSecret, technicalPassword);

    [Fact]
    public void Resolve_Reads_Active_Account_Decrypts_All_Secrets_And_Parses_Identifiers()
    {
        var protector = new FakeSecretProtector();
        var resolver = new ChorusProAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, SecretsWith())),
            protector);

        var config = resolver.Resolve(Descriptor());

        config.Environment.Should().Be(ChorusProEnvironment.Qualification, "Staging → Qualification");
        config.AccountId.Should().Be("ACC-1");
        config.PisteClientId.Should().Be("cid", "le client_id PISTE est déchiffré sous son purpose dédié");
        config.PisteClientSecret.Should().Be("csecret", "le client_secret PISTE est déchiffré sous son purpose dédié");
        config.TechnicalPassword.Should().Be("cpwd", "le mot de passe technique est déchiffré sous son purpose dédié");
        config.TechnicalLogin.Should().Be("tech-login");
        config.ConnectionEmail.Should().Be("tech@example.test");
        config.BaseUrl.Should().Be(new Uri("https://sandbox-api.piste.gouv.fr/cpro/", UriKind.Absolute));
        config.TokenEndpoint.Should().Be(new Uri("https://sandbox-oauth.piste.gouv.fr/api/oauth/token", UriKind.Absolute));

        // Chaque secret est déchiffré sous SON purpose (isolation cryptographique).
        protector.UnprotectPurposes.Should().Contain((PaAccountSecretPurposes.ClientId, "ENC:cid"));
        protector.UnprotectPurposes.Should().Contain((PaAccountSecretPurposes.ClientSecret, "ENC:csecret"));
        protector.UnprotectPurposes.Should().Contain((PaAccountSecretPurposes.TechnicalPassword, "ENC:cpwd"));
    }

    [Fact]
    public void Resolve_Maps_Production_Environment_To_Production()
    {
        var resolver = new ChorusProAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, SecretsWith(PaEnvironment.Production))),
            new FakeSecretProtector());

        var config = resolver.Resolve(Descriptor());

        config.Environment.Should().Be(ChorusProEnvironment.Production, "Production → Production");
    }

    [Fact]
    public void Resolve_Throws_When_No_Company_Profile()
    {
        var resolver = new ChorusProAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(companyId: null, secrets: null)),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage("*tenant*");
    }

    [Fact]
    public void Resolve_Throws_When_No_Active_Account()
    {
        var resolver = new ChorusProAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, secrets: null)),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage("*compte Chorus Pro actif*");
    }

    [Theory]
    [InlineData(null, "ENC:csecret", "ENC:cpwd")]
    [InlineData("ENC:cid", null, "ENC:cpwd")]
    [InlineData("ENC:cid", "ENC:csecret", null)]
    public void Resolve_Throws_When_A_Secret_Is_Missing(string? clientId, string? clientSecret, string? technicalPassword)
    {
        var secrets = SecretsWith(clientId: clientId, clientSecret: clientSecret, technicalPassword: technicalPassword);
        var resolver = new ChorusProAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, secrets)),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage("*incomplet*");
    }

    [Fact]
    public void Resolve_Throws_When_Account_Identifiers_Blank()
    {
        var resolver = new ChorusProAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, SecretsWith(accountIdentifiers: "  "))),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage("*incomplet*");
    }

    [Fact]
    public void Resolve_Throws_When_Account_Identifiers_Not_Valid_Json()
    {
        var resolver = new ChorusProAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, SecretsWith(accountIdentifiers: "not-json"))),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage("*JSON valide*");
    }

    [Theory]
    [InlineData(ChorusProAccountResolver.AccountIdKey)]
    [InlineData(ChorusProAccountResolver.TechnicalLoginKey)]
    [InlineData(ChorusProAccountResolver.ConnectionEmailKey)]
    [InlineData(ChorusProAccountResolver.BaseUrlKey)]
    [InlineData(ChorusProAccountResolver.TokenEndpointKey)]
    public void Resolve_Throws_When_A_Required_Identifier_Is_Missing(string missingKey)
    {
        var identifiers = $$"""
        {
          "accountId": "ACC-1",
          "technicalLogin": "tech-login",
          "connectionEmail": "tech@example.test",
          "baseUrl": "https://sandbox-api.piste.gouv.fr/cpro/",
          "tokenEndpoint": "https://sandbox-oauth.piste.gouv.fr/api/oauth/token"
        }
        """.Replace($"\"{missingKey}\"", "\"_removed_\"", StringComparison.Ordinal);

        var resolver = new ChorusProAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, SecretsWith(accountIdentifiers: identifiers))),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{missingKey}*");
    }

    [Fact]
    public void Resolve_Throws_When_A_Url_Is_Not_Absolute()
    {
        var identifiers = """
        {
          "accountId": "ACC-1",
          "technicalLogin": "tech-login",
          "connectionEmail": "tech@example.test",
          "baseUrl": "/cpro/",
          "tokenEndpoint": "https://sandbox-oauth.piste.gouv.fr/api/oauth/token"
        }
        """;
        var resolver = new ChorusProAccountResolver(
            new FakeTenantScopeFactory(BuildScopeServices(CompanyId, SecretsWith(accountIdentifiers: identifiers))),
            new FakeSecretProtector());

        var act = () => resolver.Resolve(Descriptor());

        act.Should().Throw<InvalidOperationException>().WithMessage("*baseUrl*");
    }

    [Fact]
    public void Resolve_DoesNotDeadlock_Under_SingleThreaded_SynchronizationContext()
    {
        // Garde anti-régression : ce résolveur est appelé au RENDU UI (description du compte) sous le
        // SynchronizationContext mono-thread du circuit Blazor Server. Le Task.Run offload la résolution hors
        // du contexte → pas de deadlock (même analyse que SuperPdpAccountResolver).
        var resolver = new ChorusProAccountResolver(
            new YieldingTenantScopeFactory(BuildScopeServices(CompanyId, SecretsWith())),
            new FakeSecretProtector());

        using var ctx = new SingleThreadSynchronizationContext();
        ChorusProAccountConfig? config = null;
        Exception? error = null;
        using var done = new ManualResetEventSlim();

        ctx.Post(
            _ =>
            {
                try
                {
                    config = resolver.Resolve(Descriptor());
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            },
            null);

        done.Wait(TimeSpan.FromSeconds(10)).Should()
            .BeTrue("Resolve ne doit pas deadlocker sous un SynchronizationContext mono-thread (circuit Blazor)");
        error.Should().BeNull();
        config!.PisteClientId.Should().Be("cid");
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

    /// <summary>
    /// Scope tenant dont le <c>DisposeAsync</c> CÈDE réellement (<c>await Task.Yield()</c>) : reproduit la
    /// capture du <see cref="SynchronizationContext"/> par le <c>await using</c> de <c>ResolveAsync</c>.
    /// </summary>
    private sealed class YieldingTenantScopeFactory : ITenantScopeFactory
    {
        private readonly IServiceProvider _services;

        public YieldingTenantScopeFactory(IServiceProvider services) => _services = services;

        public ITenantScope Create(string tenantId) => new YieldingTenantScope(tenantId, _services);
    }

    private sealed class YieldingTenantScope : ITenantScope
    {
        public YieldingTenantScope(string tenantId, IServiceProvider services)
        {
            TenantId = tenantId;
            Services = services;
        }

        public string TenantId { get; }

        public IServiceProvider Services { get; }

        public async ValueTask DisposeAsync() => await Task.Yield();
    }

    /// <summary>
    /// <see cref="SynchronizationContext"/> mono-thread avec pompe de messages — modélise le circuit Blazor
    /// Server : une continuation postée pendant qu'un appelant bloque le thread ne s'exécute jamais (deadlock).
    /// </summary>
    private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public SingleThreadSynchronizationContext()
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "test-blazor-circuit" };
            _thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }

        private void Pump()
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }
}
