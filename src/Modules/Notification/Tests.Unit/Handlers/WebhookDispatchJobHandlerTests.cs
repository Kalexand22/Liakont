namespace Stratum.Modules.Notification.Tests.Unit.Handlers;

using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Infrastructure.Handlers.Jobs;
using Stratum.Modules.Notification.Tests.Unit.Fakes;
using Xunit;

public class WebhookDispatchJobHandlerTests
{
    [Fact]
    public async Task HandleAsync_Should_Post_Payload_With_Signature_Header()
    {
        var capturedRequest = new CapturedHttpRequest();
        var httpFactory = CreateHttpClientFactory(capturedRequest, HttpStatusCode.OK);
        var subscription = CreateSubscription();
        var uowFactory = new FakeNotificationUnitOfWorkFactory(existingWebhook: subscription);
        var handler = new WebhookDispatchJobHandler(httpFactory, uowFactory, NullLogger<WebhookDispatchJobHandler>.Instance);

        var payload = CreatePayload(subscription.Id);

        await handler.HandleAsync(payload);

        capturedRequest.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri.Should().Be("https://example.com/hook");
        capturedRequest.Headers.Should().ContainKey("X-Stratum-Signature");
        capturedRequest.Headers["X-Stratum-Signature"].Should().StartWith("sha256=");
        capturedRequest.Headers.Should().ContainKey("X-Stratum-Event");
        capturedRequest.Headers["X-Stratum-Event"].Should().Be("notification.email.sent");
        capturedRequest.Body.Should().Be("""{"event":"test"}""");
        uowFactory.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_Throw_On_Http_Failure()
    {
        var capturedRequest = new CapturedHttpRequest();
        var httpFactory = CreateHttpClientFactory(capturedRequest, HttpStatusCode.InternalServerError);
        var subscription = CreateSubscription();
        var uowFactory = new FakeNotificationUnitOfWorkFactory(existingWebhook: subscription);
        var handler = new WebhookDispatchJobHandler(httpFactory, uowFactory, NullLogger<WebhookDispatchJobHandler>.Instance);

        var payload = CreatePayload(subscription.Id);

        var act = () => handler.HandleAsync(payload);

        await act.Should().ThrowAsync<HttpRequestException>();
        uowFactory.Committed.Should().BeTrue(); // failed event published
    }

    private static WebhookSubscription CreateSubscription() =>
        WebhookSubscription.Create(
            "Test Webhook",
            "notification.email.sent",
            "https://example.com/hook",
            "abcdefghijklmnopqrstuvwxyz0123456789",
            Guid.NewGuid());

    private static WebhookDispatchJobPayload CreatePayload(Guid subscriptionId) => new()
    {
        SubscriptionId = subscriptionId,
        EventType = "notification.email.sent",
        TargetUrl = "https://example.com/hook",
        PayloadJson = """{"event":"test"}""",
    };

    private static FakeHttpClientFactory CreateHttpClientFactory(CapturedHttpRequest captured, HttpStatusCode statusCode)
        => new(captured, statusCode);

    private sealed class CapturedHttpRequest
    {
        public HttpMethod? Method { get; set; }

        public string? RequestUri { get; set; }

        public Dictionary<string, string> Headers { get; } = [];

        public string? Body { get; set; }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly CapturedHttpRequest _captured;
        private readonly HttpStatusCode _statusCode;

        public FakeHttpClientFactory(CapturedHttpRequest captured, HttpStatusCode statusCode)
        {
            _captured = captured;
            _statusCode = statusCode;
        }

        public HttpClient CreateClient(string name) =>
            new(new FakeHandler(_captured, _statusCode));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly CapturedHttpRequest _captured;
        private readonly HttpStatusCode _statusCode;

        public FakeHandler(CapturedHttpRequest captured, HttpStatusCode statusCode)
        {
            _captured = captured;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _captured.Method = request.Method;
            _captured.RequestUri = request.RequestUri?.ToString();

            foreach (var header in request.Headers)
            {
                _captured.Headers[header.Key] = string.Join(",", header.Value);
            }

            if (request.Content is not null)
            {
                _captured.Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode);
        }
    }
}
