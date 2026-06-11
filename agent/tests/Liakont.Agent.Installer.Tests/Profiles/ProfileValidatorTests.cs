namespace Liakont.Agent.Installer.Tests.Profiles;

using FluentAssertions;
using Liakont.Agent.Installer.Profiles;
using Xunit;

public class ProfileValidatorTests
{
    [Fact]
    public void Empty_profile_is_valid_required_fields_default_to_open_for_input()
    {
        // platformUrl et apiKey non déclarés → défaut ouvert → affichés pour saisie → requis résolus.
        ValidateChamps("{}").IsValid.Should().BeTrue();
    }

    [Fact]
    public void Populated_valid_profile_passes()
    {
        ProfileValidationResult result = ValidateChamps(@"{
            ""adapter"": { ""etat"": ""masqué"", ""valeur"": ""EncheresV6"" },
            ""platformUrl"": { ""etat"": ""verrouillé"", ""valeur"": ""https://exemple.tld"" },
            ""apiKey"": { ""etat"": ""affiché"" },
            ""odbcConnection"": { ""etat"": ""affiché"" },
            ""instanceName"": { ""etat"": ""masqué"", ""valeur"": ""Default"" }
        }");

        result.IsValid.Should().BeTrue(string.Join(" | ", result.Errors));
    }

    [Fact]
    public void Unknown_key_is_rejected()
    {
        ProfileValidationResult result = ValidateChamps(@"{ ""champInexistant"": { ""etat"": ""affiché"" } }");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("champInexistant") && e.Contains("inconnu"));
    }

    [Fact]
    public void Hidden_field_without_value_is_rejected()
    {
        ProfileValidationResult result = ValidateChamps(@"{ ""logging"": { ""etat"": ""masqué"" } }");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("logging") && e.Contains("masqué") && e.Contains("valeur"));
    }

    [Fact]
    public void ApiKey_with_a_value_is_rejected_secret_never_imposed()
    {
        ProfileValidationResult result = ValidateChamps(@"{ ""apiKey"": { ""etat"": ""affiché"", ""valeur"": ""pk_FICTIF"" } }");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("apiKey") && e.Contains("valeur"));
    }

    [Fact]
    public void ApiKey_locked_is_rejected_secret_must_stay_shown()
    {
        ProfileValidationResult result = ValidateChamps(@"{ ""apiKey"": { ""etat"": ""verrouillé"", ""valeur"": ""pk_FICTIF"" } }");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("apiKey") && e.Contains("affiché"));
    }

    [Fact]
    public void Required_field_locked_without_value_is_unresolved()
    {
        ProfileValidationResult result = ValidateChamps(@"{ ""platformUrl"": { ""etat"": ""verrouillé"" } }");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("platformUrl") && e.Contains("résolu"));
    }

    [Fact]
    public void Required_field_hidden_with_value_is_resolved()
    {
        ProfileValidationResult result = ValidateChamps(@"{ ""platformUrl"": { ""etat"": ""masqué"", ""valeur"": ""https://exemple.tld"" } }");

        result.IsValid.Should().BeTrue(string.Join(" | ", result.Errors));
    }

    [Fact]
    public void InstanceName_default_is_accepted()
    {
        ProfileValidationResult result = ValidateChamps(@"{ ""instanceName"": { ""etat"": ""masqué"", ""valeur"": ""Default"" } }");

        result.IsValid.Should().BeTrue(string.Join(" | ", result.Errors));
    }

    [Fact]
    public void InstanceName_with_invalid_service_name_is_rejected()
    {
        ProfileValidationResult result = ValidateChamps(@"{ ""instanceName"": { ""etat"": ""verrouillé"", ""valeur"": ""client/azmut"" } }");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("instanceName"));
    }

    [Fact]
    public void InstanceName_empty_value_is_rejected()
    {
        ProfileValidationResult result = ValidateChamps(@"{ ""instanceName"": { ""etat"": ""affiché"", ""valeur"": """" } }");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("instanceName"));
    }

    private static ProfileValidationResult ValidateChamps(string champsJson)
    {
        string json = "{ \"profil\": \"test\", \"champs\": " + champsJson + " }";
        return ProfileValidator.Validate(IntegratorProfileLoader.Parse(json, "(test)"));
    }
}
