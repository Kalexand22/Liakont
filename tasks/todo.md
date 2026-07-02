# Lot : rapatriement des PDF BA/BV via les tables GED (recette enchères)

Feu vert Karl 02/07. Branche : feat/recette-encheres-ged-extraction.
Design validé : source PDF « tables GED » lue en ODBC (lecture seule), chemin reconstruit
`<racine>\<No_dossier>\<Année>\<Mois 2 ch.>\<Type_modele>\<Référence>\<Nom_fichier><ext>`,
liaison GED_Relation (Ref_numerique1 = no_ba/no_bv), racine = GED_Param_Document par défaut
avec override agent.json (démo). Prouvé sur base démo : 570/570 PDF trouvés.

## Étapes

- [x] 1. Lire le rail existant — fait. Découvertes clés : sourceReference = `encheresv6:ba:<no>` /
      `encheresv6:bv:<no>` ; transport complet (queue Pdf → POST /documents/{ref}/pdf → IIngestedPdfStore) ;
      BUG de robustesse dans ExtractionCycle (collecte PDF APRÈS le skip anti re-push → PDF tardif/raté
      perdu définitivement) ; flux GED 7 (factures client) a Ref_numerique1=0 → hors périmètre (non sourcé)
- [x] 2. GedTableEncheresV6PdfSource : requête SelectGedLinkedPdfSql (Supprime=0 relation+document,
      scope dossier), chemin racine\dossier\année\mois(2ch.)\type\réf\fichier, override gedPdfRoot,
      garde sous-racine (composante absolue/..), fail-closed ODBC (SourceUnavailableException propagée),
      Warnings données (fichier absent, stockage ≠ D, réf inexploitable)
- [x] 3. Délégation : PervasiveExtractor (capacités reflétées + GetAttachments/ListPoolDocuments délégués,
      param optionnel → NullEncheresV6PdfSource). FixtureExtractor NON modifié (pas d'ODBC en mode
      fixtures — config GED refusée dans ce mode). + Fix ExtractionCycle : collecte PDF AVANT le skip
      document (rattrapage, idempotence par la clé PDF)
- [x] 4. Config : adapterConfig.EncheresV6.gedPdf="tables" + gedPdfRoot (override) ; valeur inconnue,
      root orphelin, ou GED en mode fixtures → AgentConfigException français ; tables GED ajoutées à
      CheckHealth quand la source est active (échec TÔT, angle mort wizard) ; gabarits demo.ps1
- [x] 5. Tests : GedTableEncheresV6PdfSourceTests (9), PervasiveExtractorTests (+2 délégation),
      EncheresV6ExtractorFactoryTests (+5 config), ExtractionCycleTests (+1 rattrapage PDF tardif)
- [x] 6. verify-fast PASS (3 solutions) — 2 reprises en route : ctor internal (CS0051,
      EncheresV6Schema interne) et SA1515/SA1116/CS8619 dans les tests
- [x] 7. Review : round 1 = 2 P2 (couplage demo.ps1 gedPdf↔tables absentes ; trou de test
      SourceUnavailableException) → corrigés (sonde OBJECT_ID dans agent-config + test de
      propagation) ; round 2 = **clean** ; verify-fast re-PASS après fixes
- [x] 8. Backlog CHANTIER GED-PDF mis à jour (fait/reste) ; gabarits demo.ps1 conditionnés à la
      présence réelle des tables GED
- [x] 9. Commit + push sur feat/recette-encheres-ged-extraction

## Review

- Round 1 : 2 P2 — (1) `demo.ps1 agent-config` émettait `gedPdf='tables'` même sur une base
  générée sans tables GED (état supporté) → CheckHealth Unhealthy + cycles avortés en boucle ;
  fixé par sonde `OBJECT_ID('enc.GED_Relation')` avant émission des clés. (2) La propriété de
  sûreté « échec ODBC → SourceUnavailableException propagée, jamais avalée » n'était pas testée ;
  test ajouté (FakeDbException à l'Open, aucun Warning de substitution).
- Round 2 : clean. verify-fast PASS après le dernier changement. 18 tests neufs exécutés verts
  (10 source GED + 7 délégation/factory + 1 rattrapage ExtractionCycle).
- Hors périmètre assumé : flux GED 7 (factures client, Ref_numerique1=0 — liaison non sourcée),
  mode fixtures (pas d'ODBC), wizard d'installation (déclaration dictionnaire = étape d'install
  à venir, notée au backlog).
