# F3 — Moteur de mapping TVA
### Document de conception — Gateway.Core/TvaMapping

> Statut : 🟨 issu de la deep research DR2 (2026-06-02) + `Analyse-Donnees-V1-Mapping-TVA.md` + validation staging. À revoir ensemble.
> ⚠️ **Particularité DR2** : la vérification adversariale a « abstenu » (0-0) sur la plupart des affirmations — non parce qu'elles sont fausses, mais parce que les vérificateurs n'ont pas pu accéder aux pages `docs.peppol.eu`/genericode pour les confirmer. Les faits de code list ci-dessous proviennent de ces sources Peppol/EC officielles et sont **cohérents avec ce qui a été validé en staging B2Brouter** ; ils restent à re-confirmer formellement sur le code list officiel avant figeage.
> Légende : ✅ validé staging · 🔶 source Peppol/EC officielle (non re-vérifiée adversarialement) · ❓ décision

---

## 1. Le problème central

Le moteur de mapping TVA est **le cœur de valeur fiscale du produit** (et sa zone de risque n°1). Il transforme un **code régime TVA du système source** (propre à chaque logiciel) en un triplet normalisé **{catégorie EN 16931, taux, code VATEX}**.

L'enjeu : une erreur de mapping = un motif d'exonération erroné transmis à la DGFiP = la responsabilité fiscale du client engagée. D'où trois exigences : **paramétrable** (par déploiement), **auditable** (traçabilité de la règle appliquée), **sûr par défaut** (un régime inconnu bloque, ne devine pas).

## 2. Référentiel cible (🔶 Peppol BIS 3.0 / EC, cohérent staging)

### 2.1 Catégories de TVA (UNCL5305) acceptées par B2Brouter
`S` (taux normal), `AA` (taux réduit), `AAA` (super réduit), `Z` (taux zéro assujetti), `E` (exonéré), `AE` (autoliquidation), `G` (export hors UE), `K` (livraison intra-UE), `O` (hors champ). (`L`/`M` Canaries/Ceuta-Melilla hors périmètre FR.)

> ⚠️ Nuance à re-vérifier : `AA`/`AAA` ne sont pas dans toutes les listes EN 16931 strictes — mais B2Brouter les accepte avec ses taux préchargés FR (20→S, 10/5,5→AA, 2,1→AAA). À confirmer sur le profil EXTENDED-CTC-FR.

### 2.2 Codes VATEX clés (motif d'exonération obligatoire si catégorie E)
| Code | Usage | Catégorie |
|---|---|---|
| **VATEX-EU-F** | biens d'occasion (régime de la marge) | E |
| **VATEX-EU-I** | œuvres d'art (régime de la marge) | E |
| **VATEX-EU-J** | objets de collection et d'antiquité (marge) | E ✅ validé staging |
| VATEX-EU-AE | autoliquidation | AE |
| VATEX-EU-IC | livraison intra-UE | K |
| VATEX-EU-G | export hors UE | G |
| VATEX-EU-O | non soumis à la TVA | O |
| **VATEX-FR-FRANCHISE** | franchise en base (art. 293 B) | E → transcodé Z par B2Brouter |
| VATEX-FR-AE | autoliquidation art. 283-2 CGI (domestique) | AE |
| VATEX-FR-CNWVAT | avoirs sans TVA | — |
| VATEX-FR-298SEXDECIESA | agences de voyages (art. 298 sexdecies A) | E |

> ⚠️ Point déjà identifié au projet : **il n'existe pas de code VATEX-FR dédié spécifiquement aux biens d'occasion/art/antiquités** — on utilise les **VATEX-EU-F/I/J**. À confirmer auprès de B2Brouter pour le « montant de la marge » du cas DGFiP n°33 (ticket support ouvert — cf. RECAP).

### 2.3 Régime de la marge (art. 297 A CGI) — ✅ confirmé DR4
- Sous régime de marge, **la TVA ne figure JAMAIS distinctement** sur le bordereau (art. 297 E). Mention « Régime particulier – Biens d'occasion ».
- Modèle **2 lignes** validé en staging : adjudication (E, 0 %, VATEX-EU-F/I/J selon nature du bien) + frais acheteur (S, 20 %).

## 3. La subtilité métier (cœur du risque — cf. Analyse-Donnees §5)

**On ne peut pas déduire mécaniquement le bon VATEX du seul code régime du logiciel.** Trois situations produisent une adjudication « sans TVA apparente » pour des raisons juridiques **différentes** :

