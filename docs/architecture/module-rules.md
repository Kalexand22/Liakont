# Règles et frontières des modules Liakont

> Frontières architecturales du produit, vérifiables et opposables en review (P1).
> **Sources** (rien n'est inventé ici) : `blueprint.md` v2 (§2 généricité, §6 rôles et frontières,
> §7 multi-tenancy, §8 montants), `docs/conception/F12-Architecture-Plateforme-Agent.md`,
> `CLAUDE.md` (règles métier non négociables n°1–12, instructions de review n°9–20),
> `docs/architecture/definition-of-done.md`. Chaque règle renvoie à sa source.
>
> Documents frères : [`repo-standards.md`](repo-standards.md) (conventions de code et de dépôt),
> [`testing-strategy.md`](testing-strategy.md) (comment ces frontières sont testées).

---

## 1. Structure interne d'un module (pattern Stratum)

Chaque module sous `src/Modules/<Module>/` suit le découpage en couches du socle vendored :

| Couche | Dossier | Contenu | Visibilité |
|---|---|---|---|
| **Contracts** | `Contracts/` | Commands, Queries, DTOs, événements d'intégration | **Seule surface publique** du module |
| **Domain** | `Domain/` | Entités, value objects, agrégats, services de domaine, invariants | Interne au module |
| **Application** | `Application/` | Handlers MediatR, services applicatifs | Interne au module |
| **Infrastructure** | `Infrastructure/` | Repositories Dapper, migrations DbUp, services techniques | Interne au module |
| **Web** | `Web/` | Pages/composants Blazor, NavSection, endpoints API | UI cliente des handlers MediatR |

Messagerie interne : **MediatR + outbox** (socle Stratum). Les événements traversent l'outbox.

---

## 2. Rôles et interdictions par module (`blueprint.md` §6)

| Module / couche | Responsabilité | **Interdit** |
|---|---|---|
| `Agent.Contracts` | DTOs du contrat agent↔plateforme, versionnés | Toute logique (DTOs purs) |
| Agent (`Liakont.Agent.*`) | Extraction, buffer, transport, heartbeat | Logique métier (TVA, validation, états), écriture sur la base source, référencer du code plateforme |
| Plug-ins source (`Agent.Adapters.*`) | Implémenter `IExtractor` pour UN logiciel | Écrire/verrouiller la base source, référencer un autre adaptateur |
| `Ingestion` | Réception pivot/PDF, anti-doublon, gestion des agents et clés | Transformer les données (délégué aux modules métier) |
| `Documents` | Machine à états, piste d'audit append-only, supersede | Update/delete sur `DocumentEvent`, purge automatique |
| `TvaMapping` | Code régime → catégorie/taux/VATEX via table du tenant | Règles en dur, données client embarquées |
| `Validation` | Détection pré-envoi de tout ce qui serait rejeté | Correction automatique des données |
| `Transmission` | Contrat `IPaClient` + capacités, envoi, suivi | Référencer un plug-in PA concret |
| Plug-ins PA (`Liakont.PaClients.*`) | Implémenter `IPaClient` pour UNE plateforme | Fuiter leurs types hors de leur assembly, référencer un autre plug-in ou un module métier |
| `Payments` | Agrégats de paiement, e-reporting Flux 10.2/10.4 | — |
| `Archive` | Coffre WORM, hashes chaînés, ancrage, export/réversibilité ; `IArchiveStore` à capacités | Tout chemin d'update/delete (WORM) ; référencer un backend de stockage concret hors de son implémentation (`if (store is S3)`) |
| `Reconciliation` | Rapprochement PDF ↔ documents | Lien automatique en confiance moyenne/basse |
| `Supervision` | Heartbeats, alertes, dashboards | — |
| Modules Stratum vendored | Identity (auth OIDC **derrière une abstraction d'IdP**), Job, Notification, Audit | Modification non consignée dans la provenance ; coupler le code à un IdP concret hors de la couche d'auth |
| `Liakont.Host` | Composition root, branding d'instance, enregistrement modules + plug-ins | Logique métier |
| Pages Blazor | UI cliente des handlers MediatR | Logique métier dans les pages, accès direct à la base |

---

## 3. Frontière inter-modules : Contracts uniquement

**Règle** (`blueprint.md` §6, `CLAUDE.md` n°14, `definition-of-done.md` ligne 18) : un module
n'accède à un autre module **que par ses `Contracts`** — jamais `Domain`, `Application` ni
`Infrastructure`. Les événements traversent l'outbox.

**Vérification** : tests d'architecture NetArchTest, hérités du socle Stratum. Le package
`NetArchTest.Rules` est déjà disponible (`Directory.Packages.props`). Au moment où le premier module
métier Liakont atterrit (PIV05 et suivants), la garde inter-modules de la plateforme est (re)mise en
place — le harness de test du socle n'a pas été copié par le vendoring SOL01 (qui ne copie que
`src/`). En attendant, la frontière reste **opposable en review (P1)**.

