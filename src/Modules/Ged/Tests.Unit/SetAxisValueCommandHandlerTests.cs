namespace Liakont.Modules.Ged.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Ged.Application;
using Liakont.Modules.Ged.Contracts.Commands;
using Liakont.Modules.Ged.Domain.Catalog;
using Liakont.Modules.Ged.Domain.Index;
using Liakont.Modules.Ged.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests unitaires (sans base) de <see cref="SetAxisValueCommandHandler"/> (F19 §3.7) : résolution d'axe (refus
/// inconnu/inactif, jamais deviner règle 2), normalisation (refus si incompatible avec le <c>data_type</c> ou hors
/// vocabulaire enum), et propagation correcte du drapeau mono-valeur (garde RL-02) à l'UoW. La preuve de la garde
/// de concurrence elle-même est portée par le test d'intégration CONCURRENT (base réelle) — un fake ne peut pas la
/// prouver.
/// </summary>
public sealed class SetAxisValueCommandHandlerTests
{
    private static readonly Guid AxisId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DocumentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Handle_normalizes_and_appends_under_mono_guard_for_a_single_valued_axis()
    {
        var axis = ActiveAxis(AxisDataType.Text, isMultiValue: false);
        var factory = new RecordingUnitOfWorkFactory();
        var handler = new SetAxisValueCommandHandler(new FakeAxisCatalog(axis), factory);

        var id = await handler.Handle(Command(rawValue: "Chantier A", source: "manual"), CancellationToken.None);

        id.Should().Be(factory.UnitOfWork.ReturnedId);
        factory.UnitOfWork.AppendedIsSingleValued.Should().BeTrue("un axe mono exige la garde de concurrence (RL-02)");
        factory.UnitOfWork.AppendedLink!.Value.ValueString.Should().Be("Chantier A");
        factory.UnitOfWork.AppendedLink!.AxisId.Should().Be(AxisId);
        factory.UnitOfWork.Committed.Should().BeTrue();
        factory.UnitOfWork.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_does_not_supersede_for_a_multi_valued_axis()
    {
        var axis = ActiveAxis(AxisDataType.Text, isMultiValue: true);
        var factory = new RecordingUnitOfWorkFactory();
        var handler = new SetAxisValueCommandHandler(new FakeAxisCatalog(axis), factory);

        await handler.Handle(Command(rawValue: "tag", source: "agent"), CancellationToken.None);

        factory.UnitOfWork.AppendedIsSingleValued.Should().BeFalse("un axe multi admet plusieurs valeurs courantes");
    }

    [Fact]
    public async Task Handle_rounds_a_number_value_half_up_to_the_axis_scale()
    {
        var axis = ActiveAxis(AxisDataType.Number, isMultiValue: false, valueScale: 2);
        var factory = new RecordingUnitOfWorkFactory();
        var handler = new SetAxisValueCommandHandler(new FakeAxisCatalog(axis), factory);

        await handler.Handle(Command(rawValue: "1234.505", source: "import"), CancellationToken.None);

        factory.UnitOfWork.AppendedLink!.Value.ValueNumber.Should().Be(1234.51m);
        factory.UnitOfWork.AppendedLink!.Value.NormalizedValue.Should().Be("1234.51");
    }

    [Fact]
    public async Task Handle_throws_when_the_axis_is_unknown()
    {
        var factory = new RecordingUnitOfWorkFactory();
        var handler = new SetAxisValueCommandHandler(new FakeAxisCatalog(null), factory);

        var act = () => handler.Handle(Command(rawValue: "x", source: "manual"), CancellationToken.None);

        await act.Should().ThrowAsync<AxisNotResolvableException>();
        factory.UnitOfWork.AppendedLink.Should().BeNull("aucune écriture ne doit avoir lieu si l'axe est refusé");
    }

    [Fact]
    public async Task Handle_throws_when_the_axis_is_inactive()
    {
        var axis = ActiveAxis(AxisDataType.Text, isMultiValue: false) with { IsActive = false };
        var factory = new RecordingUnitOfWorkFactory();
        var handler = new SetAxisValueCommandHandler(new FakeAxisCatalog(axis), factory);

        var act = () => handler.Handle(Command(rawValue: "x", source: "manual"), CancellationToken.None);

        await act.Should().ThrowAsync<AxisNotResolvableException>();
        factory.UnitOfWork.AppendedLink.Should().BeNull();
    }

    [Fact]
    public async Task Handle_throws_when_the_value_does_not_match_the_data_type()
    {
        var axis = ActiveAxis(AxisDataType.Number, isMultiValue: false, valueScale: 2);
        var factory = new RecordingUnitOfWorkFactory();
        var handler = new SetAxisValueCommandHandler(new FakeAxisCatalog(axis), factory);

        var act = () => handler.Handle(Command(rawValue: "pas-un-nombre", source: "manual"), CancellationToken.None);

        await act.Should().ThrowAsync<AxisValueFormatException>();
        factory.UnitOfWork.AppendedLink.Should().BeNull();
    }

    [Fact]
    public async Task Handle_throws_when_an_enum_value_is_outside_the_declared_vocabulary()
    {
        var axis = ActiveAxis(AxisDataType.Enum, isMultiValue: false) with { AllowedEnumValues = ["fr", "de"] };
        var factory = new RecordingUnitOfWorkFactory();
        var handler = new SetAxisValueCommandHandler(new FakeAxisCatalog(axis), factory);

        var act = () => handler.Handle(Command(rawValue: "es", source: "manual"), CancellationToken.None);

        await act.Should().ThrowAsync<AxisValueFormatException>();
        factory.UnitOfWork.AppendedLink.Should().BeNull("une valeur d'enum hors vocabulaire est refusée (règle 2)");
    }

    [Fact]
    public async Task Handle_accepts_an_enum_value_inside_the_declared_vocabulary()
    {
        var axis = ActiveAxis(AxisDataType.Enum, isMultiValue: false) with { AllowedEnumValues = ["fr", "de"] };
        var factory = new RecordingUnitOfWorkFactory();
        var handler = new SetAxisValueCommandHandler(new FakeAxisCatalog(axis), factory);

        await handler.Handle(Command(rawValue: "fr", source: "manual"), CancellationToken.None);

        factory.UnitOfWork.AppendedLink!.Value.ValueString.Should().Be("fr");
    }

    [Fact]
    public void AddGedModule_registers_the_handler_and_the_index_write_services()
    {
        var services = new ServiceCollection();

        services.AddGedModule();

        services.Should().ContainSingle(d => d.ServiceType == typeof(IRequestHandler<SetAxisValueCommand, Guid>));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IAxisCatalog) && d.ImplementationType == typeof(PostgresAxisCatalog));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IGedIndexUnitOfWorkFactory)
            && d.ImplementationType == typeof(PostgresGedIndexUnitOfWorkFactory));
    }

    private static AxisDefinition ActiveAxis(AxisDataType dataType, bool isMultiValue, int? valueScale = null) =>
        new()
        {
            Id = AxisId,
            Code = "axe_test",
            DataType = dataType,
            ValueScale = valueScale,
            IsMultiValue = isMultiValue,
            IsActive = true,
        };

    private static SetAxisValueCommand Command(string rawValue, string source) =>
        new()
        {
            DocumentId = DocumentId,
            AxisCode = "axe_test",
            RawValue = rawValue,
            Source = source,
        };

    private sealed class FakeAxisCatalog : IAxisCatalog
    {
        private readonly AxisDefinition? _axis;

        public FakeAxisCatalog(AxisDefinition? axis) => _axis = axis;

        public Task<AxisDefinition?> ResolveAsync(string axisCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(_axis);
    }

    private sealed class RecordingUnitOfWorkFactory : IGedIndexUnitOfWorkFactory
    {
        public RecordingUnitOfWork UnitOfWork { get; } = new();

        public Task<IGedIndexUnitOfWork> BeginAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IGedIndexUnitOfWork>(UnitOfWork);
    }

    private sealed class RecordingUnitOfWork : IGedIndexUnitOfWork
    {
        public Guid ReturnedId { get; } = Guid.Parse("33333333-3333-3333-3333-333333333333");

        public DocumentAxisLink? AppendedLink { get; private set; }

        public bool? AppendedIsSingleValued { get; private set; }

        public bool Committed { get; private set; }

        public bool Disposed { get; private set; }

        public Task<Guid> AppendAxisLinkAsync(DocumentAxisLink link, bool isSingleValued, CancellationToken cancellationToken = default)
        {
            AppendedLink = link;
            AppendedIsSingleValued = isSingleValued;
            return Task.FromResult(ReturnedId);
        }

        // Surface d'indexation GED05b non exercée par les tests de SetAxisValueCommandHandler (GED04).
        public Task<string?> BeginDocumentIndexingAsync(Guid managedDocumentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpsertManagedDocumentAsync(ManagedDocument document, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Guid> ResolveOrCreateEntityAsync(Guid entityTypeId, string? identityValue, string displayName, string source, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Guid> AppendDocumentEntityLinkAsync(DocumentEntityLink link, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // SetAxisValueCommandHandler n'écrit que des liens d'axe : le chemin relation (GED24) n'est pas exercé ici.
        public Task<Guid> AppendRelationAsync(EntityRelation relation, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("RecordingUnitOfWork ne couvre que AppendAxisLinkAsync (GED04).");

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
