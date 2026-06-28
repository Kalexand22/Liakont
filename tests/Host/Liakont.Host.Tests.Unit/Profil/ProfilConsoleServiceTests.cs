namespace Liakont.Host.Tests.Unit.Profil;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Profil;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// <see cref="ProfilConsoleService"/> (BUG-15) : pré-remplissage depuis <c>GetTenantProfileQuery</c>, et
/// enregistrement via <c>SaveTenantProfileCommand</c> avec le SIREN REPRIS du profil persisté (immuable,
/// INV-TENANTSETTINGS-001 — jamais saisi côté client). Tenant-scopé (la société est résolue côté handler).
/// </summary>
public sealed class ProfilConsoleServiceTests
{
    [Fact]
    public async Task GetAsync_Prefills_From_The_Current_Profile_With_Siren_Read_Only()
    {
        var sender = new FakeSender { Profile = BuildProfile() };
        var service = new ProfilConsoleService(sender);

        var model = await service.GetAsync();

        model.Should().NotBeNull();
        model!.Siren.Should().Be("123456782");
        model.Form.RaisonSociale.Should().Be("Étude des Enchères");
        model.Form.Street.Should().Be("1 rue de l'Exemple");
        model.Form.PostalCode.Should().Be("35000");
        model.Form.City.Should().Be("Rennes");
        model.Form.Country.Should().Be("FR");
        model.Form.ContactEmailAlerte.Should().Be("alerte@exemple.fr");
    }

    [Fact]
    public async Task GetAsync_Returns_Null_When_No_Profile_Yet()
    {
        var service = new ProfilConsoleService(new FakeSender { Profile = null });

        (await service.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_Reuses_The_Persisted_Immutable_Siren_And_Maps_The_Form()
    {
        var sender = new FakeSender { Profile = BuildProfile() };
        var service = new ProfilConsoleService(sender);

        await service.SaveAsync(new ProfilInput
        {
            RaisonSociale = "Étude des Enchères (corrigée)",
            Street = "2 avenue des Ventes",
            PostalCode = "75009",
            City = "Paris",
            Country = "FR",
            ContactEmailAlerte = "  nouveau@exemple.fr  ",
        });

        var command = sender.Sent.OfType<SaveTenantProfileCommand>().Should().ContainSingle().Subject;

        // Le SIREN provient du profil PERSISTÉ, jamais d'une saisie : ce chemin ne peut pas le changer.
        command.Siren.Should().Be("123456782");
        command.RaisonSociale.Should().Be("Étude des Enchères (corrigée)");
        command.Street.Should().Be("2 avenue des Ventes");
        command.PostalCode.Should().Be("75009");
        command.City.Should().Be("Paris");
        command.Country.Should().Be("FR");
        command.ContactEmailAlerte.Should().Be("nouveau@exemple.fr");
    }

    [Fact]
    public async Task SaveAsync_Normalizes_A_Blank_Contact_Email_To_Null()
    {
        var sender = new FakeSender { Profile = BuildProfile() };
        var service = new ProfilConsoleService(sender);

        await service.SaveAsync(new ProfilInput
        {
            RaisonSociale = "Étude des Enchères",
            Street = "1 rue de l'Exemple",
            PostalCode = "35000",
            City = "Rennes",
            Country = "FR",
            ContactEmailAlerte = "   ",
        });

        var command = sender.Sent.OfType<SaveTenantProfileCommand>().Should().ContainSingle().Subject;
        command.ContactEmailAlerte.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_When_No_Profile_Exists_Is_Refused_Without_Emitting_A_Command()
    {
        var sender = new FakeSender { Profile = null };
        var service = new ProfilConsoleService(sender);

        var act = async () => await service.SaveAsync(new ProfilInput
        {
            RaisonSociale = "X",
            Street = "X",
            PostalCode = "X",
            City = "X",
            Country = "FR",
        });

        await act.Should().ThrowAsync<ConflictException>();
        sender.Sent.OfType<SaveTenantProfileCommand>().Should().BeEmpty();
    }

    private static TenantProfileDto BuildProfile() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Siren = "123456782",
        RaisonSociale = "Étude des Enchères",
        Street = "1 rue de l'Exemple",
        PostalCode = "35000",
        City = "Rennes",
        Country = "FR",
        ContactEmailAlerte = "alerte@exemple.fr",
        Statut = "Actif",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeSender : ISender
    {
        public List<object> Sent { get; } = [];

        public TenantProfileDto? Profile { get; init; }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Sent.Add(request!);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            if (request is GetTenantProfileQuery)
            {
                return Task.FromResult((TResponse)(object?)Profile!);
            }

            return Task.FromResult(default(TResponse)!);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
