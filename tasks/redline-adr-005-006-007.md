# Redline ADR-0005 / ADR-0006 / ADR-0007 — source du lot RDL

Revue critique (« redline ») demandée par Karl (2026-06-19) des trois ADR ci-dessous **et de tout
ce qui en a découlé** dans le code, les tests, les docs et les ADR aval, maintenant que l'analyse
peut être plus fine. Objectif : repérer ce qu'on a **manqué** et qui mordra **plus tard** (pièges
silencieux, faux-verts, divergences de hash, fuites cross-tenant, dette de bascule), pas réécrire
ce qui marche.

- ADR-0005 — L'agent dans un dépôt Git séparé ; contrat partagé via NuGet versionné.
- ADR-0006 — Mécanique de jobs multi-tenant dans `src/Common`, basculement de tenant derrière une abstraction.
- ADR-0007 — Sérialisation canonique du pivot et empreinte de payload (PIV02).

## Méthode

Revue multi-agents (14 lentilles critiques) puis **vérification adversariale** de chaque finding
(scepticisme par défaut : réfuter sauf si les faits tiennent, et chercher une garde existante qui
annule le finding). Bilan : **57 findings → 43 confirmés, 2 arbitrage humain, 12 réfutés.**

Sévérités : P1 = bloquant ; P2 = important non bloquant ; P3 = dette/futur à surveiller. Les
sévérités ci-dessous sont les sévérités **corrigées** après vérification adversariale.

Les 43 confirmés + 2 arbitrages sont regroupés en **13 items RDL** (un par session d'agent, par
localité de fichier / cohérence de thème). Les règles non négociables CLAUDE.md restent valables
sur chaque item : montants `decimal`, aucune règle fiscale inventée, DocumentEvent/WORM append-only,
scoping tenant, lecture seule base source, frontières de modules, secrets jamais versionnés, socle
vendored `Stratum.*` → consigner en provenance.

---

## Items du lot RDL

### RDL01 — Enum canonique gardé (`Enum.IsDefined`) + binding strict [P1]
Findings : **A7-fmt-1 (P1)**, **A7-fmt-2 (P1)**, **A7-det-2 (P3)**, **A5-purity-4 (P3)**.
- `CanonicalJson.cs:56` (`OperationCategory.ToString()`) et `:189` (`CategoryCode.Value.ToString()`)
  émettent un **nombre** pour une valeur d'enum non définie (les enums commencent à 1 ; `((Enum)0/99).ToString()` → `"0"`/`"99"`), contredisant ADR-0007 règle 4 (« émis par leur NOM »). `ToString()`
  ne lève pas → l'enveloppe try/catch d'ingestion (`IngestDocumentBatchHandler.cs:160`) ne capte rien.
- `AgentApiJson.cs:32-33` laisse `allowIntegerValues=true` sur les `JsonStringEnumConverter<OperationCategory/VatCategory>` → un entier hors plage entre par le fil sans rejet.
- Le site `CategoryCode` est largement déjà défendu (mapping plateforme gardé `Enum.IsDefined`, hash calculé avant mapping quand `CategoryCode=null`) ; le **vrai trou résiduel est `OperationCategory`** (champ non-nullable toujours sérialisé, sans validation plateforme). Impact borné : « nombre muet » dans le hash/archive WORM, pas un code sur la facture transmise.
- Fix : `WriteEnum<T>` sur `CanonicalJsonWriter` gardé par `Enum.IsDefined` qui **lève** si indéfini (déjà attrapé en rejet par le handler) ; `allowIntegerValues:false` sur les 2 convertisseurs ; tests négatifs des deux côtés. Aligné sur « bloquer plutôt qu'envoyer faux » (CLAUDE.md n°3).

