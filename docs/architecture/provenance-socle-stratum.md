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
(`git hash-object`, indépendant de la plateforme) de chacun des **vrais fichiers vendorés
`Stratum.*`** sous les racines vendorées (`src/Common` + `src/Modules/{Identity,Party,Job,Notification,
Audit}` ; le Host adapté `Liakont.Host` est exclu — code Liakont, pas `Stratum.*`). À chaque
`verify-fast`, le script recalcule les hashes : tout fichier épinglé qui a dérivé du baseline (modifié
ou supprimé) DOIT figurer, **par son chemin repo-relatif EXACT**, dans le bloc de consignation balisé
ci-dessous (`SOCLE-CONSIGNED-DRIFT`), sinon `verify-fast` échoue (exit 2).

**Les ajouts Liakont sont exclus du périmètre épinglé (correctif RDL09, finding A6-prov-1).** Un
fichier AJOUTÉ par Liakont sous une racine vendorée (code Liakont placé dans l'arbre vendored, pas un
fichier `Stratum.*` : mécanique multi-tenant SOL06, catalogue/exécutions de jobs FIX210/FIX211,
affichage des dates côté navigateur, provisioning utilisateur Keycloak OPS03…) **n'est pas épinglé** :
l'éditer est du travail Liakont normal, pas une dérive du socle. Ces fichiers sont identifiés — et
exclus de `-Generate` **comme** de la vérification — par un **marqueur de tête** : leur **première
ligne** contient la chaîne littérale `Liakont addition` (convention §4.14 ; ex. `// Liakont addition
(SOL06): …`, `@* Liakont addition … *@`, `-- Liakont addition …`). Un vrai fichier `Stratum.*` ne
porte jamais ce marqueur : il reste épinglé et toute édition réelle reste détectée. Avant RDL09 le
baseline épinglait ces ajouts (régénération OPS03 non consignée — A6-prov-1), ce qui faisait échouer
`verify-fast` sur une édition LÉGITIME d'un de ces fichiers (faux positif « dérive socle non
consignée ») : la garde poussait alors au mauvais geste. Le décompte exact des fichiers épinglés est
**tenu à jour à chaque régénération dans le journal §4.37** (1226 à la régénération RDL09) — il n'est
plus codé en dur ici.

**Matching par chemin exact, jamais par nom de fichier** (correctif review SOL03 round 1, P1) : les
noms de fichiers vendored sont très souvent en collision (`ServiceCollectionExtensions.cs` ×14,
`_Imports.razor` ×6, `MODULE.md`/`INVARIANTS.md`/`SCENARIOS.md` ×4 chacun, `NullPartyQueries.cs`,
`AssemblyInfo.cs`…). Un match par leaf ou par sous-chaîne laisserait passer une modification
silencieuse du fichier B dès qu'un homonyme A est consigné — exactement le faux-vert corrigé ici.

Workflow d'une modification légitime future d'un fichier `Stratum.*` EXISTANT : (1) éditer le fichier,
(2) ajouter une sous-section 4.x narrative ci-dessus, (3) ajouter son **chemin repo-relatif exact**
dans le bloc `SOCLE-CONSIGNED-DRIFT` ci-dessous. (Optionnel : régénérer le baseline avec
`tools/socle-provenance-check.ps1 -Generate` pour « cuire » le nouvel état — il ne dérive alors plus
et peut être retiré du bloc ; **toute régénération est consignée au journal §4.37**.)

Workflow d'un AJOUT Liakont sous une racine vendorée (pas un fichier `Stratum.*`) : (1) créer le
fichier en mettant le **marqueur de tête `Liakont addition (…)` en première ligne** (syntaxe de
commentaire du type de fichier), (2) le décrire dans une sous-section 4.x. Il est alors automatiquement
exclu du périmètre épinglé — ne **jamais** l'inscrire dans le bloc `SOCLE-CONSIGNED-DRIFT` (réservé aux
fichiers `Stratum.*` épinglés qui dérivent).

