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

> **Amendement (2026-06-22, décision Karl, interactif) — `GATE_B2C_SHAPE_SOURCING` LEVÉE.** Les deux décisions
> déférées ci-dessus sont tranchées et ancrées ici. Aucun taux ni forme n'est **inventé** : chaque élément
> renvoie soit à une donnée source (opérateur), soit au moteur F03 déjà validé, soit au §270 (n°2).
>
> 1. **Nature de la donnée source = TTC.** Karl (opérateur enchères) confirme que l'honoraire émis par la
>    source (EncheresV6 `type_ligne 2`, frais acheteur **et** vendeur) est porté **TTC**, pas HT. → le
>    commentaire « Montant HT » de `PivotBuyerFeeDto.NetAmount` / `PivotSellerFeeDto.NetAmount` est **corrigé
>    en TTC** (correction de **libellé** uniquement — le nom de propriété et la valeur sérialisée sont
>    inchangés, donc **hash-neutre** ; pas de golden contrat-v2). `MarginCalculator` cesse de traiter ce
>    montant comme une base HT finale.
> 2. **Marge = SOMME des honoraires (frais acheteur + frais vendeur) — AUCUNE séparation acheteur/vendeur.**
>    Sourcé §270 (« la marge correspond en fait à la **commission totale** ») / F03 §2.4 : les deux honoraires
>    **se somment** en **une seule base**, ils ne forment pas deux marges distinctes. **Taux = le taux mappé de
>    la ligne d'honoraire** (moteur F03, `TvaMapper` sur `SourceRegimeCode`) — pas un taux inventé : celui que
>    la **table validée du déploiement** (§3-§4) assigne. **En pratique les honoraires d'une vente sont au même
>    taux (S/20 %, §2.3) → UNE base, UN taux.** Le **seul** découpage `tax_subtotals` est **par taux, au niveau
>    de l'AGRÉGAT du JOUR** (flux 10.3 = jour×devise×taux, §2.5, sourcé), entre **ventes distinctes** — **jamais**
>    une séparation acheteur/vendeur. **Fail-closed** (n°2/n°3) : honoraire à code TVA non mappé → **bloqué** ;
>    honoraires d'**une même vente** à taux **différents** (découpage de la marge **non sourcé**) → **bloqué**, jamais deviné.
> 3. **Conversion TTC→HT (par taux, decimal half-up, `PivotRounding`)** : `marge_HT = marge_TTC / (1 + taux)` ;
>    `TVA_marge = marge_TTC − marge_HT`. Forme sur le standard (G1.57/§2.5) : `TT-87 (TaxableAmount)` = `marge_HT`
>    par taux ; `TT-88 (TaxTotal)` = `TVA_marge` ; `TT-86 (TaxPercent)` = taux ; `TT-81` = **TMA1**. **Cohérent
>    297 E** : aucune TVA distincte au grain document ; elle n'apparaît qu'au niveau de l'agrégat TMA1 (TT-88).
> 4. **Méthode = par document/part** (la seule ancrée pour l'OVV, §270) ; **globalisation non retenue** →
>    **aucun** paramètre de méthode (cohérent §2.4 ; pas d'enum `{Globalisation, CoupParCoup}`).
> 5. **`role_code` (fil SuperPDP, requis) = `SE`** (vente) — **SOURCÉ**. La donnée DGFiP est **TT-15 « Code
>    rôle » du déclarant** (`/ReportDocument/Issuer/RoleCode`, règle **G7.52**, Annexe 6 — Format sémantique
>    e-reporting V1.10, repo `docs/references/dgfip-v3.2/2- Annexes_v3.2/`), verbatim : « **BY** si le déclarant
>    est **Acheteur** ; **SE** si le déclarant est **Vendeur** ». L'OVV reporte ses **ventes** aux particuliers
>    → déclarant = **Vendeur** → **`SE`**. ⚠️ **BA/BV n'est PAS BY/SE** : BY/SE qualifie le rôle **du déclarant**
>    (acheteur vs vendeur), pas la jambe — les parts BA+BV sont composantes de la **même** vente-marge → **un
>    seul** report `SE`, **jamais** scindé. *(Un report d'ACHATS de l'OVV à des non-assujettis, s'il était requis,
>    serait un report `BY` distinct — hors marge, hors de ce périmètre.)*
>
> **Conséquence orchestration :** la branche B2C-09c (calcul + transmission de la marge) devient **codable**.
> L'item `B2C09c` et le gate `GATE_B2C_SHAPE_SOURCING` côté `orchestration/state.yaml` sont à réconcilier
> (geste opérateur, hors de cette session interactive — on n'édite pas `state.yaml` pendant des sessions actives).

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

### 2.6 Codelist complète TT-81 — catégories de transaction e-reporting (Flux 10.3) — ✅ SOURCÉ (primaire dans le repo)

> Statut : ✅ **sourcé sur texte primaire présent dans le repo** (pas une glose d'éditeur/blog). Là où §2.5
> ne détaillait que **TMA1** (cas marge n°33), la présente section fige la **liste fermée complète** des 4
> catégories de transaction — nécessaire dès qu'un e-reporting B2C (10.3) doit porter une catégorie autre
> que la marge (biens ou services taxables, opérations non soumises à la TVA en France).

La donnée **TT-81 « Catégorie des transactions »** (`/TransactionsReport/Transactions/CategoryCode`, bloc
agrégé TG-31 — cf. §2.5) est codifiée par la **règle G1.68**. Liste **verbatim** (G1.68) :

| Code | Libellé normatif (G1.68, verbatim) |
|---|---|
| **TLB1** | Livraisons de **biens** soumises à la taxe sur la valeur ajoutée |
| **TPS1** | Prestations de **services** soumises à la taxe sur la valeur ajoutée |
| **TNT1** | Livraisons de biens et prestations de services **non soumises à la TVA en France** dont les ventes à distance intracommunautaires mentionnées au 1° du I de l'art. 258 A et à l'art. 259 B du CGI |
| **TMA1** | Opérations donnant lieu à l'application des régimes prévus au e) du 1 de l'art. 266 et aux art. 268 et **297 A** du CGI (régime de TVA sur la marge) — cf. §2.4/§2.5 |

**Liste FERMÉE.** La codelist TT-81 se limite à ces **4** valeurs ; tout autre code (p. ex. la coquille
« TLS1 » vue chez certains éditeurs) est **invalide**. Conséquence produit (règles n°2/n°3) : la catégorie
de transaction est **validée contre cette liste fermée, fail-closed** — une transaction dont la catégorie
ne peut être dérivée de manière sourcée est **bloquée** (Blocking), jamais transmise avec une catégorie
devinée. Les libellés **ne doivent jamais** être recodés en glose approximative : ne pas écrire TNT1 =
« exonéré » ni « hors champ » (le sens normatif est « non soumises **en France** », opérations non situées
en France / intracommunautaires), ne pas écrire TMA1 = « mixte » (c'est la **marge**) — seul le libellé
G1.68 fait foi.

> ⚠️ **Dérivation catégorie ← donnée source : NON figée ici (se source comme le mapping TVA, §3-§4).**
> Quelle catégorie s'applique à une transaction (biens vs services ; soumis FR vs non ; marge) se dérive
> **de la même façon que le mapping TVA** : par une **table validée régime par régime** (§4.1), jamais par
> une heuristique. G1.57 lie déjà **TMA1 ↔ régime de la marge** (§2.5). Les correspondances pour
> **TLB1/TPS1/TNT1** (nature biens/services, territorialité) restent un **paramétrage tenant** sourcé,
> fail-closed — aucun enum de dérivation n'est pré-câblé tant qu'une `F*.md` ne l'ancre pas.

**Source primaire (dans le repo) :** DGFiP, **Annexe 7 — Règles de gestion V1.9**, feuille « Règles de
gestion », règle **G1.68 « Catégorie de transactions »** (ligne 43) —
`docs/references/dgfip-v3.2/2- Annexes_v3.2/20260430_Annexe 7 - Règles de gestion - V1.9.xlsx`. Confirmée
indépendamment sur les Annexes A AFNOR XP Z12-012 V1.3 / Z12-014 V1.2 (FNFE-MPE) et la spec externe B2B
DGFiP v3.2 (impots.gouv.fr). ⚠️ Codelist mise à jour par la DGFiP les **15 mai / 15 novembre** — tracer la
version (ici **V1.9**, fichiers datés 2026-04-30) au figeage d'un mapping de prod.

### 2.7 E-reporting B2C TAXABLE (régime du prix total, livraison de biens) — flux 10.3, catégorie TLB1 — 🟧 PROPOSÉ (BUG-8)

> ⚠️ **Statut : PROPOSITION d'ancrage, défauts défendables marqués `[À CONFIRMER EC]`.** Là où §2.4/§2.5
> sourcent le cas **marge** (vendeur NON assujetti → TMA1), la présente section source le cas **symétrique**
> du **régime du prix total** (vendeur ASSUJETTI → livraison taxable) en e-reporting B2C. Elle **lève le point
> ouvert de §2.6** (« dérivation TT-81 non figée tant qu'une `F*.md` ne l'ancre pas ») **pour le seul cas des
> enchères mobilières** : c'est **cette section** qui ancre la dérivation `TLB1`. Aucun taux ni base n'est
> **inventé** : chaque élément renvoie à une source primaire (§270, G1.68, G7.52) ou au moteur F03 validé
> (§3-§4). Le produit reste **agnostique PA** (CLAUDE.md n°8/16). Les résiduels non tranchés par le texte sont
> marqués `[À CONFIRMER EC]` (décision expert-comptable du tenant au déploiement, §4.1) et **fail-closed** par
> défaut — jamais devinés (n°2/n°3).
>
> **Go/no-go (activation).** Le flux TLB1 est **activé bout-en-bout en build** (marquage CHECK, job agrégé,
> handler/définition de job enregistrés) sur cet ancrage **sourcé** (¶270/G1.68/G7.52) avec ses résiduels
> fail-closed — posture build-stage assumée (défaut défendable, données jetables). **Figeage PROD subordonné à
> la levée explicite du statut « proposition » par l'expert-comptable du tenant** (validation des résiduels :
> sort du droit de suite, rattachement de la commission acheteur, lots non-biens) — à tracer avant tout
> déploiement réel, comme `GATE_B2C_SOURCING`/`GATE_B2C_SHAPE_SOURCING` l'ont été pour la marge (§2.4/§2.5).

**Périmètre — les enchères sont TOUJOURS opaques (CGI 256 V).** L'OVV / commissaire-priseur agit **en son nom
propre** (jamais en intermédiaire transparent — le mode opaque « ne se présume pas » est ici la règle métier
établie, Livre blanc CNCJ ch. 5.1). L'opération se décompose en **deux livraisons** (commettant→OVV, puis
OVV→adjudicataire). Le **régime du lot suit le statut du commettant** (§3, « piège confirmé ») :
- commettant **non assujetti** (particulier) → régime de la **marge** (297 A) → **TMA1**, cf. §2.4/§2.5 ;
- commettant **assujetti** (livraison ouvrant droit à déduction) → **régime du prix total**, la revente
  OVV→adjudicataire est une **livraison de biens taxable** au prix total (TVA distincte, art. 297 E **ne
  s'applique pas**). C'est le périmètre de la présente section.

**Canal = STATUT DU TIERS, jamais la nature du bien.** Pour un lot au régime du prix total, l'aiguillage de la
revente OVV→adjudicataire suit le **statut de l'adjudicataire** : adjudicataire **professionnel** (SIREN /
n° TVA / indice société) → **e-invoicing B2B** (facture, hors de cette section) ; adjudicataire **particulier**
(non assujetti, sans identifiant) → **e-reporting B2C, flux 10.3**, objet de la présente section. *(Le « bordereau
d'adjudication vaut facture remise à l'acquéreur » — Livre blanc CNCJ ch. 5.1 ; côté B2C il alimente la
transaction agrégée, pas une facture nominative.)*

**Catégorie TT-81 = `TLB1` — SOURCÉ (lève §2.6 pour ce cas).** La donnée TT-81 (§2.6) vaut **`TLB1`** :
> G1.68, verbatim : « **TLB1** — Livraisons de **biens** soumises à la taxe sur la valeur ajoutée ».

L'objet d'une **vente aux enchères mobilières** est par nature un **bien meuble corporel** (le lot) ; la revente
taxable de ce lot par l'OVV est une **livraison de biens soumise à la TVA** → **TLB1**. Ce n'est pas une
heuristique (interdite par §2.6) mais l'**application directe du libellé normatif G1.68** au fait, **ancrée
ici** dans une `F*.md` comme §2.6 l'exige. **Rôle déclarant TT-15 = `SE`** (G7.52, cf. §2.5 ⑤) : l'OVV reporte
ses **ventes** aux particuliers → déclarant = **Vendeur** → `SE`. *(Une prestation de service pure de l'OVV — p.
ex. honoraire facturé à un assujetti — relèverait de `TPS1` et d'un flux distinct ; hors de cette section.)*

**Base d'imposition = prix total payé par l'adjudicataire — SOURCÉ §270.** Le **BOI-TVA-SECT-90-50 §270**
(ventes aux enchères publiques, version 2025-05-14) définit verbatim le **prix total payé par l'adjudicataire** :
> prix d'adjudication + impôts/droits/taxes dus au titre de l'opération + **frais accessoires demandés à
> l'acquéreur** (commission acheteur).

Au régime du prix total (commettant assujetti), le prix d'adjudication **ne s'annule pas** dans une différence
(contrairement à la marge §2.4 où achat = vente du bien) : la base de la livraison taxable OVV→adjudicataire est
l'**intégralité du prix total payé**. La **commission acheteur** est, au sens du §270, un **frais accessoire à la
livraison** (même opération) → elle entre dans la **base imposable de la même transaction `TLB1`**, **pas** une
prestation `TPS1` distincte. **C'est le §270 — texte spécifique aux enchères — qui fait foi**, pas une réduction
faite ici. En données source :

> **base TLB1 = adjudication (HT, par taux) + commission acheteur (ramenée HT, par taux)**

| Élément | Réf. (transaction.xsd) | Contenu (prix total taxable) | Source |
|---|---|---|---|
| Catégorie de transaction | `Transactions/CategoryCode` **TT-81** | **`TLB1`** | G1.68 |
| Rôle déclarant | `Issuer/RoleCode` **TT-15** | **`SE`** | G7.52 |
| Montant total HT | **TT-82** | somme des bases HT par taux (adjudication + commission acheteur) | §270 |
| Taux | `TaxSubtotal/TaxPercent` **TT-86** | taux de TVA mappé (moteur F03) | §4.1 |
| Base d'imposition | `TaxSubtotal/TaxableAmount` **TT-87** | base HT pour ce taux | §270 |
| Montant TVA | `TaxSubtotal/TaxTotal` **TT-88** | TVA pour ce taux | XSD (TG-32) |

**Conversion des montants (decimal half-up, `PivotRounding`).** Contrairement à la marge (297 E, aucune TVA
distincte → conversion TTC→HT de toute la marge, §2.5 ③), au régime du prix total la **TVA est distincte au
grain document** :
- l'**adjudication** porte déjà sa ventilation `{HT, TVA}` séparée en source (`montant_adj_ht` + TVA) → reprise
  directe de la ventilation sourcée du CHECK (ADR-0015), **aucun recalcul** ;
- la **commission acheteur** est portée **TTC** par la source (§2.5 ①) → ramenée HT **par taux** comme la marge :
  `HT = TTC / (1 + taux)`, `TVA = TTC − HT`, taux = **taux mappé** de la ligne d'honoraire (§4.1), jamais inventé.

Les deux composantes se **somment par taux** ; le découpage `tax_subtotals` est **par taux**, au niveau de
l'**agrégat du jour** (flux 10.3 = **jour × devise × taux**, §2.5), entre **ventes distinctes** — **jamais** une
séparation adjudication/commission ni acheteur/vendeur. **En pratique, adjudication et commission d'une même
vente sont au même taux (S/20 %) → une base, un taux.** **Fail-closed** (n°2/n°3) : régime ou honoraire à code
TVA non mappé → **bloqué** ; ligne non taxable mêlée (catégorie E/exonérée sur un lot censé taxable) → **bloqué**,
jamais agrégé en `TLB1` à tort.

**Distinction nette d'avec la marge (§2.4/§2.5).** Même **canal** (acheteur particulier → e-reporting B2C 10.3),
**catégorie TT-81 différente** :

| | Marge (§2.4/§2.5) | Prix total (§2.7) |
|---|---|---|
| Statut commettant | non assujetti | **assujetti** |
| Régime | marge (297 A) | **prix total (droit commun)** |
| TVA distincte (297 E) | **non** (`TotalTax == 0`) | **oui** (`TotalTax > 0`) |
| Catégorie UNCL5305 (lignes) | `E` + VATEX-EU-F/I/J | **`S`** + taux |
| Catégorie TT-81 | `TMA1` | **`TLB1`** |
| Base | commission totale (frais acheteur + vendeur) | **prix total payé** (adjudication + commission acheteur) |
| Rôle déclarant | `SE` | `SE` |

Le **marquage** plateforme distingue les deux par `TotalTax` (== 0 → marge ; > 0 → prix total) : aucune
ambiguïté, fail-closed des deux côtés.

**Honoraire vendeur d'un lot taxable — HORS de ce flux.** Le commettant étant **assujetti**, la **commission que
l'OVV lui facture** (honoraire vendeur) est une **prestation de service B2B** de l'OVV au vendeur → **facture
e-invoicing** (aiguillée par le **SIREN vendeur**), `TPS1` le cas échéant — **jamais** agrégée dans la
transaction B2C `TLB1` de la revente. *(Au régime de la marge, l'honoraire vendeur d'un commettant non assujetti
est au contraire une composante de la marge B2C, §2.4 ; cf. mémoire projet « modèle reporting enchères ».)* La
**livraison commettant assujetti→OVV** (première jambe de l'opaque) est l'obligation **du vendeur** (ou une
autofacturation 389 sous mandat, nouveauté — cf. F15) ; elle ne fait pas l'objet d'une émission par l'OVV dans ce flux.

**Résiduels `[À CONFIRMER EC]` (décision expert-comptable du tenant, §4.1 ; fail-closed par défaut) :**
1. **3e terme du §270** — « impôts, droits, prélèvements et taxes dus au titre de l'opération » (p. ex. **droit
   de suite**) : présent dans le prix total payé côté acheteur. Symétrique au ⚠️ de §2.4 — un implémenteur ne
   l'agrège **pas** d'office. Par défaut, **seul ce que la table validée mappe taxable** (`S` + taux) entre dans
   la base `TLB1` ; un poste non mappé **bloque** (jamais une base sur/sous-estimée). À confirmer : traitement
   du droit de suite (hors base, ou base à taux spécifique).
2. **Rattachement de la commission acheteur** — ancré ici **`TLB1` accessoire à la livraison** (§270, frais
   accessoire). Si un déploiement la traite en **prestation `TPS1` distincte**, c'est un **paramétrage tenant**
   (table validée) — la dérivation TT-81 restant table-pilotée (§2.6), ce cas est représentable sans réécriture.
3. **Lots non-biens** — un lot qui ne serait pas une livraison de biens (cas non observé en enchères mobilières)
   ne relève pas de `TLB1` : sa dérivation TT-81 reste **bloquée** tant qu'une règle validée ne l'ancre pas (§2.6).

**Sources primaires citées :**
- CGI **art. 256 V** (intermédiaire opaque agissant en son nom propre — enchères toujours opaques).
- CGI **art. 297 A** *a contrario* (commettant assujetti ⇒ **hors** régime de la marge ⇒ prix total) ; **297 E**
  *a contrario* (TVA distincte au régime du prix total).
- **BOI-TVA-SECT-90-50 §270** (ventes aux enchères publiques — composition du prix total payé), version 2025-05-14.
- **DGFiP, Annexe 7 — Règles de gestion V1.9** : **G1.68** (`TLB1` = livraisons de biens taxables ; cf. §2.6) ;
  **G1.57** (forme du bloc de transaction agrégé, réutilisée) ; **G7.52** (`SE`, Annexe 6, cf. §2.5 ⑤).
- **DGFiP, XSD e-reporting v3.2**, `transaction.xsd` : `Transactions` **TG-31** (TT-81/82/83) et `TaxSubtotal`
  **TG-32** (TT-86/87/88) — même porteur que §2.5.
- **Livre blanc CNCJ — Facturation électronique des ventes judiciaires** ch. 5.1 (opaque/transparent, statut de
  l'adjudicataire → e-invoicing vs e-reporting, bordereau valant facture). *(Preuve d'appui métier, pas un texte
  fiscal primaire.)*

### 2.8 Cartographie générique « régime fiscal → e-reporting B2C » (flux 10.3) — total / marge / export / intracom / franchise / caution — ✅ exonéré international (export/intracom/franchise) IMPLÉMENTÉ, validé PO (BUG-11) ; 🟧 caution résiduelle

> ⚠️ **Statut : PROPOSITION d'ancrage ARCHITECTURAL, défauts défendables `[À CONFIRMER EC]` fail-closed.** §2.4/§2.5
> (marge → `TMA1`) et §2.7 (prix total → `TLB1`) sourcent deux régimes. La présente section les **généralise** en
> une **cartographie unique** *régime fiscal → catégorie de transaction `TT-81`*, étendue aux régimes **exonérés**
> (export, intracommunautaire, franchise) et au **mécanisme de caution**. **Principe** : la plateforme mappe un
> **régime fiscal GÉNÉRIQUE** fourni par l'adaptateur source — jamais un signal propre à un logiciel (généricité,
> blueprint.md). Aucune TT-81/catégorie inventée (n°2) : chaque case renvoie à une source primaire ou est marquée
> `[À CONFIRMER EC]` fail-closed (n°2/n°3).
>
> **Taxonomie cible (modèle autoritaire d'un système d'enchères de production).** Un système réel (VPAuto /
> `open-auction`) classe chaque bordereau par un **groupe de TVA métier** dérivé de `pays acheteur + société/
> particulier` : `ASS` (assujetti, prix total), `NASS` (non assujetti, marge), `INTRA` (intracommunautaire),
> `EXP` (export hors UE), `EXO` (franchise) + variantes *caution*. C'est un **régime de premier rang**, pas une
> heuristique. Liakont mappe cette taxonomie ; l'adaptateur la **fournit** (proprement pour un système qui la
> porte ; **dérivée au mieux**, fail-closed, pour un legacy comme EncheresV6).

**La réalité du flux 10.3 (Dossier général DGFiP v3.2 §3.7, vérifié sur pièce).** Toute opération **B2C** —
domestique **comme internationale** — se transmet via le **bloc de données de transaction AGRÉGÉ (10.3)** (footnote
118 : « Les opérations auprès de non-assujettis (B2C) doivent être transmises via le bloc de données de transaction
(10.3) »). Le **10.1** (par-facture, **pays acheteur OBLIGATOIRE** TT-39) est réservé au **B2B international**. Le
bloc 10.3 (`transaction.xsd` TG-31/TG-32) ne porte **NI catégorie UNCL5305, NI VATEX** — uniquement :
`TT-77 Date · TT-78 Devise · TT-81 Catégorie de transaction · TT-82 HT · TT-83 TVA · TG-32{ TT-86 Taux · TT-87 Base · TT-88 TVA }`,
agrégé **jour × devise × type de transaction (TT-81)**. **Conséquence majeure** : en e-reporting B2C, seuls **TT-81
+ taux + base** sont transmis ; **aucun pays acheteur requis**. Les catégories **UNCL5305 (S/G/K/E) et les VATEX**
ne servent qu'(a) **en interne** au marquage/classification Liakont et (b) à la représentation **e-invoicing/
Factur-X** (par ligne) — **jamais dans le flux 10.3**.

**La cartographie (régime fiscal → UNCL5305 interne → `TT-81` → taux) :**

| Régime fiscal (groupe TVA) | Mention légale | UNCL5305 (interne / e-invoicing) | **`TT-81` (flux 10.3)** | Taux | Statut |
|---|---|---|---|---|---|
| **Prix total** (`ASS`) | — | `S` (+ taux) | **`TLB1`** | plein | ✅ §2.7 |
| **Marge** (`NASS`) | Art. 297-A | `E` + VATEX-EU-F/I/J | **`TMA1`** | 0 | ✅ §2.4/§2.5 |
| **Export hors UE** (`EXP`) | Art. 262-1 | `G` + VATEX-EU-G | **`TLB1`** | 0 | ✅ IMPLÉMENTÉ (BUG-11) |
| **Intracommunautaire** (`INTRA`) | Art. 262 Ter-1 | `K` + VATEX-EU-IC | **`TNT1`** | 0 | ✅ IMPLÉMENTÉ — validé PO (BUG-11) |
| **Franchise** (`EXO`) | Art. 275 | `G` (export-bound) | **`TLB1`** | 0 | ✅ IMPLÉMENTÉ — validé PO (BUG-11) |
| **Caution** (étape 1) | 262-1 / 262 Ter-1 | `S` (+ taux) | **`TLB1`** | **plein** | 🟧 cycle de vie, résiduel ④ |

Rôle déclarant **`SE`** pour tous (G7.52 — l'OVV reporte ses ventes ; cf. §2.5 ⑤). Le marquage plateforme dérive
la `TT-81` de la catégorie UNCL5305 interne (S→`TLB1`, E+VATEX marge→`TMA1`, `G`→`TLB1`@0, `K`→`TNT1`@0) — ce qui
**lève §2.6** pour chaque cas sourcé ci-dessous. Chaque régime exonéré exige son **propre marquage** (le marquage
taxable §2.7, `S` + `TotalTax>0`, n'attrape pas un exonéré à `TotalTax==0` ; ni le marquage marge `E`+VATEX-F/I/J) ;
fail-closed (ni marge, ni taxable, ni exonéré reconnu → bloqué).

**Export hors UE (`EXP`) → `TLB1` à 0 % — SOURCÉ.** La livraison de biens destinée à l'export reste **dans le champ**
de la TVA française (« soumise », G1.68 `TLB1`), **exonérée avec droit à déduction** (CGI **art. 262 I** — « TVA
récupérable ») : l'exonération annule la TVA **sans changer la nature** (livraison de biens) → `TLB1`, taux 0, base
= prix total HT (§270, comme §2.7 mais TVA 0 ; commission acheteur exonérée comme accessoire — `montant_tva_frais=0`
en source). UNCL5305 interne `G` + VATEX-EU-G, mention **Art. 262-1** (confirmé par le système de référence VPAuto).

**Intracommunautaire (`INTRA`) → `TNT1` — VALIDÉ PO (2026-06-26).** G1.68 nomme explicitement les **ventes à distance
intracommunautaires (art. 258 A)** dans `TNT1` (« non soumises **en France** », taxées au pays de destination,
guichet **OSS**). Mention **Art. 262 Ter-1**, catégorie interne `K` + VATEX-EU-IC → e-reporting B2C **`TNT1` à 0**
(taxe française nulle). La taxe à destination via **OSS** est une obligation distincte, **hors de ce flux 10.3** —
le `TNT1` à 0 est le report français correct (défaut défendable, refinable si l'OSS doit être représenté ici).

**Franchise (`EXO`, art. 275) → `TLB1` à 0 — VALIDÉ PO (2026-06-26).** « Achat en franchise » (CGI **art. 275**) :
l'acheteur achète **hors TVA** sous attestation, **en vue d'un export ultérieur** (« acheteur en France qui exporte
lui-même »). Opération détaxée, **export-bound** → traitée comme l'export 262 I : catégorie interne `G` →
e-reporting B2C **`TLB1` à 0**. Défaut défendable (refinable si une mention/catégorie 275 distincte est requise).
*(Cas des bordereaux EncheresV6 « `code_export=1` + `mode_livraison=FRANCE` » — 100132/100263.)* Aiguillage B2C/B2B
préservé : un acheteur à SIREN reste happé par le B2B (e-invoicing), jamais l'e-reporting B2C.

**Caution de TVA (export/intracom en 2 temps) → 🟧 future, `[À CONFIRMER EC]`.** Un système de production peut, quand
l'acheteur doit **prouver la sortie de territoire**, **encaisser la TVA en caution** (étape 1 : bordereau **taxable**
au taux plein → e-reporting **`TLB1` taux plein**) puis, **preuve reçue**, émettre un **avoir** (remboursement) + un
**bordereau final exonéré** (étape 2 → `TLB1`/`TNT1` à 0). L'e-reporting d'une caution = donc **une transaction
taxable PUIS un correctif** (avoir + final exonéré), net TVA = 0 (résiduel ④). **Hors périmètre immédiat** : les 6
exports EncheresV6 sont en **exonération DIRECTE** (0 % d'emblée, pas de caution) → cas simple. Le cycle
caution/avoir est à traiter quand l'adaptateur source le porte (VPAuto, entité `CountryExit`).

**Signal & dérivation du régime — par ADAPTATEUR (l'agent transporte le brut, aucune logique fiscale — n°6).**
- **Système portant la taxonomie (VPAuto)** : le **groupe de TVA** est un champ de premier rang → mappé directement
  (`EXP`→export, `INTRA`→intracom, `EXO`→franchise, `NASS`→marge, `ASS`→prix total).
- **Legacy EncheresV6** : pas de groupe TVA structuré → zone **dérivée** de `code_export` (LOGICAL, autoritaire pour
  « détaxé international ») + `mode_livraison` (`HORS CEE`→export, `CEE`→intracom, `FRANCE`→franchise). Le `code_regime_tva`
  domestique ne sert PAS à la classification détaxée (l'exonération internationale prime) ; il reste dans `SourceData`.

**Mécanique de clé par ZONE (mapping).** Le mapping est clé `(code régime, part)`, **une règle par couple** (§4.1,
TvaMapper). Hors export la clé est le `code_regime_tva` brut (`5` → `S`/plein) ; en export elle est la **ZONE seule**
(`EXP_HORSUE` → `G`/0 ; `EXP_CEE` → `K`/0 ; `EXP_FR` → `G`/0), dérivée de `code_export` + `mode_livraison`
(`RegimeKeyShape.Composite`). **L'exonération internationale primant sur le régime domestique** (262 I / 262 ter /
275), la clé ne dépend QUE de la zone → **3 règles** au lieu d'une par couple régime×zone (pas d'énumération
fragile). Le domestique `5 → S` reste inchangé (zéro régression). La catégorie/VATEX restent décidées par la
**table validée** (plateforme), jamais par l'agent ; le régime brut reste dans `SourceData` (audit).

**Résiduels (refinables, défauts défendables — PAS de blocage) :** ① **intracom OSS** (la taxe à destination d'un
VAD-IC B2C est un flux OSS distinct, hors du `TNT1` français à 0) ; ② **mention franchise 275** (mappée comme l'export
`G`/TLB1 — refinable si une catégorie 275 distincte est requise) ; ③ **caution** (cycle taxable→avoir→exonéré,
e-reporting multi-étapes — non rencontré : exports EncheresV6 en exonération directe) ; ④ **preuve de sortie**
(transportée, non vérifiée — charge à l'OVV) ; ⑤ **commission acheteur d'un détaxé** (ancrée exonérée, accessoire).

**Go/no-go (activation).** ✅ Déjà actifs : prix total (§2.7), marge (§2.4/§2.5). ✅ **IMPLÉMENTÉ (BUG-11)** :
**exonéré international → e-reporting B2C UNITAIRE** (`B2cExportReportingTenantJob` : une transaction `SE` au taux 0
PAR opération, base HT = adjudication + commission acheteur — jamais agrégé). Marquage `B2cExportMarking` (toutes
lignes UNE MÊME catégorie `G`/`K` + `TotalTax==0` + B2C + frais), aiguillage `B2cExportDeclaration` (exclu de la
marge par `!IsExportDeclaration`), **TT-81 dérivée de la catégorie** : export hors UE 262 I (`G`) → `TLB1` ; intracom
262 ter / 258 A (`K`) → `TNT1` ; franchise 275 export-bound (`G`) → `TLB1`. **Classification VALIDÉE PO (2026-06-26)**
— les trois zones sont ACTIVES (défauts défendables, refinables), jamais devinées par le code (la table tranche).
🟧 Hors périmètre : caution (cycle multi-étapes). Figeage PROD subordonné à la validation de la table par le tenant,
comme §2.4/§2.5/§2.7.

**Sources primaires :**
- CGI **262 I** (export), **262 ter I** / **258 A** (intracom / VAD-IC), **275** (franchise), **297 A** (marge),
  **256 V** (opaque, §2.7).
- **DGFiP Dossier général v3.2 §3.7** (flux 10.x ; footnote 118 : B2C → 10.3 ; 10.1 = B2B international) ;
  `transaction.xsd` **TG-31/TG-32** (10.3 sans UNCL5305/VATEX) ; **Annexe 7** **G1.68** (`TT-81`), **G7.52** (`SE`).
- **F03 §2.1/§2.2** (UNCL5305 `G`/`K` + VATEX-EU-G/IC), **§2.4/§2.5** (marge/`TMA1`), **§2.7** (prix total/`TLB1`).
- **Systèmes d'enchères** : EncheresV6 (`entete_ba.code_export` LOGICAL, `mode_livraison`, `Analyse-Donnees-V1-
  Mapping-TVA.md`) ; **VPAuto / open-auction** (groupes TVA `ASS/NASS/INTRA/EXP/EXO`, mentions 297-A/262-1/262 Ter-1/
  275, mécanisme de caution `CountryExit`) — **taxonomie cible**.

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
