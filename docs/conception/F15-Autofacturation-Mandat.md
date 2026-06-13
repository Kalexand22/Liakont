# F15 — Autofacturation sous mandat (type 389)
### Note de conception — module `Mandats`, capacités A (émission 389) & B (réception/rapprochement)

> Statut : 🟨 **NOTE D'ORIENTATION** (2026-06-13), rédigée en sessions interactives à partir des pistes de
> `C:\Source\Liakont-GoToMarket`. **Ce document n'est PAS une spec figée** : il grave ce qui est sourcé sur
> texte primaire et empiriquement vérifié, et marque explicitement les points **`❓ NON TRANCHÉ`** (décision
> expert-comptable / re-sourçage / confirmation terrain). Aucune règle fiscale n'y est inventée (CLAUDE.md n°2) :
> tout point sensible est soit cité (source + URL), soit marqué non tranché. Aucun code n'est écrit à ce stade.
>
> Légende : ✅ source primaire vérifiée (citation + URL, re-vérifiée) · 🔶 établi mais à reconfirmer · ❓ décision/zone ouverte.
>
> **Sources primaires vérifiées le 2026-06-13** (fetch + re-vérification indépendante) : Légifrance (CGI),
> BOFiP (bofip.impots.gouv.fr), Peppol/UNECE (UNTDID 1001), specs externes DGFiP V3.2 (archive officielle
> `specifications-externes-v3.2.zip`, impots.gouv.fr, extraite et lue localement). **Source terrain** : base
> SQL `Kerport_Fact` (criée de Keroman, moteur LOGIC/Bucodi), lue en **SELECT / READ UNCOMMITTED uniquement**
> (règle n°5 — lecture seule stricte de la base source).

---

## 0. Objet et périmètre

L'**autofacturation sous mandat** (BT-3 = 389, art. 289 I-2 CGI) est le **socle commun** de tout le portefeuille
verticales (enchères, criées, paie de lait, semences, intégration, RFA, droits d'auteur). Elle se décline en
**deux capacités produit** :

- **Capacité A — Émission de la facture sous mandat (389)** : générer la facture *au nom et pour le compte du
  vendeur (mandant)*, depuis la base legacy en lecture seule. **Périmètre de F15.**
- **Capacité B — Réception + rapprochement des factures fournisseurs** : lire le flux entrant de la PA et
  confronter la facture reçue au décompte interne. Esquissée ici (§6), conçue à part (phase 2 du blueprint §13).

Le pivot porte **déjà** `Invoicer` (émetteur ≠ vendeur) et `IsSelfBilled` au contrat
(`Liakont.Agent.Contracts/Pivot/PivotDocumentDto.cs`, ADR-0004), écrits par le sérialiseur canonique et lus par
le pipeline — **mais aucun sérialiseur PA ne projette le 389 et aucune `PaCapability` ne le pilote**. Le socle de
données existe ; le comportement est à construire.

---

## 1. Référentiel fiscal sourcé (texte primaire vérifié)

> Tout ce tableau a été récupéré et re-vérifié sur source officielle le 2026-06-13. **L'interprétation produit
> (le mapping, le rattachement de catégorie) reste une décision de l'expert-comptable du tenant** — F15 fournit
> le texte, pas la décision.

### 1.1 ✅ Le mandat d'auto-facturation — CGI art. 289, I.2

> « **Les factures peuvent être matériellement émises par le client ou par un tiers lorsque l'assujetti leur
> donne mandat à cet effet. Sous réserve de son acceptation par l'assujetti, chaque facture est alors émise en
> son nom et pour son compte.** »
> — Légifrance, CGI art. 289 (LEGIARTI000048827413), en vigueur au 2026-06-13.

Conséquence : le **vendeur reste l'émetteur juridique** ; le mandataire (le tenant) n'est que l'émetteur
matériel. Deux conditions du texte lui-même : (1) un **mandat** donné par l'assujetti, (2) l'**acceptation** de
la facture par l'assujetti.

### 1.2 ✅ Le code 389 = « facture auto-facturée »

> « **389 — Self-billed invoice — An invoice the invoicee is producing instead of the seller.** »
> — UNTDID/UNCL 1001 (sous-ensemble retenu pour BT-3 par EN 16931 / Peppol BIS Billing 3.0).
> URL : `https://docs.peppol.eu/poacc/billing/3.0/codelist/UNCL1001-inv/` (confirmé UNECE D.98A).

### 1.3 ✅ Le 389 relève du **SOCLE de démarrage** (pas du profil EXTENDED) — specs DGFiP V3.2

Lecture directe de l'**Annexe 1 « Format sémantique FE e-invoicing — Flux 1 » v1.2 (30/04/2026)**, extraite de
l'archive officielle. Le modèle a deux axes de profil : colonne **Trajectoire** (`DEMARRAGE` = socle dès 09/2026 /
`CIBLE` = ultérieur) et colonnes **Profil XSD `Base`** vs **`Full`**.

