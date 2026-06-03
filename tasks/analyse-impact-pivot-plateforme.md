# Analyse d'impact — Pivot d'architecture : plateforme centralisée + agent léger

**Date** : 2026-06-03
**Statut** : décision actée (voir tasks/decisions.md) — ce document prépare le retravail du backlog (manifest v6)
**Portée** : impact sur le backlog v5, le travail déjà livré, les specs, l'outillage d'orchestration

---

## 1. Rappel de la décision

L'architecture on-premise (service Windows + console WPF + tracking SQLite chez chaque client)
est remplacée par :

```
CLIENT FINAL                          PLATEFORME LIAKONT (web, .NET 10, socle Stratum)
┌──────────────────────────┐          ┌─────────────────────────────────────────────────┐
│ Serveur legacy (vieux OK) │          │  Multi-tenant (1 tenant = 1 client final)        │
│  ┌─────────────────────┐ │  HTTPS   │  ├─ Pivot EN 16931 (contrat d'API)               │
│  │ AGENT (net48/x86)   │─┼─────────▶│  ├─ Moteur TVA (tables paramétrées par tenant)   │
│  │ ├ IExtractor (ODBC) │ │  push +  │  ├─ Validation (~20 règles)                      │
│  │ ├ Pool PDF          │ │ heartbeat│  ├─ Tracking + machine à états (PostgreSQL)      │
│  │ ├ Buffer SQLite     │ │          │  ├─ Archivage WORM + ancrage temporel            │
│  │ └ Config DPAPI      │ │          │  ├─ Pipeline + ordonnanceur (module Job)         │
│  └─────────────────────┘ │          │  ├─ Plug-ins PA (B2Brouter, Super PDP, Fake)     │
└──────────────────────────┘          │  ├─ Console web (Blazor) multi-rôles             │
                                      │  └─ Supervision proactive (dead-man's switch)    │
   Comptables / opérateurs            └────────────────────┬────────────────────────────┘
   ────────── navigateur ──────────────────────────────────┘            │
                                                                 Plateformes Agréées → DGFiP
```

### Les trois topologies de déploiement (même produit, même code)

| Topologie | Pour qui | Qui opère |
|---|---|---|
| **Self-hosted éditeur** | Éditeur avec serveur chez un hébergeur | L'éditeur (appliance Docker sur son infra) |
| **Instance dédiée hébergée** | Éditeur sans infra | IT Innovations (instance cloisonnée, marque éditeur, réversible) |
| **Mutualisée** | Clients directs (ISATECH/CMP, prospects) | IT Innovations |

Une instance est multi-tenant : les tenants d'une instance éditeur = SES clients finaux.
IT Innovations ne voit jamais le parc d'un éditeur self-hosted ou dédié.

### Ce qui survit de l'ancien monde

| Élément | Sort |
|---|---|
| net48/x86 | Uniquement l'**agent** (vieux Windows + ODBC Pervasive 32-bit) |
| SQLite | Uniquement le **buffer local de l'agent** (reprise sur coupure réseau) |
| DPAPI | Uniquement les **secrets de l'agent** (clé API) |
| WPF | Au plus un mini-outil de configuration d'agent (optionnel — un CLI peut suffire) |
| Console WPF de gestion | ❌ Remplacée par la console web (Blazor) |
| Tracking SQLite / SQL Server Express | ❌ Remplacé par PostgreSQL central (la décision multi-provider devient sans objet) |
| API self-hosted + auth Windows | ❌ Remplacée par ASP.NET Core + Keycloak OIDC |

---

## 2. Impact sur le travail DÉJÀ livré (orchestration des 2026-06-02/03)

