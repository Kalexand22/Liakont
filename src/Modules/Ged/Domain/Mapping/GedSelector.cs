namespace Liakont.Modules.Ged.Domain.Mapping;

using System;
using System.Collections.Generic;
using System.Text;
using Liakont.Agent.Contracts.Ged;

/// <summary>
/// Sélecteur <b>JSONPath restreint</b> sur un <see cref="IngestedDocumentDto"/> BRUT (F19 §4.5, décision « pas
/// un moteur d'expression »). Grammaire volontairement pauvre — <b>chemins simples + filtre d'égalité</b>, aucun
/// calcul :
/// <list type="bullet">
/// <item><description><c>$</c> : racine (le document ingéré).</description></item>
/// <item><description><c>.nom</c> : accès à une propriété au nom <c>[A-Za-z0-9_]</c> (<c>documentType</c>,
/// <c>sourceReference</c>, <c>fields</c>, <c>axes</c>, <c>entities</c>, <c>relations</c>, ou une clé simple de
/// <c>fields</c>).</description></item>
/// <item><description><c>['clé arbitraire']</c> : accès à une propriété dont la clé porte des espaces, tirets,
/// points ou accents (les clés de <c>SourceFields</c> sont BRUTES et non contraintes, ex. <c>Réf facture</c>) ;
/// une apostrophe littérale s'écrit doublée (<c>''</c>).</description></item>
/// <item><description><c>[?champ=='valeur']</c> : filtre d'un tableau sur l'égalité d'un champ scalaire ; la
/// valeur suit les mêmes règles d'apostrophe (<c>''</c> = apostrophe littérale, ex. <c>l''établissement</c>).</description></item>
/// <item><description><c>[*]</c> : jokers, tous les éléments d'un tableau.</description></item>
/// </list>
/// Le document est projeté sur un modèle générique minimal (scalaire = <see cref="string"/>, objet =
/// dictionnaire, tableau = liste) puis parcouru. La résolution est <b>pure</b> (aucun accès base) : elle rend
/// la LISTE des valeurs scalaires atteintes (0..n) — c'est l'appelant (<see cref="GedMapper"/>) qui décide de
/// DÉFÉRER selon la cardinalité (obligatoire/mono-valeur), jamais deviner.
/// </summary>
public static class GedSelector
{
    private enum StepKind
    {
        Property,
        Filter,
        Wildcard,
    }

    /// <summary>
    /// Valide la SYNTAXE d'un sélecteur (utilisé à la construction d'un profil). Ne vérifie PAS que le chemin
    /// existe dans un document donné (une propriété absente rend simplement 0 valeur au mapping).
    /// </summary>
    /// <param name="selector">Le sélecteur brut.</param>
    /// <exception cref="InvalidGedSelectorException">Le sélecteur est syntaxiquement invalide.</exception>
    public static void Validate(string selector) => Parse(selector);

    /// <summary>
    /// Évalue le sélecteur sur un document ingéré et rend la liste des valeurs scalaires atteintes (ordre du
    /// document, doublons conservés). Rend une liste vide si le chemin ne résout rien.
    /// </summary>
    /// <param name="selector">Le sélecteur brut (doit être syntaxiquement valide).</param>
    /// <param name="document">Le document ingéré BRUT.</param>
    /// <returns>Les valeurs scalaires sélectionnées.</returns>
    /// <exception cref="InvalidGedSelectorException">Le sélecteur est syntaxiquement invalide.</exception>
    public static IReadOnlyList<string> Evaluate(string selector, IngestedDocumentDto document)
    {
        var steps = Parse(selector);
        var model = BuildModel(document);

        var current = new List<object?> { model };
        foreach (var step in steps)
        {
            var next = new List<object?>();
            foreach (var node in current)
            {
                ApplyStep(step, node, next);
            }

            current = next;
        }

        var results = new List<string>();
        foreach (var node in current)
        {
            switch (node)
            {
                case string scalar:
                    results.Add(scalar);
                    break;
                case List<object?> list:
                    // Terminal sur un tableau (ex. « .values ») : aplatir d'un niveau vers les scalaires.
                    foreach (var element in list)
                    {
                        if (element is string s)
                        {
                            results.Add(s);
                        }
                    }

                    break;
                default:
                    // Terminal sur un objet : aucune valeur scalaire (sélecteur mal ciblé — 0 résultat).
                    break;
            }
        }

        return results;
    }

    private static void ApplyStep((StepKind Kind, string A, string B) step, object? node, List<object?> output)
    {
        switch (step.Kind)
        {
            case StepKind.Property:
                if (node is Dictionary<string, object?> obj
                    && obj.TryGetValue(step.A, out var value)
                    && value is not null)
                {
                    output.Add(value);
                }

                break;

            case StepKind.Filter:
                if (node is List<object?> filtered)
                {
                    foreach (var element in filtered)
                    {
                        if (element is Dictionary<string, object?> elementObj
                            && elementObj.TryGetValue(step.A, out var fieldValue)
                            && fieldValue is string s
                            && string.Equals(s, step.B, StringComparison.Ordinal))
                        {
                            output.Add(element);
                        }
                    }
                }

                break;

            case StepKind.Wildcard:
                if (node is List<object?> all)
                {
                    output.AddRange(all);
                }

                break;

            default:
                break;
        }
    }