La ligne **BT-3 « Code de type de facture »** : `Liste = UNTDID 1001` · `Trajectoire = DEMARRAGE` · `Profil Base = X`
· `Profil Full = X`, avec la note EN 16931 reprise par la DGFiP :

> « Les factures commerciales et les notes de crédit sont définies selon les entrées issues de la liste UNTDID
> 1001. **Les autres entrées de la liste UNTDID 1001 concernant des factures ou des notes de crédit spécifiques
> peuvent être utilisées, le cas échéant.** »

De plus, **aucune extension `EXT-FR-FE`** de l'annexe ne concerne l'autofacturation / le « tiers facturant » : les
extensions françaises portent seulement sur les références d'acompte en ligne, le multi-livraison et les
dates/adresses de livraison à la ligne (toutes en `CIBLE` + Profil `Full` uniquement).

**→ Acquis (✅) :** le champ BT-3 et la valeur 389 sont en Trajectoire `DEMARRAGE` et présents dans **les deux
profils XSD (Base et Full)** — le code 389 n'est donc **pas réservé au profil EXTENDED-CTC-FR**. *(Extrait
re-vérifiable : archive `specifications-externes-v3.2.zip` → `Annexe 1 — Format sémantique FE e-invoicing —
Flux 1 - v1.2.xlsx`, onglet « FE - Flux 1 - UBL », ligne BT-3.)*

