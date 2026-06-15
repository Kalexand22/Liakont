# Scénarios de test — module FacturX

## Unit (`Liakont.Modules.FacturX.Tests.Unit`)

### Confinement de QuestPDF (INV-FX-1)
- `FacturXBoundaryTests.QuestPdf_IsNotReferencedAboveInfrastructure` — Contracts, Domain et
  Application ne référencent JAMAIS l'assembly QuestPDF (réflexion `GetReferencedAssemblies`).
- `FacturXBoundaryTests.QuestPdf_IsReferencedByInfrastructure` — contre-épreuve : la dépendance
  QuestPDF EST présente dans `FacturX.Infrastructure` (sinon le confinement serait vide de sens).
- `FacturXBoundaryTests.ModuleCsprojs_ConfineQuestPdf_AndForbidCrossModuleReferences` (partie a) —
  scan déclaratif : `QuestPDF` n'est déclarée (PackageReference) que par le `.csproj`
  `FacturX.Infrastructure`.

### Indépendance PA et frontière inter-modules (INV-FX-4)
- `FacturXBoundaryTests.FacturX_DoesNotReferenceAnyConcretePaPlugin` — aucune couche FacturX ne
  référence `Liakont.PaClients.*` (réflexion).
- `FacturXBoundaryTests.FacturX_DoesNotDependOnTransmissionNorPaCapabilities` — aucune couche FacturX
  ne référence `Liakont.Modules.Transmission.*` (donc aucune consultation de `PaCapabilities`).
- `FacturXBoundaryTests.FacturX_DoesNotReachIntoAnotherBusinessModule` — aucune couche FacturX ne
  référence un autre `Liakont.Modules.*` (seule dépendance inter-projet : le contrat agent partagé).
- `FacturXBoundaryTests.ModuleCsprojs_ConfineQuestPdf_AndForbidCrossModuleReferences` (partie b) —
  scan déclaratif : un `.csproj` FacturX ne référence que le module lui-même et
  `Liakont.Agent.Contracts` (détecte une `ProjectReference` interdite même non encore exercée).

## Integration

Aucun en FX02 : l'ossature du module n'a ni base ni I/O. La génération réelle (sérialisation CII,
scellement PDF/A-3) et sa validation conteneur (veraPDF + Mustangproject, tier intégration) arrivent
en FX03/FX04 ; les golden files (mono-taux, multi-taux, exonéré VATEX, autoliquidation, criée
mono-Seller) sont livrés par FX03. La recette humaine de bout en bout est portée par GATE_FACTURX.
