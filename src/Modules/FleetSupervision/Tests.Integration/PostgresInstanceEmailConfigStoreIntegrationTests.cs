namespace Liakont.Modules.FleetSupervision.Tests.Integration;

using System.Data;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Infrastructure;
using Xunit;

/// <summary>
/// Store de config email d'instance (ADR-0039) sur une base PostgreSQL réelle (Testcontainers, base SYSTÈME).
/// Round-trip du ciphertext (le store ne chiffre pas : il persiste tel quel les colonnes <c>encrypted_*</c>),
/// et unicité SINGLETON (deux upserts → une seule ligne, la dernière l'emporte — jamais une 2e ligne).
/// Le harnais applique la migration <c>V003__create_instance_email_config</c> (assembly du module).
/// </summary>
[Collection("FleetIntegration")]
public sealed class PostgresInstanceEmailConfigStoreIntegrationTests : IClassFixture<FleetDatabaseFixture>
{
    private readonly FleetDatabaseFixture _fixture;

    public PostgresInstanceEmailConfigStoreIntegrationTests(FleetDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetAsync_On_An_Empty_Table_Returns_Null()
    {
        var store = NewStore();

        var config = await store.GetAsync();

        config.Should().BeNull("aucune configuration n'a encore été enregistrée");
    }

    [Fact]
    public async Task Upsert_Then_Get_Round_Trips_The_Oauth_Config_Including_Ciphertext()
    {
        var store = NewStore();
        var written = new InstanceEmailConfig
        {
            Kind = EmailProviderKind.MicrosoftOAuth2,
            Host = "smtp.office365.com",
            Port = 587,
            UseStartTls = true,
            FromAddress = "conformite@exemple.fr",
            FromName = "Conformité Exemple",
            Username = "conformite@exemple.fr",
            EncryptedSmtpPassword = null,
            OAuthClientId = "client-abc",
            OAuthTenantId = "tenant-xyz",
            EncryptedOAuthClientSecret = "CIPHER-client-secret",
            EncryptedOAuthRefreshToken = "CIPHER-refresh-token",
            Enabled = true,
        };

        await store.UpsertAsync(written);
        var read = await store.GetAsync();

        read.Should().BeEquivalentTo(written, "toutes les colonnes (kind, non-secrets, ciphertext) sont fidèlement relues");
    }

    [Fact]
    public async Task Upsert_Is_Singleton_The_Second_Write_Replaces_The_First()
    {
        var store = NewStore();

        await store.UpsertAsync(SmtpBasicConfig(host: "first.smtp.test", password: "CIPHER-1"));
        await store.UpsertAsync(SmtpBasicConfig(host: "second.smtp.test", password: "CIPHER-2"));

        var read = await store.GetAsync();
        read!.Host.Should().Be("second.smtp.test", "la ligne singleton est REMPLACÉE, pas dupliquée");
        read.EncryptedSmtpPassword.Should().Be("CIPHER-2");

        var rowCount = await CountRowsAsync();
        rowCount.Should().Be(1, "la table de config d'instance ne contient JAMAIS qu'une seule ligne (singleton)");
    }

    private static InstanceEmailConfig SmtpBasicConfig(string host, string password) => new()
    {
        Kind = EmailProviderKind.SmtpBasic,
        Host = host,
        Port = 587,
        UseStartTls = true,
        FromAddress = "supervision@liakont.test",
        FromName = "Supervision",
        Username = "supervision@liakont.test",
        EncryptedSmtpPassword = password,
        Enabled = true,
    };

    private async Task<long> CountRowsAsync()
    {
        using IDbConnection connection = await _fixture.CreateConnectionFactory().OpenAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fleet.instance_email_config;");
    }

    private PostgresInstanceEmailConfigStore NewStore() => new(_fixture.CreateConnectionFactory());
}
