namespace Liakont.Agent.Installer.Tests.Silent;

using System;
using FluentAssertions;
using Liakont.Agent.Installer.Configuration;
using Liakont.Agent.Installer.Profiles;
using Liakont.Agent.Installer.Silent;
using Xunit;

/// <summary>
/// Garde du chargeur de fichier de réponses (mode silencieux, F13 §3) : lecture des champs connus, et
/// REFUS explicite d'une clé inconnue ou d'un JSON malformé (anti-faux-vert : une faute de frappe ne doit
/// jamais retomber silencieusement sur un défaut).
/// </summary>
public class AnswerFileLoaderTests
{
    [Fact]
    public void Parse_lit_les_champs_connus()
    {
        InstallationInput input = AnswerFileLoader.Parse(
            @"{ ""valeurs"": { ""platformUrl"": ""https://x.fr"", ""apiKey"": ""pk"" } }",
            "(test)");

        input.Get(ProfileFieldKeys.PlatformUrl).Should().Be("https://x.fr");
        input.Get(ProfileFieldKeys.ApiKey).Should().Be("pk");
    }

    [Fact]
    public void Parse_rejette_une_cle_inconnue()
    {
        Action act = () => AnswerFileLoader.Parse(
            @"{ ""valeurs"": { ""clefInconnue"": ""x"" } }",
            "(test)");

        act.Should().Throw<AnswerFileFormatException>().WithMessage("*clefInconnue*");
    }

    [Fact]
    public void Parse_rejette_un_json_malforme()
    {
        Action act = () => AnswerFileLoader.Parse("{ ceci n'est pas du JSON", "(test)");

        act.Should().Throw<AnswerFileFormatException>();
    }

    [Fact]
    public void Parse_accepte_un_bloc_valeurs_absent()
    {
        InstallationInput input = AnswerFileLoader.Parse("{}", "(test)");

        input.Get(ProfileFieldKeys.PlatformUrl).Should().BeNull();
    }

    [Fact]
    public void Parse_convertit_un_booleen_en_chaine()
    {
        InstallationInput input = AnswerFileLoader.Parse(
            @"{ ""valeurs"": { ""autoUpdate"": true } }",
            "(test)");

        input.Get(ProfileFieldKeys.AutoUpdate).Should().Be("true");
    }
}
