namespace Liakont.Host.Tests.Unit.Ged;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Ged;
using Liakont.Host.Security;
using Liakont.Modules.Ged.Application.Index;
using Liakont.Modules.Ged.Contracts.Consultation;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Le seam de composition en lecture de l'exploration de graphe GED (GED09c) : prouve que le droit de
/// confidentialité est résolu SERVER-SIDE depuis les permissions (jamais fourni par la page), que la racine, la
/// borne de profondeur, le curseur keyset et la taille de page sont transmis tels quels à l'index de graphe
/// (GED08), que le résultat est projeté vers les modèles de vue, et que chaque exploration écrit une trace de
/// consultation <c>explore_entity</c> (GED13, §6.6) — fail-closed en régime probant.
/// </summary>
public sealed class GedGraphQueryServiceTests
{
    [Fact]
    public async Task Resolves_The_Confidential_Right_Server_Side_When_Granted()
    {
        var index = new CapturingGraphIndex();
        var audit = new CapturingAuditWriter();
        var service = new GedGraphQueryService(index, audit, Permissions(LiakontPermissions.GedConfidential));

        await service.ExploreAsync(new GedGraphRequest { RootEntityId = Guid.NewGuid(), EntityTypeCode = "entreprise" });

        index.LastQuery!.HasConfidentialRight.Should().BeTrue();
        audit.LastEntry!.ActorHasConfidentialAccess.Should().BeTrue();
    }

    [Fact]
    public async Task Masks_By_Default_When_The_Confidential_Right_Is_Absent()
    {
        var index = new CapturingGraphIndex();
        var audit = new CapturingAuditWriter();

        // Un porteur de la seule lecture GED n'a PAS le droit confidentiel → traversée fail-safe (racine/voisins
        // confidentiels exclus côté SQL).
        var service = new GedGraphQueryService(index, audit, Permissions(LiakontPermissions.GedRead));

        await service.ExploreAsync(new GedGraphRequest { RootEntityId = Guid.NewGuid(), EntityTypeCode = "entreprise" });

        index.LastQuery!.HasConfidentialRight.Should().BeFalse();
        audit.LastEntry!.ActorHasConfidentialAccess.Should().BeFalse();
    }

    [Fact]
    public async Task Forwards_The_Root_Depth_Keyset_Cursor_And_Page_Size_To_The_Index()
    {
        var index = new CapturingGraphIndex();
        var root = Guid.NewGuid();
        var cursor = new GedGraphCursor(Guid.NewGuid(), Guid.NewGuid(), "emitter");
        var service = new GedGraphQueryService(index, new CapturingAuditWriter(), Permissions());

        await service.ExploreAsync(new GedGraphRequest
        {
            RootEntityId = root,
            EntityTypeCode = "entreprise",
            MaxDepth = 6,
            After = cursor,
            PageSize = 25,
        });

        var query = index.LastQuery!;
        query.RootEntityId.Should().Be(root);
        query.MaxDepth.Should().Be(6);
        query.PageSize.Should().Be(25);
        query.After.Should().Be(new GraphCursor(cursor.ManagedDocumentId, cursor.EntityId, "emitter"));
    }

    [Fact]
    public async Task First_Page_Forwards_A_Null_Keyset_Cursor()
    {
        var index = new CapturingGraphIndex();
        var service = new GedGraphQueryService(index, new CapturingAuditWriter(), Permissions());

        await service.ExploreAsync(new GedGraphRequest { RootEntityId = Guid.NewGuid() });

        index.LastQuery!.After.Should().BeNull("la première page ne porte pas de curseur keyset");
    }

