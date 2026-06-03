namespace Stratum.Common.Infrastructure.Tests.Unit.FieldChange;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.FieldChange;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Abstractions.UiRules;
using Stratum.Common.Infrastructure.FieldChange;
using Xunit;

public sealed class FieldChangeEngineTests : IDisposable
{
    private readonly ServiceCollection _services = new();

    public FieldChangeEngineTests()
    {
        _services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _services.AddScoped<IFieldChangeEngine, FieldChangeEngine>();
    }

    public void Dispose()
    {
        FieldChangeEngine.ResetCache();
    }

    [Fact]
    public async Task ProcessChangesAsync_NoHandlers_Returns_Empty()
    {
        var engine = BuildEngine();

        var result = await engine.ProcessChangesAsync(
            new InvoiceEntity { Total = 100m },
            new HashSet<string> { nameof(InvoiceEntity.Total) },
            new FakeActorContext());

        result.FieldsToSet.Should().BeEmpty();
        result.UiAttributes.Should().BeNull();
    }

    [Fact]
    public async Task ProcessChangesAsync_EmptyChangedFields_Returns_Empty()
    {
        _services.AddSingleton<IFieldChangeHandler<InvoiceEntity>, InvoiceTotalHandler>();
        var engine = BuildEngine();

        var result = await engine.ProcessChangesAsync(
            new InvoiceEntity { Total = 100m },
            new HashSet<string>(),
            new FakeActorContext());

        result.FieldsToSet.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessChangesAsync_Discovers_And_Executes_Handler()
    {
        _services.AddSingleton<IFieldChangeHandler<InvoiceEntity>, InvoiceTotalHandler>();
        var engine = BuildEngine();

        var result = await engine.ProcessChangesAsync(
            new InvoiceEntity { Total = 100m },
            new HashSet<string> { nameof(InvoiceEntity.Total) },
            new FakeActorContext());

        result.FieldsToSet.Should().ContainKey(nameof(InvoiceEntity.TaxAmount));
        result.FieldsToSet[nameof(InvoiceEntity.TaxAmount)].Should().Be(20m);
    }

    [Fact]
    public async Task ProcessChangesAsync_Ignores_Unregistered_Fields()
    {
        _services.AddSingleton<IFieldChangeHandler<InvoiceEntity>, InvoiceTotalHandler>();
        var engine = BuildEngine();

        var result = await engine.ProcessChangesAsync(
            new InvoiceEntity { Total = 100m },
            new HashSet<string> { "NonExistentField" },
            new FakeActorContext());

        result.FieldsToSet.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessChangesAsync_Multiple_Handlers_Merged()
    {
        _services.AddSingleton<IFieldChangeHandler<InvoiceEntity>, InvoiceTotalHandler>();
        _services.AddSingleton<IFieldChangeHandler<InvoiceEntity>, InvoiceClientHandler>();
        var engine = BuildEngine();

        var result = await engine.ProcessChangesAsync(
            new InvoiceEntity { Total = 200m, ClientName = "Acme" },
            new HashSet<string> { nameof(InvoiceEntity.Total), nameof(InvoiceEntity.ClientName) },
            new FakeActorContext());

        result.FieldsToSet.Should().ContainKey(nameof(InvoiceEntity.TaxAmount));
        result.FieldsToSet[nameof(InvoiceEntity.TaxAmount)].Should().Be(40m);
        result.FieldsToSet.Should().ContainKey(nameof(InvoiceEntity.PaymentTerms));
        result.FieldsToSet[nameof(InvoiceEntity.PaymentTerms)].Should().Be("Net 30");
    }

    [Fact]
    public async Task ProcessChangesAsync_Cascade_Triggers_Dependent_Handler()
    {
        _services.AddSingleton<IFieldChangeHandler<CascadeEntity>, CascadeHandlerA>();
        _services.AddSingleton<IFieldChangeHandler<CascadeEntity>, CascadeHandlerB>();
        var engine = BuildEngine();

        var result = await engine.ProcessChangesAsync(
            new CascadeEntity(),
            new HashSet<string> { nameof(CascadeEntity.FieldA) },
            new FakeActorContext());

        result.FieldsToSet.Should().ContainKey(nameof(CascadeEntity.FieldB));
        result.FieldsToSet[nameof(CascadeEntity.FieldB)].Should().Be("from-A");
        result.FieldsToSet.Should().ContainKey(nameof(CascadeEntity.FieldC));
        result.FieldsToSet[nameof(CascadeEntity.FieldC)].Should().Be("from-B");
    }

    [Fact]
    public async Task ProcessChangesAsync_Cycle_Stopped_By_Processed_Fields_Tracking()
    {
        _services.AddSingleton<IFieldChangeHandler<LoopEntity>, LoopHandlerA>();
        _services.AddSingleton<IFieldChangeHandler<LoopEntity>, LoopHandlerB>();
        var engine = BuildEngine();

        // FieldX → sets FieldY, FieldY → sets FieldX.
        // The processedFields set prevents re-triggering already-handled fields,
        // stopping the cycle after 2 iterations (not infinite).
        var result = await engine.ProcessChangesAsync(
            new LoopEntity(),
            new HashSet<string> { nameof(LoopEntity.FieldX) },
            new FakeActorContext());

        result.FieldsToSet.Should().ContainKey(nameof(LoopEntity.FieldY));
        result.FieldsToSet.Should().ContainKey(nameof(LoopEntity.FieldX));
    }

    [Fact]
    public async Task ProcessChangesAsync_Respects_Cancellation()
    {
        _services.AddSingleton<IFieldChangeHandler<InvoiceEntity>, InvoiceTotalHandler>();
        var engine = BuildEngine();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => engine.ProcessChangesAsync(
            new InvoiceEntity { Total = 100m },
            new HashSet<string> { nameof(InvoiceEntity.Total) },
            new FakeActorContext(),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ProcessChangesAsync_Handler_Exception_Is_Swallowed_And_Logged()
    {
        _services.AddSingleton<IFieldChangeHandler<InvoiceEntity>, ThrowingHandler>();
        var engine = BuildEngine();

        var result = await engine.ProcessChangesAsync(
            new InvoiceEntity { Total = 100m },
            new HashSet<string> { nameof(InvoiceEntity.Total) },
            new FakeActorContext());

        result.FieldsToSet.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessChangesAsync_Async_Handler_Exception_Is_Swallowed_And_Logged()
    {
        _services.AddSingleton<IFieldChangeHandler<InvoiceEntity>, AsyncThrowingHandler>();
        var engine = BuildEngine();

        var result = await engine.ProcessChangesAsync(
            new InvoiceEntity { Total = 100m },
            new HashSet<string> { nameof(InvoiceEntity.Total) },
            new FakeActorContext());

        result.FieldsToSet.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessChangesAsync_Returns_UiAttributes_From_Handler()
    {
        _services.AddSingleton<IFieldChangeHandler<InvoiceEntity>, InvoiceUiHandler>();
        var engine = BuildEngine();

        var result = await engine.ProcessChangesAsync(
            new InvoiceEntity { Total = 0m },
            new HashSet<string> { nameof(InvoiceEntity.Total) },
            new FakeActorContext());

        result.UiAttributes.Should().NotBeNull();
        result.UiAttributes!.ContainsKey(nameof(InvoiceEntity.TaxAmount)).Should().BeTrue();
        result.UiAttributes[nameof(InvoiceEntity.TaxAmount)].ReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessChangesAsync_Invalid_Signature_Handler_Skipped_At_Discovery()
    {
        _services.AddSingleton<IFieldChangeHandler<InvoiceEntity>, VoidReturnHandler>();
        var engine = BuildEngine();

        var result = await engine.ProcessChangesAsync(
            new InvoiceEntity { Total = 100m },
            new HashSet<string> { nameof(InvoiceEntity.Total) },
            new FakeActorContext());

        result.FieldsToSet.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessChangesAsync_Wrong_Param_Handler_Skipped_At_Discovery()
    {
        _services.AddSingleton<IFieldChangeHandler<InvoiceEntity>, WrongParamHandler>();
        var engine = BuildEngine();

        var result = await engine.ProcessChangesAsync(
            new InvoiceEntity { Total = 100m },
            new HashSet<string> { nameof(InvoiceEntity.Total) },
            new FakeActorContext());

        result.FieldsToSet.Should().BeEmpty();
    }

    private IFieldChangeEngine BuildEngine()
    {
        var sp = _services.BuildServiceProvider();
        return sp.GetRequiredService<IFieldChangeEngine>();
    }

    public sealed class InvoiceEntity
    {
        public decimal Total { get; set; }

        public decimal TaxAmount { get; set; }

        public string? ClientName { get; set; }

        public string? PaymentTerms { get; set; }
    }

    public sealed class CascadeEntity
    {
        public string? FieldA { get; set; }

        public string? FieldB { get; set; }

        public string? FieldC { get; set; }
    }

    public sealed class LoopEntity
    {
        public string? FieldX { get; set; }

        public string? FieldY { get; set; }
    }

#pragma warning disable CA1822 // Handler methods must be instance methods for reflection-based discovery

    public sealed class InvoiceTotalHandler : IFieldChangeHandler<InvoiceEntity>
    {
        [OnChange(nameof(InvoiceEntity.Total))]
        public Task<FieldChangeResult> OnTotalChanged(FieldChangeContext<InvoiceEntity> ctx)
        {
            var tax = ctx.Entity.Total * 0.2m;
            return Task.FromResult(FieldChangeResult.WithFields(
                new Dictionary<string, object?> { [nameof(InvoiceEntity.TaxAmount)] = tax }));
        }
    }

    public sealed class InvoiceClientHandler : IFieldChangeHandler<InvoiceEntity>
    {
        [OnChange(nameof(InvoiceEntity.ClientName))]
        public Task<FieldChangeResult> OnClientChanged(FieldChangeContext<InvoiceEntity> ctx)
        {
            return Task.FromResult(FieldChangeResult.WithFields(
                new Dictionary<string, object?> { [nameof(InvoiceEntity.PaymentTerms)] = "Net 30" }));
        }
    }

    public sealed class CascadeHandlerA : IFieldChangeHandler<CascadeEntity>
    {
        [OnChange(nameof(CascadeEntity.FieldA))]
        public Task<FieldChangeResult> OnFieldAChanged(FieldChangeContext<CascadeEntity> ctx)
        {
            return Task.FromResult(FieldChangeResult.WithFields(
                new Dictionary<string, object?> { [nameof(CascadeEntity.FieldB)] = "from-A" }));
        }
    }

    public sealed class CascadeHandlerB : IFieldChangeHandler<CascadeEntity>
    {
        [OnChange(nameof(CascadeEntity.FieldB))]
        public Task<FieldChangeResult> OnFieldBChanged(FieldChangeContext<CascadeEntity> ctx)
        {
            return Task.FromResult(FieldChangeResult.WithFields(
                new Dictionary<string, object?> { [nameof(CascadeEntity.FieldC)] = "from-B" }));
        }
    }

    public sealed class LoopHandlerA : IFieldChangeHandler<LoopEntity>
    {
        [OnChange(nameof(LoopEntity.FieldX))]
        public Task<FieldChangeResult> OnFieldXChanged(FieldChangeContext<LoopEntity> ctx)
        {
            return Task.FromResult(FieldChangeResult.WithFields(
                new Dictionary<string, object?> { [nameof(LoopEntity.FieldY)] = "from-X" }));
        }
    }

    public sealed class LoopHandlerB : IFieldChangeHandler<LoopEntity>
    {
        [OnChange(nameof(LoopEntity.FieldY))]
        public Task<FieldChangeResult> OnFieldYChanged(FieldChangeContext<LoopEntity> ctx)
        {
            return Task.FromResult(FieldChangeResult.WithFields(
                new Dictionary<string, object?> { [nameof(LoopEntity.FieldX)] = "from-Y" }));
        }
    }

    public sealed class AsyncThrowingHandler : IFieldChangeHandler<InvoiceEntity>
    {
        [OnChange(nameof(InvoiceEntity.Total))]
        public async Task<FieldChangeResult> OnTotalChanged(FieldChangeContext<InvoiceEntity> ctx)
        {
            await Task.Yield();
            throw new InvalidOperationException("Simulated async failure");
        }
    }

    public sealed class VoidReturnHandler : IFieldChangeHandler<InvoiceEntity>
    {
        [OnChange(nameof(InvoiceEntity.Total))]
        public void OnTotalChanged(FieldChangeContext<InvoiceEntity> ctx)
        {
            // Invalid signature: void return instead of Task<FieldChangeResult>
        }
    }

    public sealed class WrongParamHandler : IFieldChangeHandler<InvoiceEntity>
    {
        [OnChange(nameof(InvoiceEntity.Total))]
        public Task<FieldChangeResult> OnTotalChanged(string wrongParam)
        {
            return Task.FromResult(FieldChangeResult.Empty());
        }
    }

    public sealed class ThrowingHandler : IFieldChangeHandler<InvoiceEntity>
    {
        [OnChange(nameof(InvoiceEntity.Total))]
        public Task<FieldChangeResult> OnTotalChanged(FieldChangeContext<InvoiceEntity> ctx)
        {
            throw new InvalidOperationException("Simulated failure");
        }
    }

    public sealed class InvoiceUiHandler : IFieldChangeHandler<InvoiceEntity>
    {
        [OnChange(nameof(InvoiceEntity.Total))]
        public Task<FieldChangeResult> OnTotalChanged(FieldChangeContext<InvoiceEntity> ctx)
        {
            var uiAttrs = new UiAttributeSet(new Dictionary<string, UiFieldAttributes>
            {
                [nameof(InvoiceEntity.TaxAmount)] = new() { ReadOnly = true },
            });

            return Task.FromResult(FieldChangeResult.WithFieldsAndUi(
                new Dictionary<string, object?>(),
                uiAttrs));
        }
    }

#pragma warning restore CA1822

    private sealed class FakeActorContext : IActorContext
    {
        public Guid UserId => Guid.NewGuid();

        public Guid CorrelationId => Guid.NewGuid();

        public bool IsAuthenticated => true;

        public string? DisplayName => "Test";

        public string? Email => "test@test.com";

        public Guid? CompanyId => null;

        public string? Timezone => null;

        public string? Language => null;

        public string? TenantId => null;
    }
}
