# Provenance du socle Stratum vendored

> **Règle non négociable (CLAUDE.md n.11)** : le socle Stratum vendored (`Stratum.*`) ne se
> modifie pas silencieusement. **Toute** modification d'un fichier `Stratum.*` (ou d'un fichier
> de configuration qui en conditionne la compilation) est consignée ici. Objectif : pouvoir
> re-converger vers des packages NuGet plus tard (option D de l'analyse d'impact).

## 1. Source copiée

| Champ | Valeur |
|---|---|
| Dépôt source | `C:\Source\Stratum` (Stratum ERP modulaire, .NET 10) |
| Commit source | `1454c7f1eec682aedf41105c1883b8bc058cde3a` |
| Date du commit source | 2026-04-20 |
| Branche source | `feat/reservation` |
| Date de la copie (vendoring) | 2026-06-03 (item SOL01, manifest v7) |
| Stratégie | Option C de `tasks/analyse-impact-pivot-plateforme.md` §6 (copie tracée, pas de NuGet) |
| Décision | `tasks/decisions.md` (2026-06-03), `docs/adr/ADR-0001-pivot-plateforme-agent.md` |

Les projets vendored **gardent leurs noms `Stratum.*`** (namespaces et assembly names) pour
préserver la provenance et permettre une re-convergence NuGet future. Seul le Host est renommé
(`Stratum.Host` → `Liakont.Host`), car c'est la racine de composition PROPRE à Liakont.

## 2. Périmètre copié (le « vendoring set »)

Copié tel quel (copie scriptée robocopy, hors `bin/`/`obj/`) depuis `Stratum/src` :

| Dossier source Stratum | Destination Liakont | Projets |
|---|---|---|
| `src/Common/Abstractions` | `src/Common/Abstractions` | Stratum.Common.Abstractions |
| `src/Common/Infrastructure` | `src/Common/Infrastructure` | Stratum.Common.Infrastructure (Dapper, tenancy, outbox, DbUp) |
| `src/Common/UI` | `src/Common/UI` | Stratum.Common.UI (Radzen, thème) |
| `src/Common/Testing` | `src/Common/Testing` | Stratum.Common.Testing (Testcontainers) |
| `src/Modules/Identity` | `src/Modules/Identity` | Stratum.Modules.Identity.* (auth/RBAC) |
| `src/Modules/Party/Contracts` | `src/Modules/Party/Contracts` | **Contracts UNIQUEMENT** (décision D1 : Identity.Application le référence en dur) |
| `src/Modules/Job` | `src/Modules/Job` | Stratum.Modules.Job.* (ordonnanceur) |
| `src/Modules/Notification` | `src/Modules/Notification` | Stratum.Modules.Notification.* (emails, clés API) |
| `src/Modules/Audit` | `src/Modules/Audit` | Stratum.Modules.Audit.* (journal technique) |
| `src/Host` | `src/Host/Liakont.Host` | Stratum.Host → **adapté** en Liakont.Host (voir §4.8) |

Tests vendored copiés (dans la solution `src/Liakont.sln`) : Common.Abstractions(Unit),
Common.Infrastructure(Unit + Integration), Common.UI(Unit), Job(Unit), Identity(Unit + Integration
+ Acceptance), Notification(Unit + Integration + Acceptance).

Config racine copiée/adaptée depuis Stratum : `Directory.Packages.props`, `Directory.Build.props`,
`global.json`, `.editorconfig` (voir §4.1). Infra de dev : `deploy/docker/keycloak/` +
`deploy/docker/docker-compose.keycloak.yml` (adaptés, voir §4.9).

### NON copié (volontairement)

