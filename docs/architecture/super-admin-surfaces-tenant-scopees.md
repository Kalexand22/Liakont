# Note d'architecture — Super-admin d'instance et surfaces tenant-scopées (décision F5b / RLF04)

- **Statut** : **Décision prise** (2026-06-16).
- **Nature** : décision de design — **raffine** [ADR-0021](../adr/ADR-0021-realm-keycloak-unique-isolation-par-claim.md)
  (realm unique, super-admin hors périmètre tenant) sur le cas précis des **surfaces et actions
  tenant-scopées** vues par le super-admin d'instance. N'invente aucune règle ni permission.
- **Source** : finding `[DESIGN] F5b` de la recette `GATE_REALM_UNIQUE`
  (`tasks/recette-gate-realm-unique.md`, 2026-06-15) — item d'orchestration **RLF04**.
- **Portée** : produit (générique, multi-tenant). Aucune donnée client.

## Contexte

Depuis [ADR-0021](../adr/ADR-0021-realm-keycloak-unique-isolation-par-claim.md), le tenant courant
est **dérivé du jeton** : le résolveur autoritaire `CompanyClaimTenantResolver` lit le claim
`company_id` et, **en son absence, renvoie `null`**
(`src/Host/Liakont.Host/MultiTenancy/CompanyClaimTenantResolver.cs:61-66`). Le **super-admin
d'instance** (`SuperAdminRoles` : `Admin` / `SystemAdmin` / `stratum-admin`,
`src/Host/Liakont.Host/Security/SuperAdminRoles.cs:12-45`) est un **compte de plateforme sans
`company_id`** : il n'est **jamais résolu vers un tenant** (ADR-0021 §2b, INV-0021-4). Il est
explicitement **exempté** du cross-check et de la suspension
(`TenantCompanyCrossCheckMiddleware.cs:82-85`, `TenantSuspensionMiddleware.cs:37`).

Or, dans la nav opérateur, le super-admin **court-circuite** la garde de permission :
`ClaimsPermissionService.HasPermission(p) => _isSuperAdmin || _permissions.Contains(p)`
(`src/Host/Liakont.Host/Security/ClaimsPermissionService.cs:30`, `:78`) et
`PermissionAuthorizationHandler` `Succeed()` pour tout super-admin
(`src/Host/Liakont.Host/Security/PermissionAuthorizationHandler.cs:20-23`). Conséquence :
`LiakontNavNodeProvider` lui présente **toutes** les entrées, dont les surfaces **tenant-scopées** —
Documents / Encaissements / Traitements (non gardées,
`src/Host/Liakont.Host/Navigation/LiakontNavNodeProvider.cs:31-33`) et le sous-menu **Paramétrage**
(servi en entier au super-admin via le court-circuit `HasPermission(Settings)`,
`LiakontNavNodeProvider.cs:90-116`). Ces pages exigent un tenant courant ; le super-admin n'en a pas
→ **cul-de-sac** (« Documents » ouvre une page sans tenant résolu).

**Question à trancher (F5b) :** que fait le super-admin de ces surfaces, et **doit-il pouvoir agir
cross-tenant** sur les documents d'un tenant ? Deux directions étaient posées : **(1)** masquer les
surfaces tenant-scopées au super-admin ; **(2)** ouvrir l'action cross-tenant via un **sélecteur de
tenant explicite** en s'appuyant sur l'audit `DocumentEvent` existant.

## Décision

### §1 — Par défaut : masquer les surfaces tenant-scopées au super-admin (option 1, immédiat)

Le super-admin d'instance est, par construction (ADR-0021), **hors périmètre tenant**. Ses surfaces
cross-tenant **légitimes** existent déjà et sont **explicitement** cross-tenant :

- **`/supervision`** + **`/supervision/{tenantId}`** : santé cross-tenant **en lecture seule** —
  l'**unique** surface cross-tenant de lecture du produit (CLAUDE.md n°9), garde
  `[Authorize(Policy = liakont.supervision)]` que le super-admin court-circuite ;
