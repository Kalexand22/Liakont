namespace Liakont.PaClients.ChorusPro;

using System;
using System.Text.Json;
using Liakont.PaClients.ChorusPro.Wire;

/// <summary>
/// Construit le payload JSON du dépôt <c>deposerFluxFacture</c> (F18 §3.1) à partir de l'artefact Factur-X
/// DÉJÀ scellé (octets opaques) et du numéro de document. Transport PUR : aucun montant, aucune donnée
/// fiscale manipulés — le plug-in encode l'artefact en base64 et fixe les constantes de syntaxe / signature
/// (CLAUDE.md n°6/2). <c>internal static</c> : isolé pour être exercé en test sans réseau, jamais exposé.
/// </summary>
internal static class ChorusProPayloadBuilder
{
    /// <summary>
    /// Sérialise le payload <c>deposerFluxFacture</c> : <c>fichierFlux</c> = base64 de l'artefact,
    /// <c>nomFichier</c> dérivé du numéro, <c>syntaxeFlux</c> = <see cref="ChorusProDefaults.SyntaxeFluxFacturX"/>,
    /// <c>avecSignature</c> = <see cref="ChorusProDefaults.DepositWithSignature"/>. <c>idUtilisateurCourant</c>
    /// est omis (F18 §3.2, cardinalité non verrouillée — voir <see cref="ChorusProDepositRequest"/>).
    /// </summary>
    /// <param name="artifact">Octets du PDF/A-3 Factur-X scellé. Doit être non vide (garde appelée en amont).</param>
    /// <param name="documentNumber">Numéro de document (BT-1), pour le <c>nomFichier</c>.</param>
    /// <returns>Le payload JSON prêt à transmettre (camelCase EXACT, F18 §3.1).</returns>
    public static string Build(ReadOnlyMemory<byte> artifact, string documentNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        if (artifact.IsEmpty)
        {
            throw new ArgumentException("L'artefact Factur-X est vide (la garde de dépôt aurait dû bloquer).", nameof(artifact));
        }

        var request = new ChorusProDepositRequest(
            FichierFlux: Convert.ToBase64String(artifact.Span),
            NomFichier: ChorusProDefaults.FileNameFor(documentNumber),
            SyntaxeFlux: ChorusProDefaults.SyntaxeFluxFacturX,
            AvecSignature: ChorusProDefaults.DepositWithSignature);

        return JsonSerializer.Serialize(request, ChorusProJson.Options);
    }
}
