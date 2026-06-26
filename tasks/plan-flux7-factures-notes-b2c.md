# Flux #7 — Factures clients + Notes d'honoraires (chaîne complète B2C plate)

## Contexte (validé Karl 2026-06-26)
Deux types de documents ORDINAIRES (hors spine enchères BA/BV) à ajouter :
- **Factures clients** (`entete_facture_clien`/`ligne_facture_client`) : régime STANDARD S/20 %, prix total taxable, JAMAIS de marge.
- **Notes d'honoraires** (`entete_notes_hono`/`lignes_notes_hono`) : honoraires d'INVENTAIRE = prestation de services autonome (OVV émetteur). Pas de 389, pas d'autofacturation.

Canal par statut du tiers : SIREN → B2B Factur-X (voie document existante) ; particulier → e-reporting B2C 10.3.

## Trou découvert (investigation 2026-06-26)
Une facture B2C PLATE (lignes taxables, **sans frais**) n'est e-reportée par AUCUN des 3 marquages (tous exigent les frais enchères) → elle partirait en Factur-X. Et **TPS1** (services) est défini mais MORT (jamais dérivé). Il faut une **4e voie e-reporting B2C générique**.

## Décisions d'archi (défauts défendables)
- **Catégorie TT-81 par `operationCategory`** : `LivraisonBiens→TLB1`, `PrestationServices→TPS1` (G1.68). `Mixte`/null → **fail-closed tracé** (le pivot ne porte pas le bien/service par ligne → on ne devine pas).
- **Réutiliser** `B2cTaxableAggregationCalculator` (contributions = lignes taxables, honoraire TTC = 0) + `B2cReportingEmitter` + journal partagé + attempt-once (D3).
- **Agrégation par catégorie** : partition des contributions par TT-81 (operationCategory) AVANT agrégation, EmitAllAsync par groupe (comme l'export pour le mixte G/K).
- **Garde D1 SEND généralisée** à `IsB2cReportingDeclaration` (un 10.3 sans frais doit aussi être différé de la voie document).

## Lot PLATEFORME
1. **F03 §2.9** — ancrage cartographie « document B2C ordinaire taxable → TLB1 (bien) / TPS1 (service) », validé PO, sourcé G1.68. Pas de règle inventée.
2. **`B2cPlainTaxableMarking`** (Domain, pur) : B2C non-pro + `TotalTax>0` + toutes lignes S/AA/AAA + **AUCUN frais** (≥1 ligne). Posé au CHECK (`CheckTvaMapping`, 4e OR).
3. **`B2cPlainTaxableDeclaration.Matches`** (Domain) : marqué 10.3 + sans frais (aiguillage job).
4. **Garde D1** : généraliser le différé SEND (`SendTenantJob`) à tout `IsB2cReportingDeclaration` (nouveau `B2cReportingDeclaration.Matches` ou OR du plain).
5. **`B2cPlainTaxableReportingTenantJob`** (Infrastructure) : découverte (sans filtre HasFees) → contributions lignes → partition par catégorie (operationCategory) → `B2cTaxableAggregationCalculator.Aggregate` par groupe → `EmitAllAsync` par TT-81. `ResolveTransactionCategory(operationCategory)` dans le job. Gate `SupportsB2cReporting`. `PipelineRunType.B2cPlainTaxableAggregate=8`.
6. **Câblage** : trigger `AggregateB2cPlainTaxableAllTrigger` + fan-out handler + registration + `SystemJobDefinitions` + DI.
7. **Tests** : marking (bien→marqué, service→marqué, avec frais→non, pro→non, exonéré→non, ligne mixte→non) ; job intégration (TLB1 bien, TPS1 service, mixte 2-POST, Mixte→fail-closed tracé, D1 différé, D3 attempt-once, capability gate) ; D1 SEND (plain différé).

## Lot AGENT (EncheresV6)
8. **Schéma** (`EncheresV6Schema.cs`) : tables + colonnes des 4 tables, 2 requêtes SELECT (auto-jointure lettrage), `ExpectedTables` étendu.
9. **Modèles source** : entête+ligne facture client, entête+ligne note hono (miroir `EncheresV6Bordereau`/`Ligne`).
10. **Extracteur** (`PervasiveExtractor`) : 2 `ExtractXxxDocuments` + Read/Map ; **FixtureExtractor** parité ; snapshot 2 listes.
11. **RowMapper** : `MapFactureClientDocument` + `MapNoteHonoDocument` (lignes plates HT/TVA/TTC, SANS BuyerFees/SellerFees), `operationCategory` = LivraisonBiens (facture) / PrestationServices (note), avoir via lettrage, préfixes `encheresv6:fc:`/`encheresv6:nh:`.
12. **Points data à confirmer en base** (défauts défendables) : HT ligne facture (`qte×prix_unitaire_ht`) ; `type_ligne` à émettre (1 facturé / ignorer récap+règlement) ; `code_tva` brut en `sourceRegimeCodes` ; typage `smallint` du dossier ; avoir facture absent du jeu démo.
13. **Tests agent** : RowMapper (facture standard, note hono service, avoir, conversion) + extracteur (streaming, filtre dossier) + fixtures.

## Lot DÉMO/SEED
14. Seed mapping pour `code_tva` standard → S/20 % (Part.Autre) si requis ; (optionnel) SIREN démo sur factures/notes pour démontrer l'aiguillage B2B.

## Vérification
verify-fast (manifest bypass : build direct) + Release (StyleCop) + run-tests intégration + codex-review (engine claude / subagent) + commit+push `feat/ereporting-b2c`.