    // Projette le DTO sur un modèle générique (dictionnaires / listes / scalaires string) parcourable par les
    // étapes. Les propriétés absentes/nulles ne sont pas ajoutées (symétrie « champ absent = null », règle 9).
    private static Dictionary<string, object?> BuildModel(IngestedDocumentDto document)
    {
        var fields = new Dictionary<string, object?>();
        foreach (var pair in document.SourceFields)
        {
            fields[pair.Key] = pair.Value;
        }

        var axes = new List<object?>();
        foreach (var axis in document.SourceAxes)
        {
            var values = new List<object?>();
            foreach (var value in axis.Values)
            {
                values.Add(value);
            }

            axes.Add(new Dictionary<string, object?> { ["name"] = axis.Name, ["values"] = values });
        }

        var entities = new List<object?>();
        foreach (var entity in document.SourceEntities)
        {
            entities.Add(new Dictionary<string, object?>
            {
                ["type"] = entity.Type,
                ["externalId"] = entity.ExternalId,
                ["display"] = entity.Display,
            });
        }

        var relations = new List<object?>();
        foreach (var relation in document.SourceRelations)
        {
            relations.Add(new Dictionary<string, object?>
            {
                ["type"] = relation.Type,
                ["targetExternalId"] = relation.TargetExternalId,
                ["targetType"] = relation.TargetType,
            });
        }

        return new Dictionary<string, object?>
        {
            ["documentType"] = document.DocumentType,
            ["sourceReference"] = document.SourceReference,
            ["fields"] = fields,
            ["axes"] = axes,
            ["entities"] = entities,
            ["relations"] = relations,
        };
    }

    private static List<(StepKind Kind, string A, string B)> Parse(string selector)
    {
        if (string.IsNullOrEmpty(selector))
        {
            throw new InvalidGedSelectorException(selector ?? string.Empty, "le sélecteur est vide.");
        }

        if (selector[0] != '$')
        {
            throw new InvalidGedSelectorException(selector, "un sélecteur doit commencer par « $ ».");
        }

        var steps = new List<(StepKind, string, string)>();
        var index = 1;
        while (index < selector.Length)
        {
            var current = selector[index];
            if (current == '.')
            {
                index++;
                var name = ReadIdentifier(selector, ref index);
                if (name.Length == 0)
                {
                    throw new InvalidGedSelectorException(selector, "nom de propriété vide après « . ».");
                }

                steps.Add((StepKind.Property, name, string.Empty));
            }
            else if (current == '[')
            {
                steps.Add(ParseBracket(selector, ref index));
            }
            else
            {
                throw new InvalidGedSelectorException(selector, $"caractère inattendu « {current} » à la position {index}.");
            }
        }

        return steps;
    }

    private static (StepKind Kind, string A, string B) ParseBracket(string selector, ref int index)
    {
        // Précondition : selector[index] == '['.
        index++;
        if (index >= selector.Length)
        {
            throw new InvalidGedSelectorException(selector, "« [ » non fermé.");
        }

        var current = selector[index];

        if (current == '*')
        {
            index++;
            Expect(selector, ref index, ']');
            return (StepKind.Wildcard, string.Empty, string.Empty);
        }

        if (current == '\'')
        {
            // Clé de propriété ARBITRAIRE entre crochets : $.fields['Réf facture'] (espaces, tirets, accents…),
            // pour cibler une clé de SourceFields non exprimable par « .ident ». Aucune interprétation, juste un
            // accès par nom exact.
            var key = ReadQuotedLiteral(selector, ref index);
            Expect(selector, ref index, ']');
            return (StepKind.Property, key, string.Empty);
        }

        if (current == '?')
        {
            index++;
            var field = ReadIdentifier(selector, ref index);
            if (field.Length == 0)
            {
                throw new InvalidGedSelectorException(selector, "nom de champ vide dans un filtre.");
            }

            Expect(selector, ref index, '=');
            Expect(selector, ref index, '=');
            var value = ReadQuotedLiteral(selector, ref index);
            Expect(selector, ref index, ']');
            return (StepKind.Filter, field, value);
        }

        throw new InvalidGedSelectorException(selector, "contenu de crochet invalide (attendu « [*] », « ['clé'] » ou « [?champ=='valeur'] »).");
    }

    // Lit un littéral entre apostrophes ; « '' » représente une apostrophe LITTÉRALE (échappement) — de sorte
    // qu'une clé/valeur source portant une apostrophe (ex. « l'établissement ») reste ciblable. Précondition :
    // selector[index] == '\''.
    private static string ReadQuotedLiteral(string selector, ref int index)
    {
        if (index >= selector.Length || selector[index] != '\'')
        {
            throw new InvalidGedSelectorException(selector, $"apostrophe ouvrante attendue à la position {index}.");
        }

        index++;
        var builder = new StringBuilder();
        while (index < selector.Length)
        {
            var c = selector[index];
            if (c == '\'')
            {
                if (index + 1 < selector.Length && selector[index + 1] == '\'')
                {
                    builder.Append('\'');
                    index += 2;
                    continue;
                }

                index++;
                return builder.ToString();
            }

            builder.Append(c);
            index++;
        }

        throw new InvalidGedSelectorException(selector, "littéral entre apostrophes non terminé.");
    }

    private static string ReadIdentifier(string selector, ref int index)
    {
        var start = index;
        while (index < selector.Length)
        {
            var c = selector[index];
            var isIdentChar = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
            if (!isIdentChar)
            {
                break;
            }

            index++;
        }

        return selector.Substring(start, index - start);
    }

    private static void Expect(string selector, ref int index, char expected)
    {
        if (index >= selector.Length || selector[index] != expected)
        {
            throw new InvalidGedSelectorException(selector, $"« {expected} » attendu à la position {index}.");
        }

        index++;
    }
}
