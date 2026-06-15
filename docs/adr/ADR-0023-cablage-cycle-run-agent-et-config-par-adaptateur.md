# ADR-0023 — Câblage du cycle de run de l'agent (AGT02) et canal de configuration par-adaptateur

**Date :** 2026-06-14

**Statut :** Accepté (2026-06-14)

---

## Contexte

Toute la tuyauterie d'extraction et de transport de l'agent existe et est testée :
`IExtractor` (contrat, ADR-0004), `ExtractionCycle` (extraction → file locale), `LocalQueue`
(tampon SQLite WAL, anti-doublon), `QueueDrainer` (push par lots + réconciliation deux-temps,
ADR-0012), `HttpPlatformClient` (POST `/api/agent/v1/documents/batch`), `AgentRunCycle`
(orchestration extraction → drainage + journal + auto-update, ADR-0013). Le `EncheresV6FixtureExtractor`
est un adaptateur de référence complet.

**Deux pièces manquent pour qu'un agent installé extraie et pousse réellement (item AGT02) :**

1. **Le cycle de run du service est NEUTRE.** `AgentHost.Create()` injecte un délégué vide
   (`Action<CancellationToken> runCycle = _ => { }`). Le service Windows reste vivant, démontre
   l'arrêt propre, mais ne touche à rien. `AgentRunCycle` existe mais n'est jamais câblé au
   composition root du service.

2. **`agent.json` n'a aucun canal pour la configuration SPÉCIFIQUE d'un adaptateur.**
   `AgentConfigLoader` ne produit qu'une `ExtractionConfig` générique
   (`adapter`, `odbcConnectionString`, `fixturesPath`, `pdfPoolPath`, `schedule`,
   `catchUpOnStart`). Or un adaptateur a besoin de paramètres qui ne sont **pas dans la base
   source** : l'identité de l'émetteur (SIREN — F01-F02 §4.3 : « le SIREN émetteur n'est pas
   dans la base, il vient du paramétrage ») et la nature d'opération (`OperationCategory`,
   champ OBLIGATOIRE du `PivotDocumentDto`). Le registre actuel `EmbeddedSourceAdapters.Create()`
   fait un `new EncheresV6Extractor()` **sans configuration** — il ne peut pas produire un
   extracteur réellement configuré.

C'est le moment de coût minimal pour trancher le câblage et le canal de config, avant que
les premiers adaptateurs réels (items ADP) ne se multiplient.

---

## Décision

### D1 — Câbler `AgentRunCycle` au composition root du service

`AgentHost.Create()` compose les dépendances réelles à partir de `agent.json` :
`LocalQueue` (chemin `AgentPaths.DatabasePath`) + `HttpPlatformClient` (URL + clé API
déchiffrée) + `ExtractionCycle` + `QueueDrainer` + l'extracteur configuré (D2) → `AgentRunCycle`,
dont `Run` devient le délégué bouclé par `AgentBackgroundRunner`. L'arrêt propre (barrière de
`gracePeriod`) est préservé. Une configuration illisible ou un adaptateur inconnu **bloque le
démarrage** avec un message opérateur français (jamais de cycle vide silencieux — règle n°3).

### D2 — Sélection d'adaptateur par une fabrique/registre

Une `IExtractorFactory` (registre des adaptateurs embarqués) résout
`(nom d'adaptateur, ExtractionConfig, AdapterConfig) → IExtractor` configuré. Elle **remplace**
l'instanciation paramétrée nulle de `EmbeddedSourceAdapters`. Chaque adaptateur enregistre sa
propre fabrique ; le cœur ne référence aucun adaptateur concret par `if (adapter is …)`
(symétrie avec `PaCapabilities`, règle n°8 et ADR-0004 D2).

### D3 — Canal de config par-adaptateur : section `adapterConfig.<nom>` dans `agent.json`

```json
{
  "platformUrl": "...",
  "apiKey": "<DPAPI>",
  "extraction": {
    "adapter": "DemoErpA",
    "odbcConnectionString": "<DPAPI>",
    "schedule": ["03:00"]
  },
  "adapterConfig": {
    "DemoErpA": {
      "emitterSiren": "123456782",
      "emitterName": "Société Fictive de Démonstration",
      "operationCategory": "LivraisonBiens"
    }
  }
}
```

