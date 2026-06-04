# TRK06 — Ancrage temporel + ArchiveVerifier + export contrôle fiscal

Branche : `feat/core-foundation-TRK06` (slot-3). Spec : `orchestration/items/TRK.yaml` (TRK06), F06.

## Décisions d'architecture
- **ITimestampAnchor** = 4ᵉ axe enfichable à capacités (comme `IArchiveStore`/PA) : on pilote par
  `TimestampAnchorCapabilities`, jamais par `if (anchor is ...)`.
- **RFC 3161** : API natives `System.Security.Cryptography.Pkcs` (aucune dépendance). On horodate la
  tête de chaîne (les 32 octets du `chain_hash`). Appel TSA via couture HTTP `ITsaClient` (testable).
- **OpenTimestamps** : reporté en V1.1 (ADR-0011) — aucune lib .NET mûre et licence-compatible, le
  sous-ensemble Merkle/Bitcoin maison serait non vérifiable pour un produit de conformité. Type présent,
  capacité `IsOperational=false`, lève une `NotSupportedException` française à l'usage (jamais silencieux).
- **NoAnchor** : défaut programmatique (instance sans internet sortant) ; la chaîne de hashes reste
  l'intégrité. RFC 3161 = ancrage RECOMMANDÉ, activé par config d'instance.
- **Preuves** stockées dans le coffre (`IArchiveStore`), indexées par une table append-only/WORM
  `documents.archive_anchors` (migration V006, même discipline que `archive_entries`).
- **ArchiveVerifier** (`IArchiveVerifier`, Contracts) : réutilise `VerifyTenantChainAsync` (contenu+
  chaînage TRK05) + vérifie les preuves d'ancrage → `ArchiveVerificationReport`.
- **Export contrôle fiscal** (`IFiscalControlExportService`) : par document ou par période — paquets +
  rapport d'intégrité + preuves d'ancrage + chronologie (DocumentEvents via `Documents.Contracts`) +
  notice de vérification en français. Période = filtre par préfixe `année/mois/` du `package_path`.

## Étapes
- [ ] Domain : `TimestampAnchorMethod`, `TimestampAnchorCapabilities`, `ITimestampAnchor`,
      `TimestampAnchorResult`, `TimestampVerification`, `NoAnchorTimestampAnchor`,
      `OpenTimestampsTimestampAnchor`.
- [ ] Contracts : `IArchiveVerifier`, `ArchiveVerificationReport`, `ArchiveAnchorVerification`,
      `IFiscalControlExportService`, `FiscalControlExport`, `FiscalExportFile`.
- [ ] Application : `ArchiveVerifier`, `IArchiveAnchorStore`+`ArchiveAnchorRecord`,
      `IArchiveAnchoringService`+`ArchiveAnchoringService`+`AnchoringOutcome`, `ArchiveAnchorLayout`,
      `DailyAnchoringTenantJob`, `DailyAnchoringTrigger`+`DailyAnchoringFanOutHandler`,
      `FiscalControlExportService`. (+ ProjectReference Documents.Contracts, Job.Contracts)
- [ ] Infrastructure : `Rfc3161TimestampAnchor`, `ITsaClient`+`HttpTsaClient`, `TimestampAnchorOptions`,
      `PostgresArchiveAnchorStore`, câblage `AddArchiveAnchoring` dans `ArchiveModuleRegistration`.
- [ ] Migration V006 `documents.archive_anchors` (append-only/WORM) dans Documents.Infrastructure.
- [ ] ADR-0011 (ancrage temporel) + limite NF Z42-013 documentée.
- [ ] Tests Unit : TSA de test réelle (jetons RFC 3161), RFC3161 (sain/altéré), NoAnchor, OTS déféré,
      ArchiveVerifier (chaîne saine/altérée/preuve manquante), service d'ancrage (idempotence),
      tenant job + fan-out handler, export fiscal.
- [ ] Tests Integration : table archive_anchors append-only + verifier sur base réelle.
- [ ] verify-fast, run-tests, codex-review propres.

## Review
(à compléter)
