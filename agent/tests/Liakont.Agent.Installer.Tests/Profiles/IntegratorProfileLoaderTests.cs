namespace Liakont.Agent.Installer.Tests.Profiles;

using FluentAssertions;
using Liakont.Agent.Installer.Profiles;
using Xunit;

public class IntegratorProfileLoaderTests
{
    [Fact]
    public void Parses_name_branding_and_fields()
    {
        IntegratorProfile profile = Parse(@"{
            ""profil"": ""exemple"",
            ""branding"": { ""nom"": ""Exemple"", ""logo"": ""logo.png"", ""couleurPrincipale"": ""#0a5"" },
            ""champs"": { ""adapter"": { ""etat"": ""masqué"", ""valeur"": ""EncheresV6"" } }
        }");

        profile.ProfileName.Should().Be("exemple");
        profile.Branding.Name.Should().Be("Exemple");
        profile.Branding.PrimaryColor.Should().Be("#0a5");
        profile.Fields[ProfileFieldKeys.Adapter].State.Should().Be(FieldState.Hidden);
        profile.Fields[ProfileFieldKeys.Adapter].DefaultValue.Should().Be("EncheresV6");
    }

    [Theory]
    [InlineData("affiché", nameof(FieldState.Shown))]
    [InlineData("verrouillé", nameof(FieldState.Locked))]
    [InlineData("masqué", nameof(FieldState.Hidden))]
    [InlineData("AFFICHÉ", nameof(FieldState.Shown))]
    public void Maps_canonical_french_states_case_insensitively(string etat, string expected)
    {
        IntegratorProfile profile = Parse(@"{ ""champs"": { ""odbcAdvanced"": { ""etat"": """ + etat + @""", ""valeur"": ""x"" } } }");

        profile.Fields[ProfileFieldKeys.OdbcAdvanced].State.ToString().Should().Be(expected);
    }

    [Fact]
    public void Boolean_value_is_read_as_lowercase_string()
    {
        IntegratorProfile profile = Parse(@"{ ""champs"": { ""autoUpdate"": { ""etat"": ""verrouillé"", ""valeur"": true } } }");

        profile.Fields[ProfileFieldKeys.AutoUpdate].DefaultValue.Should().Be("true");
    }

    [Fact]
    public void Missing_profil_name_falls_back_to_a_placeholder()
    {
        Parse(@"{ ""champs"": {} }").ProfileName.Should().Be("(sans nom)");
    }

    [Fact]
    public void Unknown_state_throws_format_exception()
    {
        System.Action act = () => Parse(@"{ ""champs"": { ""adapter"": { ""etat"": ""caché"" } } }");

        act.Should().Throw<ProfileFormatException>().WithMessage("*caché*");
    }

    [Fact]
    public void Missing_state_throws_format_exception()
    {
        System.Action act = () => Parse(@"{ ""champs"": { ""adapter"": { ""valeur"": ""x"" } } }");

        act.Should().Throw<ProfileFormatException>().WithMessage("*etat*");
    }

    [Fact]
    public void Malformed_json_throws_format_exception()
    {
        System.Action act = () => Parse(@"{ ""champs"": { ");

        act.Should().Throw<ProfileFormatException>();
    }

    [Fact]
    public void Fields_block_must_be_an_object()
    {
        System.Action act = () => Parse(@"{ ""champs"": [] }");

        act.Should().Throw<ProfileFormatException>().WithMessage("*champs*");
    }

    [Fact]
    public void Field_value_must_be_a_scalar()
    {
        System.Action act = () => Parse(@"{ ""champs"": { ""adapter"": { ""etat"": ""affiché"", ""valeur"": { ""x"": 1 } } } }");

        act.Should().Throw<ProfileFormatException>().WithMessage("*scalaire*");
    }

    private static IntegratorProfile Parse(string json) => IntegratorProfileLoader.Parse(json, "(test)");
}
