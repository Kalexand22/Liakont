# SIG04 — Module `Liakont.Modules.DocumentApproval` (ADR-0028, F17 §3/§4)

Cœur réutilisable du lot SIG. Module générique de workflow de validation de document.
SIG04 NE touche PAS Mandats (refactor = SIG05) ni les ports par purpose (= SIG06) ni les
jobs TenantJobRunner/WORM (= SIG06/SIG07). Périmètre = le module générique + sa persistance + ses tests.

## Plan

### Squelette du module (pattern Stratum, calqué Mandats/Signature)
- [ ] src/Modules/DocumentApproval/{Contracts,Domain,Application,Infrastructure,Web,Tests.Unit,Tests.Integration}
- [ ] MODULE.md + INVARIANTS.md (INV-APPROVAL-1..8)
- [ ] Inscription des 8 projets dans src/Liakont.sln + AddDocumentApprovalModule() au Host (AppBootstrap)

### Domain
- [ ] `ValidationState` enum (7 états distincts, persisté int figé) — Rejected≠Contested
- [ ] `ApprovalSlotState` enum (Pending/Approved/Rejected)
- [ ] `ApprovalSlot` (SignerId, State, ProofLevel, ProofId?) idempotent par SignerId
- [ ] `ValidationMachine` : graphe d'arêtes universel §3 + garde de transition
- [ ] `ValidationPurposePolicy` : sous-graphe autorisé PAR purpose (self-billing = 4 états), AllowsRetry, RequiresExpressAcceptanceForm
- [ ] `DocumentValidation` agrégat clé (company,document,purpose,attempt) + transitions + slots
- [ ] `SignatureLevelAssurance` : rang d'assurance (Recorded<SES<AES<QES) + Satisfies (≥)
- [ ] `ApprovalGate` : règle de gate §5 (3 conditions, par slot, cond.3 hors tacite)

### Contracts (surface publique minimale)
- [ ] `ValidationPurpose` enum (6 purposes, clé de couplage — public)
- [ ] `DocumentValidationDto`, `DocumentApprovalLogEntryDto`, `ApprovalSlotDto`
- [ ] `IDocumentApprovalQueries` (lecture tenant-scopée : dernière tentative + journal)

### Application
- [ ] `IDocumentValidationUnitOfWork` (+ factory) : Insert/GetForUpdate/SaveTransition/CreateNextAttempt — transition + journal MÊME transaction
- [ ] `DocumentApprovalLogEntry` (entrée de journal)

### Infrastructure
- [ ] Migrations V001 schéma `documentapproval`, V002 document_validations (+ index unique partiel non-terminaux), V003 document_validation_slots, V004 document_approval_log (+ DOUBLE trigger append-only)
- [ ] `PostgresDocumentValidationUnitOfWork` (+ factory), `DocumentApprovalLogFactory`, `DocumentValidationMaterializer`, `RowReader`
- [ ] `PostgresDocumentApprovalQueries`
- [ ] `DocumentApprovalModuleRegistration` (AddDocumentApprovalModule : migrations DbUp + UoW + queries)

### Tests.Unit
- [ ] Machine fermée : produit cartésien (toute transition hors graphe rejetée), aucun retour terminal
- [ ] Garde de purpose : self-billing 4 états (ValidationInProgress/Expired/Rejected rejetés)
- [ ] Slots : idempotence SignerId, complétude = tous distincts, slot refusé → terminal immédiat, niveau par slot
- [ ] Règle de gate : Recorded nu ne franchit pas AES/QES ; tacit ne satisfait que Recorded ; forme EC hors tacit
- [ ] Frontière NetArchTest : Signature.Contracts AUTORISÉ, Signature.Domain/.PlugIns + autres modules INTERDITS ; scan csproj

### Tests.Integration (Testcontainers, ≥2 bases)
- [ ] Round-trip état + journal de genèse ; transition + journal MÊME transaction
- [ ] Journal append-only : UPDATE/DELETE/TRUNCATE rejetés
- [ ] Atomicité : transaction abandonnée ⇒ rien
- [ ] Tenant-scoping ≥2 bases
- [ ] Ré-essai : index unique partiel (1 seule non-terminale) + garde anti-race (test de concurrence) + self-billing exclu
- [ ] Slots N-parties persistés + complétude + slot refusé

### Vérification
- [ ] verify-fast (net10 + agent net48)
- [ ] run-tests (intégration)
- [ ] codex-review boucle propre

## Notes / décisions
- CreditNoteAcceptance : défaut défendable #9 (ADR-0028) = même discipline que 389 (4 états, pas de ré-essai).
- Gate rule = mécanisme Domain pur (pas de port par purpose ici — SIG06). Niveau requis tenant = paramètre (câblé SIG06).
- DocumentApproval → Signature.Contracts (SignatureLevel) autorisé (ADR §9) ; jamais Signature.Domain/.PlugIns.
