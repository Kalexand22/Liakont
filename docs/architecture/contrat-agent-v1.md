# Contrat agent → plateforme, version 1

> **Objet (PIV03).** Documenter le contrat d'ingestion **v1** entre l'agent (net48, chez le client)
> et la plateforme (.NET 10) : ses endpoints, ses DTOs, et ses **règles d'évolution**. Le contrat
> est l'assemblage `Liakont.Agent.Contracts` (netstandard2.0, zéro dépendance) partagé par les deux
> runtimes. Les **golden files** (`tests/fixtures/contrat-v1/`) en sont la référence de
> compatibilité, exécutée des deux côtés à chaque commit.
>
> **Sources (rien n'est inventé — CLAUDE.md n°2).**
> - `docs/conception/F12-Architecture-Plateforme-Agent.md` §3 (endpoints, principes, erreurs).
> - `docs/conception/F01-F02-Modele-Pivot-Contrat-Extraction.md` (structure du pivot).
> - `docs/adr/ADR-0007-serialisation-canonique-pivot.md` (règles de sérialisation canonique figées, PIV02).
> - `docs/architecture/mapping-pivot-en16931.md` (couverture EN 16931 / Flux 10.x du pivot, PIV02).

---

## 1. Principes (F12 §3.1)

| Principe | Mise en œuvre |
|---|---|
| **Versionné** | Préfixe d'URL `/api/agent/v1/` + `AgentContractVersion.ContractVersion` (= `"1"`) porté par l'assembly. La plateforme supporte N et N-1. |
| **Authentifié** | Header `X-Agent-Key: <prefix>.<secret>` — la plateforme ne stocke que `prefix` + hash (PIV05). Une clé = UN agent = UN tenant. |
| **Idempotent** | Re-pousser un document déjà reçu (même `payload_hash` pour le tenant) → `duplicate`, aucun effet. |
| **Par lots** | Push par batch (max 100 documents, imposé par l'ingestion PIV04), **résultat individuel par document**, lot NON transactionnel. |
| **Sortant uniquement** | L'agent initie toutes les connexions ; la plateforme ne contacte jamais l'agent (HTTPS sortant — rien à ouvrir sur le firewall client). |

---

## 2. Endpoints (F12 §3.2)

| Méthode | Route | Rôle | Requête → Réponse |
|---|---|---|---|
| POST | `/api/agent/v1/heartbeat` | État de l'agent → config effective + version attendue | `HeartbeatRequestDto` → `HeartbeatResponseDto` |
| GET | `/api/agent/v1/configuration` | Planification, période à extraire | — → `AgentConfigurationDto` |
| POST | `/api/agent/v1/documents/batch` | Push de documents pivot | `PushBatchRequestDto` → `PushBatchResponseDto` |
| POST | `/api/agent/v1/documents/{sourceReference}/pdf` | PDF lié à un document | fichier → 200/4xx |
| POST | `/api/agent/v1/pdf-pool` | PDF non liés (réconciliation F06/TRK07) | fichier → 200/4xx |

Codes de réponse (F12 §3.3) : `200` (reçu : `accepted`/`duplicate` par élément), `400` (payload
non conforme au contrat), `401/403` (clé invalide/révoquée), `413` (lot trop gros), `426` (version
non supportée → auto-update), `429/5xx` (backoff). Les routes PDF/pdf-pool et la gestion d'agents
sont portées par les items d'ingestion (PIV04/PIV05) — ce document fige la **forme du contrat**, pas
son implémentation serveur.

---

## 3. DTOs (assemblage `Liakont.Agent.Contracts`, netstandard2.0)

### 3.1 Document pivot (le cœur — F01-F02)

| DTO | Rôle |
|---|---|
| `PivotDocumentDto` | Le document à transmettre, aligné EN 16931. Porte les montants calculés par la source ; ne calcule/valide RIEN. |
| `PivotPartyDto` / `PivotAddressDto` | Tiers (vendeur, acheteur, émetteur de facture, bénéficiaire) + adresse. |
| `PivotLineDto` / `PivotLineTaxDto` | Lignes et leur ventilation TVA. `SourceRegimeCodes` = régime source BRUT (collection) ; `CategoryCode`/`VatexCode` = résultat du mapping PLATEFORME (nuls dans le contrat). |
| `PivotTotalsDto` | Totaux de contrôle (BG-22). |
| `PivotPaymentDto` | Encaissements bruts (F09) ; agrégats jour × taux calculés sur la plateforme. |
| `PivotDocumentChargeDto` | Charges/remises niveau document (BG-20/BG-21, taxes non-TVA). |
| `PivotDocumentRefDto` | Référence d'un document d'origine d'un avoir — `Number` + `IssueDate` (date OBLIGATOIRE), multi-références. |
| `OperationCategory` / `VatCategory` | Énumérations (nature d'opération ; catégorie UNCL5305). |

**Sérialisation canonique (PIV02, ADR-0007).** Un UNIQUE writer (`CanonicalJson` / `CanonicalJsonWriter`)
produit le JSON hashé pour l'anti-doublon : membres dans l'ordre de déclaration, noms PascalCase,
`null` optionnel OMIS, collections toujours émises (`[]`), enums par nom, `decimal` invariant à
échelle préservée sans exposant, dates `yyyy-MM-dd`, **sortie ASCII pure** (non-ASCII échappé
`\uXXXX`). `PayloadHasher` en calcule le SHA-256 (hex minuscule, 64 car.). Le résultat est IDENTIQUE
octet par octet entre net48 et .NET 10 (preuve : golden files exécutés des deux côtés). Les golden
fixtures exercent le modèle pivot complet — à la fois la forme push agent réelle
(`facture-push-agent-brut`, `CategoryCode`/`VatexCode` nuls, forme hashée par l'anti-doublon PIV04)
ET des documents avec les champs de mapping renseignés, pour verrouiller les chemins
enum/champ-optionnel du sérialiseur cross-runtime ; l'agent lui-même ne remplit jamais
`CategoryCode`/`VatexCode` — cette frontière appartient à la plateforme (lot F03/TVA).

### 3.2 Enveloppes de transport (F12 §3.4)

| DTO | Champs |
|---|---|
| `PushBatchRequestDto` | `ContractVersion`, `Documents` (liste de `PivotDocumentDto`), `SourceTaxRegimes` (liste de `SourceTaxRegimeDto` — métadonnée de push, ajout add-only §4.1, optionnel/en fin de DTO, enveloppe non hashée), `ExtractorCapabilities?` (`ExtractorCapabilitiesDto` — capacités déclarées de la source ADR-0004 D2, métadonnée de push, ajout add-only §4.1, optionnel/en fin de DTO, **omis du format fil quand `null`**, enveloppe non hashée ; persisté par agent/tenant côté plateforme — RD401). |
| `PushBatchResponseDto` | `Results` (liste de `DocumentPushResultDto`). |
| `DocumentPushResultDto` | `SourceReference`, `Status` (`Accepted`/`Duplicate`/`Rejected`), `Reason?`. |
| `HeartbeatRequestDto` | `ContractVersion`, `AgentVersion`, `SentAtUtc`, `LastSuccessfulSyncUtc?`, puis **télémétrie d'exploitation ajoutée add-only (AGT03, §4.1)** : `ServiceState?`, `PushQueueDepth?`, `PushQueueErrorCount?`, `LastRunStartedUtc?`, `LastRunCompletedUtc?`, `LastRunOutcome?`, `LastError?`, `DiskFreeBytes?`. Tous optionnels (un agent N-1 les omet) ; exigés par F12 §2.5 et consommés par la supervision (F12 §5.2 « file qui grossit »/« run manqué », §5.3 dashboard). Enveloppe NON hashée → aucun impact d'empreinte. |
| `HeartbeatResponseDto` | `ServerTimeUtc`, `Configuration`. |
| `AgentConfigurationDto` | `ExtractionSchedule?`, `ExtractFromUtc?`, `ExtractToUtc?`, `LatestAgentVersion?`, `UpdateRequired` (défaut `false`, sûr), `UpdateUrl?`, `VersionManifestSignature?`. |
| `SourceTaxRegimeDto` | `Code` (brut), `Label?`, `Occurrences` — métadonnée de push pour la détection de couverture TVA03. |
| `ExtractorCapabilitiesDto` | `ProvidesSourceDocuments`, `ProvidesUnlinkedDocumentPool`, `HasDetailedLines`, `HasCreditNoteLink`, `ExposesPayments`, `RegimeKeyShape?`, `EmitterIdentitySource?`, `HasStoredHeaderTotal`, `IsMutableAfterIssue`, `NumberUniquenessScope?` — capacités DÉCLARÉES de la source (ADR-0004 D2, symétrique de `PaCapabilities`). Les formes énumérées voyagent en valeur BRUTE (nom de l'énumération source) ; l'agent DÉCLARE, il n'interprète jamais (CLAUDE.md n°6). Persisté par agent/tenant côté plateforme (RD401), consommé par RD403/RD409. |

> **Frontière hash.** Seul le **payload PAR DOCUMENT** porte une empreinte canonique (anti-doublon).
> Les enveloppes (batch, heartbeat) ne sont PAS hashées : leur encodage fil (négociation de contenu)
> est porté par l'ingestion (PIV04/PIV05). Les golden files `batch-mixte.json` / `heartbeat.json`
> sont des **références ILLUSTRATIVES** du format fil (noms de propriété exacts des DTOs + horodatages
> UTC `yyyy-MM-ddTHH:mm:ssZ`), pas un artefact hashé.

---

## 4. Règles d'évolution du contrat

1. **En v1, un champ s'AJOUTE — il ne se renomme ni ne se supprime jamais.** Un ajout est compatible
   (les anciens agents l'ignorent ; la plateforme traite l'absence comme `null`/défaut). Comme le
   writer canonique émet les membres dans l'**ordre de déclaration** et OMET les `null`, un champ
   optionnel ajouté **en fin de DTO** ne change pas l'empreinte des documents qui ne le renseignent
   pas — la compatibilité golden est préservée.
2. **Toute rupture (renommage, suppression, changement de type/sémantique) = v2 du contrat** :
   nouveau préfixe d'URL `/api/agent/v2/`, nouvel `AgentContractVersion`, nouveaux golden files. La
   v1 reste servie tant que des agents N-1 existent.
3. **Compatibilité N / N-1.** La plateforme accepte la version courante et la précédente ; une
   version plus ancienne reçoit `426 Upgrade Required` (déclenche l'auto-update — AGT04/OPS07).
4. **Un changement d'empreinte golden = rupture détectée.** Les hashes figés
   (`ContractFixtureTests.FrozenHashes`) sont l'ancre : si une modification du writer ou d'un DTO
   change la sortie canonique, les tests cassent des DEUX côtés. C'est volontaire — la régénération
   est un acte explicite, revu en gate humaine.

---

## 5. Golden files (`tests/fixtures/contrat-v1/`)

Jeu de référence FICTIF (aucune donnée client — CLAUDE.md n°7), généré par les builders de
`ContractFixtures` et vérifié par `ContractFixtureTests`, **liés dans les deux projets de test**
(plateforme `.NET 10` + agent `net48`). L'ensemble représente le **modèle pivot partagé** ; la forme
push agent réelle (champs de mapping nuls) est spécifiquement `facture-push-agent-brut`.

| Fichier | Cas représentatif |
|---|---|
| `facture-standard-b2c.json` | Facture B2C, taux normal (S 20 %) — le cas le plus fréquent. |
| `vente-sur-marge-exoneree.json` | Vente sur marge exonérée (catégorie E + `VATEX-EU-J`, taux 0). |
| `avoir-simple-lie.json` | Avoir lié à une seule facture d'origine (montants négatifs). |
| `avoir-partiel.json` | Avoir partiel (+ `SourceData` brut). |
| `avoir-groupe-multi-refs.json` | Avoir groupé multi-références (`Number` + `IssueDate` par référence). |
| `facture-b2b-pro.json` | Acheteur professionnel identifié, multi-lignes, multi-taxes (S + AA), charge document. |
| `facture-prestation-paiements.json` | Prestation de services + encaissements (F09), auto-facturation + affacturage, acompte. |
| `facture-push-agent-brut.json` | Push agent RÉEL : régime source brut, champs de mapping (`CategoryCode`/`VatexCode`) nuls — la forme hashée par l'anti-doublon PIV04. |
| `batch-mixte.json` | Lot illustratif portant deux documents (enveloppe, non hashée). |
| `heartbeat.json` | Heartbeat illustratif (enveloppe, non hashée). |

**Régénérer** (après une évolution VOLONTAIRE et sourcée du contrat) :

```powershell
$env:LIAKONT_REGEN_FIXTURES = "1"
$env:LIAKONT_FIXTURE_OUT = "$PWD\tests\fixtures\contrat-v1"   # chemin absolu — exécuter depuis la racine du repo
dotnet test tests/Contracts/Liakont.Agent.Contracts.Tests.Unit `
  --filter "FullyQualifiedName~Fixtures_are_present_or_regenerated"
```

Puis reporter les nouvelles empreintes dans `ContractFixtureTests.FrozenHashes` et committer les
fichiers — la modification passe en revue humaine (gate de segment), jamais en silence.
