# F16 — Génération Factur-X (PDF/A-3 + CII) & PA générique
### Document de conception — module `Liakont.Modules.FacturX` (`src/Modules/FacturX`) + plug-in `Liakont.PaClients.Generique` (`src/PaClients/`)

> Statut : 🟨 **conception** (décision produit interactive 2026-06-15). Aucun code livré.
> Dernière mise à jour : 2026-06-15.
> Décision d'architecture associée : **[ADR-0023](../adr/ADR-0023-generation-facturx-pdfa3-cii.md)**
> (QuestPDF pour le scellement + sérialiseur CII maison + validation veraPDF/Mustang bloquante).
>
> **Sources (rien d'inventé)** : ADR-0023 ; [F01-F02](F01-F02-Modele-Pivot-Contrat-Extraction.md)
> (pivot EN 16931, BG-22 totaux / BG-30 ligne) ; [F03](F03-Mapping-TVA.md) (mapping TVA, catégories
> UNCL5305 / codes VATEX) ; [F04](F04-Controles-Qualite-Validation.md) (validation BR-CO-13/14/15,
> arrondi commercial half-up, zéro tolérance) ; [F06](F06-Tracking-Piste-Audit.md) (piste d'audit
> append-only) ; [F14](F14-Plugin-SuperPdp.md) (contrat `IPaClient`, capacités déclarées) ;
> [ADR-0018](../adr/ADR-0018-transport-smtp-mailkit.md) (transport SMTP MailKit) ; `blueprint.md` §6
> (frontières / indépendance PA). **Faits code vérifiés** (workflow de cartographie 2026-06-15) :
> `PivotTotalsDto`, `PivotLineTaxDto`, `SuperPdpPayloadBuilder.BuildVatBreakDown`, `ArithmeticRule`,
> `PivotRounding`.
>
> ⚠️ Tout point fiscal non tranché par une spec interne est marqué **NON TRANCHÉ** et **bloque** l'item
> (CLAUDE.md n°2 : aucune règle inventée).

---

## 1. Objet & périmètre

Au **niveau de service « Essentiel »**, Liakont **génère lui-même le Factur-X** (PDF/A-3 + `factur-x.xml`
CII embarqué) et le **transmet** via une **nouvelle PA générique**, par **email** ou **dépôt de fichier**.
Rationale produit : *« toutes les PA comprennent le Factur-X »* — un seul artefact canonique, transportable
par le plus petit dénominateur commun.

**Dans le périmètre :**
1. un module plateforme `FacturX` qui **produit** le Factur-X depuis le pivot EN 16931 ;
2. un plug-in **PA générique** (`IPaClient`) qui **transmet** le Factur-X par email / dépôt de fichier ;
3. la **journalisation** de l'envoi + une **trace de support** du document transmis.

**Hors périmètre (et explicitement inchangé) :**
- les plug-ins PA **API existants** (Super PDP, B2Brouter) = cœur du niveau **Pilotage**, **non modifiés** ;
- la gestion des **retours / statuts / cycle de vie** (Pilotage) ;
- l'**archivage probant** (coffre WORM, NF Z42-013) = Pilotage. La trace de support (§7) **n'est pas**
  l'archive probante.

## 2. Le Factur-X (trois composantes, profil EN 16931 COMFORT)

Un Factur-X conforme = (1) **rendu visuel** PDF lisible + (2) **XML CII** EN 16931 + (3) **scellement
PDF/A-3** (embarquement du XML + métadonnées XMP). Profil cible V1 = **EN 16931 (COMFORT)** : il correspond
exactement à la sémantique du pivot (BT/BG EN 16931). EXTENDED-CTC-FR = suivi (§10). Le choix d'outillage
(QuestPDF, sérialiseur maison, validation) est tranché par **ADR-0023**.

## 3. Sérialiseur CII maison — le cœur fiscal

Pivot → `CrossIndustryInvoice` (CII), **code pur** dans `FacturX.Domain` / `FacturX.Application`, **jamais**
via le `convert` d'une PA (blueprint §6). Renvoi ADR-0023 §2 + invariants INV-FX-2 / INV-FX-7.

### 3.1 Frontière recopie (qualitatif) vs dérivation (arithmétique)

Le pivot ne porte la TVA qu'au **niveau ligne (BG-30)** et les totaux au **niveau document (BG-22)** ; il ne
porte **pas** la ventilation TVA document (BG-23), ni BT-106, ni BT-115. Le sérialiseur **recopie** les
valeurs qualitatives et **dérive** les agrégats obligatoires par arithmétique normative — exactement ce que
fait déjà `SuperPdpPayloadBuilder.BuildVatBreakDown` (l.130-145).

| Élément CII | Traitement | Origine / règle |
|---|---|---|
| BT-131 (net ligne), BT-129 (qté), BT-146 (PU) | **RECOPIÉ** | `PivotLineDto` |
| BT-151 (catégorie ligne), BT-152 (taux ligne), montant TVA ligne, code VATEX | **RECOPIÉ** | `PivotLineTaxDto` |
| BG-23 — BT-118 (catégorie), BT-119 (taux), BT-121 (motif VATEX) | **RECOPIÉ** depuis la ligne | `PivotLineTaxDto` (clé de groupage) |
| BT-109 (HT), BT-110 (TVA), BT-112 (TTC), BT-113 (acompte) | **RECOPIÉ** | `PivotTotalsDto`, `PivotDocumentDto` |
| BT-106 (somme des nets) | **DÉRIVÉ** | `Σ BT-131` (BR-CO-10) |
| BG-23 — BT-116 (base par catégorie) | **DÉRIVÉ** | `Σ` nets du groupe `(catégorie, taux, VATEX)` |
| BG-23 — BT-117 (TVA par catégorie) | **DÉRIVÉ** | `arrondi(BT-116 × BT-119/100)` (BR-CO-17, voir §3.3) |
| BT-115 (montant dû) | **DÉRIVÉ** | `BT-112 − BT-113` (BR-CO-16) |
| BT-24 (identifiant de spécification) | **CONSTANTE de profil** | URN du profil (non fiscal) |
| BT-5 (code devise) | **RECOPIÉ** | `PivotDocumentDto.CurrencyCode` (défaut EUR sourcé F01-F02) — jamais codé en dur |

> **BT-146 (PU ligne)** est obligatoire (BR-26). Absent du pivot (`UnitPriceNet` null) → **blocage** : jamais
> dérivé de `BT-131/BT-129` (identité non sûre — ignore la quantité de base BT-149 et les remises de ligne).

### 3.2 Le critère « déterministe ≠ inventé »

- **INVENTÉ = interdit (CLAUDE.md n°2)** : choisir/fabriquer une **valeur qualitative** (catégorie, taux,
  VATEX, seuil). Ces valeurs viennent **uniquement** du pivot (mapping F03) ; si une catégorie est nulle →
  **blocage** (garde `SuperPdpPayloadBuilder.SingleTax` l.177-198 : « aucune catégorie n'est inventée »).
- **DÉRIVÉ = autorisé** : un **agrégat quantitatif** produit par une **identité arithmétique de la norme**,
  en `decimal`, sans aucun degré de liberté, et **revérifiable** (Schematron).

Test de tri : *« y a-t-il un choix / un jugement ? »* — oui → interdit ; conséquence forcée de la norme →
autorisé.

### 3.3 BG-23 + BR-CO-17 : produire, réconcilier, bloquer (le point sensible)

La validation plateforme (F04) contrôle les totaux **ligne/document** (BR-CO-13 : `BT-109 = Σ BT-131 −
remises + charges` ; `BT-110 = Σ TVA ligne` ; BR-CO-15 : `BT-112 = BT-109 + BT-110`) **mais pas la
ventilation BG-23** — logique, puisque le pivot ne la porte pas. Donc **BG-23 et l'identité BR-CO-17
(`BT-117 = arrondi(BT-116 × BT-119/100)`) deviennent la responsabilité du sérialiseur**, validées seulement
par Schematron (tier conformité §8).

Conduite à tenir (déterministe, pas inventé) :
1. agréger par `GroupBy(catégorie, taux, VATEX)` → `BT-116 = Σ nets du groupe` ;
2. `BT-117 = arrondi_half_up(BT-116 × BT-119 / 100)` (satisfait BR-CO-17) ;
3. **réconcilier** avec les totaux portés : `Σ BT-117 = BT-110` (BR-CO-14) et `BT-109 + BT-110 = BT-112`
   (BR-CO-15), en `decimal` half-up ;
4. tout **écart non réconciliable** (arrondi divergent côté source) → **blocage** tracé, jamais un CII non
   conforme ni un « rattrapage » silencieux.

### 3.4 Arrondi

`PivotRounding.RoundAmount` = `Math.Round(value, 2, MidpointRounding.AwayFromZero)` — commercial half-up,
2 décimales, **source unique** partagée agent (net48) / plateforme (.NET 10) (`PivotRounding.cs:16-29`).
**Zéro tolérance** : un centime d'écart bloque (`ArithmeticRule.cs:26-40`, INV-VALIDATION-017).

### 3.5 Conformité CTC-FR / PEPPOL : termes de paiement, mentions FR, livraison

> **Note d'amendement (2026-06-27, BUG-26 — recette EncheresV6/SuperPDP).** Une facture B2B émise vers
> SuperPDP (converter `en16931 → cii`, profil **CTC-FR**) a été **rejetée** par trois règles que le
> sérialiseur CII (FX03) et le builder Super PDP ne produisaient pas. Ces éléments sont des **données de
> l'entreprise** (conditions générales de vente), **jamais inventés par le produit** (CLAUDE.md n°2) :
> ils sont **paramétrés au niveau tenant** (groupe « Mentions de facturation », **[F12-A §3.4](F12-A-Parametrage-Tenant.md)**),
> **surchargeables par document** (le pivot prime sur le défaut tenant), et **absents ⇒ blocage** sur une
> facture B2B FR (CLAUDE.md n°3, jamais un envoi voué au rejet). Ces champs sont portés par le pivot
> (`PaymentTerms`, `Notes`, `DeliveryDate` — additifs EXT01, F01-F02) et projetés à l'identique par les
> **deux** projecteurs (sérialiseur CII *et* `SuperPdpPayloadBuilder`, schéma `en_invoice` :
> `payment_terms`, `notes[]`, `delivery_information`).

| Règle rejetée | Exigence | Élément CII (BT/BG) | Source de la valeur |
|---|---|---|---|
| **BR-CO-25** | montant dû (BT-115) positif ⇒ **BT-9 OU BT-20** présent | `SpecifiedTradePaymentTerms` : `DueDateDateTime` (BT-9) et/ou `Description` (BT-20) | BT-9 = `PaymentDueDate` du pivot, **jamais fabriquée** (EXT01, F14 §3.2) ; BT-20 = mention tenant « termes de paiement » |
| **BR-FR-05** ×3 | 3 mentions légales FR obligatoires entre professionnels | `ram:IncludedNote` (BT-22 `Content` + BT-21 `SubjectCode`, codelist UNTDID 4451) | mentions tenant (3 textes), `SubjectCode` imposés par le converter CTC-FR : **PMD** pénalités de retard, **PMT** indemnité forfaitaire de recouvrement, **AAB** escompte/absence |
| **PEPPOL-EN16931-R008** | « Document MUST not contain empty elements » | `ApplicableHeaderTradeDelivery` **jamais vide** → `ActualDeliverySupplyChainEvent/OccurrenceDateTime` (BT-72) | date de livraison effective = **date de vente** (BG-13 hors socle V1, F15 §82-83 ; aux enchères la livraison du lot intervient **à l'adjudication** — fait du modèle, pas une fabrication) |

**BR-CO-25 — aux enchères.** Le bordereau acheteur est une **demande de paiement** exigible **comptant à la
vente** ; à l'émission il n'est pas nécessairement soldé. La satisfaction de BR-CO-25 passe donc par **BT-20**
(termes de paiement, mention tenant — ex. « Paiement comptant exigible à la vente »), valable que la facture
soit payée ou non, **jamais par une échéance BT-9 fabriquée** (CLAUDE.md n°2 ; BT-9 n'est émise que si la
source la porte — F14 §3.2/O11). Le **reflet du paiement effectif** (acompte BT-113 = somme des règlements ⇒
montant dû nul, cas « facture soldée » de F14 §3.2) est un **raffinement distinct** : les règlements sont
déjà un flux séparé (e-reporting de paiement F09, extraction `ExtractPayments` par `date_reglement`), non
rattaché au bordereau dans la requête document — leur projection en BT-113 reste un suivi et **ne conditionne
pas** la levée de BR-CO-25, déjà assurée par BT-20.

**Périmètre du blocage.** Les mentions FR (BR-FR-05) et le BT-20 sont exigés sur la **voie document** (facture
B2B à destinataire identifié SIREN, FR). Le **B2C** part par l'**e-reporting agrégé** (pas de Factur-X par
document) et n'est donc pas concerné. Codes UNTDID 4451 et obligation **non inventés** : imposés par le verdict
du converter CTC-FR (autorité sur ce qu'il exige) et adossés aux mentions obligatoires entre professionnels
(C. com. art. L441-9 / L441-10) — le **contenu** reste saisi par le client/EC, le produit n'embarque aucun texte.

## 4. Scellement PDF/A-3

**QuestPDF** (déjà au repo) : `PDFA_3B` + `AddAttachment` (relation **`Alternative`**, cf. ADR-0023 §3) +
`ExtendMetadata` (XMP `fx:`, URI `urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#`,
`fx:ConformanceLevel = EN 16931`). Lib confinée à `FacturX.Infrastructure`, derrière le port
`IFacturXBuilder`. Détails + garde-fou Alpine/musl : ADR-0023 §1/§3/§5. Le XMP porte aussi `fx:Version` =
**version majeure Factur-X** (p. ex. `1.0`) — **à ne pas confondre** avec la version de spec (1.0.05 /
DGFiP v3.2) ; chaîne exacte figée au moment de coder, jamais inventée.

## 5. Couche visuelle

Rendu PDF **depuis le pivot**, en réutilisant `ArchiveReadableDocument` (option B retenue : déterministe,
toujours disponible, pas le PDF source de l'agent). Un **test de cohérence visuel↔XML** vérifie que le rendu
et le CII portent les mêmes montants.

## 6. La PA générique (plug-in)

`src/PaClients/Liakont.PaClients.Generique` (nom à confirmer §10) — ne référence que
`Transmission.Contracts` (+ Common), **jamais** un autre module (y compris `Notification` et `FacturX`),
jamais un autre plug-in, **jamais MailKit** (blueprint §6 ; NetArchTest + `…BoundaryTests` livrés par le
plug-in, modèle `SuperPdpBoundaryTests`).

- **`PaCapabilities`** : déclare **`SupportsFacturXTransmission = true`** (les PA existantes Super PDP /
  B2Brouter le déclarent `false`). **À ajouter au record `PaCapabilities` ET à l'enum miroir `PaCapability`**
  (capacité typée/journalisable, comme les 8 valeurs existantes). Elle **ne** récupère **pas** de statut et
  n'a **pas** de cycle de vie — c'est précisément ce qui en fait une PA de niveau **Essentiel**, pas Pilotage.

### 6.1 Flux positif (qui génère, qui transmet)

C'est le **pipeline** (module Transmission / `SendTenantJob`) qui, **à l'étape `Sending` (`SendReadyAsync`,
JUSTE AVANT l'appel de transmission au plug-in — surtout PAS dans `FinalizeIssuedAsync`, qui ne s'exécute
qu'en AVAL, après le retour `Issued`)**, et **quand la PA active déclare `SupportsFacturXTransmission`**
(jamais `if (pa is Generique)`, CLAUDE.md n°8), résout `IFacturXBuilder` et **génère le Factur-X**, puis le
transmet au plug-in. Le plug-in **ne** référence **jamais** `IFacturXBuilder` ni `FacturX.*` ; il ne fait
que **transporter** l'artefact. Les PA existantes (capacité `false`) suivent leur chemin **inchangé** (elles
construisent leur propre payload).

**Contrat de passage de l'artefact (extension de `Transmission.Contracts`).** La signature actuelle
`SendDocumentAsync(PivotDocumentDto, …)` ne porte **pas** d'octets pré-construits. Il faut étendre le
contrat `IPaClient` (p. ex. un `PaSendContext` optionnel portant le Factur-X déjà scellé) : les PA
existantes l'**ignorent** (elles construisent leur payload), la PA générique l'**exige** (artefact absent →
**blocage**, jamais une régénération dans le plug-in qui violerait l'indépendance). Décision scopée à FX07.

> **Note d'amendement (2026-06-16, FX07 — décision de frontière).** Le pipeline (`SendTenantJob`, module
> `Pipeline`) ne peut PAS référencer directement `IFacturXBuilder` : ce port vit en couche `Application` du
> module FacturX, or un module n'accède à un autre que par ses **`Contracts`** (module-rules §3 / CLAUDE.md
> n°14 ; une référence `Pipeline → FacturX.Application` serait un P1, sans précédent dans le dépôt). La
> génération reste donc « **derrière `IFacturXBuilder`** » mais via un **pont** `IFacturXArtifactBuilder`
> défini dans **`Transmission.Contracts`** (là où vivent déjà la capacité `SupportsFacturXTransmission` qui
> le déclenche et le canal `IDocumentDeliveryChannel`), **implémenté au Host** (composition root) en
> déléguant à `IFacturXBuilder`. Seul le Host référence à la fois `Transmission.Contracts` et le module
> FacturX. Le pipeline reste Contracts-only ; FacturX reste indépendant des PA (INV-FX-4 intact).
> La **journalisation** (FX06) est posée par un port d'écriture ségrégué `IPaTransmissionJournal`
> (`Documents.Contracts`, symétrique au read `IPaTransmissionJournalQueries`), et la **trace de support**
> par `ISupportTraceStore` (`SupportTrace.Contracts`), tous deux sur le chemin Factur-X uniquement.

### 6.2 Canaux de livraison (abstraction définie dans `Transmission.Contracts`)

Le plug-in transmet via une **nouvelle abstraction définie dans `Transmission.Contracts`**, p. ex.
`IDocumentDeliveryChannel` (livrer : cible/destinataire, octets, nom de fichier). Elle est **implémentée au
Host** (composition root) ; le plug-in n'en voit que le contrat. Implémentations :
- **email** : l'implémentation Host **compose un message MIME AVEC pièce jointe** (MimeKit/MailKit,
  **Host-only**, cf. [ADR-0018](../adr/ADR-0018-transport-smtp-mailkit.md)) — le Factur-X est une **pièce
  jointe**, or `IEmailTransport` du socle (`SendAsync(destinataire, sujet, corps)`) ne porte **qu'un corps
  texte**. On **ne réutilise donc PAS `IEmailTransport`** et on **ne modifie PAS le socle vendored** (n°11/20) :
  l'impl. de `IDocumentDeliveryChannel` réutilise la **config/connexion SMTP** d'ADR-0018 mais compose son
  propre MIME (attachment-capable). **Le plug-in ne référence ni MailKit ni `Notification`** — uniquement
  `IDocumentDeliveryChannel` de `Transmission.Contracts`. Destinataire = **boîte PA du tenant**.
- **dépôt de fichier** : V1 = écriture dans un dossier / point de montage configuré par tenant. **SFTP =
  fast-follow** (package SSH.NET → ADR dédié, §10).

**Secrets** (mot de passe SMTP, identifiants SFTP) **chiffrés par tenant**, jamais en clair, jamais
journalisés (CLAUDE.md n°10/18).

## 7. Intégration pipeline & journalisation

- **Génération à l'étape `Sending`** (`SendReadyAsync`, **juste avant** l'appel de transmission), derrière
  `IFacturXBuilder`, déclenchée par la capacité **`SupportsFacturXTransmission`** de la PA active (cf. §6.1).
  ⚠️ **PAS dans `FinalizeIssuedAsync`** : cette méthode s'exécute en aval (après le retour `Issued`) et ne
  fait que l'archive WORM + `MarkIssued` + purge ; y générer le Factur-X le produirait **après** la
  transmission (artefact jamais transmis / faux-vert). `FinalizeIssuedAsync` ne porte que la **journalisation
  FX06**. Le plug-in reçoit l'artefact **déjà construit** (contrat étendu, §6.1), il ne le génère jamais.
- **Journalisation de l'envoi (FX06)** — sur F06 `document_events` **append-only** : ajouter le **compte /
  plug-in PA** (colonne explicite — aujourd'hui non tracé), les **horodatages** requête/réponse, le **hash**
  de l'artefact transmis, la **réponse PA**, et la clé d'idempotence recherchable. **Aucun chemin
  update/delete** (WORM/append-only respecté, CLAUDE.md n°4).
- **Trace de support** — copie du **Factur-X réellement transmis**, dans un **store dédié à rétention
  courte** (proposition : 90 jours, §10), **purge planifiée autorisée car NON-audit**. À **distinguer
  formellement** de l'archive probante (Pilotage) et de la piste d'audit WORM (qui, elle, ne se purge
  jamais).

## 8. Validation / conformité

- Tier rapide (`verify-fast`/unit) : assertions structurelles + **XSD CII** + **Schematron CEN/TC 434**
  (BR-* dont BR-CO-15 fatale) + artefacts **FNFE-MPE** (BR-FR-*, §10).
- Tier intégration (`run-tests`, Testcontainers) : **veraPDF + Mustangproject** (Docker) — **bloquant**.
- Un Factur-X qui échoue ces validations n'est **pas émissible** (ADR-0023 §4, INV-FX-5).

> **Note d'amendement (2026-06-16, FX03).** Les **XSD CII présents au dépôt**
> (`docs/references/dgfip-v3.2/.../F1_BASE_CII_D22B`, `F1_FULL_CII_D22B`) sont des **profils DGFiP
> RESTREINTS** : la plupart des Business Terms EN 16931 y sont **commentés** dans le XSD — dont
> BT-106 (`LineTotalAmount`), BT-112 (`GrandTotalAmount`), BT-115 (`DuePayableAmount`), pourtant
> obligatoires EN 16931. Valider un CII EN 16931 (COMFORT) contre eux produit un **faux négatif**
> (rejet d'un document conforme). Le **XSD CII EN 16931 complet** et le **Schematron CEN/TC 434** sont
> donc des **artefacts externes non vendorés** (même bucket que les artefacts FNFE-MPE — §10 action
> A4, NON TRANCHÉ) ; le Schematron exige en plus un **processeur XSLT 2.0** (Saxon → ADR dédié, aucun
> package sans ADR). En conséquence, **FX03** assure au tier rapide : (1) bonne formation + **complétude
> structurelle EN 16931** (BT/BG obligatoires) et (2) **identités arithmétiques BR-CO** (BR-CO-10/13/14/
> 15/16/17, dont BR-CO-15 fatale) sur les golden files. La **conformité XSD/Schematron EN 16931 réelle**
> reste portée par **Mustangproject** (qui rejoue le Schematron EN 16931) **au tier intégration de FX04**
> + la **recette GATE_FACTURX**.

## 9. Frontières & règles (récapitulatif)

| Règle | Application |
|---|---|
| Montants `decimal` (n°1) | Pivot + sérialiseur ; formatage XML invariant culture |
| Ne rien inventer (n°2) | Valeurs **qualitatives** uniquement du pivot ; agrégats = arithmétique normative |
| Bloquer plutôt qu'envoyer faux (n°3) | BT manquant/non dérivable/non réconciliable → blocage |
| Indépendance PA (§6) | Génération sans aucun plug-in PA ; NetArchTest **livré par le module FacturX** (modèle `TransmissionBoundaryTests`) |
| Agent sans logique métier (§6) | L'agent n'est pas touché ; génération 100 % plateforme |
| WORM / append-only (n°4) | F06 inchangé en nature ; trace de support = store séparé, purgeable |
| Génération sans connaissance PA | `FacturX` ne dépend **pas** de `Transmission.Contracts` ni d'un module métier et **ne consulte aucune `PaCapabilities`** ; sortie déterministe du pivot seul (la décision de générer est prise par le pipeline appelant). Vérifié par les tests de frontière FacturX |

## 10. Points ouverts (NON TRANCHÉ)

- **Rétention** exacte de la trace de support (proposition : 90 jours, configurable).
- **Nom** du plug-in générique (`Generique` / `FacturXMail` / autre).
- **SFTP** en V1 ou fast-follow (nouveau package SSH.NET → ADR dédié).
- **EXTENDED-CTC-FR / BR-FR-*** : récupérer les artefacts de validation FNFE-MPE (action A4 de l'index) —
  profil EXTENDED = suivi, pas V1.
- **Canal email PA** : serveur SMTP d'envoi au **niveau instance** (comme ADR-0018) tandis que la **boîte PA
  destinataire** est **par tenant** — à confirmer.

## 11. Décomposition en items (cf. `tasks/todo.md`, lot FACTURX)

`FX02` module `FacturX` + `IFacturXBuilder` · `FX03` sérialiseur CII (§3) · `FX04` scellement PDF/A-3 +
couche visuelle (§4-5) · `FXG` plug-in PA générique (§6) · `FX06` journalisation + trace de support (§7) ·
`FX07` intégration pipeline + gate de recette (§8). Prérequis `FX00` (ADR-0023) **fait**.

## Références

- **ADR-0023** (génération Factur-X) ; ADR-0004 socle (QuestPDF) ; ADR-0018 (MailKit).
- F01-F02 (pivot, BG-22/BG-30) ; F03 (mapping TVA) ; F04 (validation BR-CO, arrondi) ; F06 (audit) ;
  F14 (plug-in PA).
- `blueprint.md` §6 ; `CLAUDE.md` n°1/2/3/4/8/10/18 ; règle de revue n°14 ; `module-rules.md` §3.
- Faits code (vérifiés 2026-06-15) : `SuperPdpPayloadBuilder.BuildVatBreakDown` (l.130-145),
  `SuperPdpPayloadBuilder.SingleTax` (l.177-198), `ArithmeticRule.cs` (l.26-40), `PivotRounding.cs`
  (l.16-29), `PivotTotalsDto.cs`, `PivotLineTaxDto.cs`.
