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
    public async Task GetModelAsync_Returns_Accounts_And_Registered_Types_Sorted()
    {
        var account = SomeAccount();
        var sender = new RecordingSender { AccountsToReturn = [account] };
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("Zeta", "alpha", "B2Brouter"));

        var model = await service.GetModelAsync();

        model.Accounts.Should().ContainSingle().Which.Should().BeSameAs(account);
        model.RegisteredPluginTypes.Should().Equal("alpha", "B2Brouter", "Zeta");
        sender.Sent.Should().ContainSingle().Which.Should().BeOfType<GetPaAccountsQuery>();
    }

    [Fact]
    public async Task GetModelAsync_With_Empty_Registry_Returns_No_Plugin_Types()
    {
        var service = new PaAccountConsoleService(new RecordingSender(), new FakePaClientRegistry());

        var model = await service.GetModelAsync();

        model.RegisteredPluginTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_Sends_Add_Command_With_Entered_Fields_And_Key()
    {
        var sender = new RecordingSender { CreatedId = Guid.NewGuid() };
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("Fake"));

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
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("Fake"));

        await service.CreateAsync(new PaAccountFormModel { PluginType = "Fake", Environment = "Staging", ApiKey = "   " });

        sender.Sent.OfType<AddPaAccountCommand>().Single().ApiKey.Should().BeNull("une clé vide = aucune clé (à compléter ensuite)");
    }

    [Fact]
    public async Task UpdateAsync_Sends_Update_Command_With_Id_And_Rotates_When_Key_Provided()
    {
        var id = Guid.NewGuid();
        var sender = new RecordingSender();
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("Fake"));

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
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("Fake"));

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
        var service = new PaAccountConsoleService(new RecordingSender(), new FakePaClientRegistry("Fake"));

        var act = () => service.UpdateAsync(new PaAccountFormModel { Environment = "Staging" });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeactivateAsync_Sends_Deactivate_Command()
    {
        var id = Guid.NewGuid();
        var sender = new RecordingSender();
        var service = new PaAccountConsoleService(sender, new FakePaClientRegistry("Fake"));

        await service.DeactivateAsync(id);

        sender.Sent.OfType<DeactivatePaAccountCommand>().Should().ContainSingle()
            .Which.PaAccountId.Should().Be(id);
    }

    private static PaAccountDto SomeAccount() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        PluginType = "Fake",
        Environment = "Staging",
        AccountIdentifiers = "{}",
        HasApiKey = false,
        IsActive = true,
        CreatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private sealed class RecordingSender : ISender
    {
        public List<object> Sent { get; } = [];

        public IReadOnlyList<PaAccountDto> AccountsToReturn { get; set; } = Array.Empty<PaAccountDto>();

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
                GetPaAccountsQuery => AccountsToReturn,
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

        public FakePaClientRegistry(params string[] types) => _types = types;

        public IReadOnlyCollection<string> RegisteredTypes => _types;

        public IPaClient Resolve(PaAccountDescriptor account) => throw new NotSupportedException();

        public bool IsRegistered(string paType) => _types.Contains(paType, StringComparer.OrdinalIgnoreCase);
    }
}
