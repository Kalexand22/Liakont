# WEB06 — Page Encaissements (e-reporting paiement)

Segment console-web, sous-branche `feat/console-web-WEB06`, blueprint `blazor-page-item`.
Page Blazor **lecture seule** (F10 §2.4), alimentée par GET /payments (API01b, done).

## Données disponibles (vérifiées sur pièce)
- `IPaymentAggregationQueries.GetAggregationsAsync(period?)` → `PaymentDailyAggregateDto[]`
  (Pipeline.Contracts, tenant-scopé, registré Scoped). Montants `decimal`. Champ `Status` ∈
  {Calculated, Suspended, NotRequired, PendingCapability} calculé par PIP03a — SEULEMENT affiché.
- `ITenantSettingsConsoleQueries.GetSettingsOverview()` → PaAccounts[] (capacité
  `SupportsDomesticPaymentReporting` + nom PA) + FiscalSettings (VatOnDebits/OperationCategory nullable).
- L'« état de transmission » réel (transmis/rejeté) relève de PIP03b (GELÉ) → HORS périmètre.
  La page affiche la qualification `Status` (read-model PIP03a), pas un état PIP03b inexistant.

## Les 3 états d'affichage (F10 §2.4) — sans inventer de règle
1. PA supporte les paiements + fiscal renseigné → agrégats avec badge `Status`, aucun bandeau.
2. PA sans capacité → bandeau « la plateforme <nom> ne supporte pas encore… » (driven par capacité PA).
3. Fiscal en attente → bandeau « Décision fiscale en attente (TVA débits / catégorie) » (driven par
   nullabilité FiscalSettings — même contrôle de complétude que WEB04b, AUCUNE règle fiscale).
La liste s'affiche toujours ; les bandeaux peuvent coexister au-dessus.

## Plan
- [ ] `Payments/PaymentAggregateStatusDisplay.cs` — libellé FR + Severity par statut (pur affichage)
- [ ] `Payments/PaymentAggregateRow.cs` — projection présentation (props non-nullables, Reason="—")
- [ ] `Payments/PaymentAggregateColumnRegistry.cs` — colonnes Jour/Taux/Base HT/TVA/État/Motif
- [ ] `Payments/EncaissementsViewModel.cs` — agrégats + flags bandeaux (interne)
- [ ] `Payments/IEncaissementsConsoleQueries.cs` + `EncaissementsConsoleQueryService.cs` — composition
      (IPaymentAggregationQueries + ITenantSettingsConsoleQueries), tenant-scopée, sans logique métier
- [ ] `Components/Pages/Encaissements.razor` (+ `.razor.css`) — route /encaissements, [Authorize],
      filtre période (mois), 2 bandeaux, DeclaredListPage + ColumnRegistry (AUCUNE grille maison)
- [ ] Enregistrement DI dans `Startup/AppBootstrap.cs`
- [ ] bUnit : 3 états + vide + erreur + badges + formatage FR decimal/taux ; registry
- [ ] E2E Playwright : login lecture → nav Encaissements → page affichée (tenant vierge → état vide)
- [ ] verify-fast + run-tests + run-e2e verts
- [ ] codex-review propre (ou P2 acceptés documentés)

## Décisions assumées (à consigner)
- Read-model = PaymentDailyAggregateDto (PIP03a) ; PIP03b gelé → pas d'état transmission réel (hors scope).
- Bandeau fiscal = contrôle de complétude FiscalSettings (VatOnDebits/OperationCategory), pas une règle.
- Filtre période = sélecteur de MOIS (« yyyy-MM », ce que prend l'endpoint), défaut mois courant.
