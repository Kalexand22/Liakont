# Plan — BUG-24 : état `EReported` persisté (fin de l'overlay read-time)

> Spec de référence : **ADR-0037** (`docs/adr/ADR-0037-etat-document-ereported-canal-b2c-agrege.md`).
> Bug : `tasks/bugs-recette-encheres-b2c.md` (BUG-24, recette 2026-07-01).
> Correctif incomplet à remplacer : commit `39174c9e` (overlay read-time sur la fiche seule).
>
> **Un seul hook couvre les 4 canaux B2C** (marge / prix-total enchères / taxable ordinaire / export) :
> `B2cReportingEmitter.EmitOneAsync`. Le fix est « état d'abord » : persister `EReported`, retirer l'overlay.

## Objectif

Après une émission B2C **acceptée**, le document passe de l'état persisté `ReadyToSend` à `EReported`, de
sorte que **liste, compteurs et fiche** concordent à partir d'**une** donnée (`documents.state`). Supprimer
l'overlay read-time qui ne corrigeait que la fiche.

## Lot 1 — Domaine Documents (état + machine + événement)

- [ ] `DocumentState.cs` : ajouter `EReported` (après `Issued`), commentaire XML « voie agrégée
  e-reporting B2C : inclus dans une déclaration acceptée par la PA ; distinct d'`Issued` (voie document,
  preuve pièce). Sans transition sortante. »
- [ ] `DocumentStateMachine.cs` : ajouter `(DocumentState.ReadyToSend, DocumentState.EReported)` à
  `AllowedTransitions` ; mettre à jour le `<remarks>` (nouvelle transition + `EReported` sans sortie).
- [ ] `DocumentEventType.cs` : ajouter `DocumentEReported` (append-only ; le détail porte l'`emissionBatchId`).
- [ ] `Document.cs` : ajouter `MarkEReported(occurredAtUtc, emissionBatchId, detail?)` en miroir de
  `MarkIssued` (valide via la machine à états, pose `State = EReported`, renvoie l'événement
  `DocumentEReported` avec l'`emissionBatchId` dans le détail).
- [ ] `DocumentStateMachineTests` : `ReadyToSend → EReported` autorisé ; tout autre chemin vers `EReported`
  refusé ; `EReported → *` refusé.

## Lot 2 — Contrats + Infrastructure Lifecycle (port de transition)

- [ ] `IDocumentLifecycle.cs` (Contracts) : ajouter
  `Task MarkEReportedAsync(Guid documentId, Guid emissionBatchId, CancellationToken ct = default)` avec doc
  XML (atomique état+événement, tenant-scopé, **non-throwant/idempotent**).
- [ ] `DocumentLifecycle.cs` (Infrastructure) : implémenter sur le modèle de `MarkIssuedAsync`
  (`TransitionAsync` + upsert + append event, même transaction tenant-scopée), MAIS :
  - rejeu (`document` déjà `EReported`) ⇒ **no-op réussi** (pas de throw) ;
  - document hors `ReadyToSend` (course) ⇒ **ne pas transitionner**, journaliser (Warning FR + n° doc),
    pas d'exception (patron `*RecheckAsync` : outcome, pas throw).
- [ ] Tests intégration Lifecycle : `ReadyToSend → EReported` (état + événement en base) ; rejeu = no-op ;
  hors-état = pas de transition, pas de throw ; **isolation cross-tenant** (≥ 2 bases).

## Lot 3 — Hook d'émission (Pipeline, le point unique)

- [ ] `B2cReportingEmitter.EmitOneAsync`, bloc `if (status == B2cMarginEmissionStatus.Issued)` : dans la
  boucle `foreach (var contribution in transaction.Contributions)` existante (celle du gel de lien),
  ajouter `await lifecycle.MarkEReportedAsync(contribution.DocumentId, emissionBatchId, ct)` **à côté** de
  `FreezeReportingPieceLinkAsync`. Résoudre `IDocumentLifecycle` comme les autres services (`services.GetRequiredService`).
  - **Résilience** : la transition ne doit PAS pouvoir faire retomber l'émission en `Technical` (le POST est
    accepté) → non-throwante (garantie côté impl Lot 2 + éventuel try/log local, cohérent avec le gel).
- [ ] Test intégration (réutiliser un harnais B2C existant, ex. `B2cMarginAggregatorJobTests`) : POST
  accepté ⇒ chaque `document_id` contributeur est `EReported` en base **et** exclu de `GetByStateAsync("ReadyToSend")`.
- [ ] Test : échec simulé de `MarkEReportedAsync` après POST accepté ⇒ l'émission reste `Issued` (pas
  `Technical`), Warning journalisé.