- **`/clients`** (OPS03) : administration d'instance (création / suspension de tenants) ; ses
  **écritures sont scopées au tenant cible** via `ITenantScopeFactory.Create(tenantId)`
  (`src/Host/Liakont.Host/Clients/ClientConsoleService.cs:191`).

Les surfaces **Documents / Encaissements / Traitements / Paramétrage** sont, elles, le **poste de
travail d'un opérateur DANS son tenant** (rôles `lecture` / `operateur` / `parametrage`, matrice §3
de [identity-permissions-liakont.md](identity-permissions-liakont.md)). Les exposer à un acteur
**sans tenant** est un cul-de-sac et n'a pas de sens fonctionnel.

> **Décision** : les entrées de nav **tenant-scopées** (Documents, Encaissements, Traitements,
> Réconciliation, Paramétrage) sont **conditionnées à la présence d'un tenant courant résolvable**
> (le principal porte un `company_id` → un tenant est résolu). Un super-admin **sans `company_id`**
> ne les voit **plus**. Il conserve **Supervision** (dont **Clients**) et **Flotte**, qui sont
> cross-tenant **par conception**.

**Mécanisme — sans inventer de permission.** La condition de visibilité est la **présence d'un
scope tenant** (un tenant résolu pour le principal), **pas** une nouvelle permission ni la permission
éditeur (que le super-admin court-circuite de toute façon). C'est l'application directe de l'invariant
ADR-0021 « super-admin = hors périmètre tenant, jamais résolu vers un tenant ». `LiakontNavNodeProvider`
est **déjà** `SCOPED` sur le tenant courant (cf. son en-tête et la branche Réconciliation
`LiakontNavNodeProvider.cs:42`) : la même source de vérité (tenant courant résolu) garde les entrées
tenant-scopées.

**Portée d'implémentation (hors de cette note).** Le geste de code (garder ces entrées sur la
présence d'un tenant) est **petit** et **co-localisé** avec le durcissement de nav par permission
**RLF03** (même fichier `LiakontNavNodeProvider.cs`, même série de findings). Il est **porté par
RLF03 ou un suivi immédiat**, et **non** par RLF04 (décision), pour éviter une édition concurrente du
même fichier. Les pages elles-mêmes restent gardées par leur policy (la garde de page est la défense
en profondeur ; le masquage de nav est un confort/cohérence, pas le contrôle de sécurité).

### §2 — Action cross-tenant sur les documents : pas par défaut ; un seul mécanisme sanctionné

**Le super-admin n'agit PAS sur les documents d'un tenant par défaut.** Une action opérateur
(déblocage, relance/renvoi, ré-émission, verdict B2B/B2C, résolution manuelle, remplacement) est une
**écriture métier**, donc **tenant-scopée** par principe (CLAUDE.md n°9 : seule la Supervision a des
vues cross-tenant, **en lecture seule**). Faire écrire un acteur **sans tenant** dans les documents
d'un tenant serait une **écriture cross-tenant** — une concession qu'on **n'ouvre pas** tant qu'un
besoin opérationnel n'est pas confirmé.

> **Décision** : **si** (et seulement si) un besoin d'action cross-tenant du super-admin sur les
> documents est confirmé, le **SEUL** mécanisme sanctionné est l'**option (2)** : un **sélecteur de
> tenant explicite** qui établit un **vrai scope tenant** pour la durée de l'action — la requête
> **redevient tenant-scopée** (n°9 respecté), elle n'est **jamais** une écriture cross-tenant ni une
> impersonation silencieuse — **avec** l'audit `DocumentEvent` nominal **déjà garanti** (§Audit).
> Ce mécanisme est un **item séparé** (non construit ici) ; il n'est **pas** une dépendance de la
> gate realm-unique.

#### Spécification du sélecteur de tenant (option 2, à construire si besoin confirmé)

