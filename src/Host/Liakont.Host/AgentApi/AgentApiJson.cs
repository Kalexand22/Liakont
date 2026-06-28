namespace Liakont.Host.AgentApi;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Liakont.Agent.Contracts;
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
/// <c>Http.Json.JsonOptions</c>. Les quatre enums du contrat (trois en requête, un en réponse) sont
/// couverts. Les trois convertisseurs de REQUÊTE (modèle pivot) posent <c>allowIntegerValues:false</c>
/// (RDL01) : un entier hors plage est rejeté au model-binding, jamais accepté comme valeur d'enum non
/// définie (qui finirait hashée/archivée en nombre muet) ; le convertisseur de RÉPONSE
/// (<see cref="DocumentPushStatus"/>) garde le défaut (la plateforme ne produit que des valeurs définies).</para>
/// <para>RDL04 — <b>liaison STRICTE des membres inconnus</b> sur les DTOs du contrat. La plateforme ne
/// hashe pas les octets reçus : elle RE-SÉRIALISE le DTO STJ-désérialisé (IngestDocumentBatchHandler).
/// Or STJ DROPPE silencieusement un membre inconnu par défaut : un agent N+1 (déploiement non atomique)
/// portant un champ post-v1 dans un payload déclaré « 1 » verrait ce champ ignoré → empreinte plateforme
/// ≠ empreinte agent → anti-doublon (PIV04) cassé / fausse altération (TRK03). Défaut SÛR : le lecteur
/// REJETTE tout membre inconnu (<see cref="JsonUnmappedMemberHandling.Disallow"/>) — un payload v1
/// portant un membre post-v1 est REFUSÉ (400), pas droppé, ce qui préserve l'intégrité du hash
/// (« bloquer plutôt qu'envoyer faux », CLAUDE.md n°3). La garde est posée par RÉFLEXION sur l'assembly
/// de contrat (et non type par type) — impossible à oublier sur un futur DTO. Elle ne touche QUE les
/// types <c>Liakont.Agent.Contracts</c> (les endpoints console, d'un autre assembly, gardent le défaut
/// permissif). Sans incidence sur la SÉRIALISATION (réponses) : <c>UnmappedMemberHandling</c> ne joue
/// qu'en désérialisation.</para>
/// <para>Le correctif vit ici, côté Host, et non sur les types du contrat : l'assembly de contrat est
/// <c>netstandard2.0</c> « zéro PackageReference » (BCL seul, pureté vérifiée par test) — y poser un
/// <c>[JsonConverter]</c> ou un <c>[JsonUnmappedMemberHandling]</c> tirerait une dépendance
/// System.Text.Json interdite.</para>
/// </summary>
internal static class AgentApiJson
{
    /// <summary>Assembly des DTOs du contrat agent (cible de la liaison stricte des membres inconnus).</summary>
    private static readonly Assembly ContractAssembly = typeof(AgentContractVersion).Assembly;

    /// <summary>
    /// Configure, sur les options fournies, la liaison JSON du contrat agent : convertisseurs
    /// string↔enum des énumérations ET rejet strict des membres inconnus sur les DTOs du contrat.
    /// </summary>
    public static void ConfigureContractBinding(JsonSerializerOptions options)
    {
        // Requête (modèle pivot) : binding STRICT (allowIntegerValues:false) — un entier hors plage
        // (p. ex. {"OperationCategory":99}) est REJETÉ au model-binding (400) au lieu d'entrer comme
        // une valeur d'enum non définie qui serait ensuite hashée/archivée en nombre muet (RDL01,
        // « bloquer plutôt qu'envoyer faux », CLAUDE.md n°3 — symétrique de la garde WriteEnum du writer).
        options.Converters.Add(new JsonStringEnumConverter<OperationCategory>(namingPolicy: null, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<VatCategory>(namingPolicy: null, allowIntegerValues: false));

        // Rôle de ligne (F03 §2.3 amendement, BUG-17 volet b) : enum de REQUÊTE du modèle pivot, émis par son NOM
        // (« BuyerFee ») par le writer canonique — sans ce convertisseur un push agent portant une ligne d'honoraire
        // acheteur serait rejeté en 400. Binding STRICT (allowIntegerValues:false, RDL01) comme les autres enums de requête.
        options.Converters.Add(new JsonStringEnumConverter<PivotLineRole>(namingPolicy: null, allowIntegerValues: false));

        // Réponse (statut d'ingestion par document) : émis par nom, symétrique de la requête
        // (contrat-agent-v1.md §3 — Status "Accepted"/"Duplicate"/"Rejected").
        options.Converters.Add(new JsonStringEnumConverter<DocumentPushStatus>());

        // RDL04 — rejet strict des membres inconnus sur les DTOs du contrat (cf. doc de classe).
        options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
            .WithAddedModifier(RejectUnknownContractMembers);
    }

    // Pose JsonUnmappedMemberHandling.Disallow sur chaque type-objet de l'assembly de contrat :
    // un membre JSON sans propriété/paramètre de constructeur correspondant lève à la désérialisation
    // (→ 400 à la frontière HTTP), au lieu d'être silencieusement ignoré.
    private static void RejectUnknownContractMembers(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind == JsonTypeInfoKind.Object && typeInfo.Type.Assembly == ContractAssembly)
        {
            typeInfo.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        }
    }
}