- [ ] Vérifier les **4 canaux** couverts (marge, taxable prix-total, taxable ordinaire, export) — un test
  par canal ou un test paramétré, sinon au minimum marge + export (unitaire) pour couvrir agrégat ET unitaire.

## Lot 4 — Suppression de l'overlay read-time (remplace `39174c9e`)

- [ ] `DocumentDetailConsoleQueryService.cs` : supprimer `ResolveB2cReportedBatchAsync` + son appel + le
  champ `_emissions` + le paramètre ctor `IB2cMarginEmissionQueries` (et la registration DI côté Host) +
  `LogB2cReportedResolutionFailed`.
- [ ] `DocumentDetailViewModel.cs` : supprimer `B2cReportedBatchId`.
- [ ] `DocumentDetailView.razor` : remplacer les 3 branches `Model.B2cReportedBatchId` par le badge d'état
  standard (`State = Model.Document.State`) ; re-sourcer le lien « Voir la déclaration »
  (`/emissions-marge-b2c/{batchId}`) depuis l'`emissionBatchId` de l'événement `DocumentEReported`
  (via le VM/lifecycle), plus aucune requête journal au read-time.
- [ ] `DocumentActionBar.razor` : `CanSend = IsReadyToSend && !IsB2cReported` → retirer `!IsB2cReported`
  (l'état n'est plus `ReadyToSend`) ; supprimer `IsB2cReported`.
- [ ] `PostgresB2cMarginEmissionQueries.GetIssuedEmissionBatchForDocumentAsync` (+ la méthode de
  `IB2cMarginEmissionQueries`) : supprimer une fois le lien re-sourcé par l'événement.

## Lot 5 — Restitution console (`EReported` visible)

- [ ] `DocumentStateDisplay.cs` : ajouter `EReported` à `CanonicalOrder` (rang après `Issued`) et un `case`
  dans `For()` — libellé FR **« E-reporté »**, `Severity.Success`.
- [ ] Vérifier `DocumentCountsBanner.razor` (hérite de `CanonicalOrder`, clic-filtre) et le filtre État de
  la grille (`@State` = EReported).
- [ ] Test bUnit : liste ET fiche affichent « E-reporté » (badge + compteur) pour un document `EReported`
  (page Blazor sans test = P1, CLAUDE.md review n°19).

## Lot 6 — Migration de backfill (V012 Documents)

- [ ] `V012__backfill_ereported_state.sql` (Documents/Migrations — **V011 pris, un V010 est dupliqué**,
  vérifier le prochain libre réel) :
  ```sql
  UPDATE documents.documents d
     SET state = 'EReported'
   WHERE d.state = 'ReadyToSend'
     AND EXISTS (SELECT 1 FROM pipeline.b2c_margin_emissions e
                  WHERE e.document_id = d.id AND e.status = 'Issued');
  ```
  - En-tête commenté sourcé (BUG-24 / ADR-0037) ; garantir que le schéma `pipeline` + la table existent
    (ordre inter-modules) ; `documents.documents` NON WORM → UPDATE légitime.
- [ ] Test migration sur base réelle : documents figés `ReadyToSend` + journal `Issued` ⇒ `EReported` après
  migration ; documents `ReadyToSend` **sans** émission ⇒ inchangés ; isolation cross-tenant.

## Vérification (obligatoire, CLAUDE.md)

- [ ] `verify-fast.ps1` vert (Release — build Debug rate SA12xx ; cf. lesson) sur les **deux** solutions.
- [ ] `run-tests.ps1` vert (le hook touche endpoints/DI/état → tests d'intégration Testcontainers requis).
- [ ] `codex-review.ps1` : boucle propre (P1/P2 corrigés).
- [ ] Recette manuelle Isatech : un document B2C reporté apparaît **« E-reporté »** en **liste** ET en
  **fiche** ; les compteurs « À envoyer » ne le comptent plus.

## Hors périmètre (posés, à ne pas perdre — ADR-0037 §Points NON TRANCHÉS)

- **D1** — sort des agrégats **rejetés** (docs `ReadyToSend` exclus par attempt-once, ni reportés ni
  re-tentés) : item/ADR dédié « gestion des rejets d'e-reporting agrégé ».
- **D2** — bouton « Envoyer » sur un doc B2C `ReadyToSend` **avant** émission : vérifier que le
  `SendTenantJob` voie-document exclut déjà les docs document-driven-B2C (aiguillage) avant de graver un
  masquage pré-émission.
