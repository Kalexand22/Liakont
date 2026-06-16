namespace Liakont.Modules.FacturX.Infrastructure.Pdf;

using Liakont.Modules.FacturX.Domain;

/// <summary>
/// Construit le bloc XMP d'extension Factur-X <c>fx:</c> injecté dans le flux <c>/Metadata</c> du PDF/A-3
/// via <c>DocumentOperation.ExtendMetadata</c> (QuestPDF ne le génère PAS automatiquement). Conforme à
/// ADR-0023 §3 / INV-FX-3 : URI d'extension <see cref="FacturXProfile.XmpExtensionUri"/> (casse + « # »
/// final), <c>fx:ConformanceLevel = EN 16931</c> (avec l'espace), <c>fx:DocumentType = INVOICE</c>,
/// <c>fx:DocumentFileName = factur-x.xml</c>, <c>fx:Version = 1.0</c>. Deux <c>rdf:Description</c> : le
/// bloc <c>fx:</c> ET la description du schéma d'extension PDF/A (<c>pdfaExtension:schemas</c>), requise
/// pour qu'un PDF/A-3 portant des propriétés XMP hors schéma standard reste conforme (veraPDF l'exige).
/// Toutes les valeurs proviennent de constantes de PROFIL (non fiscales) — rien d'inventé (CLAUDE.md n°2).
/// </summary>
internal static class FacturXXmpMetadata
{
    /// <summary>Produit le fragment RDF/XMP <c>fx:</c> à passer à <c>ExtendMetadata</c>.</summary>
    public static string Build()
    {
        // Valeurs interpolées depuis des constantes de profil (non fiscales). Modèle aligné sur l'exemple
        // ZUGFeRD officiel de QuestPDF (resource-zugferd-metadata.xml) : le bloc fx: + la déclaration du
        // schéma d'extension PDF/A (pdfaExtension). Le préfixe « fx » est conventionnel.
        return
            $"<rdf:Description xmlns:fx=\"{FacturXProfile.XmpExtensionUri}\" rdf:about=\"\">\n" +
            $"    <fx:ConformanceLevel>{FacturXProfile.XmpConformanceLevel}</fx:ConformanceLevel>\n" +
            $"    <fx:DocumentType>{FacturXProfile.XmpDocumentType}</fx:DocumentType>\n" +
            $"    <fx:DocumentFileName>{FacturXProfile.AttachmentFileName}</fx:DocumentFileName>\n" +
            $"    <fx:Version>{FacturXProfile.XmpVersion}</fx:Version>\n" +
            "</rdf:Description>\n" +
            "<rdf:Description xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\"" +
            " xmlns:pdfaProperty=\"http://www.aiim.org/pdfa/ns/property#\"" +
            " xmlns:pdfaSchema=\"http://www.aiim.org/pdfa/ns/schema#\" rdf:about=\"\">\n" +
            "    <pdfaExtension:schemas>\n" +
            "        <rdf:Bag>\n" +
            "            <rdf:li rdf:parseType=\"Resource\">\n" +
            "                <pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>\n" +
            $"                <pdfaSchema:namespaceURI>{FacturXProfile.XmpExtensionUri}</pdfaSchema:namespaceURI>\n" +
            "                <pdfaSchema:prefix>fx</pdfaSchema:prefix>\n" +
            "                <pdfaSchema:property>\n" +
            "                    <rdf:Seq>\n" +
            "                        <rdf:li rdf:parseType=\"Resource\">\n" +
            "                            <pdfaProperty:name>DocumentFileName</pdfaProperty:name>\n" +
            "                            <pdfaProperty:valueType>Text</pdfaProperty:valueType>\n" +
            "                            <pdfaProperty:category>external</pdfaProperty:category>\n" +
            "                            <pdfaProperty:description>name of the embedded XML invoice file</pdfaProperty:description>\n" +
            "                        </rdf:li>\n" +
            "                        <rdf:li rdf:parseType=\"Resource\">\n" +
            "                            <pdfaProperty:name>DocumentType</pdfaProperty:name>\n" +
            "                            <pdfaProperty:valueType>Text</pdfaProperty:valueType>\n" +
            "                            <pdfaProperty:category>external</pdfaProperty:category>\n" +
            "                            <pdfaProperty:description>INVOICE</pdfaProperty:description>\n" +
            "                        </rdf:li>\n" +
            "                        <rdf:li rdf:parseType=\"Resource\">\n" +
            "                            <pdfaProperty:name>Version</pdfaProperty:name>\n" +
            "                            <pdfaProperty:valueType>Text</pdfaProperty:valueType>\n" +
            "                            <pdfaProperty:category>external</pdfaProperty:category>\n" +
            "                            <pdfaProperty:description>The actual version of the Factur-X XML schema</pdfaProperty:description>\n" +
            "                        </rdf:li>\n" +
            "                        <rdf:li rdf:parseType=\"Resource\">\n" +
            "                            <pdfaProperty:name>ConformanceLevel</pdfaProperty:name>\n" +
            "                            <pdfaProperty:valueType>Text</pdfaProperty:valueType>\n" +
            "                            <pdfaProperty:category>external</pdfaProperty:category>\n" +
            "                            <pdfaProperty:description>The selected Factur-X profile</pdfaProperty:description>\n" +
            "                        </rdf:li>\n" +
            "                    </rdf:Seq>\n" +
            "                </pdfaSchema:property>\n" +
            "            </rdf:li>\n" +
            "        </rdf:Bag>\n" +
            "    </pdfaExtension:schemas>\n" +
            "</rdf:Description>";
    }
}