### RDL02 — Fermer la boucle de hash RÉELLE (wire-STJ↔writer) + acter le lecteur prod [P2]
Findings : **A7-det-1 (P2)**, **A7-golden-1 (P3)**, **X-down-2 (P2)**.
- En prod, la plateforme ne hashe PAS les octets reçus de l'agent : elle **re-désérialise via System.Text.Json** (`AgentApiEndpoints.cs:92-96`) puis `CanonicalJson.Serialize(document)` + `PayloadHasher.ComputeHash` sur le DTO STJ-désérialisé (`IngestDocumentBatchHandler.cs:157-158`). L'axe **STJ(wire)→writer→hash**, dont dépendent PIV04 (anti-doublon) et TRK03 (altération), n'a **aucune ancre de test** : les golden prouvent writer↔writer (DTO en mémoire). Ça marche aujourd'hui par chance du comportement STJ courant.
- ADR-0007:77-79 promet « le lecteur ne vit PAS en production », mais un **second lecteur canonique vit en prod** (`PivotCanonicalJsonReader` du Pipeline, sert le SEND : `SendTenantJob.cs:746→Read`). 3 artefacts à garder synchronisés au champ près (writer, lecteur de test, lecteur prod), sans garde de complétude par réflexion sur le lecteur prod (un champ porté par l'agent mais oublié dans le lecteur prod est **amputé avant transmission PA** — EXT01/BT-9 l'a frôlé).
- Fix : (a) test Host (net10) qui, par fixture contrat-v1, `Deserialize<PushBatchRequestDto>(golden_wire, HostMinimalApiOptions())` puis assert `ComputeHash(request.Documents[i]) == FrozenHashes[name]` ; (b) garde de complétude par réflexion sur `PivotCanonicalJsonReader` (chaque propriété DTO consommée) ; (c) mettre à jour ADR-0007 §Conséquences pour acter le lecteur prod (miroir du writer, ré-hash interdit, invariant INV-PIPELINE-001/002).

### RDL03 — Garde de complétude/ordre par réflexion du writer + cas limites [P2]
Findings : **A7-cov-1 (P2)**, **A7-cov-3 (P3)**, **A7-cov-4 (P3)**, **A7-fmt-4 (P2)**, **A7-evo-3 (P2)**, **A7-evo-5 (P3)**.
- Le test de couverture par réflexion (`CanonicalJsonRulesTests.cs:176-202`) vérifie la **présence** de chaque propriété mais **jamais l'ordre de déclaration** (règle 1 ADR) ; le seul test d'ordre ne contrôle que 4 paires au niveau document, aucun DTO imbriqué. Un champ inséré au milieu d'un DTO mais écrit en fin passe vert puis casse l'empreinte du premier document réel qui le porte.
- Les optionnels absents des 8 fixtures (Email, Siret, Line2, ReasonCode, ordre party) ne sont jamais exercés (A7-evo-3). Aucun test pour 0x7F/0x00, paire de substitution UTF-16, `0.00m` (A7-fmt-4). Les enveloppes (Heartbeat) sont écrites à la main et divergent déjà de 8 champs (A7-cov-3, hors hash mais pattern « writer manuel qui dérive »). Double maintenance writer + `BuildFullyPopulated`, message d'échec ambigu (A7-cov-4). La non-régression « additif → hash inchangé » n'est ancrée que sur un golden (A7-evo-5).
- Fix : assertion d'ordre par réflexion (parser positionnel ordonné vs `GetProperties()` filtré) pour CHAQUE DTO ; tests des cas limites d'échappement et decimal ; généraliser `AssertAllPropertiesArePresent` aux enveloppes (ou figer la liste hors-périmètre) ; ancrer la non-régression additive sur tout le jeu de fixtures.

