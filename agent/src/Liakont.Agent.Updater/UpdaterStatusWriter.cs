namespace Liakont.Agent.Updater;

using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// Écrit le statut final de l'updater dans le fichier JSON partagé avec l'agent (ADR-0013). La FORME
/// (noms de propriété) est le contrat avec <c>Liakont.Agent.Core.Update.AutoUpdateStateStore</c> qui le
/// relit pour le signalement au heartbeat (F12 §2.5). JSON écrit à la main (updater autonome, sans
/// sérialiseur). Best-effort : un échec d'écriture ne change pas l'issue déjà obtenue.
/// </summary>
public static class UpdaterStatusWriter
{
    /// <summary>Écrit le statut correspondant à <paramref name="result"/>.</summary>
    /// <param name="statusPath">Fichier de statut.</param>
    /// <param name="targetVersion">Version visée.</param>
    /// <param name="result">Résultat du cycle d'updater.</param>
    public static void Write(string statusPath, string targetVersion, UpdaterResult result)
    {
        if (string.IsNullOrEmpty(statusPath) || result == null)
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(statusPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var builder = new StringBuilder();
            builder.Append('{');
            builder.Append("\"targetVersion\":").Append(JsonString(targetVersion)).Append(',');
            builder.Append("\"phase\":").Append(JsonString(result.Outcome.ToString())).Append(',');
            builder.Append("\"succeeded\":").Append(result.Succeeded ? "true" : "false").Append(',');
            builder.Append("\"message\":").Append(JsonString(result.Message)).Append(',');
            builder.Append("\"atUtc\":").Append(JsonString(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
            builder.Append('}');

            File.WriteAllText(statusPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (IOException)
        {
            // Best-effort.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string JsonString(string? value)
    {
        if (value == null)
        {
            return "null";
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