- Les 15 autres modules ERP de Stratum : Party (hors Contracts), Sales, Finance, Reservation,
  Resource, Company, Workflow, FormEngine, Sequence, ReferenceData, Tax, Config, Showcase, Document.
  Liakont n'en a pas besoin et ils tirent des dépendances inter-modules. Le graphe de références
  du vendoring set a été vérifié **fermé** (aucune référence fuyante hors périmètre : seules
  `Identity.Application → Party.Contracts` et `Notification.Infrastructure → Job.Contracts`, toutes
  deux dans le périmètre).

## 3. Vérification de compilation

`src/Liakont.sln` (37 projets + `Liakont.Host`) compile en `dotnet build` avec **0 erreur / 0
avertissement** sous la config analyzer du socle (StyleCop + TreatWarningsAsErrors). Le périmètre
de review de SOL01 porte sur l'**adaptation** (Liakont.Host, csproj, provenance, ADR, neutralisations,
seam D10), PAS sur le code Stratum copié tel quel (déjà reviewé dans son dépôt d'origine).

## 4. Modifications locales du socle (consignation obligatoire)

### 4.1 `.editorconfig` — adoption de la config socle Stratum
Le `.editorconfig` racine v5 (placeholder, convention net48 `using outside_namespace`) a été
**remplacé par celui de Stratum** (`using inside_namespace`, suppressions StyleCop SA1633/SA1101/
SA0001/… requises par le code vendored). Sans cela le code copié ne compile pas sous sa propre
config analyzer (292 erreurs StyleCop au premier build). Le rappel Liakont « montants en decimal,
jamais float/double » est ajouté en fin de fichier. La convention net48 de l'agent (SOL02) sera
ré-introduite par un scope dédié à `agent/`.

### 4.2 Migrations Notification `V009`/`V010` — neutralisées
`V009__seed_reservation_templates.sql` et `V010__seed_reservation_routing_rules.sql` seedaient des
templates d'email et des règles de routage du module ERP **Reservation** (données métier municipales :
salles, voirie, police, emails `@commune.local`). Contenu remplacé par un no-op (`SELECT 1;` +
commentaire). **Non supprimées** pour préserver la séquence de versions DbUp (V011..V014 restent
ordonnées, le journal de migration reste continu). Règle CLAUDE.md n.7 (aucune donnée métier d'un
domaine client dans le code générique).

Conséquence de tests : 5 tests d'intégration vendored (`Notification.Tests.Integration`) asservissaient
la PRÉSENCE de ce seed global (`RoutingEngineIntegrationTests.Seeded_Data_Should_Be_Queryable…`,
`RoutingRuleIntegrationTests.{ListByEntityType_Should_Return_Seeded_Reservation_Rules,
GetByCode_Should_Return_Existing_Rule}`, `ServiceDefinitionIntegrationTests.{List_Should_Return_Seeded_Services,
GetByCode_Should_Return_Existing_Service}` — assertions `≥ 6 items`, `Code == "gestion-salles"`,
`@commune.local`). Leur sujet (le seed réservation) n'existe plus par conception : ils sont **retirés**.
Le dépôt routing-rule/service-definition reste couvert par les 33 autres tests du projet (qui INSÈRENT
leurs propres fixtures). Aucune assertion affaiblie, aucun `[Skip]`.

### 4.3 `ReflectionPermissionCatalog.cs` — découverte des permissions Liakont
`src/Modules/Identity/Infrastructure/Security/ReflectionPermissionCatalog.cs` ne scannait que les
assemblies `Stratum.*`. Deux modifications (marquées `// Liakont:` dans le code) :
- `Scan()` : le filtre d'assembly accepte désormais AUSSI le préfixe `Liakont.*`, afin de découvrir
  les permissions du produit consommateur (`LiakontPermissions` : `liakont.read/actions/settings/
  supervision`). Sans cela, les permissions Liakont ne seraient jamais cataloguées.
