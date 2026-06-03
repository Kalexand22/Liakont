namespace Stratum.Common.Infrastructure.Tests.Unit.Validation;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Abstractions.Validation;
using Stratum.Common.Infrastructure.Validation;
using Xunit;

public sealed class ValidationEngineTests
{
    [Fact]
    public async Task ValidateAsync_NoValidators_Returns_Valid()
    {
        var engine = CreateEngine();

        var result = await engine.ValidateAsync(new TestEntity(), new FakeActorContext());

        result.IsValid.Should().BeTrue();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_Single_Valid_Validator_Returns_Valid()
    {
        var engine = CreateEngine(entityValidators: [new AlwaysValidValidator()]);

        var result = await engine.ValidateAsync(new TestEntity(), new FakeActorContext());

        result.IsValid.Should().BeTrue();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_Single_Invalid_Validator_Returns_Invalid()
    {
        var engine = CreateEngine(entityValidators:
        [
            new AlwaysInvalidValidator("Name is required", "Name", "INV-001"),
        ]);

        var result = await engine.ValidateAsync(new TestEntity(), new FakeActorContext());

        result.IsValid.Should().BeFalse();
        result.Findings.Should().ContainSingle();
        result.Findings[0].Message.Should().Be("Name is required");
        result.Findings[0].Field.Should().Be("Name");
        result.Findings[0].Code.Should().Be("INV-001");
    }

    [Fact]
    public async Task ValidateAsync_Multiple_Validators_Aggregates_All_Findings()
    {
        var engine = CreateEngine(entityValidators:
        [
            new AlwaysInvalidValidator("Error 1", "Field1"),
            new AlwaysInvalidValidator("Error 2", "Field2"),
        ]);

        var result = await engine.ValidateAsync(new TestEntity(), new FakeActorContext());

        result.IsValid.Should().BeFalse();
        result.Findings.Should().HaveCount(2);
    }

    [Fact]
    public async Task ValidateAsync_Continues_After_Error_Finding()
    {
        var executionOrder = new List<string>();

        var engine = CreateEngine(entityValidators:
        [
            new TrackingInvalidValidator("first", executionOrder),
            new TrackingInvalidValidator("second", executionOrder),
            new TrackingInvalidValidator("third", executionOrder),
        ]);

        var result = await engine.ValidateAsync(new TestEntity(), new FakeActorContext());

        result.IsValid.Should().BeFalse();
        result.Findings.Should().HaveCount(3);
        executionOrder.Should().ContainInOrder("first", "second", "third");
    }

    [Fact]
    public async Task ValidateAsync_Mixed_Severities_Aggregated()
    {
        var engine = CreateEngine(entityValidators:
        [
            new WarningValidator(),
            new AlwaysInvalidValidator("Some error"),
            new InfoValidator(),
        ]);

        var result = await engine.ValidateAsync(new TestEntity(), new FakeActorContext());

        result.IsValid.Should().BeFalse();
        result.Findings.Should().HaveCount(3);
        result.Findings.Should().Contain(f => f.Severity == ValidationSeverity.Warning);
        result.Findings.Should().Contain(f => f.Severity == ValidationSeverity.Error);
        result.Findings.Should().Contain(f => f.Severity == ValidationSeverity.Info);
    }

    [Fact]
    public async Task ValidateAsync_Only_Warnings_And_Info_Returns_Valid()
    {
        var engine = CreateEngine(entityValidators:
        [
            new WarningValidator(),
            new InfoValidator(),
        ]);

        var result = await engine.ValidateAsync(new TestEntity(), new FakeActorContext());

        result.IsValid.Should().BeTrue();
        result.Findings.Should().HaveCount(2);
    }

    [Fact]
    public async Task ValidateAsync_Throwing_Validator_Returns_Error_Finding()
    {
        var engine = CreateEngine(entityValidators:
        [
            new ThrowingValidator(),
            new AlwaysValidValidator(),
        ]);

        var result = await engine.ValidateAsync(new TestEntity(), new FakeActorContext());

        result.IsValid.Should().BeFalse();
        result.Findings.Should().ContainSingle(f => f.Code == "VAL-ENGINE-ERR");
    }

    [Fact]
    public async Task ValidateAsync_Respects_Cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var engine = CreateEngine(entityValidators: [new AlwaysValidValidator()]);

        var act = () => engine.ValidateAsync(new TestEntity(), new FakeActorContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ValidateAsync_Passes_Entity_And_Actor_To_Validator()
    {
        var capturingValidator = new CapturingValidator();
        var engine = CreateEngine(entityValidators: [capturingValidator]);

        var entity = new TestEntity { Name = "Test" };
        var actor = new FakeActorContext();

        await engine.ValidateAsync(entity, actor);

        capturingValidator.CapturedEntity.Should().BeSameAs(entity);
        capturingValidator.CapturedActor.Should().BeSameAs(actor);
    }

    private static IValidationEngine CreateEngine(
        IEntityValidator<TestEntity>[]? entityValidators = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        foreach (var v in entityValidators ?? [])
        {
            services.AddSingleton<IEntityValidator<TestEntity>>(v);
        }

        services.AddScoped<IValidationEngine, ValidationEngine>();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IValidationEngine>();
    }

    public sealed record TestEntity
    {
        public string? Name { get; init; }
    }

    private sealed class FakeActorContext : IActorContext
    {
        public Guid UserId { get; } = Guid.NewGuid();

        public Guid CorrelationId { get; } = Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName => "Test";

        public string? Email => "test@test.com";

        public Guid? CompanyId => null;

        public string? Timezone => null;

        public string? Language => null;

        public string? TenantId => null;
    }

    private sealed class AlwaysValidValidator : IEntityValidator<TestEntity>
    {
        public Task<ValidationResult> ValidateAsync(TestEntity entity, IActorContext actor, CancellationToken ct = default) =>
            Task.FromResult(ValidationResult.Valid());
    }

    private sealed class AlwaysInvalidValidator(string message, string? field = null, string? code = null)
        : IEntityValidator<TestEntity>
    {
        public Task<ValidationResult> ValidateAsync(TestEntity entity, IActorContext actor, CancellationToken ct = default) =>
            Task.FromResult(ValidationResult.Invalid(message, field, code));
    }

    private sealed class TrackingInvalidValidator(string name, List<string> executionOrder)
        : IEntityValidator<TestEntity>
    {
        public Task<ValidationResult> ValidateAsync(TestEntity entity, IActorContext actor, CancellationToken ct = default)
        {
            executionOrder.Add(name);
            return Task.FromResult(ValidationResult.Invalid($"Error from {name}"));
        }
    }

    private sealed class WarningValidator : IEntityValidator<TestEntity>
    {
        public Task<ValidationResult> ValidateAsync(TestEntity entity, IActorContext actor, CancellationToken ct = default) =>
            Task.FromResult(ValidationResult.Valid(
            [
                new ValidationFinding
                {
                    Severity = ValidationSeverity.Warning,
                    Message = "A warning",
                },
            ]));
    }

    private sealed class InfoValidator : IEntityValidator<TestEntity>
    {
        public Task<ValidationResult> ValidateAsync(TestEntity entity, IActorContext actor, CancellationToken ct = default) =>
            Task.FromResult(ValidationResult.Valid(
            [
                new ValidationFinding
                {
                    Severity = ValidationSeverity.Info,
                    Message = "An info",
                },
            ]));
    }

    private sealed class ThrowingValidator : IEntityValidator<TestEntity>
    {
        public Task<ValidationResult> ValidateAsync(TestEntity entity, IActorContext actor, CancellationToken ct = default) =>
            throw new InvalidOperationException("Boom!");
    }

    private sealed class CapturingValidator : IEntityValidator<TestEntity>
    {
        public TestEntity? CapturedEntity { get; private set; }

        public IActorContext? CapturedActor { get; private set; }

        public Task<ValidationResult> ValidateAsync(TestEntity entity, IActorContext actor, CancellationToken ct = default)
        {
            CapturedEntity = entity;
            CapturedActor = actor;
            return Task.FromResult(ValidationResult.Valid());
        }
    }
}
