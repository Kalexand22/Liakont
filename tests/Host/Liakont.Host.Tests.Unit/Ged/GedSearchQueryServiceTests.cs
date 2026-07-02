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
/// Le seam de composition en lecture du portail GED (GED09a) : prouve que le droit de confidentialité est résolu
/// SERVER-SIDE depuis les permissions (jamais fourni par la page), que la pagination keyset et les filtres sont
/// transmis tels quels à l'index (GED08), que le résultat est projeté vers les modèles de vue, et que chaque
/// recherche écrit une trace de consultation (GED13, §6.6) — fail-closed en régime probant.
/// </summary>
public sealed class GedSearchQueryServiceTests
{
    [Fact]
    public async Task Resolves_The_Confidential_Right_Server_Side_When_Granted()
    {
        var index = new CapturingSearchIndex();
        var audit = new CapturingAuditWriter();
        var service = new GedSearchQueryService(index, audit, Permissions(LiakontPermissions.GedConfidential));

        await service.SearchAsync(new GedSearchRequest { FullText = "x" });

        index.LastQuery!.HasConfidentialRight.Should().BeTrue();
        audit.LastEntry!.ActorHasConfidentialAccess.Should().BeTrue();
    }

    [Fact]
    public async Task Masks_By_Default_When_The_Confidential_Right_Is_Absent()
    {
        var index = new CapturingSearchIndex();
        var audit = new CapturingAuditWriter();

        // Un porteur de la seule lecture GED n'a PAS le droit confidentiel → masquage (fail-safe).
        var service = new GedSearchQueryService(index, audit, Permissions(LiakontPermissions.GedRead));

        await service.SearchAsync(new GedSearchRequest { FullText = "x" });

        index.LastQuery!.HasConfidentialRight.Should().BeFalse();
        audit.LastEntry!.ActorHasConfidentialAccess.Should().BeFalse();
    }

    [Fact]
    public async Task Forwards_The_Keyset_Cursor_Filters_And_Page_Size_To_The_Index()
    {
        var index = new CapturingSearchIndex();
        var cursor = Guid.NewGuid();
        var service = new GedSearchQueryService(index, new CapturingAuditWriter(), Permissions());

        await service.SearchAsync(new GedSearchRequest
        {
            FullText = "bordereau",
            AxisFilters = [new GedAxisFilter("annee", "2026"), new GedAxisFilter("acheteur", "Dupont")],
            AfterDocumentId = cursor,
            PageSize = 25,
        });

        var query = index.LastQuery!;
        query.FullText.Should().Be("bordereau");
        query.AfterManagedDocumentId.Should().Be(cursor);
        query.PageSize.Should().Be(25);
        query.AxisFilters.Should().BeEquivalentTo(new[]
        {
            new AxisFilter("annee", "2026"),
            new AxisFilter("acheteur", "Dupont"),
        });
    }

    [Fact]
    public async Task Projects_The_Index_Result_To_The_View_Models()
    {
        var docId = Guid.NewGuid();
        var next = Guid.NewGuid();
        var index = new CapturingSearchIndex
        {
            Result = new DocumentSearchResult
            {
                Hits = [new DocumentSearchHit { ManagedDocumentId = docId, Title = "Bordereau 42", DocKind = "bordereau", Status = "indexed" }],
                Facets = [new SearchFacet("annee", "2026", 12)],
                NextCursor = next,
            },
        };
        var service = new GedSearchQueryService(index, new CapturingAuditWriter(), Permissions());

        var result = await service.SearchAsync(new GedSearchRequest { FullText = "b" });

        result.Hits.Should().ContainSingle();
        result.Hits[0].Should().Be(new GedSearchHit(docId, "Bordereau 42", "bordereau", "indexed"));
        result.Facets.Should().ContainSingle().Which.Should().Be(new GedSearchFacet("annee", "2026", 12));
        result.NextCursor.Should().Be(next);
    }

    [Fact]
    public async Task Writes_A_Search_Consultation_Entry_With_Query_And_Result_Count()
    {
        var index = new CapturingSearchIndex
        {
            Result = new DocumentSearchResult
            {
                Hits =
                [
                    new DocumentSearchHit { ManagedDocumentId = Guid.NewGuid(), Title = "A", Status = "indexed" },
                    new DocumentSearchHit { ManagedDocumentId = Guid.NewGuid(), Title = "B", Status = "indexed" },
                ],
                Facets = [],
                NextCursor = null,
            },
        };
        var audit = new CapturingAuditWriter();
        var service = new GedSearchQueryService(index, audit, Permissions());

        await service.SearchAsync(new GedSearchRequest
        {
            FullText = "facture",
            AxisFilters = [new GedAxisFilter("annee", "2026")],
        });

        var entry = audit.LastEntry!;
        entry.Action.Should().Be(ConsultationAction.Search);
        entry.QueryText.Should().Be("facture");
        entry.ResultCount.Should().Be(2);
        entry.TargetedAxisCodes.Should().Contain("annee");
        entry.Detail!.Should().Contain(new KeyValuePair<string, string?>("annee", "2026"));
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
        var service = new GedSearchQueryService(new CapturingSearchIndex(), audit, Permissions());

        var act = async () => await service.SearchAsync(new GedSearchRequest { FullText = "x" });

        await act.Should().ThrowAsync<ConsultationAuditException>();
    }

    private static FakePermissionService Permissions(params string[] permissions) => new(permissions);

    private sealed class CapturingSearchIndex : IDocumentSearchIndex
    {
        public DocumentSearchQuery? LastQuery { get; private set; }

        public DocumentSearchResult Result { get; init; } = DocumentSearchResult.Empty;

        public Task<DocumentSearchResult> SearchAsync(DocumentSearchQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(Result);
        }

        public Task RefreshDocumentAsync(Guid managedDocumentId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<GraphExplorationResult> ExploreGraphAsync(GraphExplorationQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(GraphExplorationResult.Empty);
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
