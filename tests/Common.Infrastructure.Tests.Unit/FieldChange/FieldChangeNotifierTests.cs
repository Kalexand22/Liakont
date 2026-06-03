namespace Stratum.Common.Infrastructure.Tests.Unit.FieldChange;

using FluentAssertions;
using Stratum.Common.Abstractions.FieldChange;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.FieldChange;
using Xunit;

public sealed class FieldChangeNotifierTests
{
    [Fact]
    public async Task NotifyAsync_Single_Field_Delegates_To_Engine()
    {
        var dto = new TestDto { Name = "Test" };
        var expected = FieldChangeResult.WithFields(
            new Dictionary<string, object?> { ["Age"] = 42 });
        var engine = new FakeEngine(expected);
        var notifier = new FieldChangeNotifier<TestDto>(engine, new FakeAccessor());

        var result = await notifier.NotifyAsync(dto, "Name");

        result.Should().BeSameAs(expected);
        engine.LastEntity.Should().BeSameAs(dto);
        engine.LastChangedFields.Should().Contain("Name").And.HaveCount(1);
    }

    [Fact]
    public async Task NotifyAsync_Multiple_Fields_Delegates_To_Engine()
    {
        var dto = new TestDto();
        var changedFields = new HashSet<string> { "Name", "Age" };
        var expected = FieldChangeResult.Empty();
        var engine = new FakeEngine(expected);
        var notifier = new FieldChangeNotifier<TestDto>(engine, new FakeAccessor());

        var result = await notifier.NotifyAsync(dto, changedFields);

        result.Should().BeSameAs(expected);
        engine.LastChangedFields.Should().BeEquivalentTo(changedFields);
    }

    [Fact]
    public async Task NotifyAsync_Uses_Current_Actor_Context()
    {
        var actor = new FakeActor { UserId = Guid.NewGuid() };
        var accessor = new FakeAccessor(actor);
        var engine = new FakeEngine(FieldChangeResult.Empty());
        var notifier = new FieldChangeNotifier<TestDto>(engine, accessor);

        await notifier.NotifyAsync(new TestDto(), "Name");

        engine.LastActor.Should().BeSameAs(actor);
    }

    [Fact]
    public async Task NotifyAsync_Throws_On_Null_Entity()
    {
        var notifier = new FieldChangeNotifier<TestDto>(new FakeEngine(FieldChangeResult.Empty()), new FakeAccessor());
        var act = () => notifier.NotifyAsync(null!, "Name");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task NotifyAsync_Throws_On_Empty_FieldName()
    {
        var notifier = new FieldChangeNotifier<TestDto>(new FakeEngine(FieldChangeResult.Empty()), new FakeAccessor());
        var act = () => notifier.NotifyAsync(new TestDto(), string.Empty);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class TestDto
    {
        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }
    }

    private sealed class FakeEngine(FieldChangeResult resultToReturn) : IFieldChangeEngine
    {
        public object? LastEntity { get; private set; }

        public IReadOnlySet<string>? LastChangedFields { get; private set; }

        public IActorContext? LastActor { get; private set; }

        public Task<FieldChangeResult> ProcessChangesAsync<T>(
            T entity,
            IReadOnlySet<string> changedFields,
            IActorContext actor,
            CancellationToken ct = default)
        {
            LastEntity = entity;
            LastChangedFields = changedFields;
            LastActor = actor;
            return Task.FromResult(resultToReturn);
        }
    }

    private sealed class FakeAccessor(IActorContext? actor = null) : IActorContextAccessor
    {
        public IActorContext Current => actor ?? new FakeActor();
    }

    private sealed class FakeActor : IActorContext
    {
        public Guid UserId { get; init; } = Guid.NewGuid();

        public Guid CorrelationId => Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName => "Test User";

        public string? Email => "test@test.com";

        public Guid? CompanyId => null;

        public string? Timezone => null;

        public string? Language => null;

        public string? TenantId => null;
    }
}
