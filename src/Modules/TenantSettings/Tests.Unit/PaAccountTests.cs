namespace Liakont.Modules.TenantSettings.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Xunit;

public sealed class PaAccountTests
{
    [Fact]
    public void Create_Stores_Opaque_Encrypted_Key_And_Is_Active()
    {
        var account = PaAccount.Create(Guid.NewGuid(), "Fake", PaEnvironment.Staging, "{}", encryptedApiKey: "CIPHERTEXT");

        account.EncryptedApiKey.Should().Be("CIPHERTEXT");
        account.IsActive.Should().BeTrue();
        account.Environment.Should().Be(PaEnvironment.Staging);
    }

    [Fact]
    public void Create_Without_Key_Leaves_EncryptedApiKey_Null()
    {
        var account = PaAccount.Create(Guid.NewGuid(), "Fake", PaEnvironment.Production, "{}", encryptedApiKey: null);

        account.EncryptedApiKey.Should().BeNull();
    }

    [Fact]
    public void Create_With_Empty_PluginType_Throws()
    {
        var act = () => PaAccount.Create(Guid.NewGuid(), "  ", PaEnvironment.Staging, "{}", null);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-002*");
    }

    [Fact]
    public void Create_Stores_Opaque_Encrypted_Technical_Password()
    {
        var account = PaAccount.Create(
            Guid.NewGuid(), "ChorusPro", PaEnvironment.Staging, "tech-login@tenant.fr",
            encryptedApiKey: null, encryptedClientId: "ENC-CID", encryptedClientSecret: "ENC-CSECRET",
            encryptedTechnicalPassword: "ENC-TECH-PWD");

        account.EncryptedTechnicalPassword.Should().Be("ENC-TECH-PWD");
    }

    [Fact]
    public void Create_Without_Technical_Password_Leaves_It_Null()
    {
        var account = PaAccount.Create(Guid.NewGuid(), "ChorusPro", PaEnvironment.Staging, "{}", encryptedApiKey: null);

        account.EncryptedTechnicalPassword.Should().BeNull();
    }

    [Fact]
    public void SetEncryptedTechnicalPassword_Replaces_Opaque_Value_And_Touches_UpdatedAt()
    {
        var account = PaAccount.Create(Guid.NewGuid(), "ChorusPro", PaEnvironment.Staging, "{}", encryptedApiKey: null);

        account.SetEncryptedTechnicalPassword("ENC-NEW");

        account.EncryptedTechnicalPassword.Should().Be("ENC-NEW");
        account.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Deactivate_Sets_Inactive()
    {
        var account = PaAccount.Create(Guid.NewGuid(), "Fake", PaEnvironment.Staging, "{}", "CIPHERTEXT");

        account.Deactivate();

        account.IsActive.Should().BeFalse();
        account.UpdatedAt.Should().NotBeNull();
    }
}
