namespace Liakont.Host.Tests.Unit.PaAccounts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.PaAccounts;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using MediatR;
using Xunit;

/// <summary>
/// Tests unitaires de <see cref="PaAccountConsoleService"/> (FIX01c) : assemblage lecture (comptes +
/// types de plug-ins triés depuis le registre) et pass-through des mutations vers les commandes
/// TenantSettings — la clé saisie est transmise telle quelle (chiffrée par le handler), une clé vide
/// devient <c>null</c> (création = aucune clé / édition = clé inchangée), et la mise à jour exige un id.
/// </summary>
public sealed class PaAccountConsoleServiceTests
{
    [Fact]
    public async Task GetModelAsync_Returns_Overview_Accounts_With_Capabilities_And_Registered_Types_Sorted()
    {
        // La lecture passe par la vue d'ensemble TenantSettings (comptes AVEC capacités résolues —
        // lot polish UX/UI : le détail des capacités s'affiche sur la page Comptes PA), pas par une
        // query dédiée : aucune requête MediatR n'est envoyée au chargement.
        var entry = SomeAccountSettings();
        var sender = new RecordingSender();
        var service = new PaAccountConsoleService(
            sender, new FakePaClientRegistry("Zeta", "alpha", "B2Brouter"), new FakeSettingsQueries(entry));

        var model = await service.GetModelAsync();

        model.Accounts.Should().ContainSingle().Which.Should().BeSameAs(entry);
        model.RegisteredPluginTypes.Should().Equal("alpha", "B2Brouter", "Zeta");
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task GetModelAsync_With_Empty_Registry_Returns_No_Plugin_Types()
    {
        var service = new PaAccountConsoleService(new RecordingSender(), new FakePaClientRegistry(), new FakeSettingsQueries());

        var model = await service.GetModelAsync();

        model.RegisteredPluginTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetModelAsync_Fills_AuthModes_From_The_Registry()
    {
        // Le registre déclare un type OAuth2 (jamais un if (type==...)) : le modèle porte ce mode pour que
        // la page présente client_id/client_secret au lieu d'une clé API (slice 4).
        var registry = new FakePaClientRegistry(
            new Dictionary<string, PaAuthMode>(StringComparer.OrdinalIgnoreCase)
            {
                ["OAuthPa"] = PaAuthMode.OAuth2ClientCredentials,
                ["KeyPa"] = PaAuthMode.ApiKey,
            });
        var service = new PaAccountConsoleService(new RecordingSender(), registry, new FakeSettingsQueries());

        var model = await service.GetModelAsync();

        model.AuthModes.Should().ContainKey("OAuthPa").WhoseValue.Should().Be(PaAuthMode.OAuth2ClientCredentials);
        model.AuthModes.Should().ContainKey("KeyPa").WhoseValue.Should().Be(PaAuthMode.ApiKey);
    }

    [Fact]
    public async Task CreateAsync_Sends_Add_Command_With_OAuth_Client_Id_And_Secret()
    {
        var sender = new RecordingSender { CreatedId = Guid.NewGuid() };
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("OAuthPa"), new FakeSettingsQueries());

        await service.CreateAsync(new PaAccountFormModel
        {
            PluginType = "OAuthPa",
            Environment = "Staging",
            AccountIdentifiers = "acct-1",
            ClientId = "the-client-id",
            ClientSecret = "the-client-secret",
        });

        var command = sender.Sent.OfType<AddPaAccountCommand>().Should().ContainSingle().Subject;
        command.ClientId.Should().Be("the-client-id", "le client_id saisi est transmis (chiffré par le handler)");
        command.ClientSecret.Should().Be("the-client-secret");
    }

    [Fact]
    public async Task CreateAsync_With_Blank_OAuth_Secrets_Sends_Null()
    {
        var sender = new RecordingSender { CreatedId = Guid.NewGuid() };
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("OAuthPa"), new FakeSettingsQueries());

        await service.CreateAsync(new PaAccountFormModel
        {
            PluginType = "OAuthPa",
            Environment = "Staging",
            AccountIdentifiers = "acct-1",
            ClientId = "   ",
            ClientSecret = null,
        });

        var command = sender.Sent.OfType<AddPaAccountCommand>().Single();
        command.ClientId.Should().BeNull("un client_id vide = non saisi");
        command.ClientSecret.Should().BeNull("un client_secret null = non saisi");
    }

    [Fact]
    public async Task UpdateAsync_Sends_Update_Command_With_OAuth_Client_Id_And_Secret()
    {
        var id = Guid.NewGuid();
        var sender = new RecordingSender();
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("OAuthPa"), new FakeSettingsQueries());

        await service.UpdateAsync(new PaAccountFormModel
        {
            PaAccountId = id,
            Environment = "Production",
            AccountIdentifiers = "acct-1",
            ClientId = "rotated-id",
            ClientSecret = "rotated-secret",
        });

