namespace Liakont.Agent.Cli.Commands;

using System;
using System.Collections.Generic;
using System.IO;
using Liakont.Agent.Cli.Diagnostics;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Security;

/// <summary>
/// Commande <c>test-api</c> (F12 §2.1, §3.3) : « heartbeat à blanc » vers la plateforme avec la clé
/// API configurée. Diagnostique URL injoignable / clé invalide / clé révoquée / OK. La sonde HTTP
/// est injectée (testable avec une doublure).
/// </summary>
internal sealed class TestApiCommand : ICliCommand
{
    private readonly string _defaultConfigPath;
    private readonly ISecretProtector _protector;
    private readonly Func<string, string, PlatformProbeResult> _probe;

    public TestApiCommand(string defaultConfigPath, ISecretProtector protector, Func<string, string, PlatformProbeResult> probe)
    {
        _defaultConfigPath = defaultConfigPath ?? throw new ArgumentNullException(nameof(defaultConfigPath));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public string Name => "test-api";

    public string Description => "Teste l'accès à la plateforme avec la clé API (heartbeat à blanc).";

    public int Execute(IReadOnlyList<string> args, TextWriter output)
    {
        string configPath = args.Count > 0 ? args[0] : _defaultConfigPath;

        AgentConfig config;
        try
        {
            config = AgentConfigLoader.Load(configPath);
        }
        catch (AgentConfigException ex)
        {
            output.WriteLine(CliFormat.Fail("Configuration illisible : " + ex.Message));
            return CliExitCode.ProblemDetected;
        }

        string apiKey;
        try
        {
            apiKey = _protector.Unprotect(config.ApiKeyProtected);
        }
        catch (Exception ex)
        {
            output.WriteLine(CliFormat.Fail($"Clé API non déchiffrable : {ex.Message} Re-chiffrez-la sur ce poste avec « liakont-agent-cli encrypt »."));
            return CliExitCode.ProblemDetected;
        }

        PlatformProbeResult result = _probe(config.PlatformUrl, apiKey);
        output.WriteLine(result.Success ? CliFormat.Ok(result.Message) : CliFormat.Fail(result.Message));
        return result.Success ? CliExitCode.Ok : CliExitCode.ProblemDetected;
    }
}
