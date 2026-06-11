# Stratégie de test Liakont

> Pyramide de tests, **taxonomie de référence des catégories xUnit** (l'autorité qui harmonise
> `verify-fast` vs `run-tests` vs `run-e2e` vs CI) et conventions de nommage.
> **Sources** (rien n'est inventé ici) : `blueprint.md` v2 (§9 stratégie de test, §10 pipeline),
> `docs/conception/F12-Architecture-Plateforme-Agent.md` (§3.4 contrat agent, §6.4 compatibilité),
> `docs/architecture/definition-of-done.md`, les scripts `tools/verify-fast.ps1`,
> `tools/run-tests.ps1`, `tools/run-e2e.ps1` (SOL05), décision **D3** (2026-06-03 : E2E en suite
> séparée). Les filtres cités sont ceux réellement appliqués par les scripts.
>
> Documents frères : [`repo-standards.md`](repo-standards.md),
> [`module-rules.md`](module-rules.md).

---

## 1. La pyramide de tests (`blueprint.md` §9)

| Niveau | Quoi | Outil | Quand (script) |
|---|---|---|---|
| **Architecture** | Frontières de modules, règles `module-rules.md` §6 | NetArchTest | `verify-fast` (chaque commit) |
| **Unit** | Handlers, domaine, composants | xUnit (+ bUnit pour Blazor) | `verify-fast` |
| **Unit agent** | Extraction fixtures, buffer, reprise | xUnit net48 (x86 en verify, x86+x64 en run-tests) | `verify-fast` / `run-tests` |
| **Integration** | PostgreSQL réel, modules bout en bout | xUnit + Testcontainers | `run-tests` |
| **Contrat agent** | Golden files du contrat (sérialisation identique des deux côtés) | xUnit (plateforme ET agent) | `run-tests` |
| **Contrat PA** | Suite commune sur chaque plug-in (mock HTTP) | xUnit | `run-tests` |
| **E2E** | Parcours navigateur (console web) | Playwright | `run-e2e` (suite dédiée) |
| **Staging / Sandbox** | Envois réels B2Brouter staging / Super PDP sandbox | Manuel | Avant chaque gate PA, **jamais en CI** |

---

## 2. Taxonomie de référence des catégories (autorité)

Deux **axes de catégorisation coexistent** et sont tous deux honorés par les scripts :

1. **Trait xUnit** `[Trait("Category", "<valeur>")]` — convention **Liakont** pour les tests propres.
2. **Convention de nommage du namespace** — convention **Stratum vendored** : les projets
   `*.Tests.Integration` / `*.Tests.Acceptance` portent les tests d'intégration / d'acceptation
   via fixtures `[Collection(...)]`, sans trait `Category`. Les scripts filtrent donc **aussi** par
   `FullyQualifiedName`.

### 2.1 Catégories normatives

| Catégorie | Marquage | `verify-fast` | `run-tests` | `run-e2e` | CI |
|---|---|:---:|:---:|:---:|:---:|
| **Unit / Architecture** | (aucun trait, ni `Tests.Integration`/`Tests.Acceptance`) | ✅ | ✅ | — | ✅ |
| **Integration** | `[Trait("Category","Integration")]` **ou** namespace `*.Tests.Integration` | ❌ | ✅ | — | ✅ (job plateforme avec PostgreSQL) |
| **Acceptance** | namespace `*.Tests.Acceptance` | ❌ | ✅ (plateforme) | — | ✅ |
| **E2E** | `[Trait("Category","E2E")]` | ❌ | ❌ | ✅ | job dédié / documenté (SOL03) |
| **Staging** | `[Trait("Category","Staging")]` (API réelle B2Brouter) | ❌ | ❌ | ❌ | ❌ (manuel, clé locale) |
| **Sandbox** | `[Trait("Category","Sandbox")]` (API réelle Super PDP) | ❌ | ❌ | ❌ | ❌ (manuel) |

### 2.2 Filtres `--filter` réellement appliqués

> Ces chaînes sont la source de vérité ; toute évolution de la taxonomie doit les mettre à jour de
> concert (sinon faux vert / faux négatif — `tasks/lessons.md` 2026-06-03).

- **`verify-fast` — plateforme** (`src/Liakont.sln`, build .NET 10) :
  ```
  Category!=Integration&Category!=Staging&Category!=Sandbox&Category!=E2E&FullyQualifiedName!~Tests.Integration&FullyQualifiedName!~Tests.Acceptance
  ```
- **`verify-fast` — agent** (`agent/Liakont.Agent.sln`, **x86 uniquement** ; x86 est la plateforme
  contraignante des drivers ODBC Pervasive 32-bit, x64 couvert par run-tests/CI) :
  ```
  Category!=Integration&Category!=Staging
  ```
- **`run-tests` — plateforme** (unit + integration Testcontainers + contrats) :
  ```
  Category!=Staging&Category!=Sandbox&Category!=E2E
  ```
- **`run-tests` — agent** (**x86 ET x64**, deux exécutions) :
  ```
  Category!=Staging
  ```
- **`run-e2e`** (livré par SOL05) : `Category=E2E` uniquement.

### 2.3 Garde anti-faux-vert (`run-tests.ps1`)

`run-tests` **compte les tests exécutés et ÉCHOUE si zéro test a tourné** pour une suite (filtre
erroné, tests non taggés, ou aucun projet de test dans la solution) — un PASS sans aucun test
exécuté est un faux vert. La garde se teste **dans les deux sens** : « échouer quand c'est cassé »
ET « ne pas échouer quand c'est sain » (y compris VSTest vs Microsoft.Testing.Platform, .NET 10 vs
net48) — `tasks/lessons.md` 2026-06-03.

