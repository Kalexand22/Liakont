namespace Liakont.Agent.Contracts.ContractTests;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Liakont.Agent.Contracts.Pivot;

// Ce fichier est compilé À LA FOIS en .NET 10 et en net48 (lié dans les deux projets de test).
// CA1510 (ArgumentNullException.ThrowIfNull) et CA2263 (Enum.Parse<TEnum> générique) suggèrent des
// API .NET 6+/Core qui n'existent PAS en net48 : on conserve les formes compatibles des deux côtés.
#pragma warning disable CA1510, CA2263

/// <summary>
/// Lecteur JSON canonique de TEST (PIV02). Reconstruit un <see cref="PivotDocumentDto"/> à partir du
/// JSON canonique produit par <c>CanonicalJson.Serialize</c>, pour prouver le round-trip sans perte
/// (acceptance PIV02). Ce fichier est LIÉ dans les deux projets de test (plateforme .NET 10 ET agent
/// net48) : un seul lecteur, exécuté des deux côtés. Il n'analyse que la grammaire canonique
/// (compacte, déterministe) — ce n'est pas un parseur JSON généraliste, et il ne vit pas dans
/// l'assembly de production (le contrat n'a besoin que du writer + du hasher).
/// </summary>
public static class PivotCanonicalReader
{
    /// <summary>Désérialise un document pivot depuis son JSON canonique.</summary>
    /// <param name="json">Le JSON canonique (tel que produit par le writer).</param>
    /// <returns>Le document pivot reconstruit.</returns>
    public static PivotDocumentDto ReadDocument(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        var parser = new Parser(json);
        var root = (IDictionary<string, object?>)parser.ParseValue()!;
        return BuildDocument(root);
    }

    /// <summary>Désérialise le JSON canonique en arbre générique (objets, tableaux, valeurs) pour
    /// inspection structurelle par les tests (clés par nœud).</summary>
    /// <param name="json">Le JSON canonique.</param>
    /// <returns>L'objet racine sous forme de dictionnaire clé → valeur.</returns>
    public static IDictionary<string, object?> ParseToMap(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        return (IDictionary<string, object?>)new Parser(json).ParseValue()!;
    }

    private static PivotDocumentDto BuildDocument(IDictionary<string, object?> map)
    {
        return new PivotDocumentDto(
            sourceDocumentKind: Text(map, "SourceDocumentKind"),
            number: Text(map, "Number"),
            issueDate: Date(map, "IssueDate"),
            sourceReference: Text(map, "SourceReference"),
            supplier: BuildParty(Object(map, "Supplier")),
            totals: BuildTotals(Object(map, "Totals")),
            operationCategory: (OperationCategory)Enum.Parse(typeof(OperationCategory), Text(map, "OperationCategory")),
            currencyCode: Text(map, "CurrencyCode"),
            customer: ObjectOrNull(map, "Customer") is { } customer ? BuildParty(customer) : null,
            lines: BuildList(map, "Lines", BuildLine),
            creditNoteRefs: BuildList(map, "CreditNoteRefs", BuildReference),
            payments: BuildList(map, "Payments", BuildPayment),
            documentCharges: BuildList(map, "DocumentCharges", BuildCharge),
            invoicer: ObjectOrNull(map, "Invoicer") is { } invoicer ? BuildParty(invoicer) : null,
            payee: ObjectOrNull(map, "Payee") is { } payee ? BuildParty(payee) : null,
            isSelfBilled: Boolean(map, "IsSelfBilled"),
            prepaidAmount: DecimalOrNull(map, "PrepaidAmount"),
            sourceData: TextOrNull(map, "SourceData"),
            paymentDueDate: DateOrNull(map, "PaymentDueDate"),
            invoicePeriod: ObjectOrNull(map, "InvoicePeriod") is { } invoicePeriod ? BuildInvoicePeriod(invoicePeriod) : null);
    }

    private static PivotInvoicePeriodDto BuildInvoicePeriod(IDictionary<string, object?> map)
    {
        return new PivotInvoicePeriodDto(
            startDate: Date(map, "StartDate"),
            endDate: Date(map, "EndDate"));
    }

    private static PivotPartyDto BuildParty(IDictionary<string, object?> map)
    {
        return new PivotPartyDto(
            name: Text(map, "Name"),
            siren: TextOrNull(map, "Siren"),
            siret: TextOrNull(map, "Siret"),
            vatNumber: TextOrNull(map, "VatNumber"),
            address: ObjectOrNull(map, "Address") is { } address ? BuildAddress(address) : null,
            email: TextOrNull(map, "Email"),
            isCompanyHint: Boolean(map, "IsCompanyHint"));
    }