- `DeriveModuleName()` : un namespace `Liakont.*` (ex. `Liakont.Host.Security`) dérive le nom de
  module « Liakont » (le préfixe `Stratum.Modules.` ne s'y applique pas).

### 4.4 Tests vendored pré-existants ROUGES en amont (vérifié sur pièce)
Constat (leçon « vérifier le socle sur pièce, pas sur réputation ») : au commit source `1454c7f`,
certains tests du socle échouent DÉJÀ dans Stratum. Vérifié en exécutant les tests dans
`C:\Source\Stratum` même. Traitement :

- **2 tests unit** (`Common.Infrastructure.Tests.Unit`) — **corrigés** (bugs de TEST, production
  vérifiée correcte) :
  - `PostgresHealthCheckTests.CheckHealthAsync_Should_ReturnHealthy_When_ConnectionSucceeds` : le
    fake n'implémentait que `IDbConnection`, incompatible avec le chemin async de Dapper (`DbConnection`
    requis). Fake réécrit sur `System.Data.Common.DbConnection`/`DbCommand`.
  - `ErrorHandlingMiddlewareTests.InvokeAsync_Should_Propagate_When_ResponseHasStarted` : le
    `DefaultHttpContext` ne rendait pas `Response.HasStarted == true` après `StartAsync()` ; ajout
    d'un `IHttpResponseFeature` qui force `HasStarted`. Le middleware (`catch when (!HasStarted)`)
    est correct.
- **5 tests** `Job.Tests.Integration` (`JobWorkerIntegrationTests`, `JobUnitOfWorkTests`) — **carvés**
  (projet retiré de la solution + dossier supprimé). Flaky/timing en amont : le helper `RunWorkerOnce`
  attend `Task.Delay(500ms)` alors que `JobWorkerOptions.PollingInterval = 2s` → 7 passent / 5 échouent
  selon l'ordonnancement. Défaut de test vendored, pas de production. Le module Job **production** est
  vendored tel quel (ses tests unit passent : 48/48) ; la mécanique de jobs multi-tenant de Liakont
  et ses tests arrivent en **SOL06**.

### 4.5 `Audit.Tests.Unit` + `Audit.Tests.Integration` — NON vendorés
Ces deux projets sont **absents de `Stratum.slnx`** (jamais compilés/exécutés par la CI Stratum,
driftés en amont : `ActivityLoggerTests` utilise une signature `IConnectionFactory` obsolète vs
`ISystemConnectionFactory`). Copiés par mégarde avec le module Audit entier, puis supprimés. Le
module Audit **production** est vendored et compile.

### 4.6 Filtre des tests d'intégration (`tools/verify-fast.ps1`)
Les tests d'intégration vendored utilisent la convention Stratum (`[Collection(...)]` + nom de
projet `*.Tests.Integration`), PAS un trait `[Trait("Category","Integration")]`. Le filtre unit de
verify-fast (`Category!=Integration`) ne les excluait donc pas. Ajout de `&FullyQualifiedName!~Tests.Integration`
pour réaliser l'intention documentée (« l'intégration tourne dans run-tests.ps1 »). Les tests
d'intégration (Identity/Notification/Common.Infrastructure) tournent et **passent** via `run-tests.ps1`
(Testcontainers PostgreSQL).

