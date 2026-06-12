namespace Liakont.Modules.FleetSupervision.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Liakont.Modules.FleetSupervision.Contracts;
using Xunit;

/// <summary>
/// CLOISONNEMENT ÉDITEUR (acceptance OPS04) : la charge utile du heartbeat ne transporte QUE de la
/// télémétrie technique. Ce test verrouille la forme du contrat (liste blanche de propriétés) ET le JSON
/// sérialisé (aucune clé/donnée métier) — ajouter un champ portant une donnée d'éditeur fait échouer ici.
/// </summary>
public sealed class InstanceHeartbeatReportIsolationTests
{
    // Liste BLANCHE des propriétés autorisées (uniquement technique). TenantCount = un entier, jamais une identité.
    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        nameof(InstanceHeartbeatReport.InstanceId),
        nameof(InstanceHeartbeatReport.DisplayName),
        nameof(InstanceHeartbeatReport.HostingMode),
        nameof(InstanceHeartbeatReport.Version),
        nameof(InstanceHeartbeatReport.HostHealth),
        nameof(InstanceHeartbeatReport.DatabaseHealth),
        nameof(InstanceHeartbeatReport.KeycloakHealth),
        nameof(InstanceHeartbeatReport.TenantCount),
        nameof(InstanceHeartbeatReport.DiskFreeBytes),
        nameof(InstanceHeartbeatReport.DiskTotalBytes),
        nameof(InstanceHeartbeatReport.LastSuccessfulBackupUtc),
        nameof(InstanceHeartbeatReport.ContactEmail),
        nameof(InstanceHeartbeatReport.SentAtUtc),
    };

    // Jetons de donnée MÉTIER interdits dans la charge utile (noms de tenant, SIREN, documents, montants…).
    private static readonly string[] ForbiddenTokens =
    [
        "siren", "siret", "tenantname", "tenantid", "companyname", "raisonsociale",
        "document", "facture", "invoice", "montant", "amount", "tva", "vat",
    ];

    [Fact]
    public void Report_Exposes_Only_The_Allowed_Technical_Properties()
    {
        var actual = typeof(InstanceHeartbeatReport)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        actual.Should().BeEquivalentTo(
            AllowedProperties,
            "le heartbeat de flotte ne doit transporter QUE de la télémétrie technique (cloisonnement éditeur OPS04)");
    }

    [Fact]
    public void No_Property_Name_Carries_A_Business_Data_Token()
    {
        foreach (PropertyInfo property in typeof(InstanceHeartbeatReport).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            string lower = property.Name.ToLowerInvariant();
            ForbiddenTokens.Should().NotContain(
                token => lower.Contains(token, StringComparison.Ordinal),
                "aucune propriété ne doit nommer une donnée métier — {0}",
                property.Name);
        }
    }

    [Fact]
    public void Serialized_Payload_Contains_No_Business_Data_Key()
    {
        // Charge utile peuplée (valeurs réalistes mais fictives) sérialisée avec les options de transport.
        var report = new InstanceHeartbeatReport
        {
            InstanceId = "azmut-prod-3",
            DisplayName = "AZMUT production 3",
            HostingMode = InstanceHostingMode.SelfHosted,
            Version = "1.4.0+9e0917c",
            HostHealth = InstanceHealthStatus.Healthy,
            DatabaseHealth = InstanceHealthStatus.Degraded,
            KeycloakHealth = InstanceHealthStatus.Unknown,
            TenantCount = 7,
            DiskFreeBytes = 42_000_000_000,
            DiskTotalBytes = 100_000_000_000,
            LastSuccessfulBackupUtc = new DateTimeOffset(2026, 6, 12, 3, 0, 0, TimeSpan.Zero),
            ContactEmail = "it@editeur.example",
            SentAtUtc = new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero),
        };

        string json = JsonSerializer.Serialize(report, FleetTransportJson.Options).ToLowerInvariant();

        foreach (string token in ForbiddenTokens)
        {
            json.Should().NotContain(token, "le JSON du heartbeat ne doit contenir aucune donnée métier d'éditeur");
        }

        // Le nombre de tenants est bien un ENTIER (jamais une liste de noms).
        using JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(report, FleetTransportJson.Options));
        doc.RootElement.GetProperty("tenantCount").ValueKind.Should().Be(JsonValueKind.Number);
    }
}
