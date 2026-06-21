# ADR-0023 — Génération du Factur-X (PDF/A-3 + CII) côté plateforme

- **Statut** : Accepté (2026-06-15)
- **Nature** : ADR normatif (génération de documents fiscaux transmis à l'administration). Le module
  `Liakont.Modules.FacturX` (`src/Modules/FacturX`) n'est **pas encore créé** — cet ADR cadre le choix
  d'outillage et les invariants avant le code.
- **Date** : 2026-06-15
- **Contexte décisionnel** : `tasks/todo.md` (lot FACTURX) ; décisions interactives Karl 2026-06-15 ;
  `blueprint.md` §5 (tout nouveau package = ADR) et §6 (frontières / indépendance PA) ; `CLAUDE.md` n°1
  (decimal), n°2 (ne rien inventer), n°3 (bloquer plutôt qu'envoyer faux) ; règle de revue n°14
  (frontières = P1) ; `docs/conception/F01-F02` (pivot EN 16931), `F04` (validation BR-*), `F14`
  (plug-in Super PDP, conversion `convert` côté PA) ; `docs/adr/socle/ADR-0004-questpdf.md` (QuestPDF,
  confiné à `Stratum.Common.UI` — R-PDF-4) ; `docs/adr/ADR-0010-extraction-texte-pdf-pdfpig.md` (iText
  écarté pour AGPL) ; `docs/adr/ADR-0018-transport-smtp-mailkit.md` (MailKit) ;
  `docs/architecture/module-rules.md` §3 (garde inter-modules).

> **Numérotation** : il existe deux ADR-0004 — `docs/adr/ADR-0004-perimetre-contrat-extraction-pivot-v1.md`
> (produit) et `docs/adr/socle/ADR-0004-questpdf.md` (socle). Cet ADR vise le **second** dès qu'il parle
> de QuestPDF.

## Contexte

Liakont génère aujourd'hui le **pivot EN 16931 (JSON)** et **délègue** la conversion EN 16931 → CII XML
à l'endpoint `convert` de Super PDP (PA-spécifique) : aucun CII ni PDF/A-3 n'est produit en propre.

Décision produit (Karl, niveau de service « Essentiel », 2026-06-15) : **Liakont génère lui-même le
Factur-X** (PDF/A-3 + `factur-x.xml` CII embarqué) comme **format pivot transmis** — « toutes les PA
comprennent ce format ». Les plug-ins PA existants (Super PDP, B2Brouter — cœur du niveau « Pilotage »)
**ne changent pas** ; une nouvelle **PA générique** transmettra le Factur-X par email / dépôt de fichier
(concerne la spec F16 et le plug-in `Generique`, **hors de cet ADR**).

Un Factur-X conforme a **trois composantes** : (1) le **rendu visuel** PDF lisible ; (2) le **XML CII**
EN 16931 ; (3) le **scellement PDF/A-3** (embarquement de `factur-x.xml` avec `/AFRelationship`,
`OutputIntent` ICC, XMP PDF/A + schéma d'extension `fx:`). Le rendu visuel (1) est traité par **FX04**
(réutilisation de `ArchiveReadableDocument`, déjà au repo) — cet ADR porte sur (2) et (3). La plupart des
libs .NET ne couvrent que (2).

**Niveau ligne vs niveau document (point central).** Le pivot ne porte la TVA qu'au **niveau ligne**
(BG-30 / `PivotLineTaxDto`). Or EN 16931 (profil COMFORT) impose la **ventilation TVA au niveau document
BG-23** (BT-116/117 par catégorie), plus **BT-106** (somme des nets) et **BT-115** (montant dû). Aucun de
ces agrégats n'est porté par le pivot : tout le code existant les **dérive par agrégation**
(`SuperPdpPayloadBuilder.BuildVatBreakDown` : `GroupBy(catégorie,taux,VATEX)` + `Sum` ;
`SendArchiveComposer`). Un invariant « aucune dérivation » pris au pied de la lettre serait donc
inapplicable (il bloquerait toute facture). La frontière correcte est **recopie des valeurs qualitatives
/ agrégation arithmétique des totaux** — voir §2.

Contraintes : blueprint §6 (la génération ne dépend d'**aucun** plug-in PA — interdit de réutiliser le
`convert` d'une PA) ; licence permissive (AGPL exclu, ADR-0010) ; montants `decimal` (n°1) ; aucune valeur
fiscale **qualitative** inventée (n°2) ; bloquer plutôt qu'envoyer faux (n°3) ; profil cible **EN 16931
(COMFORT)** en V1.

## Décision

1. **Scellement PDF/A-3 = QuestPDF** (déjà au repo — `docs/adr/socle/ADR-0004-questpdf.md`, version
   `2024.12.3` dans `Directory.Packages.props`). QuestPDF gère nativement le **PDF/A-3b** depuis
   **2024.10.3** (deux API distinctes : `DocumentSettings.PdfA` booléen, et l'enum
   `PDFA_Conformance.PDFA_3B`), l'embarquement de pièce jointe avec relation (`AddAttachment` +
   `DocumentAttachmentRelationship`) et l'extension du XMP (`ExtendMetadata`). **Aucun nouveau package
   produit.** Bump **2024.12.3 → ≥ 2025.7.4** pour le correctif de compatibilité **Mustang** (changelog
   QuestPDF). Confiné à la couche `Infrastructure` du module FacturX, derrière le port `IFacturXBuilder`.

   **Extension de périmètre à acter** : l'ADR socle confine QuestPDF à `Stratum.Common.UI` (R-PDF-4) ;
   l'utiliser dans `FacturX.Infrastructure` élargit ce périmètre. La garde inter-modules globale n'étant
   **pas câblée** dans le repo Liakont (`module-rules.md` §3), le module FacturX **livre ses propres
   tests NetArchTest de frontière** (modèle `TransmissionBoundaryTests`).

2. **XML CII = sérialiseur maison** (décision Karl 2026-06-15). Pivot → `CrossIndustryInvoice` (profil
   EN 16931 COMFORT), code Liakont **pur** dans `FacturX.Domain`/`Application`.
   - **Recopie stricte** des montants et des catégorie/taux/VATEX **portés par le pivot au niveau ligne
     (BG-30)** (`decimal`, formatage invariant culture ; issus du mapping F03). Le sérialiseur
     **n'invente aucune valeur fiscale qualitative** (catégorie/taux/VATEX) et **n'applique aucune valeur
     par défaut**.
   - Il **dérive en revanche les agrégats obligatoires non portés par le pivot** — **BG-23**
     (BT-116/117 = sommes exactes ; BT-118/119/121 recopiés de la ligne), **BT-106** (somme des nets),
     **BT-115 = BT-112 − BT-113** (identité BR-CO-16) — par **agrégation arithmétique déterministe
     imposée par la norme**, en `decimal`, puis revalidée par Schematron (**BR-CO-15** fatale).
     **« Agrégation déterministe imposée par EN 16931 » ≠ « règle fiscale inventée » (n°2)** : aucune
     valeur n'est choisie, seules des sommes/identités normatives sont calculées (c'est déjà ce que fait
     `BuildVatBreakDown`).
   - **Constante de profil** non fiscale : BT-24 (identifiant de spécification) est posée comme telle.
     **BT-5 (code devise)** est en revanche **recopié du pivot** (`CurrencyCode`, défaut EUR sourcé F01-F02 —
     jamais codé en dur). L'interdit de §2 ne porte que sur les **données fiscales qualitatives**.
   - **BG-23 / BR-CO-17 (angle mort amont)** : la ventilation TVA document (BG-23) et l'identité
     `BT-117 = arrondi(BT-116 × BT-119 / 100)` (BR-CO-17) ne sont **pas** validées en amont (le pivot ne
     porte pas BG-23 ; la validation plateforme contrôle les totaux ligne/document, pas la ventilation).
     Le sérialiseur les **produit et les réconcilie** avec les totaux portés (BR-CO-14 : Σ BT-117 = BT-110 ;
     BR-CO-15 : BT-109 + BT-110 = BT-112), en `decimal` half-up. Tout écart non réconciliable → **blocage**,
     jamais un CII non conforme ni un « rattrapage » silencieux.
   - **Blocage** si un BT obligatoire n'est **ni porté ni dérivable** (jamais de XML tronqué ni de valeur
     fabriquée). Le sérialiseur n'appelle **jamais** le `convert` d'une PA (§6).

3. **XMP `fx:` et `AFRelationship` = responsabilité Liakont.** Le bloc XMP Factur-X est écrit par nous via
   `ExtendMetadata` : URI d'extension `urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#` (casse et `#`
   final **obligatoires**), `fx:DocumentType=INVOICE`, `fx:DocumentFileName=factur-x.xml`, `fx:Version` (version majeure Factur-X, p. ex. `1.0` — à ne pas confondre avec la version de spec 1.0.05/DGFiP v3.2),
   `fx:ConformanceLevel=EN 16931` (avec l'espace).
   La pièce jointe `factur-x.xml` utilise `/AFRelationship = Alternative` : **valeur la plus interopérable
   retenue par prudence** (requise en Allemagne, permise en France au même titre que `Data`/`Source`). La
   spec admet aussi `Source` quand le PDF résulte de la transformation du XML pour un destinataire hors
   d'Allemagne ; l'exemple QuestPDF n'est donc **pas invalide**. Source normative : FNFE-MPE / Factur-X 1.0
   (pas QuestPDF).

4. **Validation de conformité = bloquante.**
   - Tier rapide (`verify-fast`/unit) : assertions structurelles + validation **XSD CII** + **Schematron
     CEN/TC 434** (BR-* dont **BR-CO-15** fatale) + artefacts **FNFE-MPE** (BR-FR-*).
   - Tier intégration (`run-tests`, déjà sous Testcontainers) : **veraPDF + Mustangproject** via image
     **Docker** (JRE embarquée, pas de Java sur le runner) sur des échantillons générés.
   - Un PDF/A-3 non conforme = facture non conforme → **échec bloquant** (n°3). **veraPDF (GPLv3+/MPLv2+)
     et Mustangproject (Apache-2.0)** sont de l'**outillage CI externe** appelé en conteneur (« mere
     aggregation ») → **aucune** contrainte de licence sur le produit.

