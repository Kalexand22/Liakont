namespace Stratum.Common.Infrastructure.Http;

using System.IO;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

public class ErrorHandlingMiddlewareTests
{
    private static ErrorHandlingMiddleware CreateMiddleware(RequestDelegate next)
        => new(next, NullLogger<ErrorHandlingMiddleware>.Instance);

    private static DefaultHttpContext CreateContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static async Task<JsonElement> ReadBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
    }

    [Fact]
    public async Task InvokeAsync_Should_PassThrough_When_NoException()
    {
        var ctx = CreateContext();
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx);

        called.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_Should_Return404_When_NotFoundException()
    {
        var ctx = CreateContext();
        var middleware = CreateMiddleware(_ => throw new NotFoundException("Order", Guid.Empty));

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        ctx.Response.ContentType.Should().Contain("application/problem+json");

        var body = await ReadBody(ctx);
        body.GetProperty("status").GetInt32().Should().Be(404);
        body.GetProperty("title").GetString().Should().Be("Resource Not Found");
        body.GetProperty("detail").GetString().Should().Contain("was not found");
    }

    [Fact]
    public async Task InvokeAsync_Should_Return409_When_ConflictException()
    {
        var ctx = CreateContext();
        var middleware = CreateMiddleware(_ => throw new ConflictException("Duplicate code."));

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        var body = await ReadBody(ctx);
        body.GetProperty("status").GetInt32().Should().Be(409);
        body.GetProperty("title").GetString().Should().Be("Conflict");
        body.GetProperty("detail").GetString().Should().Be("Duplicate code.");
    }

    [Fact]
    public async Task InvokeAsync_Should_Return400_When_DomainException()
    {
        var ctx = CreateContext();
        var middleware = CreateMiddleware(_ => throw new ConcreteDomainException("Business rule violated."));

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        var body = await ReadBody(ctx);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("detail").GetString().Should().Be("Business rule violated.");
    }

    [Fact]
    public async Task InvokeAsync_Should_Return401_When_UnauthorizedAccessException()
    {
        var ctx = CreateContext();
        var middleware = CreateMiddleware(_ => throw new UnauthorizedAccessException("Access denied."));

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        var body = await ReadBody(ctx);
        body.GetProperty("status").GetInt32().Should().Be(401);

        // Detail must not be exposed for 401
        body.TryGetProperty("detail", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Should_Return500_When_UnhandledException()
    {
        var ctx = CreateContext();
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("Internal failure."));

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        var body = await ReadBody(ctx);
        body.GetProperty("status").GetInt32().Should().Be(500);

        // Detail must not be exposed for 500
        body.TryGetProperty("detail", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Should_IncludeInstance_From_RequestPath()
    {
        var ctx = CreateContext();
        ctx.Request.Path = "/api/orders/42";
        var middleware = CreateMiddleware(_ => throw new NotFoundException("Order", 42));

        await middleware.InvokeAsync(ctx);

        var body = await ReadBody(ctx);
        body.GetProperty("instance").GetString().Should().Be("/api/orders/42");
    }

    [Fact]
    public async Task InvokeAsync_Should_Propagate_When_ResponseHasStarted()
    {
        var ctx = CreateContext();

        // DefaultHttpContext's in-memory response feature never flips HasStarted, even after
        // StartAsync(). Force it true so the middleware's `when (!HasStarted)` guard is false.
        ctx.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var middleware = CreateMiddleware(_ => throw new NotFoundException("Thing", 1));

        var act = async () => await middleware.InvokeAsync(ctx);

        // The when-guard is false when response has started; exception propagates unhandled
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task InvokeAsync_Should_IncludeType_With_HttpStatus()
    {
        var ctx = CreateContext();
        var middleware = CreateMiddleware(_ => throw new NotFoundException("X", 1));

        await middleware.InvokeAsync(ctx);

        var body = await ReadBody(ctx);
        body.GetProperty("type").GetString().Should().Be("about:blank");
    }

    /// <summary>Concrete subclass of DomainException for testing the base case.</summary>
    private sealed class ConcreteDomainException : DomainException
    {
        public ConcreteDomainException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Response feature that genuinely reports HasStarted == true, unlike the default
    /// in-memory feature on DefaultHttpContext which stays false even after StartAsync().
    /// </summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = new MemoryStream();

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }
}
