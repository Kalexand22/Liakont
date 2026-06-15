# Lot FACTURX — Génération Factur-X + PA générique (niveau Essentiel)

Demande Karl (2026-06-15, session interactive). Le Factur-X devient le document que Liakont
**génère** et **transmet** au niveau de service « Essentiel », via une **nouvelle PA générique**.

## Décisions tranchées (Karl, 2026-06-15)
- **Factur-X généré côté Liakont**, profil **EN 16931 / COMFORT** (EXTENDED-CTC-FR en suivi).
- **On ne touche à RIEN de l'existant.** Les plug-ins API (Super PDP, B2Brouter) = cœur du **Pilotage**, inchangés.
- **On ajoute une « PA générique »** : transmet le Factur-X par **email** ou **dépôt de fichier** (mécanisme du niveau **Essentiel**).
- **Exutoire = trace de support** : copie du Factur-X transmis gardée ~quelques mois puis purgeable.
  **SÉPARÉE de la piste d'audit WORM** : `document_events` (F06) reste append-only, aucune purge.
- **Conformité PDF/A-3** : assertions structurelles en CI rapide + **veraPDF** (conteneur) au tier intégration.

## Constat de départ (vérifié)
- Liakont ne génère ni CII ni PDF/A-3 ; la conversion EN 16931→CII est déléguée au `convert` de Super PDP (PA-spécifique).
- Blueprint §6 : la génération ne dépend d'aucun plug-in PA → **sérialiseur CII maison** obligatoire ; un plug-in PA ne référence que `Transmission.Contracts`.
- Journalisation F06 déjà solide (payload + réponse PA + horodatage + ID PA, append-only). Manque : compte/plug-in PA explicite, granularité par envoi, et l'artefact réellement transmis.

## Plan (items proposés — lot FACTURX)

### FX00 — Spike + ADR-0023 (génération Factur-X)  [SPIKE FAIT — ADR rédigé 2026-06-15]
- [x] Spike libs (recherche web 2026-06-15) : QuestPDF (déjà au repo) fait PDF/A-3b + AddAttachment + XMP nativement → pas de nouveau package de scellement.
- [x] Décisions : scellement = QuestPDF ; CII = sérialiseur maison ; validation = veraPDF + Mustangproject (Docker, tier intégration), bloquant.
- [x] `docs/adr/ADR-0023-generation-facturx-pdfa3-cii.md` rédigé (PROPOSÉ — à valider/merger par Karl).
- [ ] Bump QuestPDF 2024.12.3 → ≥ 2025.7.4 (correctif Mustang/veraPDF) via `Directory.Packages.props`.
- [ ] Garde-fou : appliance sur base glibc (aspnet:10.0 Debian — vérifié) ; jamais Alpine/musl (natif QuestPDF).

### FX01 — Spec F16 + artefacts de validation FR
- [ ] `docs/conception/F16-FacturX-Generation.md` + index.
- [ ] Récupérer Schematron CEN/TC 434 + artefacts FNFE-MPE dans `docs/references/`.

### FX02 — Module plateforme FacturX
- [ ] `src/Modules/FacturX/` (Contracts/Domain/Application/Infrastructure) + port `IFacturXBuilder`.
- [ ] NetArchTest : aucune référence plug-in PA ; lib PDF confinée à `Infrastructure`.

### FX03 — Sérialiseur EN 16931 → CII XML (maison)
- [ ] Domain pur : pivot → CrossIndustryInvoice (EN 16931), `decimal`, recopie stricte catégorie/taux/VATEX,
      **blocage** si champ obligatoire absent (ne rien inventer).
- [ ] Tests : golden files XML + validation XSD CII + Schematron CEN/TC 434.

### FX04 — Gabarit PDF + scellement PDF/A-3
- [ ] Rendu visuel depuis le pivot (réutiliser `ArchiveReadableDocument`).
- [ ] Sceller via **QuestPDF** : PDFA_3B + `AddAttachment` relation **`Alternative`** (spec EN 16931) + `ExtendMetadata` (XMP `fx:`) — isolé derrière `IFacturXBuilder`.
- [ ] Tests : assertions structurelles en CI + veraPDF (conteneur) au tier intégration (pas de faux vert).

### FXG — Plug-in « PA générique » (Factur-X par email / dépôt de fichier)  [NOUVEAU — remplace l'ancien FX05]
- [ ] `src/PaClients/Liakont.PaClients.Generique/` (nom à confirmer) ; ne référence que `Transmission.Contracts`.
- [ ] Transport **email** (SMTP) et **dépôt de fichier** (dossier/SFTP) ; paramétrage par tenant.
- [ ] **Secrets chiffrés** (mot de passe SMTP, identifiants SFTP) — jamais en clair (règle #10/#18).
- [ ] `PaCapabilities` : transmet Factur-X, canaux email/fichier, **pas** de récupération de statut ni de cycle de vie.
- [ ] Le pipeline construit le Factur-X (via `IFacturXBuilder`) et le passe au plug-in **piloté par capacité** (jamais `if (pa is X)`).
- [ ] **Existant inchangé** : Super PDP / B2Brouter ne sont pas modifiés.

### FX06 — Journalisation envoi + trace de support
- [ ] Métadonnées d'envoi dans F06 **append-only** : compte/plug-in PA (colonne explicite), horodatages requête/réponse,
      hash de l'artefact transmis, réponse PA, clé d'idempotence recherchable. Aucun chemin update/delete.
- [ ] **Trace de support** (store dédié, rétention ~quelques mois, purge planifiée OK car NON-audit) : copie du Factur-X transmis.
      Bien distinguer de l'archive probante (Pilotage) et de la piste d'audit WORM.

### FX07 — Intégration pipeline + gate
- [ ] Génération du Factur-X à l'émission quand la PA active le requiert (capacité), derrière `IFacturXBuilder`.
- [ ] Gate FACTURX : recette bout-en-bout (facture B2C → Factur-X → email/dépôt → journalisé + trace support → conformité veraPDF).

## Points mineurs à confirmer
- Store de support : filesystem ? Durée de rétention exacte (proposition : 90 jours, configurable) ?
- Nom du plug-in générique (`Generique` / `FacturXMail` / autre).

## Review
_(à compléter en fin de lot)_
