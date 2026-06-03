namespace Stratum.Modules.Notification.Tests.Unit.Handlers;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;
using Stratum.Modules.Notification.Infrastructure.Services;
using Stratum.Modules.Notification.Tests.Unit.Fakes;
using Xunit;

public class NotificationSenderTests
{
    [Fact]
    public async Task SendEmailAsync_Should_Resolve_Template_And_Enqueue_Job()
    {
        var templateDto = new EmailTemplateDto
        {
            Id = Guid.NewGuid(),
            Code = "WELCOME",
            SubjectTemplate = "Hello {{Name}}",
            BodyTemplate = "<p>Welcome {{Name}} to {{App}}</p>",
            LanguageCode = "en",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var queries = new FakeEmailTemplateQueries(templateDto);
        var jobQueue = new FakeJobQueue();
        var sender = new NotificationSender(queries, jobQueue, new FakeRoutingEngine(), new FakeNotificationUnitOfWorkFactory(), NullLogger<NotificationSender>.Instance);

        var placeholders = new Dictionary<string, string>
        {
            ["Name"] = "Alice",
            ["App"] = "Stratum",
        };

        await sender.SendEmailAsync("WELCOME", "en", "alice@example.com", placeholders);

        jobQueue.LastPayload.Should().NotBeNull();
        var payload = jobQueue.LastPayload.Should().BeOfType<EmailSendJobPayload>().Subject;
        payload.RecipientEmail.Should().Be("alice@example.com");
        payload.Subject.Should().Be("Hello Alice");
        payload.Body.Should().Be("<p>Welcome Alice to Stratum</p>");
        payload.TemplateCode.Should().Be("WELCOME");
    }

    [Fact]
    public async Task SendEmailAsync_Should_Throw_When_TemplateNotFound()
    {
        var queries = new FakeEmailTemplateQueries(null);
        var jobQueue = new FakeJobQueue();
        var sender = new NotificationSender(queries, jobQueue, new FakeRoutingEngine(), new FakeNotificationUnitOfWorkFactory(), NullLogger<NotificationSender>.Instance);

        var act = () => sender.SendEmailAsync("MISSING", "en", "alice@example.com", new Dictionary<string, string>());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*INV-NOTIF-007*");
    }

    [Fact]
    public async Task SendEmailAsync_Should_Throw_When_EmptyRecipient()
    {
        var queries = new FakeEmailTemplateQueries(null);
        var jobQueue = new FakeJobQueue();
        var sender = new NotificationSender(queries, jobQueue, new FakeRoutingEngine(), new FakeNotificationUnitOfWorkFactory(), NullLogger<NotificationSender>.Instance);

        var act = () => sender.SendEmailAsync("WELCOME", "en", string.Empty, new Dictionary<string, string>());

        (await act.Should().ThrowAsync<ArgumentException>())
            .WithMessage("*INV-NOTIF-006*");
    }

    [Fact]
    public async Task SendRoutedNotificationsAsync_Should_Return_Early_When_NoMatches()
    {
        var queries = new FakeEmailTemplateQueries(null);
        var jobQueue = new FakeJobQueue();
        var sender = new NotificationSender(queries, jobQueue, new FakeRoutingEngine(), new FakeNotificationUnitOfWorkFactory(), NullLogger<NotificationSender>.Instance);

        await sender.SendRoutedNotificationsAsync(
            "reservation",
            "REQ-001",
            new Dictionary<string, System.Text.Json.JsonElement>(),
            "reservation-routing",
            "fr",
            new Dictionary<string, string>());

        jobQueue.EnqueuedCount.Should().Be(0);
    }

    [Fact]
    public async Task SendRoutedNotificationsAsync_Should_Throw_When_TemplateNotFound()
    {
        var routingEngine = new ConfigurableRoutingEngine([
            new RoutingResultDto
            {
                RuleCode = "test",
                RuleName = "Test Service",
                ServiceCode = "SVC01",
                RecipientType = "ServiceEmail",
                RecipientValue = "svc@test.com",
                Priority = 1,
            },
        ]);
        var queries = new FakeEmailTemplateQueries(null);
        var jobQueue = new FakeJobQueue();
        var sender = new NotificationSender(queries, jobQueue, routingEngine, new FakeNotificationUnitOfWorkFactory(), NullLogger<NotificationSender>.Instance);

        var act = () => sender.SendRoutedNotificationsAsync(
            "reservation",
            "REQ-001",
            new Dictionary<string, System.Text.Json.JsonElement>(),
            "reservation-routing",
            "fr",
            new Dictionary<string, string>());

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*INV-NOTIF-007*");
    }

    [Fact]
    public async Task SendRoutedNotificationsAsync_Should_Enqueue_Jobs_For_Matched_Services()
    {
        var templateDto = new EmailTemplateDto
        {
            Id = Guid.NewGuid(),
            Code = "reservation-routing",
            SubjectTemplate = "New request for {{SERVICE_NAME}}",
            BodyTemplate = "<p>Service: {{SERVICE_CODE}}</p>",
            LanguageCode = "fr",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var routingEngine = new ConfigurableRoutingEngine([
            new RoutingResultDto
            {
                RuleCode = "voirie",
                RuleName = "Service Voirie",
                ServiceCode = "SVC_VOIRIE",
                RecipientType = "ServiceEmail",
                RecipientValue = "voirie@mairie.fr",
                Priority = 1,
            },
            new RoutingResultDto
            {
                RuleCode = "technique",
                RuleName = "Service Technique",
                ServiceCode = "SVC_TECH",
                RecipientType = "ServiceEmail",
                RecipientValue = "technique@mairie.fr",
                Priority = 2,
            },
        ]);

        var queries = new FakeEmailTemplateQueries(templateDto);
        var jobQueue = new FakeJobQueue();
        var sender = new NotificationSender(queries, jobQueue, routingEngine, new FakeNotificationUnitOfWorkFactory(), NullLogger<NotificationSender>.Instance);

        await sender.SendRoutedNotificationsAsync(
            "reservation",
            "REQ-001",
            new Dictionary<string, System.Text.Json.JsonElement>(),
            "reservation-routing",
            "fr",
            new Dictionary<string, string>());

        jobQueue.EnqueuedCount.Should().Be(2);
        jobQueue.AllPayloads.Should().AllBeOfType<EmailSendJobPayload>();

        var first = (EmailSendJobPayload)jobQueue.AllPayloads[0];
        first.RecipientEmail.Should().Be("voirie@mairie.fr");
        first.Subject.Should().Be("New request for Service Voirie");

        var second = (EmailSendJobPayload)jobQueue.AllPayloads[1];
        second.RecipientEmail.Should().Be("technique@mairie.fr");
    }

    private sealed class ConfigurableRoutingEngine : IRoutingEngine
    {
        private readonly IReadOnlyList<RoutingResultDto> _results;

        public ConfigurableRoutingEngine(IReadOnlyList<RoutingResultDto> results)
        {
            _results = results;
        }

        public Task<IReadOnlyList<RoutingResultDto>> EvaluateRoutingAsync(
            string entityType,
            IReadOnlyDictionary<string, System.Text.Json.JsonElement> data,
            Guid? companyId,
            CancellationToken ct)
        {
            return Task.FromResult(_results);
        }
    }

    private sealed class FakeEmailTemplateQueries : IEmailTemplateQueries
    {
        private readonly EmailTemplateDto? _template;

        public FakeEmailTemplateQueries(EmailTemplateDto? template)
        {
            _template = template;
        }

        public Task<EmailTemplateDto?> GetByCode(string code, string languageCode, Guid? companyId, CancellationToken ct = default)
            => Task.FromResult(_template);

        public Task<EmailTemplateDto?> GetById(Guid templateId, CancellationToken ct = default)
            => Task.FromResult(_template);

        public Task<IReadOnlyList<EmailTemplateDto>> List(Guid? companyId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmailTemplateDto>>(Array.Empty<EmailTemplateDto>());
    }

    private sealed class FakeJobQueue : IJobQueue
    {
        public object? LastPayload { get; private set; }

        public List<object> AllPayloads { get; } = [];

        public int EnqueuedCount => AllPayloads.Count;

        public Task<Guid> EnqueueAsync<T>(T payload, int priority = 0, DateTimeOffset? scheduledAt = null, Guid? companyId = null, CancellationToken ct = default)
        {
            LastPayload = payload;
            AllPayloads.Add(payload!);
            return Task.FromResult(Guid.NewGuid());
        }
    }
}
