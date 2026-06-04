namespace Liakont.Modules.Reconciliation.Tests.Unit.Doubles;

using System.Text;
using Liakont.Modules.Reconciliation.Application;

/// <summary>
/// Extracteur de texte fictif : interprète les « octets PDF » du test comme du texte UTF-8 (les tests
/// fournissent directement le texte à matcher). Des octets vides simulent un PDF sans texte (orphelin).
/// </summary>
internal sealed class FakePdfTextExtractor : IPdfTextExtractor
{
    public string? TryExtractText(byte[] pdfBytes) =>
        pdfBytes.Length == 0 ? null : Encoding.UTF8.GetString(pdfBytes);
}