    private static PivotAddressDto BuildAddress(IDictionary<string, object?> map)
    {
        return new PivotAddressDto(
            line1: TextOrNull(map, "Line1"),
            line2: TextOrNull(map, "Line2"),
            postalCode: TextOrNull(map, "PostalCode"),
            city: TextOrNull(map, "City"),
            countryCode: TextOrNull(map, "CountryCode"));
    }

    private static PivotTotalsDto BuildTotals(IDictionary<string, object?> map)
    {
        return new PivotTotalsDto(
            totalNet: Number(map, "TotalNet"),
            totalTax: Number(map, "TotalTax"),
            totalGross: Number(map, "TotalGross"),
            sourceTotalGross: DecimalOrNull(map, "SourceTotalGross"));
    }

    private static PivotLineDto BuildLine(IDictionary<string, object?> map)
    {
        return new PivotLineDto(
            description: Text(map, "Description"),
            netAmount: Number(map, "NetAmount"),
            quantity: Number(map, "Quantity"),
            unitPriceNet: DecimalOrNull(map, "UnitPriceNet"),
            sourceRegimeCodes: TextList(map, "SourceRegimeCodes"),
            taxes: BuildList(map, "Taxes", BuildLineTax),
            sourceLineRef: TextOrNull(map, "SourceLineRef"),
            sourceData: TextOrNull(map, "SourceData"),
            unitCode: TextOrNull(map, "UnitCode"));
    }

    private static PivotLineTaxDto BuildLineTax(IDictionary<string, object?> map)
    {
        VatCategory? category = map.TryGetValue("CategoryCode", out var raw)
            ? (VatCategory)Enum.Parse(typeof(VatCategory), (string)raw!)
            : (VatCategory?)null;

        return new PivotLineTaxDto(
            taxAmount: Number(map, "TaxAmount"),
            rate: DecimalOrNull(map, "Rate"),
            categoryCode: category,
            vatexCode: TextOrNull(map, "VatexCode"));
    }

    private static PivotDocumentRefDto BuildReference(IDictionary<string, object?> map)
    {
        return new PivotDocumentRefDto(
            number: Text(map, "Number"),
            issueDate: Date(map, "IssueDate"),
            sourceReference: TextOrNull(map, "SourceReference"));
    }

    private static PivotPaymentDto BuildPayment(IDictionary<string, object?> map)
    {
        return new PivotPaymentDto(
            paymentDate: Date(map, "PaymentDate"),
            amount: Number(map, "Amount"),
            method: TextOrNull(map, "Method"),
            relatedDocumentNumber: TextOrNull(map, "RelatedDocumentNumber"),
            sourceReference: TextOrNull(map, "SourceReference"));
    }

    private static PivotDocumentChargeDto BuildCharge(IDictionary<string, object?> map)
    {
        return new PivotDocumentChargeDto(
            isCharge: Boolean(map, "IsCharge"),
            amount: Number(map, "Amount"),
            reason: TextOrNull(map, "Reason"),
            reasonCode: TextOrNull(map, "ReasonCode"),
            sourceRegimeCodes: TextList(map, "SourceRegimeCodes"));
    }

    private static List<T> BuildList<T>(
        IDictionary<string, object?> map,
        string key,
        Func<IDictionary<string, object?>, T> build)
    {
        var items = (List<object?>)map[key]!;
        return items.Select(item => build((IDictionary<string, object?>)item!)).ToList();
    }

    private static List<string> TextList(IDictionary<string, object?> map, string key)
    {
        var items = (List<object?>)map[key]!;
        return items.Select(item => (string)item!).ToList();
    }

    private static string Text(IDictionary<string, object?> map, string key) => (string)map[key]!;

    private static string? TextOrNull(IDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var value) ? (string?)value : null;

    private static decimal Number(IDictionary<string, object?> map, string key) => (decimal)map[key]!;

    private static decimal? DecimalOrNull(IDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var value) ? (decimal?)(decimal)value! : null;

    private static bool Boolean(IDictionary<string, object?> map, string key) => (bool)map[key]!;