### 4.7 Secrets réels purgés de `appsettings.Development.json`
Le `appsettings.Development.json` copié contenait des **secrets tiers RÉELS** de Stratum : une clé
API OpenRouter (`sk-or-v1-…`) et un token GitHub (`ghp_…`) dans une section `BugCapture`. Section
**supprimée intégralement** (feature Stratum hors périmètre Liakont + violation P1 secret-en-clair,
CLAUDE.md n.10/n.18). Les autres appsettings rebrandés `liakont` ; aucun secret de production versionné
(ils viennent des variables d'environnement — F12 §6.1).

### 4.8 Host — adaptation `Stratum.Host` → `Liakont.Host`
Copie ADAPTÉE (l'analyse d'impact §5 prévoit la divergence). Namespace renommé `Stratum.Host`
→ `Liakont.Host` ; `AssemblyName`/`RootNamespace` = `Liakont.Host`. N'enregistre QUE les 4 modules
vendored (Identity, Job, Notification, Audit). Retirés : les 15 modules ERP (ProjectReferences,
`Add*Module`, NavSectionProviders, endpoints, job handlers, assemblies Blazor), les features ERP du
Host (CsvImport, Portal public Showcase, pages `Components/Pages/Agent` et `Pages/Public`,
`AgentNavSectionProvider`, `AdminUserSeeder` réduit aux permissions des 4 modules). Branding
« Stratum ERP » → « Liakont ». **Seam d'IdP D10** : l'ENREGISTREMENT et la VALIDATION du pipeline d'authentification sont consommés
derrière `IIdentityProviderAuthenticator` (impl `KeycloakIdentityProviderAuthenticator`, sélectionnée
par un registre de providers) — toute la configuration OIDC/JwtBearer/cookie/JWKS Keycloak-spécifique
vit dans `src/Host/Liakont.Host/Security/Keycloak/`. **Limite connue (suivi)** : deux résidus
Keycloak-spécifiques subsistent encore dans le composition root (`AppBootstrap`) — la dérivation
d'autorité de realm (`…/realms/{realm}`) du seeding `SeedRealmRegistryFromDatabaseAsync`, et les
endpoints `/auth/oidc-login` /`/auth/oidc-logout` gardés par `KeycloakSettings`. Brancher une
alternative (OpenIddict) exigerait donc encore d'adapter ces deux points : leur extraction derrière
l'abstraction (méthodes `ConfigureEndpoints`/`SeedRegistry` sur `IIdentityProviderAuthenticator`) est
un suivi du segment plateforme. Le seam établi rend le gros du basculement (pipeline d'auth) déjà
swappable sans toucher au métier.

### 4.9 Infra de dev — realm Keycloak + docker-compose
`deploy/docker/keycloak/realm-export.json` adapté : realm `liakont-dev`, client `liakont`, rôles
standard (lecture, opérateur, paramétrage, superviseur), un utilisateur de test par rôle. Réalms
Stratum `enterprise`/`association` et fournisseurs sociaux retirés. `docker-compose.keycloak.yml`
rebrandé `liakont` ; le chemin du volume d'import du realm a été corrigé en relatif au nouvel
emplacement (`./keycloak/realm-export.json`, le compose vivant désormais dans `deploy/docker/`).
Détails : `docs/architecture/identity-permissions-liakont.md`.

### 4.10 `NullPartyQueries` — shim DI pour le module Party non vendoré
`Identity.Infrastructure` (`CreateUserHandler`) dépend par injection de `IPartyQueries`
(`Party.Contracts`), dont l'IMPLÉMENTATION vit dans `Party.Infrastructure` — non vendoré (décision
D1 : seul `Party.Contracts`). Le boot du Host en Development (`ValidateOnBuild`) échouait donc sur
la résolution de `IPartyQueries`. `CreateUserHandler` n'appelle ces requêtes que si
`request.PartyId.HasValue` ; Liakont ne lie pas ses utilisateurs à des « Party » ERP (PartyId
toujours null). Ajout d'un shim no-op `src/Host/Liakont.Host/Compatibility/NullPartyQueries.cs`
(retours null/vides) enregistré dans `AppBootstrap`. Sans cela il aurait fallu tirer
`Party.Infrastructure` (qui dépend de modules non vendorés). **Boot vérifié** : le Host démarre en
dev (PostgreSQL + Keycloak `liakont-dev`), applique les migrations DbUp et répond `Healthy` sur
`/health`.

### 4.11 `tools/run-tests.ps1` — locale CLI forcée en anglais
Le garde anti-faux-vert de run-tests compte les tests via des regex sur le résumé `dotnet test`.
Sur un Windows FR, ce résumé est en français (« Réussi! … total : N ») qu'aucune regex anglaise ne
matche → faux « 0 test / format non reconnu ». Ajout de `$env:DOTNET_CLI_UI_LANGUAGE = 'en'` en
tête de run-tests.ps1 pour rendre le parsing indépendant de la locale (le garde reste intact).

### 4.12 `tools/socle-provenance-check.ps1` + `tools/socle-baseline.sha1` — garde automatique (SOL03)
La règle « le socle vendored ne se modifie pas silencieusement » (CLAUDE.md n.11) est désormais
**vérifiée automatiquement**. `tools/socle-baseline.sha1` épingle le hash de blob git
(`git hash-object`, indépendant de la plateforme) de chacun des 1226 fichiers vendored
(`src/Common` + `src/Modules/{Identity,Party,Job,Notification,Audit}` ; le Host adapté `Liakont.Host`
est exclu — code Liakont, pas `Stratum.*`). À chaque `verify-fast`, le script recalcule les hashes :
tout fichier épinglé qui a dérivé du baseline (modifié ou supprimé) DOIT figurer, **par son chemin
repo-relatif EXACT**, dans le bloc de consignation balisé ci-dessous (`SOCLE-CONSIGNED-DRIFT`), sinon
`verify-fast` échoue (exit 2).

**Matching par chemin exact, jamais par nom de fichier** (correctif review SOL03 round 1, P1) : les
noms de fichiers vendored sont très souvent en collision (`ServiceCollectionExtensions.cs` ×14,
`_Imports.razor` ×6, `MODULE.md`/`INVARIANTS.md`/`SCENARIOS.md` ×4 chacun, `NullPartyQueries.cs`,
`AssemblyInfo.cs`…). Un match par leaf ou par sous-chaîne laisserait passer une modification
silencieuse du fichier B dès qu'un homonyme A est consigné — exactement le faux-vert corrigé ici.

Workflow d'une modification légitime future d'un fichier `Stratum.*` : (1) éditer le fichier,
(2) ajouter une sous-section 4.x narrative ci-dessus, (3) ajouter son **chemin repo-relatif exact**
dans le bloc `SOCLE-CONSIGNED-DRIFT` ci-dessous. (Optionnel : régénérer le baseline avec
`tools/socle-provenance-check.ps1 -Generate` pour « cuire » le nouvel état — il ne dérive alors plus
et peut être retiré du bloc.) Limite assumée : un fichier AJOUTÉ sous un dossier vendored (ex.
mécanique multi-tenant Liakont de SOL06) n'est pas épinglé par le baseline — la garde cible la
modification/suppression silencieuse d'un fichier vendored existant, exactement la règle n.11.

Le baseline a été généré sur l'état consigné courant (vendoring SOL01 + les modifications des sections
4.1–4.11 ci-dessus) : ces modifications sont déjà **incorporées** dans le baseline et ne dérivent donc
pas — le bloc de consignation est par conséquent **vide** tant qu'aucune dérive POST-baseline n'a été
introduite.