| Situation | Pourquoi 0 % | Catégorie/VATEX correct |
|---|---|---|
| Régime de la marge (`RegimeMarge=true`) | TVA sur la marge du revendeur | E + VATEX-EU-F/I/J selon le bien |
| Vendeur non assujetti (`assujetti_tva=false`) | le vendeur est un particulier | plutôt hors champ / E + motif différent |
| Assujetti normal | — | S + taux |

**Piège confirmé** : en pratique des enchères, le cas « vendeur non assujetti » est *souvent le déclencheur même du régime de la marge* (le CP revend pour un particulier sous le régime de la marge), alors que le flag `RegimeMarge` peut être à `false` dans la base. → **Un libellé "non assujetti" peut correspondre fiscalement à une vente au régime de la marge.** Se tromper = mauvais motif transmis.

**Conclusion : le mapping doit être validé régime par régime par le CMP / l'expert-comptable.** L'outil ne devine pas ; il applique une table validée humainement.

## 4. Conception du moteur

### 4.1 La table de mapping (paramétrable, par déploiement)

Format JSON, externe au code, versionné et horodaté :

```json
{
  "mappingVersion": "cmp-v1",
  "validatedBy": "Expert-comptable CMP",
  "validatedDate": "2026-07-15",
  "defaultBehavior": "block",        // régime non listé → blocage (jamais "guess")
  "rules": [
    { "sourceRegimeCode": "5",  "label": "Assujetti 20%",
      "part": "adjudication", "category": "S", "rate": 20, "vatex": null },
    { "sourceRegimeCode": "6",  "label": "Non assujetti 20%",
      "part": "adjudication", "category": "E", "rate": 0, "vatex": "VATEX-EU-J",
      "note": "À VALIDER : marge (EU-J) ou hors champ ? — défaut marge collection" },
    { "sourceRegimeCode": "*", "part": "frais_acheteur",
      "category": "S", "rate": null, "rateFrom": "computed",
      "note": "frais toujours taxables, taux = montant_tva_frais/montant_frais_ht" }
  ]
}
```

- **`defaultBehavior: "block"`** est non négociable : un régime non mappé bloque le document (contrôle F4), ne produit jamais un envoi à l'aveugle.
- La règle peut dépendre de la **part** (adjudication vs frais) et de flags source (`RegimeMarge`, `assujetti_tva`) — donc condition plus riche qu'un simple code.
- Le **taux des frais** peut être calculé (`montant_tva_frais / montant_frais_ht`) plutôt que figé.

### 4.2 Sortie : `PivotLineTax` + trace

Pour chaque ligne, le moteur produit le `PivotLineTax` (cf. F1/F2) **et** une `MappingTrace` :
> « Ligne adjudication, régime source 6 (Non assujetti 20%) → catégorie E / 0% / VATEX-EU-J, par règle #2 de la table cmp-v1 (validée 2026-07-15). »

Cette trace est **archivée dans le Tracking** (F6) → piste d'audit fiable : on peut prouver, des années plus tard, quelle règle a produit quel motif d'exonération.

### 4.3 Détection des régimes non mappés (proactif)
Au démarrage / via `check-config`, le moteur croise `IExtractor.ListSourceTaxRegimes()` avec la table de mapping → liste les régimes présents en base mais absents de la table. Permet de compléter la table **avant** qu'un document ne soit bloqué en production.

## 5. Risques et sanctions (DR2 — non confirmé adversarialement, mais cohérent)

- Motif d'exonération erroné transmis = donnée fiscale fausse. La responsabilité incombe à l'entreprise assujettie (le CMP), pas à l'éditeur — **mais** la passerelle doit border contractuellement (cf. F6/DR6) et techniquement (mapping validé par l'expert-comptable, trace d'audit).
- C'est pourquoi le processus de **validation humaine du mapping** est un livrable du projet, pas une option : la table doit porter `validatedBy` / `validatedDate`.

## 6. Décisions à valider ensemble

| # | Décision | Recommandation |
|---|---|---|
| 1 | Régime 6 (non assujetti, omniprésent en DEMO) : marge EU-J ou hors champ ? | ❓ **à trancher impérativement par l'expert-comptable CMP** — c'est LE point fiscal du projet. Défaut prudent : bloquer jusqu'à validation |
| 2 | Confirmer la liste VATEX + le « montant de marge » cas n°33 | ticket support B2Brouter (déjà ouvert) |
| 3 | La table de mapping est-elle éditable en console (F10) ou fichier seul ? | fichier seul en V1 (auditable, validé formellement) |
| 4 | Re-vérifier les codes catégories/VATEX sur le code list officiel EN 16931 v17.0 + EXTENDED-CTC-FR | avant figeage du mapping de prod |
| 5 | Conserver l'historique des versions de table (cmp-v1, cmp-v2…) | oui — chaque doc émis pointe la version de table utilisée |
