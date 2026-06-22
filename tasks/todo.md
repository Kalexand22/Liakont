# B2C09b — CALCUL de la marge par lot (decimal half-up, aucune TVA distincte 297 E) — sans transmission

Segment : ereporting-b2c (feat/ereporting-b2c) · Sub-branch : feat/ereporting-b2c-B2C09b

## Objectif
Calculer le montant de marge PAR LOT (no_ba) à partir des frais acheteur (B2C-08c) et frais
vendeur (B2C-08) déjà portés en pivot, en `decimal` half-up via `PivotRounding.RoundAmount`, et
produire le montant calculé pour B2C-09c (TRANSMISSION, hors scope ici).

## Sources fiscales (validées GATE_B2C_SOURCING `done`)
- F03 §2.4 : `marge = Σ frais acheteur + Σ frais vendeur`, PAR LOT, méthode PAR OPÉRATION unique
  (pas d'enum méthode — globalisation non ancrée pour l'OVV). 3e terme « impôts/droits/taxes » HORS marge.
- F03 §2.3 / §2.4 (art. 297 E) : le montant de marge est une BASE — aucune TVA distincte.

## Plan (cœur PUR dans Pipeline/Domain — pattern PaymentAggregationCalculator)
- [ ] `Domain/Margin/LotMargin.cs` — record par lot : LotReference, BuyerFeesTotal, SellerFeesTotal, MarginAmount (decimal half-up)
- [ ] `Domain/Margin/MarginCalculationResult.cs` — record : Lots (ordre déterministe), TotalMargin
- [ ] `Domain/Margin/MarginVatNotSeparableException.cs` — exception bloquante 297 E (message FR + numéro de document)
- [ ] `Domain/Margin/MarginCalculator.cs` — static pur :
      - GARDE 297 E EN PREMIER : `Totals.TotalTax != 0` OU une ligne porte une TVA distincte
        (`Taxes` avec `TaxAmount > 0`) → throw `MarginVatNotSeparableException` (jamais calculer faux)
      - regroupe SellerFees + BuyerFees par LotReference (ordinal, ordre de 1re apparition = déterministe)
      - somme en `decimal`, `PivotRounding.RoundAmount` sur chaque total + la marge par lot
      - TotalMargin = RoundAmount(Σ marges des lots)
      - document sans aucun frais → résultat vide (Lots = [], TotalMargin = 0)

## Tests (Tests.Unit/Margin/ — exécutés par run-tests)
- [ ] marge = Σ acheteur + Σ vendeur par lot, ≥ 2 lots (bases distinctes), ordre déterministe
- [ ] test d'arrondi half-up obligatoire (n°1 ; ex. 0.005 → 0.01)
- [ ] CRITÈRE BLOQUANT 297 E : `TotalTax > 0` → throw ; ligne avec `TaxAmount > 0` → throw
- [ ] cas sain : adjudication E/0% (TaxAmount=0) + frais → calcul OK (pas de throw)
- [ ] frais vendeur seul / frais acheteur seul / aucun frais (résultat vide)

## Vérif
- [ ] verify-fast (2 solutions : plateforme .NET 10 + agent net48)
- [ ] run-tests verts (calcul decimal + absence de TVA distincte, ≥ 2 bases)
- [ ] codex-review clean

## Review
(à compléter)
