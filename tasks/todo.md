# B2C08 — Porter le BV (frais vendeur) comme DONNÉE SOURCE hash-neutre dans le pivot

Segment : ereporting-b2c (feat/ereporting-b2c) · Sub-branch : feat/ereporting-b2c-B2C08

## Objectif
Porter le BV (bordereau vendeur / frais vendeur, extrait par B2C-07 = EncheresV6SellerFee) comme
DONNÉE DE CALCUL de marge dans le pivot, en HASH-NEUTRE (pattern EXT01). Jamais une ligne taxable,
jamais une ventilation TVA, jamais une part `frais_vendeur` (TvaMappingPart figé). Alimente B2C-09b.

## Plan (option B : champ additif hash-neutre au grain lot)
- [ ] `PivotSellerFeeDto` (contract, grain lot : LotReference, NetAmount, SourceRegimeCode?, SourceLineRef?, Description?) — aucun champ de taxe
- [ ] `PivotDocumentDto.SellerFees` (IReadOnlyList<PivotSellerFeeDto>?, param additif en fin, null PRÉSERVÉ — jamais coalescé)
- [ ] `CanonicalJson` : émettre "SellerFees" SEULEMENT si non-null, en queue (après IsB2cReportingDeclaration) ; aucun impact Totals
- [ ] Lecteur production `PivotCanonicalJsonReader` : reconstruit SellerFees (optionnel)
- [ ] Lecteur test `PivotCanonicalReader` : reconstruit SellerFees (optionnel) — round-trip sans perte
- [ ] Tests : absent → hash golden INCHANGÉ ; présent → émis en queue, round-trip, aucune Taxes/Rate/TaxAmount, Totals non gonflés (297 E) ; test de réflexion étendu

## Vérif
- [ ] verify-fast (2 solutions : plateforme .NET 10 + agent net48)
- [ ] run-tests verts (neutralité hash + absence de TVA distincte)
- [ ] codex-review clean
