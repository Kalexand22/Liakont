# Conformat — Blueprint d'architecture

Doctrine d'architecture du produit. Toute décision de code doit pouvoir se justifier
par rapport à ce document, aux specs `docs/conception/F*.md` ou à un ADR dans `docs/adr/`.

---

## 1. Objectif du produit

Conformat est une **passerelle de conformité facturation électronique** entre un logiciel
métier legacy (base de données accessible, aucune API) et une **Plateforme Agréée (PA)**
qui porte le routage, l'interopérabilité et la déclaration fiscale (réforme française,
échéances septembre 2026 / 2027).

**Flux nominal** :

```
Base legacy ──extraction (lecture seule, ODBC)──▶ Modèle pivot (EN 16931)
   ──mapping TVA (table validée)──▶ Contrôles qualité ──▶ Envoi PA (B2Brouter)
   ──▶ Suivi statuts + tax reports ──▶ Piste d'audit (SQLite, 10 ans)
```

**Ce que Conformat N'EST PAS** :
- Pas une Plateforme Agréée (l'agrément est délégué à la PA partenaire)
- Pas un ERP (il ne remplace pas le logiciel source)
- Pas un outil OCR/scan (extraction structurée, pas de papier)
- Pas un SaaS multi-tenant (on-premise : une config = un client = une étude/site)

## 2. Doctrine

1. **Le Core ne connaît aucune source.** La frontière produit/adaptateur est sacrée :
   `Gateway.Core` dépend de la réforme (EN 16931, TVA, PA), jamais d'un logiciel source.
   Un adaptateur dépend du logiciel source, jamais d'un autre adaptateur.
2. **Lecture seule stricte de la base source.** Jamais d'écriture, jamais de verrou,
   jamais de transaction sur la base du client. C'est une décision structurante
   (zéro autorisation éditeur nécessaire) et un argument commercial.
3. **Le pivot ne calcule rien.** Il porte les montants calculés par le logiciel source.
   Conformat contrôle la cohérence (BR-CO-15...), il ne recalcule jamais.
4. **Aucune règle fiscale inventée.** Toute règle TVA/validation provient de
   `docs/conception/` ou est explicitement marquée « à valider expert-comptable ».
   En cas de doute : bloquer le document, jamais deviner.
5. **Bloquer plutôt qu'envoyer faux.** Un document douteux reste en état `Blocked` avec
   un message opérateur français. Jamais d'envoi à l'aveugle.
6. **La piste d'audit est immuable.** `DocumentEvent` est append-only. Aucune purge
   automatique des tables d'audit. Conservation 10 ans.
7. **Tout message visible par l'opérateur est en français**, sans jargon technique,
   avec le numéro de document et l'action corrective.

## 3. Architecture globale

**Monolithe modulaire en couches simples** — pas de microservices, pas de bus d'événements,
pas de CQRS : le produit est un EXE on-premise qui traite quelques milliers de documents par an.

```
Gateway.sln
│
├─ Gateway.Core/                      ★ LE PRODUIT — générique (≈90 % du code)
│  ├─ Pivot/                          PivotDocument, PivotLine, PivotLineTax, PivotParty,
│  │                                  PivotPayment, PivotTotals, IExtractor, FixtureExtractor,
│  │                                  sérialisation canonique + hash SHA-256
│  ├─ TvaMapping/                     MappingTable (JSON externe versionné), TvaMapper,
│  │                                  MappingTrace, MappingCoverageReport
│  ├─ Validation/                     ValidationPipeline, IDocumentRule, ~20 règles
│  │                                  (Luhn, BR-CO-15, VATEX, avoirs, garde-fou B2B)
│  ├─ Tracking/                       SQLite : Document, DocumentEvent (append-only),
│  │                                  TaxReport, PaymentAggregate, RunLog, AuditExporter
│  ├─ PaClient/                       IPaClient (abstraction multi-PA) + B2Brouter/
│  │                                  (implémentation eDocExchange)
│  ├─ Pipeline/                       PipelineRunner (extract→check→send→sync),
│  │                                  pipeline avoirs, agrégation paiements, mutex global
│  └─ Configuration/                  GatewayConfig (JSON), SecretProtector (DPAPI)
│
├─ Gateway.Adapters.EncheresV6/       ★ PREMIER ADAPTATEUR (≈10 % du code, spécifique)
│  ├─ PervasiveExtractor              ODBC réel (déployé uniquement sur serveur licencié)
│  ├─ EncheresV6FixtureExtractor      Rejeu de fixtures JSON (dev, démo, tests)
│  └─ EncheresV6RowMapper             Transformation lignes source → pivot (partagé)
│
├─ Gateway.App/                       WPF — console admin (supervision, envoi manuel, audit)
├─ Gateway.Cli/                       EXE ligne de commande (tâche planifiée Windows)
└─ Gateway.Service/                   Service Windows (ordonnanceur interne)

tests/
├─ Gateway.Core.Tests/                Unit + integration (SQLite, fixtures, FakePaClient)
├─ Gateway.Adapters.EncheresV6.Tests/
└─ Gateway.Staging.Tests/             Tests B2Brouter staging (JAMAIS en CI — clé API requise)
```

## 4. Stack technique (décisions actées)

| Composant | Choix | Justification |
|---|---|---|
| Framework | **.NET Framework 4.8** (jamais 4.7, jamais .NET 8+) | Compatibilité Windows 7 SP1 / Server 2008 R2 — portée legacy maximale |
| Style projets | SDK-style csproj ciblant `net48` | `dotnet build` utilisable, outillage moderne, runtime legacy |
| Builds | **x86 ET x64** | Drivers ODBC legacy (Pervasive) souvent 32-bit |
| UI console | **WPF** desktop + MahApps.Metro | Pas web, pas multi-tenant, déploiement simple on-premise |
| MVVM | Minimal maison (pas de Prism) | INotifyPropertyChanged + RelayCommand ≈ 50 lignes |
| Mode auto | EXE CLI + tâche planifiée **OU** Service Windows | Les deux livrés, un seul activé par déploiement |
| Persistence | **SQLite** local (WAL) | Anti-doublons, états, piste d'audit ; console lit pendant que le CLI écrit |
| Accès source | **ODBC générique, lecture seule** | Pilote de chaque système ; zéro modification du logiciel source |
| Sérialisation | Newtonsoft.Json | Pattern SynchroAxelor éprouvé chez le client fondateur |
| HTTP | System.Net.Http + TLS 1.2/1.3 (ServicePointManager) | Built-in net48 |
| PA partenaire | **B2Brouter eDocExchange** (staging validé) | Derrière l'abstraction `IPaClient` (multi-PA futur) |
| Formats sortie | JSON propriétaire B2Brouter (V1) | Factur-X/UBL/CII générés côté PA ; génération directe = phase 2 |
| Secrets | DPAPI (machine scope) | Clé API PA et SMTP jamais en clair |

**Règle de dépendances** : tout est built-in .NET Framework 4.8 ou Newtonsoft.Json + SQLite +
MahApps.Metro. **Tout nouveau package nécessite un ADR.**

## 5. Rôles des couches

| Couche | Responsabilité | Interdit |
|---|---|---|
| `Pivot` | Représentation neutre, contrat d'extraction | Calculs, accès réseau, accès fichier |
| `TvaMapping` | Code régime source → catégorie/taux/VATEX | Règles en dur (tout vient de la table) |
| `Validation` | Détection pré-envoi de tout ce qui serait rejeté | Correction automatique des données |
| `Tracking` | États, anti-doublons, piste d'audit | Purge automatique, update d'events |
| `PaClient` | Toutes les interactions PA | Fuite de types HTTP/B2Brouter hors du dossier |
| `Pipeline` | Orchestration extract→check→send→sync | Logique métier (déléguée aux couches) |
| Hôtes (App/Cli/Service) | Coquilles minces : config + appel moteur + restitution | Toute logique métier |

## 6. Communication inter-couches

- Les couches du Core communiquent par **interfaces et types du pivot** — jamais par types
  concrets d'une autre couche.
- Les adaptateurs n'implémentent que `IExtractor` (+ leur config). Ils ne voient ni le
  Tracking, ni le PaClient, ni le Pipeline.
- Les hôtes ne voient que `PipelineRunner`, `GatewayConfig` et les repositories de lecture
  (console). Jamais d'appel PA direct depuis un hôte.

## 7. Gestion des montants (règle absolue)

- **`decimal` partout.** float/double sur un montant = P1 en review, sans exception.
- Arrondi commercial (half-up) à 2 décimales.
- Les montants originaux de la source sont conservés bruts dans `SourceData`.
- Aucune tolérance dans les réconciliations de totaux (BR-CO-15 est une règle fatale).

## 8. Stratégie de test

| Niveau | Quoi | Quand |
|---|---|---|
| Unit | Toutes les couches Core, ViewModels WPF | verify-fast (chaque commit) |
| Integration | SQLite réel (base temp), fixtures EncheresV6, FakePaClient | run-tests (avant review) |
| Parité | FixtureExtractor vs PervasiveExtractor mocké sur le même jeu | run-tests |
| Staging | Envois réels B2Brouter staging | Manuel uniquement (jamais CI), avant chaque gate PA |
| Smoke WPF | Checklists manuelles par écran (`docs/architecture/smoke-checklists/`) | Avant chaque gate console |

**Faux verts interdits** : un test écrit mais jamais exécuté, une assertion affaiblie,
un test `[Skip]`, un script qui réussit quand il devrait échouer — tous P1 en review.

## 9. Pipeline de validation d'un changement

```
code → verify-fast (build + analyzers + unit tests)
     → run-tests (integration) [si applicable]
     → codex-review (P1/P2, rounds jusqu'à clean)
     → merge --no-ff dans la branche de segment
     → gate humaine (PR review + validation fonctionnelle) avant main
```

## 10. Gouvernance IA

- 1 item d'orchestration = 1 branche = 1 objectif = bornage strict du périmètre.
- L'IA ne merge **jamais** dans `main` — les gates sont validées par un humain.
- Les décisions fiscales (régime 6, TVA sur débits, OperationCategory) appartiennent à
  l'expert-comptable du client. L'IA les matérialise, elle ne les prend pas.
- Toute correction utilisateur alimente `tasks/lessons.md`.

## 11. Points ouverts structurants (à trancher hors code)

| Point | Propriétaire | Impact |
|---|---|---|
| Régime 6 EncheresV6 = marge EU-J ou hors champ ? | Expert-comptable CMP | Table TVA (TVA04) |
| CMP a-t-il opté pour la TVA sur les débits ? | Expert-comptable CMP | Existence de F9 (PIP03, WPF06) |
| OperationCategory = Mixte ? | Expert-comptable CMP | F9 sur la part frais |
| Volume acheteurs professionnels CMP ? | Expert-comptable CMP | F8 garde-fou vs phase 2 B2B |
| Transmission Flux 10.2/10.4 disponible ? | Support B2Brouter | Activation de PaymentReportingEnabled |
| Montant marge cas n°33 | Support B2Brouter | Transformation pivot → JSON (PAC02) |

Ces points ne bloquent pas le démarrage du développement (tout est derrière des flags ou
des tables marquées « à valider »), mais ils bloquent la **mise en production**.