**→ Conséquence opérationnelle (🔶, à confirmer) :** déposer une auto-facture 389 serait alors un cas pleinement
supporté du socle dès le 1ᵉʳ septembre 2026 (et `SupportsSelfBilling` d'une PA agréée tendrait vers `true`),
**sous réserve de la lecture de l'Annexe 7 (BR-FR-CTC)** qui pourrait ajouter des contrôles Schematron / mentions
exigées propres au 389 (cf. §6.1). La zone grise résiduelle de l'autofacturation est de toute façon ailleurs
(statuts inversés vers un mandant sans PA, décomptes complexes), pas dans le code 389 lui-même.

### 1.4 🔶 Numérotation sous mandat — séquence distincte par mandant (BOI-TVA-DECLA-30-20-20-10)

> §120 : « Lorsque les factures afférentes aux opérations réalisées par un assujetti sont matériellement établies
> par un mandataire (client ou tiers), **la séquence de numérotation utilisée par ce mandataire, qui doit être
> chronologique et continue, doit être propre à l'assujetti concerné.** »
> §130 : « […] **le mandataire utilise une séquence de facturation chronologique et continue distincte pour chacun de
> ses mandants.** Il peut, à cet effet, **faire précéder chaque numéro de facture d'un préfixe propre** à chacun
> des assujettis qui lui a donné mandat pour établir ses factures. »
> — BOFiP BOI-TVA-DECLA-30-20-20-10 (version 18/10/2013, à reconfirmer en version courante).

### 1.5 🔶 Acceptation / contestation — mandat tacite vs écrit (BOI-TVA-DECLA-30-20-10)

> §290 : « Lorsque **le mandat de facturation est tacite**, toute facture émise au nom et pour le compte du
> fournisseur devra faire l'objet d'une **acceptation formelle et expresse** par ce dernier. »
> §300 : « Les factures émises au nom et pour le compte du fournisseur dans le cadre d'un **contrat de mandat écrit
> et préalable** n'ont pas à être formellement authentifiées par celui-ci. »
> §390 : « Le contrat de mandat doit, en outre, expressément **stipuler le délai accordé au mandant pour contester**
> le contenu des factures émises en son nom et pour son compte. »
> — BOFiP BOI-TVA-DECLA-30-20-10 (version citée 12/09/2012 ; le re-fetch 2026-06-13 rend la version courante
> 13/08/2021 — à reconfirmer). Le délai de contestation est, selon les modalités d'acceptation du BOFiP,
> librement déterminé par les parties (donnée du contrat de mandat, jamais un défaut produit — cf. §6.4).

**→ Règle structurante :** la logique « acceptation tacite par non-contestation au-delà d'un délai » n'existe
**que sous mandat écrit et préalable**. Sous mandat tacite, **chaque** facture exige une acceptation expresse.

### 1.6 ✅ Criée — relevé « tient lieu de facture » + exonération pêche

> BOI-TVA-CHAMP-30-10-60-10 §130 : « **Il est admis que les relevés de ventes établis par les gestionnaires des
> halles à marée et adressés aux pêcheurs et aux armateurs tiennent lieu de factures pour ces derniers** à
> condition que ces relevés fassent apparaître la date de la vente, la quantité, le prix, la nature des produits
> vendus et la dénomination précise de l'acquéreur. **Bien entendu, ce document ne doit pas mentionner la TVA
> puisque la vente en est exonérée.** » (le § ouvre par : « Ayant la qualité d'assujettis, les pêcheurs et les
> armateurs doivent délivrer une facture à leurs clients mareyeurs en application de l'article 289 du CGI. »).
> — version 09/07/2025.

> CGI art. 261-2-4° : « **les opérations effectuées par les pêcheurs et armateurs à la pêche**, à l'exception
> des pêcheurs en eau douce, **en ce qui concerne la vente des produits de leur pêche** (poissons, crustacés,
> coquillages frais ou conservés à l'état frais par un procédé frigorifique). » → **opération EXONÉRÉE** (pas un
> taux). *🔶 Énumération limitative → routage exonéré/taxé potentiellement lot par lot selon l'état du produit.*

### 1.7 ⚠️ RFA — art. 298 quater (taux 5,59 % / 4,43 %), abrogation au 1ᵉʳ septembre 2026

✅ L'art. 298 quater (remboursement forfaitaire agricole, taux 5,59 % / 4,43 %) est **abrogé par l'Ordonnance
n°2025-1247 du 17/12/2025 (art. 9), avec effet au 1ᵉʳ septembre 2026** — pile à l'échéance de la réforme.
**Nuance (à ne pas sur-affirmer) :** les sections **I bis (qui portent les taux) et III demeurent en vigueur à
titre transitoire** jusqu'à leur reprise par le futur code des impositions sur les biens et services. Tout design
touchant le RFA doit intégrer ce changement de régime.
❓ Le « bulletin d'achat » **ne figure pas** dans 298 quater (il relèverait d'un autre texte — art. 267 quater
annexe II au CGI / doctrine — à sourcer ; cf. §6.8).

---

## 2. Le module `Mandats` (capacité A)

### 2.1 Décision de structure — le mandant n'est PAS un tenant

`1 tenant = 1 client final qui paie = le mandataire-émetteur` (blueprint §7). Le **mandant-vendeur** (livreur de
lait, multiplicateur, armement de pêche, commettant d'enchères) est un **tiers récurrent** du tenant, **jamais un
sous-tenant**. Il vit dans un **nouveau module `Mandats`**, schéma `mandats` en base tenant (database-per-tenant),
exposé aux autres modules uniquement par ses `Contracts` (NetArchTest). **À acter en ADR (à créer).**

Le module copie les patterns réels du repo :
- **paramétrage journalisé + revalidé** sur le modèle `TvaMapping` (validation humaine `ValidatedBy/ValidatedDate`,
  `Invalidate()` à chaque mutation → 389 suspendu, mutation + journal append-only dans la **même transaction**,
  immuabilité du journal par **trigger base**, jamais par du code) ;
- **statut/délai NULLABLE = suspendu** sur le modèle `FeeImputationMethod` (jamais de défaut inventé) ;
- **job multi-tenant** `TenantJobRunner` (ADR-0006/0016) pour l'acceptation tacite, jamais une boucle maison ;
- **file mutable** sur le modèle `ReconciliationQueueEntry` pour l'écran de contestation.

### 2.2 Modèle de données (esquisse)

| Entité | Clé | Champs clés | Notes |
|---|---|---|---|
| `Mandant` | `(company_id, reference)` | `RaisonSociale`, `SellerVatNumber` (BT-31, nullable), `Siren`, `NumberingPrefix` (paramétrage) | tiers récurrent, tenant-scopé |
| `Mandat` | `(company_id, mandant_id, reference)` | `ClauseText`, `EstEcrit` (écrit/tacite — §1.5), `AssujettissementStatus?` (null = suspendu), `ContestationDelay?` (null = tacite impossible), `ValidatedBy/Date`, `RevokedDate` | calqué `MappingTable` |
| `MandatSequence` | `(company_id, mandant_id)` | `Prefix`, `NextValue` (bigint) | numérotation par mandant (§1.4) |
| `SelfBilledAcceptance` | `(company_id, document_id)` | `State`, `AllocatedNumber`, `PendingSince`, `DeadlineUtc?` | état mutable + journal append-only |

Journaux **append-only** (`mandat_change_log`, `self_billed_acceptance_log`) immuables par double trigger base
(UPDATE/DELETE/TRUNCATE rejetés, message FR citant CLAUDE.md n°4).

### 2.3 Workflow d'acceptation / contestation

Agrégat **distinct** `SelfBilledAcceptance` (machine fermée `PendingAcceptance → {Accepted, TacitlyAccepted,
Contested}`), **couplé à l'émission par un port `ISelfBilledGate`** : le pipeline interroge le gate avant l'envoi ;
tant que non accepté, le `Document` reste `Blocked` (nouveau motif, **aucun nouvel état dans `DocumentState`**).
**Ne pas étendre la machine d'émission** (à acter en ADR). Branchement sur §1.5 :
- **mandat tacite** (`EstEcrit = false`) → acceptation **expresse** requise pour chaque facture (pas de bascule tacite) ;
- **mandat écrit** (`EstEcrit = true`) → bascule `TacitlyAccepted` par le job au-delà de `DeadlineUtc =
  PendingSince + ContestationDelay`, tracée append-only ; si `ContestationDelay` null → tacite impossible, document bloqué.

La correction d'une auto-facture contestée se fait par **avoir + nouvelle facture** (jamais un état « annulé »).

### 2.4 Garde-fous (à tester)

Tenant-scoping strict (un mandant n'est jamais cross-tenant) · journaux append-only prouvés par test d'intégration
(UPDATE/DELETE/TRUNCATE rejetés) · `AssujettissementStatus`/`ContestationDelay`/mandat non validé/révoqué ⇒ 389
suspendu · machine d'acceptation = liste fermée (test produit cartésien) · frontières NetArchTest (Mandats exposé
par Contracts seulement ; aucune logique Mandat dans l'agent) · **aucune donnée client dans le code** (mandants/
préfixes/n° TVA réels = paramétrage tenant ou `deployments/<client>/` ; seuls exemples fictifs dans `config/exemples/`).

---

## 3. Le numéro BT-1 en 389 — allocation hybride

### 3.1 Le problème

Aujourd'hui le `Number` (BT-1) **vient de la source**, est obligatoire, et **ancre l'idempotence à trois niveaux**
(`payload_hash` d'ingestion, anti-doublon F06 `(supplier_siren, document_number)`, `external_id` PA — et SuperPDP
**déduplique au numéro**). Or §1.4 impose une **séquence chronologique distincte par mandant** ; un legacy n'a aucune
raison de la produire (preuve : Kerport_Fact numérote **globalement**, `F00172473`, pas par armement — §5).

### 3.2 Décision — l'hybride (à acter en ADR)

On **dédouble** ce que `Number` confond :
1. **Clé d'idempotence** = un **identifiant interne de la source**, **déjà présent dans le pivot et déjà hashé** :
   `SourceReference` (écrit inconditionnellement par `CanonicalJson`, à côté de `Number`). Le `payload_hash` reste
   **inchangé** — aucun format canonique conditionnel au flux.
2. **BT-1 fiscal 389** = **alloué par le module `Mandats`** (séquence par mandant), via un **`get-or-create` sur la
   clé source** : alloué une fois et mémorisé ; toute ré-extraction **relit le même numéro**, jamais ré-alloué.

**L'idempotence remplace l'atomicité.** Le P1 connu (allocation schéma `mandats` vs écriture schéma `documents` =
deux transactions, non atomisables à travers la frontière de module) est résolu non en cherchant l'atomicité, mais
en rendant chaque étape **idempotente et rejouable** sur une clé source stable — exactement comme l'écriture du
document l'est déjà (`DocumentId` + `ON CONFLICT (id) DO NOTHING`). 🔶 Le **moment d'allocation** (piste : au plus
tard, juste avant l'envoi après Validation, pour minimiser les trous) reste **conditionné à la tolérance des trous
de séquence (§6.7), non sourcée** — la chronologie « continue » exigée au §1.4 pourrait l'interdire et changer la stratégie.

**Re-clé anti-doublon F06 (🔶 piste, à acter)** : `(supplier_siren, document_number)` → `(tenant, mandant_id,
document_number)` quand le document est sous mandat (le « supplier » fiscal est le mandant). Piste : champ `MandatId`
additif dans `DuplicateCheckRequest` + prédicat neutralisant la clé SIREN sur un 389. **Conditionné à l'amendement
de F06 §4** (la clé fonctionnelle anti-doublon est une règle de F06 ; ne pas la modifier sans amender la spec —
point ouvert listé en §6.10).

### 3.3 Règle nette

**En flux 389, un numéro fourni par la source n'est JAMAIS le BT-1 fiscal** : il n'alimente que la clé d'idempotence
interne. Le BT-1 fiscal est toujours alloué plateforme. La garde d'ingestion n'est **pas levée** mais **remplacée**
pour le cas 389 : elle exige un **identifiant source interne NON NUL** à la place du `Number` — jamais d'acceptation
d'un document 389 sans clé d'idempotence (ce n'est donc **pas un affaiblissement de validation**, mais une
substitution d'invariant, à graver en ADR).

**Voie propre (corrige une fausse piste) :** ne **PAS** retirer `Number` du hash canonique pour le seul cas 389 — ce
serait un **format conditionnel au flux** qui casse la reproductibilité octet-par-octet et amenderait la règle
additive d'**ADR-0007** (`Number` est en position 2, **non optionnel** — `CanonicalJson.cs:45`). À la place : en 389,
`Number` **reste toujours rempli** (par l'identifiant source interne, déjà porté par `SourceReference`), et le
**BT-1 fiscal alloué par mandant est une valeur SÉPARÉE, hors du payload hashé** (assignée à l'émission, côté
plateforme). Aucun branchement de format ; ADR-0007 préservé.

**Garde-fous à graver dans l'ADR fille BT-1/acceptation (avant tout code) :** (i) la re-clé F06 bascule
**atomiquement** vers `(mandant_id, document_number)` côté SQL (index d'unicité + tests « deux 389 même numéro /
mandants ≠ → Send ; même mandant / ré-extraction → Blocked ») — **ne pas** seulement « neutraliser le SIREN » (sinon
fenêtre sur un 389 en attente ré-extrait) ; (ii) « chronologique » **et** « continue » (§1.4) sont **deux invariants
distincts** (ordre cohérent vs sans trou — l'ordre n'est pas garanti sous allocation concurrente d'un même mandant) ;
(iii) bascule `TacitlyAccepted` **uniquement si `EstEcrit = true` ET `ContestationDelay` non null** (invariant testé).

---

## 4. La criée — flux réel et conséquence mono-Seller

### 4.1 ✅ Constat terrain (base `Kerport_Fact`, lecture seule)

| Jambe | Table | Volume | Vendeurs/document | Verdict |
|---|---|---|---|---|
| **Vendeur (relevé producteur)** | `TBL_HEADER_VES_FACT` | 52 879 factures | **1 navire — 100 %** | **mono-Seller strict** |
| **Acheteur (bordereau mareyeur)** | `TBL_HEADER_BUY_FACT` | 346 125 factures | 6,41 en moyenne, jusqu'à 116 | multi-vendeurs |

Chaque lot (`TBL_TRANSACTIONS`) est facturé une fois côté navire (`tra_ves_fact`) et une fois côté acheteur
(`tra_buy_fact`). La TVA/exonération est taguée **au lot** (`tra_vat_code`, `tra_NoTax_amount`).

### 4.2 Conséquence produit

La criée **produit déjà** un **décompte mono-Seller par producteur** (par vacation) : on l'émet en **389 standard**
(Seller = armement, émetteur matériel = halle). **Pas d'éclatement, pas de blocage multi-vendeurs nécessaire côté
émission** — c'est la **seule résolution affirmée ici** : on n'affirme que la **structure** mono-Seller (confirmée
terrain). Le **statut TVA** se lit **au lot** (`tra_vat_code`) — l'exonération 261-2-4° ne vaut que pour les produits
« frais ou conservés frais » (§1.6) et le **routage exonéré/taxé lot par lot reste NON TRANCHÉ (§6.6)** (un lot
transformé/cuit n'est pas exonéré). Le multi-vendeurs
vit **côté acheteur** (bordereau du mareyeur) = jambe **réception (capacité B)**, dont **le statut fiscal reste
ouvert (§6.2)** ; **aucun « document fiscal multi-vendeurs » à modéliser côté émission**. La scission du flux inverse
« taxe de criée » (service halle→pêcheur taxé 20 % précompté sur le même relevé exonéré) reste à modéliser — côté
plateforme, jamais l'agent — voir **§6.11**.

---

## 5. Capacité B — réception/rapprochement (esquisse, hors périmètre figé)

Renvoi blueprint §13 et `GoToMarket/Metiers/Encheres/Roadmap-Gestion-BV-Liakont.md` §2-B. Principes posés, non figés :
contrat de réception **`IPaInboundClient` SÉPARÉ** d'`IPaClient` ; **agrégat `InboundDocument` distinct** (ne pas
polluer la machine d'émission ni l'append-only) ; rapprochement facture entrante ↔ décompte par `MatchConfidence`
(jamais de lien auto en confiance moyenne/basse) ; **conditionnement du règlement = SIGNAL** (feu vert/rouge en
console/export), **jamais** une écriture ni un ordre de virement (règle n°5). À concevoir dans un document dédié.

---

## 6. ❓ Points NON TRANCHÉS (décision expert-comptable / re-sourçage / terrain — ne rien coder avant)

1. **Annexe 7 (BR-FR-CTC)** non lue : contrôles Schematron éventuellement spécifiques au type 389 (mentions exigées).
2. **Statut fiscal d'un bordereau acheteur multi-vendeurs** dans la réforme (réception → N factures ? pièce halle→mareyeur ?) — capacité B.
3. **Circuit d'enrôlement / impersonation PA** pour N vendeurs (la session PA = le vendeur, cf. sandbox SuperPDP) : qui enrôle N pêcheurs, comment la halle impersonne.
4. **Délai de contestation** (valeur) : relève du contrat de mandat (« librement déterminé par les parties », §1.5), jamais un défaut produit.
5. **Code de l'avoir auto-facturé** (261 vs 381 + indicateur) et **complément de prix a posteriori** (« pas de code BT-3 dédié ») — non tranchés ; bloquer plutôt qu'inventer. Articulation à clarifier : un avoir auto-facturé passe-t-il le `ISelfBilledGate` / le workflow d'acceptation (§2.3), et comment se classe-t-il vs F07-F08 ?
6. **Routage 261-2-4° lot par lot** (frais/transformé vs conservé frais) et **code/motif d'exonération** porté au pivot — décision EC.
7. **Tolérance des trous de séquence** sous mandat (numéro alloué puis abandonné) — non sourcée ; conditionne le moment d'allocation.
8. **Verticales non encore sourcées** présentes uniquement dans GoToMarket : **droits d'auteur (retenue 285 bis + dispense de facture)**, **TFOP (objets précieux)**, **débours (267 II-2°)**, **RFA post-298 quater** (régime au 09/2026) — à ancrer dans une F-spec avant tout code (CLAUDE.md n°2).
9. **Versions BOFiP** des §1.4/§1.5 (2012/2013 ; re-fetch 2026-06-13 → 13/08/2021 pour BOI-30-20-10) à reconfirmer en version courante avant figeage.
10. **Re-clé anti-doublon F06** `(tenant, mandant_id, document_number)` (§3.2) — **conditionnée à un amendement de F06 §4** + ADR ; non figé tant que F06 §4 n'est pas amendé.
11. **« Taxe de criée »** (§4.2) : modélisation d'un relevé portant **une ligne exonérée 261-2-4° ET une ligne de service taxée 20 %** précomptée (flux de sens opposé sur le même document) — non tranché.

---

## 7. Renvois

- Architecture : `blueprint.md` (§1, §2 généricité, §6 frontières, §7 multi-tenancy, §13 phase 2 réception).
- Specs liées : `F01-F02` (pivot, `Invoicer`/`IsSelfBilled`, Number BT-1, idempotence R2), `F03` (mapping TVA,
  catégories/VATEX), `F04` (validation), `F06` (anti-doublon §4 — **à amender pour le cas 389**, piste d'audit
  append-only), `F07-F08` (avoirs, classification facture/avoir, frontière B2B/B2C).
- ADR à créer (frontières/décisions de F15) : module `Mandats` 1ʳᵉ classe ; agrégat d'acceptation distinct +
  `ISelfBilledGate` ; allocation BT-1 hybride + re-clé F06 ; `IPaInboundClient` séparé (capacité B).
- GoToMarket : `Metiers/Encheres/Roadmap-Gestion-BV-Liakont.md`, `PA/Validation-Autofacturation-389-PA.md`
  (grille Q1-Q15), `Metiers/Validation-Verticales/DR-Autofacturation-Agro.md`, `Metiers/Validation-Verticales/DR-Validation-Criees.md`.
