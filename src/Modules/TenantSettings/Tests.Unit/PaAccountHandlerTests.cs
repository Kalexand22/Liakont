namespace Liakont.Modules.TenantSettings.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Liakont.Modules.TenantSettings.Infrastructure;
using Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.DataIsolation;
using Xunit;

/// <summary>
/// Tests unitaires des handlers Add/Update d'un compte PA (slice 2) : chaque secret OAuth2 (client_id /
/// client_secret) est chiffré sous SON purpose dédié ; null/vide n'est PAS chiffré (et, en update, laisse
/// la valeur inchangée). Le journal porte des FLAGS BOOLÉENS (Has*/{*}Rotated), jamais le secret en clair
/// (CLAUDE.md n°10).
/// </summary>
public sealed class PaAccountHandlerTests
{
    private static readonly Guid CompanyId = Guid.Parse("44444444-4444-4444-a444-444444444444");

    [Fact]
    public async Task Add_Encrypts_Client_Id_And_Secret_Under_Their_Dedicated_Purposes()
    {
        var protector = new RecordingSecretProtector();
        var uow = new FakePaAccountUnitOfWork();
        var journal = new RecordingJournal();
        var handler = new AddPaAccountHandler(new FakeUowFactory(uow), new FakeCompanyFilter(CompanyId), protector, journal.Journal);

        await handler.Handle(
            new AddPaAccountCommand
            {
                PluginType = "SuperPdp",
                Environment = "Staging",
                AccountIdentifiers = "acct-1",
                ClientId = "the-client-id",
                ClientSecret = "the-client-secret",
            },
            CancellationToken.None);

        protector.Protected.Should().Contain((PaAccountSecretPurposes.ClientId, "the-client-id"));
        protector.Protected.Should().Contain((PaAccountSecretPurposes.ClientSecret, "the-client-secret"));

        var inserted = uow.Inserted.Should().ContainSingle().Subject;
        inserted.EncryptedClientId.Should().Be("ENC[" + PaAccountSecretPurposes.ClientId + "]:the-client-id");
        inserted.EncryptedClientSecret.Should().Be("ENC[" + PaAccountSecretPurposes.ClientSecret + "]:the-client-secret");
    }

    [Fact]
    public async Task Add_Without_OAuth_Secrets_Leaves_Them_Null_And_Does_Not_Encrypt()
    {
        var protector = new RecordingSecretProtector();
        var uow = new FakePaAccountUnitOfWork();
        var journal = new RecordingJournal();
        var handler = new AddPaAccountHandler(new FakeUowFactory(uow), new FakeCompanyFilter(CompanyId), protector, journal.Journal);

        await handler.Handle(
            new AddPaAccountCommand
            {
                PluginType = "SuperPdp",
                Environment = "Staging",
                AccountIdentifiers = "acct-1",
                ClientId = null,
                ClientSecret = "   ",
            },
            CancellationToken.None);

        var inserted = uow.Inserted.Should().ContainSingle().Subject;
        inserted.EncryptedClientId.Should().BeNull("null = secret non saisi (jamais chiffré)");
        inserted.EncryptedClientSecret.Should().BeNull("vide = secret non saisi (jamais chiffré)");
        protector.Protected.Should().NotContain(p => p.Purpose == PaAccountSecretPurposes.ClientId);
        protector.Protected.Should().NotContain(p => p.Purpose == PaAccountSecretPurposes.ClientSecret);
    }

    [Fact]
    public async Task Add_Journal_Carries_Boolean_Has_Flags_Never_The_Secret()
    {
        var protector = new RecordingSecretProtector();
        var journal = new RecordingJournal();
        var handler = new AddPaAccountHandler(new FakeUowFactory(new FakePaAccountUnitOfWork()), new FakeCompanyFilter(CompanyId), protector, journal.Journal);

        await handler.Handle(
            new AddPaAccountCommand
            {
                PluginType = "SuperPdp",
                Environment = "Staging",
                AccountIdentifiers = "acct-1",
                ClientId = "the-client-id",
                ClientSecret = null,
            },
            CancellationToken.None);

        var metadata = journal.Entries.Should().ContainSingle().Subject.Metadata;
        MetaBool(metadata, "HasClientId").Should().BeTrue();
        MetaBool(metadata, "HasClientSecret").Should().BeFalse();

        // Le secret en clair ne fuit JAMAIS dans la métadonnée du journal (CLAUDE.md n°10).
        var serialized = string.Join(
            "|",
            metadata!.GetType().GetProperties().Select(p => p.GetValue(metadata)?.ToString()));
        serialized.Should().NotContain("the-client-id");
    }