### RDL04 — Évolution du contrat : sens N+1→N + croisement version↔forme [P2]
Findings : **A7-evo-2 (P2)**, **A7-evo-4 (P3)**.
- Le hash plateforme est recalculé par re-sérialisation du DTO (pas des octets HTTP) et le lecteur **droppe silencieusement** tout membre inconnu (`TryGetProperty` par nom). Donc **agent N+1 (déploiement non atomique) → plateforme N** : le champ v2 est ignoré, empreinte plateforme ≠ empreinte agent → anti-doublon cassé / fausse alerte d'altération. Aucun garde-fou ne refuse un payload `"1"` portant un membre v2.
- `ContractVersion="1"` (payload) et préfixe d'URL `/v1` sont deux constantes indépendantes jamais croisées ; aucun test ne lie la version déclarée à la forme réelle.
- Fix : trancher et tester le sens N+1→N (défaut sûr : le lecteur **rejette** tout membre inconnu pour préserver l'intégrité du hash) ; invariant statique liant `AgentContractVersion.Current` et `ContractVersion` (+ dériver le préfixe MapGroup de `Current`) ; runbook de bascule v2.

### RDL05 — Déterminisme : normalisation Unicode (décision) + date calendaire + verrou EOL des golden [P3 + arbitrage]
Findings : **A7-det-3 (P3, arbitrage humain)**, **A7-fmt-5 (P3)**, **A7-det-4 (P3)**.
- **A7-det-3 (décision à trancher, règle n°2)** : `AppendEscapedString` n'applique aucune normalisation Unicode ; « café » NFC (U+00E9) et NFD (U+0065 U+0301) produisent deux empreintes. Identique net48/net10 (pas une divergence cross-runtime) mais risque PIV04 si une source ODBC renvoie tantôt NFC tantôt NFD. ADR-0007:96-104 délègue le déterminisme de `SourceData` à l'adaptateur mais n'énumère PAS la normalisation. **Décision défendable par défaut** : normaliser en NFC dans le writer (`value.Normalize(NormalizationForm.FormC)`, identique des deux côtés) + figer en règle 7 de l'ADR ; **OU** ajouter NFC à la liste explicite des invariants que l'adaptateur DOIT garantir + test ADP. Tracer la décision (ne pas la laisser implicite).
- **A7-fmt-5** : `WriteDate` sérialise la date verbatim sans normaliser `Kind`/fuseau ; pas de bug aujourd'hui (les adaptateurs passent la colonne telle quelle), mais un futur adaptateur dérivant la date d'un `DateTimeOffset`/`ToLocalTime` décalerait la date hashée près de minuit. Documenter l'invariant « date calendaire, fuseau ignoré » + test (deux `Kind` différents, même octet).
- **A7-det-4** : les golden `.json` ne sont pas verrouillés LF/no-BOM dans `.gitattributes` (seul `*.sh text eol=lf`). Risque actuel nul (fixtures mono-ligne, 0 octet LF) mais cohérent avec la lesson repo « forcer LF » : ajouter `tests/fixtures/contrat-v1/*.json -text` + asserter no-BOM/no-CRLF/no-trailing-newline sur octets bruts.

### RDL06 — Câbler les 4 fan-out Pipeline via `AddJobHandler` + test de dispatchabilité [P1]
Findings : **A6-cons-1 (P1)**, **A6-cons-3 (P2)**.
- `PipelineModuleRegistration.cs:65,69,75,83` enregistre `SendAllTrigger`/`SyncAllTrigger`/`AggregatePaymentsAllTrigger`/`RectifyReportsAllTrigger` en `AddScoped<IJobHandler<T>>` **seul**, alors que tous les autres jobs système passent par `AddJobHandler<T,H>` (qui ajoute la `JobHandlerRegistration` singleton). Or `JobHandlerResolver` (`CanHandle`/`ExecuteAsync` lève `INV-JOB-001` ligne 53 AVANT la résolution DI ligne 68) et `JobTypeCatalog` se construisent **uniquement** depuis ces singletons. Vérifié sur pièce : ces 4 déclencheurs ne sont **ni planifiables** (absents du catalogue) **ni dispatchables**. `AddScoped` est nécessaire mais **pas suffisant**.
- Conséquence : SEND a un repli console mono-tenant (`SendTenantTrigger`, lui `AddJobHandler`'d), mais **SYNC (rapprochement tax reports DGFiP + addenda WORM), AGGREGATE (projection paiements → page Encaissements) et RECTIFY (rectificatifs e-reporting annule-et-remplace) n'ont AUCUN chemin alternatif → ne tournent jamais.**
- Faux-vert co-responsable (A6-cons-3) : les tests de fan-out prouvent la délégation au runner, jamais la dispatchabilité (catalog/resolver) — la chaîne réelle (schedule→enqueue système→JobWorker→resolver→handler) n'est jamais exercée.
- Fix : remplacer les 4 `AddScoped` par `AddJobHandler<*AllTrigger, *AllFanOutHandler>("libellé FR")` ; ajouter un test de composition (graphe DI réel) asserttant que CHAQUE `IJobHandler<T>` de fan-out récurrent a `CanHandle(FullName)==true` ET `JobTypeCatalog.Find(FullName)!=null`. Ce test aurait attrapé le bug et le rattrapera au prochain consommateur.

### RDL07 — Observabilité des jobs de fan-out (faux-verts opérationnels) [P2 + arbitrage]
Findings : **A6-runtime-1 (P3, arbitrage humain)**, **A6-cons-2 (P2)**, **A6-runtime-3 (P2)**.
- **A6-runtime-1 (décision à trancher)** : les 6 fan-out handlers loguent un `Warning` sur `FailedCount>0` puis retournent sans `throw` → `JobWorker.MarkCompleted()` + `job.completed`, aucun retry/dead-letter pour un échec PARTIEL. **Nuance vérifiée (sévérité corrigée P1→P3)** : l'escalade existe DÉJÀ pour les cas à enjeu, par conception, via le dead-man's-switch Supervision + états document (Blocked/RejectedByPa) + règles d'alerte testées (`BlockedDocumentsAlertRule`, `PaRejectedDocumentsAlertRule`, `agent.missed_run`) — un SEND à moitié échoué N'EST PAS invisible. **Résidu réel mais étroit** : aucune règle d'alerte ne couvre « ancrage quotidien échoué pour tenant X » (TRK06) ni « purge support échouée » (FX06). Décision produit/observabilité à acter dans ADR-0006 : sémantique d'un run partiel + faut-il une règle d'alerte dédiée à l'ancrage ?
- **A6-cons-2** : `SystemJobScheduleHealthCheck` (filet FIX203b « job.schedules vide ⇒ supervision morte / coffre jamais ancré ») ne couvre que 2 jobs (`SystemJobDefinitions.All` = SupervisionEvaluation + DailyAnchoring). Réconciliation, purge, drain signature, tacite 389 (+ les 4 de RDL06 une fois câblés) sont enregistrés mais absents → s'ils ne sont pas planifiés, aucun warning de démarrage. Fix : étendre le health-check à tous les `IJobHandler<T>` de fan-out récurrent (classe « requis seedé + dur » vs « attendu cadence-déploiement »).
- **A6-runtime-3** : `ListAsync` renvoyant 0 tenant actif est un no-op `Information` indistinct d'une anomalie (catalogue vide par bug de provisioning, mauvaise base). Logger en `Warning` + exposer la distinction.

### RDL08 — Robustesse du runner à l'échelle : reprise, anti-recouvrement, budget temps [P2]
Findings : **A6-scale-1 (P3)**, **A6-scale-2 (P2)**, **A6-scale-3 (P2)**, **A6-scale-5 (P3)**, **A6-runtime-2 (P2)**, **A6-runtime-4 (P3)**, **A6-di-2 (P3)**.
- **A6-scale-1** : un fan-out qui meurt en cours laisse le `JobEntry` bloqué `Running` à jamais (aucun reaper : grep `reaper|reclaim|recover|stuck` = 0) ; les tenants restants ne sont pas rattrapés ce tick. Pour l'ancrage WORM quotidien, une fraction non scellée sans alerte ni reprise.
- **A6-runtime-2** : annulation (shutdown) pendant un run → job orphelin `Running` (ni Completed ni Failed) + summary partiel perdu (l'OCE jette tout).
- **A6-scale-2** : pas de garde anti-recouvrement ; un fan-out plus long que l'intervalle cron (supervision `*/15`) empile des déclencheurs identiques qui affament le worker mono-job (`BatchSize=1`).
- **A6-scale-3** : aucune borne de temps par tenant autour de `job.ExecuteAsync` ; un tenant lent (TSA, base saturée) retarde tout le run séquentiel.
- **A6-scale-5** : le témoin de vie du dead-man's-switch lit l'achèvement du fan-out ENTIER (`GetLastCompletedAtByTypeAsync`) ; à l'échelle, fausse alarme si > 2×15 min, et fraîcheur par-tenant invisible.
- **A6-runtime-4** : filtre OCE basé sur l'état global du token appelant — robuste aujourd'hui, piège latent si un timeout par tenant (linked CTS) est introduit (filtrer alors sur `ex.CancellationToken == cancellationToken`).
- **A6-di-2** : le seam de prod (Host `TenantScopeFactory`) n'est jamais exercé bout-en-bout sur 2 vraies bases (l'intégration utilise un double). Ajouter un test d'intégration DI réel (Testcontainers, `AddStratumMultiTenancy` + Host `TenantScopeFactory`) prouvant que `RunForAllTenantsAsync` écrit dans la bonne base.
- Fix : documenter dans ADR-0006 le contrat de reprise (reaper des `Running` expirés → `Pending`/`Dead`, idempotence des `ITenantJob`) ; dé-duplication à l'enqueue ; budget temps par tenant (linked CTS → `TenantJobFailure` isolé) ; fraîcheur par tenant pour la liveness ; le test DI réel. (Note : A6-scale-4 — `ListAsync` non borné/séquentiel — jugé acceptable « none » à l'échelle visée, non inscrit ; pousser au plus le filtre `IsActive` en SQL.)

### RDL09 — Provenance du socle : lever la contradiction baseline↔doc + consigner les régénérations [P2] (tooling)
Findings : **A6-prov-1 (P2)**, **A6-prov-2 (P3)**.
- **A6-prov-1** : `tools/socle-baseline.sha1` **épingle désormais** les fichiers AJOUTÉS par SOL06 (ex. `src/Common/Abstractions/Jobs/ITenantJob.cs`, `TenantJobRunner.cs`) — régénérés par OPS03 — alors que provenance §4.14 et ADR-0006:52-55 affirment « ces ajouts ne dérivent pas et ne figurent PAS dans le bloc ». **Faux positif latent démontré** : éditer légitimement un de ces fichiers fait échouer verify-fast (« modif socle non consignée »), poussant au mauvais geste (consigner du code Liakont comme dérive Stratum, ou régénérer le baseline et masquer une vraie dérive). ⚠️ **RDL07/RDL08 éditent `TenantJobRunner.cs` (épinglé) → cet item doit précéder.**
- **A6-prov-2** : décompte périmé (§4.12 dit 1226, réel 1249) ; régénération OPS03 jamais consignée en provenance.
- Fix : choisir une politique cohérente — (A) **exclure** les fichiers `// Liakont addition (…)` du périmètre épinglé (filtre sur le marqueur de tête dans `Get-VendoredHashes`), l'invariant doc redevient vrai ; **OU** (B) assumer que tout fichier sous les roots vendored est épinglé après régénération et corriger §4.14/§4.25/§4.27/§4.28 + workflow §4.12. Plus : consigner chaque `-Generate` (item, commit, raison, décompte) ; remplacer le « 1226 » codé en dur. Smoke-test sur état propre/dérive/régénération.

### RDL10 — Mettre à jour ADR-0005 : prémisse périmée + plan de bascule exhaustif [P2] (docs-spec)
Findings : **A5-coupling-1 (P2)**, **A5-coupling-5 (P3)**, **A5-fix-1 (P2)**, **A5-fix-4 (P3)**, **A5-fix-5 (P2)**.
- **A5-coupling-1** : la prémisse « rien de substantiel à déplacer » est invalidée (8 projets de prod, ~190 fichiers, ~15 900 LOC : extraction ODBC réelle, transport/queue/auto-update, installeur WinForms, updater, 3 adaptateurs). La fenêtre « scission bon marché » est passée.
- **A5-coupling-5** : `Liakont.Agent.Updater` (AGT04/ADR-0013) et les adaptateurs de démo `DemoErpA/DemoErpB` sont absents de l'énumération « Contenu » de l'ADR ; statuer (Updater dans le produit ; démo → fixtures de test ?).
- **A5-fix-1** : les golden/lecteur de contrat sont des fichiers liés intra-repo (`tests/_shared/contract-v1`, `tests/fixtures/contrat-v1`) ; à la scission, soit duplication (deux ancres SHA-256 qui divergent — le faux-vert que F12 §3.4 veut empêcher), soit la preuve net48 disparaît. Décider le vecteur de partage (NuGet `*.TestKit` en contentFiles consommé des deux côtés) AVANT la bascule.
- **A5-fix-5** : `verify-fast.ps1` Step 4 + le job CI `agent:` codent le mono-repo en dur ; documenter la séquence de transition (retrait Step 4 + retrait job agent + sortie de SOL02 du gardien bootstrap) et garantir que la preuve de hash net48 est re-jouée côté plateforme via le golden empaqueté.
- **A5-fix-4** : le champ `repo:` au niveau segment n'existe ni dans `MANIFEST-CONVENTIONS.md` ni dans le manifest ; le réserver/spécifier dans les conventions (ADR-0005:78-79).
- Fix : réécrire §Conséquences d'ADR-0005 avec un inventaire de bascule EXHAUSTIF et à jour (cf. RDL11 pour les gardes exécutables) ; pas de code produit dans cet item (l'ADR acte la bascule DIFFÉRÉE).

