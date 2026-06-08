# API02b — Endpoints d'action documents : garde-fou (verdict) + re-vérification (recheck)

Segment : console-web (`feat/console-web`) · sous-branche `feat/console-web-API02b` · slot-1
Session : orch-20260608-112518-Liakont3-s1

## Objet (spec API.yaml API02b, F08 §A.4, VAL05 #3, WEB03b)
Deux endpoints d'action sur Documents.Web (permission `liakont.actions`), réutilisant le harness
API01a + le scaffold d'actions API02a :
1. `POST /api/v1/documents/{id}/verdict` — verdict garde-fou B2C/B2B (VAL05) :
   - `confirm_b2c` (« Confirmer particulier ») → enregistre la confirmation B2C (persistée + journalisée),
     ne change PAS l'état (reste Blocked) ; le déblocage effectif se fait au recheck (« verdict posé → recheck »).
   - `handle_manually` (« Traiter manuellement B2B ») → Blocked→ManuallyHandled (terminal), journalisé.
2. `POST /api/v1/documents/{id}/recheck` — re-valide UN document Blocked via le CHECK complet
   (mapping TVA → garde-fou prod → validation) et le fait passer Blocked→ReadyToSend s'il passe,
   sinon renvoie les nouveaux motifs (reste Blocked, pas de ré-écriture d'événement — Blocked→Blocked interdit).

## Décisions de conception (sourcées, aucune règle fiscale inventée)
- F08 §A.4 : « confirmer B2C → débloque en B2C, décision journalisée » = override OPÉRATEUR
  SANCTIONNÉ et JOURNALISÉ. Implémenté en INCORPORANT la décision opérateur dans l'ENTRÉE de validation
  (`DocumentValidationContext.BuyerConfirmedAsIndividual`), de sorte que `BuyerLooksProfessionalRule`
  (détection-seule) ne PRODUIT plus l'anomalie pour CE document. Aucune anomalie Blocking n'est « retirée ».
- Recheck = source UNIQUE de la décision fiscale : réutilise `DocumentCheckEvaluator` (jamais de 2e impl).
  Nouveau `IDocumentRecheckService` (Pipeline.Contracts) mirroir `SendTenantJob.ReconcileCreditNotesAsync`
  pour UN document, en scope requête (tenant déjà résolu, slug via ITenantContext).
- La garde-fou PRODUCTION (table TVA non validée) n'est PAS overridable par le verdict B2C (block distinct).
- Recheck → ReadyToSend écrit aussi le snapshot de ventilation (ADR-0015) AVANT la transition.

## Plan d'implémentation
### Validation (additif)
- [ ] `Contracts/DocumentValidationContext.cs` : param optionnel `bool buyerConfirmedAsIndividual = false` + propriété.
- [ ] `Domain/Rules/BuyerLooksProfessionalRule.cs` : si `context.BuyerConfirmedAsIndividual` → aucune anomalie (doc F08 §A.4).
- [ ] Test unitaire : règle avec flag → pas d'anomalie.

### Documents (domaine + persistance)
- [ ] `Domain/Entities/Document.cs` : propriété `BuyerConfirmedAsIndividual` ; `Reconstitute` param optionnel ;
      méthode `ConfirmBuyerAsIndividual(operatorIdentity, at)` → DocumentEvent (garde State==Blocked).
- [ ] `Domain/Entities/DocumentEventType.cs` : `DocumentBuyerConfirmedB2C`.
- [ ] `Infrastructure/Migrations/V008__add_buyer_confirmed_as_individual.sql`.
- [ ] `Infrastructure/PostgresDocumentUnitOfWork.cs` : colonne dans INSERT/UPSERT/SELECT/MapDocument.
- [ ] `Infrastructure/Queries/PostgresDocumentQueries.cs` : colonne dans les SELECT mappés vers DocumentDto.
- [ ] `Contracts/DTOs/DocumentDto.cs` : `bool BuyerConfirmedAsIndividual` (non-required, défaut false).
- [ ] `Contracts/Lifecycle/IDocumentLifecycle.cs` + `Infrastructure/Lifecycle/DocumentLifecycle.cs` :
      `ConfirmBuyerAsIndividualAsync` + `MarkManuallyHandledAsync(reason, operatorIdentity)`.
- [ ] Tests unitaires Document/Lifecycle.

### Pipeline (recheck — source unique de décision)
- [ ] `Contracts/IDocumentRecheckService.cs` + `Contracts/RecheckResult.cs`.
- [ ] `Infrastructure/Check/DocumentCheckEvaluator.cs` : param optionnel `bool buyerConfirmedB2C = false`.
- [ ] `Infrastructure/Check/DocumentRecheckService.cs`.
- [ ] `Infrastructure/PipelineModuleRegistration.cs` : AddScoped.

### Documents.Web (endpoints)
- [ ] `Web/DocumentActionsEndpointMapping.cs` : `/verdict` + `/recheck` (liakont.actions), DTOs, 404/409/400, audit + DocumentEvent.

### Tests d'intégration (ConsoleApiFactory : ajouter staging root + helper StagePayload)
- [ ] verdict confirm_b2c → 200, reste Blocked, event + audit.
- [ ] verdict handle_manually → 200, ManuallyHandled + audit.
- [ ] verdict lecture seule → 403 ; non-Blocked → 409 ; autre tenant → 404.
- [ ] recheck (pivot stagé pro + TVA validée + confirm_b2c) → 200 ReadyToSend.
- [ ] recheck encore bloqué → 200 Blocked + motifs.
- [ ] recheck lecture seule → 403 ; non-Blocked → 409 ; autre tenant → 404.

## Vérification
- [x] verify-fast (plateforme .NET10 + agent net48) — PASS
- [x] run-tests (unit + intégration) — PASS (4038 tests, 0 échec)
- [x] codex-review -Base feat/console-web — boucle jusqu'à clean

## Review
- **Round 1** : 0 P1, 2 P2.
  - **P2 #1 (trou de test — chemin `ContentUnavailable` du recheck non couvert)** : CORRIGÉ — ajout du
    seeder `SeedBlockedDocumentWithoutStagedPivotAsync` (document bloqué sans pivot stagé) + test
    `PostRecheck_With_Unavailable_Staged_Pivot_Returns_409` (assure 409 + document toujours Blocked).
  - **P2 #2 (course TOCTOU : changement d'état concurrent entre la pré-vérification `GetByIdAsync` et la
    mutation sous verrou → `InvalidOperationException`/`InvalidDocumentTransitionException` non capturée →
    500 au lieu d'un 409)** : ACCEPTÉ avec justification. La course exige deux actions opérateur concurrentes
    sur le MÊME document bloqué ; la machine à états garantit l'atomicité (AUCUNE corruption — l'action perdante
    échoue proprement) ; c'est le patron déjà en place dans l'API02a et le CHECK (pré-vérification puis action).
    Le mapper en 409 exigerait soit un `catch (InvalidOperationException)` large (risque de masquer un vrai bug),
    soit une nouvelle surface d'exception dédiée — disproportionné pour un edge rare et bénin (CLAUDE.md :
    « ne pas sur-concevoir »). Note reviewer sur l'exécution de la suite : verify-fast + run-tests ONT été
    exécutés (PASS) — la review statique ne pouvait pas le voir.
