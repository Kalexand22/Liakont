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

## Integration

Aucun en FX02 : l'ossature du module n'a ni base ni I/O. La génération réelle (sérialisation CII,
scellement PDF/A-3) et sa validation conteneur (veraPDF + Mustangproject, tier intégration) arrivent
en FX03/FX04 ; les golden files (mono-taux, multi-taux, exonéré VATEX, autoliquidation, criée
mono-Seller) sont livrés par FX03. La recette humaine de bout en bout est portée par GATE_FACTURX.