1. **Entrée** : surface **réservée au super-admin** (court-circuit `SuperAdminRoles.IsSuperAdmin`),
   p. ex. depuis `/supervision/{tenantId}` ou `/clients` — **jamais** la nav opérateur ordinaire.
   Le tenant est **choisi explicitement** (liste du registre `outbox.tenants`), **affiché en
   permanence** pendant la session d'action (bandeau « Vous agissez sur le tenant : `<id>` »).
2. **Établissement du scope** : l'action s'exécute via `ITenantScopeFactory.Create(tenantId)`
   (`src/Host/Liakont.Host/MultiTenancy/TenantScopeFactory.cs:27-43`) — **le même** mécanisme que les
   jobs tenant (`TenantJobRunner.cs:50`) et le provisioning (`ClientConsoleService.cs:191`). Dans ce
   scope, **toute** lecture/écriture du module Documents passe par la connexion **du tenant choisi**
   (`PostgresDocumentUnitOfWorkFactory`) : il n'y a **pas** de requête cross-tenant, le scope est
   réel.
3. **Identité opérateur obligatoire** : l'action porte l'identité **nominale** du super-admin
   (`IActorContext.UserId` / `DisplayName`,
   `src/Modules/Documents/Web/DocumentActionsEndpointMapping.cs:447-457`). Les ports d'action
   **refusent** déjà une identité vide (§Audit) — aucune action anonyme n'est possible.
4. **Garde-fous non négociables** :
   - **read-only par défaut** : ouvrir l'écriture cross-tenant **action par action**, jamais en bloc ;
   - **aucune** dérogation à une validation `Blocking` (CLAUDE.md n°3) — le super-admin ne « force »
     rien que l'opérateur du tenant ne pourrait faire ;
   - **aucune** écriture sur la base **source** du client (n°5/13) ;
   - **traçabilité** : l'action est un `DocumentEvent` **append-only** nominatif (§Audit) ;
   - **secrets** inchangés (n°10/18).

#### Audit — la fondation existe (preuve, option 2)

L'audit exigé par l'option (2) est **déjà** garanti par le module Documents — aucun ajout requis :

- **Identité capturée et figée** : `DocumentEvent.OperatorIdentity`
  (`src/Modules/Documents/Domain/Entities/DocumentEvent.cs:44`) et `OperatorName`
  (`DocumentEvent.cs:54`, figé à l'instant de l'action, FIX305).
- **Obligatoire pour toute action manuelle** : les fabriques d'événements d'action **lèvent
  `ArgumentException`** si l'identité est vide — `ReconciledManually` / `MarkManuallyHandled`,
  `BuyerConfirmedAsIndividual`, `RecheckedStillBlocked`, `Supersede`
  (`DocumentEvent.cs:205-283`, `Document.cs:286-389`) ; les services d'action valident l'identité
  **avant** exécution (p. ex. `DocumentRecheckService.cs:65-70`, F06 §3). Une action super-admin
  serait donc tracée **nominativement**, sans chemin anonyme.
- **Append-only (immuable)** : trigger PostgreSQL rejetant `UPDATE` / `DELETE` / `TRUNCATE` sur
  `documents.document_events`
  (`src/Modules/Documents/Infrastructure/Migrations/V003__create_document_events_table.sql:35-54`,
  message `P0001`, CLAUDE.md n°4) ; prouvé par
  `src/Modules/Documents/Tests.Integration/DocumentEventAppendOnlyIntegrationTests.cs`. Aucun chemin
  applicatif d'update/delete (`IDocumentUnitOfWork` n'expose que `AppendEventAsync`).

## Conséquences

**Positif** : le cul-de-sac F5b disparaît (le super-admin ne voit plus des surfaces inutilisables) ;
la frontière « super-admin hors périmètre tenant » (ADR-0021) est **cohérente de bout en bout** (nav,
résolution, cross-check, suspension) ; on n'ouvre **aucune** écriture cross-tenant tant qu'un besoin
n'est pas confirmé ; et **si** on l'ouvre, le mécanisme est cadré d'avance, tenant-scopé et audité.

