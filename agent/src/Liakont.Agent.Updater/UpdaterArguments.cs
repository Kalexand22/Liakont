namespace Liakont.Agent.Updater;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Analyse les arguments de ligne de commande de l'updater (<c>--clé valeur</c>) en un
/// <see cref="UpdaterPlan"/>. C'est le CONTRAT agent ↔ updater (ADR-0013) — couplage faible, aucun
/// type partagé. Un argument requis manquant produit une erreur explicite (français), jamais une levée.
/// </summary>
public static class UpdaterArguments
{
    private const int DefaultHealthTimeoutSeconds = 300;

    /// <summary>Tente de construire un plan à partir des arguments.</summary>
    /// <param name="args">Arguments de ligne de commande.</param>
    /// <param name="plan">Le plan construit, ou <c>null</c>.</param>
    /// <param name="logPath">Chemin du fichier de log (optionnel), ou <c>null</c>.</param>
    /// <param name="error">Message d'erreur si le plan n'a pas pu être construit, ou <c>null</c>.</param>
    /// <returns><c>true</c> si le plan est complet.</returns>
    public static bool TryParse(string[] args, out UpdaterPlan? plan, out string? logPath, out string? error)
    {
        plan = null;
        logPath = null;
        error = null;

        Dictionary<string, string> map = BuildMap(args);
        logPath = Get(map, "log");

        string? targetVersion = Get(map, "target-version");
        string? staging = Get(map, "staging");
        string? install = Get(map, "install");
        string? backup = Get(map, "backup");
        string? service = Get(map, "service");
        string? status = Get(map, "status");
        string? marker = Get(map, "heartbeat-marker");

        string? missing = FirstMissing(
            ("target-version", targetVersion),
            ("staging", staging),
            ("install", install),
            ("backup", backup),
            ("service", service),
            ("status", status),
            ("heartbeat-marker", marker));
        if (missing != null)
        {
            error = "Argument requis manquant : --" + missing;
            return false;
        }

        if (!int.TryParse(Get(map, "health-timeout-seconds"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) || seconds <= 0)
        {
            seconds = DefaultHealthTimeoutSeconds;
        }

        plan = new UpdaterPlan(
            targetVersion!,
            staging!,
            install!,
            backup!,
            service!,
            TimeSpan.FromSeconds(seconds),
            marker!,
            status!);
        return true;
    }

    private static Dictionary<string, string> BuildMap(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (args == null)
        {
            return map;
        }

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != null && args[i].StartsWith("--", StringComparison.Ordinal))
            {
                map[args[i].Substring(2)] = args[i + 1];
                i++;
            }
        }

        return map;
    }

    private static string? Get(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string value) ? value : null;

    private static string? FirstMissing(params (string Name, string? Value)[] required)
    {
        foreach ((string name, string? value) in required)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return name;
            }
        }

        return null;
    }
}
