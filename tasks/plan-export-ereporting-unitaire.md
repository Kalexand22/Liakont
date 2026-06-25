# Plan — e-reporting B2C EXPORT unitaire (BUG-11, suite du checkpoint `2dca6291`)

> **Sourçage + décisions FAITS** (F03 §2.8 + conversation) : export = **e-reporting B2C UNITAIRE**
> (une transaction par opération, PAS agrégé comme le domestique), TT-81 = **TLB1** au **taux 0**,
> base HT (adjudication + commission acheteur), réutilise `B2cReportingEmitter` (anti-doublon par doc).
> Décision Karl : **Option B** (unitaire « simple » dans le 10.3). « Quitte à refaire ».
> Limite PA connue : le fil SuperPDP n'a NI mention (262 I) NI pays → export = TLB1/taux 0 au bord PA.

## DÉJÀ FAIT (committé `2dca6291`)
- F03 §2.8 = cartographie générique régime fiscal → e-reporting TT-81.
- Agent EncheresV6 : transport `code_export` + `mode_livraison` → clé de régime COMPOSITE
  `{regime}_EXP_{zone}` (`5_EXP_HORSUE`/`CEE`/`FR`), capability `RegimeKeyShape.Composite` + tests.
- **État courant cohérent** : un export émet `5_EXP_HORSUE` **non mappé** → CHECK bloque
  « régime non mappé » = fail-closed (plus de faux message « marge »).

## RESTE — le lot plateforme (~13 fichiers, échelle BUG-8). ⚠️ Une modif sensible (★).
1. **Mapping démo** (`deployments/encheres-demo/tenant-seed/{volontaire,judiciaire}/mapping-tva.json`) :
   règle `5_EXP_HORSUE → category G, rate 0, vatex VATEX-EU-G` (262 I). CEE/FR **non mappés** = fail-closed.
2. **`B2cExportMarking.cs`** (NEW, pur) : `IsExportDeclaration` = toutes lignes catégorie `G` + `TotalTax==0`
   + B2C (`B2cBuyerClassification.IsNonProfessional`) + `hasFees`. Miroir de `B2cTaxableMarking`.
3. **`B2cExportDeclaration.cs`** (NEW, pur) : `Matches` = `B2cAggregatedDeclaration.Matches` + `TotalTax==0`
   + `B2cExportMarking.IsExportDeclaration`.
4. **`CheckTvaMapping.Evaluate`** (edit) : marquer `IsB2cReportingDeclaration` si marge **OU** taxable **OU**
   `B2cExportMarking.IsExportDeclaration` (ligne ~133-137).
5. **`B2cMarginMarking.LooksLikeUnclassifiedMargin`** (edit) : ajouter `&& !B2cExportMarking.IsExportDeclaration`
   → un export reconnu n'est plus happé par la garde marge (sinon faux « marge »).
6. **★ `B2cMarginDeclaration.Matches`** (edit, RÉGRESSION-sensible) : ajouter
   `&& !B2cExportMarking.IsExportDeclaration(pivot)` → le job marge ne grab plus un export (même `TotalTax==0`).
   Le job taxable (`TotalTax>0`) exclut déjà l'export. **GATE = run-tests (intégration marge/taxable).**
7. **`B2cExportReportingTenantJob.cs`** (NEW) : jumeau UNITAIRE de `B2cTaxableAggregatorTenantJob` —
   découvre les docs `B2cExportDeclaration`, construit **UNE** `B2cAggregatedTransaction` PAR doc
   (Date=IssueDate, base HT = Σ lignes `NetAmount` + Σ `BuyerFees.NetAmount`, un sous-total taux 0,
   TVA 0, une contribution), puis `B2cReportingEmitter.EmitAllAsync(..., Tlb1, Seller)`. PAS d'agrégation.
8. **Câblage** : `AggregateB2cExportAllTrigger` (Contracts/Jobs) + `AggregateB2cExportAllFanOutHandler` +
   `PipelineRunType.B2cExportReporting` + `PipelineSystemJobHandlers.AddJobHandler<...>` +
   `SystemJobDefinitions` (entrée DeploymentCadence, cron null).
9. **Tests** : `B2cExportMarkingTests` (G/0%/B2C/fees → export ; ligne S/E mêlée → non) +
   intégration `B2cExportReportingTenantJob` (un export hors UE → 1 transaction TLB1 taux 0, base HT) +
   non-régression marge/taxable (run-tests).
10. **Vérif** : verify-fast (Debug+Release) + **run-tests** (gate régression ★) + codex-review (`-Engine claude`)
    + commit (« feat(ereporting-b2c): emission unitaire export TLB1 taux 0 (BUG-11) »).

## Pourquoi l'arrêt au checkpoint
- (6) touche la partition du flux marge/taxable **qui marche** ; à faire sous supervision + gate run-tests.
- Lot interconnecté de 13 fichiers à profondeur de session extrême = risque d'erreur cumulée.
- Le checkpoint est cohérent (export fail-closed, pas de faux « marge ») → rien de cassé.