**Ce qui n'est PAS fait ici** : (a) le geste de code du masquage (porté par RLF03 / suivi) ; (b) le
sélecteur de tenant de l'option (2) (item séparé, **uniquement** si le besoin est confirmé — ne pas
le construire « au cas où »). Cette note **décide** ; elle ne livre pas de code (RLF04 est un item de
décision).

**Limite** : un client exigeant qu'un rôle de plateforme agisse couramment sur les documents de ses
tenants relève d'un **besoin produit à qualifier** (qui ? quelles actions ? quel contrôle ?), pas
d'un défaut technique — c'est précisément ce que l'option (2) encadre le jour où il est posé.

## Invariants

- **INV-RLF04-1** — Un principal **sans tenant résolu** (super-admin sans `company_id`) ne voit
  **aucune** entrée de nav **tenant-scopée** (Documents / Encaissements / Traitements /
  Réconciliation / Paramétrage). *Preuve* : test bUnit nav « super-admin → pas d'entrée
  tenant-scopée ; conserve Supervision/Clients/Flotte » (porté par RLF03 / suivi).
- **INV-RLF04-2** — Aucune **écriture cross-tenant** du super-admin sur les documents n'existe par
  défaut : toute action documentaire passe par un **scope tenant réel**
  (`ITenantScopeFactory.Create`) ; il n'y a pas de chemin d'action « sans tenant ». *Preuve* :
  revue — aucune action documentaire ne s'exécute hors d'un scope tenant ; le sélecteur (option 2)
  n'est livré que si un item dédié l'introduit, avec ses propres tests.
- **INV-RLF04-3** — Toute action manuelle (y compris super-admin via le futur sélecteur) est
  **tracée nominativement** dans un `DocumentEvent` **append-only** ; aucune action anonyme. *Preuve* :
  les gardes d'identité obligatoire (`DocumentEvent.cs`, `Document.cs`, `DocumentRecheckService.cs`)
  et l'immuabilité (`V003…sql:35-54`, `DocumentEventAppendOnlyIntegrationTests`) — **déjà en place**.

## Références

- [ADR-0021 — Realm Keycloak unique](../adr/ADR-0021-realm-keycloak-unique-isolation-par-claim.md)
  §2b (super-admin exempté, hors périmètre tenant), INV-0021-4 ; **État actuel vs cible** (ligne
  « Super-admin d'instance » : statut explicite, jamais résolu vers un tenant).
- [identity-permissions-liakont.md](identity-permissions-liakont.md) §1-§3 (permissions, matrice,
  Supervision cross-tenant **lecture seule**).
- `CLAUDE.md` n°3 (jamais affaiblir une validation Blocking), n°4 (audit append-only), n°5/13
  (lecture seule de la base source), n°9 (tenant-scope ; cross-tenant **lecture seule** Supervision),
  n°10/18 (secrets).
- Code (faits cités) : `SuperAdminRoles.cs:12-45`, `ClaimsPermissionService.cs:30,78`,
  `PermissionAuthorizationHandler.cs:20-23`, `CompanyClaimTenantResolver.cs:61-66`,
  `TenantCompanyCrossCheckMiddleware.cs:82-85`, `TenantSuspensionMiddleware.cs:37`,
  `LiakontNavNodeProvider.cs:31-116`, `TenantScopeFactory.cs:27-43`, `ClientConsoleService.cs:191`,
  `TenantJobRunner.cs:50`, `DocumentActionsEndpointMapping.cs:447-457`,
  `DocumentEvent.cs:44,54,205-283`, `Document.cs:286-389`, `DocumentRecheckService.cs:65-70`,
  `V003__create_document_events_table.sql:35-54`, `DocumentEventAppendOnlyIntegrationTests.cs`.
- Source du finding : `tasks/recette-gate-realm-unique.md` (`[DESIGN] F5b`).