    private static DateTime Date(IDictionary<string, object?> map, string key) =>
        DateTime.ParseExact((string)map[key]!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime? DateOrNull(IDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var value)
            ? DateTime.ParseExact((string)value!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    private static IDictionary<string, object?> Object(IDictionary<string, object?> map, string key) =>
        (IDictionary<string, object?>)map[key]!;

    private static IDictionary<string, object?>? ObjectOrNull(IDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var value) ? (IDictionary<string, object?>?)value : null;

    /// <summary>Parseur récursif descendant de la grammaire canonique (objets, tableaux, chaînes, décimaux, booléens, null).</summary>
    private sealed class Parser
    {
        private readonly string _text;
        private int _position;

        public Parser(string text)
        {
            _text = text;
            _position = 0;
        }

        public object? ParseValue()
        {
            SkipWhitespace();
            char c = _text[_position];
            switch (c)
            {
                case '{':
                    return ParseObject();
                case '[':
                    return ParseArray();
                case '"':
                    return ParseString();
                case 't':
                case 'f':
                    return ParseBoolean();
                case 'n':
                    _position += 4;
                    return null;
                default:
                    return ParseNumber();
            }
        }

        private OrderedJsonDictionary ParseObject()
        {
            // Objet à clés ORDONNÉES (ordre d'émission du writer) : un Dictionary ne garantit aucun
            // ordre d'énumération, ce dont dépend l'assertion d'ordre par réflexion (RDL03).
            var map = new OrderedJsonDictionary();
            _position++;
            SkipWhitespace();
            if (_text[_position] == '}')
            {
                _position++;
                return map;
            }

            while (true)
            {
                SkipWhitespace();
                string key = ParseString();
                SkipWhitespace();
                _position++;
                map[key] = ParseValue();
                SkipWhitespace();
                char separator = _text[_position++];
                if (separator == '}')
                {
                    break;
                }
            }

            return map;
        }

        private List<object?> ParseArray()
        {
            var list = new List<object?>();
            _position++;
            SkipWhitespace();
            if (_text[_position] == ']')
            {
                _position++;
                return list;
            }

            while (true)
            {
                list.Add(ParseValue());
                SkipWhitespace();
                char separator = _text[_position++];
                if (separator == ']')
                {
                    break;
                }
            }

            return list;
        }

        private string ParseString()
        {
            SkipWhitespace();
            _position++;
            var builder = new StringBuilder();
            while (true)
            {
                char c = _text[_position++];
                if (c == '"')
                {
                    break;
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                char escaped = _text[_position++];
                switch (escaped)
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        string hex = _text.Substring(_position, 4);
                        builder.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _position += 4;
                        break;
                    default:
                        throw new FormatException("Séquence d'échappement JSON inconnue : \\" + escaped);
                }
            }

            return builder.ToString();
        }

        private bool ParseBoolean()
        {
            if (_text[_position] == 't')
            {
                _position += 4;
                return true;
            }

            _position += 5;
            return false;
        }

        private decimal ParseNumber()
        {
            int start = _position;
            while (_position < _text.Length)
            {
                char c = _text[_position];
                bool isNumeric = c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E' || (c >= '0' && c <= '9');
                if (!isNumeric)
                {
                    break;
                }

                _position++;
            }

            string token = _text.Substring(start, _position - start);
            return decimal.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length)
            {
                char c = _text[_position];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                {
                    break;
                }

                _position++;
            }
        }
    }

    /// <summary>
    /// Objet JSON qui PRÉSERVE l'ORDRE D'INSERTION des clés (l'ordre d'émission du writer canonique).
    /// <see cref="Dictionary{TKey,TValue}"/> n'offre aucune garantie d'ordre d'énumération ; l'assertion
    /// d'ordre par réflexion (RDL03 : ordre d'émission == ordre de déclaration du DTO, ADR-0007 règle 1)
    /// a besoin de relire <see cref="Keys"/> dans l'ordre réel du document. Compilé des deux côtés
    /// (net48 + .NET 10) comme le reste du lecteur.
    /// </summary>
    private sealed class OrderedJsonDictionary : IDictionary<string, object?>
    {
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, object?> _values = new Dictionary<string, object?>(StringComparer.Ordinal);

        public ICollection<string> Keys => _order;

        public ICollection<object?> Values => _order.Select(key => _values[key]).ToList();

        public int Count => _order.Count;

        public bool IsReadOnly => false;

        public object? this[string key]
        {
            get => _values[key];
            set
            {
                if (!_values.ContainsKey(key))
                {
                    _order.Add(key);
                }

                _values[key] = value;
            }
        }

        public void Add(string key, object? value)
        {
            _values.Add(key, value);
            _order.Add(key);
        }

        public void Add(KeyValuePair<string, object?> item) => Add(item.Key, item.Value);

        public void Clear()
        {
            _order.Clear();
            _values.Clear();
        }

        public bool Contains(KeyValuePair<string, object?> item) => _values.ContainsKey(item.Key);

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
        {
            foreach (string key in _order)
            {
                array[arrayIndex++] = new KeyValuePair<string, object?>(key, _values[key]);
            }
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            foreach (string key in _order)
            {
                yield return new KeyValuePair<string, object?>(key, _values[key]);
            }
        }

        public bool Remove(string key)
        {
            if (!_values.Remove(key))
            {
                return false;
            }

            _order.Remove(key);
            return true;
        }

        public bool Remove(KeyValuePair<string, object?> item) => Remove(item.Key);

        public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