> Une référence `module A → Domain/Application/Infrastructure de module B` (hors `B.Contracts`) est
> un **P1** (`CLAUDE.md` n°14).

---

## 4. Frontière agent ↔ plateforme

**Règles** (`blueprint.md` §2 règle 5, §6 ; `F12` §2.6 ; `CLAUDE.md` n°6, n°14) :

1. **L'agent n'a AUCUNE logique métier** : pas de TVA, pas de validation, pas de machine à états.
   Extraction + transport, c'est tout. Toute l'intelligence est sur la plateforme.
2. **L'agent ne référence JAMAIS la plateforme** (`src/Host`, `src/Modules`, `src/Common`,
   `src/PaClients`). Sa seule dépendance cross-solution est `src/Contracts/Liakont.Agent.Contracts`.
3. **`Liakont.Agent.Contracts` est BCL-only** : zéro `PackageReference` hors BCL ; DTOs purs,
   aucune logique (`F12` §3.4).
4. **Lecture seule stricte de la base source** : aucun INSERT/UPDATE/DELETE, aucun verrou, aucune
   transaction d'écriture (`CLAUDE.md` n°5 ; `F12` §2.6).

**Vérification (tests existants, côté agent)** :

| Test | Fichier | Garde |
|---|---|---|
| `ContractsPurityTests` | `agent/tests/Liakont.Agent.Core.Tests/ContractsPurityTests.cs` | `Liakont.Agent.Contracts` ne dépend que du BCL (inspection IL des assemblies référencées) |
| `AgentBoundaryTests` | `agent/tests/Liakont.Agent.Core.Tests/AgentBoundaryTests.cs` | Liste blanche : chaque assembly agent ne référence que BCL + `Liakont.Agent.*` + son sérialiseur (Newtonsoft.Json) |
| `AgentProjectReferenceTests` | `agent/tests/Liakont.Agent.Core.Tests/AgentProjectReferenceTests.cs` | Les `.csproj` agent ne référencent que `agent/` + l'unique `src/Contracts/Liakont.Agent.Contracts` |

> Une logique métier dans l'agent, ou une référence agent → code plateforme, est un **P1**
> (`CLAUDE.md` n°14). Une écriture/verrou sur la base source dans un adaptateur est un **P1**
> (`CLAUDE.md` n°13).

---

## 5. Généricité : PA, stockage d'archive, IdP, intégrateur

Quatre axes de variabilité sont **enfichables / déclaratifs**, jamais des `if` sur un type concret
(`blueprint.md` §2, §6 ; `F12` §7 ; `CLAUDE.md` n°6, n°8) :

- **Plug-ins PA** : `Transmission` ne référence jamais un plug-in PA concret ; un plug-in ne
  référence que `Transmission.Contracts` (+ Common), jamais un autre plug-in ni un module métier.
  Le comportement produit est piloté par les **capacités déclarées** (`PaCapabilities`), jamais par
  un flag produit ni un `if (pa is B2Brouter)`. → P1 (`CLAUDE.md` n°16).
- **Stockage d'archive** : `Archive` ne dépend que de l'abstraction `IArchiveStore` à capacités
  (`ArchiveStoreCapabilities`). `if (store is S3)` interdit. V1 = FileSystem + S3-compatible ;
  Azure/GCS = plug-ins fast-follow. L'intégrité (hashes chaînés + ancrage) reste **produit**,
  indépendante du WORM natif du backend. → P1 (`CLAUDE.md` n°14).
- **IdP** : l'authentification est consommée **derrière une abstraction d'IdP** (Keycloak = une
  implémentation). Aucun appel IdP-spécifique hors de la couche d'auth du module Identity (D10).
  → P1 (`CLAUDE.md` n°14).