---

## 3. Conventions de nommage des tests

- **Projets** : `<Cible>.Tests.Unit`, `<Cible>.Tests.Integration`, `<Cible>.Tests.Acceptance`
  (convention socle), ou `Liakont.Agent.Core.Tests` côté agent.
- **Classes** : `<SujetTesté>Tests` (ex. `ContractsPurityTests`, `AuditPolicyTests`).
- **Méthodes** : nom descriptif du cas (`Method_Scenario_Expected` ou phrase) ; les tests d'un
  invariant citent l'`INV-ID` dans `SCENARIOS.md` (voir `module-rules.md` §11).
- **Assertions** : FluentAssertions (présent des deux côtés), avec un message qui renvoie à la
  règle (ex. `… "(blueprint.md §6)"`).
- Les écarts de nommage xUnit sont tolérés par le `.editorconfig` dans les projets de test
  (`CA1707`, `SA1116/7`, `SA1202/10`, `SA1512` relâchés — voir `repo-standards.md` §2).

---

## 4. Tests d'architecture

- Package `NetArchTest.Rules` disponible (`Directory.Packages.props`).
- **Côté agent (existants)** : `ContractsPurityTests`, `AgentBoundaryTests`,
  `AgentProjectReferenceTests` (`agent/tests/Liakont.Agent.Core.Tests/`) — voir
  `module-rules.md` §4.
- **Côté plateforme** : la garde inter-modules (Contracts-only) et l'obligation documentaire par
  module (`MODULE.md`/`INVARIANTS.md`/`SCENARIOS.md`) sont héritées des `ModuleIsolationTests` du
  socle ; le harness du socle n'ayant pas été copié par le vendoring SOL01 (qui ne copie que
  `src/`), la garde plateforme est (re)mise en place avec le premier module métier Liakont. Les
  règles restent **opposables en review (P1)** entre-temps (voir `module-rules.md` §3, §11).

---

## 5. Contrat agent (golden files)

