namespace Liakont.Modules.TvaMapping.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Infrastructure.Services;
using Xunit;

/// <summary>
/// <see cref="TvaMappingService"/> (PIP01a) expose le moteur <c>TvaMapper</c> à la frontière Contracts :
/// mappe des requêtes EXPLICITES, remonte l'état de validation de la table, n'invente AUCUNE catégorie pour
/// un régime non couvert, et bloque toutes les lignes en l'absence de table. La part est fournie par
/// l'appelant (jamais dérivée). Le seam <see cref="IMappingTableSource"/> permet le test sans base.
/// </summary>
public sealed class TvaMappingServiceTests
{
    [Fact]
    public async Task MapAsync_Maps_Known_Regime_And_Surfaces_Validation()
    {
        var companyId = Guid.NewGuid();
        var service = new TvaMappingService(new FakeTableSource(ValidatedTable(companyId)));

        var result = await service.MapAsync(companyId, new[]
        {
            new TvaLineMappingRequest { SourceRegimeCode = "NORMAL", Part = TvaMappingPart.Adjudication, LineRef = "L1" },
        });

        result.TableExists.Should().BeTrue();
        result.IsValidated.Should().BeTrue();
        result.MappingVersion.Should().Be("cmp-v1");

        var line = result.Lines.Should().ContainSingle().Subject;
        line.IsMapped.Should().BeTrue();
        line.Category.Should().Be("S");
        line.Rate.Should().Be(20m);
        line.LineRef.Should().Be("L1");
        line.BlockReason.Should().BeNull();
    }

    [Fact]
    public async Task MapAsync_Blocks_Unknown_Regime_Without_Inventing_A_Category()
    {
        var companyId = Guid.NewGuid();
        var service = new TvaMappingService(new FakeTableSource(ValidatedTable(companyId)));

        var result = await service.MapAsync(companyId, new[]
        {
            new TvaLineMappingRequest { SourceRegimeCode = "INCONNU", Part = TvaMappingPart.Adjudication },
        });

        var line = result.Lines.Should().ContainSingle().Subject;
        line.IsMapped.Should().BeFalse();
        line.Category.Should().BeNull("aucune catégorie n'est devinée pour un régime non couvert (F03 §4.1)");
        line.BlockReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task MapAsync_Without_Table_Blocks_Every_Line()
    {
        var companyId = Guid.NewGuid();
        var service = new TvaMappingService(new FakeTableSource(table: null));

        var result = await service.MapAsync(companyId, new[]
        {
            new TvaLineMappingRequest { SourceRegimeCode = "NORMAL", Part = TvaMappingPart.Adjudication },
            new TvaLineMappingRequest { SourceRegimeCode = "AUTRE", Part = TvaMappingPart.Frais },
        });

        result.TableExists.Should().BeFalse();
        result.IsValidated.Should().BeFalse();
        result.Lines.Should().HaveCount(2);
        result.Lines.Should().OnlyContain(l => !l.IsMapped && l.BlockReason != null);
    }

    [Fact]
    public async Task MapAsync_Surfaces_An_Unvalidated_Table()
    {
        var companyId = Guid.NewGuid();
        var service = new TvaMappingService(new FakeTableSource(UnvalidatedTable(companyId)));

        var result = await service.MapAsync(companyId, Array.Empty<TvaLineMappingRequest>());

        result.TableExists.Should().BeTrue();
        result.IsValidated.Should().BeFalse("table sans validateur = non validée (garde-fou production PIP01b)");
        result.Lines.Should().BeEmpty();
    }

    private static MappingTable ValidatedTable(Guid companyId) => MappingTable.Create(
        companyId, "cmp-v1", "Expert-comptable CMP", new DateOnly(2026, 1, 1), MappingDefaultBehavior.Block, Rules());

    private static MappingTable UnvalidatedTable(Guid companyId) => MappingTable.Create(
        companyId, "v1", validatedBy: null, validatedDate: null, MappingDefaultBehavior.Block, Rules());

    private static MappingRule[] Rules() => new[]
    {
        new MappingRule
        {
            SourceRegimeCode = "NORMAL",
            Part = MappingPart.Adjudication,
            Category = VatCategory.S,
            RateMode = RateMode.Fixed,
            RateValue = 20m,
        },
    };

    private sealed class FakeTableSource : IMappingTableSource
    {
        private readonly MappingTable? _table;

        public FakeTableSource(MappingTable? table) => _table = table;

        public Task<MappingTable?> LoadAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_table);
    }
}
