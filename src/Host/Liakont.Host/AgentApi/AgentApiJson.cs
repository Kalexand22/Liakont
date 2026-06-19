namespace Liakont.Host.AgentApi;

using System.Text.Json;
using System.Text.Json.Serialization;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;

/// <summary>
/// Liaison JSON du contrat agent → plateforme. Le contrat émet ses énumérations par leur NOM
/// (catégorie UNCL5305 <c>"S"</c>, <see cref="OperationCategory"/> <c>"Mixte"</c>, statut d'ingestion
/// <c>"Accepted"</c> — cf. <c>CanonicalJson</c>, les fixtures <c>tests/fixtures/contrat-v1/</c> et
/// <c>docs/architecture/contrat-agent-v1.md</c> §3), jamais par leur valeur numérique. Or System.Text.Json
/// attend par défaut un NOMBRE pour un enum : sans ces convertisseurs, un lot au format documenté est
/// rejeté en 400 au model-binding (REQUÊTE), et le statut de réponse partirait en nombre (RÉPONSE),
/// rompant le contrat dans les deux sens.
/// <para>On enregistre un convertisseur PAR énumération du contrat (et non un
/// <see cref="JsonStringEnumConverter"/> global) pour ne pas changer le format des autres enums des
/// endpoints console — la liaison agent et la liaison console partagent les mêmes
/// <c>Http.Json.JsonOptions</c>. Les trois enums du contrat (deux en requête, un en réponse) sont
/// couverts. Les deux convertisseurs de REQUÊTE (modèle pivot) posent <c>allowIntegerValues:false</c>
/// (RDL01) : un entier hors plage est rejeté au model-binding, jamais accepté comme valeur d'enum non
/// définie (qui finirait hashée/archivée en nombre muet) ; le convertisseur de RÉPONSE
/// (<see cref="DocumentPushStatus"/>) garde le défaut (la plateforme ne produit que des valeurs définies).</para>
/// <para>Le correctif vit ici, côté Host, et non sur les types du contrat : l'assembly de contrat est
/// <c>netstandard2.0</c> « zéro PackageReference » (BCL seul, pureté vérifiée par test) — y poser un
/// <c>[JsonConverter]</c> tirerait une dépendance System.Text.Json interdite.</para>
/// </summary>
internal static class AgentApiJson
{
    /// <summary>Ajoute, aux options fournies, les convertisseurs string↔enum des énumérations du contrat.</summary>
    public static void ConfigureContractEnums(JsonSerializerOptions options)
    {
        // Requête (modèle pivot) : binding STRICT (allowIntegerValues:false) — un entier hors plage
        // (p. ex. {"OperationCategory":99}) est REJETÉ au model-binding (400) au lieu d'entrer comme
        // une valeur d'enum non définie qui serait ensuite hashée/archivée en nombre muet (RDL01,
        // « bloquer plutôt qu'envoyer faux », CLAUDE.md n°3 — symétrique de la garde WriteEnum du writer).
        options.Converters.Add(new JsonStringEnumConverter<OperationCategory>(namingPolicy: null, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<VatCategory>(namingPolicy: null, allowIntegerValues: false));

        // Réponse (statut d'ingestion par document) : émis par nom, symétrique de la requête
        // (contrat-agent-v1.md §3 — Status "Accepted"/"Duplicate"/"Rejected").
        options.Converters.Add(new JsonStringEnumConverter<DocumentPushStatus>());
    }
}
