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

## À venir dans ce dossier (lots CMP02 / CMP03)

- `tenant-cmp.json` — profil tenant (SIREN, contact, paramètres fiscaux en attente, planification,
  seuils), compte PA (clé en placeholder).
- `agent-cmp.json.example` — configuration de l'agent côté CMP (adaptateur EncheresV6, placeholders).
- `fixtures-demo/` — jeu de bordereaux EncheresV6 pour la démo ISATECH.
- `SCENARIO-DEMO-ISATECH.md` — scénario de démo pas à pas.

## Règles

- **Aucune donnée client réelle hors de ce dossier** (un SIREN réel, une table TVA réelle, une chaîne
  ODBC ou un compte PA hors de `deployments/` est un P1 — CLAUDE.md n°15).
- **Aucun secret en clair** : clés API PA et identifiants chiffrés en base (par tenant) ou injectés au
  déploiement (CLAUDE.md n°10) — les seeds ne portent que des placeholders.
- **Aucune règle fiscale devinée** : tout vient de `docs/conception/F03-Mapping-TVA.md` ; une question
  non tranchée reste ouverte dans `DECISIONS-FISCALES.md`, jamais comblée par une valeur inventée.
