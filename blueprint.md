# Liakont — Blueprint d'architecture (v2 — plateforme centralisée)

Doctrine d'architecture du produit. Toute décision de code doit pouvoir se justifier
par rapport à ce document, aux specs `docs/conception/F*.md` ou à un ADR dans `docs/adr/`.

> **v2 (2026-06-03)** : ce blueprint remplace intégralement la v1 (architecture on-premise
> service Windows + console WPF + SQLite). Décision et analyse : `tasks/decisions.md` +
> `tasks/analyse-impact-pivot-plateforme.md`. L'historique git porte la v1.

---

## 1. Objectif du produit

Liakont est une **passerelle de conformité facturation électronique GÉNÉRIQUE** entre
des logiciels métier legacy (base de données accessible, aucune API) et des **Plateformes
Agréées (PA)** qui portent le routage, l'interopérabilité et la déclaration fiscale
(réforme française, échéances septembre 2026 / 2027).

**Flux nominal** :

```
[CHEZ LE CLIENT]  Base legacy ──ODBC lecture seule──▶ AGENT ──pivot EN 16931 (HTTPS)──▶
[PLATEFORME]      Ingestion ──▶ Mapping TVA (table du tenant) ──▶ Validation ──▶ Envoi PA
                  ──▶ Suivi statuts + tax reports ──▶ Archivage WORM + piste d'audit (10 ans)
[CONSOLE WEB]     Comptables (consultation, déblocage, paramétrage) + opérateur (supervision)
```

