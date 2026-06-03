namespace Stratum.Modules.Notification.Infrastructure;

using System.Text.Json;
using Dapper;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Domain.ValueObjects;
using Stratum.Modules.Notification.Infrastructure.Services;

internal sealed class PostgresNotificationUnitOfWork : INotificationUnitOfWork
{
    private readonly TransactionScope _txn;
    private readonly IOutboxWriter _outboxWriter;

    private PostgresNotificationUnitOfWork(TransactionScope txn, IOutboxWriter outboxWriter)
    {
        _txn = txn;
        _outboxWriter = outboxWriter;
    }

    public static async Task<PostgresNotificationUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        IOutboxWriter outboxWriter,
        CancellationToken ct = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, ct);
        return new PostgresNotificationUnitOfWork(txn, outboxWriter);
    }

    public async Task InsertEmailTemplateAsync(EmailTemplate emailTemplate, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO notification.email_templates (id, code, subject_template, body_template, language_code, category, entity_type, template_links, company_id, created_at, updated_at)
            VALUES (@Id, @Code, @SubjectTemplate, @BodyTemplate, @LanguageCode, @Category, @EntityType, @TemplateLinks::jsonb, @CompanyId, @CreatedAt, @UpdatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                emailTemplate.Id,
                emailTemplate.Code,
                emailTemplate.SubjectTemplate,
                emailTemplate.BodyTemplate,
                emailTemplate.LanguageCode,
                Category = (int)emailTemplate.Category,
                emailTemplate.EntityType,
                TemplateLinks = TemplateLinkSerializer.Serialize(emailTemplate.TemplateLinks),
                emailTemplate.CompanyId,
                emailTemplate.CreatedAt,
                emailTemplate.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateEmailTemplateAsync(EmailTemplate emailTemplate, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notification.email_templates
            SET subject_template = @SubjectTemplate,
                body_template = @BodyTemplate,
                category = @Category,
                entity_type = @EntityType,
                template_links = @TemplateLinks::jsonb,
                updated_at = @UpdatedAt
            WHERE id = @Id
            """;

        var rowsAffected = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                emailTemplate.Id,
                emailTemplate.SubjectTemplate,
                emailTemplate.BodyTemplate,
                Category = (int)emailTemplate.Category,
                emailTemplate.EntityType,
                TemplateLinks = TemplateLinkSerializer.Serialize(emailTemplate.TemplateLinks),
                emailTemplate.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));

        if (rowsAffected != 1)
        {
            throw new InvalidOperationException(
                $"Email template '{emailTemplate.Id}' was not found or was deleted concurrently.");
        }
    }

    public async Task<EmailTemplate?> GetEmailTemplateByIdAsync(Guid emailTemplateId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, code, subject_template, body_template, language_code, category, entity_type, template_links, company_id, created_at, updated_at
            FROM notification.email_templates
            WHERE id = @Id
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = emailTemplateId }, _txn.Transaction, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return MapEmailTemplate(row);
    }

    public async Task InsertWebhookSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO notification.webhook_subscriptions (id, name, event_type, target_url, secret, is_active, company_id, created_at, updated_at)
            VALUES (@Id, @Name, @EventType, @TargetUrl, @Secret, @IsActive, @CompanyId, @CreatedAt, @UpdatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                subscription.Id,
                subscription.Name,
                subscription.EventType,
                subscription.TargetUrl,
                subscription.Secret,
                subscription.IsActive,
                subscription.CompanyId,
                subscription.CreatedAt,
                subscription.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateWebhookSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notification.webhook_subscriptions
            SET name       = @Name,
                event_type = @EventType,
                target_url = @TargetUrl,
                secret     = @Secret,
                is_active  = @IsActive,
                updated_at = @UpdatedAt
            WHERE id = @Id
            """;

        var rowsAffected = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                subscription.Id,
                subscription.Name,
                subscription.EventType,
                subscription.TargetUrl,
                subscription.Secret,
                subscription.IsActive,
                subscription.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));

        if (rowsAffected != 1)
        {
            throw new NotFoundException("WebhookSubscription", subscription.Id);
        }
    }

    public async Task DeleteWebhookSubscriptionAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM notification.webhook_subscriptions WHERE id = @Id";

        var rowsAffected = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = subscriptionId },
            _txn.Transaction,
            cancellationToken: ct));

        if (rowsAffected != 1)
        {
            throw new NotFoundException("WebhookSubscription", subscriptionId);
        }
    }

    public async Task<WebhookSubscription?> GetWebhookSubscriptionByIdAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, name, event_type, target_url, secret, is_active, company_id, created_at, updated_at
            FROM notification.webhook_subscriptions
            WHERE id = @Id
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = subscriptionId }, _txn.Transaction, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return WebhookSubscription.Reconstitute(
            (Guid)row.id,
            (string)row.name,
            (string)row.event_type,
            (string)row.target_url,
            (string)row.secret,
            (bool)row.is_active,
            (Guid)row.company_id,
            (DateTimeOffset)row.created_at,
            (DateTimeOffset?)row.updated_at);
    }

    public async Task InsertServiceDefinitionAsync(ServiceDefinition serviceDefinition, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO notification.service_definitions (id, code, name, email, description, is_active, company_id, manager_name, default_sla_hours, color, competences, created_at, updated_at)
            VALUES (@Id, @Code, @Name, @Email, @Description, @IsActive, @CompanyId, @ManagerName, @DefaultSlaHours, @Color, @Competences, @CreatedAt, @UpdatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                serviceDefinition.Id,
                serviceDefinition.Code,
                serviceDefinition.Name,
                serviceDefinition.Email,
                serviceDefinition.Description,
                serviceDefinition.IsActive,
                serviceDefinition.CompanyId,
                serviceDefinition.ManagerName,
                serviceDefinition.DefaultSlaHours,
                serviceDefinition.Color,
                serviceDefinition.Competences,
                serviceDefinition.CreatedAt,
                serviceDefinition.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateServiceDefinitionAsync(ServiceDefinition serviceDefinition, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notification.service_definitions
            SET name = @Name,
                email = @Email,
                description = @Description,
                is_active = @IsActive,
                manager_name = @ManagerName,
                default_sla_hours = @DefaultSlaHours,
                color = @Color,
                competences = @Competences,
                updated_at = @UpdatedAt
            WHERE id = @Id
            """;

        var rowsAffected = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                serviceDefinition.Id,
                serviceDefinition.Name,
                serviceDefinition.Email,
                serviceDefinition.Description,
                serviceDefinition.IsActive,
                serviceDefinition.ManagerName,
                serviceDefinition.DefaultSlaHours,
                serviceDefinition.Color,
                serviceDefinition.Competences,
                serviceDefinition.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));

        if (rowsAffected != 1)
        {
            throw new NotFoundException("ServiceDefinition", serviceDefinition.Id);
        }
    }

    public async Task DeleteServiceDefinitionAsync(Guid serviceDefinitionId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM notification.service_definitions WHERE id = @Id";

        var rowsAffected = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = serviceDefinitionId },
            _txn.Transaction,
            cancellationToken: ct));

        if (rowsAffected != 1)
        {
            throw new NotFoundException("ServiceDefinition", serviceDefinitionId);
        }
    }

    public async Task<bool> HasRoutingRulesForServiceCodeAsync(string serviceCode, CancellationToken ct = default)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM notification.routing_rules WHERE service_code = @ServiceCode)";

        return await _txn.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql,
            new { ServiceCode = serviceCode },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task<ServiceDefinition?> GetServiceDefinitionByIdAsync(Guid serviceDefinitionId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, code, name, email, description, is_active, company_id,
                   manager_name, default_sla_hours, color, competences,
                   created_at, updated_at
            FROM notification.service_definitions
            WHERE id = @Id
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = serviceDefinitionId }, _txn.Transaction, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return ServiceDefinition.Reconstitute(
            (Guid)row.id,
            (string)row.code,
            (string)row.name,
            (string)row.email,
            (string?)row.description,
            (bool)row.is_active,
            (Guid?)row.company_id,
            (DateTimeOffset)row.created_at,
            (DateTimeOffset?)row.updated_at,
            (string?)row.manager_name,
            (int?)row.default_sla_hours,
            (string?)row.color,
            (string?)row.competences);
    }

    public async Task InsertRoutingRuleAsync(RoutingRule routingRule, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO notification.routing_rules (id, code, name, entity_type, service_code, recipient_type, recipient_value, conditions, priority, is_active, company_id, created_at, updated_at)
            VALUES (@Id, @Code, @Name, @EntityType, @ServiceCode, @RecipientType, @RecipientValue, @Conditions::jsonb, @Priority, @IsActive, @CompanyId, @CreatedAt, @UpdatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                routingRule.Id,
                routingRule.Code,
                routingRule.Name,
                routingRule.EntityType,
                routingRule.ServiceCode,
                RecipientType = (int)routingRule.RecipientType,
                routingRule.RecipientValue,
                Conditions = SerializeConditions(routingRule.Conditions),
                routingRule.Priority,
                routingRule.IsActive,
                routingRule.CompanyId,
                routingRule.CreatedAt,
                routingRule.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateRoutingRuleAsync(RoutingRule routingRule, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notification.routing_rules
            SET name = @Name,
                service_code = @ServiceCode,
                recipient_type = @RecipientType,
                recipient_value = @RecipientValue,
                conditions = @Conditions::jsonb,
                priority = @Priority,
                is_active = @IsActive,
                updated_at = @UpdatedAt
            WHERE id = @Id
            """;

        var rowsAffected = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                routingRule.Id,
                routingRule.Name,
                routingRule.ServiceCode,
                RecipientType = (int)routingRule.RecipientType,
                routingRule.RecipientValue,
                Conditions = SerializeConditions(routingRule.Conditions),
                routingRule.Priority,
                routingRule.IsActive,
                routingRule.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));

        if (rowsAffected != 1)
        {
            throw new NotFoundException("RoutingRule", routingRule.Id);
        }
    }

    public async Task DeleteRoutingRuleAsync(Guid routingRuleId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM notification.routing_rules WHERE id = @Id";

        var rowsAffected = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = routingRuleId },
            _txn.Transaction,
            cancellationToken: ct));

        if (rowsAffected != 1)
        {
            throw new NotFoundException("RoutingRule", routingRuleId);
        }
    }

    public async Task<RoutingRule?> GetRoutingRuleByCodeAsync(string code, string entityType, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, code, name, entity_type, service_code, recipient_type, recipient_value, conditions, priority, is_active, company_id, created_at, updated_at
            FROM notification.routing_rules
            WHERE code = @Code AND entity_type = @EntityType
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Code = code, EntityType = entityType }, _txn.Transaction, cancellationToken: ct));

        return row is null ? null : MapRoutingRule(row);
    }

    public async Task<IReadOnlyList<RoutingRule>> GetActiveRoutingRulesAsync(string entityType, Guid? companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, code, name, entity_type, service_code, recipient_type, recipient_value, conditions, priority, is_active, company_id, created_at, updated_at
            FROM notification.routing_rules
            WHERE entity_type = @EntityType AND is_active = true
              AND (company_id IS NULL OR company_id = @CompanyId)
            ORDER BY priority ASC
            """;

        var rows = await _txn.Connection.QueryAsync(
            new CommandDefinition(sql, new { EntityType = entityType, CompanyId = companyId }, _txn.Transaction, cancellationToken: ct));

        var result = new List<RoutingRule>();
        foreach (var r in rows)
        {
            result.Add(MapRoutingRule(r));
        }

        return result;
    }

    public async Task InsertDeliverySlaAsync(DeliverySla sla, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO notification.delivery_sla (id, category, max_delay_seconds, escalation_action, escalation_recipient, company_id, created_at, updated_at)
            VALUES (@Id, @Category, @MaxDelaySeconds, @EscalationAction, @EscalationRecipient, @CompanyId, @CreatedAt, @UpdatedAt)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                sla.Id,
                Category = (int)sla.Category,
                sla.MaxDelaySeconds,
                sla.EscalationAction,
                sla.EscalationRecipient,
                sla.CompanyId,
                sla.CreatedAt,
                sla.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateDeliverySlaAsync(DeliverySla sla, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notification.delivery_sla
            SET max_delay_seconds = @MaxDelaySeconds,
                escalation_action = @EscalationAction,
                escalation_recipient = @EscalationRecipient,
                updated_at = @UpdatedAt
            WHERE id = @Id
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                sla.Id,
                sla.MaxDelaySeconds,
                sla.EscalationAction,
                sla.EscalationRecipient,
                sla.UpdatedAt,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task DeleteDeliverySlaAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM notification.delivery_sla WHERE id = @Id";

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task<DeliverySla?> GetDeliverySlaByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, category, max_delay_seconds, escalation_action, escalation_recipient, company_id, created_at, updated_at
            FROM notification.delivery_sla
            WHERE id = @Id
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(new CommandDefinition(
            sql,
            new { Id = id },
            _txn.Transaction,
            cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return DeliverySla.Reconstitute(
            (Guid)row.id,
            (TemplateCategory)(int)(short)row.category,
            (int)row.max_delay_seconds,
            (string?)row.escalation_action,
            (string?)row.escalation_recipient,
            (Guid?)row.company_id,
            (DateTimeOffset)row.created_at,
            (DateTimeOffset?)row.updated_at);
    }

    public async Task InsertDeliveryRecordAsync(DeliveryRecord record, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO notification.delivery_records (id, notification_id, template_code, recipient_email, entity_type, entity_id, sent_at, delivered_at, failed_at, retry_count, sla_breached, company_id)
            VALUES (@Id, @NotificationId, @TemplateCode, @RecipientEmail, @EntityType, @EntityId, @SentAt, @DeliveredAt, @FailedAt, @RetryCount, @SlaBreached, @CompanyId)
            """;

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                record.Id,
                record.NotificationId,
                record.TemplateCode,
                record.RecipientEmail,
                record.EntityType,
                record.EntityId,
                record.SentAt,
                record.DeliveredAt,
                record.FailedAt,
                record.RetryCount,
                record.SlaBreached,
                record.CompanyId,
            },
            _txn.Transaction,
            cancellationToken: ct));
    }

    public async Task UpdateDeliveryRecordAsync(DeliveryRecord record, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notification.delivery_records
            SET delivered_at = @DeliveredAt,
                failed_at = @FailedAt,
                retry_count = @RetryCount,
                sla_breached = @SlaBreached
            WHERE id = @Id
            """;

        var rowsAffected = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                record.Id,
                record.DeliveredAt,
                record.FailedAt,
                record.RetryCount,
                record.SlaBreached,
            },
            _txn.Transaction,
            cancellationToken: ct));

        if (rowsAffected != 1)
        {
            throw new NotFoundException("DeliveryRecord", record.Id);
        }
    }

    public async Task<DeliveryRecord?> GetDeliveryRecordByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, notification_id, template_code, recipient_email, entity_type, entity_id,
                   sent_at, delivered_at, failed_at, retry_count, sla_breached, company_id
            FROM notification.delivery_records
            WHERE id = @Id
            """;

        var row = await _txn.Connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, _txn.Transaction, cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return DeliveryRecord.Reconstitute(
            (Guid)row.id,
            (Guid?)row.notification_id,
            (string)row.template_code,
            (string)row.recipient_email,
            (string?)row.entity_type,
            (string?)row.entity_id,
            (DateTimeOffset)row.sent_at,
            (DateTimeOffset?)row.delivered_at,
            (DateTimeOffset?)row.failed_at,
            (int)row.retry_count,
            (bool)row.sla_breached,
            (Guid?)row.company_id);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _txn.CommitAsync(ct);
    }

    public async Task CommitWithEventAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken ct = default)
    {
        await _outboxWriter.WriteAsync(_txn, integrationEvent, ct);
        await _txn.CommitAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }

    private static EmailTemplate MapEmailTemplate(dynamic row)
    {
        var templateLinks = TemplateLinkSerializer.Deserialize((string)row.template_links);
        return EmailTemplate.Reconstitute(
            (Guid)row.id,
            (string)row.code,
            (string)row.subject_template,
            (string)row.body_template,
            ((string)row.language_code).Trim(),
            (Guid?)row.company_id,
            (DateTimeOffset)row.created_at,
            (DateTimeOffset?)row.updated_at,
            (TemplateCategory)(int)(short)row.category,
            (string?)row.entity_type,
            templateLinks);
    }

    private static RoutingRule MapRoutingRule(dynamic row)
    {
        var conditions = DeserializeConditions((string)row.conditions);
        return RoutingRule.Reconstitute(
            (Guid)row.id,
            (string)row.code,
            (string)row.name,
            (string)row.entity_type,
            (string)row.service_code,
            (RecipientType)(int)(short)row.recipient_type,
            (string)row.recipient_value,
            conditions,
            (int)row.priority,
            (bool)row.is_active,
            (Guid?)row.company_id,
            (DateTimeOffset)row.created_at,
            (DateTimeOffset?)row.updated_at);
    }

    private static string SerializeConditions(IReadOnlyList<RoutingCondition> conditions)
    {
        return RoutingConditionSerializer.Serialize(conditions);
    }

    private static List<RoutingCondition> DeserializeConditions(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return [];
        }

        var elements = JsonSerializer.Deserialize<JsonElement[]>(json) ?? [];
        return elements.Select(ParseCondition).ToList();
    }

    private static RoutingCondition ParseCondition(JsonElement element)
    {
        if (element.TryGetProperty("and", out var andChildren))
        {
            var children = andChildren.EnumerateArray().Select(ParseCondition).ToList();
            return RoutingCondition.Reconstitute(null, null, null, "and", children);
        }

        if (element.TryGetProperty("or", out var orChildren))
        {
            var children = orChildren.EnumerateArray().Select(ParseCondition).ToList();
            return RoutingCondition.Reconstitute(null, null, null, "or", children);
        }

        var field = element.GetProperty("field").GetString();
        var op = element.GetProperty("op").GetString();
        JsonElement? value = element.TryGetProperty("value", out var v) ? v : null;
        return RoutingCondition.Reconstitute(field, op, value, null, null);
    }
}
