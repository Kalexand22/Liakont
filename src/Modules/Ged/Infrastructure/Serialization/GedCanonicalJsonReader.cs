namespace Liakont.Modules.Ged.Infrastructure.Serialization;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Liakont.Agent.Contracts.Ged;

/// <summary>
/// Désérialise le JSON canonique GED (F19 §4.2, <c>GedCanonicalJson</c>) relu depuis le magasin de staging par le
/// consommateur d'ingestion (GED05b). MIROIR EXACT de <c>GedCanonicalJson.WriteDocument</c> : mêmes noms de membre,
/// optionnels omis, <c>SourceFields</c> objet toujours présent, horodatage <c>yyyy-MM-ddTHH:mm:ssZ</c>. Lecteur
/// EXPLICITE (parcours de <see cref="JsonDocument"/> + constructeurs immuables, AUCUNE réflexion). Il vit DANS le
/// module GED (pas dans <c>Liakont.Agent.Contracts.Ged</c>, BCL-only). Il NE RE-SÉRIALISE PAS pour ré-hacher : la
/// re-vérification du <c>payload_hash</c> est faite par <c>IPayloadStagingStore.ReadAsync</c> sur la string brute.
/// </summary>
public static class GedCanonicalJsonReader
{
    /// <summary>Reconstruit un document GED ingéré depuis son JSON canonique.</summary>
    /// <param name="canonicalJson">Le JSON canonique (tel que produit par <c>GedCanonicalJson</c>).</param>
    /// <returns>Le document ingéré reconstruit.</returns>
    public static IngestedDocumentDto Read(string canonicalJson)
    {
        ArgumentNullException.ThrowIfNull(canonicalJson);

        using JsonDocument document = JsonDocument.Parse(canonicalJson);
        return ReadDocument(document.RootElement);
    }

    private static IngestedDocumentDto ReadDocument(JsonElement element) => new(
        sourceReference: Str(element, "SourceReference"),
        documentType: Str(element, "DocumentType"),
        sourceTimestampUtc: TimestampOrNull(element, "SourceTimestampUtc"),
        content: element.TryGetProperty("Content", out JsonElement content) ? ReadContent(content) : null,
        sourceFields: ReadFields(element.GetProperty("SourceFields")),
        sourceAxes: ReadList(element, "SourceAxes", ReadAxis),
        sourceEntities: ReadList(element, "SourceEntities", ReadEntity),
        sourceRelations: ReadList(element, "SourceRelations", ReadRelation));

    // Horodatage optionnel : miroir exact de GedCanonicalJson.WriteDateTimeUtc (yyyy-MM-ddTHH:mm:ssZ, Kind ignoré).
    // Absent du JSON → null (le writer omet un optionnel absent).
    private static DateTime? TimestampOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
            ? DateTime.ParseExact(value.GetString()!, "yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.None)
            : null;

    private static IngestedContentRef ReadContent(JsonElement element) => new(
        contentRef: Str(element, "ContentRef"),
        mediaType: Str(element, "MediaType"),
        byteLength: element.GetProperty("ByteLength").GetInt64(),
        contentHash: Str(element, "ContentHash"));

    private static Dictionary<string, string> ReadFields(JsonElement element)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            fields[property.Name] = property.Value.GetString()!;
        }

        return fields;
    }

    private static RawAxisHint ReadAxis(JsonElement element) => new(
        name: Str(element, "Name"),
        values: StrList(element, "Values"));

    private static RawEntityHint ReadEntity(JsonElement element) => new(
        type: Str(element, "Type"),
        externalId: Str(element, "ExternalId"),
        display: StrOrNull(element, "Display"));

    private static RawRelationHint ReadRelation(JsonElement element) => new(
        type: Str(element, "Type"),
        targetExternalId: Str(element, "TargetExternalId"),
        targetType: Str(element, "TargetType"));

    private static string Str(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    private static string? StrOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;

    private static List<string> StrList(JsonElement element, string name)
    {
        JsonElement array = element.GetProperty(name);
        var list = new List<string>(array.GetArrayLength());
        foreach (JsonElement item in array.EnumerateArray())
        {
            list.Add(item.GetString()!);
        }

        return list;
    }

    private static List<T> ReadList<T>(JsonElement element, string name, Func<JsonElement, T> read)
    {
        JsonElement array = element.GetProperty(name);
        var list = new List<T>(array.GetArrayLength());
        foreach (JsonElement item in array.EnumerateArray())
        {
            list.Add(read(item));
        }

        return list;
    }
}
