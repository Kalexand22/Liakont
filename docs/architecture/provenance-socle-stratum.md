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
src/Common/UI/Models/BulkActionConfig.cs
src/Common/UI/Components/DeclaredListPage.razor.cs
src/Common/Infrastructure/BugCapture/VideoAnalysisService.cs
src/Common/UI/Services/BugCapture/BugCaptureService.cs
src/Common/UI/Components/StratumDataGrid.razor
src/Common/UI/Components/DeclaredListPage.razor
src/Common/UI/Components/StratumButton.razor
src/Common/UI/Components/GlobalShortcutHandler.razor
src/Common/UI/Resources/SharedResources.resx
src/Common/UI/Resources/SharedResources.fr.resx
src/Modules/Job/Application/IScheduleUnitOfWork.cs
src/Modules/Job/Infrastructure/PostgresScheduleUnitOfWork.cs
src/Modules/Job/Infrastructure/JobHandlerRegistration.cs
src/Modules/Job/Infrastructure/JobHandlerRegistrationExtensions.cs
src/Modules/Job/Infrastructure/JobModuleRegistration.cs
src/Modules/Job/Web/Pages/AdminJobScheduleForm.razor
src/Modules/Job/Web/Pages/AdminJobSchedules.razor
src/Modules/Job/Web/JobNavSectionProvider.cs
src/Modules/Job/Infrastructure/JobHandlerResolver.cs
src/Modules/Job/Contracts/Queries/IJobQueries.cs
src/Modules/Job/Infrastructure/Queries/PostgresJobQueries.cs
src/Common/Infrastructure/Database/TenantProvisioningService.cs
src/Common/Infrastructure/Keycloak/KeycloakRealmProvisioner.cs
src/Common/Abstractions/MultiTenancy/KeycloakRealmProvisionRequest.cs
src/Common/Abstractions/MultiTenancy/TenantProvisionResult.cs
src/Common/Infrastructure/Database/ServiceCollectionExtensions.cs
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

### 4.14 Mécanique de jobs multi-tenant Liakont (SOL06) — fichiers AJOUTÉS aux projets Common vendored
SOL06 ajoute la mécanique `TenantJobRunner` (jobs multi-tenant — voir
`docs/adr/ADR-0006-mecanique-jobs-multi-tenant.md` et `docs/architecture/tenant-jobs.md`) **sous les
dossiers vendored `src/Common`**, conformément au choix de placement de l'ADR-0006 (réutilisable par
tous les modules sans dépendance circulaire). Ce sont des **fichiers AJOUTÉS**, pas des modifications
de fichiers `Stratum.*` existants : le baseline de provenance (§4.12) n'épingle que les fichiers
vendorés existants, donc ces ajouts **ne dérivent pas** et ne figurent PAS dans le bloc
`SOCLE-CONSIGNED-DRIFT` (réservé aux fichiers épinglés modifiés/supprimés). Ils sont consignés ici
pour la re-convergence NuGet et marqués `// Liakont addition (SOL06)` en tête de fichier.

Fichiers ajoutés (namespaces cohérents avec l'assembly hôte ; code **Liakont**, pas Stratum amont) :
- `src/Common/Abstractions/Jobs/` : `ITenantJob.cs`, `TenantJobContext.cs`, `ITenantJobRunner.cs`,
  `TenantJobRunSummary.cs`, `TenantJobFailure.cs` (projet `Stratum.Common.Abstractions`)
- `src/Common/Abstractions/MultiTenancy/` : `ITenantScope.cs`, `ITenantScopeFactory.cs` (seam de
  basculement de tenant, implémenté côté Host)
- `src/Common/Infrastructure/Jobs/` : `TenantJobRunner.cs`, `ServiceCollectionExtensions.cs`
  (`AddTenantJobs()`) (projet `Stratum.Common.Infrastructure`)

Code Liakont **hors** socle (pas de consignation requise, mais cité pour le contexte) :
`src/Host/Liakont.Host/MultiTenancy/TenantScopeFactory.cs` (implémentation du seam, positionne le
`MutableTenantContext` interne au Host) + son enregistrement dans `MultiTenantServiceCollectionExtensions`
et `AppBootstrap`. **Aucun fichier `Stratum.*` existant n'a été modifié par SOL06.**

### 4.15 `BulkActionConfig` + `DeclaredListPage.ExecuteBulkAsync` — option `SuppressSuccessToast` (WEB05)
La barre d'actions groupées de `DeclaredListPage` (`ExecuteBulkAsync`) affichait un toast de succès
**inconditionnel** (`"{N} élément(s) traité(s)."`) dès que le rappel `Execute` retournait sans exception.
WEB05 (« Envoyer la sélection ») doit, comme « Tout envoyer », passer par une **confirmation explicite**
avant un envoi fiscal IRRÉVERSIBLE (acceptance WEB05 : « Envoi sélection ET tout-envoyer avec confirmation »).
Or l'action groupée n'EXÉCUTE pas l'envoi : elle ouvre la confirmation — un toast « traité(s) » au retour
d'`Execute` serait donc trompeur (rien n'est encore envoyé), exactement le défaut d'ambiguïté opérateur que
proscrit CLAUDE.md n°12 (produit de conformité fiscale).

Deux modifications **rétro-compatibles** (marquées par le commentaire de garde dans le code) :
- `src/Common/UI/Models/BulkActionConfig.cs` : ajout d'un paramètre optionnel `bool SuppressSuccessToast = false`
  (dernier paramètre positionnel du record ; valeur par défaut = comportement inchangé pour toutes les actions
  groupées existantes).
- `src/Common/UI/Components/DeclaredListPage.razor.cs` (`ExecuteBulkAsync`) : le toast de succès n'est affiché
  que si `!action.SuppressSuccessToast`. Le reste du flux (gestion d'erreur via `_bulkError`, désélection,
  rechargement) est inchangé.

Capacité GÉNÉRIQUE du design-system (toute action groupée différée à une confirmation en bénéficie), candidate
à reverser en amont (§6). La garde est un simple test booléen autour de l'appel `ToastService.Show` existant ;
aucun test socle dédié n'est ajouté car l'exercer exigerait de simuler la sélection de lignes de la grille Radzen,
qui dépend d'un JS interop indisponible en bUnit (`JSRuntimeMode.Loose`) — même contrainte que le câblage
« Envoyer la sélection » de la page, couvert côté Liakont par un test bUnit invoquant directement le rappel
`Execute` (`Liakont.Host.Tests.Unit.Pages.DocumentsTests`).