    [Fact]
    public async Task Update_Rotates_Client_Id_And_Secret_When_Provided()
    {
        var existing = PaAccount.Create(CompanyId, "SuperPdp", PaEnvironment.Staging, "acct-1", null, "old-cid", "old-csecret");
        var protector = new RecordingSecretProtector();
        var uow = new FakePaAccountUnitOfWork(existing);
        var journal = new RecordingJournal();
        var handler = new UpdatePaAccountHandler(new FakeUowFactory(uow), new FakeCompanyFilter(CompanyId), protector, journal.Journal);

        await handler.Handle(
            new UpdatePaAccountCommand
            {
                PaAccountId = existing.Id,
                Environment = "Production",
                AccountIdentifiers = "acct-1",
                ClientId = "new-client-id",
                ClientSecret = "new-client-secret",
            },
            CancellationToken.None);

        protector.Protected.Should().Contain((PaAccountSecretPurposes.ClientId, "new-client-id"));
        protector.Protected.Should().Contain((PaAccountSecretPurposes.ClientSecret, "new-client-secret"));

        var updated = uow.Updated.Should().ContainSingle().Subject;
        updated.EncryptedClientId.Should().Be("ENC[" + PaAccountSecretPurposes.ClientId + "]:new-client-id");
        updated.EncryptedClientSecret.Should().Be("ENC[" + PaAccountSecretPurposes.ClientSecret + "]:new-client-secret");

        var metadata = journal.Entries.Should().ContainSingle().Subject.Metadata;
        MetaBool(metadata, "ClientIdRotated").Should().BeTrue();
        MetaBool(metadata, "ClientSecretRotated").Should().BeTrue();
    }

    [Fact]
    public async Task Update_With_Blank_OAuth_Secrets_Leaves_Them_Unchanged()
    {
        var existing = PaAccount.Create(CompanyId, "SuperPdp", PaEnvironment.Staging, "acct-1", null, "old-cid", "old-csecret");
        var protector = new RecordingSecretProtector();
        var uow = new FakePaAccountUnitOfWork(existing);
        var journal = new RecordingJournal();
        var handler = new UpdatePaAccountHandler(new FakeUowFactory(uow), new FakeCompanyFilter(CompanyId), protector, journal.Journal);

        await handler.Handle(
            new UpdatePaAccountCommand
            {
                PaAccountId = existing.Id,
                Environment = "Staging",
                AccountIdentifiers = "acct-1",
                ClientId = null,
                ClientSecret = "  ",
            },
            CancellationToken.None);

        var updated = uow.Updated.Should().ContainSingle().Subject;
        updated.EncryptedClientId.Should().Be("old-cid", "null = client_id inchangé (rotation seulement si non vide)");
        updated.EncryptedClientSecret.Should().Be("old-csecret", "vide = client_secret inchangé");
        protector.Protected.Should().NotContain(p => p.Purpose == PaAccountSecretPurposes.ClientId);
        protector.Protected.Should().NotContain(p => p.Purpose == PaAccountSecretPurposes.ClientSecret);

        var metadata = journal.Entries.Should().ContainSingle().Subject.Metadata;
        MetaBool(metadata, "ClientIdRotated").Should().BeFalse();
        MetaBool(metadata, "ClientSecretRotated").Should().BeFalse();
    }

    [Fact]
    public async Task Add_Encrypts_Technical_Password_Under_Its_Dedicated_Purpose_And_Journals_The_Flag()
    {
        var protector = new RecordingSecretProtector();
        var uow = new FakePaAccountUnitOfWork();
        var journal = new RecordingJournal();
        var handler = new AddPaAccountHandler(new FakeUowFactory(uow), new FakeCompanyFilter(CompanyId), protector, journal.Journal);

        await handler.Handle(
            new AddPaAccountCommand
            {
                PluginType = "ChorusPro",
                Environment = "Staging",
                AccountIdentifiers = "tech-login@tenant.fr",
                ClientId = "piste-client-id",
                ClientSecret = "piste-client-secret",
                TechnicalPassword = "the-technical-password",
            },
            CancellationToken.None);

        protector.Protected.Should().Contain((PaAccountSecretPurposes.TechnicalPassword, "the-technical-password"));

        var inserted = uow.Inserted.Should().ContainSingle().Subject;
        inserted.EncryptedTechnicalPassword.Should().Be("ENC[" + PaAccountSecretPurposes.TechnicalPassword + "]:the-technical-password");

        var metadata = journal.Entries.Should().ContainSingle().Subject.Metadata;
        MetaBool(metadata, "HasTechnicalPassword").Should().BeTrue();

        // Le mot de passe technique en clair ne fuit JAMAIS dans la métadonnée du journal (CLAUDE.md n°10).
        var serialized = string.Join(
            "|",
            metadata!.GetType().GetProperties().Select(p => p.GetValue(metadata)?.ToString()));
        serialized.Should().NotContain("the-technical-password");
    }