    [Fact]
    public async Task Projects_The_Index_Result_To_The_View_Models()
    {
        var docId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var next = new GraphCursor(Guid.NewGuid(), Guid.NewGuid(), "subject");
        var index = new CapturingGraphIndex
        {
            Result = new GraphExplorationResult
            {
                Documents = [new GraphDocumentHit { ManagedDocumentId = docId, EntityId = entityId, Role = "emitter", Depth = 2 }],
                NextCursor = next,
            },
        };
        var service = new GedGraphQueryService(index, new CapturingAuditWriter(), Permissions());

        var result = await service.ExploreAsync(new GedGraphRequest { RootEntityId = Guid.NewGuid() });

        result.Hits.Should().ContainSingle()
            .Which.Should().Be(new GedGraphHit(docId, entityId, "emitter", 2));
        result.NextCursor.Should().Be(new GedGraphCursor(next.ManagedDocumentId, next.EntityId, "subject"));
    }

    [Fact]
    public async Task A_Last_Page_Projects_A_Null_Next_Cursor()
    {
        var index = new CapturingGraphIndex
        {
            Result = new GraphExplorationResult { Documents = [], NextCursor = null },
        };
        var service = new GedGraphQueryService(index, new CapturingAuditWriter(), Permissions());

        var result = await service.ExploreAsync(new GedGraphRequest { RootEntityId = Guid.NewGuid() });

        result.NextCursor.Should().BeNull();
        result.Hits.Should().BeEmpty();
    }

    [Fact]
    public async Task Writes_An_Explore_Consultation_Entry_With_The_Root_Entity_Type_And_Result_Count()
    {
        var root = Guid.NewGuid();
        var index = new CapturingGraphIndex
        {
            Result = new GraphExplorationResult
            {
                Documents =
                [
                    new GraphDocumentHit { ManagedDocumentId = Guid.NewGuid(), EntityId = root, Role = "emitter", Depth = 0 },
                    new GraphDocumentHit { ManagedDocumentId = Guid.NewGuid(), EntityId = Guid.NewGuid(), Role = "subject", Depth = 1 },
                ],
                NextCursor = null,
            },
        };
        var audit = new CapturingAuditWriter();
        var service = new GedGraphQueryService(index, audit, Permissions());

        await service.ExploreAsync(new GedGraphRequest { RootEntityId = root, EntityTypeCode = "entreprise" });

        var entry = audit.LastEntry!;
        entry.Action.Should().Be(ConsultationAction.ExploreEntity);
        entry.EntityId.Should().Be(root);
        entry.TargetedEntityTypeCode.Should().Be("entreprise");
        entry.ResultCount.Should().Be(2);
    }

    [Fact]
    public async Task Does_Not_Swallow_A_Fail_Closed_Audit_Exception()
    {
        // Régime probant (Evidential, §6.6/ADR-0036) : une trace non écrite lève ; le seam laisse remonter
        // l'exception (la page la traduit en refus — fail-closed, aucun résultat affiché). Ne jamais avaler.
        var audit = new CapturingAuditWriter
        {
            OnWrite = () => throw new ConsultationAuditException("trace impossible", new InvalidOperationException()),
        };
        var service = new GedGraphQueryService(new CapturingGraphIndex(), audit, Permissions());

        var act = async () => await service.ExploreAsync(new GedGraphRequest { RootEntityId = Guid.NewGuid() });

        await act.Should().ThrowAsync<ConsultationAuditException>();
    }

    private static FakePermissionService Permissions(params string[] permissions) => new(permissions);

    private sealed class CapturingGraphIndex : IDocumentSearchIndex
    {
        public GraphExplorationQuery? LastQuery { get; private set; }

        public GraphExplorationResult Result { get; init; } = GraphExplorationResult.Empty;

        public Task<GraphExplorationResult> ExploreGraphAsync(GraphExplorationQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(Result);
        }

        public Task<DocumentSearchResult> SearchAsync(DocumentSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentSearchResult.Empty);

        public Task RefreshDocumentAsync(Guid managedDocumentId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CapturingAuditWriter : IConsultationAuditWriter
    {
        public ConsultationLogEntry? LastEntry { get; private set; }

        public Action? OnWrite { get; init; }

        public Task WriteAsync(ConsultationLogEntry entry, CancellationToken cancellationToken = default)
        {
            LastEntry = entry;
            OnWrite?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly HashSet<string> _permissions;

        public FakePermissionService(string[] permissions) =>
            _permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => _permissions.Contains(permission);
    }
}
