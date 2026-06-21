# Redline ADR-0004 — périmètre du contrat d'extraction & pivot V1

> Source de vérité du lot **RD4** (segment `adr-redline-0004`, manifest v32). Revue demandée par Karl
> (2026-06-19) : re-passe de l'ADR-0004 et de tout ce qui en découle, pour repérer ce qui a été manqué
> et impacterait le projet plus tard.
>
> Lot DISTINCT des 2 autres redlines parallèles : **RDF** (ADR fondateurs 0001/0002/0003, manifest v31,
> mergé) et **RDL** / segment `adr-redline` (ADR-0005/0006/0007, branche `feat/adr-redline`, non mergée).

## Méthode

1. **Trace** (workflow multi-agents) : chaque décision de l'ADR-0004 (D1, D2, D3 #1-8, D4 familles 1/2/3,
   différés réservés) tracée vers son code/spec réel, avec preuves `file:line`.
2. **Redline** : pour chaque cluster, identification des écarts décision↔code, capacités mortes, slots
   « réservés » manquants, contradictions ADR↔spec↔code, et décisions manquantes (regard neuf + fraîcheur
   réglementaire DGFiP v3.2).
3. **Vérification adversariale** : chaque finding re-vérifié par un agent sceptique indépendant sur le code
   réel (confirmer avec preuve, ou réfuter ; ajuster la sévérité si mal calibrée).

Résultat : **19 findings → 12 confirmés, 6 ajustés, 1 réfuté.**

## Constat transversal

Le **contrat pivot** (modèle de données) honore très bien l'ADR. Mais les **mécanismes d'adaptation**
n'ont pas suivi : (1) les capacités déclarées D2 (`ExtractorCapabilities`) sont **mortes côté plateforme**
(déclarées par l'agent, jamais transmises ni consommées) ; (2) plusieurs **slots « réservés » des différés
n'existent pas**. L'overfit que l'ADR voulait tuer est **déplacé** vers la couche de consommation, pas
supprimé — encore réparable à coût faible car **aucun adaptateur réel n°2 n'existe** (seuls EncheresV6
placeholder + DemoErpA/B). La vérification a confirmé les FAITS mais **dégonflé les fausses urgences** :
aucune donnée fiscale n'est produite fausse aujourd'hui (pas de source NAV/Axelor, pas de charge à TVA, pas
de devise — voir ci-dessous).

## Décision opérateur (Karl, 2026-06-19)

**L'intégralité des cibles produit est en FULL EURO.** On ne bâtit PAS la gestion multi-devises
(BT-111/BT-6/taux de change). Le finding devise (RD4-03, seul « P1 confirmé ») est ramené à un **simple
verrou bloquant non-EUR** + correction de l'ADR (RD408).

## Verdicts (19 findings)

| Finding | Sujet | Verdict | Sév. retenue | Item |
|---|---|---|---|---|
| RD4-01 | `ExtractorCapabilities` mortes côté plateforme (D2 non câblé) | ajusté | P2 | **RD401** |
| RD4-02 | Scission BG-30 non implémentée (moteur bloque le multi-codes) | ajusté | P3 | RD409 (reclasser) |
| RD4-03 | Devise étrangère : pas de BT-111, toute devise ISO acceptée | confirmé→**EUR-only** | P3 | **RD408** |
| RD4-04 | TVA des charges non réconciliée (skip si charge présente) | ajusté | P3 | (tracé F04 §3.3 ; TVA04) |
| RD4-05 | Débours art. 267 : ADR « V1 P1 » vs F15 « non sourcé » | ajusté | P2 | RD409 |
| RD4-06 | Différés « réservés » sans slot nommé (viol Conséquence §5) | ajusté | P3 | **RD406** |
| RD4-07 | Capacité `FlowKind` (POS B2C agrégé) absente (ADR P1) | confirmé | P2 | **RD406** |
| RD4-08 | `HasDetailedLines` morte + pas de lignes synthétiques | confirmé | P2 | RD409 (différé tracé) |
| RD4-09 | Validation ne contrôle pas IsSelfBilled/Invoicer ; Payee inerte | confirmé | P2 | **RD404** |
| RD4-10 | `EmitterIdentitySource.DerivedFromVatNumber` morte + pas de normalisation TVA→SIREN | confirmé | P2 | RD409 (différé tracé) |
| RD4-11 | Gate « document finalisé » inexistant (brouillons transmis) | ajusté | **P1** | **RD402** |
| RD4-12 | `IsMutableAfterIssue` / `NumberUniquenessScope` morts | confirmé | P2 | **RD403** + RD409 |
| RD4-13 | Unité de mesure BT-130 absente, hardcodée C62 | confirmé | P2 | **RD407** |
| RD4-14 | Dates `DateTime` → instabilité du hash (R2) | **réfuté** | — | **écarté** (CanonicalJson tronque à yyyy-MM-dd avant hash) |
| RD4-15 | Marge/Mixte mappable seulement si source déjà 2 lignes | confirmé | P2 | RD409 |
| RD4-16 | Classificateur `SourceDocumentKind` → facture/avoir non bâti | confirmé | P2 | **RD405** |
| RD4-17 | Drift spec↔code : F01-F02 §3.3/R3 + F03 (régime singulier) | confirmé | P3 | RD409 |
| RD4-18 | Adaptateurs démo DemoErpA/B inversés vs panel ADR (float) | confirmé | P3 | RD409 (note) |
| RD4-19 | Veille DGFiP v3.2 : Annexe 7 (règles de gestion) non croisée avec F04 | confirmé | P3 | RD409 |

