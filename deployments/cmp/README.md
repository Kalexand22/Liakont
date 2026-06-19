# deployments/cmp — paramétrage du déploiement CMP (Crédit Municipal de Paris)

Seed **versionné** du premier déploiement réel (lot CMP). Ce dossier ne contient **aucune ligne de
code produit** : il prouve que le produit se déploie par **paramétrage**, sans toucher au code
(blueprint.md §2, CLAUDE.md n°7). Un seed concret de client vit ici, **jamais** dans `src/` ni
`config/exemples/`.

## Fichiers (état CMP01)

- [`tva-mapping-cmp-v1.json`](tva-mapping-cmp-v1.json) — table de mapping TVA du CMP, version `cmp-v1`,
  matérialisant les régimes EncheresV6 documentés dans `docs/conception/F03-Mapping-TVA.md` (régimes 5
  et 6, vertical enchères : adjudication / frais). **NON VALIDÉE** (`validatedDate` absente) : le
  garde-fou d'envoi (PIP01b) suspend tout envoi réel tant que l'expert-comptable du CMP n'a pas tranché
  et validé. À l'import, copiée en `mapping-tva.json` à la racine du dossier de seed du tenant.
- [`DECISIONS-FISCALES.md`](DECISIONS-FISCALES.md) — les 4 questions fiscales ouvertes (régime 6,
  TVA sur débits, catégorie d'opération, acheteurs professionnels), leur impact et l'espace pour
  consigner les réponses de l'expert-comptable.

## Fichiers (état CMP02)

- [`tenant-cmp.json`](tenant-cmp.json) — profil du tenant CMP (format `TenantProfileSeed`) : SIREN
  (registre public 267500007), raison sociale, adresse, contact (placeholder), paramètres fiscaux
  **en attente** (`vatOnDebits`/`operationCategory` = `null`), planification (03:00) et seuils
  d'alerte. À l'import, copié en `tenant-profile.json` (le lecteur ne lit que ce nom).
- [`pa-accounts-cmp.json`](pa-accounts-cmp.json) — comptes Plateforme Agréée du CMP : compte
  **B2Brouter staging** pour la démo (clé API en **placeholder**, jamais importée — saisie console),
  emplacement du compte production pré-décrit. À l'import, copié en `pa-accounts.json`.
- [`agent-cmp.json.example`](agent-cmp.json.example) — configuration de l'agent côté CMP : mode
  **fixtures ACTIF** (démo), mode **Pervasive** (ODBC chiffré) documenté pour la production. Secrets
  (apiKey, odbcConnectionString) en placeholders chiffrés.
- [`fixtures-demo/`](fixtures-demo/) — jeu de bordereaux EncheresV6 **fictifs** pour la démo ISATECH
  (vente normale, régime de la marge, avoir, acheteur pro B2B, encaissements) + `pdf-pool/` (PDF
  factices pour la réconciliation). Voir [`fixtures-demo/README.md`](fixtures-demo/README.md).

> **Mapping des noms à l'import** (convention CMP01) : les fichiers versionnés/descriptifs de ce
> dossier sont copiés sous les noms canoniques attendus par le lecteur de seed (assistant OPS03 /
> CFG02 `ImportTenantSeed`) : `tenant-cmp.json` → `tenant-profile.json`, `pa-accounts-cmp.json` →
> `pa-accounts.json`, `tva-mapping-cmp-v1.json` → `mapping-tva.json`. Chaque fichier reste un
> document canonique, simplement renommé — aucune transformation de structure.

## À venir dans ce dossier (lot CMP03)

- `SCENARIO-DEMO-ISATECH.md` — scénario de démo pas à pas.

## Règles

- **Aucune donnée client réelle hors de ce dossier** (un SIREN réel, une table TVA réelle, une chaîne
  ODBC ou un compte PA hors de `deployments/` est un P1 — CLAUDE.md n°15).
- **Aucun secret en clair** : clés API PA et identifiants chiffrés en base (par tenant) ou injectés au
  déploiement (CLAUDE.md n°10) — les seeds ne portent que des placeholders.
- **Aucune règle fiscale devinée** : tout vient de `docs/conception/F03-Mapping-TVA.md` ; une question
  non tranchée reste ouverte dans `DECISIONS-FISCALES.md`, jamais comblée par une valeur inventée.
