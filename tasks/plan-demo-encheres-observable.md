# Plan — Parcours « Enchères » observable bout-en-bout (recette visuelle Karl)

> Décisions Karl : **chaîne réelle complète** (base SQL EncheresV6 → agent ODBC → plateforme → job B4 → UI)
> + **SuperPDP sandbox** (ligne réelle). Instance PROPRE pour l'occasion. Cas dans la console.

## Cible
Sur une instance propre : une base SQL au schéma EncheresV6 (le plus proche d'Enchères SVV) avec un jeu de
données métier couvrant les cas → l'agent l'extrait en ODBC lecture seule → la plateforme ingère → CHECK →
le job B4 agrège la marge B2C → transmission SuperPDP sandbox (ligne réelle) → **on voit chaque cas dans la console**.

## Lots (séquencés)
- [ ] **D1 — Base SQL EncheresV6 + dataset.** `deployments/encheres-demo/` : schéma PostgreSQL `entete_ba`/`lignes_ba`/`Regime_tva` FIDÈLE à `EncheresV6Schema.cs` (mêmes tables/colonnes), login lecture seule, dataset par cas (B2C-marge, B2C taxable, B2B criée, avoir lettré, régime non mappé, multi-lot/taux). **(décision : moteur SQL — reco PostgreSQL in-stack, ODBC psqlODBC.)**
- [ ] **D2 — Agent EncheresV6 réel (câblage B2C-06).** (a) brancher EncheresV6 dans `EmbeddedSourceAdapters` (retirer le placeholder) ; (b) `PervasiveExtractor` (ODBC) validé contre la base SQL ; (c) **fusionner les frais (type 5/2) dans le pivot du document par `no_ba`** + **poser le marqueur de déclaration B2C-marge** (le trou actuel : `ExtractionCycle` n'appelle pas l'extraction des frais, `RowMapper` ne les attache pas). **(décision fiscale : OÙ et SUR QUEL CRITÈRE le marqueur `IsB2cReportingDeclaration` est posé — agent vs plateforme à l'ingestion ; critère = régime marge + frais + acheteur particulier.)**
- [ ] **D3 — Vue console B2C-marge.** Étendre `/demo/b2c` (ou nouvelle page) pour MONTRER la sortie B4 : agrégat jour×devise×taux (TMA1/SE), état d'émission (Pending→Issued + id SuperPDP), lien traçabilité. Page Blazor → test bUnit/Playwright (règle review n°19).
- [ ] **D4 — Instance propre + orchestration + run observé.** `demo.ps1 reset` (clean) → provisioning tenant (vertical enchères ON, seed mapping `encheres`, compte PA SuperPDP sandbox) → base source + agent câblés → run agent → ingestion → CHECK → job B4 → SuperPDP sandbox → observation console. Commandes exactes pour Karl (Karl teste lui-même).

## Réutilisable (cartographié)
- Stack Docker **Bucodi** (`deployments/bucodi/`, `demo.ps1 reset`) = instance propre.
- Schéma + extracteurs EncheresV6 (`agent/src/Liakont.Agent.Adapters.EncheresV6/` : `EncheresV6Schema`, `PervasiveExtractor`, `EncheresV6FixtureExtractor`, `EncheresV6RowMapper`) + fixture 3 bordereaux.
- Seed `config/exemples/tenant-seed/encheres/mapping-tva.json` (régimes 5/6, découpage Adjudication/Frais) + `AuctionVerticalSettings` (per-tenant).
- Pages console `/documents`, `/documents/{id}`, `/traitements`, `/encaissements`, `/supervision`, `/parametrage/fiscal`, `/demo/b2c`.
- Ma chaîne **B4** (job + résolveur + agrégateur + émission + envoi SuperPDP) — prouvée (5/5 tests + id 591).

## Décisions à confirmer (avant D2)
1. **Moteur SQL source** : PostgreSQL in-stack (reco) vs SQL Server (pattern demo-local).
2. **Marqueur B2C-marge** : critère + emplacement (agent attache les frais ; plateforme dérive `IsB2cReportingDeclaration` à l'ingestion sur : régime marge + frais présents + acheteur particulier). À sourcer/valider (fiscal).

## ⚠️ Séparé : finir B4 avant merge
B4+B6+D1 codés + 5/5 tests d'intégration verts, **non committés**. Avant merge : `run-tests` complet + **A6** (`vat_regime` PATCH /companies) + codex-review + commit. Indépendant de la démo.
