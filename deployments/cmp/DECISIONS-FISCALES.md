# Décisions fiscales — déploiement CMP (Crédit Municipal de Paris)

> **Statut : EN ATTENTE — Expert-comptable CMP.**
> Tant que ces questions ne sont pas tranchées et que la table de mapping
> [`tva-mapping-cmp-v1.json`](tva-mapping-cmp-v1.json) n'est pas validée/horodatée (`validatedBy` +
> `validatedDate`), elle reste **NON VALIDÉE** et le garde-fou d'envoi (PIP01b) **suspend tout envoi
> réel** à la Plateforme Agréée. La mise en production (`GATE_PROD_CMP`) est conditionnée à cette
> validation.

Ce document liste les questions fiscales ouvertes du déploiement CMP, leur **impact** sur le
paramétrage, et l'**espace pour consigner la réponse** de l'expert-comptable. L'agent d'orchestration
ne tranche **aucune** de ces questions (CLAUDE.md n°2) : il matérialise l'état des connaissances de
`docs/conception/F03-Mapping-TVA.md`. Toute réponse ci-dessous doit être **sourcée** (texte CGI,
position de l'expert-comptable, ticket support B2Brouter) avant d'être reportée dans le seed.

---

## Question 1 — Régime 6 (« non assujetti ») : régime de la marge ou hors champ ?

- **Contexte (F03 §3, §6 décision #1).** Le régime 6 produit une adjudication « sans TVA apparente ».
  Deux qualifications juridiques **différentes** aboutissent à 0 % :
  - **régime de la marge** (art. 297 A CGI) : la TVA porte sur la marge du revendeur, jamais distincte
    sur le bordereau (art. 297 E) → catégorie **E** + **VATEX-EU-J** (objets de collection/antiquité),
    ou EU-F (biens d'occasion) / EU-I (œuvres d'art) selon la nature du bien ;
  - **hors champ** (vendeur particulier non assujetti) → catégorie **O** (hors champ d'application).
- **Piège confirmé (F03 §3).** En pratique des enchères, « vendeur non assujetti » est *souvent le
  déclencheur même* du régime de la marge — un libellé « non assujetti » peut correspondre fiscalement
  à une vente au régime de la marge. Se tromper = mauvais motif d'exonération transmis à la DGFiP.
- **Impact paramétrage.** Détermine la catégorie/VATEX des règles `(régime 6, Adjudication)` du seed.
  La valeur actuellement présente dans `tva-mapping-cmp-v1.json` est **VATEX-EU-J**, mais c'est un
  simple **PLACEHOLDER chargeable** (la catégorie E *exige* un code VATEX non nul — sinon la table est
  rejetée), **PAS une recommandation**. Le code correct dépend de la **nature du bien vendu** (F03 §2.3
  « EU-F/I/J selon la nature du bien ») :
  - **EU-F** — biens d'occasion (bijoux, montres, métaux précieux issus de gages — fréquents pour un
    mont-de-piété) ;
  - **EU-I** — œuvres d'art ;
  - **EU-J** — objets de collection et d'antiquité.
  Pour un mont-de-piété, **EU-F** (biens d'occasion) est au moins aussi plausible qu'EU-J : à trancher
  selon la nature réelle des biens adjugés. Alternative : **hors champ** (catégorie O) si le vendeur
  particulier non assujetti ne relève pas du régime de la marge.
- **Réponse expert-comptable CMP :** _(à compléter — préciser : (a) marge vs hors champ ; (b) si marge,
  le VATEX exact selon la nature des biens : EU-F biens d'occasion / EU-I œuvres d'art / EU-J
  collection-antiquité)_
- **Source :** _(à compléter)_  **Date :** _(à compléter)_

## Question 2 — TVA sur les débits ou sur les encaissements ?

- **Contexte.** Le paramètre fiscal du tenant `vatOnDebits` (profil — voir CMP02 `tenant-cmp.json`,
  laissé `null` en attente) détermine le fait générateur de la TVA pour les prestations de services
  (frais acheteur notamment). Sur les débits = exigibilité à la facturation ; sur les encaissements =
  exigibilité au paiement.
- **Impact paramétrage.** Influe sur le moment de l'exigibilité et, en aval, sur l'e-reporting de
  paiement (F09). Reste `null` tant que non tranché (le produit ne devine jamais — F12-A §3.1).
- **Réponse expert-comptable CMP :** _(à compléter — débits / encaissements)_
- **Source :** _(à compléter)_  **Date :** _(à compléter)_

## Question 3 — Catégorie d'opération (`OperationCategory`)

- **Contexte.** Le paramètre `operationCategory` du profil tenant qualifie l'activité (livraison de
  biens / prestation de services / mixte) et conditionne certaines règles de l'e-invoicing/e-reporting.
  Pour une vente aux enchères, l'adjudication (bien) et les frais acheteur (service) peuvent relever de
  qualifications distinctes.
- **Impact paramétrage.** Renseigne `operationCategory` dans `tenant-cmp.json` (laissé `null` en
  attente). Une valeur erronée peut fausser le profil EN 16931 transmis.
- **Réponse expert-comptable CMP :** _(à compléter)_
- **Source :** _(à compléter)_  **Date :** _(à compléter)_

## Question 4 — Volume / présence d'acheteurs professionnels (B2B)

- **Contexte.** Le CMP vend-il à des **acheteurs professionnels identifiés** (SIREN/TVA intra) ? Si oui,
  ces ventes relèvent de l'e-invoicing B2B (facture électronique via PA), distinct de l'e-reporting B2C.
  Le pipeline distingue les deux selon l'identification de l'acheteur (capacités PA `SupportsB2bInvoicing`).
- **Impact paramétrage.** Détermine si le compte PA doit gérer le flux B2B et la couverture des cas de
  démo (acheteur pro bloqué/accepté). Sans réponse, le défaut est B2C ; un acheteur identifié comme
  professionnel est traité selon les capacités du plug-in PA configuré.
- **Réponse expert-comptable CMP :** _(à compléter — proportion estimée d'acheteurs pros, flux B2B
  attendu)_
- **Source :** _(à compléter)_  **Date :** _(à compléter)_

---

## Validation finale de la table

Une fois les questions 1 à 4 tranchées et reportées dans `tva-mapping-cmp-v1.json` :

1. Compléter `validatedBy` (nom de l'expert-comptable) **et** `validatedDate` (date de validation) dans
   `tva-mapping-cmp-v1.json` → la table devient **VALIDÉE** (`IsValidated = true`).
2. Compléter les régimes éventuellement manquants découverts par `check-config` sur la base réelle CMP
   (F03 §4.3) — une règle **explicite par régime source et par part**, jamais de joker `*` (refusé).
3. Renseigner les paramètres fiscaux du profil (`vatOnDebits`, `operationCategory`) dans
   `tenant-cmp.json`.
4. La levée du garde-fou d'envoi et la mise en production restent conditionnées à `GATE_PROD_CMP`.
