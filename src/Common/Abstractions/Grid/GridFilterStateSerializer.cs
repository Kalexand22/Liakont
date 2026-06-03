namespace Stratum.Common.Abstractions.Grid;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// JSON round-trip for <see cref="GridFilterState"/> (GFI14).
/// Used to persist the last filter state per user/grid via <see cref="IGridPreferenceService"/>
/// so a page can restore it on return.
/// </summary>
/// <remarks>
/// <para>
/// Values inside <see cref="FilterCriterion.Value"/> and <see cref="FilterCriterion.ValueEnd"/>
/// are <see cref="object"/>?. System.Text.Json cannot preserve the original CLR type
/// of an <c>object</c> across a round-trip — it returns <see cref="JsonElement"/> instances
/// on deserialization. To survive this, we carry a string type tag alongside the value:
/// <c>"s"</c> (string), <c>"n"</c> (double), <c>"dec"</c> (decimal, preserves currency precision),
/// <c>"b"</c> (bool), <c>"d"</c> (DateOnly), <c>"t"</c> (DateTimeOffset), <c>"g"</c> (Guid),
/// <c>"l"</c> (list of strings). Unknown types fall back to <c>"s"</c> (string) using <c>ToString()</c>.
/// </para>
/// <para>
/// The deserialized criterion values use these raw CLR types; the
/// <see cref="FilterExpressionBuilder{T}"/> coerces them to the target property type
/// via <c>Convert.ChangeType</c> / explicit <c>DateOnly</c> handling (DF-11).
/// </para>
/// </remarks>
public static class GridFilterStateSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Returns the JSON encoding of <paramref name="state"/>. Returns <c>null</c> when the
    /// state has no active filters so the caller can clear the persisted row.
    /// </summary>
    public static string? Serialize(GridFilterState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.HasActiveFilters)
        {
            return null;
        }

        var dto = new GridFilterStateDto
        {
            GlobalSearch = state.GlobalSearch,
            AdvancedFilter = state.AdvancedFilter is null ? null : ToGroupDto(state.AdvancedFilter),
        };

        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>
    /// Parses a previously serialized <see cref="GridFilterState"/>.
    /// Returns a fresh empty state when <paramref name="json"/> is null, blank or invalid —
    /// callers never see an exception from a corrupt persisted row.
    /// </summary>
    public static GridFilterState Deserialize(string? json)
    {
        var state = new GridFilterState();
        if (string.IsNullOrWhiteSpace(json))
        {
            return state;
        }

        GridFilterStateDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<GridFilterStateDto>(json, Options);
        }
        catch (JsonException)
        {
            // Corrupt blob → empty state. Never crash the page on a stale preference.
            return state;
        }

        if (dto is null)
        {
            return state;
        }

        state.GlobalSearch = dto.GlobalSearch;

        if (dto.AdvancedFilter is not null)
        {
            state.AdvancedFilter = FromGroupDto(dto.AdvancedFilter);
        }

        // Backward compatibility (GFI16): older blobs stored "simple" as a
        // separate array alongside "advanced". Merge those criteria into the
        // unified filter tree using AddSimpleFilter so the post-GFI16 runtime
        // can still consume state persisted before the unification.
        if (dto.SimpleFilters is { Count: > 0 })
        {
            foreach (var criterionDto in dto.SimpleFilters)
            {
                var criterion = FromCriterionDto(criterionDto);
                if (criterion is not null)
                {
                    state.AddSimpleFilter(criterion);
                }
            }
        }

        return state;
    }

    private static FilterCriterionDto ToCriterionDto(FilterCriterion c)
    {
        var (valueTag, valueJson) = EncodeValue(c.Value);
        var (valueEndTag, valueEndJson) = EncodeValue(c.ValueEnd);

        return new FilterCriterionDto
        {
            Field = c.Field,
            Operator = c.Operator.ToString(),
            ValueTag = valueTag,
            Value = valueJson,
            ValueEndTag = valueEndTag,
            ValueEnd = valueEndJson,
        };
    }

    private static FilterCriterion? FromCriterionDto(FilterCriterionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Field))
        {
            return null;
        }

        if (!Enum.TryParse<FilterOperator>(dto.Operator, ignoreCase: true, out var op))
        {
            return null;
        }

        var value = DecodeValue(dto.ValueTag, dto.Value);
        var valueEnd = DecodeValue(dto.ValueEndTag, dto.ValueEnd);
        return new FilterCriterion(dto.Field, op, value, valueEnd);
    }

    private static FilterGroupDto ToGroupDto(FilterGroup group)
    {
        return new FilterGroupDto
        {
            Logic = group.Logic.ToString(),
            Criteria = group.Criteria.Count == 0
                ? null
                : group.Criteria.Select(ToCriterionDto).ToList(),
            SubGroups = group.SubGroups is { Count: > 0 }
                ? group.SubGroups.Select(ToGroupDto).ToList()
                : null,
        };
    }

    private static FilterGroup? FromGroupDto(FilterGroupDto dto)
    {
        if (!Enum.TryParse<FilterLogic>(dto.Logic, ignoreCase: true, out var logic))
        {
            return null;
        }

        var criteria = new List<FilterCriterion>();
        if (dto.Criteria is not null)
        {
            foreach (var c in dto.Criteria)
            {
                var criterion = FromCriterionDto(c);
                if (criterion is not null)
                {
                    criteria.Add(criterion);
                }
            }
        }

        var subs = new List<FilterGroup>();
        if (dto.SubGroups is not null)
        {
            foreach (var sub in dto.SubGroups)
            {
                var parsed = FromGroupDto(sub);
                if (parsed is not null)
                {
                    subs.Add(parsed);
                }
            }
        }

        return new FilterGroup(logic, criteria, subs.Count == 0 ? null : subs);
    }

    // ── Value encoding with type tags ───────────────────────────────
    private static (string? Tag, JsonElement? Json) EncodeValue(object? value)
    {
        if (value is null)
        {
            return (null, null);
        }

        // Compact type tags keep the persisted blob small and readable.
        switch (value)
        {
            case string s:
                return ("s", ToElement(s));
            case bool b:
                return ("b", ToElement(b));
            case decimal dec:
                // decimal must be handled BEFORE the generic numeric path — going
                // through Convert.ToDouble would silently lose precision for
                // currency values (e.g. 1234567.89m → 1234567.8900000001d).
                // We serialize as an invariant string and parse back to decimal.
                return ("dec", ToElement(dec.ToString("G29", System.Globalization.CultureInfo.InvariantCulture)));
            case DateOnly d:
                return ("d", ToElement(d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            case DateTimeOffset dto:
                return ("t", ToElement(dto.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
            case DateTime dt:
                return ("t", ToElement(new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero).ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
            case Guid g:
                return ("g", ToElement(g.ToString("D")));
            case System.Collections.IEnumerable enumerable when value is not string:
                {
                    var list = new List<string>();
                    foreach (var item in enumerable)
                    {
                        list.Add(item?.ToString() ?? string.Empty);
                    }

                    return ("l", ToElement(list));
                }
        }

        if (IsNumeric(value))
        {
            var d = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            return ("n", ToElement(d));
        }

        return ("s", ToElement(value.ToString() ?? string.Empty));
    }

    private static object? DecodeValue(string? tag, JsonElement? json)
    {
        if (tag is null || json is null || json.Value.ValueKind == JsonValueKind.Null || json.Value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var el = json.Value;
        return tag switch
        {
            "s" => el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText(),
            "b" => el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False
                ? el.GetBoolean()
                : bool.TryParse(el.GetString(), out var bv) && bv,
            "n" => el.ValueKind == JsonValueKind.Number
                ? el.GetDouble()
                : double.TryParse(el.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nv) ? nv : 0d,
            "dec" => decimal.TryParse(
                        el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var decv)
                    ? decv
                    : (object?)null,
            "d" => DateOnly.TryParseExact(
                        el.GetString(),
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out var d)
                    ? d
                    : null,
            "t" => DateTimeOffset.TryParse(
                        el.GetString(),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var t)
                    ? t
                    : null,
            "g" => Guid.TryParse(el.GetString(), out var g) ? g : null,
            "l" => el.ValueKind == JsonValueKind.Array
                ? el.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? string.Empty : x.GetRawText()).ToList()
                : null,
            _ => el.ToString(),
        };
    }

    private static JsonElement ToElement<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    private static bool IsNumeric(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    // ── DTO shapes ──────────────────────────────────────────────────
    private sealed class GridFilterStateDto
    {
        [JsonPropertyName("q")]
        public string? GlobalSearch { get; set; }

        [JsonPropertyName("simple")]
        public List<FilterCriterionDto>? SimpleFilters { get; set; }

        [JsonPropertyName("advanced")]
        public FilterGroupDto? AdvancedFilter { get; set; }
    }

    private sealed class FilterCriterionDto
    {
        [JsonPropertyName("f")]
        public string Field { get; set; } = string.Empty;

        [JsonPropertyName("op")]
        public string Operator { get; set; } = string.Empty;

        [JsonPropertyName("vt")]
        public string? ValueTag { get; set; }

        [JsonPropertyName("v")]
        public JsonElement? Value { get; set; }

        [JsonPropertyName("vet")]
        public string? ValueEndTag { get; set; }

        [JsonPropertyName("ve")]
        public JsonElement? ValueEnd { get; set; }
    }

    private sealed class FilterGroupDto
    {
        [JsonPropertyName("logic")]
        public string Logic { get; set; } = string.Empty;

        [JsonPropertyName("criteria")]
        public List<FilterCriterionDto>? Criteria { get; set; }

        [JsonPropertyName("subs")]
        public List<FilterGroupDto>? SubGroups { get; set; }
    }
}
