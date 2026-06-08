# Module Reconciliation

> Rapprochement des PDF du pool non lié ↔ documents émis (item TRK07). Certains logiciels
> source déposent tous leurs PDF au même endroit, sans lien fiable avec le document : ce module
> propose/établit le lien, ajoute le PDF au paquet d'archive en addendum (WORM) et journalise le fait
> d'audit. Décision 2026-06-02 : lien automatique en confiance HAUTE uniquement ; confiance moyenne =
> proposition à confirmer ; jamais de lien automatique en dessous (un rapprochement erroné archivé en
> WORM serait incorrigible).

## Purpose

Pour chaque PDF du pool de réconciliation d'un tenant, proposer ou établir une correspondance avec un
document ÉMIS via trois stratégies (TRK07) : numéro de document dans le **nom de fichier** (confiance
haute), numéro dans le **texte du PDF** (confiance haute), **date + montant TTC** candidat unique
(confiance moyenne). Une correspondance de confiance haute est liée AUTOMATIQUEMENT : le PDF rejoint le
paquet d'archive du document en **addendum chaîné** (WORM, TRK05) et un `DocumentReconciledAuto`
append-only est inscrit (Documents). Une confiance moyenne devient une **proposition**, le reste un
**orphelin** — tous deux en file d'attente manuelle. Le moteur de décision est PUR et tenant-scopé.

## Boundaries

| Schéma / ressource | Accès | Détail |
|---|---|---|
| `reconciliation.reconciliation_queue` | **read + insert + update** | File d'attente (base du tenant) : un PDF du pool = une entrée (auto / proposition / orphelin / manuel). Table MUTABLE (une proposition/un orphelin peut devenir `ReconciledManual`) — distincte de la piste d'audit append-only. Migration portée par ce module. |
| Pool PDF (`IIngestedPdfStore`, Ingestion.Contracts) | **read** | Énumération et lecture des PDF du pool du tenant (ADR-0008 : le système de fichiers EST le registre du pool). Aucun couplage au store concret (FileSystem) — uniquement l'abstraction. |
| `IArchiveService` (Archive.Contracts) | **appel** | `AddAddendumAsync` : ajoute le PDF réconcilié au paquet d'archive en addendum chaîné (WORM). |
| `IDocumentReconciliationJournal` (Documents.Contracts) | **appel** | Inscrit le `DocumentEvent` de rapprochement (auto/manuel) sur le document émis — seule surface autorisée (la piste d'audit est interne à Documents). |
| `IDocumentQueries` (Documents.Contracts) | **read** | Liste des documents émis candidats (par état `Issued`) et résolution d'un document par id. |

Le module **n'accède aux autres modules que par leurs `Contracts`** (Ingestion, Archive, Documents) —
jamais leur Domain/Application/Infrastructure (module-rules §3, CLAUDE.md n°14). **Tenant-scopé** : la
base, le coffre et le pool routent vers le tenant courant (blueprint §7).

## Published Events

Aucun en TRK07 (les effets — addendum, fait d'audit, file d'attente — sont synchrones).

## Consumed Events

Aucun en TRK07. Le service (`IReconciliationService`) est **appelé directement** par :
- le **job système** de réconciliation (`ReconciliationFanOutJobHandler` → `TenantJobRunner` SOL06, fan-out
  sur tous les tenants actifs) — déclenché périodiquement (planification module Job), à la demande
  (API04), et après réception de PDF du pool (hook d'ingestion) ;
- la **console** (API04/WEB08) : passe à la demande (`RunForCurrentTenantAsync`), confirmation d'une
  proposition (`ConfirmProposalAsync`) ou lien manuel d'un orphelin (`ConfirmManualReconciliationAsync`),
  rejet d'une proposition (`RejectProposalAsync` → redevient orphelin), affichage d'un PDF
  (`OpenQueueEntryPdfAsync`) et lectures (`IReconciliationQueries`).

> Le SEEDING d'une planification cron et le hook « après réception de PDF du pool » sont du câblage
> consommateur (lots PIP/OPS et endpoint d'ingestion) ; ce module livre la mécanique réutilisable
> (`ITenantJob` + handler de fan-out) déjà enregistrable, comme l'ancrage quotidien TRK06.

## Dependencies

- `Ingestion.Contracts` : `IIngestedPdfStore` (énumération + lecture du pool), `PooledPdfReference`.
- `Archive.Contracts` : `IArchiveService.AddAddendumAsync` (addendum WORM).
- `Documents.Contracts` : `IDocumentReconciliationJournal` (fait d'audit), `IDocumentQueries` (candidats).
- `Stratum.Common` : `IConnectionFactory` (base tenant-scopée), `ITenantContext`, `ITenantJob`/
  `TenantJobRunner` (SOL06, ADR-0006), `Stratum.Modules.Job.Contracts.IJobHandler`.
- **PdfPig** (Apache-2.0, ADR-0010) — extraction de texte, isolée dans l'Infrastructure derrière le port
  `IPdfTextExtractor`.

## Layers

- **Contracts** : `IReconciliationService` (passe `RunForCurrentTenantAsync` ; lien manuel
  `ConfirmManualReconciliationAsync` ; et pour la console API04 : `ConfirmProposalAsync` — confirmer la
  proposition vers le document proposé, `RejectProposalAsync` — rejeter une proposition, qui REDEVIENT un
  orphelin), `IReconciliationQueries` (propositions, orphelins, documents sans PDF, et
  `OpenQueueEntryPdfAsync` — flux du PDF d'une entrée pour affichage) + DTOs (dont `ReconciliationPdfContent`).
- **Web (console, API04)** : `Web/ReconciliationEndpointMapping.cs` monte `/api/v1/reconciliation`
  (file d'attente, PDF, `confirm`/`reject` d'une proposition, `link` manuel) — permission
  `liakont.actions`. Aucune logique métier dans les endpoints : ils délèguent au service ; l'identité de
  l'opérateur (journalisée) vient du contexte authentifié (`IActorContext`).
- **Domain** : `ReconciliationEngine` (PUR, 3 stratégies + règle d'ambiguïté), `DocumentNumberMatcher`,
  value objects (`PooledPdfContent`, `DocumentCandidate`, `ReconciliationDecision`), entité
  `ReconciliationQueueEntry`, énumérations (`MatchStrategy`, `MatchConfidence`, `ReconciliationStatus`).
- **Application** : `ReconciliationService` (orchestration : pool → moteur → addendum/audit/file),
  ports `IPdfTextExtractor` et `IReconciliationQueueStore`.
- **Infrastructure** : `PdfPigTextExtractor`, `PostgresReconciliationQueueStore`, migrations DbUp
  (schéma `reconciliation` + file d'attente), `ReconciliationTenantJob` + `ReconciliationFanOutJobHandler`,
  enregistrement DI.
