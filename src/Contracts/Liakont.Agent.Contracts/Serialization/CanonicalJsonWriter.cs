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
/// <item>Chaînes : sortie ASCII pur ; tout caractère &lt; 0x20 ou &gt; 0x7E est échappé en
/// <c>\uXXXX</c> (hexadécimal minuscule) ; <c>"</c> et <c>\</c> sont échappés.</item>
/// <item>Dates : format unique <c>yyyy-MM-dd</c> en culture invariante (les horodatages UTC
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

    /// <summary>Écrit une valeur chaîne, échappée en ASCII pur.</summary>
    /// <param name="value">La chaîne à écrire (non nulle).</param>
    public void WriteString(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        AppendEscapedString(value);
    }

    /// <summary>Écrit un montant <see cref="decimal"/> (invariant, échelle préservée, sans exposant).</summary>
    /// <param name="value">Le montant.</param>
    public void WriteDecimal(decimal value) =>
        _builder.Append(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Écrit un booléen (<c>true</c>/<c>false</c>).</summary>
    /// <param name="value">La valeur.</param>
    public void WriteBoolean(bool value) => _builder.Append(value ? "true" : "false");

    /// <summary>Écrit une date au format canonique <c>yyyy-MM-dd</c> (composante horaire ignorée).</summary>
    /// <param name="value">La date.</param>
    public void WriteDate(DateTime value) =>
        AppendEscapedString(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>Restitue le JSON canonique accumulé.</summary>
    /// <returns>Le document JSON canonique.</returns>
    public override string ToString() => _builder.ToString();

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