### 4.16 `VideoAnalysisService.BuildPrompt` — la narration audio devient la source principale du rapport
Le prompt d'analyse vidéo BugCapture (fr et en) demandait un rapport QA basé sur ce qui est
**visible à l'écran**, sans jamais mentionner la piste audio. Or l'usage réel de l'outil est la
**dictée** : l'opérateur narre le problème au micro pendant la capture. Vérifié sur pièce
(2026-06-10, test GATE_CONSOLE_WEB) : Gemini reçoit bien la piste audio via OpenRouter (il la
transcrit mot à mot sur demande explicite), mais avec le prompt d'origine il décrit l'écran et
ignore la narration → rapport sans rapport avec le bug dicté. Défaut présent à l'identique dans
Stratum amont (fichier vendored == fichier source au moment du fix).

Modification (marquée `// Liakont:` dans le code) : les deux variantes du prompt (fr/en) de
`BuildPrompt` instruisent désormais que la narration audio, si présente, est la source PRINCIPALE
du titre/résumé/étapes, la vidéo servant d'illustration. Aucun changement de signature, de parsing
(`ParseResponse`) ni de format JSON attendu. Candidate à reverser en amont (§6).

**Itération 2 (même jour) — transcription Whisper en deux passes.** L'instruction de prompt seule
ne suffit pas : rejoué 3× sur la même capture réelle, le modèle n'exploite l'audio qu'1 fois sur 3
et HALLUCINE sinon un bug générique (« connexion impossible ») sans rapport avec la dictée. Whisper,
lui, transcrit la même piste mot à mot à chaque essai (l'API accepte le conteneur WebM vidéo tel
quel, vérifié avec le content-type `audio/webm` déjà émis par `TranscriptionService` — inchangé).
Fix en deux passes (marqué `// Liakont:`) :
- `VideoAnalysisService.AnalyzeAsync` : paramètre optionnel `transcript` (défaut `null`, rétro-
  compatible) ; `BuildPrompt` ajoute en fin de prompt la transcription comme narration de référence.