État du baseline : il est généré sur l'état COURANT de l'arbre (les modifications consignées des
sections 4.x sont « cuites » dedans). Après une régénération complète, les fichiers `Stratum.*`
épinglés sont conformes au baseline et **ne dérivent pas** : le bloc `SOCLE-CONSIGNED-DRIFT` ci-dessous
est alors **informatif** (trace, pour la re-convergence NuGet, des fichiers `Stratum.*` qui divergent
de l'amont) et n'est consulté par la garde que lorsqu'un fichier épinglé dérive effectivement.

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
<!-- §4.38 — RDL08 : dé-duplication à l'enqueue des jobs récurrents (anti-empilement) -->
src/Modules/Job/Infrastructure/JobScheduler.cs
src/Common/Infrastructure/Database/TenantProvisioningService.cs
src/Common/Infrastructure/Keycloak/KeycloakRealmProvisioner.cs
src/Common/Abstractions/MultiTenancy/KeycloakRealmProvisionRequest.cs
src/Common/Abstractions/MultiTenancy/TenantProvisionResult.cs
src/Common/Infrastructure/Database/ServiceCollectionExtensions.cs
src/Common/UI/CommonUIServiceExtensions.cs
src/Modules/Audit/Web/Pages/AdminAudit.razor
src/Modules/Audit/Web/Pages/AdminAuditDetail.razor
src/Modules/Audit/Web/Pages/AdminAuditPolicies.razor
src/Modules/Audit/Web/Pages/AdminAuditPolicyForm.razor
src/Modules/Identity/Web/Pages/AdminUsers.razor
src/Modules/Identity/Web/Pages/AdminUserForm.razor
src/Modules/Identity/Web/Pages/AdminAgents.razor
src/Modules/Identity/Web/Pages/AdminAgentForm.razor
src/Modules/Identity/Web/Pages/AdminTeams.razor
src/Modules/Identity/Web/Pages/AdminTeamForm.razor
src/Modules/Identity/Web/Pages/AdminRoleForm.razor
src/Modules/Identity/Web/Pages/AdminDelegations.razor
src/Modules/Identity/Web/Pages/AdminDelegationForm.razor
src/Modules/Notification/Web/Pages/AdminSla.razor
src/Modules/Notification/Web/Pages/AdminSlaForm.razor
src/Modules/Notification/Web/Pages/AdminCatalogServiceForm.razor
src/Modules/Notification/Web/Pages/AdminNotificationRoutingDetail.razor
src/Modules/Notification/Web/Pages/AdminNotificationTemplates.razor
src/Modules/Notification/Web/Pages/AdminNotificationRouting.razor
src/Modules/Notification/Web/Pages/AdminCatalogServices.razor
src/Modules/Notification/Web/Pages/AdminWebhookSubscriptions.razor
src/Modules/Notification/Web/Pages/AdminIntegrations.razor
<!-- §4.39 — BUG-4 volet A : echec de save DeclaredFormPage rendu visible (MapDomainError -> Func<string,string?>) -->
src/Common/UI/Components/DeclaredFormPage.razor.cs
src/Modules/Notification/Web/Pages/AdminWebhookForm.razor
<!-- §4.36 — lecture timestamptz via DbTimestamp (casts directs (DateTimeOffset)row.x corriges) -->
src/Modules/Job/Infrastructure/PostgresJobUnitOfWork.cs
src/Modules/Job/Infrastructure/Queries/PostgresScheduleQueries.cs
src/Modules/Notification/Infrastructure/PostgresNotificationUnitOfWork.cs
src/Modules/Notification/Infrastructure/Queries/PostgresApiKeyQueries.cs
src/Modules/Notification/Infrastructure/Queries/PostgresIntegrationConfigQueries.cs
src/Modules/Notification/Infrastructure/Queries/PostgresWebhookQueries.cs
src/Modules/Notification/Infrastructure/Queries/PostgresDeliverySlaQueries.cs
src/Modules/Notification/Infrastructure/Queries/PostgresServiceDefinitionQueries.cs
src/Modules/Notification/Infrastructure/Queries/PostgresDeliveryRecordQueries.cs
src/Modules/Notification/Infrastructure/Queries/PostgresRoutingRuleQueries.cs
src/Modules/Notification/Infrastructure/Queries/PostgresEmailTemplateQueries.cs
src/Modules/Identity/Infrastructure/PostgresIdentityUnitOfWork.cs
src/Modules/Identity/Infrastructure/Queries/PostgresDelegationQueries.cs
src/Modules/Identity/Infrastructure/Queries/PostgresIdentityQueries.cs
src/Modules/Identity/Infrastructure/Queries/PostgresAgentQueries.cs
src/Modules/Identity/Infrastructure/Queries/PostgresTeamQueries.cs
src/Common/UI/wwwroot/js/stratum-ui.js
<!-- §4.44 — GDF01 : registre de types d'événements outbox peuplé au build DI (course de démarrage supprimée) -->
src/Common/Infrastructure/Events/ServiceCollectionExtensions.cs
<!-- SOCLE-CONSIGNED-DRIFT:END -->

### 4.13 Harness E2E — adapté de `Stratum.Tests.E2E` (SOL05)
Le harness de tests E2E (`tests/Liakont.Tests.E2E`) est **adapté** du harness Stratum
`tests/Stratum.Tests.E2E` — **hors périmètre du vendoring SOL01** (qui ne copie que `src/`), d'où
sa consignation ici (adaptation, pas copie brute). Le nouveau projet porte le namespace
**`Liakont.Tests.E2E`** (code Liakont, pas `Stratum.*`). Infrastructure reprise et adaptée :
`KeycloakE2EWebFactory` (démarre `Liakont.Host` sur PostgreSQL `postgres:16-alpine` + Keycloak
`quay.io/keycloak/keycloak:26.0.8` via Testcontainers, ports dynamiques), `PlaywrightFixture`,
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
de fichiers `Stratum.*` existants : le baseline de provenance (§4.12) n'épingle que les vrais fichiers
`Stratum.*`, **jamais** un ajout Liakont (exclusion par marqueur de tête, cf. §4.12 — correctif
RDL09). Ces ajouts **ne dérivent pas** et ne figurent PAS dans le bloc `SOCLE-CONSIGNED-DRIFT` (réservé
aux fichiers `Stratum.*` épinglés modifiés/supprimés). Ils sont consignés ici pour la re-convergence
NuGet et **doivent** porter le marqueur `// Liakont addition (SOL06)` en première ligne (c'est ce
marqueur, désormais, qui les exclut automatiquement du périmètre épinglé).

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
### 4.30 `Directory.Packages.props` — bump QuestPDF `2024.12.3 → 2025.7.4` (FX02, ADR-0023 §1)

**Motif** : la génération Factur-X (module Liakont `FacturX.Infrastructure`, ADR-0023) scelle le
PDF/A-3 avec QuestPDF et exige le correctif de compatibilité **Mustang** livré à partir de **2025.7.4**
(changelog QuestPDF). La version est **centralisée** (`ManagePackageVersionsCentrally`) : la bumper
recompile aussi le **socle vendored** `Stratum.Common.UI` (`Services/PdfExportHelper.cs` →
`document.GeneratePdf()`, export PDF des grilles), d'où la consignation ici — la règle de tête (§0)
vise « un fichier de configuration qui en conditionne la compilation », et `Directory.Packages.props`
est listé §2 comme config socle adaptée.

**Changement** : `Directory.Packages.props` — `<PackageVersion Include="QuestPDF" Version="2024.12.3" />`
→ `Version="2025.7.4"`, commentaire actualisé (périmètre élargi à `FacturX.Infrastructure`, QuestPDF
confinée à cette couche, INV-FX-1). **Aucun fichier `Stratum.*` source modifié** ; l'API Fluent QuestPDF
utilisée par `PdfExportHelper` (`Document.Create`/`Page`/`Table`/`GeneratePdf`, `PageSizes`, `Colors`)
est **stable** entre 2024.12.3 et 2025.7.4. **Aucun nouveau package** (ADR-0023 §1) : l'ADR socle
`docs/adr/socle/ADR-0004-questpdf.md` reste la référence du choix de lib, non re-décidé.

**Vérification** : `verify-fast` (build 2 solutions + analyzers) et `run-tests` (5702 tests, dont
`tests/Common.UI/Unit/PdfExportHelperTests.cs` qui asserte les magic bytes `%PDF` de l'export socle)
**verts** sous 2025.7.4. **Limite connue de la garde** : `tools/socle-provenance-check.ps1` +
`tools/socle-baseline.sha1` (§4.12) n'épinglent que `src/**` — un changement de version dans la config
RACINE n'est donc PAS détecté automatiquement (il passe `verify-fast` au vert sans alerte). La présente
consignation comble ce trou côté **traçabilité** ; étendre la garde à la config racine est un item
d'outillage de suivi (hors périmètre FX02).

### 4.31 `Stratum.Common.UI` — affichage des dates au fuseau du NAVIGATEUR (RB6)

**Motif** : les horodatages s'affichaient en UTC (le serveur tourne en UTC ; `ToLocalTime()` côté Blazor
Server ne convertit rien — il donne l'heure SERVEUR). Le correctif RB6 vise les pages Host (Liakont) ET les
pages d'admin du socle (`Stratum.Modules.*` : Audit/Identity/Job/Notification). Le composant partagé est donc
placé dans `Stratum.Common.UI` (RCL commune référencée par tous) — sinon les pages socle, qui ne référencent
pas le Host, ne pourraient pas l'utiliser (dépendance socle→app interdite). NB : la sonde `<BrowserTimeProbe>`
vit dans le shell Host (`ErpShellLayout`) ; les pages d'admin socle sont routées DANS l'app Host sous ce shell
→ elles partagent le CIRCUIT et bénéficient du fuseau résolu (service scopé/circuit, résolu 1× par n'importe
quelle page du shell). Une page socle servie hors de ce shell resterait en UTC (non applicable ici — toutes les
pages transitent par `ErpShellLayout`).

**Changement** :
- AJOUTS (fichiers NEUFS — non épinglés par la garde, cf. §4.30 « Files ADDED later … are ignored ») :
  `Time/IBrowserTimeZone.cs`, `Time/BrowserTimeZone.cs`, `Time/LiakontDateDisplay.cs`,
  `Components/LiakontDate.razor`, `Components/BrowserTimeProbe.razor`, `wwwroot/js/liakont-time.js`.
  `IBrowserTimeZone` (scopé/circuit) résout le fuseau via JS interop (`liakontTime.getTimeZone`, IANA →
  `TimeZoneInfo`, repli UTC sans exception, mémorisé). `<LiakontDate>` formate au fuseau résolu (repli UTC
  EXPLICITE avant résolution, jamais une fausse heure locale). `<BrowserTimeProbe>` (au layout Host) résout 1×/circuit.
- MODIFICATION (fichier ÉPINGLÉ) : `src/Common/UI/CommonUIServiceExtensions.cs` — `AddCommonUI()` enregistre
  désormais `AddScoped<IBrowserTimeZone, BrowserTimeZone>` (auto-suffisance : un hôte qui appelle `AddCommonUI()`
  + rend `<LiakontDate>`/`<BrowserTimeProbe>` n'a rien d'autre à câbler).
- Noms à marque « Liakont » ASSUMÉS dans le socle (le no-touch socle n'est pas une exigence produit) ; à
  neutraliser le jour d'une re-convergence amont Stratum (§6).

**Vérification** : `verify-fast` vert (build 2 solutions + analyzers + tests unitaires, dont
`tests/Host/Liakont.Host.Tests.Unit/Time/*` : helper DST/UTC-fallback/null, service mapping/mémorisation/
repli, bUnit `<LiakontDate>`/sonde). `CommonUIServiceExtensions.cs` (modifié, épinglé) consigné dans le bloc
`SOCLE-CONSIGNED-DRIFT` — **baseline HEAD INCHANGÉ** (pas de re-pin en masse qui mélangerait des fichiers
étrangers à RB6). Les 6 ajouts ne sont pas épinglés (garde par conception) — tracés ici uniquement.

### 4.32 `Stratum.Modules.Job.Web` — horodatages d'admin Job au fuseau navigateur (RB6 P2)

**Motif** : suite de RB6 (§4.31) — les pages d'admin du socle affichaient l'heure UTC/serveur. Migration des
horodatages d'ÉVÉNEMENT vers `<LiakontDate>` (composant socle, §4.31).

**Changement** — RÈGLE : les ÉVÉNEMENTS passent au fuseau du NAVIGATEUR (`<LiakontDate>`) ; les PRÉVISIONS serveur
restent en UTC EXPLICITE (le cron est interprété en UTC — afficher une prévision en heure locale induirait en
erreur, car le job fire à l'heure UTC ; cohérence + honnêteté du fuseau) :
- `AdminJobSchedules.razor` : LastRunAt (ÉVÉNEMENT) → `<LiakontDate>` ; NextRunAt (PRÉVISION cron) → UTC explicite.
  DÉJÀ au bloc `SOCLE-CONSIGNED-DRIFT` (items Job antérieurs). **⚠️ NextRunAt SUPERSÉDÉ par §4.41 (BUG-25)** :
  passé au fuseau navigateur (`<LiakontDate>`) pour rester cohérent avec LastRunAt en recette.
- `AdminJobScheduleForm.razor` : Créé/Modifié le (ÉVÉNEMENTS) → `<LiakontDate>` ; aperçu cron (PRÉVISION) → UTC
  explicite (cohérent avec le titre « (UTC) » et avec NextRunAt). DÉJÀ au bloc.
- `AdminJobExecutions.razor` : CreatedAt/StartedAt/CompletedAt (exécutions = ÉVÉNEMENTS) → `<LiakontDate>` (helper
  `RenderLocalDate`) ; `FormatUtc`/`FrCulture` retirés (orphelins). AJOUTÉ au bloc `SOCLE-CONSIGNED-DRIFT`.

**Vérification** : `verify-fast` vert ; `Host.Tests.Unit` 963/963 (les tests des pages Job appellent `AddCommonUI()`
qui fournit désormais `IBrowserTimeZone` → aucun échec DI ; repli UTC en bUnit, aucune assertion de date cassée).
La localisation est couverte au niveau composant (`LiakontDateTests`/`LiakontDateDisplayTests`).

### 4.33 `Stratum.Modules.Audit.Web` — horodatages d'admin Audit au fuseau navigateur (RB6 P2)

**Motif** : suite de RB6 (§4.31/§4.32) — mêmes pages d'admin du socle affichant l'heure UTC/serveur. Module Audit :
**que des ÉVÉNEMENTS** (aucune prévision serveur, aucune date de validité) → tous migrés vers `<LiakontDate>`.

**Changement** :
- `AdminAuditDetail.razor` : date du fait (`ActivityDto.CreatedAt`, format `yyyy-MM-dd HH:mm:ss`) et heure d'un
  changement de champ (`FieldChangeDto.OccurredAt`, format `HH:mm:ss`) → `<LiakontDate>` (formats conservés).
- `AdminAuditPolicyForm.razor` : « Créé le » / « Modifié le » de la section Audit (`CreatedAt`/`UpdatedAt`) →
  `<LiakontDate>` (les 2 blocs dupliqués création/vue).
- `AdminAudit.razor` : colonne « Date » du journal (`CreatedAt`, visible par défaut) — `ColumnTemplate` AJOUTÉ avec
  `<LiakontDate>` (la grille rendait sinon le `DateTimeOffset` serveur brut via `value.ToString()`).
- `AdminAuditPolicies.razor` : colonnes « Créé le » (visible) ET « Modifié le » (`defaultVisible:false`) — `ColumnTemplate`
  AJOUTÉ avec `<LiakontDate>` pour les deux.

**Périmètre RB6 — colonnes de grille masquées par défaut MAIS activables = MIGRÉES** : une colonne Date
`defaultVisible:false` (ex. « Modifié le » d'`AdminAuditPolicies`, `CreatedAt` masqués d'Identity §4.34) reste
ACTIVABLE par l'opérateur ; sans template elle rendrait le `DateTimeOffset` serveur brut (`value.ToString()`) — le
même bug de fuseau que RB6 corrige, et une incohérence avec sa colonne sœur. Donc on les migre. Test : la préférence
de grille stub (`FakeGridPreferenceService`) force la colonne visible pour exercer son template (sinon la grille
retombe sur les colonnes par défaut et le template ne serait jamais rendu — faux-vert évité). En revanche les dates
de VALIDITÉ (`DateOnly`/jour : ValidFrom/ValidUntil de délégation, HireDate, échéances) restent telles quelles.

Les registres (`AuditEntryColumnRegistry`/`AuditPolicyColumnRegistry`) **ne sont pas touchés** (migration via le
`.razor`, comme pour Job). Les 4 `.razor` sont AJOUTÉS au bloc `SOCLE-CONSIGNED-DRIFT`.

**Vérification** : `verify-fast` vert ; tests bUnit ajoutés par page (`AdminAuditTests`, `AdminAuditDetailTests`,
`AdminAuditPoliciesTests`, `AdminAuditPolicyFormTests`) via stubs partagés (`AdminPageTestServices.AddAdminPageStubs`) —
repli UTC déterministe en bUnit (sonde absente). La localisation reste couverte au niveau composant.

### 4.34 `Stratum.Modules.Identity.Web` — horodatages d'admin Identity au fuseau navigateur (RB6 P2)

**Motif** : suite de RB6 (§4.31→§4.33). Module Identity = ÉVÉNEMENTS migrés vers `<LiakontDate>` ; **dates de
VALIDITÉ laissées** (convertir au fuseau décalerait le jour).

**Changement — ÉVÉNEMENTS migrés** :
- `AdminUsers.razor` : colonne « Dernière connexion » (`LastLoginAt`, visible) → `<LiakontDate>` (cas null « Jamais » conservé).
- `AdminUserForm.razor` : « Dernière connexion » de la section Audit → `<LiakontDate>`.
- `AdminAgentForm.razor` : « Créé le » / « Modifié le » (section Audit) → `<LiakontDate>`.
- `AdminTeamForm.razor` : « Depuis le » des membres (`JoinedAt`, visible, jour) → `<LiakontDate DateOnly>` ; « Créé le » /
  « Modifié le » (Audit) → `<LiakontDate>`.
- `AdminRoleForm.razor` : « Créé le » (Audit) → `<LiakontDate>`.
- `AdminDelegationForm.razor` : « Créé le » (Audit) → `<LiakontDate>`.
- `AdminAgents.razor` / `AdminTeams.razor` / `AdminDelegations.razor` : colonne « Créé le » (`defaultVisible:false` mais
  ACTIVABLE) → `ColumnTemplate` `<LiakontDate>` (cohérence ; testée via `FakeGridPreferenceService` forçant la visibilité).

**Dates LAISSÉES (validité / jour)** : `ValidFrom`/`ValidUntil` de délégation (`AdminDelegations` colonnes + `AdminDelegationForm`
inputs), `HireDate` et compétence `ValidUntil` (`AdminAgentForm`, `DateOnly`). Les calculs de statut (`DateTimeOffset.UtcNow`
comparé à ValidFrom/ValidUntil) ne sont pas des affichages → inchangés.

Les registres `*ColumnRegistry.cs` **ne sont pas touchés**. Les 9 `.razor` sont AJOUTÉS au bloc `SOCLE-CONSIGNED-DRIFT`.
`AdminRoles.razor` (liste) n'a aucune date → non modifié, hors périmètre.

**Vérification** : `verify-fast` vert ; 1 test bUnit par page modifiée (`AdminUsersTests`, `AdminUserFormTests`,
`AdminAgentsTests`, `AdminAgentFormTests`, `AdminTeamsTests`, `AdminTeamFormTests`, `AdminRoleFormTests`,
`AdminDelegationsTests`, `AdminDelegationFormTests`) + fakes de query partagés ; assertion discriminante sur
`AdminDelegations` (ValidFrom rendue SANS suffixe « UTC » → preuve qu'elle est laissée).

### 4.35 `Stratum.Modules.Notification.Web` — horodatages d'admin Notification au fuseau navigateur (RB6 P2)

**Motif** : suite de RB6 (§4.31→§4.34) — dernier module d'admin du socle. ÉVÉNEMENTS migrés vers `<LiakontDate>` ;
ÉCHÉANCES/DURÉES/dates de VALIDITÉ laissées.

**Changement — ÉVÉNEMENTS migrés** :
- `AdminSla.razor` : colonne « Modifié le » (`UpdatedAt ?? CreatedAt`, onglet Config, `<StratumColumn>`) + « Envoyé le »
  (`SentAt` d'un breach, onglet Monitoring) → `<LiakontDate>`.
- `AdminSlaForm.razor` / `AdminCatalogServiceForm.razor` / `AdminNotificationRoutingDetail.razor` : « Créé le » /
  « Modifié le » (section Audit) → `<LiakontDate>`.
- `AdminNotificationTemplates.razor` : colonne « Dernière modif. » (`UpdatedAt ?? CreatedAt`, visible) + « Créé le »
  (masquée activable) → `ColumnTemplate` `<LiakontDate>`.
- `AdminNotificationRouting.razor` / `AdminCatalogServices.razor` : colonne « Créé le » (masquée activable) → `<LiakontDate>`.
- `AdminWebhookSubscriptions.razor` : « Créé le » (visible) + « Modifié le » (masquée activable) → `<LiakontDate>`.
- `AdminIntegrations.razor` : colonne « Créée le » des clés API (`CreatedAt`, visible) → `<LiakontDate>`.

**LAISSÉES** : `SlaCountdown` (échéances SLA calculées), durées (`MaxDelaySeconds`/`FormatDelay`, `DefaultSlaHours`,
intervalle de sync, `SimulationDuration`), et la date d'EXPIRATION de clé API `ApiKey.ExpiresAt` (date de
validité/échéance). `AdminNotificationTemplateDetail`/`AdminNotificationPreview` : strings d'exemple, non concernés.

Les registres `*ColumnRegistry.cs` **ne sont pas touchés**. Les 9 `.razor` sont AJOUTÉS au bloc `SOCLE-CONSIGNED-DRIFT`.

**Vérification** : `verify-fast` vert ; 1 test bUnit par page modifiée (`AdminSlaTests`, `AdminSlaFormTests`,
`AdminCatalogServiceFormTests`, `AdminNotificationRoutingDetailTests`, `AdminNotificationTemplatesTests`,
`AdminNotificationRoutingTests`, `AdminCatalogServicesTests`, `AdminWebhookSubscriptionsTests`, `AdminIntegrationsTests`)
+ fakes de query ; assertion discriminante sur `AdminIntegrations` (`ExpiresAt` rendue SANS suffixe « UTC »).

### 4.36 Lecture des `timestamptz` — casts directs `(DateTimeOffset)row.x` corrigés (recette RB, bug bloquant)

**Contexte** : en recette (env Bucodi neuf), les pages d'admin du socle (supervision/alertes, admin Job,
Notification, Identity) levaient `System.InvalidCastException: Invalid cast from 'System.DateTime' to
'System.DateTimeOffset'`. Npgsql renvoie un `DateTime` (Kind=Utc) pour une colonne `timestamptz` ; le code
socle lisait ces colonnes par **cast direct** `(DateTimeOffset)row.x` / `(DateTimeOffset?)row.x` et un
`ExecuteScalar<DateTimeOffset?>`, ce qui échoue à l'exécution. Latent depuis le vendoring (SOL01), masqué
par les tests bUnit (données mockées, jamais de Postgres réel). Les modules **Liakont** n'étaient pas
touchés (chacun a un `RowReader` robuste, ex. `TenantSettingsRowReader.ToDateTimeOffset`).

**Correctif** :
- **AJOUTÉ** `src/Common/Infrastructure/Database/DbTimestamp.cs` (`Stratum.Common.Infrastructure.Database`,
  fichier Liakont) : `ToDateTimeOffset(object)` / `ToNullableDateTimeOffset(object?)` — même contrat que les
  RowReader Liakont (`DateTime` → `new DateTimeOffset(SpecifyKind(dt, Utc))`, `DateTimeOffset` inchangé,
  `null`/`DBNull` → `null`). L'argument est casté en `(object)` à l'appel pour ne pas propager `dynamic`.
- **MODIFIÉ** (casts directs → `DbTimestamp`) : `Stratum.Modules.Job.Infrastructure` (PostgresJobQueries +
  `GetLastCompletedAtByTypeAsync` via `object?`, PostgresScheduleQueries, PostgresJobExecutionsQueries,
  PostgresJobUnitOfWork, PostgresScheduleUnitOfWork) ; `Stratum.Modules.Notification.Infrastructure`
  (PostgresNotificationUnitOfWork + 8 Queries) ; `Stratum.Modules.Identity.Infrastructure`
  (PostgresIdentityUnitOfWork + Delegation/Agent/Team/Identity Queries). **Audit NON touché** (lisait déjà
  via `new DateTimeOffset((DateTime)row.x, TimeSpan.Zero)`). Le module Liakont `TvaMapping`
  (PostgresTvaMappingQueries, `occurred_at`) est corrigé de la même façon (hors socle).

**Vérification** : build Release `0/0` (StyleCop) ; `DbTimestampTests` (unitaire) ; recette manuelle des
pages admin socle (plus d'`InvalidCastException`). Aucun `*ColumnRegistry` ni `.razor` touché.

### 4.37 Journal des régénérations du baseline de provenance (`-Generate`) — RDL09
Chaque exécution de `tools/socle-provenance-check.ps1 -Generate` « cuit » l'état courant de l'arbre
dans `tools/socle-baseline.sha1` ; c'est un acte délibéré qui REMPLACE la référence et doit donc être
tracé (item, commit, raison, décompte). Avant RDL09 une régénération « OPS03 » avait été faite sans
trace (A6-prov-2) : le décompte codé en dur en §4.12 (« 1226 ») était périmé (réel 1249) et la
régénération elle-même invisible. Désormais, toute régénération s'inscrit ici.

| Date | Item | Raison | Décompte fichiers épinglés |
|---|---|---|---|
| (non daté, antérieur à RDL09) | OPS03 (non consigné à l'époque) | Régénération après ajouts sous racines vendorées ; a par effet de bord épinglé les ajouts Liakont (cause d'A6-prov-1) | 1249 |
| 2026-06-19 | RDL09 | Exclure les ajouts Liakont du périmètre épinglé (marqueur de tête `Liakont addition`) → lever la contradiction baseline↔doc §4.12/§4.14/ADR-0006 ; rebaser sur les seuls `Stratum.*` | 1226 |

> Note RDL08 (2026-06-20) : **aucune régénération** — la modification de `JobScheduler`/`IJobQueries`/
> `PostgresJobQueries` est absorbée par le bloc `SOCLE-CONSIGNED-DRIFT` (§4.38), pas par une re-cuisson du
> baseline (qui resterait la référence stable). Le baseline reste à 1226.

Au-delà du décompte, la régénération RDL09 a aussi : (a) ajouté le marqueur de tête `Liakont addition`
aux 21 ajouts Liakont qui en étaient dépourvus (les 13 ajouts SOL06 le portaient déjà) — total 34
ajouts désormais tous marqués et exclus ; (b) retiré du bloc `SOCLE-CONSIGNED-DRIFT` deux entrées qui
étaient des ajouts Liakont (donc plus jamais épinglés) :
`src/Modules/Job/Web/Pages/AdminJobExecutions.razor` et
`src/Modules/Job/Infrastructure/Queries/PostgresJobExecutionsQueries.cs`.

### 4.38 RDL08 — dé-duplication à l'enqueue des jobs récurrents (anti-empilement)
RDL08 (redline ADR-0006, finding A6-scale-2) ajoute une garde de dé-duplication à l'enqueue : le
`JobScheduler` récurrent ne doit pas empiler un déclencheur identique quand un job du même type/portée est
déjà `Pending` (sinon un fan-out plus long que la cadence cron affame le worker mono-job). La logique de
décision et la requête vivent côté **Liakont** (ajouts non épinglés, marqués `// Liakont addition (RDL08)`) :
`Stratum.Common.Abstractions.Jobs.IRecurringJobEnqueueGuard` (+ impl. Host `RecurringJobEnqueueGuard`),
`TenantJobRunnerOptions` (budget par tenant, A6-scale-3). Trois fichiers `Stratum.*` épinglés sont **modifiés**
(ajouts additifs uniquement, aucune logique métier socle changée) :

- `src/Modules/Job/Contracts/Queries/IJobQueries.cs` (déjà consigné) : signature `HasPendingJobOfTypeAsync`.
- `src/Modules/Job/Infrastructure/Queries/PostgresJobQueries.cs` (déjà consigné) : implémentation SQL
  (`EXISTS … status = 'Pending' AND company_id IS NOT DISTINCT FROM …`). `Pending`-only (jamais `Running`)
  pour ne pas bloquer sur un `Running` orphelin — ADR-0006 §5.2.
- `src/Modules/Job/Infrastructure/JobScheduler.cs` (**AJOUTÉ** au bloc CONSIGNED-DRIFT) : avant `InsertJobAsync`,
  consulte `IRecurringJobEnqueueGuard` (résolu en option, `GetService` — comportement inchangé si absent) ;
  si suppression, avance `next_run_at` et saute l'enqueue avec un log `Information` structuré.

`TenantJobRunner.cs` (ajout SOL06, NON épinglé) gagne le budget par tenant (linked CTS). Aucune table ni
migration socle modifiée. La dérive de ces trois fichiers est absorbée par le bloc `SOCLE-CONSIGNED-DRIFT`
ci-dessus — **le baseline n'est PAS régénéré** (il reste la référence stable ; régénérer est optionnel, §4.12).
**Vérification** : `verify-fast` (2 solutions, dont `socle-provenance-check` exit 0) + `run-tests` (unit
garde/runner + intégration `HasPendingJobOfTypeAsync` et seam DI réel sur 2 bases).

### 4.39 BUG-4 volet A — échec d'enregistrement de `DeclaredFormPage` rendu VISIBLE
Recette EncheresV6 : sur un formulaire bâti sur `DeclaredFormPage` (socle), un échec de save **non
mappé à un champ** (ex. `AdminJobScheduleForm.CreateAsync` qui lève `InvalidOperationException`
« Aucune société sélectionnée. » pour un opérateur sans société courante) restait **SILENCIEUX** : le
bouton « Enregistrer » semblait inerte, aucune bannière d'erreur. Cause : `MapDomainError` (le `MapError`
du formulaire) écrivait le champ `_globalError` du composant **parent**, mais la bannière est rendue
depuis le **paramètre** `GlobalError` de l'enfant ; comme le clic est géré par l'enfant, le parent ne se
re-rendait pas et la valeur fraîche ne redescendait jamais dans le paramètre → bannière vide. (Les erreurs
PAR CHAMP, elles, s'affichaient car lues en direct dans le `RenderFragment Content` du parent ré-évalué au
rendu de l'enfant — seul le cas global était avalé.)

Correctif (modification additive d'un fichier `Stratum.*` épinglé, marqué `// BUG-4 volet A` dans le code) :
- `src/Common/UI/Components/DeclaredFormPage.razor.cs` (**AJOUTÉ** au bloc CONSIGNED-DRIFT) : le paramètre
  `MapDomainError` passe de `Action<string>` à `Func<string, string?>`. Il retourne `null` quand le
  formulaire a routé l'erreur vers un champ (rien à afficher globalement), sinon le **message à afficher**
  (éventuellement reformulé). Dans le `catch` de `PerformSaveAsync`, ce message non-null passe par
  `SetGlobalError` — qui invoque l'`EventCallback` `GlobalErrorChanged` (re-rend le parent), garantissant la
  visibilité. Le chemin succès et le mappage par champ sont INCHANGÉS.

Les 9 formulaires consommateurs adaptent leur `MapError`/`HandleDomainError` (retour `string?` : `null` si
mappé à un champ, le message sinon — au lieu d'écrire `_globalError` directement, désormais possédé par le
socle). Huit étaient déjà consignés (§4.20, §4.31–4.35) ; `AdminWebhookForm.razor` (qui reformule un code
d'erreur en libellé FR) est **AJOUTÉ** au bloc CONSIGNED-DRIFT. Le champ `_globalError` reste la cible du
`@bind-GlobalError` (renseignée par le socle via le callback). Le baseline n'est PAS régénéré (la dérive est
absorbée par le bloc `SOCLE-CONSIGNED-DRIFT`). **Vérification** : build solution (0 warning), test bUnit
ciblé `AdminJobScheduleFormTests.Save_Failure_Without_Current_Company_Shows_A_Visible_Error` (échec VISIBLE,
contrôle négatif confirmé) + suites `Liakont.Host.Tests.Unit` (1071) et `Stratum.Common.UI.Tests.Unit` (802)
vertes.

### 4.40 BUG-4 volet B — jobs SYSTÈME planifiables/consultables par un opérateur PLATEFORME (société porteuse)
Recette EncheresV6 : un opérateur PLATEFORME (super-admin cross-tenant, sans `company_id` dans son jeton —
`ActorContext.Current.CompanyId == null`) ne pouvait planifier AUCUN job (`AdminJobScheduleForm.CreateAsync`
levait « Aucune société sélectionnée. ») ni VOIR les planifications (`AdminJobSchedules` renvoyait une liste
vide). Or un job SYSTÈME (fan-out tous tenants : supervision, ancrage, e-reporting B2C…) n'appartient à AUCUN
tenant. Constat clé (vérifié sur pièce) : `schedule.CompanyId` n'a **aucun effet de portée** à l'exécution
d'un fan-out — le runner SOL06 (`TenantJobRunner`) itère tous les tenants via `ITenantQueries`, indépendamment
de cette valeur ; ce n'est qu'une clé d'unicité `(name, company)` et de dé-duplication d'enqueue
`(type, company)` (ADR-0006). Pas de FK sur `job.schedules.company_id` (migration `V003`), donc une société
porteuse sentinel s'insère sans contrainte référentielle.

Correctif — une abstraction de **société porteuse système** (`ISystemScheduleHost`) : un job système est porté
par UNE société porteuse plateforme (sentinel, pas un tenant réel), planifiable et consultable sans société
courante ; un job tenant-scopé garde le comportement actuel (société courante requise, échec VISIBLE — §4.39).
La MÊME porteuse sert au formulaire, à la liste cross-tenant ET à l'amorçage de dev, pour qu'une planification
système soit UNIQUE (sinon un opérateur recréerait des doublons invisibles → double fan-out).

- **Fichiers `Stratum.*` AJOUTÉS** (marqués `// Liakont addition (BUG-4b)` en première ligne → exclus du
  périmètre épinglé §4.12, comme SOL06/FIX211 ; **PAS** dans le bloc CONSIGNED-DRIFT) :
  - `src/Modules/Job/Contracts/Services/ISystemScheduleHost.cs` : l'abstraction (résout la société porteuse
    d'un type de job système ; `null` = tenant-scopé).
  - `src/Modules/Job/Infrastructure/Services/NullSystemScheduleHost.cs` : défaut no-op (socle auto-suffisant —
    aucun job « système », comportement du socle nu préservé).
- **Fichiers `Stratum.*` MODIFIÉS** (additifs ; déjà présents au bloc CONSIGNED-DRIFT depuis FIX211 §4.20 — le
  baseline n'est PAS régénéré, la dérive est absorbée) :
  - `src/Modules/Job/Infrastructure/JobModuleRegistration.cs` : `TryAddSingleton<ISystemScheduleHost,
    NullSystemScheduleHost>()` (défaut écrasable par le Host).
  - `src/Modules/Job/Web/Pages/AdminJobScheduleForm.razor` (`CreateAsync`) : `companyId =
    SystemScheduleHost.ResolveHostCompanyId(_jobType) ?? CurrentCompany ?? throw`.
  - `src/Modules/Job/Web/Pages/AdminJobSchedules.razor` (`LoadSchedulesAsync`) : `companyId = CurrentCompany
    ?? SystemScheduleHost.CrossTenantHostCompanyId` (liste de la porteuse pour un admin cross-tenant).

Côté Liakont (hors socle, non consigné par le baseline) : `LiakontSystemScheduleHost` (impl backée par la
source unique `SystemJobDefinitions.All` + sentinel `5c8ed001-…-b000-…0001`), enregistrée dans `AppBootstrap`
APRÈS `AddJobModule` (gagne la résolution sur le défaut socle) ; `DevJobScheduleSeeder` amorce désormais sur
cette MÊME porteuse (au lieu de `DevTenantSeed:CompanyId`). **Vérification** : verify-fast (Debug + Release),
run-tests, tests bUnit `AdminJobScheduleFormTests`/`AdminJobSchedulesTests` (planif + liste d'un opérateur
plateforme) + `LiakontSystemScheduleHostTests` (résolution + cohérence du sentinel).

### 4.41 BUG-25 — `AdminJobSchedules` : « Prochaine exécution » au fuseau du navigateur (cohérence avec « Dernière »)

Recette EncheresV6 (Karl, 27/06) : la colonne **« Prochaine exécution »** (`NextRunAt`) s'affichait en **UTC
explicite** (suffixe « UTC ») tandis que **« Dernière exécution »** (`LastRunAt`) passait au fuseau du
**navigateur** (`<LiakontDate>`, RB6 §4.32). Résultat trompeur : pour un job qui vient de tourner, la prochaine
(UTC) *paraissait antérieure* à la dernière (locale) — alors qu'en UTC elle est bien postérieure. Pur mélange de
fuseaux à l'affichage (l'ordonnancement `*/n` est correct).

**Changement** — `src/Modules/Job/Web/Pages/AdminJobSchedules.razor` : le `ColumnTemplate` de `NextRunAt` passe
de la mise en forme UTC inline (`UtcDateTime.ToString(... 'UTC')`) à `<LiakontDate Value="item.NextRunAt" />` —
**même composant, même fuseau (navigateur)** que `LastRunAt`. **SUPERSÈDE** la règle « PRÉVISION serveur → UTC
explicite » posée en §4.32 pour CETTE colonne : la cohérence visuelle entre les deux colonnes (et la non-confusion
de l'opérateur) prime sur le distinguo prévision/événement. `LiakontDate` conserve le repli UTC EXPLICITE tant que
le fuseau navigateur n'est pas résolu (pré-rendu) — aucune heure locale fausse. Fichier DÉJÀ au bloc
`SOCLE-CONSIGNED-DRIFT` (items Job antérieurs) ; aucun nouveau pin.

**Vérification** : bUnit `AdminJobSchedulesTests.Next_And_Last_Run_Render_In_The_Same_Browser_Timezone` (fuseau
Europe/Paris résolu → 08:15 UTC = 10:15, 07:50 UTC = 09:50, aucun suffixe « UTC ») ; le test existant
`Job_Type_Column_…` garde l'assertion de repli UTC (fuseau non résolu en bUnit). La conversion elle-même reste
couverte par `LiakontDateTests`/`LiakontDateDisplayTests`.

### 4.42 BUG-19 — navigation précédent/suivant en vue détail (transverse) via le gabarit de liste

Recette EncheresV6 (Karl, 26/06) : depuis une vue DÉTAIL, aucun moyen de passer au document SUIVANT sans revenir
à la grille (très pénible pour parcourir les bloqués un par un). Besoin TRANSVERSE (documents, émissions, agents…),
parcourant la liste **filtrée/triée telle qu'affichée**. C'est un comportement du gabarit Stratum `DeclaredListPage`.

**Conception — blast radius minimal** : l'ORDRE AFFICHÉ vit dans `DeclaredListPage._filteredItems` (privé). On le
capture au clic d'une ligne sous forme d'URLs de détail, dans une mémoire de circuit ; la vue détail résout ses
voisins par l'URL courante. Aucune entité codée en dur — l'identité est l'URL de détail (vraie transversalité).

- **Additions Liakont** (NON vendored, hors baseline — fichiers neufs, marqués « Liakont addition ») :
  `src/Common/UI/Navigation/IListNavigationContext.cs` + `ListNavigationNeighbors.cs` + `ListNavigationContext.cs`
  (mémoire de circuit `Scoped`, résolution des voisins, normalisation d'URL casse/slash/query) ;
  `src/Common/UI/Components/RecordNavigator.razor` (composant socle préc/suiv, masqué hors contexte de liste).
- **Modifs VENDORED (consignées ici)** :
  - `src/Common/UI/Components/DeclaredListPage.razor.cs` : `[Inject] IListNavigationContext?` + helper
    `CaptureListNavigationContext()` (mappe `_filteredItems` → URLs de détail) appelé dans `HandleRowActivated`
    ET `HandleMultiViewRowActivated` AVANT la navigation. Sans effet si `DetailUrl` nul ou service absent
    (rétro-compatible : aucune liste existante n'est impactée).
  - `src/Common/UI/CommonUIServiceExtensions.cs` (DÉJÀ au bloc `SOCLE-CONSIGNED-DRIFT`, RB6 §4.31) :
    enregistrement `AddScoped<IListNavigationContext, ListNavigationContext>()` (un état par circuit).
- **Câblage Liakont (hors socle)** : `<RecordNavigator />` dans `DocumentDetail.razor` et `B2cMarginEmissionDetail.razor`
  (les deux entités exigées par la transversalité ; tout autre détail atteint depuis un `DeclaredListPage` peut
  l'ajouter d'une ligne).

**Vérification** : `ListNavigationContextTests` (bornes, hors-liste, normalisation, remplacement transverse) +
`RecordNavigatorTests` bUnit (rendu préc/suiv + position, bornes désactivées, « Suivant » navigue, masqué hors
contexte). Build Release 0/0 ; garde `socle-provenance-check` verte (drift consigné).

### 4.43 BUG-16 — densité par défaut « standard » (alignée sur le défaut du modèle métier)

Recette EncheresV6 (Karl, 26/06) : un NOUVEL utilisateur (sans préférence enregistrée) atterrissait en densité
**« compact »** alors que le modèle métier Liakont a pour défaut **« standard »** (`UserPreferences.Density =
DensityStandard`, déjà couvert par `UserPreferencesTests`). Cause : le socle applique son défaut « Civic Blueprint
= compact » dans le prélude JS (avant la connexion du circuit), qui prime sur le défaut du modèle (appliqué après).
Le `localStorage` étant vide pour un nouvel utilisateur, le repli `compact` s'affichait sans moyen de le corriger
avant le rendu.

**Changement VENDORED (consigné ici)** — `src/Common/UI/wwwroot/js/stratum-ui.js` : le repli de densité passe de
`'compact'` à `'standard'` aux trois points de défaut (IIFE `apply()`, `MutationObserver` de l'enhanced navigation,
et `getDensity()`), pour aligner le socle sur le défaut du modèle Liakont. Sans ce changement, le `MutationObserver`
du socle réimposerait `compact` à chaque navigation, annulant le correctif côté hôte. Aucun nouveau pin (drift
consigné). Le prélude inline `App.razor` (Liakont Host, **non** vendored) est aligné en parallèle (même défaut
`standard`) pour le tout premier rendu (avant l'évaluation des tokens CSS).

**Vérification** : `UserPreferencesTests.Default_…StandardDensity` (le modèle attend déjà `standard`) ; le socle JS
ne porte aucune logique C# testable en bUnit (constante de repli côté navigateur) ; garde `socle-provenance-check`
verte (drift consigné). Recette manuelle Karl : un nouvel utilisateur voit « standard ».

**Garde de non-régression — explicitement MANUELLE (no silent cap).** Le défaut pré-paint vit dans une constante JS
évaluée AVANT la connexion du circuit Blazor : il n'est testable ni en bUnit (pas de DOM/JS) ni en intégration
in-process, et le Host ne porte aucun projet Playwright/E2E navigateur en V1. Un retour silencieux des quatre points
de défaut à `compact` ne serait donc PAS rattrapé par un test automatisé — seulement par la recette manuelle
ci-dessus. Dette assumée et tracée : si un projet E2E navigateur Host est introduit, y ajouter le cas « nouvel
utilisateur sans préférence → `document.documentElement[data-density] === 'standard'` » pour fermer ce trou.

### 4.44 GDF01 — registre de types d'événements outbox peuplé AU BUILD DI (course de démarrage supprimée)

Review d'intégration GED (2026-07-02, CONFIRMED) : une course de démarrage constructible faisait perdre
SILENCIEUSEMENT des événements outbox pendants. L'`OutboxWorker` (`BackgroundService`, enregistré par
`AddStratumEvents`) démarre AVANT les `...EventTypeRegistrar` (`IHostedService`) et son `ExecuteAsync`
polle immédiatement ; un `event_type` non encore enregistré est vu « inconnu » et marqué `processed` à
vide, sans dead-letter (`OutboxWorker.cs` — comportement INCHANGÉ, correct pour un vrai typo qui, sinon,
re-pollerait à l'infini). Au redémarrage du Host avec des `ged.managed-document.received` pendants →
documents jamais indexés. La correction déplace l'enregistrement des types de « au démarrage des hosted
services » vers « à la construction du registre » (au build DI), donc AVANT tout poll.

**Changement VENDORED (consigné ici)** — `src/Common/Infrastructure/Events/ServiceCollectionExtensions.cs`
(`AddStratumEvents`) : la registration de `IEventTypeRegistry` passe d'un simple
`AddSingleton<IEventTypeRegistry, EventTypeRegistry>()` à une **factory** qui, à la construction du
singleton, applique **tous les `IEventTypeRegistrar` enregistrés** (`sp.GetServices<IEventTypeRegistrar>()`).
Comme le worker (et les consommateurs) résolvent `IEventTypeRegistry` à leur propre construction — au
tout début de `IHost.StartAsync`, avant l'exécution du moindre `StartAsync` de hosted service — le registre
est déjà peuplé des types contribués avant le premier poll. Un enregistrement TARDIF via `IHostedService`
reste possible (il mute le même singleton mutable `EventTypeRegistry`) : les modules socle non migrés
(Identity/Job/Notification) gardent leur `...EventTypeRegistrar` `IHostedService` inchangé — **hors périmètre
GDF01** (nommé GED + Ingestion), comportement STRICTEMENT préservé, aucune régression. Aucun `EventTypeRegistry`
n'est modifié (l'API `Register` reste identique).

**Périmètre = les canaux à consommateur durable (risque de perte OBSERVABLE).** La perte silencieuse au
démarrage n'a d'effet QUE si l'événement pendant possède un `IIntegrationEventConsumer<T>` durable : sans
consommateur, « marqué processed à vide (type inconnu) » et « dispatché vers zéro consommateur » sont
strictement équivalents (no-op). Or les SEULS types d'événements portant un consommateur durable sont
`ingestion.document.received` / `ingestion.source.altered` (Pipeline, Documents) et
`ged.managed-document.received` (indexeur + projecteur GED) — exactement les deux canaux migrés par GDF01.
Les événements socle Identity/Job/Notification (`identity.user.*`, `job.job.*`, `notification.email.*`)
n'ont **aucun** consommateur durable enregistré dans Liakont : leur course de démarrage pré-existante est
donc **sans effet observable** aujourd'hui, ce qui justifie de NE PAS migrer ces trois registrars socle
(éviter 6 dérives socle pour un bénéfice comportemental nul, règle n°11). **Suivi (RESTE OUVERT) :** si un
canal socle reçoit un jour un consommateur durable, migrer son registrar vers `IEventTypeRegistrar` +
`AddSingleton<IEventTypeRegistrar, …>()` (même patch mécanique, fichier socle → à consigner ici) AVANT de
le brancher, sinon la course redevient une perte silencieuse réelle.

**Fichier AJOUTÉ (non épinglé)** — `src/Common/Infrastructure/Outbox/IEventTypeRegistrar.cs` : nouvelle
abstraction de contributeur, porteuse du marqueur de tête `// Liakont addition (GDF01)` (exclue du périmètre
épinglé par §4.12, comme §4.14). Les registrars Ged/Ingestion (code **Liakont**, `Liakont.Modules.*`, non
épinglés) l'implémentent désormais au lieu d'`IHostedService`, et sont enregistrés via
`AddSingleton<IEventTypeRegistrar, …>()`.

**Vérification** : test de mécanisme (`Common.Infrastructure.Tests.Unit` — un `IEventTypeRegistrar` de test
est appliqué à la construction du registre, prouvé SANS démarrer aucun hosted service) ; test d'intégration
GED end-to-end **via le VRAI `OutboxWorker`** (événement `ged.managed-document.received` pendant → réellement
dispatché et indexé, JAMAIS marqué processed à vide — ne contourne PAS le worker, contrairement aux tests
d'ingestion existants) ; test Ingestion (le registre issu de `AddStratumEvents + AddIngestionModule` connaît
`ingestion.document.received` / `ingestion.source.altered`). Garde `socle-provenance-check` verte (drift
consigné ci-dessous). Aucun nouveau pin.

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

### 6.1 Mesure de dérive CUMULÉE vs le commit source `1454c7f` (RDF16 / RL-SOCLE-1)

La garde automatique (§4.12, `socle-provenance-check.ps1`) mesure la dérive **vs le baseline
post-régénération** (`socle-baseline.sha1`), donc elle **ne voit pas** les modifications
d'adaptation **cuites dans le baseline** au moment de sa génération (vendoring SOL01 + §4.1–4.11).
La **vraie** dette de re-convergence se mesure **vs le commit source `1454c7f`** lui-même.

**Mesure publiée (au 2026-06-20, finding RL-SOCLE-1).** Comparaison fichier à fichier des racines
vendorées (`src/Common/{Abstractions,Infrastructure,UI,Testing}`,
`src/Modules/{Identity,Job,Notification,Audit}`, `src/Modules/Party/Contracts`) entre `HEAD` et
`Stratum@1454c7f`, contenu **normalisé** (CRLF→LF) :

| Catégorie | Fichiers | Note |
|---|---|---|
| Source `1454c7f` dans le périmètre | **1234** | |
| Copiés dans Liakont (présents dans les deux) | **1226** | = le set épinglé par le baseline §4.12 |
| — dont **identiques** au source | **1153** | aucune dette |
| — dont **MODIFIÉS** (dérive réelle) | **73** | **la dette de re-convergence** |
| **NON copiés** (présents source, absents Liakont) | **8** | tous = `src/Modules/Audit/Tests.*` (NON vendorés délibérément, §4.5) |
| **AJOUTÉS** par Liakont sous les racines vendorées | **34** | code net-neuf marqué `// Liakont addition` (SOL06/FIX211/RLM…), non reversable tel quel |

**Lecture :** la dérive cumulée réelle est de **73 fichiers modifiés** (≈ 5,9 % des 1226 copiés),
contre **67** fichiers actuellement épinglés-driftés dans le bloc `SOCLE-CONSIGNED-DRIFT` (§4.12) —
l'écart de ~6 fichiers correspond aux modifications d'adaptation cuites dans le baseline, invisibles
à la garde post-régénération. Répartition des 73 : Notification 23, Identity 15, Job 13, Common/UI
10, Common/Infrastructure 5, Audit 4, Common/Abstractions 3. Une part de ces modifications est
**structurellement Liakont** (ex. §4.31–4.35 fuseau navigateur, §4.20 admin des jobs, §4.24–4.28
provisioning de tenant) et **n'est pas reversable** en l'état vers Stratum amont.

**Méthode (reproductible, côté développeur — PAS une garde CI).** La mesure exige le dépôt source
`Stratum` (commit `1454c7f`) en local ; elle n'est donc **pas** ajoutée à `verify-fast` (qui ne doit
pas dépendre d'un second dépôt). Reproduire : pour chaque fichier des racines ci-dessus présent dans
les deux dépôts, comparer `git show HEAD:<f>` et `git show 1454c7f:<f>` après normalisation des fins
de ligne ; compter identiques / modifiés, plus les ajoutés (Liakont seul) et non-copiés (source seul).

### 6.2 Politique de BACKPORT des correctifs de sécurité Stratum amont (RL-SOCLE-1)

ADR-0001 (Conséquence 1) **reconnaît** que la copie vendorée **ne reçoit pas automatiquement** les
correctifs de Stratum, **sans trancher** de politique. Politique actée par RDF16 :

1. **Pas de descente automatique** (confirmé) : aucun mécanisme ne tire les correctifs amont — c'est
   le prix assumé de l'option C.
2. **Backport manuel des correctifs de SÉCURITÉ amont** : quand un correctif de sécurité Stratum
   touche un fichier **présent dans le vendoring set** (les 1226 ci-dessus), il est **porté à la
   main** dans la copie vendorée et **consigné en §4** le jour même (comme toute modification
   `Stratum.*`). Les correctifs non-sécurité sont laissés à l'appréciation (pas d'obligation).
3. **Veille** : la responsabilité de surveiller les avis de sécurité Stratum amont est une **tâche
   d'exploitation** (pas automatisée à ce jour) — à réévaluer si le volume le justifie.

### 6.3 Requalification de l'option D (RL-SOCLE-1)

ADR-0001 décrit l'option D (retour aux packages NuGet) comme « possible et **explicitement visée à
terme** ». Le redline montre que cette réversibilité **n'est pas exercée** et que sa dette croît
(§6.1 : 73 fichiers driftés, dont une part non reversable). **Statut requalifié par RDF16 :**

> **Option D — re-convergence NuGet : NON PLANIFIÉE à ce jour, à réévaluer.** Elle reste la cible
> *conceptuelle* (le nommage `Liakont.*` / `Stratum.*` et cette provenance la préparent), mais
> **aucune** échéance ni budget ne lui sont alloués en V1.

**Critère de réévaluation** (déclencher l'étude D quand l'un est vrai) :

- les **besoins socle** de Liakont sont stabilisés (plus d'afflux de modifications `Stratum.*` sur
  plusieurs jalons) **ET** la part **reversable** de la dérive (§6.1) justifie l'effort de packaging ;
- **ou** la dérive cumulée (§6.1) franchit un seuil d'exploitation où la maintenance de la copie
  vendorée coûte plus que la friction de packaging — à mesurer, pas à supposer.

Tant que le critère n'est pas atteint, l'option D **n'est pas un engagement** ; les ADR qui la
citent « à terme » sont à lire à travers cette requalification (avenant ADR-0001 du 2026-06-20, RDF16).