Les DTOs de `Liakont.Agent.Contracts` doivent se sérialiser **à l'identique** par Newtonsoft.Json
(agent net48) et System.Text.Json (plateforme .NET 10) — garanti par des **tests de contrat golden
files exécutés des DEUX côtés** (`F12` §3.4). Ordre de propriétés JSON canonique → hash de payload
reproductible (idempotence d'ingestion). La CI vérifie que les golden files du contrat **N-1**
passent toujours sur la plateforme **N** (`F12` §6.4 — matrice de compatibilité).

---

## 6. Contrat PA

Suite de contrat **commune** rejouée sur chaque plug-in PA (mock HTTP) : tout plug-in PA passe la
même suite (`blueprint.md` §2 règle 3). Le comportement attendu est piloté par les capacités
déclarées (`PaCapabilities`), jamais par un `if (pa is …)` (voir `module-rules.md` §5).

---

## 7. E2E (Playwright) — suite séparée

- **Décision D3** (2026-06-03) : les E2E sont une suite **séparée**, exécutée par
  `tools/run-e2e.ps1` (livré par **SOL05**), **jamais** par `run-tests.ps1` ni `verify-fast.ps1`.
- Catégorie `[Trait("Category","E2E")]`, projet `tests/Liakont.Tests.E2E` (dans `src/Liakont.sln`
  pour être compilé par `verify-fast`/`run-tests`, mais ses tests `E2E` y sont **exclus** par le
  filtre — seul `run-e2e.ps1` les exécute).
- Infra démarrée par Testcontainers (PostgreSQL `postgres:16-alpine` + Keycloak
  `quay.io/keycloak/keycloak:26.0`, realm `liakont-dev` seedé par SOL01) via la collection-fixture
  `KeycloakE2EWebFactory` ; navigateurs installés par `run-e2e.ps1` (`playwright install chromium`).
- `run-e2e.ps1` **échoue avec un message explicite** si Docker ou les navigateurs manquent —
  **jamais de skip silencieux** (un test E2E écrit mais jamais exécuté est un faux vert). Il porte la
  même garde anti-faux-vert que `run-tests` : un PASS qui n'exécute **aucun** test E2E est un échec.

**Comment lancer** (Docker requis) :

```powershell
powershell -ExecutionPolicy Bypass -File tools/run-e2e.ps1
# Un scénario ciblé (items blazor-page-item) :
powershell -ExecutionPolicy Bypass -File tools/run-e2e.ps1 -Filter "Category=E2E&FullyQualifiedName~LoginShell"
```

Le harness (`tests/Liakont.Tests.E2E`) fournit : `KeycloakE2EWebFactory` (app + PostgreSQL +
Keycloak), `PlaywrightFixture` (navigateur Chromium partagé), `KeycloakBaseE2ETest`
(helper `LoginViaKeycloakAsync` + capture d'écran à l'échec), les POM `KeycloakLoginPage` /
`ErpShellPage`, et un test de preuve (`LoginShellE2ETests` : login OIDC de l'utilisateur de rôle
`lecture` → shell `.erp-shell`). Chaque item `blazor-page-item` ajoute ses scénarios en héritant de
`KeycloakBaseE2ETest`. **Contrainte** : le username Keycloak doit être un identifiant court
(alphanumérique + underscores, 3-50 car. — INV-IDENTITY-007), pas un email ; le harness en tient
compte (handles `lecture`/`operateur`/… avec email en champ séparé).

- **Prérequis de tous les items `blazor-page-item`** (WEB01-09, SUP02, OPS03) : leur Definition of
  Done exige des tests bUnit **et** E2E Playwright écrits **et exécutés** (`definition-of-done.md`
  §blazor-page-item). Les anciennes checklists smoke manuelles sont supprimées (gain du pivot).

---

## 8. Suites réelles : Staging / Sandbox

Envois réels vers B2Brouter (Staging) et Super PDP (Sandbox) : **manuels**, exécutés **avant chaque
gate PA**, **jamais en CI** (clé/API réelle requise). Exclus par tous les scripts automatiques
(`Category=Staging` / `Category=Sandbox`).

### 8.1 Lancer la suite staging B2Brouter

La suite `B2BrouterStagingTests` (`[Trait("Category","Staging")]`, projet
`src/PaClients/Liakont.PaClients.B2Brouter.Tests.Unit`) envoie une facture fixture sur le compte
staging réel puis relit son statut, les tax reports et les informations de compte. Elle est exclue de
`verify-fast` ET de `run-tests` par leur filtre (`Category!=Staging`) : `run-tests.ps1` ne peut donc
**pas** la lancer — on l'exécute directement avec `dotnet test`.

**Prérequis** : un compte B2Brouter staging actif et sa clé API (jamais committée — CLAUDE.md n°10).
Deux variables d'environnement portent la configuration :

| Variable | Contenu |
|---|---|
| `B2BROUTER_STAGING_KEY` | Clé API du compte staging (en-tête `X-B2B-API-Key`) |
| `B2BROUTER_STAGING_ACCOUNT_ID` | Identifiant de compte (segment d'URL des endpoints) |

**Commande** (PowerShell) :

```powershell
$env:B2BROUTER_STAGING_KEY = "<clé staging — ne jamais committer>"
$env:B2BROUTER_STAGING_ACCOUNT_ID = "<account id staging>"
dotnet test src/PaClients/Liakont.PaClients.B2Brouter.Tests.Unit `
  --filter "Category=Staging"
```

Sans ces variables, la suite **échoue avec un message d'action explicite** (xUnit 2.9 n'a pas de skip
dynamique et CLAUDE.md interdit d'ajouter `Xunit.SkippableFact` sans ADR ; un `[Skip]` statique serait
un faux-vert — §9). C'est volontaire : la suite ne tourne que lancée délibérément par un opérateur,
avec la clé en place. URL staging = `api-staging.b2brouter.net` (PAS `app-staging` — F05 §2).

### 8.2 Lancer la suite sandbox Super PDP

La suite `SuperPdpSandboxTests` (`[Trait("Category","Sandbox")]`, projet
`src/PaClients/Liakont.PaClients.SuperPdp.Tests.Unit`) envoie une facture fixture (numéro unique) sur la
sandbox Super PDP réelle puis relit son statut. Elle est exclue de `verify-fast` ET de `run-tests` par leur
filtre (`Category!=Sandbox`) : on l'exécute directement avec `dotnet test`.

**Prérequis** : une sandbox Super PDP ouverte (action humaine DR17-A4) et ses identifiants OAuth 2.0
`client_credentials` (jamais committés — CLAUDE.md n°10). Différence avec B2Brouter (clé statique) : Super PDP
s'authentifie par un échange `POST <base>/oauth2/token` (`grant_type=client_credentials`) → jeton bearer
(F14 §3.1). Deux variables d'environnement portent la configuration :

| Variable | Contenu |
|---|---|
| `SUPERPDP_SANDBOX_CLIENT_ID` | Identifiant client OAuth de la sandbox |
| `SUPERPDP_SANDBOX_CLIENT_SECRET` | Secret client OAuth de la sandbox |

**Commande** (PowerShell) :

```powershell
$env:SUPERPDP_SANDBOX_CLIENT_ID = "<client id sandbox — ne jamais committer>"
$env:SUPERPDP_SANDBOX_CLIENT_SECRET = "<client secret sandbox — ne jamais committer>"
dotnet test src/PaClients/Liakont.PaClients.SuperPdp.Tests.Unit `
  --filter "Category=Sandbox"
```

Sans ces variables, la suite **échoue avec un message d'action explicite** (même règle anti-faux-vert que
§8.1 : pas de skip silencieux). C'est volontaire : la suite ne tourne que lancée délibérément par un
opérateur, identifiants en place. Base URL sandbox = `https://api.superpdp.tech` (token-endpoint
`<base>/oauth2/token`, confirmés par test réel le 2026-06-11 — F14 §12 O1).

---

## 9. Faux verts interdits (`blueprint.md` §9, `CLAUDE.md`)

Sont des **P1** en review : un test écrit mais jamais exécuté, une assertion affaiblie, un test
`[Skip]` non justifié, un script qui réussit quand il devrait échouer, une garde anti-faux-vert
non testée dans les deux sens. Règle d'or : pour chaque script de vérification, exécuter d'abord le
scénario « ça DOIT échouer » avant de s'y fier (`tasks/lessons.md` 2026-06-02 / 2026-06-03).

---

## 10. Obligation de test par type d'item (`definition-of-done.md`)

| Type d'item | Tests exigés (exécutés, pas seulement écrits) |
|---|---|
| `module-work-item` | Unit + Integration (`run-tests`), frontières (NetArchTest), trois fichiers de doc de module |
| `blazor-page-item` | bUnit (rendu par état/permission) **+** E2E Playwright via `run-e2e` ; aucune logique métier dans les pages |
| Item **agent** (lot AGT) | Build et tests **x86 ET x64** verts ; lecture seule source ; secrets DPAPI |
| `tooling-item` | Testé sur état **vide / sale / échec** (pas seulement le chemin nominal) ; échec ⇒ exit non-zéro |
| `docs-spec-item` | `verify-fast` vert (le build et les liens ne cassent pas) ; aucune règle inventée |

Le pipeline complet (`blueprint.md` §10) :
`code → verify-fast → run-tests (si applicable) → codex-review (rounds jusqu'à clean, -Base
obligatoire) → merge --no-ff dans la branche de segment → gate`.
