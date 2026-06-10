namespace Liakont.Agent.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Liakont.Agent.Core.Hosting;

/// <summary>
/// Identité d'une INSTANCE de l'agent sur un poste (OPS05 pt 5, décision 2026-06-10) : sur un
/// serveur mutualisé hébergeant plusieurs bases clientes (une par client), chaque base est servie
/// par sa propre instance — un service Windows, une configuration, un tampon local et un verrou de
/// run par instance. Toutes les dérivations dépendantes de l'instance (nom de service, mutex,
/// répertoire de données) sont centralisées ici : aucun composant ne refabrique ces noms lui-même.
/// <para>
/// L'instance <see cref="Default"/> conserve STRICTEMENT les noms et chemins historiques
/// (service « LiakontAgent », mutex <see cref="InterProcessRunLock.DefaultMutexName"/>,
/// racine <c>%ProgramData%\Liakont</c>) : les installations mono-instance déjà déployées
/// ne changent pas de comportement.
/// </para>
/// </summary>
public sealed class AgentInstance
{
    /// <summary>Nom de l'instance par défaut (insensible à la casse au parsing).</summary>
    public const string DefaultName = "Default";

    /// <summary>Option de ligne de commande portant le nom d'instance (service ET CLI).</summary>
    public const string CommandLineOption = "--instance";

    // Lettres/chiffres, tirets et soulignés, première position alphanumérique, 32 caractères max :
    // le nom entre dans un nom de service Windows, un nom de mutex et un nom de répertoire.
    private static readonly Regex NamePattern = new Regex(
        "^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Noms interdits : sous-répertoires/objets de l'instance Default vivant à la racine partagée
    // %ProgramData%\Liakont (une instance nommée « logs » écraserait les journaux de Default),
    // et noms de périphériques réservés par Windows (invalides comme nom de répertoire).
    private static readonly string[] ReservedNames =
    {
        "logs", "update-work",
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private AgentInstance(string name)
    {
        Name = name;
    }

    /// <summary>L'instance par défaut — comportement identique à l'agent mono-instance historique.</summary>
    public static AgentInstance Default { get; } = new AgentInstance(DefaultName);

    /// <summary>Nom validé de l'instance (« Default » pour l'instance par défaut).</summary>
    public string Name { get; }

    /// <summary>Vrai pour l'instance par défaut (noms et chemins historiques).</summary>
    public bool IsDefault => string.Equals(Name, DefaultName, StringComparison.Ordinal);

    /// <summary>
    /// Nom du service Windows : <c>LiakontAgent</c> (Default) ou <c>LiakontAgent$&lt;nom&gt;</c>
    /// (convention des instances nommées, à la façon de SQL Server).
    /// </summary>
    public string ServiceName => IsDefault ? "LiakontAgent" : "LiakontAgent$" + Name;

    /// <summary>Nom d'affichage du service dans la console Services de Windows.</summary>
    public string DisplayName => IsDefault ? "Liakont Agent" : "Liakont Agent (" + Name + ")";

    /// <summary>
    /// Nom du mutex de sérialisation des runs (service + CLI) PROPRE à l'instance : deux instances
    /// sur le même poste extraient en parallèle sans se bloquer mutuellement, mais le run planifié
    /// et le run manuel d'une MÊME instance restent mutuellement exclusifs.
    /// <para>
    /// La composante instance est CANONICALISÉE en majuscules : les chemins Windows sont insensibles
    /// à la casse (« ClientA » et « clienta » partagent le même répertoire de données et la même
    /// file SQLite) mais les mutex nommés sont sensibles à la casse — sans canonicalisation, le
    /// service et un CLI lancé avec une autre casse prendraient deux verrous différents et
    /// extrairaient en parallèle sur la même file.
    /// </para>
    /// </summary>
    public string RunMutexName =>
        IsDefault
            ? InterProcessRunLock.DefaultMutexName
            : InterProcessRunLock.DefaultMutexName + "-" + Name.ToUpperInvariant();

    /// <summary>
    /// Racine des données de l'instance : <c>%ProgramData%\Liakont</c> (Default) ou
    /// <c>%ProgramData%\Liakont\&lt;nom&gt;</c> (instance nommée).
    /// </summary>
    public string DataDirectory
    {
        get
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Liakont");
            return IsDefault ? root : Path.Combine(root, Name);
        }
    }

    /// <summary>
    /// Valide <paramref name="rawName"/> et produit l'instance correspondante. « default »
    /// (toute casse) est normalisé vers <see cref="Default"/>. Message d'erreur en français,
    /// orienté intégrateur (CLAUDE.md n°12).
    /// </summary>
    public static bool TryParse(string? rawName, out AgentInstance instance, out string? error)
    {
        instance = Default;
        error = null;

        string trimmed = (rawName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            error = "Nom d'instance vide. Indiquez un nom (lettres, chiffres, « - » ou « _ », " +
                    "32 caractères maximum), ou omettez l'option pour l'instance par défaut.";
            return false;
        }

        if (string.Equals(trimmed, DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!NamePattern.IsMatch(trimmed))
        {
            error = $"Nom d'instance invalide : « {trimmed} ». Règle : lettres ou chiffres en première " +
                    "position, puis lettres, chiffres, « - » ou « _ », 32 caractères maximum.";
            return false;
        }

        if (ReservedNames.Any(r => string.Equals(r, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Nom d'instance réservé : « {trimmed} ». Choisissez un autre nom.";
            return false;
        }

        instance = new AgentInstance(trimmed);
        return true;
    }

    /// <summary>
    /// Extrait l'option <c>--instance &lt;nom&gt;</c> de la ligne de commande (où qu'elle se trouve)
    /// et rend les arguments restants. Option absente = instance <see cref="Default"/>. Échec
    /// (valeur manquante, nom invalide, option en double) : <paramref name="error"/> est renseigné
    /// en français et la méthode rend <c>false</c>.
    /// </summary>
    public static bool TryFromCommandLine(
        string[]? args,
        out AgentInstance instance,
        out string[] remainingArgs,
        out string? error)
    {
        instance = Default;
        remainingArgs = args ?? Array.Empty<string>();
        error = null;

        if (args is null || args.Length == 0)
        {
            return true;
        }

        var remaining = new List<string>(args.Length);
        string? rawName = null;
        bool seen = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], CommandLineOption, StringComparison.OrdinalIgnoreCase))
            {
                if (seen)
                {
                    error = $"Option « {CommandLineOption} » indiquée plusieurs fois — une seule instance par processus.";
                    return false;
                }

                seen = true;
                if (i + 1 >= args.Length)
                {
                    error = $"Option « {CommandLineOption} » sans valeur. Usage : {CommandLineOption} <nom>.";
                    return false;
                }

                rawName = args[++i];
                continue;
            }

            remaining.Add(args[i]);
        }

        if (seen && !TryParse(rawName, out instance, out error))
        {
            return false;
        }

        remainingArgs = remaining.ToArray();
        return true;
    }
}
