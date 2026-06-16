# SIG05 — Refactor SelfBilledAcceptance → projection via DocumentApproval (option A)

## Objectif
Faire de SelfBilledAcceptance (MND02) une PROJECTION restreinte du module DocumentApproval
(purpose = SelfBilledAcceptance). Comportement fiscal observable INCHANGÉ. Journal =
document_approval_log. Gate délègue à DocumentApproval.Contracts. Migration sans perte.

## Faits clés (analyse)
- DocumentApproval a déjà le purpose `SelfBilledAcceptance` (sous-graphe 4 états :
  PendingValidation / Validated / TacitlyValidated / Contested) — machine self-billing identique.
- Le WRITE side du self-billing (création, allocation BT-1, accept express, contestation) est
  TEST-ONLY aujourd'hui ; seuls le gate (read, pipeline) et le job tacite (MND04) sont câblés prod.
- `allocated_number` (BT-1, MND05) est dénormalisé sur `self_billed_acceptances` et lu par le
  pipeline (SendTenantJob). L'allocateur n'est PAS appelé en prod aujourd'hui (test-only).
- Boundary test Mandats interdit toute réf à un autre module → à élargir pour DocumentApproval.Contracts.
- DbUp ordonne les scripts par ordre de PROVIDER (registration), puis par nom dans un provider.
  AppBootstrap enregistre Mandats(191) AVANT DocumentApproval(326) → une migration Mandats qui écrit
  dans documentapproval échoue. => REORDONNER : DocumentApproval avant Mandats.

## Plan
1. AppBootstrap : deplacer AddDocumentApprovalModule() AVANT AddMandatsModule() (ordre de migration).
2. DocumentApproval.Contracts (generique, SANS SignatureLevel — module-free) :
   - IDocumentApprovalWorkflow : RequestValidationAsync (genese Pending), RecordRecordedValidationAsync
     (-> Validated, preuve Recorded, express=true), ContestAsync (-> Contested),
     RecordTacitValidationIfDueAsync (verrou + re-check Pending & deadline<=now -> TacitlyValidated, bool).
   - IDocumentApprovalQueries.ListTacitDueDocumentsAsync(purpose, nowUtc) -> liste (companyId, documentId)
     des candidats a bascule tacite (Pending, deadline non null, deadline<=now), base du tenant.
3. DocumentApproval.Infrastructure : DocumentApprovalWorkflow (sur IDocumentValidationUnitOfWorkFactory +
   DocumentApprovalLogFactory) + SQL tacit-due + enregistrement DI.
4. Mandats :
   - self_billed_acceptances devient COMPANION fiscal : garde (company_id, document_id, allocated_number,
     pending_since, created_at) ; DROP state, deadline_utc ; allocateur MND05 INCHANGE.
   - ISelfBilledAcceptanceCommands (Contracts) + impl (Infra) : OpenPending (DA RequestValidation +
     INSERT companion), AcceptExpressly (DA RecordRecorded), Contest (DA Contest). Remplace l'UoW supprime.
   - SelfBilledGate INCHANGE (lit ISelfBilledAcceptanceQueries -> IsAccepted).
   - PostgresSelfBilledAcceptanceQueries : compose DA (etat/deadline via GetLatestAttempt) + companion
     (allocated_number/pending_since) -> SelfBilledAcceptanceDto ; GetAcceptanceLog via DA GetApprovalLog
     (mappe noms ValidationState -> SelfBilledAcceptanceState).
   - TacitAcceptanceService : via IDocumentApprovalQueries.ListTacitDue + IDocumentApprovalWorkflow.
   - SUPPRIMER : aggregate SelfBilledAcceptance, ISelfBilledAcceptanceUnitOfWork(+impl/factory),
     SelfBilledAcceptanceLogEntry, SelfBilledAcceptanceLogFactory, SelfBilledAcceptanceMaterializer,
     ITacitAcceptanceCandidateReader(+impl). GARDER l'enum SelfBilledAcceptanceState (mapping/tests).
   - Migration V010 (bascule) : copie state->document_validations + log->document_approval_log
     (mapping etats), DROP self_billed_acceptance_log, DROP V007 index, ALTER self_billed_acceptances
     DROP state/deadline_utc. Historique preserve (relocalisation, pas purge).
   - MandatsModuleRegistration : retirer l'UoW d'acceptation/candidate reader ; ajouter Commands ;
     ref DA.Contracts.
   - csproj Mandats.Infrastructure : + ProjectReference DocumentApproval.Contracts.
   - MandatsBoundaryTests : autoriser DocumentApproval.Contracts (assembly + csproj allowlist).
5. Tests : MandatsHarness + fixtures (appliquer migrations DA puis Mandats), reecrire
   SelfBilledAcceptanceIntegrationTests / TacitAcceptance* / SelfBilledGateTests /
   SelfBilledAcceptanceTests (cartesien INV-ACCEPT-4 sur le purpose) / MandatsModuleRegistrationTests.
   Mandats.Tests.Integration.csproj : + ref DocumentApproval.Infrastructure.

## Mapping etats (migration + DTO)
PendingAcceptance(0)->PendingValidation(0) ; Accepted(1)->Validated(2) ; TacitlyAccepted(2)->TacitlyValidated(3) ;
Contested(3)->Contested(5). proof_level : Accepted/TacitlyAccepted->Recorded(1), sinon None(0).
express_acceptance_recorded : Accepted->true, sinon false. purpose=0, attempt=1.

## Verification
- verify-fast (2 solutions) ; run-tests (integration >= 2 bases : machine + journal append-only + tenant
  scoping + non-regression MND + pipeline) ; codex-review jusqu'au clean.

## Review (a completer)
