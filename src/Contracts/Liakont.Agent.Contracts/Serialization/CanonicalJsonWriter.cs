namespace Liakont.Agent.Contracts.Serialization;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Writer JSON canonique manuel, partagé par l'agent (net48) et la plateforme (.NET 10) — PIV02.
/// C'est l'UNIQUE sérialiseur du contrat : un seul code, compilé des deux côtés, produit une sortie
/// IDENTIQUE octet par octet (jamais deux sérialiseurs « configurés pareil » qui divergeraient —
/// voir <c>ADR-0007</c>). Il porte UNIQUEMENT les règles de format figées, aucune logique métier :
/// la frontière « DTOs purs » (PIV01) interdit la logique MÉTIER, pas un utilitaire de sérialisation
/// du contrat (même raison d'être que <see cref="PivotRounding"/>).
///
/// Règles de format figées (testées des deux côtés, documentées dans <c>ADR-0007</c>) :
/// <list type="bullet">
/// <item>Decimals : <see cref="decimal"/> en culture invariante, point décimal, l'échelle de la
/// source est PRÉSERVÉE (10,00 → « 10.00 » ; 1234,5 → « 1234.5 »), jamais de notation
/// exponentielle (garanti par le type <see cref="decimal"/>).</item>
/// <item>Chaînes (valeurs de texte LIBRE) : normalisées en <b>Unicode NFC</b> puis sortie ASCII
/// pur ; tout caractère &lt; 0x20 ou &gt; 0x7E est échappé en <c>\uXXXX</c> (hexadécimal minuscule) ;
/// <c>"</c> et <c>\</c> sont échappés. La normalisation NFC (équivalence canonique : « café »
/// précomposé U+00E9 ≡ décomposé U+0065 U+0301) garantit qu'une source renvoyant tantôt NFC tantôt
/// NFD produit la MÊME empreinte (ADR-0007 règle 7).</item>
/// <item>Dates : format unique <c>yyyy-MM-dd</c> en culture invariante, composantes calendaires
/// (Year/Month/Day) seules — le <c>DateTimeKind</c> et le fuseau sont ignorés (les horodatages UTC
/// éventuels — enveloppes de transport, hors périmètre PIV02 — utiliseront
/// <c>yyyy-MM-ddTHH:mm:ssZ</c>).</item>
/// <item>Pas d'espace ni de saut de ligne hors des chaînes : la sortie est compacte et
/// déterministe.</item>
/// </list>
/// </summary>
public sealed class CanonicalJsonWriter
{
    private readonly StringBuilder _builder = new StringBuilder();

    // Pour chaque conteneur ouvert (objet ou tableau), indique s'il est encore vide : permet
    // d'insérer une virgule de séparation avant le 2e membre/élément et les suivants.
    private readonly Stack<bool> _containerIsEmpty = new Stack<bool>();

    /// <summary>Ouvre un objet JSON (<c>{</c>).</summary>
    public void BeginObject()
    {
        _builder.Append('{');
        _containerIsEmpty.Push(true);
    }

    /// <summary>Ferme l'objet JSON courant (<c>}</c>).</summary>
    public void EndObject()
    {
        _builder.Append('}');
        _containerIsEmpty.Pop();
    }

    /// <summary>Ouvre un tableau JSON (<c>[</c>).</summary>
    public void BeginArray()
    {
        _builder.Append('[');
        _containerIsEmpty.Push(true);
    }

    /// <summary>Ferme le tableau JSON courant (<c>]</c>).</summary>
    public void EndArray()
    {
        _builder.Append(']');
        _containerIsEmpty.Pop();
    }

    /// <summary>
    /// Écrit le nom d'un membre d'objet (<c>"nom":</c>), précédé d'une virgule si ce n'est pas le
    /// premier membre. L'appelant écrit ensuite la valeur via une méthode <c>WriteXxx</c>.
    /// </summary>
    /// <param name="name">Nom du membre (échappé comme une chaîne).</param>
    public void WritePropertyName(string name)
    {
        Separate();
        AppendEscapedString(name);
        _builder.Append(':');
    }

