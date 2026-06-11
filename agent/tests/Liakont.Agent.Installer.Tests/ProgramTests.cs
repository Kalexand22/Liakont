namespace Liakont.Agent.Installer.Tests;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Installer;
using Xunit;

/// <summary>
/// Garde le CONTRAT d'automatisation du mode headless « --validate » (réutilisé par le packaging
/// OPS08c) : les codes de sortie 0 (valide) / 1 (invalide ou illisible) / 2 (usage ou fichier absent)
/// ne doivent pas régresser. Un contrat d'exit code sans test est un faux-vert potentiel.
/// </summary>
public class ProgramTests
{
    [Fact]
    public void Returns_0_for_a_valid_profile()
    {
        string path = WriteTempProfile(
            @"{ ""profil"": ""t"", ""champs"": { ""platformUrl"": { ""etat"": ""affiché"" }, ""apiKey"": { ""etat"": ""affiché"" } } }");
        try
        {
            Program.Main(new[] { "--validate", path }).Should().Be(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Returns_1_for_an_invalid_profile()
    {
        string path = WriteTempProfile(
            @"{ ""profil"": ""t"", ""champs"": { ""apiKey"": { ""etat"": ""affiché"", ""valeur"": ""pk_FICTIF"" } } }");
        try
        {
            Program.Main(new[] { "--validate", path }).Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Returns_1_for_malformed_json()
    {
        string path = WriteTempProfile(@"{ ""champs"": { ");
        try
        {
            Program.Main(new[] { "--validate", path }).Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Returns_2_when_no_arguments()
    {
        Program.Main(Array.Empty<string>()).Should().Be(2);
    }

    [Fact]
    public void Returns_2_when_profile_file_is_missing()
    {
        string path = Path.Combine(Path.GetTempPath(), "profil-inexistant-" + Guid.NewGuid().ToString("N") + ".json");

        Program.Main(new[] { "--validate", path }).Should().Be(2);
    }

    private static string WriteTempProfile(string json)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return path;
    }
}
