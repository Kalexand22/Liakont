namespace Liakont.Modules.Signature.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Infrastructure.Persistence;
using Liakont.Modules.Signature.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Store des comptes de signature sur une vraie base (ADR-0029 §6) : round-trip des secrets CHIFFRÉS (jamais
/// en clair), upsert, et isolation par <c>company_id</c> (un compte d'une société n'est pas rendu pour une
/// autre).
/// </summary>
[Collection("SignatureIntegration")]
public sealed class SignatureAccountStoreIntegrationTests
{
    private readonly SignatureDatabaseFixture _fixture;

    public SignatureAccountStoreIntegrationTests(SignatureDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Upsert_then_read_round_trips_encrypted_secrets_and_environment()
    {
        var store = new PostgresSignatureAccountStore(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();

        await store.UpsertAsync(company, "Yousign", "Sandbox", "{\"workspace\":\"w1\"}", "enc-api-key", "enc-webhook-secret");

        var account = await store.GetActiveAccountAsync(company, "Yousign");

        account.Should().NotBeNull();
        account!.ProviderType.Should().Be("Yousign");
        account.Environment.Should().Be("Sandbox");
        account.Settings[SignatureAccountSettingKeys.Environment].Should().Be("Sandbox");
        account.Settings[SignatureAccountSettingKeys.EncryptedApiKey].Should().Be("enc-api-key");
        account.Settings[SignatureAccountSettingKeys.EncryptedWebhookSecret].Should().Be("enc-webhook-secret");
    }

    [Fact]
    public async Task Upsert_replaces_existing_account_for_same_company_and_type()
    {
        var store = new PostgresSignatureAccountStore(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();

        await store.UpsertAsync(company, "Yousign", "Sandbox", string.Empty, "enc-1", "wh-1");
        await store.UpsertAsync(company, "Yousign", "Production", string.Empty, "enc-2", "wh-2");

        var account = await store.GetActiveAccountAsync(company, "Yousign");

        account!.Environment.Should().Be("Production");
        account.Settings[SignatureAccountSettingKeys.EncryptedApiKey].Should().Be("enc-2");
    }

    [Fact]
    public async Task Account_is_scoped_by_company()
    {
        var store = new PostgresSignatureAccountStore(_fixture.CreateConnectionFactory());
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await store.UpsertAsync(companyA, "Yousign", "Sandbox", string.Empty, "enc-a", "wh-a");

        (await store.GetActiveAccountAsync(companyA, "Yousign")).Should().NotBeNull();
        (await store.GetActiveAccountAsync(companyB, "Yousign")).Should().BeNull(
            "le compte d'une société n'est jamais rendu pour une autre (tenant-scoping)");
    }
}