5. **Garde-fou natif.** QuestPDF embarque un binaire natif (Skia/qpdf) : **musl-x64 (Alpine) est
   nominalement supporté mais sujet à des échecs de chargement réels** (issues QuestPDF #1039/#812) →
   **risque opérationnel**. L'appliance actuelle est `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian/glibc)
   → **OK**. Tout passage à une base musl impose une validation préalable et la réévaluation de cet ADR.

## Invariants

- **INV-FX-1** — QuestPDF n'est référencée, pour la génération Factur-X, que par `FacturX.Infrastructure`
  (port `IFacturXBuilder` en `Application`).
- **INV-FX-2** — Le sérialiseur n'invente aucune valeur fiscale **qualitative** (catégorie/taux/VATEX) et
  n'applique aucun défaut. Les agrégats obligatoires non portés par le pivot (BG-23/BT-116/117, BT-106,
  BT-115) sont **dérivés par agrégation arithmétique déterministe** (norme EN 16931), en `decimal`,
  revalidés Schematron. Les constantes de profil (BT-24, BT-5) sont autorisées. Un BT obligatoire ni
  porté ni dérivable → blocage tracé (jamais de valeur fabriquée).
- **INV-FX-3** — `/AFRelationship = Alternative` (choix d'interopérabilité, §3) ; URI d'extension XMP
  `urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#` ; `fx:ConformanceLevel = EN 16931`.
- **INV-FX-4** — La génération ne référence aucun plug-in PA et n'appelle jamais le `convert` d'une PA.
  `FacturX` ne dépend **pas non plus** de `Transmission.Contracts` ni d'un module métier, et **ne consulte
  aucune `PaCapabilities`** : la décision de générer est prise par le pipeline appelant, jamais par
  `FacturX` ; la sortie est **déterministe du pivot seul**. Garde **non automatique** dans le repo
  (`module-rules.md` §3) → assurée par les **tests de frontière livrés par le module FacturX** (modèle
  `TransmissionBoundaryTests`).
- **INV-FX-5** — Un Factur-X qui échoue veraPDF + Mustang en CI ne peut pas être considéré émissible.
- **INV-FX-6** — Montants en `decimal` ; formatage XML invariant culture.
- **INV-FX-7** — BG-23 et BR-CO-17 (non validés en amont) sont **produits par le sérialiseur** et
  réconciliés avec les totaux portés (BR-CO-14/15) ; un écart non réconciliable → blocage tracé, jamais un
  CII non conforme.
- **INV-FX-8** (RDF18) — Le `LicenseType` QuestPDF du chemin d'émission **n'est jamais codé en dur** : il
  est déclaré par **topologie/instance** via la configuration `QuestPdf:LicenseType` et **validé au
  déploiement** (fermé par défaut : valeur absente, vide, numérique ou inconnue → démarrage refusé,
  message opérateur FR). Seuls les **noms** d'énumération QuestPDF (`Community` &lt; 1 M$ CA / `Professional`
  / `Enterprise`) sont admis. La **déclaration du licensee** par topologie (ambiguïté de marque grise)
  reste un geste de **paramétrage/déploiement** (DEC-3), jamais une donnée client dans le code
  (`CLAUDE.md` n°7). Résolution co-localisée dans `FacturX.Infrastructure`
  (`QuestPdfLicenseConfiguration`), seule couche Liakont à référencer QuestPDF (INV-FX-1) ; le Host ne
  référence jamais QuestPDF.

