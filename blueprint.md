# Conformat — Blueprint d'architecture

Doctrine d'architecture du produit. Toute décision de code doit pouvoir se justifier
par rapport à ce document, aux specs `docs/conception/F*.md` ou à un ADR dans `docs/adr/`.

---

## 1. Objectif du produit

Conformat est une **passerelle de conformité facturation électronique GÉNÉRIQUE** entre
des logiciels métier legacy (base de données accessible, aucune API) et des **Plateformes
Agréées (PA)** qui portent le routage, l'interopérabilité et la déclaration fiscale
(réforme française, échéances septembre 2026 / 2027).

**Flux nominal** :

```
Base legacy ──extraction (lecture seule, ODBC)──▶ Modèle pivot (EN 16931)
   ──mapping TVA (table paramétrée)──▶ Contrôles qualité ──▶ Envoi PA (plug-in)
   ──▶ Suivi statuts + tax reports ──▶ Piste d'audit (10 ans)
```

**Ce que Conformat N'EST PAS** :
- Pas une Plateforme Agréée (l'agrément est délégué aux PA partenaires)
- Pas un ERP (il ne remplace pas le logiciel source)
- Pas un développement spécifique pour un client (c'est un PRODUIT)
- Pas un SaaS multi-tenant (on-premise : une config = un déploiement)

## 2. Le principe fondateur : la généricité sur DEUX axes

```
                         LE PRODUIT CONFORMAT (générique)
        ┌────────────────────────────────────────────────────────────────────┐
        │                                                                    │
 Plug-ins SOURCE          CORE (ne connaît NI les sources NI les PA)   Plug-ins PA
 (IExtractor)             ┌──────────────────────────────┐          (IPaClient + capacités)
 ┌─────────────────┐      │  Pivot EN 16931              │            ┌──────────────────┐
 │ EncheresV6      │─────▶│  Moteur TVA (tables = param) │───────────▶│ B2Brouter        │
 │ (le 1er)        │      │  Validation (~20 règles)     │            │ (PA #1)          │
 └─────────────────┘      │  Tracking / piste d'audit    │            ├──────────────────┤
 ┌─────────────────┐      │  Pipeline extract→check→send │            │ Super PDP        │
 │ Futurs : AS400, │      └──────────────────────────────┘            │ (PA #2 — Offre   │
 │ Sage, Access... │                                                  │  Éco marque grise)│
 └─────────────────┘       Hôte : Service (API) + clients             ├──────────────────┤
        │                                                             │ Futurs : Iopole, │
        │                                                             │ Seqino...        │
        │                                                             └──────────────────┘
        └──────────────── DEUX axes de plug-in symétriques ──────────────────┘

                    PARAMÉTRAGE PAR DÉPLOIEMENT (jamais du code) :
          table TVA du client (validée par SON expert-comptable — éditable depuis
          la console, toute modification = revalidation requise) • SIREN •
          chaîne ODBC • choix du/des PA + comptes • planification
```

**Règles absolues de généricité :**

1. **Aucune donnée client dans le code.** Table TVA, SIREN, raison sociale, chaîne de
   connexion, compte PA : tout est paramétrage, livré par déploiement dans `deployments/`.
   Le Core embarque uniquement des tables/configs d'EXEMPLE pour ses tests.
2. **Aucune fonctionnalité du produit ne dépend de ce qu'UN PA sait faire.** Chaque
   plug-in PA déclare ses **capacités** (`PaCapabilities`) ; le produit s'adapte.
   Si un PA ne supporte pas l'e-reporting de paiement, c'est le PA qui est limité —
   le produit, lui, est complet.
3. **Tout plug-in PA doit passer les tests de contrat** (`Gateway.PaClients.Contract.Tests`).
   Tout plug-in source doit produire un pivot qui passe la validation.
4. **Le Core ne référence jamais un plug-in.** Ni source, ni PA. Les plug-ins référencent
   le Core, jamais l'inverse, jamais entre eux.

## 3. Architecture d'exécution : le Service est le cœur

Décision d'architecture (2026-06-02) : pour permettre le **multi-utilisateurs** sur la
console d'administration (plusieurs comptables, depuis leurs postes), le produit s'exécute
en **service Windows central** qui est le **seul processus** à toucher la base de tracking.

```
POSTE COMPTABLE 1        POSTE COMPTABLE 2          POSTE COMPTABLE N
  ┌──────────────┐         ┌──────────────┐          ┌──────────────┐
  │ Gateway.App  │         │ Gateway.App  │          │ Gateway.App  │
  │ (console WPF)│         │ (console WPF)│          │ (console WPF)│
  └──────┬───────┘         └──────┬───────┘          └──────┬───────┘
         │        HTTP + auth Windows (réseau local)        │
         └──────────────────────┬──────────────────┬────────┘
                                ▼                  ▼
  ╔═════════════════════════════════════════════════════════╗
  ║ SERVEUR (proche de la base legacy)                       ║
  ║                                                          ║
  ║  Gateway.Service (service Windows)                       ║
  ║   ├─ API HTTP self-hosted (auth Windows intégrée)        ║
  ║   ├─ Ordonnanceur interne (runs planifiés)               ║
  ║   ├─ PipelineRunner (extract → check → send → sync)      ║
  ║   └─ Tracking SQLite ◄── SEUL processus écrivain         ║
  ║                                                          ║
  ║  Gateway.Cli (utilitaire) : check-config, encrypt-secret,║
  ║   run manuel (service arrêté), backup                    ║
  ║                                                          ║
  ║  ODBC (lecture seule) ──▶ base du logiciel legacy        ║
  ╚═════════════════════════════════════════════════════════╝
```

**Conséquences structurantes :**

- **Un seul écrivain** (le service) → SQLite suffit définitivement, aucun problème de
  concurrence, aucun verrou réseau, la piste d'audit ne peut pas être corrompue par
  un accès concurrent.
- **La console WPF ne touche JAMAIS la base ni le Core directement** : elle parle à
  l'API HTTP via `Gateway.ApiClient`. Elle peut tourner sur n'importe quel poste du réseau.
- **Le CLI devient un utilitaire** de mise en service et de secours (check-config,
  chiffrement de secrets, run manuel quand le service est arrêté — protégé par mutex,
  backup). Le mode nominal de production est le service.
- **Évolution naturelle** : une console web (navigateur) pourra remplacer/compléter la
  console WPF sans toucher au serveur — l'API est déjà là.

## 4. Structure de la solution

```
src/
├─ Gateway.Core/                       ★ LE PRODUIT (générique)
│  ├─ Pivot/                           PivotDocument, PivotLine, PivotLineTax, PivotParty,
│  │                                   PivotPayment, PivotTotals, IExtractor, FixtureExtractor,
│  │                                   sérialisation canonique + hash SHA-256
│  ├─ TvaMapping/                      MappingTable (JSON externe), TvaMapper, MappingTrace,
│  │                                   MappingCoverageReport, MappingTableEditor + MappingChangeLog
│  │                                   (édition journalisée depuis la console) — AUCUNE table
│  │                                   client embarquée
│  ├─ Validation/                      ValidationPipeline, IDocumentRule, ~20 règles
│  ├─ Tracking/                        SQLite : Document, DocumentEvent (append-only),
│  │                                   TaxReport, PaymentAggregate, RunLog, AuditExporter,
│  │                                   Reconciliation (rapprochement PDF pool ↔ documents)
│  ├─ PaClient/                        IPaClient + PaCapabilities + DTOs (ABSTRACTION SEULE)
│  ├─ Pipeline/                        PipelineRunner, pipeline avoirs, agrégation paiements
│  └─ Configuration/                   GatewayConfig (JSON), SecretProtector (DPAPI)
│
├─ Gateway.PaClients.B2Brouter/        ★ Plug-in PA #1 (staging validé)
├─ Gateway.PaClients.SuperPdp/         ★ Plug-in PA #2 (Offre Éco marque grise)
├─ Gateway.Adapters.EncheresV6/        ★ Plug-in source #1 (Magic XPA / Pervasive)
│
├─ Gateway.Api/                        Contrats de l'API HTTP (DTOs, versionnés)
├─ Gateway.ApiClient/                  Client .NET de l'API (utilisé par la console)
│
├─ Gateway.Service/                    L'HÔTE : service Windows = ordonnanceur + pipeline
│                                      + API HTTP self-hosted + seul accès SQLite
├─ Gateway.App/                        Console WPF multi-postes (cliente de l'API)
└─ Gateway.Cli/                        Utilitaire : check-config, encrypt-secret,
                                       run manuel de secours, backup

tests/
├─ Gateway.Core.Tests/                 Unit + integration (SQLite temp, fixtures, FakePaClient)
├─ Gateway.PaClients.Contract.Tests/   ★ Tests de contrat : TOUT plug-in PA doit les passer
├─ Gateway.PaClients.B2Brouter.Tests/  (+ suite staging séparée, jamais en CI)
├─ Gateway.PaClients.SuperPdp.Tests/   (+ suite sandbox séparée, jamais en CI)
├─ Gateway.Adapters.EncheresV6.Tests/
├─ Gateway.Service.Tests/              (API + ordonnanceur)
└─ Gateway.App.Tests/                  (ViewModels)

config/
└─ exemples/                           Table TVA d'exemple, config d'exemple (pour tests/démo)

deployments/                           ★ PARAMÉTRAGE par déploiement (jamais du code)
└─ cmp/                                Table TVA CMP (à valider expert-comptable),
                                       config CMP, fixtures de démo ISATECH
```

## 5. Stack technique (décisions actées)

| Composant | Choix | Justification |
|---|---|---|
| Framework | **.NET Framework 4.8** (jamais 4.7, jamais .NET 8+) | Compatibilité Windows 7 SP1 / Server 2008 R2 — portée legacy maximale |
| Style projets | SDK-style csproj ciblant `net48` | `dotnet build` utilisable, outillage moderne, runtime legacy |
| Builds | **x86 ET x64** | Drivers ODBC legacy (Pervasive) souvent 32-bit |
| Hôte d'exécution | **Service Windows + API HTTP self-hosted** | Multi-utilisateurs, un seul écrivain, ordonnanceur interne |
| API HTTP | Self-host .NET 4.8 (HttpListener/OWIN) + **auth Windows intégrée** | Pas de credentials à distribuer, IT-friendly |
| UI console | **WPF** desktop + MahApps.Metro, **cliente de l'API** | Multi-postes, pas d'accès BDD direct |
| MVVM | Minimal maison (pas de Prism) | INotifyPropertyChanged + RelayCommand ≈ 50 lignes |
| Persistence | **SQLite** local (WAL), accédé par le Service UNIQUEMENT | Un seul écrivain → zéro admin, backup = copie fichier |
| Accès source | **ODBC générique, lecture seule** | Pilote de chaque système ; zéro modification du logiciel source |
| Sérialisation | Newtonsoft.Json | Pattern éprouvé chez le client fondateur |
| Tests | xUnit | Standard, net48-compatible |
| PA | Plug-ins **B2Brouter** + **Super PDP**, abstraction `IPaClient` + capacités | Multi-PA = exigence commerciale (Offre Éco), pas du futur-proofing |
| Secrets | DPAPI (machine scope) | Clés API PA et SMTP jamais en clair |

**Règle de dépendances** : tout est built-in .NET Framework 4.8 ou Newtonsoft.Json + SQLite +
MahApps.Metro. **Tout nouveau package nécessite un ADR.**

## 6. Rôles des couches

| Couche | Responsabilité | Interdit |
|---|---|---|
| `Pivot` | Représentation neutre, contrat d'extraction | Calculs, accès réseau, accès fichier |
| `TvaMapping` | Code régime source → catégorie/taux/VATEX via table | Règles en dur, données client embarquées |
| `Validation` | Détection pré-envoi de tout ce qui serait rejeté | Correction automatique des données |
| `Tracking` | États, anti-doublons, piste d'audit | Purge automatique, update d'events, accès hors Service |
| `PaClient` (abstraction) | Contrat IPaClient + capacités | Référencer un plug-in concret |
| Plug-ins PA | Implémenter IPaClient pour UNE plateforme | Fuiter leurs types hors de leur assembly |
| Plug-ins source | Implémenter IExtractor pour UN logiciel | Écrire/verrouiller la base source, voir le Tracking ou les PA |
| `Pipeline` | Orchestration extract→check→send→sync | Logique métier (déléguée aux couches) |
| `Api` / `ApiClient` | Contrat HTTP versionné entre Service et clients | Logique métier |
| `Service` | Hôte : ordonnanceur + pipeline + API + accès SQLite | — |
| `App` (console) | UI cliente de l'API | Accès direct BDD/Core/plug-ins |
| `Cli` | Utilitaire setup/secours | Toute logique métier |

## 7. Gestion des montants (règle absolue)

- **`decimal` partout.** float/double sur un montant = P1 en review, sans exception.
- Arrondi commercial (half-up) à 2 décimales.
- Les montants originaux de la source sont conservés bruts dans `SourceData`.
- Aucune tolérance dans les réconciliations de totaux (BR-CO-15 est une règle fatale).

## 8. Stratégie de test

| Niveau | Quoi | Quand |
|---|---|---|
| Unit | Toutes les couches Core, ViewModels WPF, API | verify-fast (chaque commit) |
| Contrat PA | `Gateway.PaClients.Contract.Tests` sur chaque plug-in PA (via mock HTTP) | run-tests (avant review) |
| Integration | SQLite réel (base temp), fixtures EncheresV6, FakePaClient, API in-process | run-tests |
| Parité | FixtureExtractor vs PervasiveExtractor mocké sur le même jeu | run-tests |
| Staging/Sandbox | Envois réels B2Brouter staging / Super PDP sandbox | Manuel uniquement (jamais CI), avant chaque gate PA |
| Smoke WPF | Checklists manuelles par écran (`docs/architecture/smoke-checklists/`) | Avant chaque gate console |

**Faux verts interdits** : un test écrit mais jamais exécuté, une assertion affaiblie,
un test `[Skip]`, un script qui réussit quand il devrait échouer — tous P1 en review.

## 9. Pipeline de validation d'un changement

```
code → verify-fast (build + analyzers + unit tests)
     → run-tests (integration + contrat) [si applicable]
     → codex-review (P1/P2, rounds jusqu'à clean)
     → merge --no-ff dans la branche de segment
     → gate humaine (PR review + validation fonctionnelle) avant main
```

## 10. Gouvernance IA

- 1 item d'orchestration = 1 branche = 1 objectif = bornage strict du périmètre.
- L'IA ne merge **jamais** dans `main` — les gates sont validées par un humain.
- Les décisions fiscales (régime 6, TVA sur débits, OperationCategory) appartiennent à
  l'expert-comptable du client concerné. L'IA les matérialise dans le PARAMÉTRAGE du
  déploiement, jamais dans le code.
- Toute correction utilisateur alimente `tasks/lessons.md`.

## 11. Points ouverts structurants (à trancher hors code)

| Point | Propriétaire | Impact |
|---|---|---|
| Régime 6 EncheresV6 = marge EU-J ou hors champ ? | Expert-comptable CMP | Paramétrage CMP (CMP01) — pas le produit |
| CMP a-t-il opté pour la TVA sur les débits ? | Expert-comptable CMP | Paramétrage CMP (déclenche ou non le e-reporting paiement pour CE déploiement) |
| OperationCategory = Mixte ? | Expert-comptable CMP | Paramétrage CMP |
| Volume acheteurs professionnels CMP ? | Expert-comptable CMP | Paramétrage CMP (garde-fou) / priorité phase 2 B2B |
| Transmission Flux 10.2/10.4 chez B2Brouter | Support B2Brouter | Capacité du plug-in B2Brouter (le produit, lui, est prêt) |
| Transmission Flux 10.2/10.4 chez Super PDP | Support Super PDP (DR17-A4) | Capacité du plug-in Super PDP |
| Montant marge cas n°33 | Support B2Brouter | Transformation du plug-in B2Brouter |
| **Ouverture sandbox Super PDP** | Nous (DR17-A4, ~1-2 jours) | Prérequis du plug-in Super PDP (PAS02+) |

Ces points ne bloquent pas le développement du produit (la généricité les isole dans les
plug-ins et le paramétrage), mais ils bloquent les mises en production concernées.
