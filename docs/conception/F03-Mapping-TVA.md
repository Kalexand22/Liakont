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

> ⚠️ Point déjà identifié au projet : **il n'existe pas de code VATEX-FR dédié spécifiquement aux biens d'occasion/art/antiquités** — on utilise les **VATEX-EU-F/I/J**. La **forme canonique** du « montant de la marge » du cas DGFiP n°33 est **sourcée sur le standard en §2.5**
(PROPOSÉE par B2C-05b, soumise à `GATE_B2C_SHAPE_SOURCING`) : pour l'**e-reporting B2C (flux 10.3)** elle
passe par la **catégorie de transaction TMA1** (marge ramenée HT, TT-82/TT-87), **sans VATEX** — les codes
**VATEX-EU-F/I/J** ci-dessus relèvent de la représentation **par facture détaillée** (e-invoicing/Factur-X),
pas du bloc de transaction agrégé. Le produit étant **agnostique PA** (CLAUDE.md n°8/16), un retour de PA
(p. ex. ticket support B2Brouter) ne vaut que comme **preuve d'appui**, jamais comme source ni comme gate —
aucune fonctionnalité produit ne dépend de ce qu'**une** PA sait faire (item de sourcing dédié, type B2C-05/05b).

### 2.3 Régime de la marge (art. 297 A CGI) — ✅ confirmé DR4
- Sous régime de marge, **la TVA ne figure JAMAIS distinctement** sur le bordereau (art. 297 E). Mention « Régime particulier – Biens d'occasion ».
- Modèle **2 lignes** validé en staging : adjudication (E, 0 %, VATEX-EU-F/I/J selon nature du bien) + frais acheteur (S, 20 %).

### 2.4 Composition du « montant de la marge » — e-reporting B2C (cas DGFiP n°33) — 🟧 PROPOSÉ (B2C-05, soumis à GATE_B2C_SOURCING)

> ⚠️ **Statut : PROPOSITION d'ancrage soumise à validation humaine** (checkpoint intra-segment `GATE_B2C_SOURCING`, owner Karl / métier — `tasks/plan-ereporting-b2c-10-3.md` §0.1, pas de ratification expert-comptable). Tant que cette gate n'est pas `done`, **aucun calcul ni transmission de marge n'est codé** (CLAUDE.md règle n°2). Cette section *établit* la source primaire ; elle ne fige **aucun** enum.

**Périmètre.** L'e-reporting B2C de la marge (document 10.3, « montant de la marge », cas DGFiP n°33) concerne l'opérateur de ventes volontaires (OVV) / commissaire-priseur **agissant en son nom propre** pour le compte d'un commettant non assujetti — l'intermédiaire opaque déjà décrit en F07-F08 §A.3 (deux jambes : commettant→OVV et OVV→acquéreur ; le périmètre CMP ne couvre que la 2e).

**Composition de la marge — SOURCÉE.** Sous le régime de la marge, la base d'imposition de cet opérateur est définie par l'**art. 297 A I-2° du CGI** :
> « la différence entre le prix total payé par l'adjudicataire et le montant net payé par cet assujetti à son commettant ».

Le **BOFiP BOI-TVA-SECT-90-50 §270** (ventes aux enchères publiques) décompose ces deux termes :
- **prix total payé par l'adjudicataire** = prix d'adjudication + impôts/droits/taxes dus au titre de l'opération + **frais accessoires demandés à l'acquéreur** (commission acheteur) ;
- **montant net payé au commettant** = prix d'adjudication **diminué de la commission et des autres frais dus par le commettant** (commission vendeur).