        var command = sender.Sent.OfType<UpdatePaAccountCommand>().Should().ContainSingle().Subject;
        command.PaAccountId.Should().Be(id);
        command.ClientId.Should().Be("rotated-id");
        command.ClientSecret.Should().Be("rotated-secret");
    }

    [Fact]
    public async Task CreateAsync_Sends_Add_Command_With_Entered_Fields_And_Key()
    {
        var sender = new RecordingSender { CreatedId = Guid.NewGuid() };
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("Fake"), new FakeSettingsQueries());

        var id = await service.CreateAsync(new PaAccountFormModel
        {
            PluginType = "Fake",
            Environment = "Staging",
            AccountIdentifiers = "{ \"accountId\": \"X\" }",
            ApiKey = "secret-key",
        });

        id.Should().Be(sender.CreatedId);
        var command = sender.Sent.OfType<AddPaAccountCommand>().Should().ContainSingle().Subject;
        command.PluginType.Should().Be("Fake");
        command.Environment.Should().Be("Staging");
        command.AccountIdentifiers.Should().Be("{ \"accountId\": \"X\" }");
        command.ApiKey.Should().Be("secret-key", "la clé saisie est transmise telle quelle (chiffrée par le handler)");
    }

    [Fact]
    public async Task CreateAsync_With_Blank_Key_Sends_Null_Key()
    {
        var sender = new RecordingSender();
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("Fake"), new FakeSettingsQueries());

        await service.CreateAsync(new PaAccountFormModel { PluginType = "Fake", Environment = "Staging", ApiKey = "   " });

        sender.Sent.OfType<AddPaAccountCommand>().Single().ApiKey.Should().BeNull("une clé vide = aucune clé (à compléter ensuite)");
    }

    [Fact]
    public async Task UpdateAsync_Sends_Update_Command_With_Id_And_Rotates_When_Key_Provided()
    {
        var id = Guid.NewGuid();
        var sender = new RecordingSender();
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("Fake"), new FakeSettingsQueries());

        await service.UpdateAsync(new PaAccountFormModel
        {
            PaAccountId = id,
            PluginType = "Fake",
            Environment = "Production",
            AccountIdentifiers = "{}",
            ApiKey = "rotated",
        });

        var command = sender.Sent.OfType<UpdatePaAccountCommand>().Should().ContainSingle().Subject;
        command.PaAccountId.Should().Be(id);
        command.Environment.Should().Be("Production");
        command.ApiKey.Should().Be("rotated");
    }

    [Fact]
    public async Task UpdateAsync_With_Blank_Key_Leaves_Key_Unchanged()
    {
        var sender = new RecordingSender();
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("Fake"), new FakeSettingsQueries());

        await service.UpdateAsync(new PaAccountFormModel
        {
            PaAccountId = Guid.NewGuid(),
            Environment = "Staging",
            ApiKey = "  ",
        });

        sender.Sent.OfType<UpdatePaAccountCommand>().Single().ApiKey.Should().BeNull("une clé vide = clé inchangée");
    }

    [Fact]
    public async Task UpdateAsync_Without_Id_Throws()
    {
        var service = new PaAccountConsoleService(new RecordingSender(), new FakePaClientRegistry("Fake"), new FakeSettingsQueries());

        var act = () => service.UpdateAsync(new PaAccountFormModel { Environment = "Staging" });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeactivateAsync_Sends_Deactivate_Command()
    {
        var id = Guid.NewGuid();
        var sender = new RecordingSender();
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("Fake"), new FakeSettingsQueries());

        await service.DeactivateAsync(id);

        sender.Sent.OfType<DeactivatePaAccountCommand>().Should().ContainSingle()
            .Which.PaAccountId.Should().Be(id);
    }

    private static PaAccountSettingsDto SomeAccountSettings() => new()
    {
        Account = new PaAccountDto
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            PluginType = "Fake",
            Environment = "Staging",
            AccountIdentifiers = "{}",
            HasApiKey = false,
            IsActive = true,
            CreatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        },
        PluginAvailable = false,
        Capabilities = null,
    };

    private sealed class FakeSettingsQueries : ITenantSettingsConsoleQueries
    {
        private readonly IReadOnlyList<PaAccountSettingsDto> _accounts;

        public FakeSettingsQueries(params PaAccountSettingsDto[] accounts) => _accounts = accounts;

        public Task<TenantSettingsOverviewDto> GetSettingsOverview(CancellationToken ct = default) =>
            Task.FromResult(new TenantSettingsOverviewDto
            {
                Profile = null,
                FiscalSettings = null,
                TvaMapping = null,
                PaAccounts = _accounts,
            });
    }

    private sealed class RecordingSender : ISender
    {
        public List<object> Sent { get; } = [];

        public Guid CreatedId { get; set; } = Guid.NewGuid();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Sent.Add(request!);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            object response = request switch
            {
                AddPaAccountCommand => CreatedId,
                _ => throw new NotSupportedException(request.GetType().Name),
            };

            return Task.FromResult((TResponse)response);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePaClientRegistry : IPaClientRegistry
    {
        private readonly string[] _types;
        private readonly IReadOnlyDictionary<string, PaAuthMode>? _authModes;

        public FakePaClientRegistry(params string[] types) => _types = types;

        public FakePaClientRegistry(IReadOnlyDictionary<string, PaAuthMode> authModes)
        {
            _authModes = authModes;
            _types = authModes.Keys.ToArray();
        }

        public IReadOnlyCollection<string> RegisteredTypes => _types;

        public IPaClient Resolve(PaAccountDescriptor account) => throw new NotSupportedException();

        public bool IsRegistered(string paType) => _types.Contains(paType, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, PaAuthMode> DescribeAuthModes() =>
            _authModes ?? new Dictionary<string, PaAuthMode>(StringComparer.OrdinalIgnoreCase);
    }
}
