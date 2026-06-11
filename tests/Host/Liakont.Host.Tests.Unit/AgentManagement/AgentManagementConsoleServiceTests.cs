namespace Liakont.Host.Tests.Unit.AgentManagement;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.AgentManagement;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Tests unitaires de <see cref="AgentManagementConsoleService"/> (WEB09) : lecture du parc tenant-scopée +
/// indicateur « muet » calculé depuis le seuil de supervision (F12 §5.2, surchargeable par tenant — même
/// définition que <c>AgentMuteAlertRule</c>), et actions de cycle de vie déléguées aux commandes PIV05
/// (enregistrement / révocation / rotation) avec PARITÉ D'AUDIT (l'action opérateur est journalisée car la
/// console dispatche en in-process). Nom vide refusé avant tout dispatch ; agent introuvable mappé en NotFound.
/// </summary>
public sealed class AgentManagementConsoleServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OperatorId = Guid.Parse("0a0a0a0a-0a0a-0a0a-0a0a-0a0a0a0a0a0a");

    [Fact]
    public async Task ListAsync_With_Unresolved_Tenant_Returns_Empty()
    {
        var agents = new FakeAgentQueries(Agent("A"));
        var service = Build(agents, tenantId: null);

        var lines = await service.ListAsync();

        lines.Should().BeEmpty("aucune lecture n'est tentée sans tenant résolu");
        agents.LastTenantId.Should().BeNull("la lecture du registre n'est pas appelée sans tenant");
    }

    [Fact]
    public async Task ListAsync_Is_Tenant_Scoped_And_Maps_Fields()
    {
        var agents = new FakeAgentQueries(Agent("Poste A", keyPrefix: "lk_pub_a", version: "1.2.3", lastSeen: Now.AddHours(-1)));
        var service = Build(agents, tenantId: "tenant-7");

        var lines = await service.ListAsync();

        agents.LastTenantId.Should().Be("tenant-7", "la lecture du registre est scopée au tenant courant (jamais cross-tenant)");
        lines.Should().ContainSingle();
        var line = lines[0];
        line.Name.Should().Be("Poste A");
        line.KeyPrefix.Should().Be("lk_pub_a");
        line.Version.Should().Be("1.2.3");
        line.IsRevoked.Should().BeFalse();
        line.IsSilent.Should().BeFalse("vu il y a 1 h, bien en-deçà du seuil 24 h");
        line.StateLabel.Should().Be("Actif");
    }

    [Fact]
    public async Task ListAsync_Marks_Agent_Silent_Past_Default_Threshold()
    {
        var agents = new FakeAgentQueries(
            Agent("Muet", lastSeen: Now.AddHours(-30)),
            Agent("Récent", lastSeen: Now.AddHours(-2)));
        var service = Build(agents, tenantId: "t");

        var lines = await service.ListAsync();

        lines.Should().Contain(l => l.Name == "Muet" && l.IsSilent && l.StateLabel == "Muet");
        lines.Should().Contain(l => l.Name == "Récent" && !l.IsSilent && l.StateLabel == "Actif");
    }

    [Fact]
    public async Task ListAsync_Never_Seen_Agent_Counts_Silence_From_CreatedAt()
    {
        var agents = new FakeAgentQueries(Agent("Jamais vu", lastSeen: null, createdAt: Now.AddHours(-30)));
        var service = Build(agents, tenantId: "t");

        var lines = await service.ListAsync();

        lines[0].IsSilent.Should().BeTrue("un agent jamais vu mesure son silence depuis son enregistrement");
    }

    [Fact]
    public async Task ListAsync_Revoked_Agent_Is_Never_Silent()
    {
        var agents = new FakeAgentQueries(Agent("Retiré", isRevoked: true, lastSeen: Now.AddHours(-500)));
        var service = Build(agents, tenantId: "t");

        var lines = await service.ListAsync();

        lines[0].IsSilent.Should().BeFalse("un agent révoqué ne parle plus par conception (pas une anomalie)");
        lines[0].StateLabel.Should().Be("Révoqué");
    }

    [Fact]
    public async Task ListAsync_Honours_Tenant_Threshold_Override()
    {
        var agents = new FakeAgentQueries(Agent("Limite", lastSeen: Now.AddHours(-30)));

        // Seuil tenant porté à 48 h (surcharge CFG02) : 30 h de silence ne déclenche plus l'alerte.
        var service = Build(agents, tenantId: "t", companyId: CompanyId, silentHours: 48);

        var lines = await service.ListAsync();

        lines[0].IsSilent.Should().BeFalse("le seuil surchargé du tenant (48 h) prime sur le défaut produit (24 h)");
    }

    [Fact]
    public async Task RegisterAsync_Blank_Name_Returns_NameRequired_Without_Dispatch_Or_Audit()
    {
        var sender = new FakeSender();
        var audit = new CapturingActivityLogger();
        var service = Build(new FakeAgentQueries(), tenantId: "t", sender: sender, audit: audit);

        var result = await service.RegisterAsync("   ");

        result.Status.Should().Be(AgentActionStatus.NameRequired);
        result.IssuedKey.Should().BeNull();
        sender.Sent.Should().BeEmpty("un nom vide est refusé AVANT tout dispatch");
        audit.Entries.Should().BeEmpty("aucune action n'a eu lieu : rien à journaliser");
    }

    [Fact]
    public async Task RegisterAsync_Dispatches_Command_Returns_Key_And_Audits()
    {
        var issued = new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "lk_pub", FullKey = "lk_pub.secret-xyz" };
        var sender = new FakeSender { KeyToReturn = issued };
        var audit = new CapturingActivityLogger();
        var service = Build(new FakeAgentQueries(), tenantId: "t", sender: sender, audit: audit);

        var result = await service.RegisterAsync("Poste comptable");

        result.Status.Should().Be(AgentActionStatus.Succeeded);
        result.IssuedKey.Should().BeSameAs(issued, "la clé complète n'est portée que par le résultat d'émission");
        sender.Sent.Should().ContainSingle().Which.Should().BeOfType<RegisterAgentCommand>()
            .Which.Name.Should().Be("Poste comptable");
        var entry = audit.Entries.Should().ContainSingle().Subject;
        entry.ActivityType.Should().Be("agents.registered");
        entry.EntityId.Should().Be(issued.AgentId.ToString());
        entry.CompanyId.Should().Be(CompanyId);
        entry.ActorId.Should().Be(OperatorId.ToString());
    }

    [Fact]
    public async Task RevokeAsync_Dispatches_Command_And_Audits()
    {
        var agentId = Guid.NewGuid();
        var sender = new FakeSender();
        var audit = new CapturingActivityLogger();
        var service = Build(new FakeAgentQueries(), tenantId: "t", sender: sender, audit: audit);

        var status = await service.RevokeAsync(agentId);

        status.Should().Be(AgentActionStatus.Succeeded);
        sender.Sent.Should().ContainSingle().Which.Should().BeOfType<RevokeAgentCommand>()
            .Which.AgentId.Should().Be(agentId);
        audit.Entries.Should().ContainSingle().Which.ActivityType.Should().Be("agents.revoked");
    }

    [Fact]
    public async Task RevokeAsync_Maps_Domain_NotFound_To_NotFound()
    {
        var sender = new FakeSender { ThrowOnSend = new NotFoundException("Agent introuvable.") };
        var audit = new CapturingActivityLogger();
        var service = Build(new FakeAgentQueries(), tenantId: "t", sender: sender, audit: audit);

        var status = await service.RevokeAsync(Guid.NewGuid());

        status.Should().Be(AgentActionStatus.NotFound);
        audit.Entries.Should().BeEmpty("l'action a échoué : aucune entrée d'audit de succès");
    }

    [Fact]
    public async Task RotateKeyAsync_Dispatches_Command_Returns_Key_And_Audits()
    {
        var agentId = Guid.NewGuid();
        var issued = new AgentKeyIssuedDto { AgentId = agentId, KeyPrefix = "lk_pub2", FullKey = "lk_pub2.secret-new" };
        var sender = new FakeSender { KeyToReturn = issued };
        var audit = new CapturingActivityLogger();
        var service = Build(new FakeAgentQueries(), tenantId: "t", sender: sender, audit: audit);

        var result = await service.RotateKeyAsync(agentId);

        result.Status.Should().Be(AgentActionStatus.Succeeded);
        result.IssuedKey.Should().BeSameAs(issued);
        sender.Sent.Should().ContainSingle().Which.Should().BeOfType<RotateAgentKeyCommand>()
            .Which.AgentId.Should().Be(agentId);
        audit.Entries.Should().ContainSingle().Which.ActivityType.Should().Be("agents.key_rotated");
    }

    [Fact]
    public async Task RotateKeyAsync_Maps_Domain_NotFound_To_NotFound()
    {
        var sender = new FakeSender { ThrowOnSend = new NotFoundException("Agent introuvable.") };
        var service = Build(new FakeAgentQueries(), tenantId: "t", sender: sender);

        var result = await service.RotateKeyAsync(Guid.NewGuid());

        result.Status.Should().Be(AgentActionStatus.NotFound);
        result.IssuedKey.Should().BeNull();
    }

    [Fact]
    public async Task RotateKeyAsync_Maps_Domain_Conflict_And_Carries_The_Message()
    {
        // Rotation d'un agent révoqué (course liste→action) : ConflictException du domaine → Conflict + message
        // porté tel quel (parité 409 endpoint), pas un Failed « Réessayez plus tard ».
        const string domainMessage = "Impossible de faire pivoter la clé d'un agent révoqué.";
        var sender = new FakeSender { ThrowOnSend = new ConflictException(domainMessage) };
        var service = Build(new FakeAgentQueries(), tenantId: "t", sender: sender);

        var result = await service.RotateKeyAsync(Guid.NewGuid());

        result.Status.Should().Be(AgentActionStatus.Conflict);
        result.IssuedKey.Should().BeNull();
        result.ErrorMessage.Should().Be(domainMessage);
    }

    [Fact]
    public async Task RegisterAsync_Maps_Domain_Conflict_And_Carries_The_Message()
    {
        const string domainMessage = "Conflit de préfixe de clé d'agent — relancer l'enregistrement.";
        var sender = new FakeSender { ThrowOnSend = new ConflictException(domainMessage) };
        var service = Build(new FakeAgentQueries(), tenantId: "t", sender: sender);

        var result = await service.RegisterAsync("Poste comptable");

        result.Status.Should().Be(AgentActionStatus.Conflict);
        result.IssuedKey.Should().BeNull();
        result.ErrorMessage.Should().Be(domainMessage);
    }

    [Fact]
    public async Task RegisterAsync_Returns_The_Issued_Key_Even_When_Audit_Throws()
    {
        // La commande PIV05 a déjà commité (agent créé, clé émise — IRRÉVERSIBLE) AVANT l'audit : un échec
        // d'audit isolé ne doit jamais faire perdre la clé unique ni convertir le succès en Failed.
        var issued = new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "lk", FullKey = "lk.secret" };
        var sender = new FakeSender { KeyToReturn = issued };
        var service = Build(new FakeAgentQueries(), tenantId: "t", sender: sender, audit: new CapturingActivityLogger { ThrowOnLog = true });

        var result = await service.RegisterAsync("Poste comptable");

        result.Status.Should().Be(AgentActionStatus.Succeeded);
        result.IssuedKey.Should().BeSameAs(issued);
    }

    [Fact]
    public async Task RotateKeyAsync_Returns_The_New_Key_Even_When_Audit_Throws()
    {
        // La nouvelle clé est émise et l'ancienne déjà invalidée AVANT l'audit : un échec d'audit ne doit
        // jamais « bricker » l'agent (nouvelle clé jamais montrée).
        var issued = new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "lk2", FullKey = "lk2.secret-new" };
        var sender = new FakeSender { KeyToReturn = issued };
        var service = Build(new FakeAgentQueries(), tenantId: "t", sender: sender, audit: new CapturingActivityLogger { ThrowOnLog = true });

        var result = await service.RotateKeyAsync(Guid.NewGuid());

        result.Status.Should().Be(AgentActionStatus.Succeeded);
        result.IssuedKey.Should().BeSameAs(issued);
    }

    private static AgentManagementConsoleService Build(
        FakeAgentQueries agents,
        string? tenantId,
        Guid? companyId = null,
        int? silentHours = null,
        FakeSender? sender = null,
        CapturingActivityLogger? audit = null) =>
        new(
            agents,
            new FakeTenantContext(tenantId),
            new FakeTenantSettingsQueries(companyId, silentHours),
            sender ?? new FakeSender(),
            new FakeActorContextAccessor(CompanyId, OperatorId),
            audit ?? new CapturingActivityLogger(),
            new FixedTimeProvider(Now),
            NullLogger<AgentManagementConsoleService>.Instance);

    private static AgentSummaryDto Agent(
        string name,
        string keyPrefix = "lk_pub",
        bool isRevoked = false,
        DateTimeOffset? lastSeen = null,
        string? version = null,
        DateTimeOffset? createdAt = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            KeyPrefix = keyPrefix,
            IsRevoked = isRevoked,
            CreatedAt = createdAt ?? Now.AddDays(-10),
            LastSeenAtUtc = lastSeen,
            LastAgentVersion = version,
        };

    private sealed class FakeAgentQueries : IAgentQueries
    {
        private readonly IReadOnlyList<AgentSummaryDto> _agents;

        public FakeAgentQueries(params AgentSummaryDto[] agents) => _agents = agents;

        public string? LastTenantId { get; private set; }

        public Task<IReadOnlyList<AgentSummaryDto>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            LastTenantId = tenantId;
            return Task.FromResult(_agents);
        }
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string? tenantId) => TenantId = tenantId;

        public string? TenantId { get; }

        public bool IsResolved => !string.IsNullOrEmpty(TenantId);
    }

    private sealed class FakeTenantSettingsQueries : ITenantSettingsQueries
    {
        private readonly Guid? _companyId;
        private readonly int? _silentHours;

        public FakeTenantSettingsQueries(Guid? companyId, int? silentHours)
        {
            _companyId = companyId;
            _silentHours = silentHours;
        }

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult(_companyId);

        public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) => Task.FromResult(false);

        public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(_silentHours is { } hours
                ? new AlertThresholdsDto
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    AgentSilentHours = hours,
                    MissedRunHours = 24,
                    PushQueueMaxItems = 1000,
                    PushQueueMaxAgeHours = 24,
                    BlockedDocumentsDays = 5,
                    PaRejectionsDays = 2,
                    AlertTenantContact = false,
                    CreatedAt = Now,
                }
                : null);

        public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<TenantProfileDto?>(null);

        public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<FiscalSettingsDto?>(null);

        public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PaAccountDto>>([]);

        public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<ExtractionScheduleDto?>(null);
    }

    private sealed class FakeSender : ISender
    {
        public List<object> Sent { get; } = [];

        public AgentKeyIssuedDto KeyToReturn { get; init; } =
            new() { AgentId = Guid.NewGuid(), KeyPrefix = "lk_pub", FullKey = "lk_pub.secret" };

        public Exception? ThrowOnSend { get; init; }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Sent.Add(request!);
            if (ThrowOnSend is { } ex)
            {
                throw ex;
            }

            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            if (ThrowOnSend is { } ex)
            {
                throw ex;
            }

            return Task.FromResult((TResponse)(object)KeyToReturn);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingActivityLogger : IActivityLogger
    {
        public List<ActivityEntry> Entries { get; } = [];

        /// <summary>Simule un audit indisponible (la vraie implémentation ne lève jamais — INV-AUDIT-002).</summary>
        public bool ThrowOnLog { get; init; }

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
            if (ThrowOnLog)
            {
                throw new InvalidOperationException("Audit indisponible.");
            }

            Entries.Add(new ActivityEntry(entityType, entityId, activityType, description, actorId, companyId));
            return Task.CompletedTask;
        }

        public sealed record ActivityEntry(
            string EntityType,
            string EntityId,
            string ActivityType,
            string Description,
            string ActorId,
            Guid? CompanyId);
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public FakeActorContextAccessor(Guid? companyId, Guid userId) =>
            Current = new FakeActorContext(companyId, userId);

        public IActorContext Current { get; }

        private sealed class FakeActorContext : IActorContext
        {
            public FakeActorContext(Guid? companyId, Guid userId)
            {
                CompanyId = companyId;
                UserId = userId;
            }

            public Guid UserId { get; }

            public Guid CorrelationId { get; } = Guid.NewGuid();

            public bool IsAuthenticated => true;

            public string? DisplayName => "Opérateur Test";

            public string? Email => null;

            public Guid? CompanyId { get; }

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId => "tenant-test";
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