## Conséquences

**Positif** : une seule lib PDF dans le socle (QuestPDF), **aucun nouveau package produit** ; licence sûre
(QuestPDF gratuit < 1 M$ CA + code maison) ; Factur-X **indépendant de toute PA** (§6) ; validation de
conformité **réelle** (pas de faux vert).

**À accepter** : écriture et maintenance d'un sérialiseur CII (borné au profil COMFORT, validé
XSD+Schematron) ; outillage CI Java-en-conteneur (veraPDF/Mustang) ; **suivi du seuil de revenu QuestPDF**
(Pro 999 $ / Enterprise 2 999 $ si dépassement — à anticiper vu le pricing premium visé) ; bump de version
QuestPDF.

**Critères de sortie V1** : un jeu d'échantillons couvrant mono-taux, multi-taux, exonéré (VATEX),
autoliquidation, et la criée mono-Seller ; la chaîne XMP `fx:Version` (version majeure Factur-X, p. ex. `1.0`) **figée au moment de coder**
— **à ne pas confondre** avec la version de spec (1.0.05 / DGFiP v3.2), **jamais inventée** ; **test de
cohérence visuel↔XML** (le rendu PDF et le CII portent les mêmes montants). La **journalisation de l'envoi**
(artefact transmis + réponse PA) est traitée par **FX06** (sur `document_events` F06 append-only).

