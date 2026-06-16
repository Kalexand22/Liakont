# Scénarios de test — module FacturX

## Unit (`Liakont.Modules.FacturX.Tests.Unit`)

### Confinement de QuestPDF (INV-FX-1)
- `FacturXBoundaryTests.QuestPdf_IsNotReferencedAboveInfrastructure` — Contracts, Domain et
  Application n'ont AUCUNE dépendance IL sur `QuestPDF` (NetArchTest `NotHaveDependencyOnAny`).
- `FacturXBoundaryTests.QuestPdf_IsReferencedByInfrastructure` — contre-épreuve : la dépendance
  QuestPDF EST présente dans `FacturX.Infrastructure` (réflexion ; sinon le confinement serait vide de sens).
- `FacturXBoundaryTests.ModuleCsprojs_ConfineQuestPdf_AndForbidCrossModuleReferences` (partie a) —
  scan déclaratif : `QuestPDF` n'est déclarée (PackageReference) que par le `.csproj`
  `FacturX.Infrastructure`.

### Indépendance PA et frontière inter-modules (INV-FX-4)
- `FacturXBoundaryTests.FacturX_DoesNotReferenceAnyConcretePaPlugin` — aucune couche FacturX ne
  dépend de `Liakont.PaClients` (NetArchTest).
- `FacturXBoundaryTests.FacturX_DoesNotDependOnTransmissionNorPaCapabilities` — aucune couche FacturX
  ne dépend de `Liakont.Modules.Transmission` (NetArchTest ; donc aucune consultation de `PaCapabilities`).
- `FacturXBoundaryTests.FacturX_DoesNotReachIntoAnotherBusinessModule` — aucune couche FacturX ne
  référence un autre `Liakont.Modules.*` (réflexion ; seule dépendance inter-projet : le contrat agent partagé).
- `FacturXBoundaryTests.ModuleCsprojs_ConfineQuestPdf_AndForbidCrossModuleReferences` (partie b) —
  scan déclaratif : un `.csproj` FacturX ne référence que le module lui-même et
  `Liakont.Agent.Contracts` (détecte une `ProjectReference` interdite même non encore exercée).

### Artefact Factur-X (robustesse de l'ossature)
- `FacturXDocumentTests.Constructor_WithValidArguments_ExposesThemUnchanged` — round-trip des octets
  PDF/A-3, du `factur-x.xml` et du nom de fichier.
- `FacturXDocumentTests.Constructor_NullPdfBytes_Throws` / `Constructor_NullXml_Throws` /
  `Constructor_BlankFileName_Throws` — un artefact de conformité ne se construit jamais incomplet
  (CLAUDE.md n°3).

### Enregistrement DI du module (`AddFacturXModule`)
- `FacturXModuleRegistrationTests.AddFacturXModule_ReturnsSameCollection_ForChaining` — l'API de
  composition chaîne (retourne la même `IServiceCollection`).
- `FacturXModuleRegistrationTests.AddFacturXModule_ActivatesQuestPdfCommunityLicense` — l'effet de
  bord public (licence QuestPDF Community) est posé.
- `FacturXModuleRegistrationTests.AddFacturXModule_IsIdempotent` — appel répété sans erreur (le socle
  peut déjà avoir posé la licence).

### Sérialiseur CII — FX03 (`Cii/CrossIndustryInvoiceSerializer*`)

- `CrossIndustryInvoiceSerializerTests` — recopie des valeurs qualitatives (BT-24/1/2/3, catégorie
  BT-151, taux BT-152, PU BT-146, qté BT-129, net BT-131, SIREN scheme 0002, TVA scheme VA, devise BT-5),
  dérivation BG-23 (une vs deux ventilations), BT-115 = BT-112 − BT-113 (acompte), et BLOCAGE
  (`FacturXGenerationException`) sur : BT-146 absent, ligne sans/avec plusieurs ventilations TVA (BG-30),
  catégorie absente (BT-151), charges document (BG-20/21 non V1), et écarts non réconciliables
  BR-CO-10/13, BR-CO-14, BR-CO-15.
- `CrossIndustryInvoiceMatrixTests` — sur la matrice (mono-taux, multi-taux, exonéré VATEX,
  autoliquidation, criée mono-Seller) : structure EN 16931 complète (`CiiStructuralValidator`),
  identités arithmétiques `CiiBusinessRuleChecker` (BR-CO-10/13/14/15/16/17), et stabilité vis-à-vis des
  golden files commités (`Cii/GoldenFiles/*.cii.xml`).

> **Périmètre de validation FX03 (fast tier).** La conformité XSD CII EN 16931 + Schematron CEN/TC 434
> n'est PAS exercée en unitaire : les XSD CII du dépôt (`F1_BASE`/`F1_FULL_CII_D22B`) sont des profils
> DGFiP RESTREINTS (BT-106/112/115 commentés → faux négatif sur un CII EN 16931 conforme) ; le XSD CII
> EN 16931 complet et le Schematron CEN sont des artefacts EXTERNES non vendorés (F16 §10 action A4, NON
> TRANCHÉ) et le Schematron exige un processeur XSLT 2.0 (Saxon → ADR). La conformité RÉELLE est portée
> par **veraPDF + Mustangproject** (conteneur, tier intégration) en **FX04** + la recette **GATE_FACTURX**.

## Integration

Aucun en FX02/FX03 : l'ossature et le sérialiseur CII sont purs (ni base ni I/O ; validation structurelle
+ BR-CO en mémoire). Le scellement PDF/A-3 et sa validation conteneur (veraPDF + Mustangproject, tier
intégration) arrivent en FX04. La recette humaine de bout en bout est portée par GATE_FACTURX.
