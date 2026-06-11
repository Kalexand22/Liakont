namespace Stratum.Modules.Job.Tests.Unit;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Job.Infrastructure;
using Xunit;

public class JobHandlerResolverTests
{
    public enum EnumMode
    {
        Fast,
        Slow,
    }

    [Fact]
    public void CanHandle_Should_Return_True_For_Registered_Type()
    {
        var (resolver, _) = CreateResolver<TestPayload, TestPayloadHandler>();

        resolver.CanHandle(typeof(TestPayload).FullName!).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_Should_Return_False_For_Unregistered_Type()
    {
        var (resolver, _) = CreateResolver<TestPayload, TestPayloadHandler>();

        resolver.CanHandle("UnknownType").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_Invoke_Handler()
    {
        var handler = new TestPayloadHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IJobHandler<TestPayload>>(handler);
        var sp = services.BuildServiceProvider();

        var registrations = new[] { new JobHandlerRegistration(typeof(TestPayload)) };
        var resolver = new JobHandlerResolver(registrations);

        await resolver.ExecuteAsync(sp, typeof(TestPayload).FullName!, """{"Message":"hello"}""", CancellationToken.None);

        handler.LastPayload.Should().NotBeNull();
        handler.LastPayload!.Message.Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteAsync_Should_Throw_For_Unregistered_Type()
    {
        var (resolver, sp) = CreateResolver<TestPayload, TestPayloadHandler>();

        var act = () => resolver.ExecuteAsync(sp, "UnknownType", "{}", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No handler registered*");
    }

    [Fact]
    public async Task ExecuteAsync_Should_Throw_When_Handler_Not_In_DI()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var registrations = new[] { new JobHandlerRegistration(typeof(TestPayload)) };
        var resolver = new JobHandlerResolver(registrations);

        var act = () => resolver.ExecuteAsync(sp, typeof(TestPayload).FullName!, """{"Message":"hi"}""", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No IJobHandler*");
    }

    [Fact]
    public async Task ExecuteAsync_Should_Throw_When_ScopedServices_Is_Null()
    {
        var (resolver, _) = CreateResolver<TestPayload, TestPayloadHandler>();

        var act = () => resolver.ExecuteAsync(
            null!, typeof(TestPayload).FullName!, "{}", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Regression: the resolver used to capture the root <see cref="IServiceProvider"/>
    /// in its constructor and resolve scoped handlers from it, which fails under
    /// service-provider scope validation. This test proves we can resolve a scoped
    /// handler from a real scope and that the same setup throws when resolving from
    /// the root provider with validation enabled.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_Should_Resolve_Scoped_Handler_From_Scope()
    {
        var services = new ServiceCollection();
        services.AddScoped<IJobHandler<TestPayload>, TestPayloadHandler>();
        var rootProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });

        var registrations = new[] { new JobHandlerRegistration(typeof(TestPayload)) };
        var resolver = new JobHandlerResolver(registrations);

        // Resolving from a real scope must succeed.
        await using (var scope = rootProvider.CreateAsyncScope())
        {
            await resolver.ExecuteAsync(
                scope.ServiceProvider,
                typeof(TestPayload).FullName!,
                """{"Message":"scoped"}""",
                CancellationToken.None);

            var handler = (TestPayloadHandler)scope.ServiceProvider
                .GetRequiredService<IJobHandler<TestPayload>>();
            handler.LastPayload!.Message.Should().Be("scoped");
        }

        // Resolving from the root provider must fail (this is exactly the
        // production bug that motivated changing the API).
        var fromRoot = () => resolver.ExecuteAsync(
            rootProvider,
            typeof(TestPayload).FullName!,
            "{}",
            CancellationToken.None);

        await fromRoot.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*scope*");
    }

    [Fact]
    public async Task ExecuteAsync_Should_Deserialize_String_Enum_Payload()
    {
        var handler = new EnumPayloadHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IJobHandler<EnumPayload>>(handler);
        var sp = services.BuildServiceProvider();
        var resolver = new JobHandlerResolver(new[] { new JobHandlerRegistration(typeof(EnumPayload)) });

        await resolver.ExecuteAsync(sp, typeof(EnumPayload).FullName!, """{"Mode":"Slow"}""", CancellationToken.None);

        handler.LastPayload!.Mode.Should().Be(EnumMode.Slow);
    }

    private static (JobHandlerResolver Resolver, IServiceProvider Services) CreateResolver<TPayload, THandler>()
        where THandler : class, IJobHandler<TPayload>, new()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJobHandler<TPayload>>(new THandler());
        var sp = services.BuildServiceProvider();
        var registrations = new[] { new JobHandlerRegistration(typeof(TPayload)) };
        return (new JobHandlerResolver(registrations), sp);
    }

    public record TestPayload
    {
        public string Message { get; init; } = string.Empty;
    }

    public class TestPayloadHandler : IJobHandler<TestPayload>
    {
        public TestPayload? LastPayload { get; private set; }

        public Task HandleAsync(TestPayload payload, CancellationToken ct = default)
        {
            LastPayload = payload;
            return Task.CompletedTask;
        }
    }

    public record EnumPayload
    {
        public EnumMode Mode { get; init; }
    }

    public class EnumPayloadHandler : IJobHandler<EnumPayload>
    {
        public EnumPayload? LastPayload { get; private set; }

        public Task HandleAsync(EnumPayload payload, CancellationToken ct = default)
        {
            LastPayload = payload;
            return Task.CompletedTask;
        }
    }
}
