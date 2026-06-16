namespace Liakont.PaClients.Generique;

/// <summary>
/// Constantes du plug-in PA générique (F16 §6) : clé de registre <see cref="PaTypeKey"/> par laquelle le
/// <c>IPaClientRegistry</c> le résout (jamais par un <c>if (pa is Generique)</c>, CLAUDE.md n°8/16), nom
/// opérateur français porté dans les capacités, et gabarit de nom de fichier de l'artefact transmis.
/// </summary>
internal static class GeneriqueDefaults
{
    /// <summary>Clé de type du plug-in (insensible à la casse) — la PA « générique » du paramétrage tenant.</summary>
    public const string PaTypeKey = "Generique";

    /// <summary>Nom de la PA porté dans les messages opérateur (français — CLAUDE.md n°12).</summary>
    public const string PaName = "Générique";

    /// <summary>Type MIME de l'artefact Factur-X (PDF/A-3).</summary>
    public const string FacturXContentType = "application/pdf";

    /// <summary>Construit un nom de fichier stable pour l'artefact transmis, à partir du numéro de document.</summary>
    /// <param name="documentNumber">Numéro de document (BT-1). Assaini pour un nom de fichier sûr.</param>
    public static string FileNameFor(string documentNumber)
    {
        var safe = string.IsNullOrWhiteSpace(documentNumber)
            ? "document"
            : string.Concat(documentNumber.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return $"factur-x_{safe}.pdf";
    }
}
