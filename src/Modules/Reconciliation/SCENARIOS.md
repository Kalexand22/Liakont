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

### File d'attente (INV-RECONCILIATION-006, 007)
- `ReconciliationQueueEntryTests` — fabriques auto (résolue, haute) / proposition (moyenne, non résolue) /
  orphelin (sans document) ; `ConfirmManually` sur orphelin → ReconciledManual (opérateur, résolution) ;
  `ConfirmManually` sur déjà rapproché → exception ; `ConfirmManually` sans opérateur → exception.

### Orchestration du service (INV-RECONCILIATION-002, 004, 005, 008)
- `ReconciliationServiceTests` (doubles en mémoire) — confiance haute (nom) → addendum + audit auto +
  entrée auto ; **confiance moyenne → proposition SANS addendum ni audit** (jamais de lien auto) ; aucune
  correspondance → orphelin ; PDF déjà traité → ignoré ; confirmation manuelle → addendum + audit manuel +
  entrée résolue ; documents émis sans PDF excluent les documents rapprochés ; tenant non résolu → exception.

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
