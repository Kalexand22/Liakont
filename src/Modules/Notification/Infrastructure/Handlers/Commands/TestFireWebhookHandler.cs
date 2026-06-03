namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Domain.Services;

public sealed class TestFireWebhookHandler : IRequestHandler<TestFireWebhookCommand, TestFireWebhookResult>
{
    private readonly INotificationUnitOfWorkFactory _uowFactory;
    private readonly IHttpClientFactory _httpClientFactory;

    public TestFireWebhookHandler(
        INotificationUnitOfWorkFactory uowFactory,
        IHttpClientFactory httpClientFactory)
    {
        _uowFactory = uowFactory;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<TestFireWebhookResult> Handle(TestFireWebhookCommand request, CancellationToken cancellationToken)
    {
        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        var subscription = await uow.GetWebhookSubscriptionByIdAsync(request.SubscriptionId, cancellationToken)
            ?? throw new NotFoundException("WebhookSubscription", request.SubscriptionId);

        if (subscription.CompanyId != request.CompanyId)
        {
            throw new NotFoundException("WebhookSubscription", request.SubscriptionId);
        }

        var testPayload = JsonSerializer.Serialize(new
        {
            test = true,
            event_type = "webhook.test",
            subscription_id = subscription.Id,
            timestamp = DateTimeOffset.UtcNow,
            message = "This is a test payload from Stratum ERP.",
        });

        var signature = WebhookSignature.Compute(testPayload, subscription.Secret);

        try
        {
            using var client = _httpClientFactory.CreateClient("WebhookDispatch");
            client.Timeout = TimeSpan.FromSeconds(10);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, subscription.TargetUrl);
            httpRequest.Content = new StringContent(testPayload, Encoding.UTF8, "application/json");
            httpRequest.Headers.Add("X-Stratum-Signature", signature);
            httpRequest.Headers.Add("X-Stratum-Event", "webhook.test");

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            return new TestFireWebhookResult
            {
                Success = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode,
            };
        }
        catch (TaskCanceledException)
        {
            return new TestFireWebhookResult { Success = false, StatusCode = 0, ErrorMessage = "Timeout (10s)" };
        }
        catch (HttpRequestException ex)
        {
            return new TestFireWebhookResult { Success = false, StatusCode = 0, ErrorMessage = ex.Message };
        }
    }
}