- **Profil intégrateur** : la variabilité par intégrateur (branding, visibilité d'écrans) est un
  **profil déclaratif** (F13), jamais un `if (integrateur == X)`. Masquer une option impose une
  valeur par défaut explicite (`blueprint.md` §2 règle 7 ; `tasks/lessons.md` 2026-06-03).

---

## 6. Multi-tenancy : requêtes tenant-scopées

**Règles** (`blueprint.md` §7 ; `CLAUDE.md` n°9 ; `definition-of-done.md` ligne 21) :

1. **1 tenant = 1 client final** (entité légale, SIREN), isolation **physique** par tenant
   (database-per-tenant, ADR-0011 Stratum).
2. **Toute requête métier est tenant-scopée.** Une requête sans tenant résolu échoue. Jamais de
   « tous les tenants » dans le code métier — **seul le module `Supervision`** a des vues
   cross-tenant, **en lecture seule**.
3. **Un agent appartient à UN tenant** : sa clé API (scopée) ne peut écrire ailleurs.
4. Paramétrage fiscal, archive et piste d'audit sont **par tenant** et exportables par tenant.

> Une requête métier non tenant-scopée (fuite de données entre tenants) est un **P1**
> (`CLAUDE.md` n°17). Les jobs multi-tenant passent par la mécanique unique `TenantJobRunner` (SOL06)
> — aucun module ne réinvente sa propre boucle de balayage des tenants.

---

## 7. Piste d'audit et coffre d'archive : append-only / WORM

**Règles** (`blueprint.md` §6 ; `CLAUDE.md` n°4 ; `definition-of-done.md`) :

- `DocumentEvent` et `MappingChangeLog` sont **append-only** : aucun chemin d'update/delete, aucune
  purge automatique d'une table d'audit.
- Le coffre d'archive est **WORM** : intégrité produit par chaîne de hashes + addenda chaînés +
  ancrage temporel, **indépendante** du WORM natif du backend (utilisé en plus quand disponible).

> Tout chemin d'update/delete sur `DocumentEvent`, `MappingChangeLog`, le coffre (WORM) ou toute
> purge d'une table d'audit est un **P1** (`CLAUDE.md` n°12).

---

## 8. Secrets et données client

- **Secrets chiffrés** : clés API des PA (en base, chiffrées, par tenant) ; clé API de l'agent et
  chaîne ODBC (DPAPI machine scope côté client). Jamais en clair dans un fichier versionné ni un log
  (`CLAUDE.md` n°10 ; `F12` §2.4). Un secret en clair est un **P1** (`CLAUDE.md` n°18).
- **Aucune donnée client dans le code** : SIREN réel, table TVA réelle, chaîne ODBC, compte PA →
  paramétrage de tenant (en base) ou seed `deployments/<client>/`. Le code n'embarque que des
  exemples fictifs dans `config/exemples/` (`CLAUDE.md` n°7). Une donnée client dans le code est un
  **P1** (`CLAUDE.md` n°15).

---

## 9. Montants et données absentes

- **`decimal` partout**, jamais `float`/`double` sur un montant. Arrondi commercial **half-up**
  à 2 décimales. Les montants source sont conservés bruts dans le pivot (`SourceData`). Aucune
  tolérance dans les réconciliations de totaux (BR-CO-15 fatale). `blueprint.md` §8.
- **Tout champ absent = `null`** (jamais une valeur par défaut implicite qui masquerait une donnée
  manquante).
- **Aucune règle fiscale inventée** : toute catégorie TVA, tout code VATEX, tout seuil vient de
  `docs/conception/F*.md`. Si la spec ne tranche pas : **bloquer l'item**, ne pas deviner.

> `float`/`double` sur un montant est un **P1**. Une règle fiscale sans source traçable dans
> `docs/conception/` est un **P1**. L'affaiblissement d'une validation Blocking en Warning est un
> **P1** (`CLAUDE.md` n°1–3, n°9–11 review).

---

## 10. Socle vendored

Toute modification d'un fichier `Stratum.*` doit être consignée dans
`docs/architecture/provenance-socle-stratum.md` (commit source, date, modification). Une
modification non consignée est un **P1** (`CLAUDE.md` n°11, n°20). Les modules Liakont suivent les
conventions du socle (Contracts/Domain/Application/Infrastructure/Web, MediatR, Dapper, NetArchTest).

---

## 11. Obligation documentaire par module

**Règle** (`definition-of-done.md` lignes 24–25 ; obligation héritée des `ModuleIsolationTests` du
socle Stratum) : **chaque dossier sous `src/Modules/`** contient trois fichiers, sinon les tests
d'architecture du socle échouent :

| Fichier | Contenu | Format observé (modules vendored) |
|---|---|---|
| `MODULE.md` | Objet du module, frontières (schémas owns/reads/writes), événements publiés/consommés, dépendances | Sections `## Purpose`, `## Boundaries`, `## Published Events`, `## Consumed Events`, `## Dependencies` (forme concise) ; ou forme étendue avec `## Schema`, `## Entities`, `## Endpoints`, `## Integration Events` (ex. `src/Modules/Notification/MODULE.md`) |
| `INVARIANTS.md` | Invariants du module, un par ligne, avec lieu d'enforcement | Tableau `\| ID \| Rule \| Enforcement \|` ; ID de la forme `INV-<MODULE>-NNN` (ex. `src/Modules/Audit/INVARIANTS.md`) |
| `SCENARIOS.md` | Scénarios de test couverts, regroupés par niveau (Unit / Integration), référençant les `INV-ID` | Listes à puces par classe de test, ou style Given/When/Then (ex. `src/Modules/Audit/SCENARIOS.md`, `src/Modules/Notification/SCENARIOS.md`) |

> **Tout item qui crée un module sous `src/Modules/` doit livrer ces trois fichiers** : notamment
> les modules `Ingestion`, `Documents`, `TvaMapping` (TVA01), `Validation` (VAL01), `Transmission`
> (PAA01), `Payments`, `Archive`, `Reconciliation`, `Supervision`, ainsi que les items du cœur
> (PIV05, TRK01…). Un module créé sans ces trois fichiers fait échouer les tests d'architecture
> hérités du socle — c'est un trou de done, pas une option.

Référence d'exemples complets : `src/Modules/Audit/`, `src/Modules/Identity/`,
`src/Modules/Notification/` (modules vendored qui portent déjà les trois fichiers).