- `src/Common/UI/Services/BugCapture/BugCaptureService.cs` (`BuildExpensiveDataAsync`) : quand il
  n'y a pas d'enregistrement audio séparé et que la vidéo ≤ 25 Mo (limite d'upload Whisper), la
  piste audio de la vidéo est transcrite via `TranscriptionService` et passée à `AnalyzeAsync`.
  Effet de bord voulu : la transcription alimente aussi `_cachedTranscription`, donc le verbatim
  de la dictée apparaît dans la description du rapport (`[Transcription] …`) même si l'analyse
  vidéo échoue. Sans `WhisperApiKey`, `TranscribeAsync` retourne `string.Empty` : comportement
  strictement identique à l'amont (dégradation silencieuse préservée).

Suites de review (round 1, 3 P2) :
- **Décision opérateur (Karl, 2026-06-10) — OpenAI actée comme second sous-traitant BugCapture** :
  la 2e passe envoie le conteneur WebM vidéo COMPLET (frames d'écran incluses) à l'API Whisper
  d'OpenAI, alors que l'écran ne partait auparavant que vers OpenRouter. Accepté : BugCapture est
  un outil de QA/dev à clés optionnelles fournies par l'opérateur — configurer `WhisperApiKey`
  vaut acceptation qu'OpenAI reçoive le contenu des captures. Alternative « extraire la piste
  audio seule » écartée (parsing Matroska ou dépendance ffmpeg = nouveau package = ADR).
- Skip de la transcription au-delà de la limite d'upload Whisper (25 Mo) désormais TRACÉ
  (`ILogger<BugCaptureService>` ajouté au constructeur, `LogWarning` avec la taille) — plus de
  dégradation invisible vers l'analyse multimodale seule.
- Tests AJOUTÉS (pas de dérive baseline, même logique que §4.14) :
  `tests/Common.Infrastructure.Tests.Unit/BugCapture/VideoAnalysisServiceTests.cs` — le transcript
  fourni atterrit dans le corps de la requête OpenRouter (fr/en), absent si non fourni, aucun
  appel HTTP sans clé. Le garde 25 Mo de `BugCaptureService` n'a pas de test dédié : l'exercer
  exige d'instancier les 13 dépendances et de piloter une session de capture complète — même
  arbitrage que §4.15 (mocking lourd disproportionné), le branchement étant un simple test de
  taille désormais journalisé.

### 4.17 Barre d'outils des listes — Rafraîchir + bouton Export unique (FIX206)

La recette humaine GATE_CONSOLE_WEB (run 2, 2026-06-11) a relevé deux défauts du gabarit commun
des listes (`DeclaredListPage` → `StratumDataGrid`) : aucun bouton de rafraîchissement (le pipeline
étant asynchrone — CHECK event-driven, envoi par job — l'opérateur n'avait que F5 pour voir un
changement d'état), et **trois icônes d'export identiques** (`Icon="download"`, sans libellé) pour
CSV/Excel/PDF, indistinguables. Décision opérateur E3 : une icône **Rafraîchir** à côté des exports
(relançant la requête serveur en CONSERVANT filtres/tri/pagination) et **un seul bouton « Exporter »**
ouvrant un menu CSV/Excel/PDF. Correctif porté sur le gabarit COMMUN pour couvrir toutes les listes
d'un coup. Modifications **rétro-compatibles** (marquées par leurs commentaires de garde / `FIX206`) :

- `src/Common/UI/Components/StratumDataGrid.razor` :
  - les **trois** `StratumButton` d'export identiques (`export-csv-btn`/`export-excel-btn`/`export-pdf-btn`)
    sont remplacés par **un** `StratumSplitButton` « Exporter » (`export-btn`) dont le menu liste les
    formats déclarés par `ExportFormats`. Le **comportement d'export est inchangé** : le bouton primaire
    exporte le premier format activé, chaque item du menu appelle le même `HandleExportAsync(format)`
    qu'avant (aucun format ajouté/retiré, Excel reste no-op sans `OnExport` custom, exactement comme
    le bouton Excel d'origine).
  - nouveau paramètre **optionnel** `EventCallback OnRefresh` : quand un délégué est fourni, une icône
    `refresh` (`refresh-btn`) est rendue dans la barre d'outils ; sinon rien (rétro-compatible — aucune
    liste existante n'affiche le bouton sans câblage explicite). La condition d'affichage de la barre
    d'outils inclut désormais `OnRefresh.HasDelegate`.
- `src/Common/UI/Components/DeclaredListPage.razor` : les trois usages de `StratumDataGrid` (vue
  multi-modes + deux vues simples) câblent `OnRefresh="LoadAsync"`. `LoadAsync` ré-exécute le rappel
  `LoadItems` (requête serveur) puis `ApplyFilters()`, qui ré-applique l'état `_filterState`/tri/page
  EXISTANT → filtres et pagination conservés (acceptance FIX206). `DeclaredListPage.razor.cs` n'est PAS
  modifié par FIX206 (il figure déjà dans le bloc de dérive depuis §4.15).
- `src/Common/UI/Components/StratumButton.razor` : ajout d'un paramètre **optionnel** `Title` (mappé
  sur les attributs `title` + `aria-label`) pour donner un nom accessible / une infobulle aux boutons
  icône seule — utilisé par l'icône Rafraîchir. Aucun impact sur les boutons existants (défaut `null`).
- `src/Common/UI/Resources/SharedResources.resx` + `SharedResources.fr.resx` : deux clés
  `Grid_ExportButton` (Export / Exporter) et `Grid_RefreshButton` (Refresh / Rafraîchir). Les libellés
  de format du menu (CSV/Excel/PDF) sont des littéraux (acronymes universels, non traduits).

Tests : `tests/Common.UI/Unit/StratumDataGridTests.cs` (projet de test, **non épinglé** par le baseline
§4.12 — sous `tests/`, pas `src/`) — les tests d'export sont mis à jour vers le bouton unique (`export-btn`,
menu CSV/Excel/PDF, désactivation chargement/vide, export par défaut CSV) et trois tests Rafraîchir sont
ajoutés (rendu conditionnel au délégué, invocation du rappel). Capacité GÉNÉRIQUE du design-system,
candidate à reverser en amont (§6).
### 4.18 `IScheduleUnitOfWork.GetActiveJobTypesAsync` — lecture des jobs planifiés (FIX203b)
La recette run 2 (2026-06-11) a révélé que `job.schedules` reste VIDE après un bring-up complet :
le dead-man's-switch de supervision (15 min, F12 §5.1) et l'ancrage quotidien du coffre (TRK06,
ADR-0011) ne sont JAMAIS planifiés → supervision morte en silence, coffre jamais ancré. FIX203b
amorce ces planifications en dev (`DevJobScheduleSeeder`, Host) ET ajoute un diagnostic de démarrage
(`SystemJobScheduleHealthCheck`, Host) qui AVERTIT, en dev comme en prod, si un job SYSTÈME attendu
n'a aucun schedule actif (même pattern que `DevRealmHealthCheck`).

Ce diagnostic doit lire les `job_type` ayant au moins un schedule actif. Aucune méthode read-only
n'existait : `GetDueSchedulesAsync` pose un `FOR UPDATE SKIP LOCKED` (réservé au scheduler) et
`ExistsByNameAndCompanyAsync` exige le couple (nom, company) — inconnu en prod (l'opérateur nomme et
scope librement). Deux modifications **additives, lecture seule** (marquées `// Liakont addition (FIX203b)`) :
- `src/Modules/Job/Application/IScheduleUnitOfWork.cs` : ajout de
  `Task<IReadOnlyList<string>> GetActiveJobTypesAsync(CancellationToken)`.
- `src/Modules/Job/Infrastructure/PostgresScheduleUnitOfWork.cs` : implémentation
  (`SELECT DISTINCT job_type FROM job.schedules WHERE is_active = true`, sans verrou).

Aucune signature existante modifiée, aucun comportement du scheduler changé. Candidate à reverser en
amont (§6). Le diagnostic lit les types système au niveau instance (table `job.schedules` de la base
SYSTÈME, comme le scheduler lui-même) — ce n'est pas une requête métier tenant-scopée (CLAUDE.md n°9) :
elle ne retourne que des noms de types techniques, aucune donnée de tenant.

### 4.19 Barre de sélection — actions GLOBALES (sans sélection) déclarées par le module (FIX207)

La recette humaine GATE_CONSOLE_WEB (run 2, 2026-06-11, décision opérateur E4) demande des actions en
masse « Revérifier la sélection » / « Revérifier tout » portées par la **barre de sélection** Stratum
(overlay fixé en bas d'écran de `DeclaredListPage`). Le point d'extension déclaratif existait déjà
(`BulkActions` = liste de `BulkActionConfig`, §4.15), mais il était entièrement **sélection-scopé** : la
barre ne s'affiche qu'avec une sélection et chaque action ne reçoit que les lignes sélectionnées. « Revérifier
tout » (tous les bloqués du périmètre courant) n'a, lui, **pas besoin de sélection**. Modifications
**rétro-compatibles** (marquées `FIX207`) :

- `src/Common/UI/Models/BulkActionConfig.cs` : ajout d'un paramètre **optionnel** `bool RequiresSelection = true`
  (dernier paramètre positionnel du record ; valeur par défaut = comportement INCHANGÉ pour toutes les actions
  groupées existantes). `false` = action **globale** (reste disponible sans sélection).
- `src/Common/UI/Components/DeclaredListPage.razor` : la barre de sélection s'affiche désormais quand il y a une
  sélection **OU** au moins une action globale déclarée (`HasGlobalBulkActions`) ; les actions sélection-scopées
  (défaut) ne sont rendues qu'avec une sélection (skip par `continue`), les actions globales sont toujours rendues ;
  le compteur « N élément(s) sélectionné(s) » et « Tout désélectionner » ne s'affichent qu'avec une sélection.
- `src/Common/UI/Components/DeclaredListPage.razor.cs` : ajout de la propriété privée `HasGlobalBulkActions`
  (vrai s'il existe une action `RequiresSelection = false`). Aucune autre logique modifiée — `ExecuteBulkAsync`
  passe la sélection (éventuellement vide) telle quelle ; une action globale ignore l'argument et opère sur le
  périmètre que la **page** détermine (aucune logique métier dans le socle, pattern capacités).

Capacité GÉNÉRIQUE du design-system (toute liste peut désormais déclarer une action de masse globale),
candidate à reverser en amont (§6). Les trois fichiers figurent déjà dans le bloc `SOCLE-CONSIGNED-DRIFT`
(§4.15/§4.17) — la garde de provenance reste verte. Côté Liakont (hors socle), la page `Documents.razor`
déclare les deux actions et délègue au service in-process `IDocumentControlActions.RecheckManyAsync` (garde
`liakont.actions`, boucle + décision de blocage dans le cœur Pipeline `IDocumentRecheckService.RecheckManyAsync`,
trace d'audit FIX02 par document) — aucun fichier `Stratum.*` supplémentaire n'est touché.

### 4.20 Admin des jobs utilisable : liste fixe des types, payload typé, helper cron, page des exécutions (FIX211)
La recette run 2 (2026-06-11, décision opérateur E7) a relevé que l'admin des planifications de jobs était
inexploitable : le type de job se saisissait en TEXTE LIBRE (FullName .NET attendu), le payload en JSON BRUT,
le cron sans presets ni mention du fuseau, et il n'existait AUCUNE page listant les exécutions. FIX211 corrige
ces points. Comme les pages d'admin des jobs sont des pages SOCLE vendored, la correction touche le socle
(évolution autorisée par E4/E7, provenance obligatoire). Modifications **additives, rétro-compatibles** :

- `src/Modules/Job/Infrastructure/JobHandlerRegistration.cs` : ajout d'un paramètre **optionnel**
  `string? DisplayName = null` au record (libellé FR du type, surfacé par le catalogue ; défaut = comportement
  inchangé).
- `src/Modules/Job/Infrastructure/JobHandlerRegistrationExtensions.cs` : `AddJobHandler<TPayload, THandler>`
  prend un paramètre **optionnel** `string? displayName = null` (transmis à la registration). Les appels
  existants qui l'omettent gardent le comportement précédent.
- `src/Modules/Job/Infrastructure/JobModuleRegistration.cs` : enregistrement de deux nouveaux services
  (`IJobTypeCatalog` singleton, `IJobExecutionsQueries` scoped).
- `src/Modules/Job/Web/Pages/AdminJobScheduleForm.razor` : le type de job devient une **liste fixe** alimentée
  par `IJobTypeCatalog` (libellés FR, FullName jamais affiché, validation que le type existe) ; le payload
  devient des **champs typés** générés depuis le type (masqué quand le type n'a aucun paramètre) ; un **helper
  cron** (presets 15 min / horaire / quotidien à HH:MM) remplit l'expression ; l'aperçu passe à 3 occurrences
  affichées en **UTC** avec mention explicite du fuseau (les crons sont interprétés en UTC).
- `src/Modules/Job/Web/Pages/AdminJobSchedules.razor` : la colonne « Type de job » affiche le **libellé FR**
  (jamais le FullName) ; ajout d'une action de ligne « Voir les exécutions » (lien planification → exécutions
  filtrées par type).
- `src/Modules/Job/Web/JobNavSectionProvider.cs` : ajout de l'entrée de menu « Exécutions »
  (`/admin/jobs/executions`). Le filtre de visibilité Liakont `JobNavVisibilityFilter` (FIX07c) délègue à ce
  provider, donc l'entrée reste gardée par la permission `job.view`.
- `src/Modules/Job/Infrastructure/JobHandlerResolver.cs` : ajout de `JsonStringEnumConverter` aux
  options de désérialisation (les paramètres de payload de type enum, saisis par nom dans l'admin des
  jobs, doivent se désérialiser ; additif, accepte aussi les nombres — rétro-compatible).

Fichiers **AJOUTÉS** sous les dossiers vendored (non épinglés par le baseline §4.12 — comme les ajouts SOL06
§4.14 ; consignés ici pour la re-convergence NuGet, marqués `Liakont addition (FIX211)`) :
- `src/Modules/Job/Contracts/Services/IJobTypeCatalog.cs`, `JobTypeDescriptor.cs`, `JobParameterDescriptor.cs`,
  `JobParameterKind.cs` (catalogue des types : clé technique, libellé FR, paramètres typés).
- `src/Modules/Job/Infrastructure/Services/JobTypeCatalog.cs` (implémentation : dérive les paramètres par
  réflexion sur les propriétés / paramètres de constructeur du type de payload).
- `src/Modules/Job/Contracts/Queries/IJobExecutionsQueries.cs` (read-model des exécutions, tenant-scopé) +
  `src/Modules/Job/Infrastructure/Queries/PostgresJobExecutionsQueries.cs` (SELECT `job.jobs` filtré
  `company_id = @CompanyId`, sans verrou — lecture seule).
- `src/Modules/Job/Web/Pages/AdminJobExecutions.razor` (page des exécutions, gabarit `DeclaredListPage`) +
  `src/Modules/Job/Web/Registries/JobExecutionColumnRegistry.cs` (colonnes).

Aucune signature existante n'a changé de façon cassante ; toutes les modifications sont candidates à reverser
en amont (§6). Tests : `JobTypeCatalogTests` (réflexion), `AdminJobScheduleFormTests` + `AdminJobExecutionsTests`
(bUnit), `JobNavVisibilityFilterTests` étendu (entrée « Exécutions »).

### 4.21 `IJobQueries.GetLastCompletedAtByTypeAsync` — dernier achèvement par type de job (FIX210)
Le témoin de vie de la supervision (FIX210, F12 §5.1) doit savoir QUAND le dead-man's-switch a été évalué
pour la dernière fois, afin de distinguer une supervision saine d'une supervision muette (« aucune alerte »
≠ filet de sécurité en panne). Aucune lecture ciblée n'existait : `ListByStatusAsync` renvoie les N jobs
`Completed` les plus récemment créés, TOUS types confondus (`ORDER BY created_at DESC LIMIT @Limit`). Filtrer
le type côté client après un `LIMIT` risque, sur une instance chargée, de pousser l'exécution recherchée hors
fenêtre → faux « jamais évalué ». Une modification **additive, lecture seule** (marquée
`// Liakont addition (FIX210)`) :
- `src/Modules/Job/Contracts/Queries/IJobQueries.cs` : ajout de
  `Task<DateTimeOffset?> GetLastCompletedAtByTypeAsync(string jobType, CancellationToken)`.
- `src/Modules/Job/Infrastructure/Queries/PostgresJobQueries.cs` : implémentation
  (`SELECT max(completed_at) FROM job.jobs WHERE type = @Type AND status = 'Completed'`).

Aucune signature existante modifiée, aucun comportement du worker/scheduler changé. Le filtre par type se fait
en SQL (pas de scan plafonné). La lecture cible la base SYSTÈME (jobs système, comme le scheduler) — ce n'est
pas une requête métier tenant-scopée (CLAUDE.md n°9) : elle ne retourne qu'un horodatage d'achèvement, aucune
donnée de tenant. Candidate à reverser en amont (§6).

### 4.22 `DeclaredListPage` — option `EnablePersistentSelection` pour désactiver la barre de sélection persistante (FIX302)
La recette run 3 (2026-06-11, décision opérateur F2) a relevé que la page Documents affiche, à la sélection,
DEUX barres concurrentes : la barre d'actions groupées (`declared-list__bulk-bar`, qui héberge « Envoyer la
sélection » / « Revérifier la sélection ») ET la barre de sélection **persistante** Stratum (GUX03,
`StratumPersistentSelectionBar`, overlay « N sélectionné(s) (M au total) » avec Ajouter / Retirer / Voir / Vider).
La page ne consomme PAS la sélection persistante (ses actions groupées opèrent sur la sélection de la page
courante), donc le compteur de la 2ᵉ barre reste figé à « (0 au total) » — l'opérateur le lit comme incohérent
(« 4 sélectionnés (0 au total) ») et, regardant cette barre, ne trouve pas « Revérifier la sélection » (qui est
dans l'AUTRE barre). Le binding persistant était créé **inconditionnellement** dans `OnInitialized` pour toute
`DeclaredListPage`. Une modification **additive, rétro-compatible** (marquée `FIX302`) :

- `src/Common/UI/Components/DeclaredListPage.razor.cs` : ajout d'un paramètre **optionnel**
  `bool EnablePersistentSelection = true` (défaut = comportement INCHANGÉ pour toutes les listes existantes). Quand
  il vaut `false`, `_persistentBinding` reste `null` dans `OnInitialized` → le `StratumDataGrid` reçoit
  `PersistentSelection = null` → aucune barre de sélection persistante rendue. La sélection elle-même reste active
  (`AllowSelection` retombe sur la présence de `BulkActions`), donc la barre d'actions groupées fonctionne toujours.

Aucune autre logique modifiée ; aucun fichier `Stratum.*` supplémentaire touché. Le fichier figure déjà dans le bloc
`SOCLE-CONSIGNED-DRIFT` (§4.15) — la garde de provenance reste verte. Capacité GÉNÉRIQUE du design-system (toute
liste pilotant ses actions de masse sur la sélection courante peut désactiver la barre persistante), candidate à
reverser en amont (§6). Côté Liakont (hors socle), `Documents.razor` pose `EnablePersistentSelection="false"`,
retire « Revérifier tout » des actions groupées et la déclare en action GLOBALE de la barre d'outils (désactivée
quand aucun document n'est bloqué dans le périmètre — plus jamais de bouton orphelin sur liste vide).

### 4.23 `GlobalShortcutHandler` — la palette de recherche voit aussi les `INavNodeProvider` (polish UX/UI)

**Fichier** : `src/Common/UI/Components/GlobalShortcutHandler.razor`. **Motif** : la palette de
recherche (Ctrl+K, `CommandPalette`) construisait son arbre via `BuildNavTree()` à partir des SEULS
`INavSectionProvider` (sections plates), alors que le socle expose `INavNodeProvider` pour la
navigation hiérarchique (sous-menus) et que la sidebar (`ErpNav`, fichier Liakont) consomme déjà les
deux via la surcharge `BuildNavTree(sections, nodes)`. Conséquence : toute entrée de navigation
déclarée en `INavNodeProvider` (sous-menu Paramétrage du lot polish UX/UI) disparaissait de la
recherche globale. **Modification minimale** : injection `IEnumerable<INavNodeProvider>` + appel de
la surcharge existante `BuildNavTree(NavProviders, NavNodeProviders)` — `CommandPalette` collecte
déjà récursivement les feuilles (`CollectSearchableItems`). Aucune autre logique modifiée. Correction
GÉNÉRIQUE (toute app socle utilisant des node providers en bénéficie), candidate à reverser en
amont (§6).

### 4.24 Provisioning de tenant — `company_id` porté par le realm + mot de passe admin réellement temporaire (OPS03 lot A)

**Fichiers** : `src/Common/Abstractions/MultiTenancy/KeycloakRealmProvisionRequest.cs`,
`TenantProvisionResult.cs`, `TenantDto.cs`, `src/Common/Infrastructure/Keycloak/KeycloakRealmProvisioner.cs`,
`src/Common/Infrastructure/Database/TenantProvisioningService.cs`, `TenantQueries.cs`,
`ServiceCollectionExtensions.cs`, migration `V016__add_company_id_to_tenants.sql`.
**Motif** : un tenant fraîchement provisionné était INUTILISABLE — le mapper `company_id` du realm
était un mapper d'ATTRIBUT utilisateur jamais renseigné (l'admin créé ne portait que
`stratum_user_id`) → claim absent → toutes les requêtes company-scopées échouent ; le mot de passe
admin était CODÉ EN DUR (`Change@First1`) et posé en credential PERMANENT malgré son commentaire
« temporary ». **Modifications** : (1) `companyId` GÉNÉRÉ au provisioning (un tenant = une société),
persisté dans `outbox.tenants.company_id` (migration V016, system-only — préfixe ajouté à
`SystemOnlyMigrationPrefixes`), exposé par `TenantDto`, et émis par le realm comme mapper
`company_id` HARDCODÉ au niveau client (comme `tenant_id` — aligné sur le realm de dev) : tout
utilisateur présent ET futur du realm porte le claim ; (2) mot de passe admin ALÉATOIRE, retourné
UNE fois via `TenantProvisionResult.AdminTemporaryPassword` (jamais persisté/journalisé), credential
`temporary=true` + action `UPDATE_PASSWORD`. **Le point (2) est SUPERSEDED par §4.26** (l'admin de
realm n'est plus créé du tout) ; le point (1) `company_id` reste valide POUR LE PROFIL DÉDIÉ
mono-tenant (cf. recadrage RLM04 ci-dessous). Épinglé par
`KeycloakRealmProvisionerTests.ProvisionRealmAsync_DedicatedProfile_Should_Emit_CompanyId_As_Hardcoded_Client_Mapper`.

**Mise à jour [ADR-0021](../adr/ADR-0021-realm-keycloak-unique-isolation-par-claim.md) (2026-06-13)** :
le modèle realm-par-tenant qui rendait le point (1) valide (un client = un tenant → mapper
`company_id` HARDCODÉ au niveau client) est **superseçu** — Liakont passe à un **realm unique
partagé**, où le mapper hardcodé client devient impossible (tous les jetons porteraient la même
valeur = isolation nulle) et `company_id` redevient un mapper d'**attribut par-utilisateur**.

**Recadrage RLM04 (2026-06-13, cf. §4.28)** : le vrai `KeycloakRealmProvisioner` (donc le mapper
`company_id` HARDCODÉ du point (1)) n'est plus câblé que dans le **profil dédié mono-tenant** ; le
profil SaaS partagé (défaut) utilise `NoOpKeycloakRealmProvisioner` et ne crée aucun realm. Le test
épinglé a été **renommé** (`…ProvisionRealmAsync_DedicatedProfile_Should_Emit_CompanyId_As_Hardcoded_Client_Mapper`)
et porte désormais le commentaire « JAMAIS pour le partagé » : il certifie le mapper hardcodé comme
comportement attendu du **dédié uniquement**, jamais du partagé (où ce serait une faute d'isolation).

### 4.25 `IKeycloakUserProvisioner` — provisioning d'utilisateur dans un realm EXISTANT (OPS03 lot A)

**Fichiers AJOUTÉS** : `src/Common/Abstractions/MultiTenancy/IKeycloakUserProvisioner.cs` +
`KeycloakUserSpec.cs`, `src/Common/Infrastructure/Keycloak/KeycloakUserProvisioner.cs`
(+ enregistrement DI dans `ServiceCollectionExtensions.cs`). **Motif** : le socle ne savait créer un
utilisateur Keycloak QUE pendant la création du realm (`CreateAdminUserAsync`, privé) ; le
provisioning du « premier utilisateur » d'un tenant (assistant opérateur OPS03) exige un seam
par-utilisateur réutilisant le client `"KeycloakAdmin"` + `KeycloakAdminTokenService` (internal —
inaccessibles depuis Liakont.Host sans dupliquer l'acquisition de token). **Périmètre** : nouveau
seam à CÔTÉ de `IKeycloakRealmProvisioner` (aucun refactoring du provisioner de realm) : recherche
par username exact, création (id du header Location), fusion d'attributs (read-modify-write),
reset-password temporaire, rôle realm idempotent (409 = succès), assignation de rôles, suppression
(compensation). Un 409 à la création (username OU email déjà pris — l'email est unique par realm,
un pré-contrôle par username ne suffit pas) lève l'exception TYPÉE `KeycloakUserConflictException`
(Abstractions) pour un refus opérateur propre, jamais un 500. La consommation produit passe par
l'abstraction IdP-agnostique `ITenantUserProvisioningService` du Host (couche d'auth). Tests :
`KeycloakUserProvisionerTests`. `FakeHttpMessageHandler` (tests socle) étendu : capture des corps
de requêtes (`AllRequestBodies`).

### 4.26 Provisioning de tenant — le realm ne crée PLUS d'admin répliqué (recette OPS03, 13/06/2026)

**Fichiers** : `src/Common/Infrastructure/Database/TenantProvisioningService.cs`,
`src/Common/Infrastructure/Keycloak/KeycloakRealmProvisioner.cs`,
`src/Common/Abstractions/MultiTenancy/KeycloakRealmProvisionRequest.cs`, `TenantProvisionResult.cs`.
**Motif** (révélé en recette manuelle OPS03) : le flux hérité du socle répliquait le super-admin de
l'INSTANCE dans CHAQUE tenant — `SeedTenantAdminAsync` copiait l'utilisateur `identity` portant le
rôle `SystemAdmin` dans la base du nouveau tenant, et `KeycloakRealmProvisioner.CreateAdminUserAsync`
créait un compte « sysadmin » (rôles `Admin`+`SystemAdmin`) dans le realm du tenant. C'est le modèle
cookie-auth natif de Stratum, INADAPTÉ à Liakont : le super-admin est un acteur CROSS-TENANT du realm
PRIMAIRE (il supervise via `ITenantScopeFactory`, jamais via un compte par tenant), et le premier
utilisateur d'un tenant vient de l'assistant opérateur (OPS03 lot A, `IKeycloakUserProvisioner`). En
exécution réelle, le seed dev crée le `sysadmin` avec le rôle `Admin` (pas `SystemAdmin`) → la requête
de `SeedTenantAdminAsync` ne trouvait personne → tout provisioning échouait (un faux-vert masqué en
test par un seed `SystemAdmin` de fixture). **Modifications** : (1) `ProvisionAsync` n'appelle plus
`SeedTenantAdminAsync` (méthode + `AdminRow` supprimés) ; le realm naît SANS utilisateur. (2)
`KeycloakRealmProvisioner` ne crée plus d'admin (`CreateAdminUserAsync`, `AssignRealmRolesAsync`,
`ExtractIdFromLocationHeader`, `KeycloakRole` supprimés) — il crée realm + client OIDC + mappers (le
mapper `company_id` HARDCODÉ de §4.24 reste, donc tout futur utilisateur porte son claim). (3)
`KeycloakRealmProvisionRequest` perd `AdminEmail/AdminUsername/AdminPassword/StratumUserId` ;
`TenantProvisionResult` perd `AdminTemporaryPassword` (plus d'admin de realm → plus de secret à
remettre ; le seul secret affiché par l'assistant est le mot de passe temporaire du PREMIER
utilisateur, lot A). Tests : `KeycloakRealmProvisionerTests` (le realm ne crée plus d'utilisateur —
4 requêtes, test « admin password » retiré), `ClientProvisioningConsoleIntegrationTests` (plus
d'assertion `AdminTemporaryPassword`), fixture `ConsoleApiFactory` (seed `SystemAdmin` retiré — le
contrat du socle ne l'exige plus).

Suite actée par [ADR-0021](../adr/ADR-0021-realm-keycloak-unique-isolation-par-claim.md) (realm
unique partagé) : le provisioning ne créera plus de realm du tout (`KeycloakRealmProvisioner` sort du
chemin de création SaaS partagé).

### 4.27 Résolution autoritaire du tenant par `company_id` (RLM02, ADR-0021 §2c) — fichiers AJOUTÉS aux projets Common vendored

RLM02 ajoute la voie de résolution `company_id(jeton) → outbox.tenants.company_id → tenant` **sous les
dossiers vendored `src/Common`**, à côté de la couche de requêtes registre existante (`TenantQueries`).
Ce sont des **fichiers AJOUTÉS**, pas des modifications de fichiers `Stratum.*` existants : le baseline
de provenance (§4.12) n'épingle que les fichiers vendorés existants, donc ces ajouts **ne dérivent pas**
et ne figurent PAS dans le bloc `SOCLE-CONSIGNED-DRIFT`. Ils sont consignés ici pour la re-convergence
NuGet et marqués `// Liakont addition (RLM02)` (`.cs`) / `-- Liakont addition (RLM02)` (`.sql`) en tête
de fichier. **Aucun fichier `Stratum.*` existant n'a été modifié par RLM02** — la contrainte de migration
est portée par une migration auto-gardée (`IF EXISTS (outbox.tenants)`) plutôt que par un ajout à
`TenantProvisioningService.SystemOnlyMigrationPrefixes` (fichier épinglé), pour éviter toute dérive socle.

Fichiers ajoutés (namespaces cohérents avec l'assembly hôte ; code **Liakont**, pas Stratum amont) :
- `src/Common/Abstractions/MultiTenancy/ICompanyTenantLookup.cs` (projet `Stratum.Common.Abstractions`)
- `src/Common/Infrastructure/Database/CompanyTenantLookup.cs` (projet `Stratum.Common.Infrastructure`,
  requête synchrone Dapper indexée par la contrainte UNIQUE de V017)
- `src/Common/Infrastructure/Migrations/V017__enforce_company_id_on_tenants.sql` (backfill du tenant
  `default` + `company_id` NOT NULL + UNIQUE ; system-only par auto-garde)

Code Liakont **hors** socle (pas de consignation requise, mais cité pour le contexte) :
`src/Host/Liakont.Host/MultiTenancy/CompanyClaimTenantResolver.cs` (résolveur autoritaire, lit le claim
via `ClaimsPrincipal`, jamais via un type Keycloak — gardé par NetArchTest) + son enregistrement EN TÊTE
de la chaîne dans `MultiTenantServiceCollectionExtensions`.

### 4.28 Sortie du provisioner de realm du chemin de création SaaS partagé (RLM04, ADR-0021 §1/§5)

**Motif** : en realm Keycloak unique partagé (ADR-0021), le provisioning d'un tenant ne doit plus
créer NI realm NI client par tenant (INV-0021-1) ; la capacité socle realm-par-tenant reste
disponible pour le **déploiement dédié mono-tenant**. Le seam est porté en DI (option « no-op
enregistrée en DI » de l'ADR), pas par une suppression de code.

**Fichier `Stratum.*` AJOUTÉ** (non épinglé par le baseline §4.12 — comme §4.14/§4.25/§4.27 ;
consigné pour la re-convergence NuGet, marqué `// Liakont addition (RLM04)`) :
- `src/Common/Infrastructure/Keycloak/NoOpKeycloakRealmProvisioner.cs` — implémentation no-op
  d'`IKeycloakRealmProvisioner` : `ProvisionRealmAsync` renvoie `Idempotent` SANS aucun appel HTTP
  (aucun `POST /admin/realms`, preuve STRUCTURELLE : pas de dépendance `IHttpClientFactory`) ;
  `DeleteRealmAsync`/`AddTenantRedirectUriAsync` sont des no-op.

**Fichiers `Stratum.*` MODIFIÉS** (épinglés → bloc `SOCLE-CONSIGNED-DRIFT`) :
- `src/Common/Infrastructure/Database/ServiceCollectionExtensions.cs` (NOUVELLE dérive) : le
  provisioner de realm enregistré dépend du profil — `NoOpKeycloakRealmProvisioner` par DÉFAUT
  (SaaS partagé), le vrai `KeycloakRealmProvisioner` seulement si
  `Keycloak:DedicatedRealmPerTenant=true` (dédié mono-tenant). Le flag est lu directement depuis
  `IConfiguration` (aucun ajout de propriété à un type d'options épinglé, pour minimiser la surface
  de dérive socle).
- `src/Common/Infrastructure/Database/TenantProvisioningService.cs` (déjà en dérive §4.24/§4.26) :
  dans `ProvisionAsync`, l'enregistrement du realm (`IRealmRegistry.RegisterRealm`) et l'ajout du
  redirect par sous-domaine (`AddTenantRedirectUriAsync`) sont désormais gardés par
  `if (!string.IsNullOrEmpty(kcResult.Authority))`. Le no-op renvoie une **autorité vide** → ces deux
  gestes vestigiaux ne s'exécutent plus en profil partagé. Le vrai provisioner (profil dédié) renvoie
  l'autorité du realm — qu'il vienne d'être créé (`Created`) OU qu'il préexiste (`Idempotent`, chemin
  de **reprise**) — donc la mécanique d'origine reste **inconditionnelle** pour lui (ré-enregistrement
  idempotent du realm pour la validation JWT, sans régression du chemin de reprise). Le redirect
  statique `default.localhost` (realm-export.json, FIX07a) n'est pas touché — le nettoyage cible les
  redirects par tenant provisionné.

**Code Liakont hors socle (pas de consignation requise, cité pour contexte)** :
`src/Host/Liakont.Host/Startup/AppBootstrap.cs` (`SeedRealmRegistryFromDatabaseAsync`) ne seede plus
le registre de realms par-tenant depuis la base en profil partagé (les `realm_name` par-tenant y sont
vestigiaux) — uniquement en dédié ; le realm partagé reste enregistré via `Keycloak:RealmTenantMap`.
Tests : `NoOpKeycloakRealmProvisionerTests`, `RealmProvisionerRegistrationTests` (le seam : le partagé
résout le no-op, le dédié le vrai), `KeycloakRealmProvisionerTests` (recadré au profil dédié, §4.24),
`TenantProvisioningRealmSeamIntegrationTests` (consommation du seam par `ProvisionAsync`, Testcontainers,
les deux directions de la garde), et l'E2E de clôture `TenantLoginSharedRealmE2ETests` (un utilisateur
de tenant **seedé** se connecte dans le realm partagé).

**Dette ouverte assumée (onboarding d'un NOUVEL utilisateur en realm partagé)** : RLM04 sort le
provisioner de *realm* du chemin SaaS partagé, mais ne touche PAS le provisioner d'*utilisateur*
(`src/Host/Liakont.Host/Security/Keycloak/KeycloakTenantUserProvisioner.cs`, OPS03 lot A). Celui-ci
crée encore le compte dans `tenant.RealmName` (= `stratum-{tenantId}`, vestigial en partagé) → l'onboarding
d'un nouvel utilisateur via l'assistant opérateur cible un realm inexistant (404 Keycloak) dans le profil
partagé par défaut. L'E2E de clôture couvre un utilisateur **pré-seedé** (RLM01), pas un utilisateur
**fraîchement provisionné**. Correctif = item de suivi (faire cibler le realm PARTAGÉ — `PrimaryRealmName` —
en profil partagé, l'attribut `company_id` par-utilisateur étant déjà posé de façon cohérente avec
`outbox.tenants.company_id` ; + recréation des comptes de recette dans le realm partagé, ADR-0021 §État
actuel vs cible). Hors périmètre du seam de *realm* RLM04.

### 4.29 Migration incrémentale au démarrage — base de tenant injoignable non comptée « migrée » + alerte agrégée (RLF02)

**Motif** : finding F4 de la recette `GATE_REALM_UNIQUE` — au démarrage,
`TenantProvisioningService.MigrateExistingTenantsAsync` journalisait « X/X tenant(s) updated » **alors
qu'une base de tenant n'existe pas**. Diagnostic root-cause à la correction : la cause n'est pas (que)
l'agrégation des logs, c'est un **bug de mapping Dapper latent**. La requête de listing
`SELECT id, database_name FROM outbox.tenants` n'**aliasait pas** `database_name`, or Dapper ne strippe
PAS les underscores par défaut (`MatchNamesWithUnderscores` n'est jamais activé dans le code — toutes
les **autres** requêtes du fichier aliasent : `… AS databasename`, `… AS isactive`, `… AS realmname`).
Résultat : `TenantRecord.DatabaseName` restait **vide** → `RunTenantMigrationsAsync("")` se connectait à
la base **par défaut/système** (qui existe et est déjà migrée) → **faux succès compté « migré »** → le
tenant dont la VRAIE base manque (`stratum_tenant2`) n'était jamais touché ni détecté, et la ligne verte
« X/X updated » le masquait (faux-vert, règle review #8). Le chemin « base injoignable → skip » n'était
donc même pas atteint en pratique.

**Fichier `Stratum.*` MODIFIÉ** (épinglé → déjà listé au bloc `SOCLE-CONSIGNED-DRIFT` depuis
§4.24/§4.26/§4.28, aucune nouvelle entrée requise) — dans `MigrateExistingTenantsAsync` :
- **(root cause)** la requête de listing alias désormais `database_name AS databasename` (même
  convention que `GetTenantForReprovisionAsync`/`GetTenantForDeactivationAsync` du même fichier) : la
  migration cible enfin la VRAIE base de chaque tenant → une base manquante lève bien `NpgsqlException`
  (`3D000`) au lieu d'une connexion silencieuse à la base par défaut.
- les tenants injoignables sont collectés (liste `skipped`) et **ne sont pas comptés migrés**
  (`migrated` ne s'incrémente qu'au succès) ; après la boucle, si au moins un tenant est injoignable,
  une **alerte agrégée explicite** est tracée au niveau Warning (`LogTenantsInaccessible`, nouveau
  `LoggerMessage`) en les **nommant**. La ligne de complétion devient honnête et ventilée —
  `LogTenantMigrationCompleted` passe de `({Migrated}/{Total})` à
  `({Migrated} migrated, {Skipped} inaccessible, {Failed} failed of {Total})`.

**Politique de disponibilité préservée (inchangée)** : une base injoignable **n'interrompt pas** le
démarrage (les autres tenants restent sains) ; seul un échec de migration SQL DbUp
(`InvalidOperationException`, collecté dans `failures`) lève l'`AggregateException` finale. RLF02 ne
modifie QUE la **visibilité** (compteur honnête + alerte), pas la politique de levée.

**Style des messages** : les logs de cette méthode socle sont en anglais (convention vendored
existante : `Skipping migration … inaccessible`, `Migration failed …`, `… migration completed`) ; les
deux messages touchés/ajoutés suivent cette langue pour rester cohérents avec le bloc environnant
(la règle CLAUDE.md n°12 « messages opérateur en français » vise les messages produit Liakont, pas
ces logs d'infrastructure du socle hérité).

**Test** : `tests/Common.Infrastructure.Tests.Integration/MigrateExistingTenantsInaccessibilityTests.cs`
(Testcontainers PostgreSQL, conteneur dédié, logger capturant) — un tenant actif sans base prouve
(1) qu'il n'est pas compté migré, (2) qu'une alerte Warning le nomme, (3) que le démarrage n'est pas
interrompu ; un second cas (tenant sain + tenant cassé) prouve le compteur mixte honnête (1 migré /
1 injoignable) et la non-régression du chemin de succès.

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