## Limite

Profil **EN 16931 COMFORT** en V1 ; EXTENDED-CTC-FR (BR-FR-*, BT additionnels) = suivi (ne rien inventer
hors spec interne — un besoin EXTENDED non tranché par F16 bloque l'item).

**Ordonnancement** : la spec **F16** (FX01) fige le périmètre Factur-X + PA générique **avant** tout code
(FX02+) ; cet ADR ne se met en œuvre qu'une fois F16 figée.

## Alternatives écartées

- **ZUGFeRD-csharp + ZUGFeRD.PDF-csharp** (Apache-2.0, 100 % managé) — lib CII mature, moins de code.
  Écartée pour V1 : sur un produit fiscal on veut le **contrôle total** de la sérialisation (aucune
  dérivation/défaut caché sur une valeur qualitative) et le **blocage explicite** sur BT manquant ; lignée
  open-core **figée** (v18 = dernière majeure OSS, suites payantes). **Réutilisable en plan B** derrière le
  même port `IFacturXBuilder` si la maintenance du sérialiseur maison devenait un coût.
- **iText 7 (.NET)** — référence Factur-X mais **AGPL** / commerciale payante : exclu (cohérent
  `docs/adr/ADR-0010`).
- **PDFsharp 6.x (MIT) seul** — pas de mode PDF/A (ICC/`OutputIntent`/XMP/`AF` à assembler à la main,
  support PDF/A « very early ») : brique bas niveau, pas clé en main.
- **Securibox.FacturX (MIT)** — génère **et** valide, mais tire **SixLabors.ImageSharp** (licence
  commerciale > ~1 M$ CA) : même piège transitif que celui qu'on évite.
- **Aspose.PDF / Syncfusion / GemBox / Docotic** — payants ; support Factur-X turnkey non confirmé (sauf
  Aspose, avec historique de bug `/AFRelationship` à re-tester).
- **Réutiliser le `convert` d'une PA (ex. Super PDP) pour produire le CII** — viole blueprint §6 (la
  génération produit dépendrait d'un plug-in PA concret) : exclu.
- **Validateur PDF/A .NET natif** — aucun crédible en open source (seulement commerciaux 3-Heights /
  Aspose) → veraPDF/Mustang en conteneur retenu.

## Références

- `tasks/todo.md` (lot FACTURX) ; décisions interactives 2026-06-15 (Factur-X = pivot transmis ; profil
  EN 16931 COMFORT ; CII = sérialiseur maison ; agrégats EN 16931 dérivés ; validation veraPDF/Mustang
  bloquante).
- `blueprint.md` §5 (package = ADR), §6 (frontières) ; `CLAUDE.md` n°1 (decimal), n°2 (ne rien inventer),
  n°3 (bloquer) ; règle de revue n°14 (frontières P1) ; `docs/architecture/module-rules.md` §3 (garde
  inter-modules non câblée dans le repo).
- `docs/conception/F01-F02` (pivot EN 16931, TVA au niveau ligne BG-30), `F04` (validation BR-*,
  BR-CO-15/16), `F14` (Super PDP `convert`) ; `docs/conception/F16` **à écrire** (génération Factur-X +
  PA générique ; journalisation envoi = FX06).
- `docs/adr/socle/ADR-0004-questpdf.md` (QuestPDF, R-PDF-4) ; `docs/adr/ADR-0010-extraction-texte-pdf-pdfpig.md`
  (iText AGPL écarté) ; `docs/adr/ADR-0018-transport-smtp-mailkit.md` (MailKit).
- Référence de recopie/agrégation sans règle inventée : `SuperPdpPayloadBuilder.BuildVatBreakDown`,
  `SendArchiveComposer` (BG-23 déjà dérivée par agrégation).
- Recherche 2026-06-15 : questpdf.com (zugferd / document-settings / pricing) + github QuestPDF
  LICENSE & issues #1039/#812 ; github stephanstapel/ZUGFeRD-csharp ; verapdf.org +
  hub.docker.com/r/verapdf/cli ; mustangproject.org. AFRelationship : FNFE-MPE / Factur-X 1.0,
  akretion/factur-x, PDFlib KB.

## Avenant 2026-06-20 (RDF18) — licence QuestPDF configurable par topologie + contrôle au déploiement

Redline ADR fondateurs **RL-TR-6** (DEC-3). Le `LicenseType` QuestPDF était **codé en dur**
(`LicenseType.Community`) en deux endroits — `FacturXModuleRegistration.cs` (chemin d'émission FISCALE) et
le socle `Stratum.Common.UI/CommonUIServiceExtensions.cs` (exports PDF opérateur). La licence Community
est gratuite **sous 1 M$ de CA**, mais elle s'applique au **scellement PDF/A-3** (INV-FX-1, chemin
d'émission fiscale) et le « licensee » est **ambigu en marque grise** (intégrateur/hébergeur vs client
final). Une licence figée dans le code ne peut pas être déclarée par déploiement.

**Décision (code autonome, RDF18) :**
1. Le `LicenseType` est **configurable** via `QuestPdf:LicenseType` (configuration de
   topologie/instance) ; défaut documenté V1 = `Community` dans `appsettings.json`, **surchargeable** par
   `deployments/<client>/` sans recompilation. Plus de licence en dur côté produit (INV-FX-8).
2. **Contrôle au déploiement** (`QuestPdfLicenseConfiguration.Resolve`) : **fail-closed** — valeur
   absente/vide/numérique/inconnue → démarrage refusé (message opérateur FR : clé + valeurs admises +
   action). Le résolveur exige un **type explicite** (nom d'énumération), jamais une clé de licence.
3. **Socle vendored NON modifié.** `Stratum.Common.UI` (R-PDF-4) garde son défaut `Community` pour ses
   exports PDF opérateur (non fiscaux). Sur le chemin **fiscal**, `AddFacturXModule(IConfiguration)` est
   composé **après** `AddCommonUI` dans `AppBootstrap` et **impose** la valeur configurée (dernière
   écriture du statique global `QuestPDF.Settings.License`). Aucune entrée de provenance socle requise (le
   socle reste inchangé).
4. **Plan B documenté (non codé)** : si la contrainte de licence devient bloquante, le scellement PDF/A-3
   pourrait migrer vers `ZUGFeRD.PDF-csharp` (cf. redline) — hors périmètre RDF18.

La **déclaration du licensee** par topologie (DEC-3) reste un **arbitrage opérateur** au déploiement, pas
une donnée codée. Cet avenant **complète** (ne remplace pas) la décision §1 ; l'ADR socle
`ADR-0004-questpdf.md` n'est pas modifié.
