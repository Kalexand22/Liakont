# Scénarios de test — module Reconciliation

## Unit (`Liakont.Modules.Reconciliation.Tests.Unit`)

### Moteur de rapprochement (INV-RECONCILIATION-001, 002, 003)
- `ReconciliationEngineTests` — numéro dans le NOM DE FICHIER → auto haute (stratégie 1) ; numéro dans le
  TEXTE → auto haute (stratégie 2) ; deux numéros distincts → ambiguïté, non réconcilié ; date + montant
  TTC candidat unique → proposition moyenne (stratégie 3) ; deux candidats date+montant → ambiguïté ;
  aucun signal → orphelin ; numéro préfixe d'un jeton plus long → pas de match ; montant à point décimal
  invariant matché.
- `DocumentNumberMatcherTests` — jeton délimité trouvé ; préfixe d'un jeton plus long NON trouvé
  (anti faux-positif) ; insensible à la casse ; entrées vides → faux.

### File d'attente (INV-RECONCILIATION-006, 007, 010)
- `ReconciliationQueueEntryTests` — fabriques auto (résolue, haute) / proposition (moyenne, non résolue) /
  orphelin (sans document) ; `ConfirmManually` sur orphelin → ReconciledManual (opérateur, résolution) ;
  `ConfirmManually` sur déjà rapproché → exception ; `ConfirmManually` sans opérateur → exception ;
  `RejectProposal` sur proposition → REDEVIENT orphelin (document/stratégie/confiance effacés, opérateur
  conservé) ; `RejectProposal` sur orphelin ou entrée rapprochée → exception ; sans opérateur → exception.

### Orchestration du service (INV-RECONCILIATION-002, 004, 005, 006, 008, 010)
- `ReconciliationServiceTests` (doubles en mémoire) — confiance haute (nom) → addendum + audit auto +
  entrée auto ; **confiance moyenne → proposition SANS addendum ni audit** (jamais de lien auto) ; aucune
  correspondance → orphelin ; PDF déjà traité → ignoré ; confirmation manuelle → addendum + audit manuel +
  entrée résolue ; documents émis sans PDF excluent les documents rapprochés ; propositions mappées en DTO
  (confiance/stratégie) ; orphelins mappés en DTO (pool PDF id + nom de fichier) ; tenant non résolu → exception.
  Console API04 : `ConfirmProposalAsync` rapproche vers le document PROPOSÉ (audit manuel) / sur orphelin →
  `ConflictException` / entrée inconnue → `NotFoundException` ; `RejectProposalAsync` reclasse en orphelin
  SANS addendum ni audit / sur orphelin → `ConflictException` / inconnue → `NotFoundException` ;
  `OpenQueueEntryPdfAsync` renvoie flux + nom de fichier (entrée inconnue → `null`).

### Endpoints console API04 (`tests/Liakont.Console.Api.Tests.Integration/ReconciliationEndpointsIntegrationTests`)
> Tests d'intégration in-process (harness HTTP API01a + Testcontainers, tenant dédié `tenant-api04`, entrées fraîches par test).
- `GetQueue_Without_Authentication_Returns_401` / `..._Without_Actions_Permission_Returns_403` — file protégée par `liakont.actions`.
- `GetQueue_As_Actions_User_Lists_Proposal_And_Orphan` — propositions + orphelins exposés.
- `GetPdf_As_Actions_User_Streams_The_Pdf` / `GetPdf_Unknown_Entry_Returns_404` — affichage du PDF (content-type `application/pdf`), 404 si inconnu.
- `RejectProposal_As_Actions_User_Reclasses_As_Orphan` — la proposition rejetée sort des propositions et apparaît en orphelins.
- `ConfirmProposal_As_Actions_User_Resolves_The_Entry` / `ConfirmProposal_Unknown_Entry_Returns_404` — confirmation (addendum WORM réel), 404 si inconnu.
- `LinkPdf_As_Actions_User_Links_Orphan_To_Document` — lien manuel d'un orphelin vers un document émis archivé.
- `Reconciliation_Is_Tenant_Scoped` — une entrée d'un tenant n'apparaît jamais dans la file d'un autre (CLAUDE.md n°9).

### Job multi-tenant (INV-RECONCILIATION-008)
- `ReconciliationJobTests` — `ReconciliationTenantJob` résout le service depuis le scope du tenant et lance
  une passe ; `ReconciliationFanOutJobHandler` fait tourner le job pour tous les tenants via le runner.

### Extraction PDF (INV-RECONCILIATION-009, ADR-0010)
- `PdfPigTextExtractorTests` — texte extrait d'un PDF généré (présence du numéro) ; contenu non-PDF → null
  (jamais d'exception) ; contenu vide → null.

## Integration (`Liakont.Modules.Reconciliation.Tests.Integration`, PostgreSQL réel)

- `PostgresReconciliationQueueStoreIntegrationTests` — round-trip de la file d'attente : insertion des
  trois catégories (auto/proposition/orphelin), recherche par PDF du pool, lecture par état, identifiants
  des documents rapprochés (la proposition en attente N'EST PAS comptée), confirmation manuelle d'un
  orphelin (mise à jour → ReconciledManual). (INV-RECONCILIATION-006, 007)
- `ReconciliationFlowIntegrationTests` — flux BOUT-EN-BOUT sur le vrai graphe DI (Documents + Archive +
  Reconciliation) : un PDF du pool nommé d'après le numéro d'un document émis → rapproché automatiquement →
  **addendum d'archive RÉEL** (chaîne WORM, `documents.archive_entries`), **fait d'audit** append-only
  (`DocumentReconciledAuto`), entrée de file `ReconciledAuto`, et exclusion du document de la liste
  « documents émis sans PDF ». (INV-RECONCILIATION-004, 005, 006)

## Integration côté Documents (`Liakont.Modules.Documents.Tests.Integration`)

- `DocumentReconciliationJournalIntegrationTests` — le port `IDocumentReconciliationJournal` inscrit un
  `DocumentReconciledAuto` (système) et un `DocumentReconciledManual` (avec identité opérateur) append-only
  sur un document émis ; un rapprochement sur un document inconnu lève. (INV-RECONCILIATION-005)

> Couverture de l'addendum WORM : prouvée de bout en bout par `ReconciliationFlowIntegrationTests` (chaîne
> réelle) et, au niveau du module Archive, par `PackageThenAddendum_ChainsAndVerifiesIntact` (TRK05).
