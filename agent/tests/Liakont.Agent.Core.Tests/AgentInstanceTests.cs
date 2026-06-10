namespace Liakont.Agent.Core.Tests;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Hosting;
using Xunit;

/// <summary>
/// Identité d'instance de l'agent (multi-instances, OPS05 pt 5) : l'instance Default conserve
/// STRICTEMENT les noms et chemins historiques (compat installations déployées), les instances
/// nommées dérivent service/mutex/répertoire de leur nom, et la validation refuse tout nom
/// inutilisable comme nom de service Windows ou de répertoire.
/// </summary>
public class AgentInstanceTests
{
    [Fact]
    public void Default_keeps_historical_service_mutex_and_directory()
    {
        AgentInstance instance = AgentInstance.Default;

        instance.IsDefault.Should().BeTrue();
        instance.Name.Should().Be("Default");
        instance.ServiceName.Should().Be("LiakontAgent");
        instance.DisplayName.Should().Be("Liakont Agent");
        instance.RunMutexName.Should().Be(InterProcessRunLock.DefaultMutexName);
        instance.DataDirectory.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Liakont"));
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    [InlineData("  default  ")]
    public void Parsing_default_in_any_case_yields_the_default_instance(string raw)
    {
        bool ok = AgentInstance.TryParse(raw, out AgentInstance instance, out string? error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        instance.IsDefault.Should().BeTrue();
        instance.ServiceName.Should().Be("LiakontAgent");
    }

    [Fact]
    public void Named_instance_derives_service_mutex_display_and_directory_from_its_name()
    {
        AgentInstance.TryParse("AZMUT-01", out AgentInstance instance, out string? error).Should().BeTrue();

        error.Should().BeNull();
        instance.IsDefault.Should().BeFalse();
        instance.Name.Should().Be("AZMUT-01");
        instance.ServiceName.Should().Be("LiakontAgent$AZMUT-01");
        instance.DisplayName.Should().Be("Liakont Agent (AZMUT-01)");
        instance.RunMutexName.Should().Be(InterProcessRunLock.DefaultMutexName + "-AZMUT-01");
        instance.DataDirectory.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Liakont", "AZMUT-01"));
    }

    [Fact]
    public void Two_named_instances_never_share_service_mutex_or_directory()
    {
        AgentInstance.TryParse("ClientA", out AgentInstance a, out _).Should().BeTrue();
        AgentInstance.TryParse("ClientB", out AgentInstance b, out _).Should().BeTrue();

        a.ServiceName.Should().NotBe(b.ServiceName);
        a.RunMutexName.Should().NotBe(b.RunMutexName);
        a.DataDirectory.Should().NotBe(b.DataDirectory);

        a.ServiceName.Should().NotBe(AgentInstance.Default.ServiceName);
        a.RunMutexName.Should().NotBe(AgentInstance.Default.RunMutexName);
        a.DataDirectory.Should().NotBe(AgentInstance.Default.DataDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_name_is_rejected_with_a_french_message(string? raw)
    {
        bool ok = AgentInstance.TryParse(raw, out _, out string? error);

        ok.Should().BeFalse();
        error.Should().Contain("Nom d'instance vide");
    }

    // Espaces, point (collision avec les fichiers de Default), première position non alphanumérique,
    // séparateurs de chemin, caractères hors [A-Za-z0-9_-], longueur > 32 : tous refusés.
    [Theory]
    [InlineData("client a")]
    [InlineData("client.a")]
    [InlineData("-client")]
    [InlineData("client/a")]
    [InlineData("client\\a")]
    [InlineData("café")]
    [InlineData("a23456789012345678901234567890123")]
    public void Invalid_names_are_rejected(string raw)
    {
        bool ok = AgentInstance.TryParse(raw, out _, out string? error);

        ok.Should().BeFalse();
        error.Should().Contain("Nom d'instance invalide");
    }

    // « logs » et « update-work » : sous-répertoires de l'instance Default à la racine partagée ;
    // « NUL » / « com3 » : périphériques réservés Windows.
    [Theory]
    [InlineData("logs")]
    [InlineData("update-work")]
    [InlineData("NUL")]
    [InlineData("com3")]
    public void Reserved_names_are_rejected(string raw)
    {
        bool ok = AgentInstance.TryParse(raw, out _, out string? error);

        ok.Should().BeFalse();
        error.Should().Contain("réservé");
    }

    [Fact]
    public void Case_variants_of_the_same_name_share_the_same_run_mutex()
    {
        // Les chemins Windows sont insensibles à la casse (même répertoire, même file SQLite) ;
        // le mutex doit l'être aussi, sinon service et CLI prendraient deux verrous différents.
        AgentInstance.TryParse("ClientA", out AgentInstance mixed, out _).Should().BeTrue();
        AgentInstance.TryParse("clienta", out AgentInstance lower, out _).Should().BeTrue();
        AgentInstance.TryParse("CLIENTA", out AgentInstance upper, out _).Should().BeTrue();

        lower.RunMutexName.Should().Be(mixed.RunMutexName);
        upper.RunMutexName.Should().Be(mixed.RunMutexName);
        lower.DataDirectory.Should().BeEquivalentTo(mixed.DataDirectory); // même dossier à la casse près
    }

    [Fact]
    public void Name_at_the_32_character_limit_is_accepted()
    {
        string name = "a2345678901234567890123456789012"; // 32 caractères exactement

        AgentInstance.TryParse(name, out AgentInstance instance, out string? error).Should().BeTrue();
        error.Should().BeNull();
        instance.Name.Should().Be(name);
    }

    [Fact]
    public void Command_line_without_the_option_yields_default_and_keeps_all_args()
    {
        string[] args = { "install" };

        bool ok = AgentInstance.TryFromCommandLine(args, out AgentInstance instance, out string[] remaining, out string? error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        instance.IsDefault.Should().BeTrue();
        remaining.Should().Equal("install");
    }

    [Fact]
    public void Option_is_extracted_wherever_it_appears_and_removed_from_remaining_args()
    {
        var variants = new[]
        {
            new[] { "--instance", "AZMUT-01", "install" },
            new[] { "install", "--instance", "AZMUT-01" },
        };

        foreach (string[] args in variants)
        {
            bool ok = AgentInstance.TryFromCommandLine(args, out AgentInstance instance, out string[] remaining, out string? error);

            ok.Should().BeTrue();
            error.Should().BeNull();
            instance.Name.Should().Be("AZMUT-01");
            remaining.Should().Equal("install");
        }
    }

    [Fact]
    public void Empty_command_line_yields_default()
    {
        bool ok = AgentInstance.TryFromCommandLine(Array.Empty<string>(), out AgentInstance instance, out string[] remaining, out string? error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        instance.IsDefault.Should().BeTrue();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public void Option_without_a_value_is_an_error()
    {
        string[] args = { "run", "--instance" };

        bool ok = AgentInstance.TryFromCommandLine(args, out _, out _, out string? error);

        ok.Should().BeFalse();
        error.Should().Contain("sans valeur");
    }

    [Fact]
    public void Duplicated_option_is_an_error()
    {
        string[] args = { "--instance", "A", "--instance", "B" };

        bool ok = AgentInstance.TryFromCommandLine(args, out _, out _, out string? error);

        ok.Should().BeFalse();
        error.Should().Contain("plusieurs fois");
    }

    [Fact]
    public void Invalid_name_on_the_command_line_propagates_the_validation_error()
    {
        string[] args = { "--instance", "client a" };

        bool ok = AgentInstance.TryFromCommandLine(args, out _, out _, out string? error);

        ok.Should().BeFalse();
        error.Should().Contain("Nom d'instance invalide");
    }
}