    [Fact]
    public async Task Add_Without_Technical_Password_Leaves_It_Null_And_Does_Not_Encrypt()
    {
        var protector = new RecordingSecretProtector();
        var uow = new FakePaAccountUnitOfWork();
        var journal = new RecordingJournal();
        var handler = new AddPaAccountHandler(new FakeUowFactory(uow), new FakeCompanyFilter(CompanyId), protector, journal.Journal);

        await handler.Handle(
            new AddPaAccountCommand
            {
                PluginType = "ChorusPro",
                Environment = "Staging",
                AccountIdentifiers = "tech-login@tenant.fr",
                TechnicalPassword = "   ",
            },
            CancellationToken.None);

        var inserted = uow.Inserted.Should().ContainSingle().Subject;
        inserted.EncryptedTechnicalPassword.Should().BeNull("vide = mot de passe technique non saisi (jamais chiffré)");
        protector.Protected.Should().NotContain(p => p.Purpose == PaAccountSecretPurposes.TechnicalPassword);
    }

    [Fact]
    public async Task Update_Rotates_Technical_Password_When_Provided_And_Leaves_It_When_Blank()
    {
        var existing = PaAccount.Create(
            CompanyId, "ChorusPro", PaEnvironment.Staging, "tech-login@tenant.fr", null, "old-cid", "old-csecret", "old-tech-pwd");
        var protector = new RecordingSecretProtector();
        var uow = new FakePaAccountUnitOfWork(existing);
        var journal = new RecordingJournal();
        var handler = new UpdatePaAccountHandler(new FakeUowFactory(uow), new FakeCompanyFilter(CompanyId), protector, journal.Journal);

        await handler.Handle(
            new UpdatePaAccountCommand
            {
                PaAccountId = existing.Id,
                Environment = "Staging",
                AccountIdentifiers = "tech-login@tenant.fr",
                TechnicalPassword = "new-technical-password",
            },
            CancellationToken.None);

        protector.Protected.Should().Contain((PaAccountSecretPurposes.TechnicalPassword, "new-technical-password"));
        var updated = uow.Updated.Should().ContainSingle().Subject;
        updated.EncryptedTechnicalPassword.Should().Be("ENC[" + PaAccountSecretPurposes.TechnicalPassword + "]:new-technical-password");
        MetaBool(journal.Entries.Should().ContainSingle().Subject.Metadata, "TechnicalPasswordRotated").Should().BeTrue();
    }

    [Fact]
    public async Task Update_With_Blank_Technical_Password_Leaves_It_Unchanged()
    {
        var existing = PaAccount.Create(
            CompanyId, "ChorusPro", PaEnvironment.Staging, "tech-login@tenant.fr", null, "old-cid", "old-csecret", "old-tech-pwd");
        var protector = new RecordingSecretProtector();
        var uow = new FakePaAccountUnitOfWork(existing);
        var journal = new RecordingJournal();
        var handler = new UpdatePaAccountHandler(new FakeUowFactory(uow), new FakeCompanyFilter(CompanyId), protector, journal.Journal);

        await handler.Handle(
            new UpdatePaAccountCommand
            {
                PaAccountId = existing.Id,
                Environment = "Staging",
                AccountIdentifiers = "tech-login@tenant.fr",
                TechnicalPassword = "  ",
            },
            CancellationToken.None);

        uow.Updated.Should().ContainSingle().Subject.EncryptedTechnicalPassword
            .Should().Be("old-tech-pwd", "vide = mot de passe technique inchangé (rotation seulement si non vide)");
        protector.Protected.Should().NotContain(p => p.Purpose == PaAccountSecretPurposes.TechnicalPassword);
        MetaBool(journal.Entries.Should().ContainSingle().Subject.Metadata, "TechnicalPasswordRotated").Should().BeFalse();
    }

