namespace Stratum.Common.Infrastructure.Tests.Unit.Database;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Infrastructure.Database;
using Xunit;

#pragma warning disable CA1001 // Disposal handled by IAsyncLifetime.DisposeAsync (xUnit test lifecycle)
public sealed class NpgsqlDataSourceRegistryTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private readonly NpgsqlDataSourceRegistry _registry = new(
        NullLoggerFactory.Instance.CreateLogger<NpgsqlDataSourceRegistry>());

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public void GetOrCreate_Should_CreateDataSource_When_CalledFirstTime()
    {
        var ds = _registry.GetOrCreate("tenant-a", "Host=localhost;Database=test_a");

        Assert.NotNull(ds);
        Assert.Equal(1, _registry.Count);
    }

    [Fact]
    public void GetOrCreate_Should_ReturnSameInstance_When_CalledTwiceForSameKey()
    {
        var ds1 = _registry.GetOrCreate("tenant-b", "Host=localhost;Database=test_b");
        var ds2 = _registry.GetOrCreate("tenant-b", "Host=localhost;Database=test_b");

        Assert.Same(ds1, ds2);
        Assert.Equal(1, _registry.Count);
    }

    [Fact]
    public void GetOrCreate_Should_CreateSeparateDataSources_When_DifferentKeys()
    {
        var ds1 = _registry.GetOrCreate("tenant-c", "Host=localhost;Database=test_c");
        var ds2 = _registry.GetOrCreate("tenant-d", "Host=localhost;Database=test_d");

        Assert.NotSame(ds1, ds2);
        Assert.Equal(2, _registry.Count);
    }

    [Fact]
    public void GetOrCreate_Should_BeThreadSafe_When_CalledConcurrently()
    {
        const string key = "concurrent-tenant";
        const string cs = "Host=localhost;Database=test_concurrent";
        const int concurrency = 20;

        var results = new Npgsql.NpgsqlDataSource[concurrency];
        var barrier = new Barrier(concurrency);

        Parallel.For(0, concurrency, i =>
        {
            barrier.SignalAndWait();
            results[i] = _registry.GetOrCreate(key, cs);
        });

        // All results must be the same instance
        var expected = results[0];
        Assert.All(results, ds => Assert.Same(expected, ds));
        Assert.Equal(1, _registry.Count);
    }

    [Fact]
    public async Task DisposeAsync_Should_DisposeAllDataSources()
    {
        var registry = new NpgsqlDataSourceRegistry(
            NullLoggerFactory.Instance.CreateLogger<NpgsqlDataSourceRegistry>());

        registry.GetOrCreate("dispose-a", "Host=localhost;Database=dispose_a");
        registry.GetOrCreate("dispose-b", "Host=localhost;Database=dispose_b");

        Assert.Equal(2, registry.Count);

        await registry.DisposeAsync();

        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task GetOrCreate_Should_ThrowObjectDisposedException_When_RegistryIsDisposed()
    {
        var registry = new NpgsqlDataSourceRegistry(
            NullLoggerFactory.Instance.CreateLogger<NpgsqlDataSourceRegistry>());

        await registry.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() =>
            registry.GetOrCreate("after-dispose", "Host=localhost;Database=nope"));
    }

    [Fact]
    public async Task DisposeAsync_Should_BeIdempotent_When_CalledTwice()
    {
        var registry = new NpgsqlDataSourceRegistry(
            NullLoggerFactory.Instance.CreateLogger<NpgsqlDataSourceRegistry>());

        registry.GetOrCreate("idempotent", "Host=localhost;Database=idempotent");

        await registry.DisposeAsync();
        await registry.DisposeAsync(); // second call should not throw
    }

    [Fact]
    public void GetOrCreate_Should_ReturnOriginalInstance_When_CalledWithDifferentConnectionString()
    {
        var ds1 = _registry.GetOrCreate("mismatch-tenant", "Host=localhost;Database=original");
        var ds2 = _registry.GetOrCreate("mismatch-tenant", "Host=localhost;Database=different");

        Assert.Same(ds1, ds2);
        Assert.Equal(1, _registry.Count);
    }
}