**Ce que Liakont N'EST PAS** :
- Pas une Plateforme Agréée (l'agrément est délégué aux PA partenaires)
- Pas un ERP (il ne remplace pas le logiciel source)
- Pas un développement spécifique pour un client (c'est un PRODUIT)
- Pas un SaaS mondial unique : **la marque grise = une instance de plateforme PAR éditeur** ;
  IT Innovations ne voit jamais le parc client d'un éditeur

## 2. Le principe fondateur : la généricité sur DEUX axes de plug-ins (+ stockage d'archive enfichable)

```
                              LE PRODUIT LIAKONT (générique)

 Plug-ins SOURCE (IExtractor)                                  Plug-ins PA (IPaClient + capacités)
 — vivent dans L'AGENT —              PLATEFORME               — vivent dans LA PLATEFORME —
 ┌─────────────────┐      ┌──────────────────────────────┐      ┌──────────────────────┐
 │ EncheresV6      │─────▶│  Ingestion (contrat versionné)│      │ B2Brouter (PA #1)    │
 │ (le 1er)        │ push │  Moteur TVA (tables/tenant)   │─────▶│ Super PDP (PA #2)    │
 └─────────────────┘      │  Validation (~20 règles)      │      │ Fake (démo/tests)    │
 ┌─────────────────┐      │  Documents + piste d'audit    │      ├──────────────────────┤
 │ Futurs : AS400, │      │  Archive WORM + ancrage       │      │ Futurs : Iopole,     │
 │ Sage, Access... │      │  Supervision proactive        │      │ Seqino...            │
 └─────────────────┘      └──────────────────────────────┘      └──────────────────────┘

              PARAMÉTRAGE PAR TENANT (jamais du code) : table TVA validée par
              l'expert-comptable du client • SIREN • compte(s) PA • planification
              — édité depuis la console web, journalisé, revalidation requise
```

**Règles absolues de généricité :**

1. **Aucune donnée client dans le code.** Table TVA, SIREN, raison sociale, compte PA :
   tout est **paramétrage de tenant** (en base, par instance). Le code n'embarque que des
   EXEMPLES fictifs (`config/exemples/`) pour ses tests. Le paramétrage versionné d'un
   déploiement (seed) vit dans `deployments/<client>/`.
2. **Aucune fonctionnalité du produit ne dépend de ce qu'UN PA sait faire.** Chaque plug-in
   PA déclare ses **capacités** (`PaCapabilities`) ; le produit s'adapte.
3. **Tout plug-in PA passe les tests de contrat** (suite commune). Tout extracteur produit
   un pivot conforme au contrat d'ingestion (golden files).
4. **Le module Transmission ne référence jamais un plug-in PA concret.** Les plug-ins
   référencent les Contracts du module, jamais l'inverse, jamais entre eux.
5. **L'agent n'a AUCUNE logique métier.** Pas de TVA, pas de validation, pas de machine à
   états : extraction + transport, c'est tout. Toute l'intelligence est sur la plateforme
   (où elle se met à jour sans toucher au parc d'agents).
6. **Le coffre d'archive est agnostique du stockage** (3ᵉ axe enfichable, choisi par
   l'éditeur au niveau instance) : le module `Archive` ne dépend que de l'abstraction
   `IArchiveStore` à capacités déclarées (`ArchiveStoreCapabilities` : Object Lock, legal
   hold…), jamais d'un backend concret (`if (store is S3)` interdit). L'**intégrité reste au
   niveau produit** (chaîne de hashes + addenda chaînés + ancrage temporel) et ne dépend
   JAMAIS du WORM natif du backend ; quand le backend l'offre (S3 Object Lock, Azure immutable
   blob, GCS bucket lock), il est utilisé EN PLUS (ceinture + bretelles). V1 = FileSystem
   (appliance) + S3-compatible (Amazon, MinIO, OVH, Scaleway — un seul code) ; Azure Blob et
   GCS sont des plug-ins fast-follow à la demande.

## 3. Architecture d'exécution : plateforme multi-tenant + agent léger

### 3.1 Les deux composants

| | **Plateforme** | **Agent** |
|---|---|---|
| Où | Hébergée (éditeur self-hosted / dédiée / mutualisée) | Serveur du client final, près de la base legacy |
| Stack | .NET 10, ASP.NET Core, Blazor, PostgreSQL | .NET Framework 4.8, x86/x64 |
| Rôle | TOUT le métier : ingestion, TVA, validation, états, envoi PA, archive, console, supervision | Extraction ODBC + pool PDF + push HTTPS + heartbeat |
| Multi-tenant | Oui (1 tenant = 1 client final) | Non (1 agent = 1 tenant, clé API scopée) |
| Mise à jour | Déploiement d'instance (toutes les instances suivent) | Auto-update (flotte) |

### 3.2 Le contrat d'API agent ↔ plateforme

- Le **document pivot EN 16931 (JSON)** est le payload du contrat — spec F01-F02.
- Contrat **versionné** (`v1`, `v2`...) : la plateforme supporte la version N et N-1
  (les agents se mettent à jour moins vite que la plateforme).
- DTOs partagés dans **`Liakont.Agent.Contracts`** (netstandard2.0, AUCUNE logique),
  référencé par l'agent (net48) ET par le module Ingestion (net10).
- Auth : **clé API par agent** (préfixe + hash, révocable), scopée à UN tenant.
- Idempotence : re-pousser un document déjà poussé est sans effet (anti-doublon par
  `payload_hash` côté plateforme).
- L'agent **bufferise localement** (SQLite) quand la plateforme est injoignable et
  rattrape au retour du réseau. Aucune donnée n'est perdue par une coupure.
- Endpoints (préfixe `/api/agent/v1/`) : push documents (batch), push PDF, heartbeat,
  récupération de la configuration d'extraction (planification, version attendue).

### 3.3 Les trois topologies de déploiement (même produit, même code)

| Topologie | Pour qui | Qui opère | Particularités |
|---|---|---|---|
| **Self-hosted éditeur** | Éditeur avec serveur chez un hébergeur | L'éditeur | Appliance Docker ; IT Innovations ne voit RIEN de son parc ; séquestre APP |
| **Instance dédiée hébergée** | Éditeur sans infra | IT Innovations | Cloisonnée, marque éditeur, **réversible** (dump/restore/DNS → self-hosted) |
| **Mutualisée** | Clients directs (ISATECH/CMP, prospects) | IT Innovations | L'instance « maison » |

Une **instance** = 1 déploiement de plateforme = 1 opérateur. Les **tenants** d'une instance =
les clients finaux de cet opérateur. Il n'y a pas de hiérarchie de tenants : la séparation
éditeur/éditeur est physique (des instances différentes).

### 3.4 Supervision proactive (dead-man's switch)

Principe : **c'est la plateforme qui détecte l'absence, pas l'agent qui signale sa présence.**

- Chaque agent envoie un heartbeat périodique (état, version, dernier run, erreurs locales).
- Un job planifié (module Job) détecte les agents muets au-delà d'un seuil paramétrable
  → alerte à l'opérateur de l'instance (email) AVANT que le client ne s'en aperçoive.
- Le dashboard de supervision montre par tenant : santé de l'agent, documents bloqués,
  rejets PA, échéances de période déclarative avec des documents non transmis.
- Justification produit : l'e-reporting a des échéances légales ; une panne silencieuse
  = client en non-conformité sans le savoir.

## 4. Structure du dépôt et de la solution

```
C:\Source\Liakont\
├─ blueprint.md, CLAUDE.md, AGENTS.md
├─ src/                                ★ LA PLATEFORME (.NET 10) — pattern Stratum
│  ├─ Host/
│  │  └─ Liakont.Host/               Composition root : Blazor + API + enregistrement
│  │                                   des modules + branding d'instance
│  ├─ Common/                          ★ SOCLE STRATUM VENDORED (provenance documentée)
│  │  ├─ Abstractions/                 Stratum.Common.Abstractions
│  │  ├─ Infrastructure/               Stratum.Common.Infrastructure (Dapper, tenancy, outbox)
│  │  ├─ UI/                           Stratum.Common.UI (Radzen, composants, thème)
│  │  └─ Testing/                      Stratum.Common.Testing
│  ├─ Modules/
│  │  ├─ Identity/                     (vendored Stratum) Utilisateurs, rôles, permissions
│  │  ├─ Job/                          (vendored Stratum) Ordonnanceur, jobs planifiés
│  │  ├─ Notification/                 (vendored Stratum) Emails, clés API
│  │  ├─ Audit/                        (vendored Stratum) Journal technique
│  │  ├─ Ingestion/                    ★ API agents : enregistrement, clés, heartbeat,
│  │  │                                  réception pivot + PDF, anti-doublon
│  │  ├─ Documents/                    ★ Machine à états, piste d'audit (append-only),
│  │  │                                  supersede, détection d'altération source
│  │  ├─ TvaMapping/                   ★ Tables TVA par tenant, mapping, traces,
│  │  │                                  édition journalisée + revalidation
│  │  ├─ Validation/                   ★ ~20 règles (Blocking/Warning), spec F04
│  │  ├─ Transmission/                 ★ IPaClient + PaCapabilities (ABSTRACTION SEULE),
│  │  │                                  envoi, suivi statuts, tax reports
│  │  ├─ Payments/                     ★ E-reporting paiement (Flux 10.2/10.4), agrégats
│  │  ├─ Archive/                      ★ Coffre WORM, chaîne de hashes, ancrage temporel,
│  │  │                                  export d'audit, réversibilité par tenant
│  │  ├─ Reconciliation/               ★ Pool PDF ↔ documents émis (auto haute confiance
│  │  │                                  / file manuelle)
│  │  └─ Supervision/                  ★ Dead-man's switch, règles d'alerte, dashboard
│  ├─ PaClients/
│  │  ├─ Liakont.PaClients.Fake/     ★ Plug-in PA #0 (démo hors-ligne + tests)
│  │  ├─ Liakont.PaClients.B2Brouter/ ★ Plug-in PA #1
│  │  └─ Liakont.PaClients.SuperPdp/ ★ Plug-in PA #2 (Offre Éco marque grise)
│  └─ Contracts/
│     └─ Liakont.Agent.Contracts/    ★ DTOs du contrat agent↔plateforme (netstandard2.0)
│
├─ tests/                              Tests plateforme (architecture, unit, integration,
│                                      contrat PA, contrat agent, E2E Playwright)
│
├─ agent/                              ★ L'AGENT (.NET Framework 4.8) — solution séparée
│  ├─ src/
│  │  ├─ Liakont.Agent/              Service Windows : planification locale, push, heartbeat
│  │  ├─ Liakont.Agent.Core/         IExtractor, buffer SQLite, config DPAPI, client HTTP
│  │  ├─ Liakont.Agent.Adapters.EncheresV6/  ★ Plug-in source #1 (Magic XPA / Pervasive)
│  │  └─ Liakont.Agent.Cli/          check-config, test ODBC, test API, run manuel
│  └─ tests/
│
├─ deploy/                             ★ Appliance & opérations
│  ├─ docker/                          Dockerfile, docker-compose (app+PostgreSQL+Keycloak),
│  │                                   realm Keycloak, config d'instance
│  └─ provisioning/                    Scripts : créer une instance, créer un tenant,
│                                      mise à jour de flotte
│
├─ config/exemples/                    Table TVA d'exemple, paramétrage d'exemple (tests/démo)
├─ deployments/                        ★ Paramétrage versionné par client (seed à importer)
│  └─ cmp/                             Table TVA CMP, SIREN, fixtures démo ISATECH
├─ docs/                               conception (F*), adr, architecture, market, references
├─ orchestration/                      Manifest, items, blueprints, protocole
├─ tasks/                              Pilotage interactif (todo, decisions, lessons, analyses)
└─ tools/                              verify-fast, run-tests, codex-review, orch-state
```

### Règle du socle vendored

Le socle Stratum (`src/Common/*` + modules Identity/Job/Notification/Audit) est une **copie**
du dépôt Stratum, tracée dans `docs/architecture/provenance-socle-stratum.md` (commit source,
date, fichiers copiés). Toute modification locale du code `Stratum.*` est autorisée mais doit
être **consignée dans ce fichier** (objectif : pouvoir re-converger vers des packages NuGet
plus tard). Les modules et le Host Liakont suivent les conventions du socle
(Contracts/Domain/Application/Infrastructure/Web, MediatR, Dapper, NetArchTest).

## 5. Stack technique (décisions actées)

| Composant | Plateforme | Agent |
|---|---|---|
| Runtime | **.NET 10 LTS** | **.NET Framework 4.8** (jamais 4.7, jamais .NET moderne) |
| Builds | AnyCPU | **x86 ET x64** (drivers ODBC Pervasive 32-bit) |
| Hôte | ASP.NET Core (Kestrel), Docker | Service Windows |
| UI | **Blazor Server + Radzen** (socle Stratum) | Aucune (CLI de diagnostic uniquement) |
| Persistence | **PostgreSQL** (database-per-tenant) via **Dapper** + DbUp | **SQLite** (buffer local uniquement, WAL) |
| Coffre d'archive | **`IArchiveStore` à capacités** : FileSystem + S3-compatible (V1) ; Azure Blob / GCS fast-follow | — |
| Auth humains | **OIDC via abstraction d'IdP** (Keycloak en dev/V1) + RBAC permissions (module Identity) ; alternative légère in-process (OpenIddict) évaluée par spike d'empreinte dès SOL01 | — |
| Auth machine | Clés API agents (préfixe + hash, scopées tenant) | Clé API stockée chiffrée **DPAPI** |
| Messaging interne | MediatR + outbox (socle Stratum) | — |
| Sérialisation | System.Text.Json (conventions socle) | Newtonsoft.Json |
| Accès source | — | **ODBC générique, lecture seule** |
| Tests | xUnit, NetArchTest, Testcontainers, bUnit/acceptance, **Playwright** | xUnit (net48) |
| Jobs | Module Job Stratum (cron, retries, dead letter) | Planification locale simple (timer) |

**Règle de dépendances** : la plateforme hérite des packages du socle Stratum
(`Directory.Packages.props`). L'agent : built-in net48 + Newtonsoft.Json + SQLite.
**Tout nouveau package nécessite un ADR** (les deux côtés).

## 6. Rôles et frontières des modules

| Module / couche | Responsabilité | Interdit |
|---|---|---|
| `Agent.Contracts` | DTOs du contrat agent↔plateforme, versionnés | Toute logique (DTOs purs) |
| Agent (`Liakont.Agent.*`) | Extraction, buffer, transport, heartbeat | Logique métier (TVA, validation, états), écriture sur la base source, référencer du code plateforme |
| Plug-ins source (`Agent.Adapters.*`) | Implémenter IExtractor pour UN logiciel | Écrire/verrouiller la base source, référencer un autre adaptateur |
| `Ingestion` | Réception pivot/PDF, anti-doublon, gestion des agents et clés | Transformer les données (délégué aux modules métier) |
| `Documents` | Machine à états, piste d'audit append-only, supersede | Update/delete sur DocumentEvent, purge automatique |
| `TvaMapping` | Code régime → catégorie/taux/VATEX via table du tenant | Règles en dur, données client embarquées |
| `Validation` | Détection pré-envoi de tout ce qui serait rejeté | Correction automatique des données |
| `Transmission` | Contrat IPaClient + capacités, envoi, suivi | Référencer un plug-in PA concret |
| Plug-ins PA (`Liakont.PaClients.*`) | Implémenter IPaClient pour UNE plateforme | Fuiter leurs types hors de leur assembly, référencer un autre plug-in ou module métier |
| `Payments` | Agrégats de paiement, e-reporting Flux 10.2/10.4 | — |
| `Archive` | Coffre WORM, hashes chaînés, ancrage, export/réversibilité ; **`IArchiveStore` à capacités** (FS, S3-compatible ; Azure/GCS fast-follow) | Tout chemin d'update/delete (WORM) ; référencer un backend de stockage concret hors de son implémentation (`if (store is S3)`) |
| `Reconciliation` | Rapprochement PDF ↔ documents | Lien automatique en confiance moyenne/basse |
| `Supervision` | Heartbeats, alertes, dashboards | — |
| Modules Stratum vendored | Identity (auth OIDC **derrière une abstraction d'IdP** — Keycloak ou alternative légère), Job, Notification, Audit | Modification non consignée dans la provenance ; coupler le code à un IdP concret hors de la couche d'auth |
| `Liakont.Host` | Composition root, branding d'instance, enregistrement modules + plug-ins | Logique métier |
| Pages Blazor (Web de chaque module) | UI cliente des handlers MediatR | Logique métier dans les pages, accès direct à la base |

**Frontières inter-modules (héritées du socle Stratum, vérifiées par NetArchTest)** :
un module n'accède à un autre module que par ses **Contracts** (jamais Domain, Application,
Infrastructure). Les événements traversent l'outbox.

## 7. Multi-tenancy : règles absolues

1. **1 tenant = 1 client final** (entité légale identifiée par son SIREN).
2. **Isolation physique par tenant** (database-per-tenant, héritée du socle — ADR-0011 Stratum).
3. **Toute requête métier est tenant-scopée.** Une requête sans tenant résolu échoue
   (jamais de « tous les tenants » dans le code métier — seul le module Supervision a des
   vues cross-tenant, en lecture).
4. **Un agent appartient à UN tenant.** Sa clé API ne peut pas écrire ailleurs.
5. **Le paramétrage fiscal est par tenant** : table TVA, SIREN, comptes PA, planification.
   Modifiable depuis la console (droit « paramétrage »), journalisé, revalidation requise.
6. **L'archive et la piste d'audit sont par tenant** et exportables par tenant (réversibilité).

## 8. Gestion des montants (règle absolue, inchangée)

- **`decimal` partout.** float/double sur un montant = P1 en review, sans exception.
- Arrondi commercial (half-up) à 2 décimales.
- Les montants originaux de la source sont conservés bruts dans le pivot (`SourceData`).
- Aucune tolérance dans les réconciliations de totaux (BR-CO-15 est une règle fatale).

## 9. Stratégie de test

| Niveau | Quoi | Outil | Quand |
|---|---|---|---|
| Architecture | Frontières de modules, règles §6 | NetArchTest | verify-fast (chaque commit) |
| Unit | Handlers, domaine, composants | xUnit (+ bUnit pour Blazor) | verify-fast |
| Unit agent | Extraction fixtures, buffer, reprise | xUnit net48 | verify-fast |
| Integration | PostgreSQL réel, modules bout en bout | xUnit + Testcontainers | run-tests |
| Contrat agent | Golden files du contrat (sérialisation identique des deux côtés) | xUnit (plateforme ET agent) | run-tests |
| Contrat PA | Suite commune sur chaque plug-in (mock HTTP) | xUnit | run-tests |
| E2E | Parcours navigateur (console web) | Playwright | run-tests (suite dédiée) |
| Staging/Sandbox | Envois réels B2Brouter staging / Super PDP sandbox | Manuel | Avant chaque gate PA, jamais en CI |

**Faux verts interdits** : un test écrit mais jamais exécuté, une assertion affaiblie,
un test `[Skip]`, un script qui réussit quand il devrait échouer — tous P1 en review.

**Fin des checklists smoke manuelles** : les écrans web sont couverts par Playwright
(c'était impossible en WPF — c'est un des gains du pivot).

## 10. Pipeline de validation d'un changement

```
code → verify-fast (build plateforme + build agent x86 + analyzers + architecture tests + unit)
     → run-tests (integration + contrats + E2E) [si applicable]
     → codex-review (P1/P2, rounds jusqu'à clean, -Base obligatoire)
     → merge --no-ff dans la branche de segment
     → gate humaine (PR review + validation fonctionnelle) avant main
```

La CI construit **les deux solutions** (plateforme .NET 10 + agent net48 x86/x64).

## 11. Gouvernance IA

- 1 item d'orchestration = 1 branche = 1 objectif = bornage strict du périmètre.
- L'IA ne merge **jamais** dans `main` — les gates sont validées par un humain.
- Les décisions fiscales (régime 6, TVA sur débits, OperationCategory) appartiennent à
  l'expert-comptable du client concerné. L'IA les matérialise dans le PARAMÉTRAGE du
  tenant, jamais dans le code.
- Toute modification du socle vendored (`Stratum.*`) est consignée dans la provenance.
- Toute correction utilisateur alimente `tasks/lessons.md`.

## 12. Points ouverts structurants (à trancher hors code)

| Point | Propriétaire | Impact |
|---|---|---|
| CMP : tenant mutualisé, instance dédiée ou appliance on-premise ? | ISATECH/CMP | Lot CMP, urgence de l'appliance |
| Hébergeur des instances hébergées (France/UE) | Karl | deploy/, contrats, RGPD |
| Auth : direction tranchée (2026-06-03, D10) — **IdP derrière une abstraction dès SOL01** + spike d'empreinte (Keycloak vs OpenIddict) ; choix final sur mesure, topologie à l'ADR OPS01 | ADR (SOL01 → OPS01) | Empreinte mémoire par instance |
| Stockage du coffre : V1 tranché (D9) — FS + S3-compatible ; backend par éditeur (Amazon/MinIO/OVH/Scaleway en V1 ; Azure/GCS fast-follow) | Éditeur (par instance) / ADR lot Archive | `IArchiveStore` à capacités |
| Régime 6 EncheresV6 = marge EU-J ou hors champ ? | Expert-comptable CMP | Paramétrage CMP — pas le produit |
| TVA sur les débits / OperationCategory / volume B2B du CMP | Expert-comptable CMP | Paramétrage CMP |
| Transmission Flux 10.2/10.4 chez B2Brouter / Super PDP | Supports PA | Capacités des plug-ins (le produit, lui, est prêt) |
| Ouverture sandbox Super PDP | Nous (DR17-A4) | Prérequis du plug-in Super PDP |
| Séquestre APP + clauses de réversibilité/restitution | Karl (contrats) | Offre commerciale, pas le code |

Ces points ne bloquent pas le développement du produit (la généricité les isole dans les
plug-ins, le paramétrage et les ADR), mais ils bloquent les mises en production concernées.

## 13. Évolutions prévues (phase 2 — ne pas anticiper dans le code, ne pas l'empêcher)

- **Réception fournisseurs + Flux 10.1 (B2B international)** : la plateforme reçoit depuis
  les PA (webhooks/polling) — aucun impact agent (la consultation est web). La règle
  « lecture seule de la base source » interdit toute écriture retour vers le legacy.
- **Nouveaux extracteurs** (AS400, Sage, Access...) : nouveaux plug-ins d'agent.
- **Nouvelles PA** (Iopole, Seqino...) : nouveaux plug-ins de plateforme.
- **Re-convergence du socle** : remplacer la copie Stratum par des packages NuGet quand
  les besoins de Liakont seront stabilisés et reversés dans Stratum.