### RDL11 — Gardes déclaratives : pureté du contrat + anti-divergence du partage des golden [P2]
Findings : **A5-purity-1 (P2)**, **A5-coupling-2 (P2)**, **A5-coupling-3 (P2)**.
- **A5-purity-1** : aucune garde DÉCLARATIVE ne verrouille « zéro PackageReference » ni « netstandard2.0 » sur `Liakont.Agent.Contracts.csproj` (les tests existants inspectent l'IL, qui rate un `PackageReference` purement déclaratif ; le test de frontière `AgentPackageReferenceBoundaryTests` ne scanne que `agent/`, pas `src/Contracts/`). Un analyseur en `PrivateAssets=all` ou un passage à `net8.0` casserait la consommabilité net48 sans test rouge — condition même de la publication NuGet.
- **A5-coupling-2** : le couplage cross-solution recensé par l'ADR est sous-compté (6 `ProjectReference` vers `src/Contracts`, pas 1 ; + 1 `Compile`-link de 4 fichiers `_shared` ; + 1 copie de fixtures). Ajouter un test qui échoue si un nouveau chemin `..\..\..\src` ou `..\..\..\tests` apparaît dans un csproj agent.
- **A5-coupling-3** : ajouter un test asserttant que les N empreintes figées sont effectivement exercées côté agent (compte des cas) pour qu'un retrait du `Compile`-link échoue bruyamment (sinon le test « disparaît » du run agent à la scission = faux-vert silencieux).
- Fix : test déclaratif lisant le csproj du contrat (TargetFramework == netstandard2.0, zéro PackageReference effectif en tenant compte du `Remove` hérité) ; tests anti-dérive sur les chemins cross-solution et la présence des golden côté agent.

### RDL12 — Sortir `PivotReconciliation` (BR-CO-13) du contrat publiable [P2]
Finding : **A5-purity-2 (P2)**.
- `PivotReconciliation.cs:14-34` (assembly de contrat) encode la sémantique EN 16931 **BR-CO-13** (« IsCharge ajoute BG-21, sinon retranche BG-20 » + placement de l'arrondi) et est la source UNIQUE d'une validation **bloquante** (`LineTotalsRule.cs:46`). L'agent net48 ne l'utilise JAMAIS (il consomme `PivotRounding`/`CanonicalJson`/`PayloadHasher`). Or ADR-0005 décision 3 (« aucune logique ») et CLAUDE.md n°6 (« l'agent n'a AUCUNE logique métier ») : ce paquet — publié et référencé par l'agent — embarquerait une règle de réconciliation fiscale. Ni ADR-0005 ni ADR-0007 ne tracent ce placement (ADR-0007 justifie nommément `PivotRounding`/writer, PAS `PivotReconciliation`).
- Fix : déplacer `PivotReconciliation` dans un assembly partagé **plateforme-seul** (côté `src/`), de sorte que le paquet NuGet du contrat ne porte que writer+hasher+rounding+DTOs ; mettre à jour les références (`Validation`, `Host`) ; la formule BR-CO-13 reste source-unique côté plateforme sans franchir la frontière agent.

### RDL13 — Intégrité documentaire des ADR : collision ADR-0023 + statut ADR-0016 [P3] (docs-spec)
Findings : **X-down-1 (P3)**, **X-down-3 (P3)**, **A6-cons-4 (P3)**.
- **X-down-1** : deux fichiers portent le numéro **ADR-0023** (`cablage-cycle-run-agent…` et `generation-facturx-pdfa3-cii`). Une convention de désambiguïsation de facto existe (FacturX = « §N » ; câblage = « D1-D5 ») et 4 ADR ultérieurs assument la collision (« numéro libre toutes branches confondues »), mais la traçabilité est déjà dégradée : `DemoErpARowMapper.cs:27` / `DemoErpBRowMapper.cs:26` citent « ADR-0023 D8 » alors que l'ADR câblage ne contient que D1-D5 (référence orpheline). Fix : renuméroter le câblage-agent vers un numéro libre (ex. ADR-0031) + en-tête « Numérotation » + index `docs/adr/README.md` ; corriger la référence orpheline D8.
- **X-down-3 / A6-cons-4** : ADR-0016 reste « Proposé » alors que sa décision (isolation tenant, `SendTenantTrigger`) est livrée et testée (CLAUDE.md n°9). Passer à « Accepté » + « Mise en œuvre : item API02a » + back-link depuis ADR-0006 (ADR-0016 raffine ADR-0006 : cron = tous-tenants / console = un tenant). La garde anti-fan-out est déjà verrouillée par test (`SendTenantTriggerHandlerIntegrationTests`), donc dette purement documentaire.

---

## Findings réfutés (12) — vérifiés et écartés (NON inscrits)

| Id | Sév. proposée | Raison de la réfutation |
|---|---|---|
| A7-cov-2 | P3 | La complétude net48 est assurée par un mécanisme PLUS FORT : le golden byte-identique cross-runtime (lié et exécuté des deux côtés) casse sur toute omission/réordonnancement. Pas de `#if` dans le writer. |
| A7-golden-2 | P2 | Prémisse de divergence cross-runtime FAUSSE : `decimal.ToString` préserve l'échelle de façon identique net48/.NET10 (conformité ECMA) ; le négatif-zéro decimal rend `"0.00"` sans signe des deux côtés (≠ double/float). Absence de fixtures scale>2 = simple complétude P3, pas un risque d'idempotence. |
| A7-golden-3 | P3 | Le lecteur prod ne re-hashe jamais sa sortie ; l'intégrité passe par la string brute re-hashée AVANT lecture (`FileSystemPayloadStagingStore.cs:129`, INV-PIPELINE-002). Déjà documenté/testé. Régression future inexistante. |
| A7-fmt-3 | P2 | `OperationCategory`/`VatCategory` sont posés depuis des frontières qui valident déjà `Enum.IsDefined` (`SourceEmitterConfig.cs:64-65`, `TenantSettingsParsing.cs:43`, mapping validé) ; « 99 » non atteignable au reader. La garde writer « à aligner » n'existe pas (c'est RDL01 qui la crée). |
| A7-evo-1 | P1 | La promesse F12 §6.4 (CI golden N-1) est conditionnelle à v2 (PIV.yaml:133-134) ; en v1 il n'y a pas de N-1 à rejouer (les golden v1 deviennent le corpus N-1 à v2). Dette déjà tracée (`AgentContractVersionPolicy.cs:9`). |
| A6-di-1 | P2 | Le repli null→base système est un comportement de contrat documenté (endpoints système, lecture catalogue) ; `TenantId` validé par regex au routage ; aucun chemin actuel ne route un job vers la base système. Pas une garde manquante. |
| A6-di-3 | P3 | Le harness d'intégration construit le VRAI composition root et résout `ITenantScopeFactory` (`ConsoleApiFactory.cs:663,678,1072…`, `DevTenantSeeder` au boot) → un retrait de registration échoue bruyamment. Le mode « silencieux jusqu'en prod » ne tient pas. |
| A5-coupling-4 | P3 | Le membre `.sln` cross-arbre est un corollaire mécanique de la ProjectReference déjà listée « couplage à défaire » par l'ADR ; une PackageReference n'est pas membre de solution. Erreur factuelle dans la preuve (Contracts n'est PAS niché sous le dossier `src`). |
| A5-purity-3 | P2 | Faits exacts mais aucun défaut présent : l'état transitoire est correct (le finding le concède), la reco se limite à des commentaires-marqueurs ; les 3 points d'ancrage sont déjà énumérés dans l'ADR et les docstrings des tests. Cosmétique. |
| A5-fix-2 | P1 | Différé explicitement décidé par ADR-0005 (« mise en œuvre outillage DIFFÉRÉE », « ne crée pas le dépôt, aucune incohérence introduite »). Ajouter PackageId/IsPackable MAINTENANT contredirait les gardes d'archi co-localisées. |
| A5-fix-3 | P1 | Le corpus golden v1 figé EXISTE et est rejoué des deux côtés en CI ; une rupture casse `FrozenHashes` bruyamment. Le contrat est consommé par référence (pas de `PackageVersion`) ; « lier ContractVersion à la version du paquet » vise un modèle de distribution inexistant. Résidu = rappel P3 d'un job N-1 nommé à v2. |
| X-down-4 | P3 | Confusion de preuve : F12-A marque `reportingFrequency` (non figée), PAS `operationCategory` (enum FIGÉE à 3 valeurs, source F01-F02, `OperationCategory.cs:9-19`). Reste une imprécision de référence triviale (§3 vs §2 pour `emitterSiren`). |

---

## Arbitrages humains (2) — inscrits, décision à acter au gate

- **A7-det-3** (dans RDL05) : normaliser en NFC dans le writer (défaut recommandé) vs invariant d'adaptateur. Décision à sourcer (règle n°2).
- **A6-runtime-1** (dans RDL07) : sémantique d'un run partiel + faut-il une règle d'alerte dédiée à l'ancrage/purge (le résidu non couvert par le dead-man's-switch existant). Décision produit/observabilité à acter dans ADR-0006.

Source brute (57 findings, file:line, preuves, verdicts adversariaux) : workflow `redline-adr-005-006-007`
(transcript de session). Ce document est la référence du lot RDL.
