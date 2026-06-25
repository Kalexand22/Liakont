# Index des ADR Liakont

Architecture Decision Records **propres à Liakont** (le produit). Les ADR **hérités du socle
Stratum vendored** vivent dans [`socle/`](socle/README.md) et ne sont pas re-décidés ici.

Convention de numérotation : **numéro libre, toutes branches actives confondues** (un numéro
n'est jamais réutilisé une fois attribué sur une branche de travail, même non encore mergée —
voir l'historique des collisions ci-dessous).

| N° | Titre | Statut |
|----|-------|--------|
| [ADR-0001](ADR-0001-pivot-plateforme-agent.md) | Pivot d'architecture : plateforme centralisée multi-tenant + agent léger | Accepté (2026-06-03) |
| [ADR-0002](ADR-0002-empreinte-idp-keycloak-vs-openiddict.md) | Spike d'empreinte de l'IdP : Keycloak vs OpenIddict | Accepté (spike livré) — décision finale d'IdP différée |
| [ADR-0003](ADR-0003-stack-paquets-agent.md) | Stack de paquets de l'agent (.NET Framework 4.8) | Accepté |
| [ADR-0004](ADR-0004-perimetre-contrat-extraction-pivot-v1.md) | Périmètre du contrat d'extraction et du pivot V1 (anti-overfit) | Accepté (2026-06-03) |
| [ADR-0005](ADR-0005-agent-repo-separe-contrat-nuget.md) | L'agent dans un dépôt Git séparé ; contrat partagé via NuGet versionné | Accepté (2026-06-03) — mise en œuvre outillage différée |
| [ADR-0006](ADR-0006-mecanique-jobs-multi-tenant.md) | Mécanique de jobs multi-tenant dans `src/Common` + basculement de tenant derrière une abstraction | Accepté (2026-06-04 ; amendé RDL07/RDL08) |
| [ADR-0007](ADR-0007-serialisation-canonique-pivot.md) | Sérialisation canonique du pivot et empreinte de payload (PIV02) | Accepté (2026-06-04) |
| [ADR-0008](ADR-0008-stockage-pdf-ingestion.md) | Stockage des PDF reçus par l'ingestion (PIV04) | Accepté (2026-06-04) |
| [ADR-0009](ADR-0009-stockage-archive-coffre-worm-fs-s3.md) | Stockage du coffre d'archive WORM : FileSystem + S3-compatible (TRK05) | Accepté (2026-06-04) |
| [ADR-0010](ADR-0010-extraction-texte-pdf-pdfpig.md) | Extraction de texte PDF pour la réconciliation : PdfPig (TRK07) | Accepté (2026-06-04) |
| [ADR-0011](ADR-0011-ancrage-temporel-rfc3161-opentimestamps.md) | Ancrage temporel du coffre : RFC 3161 natif par défaut, OpenTimestamps reporté V1.1 (TRK06) | Accepté (2026-06-04) |
| [ADR-0012](ADR-0012-acquittement-agent-deux-temps-reconciliation-statut.md) | Acquittement agent en deux temps : création + réconciliation par statut | Accepté (2026-06-05) |
| [ADR-0013](ADR-0013-modele-confiance-auto-update-agent.md) | Modèle de confiance de l'auto-update de l'agent | Accepté (2026-06-05) |
| [ADR-0014](ADR-0014-staging-durable-contenu-pivot-intake.md) | Staging durable du contenu à l'intake : la plateforme détient le pivot pour le pipeline | Accepté (2026-06-05) |
| [ADR-0015](ADR-0015-capture-ventilation-tva-snapshot.md) | Capture d'un snapshot requêtable de la ventilation TVA pour l'e-reporting de paiement | Proposé |
| [ADR-0016](ADR-0016-job-tenant-scope.md) | Scope tenant d'un job déclenché depuis la console (action HTTP → worker) | Accepté (acté 2026-06-20, item RDL13 ; mise en œuvre API02a) |
| [ADR-0017](ADR-0017-pont-role-permission-claims-oidc.md) | Pont rôle→permission sous OIDC : population des claims de permission au sign-in | Proposé (2026-06-08) |
| [ADR-0018](ADR-0018-transport-smtp-mailkit.md) | Transport SMTP réel des notifications (MailKit) pour les alertes de supervision | Proposé (2026-06-08) |
| [ADR-0019](ADR-0019-format-paquet-installeur-agent.md) | Format du paquet d'installation de l'agent + transport de la clé de pré-configuration | Accepté |
| [ADR-0020](ADR-0020-topologie-deploiement-idp.md) | Topologie de déploiement de l'IdP (Keycloak par instance, empreinte mesurée) | Accepté |
| [ADR-0021](ADR-0021-realm-keycloak-unique-isolation-par-claim.md) | Realm Keycloak unique partagé : isolation des tenants par claim `company_id` | Accepté (2026-06-13) |
| [ADR-0022](ADR-0022-mandant-tiers-premiere-classe-module-mandats.md) | Le mandant d'autofacturation : tiers de première classe d'un module `Mandats` tenant-scopé | Proposé (2026-06-13) |
| [ADR-0023](ADR-0023-generation-facturx-pdfa3-cii.md) | Génération du Factur-X (PDF/A-3 + CII) côté plateforme | Accepté (2026-06-15) |
| [ADR-0024](ADR-0024-workflow-acceptation-self-billed-gate.md) | Workflow d'acceptation de l'autofacturation : agrégat `SelfBilledAcceptance` + port `ISelfBilledGate` | Proposé (2026-06-15) |
| [ADR-0025](ADR-0025-allocation-bt1-hybride-recle-f06.md) | Allocation hybride du BT-1 en autofacturation (389) et re-clé anti-doublon F06 §4 par mandant | Proposé (2026-06-15) |
| [ADR-0026](ADR-0026-capacite-b-reception-ipainboundclient.md) | Capacité B (réception/rapprochement) : `IPaInboundClient` + agrégat `InboundDocument` (esquisse, phase 2) | Proposé — esquisse (phase 2) |
| [ADR-0027](ADR-0027-abstraction-signature-capacites.md) | Abstraction de signature électronique enfichable à capacités : `ISignatureProvider` (signature optionnelle) | Proposé (2026-06-16) |
| [ADR-0028](ADR-0028-workflow-validation-document-generique.md) | Workflow de validation de document générique : module `Liakont.Modules.DocumentApproval` | Proposé (2026-06-16) |
| [ADR-0029](ADR-0029-plugin-yousign-signature-distance.md) | Plug-in de signature à distance Yousign : provider server-side + webhook durable/idempotent | Proposé (2026-06-16) |
| [ADR-0030](ADR-0030-client-soft-wacom-signature-sur-place.md) | Client soft de signature sur place (Wacom) : capteur desktop hors `agent/` + proxy `OnSiteCapture` | Proposé (2026-06-16) |
| [ADR-0031](ADR-0031-cablage-cycle-run-agent-et-config-par-adaptateur.md) | Câblage du cycle de run de l'agent (AGT02) et canal de configuration par-adaptateur | Accepté (2026-06-14) |
| [ADR-0032](ADR-0032-meta-modele-ged-axes-entites-polymorphes.md) | Méta-modèle GED dynamique : axes typés et entités polymorphes append-only (anti-EAV), module unique `Liakont.Modules.Ged` à trois schémas PostgreSQL | Proposé (2026-06-25) |
| [ADR-0033](ADR-0033-coffre-probant-tiers-sae-5e-axe-option-c.md) | Coffre probant tiers / SAE comme 5ᵉ axe enfichable (`ISealedArchiveProvider`) et archivage WORM des documents GED hors chaîne fiscale (option C ; fast-follow GED20) | Proposé (2026-06-25) |
| [ADR-0034](ADR-0034-canal-ingestion-generique-ged-managed-document.md) | Canal d'ingestion générique GED par agents : `IngestedDocumentDto` / `ManagedDocumentReceivedV1` add-only, registre dédié en base système, `IManagedExtractor` distinct | Proposé (2026-06-25) |
| [ADR-0035](ADR-0035-recherche-index-ged-tsvector.md) | Recherche & index GED : `tsvector` PostgreSQL derrière `IDocumentSearchIndex`, projection asynchrone reconstructible, graphe borné bidirectionnel | Proposé (2026-06-25) |
| [ADR-0036](ADR-0036-journal-consultation-ged-append-only.md) | Journal de consultation GED append-only (`ged_index.consultation_log`, base tenant, WORM) : best-effort par défaut, fail-closed si finalité probante | Proposé (2026-06-25) |

## Collisions de numéro résolues

- **ADR-0023** (résolue le 2026-06-20, item RDL13) : deux fichiers portaient le numéro
  `ADR-0023` — le **câblage du cycle de run de l'agent** et la **génération Factur-X**. Le câblage
  de l'agent a été **renuméroté `ADR-0031`** ; `ADR-0023` reste attribué à la génération Factur-X.
  Les renvois historiques « ADR-0023 D1–D5 / amendé » du câblage de l'agent pointent désormais vers
  `ADR-0031`. (La collision `ADR-0010` du socle Stratum est, elle, documentée dans
  [`socle/README.md`](socle/README.md).)
- **ADR-0031** : deux fichiers portent le numéro `0031` — le **câblage du cycle de run de
  l'agent** ([`ADR-0031-cablage-cycle-run-agent-et-config-par-adaptateur.md`](ADR-0031-cablage-cycle-run-agent-et-config-par-adaptateur.md),
  listé dans l'index) et la **licence FluentAssertions : préparation de décision**
  ([`ADR-0031-licence-fluentassertions-decision-prep.md`](ADR-0031-licence-fluentassertions-decision-prep.md),
  **non listé** dans l'index). C'est pourquoi la numérotation libre de la GED démarre à
  `ADR-0032` (le numéro `0031` ayant été attribué deux fois, il n'est pas réutilisé).