Format du bloc : un chemin repo-relatif EXACT par ligne (ex. `src/Modules/Identity/Infrastructure/
Security/ReflectionPermissionCatalog.cs`). Les lignes vides et les lignes de commentaire (`<!--`,
`#`, `//`) sont ignorées par le parseur. Ne jamais mettre un nom de fichier seul : le matching est
ancré sur le chemin complet.

<!-- SOCLE-CONSIGNED-DRIFT:START -->
<!-- SOCLE-CONSIGNED-DRIFT:END -->

### 4.13 Harness E2E — adapté de `Stratum.Tests.E2E` (SOL05)
Le harness de tests E2E (`tests/Liakont.Tests.E2E`) est **adapté** du harness Stratum
`tests/Stratum.Tests.E2E` — **hors périmètre du vendoring SOL01** (qui ne copie que `src/`), d'où
sa consignation ici (adaptation, pas copie brute). Le nouveau projet porte le namespace
**`Liakont.Tests.E2E`** (code Liakont, pas `Stratum.*`). Infrastructure reprise et adaptée :
`KeycloakE2EWebFactory` (démarre `Liakont.Host` sur PostgreSQL `postgres:16-alpine` + Keycloak
`quay.io/keycloak/keycloak:26.0` via Testcontainers, ports dynamiques), `PlaywrightFixture`,
`KeycloakE2ECollection`, `KeycloakBaseE2ETest`, `E2EAuthenticationStateProvider` (pont SSR↔circuit),
`Pages/KeycloakLoginPage`. **Retiré du portage** (spécifique ERP Stratum, hors périmètre Liakont) :
la configuration `BugCapture` + `MockGitHubHandler`/`GitHubIssueReporter`, et les ~130 Page Objects /
Scenarios ERP. **Adapté aux clés réelles du Host Liakont** : `Database:ConnectionString` +
`TenantConnections:ConnectionStrings:default` (même base — le tenant `default` partage la base
système, cohérent avec `appsettings.Development.json`), `Keycloak:{Authority,ClientId,ClientSecret,
UseKeycloak,RealmTenantMap}`. Fixture realm `Fixtures/keycloak-e2e-realm.json` dérivée du realm dev
`deploy/docker/keycloak/realm-export.json` (realm `liakont-dev`, client `liakont`, 4 rôles, 4
utilisateurs de test SOL01) avec redirect URIs en joker de port (substitués au runtime). Le projet
est dans `src/Liakont.sln` (compilé par `verify-fast`/`run-tests`) mais ses tests `Category=E2E` y
sont exclus ; seul `tools/run-e2e.ps1` (livré par SOL05) les exécute. POM `ErpShellPage` et test de
preuve `LoginShellE2ETests` = code Liakont neuf (pas d'origine Stratum).

**Écart assumé vs realm dev + défaut signalé (hors périmètre SOL05).** Dans la fixture E2E, le
`username` Keycloak des utilisateurs de test est un identifiant court (`lecture`, `operateur`,
`parametrage`, `superviseur`) avec l'email en champ séparé — alors que le realm dev SOL01 fixe
`username = email` (`lecture@liakont.local`). Raison : le sync OIDC (`UserSyncService.SyncFromOidcClaimsAsync`,
vendored) prend le claim `preferred_username` **brut** et ne le passe PAS par `SanitizeUsername`
(qui n'est appliqué qu'au *fallback* email) ; le value object `Username` rejette alors un email
(« 3-50 car. alphanumériques + underscores », INV-IDENTITY-007). **Conséquence : le login OIDC
échoue avec le realm dev tel quel** (jamais exercé par SOL01 qui ne testait que `/health`). C'est un
**défaut réel à corriger hors SOL05** (item dédié) : soit `UserSyncService` sanitise aussi
`preferred_username`, soit le realm dev passe à des usernames courts — décision plateforme touchant
le module Identity vendoré. La fixture E2E à handles est compatible avec les deux résolutions.

## 5. ADR du socle hérités

Les ADR Stratum pertinents au socle sont copiés dans `docs/adr/socle/` (référence, non re-décidés).
La collision de numéro 0010 (deux ADR Stratum) est résolue en n'important que `ADR-0010-github-issue-reporter`
(l'autre, `multi-tenant-strategy` / schema-per-tenant, est superseded par `ADR-0011-database-per-tenant`).
La numérotation ADR PROPRE à Liakont vit dans `docs/adr/` (racine) : `ADR-0001-pivot-plateforme-agent`.

## 6. Re-convergence future (option D)

Quand les besoins socle de Liakont seront stabilisés, les modifications ci-dessus (notamment §4.3
`ReflectionPermissionCatalog`, §4.4 corrections de tests) sont les candidates à reverser dans Stratum
pour permettre un retour à des packages NuGet. Toute nouvelle modification d'un fichier `Stratum.*`
DOIT être ajoutée à la §4 le jour même.
