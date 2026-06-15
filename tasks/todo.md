# Lot FACTURX — Génération Factur-X + PA générique (niveau Essentiel)

Demande Karl (2026-06-15, session interactive). Le Factur-X devient le document que Liakont
**génère** et **transmet** au niveau de service « Essentiel », via une **nouvelle PA générique**.
Routé via l'orchestration : segment `facturx`, manifest v25, PR #49. Spec F16 + ADR-0023 (Accepté).

## Décisions tranchées (Karl, 2026-06-15)
- **Factur-X généré côté Liakont**, profil **EN 16931 / COMFORT** (EXTENDED-CTC-FR en suivi).
- **On ne touche à RIEN de l'existant.** Les plug-ins API (Super PDP, B2Brouter) = cœur du **Pilotage**, inchangés.
- **On ajoute une « PA générique »** : transmet le Factur-X par **email** (pièce jointe MIME) ou **dépôt de fichier** (niveau **Essentiel**).
- **Exutoire = trace de support** : copie du Factur-X transmis gardée ~quelques mois puis purgeable, **tenant-scopée** (n°9).
  **SÉPARÉE de la piste d'audit WORM** : `document_events` (F06) reste append-only, aucune purge.
- **Conformité PDF/A-3** : assertions structurelles en CI rapide + **veraPDF + Mustangproject** (conteneur) au tier intégration ; + test **visuel↔XML**.
- **CII = sérialiseur maison** ; scellement = **QuestPDF** (déjà au repo, pas de nouveau package).

## Constat de départ (vérifié)
- Liakont ne génère ni CII ni PDF/A-3 ; la conversion EN 16931→CII est déléguée au `convert` de Super PDP (PA-spécifique).
- Blueprint §6 : la génération ne dépend d'aucun plug-in PA → **sérialiseur CII maison** obligatoire ; un plug-in PA ne référence que `Transmission.Contracts`.
- Journalisation F06 déjà solide (payload + réponse PA + horodatage + ID PA, append-only). Manque : compte/plug-in PA explicite, granularité par envoi, et l'artefact réellement transmis.
- ⚠️ **Ancrage** : `SendTenantJob.FinalizeIssuedAsync` s'exécute APRÈS la transmission (aval : archive/MarkIssued/purge). La génération doit se faire à l'étape **Sending** (`SendReadyAsync`, avant `SendDocumentAsync`).

## Plan (items — lot FACTURX)

### FX00 — Spike + ADR-0023 (génération Factur-X)  [FAIT — ADR Accepté 2026-06-15]
- [x] Spike libs : QuestPDF (déjà au repo) fait PDF/A-3b + AddAttachment + XMP nativement → pas de nouveau package de scellement.
- [x] Décisions : scellement = QuestPDF ; CII = sérialiseur maison ; validation = veraPDF + Mustangproject (Docker, tier intégration), bloquant.
- [x] `docs/adr/ADR-0023-generation-facturx-pdfa3-cii.md` (Accepté). (Bump QuestPDF → FX02 ; garde-fou Alpine/musl → ADR §5.)

