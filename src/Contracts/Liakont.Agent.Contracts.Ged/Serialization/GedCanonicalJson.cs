namespace Liakont.Agent.Contracts.Ged.Serialization;

using System;
using System.Collections.Generic;
using System.Linq;
using Liakont.Agent.Contracts.Serialization;

/// <summary>
/// Sérialisation JSON canonique du canal GED (F19 §4.2/§4.5), bâtie sur l'UNIQUE
/// <see cref="CanonicalJsonWriter"/> partagé (mêmes règles de format figées qu'<c>ADR-0007</c> :
/// membres dans l'ordre de déclaration, noms PascalCase, optionnels <c>null</c> OMIS, chaînes NFC/ASCII,
/// horodatage <c>yyyy-MM-ddTHH:mm:ssZ</c>). Un seul code, compilé côté agent (net48) ET plateforme
/// (.NET 10) → sortie identique octet par octet (golden cross-runtime, RL-39).
///
/// Spécificité GED : <see cref="IngestedDocumentDto.SourceFields"/> est un dictionnaire (ordre
/// d'itération NON garanti) — il est émis TRIÉ PAR CLÉ en comparaison ORDINALE, sinon l'anti-doublon
/// <c>(tenant, payload_hash)</c> du registre GED casserait selon l'ordre de parcours (RL-39). Aucune
/// logique métier : ni calcul, ni validation, ni interprétation d'axe/entité (celle-ci vit sur la
/// plateforme). C'est le JSON hashé par <c>PayloadHasher.ComputeHash(string)</c> (primitive réutilisée
/// telle quelle) pour l'espace de hash GED, STRICTEMENT séparé du canal fiscal (§4.1/§4.3.1).
/// </summary>
public static class GedCanonicalJson
{
    /// <summary>Sérialise un document GED ingéré en JSON canonique.</summary>
    /// <param name="document">Le document à sérialiser (non nul).</param>
    /// <returns>Le JSON canonique (ASCII, compact, déterministe ; <c>SourceFields</c> trié par clé ordinal).</returns>
    public static string Serialize(IngestedDocumentDto document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var writer = new CanonicalJsonWriter();
        WriteDocument(writer, document);
        return writer.ToString();
    }

    private static void WriteDocument(CanonicalJsonWriter writer, IngestedDocumentDto document)
    {
        writer.BeginObject();

        writer.WritePropertyName("SourceReference");
        writer.WriteString(document.SourceReference);
        writer.WritePropertyName("DocumentType");
        writer.WriteString(document.DocumentType);

        // Horodatage optionnel : OMIS quand la source ne le porte pas (hash inchangé, comme un optionnel pivot).
        if (document.SourceTimestampUtc.HasValue)
        {
            writer.WritePropertyName("SourceTimestampUtc");
            writer.WriteDateTimeUtc(document.SourceTimestampUtc.Value);
        }

        // Binaire optionnel : OMIS quand absent (métadonnées seules).
        if (document.Content != null)
        {
            writer.WritePropertyName("Content");
            WriteContent(writer, document.Content);
        }

        // SourceFields : dictionnaire émis TRIÉ PAR CLÉ (ordinal) — RL-39. Toujours émis (objet, même vide),
        // comme une collection pivot ; l'anti-doublon dépend de cet ordre déterministe.
        writer.WritePropertyName("SourceFields");
        WriteSortedFields(writer, document.SourceFields);

        writer.WritePropertyName("SourceAxes");
        WriteArray(writer, document.SourceAxes, WriteAxis);
        writer.WritePropertyName("SourceEntities");
        WriteArray(writer, document.SourceEntities, WriteEntity);
        writer.WritePropertyName("SourceRelations");
        WriteArray(writer, document.SourceRelations, WriteRelation);

        writer.EndObject();
    }

    private static void WriteContent(CanonicalJsonWriter writer, IngestedContentRef content)
    {
        writer.BeginObject();
        writer.WritePropertyName("ContentRef");
        writer.WriteString(content.ContentRef);
        writer.WritePropertyName("MediaType");
        writer.WriteString(content.MediaType);
        writer.WritePropertyName("ByteLength");
        writer.WriteDecimal(content.ByteLength);
        writer.WritePropertyName("ContentHash");
        writer.WriteString(content.ContentHash);
        writer.EndObject();
    }

    private static void WriteSortedFields(CanonicalJsonWriter writer, IReadOnlyDictionary<string, string> fields)
    {
        writer.BeginObject();

        // Tri ORDINAL de la clé (RL-39) : indépendant de la culture et de l'ordre d'insertion du dictionnaire,
        // donc identique net48/.NET 10. Le nom de champ est émis comme un nom de membre (WritePropertyName) ;
        // la valeur comme une chaîne libre (WriteString, NFC/ASCII).
        foreach (KeyValuePair<string, string> field in fields.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(field.Key);
            writer.WriteString(field.Value);
        }

        writer.EndObject();
    }

    private static void WriteAxis(CanonicalJsonWriter writer, RawAxisHint axis)
    {
        writer.BeginObject();
        writer.WritePropertyName("Name");
        writer.WriteString(axis.Name);
        writer.WritePropertyName("Values");
        WriteStringArray(writer, axis.Values);
        writer.EndObject();
    }

    private static void WriteEntity(CanonicalJsonWriter writer, RawEntityHint entity)
    {
        writer.BeginObject();
        writer.WritePropertyName("Type");
        writer.WriteString(entity.Type);
        writer.WritePropertyName("ExternalId");
        writer.WriteString(entity.ExternalId);

        // Libellé optionnel : OMIS quand la source n'en fournit pas (symétrie pivot « absent → null »).
        if (entity.Display != null)
        {
            writer.WritePropertyName("Display");
            writer.WriteString(entity.Display);
        }

        writer.EndObject();
    }

    private static void WriteRelation(CanonicalJsonWriter writer, RawRelationHint relation)
    {
        writer.BeginObject();
        writer.WritePropertyName("Type");
        writer.WriteString(relation.Type);
        writer.WritePropertyName("TargetExternalId");
        writer.WriteString(relation.TargetExternalId);
        writer.WritePropertyName("TargetType");
        writer.WriteString(relation.TargetType);
        writer.EndObject();
    }

    private static void WriteArray<T>(
        CanonicalJsonWriter writer,
        IReadOnlyList<T> items,
        Action<CanonicalJsonWriter, T> writeItem)
    {
        writer.BeginArray();
        foreach (T item in items)
        {
            writer.BeginArrayElement();
            writeItem(writer, item);
        }

        writer.EndArray();
    }

    private static void WriteStringArray(CanonicalJsonWriter writer, IReadOnlyList<string> items)
    {
        writer.BeginArray();
        foreach (string item in items)
        {
            writer.BeginArrayElement();
            writer.WriteString(item);
        }

        writer.EndArray();
    }
}