| Livré | Contenu | Sort |
|---|---|---|
| SOL01 (commit afc79e2) | Scaffold Gateway.sln : 10 projets produit + 8 projets test, tout en net48 | 🔴 **Invalidé** — la structure de solution change entièrement |
| SOL02 (commit e3edd92) | GitHub Actions CI + docs outillage de vérification | 🟡 Partiellement transposable (les principes CI/DoD restent, les commandes de build changent) |
| SOL03 (commit bd6d33a) | Docs d'architecture du repo (repo-standards) | 🟡 Partiellement transposable |
| **PR #1 (GATE_SOCLE)** | PR du segment socle en attente de validation humaine | ❗ **NE PAS MERGER — à fermer avec un commentaire renvoyant à cette analyse** |
| state.yaml (liakont-orchestration) | SOL01/02/03 done, GATE_SOCLE gate_pending | À réinitialiser lors du passage au manifest v6 |

**Perte sèche : ~2-3 sessions.** Le pivot intervient avant le démarrage du métier — c'est le
meilleur moment possible (après, chaque lot métier livré en net48 aurait été à re-livrer).

---

## 3. Impact sur le backlog v5, lot par lot

Légende : 🟢 transposé (la spec de l'item reste valide, le runtime/stockage change) ·
🟡 réécrit (l'intention survit, le contenu change) · 🔴 remplacé/supprimé · ➕ nouveau

| Lot | Items v5 | Verdict | Détail |
|---|---|---|---|
| SOL | 3 | 🔴 Réécrit | Nouvelle structure : solution plateforme (.NET 10, pattern Stratum) + solution agent (net48). CI double (build plateforme + build agent x86) |
| PIV | 5 | 🟢 Transposé | Le pivot EN 16931 devient **le contrat d'API agent→plateforme** (JSON versionné). IExtractor vit côté agent (net48). Le modèle pivot existe des deux côtés du contrat |
| TVA | 5 | 🟢 Transposé | Module plateforme. TVA05 (édition + journal) devient page Blazor + endpoints — plus simple qu'en WPF |
| VAL | 5 | 🟢 Transposé | Module plateforme, spec F04 inchangée |
| TRK | 8 | 🟢 Transposé **+ simplifié** | PostgreSQL remplace SQLite. TRK07 (RFC 3161 / OpenTimestamps) : les API .NET modernes existent — **le point dur net48 disparaît**. TRK08 (parsing PDF) : bibliothèques modernes sans contrainte AGPL |
| CFG | 2 | 🟡 Réécrit | Config plateforme = paramétrage par tenant (+ module Config Stratum) ; config agent = fichier local + DPAPI. CFG02 (DPAPI) survit côté agent uniquement |
| PAA | 3 | 🟢 Transposé | IPaClient + PaCapabilities + plug-in Fake en .NET 10 — conceptuellement identique |
| PAB | 4 | 🟢 Transposé | HttpClient moderne (resilience native) — plus simple qu'en net48 |
| PAS | 3 | 🟢 Transposé | Idem |
| PIP | 4 | 🟢 Transposé | Le pipeline tourne sur la plateforme : déclenché par les push agents + jobs planifiés (module Job Stratum). Pre-send/recovery/garde production inchangés |
| SVC | 3 | 🔴 Remplacé | Le service Windows lourd disparaît. L'ordonnanceur = module Job Stratum. Un composant **nouveau** le remplace côté client : l'agent (lot AGT) |
| API | 4 | 🟡 Réécrit | La **sémantique** des endpoints (resend, resolve-manually, supersede, recheck, export période, paramétrage) se transpose telle quelle. La technique change : minimal APIs ASP.NET Core + permissions Keycloak (au lieu de self-hosted + auth Windows). S'ajoute l'API d'ingestion agents (lot AGT) |
| CLI | 1 | 🔴 Remplacé | L'admin plateforme est web. L'agent garde un petit outil CLI (check-config, test connexion) |
| ADP | 5 | 🟢 Transposé | L'extracteur EncheresV6 (ODBC Pervasive x86) vit **dans l'agent** — code net48 comme prévu. ADP05 (pool PDF) idem |
| WPF | 8 | 🔴 Remplacé | Pages **Blazor** (Radzen) : dashboard, liste/détail documents, actions, paramétrage TVA, réconciliation, droits. Le contenu fonctionnel et les specs d'écran se transposent. **Bonus : E2E Playwright possible** (impossible en WPF) — l'infra existe dans Stratum |
| PKG | 1 | 🟡 Réécrit | Devient : installeur agent (MSI net48) + appliance Docker plateforme — absorbé par le lot OPS |
| DOC | 1 | 🟢 Transposé | Contenu mis à jour (guide d'installation agent + guide opérateur web) |
| CMP | 4 | 🟢 Transposé | CMP devient un **tenant** (mutualisé ou instance dédiée — question ISATECH, voir §8). Le paramétrage (table TVA, SIREN, fiscalité) est inchangé |
| Gates | 12 | 🟡 Ajustées | La structure gates/segments est conservée ; GATE_CONSOLE_ADMIN devient GATE_CONSOLE_WEB, GATE_SERVICE_API devient GATE_PLATEFORME, etc. |

**Bilan sur les 69 items v5 : ~42 transposés · ~12 réécrits · ~15 remplacés.**

---

## 4. Les nouveaux lots (inexistants dans le v5)

### Lot AGT — L'agent local (net48) ➕
| Item | Contenu |
|---|---|
| AGT01 | Structure agent : service Windows léger + buffer SQLite + config DPAPI |
| AGT02 | Protocole de push : auth par clé API, batching, idempotence, reprise sur coupure, versionnement du contrat |
| AGT03 | Heartbeat + remontée d'état (version agent, dernier run, erreurs locales) |
| AGT04 | Auto-update de l'agent (une flotte d'agents se met à jour sans intervention) |
| AGT05 | Outil de configuration/diagnostic (CLI : check-config, test ODBC, test API) |
| AGT06 | Côté plateforme : endpoints d'ingestion + gestion des agents (enregistrement, clés, révocation) |

### Lot SUP — Supervision proactive ➕
| Item | Contenu |
|---|---|
| SUP01 | Dead-man's switch : détection des agents muets (> seuil paramétrable) + règles d'alerte |
| SUP02 | Dashboard de supervision (par instance : santé de tous les tenants/agents, rejets PA, documents bloqués, échéances de période) |
| SUP03 | Alerting sortant (email via module Notification Stratum) vers l'opérateur de l'instance ET optionnellement le client final |

### Lot OPS — Opérations multi-instances ➕ (absorbe PKG)
| Item | Contenu |
|---|---|
| OPS01 | Appliance Docker : Dockerfile + docker-compose (app + PostgreSQL + Keycloak) — **n'existe pas dans Stratum** |
| OPS02 | Provisioning d'instance : créer une instance éditeur = 1 commande (base, realm, config, DNS) |
| OPS03 | Provisioning de tenant : créer un client final = 1 écran (l'éditeur le fait lui-même) |
| OPS04 | Mise à jour de flotte + méta-supervision des instances (heartbeat d'instance, version, espace disque) |
| OPS05 | Installeur de l'agent (MSI/setup net48, x86) — ex-PKG01 |
| OPS06 | Réversibilité : export complet d'un tenant (tracking + archive + paramétrage) et d'une instance (migration self-hosted) |

### Lot BRD — Marque grise ➕
| Item | Contenu |
|---|---|
| BRD01 | Branding par instance : logo, couleurs, nom, domaine — **n'existe pas dans Stratum** (titre/thème en dur) |

---

## 5. Ce que le socle Stratum fournit (vérifié sur pièce le 2026-06-03)

| Besoin Liakont | Fourni par Stratum | État |
|---|---|---|
| Multi-tenancy (isolation physique par tenant) | `ITenantContext` + connection factory tenant-aware (ADR-0011 database-per-tenant) | ✅ Direct |
| Auth utilisateurs + 3 niveaux de droits (lecture/actions/paramétrage) | Keycloak OIDC + module Identity (RBAC par permissions, `PermissionPolicyProvider`) | ✅ Direct |
| Ordonnanceur (runs planifiés, retries) | Module Job (`JobWorker` + cron Cronos + dead letter) | 🟡 Le moteur existe mais SANS résolution de tenant (aucun `ITenantContext` dans le module, tables en base système) — la mécanique de jobs multi-tenant est à construire (SOL06) |
| Journal d'audit technique | Module Audit | ✅ Complète la piste d'audit métier (qui reste à développer) |
| Notifications email | Module Notification | 🟡 Le pipeline existe (templates, queue, retry) mais le seul transport est un STUB qui ne fait que logger — un `IEmailTransport` SMTP réel est à implémenter (SUP03, ADR MailKit) |
| Clés API machine-to-machine (agents) | Module Notification (`ApiKey` : prefix + hash, scopes, expiration) | 🟡 L'agrégat existe, le middleware de validation est à compléter |
| GED / stockage de PDF | Module Document | 🟡 À évaluer pour les PDF de réconciliation et les Factur-X archivées |
| UI shell (navigation, composants, thème clair/sombre) | Common.UI + Radzen + `INavSectionProvider` | ✅ Direct |
| Tests d'architecture (frontières de modules) | NetArchTest + CI | ✅ Direct — remplace nos règles de frontière manuelles |
| Tests E2E navigateur | Playwright (infra existante) | ✅ Direct — **impossible avec WPF**, gros gain |
| Pattern modulaire éprouvé | Contracts/Domain/Application/Infrastructure/Web par module | ✅ Le module « Conformité » suit le pattern |

### Ce qui MANQUE dans Stratum (devient du travail Liakont)

| Manque | Conséquence |
|---|---|
| ❌ Dockerfile / appliance | OPS01 |
| ❌ Branding multi-instance (titre « Stratum ERP » et thème en dur) | BRD01 |
| ❌ Provisioning d'instances outillé | OPS02 |
| ❌ Modules enregistrés en dur dans `AppBootstrap.cs` (pas de découverte/configuration) | Le Host Liakont est une **copie adaptée** du Host Stratum (risque de divergence à documenter) |
| ✅ (non bloquant) Hiérarchie de tenants absente (tenants plats) | **Pas nécessaire** : avec le modèle instance-par-éditeur, les tenants d'une instance = clients finaux, à plat |

---

## 6. Question structurante : où vit le code de la plateforme ?

Stratum est un produit (ERP) avec sa propre vie. Liakont en réutilise le socle. Quatre options :

| Option | Description | Pour | Contre |
|---|---|---|---|
| A — Modules Liakont dans Stratum.Host | La passerelle devient des modules de l'ERP | Réutilisation maximale | ❌ Le déploiement Liakont embarque Sales/Reservation/Tourisme (poids mort, surface d'attaque), releases couplées, confusion commerciale. **Rejetée** |
| B — Liakont.Host séparé DANS le repo Stratum | Deux Hosts, un repo | Références projet directes, architecture tests existants | ❌ L'orchestration/docs/specs Liakont vivent dans le repo Liakont → orchestration cross-repo non gérée par le protocole ; cycles de vie couplés |
| **C — Code plateforme dans le repo Liakont, socle Stratum copié (vendored)** | Copier Common/* + modules autonomes (Identity, Job, Notification, Audit) avec note de provenance | ✅ Tout au même endroit pour l'orchestration multi-agents (critique) ; liberté d'adaptation (branding, provisioning) sans risquer l'ERP ; zéro infra de packaging sous contrainte de délai | ❌ Divergence : les correctifs socle ne se propagent pas automatiquement entre les deux produits |
| D — Code plateforme dans le repo Liakont, socle Stratum en packages NuGet | Stratum publie Stratum.Common.* sur GitHub Packages | Propre, pas de divergence silencieuse | ❌ Infrastructure de packaging à monter + friction de version à chaque évolution du socle (et Liakont VA demander des évolutions du socle : branding, API keys, provisioning) |

**Recommandation : Option C maintenant, convergence vers D plus tard** (quand les besoins socle
de Liakont seront stabilisés et reversés dans Stratum). La copie est datée et tracée
(`docs/architecture/provenance-socle-stratum.md` : version, date, fichiers, écarts).

⚠️ **Décision à valider par Karl** — c'est une décision de propriété et de cycle de vie du code,
pas une décision technique.

---

## 7. Risques nouveaux (dus au pivot) et comment les border

| Risque | Mitigation |
|---|---|
| **Responsabilité d'hébergement de données fiscales** (instances hébergées) | Hébergeur français (OVH/Scaleway), DPA (sous-traitant RGPD), RC Pro adaptée, registre des traitements |
| **Archivage 10 ans = engagement de l'opérateur de l'instance** | La réversibilité (OPS06 : export complet par tenant) est un item de V1, pas une option. Clause contractuelle de restitution + sort des archives en fin de contrat |
| **Continuité : panne de plateforme = tous les clients de l'instance bloqués** | Sauvegardes + PRA documenté pour les instances hébergées ; séquestre APP du code pour les self-hosted ; l'agent bufferise pendant l'indisponibilité (aucune donnée perdue) |
| **Empreinte Keycloak** (~1-2 GB JVM) → ~3-5 GB RAM par instance | Acceptable (VPS 15-40 €/mois). Alternative à étudier par ADR : Keycloak mutualisé (un realm par instance) pour les instances hébergées |
| **Matrice de compatibilité agent ↔ plateforme** (les agents se mettent à jour moins vite) | Contrat d'API versionné dès AGT02 ; la plateforme supporte N et N-1 |
| **Deux stacks (agent net48 + plateforme .NET 10)** | L'agent est petit et stable par conception (extraction + transport, aucune logique métier) |

---

## 8. Questions ouvertes (à trancher avant le manifest v6)

| # | Question | Propriétaire | Impact |
|---|---|---|---|
| 1 | **CMP : tenant mutualisé ou instance dédiée/on-premise ?** | ISATECH/CMP | Dimensionne le lot CMP et l'urgence d'OPS01 (appliance) |
| 2 | ~~Où vit le code plateforme ?~~ **TRANCHÉ (2026-06-03)** : repo Liakont, socle Stratum vendored (option C) | Karl | Structure du repo, démarrage du scaffold |
| 3 | **Hébergeur des instances hébergées** (OVH, Scaleway, autre) | Karl | OPS02, contrats, RGPD |
| 4 | **Auth : Keycloak par instance, Keycloak mutualisé, ou alternative ?** | ADR (début du dev) | OPS01/OPS02, empreinte par instance |
| 5 | ~~Nommage des projets~~ **TRANCHÉ (2026-06-03)** : **Liakont.*** | Karl | Scaffold v2 (SOL01 v6) |
| 6 | ~~Sort de la PR #1~~ **TRANCHÉ (2026-06-03)** : fermée sans merge | Karl | Propreté de l'historique |

---

## 9. Travail de préparation avant de relancer l'orchestration

Dans l'ordre :

1. ~~Fermer la PR #1 sans merger~~ ✅ FAIT (2026-06-03)
2. ~~Trancher les questions ouvertes #2 et #5 (repo + nommage)~~ ✅ FAIT (2026-06-03)
3. Réécrire `blueprint.md` (architecture cible plateforme + agent)
4. Réécrire les règles métier du `CLAUDE.md` (la règle « .NET Framework 4.8 jamais .NET 8+ »
   ne vaut plus que pour l'agent ; les frontières Core/plug-ins deviennent les frontières
   de modules Stratum ; etc.)
5. Amender les specs F10/F11 + créer F12 (architecture plateforme/agent + contrat d'API)
6. Réécrire le manifest v6 + `orchestration/items/*.yaml` + blueprints
   (`wpf-screen-item` → `blazor-page-item`, nouveau `agent-item`, etc.)
7. Adapter l'outillage (`verify-fast.ps1`, `run-tests.ps1` : build .NET 10 + build agent x86 net48,
   tests Playwright)
8. Réinitialiser `state.yaml` (v6) dans liakont-orchestration
9. Relancer l'orchestration

**Estimation du travail de préparation : ~4-6 sessions interactives** (comparable à ce qui a été
fait pour les v4/v5). Le développement lui-même est ensuite **plus rapide** que le plan v5 :
les points durs net48 ont disparu, Stratum fournit l'infrastructure, et les écrans Blazor + E2E
sont plus productifs que WPF + checklists manuelles.