    /// <summary>
    /// Marque le début d'un élément de tableau (insère une virgule si ce n'est pas le premier).
    /// L'appelant écrit ensuite la valeur de l'élément.
    /// </summary>
    public void BeginArrayElement() => Separate();

    /// <summary>Écrit une valeur de texte LIBRE, normalisée en Unicode NFC (si possible) puis échappée en ASCII pur.</summary>
    /// <param name="value">La chaîne à écrire (non nulle).</param>
    public void WriteString(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        AppendEscapedString(NormalizeToNfc(value));
    }

    /// <summary>Écrit un montant <see cref="decimal"/> (invariant, échelle préservée, sans exposant).</summary>
    /// <param name="value">Le montant.</param>
    public void WriteDecimal(decimal value) =>
        _builder.Append(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Écrit un booléen (<c>true</c>/<c>false</c>).</summary>
    /// <param name="value">La valeur.</param>
    public void WriteBoolean(bool value) => _builder.Append(value ? "true" : "false");

    /// <summary>
    /// Écrit une date au format canonique <c>yyyy-MM-dd</c> (culture invariante).
    /// INVARIANT « date calendaire » (ADR-0007 règle 6) : seules les composantes Year/Month/Day sont
    /// émises ; le <see cref="DateTimeKind"/> (Utc/Local/Unspecified), l'heure et le fuseau sont IGNORÉS —
    /// aucune conversion de fuseau n'est appliquée, donc deux <see cref="DateTime"/> de même date
    /// calendaire mais de Kind différent produisent le MÊME octet (vérifié par test). Un adaptateur ne
    /// doit jamais dériver une date du pivot d'un <c>DateTimeOffset.ToLocalTime()</c> qui décalerait la
    /// composante calendaire près de minuit (responsabilité de déterminisme de la source, ADR-0007 §traçabilité).
    /// </summary>
    /// <param name="value">La date (sa composante calendaire seule est retenue).</param>
    public void WriteDate(DateTime value) =>
        AppendEscapedString(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>
    /// Écrit un HORODATAGE UTC au format canonique <c>yyyy-MM-ddTHH:mm:ssZ</c> (culture invariante,
    /// précision seconde), format anticipé par <c>ADR-0007</c> pour les horodatages (« hors périmètre
    /// PIV02 : yyyy-MM-ddTHH:mm:ssZ »). Comme <see cref="WriteDate"/>, les composantes calendaires et
    /// horaires (Year..Second) sont émises VERBATIM et le <see cref="DateTimeKind"/> est IGNORÉ (aucune
    /// conversion de fuseau : deux <see cref="DateTime"/> de mêmes composantes mais de Kind différent
    /// produisent le MÊME octet) ; le suffixe <c>Z</c> est un littéral déclarant la convention UTC du
    /// contrat, PAS une conversion. La source est responsable de fournir un instant UTC déterministe
    /// (ADR-0007 §traçabilité). Les sous-secondes sont TRONQUÉES (précision seconde figée du contrat) —
    /// deux instants de même seconde sont indiscernables, comme deux dates de même jour pour
    /// <see cref="WriteDate"/>. Le premier consommateur est le canal GED (<c>GedCanonicalJson</c>,
    /// F19 §4.2) ; le pivot fiscal ne l'utilise pas (son empreinte figée reste inchangée).
    /// </summary>
    /// <param name="value">L'horodatage (ses composantes date+heure à la seconde sont retenues).</param>
    public void WriteDateTimeUtc(DateTime value) =>
        AppendEscapedString(value.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture));

    /// <summary>
    /// Écrit une énumération par son NOM (règle 4 d'<c>ADR-0007</c> : « émis par leur NOM »),
    /// GARDÉE par <see cref="Enum.IsDefined(Type, object)"/> : une valeur hors plage LÈVE au lieu
    /// d'émettre le NOMBRE muet que produirait <c>ToString()</c> (les enums du contrat commencent à 1 ;
    /// <c>((OperationCategory)99).ToString()</c> donne « 99 »). L'exception fait REJETER le document, dans
    /// les deux sens du contrat : côté PLATEFORME l'enveloppe try/catch de l'ingestion la convertit en rejet
    /// par document ; côté AGENT le cycle d'extraction met le document en quarantaine (non transmis,
    /// journalisé pour l'opérateur) sans bloquer la fenêtre. Dans les deux cas, jamais un chiffre hashé puis
    /// archivé (WORM) — « bloquer plutôt qu'envoyer faux », CLAUDE.md n°3. Garde GÉNÉRIQUE, donc impossible
    /// à oublier sur un futur champ enum (contrairement à un <c>ToString()</c> écrit à la main site par site).
    /// </summary>
    /// <typeparam name="T">Le type d'énumération du contrat.</typeparam>
    /// <param name="value">La valeur (doit être définie dans <typeparamref name="T"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">La valeur n'est pas définie dans <typeparamref name="T"/>.</exception>
    public void WriteEnum<T>(T value)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Valeur d'énumération non définie pour " + typeof(T).Name
                    + " : sérialisation canonique refusée (ADR-0007 règle 4 — émise par son nom, jamais un nombre).");
        }

        AppendEscapedString(value.ToString());
    }

    /// <summary>Restitue le JSON canonique accumulé.</summary>
    /// <returns>Le document JSON canonique.</returns>
    public override string ToString() => _builder.ToString();

    // Normalisation Unicode NFC (ADR-0007 règle 7). Une valeur de texte LIBRE issue de la source (raison
    // sociale, libellé, SourceData…) peut arriver en NFC ou NFD selon le pilote ODBC ; « café » précomposé
    // (U+00E9) et décomposé (U+0065 U+0301) sont la MÊME chaîne abstraite (équivalence canonique Unicode)
    // mais produiraient deux empreintes — anti-doublon PIV04 rompu. On canonicalise la FORME d'encodage ici,
    // au même titre que l'échappement ASCII : c'est une canonicalisation d'ENCODAGE de chaîne, distincte du
    // déterminisme de CONTENU/structure (ordre des champs, absence d'horodatage) qui reste la responsabilité
    // de l'adaptateur (ADR-0007 §traçabilité). La forme NFC est STABLE entre net48 (NLS) et .NET 10 (ICU) —
    // Unicode Normalization Stability Policy : la décomposition canonique d'un caractère ASSIGNÉ ne change
    // jamais — donc l'empreinte reste identique des deux côtés (prouvé par les golden cross-runtime).
    private static string NormalizeToNfc(string value)
    {
        try
        {
            return value.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // Unicode INVALIDE (typiquement un surrogate UTF-16 ISOLÉ issu d'un nvarchar tronqué côté ODBC) :
            // String.Normalize lève. Une chaîne mal formée n'a PAS de forme NFC définie — on PRÉSERVE alors le
            // comportement antérieur à RDL05 (échappement code-unité par code-unité, déterministe et identique
            // des deux côtés via AppendEscapedString), plutôt que d'introduire un nouveau chemin de rejet hors
            // périmètre. Le contenu mal formé reste hashé EXACTEMENT comme avant ce correctif (aucune régression).
            return value;
        }
    }

    private void Separate()
    {
        bool isEmpty = _containerIsEmpty.Pop();
        if (!isEmpty)
        {
            _builder.Append(',');
        }

        _containerIsEmpty.Push(false);
    }

    private void AppendEscapedString(string value)
    {
        _builder.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    _builder.Append("\\\"");
                    break;
                case '\\':
                    _builder.Append("\\\\");
                    break;
                case '\b':
                    _builder.Append("\\b");
                    break;
                case '\f':
                    _builder.Append("\\f");
                    break;
                case '\n':
                    _builder.Append("\\n");
                    break;
                case '\r':
                    _builder.Append("\\r");
                    break;
                case '\t':
                    _builder.Append("\\t");
                    break;
                default:
                    if (c < ' ' || c > '~')
                    {
                        // Hors ASCII imprimable (< 0x20 ou > 0x7E) : échappement \uXXXX minuscule.
                        // Les paires de substitution UTF-16 sont émises code-unité par code-unité,
                        // ce qui reste déterministe et identique des deux côtés.
                        _builder.Append("\\u");
                        _builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        _builder.Append(c);
                    }

                    break;
            }
        }

        _builder.Append('"');
    }
}
