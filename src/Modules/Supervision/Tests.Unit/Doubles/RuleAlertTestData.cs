namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>Fabriques concises des DTO Contract consommés par les règles SUP01b (champs requis remplis).</summary>
internal static class RuleAlertTestData
{
    public static AgentSummaryDto Agent(
        string name,
        DateTimeOffset? lastSeenAtUtc,
        DateTimeOffset createdAt,
        bool isRevoked = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            KeyPrefix = "lkk_test",
            IsRevoked = isRevoked,
            CreatedAt = createdAt,
            RevokedAt = isRevoked ? createdAt : null,
            LastSeenAtUtc = lastSeenAtUtc,
            LastAgentVersion = lastSeenAtUtc is null ? null : "1.0.0",
        };

    public static DocumentSummaryDto Document(string documentNumber, string state, DateTimeOffset lastUpdateUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            DocumentNumber = documentNumber,
            DocumentType = "Invoice",
            IssueDate = new DateOnly(2026, 1, 1),
            CustomerName = "Client Fictif",
            TotalGross = 120.00m,
            State = state,
            LastUpdateUtc = lastUpdateUtc,
        };

    /// <summary>Seuils du tenant — seuls les 3 champs consommés par SUP01b sont paramétrables, les autres
    /// portent des valeurs par défaut produit (F12 §5.2) sans incidence sur ces règles.</summary>
    public static AlertThresholdsDto Thresholds(
        int agentSilentHours = 24,
        int blockedDocumentsDays = 5,
        int paRejectionsDays = 2) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            AgentSilentHours = agentSilentHours,
            MissedRunHours = 36,
            PushQueueMaxItems = 50,
            PushQueueMaxAgeHours = 6,
            BlockedDocumentsDays = blockedDocumentsDays,
            PaRejectionsDays = paRejectionsDays,
            AlertTenantContact = false,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
}
