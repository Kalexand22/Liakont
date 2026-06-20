# ADR-0004 — Périmètre du contrat d'extraction et du pivot V1 (anti-overfit)

**Date :** 2026-06-03

**Statut :** Accepté (2026-06-03)

---

## Contexte

Le pivot Liakont est **aligné sémantiquement sur EN 16931** (décision F01-F02 §2, option a).
Ce choix est correct et constitue en lui-même l'**anti-overfit** : le vocabulaire est
européen, stable, opposable, indépendant de la PA. Le risque résiduel n'est donc **pas** le
choix de cible, mais que le **contrat d'extraction `IExtractor`** et le **sous-ensemble V1**
du pivot aient été taillés sur la forme — particulièrement *propre* — de la base de
référence **EncheresV6** (le partenaire de conception, une SVV d'art sous Pervasive). Le
risque, formulé par le pilotage : faire un **product overfit sur Enchères SVV** et payer
chaque intégration suivante par un refactor du socle.

`IExtractor` existe déjà côté agent (`agent/src/Liakont.Agent.Core/IExtractor.cs`), mais
seulement comme **frontière nue** — une unique propriété `SourceName`, aucune logique (son
XML-doc précise que « l'extraction réelle arrive avec les items ADP »), et un adaptateur
`EncheresV6Extractor` de même nature. Le **contrat d'extraction substantiel** (régime,
montants, lignes) et **`PivotDocument` n'existent pas encore**. C'est donc le moment de coût
minimal pour arbitrer le périmètre — avant que la frontière nue ne se remplisse.

### Panel de validation (profilage en lecture seule stricte — règle métier #5)

Cinq sources réelles de facturation, de familles techniques radicalement différentes, ont
été profilées en lecture seule stricte (métadonnées + échantillons, aucun écrit/verrou) :

| Source | Famille technique | Métier | Particularité structurante |
|---|---|---|---|
| **Réf.** (EncheresV6) | Pervasive/XPA | SVV / ventes d'art | Forme « propre » : flag facture/avoir, lien avoir→origine explicite, régime de la marge |
| **Source A** | ERP transport legacy (famille APLON) | Transport routier | **Tous les montants en `float`/`real`** ; multi-dossier ; écotaxe ; tiers expéditeur/destinataire |
| **Source B** | Logiciel de criée (horloge Aucxis) | Criée / vente au cadran | **Double flux + auto-facturation fournisseur** ; entête mono-ligne ; paiements absents |
| **Source C** | Microsoft Dynamics NAV | Ventes de véhicules | **Régime TVA = couple de posting groups** ; identité émetteur **en base** ; total non stocké |
| **Source D** | Axelor Open Suite (ORM moderne) | Ventes de véhicules | **Un même champ encode sens+type** ; **lignes multi-taxes** ; n° attribué à la ventilation |

_Les quatre sources de production sont anonymisées (règle métier #7) : seules les familles
techniques — des produits du marché — sont citées, jamais les raisons sociales, noms de bases
ou valeurs de configuration tenant. Les libellés « Source A/B/C/D » sont réutilisés dans tout
l'ADR._

Enseignement transversal : **les 4 sources de production confrontées à la référence cassent
chacune au moins une hypothèse du contrat** calibrée sur EncheresV6. Le pivot tient
sémantiquement ; le **contrat** et **~8 hypothèses de champ** sont à découpler.

> Détail probant : **trois des cinq sources sont des maisons de ventes** — la référence (art,
> Pervasive), la source C (véhicules, NAV) et la source D (véhicules, Axelor) — sur **trois
> socles techniques sans rien de commun**. Surajuster au *schéma* de la référence casserait
> donc même sur une autre SVV : preuve que l'overfit est au niveau du **contrat d'extraction**,
> pas du modèle métier. Et le régime de la marge, qu'on aurait pu croire propre à l'art, est
> présent dans les **trois** — il est *produit*, paramétrable par tenant, pas SVV-spécifique.

---

## Décision

### D1 — Le pivot V1 reste aligné EN 16931 (confirmé)

Aucun changement de cible. La généricité se gagne en **ne coupant pas** du sous-ensemble V1
les emplacements qu'EN 16931 fournit déjà (cf. D4), pas en s'éloignant de la norme.

### D2 — Étendre `ExtractorCapabilities` (symétrique à `PaCapabilities`)

Le blueprint pilote déjà le comportement côté PA par des **capacités déclarées**
(`PaCapabilities`), jamais par `if (pa is B2Brouter)` (règle métier #8). Côté source, le
**mécanisme existe déjà** mais reste embryonnaire : `ExtractorCapabilities` (contrat `IExtractor`,
item AGT02) ne déclare aujourd'hui que les capacités **pièces jointes PDF**
(`ProvidesSourceDocuments` / `ProvidesUnlinkedDocumentPool`). On l'**étend** aux dimensions qui
varient réellement d'une source à l'autre : chaque adaptateur **déclare** ce que sa source sait
fournir, et la plateforme (Validation F4, Ingestion, Transmission) s'adapte aux capacités
déclarées — **jamais** par `if (source is NAV)`.

Capacités minimales à déclarer (issues du panel) :

- `HasDetailedLines` — la source porte des lignes détaillées (sinon : lignes synthétiques par
  taux) — *faux pour la source B (entête mono-ligne)*
- `HasCreditNoteLink` — l'avoir référence sa facture d'origine de façon fiable — *faux pour
  la source B (lien 100 % NULL), partiel pour les sources A et C (~63 %)*
- `ExposesPayments` — la source expose des encaissements datés — *faux pour la source B
  (tables vides), faible pour la source A*
- `RegimeKeyShape` — `Simple` (un code) / `Composite` (couple de codes) / `MultiplePerLine`
  (plusieurs taxes sur une même ligne) — *Composite pour la source C (NAV), MultiplePerLine pour la source D (Axelor)*
- `EmitterIdentitySource` — `InBase` / `FromConfig` / `DerivedFromVatNumber` — *FromConfig
  pour les sources A et D, InBase pour la source C, dérivé du n° TVA pour la source B*
- `HasStoredHeaderTotal` — total d'entête stocké et réconciliable — *faux pour la source C
  (FlowFields), ventilé par taux pour la source A*
- `IsMutableAfterIssue` — la source autorise la modification d'une facture émise (impacte R2)
- `NumberUniquenessScope` — granularité d'unicité du numéro (global, ou par établissement/série/année)

### D3 — Transformer 8 hypothèses « toujours X » en stratégies explicites

| # | Hypothèse EncheresV6 figée | Sources qui la cassent | Stratégie retenue | Grav. |
|---|---|---|---|---|
| 1 | `SourceRegimeCode` = **une string** par ligne suffit | Source C (couple Bus×Prod, un identifiant recouvrant 3 régimes) ; source D (ligne à 2 taxes) | **Régime = collection de N régimes par ligne** (brut, jamais interprété — règle #2) ; le moteur F3 mappe la collection, et scinde en plusieurs lignes pivot si EN 16931 l'exige (BG-30 = 1 catégorie/ligne) | **P1** |
| 2 | SIREN émetteur **vient de la config** | Source C (en base), source B (dérivé du n° TVA) | `EmitterIdentitySource` (D2) + normalisation (TVA→SIREN) | P2 |
| 3 | facture/avoir distinguables + lien origine fiable | Sources B, A, C (partiel) | Adaptateur transmet un `SourceDocumentKind` **brut** ; la **classification** vit dans Validation ; **avoir sans origine résoluble → bloquer, ne pas deviner** (règle #3) | **P1** |
| 4 | document = lignes détaillées (desc/qté/PU) | Source B (entête mono-ligne) | `HasDetailedLines` (D2) → sinon lignes synthétiques par taux, description dérivée | P2 |
| 5 | total d'entête stocké, réconciliable à Σ lignes | Source C (absent), source A (ventilé) | `SourceTotalGross` **optionnel** ; F4 tolère « pas de total » et « total ventilé par taux » | P2 |
| 6 | un seul sens émetteur→client | Source B (auto-facturation) | Distinguer *Invoicer* / *Seller* (EN 16931) + `Payee` ; flag auto-facturation | **P1** |
| 7 | montants déjà en `decimal` | Source A (`float`/`real`) | **Conversion `float`→`decimal` half-up 2 déc. obligatoire et testée** à la frontière adaptateur (règle #1) ; original conservé en `SourceData` | **P1** |
| 8 | la TVA est la seule taxe | Source A (écotaxe), source C (marge/encaissement) | Charges/taxes niveau document (EN 16931 BG-21) ; régimes spéciaux en catégorie propre | P2→P1 si dans la base TVA |

**Affinements issus du 5e point de contrôle (source D, Axelor) :**

- **Régime = collection par ligne, pas couple** (renforce la stratégie #1) : un ORM propre
  porte des lignes à 2 taxes (marge + total ≈ 40 %), et la marge est encodée dans le *libellé*
  du code. L'adaptateur passe la collection **brute** ; l'interprétation est centrale (règle #2).
- **Une colonne source peut encoder plusieurs axes pivot** : sur la source D (Axelor), un même
  champ fusionne *sens commercial* (achat/vente) **et** *type de pièce* (facture/avoir).
  L'extracteur doit pouvoir **projeter 1 colonne → N champs pivot** par une table de
  correspondance **déterministe et documentée** (≠ heuristique métier, qui reste interdite
  côté adaptateur — R3/R4). À distinguer du `SourceDocumentKind` brut de la stratégie #3.

### D4 — Périmètre opposable : matrice cas limites × décision

Au-delà du panel, la longue traîne des logiciels de facturation. **Règle de tri :** tout ce
qui touche la **correction fiscale** entre dans le pivot V1 (EN 16931 a déjà le slot) ; tout
ce qui touche la **forme de la source** ou la **nature du flux** passe derrière une capacité
déclarée ou est différé (mais **réservé** dans le contrat).

Décision ∈ { **Pivot V1** · **Capability** · **Contrat IExtractor** · **Différé (réservé)** }.

#### Famille 1 — Fiscal/structurel (EN 16931 le modélise → ne pas couper du V1)

| Cas limite | Slot EN 16931 | Décision | Grav. |
|---|---|---|---|
| Acompte + montant prépayé + chaînage acompte→solde | BT-113 | **Pivot V1** | **P1** |
| Autoliquidation (reverse charge — BTP, intra-UE, import) | catégorie `AE` | **Pivot V1** | **P1** |
| Régime de la marge (occasion, art, agences de voyage) | catégorie + VATEX | **Pivot V1** | **P1** |
| Débours (art. 267 — hors base TVA) | ligne hors base | **Pivot V1** | **P1** |
| Franchise en base (art. 293 B) | catégorie `E` + VATEX | **Pivot V1** | **P1** |
| TVA sur encaissements vs débits (fait générateur) | mention + timing flux 10.4 | **Pivot V1** | **P1** |
| Multi-taux / DOM / Corse / taux particuliers | référentiel de taux étendu | **Pivot V1** | **P1** |
| Éco-contributions / taxes parafiscales (non-TVA) | charges niveau doc BG-21 | **Pivot V1** | P1 si base TVA |
| Devise étrangère + taux de change (TVA exprimée en EUR) | — (BT-111/BT-6 non introduits) | **Hors V1 — EUR-only (verrou bloquant)** : cibles produit FULL EURO (décision Karl 2026-06-19, RD408) ; document non-EUR BLOQUÉ en Validation, pas de gestion multi-devises | P3 (verrou) |
| Remises/escomptes/charges niveau document | BG-20 / BG-21 | **Pivot V1** | P2 |
| Écart d'arrondi ligne vs global (toléré) | BT-114 | **Pivot V1** (régler F4) | P2 |

#### Famille 2 — Extraction (problème de source, pas de format)

| Cas limite | Décision | Grav. |
|---|---|---|
| Source mutable après émission (casse l'idempotence R2) | **Contrat** : stratégie de gel (hash de contenu / gel à la transmission) | **P1** |
| Brouillon / non comptabilisé mêlé aux factures validées | **Capability** : gate « document finalisé » | **P1** |
| Suppression / annulation post-extraction d'une facture déjà transmise | **Contrat** + procédure de rapprochement/alerte | P2 |
| Numérotation multi-séquence / multi-POS / reset annuel | **Contrat** : clé d'idempotence composite (`NumberUniquenessScope`) | P2 |
| Sémantique cachée dans un champ libre (ex. origine d'avoir en texte) | **Contrat** : transcription brute, **inférence interdite côté adaptateur** (R3/R4) | P2 |
| Sentinelles / NULL / encodage legacy (`1901-01-01`, EBCDIC) | **Contrat** : normalisation robuste | P2 |
| Total absent ou ailleurs (FlowFields, grand livre) | **Capability** `HasStoredHeaderTotal` | P2 |
| Détail non rattaché à la facture | **Capability** `HasDetailedLines` → lignes synthétiques par taux | P2 |
| Backfill historique massif | **Contrat** : streaming, pagination, reprise sur incident (R8) | P2 |

#### Famille 3 — Modèle économique (change la nature du flux)

| Cas limite | Décision | Grav. |
|---|---|---|
| Auto-facturation / facturation pour compte de tiers | **Capability** + *Invoicer/Seller*/`Payee` (D3-6) | **P1** |
| Caisse / POS retail B2C agrégé (flux 10.3, Z journalier) | **Capability** `FlowKind` (facture unitaire / e-reporting agrégé) | P1 |
| Affacturage (paiement à un tiers `Payee`) | **Pivot V1** : `Payee` (BG-10) | P2 |
| Marketplace / plateforme tripartite | **Différé (réservé)** | — |
| Abonnement / usage (SaaS metered, lignes calculées) | **Différé (réservé)** | — |
| B2G (Chorus Pro : code service + engagement juridique) | **Différé (réservé)** | — |
| BTP : situations de travaux cumulatives, retenue de garantie | **Différé (réservé)** | — |
| Tiers payant (payeur ≠ destinataire — santé) | **Différé (réservé)** | — |

##### Suivi RD406 (redline 2026-06-19) — concrétisation des « Différé (réservé) » et de la capacité `FlowKind`

La Conséquence §5 (« ce qui reste hors V1 est *réservé*, pas oublié ») était **normative mais non
tenue** pour la Famille 3 : la capacité `FlowKind` était décrite mais absente du contrat, et les cas
« Différé (réservé) » n'avaient **aucun emplacement nommé**. RD406 tranche **par cas** (CLAUDE.md n°2 :
aucune règle inventée — uniquement des slots réservés inertes, sans logique métier) :

| Cas Famille 3 | Décision RD406 | Emplacement réservé (inerte V1) |
|---|---|---|
| POS B2C agrégé (`FlowKind`) | **(a) slot capacité réservé** | `ExtractorCapabilities.FlowKind` (défaut `UnitInvoice`) |
| Abonnement / usage (période) | **(a) champ optionnel réservé** | `PivotDocumentDto.InvoicePeriod` (EN 16931 BG-14 : BT-73/BT-74) |
| Marketplace / plateforme tripartite | **(b) rupture assumée à V-next** | — (modèle tripartite hors EN 16931 cœur ; pas de slot d'extraction présumable) |
| B2G (Chorus Pro : code service + engagement) | **(b) rupture assumée à V-next** | — (donnée de **transmission/acheteur**, portée par le plug-in PA Chorus Pro + le profil tenant, **pas** extraite de la source ; réserver un BT dans le contrat d'extraction risquerait une correspondance fausse — CLAUDE.md n°2) |
| BTP : retenue de garantie / situations cumulatives | **(b) rupture assumée à V-next** | — (mécanisme financier sans slot EN 16931 unique ; décision source à sourcer) |
| Tiers payant (payeur ≠ destinataire) | **(b) rupture assumée à V-next** | — (santé ; hors cibles produit V1) |

**(a)** = un futur connecteur est un **ajout** (le slot existe déjà, nul par défaut, hash canonique
inchangé tant qu'il est absent — prouvé par test). **(b)** = aucun slot d'extraction réservé : le cas
est une **rupture assumée**, ré-arbitrée à V-next quand une source réelle l'exige (la cohérence ADR ↔
contrat est rétablie : §5 ne promet plus un slot pour ces cas). Aucun de ces slots n'est consommé en V1.

---

## Conséquences

1. **Coût quasi nul, maintenant.** Tout ceci est arbitré **avant** la première classe pivot.
   Reporter, c'est transformer chaque arbitrage en refactor du socle + risque de donnée
   fiscale fausse en production.
2. **Impacts en cascade à intégrer dans les specs concernées :**
   - **F03 (mapping TVA)** : le moteur F3 doit accepter une **collection de régimes par
     ligne** (composite ou multi-taxes), pas une simple string, et pouvoir scinder en
     plusieurs lignes pivot. Catégories `AE`, marge, franchise en base à couvrir dès V1.
   - **F04 (validation)** : tolérer « pas de total d'entête » et « total ventilé par taux » ;
     tolérance d'arrondi ligne/global ; **avoir orphelin → blocage** (jamais deviner).
   - **F01-F02 (contrat)** : ajouter `ExtractorCapabilities`, `SourceDocumentKind` brut,
     distinction *Invoicer/Seller*, `Payee`, montant prépayé, charges niveau document,
     stratégie de gel/idempotence, conversion `float`→`decimal` obligatoire.
   - **F09 (e-reporting/paiement)** : `ExposesPayments` peut être faux ; flux 10.4
     conditionné à la capacité, pas présumé.
3. **Chaque adaptateur futur = déclaration de capacités + couche de normalisation** (devise
   vide→EUR, pays→ISO 3166-1 alpha-2, TVA→SIREN), **pas** un refactor du socle. C'est la
   bascule « refactor » → « plug-in ».
4. **Renforcement des règles métier non négociables**, pas affaiblissement : #1 (decimal),
   #2 (aucune règle inventée — les régimes spéciaux viennent de la table validée par
   l'expert-comptable du tenant), #3 (bloquer plutôt qu'envoyer faux — avoir orphelin),
   #8 (capacités déclarées) sont **étendues à la frontière d'extraction**.
5. **Ce qui reste hors V1 est réservé, pas oublié.** Les `Différé (réservé)` exigent un
   emplacement nommé dans le contrat (capacité ou champ optionnel) pour qu'un futur connecteur
   soit un ajout, jamais une rupture. **Tenu par RD406** (redline 2026-06-19, voir le tableau
   « Suivi RD406 » sous la matrice Famille 3) : les cas où un slot d'extraction est présumable
   et standard sont **réservés maintenant** (`FlowKind`, `InvoicePeriod` BG-14 — inertes, hash
   inchangé) ; les cas sans slot d'extraction présumable (marketplace, B2G code service, BTP,
   tiers payant) sont requalifiés en **rupture assumée à V-next** — §5 ne promet plus un slot
   pour eux, ce qui rétablit la cohérence ADR ↔ contrat.

---

## Alternatives rejetées

- **Figer le contrat sur la forme EncheresV6 (statu quo).** Rejeté : les 4 sources de
  production le cassent déjà ; chaque intégration deviendrait un refactor du socle —
  exactement l'overfit à éviter.
- **Adopter EN 16931 EXTENDED + XP Z12-013 comme modèle interne dès V1.** Rejeté : déjà
  tranché en F01-F02 (option c) — sur-ingénierie V1, payload XP Z12-013 non figé. La
  sérialisation XP Z12-013 reste une **sortie** future, pas le modèle interne.
- **Capacités côté PA uniquement (asymétrie actuelle).** Rejeté : l'asymétrie *est* le trou.
  La frontière d'extraction est aussi variable que la frontière PA et mérite la même
  discipline de capacités déclarées.

---

## Références

- `docs/conception/F01-F02-Modele-Pivot-Contrat-Extraction.md` (pivot + contrat `IExtractor`)
- `docs/conception/F03-Mapping-TVA.md` · `F04-Controles-Qualite-Validation.md` ·
  `F07-F08-Avoirs-Frontiere-B2B-B2C.md` · `F09-E-Reporting-Paiement.md`
- `blueprint.md` §2 et §6 (frontières de la généricité), §8 (capacités déclarées PA)
- `docs/adr/ADR-0001-pivot-plateforme-agent.md` (pivot plateforme + agent)
- Panel de validation (bases locales, hors dépôt — données client, **anonymisées ici**
  conformément à la règle métier #7) : référence EncheresV6 + 4 sources de production de
  familles distinctes (ERP transport legacy/float, logiciel de criée Aucxis, Microsoft
  Dynamics NAV, Axelor Open Suite) — profilées en lecture seule stricte le 2026-06-03.