Le prix d'adjudication s'annule dans la différence. Le **§270 conclut lui-même, verbatim**, que la marge « correspond en fait à la **commission totale** du commissaire-priseur (sur son commettant et l'acheteur) ». **C'est cette conclusion du texte primaire — et non une réduction algébrique faite ici — qui fait foi** ; en termes de données source elle s'exprime :

> **marge = frais (commission) à la charge de l'acheteur + frais (commission) à la charge du vendeur**

> ⚠️ **Sort du 3e terme « impôts, droits, prélèvements et taxes dus au titre de l'opération »** (présent côté acheteur dans le §270). Le §270 ne le retient PAS dans la marge nette : il conclut que le résultat *est* la commission totale, donc ces taxes (collectées au titre de l'opération, non conservées par l'opérateur) sont **hors marge**. **À valider à `GATE_B2C_SOURCING`** : si la donnée source d'un déploiement porte des impôts/droits/taxes propres à l'opération (p. ex. droit de suite), l'humain confirme qu'ils restent hors base de marge — un implémenteur ne doit PAS les agréger à `frais acheteur + frais vendeur` sans cette confirmation (sinon base sur/sous-estimée).

→ La « formule = frais acheteur + frais vendeur » du plan B2C est donc **confirmée sur texte primaire** (CGI 297 A I-2° + BOI-TVA-SECT-90-50 §270), et n'est plus une hypothèse. Conséquence : le **bordereau vendeur** (BV, frais vendeur) est **intégral au calcul** — l'omettre tronque la marge (alimente B2C-06/07/08). Le calcul reste en **`decimal`, arrondi half-up 2 décimales** (règle n°1).

**Cohérence art. 297 E.** Le montant de marge reste sous le régime de la marge : **aucune TVA distincte** n'y figure (art. 297 E ; cf. §2.3 et F07-F08:36). Le « montant de marge » est une **base**, jamais une ligne portant une ventilation de TVA > 0 (critère bloquant de B2C-09b).

**Méthode de calcul — NON figée, AUCUN enum pré-câblé.** Deux méthodes existent dans le régime *général* des assujettis-revendeurs :
- **au coup par coup** (par opération) : différence prix de vente / prix d'achat de **chaque** objet, seules les opérations bénéficiaires imposées (BOI-TVA-SECT-90-20-20 §110) ;
- **par globalisation** (par période mensuelle/trimestrielle) : différence achats globaux / ventes globales, **facultative** (§260) et réservée au cas où « les articles **ne peuvent pas être individualisés** » (§250).

Or pour l'OVV agissant en son nom propre, le texte *spécifique aux enchères* (**SECT-90-50 §270**) détermine la marge **par opération** (la commission est connue lot par lot) et **ne mentionne pas la globalisation**. L'applicabilité de la globalisation à la marge-commission de l'OVV n'est donc **pas établie** par le texte applicable à ce cas.

→ **Application de la gouvernance §0.1 (« source l'existence avant de nommer ; 0/1 option ancrée pour CE cas → pas d'enum inventé ») :** pour le périmètre visé (OVV, marge-commission par lot), **seule la détermination par opération est ancrée**. On ne pré-câble donc **aucun** enum de méthode (`{Globalisation, CoupParCoup}` interdit). L'ouverture d'un paramètre « méthode de marge » (et donc l'item B2C-08bis) est **conditionnée** à un ancrage ultérieur, dans une `F*.md`, établissant que la globalisation s'applique à la marge-commission de l'OVV — à défaut, méthode **par opération unique**, sans paramètre.

**Décision en attente (GATE_B2C_SOURCING, humain).** Valide (a) la composition ci-dessus et (b) l'absence (ou non) d'un paramètre de méthode. Tant que `pending`, l'aval marge (B2C-07/08/08b/09b) reste bloqué — c'est voulu (règle n°2).

**Sources primaires citées :**
- CGI art. 297 A (I-1° revendeurs ; **I-2° ventes aux enchères publiques**) — l'ancrage faisant foi est le **numéro d'article (297 A I-2°)** ; l'identifiant Légifrance `LEGIARTI000048835065` n'est qu'un lien de commodité, dont la version en vigueur (article non abrogé/distinct) est à reconfirmer manuellement à la validation de `GATE_B2C_SOURCING`.
- BOI-TVA-SECT-90-50 **§270** (ventes aux enchères publiques — base d'imposition) — version 2025-05-14.
- BOI-TVA-SECT-90-20-20 **§110** (coup par coup), **§240-260** (globalisation, facultative) — version 2025-05-14.
- CGI art. 297 E (pas de TVA distincte sous le régime de la marge).

### 2.5 Forme canonique du « montant de marge » en e-reporting B2C (flux 10.3, cas DGFiP n°33) — 🟧 PROPOSÉ (B2C-05b, soumis à GATE_B2C_SHAPE_SOURCING)

> ⚠️ **Statut : PROPOSITION d'ancrage de la FORME canonique, soumise à validation humaine**
> (checkpoint intra-segment `GATE_B2C_SHAPE_SOURCING`). Là où §2.4 source la *composition* de la marge
> (de quoi elle est faite : frais acheteur + frais vendeur), la présente section source la *forme* :
> **quel champ du standard porte le montant de marge, sous quelle catégorie, avec quel (éventuel) code
> d'exonération**. Tant que cette gate n'est pas `done`, **aucun code de transmission/calcul de la marge
> n'est écrit** et la forme du payload reste **gelée** (CLAUDE.md n°2/n°3). Cette section *établit la
> source primaire* ; elle ne fige **aucun** enum. Le produit étant **agnostique PA** (CLAUDE.md n°8/16),
> aucun élément ci-dessous ne provient d'un retour de PA : un ticket support ne vaut que comme preuve
> d'appui, jamais comme source ni comme gate.

**Le porteur canonique sur le standard — SOURCÉ.** Le flux 10.3 (e-reporting de *transactions* B2C) se
transmet via le **bloc de données de transaction agrégé** (`transaction.xsd` → `Transactions` **TG-31**
/ `TaxSubtotal` **TG-32**), *non* via le bloc `Invoice` détaillé (TG-8/TG-23) : le Dossier général v3.2
précise que « chaque occurrence du bloc de données de transaction (10.3) correspond à un jour d'activité,
une devise [et un taux] ». L'**Annexe 7 — Règles de gestion V1.9, règle G1.57** ancre la forme du montant
de marge dans ce bloc, verbatim :

> « Si la catégorie de transaction indiquée est **TMA1** (Opérations soumises à un régime [...] de TVA sur
> la marge [...]), le **montant HT (TT-82)** est le montant total de la **marge ramenée HT**, et la **base
> d'imposition (TT-87)** indiquée est le montant de la **marge ramenée HT** correspondant au **taux de TVA
> (TT-86)**. »

La catégorie **TMA1** est définie par la même Annexe 7, **règle G1.68** : « Opérations donnant lieu à
l'application des régimes prévus au e) du 1 de l'article 266 et aux articles 268 et 297 A du CGI (régime
de TVA sur la marge) » — ce qui couvre exactement l'OVV/commissaire-priseur de §2.4 (297 A I-2°).

**Forme canonique proposée (cohérente XSD + G1.57/G1.68) :**

| Élément | Réf. (transaction.xsd) | Contenu pour la marge n°33 | Source |
|---|---|---|---|
| Catégorie de transaction | `Transactions/CategoryCode` **TT-81** (TG-31) | **`TMA1`** | G1.68 |
| Montant total HT | `Transactions/TaxExclusiveAmount` **TT-82** (TG-31) | montant total de la **marge ramenée HT** | G1.57 |
| Taux | `TaxSubtotal/TaxPercent` **TT-86** (TG-32) | taux de TVA applicable | G1.57 |
| Base d'imposition | `TaxSubtotal/TaxableAmount` **TT-87** (TG-32) | **marge ramenée HT** pour ce taux | G1.57 |
| Montant TVA | `TaxSubtotal/TaxTotal` **TT-88** (TG-32) | TVA sur la marge | XSD (TG-32) |

**Conséquence n°1 — PAS de catégorie UNCL5305 {E} ni de VATEX dans ce bloc.** Le bloc agrégé `TaxSubtotal`
(TG-32) ne porte **ni code de catégorie de TVA (UNCL5305) ni code d'exonération (VATEX)** : ses seuls champs
sont taux / base / montant de TVA (TT-86/87/88). La marge est portée par la **catégorie de transaction
TMA1** (TT-81), pas par un motif d'exonération. **Cela distingue le cas de §2.2/§2.3** : les codes
**VATEX-EU-F/I/J** (biens d'occasion / œuvres d'art / objets de collection) relèvent de la représentation
**par facture détaillée** (TG-23 `TaxCategory`/TT-56 + TT-59) — c.-à-d. l'**e-invoicing** (Factur-X, flux 1,
phase 2) ou le modèle 2-lignes validé en staging —, **et non** du bloc de transaction agrégé du flux 10.3
B2C. Les deux représentations coexistent pour deux flux distincts ; il ne faut pas transposer le VATEX du
détail facture vers l'agrégat de transaction.

**Conséquence n°2 — la marge est une base imposable agrégée, jamais une ligne à TVA distincte.** Conforme
art. 297 E (§2.3) : aucune TVA ne figure « distinctement » au niveau du bien ; la TVA n'apparaît qu'au
niveau de l'agrégat TMA1 (TT-88), calculée sur la marge ramenée HT. Le « montant de marge » reste une
**base** (critère bloquant de B2C-09b : jamais une ligne portant une ventilation de TVA distincte au grain bien).

**Conséquence n°3 — grain.** Le bloc 10.3 est **agrégé par jour × devise × taux** côté plateforme (cohérent
`mapping-pivot-en16931.md` §1/§10 : le pivot porte la donnée par document, la PLATEFORME agrège). B2C-05b
ne fige donc **aucun nouveau champ pivot** ; le portage de l'indicateur « opération au régime de la marge »
et du montant relèvera de l'aval gelé (B2C-09b/09c), après la gate.

**Décisions DÉFÉRÉES à GATE_B2C_SHAPE_SOURCING (humain) — le standard ne tranche pas seul (n°2) :**

1. **« Marge ramenée HT » — conversion et taux.** G1.57 exige une marge **ramenée HT** « correspondant au
   taux de TVA (TT-86) », mais ne fixe **pas** le taux ni la formule de conversion TTC→HT. La marge de §2.4
   (commission totale = frais acheteur + frais vendeur) est, en pratique des enchères, exprimée **commission
   TTC** (la ligne « frais acheteur » est S/20 % en §2.3). Le passage à la base HT (`marge_HT = marge_TTC /
   (1 + taux)`, TVA = `marge_TTC − marge_HT`) et **le taux applicable** (standard 20 % pour le mobilier
   courant ? cas réduits ?) doivent être **confirmés par l'humain** (table de mapping validée par
   l'expert-comptable du tenant, §4.1) — un implémenteur ne doit pas figer le taux ni la formule sans cette
   validation (sinon base sur/sous-estimée).
2. **Méthode de détermination de la marge.** G1.57 (N.B. : « règle de gestion métier, non contrôlable
   applicativement ») admet, via le Dossier général v3.2 (« Tolérances »), une **méthode de calcul
   simplifiée** (marge fondée sur un **taux de marge moyen**) renvoyée à la **norme de facturation
   électronique AFNOR** (XP Z12-012/-013/-014, **payantes, non incluses dans le repo** — cf.
   `docs/references/dgfip-v3.2/LECTURE-LIAKONT.md`). Le choix méthode réelle (par lot, §2.4) vs simplifiée
   (taux moyen) et l'éventuel sourcing de la norme AFNOR sont **déférés à l'humain** ; à défaut d'ancrage
   AFNOR, **détermination réelle par opération** (cohérent §2.4), sans paramètre de méthode inventé.

**Débloque** (une fois la gate `done`) la branche transmission B2C-09c : capacité `SupportsMarginAmountReporting`
déjà posée en B2C-09a + méthode `IPaClient` **agnostique** à ajouter (jamais conditionnée à ce qu'une PA sait faire).

**Sources primaires citées :**
- **DGFiP, Annexe 7 — Règles de gestion V1.9** (`docs/references/dgfip-v3.2/2- Annexes_v3.2/`) : règle
  **G1.57** (régime de la marge → TT-82/TT-87 = marge ramenée HT sous TMA1) et règle **G1.68** (liste des
  catégories de transaction ; **TMA1** = art. 266-1-e, 268, 297 A CGI).
- **DGFiP, XSD e-reporting v3.2** (`docs/references/dgfip-v3.2/3- XSD_v3.2/1 - E-reporting/transaction.xsd`) :
  bloc `Transactions` **TG-31** (TT-81/82/83) et `TaxSubtotal` **TG-32** (TT-86/87/88).
- **DGFiP, Dossier de spécifications externes FE — Dossier général v3.2** : nature agrégée (jour × devise ×
  taux) du bloc de données de transaction (10.3) ; tolérance « méthode de calcul simplifiée de la TVA sur la
  marge en B2C » renvoyée à la norme AFNOR.
- CGI **art. 297 A** (régime de la marge, §2.4) et **art. 297 E** (pas de TVA distincte, §2.3).
- Cohérence pivot ↔ EN 16931 : `docs/architecture/mapping-pivot-en16931.md` §1/§9/§10.

> ℹ️ **Sur le libellé « cas n°33 ».** « cas DGFiP n°33 » est le **label projet** de ce cas (e-reporting B2C
> de la marge), repris de §2.4 / `tasks/plan-ereporting-b2c-10-3.md`. L'**ancrage faisant foi** de la forme
> canonique est **G1.57 + G1.68 + transaction.xsd** ci-dessus, indépendamment de la numérotation interne.

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

> **Amendement v6 (2026-06-04, item TVA02) :** le modèle v6 (table EN BASE, par tenant) est clé par
> le couple **(code régime source, part)** avec unicité (INV-TVAMAPPING-003) et match **EXACT** par le
> moteur (`TvaMapper`). Le **joker `"*"`** de l'exemple ci-dessous (forme fichier pré-v6) est
> **obsolète et REFUSÉ** par `MappingTableValidator` : il ouvrait un piège silencieux (table acceptée
> à l'écriture, puis sur-blocage de toutes les lignes « frais » à l'exécution). La couverture des frais
> s'exprime donc par une règle **explicite (code régime, part frais)** pour chaque régime réel — ce qui
> est cohérent avec le §3 (« le mapping doit être validé **régime par régime** par l'expert-comptable »).
> L'exemple JSON ci-dessous est conservé pour mémoire ; sa ligne `"sourceRegimeCode": "*"` ne doit pas
> être reproduite telle quelle.

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

#### 4.1 bis — Collection de régimes par ligne + scission BG-30 = différé tracé (redline RD409)

> **Amendement RD409 (2026-06-20, addendum ADR-0004-bis).** La ligne pivot porte une **collection** de
> régimes source bruts (`PivotLineDto.SourceRegimeCodes`, ADR-0004 D3-1), jamais une simple chaîne : une
> source peut encoder un **couple de codes** (NAV) ou **plusieurs taxes sur une même ligne** (Axelor). La
> **scission BG-30** (EN 16931 : exactement 1 catégorie de TVA par ligne pivot) d'une ligne multi-codes/
> multi-taxes est un **différé tracé**, PAS « V1 » : l'**association** d'un code régime à une ventilation TVA
> particulière n'est **pas sourcée** pour ce cas. Le moteur de CHECK **bloque délibérément** toute ligne qui
> ne présente pas la forme NON AMBIGUË (exactement 1 code régime ET 1 ventilation) — avec un motif explicite,
> sans jamais deviner l'association (CLAUDE.md n°2/n°3). Implémenté et testé :
> `Liakont.Modules.Pipeline.Infrastructure.Check.CheckTvaMapping` (blocage de forme, lignes 53-61 ;
> documentation de classe lignes 25-30). Les documents réels du contrat (golden files contrat-v1) sont tous
> de la forme non ambiguë. La scission multi-codes ne sera implémentée que lorsque F03 sourcera l'association
> régime→ventilation (table validée par l'expert-comptable du tenant, jamais une heuristique).

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

> **Amendement (2026-06-08, items TVA05 / API04 / WEB07a-b) : décision #3 révisée.** La table de mapping est désormais **éditable en console** (moteur d'édition TVA05 — `AddMappingRuleCommand` / `UpdateMappingRuleCommand` / `RemoveMappingRuleCommand`, endpoints API04, page WEB07a/b), tout en conservant les garanties d'auditabilité : validation humaine obligatoire (`validatedBy` / `validatedDate`), toute mutation repasse la table « NON VALIDÉE » et suspend les envois, journal append-only (`MappingChangeLog`). Le « fichier seul en V1 » reste possible via le seed versionné ; les deux chemins écrivent la même table. Aucune règle fiscale n'est ajoutée : catégories (F03 §2.1) et codes VATEX (F03 §2.2) sont consommés tels quels, jamais en saisie libre.
