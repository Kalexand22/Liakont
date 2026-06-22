# B2C-05b — SOURCER la forme canonique du « montant de marge » (cas DGFiP n°33) sur le STANDARD

Segment : ereporting-b2c (feat/ereporting-b2c) · Sub-branch : feat/ereporting-b2c-B2C05b
Blueprint : docs-spec-item (AUCUN code ; livrable = section F03 PROPOSÉE, soumise à GATE_B2C_SHAPE_SOURCING).

## Objectif
Sourcer SUR LE STANDARD (DGFiP / EN 16931 / EXTENDED-CTC-FR, jamais une PA) la FORME CANONIQUE du
« montant de marge » en e-reporting B2C (flux 10.3) : quel champ porte la marge, quelle catégorie,
quel (éventuel) VATEX. Exposer les options et déférer la décision à la gate humaine si le standard
est ambigu (CLAUDE.md n°2).

## Sources primaires trouvées (dans le repo)
- `docs/references/dgfip-v3.2/3- XSD_v3.2/1 - E-reporting/transaction.xsd` — structure officielle flux 10.x.
- Annexe 7 — Règles de gestion V1.9 : **G1.57** (régime de la marge → TT-82/TT-87 = marge ramenée HT
  sous catégorie TMA1) et **G1.68** (liste des catégories de transaction, dont TMA1 = art. 266-1-e/268/297 A CGI).
- Dossier général v3.2 : bloc de données de transaction (10.3) agrégé par jour × devise × taux ;
  tolérance « méthode de calcul simplifiée de la marge » renvoyée à la norme AFNOR.

## Réponse sourcée (canonique)
- Flux 10.3 B2C = bloc **Transactions** agrégé (TG-31/TG-32), PAS le bloc Invoice détaillé.
- Catégorie de transaction **TT-81 = TMA1** (régime de TVA sur la marge) — pas d'UNCL5305 {E}, pas de VATEX.
- **TT-82** (TG-31) = montant total de la marge ramenée HT ; **TT-87** (TG-32) = marge ramenée HT par taux
  **TT-86** ; **TT-88** = TVA sur la marge.

## Points déférés à GATE_B2C_SHAPE_SOURCING (humain)
- « ramenée HT » : conversion TTC→HT de la marge (commission totale §2.4) + taux applicable + méthode
  (réelle vs simplifiée AFNOR, norme payante absente du repo).
- Articulation §2.4 (composition = frais acheteur + frais vendeur, en TTC ?) → marge ramenée HT de G1.57.

## Plan
- [x] Recherche documentaire (sources primaires repo)
- [ ] Rédiger §2.5 PROPOSÉ dans F03 + mettre à jour le pointeur §2.2
- [ ] verify-fast (2 solutions) + codex-review

## Review
(à compléter)