    [Fact]
    public async Task Update_Missing_Account_Throws_NotFound()
    {
        var handler = new UpdatePaAccountHandler(
            new FakeUowFactory(new FakePaAccountUnitOfWork()), new FakeCompanyFilter(CompanyId), new RecordingSecretProtector(), new RecordingJournal().Journal);

        var act = () => handler.Handle(
            new UpdatePaAccountCommand { PaAccountId = Guid.NewGuid(), Environment = "Staging", AccountIdentifiers = "acct-1" },
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private static bool MetaBool(object? metadata, string property)
    {
        var value = metadata!.GetType().GetProperty(property)!.GetValue(metadata);
        return (bool)value!;
    }

    /// <summary>Coffre FACTICE qui ENREGISTRE (purpose, clair) et produit un texte chiffré porteur du purpose.</summary>
    private sealed class RecordingSecretProtector : ISecretProtector
    {
        public List<(string Purpose, string Plaintext)> Protected { get; } = [];

        public string Protect(string plaintext)
        {
            Protected.Add((PaAccountSecretPurposes.ApiKey, plaintext));
            return Encode(PaAccountSecretPurposes.ApiKey, plaintext);
        }

        public string Protect(string plaintext, string purpose)
        {
            Protected.Add((purpose, plaintext));
            return Encode(purpose, plaintext);
        }

        public string Unprotect(string protectedValue) => protectedValue;

        public string Unprotect(string protectedValue, string purpose) => protectedValue;

        private static string Encode(string purpose, string plaintext) => $"ENC[{purpose}]:{plaintext}";
    }

    private sealed class FakeCompanyFilter : ICompanyFilter
    {
        private readonly Guid _companyId;

        public FakeCompanyFilter(Guid companyId) => _companyId = companyId;

        public Guid GetRequiredCompanyId() => _companyId;
    }

    private sealed class FakeUowFactory : ITenantSettingsUnitOfWorkFactory
    {
        private readonly ITenantSettingsUnitOfWork _uow;

        public FakeUowFactory(ITenantSettingsUnitOfWork uow) => _uow = uow;

        public Task<ITenantSettingsUnitOfWork> BeginAsync(CancellationToken ct = default) => Task.FromResult(_uow);
    }

    /// <summary>UoW factice : ne couvre que le périmètre comptes PA des deux handlers (insert/get/update).</summary>
    private sealed class FakePaAccountUnitOfWork : ITenantSettingsUnitOfWork
    {
        private readonly PaAccount? _existing;

        public FakePaAccountUnitOfWork(PaAccount? existing = null) => _existing = existing;

        public List<PaAccount> Inserted { get; } = [];

        public List<PaAccount> Updated { get; } = [];

        public Task<PaAccount?> GetPaAccountByIdAsync(Guid id, Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(_existing is not null && _existing.Id == id ? _existing : null);

        public Task InsertPaAccountAsync(PaAccount account, CancellationToken ct = default)
        {
            Inserted.Add(account);
            return Task.CompletedTask;
        }

        public Task UpdatePaAccountAsync(PaAccount account, CancellationToken ct = default)
        {
            Updated.Add(account);
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        // ── Membres hors périmètre des handlers testés ──
        public Task<TenantProfile?> GetTenantProfileByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InsertTenantProfileAsync(TenantProfile profile, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpdateTenantProfileAsync(TenantProfile profile, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FiscalSettings?> GetFiscalSettingsByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InsertFiscalSettingsAsync(FiscalSettings settings, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpdateFiscalSettingsAsync(FiscalSettings settings, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PaAccount>> GetPaAccountsByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExtractionSchedule?> GetExtractionScheduleByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InsertExtractionScheduleAsync(ExtractionSchedule schedule, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpdateExtractionScheduleAsync(ExtractionSchedule schedule, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AlertThresholds?> GetAlertThresholdsByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InsertAlertThresholdsAsync(AlertThresholds thresholds, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpdateAlertThresholdsAsync(AlertThresholds thresholds, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AuctionVerticalSettings?> GetAuctionVerticalSettingsByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InsertAuctionVerticalSettingsAsync(AuctionVerticalSettings settings, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpdateAuctionVerticalSettingsAsync(AuctionVerticalSettings settings, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ReplaceAlertRoutingRulesAsync(Guid companyId, IReadOnlyList<AlertRoutingRule> rules, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Construit un vrai <see cref="TenantSettingsJournal"/> branché sur un logger qui capture les métadonnées.</summary>
    private sealed class RecordingJournal
    {
        private readonly CapturingActivityLogger _logger = new();

        public RecordingJournal() => Journal = new TenantSettingsJournal(_logger, new FixedActorContextAccessor());

        public TenantSettingsJournal Journal { get; }

        public IReadOnlyList<RecordedEntry> Entries => _logger.Entries;

        public sealed record RecordedEntry(string EntityType, string ActivityType, object? Metadata);

        private sealed class CapturingActivityLogger : IActivityLogger
        {
            public List<RecordedEntry> Entries { get; } = [];

            public Task LogActivityAsync(
                string entityType,
                string entityId,
                string activityType,
                string description,
                string actorId,
                object? metadata = null,
                Guid? companyId = null,
                CancellationToken cancellationToken = default)
            {
                Entries.Add(new RecordedEntry(entityType, activityType, metadata));
                return Task.CompletedTask;
            }
        }

        private sealed class FixedActorContextAccessor : IActorContextAccessor
        {
            public IActorContext Current { get; } = new Ctx();

            private sealed class Ctx : IActorContext
            {
                public Guid UserId => Guid.Empty;

                public Guid CorrelationId => Guid.Empty;

                public bool IsAuthenticated => true;

                public string? DisplayName => null;

                public string? Email => null;

                public Guid? CompanyId => null;

                public string? Timezone => null;

                public string? Language => null;

                public string? TenantId => null;
            }
        }
    }
}