`AgentConfigLoader` parse `adapterConfig` **génériquement** : il expose la section brute
(dictionnaire nom → objet JSON) sans connaître les champs d'un adaptateur donné. C'est la
**fabrique de chaque adaptateur** qui lit et valide SA section (présence du SIREN, valeur
d'`OperationCategory`…), avec des messages opérateur français. Le loader reste découplé des
adaptateurs : ajouter un adaptateur n'élargit jamais le schéma générique.

### D4 — Les paramètres émetteur/nature d'opération vivent dans `agent.json` (AGT02), surchargeables par la plateforme plus tard (AGT03)

Conformément à F12-A §3, `emitterSiren` et `operationCategory` sont conceptuellement du
**paramétrage tenant**. En l'absence d'AGT03 (push de configuration effective par le heartbeat),
ils sont portés par `agent.json` (`adapterConfig`) pour qu'un agent fonctionne **hors-ligne et
de façon autonome**. AGT03 pourra ensuite faire surcharger cette config par la plateforme
(plateforme prioritaire) sans casser le schéma : `adapterConfig` reste le point de fusion.

### D5 — Adaptateurs de démonstration `DemoErpA` / `DemoErpB`

Pour exercer le câblage (D1) et le contrat `IExtractor` contre de **vraies sources ODBC**
(SQL Server local, deux schémas distincts), deux adaptateurs de référence sont livrés :
`DemoErpA` (schéma normalisé, montants `decimal`) et `DemoErpB` (schéma dénormalisé, montants
`float` legacy → exerce la conversion `float`→`decimal` half-up obligatoire, ADR-0004 D3-7).
Ils sont **embarqués** dans l'agent (sinon un service installé ne les connaît pas) et
**clairement marqués démo** ; les adaptateurs ERP réels arriveront avec leurs items ADP. Leurs
données sources sont fictives (règle n°7), versionnées sous `deployments/demo-local/`.

---

## Conséquences

1. **AGT02 avance réellement** : un agent installé extrait et pousse, au lieu de tourner à vide.
   La frontière reste respectée — l'agent n'a aucune logique métier (pas de TVA, pas de
   validation, pas de machine à états ; règle n°6) : il lit en seule lecture, mappe vers le
   pivot brut et pousse.
2. **Schéma `agent.json` étendu de façon ADDITIVE** (`adapterConfig` optionnel) : compatible
   ascendant, les agents EncheresV6/fixtures existants ne sont pas impactés.
3. **Chaque adaptateur déclare son contrat de config** (sa fabrique valide sa section). Pas de
   couplage du loader aux adaptateurs.
4. **Sécurité** : la chaîne ODBC et la clé API restent chiffrées DPAPI dans `agent.json` ;
   aucun secret en clair versionné (règles n°10). Les messages d'erreur d'extraction ne
   contiennent jamais d'identifiant de connexion (ADR-0004, R7).
5. **Lecture seule stricte renforcée** (règle n°5) : la connexion source utilise un login en
   lecture seule (`db_datareader`), pas l'identité du service ; aucun INSERT/UPDATE/DELETE/verrou.
6. **AGT03 reste réservé, pas oublié** : la surcharge de config par la plateforme se branchera
   sur `adapterConfig` (point de fusion), sans rupture de schéma.

---

## Alternatives rejetées

- **Étendre `ExtractionConfig` avec `emitterSiren`/`operationCategory`/… (Option A).** Rejeté :
  couple `AgentConfigLoader` à TOUS les champs de TOUS les adaptateurs ; chaque nouvel adaptateur
  élargit le schéma générique. La section `adapterConfig.<nom>` isole proprement.
- **N'attendre que la config poussée par la plateforme (Option C, AGT03 seul).** Rejeté pour V1 :
  exige AGT03 (heartbeat de config) non livré, et supprime le fonctionnement autonome/hors-ligne
  de l'agent. `adapterConfig` dans `agent.json` est le secours autonome ; AGT03 viendra surcharger.
- **Laisser le cycle neutre et seeder les documents côté plateforme (`dev-seed-demo-docs.ps1`).**
  Rejeté ici : ne prouve pas le chemin agent réel (extraction ODBC → file locale → push →
  réconciliation), qui est précisément l'objet d'AGT02 et du test d'installation des services.

---

## Références

- `docs/adr/ADR-0004-perimetre-contrat-extraction-pivot-v1.md` (contrat `IExtractor`, capacités, R1–R8)
- `docs/adr/ADR-0012-acquittement-agent-deux-temps-reconciliation-statut.md` (drainage/réconciliation)
- `docs/adr/ADR-0013-modele-confiance-auto-update-agent.md` (auto-update, dépendance optionnelle du cycle)
- `docs/conception/F12*` (CLI/agent, schéma `agent.json`, planification d'extraction)
- `agent/src/Liakont.Agent/AgentHost.cs` (point d'injection du cycle), `AgentRunCycle.cs`,
  `Liakont.Agent.Core/Configuration/AgentConfigLoader.cs`, `EmbeddedSourceAdapters.cs`
- `blueprint.md` §2 et §6 (frontières de la généricité — l'agent ne référence que ses contrats)
