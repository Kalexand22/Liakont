namespace Liakont.Modules.Reconciliation.Infrastructure;

using System;
using System.Text;
using Liakont.Modules.Reconciliation.Application;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

/// <summary>
/// Extraction de texte d'un PDF via PdfPig (Apache-2.0 — ADR-0010), pour la stratégie 2 du rapprochement
/// (numéro de document dans le texte du PDF, item TRK07, F06 §7 §1). Pas d'OCR en V1 : un PDF scanné
/// (image) ne renvoie pas de texte. Conformément au contrat du port, un PDF illisible ou malformé ne
/// lève JAMAIS — il renvoie <c>null</c> et le PDF devient un orphelin (la passe de réconciliation n'est
/// pas interrompue par un fichier corrompu).
/// </summary>
internal sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public string? TryExtractText(byte[] pdfBytes)
    {
        if (pdfBytes is null || pdfBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using PdfDocument document = PdfDocument.Open(pdfBytes);
            var builder = new StringBuilder();
            foreach (Page page in document.GetPages())
            {
                builder.AppendLine(page.Text);
            }

            string text = builder.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            // Contenu non-PDF, PDF tronqué/chiffré/malformé : pas de texte exploitable → orphelin.
            // (catch large assumé : PdfPig lève des types variés selon le défaut du fichier ; aucune
            // annulation n'est en jeu ici, l'opération est purement CPU/mémoire.)
            return null;
        }
    }
}