### FX01 — Spec F16 + artefacts de validation FR  [F16 FAIT]
- [x] `docs/conception/F16-FacturX-Generation.md` + entrée d'index.
- [ ] Récupérer Schematron CEN/TC 434 + artefacts FNFE-MPE (BR-FR-*) dans `docs/references/` (action A4 de l'index).

### FX02 — Module plateforme FacturX
- [ ] `src/Modules/FacturX/` (Contracts/Domain/Application/Infrastructure) + port `IFacturXBuilder`.
- [ ] Bump QuestPDF 2024.12.3 → ≥ 2025.7.4 (Directory.Packages.props) + MAJ du commentaire QuestPDF.
- [ ] NetArchTest : QuestPDF référencée **uniquement** par `FacturX.Infrastructure` (INV-FX-1).
- [ ] BoundaryTests : FacturX ne référence aucun plug-in PA ni `Transmission.Contracts`, ne lit aucune `PaCapabilities` (INV-FX-4).

### FX03 — Sérialiseur EN 16931 → CII XML (maison)
- [ ] Domain pur : pivot → CrossIndustryInvoice (EN 16931), `decimal` ; recopie qualitative (catégorie/taux/VATEX + BT-5 devise ; BT-146 PU absent → blocage) ; dérivation BG-23/BT-106/BT-115 + BR-CO-17 ; réconciliation BR-CO-14/15 ; **blocage** si non réconciliable (ne rien inventer).
- [ ] Golden files couvrant la **matrice V1** : mono-taux, multi-taux, exonéré (VATEX), autoliquidation, criée mono-Seller.
- [ ] Validation XSD CII + Schematron CEN/TC 434 sur chaque golden file.

### FX04 — Gabarit PDF + scellement PDF/A-3
- [ ] Rendu visuel depuis le pivot (réutiliser `ArchiveReadableDocument`).
- [ ] Sceller via **QuestPDF** : PDFA_3B + `AddAttachment` relation **`Alternative`** (choix d'interopérabilité, ADR-0023 §3) + `ExtendMetadata` (XMP `fx:`) — isolé derrière `IFacturXBuilder`.
- [ ] Tests : assertions structurelles en CI + **veraPDF + Mustangproject** (conteneur) au tier intégration + **test de cohérence visuel↔XML** (pas de faux vert).

### FXG — Plug-in « PA générique » (Factur-X par email / dépôt de fichier)
- [ ] `src/PaClients/Liakont.PaClients.Generique/` (nom à confirmer) ; ne référence que `Transmission.Contracts` (ni MailKit ni Notification ni FacturX).
- [ ] `SupportsFacturXTransmission` ajoutée au **record `PaCapabilities` ET à l'enum miroir `PaCapability`**.
- [ ] Abstraction `IDocumentDeliveryChannel` (Transmission.Contracts), implémentée au Host :
  - email = **MIME avec pièce jointe** (MimeKit/MailKit Host-only ; `IEmailTransport` socle non réutilisé/non modifié, n°11/20) ; destinataire = boîte PA du tenant ;
  - dépôt de fichier (dossier/montage par tenant ; SFTP = fast-follow + ADR SSH.NET).
- [ ] Reçoit le Factur-X **pré-construit** (contrat IPaClient étendu, FX07) ; ne régénère jamais.
- [ ] **Secrets chiffrés** par tenant (SMTP/SFTP) — jamais en clair (n°10/18). **Existant inchangé**.

### FX06 — Journalisation envoi + trace de support
- [ ] F06 `document_events` **append-only** : compte/plug-in PA, horodatages, hash de l'artefact, réponse, clé d'idempotence — **ADD COLUMN nullable (nouvelle migration, modèle V009), AUCUN backfill UPDATE** (piège trigger append-only). Aucun update/delete.
- [ ] **Trace de support** (store dédié, rétention ~90j configurable, purge OK car NON-audit) : copie du Factur-X transmis, **tenant-scopée (n°9)**, protégée au repos. Distincte du WORM et de l'archive probante (Pilotage).

### FX07 — Intégration pipeline + gate
- [ ] Génération à l'étape **Sending** (`SendReadyAsync`, avant `SendDocumentAsync`), pilotée par capacité ; **PAS** dans `FinalizeIssuedAsync`.
- [ ] **Étendre le contrat `IPaClient`** (Transmission.Contracts) pour porter l'artefact pré-construit (p. ex. `PaSendContext`) : existants l'ignorent, générique l'exige (absent → blocage).
- [ ] Gate FACTURX : recette bout-en-bout (B2C → Factur-X → email/dépôt → journalisé + trace support → veraPDF/Mustang + visuel↔XML + matrice).

## Points mineurs à confirmer
- Store de support : filesystem ? Durée de rétention exacte (proposition : 90 jours, configurable) ?
- Nom du plug-in générique (`Generique` / `FacturXMail` / autre).
- Forme exacte de l'extension du contrat `IPaClient` (paramètre optionnel vs `PaSendContext`).

## Review
_(à compléter en fin de lot)_
