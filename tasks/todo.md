# ADP05 — Extraction des bordereaux PDF EncheresV6 (pièces jointes)

Session : orch-20260605-144059-l2s2 (slot-2, clone Liakont2)
Sous-branche : feat/adapter-encheresv6-ADP05

## Contexte

- Contrat `IExtractor.GetAttachments` / `ListPoolDocuments` + `SourceAttachment` / `PoolDocument`
  + capacités `ProvidesSourceDocuments` / `ProvidesUnlinkedDocumentPool` : DÉJÀ définis (AGT02).
- Consommateur `ExtractionCycle.Run` : DÉJÀ câblé (CollectLinkedPdfs / CollectPoolPdfs selon capacités).
- Aujourd'hui les 2 extracteurs réels (Fixture, Pervasive) renvoient vide + capacités false.
- `PivotDocumentDto.SourceReference` = `"no_ba=<value>"` (EncheresV6RowMapper.SourceRef).

## Décision d'architecture

- Source PDF = abstraction `IEncheresV6PdfSource` injectée dans les 2 extracteurs (la source des PDF
  — dossier de fichiers — est la MÊME que les documents viennent des fixtures ou de l'ODBC).
- Implémentation V1 = système de fichiers (`FileSystemEncheresV6PdfSource`), couvrant mode LIÉ
  (nom de fichier contient le no_ba) ET mode POOL (vrac de PDF du dossier).
- RÉSERVE TRACÉE (cohérente avec la réserve schéma EncheresV6Schema) : la source "blob en base
  Pervasive" n'est PAS implémentée (colonne PDF non documentée, base réelle absente — ne pas inventer
  de colonne, CLAUDE.md n°2). L'abstraction admet une future source blob, résolue à GATE_DEMO_ISATECH.

## Plan d'implémentation

- [ ] `IEncheresV6PdfSource.cs` — abstraction + null-object `None`
- [ ] `EncheresV6PdfSourceOptions.cs` — config filesystem (dossier lié, dossier pool, pattern), validée
- [ ] `FileSystemEncheresV6PdfSource.cs` — implémentation lecture seule (lié + pool, Warning si introuvable)
- [ ] `EncheresV6RowMapper` — extraire `SourceReferencePrefix` (constante partagée, strip dans la source PDF)
- [ ] `EncheresV6FixtureExtractor` — param optionnel `IEncheresV6PdfSource`, capacités calculées, délégation
- [ ] `PervasiveExtractor` — param optionnel `IEncheresV6PdfSource`, capacités calculées, délégation
- [ ] Tests : `FileSystemEncheresV6PdfSourceTests` (présent/absent/multiple/pool/période/dossier manquant/lecture seule)
- [ ] Tests : extracteurs avec source PDF configurée (capacités + délégation) ; défaut inchangé (vide/false)
- [ ] verify-fast + run-tests + codex-review

## Acceptance (rappel)

- Capacités déclarées selon la config (lien explicite et/ou pool)
- Mode lié : GetAttachments retrouve le PDF d'un bordereau par sa référence
- Mode pool : ListPoolDocuments expose les PDF du dossier
- Source PDF configurable dans la config adaptateur
- PDF introuvable = liste vide + Warning, jamais d'échec
- Tests : présent / absent / multiple / pool / PDF factices