### Le seul finding réfuté (bon signe de la rigueur de la vérification)

**RD4-14 (dates)** : `CanonicalJsonWriter.WriteDate` tronque toutes les dates à `yyyy-MM-dd` (InvariantCulture)
**avant** le hash (`PayloadHasher` → `CanonicalJson.Serialize`). Un `DateTime` à heure non-minuit produit
donc un hash identique à minuit → l'idempotence R2 n'est pas cassée. Écarté.

## Cartographie findings → items du lot RD4

- **RD401** — Transport + persistance des `ExtractorCapabilities` (fondation D2 ; débloque RD403). ← RD4-01
- **RD402** — Gate « document finalisé » (ne jamais transmettre un brouillon). ← RD4-11 (P1)
- **RD403** — Premiers consommateurs : ExposesPayments→F09, IsMutableAfterIssue→alerte. ← RD4-12 (+ ExposesPayments)
- **RD404** — Cohérence auto-facturation en Validation + sort de Payee. ← RD4-09
  - **Livré** : `PartyRoleConsistencyRule` (Validation) — `IsSelfBilled` ⟺ `Invoicer` présent+identifié (SIREN BT-30)
    BLOQUANT ; INV-VALIDATION-024. **Décision `Payee` tranchée = DIFFÉRÉ EXPLICITE** (affacturage BG-10 non projeté
    par les sérialiseurs PA en V1, non sourcé — F15 §6.5/§6) : sa présence est SIGNALÉE (`PAYEE_NOT_TRANSMITTED`,
    Warning), jamais inventée. **À acter dans l'addendum RD409** (ci-dessous).
- **RD405** — Classificateur `SourceDocumentKind`→type (spec d'abord, table tenant). ← RD4-16
- **RD406** — Slots réservés des différés (FlowKind, InvoicePeriod, B2G…) ou amender §5. ← RD4-06, RD4-07
- **RD407** — Unité de mesure BT-130 (UnitCode additif) ou C62 documenté. ← RD4-13
- **RD408** — Verrou EUR-only + correction ADR (PAS de gestion multi-devises). ← RD4-03 (décision Karl)
- **RD409** — Addendum ADR-0004-bis + alignement specs + différés tracés. ← RD4-02, RD4-05, RD4-08, RD4-10, RD4-15, RD4-17, RD4-18, RD4-19
  - **+ différé `Payee` (affacturage BG-10)** tranché par RD404 : champ contractuel + hashé mais INERTE (aucun
    sérialiseur PA ne le projette en V1) ; signalé à l'opérateur (`PAYEE_NOT_TRANSMITTED`, Warning), projection
    affacturage 393/396 non sourcée → à graver « différé » dans l'addendum ADR-0004-bis.

Détail des items (description + acceptance) : `orchestration/items/RD4.yaml`.

## Ce qui est SOLIDE (vérifié, à ne pas re-toucher)

Alignement EN 16931 ; catégories AE/marge/franchise + VATEX + règles bloquantes ; arrondi half-up sans
tolérance ; avoir orphelin → blocage ; conversion float→decimal testée (SourceAmounts) ; `SourceTotalGross`
optionnel toléré ; auto-facturation câblée bout-en-bout (gate 389) ; streaming R8 + `OdbcCellReader` ;
hash canonique stable (dates tronquées) ; écart d'arrondi BT-114 = dérive assumée et sourcée (EN 16931).
