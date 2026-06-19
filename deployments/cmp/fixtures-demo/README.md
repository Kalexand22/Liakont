# fixtures-demo — jeu de démonstration ISATECH (déploiement CMP)

Fixtures **fictives** rejouables par l'agent en mode `Fixture` (adaptateur EncheresV6), pour la démo
ISATECH (lot CMP02 ; scénario pas à pas livré par CMP03). Aucune donnée client réelle : tous les
**acheteurs sont fictifs** (suffixe `(fictif)`). Le seul identifiant réel du déploiement est l'émetteur
(CMP, SIREN 267500007), qui vit dans [`../tenant-cmp.json`](../tenant-cmp.json), **jamais** dans ces
fixtures (l'agent ne porte aucune identité d'émetteur dans la source — F12).

## Contenu

- [`encheresv6-demo.json`](encheresv6-demo.json) — instantané source EncheresV6 (format
  `EncheresV6SourceSnapshot` : `regimes` + `bordereaux`/`lignes`), couvrant tous les cas du scénario.
- `pdf-pool/` — bordereaux PDF **factices** (sans valeur juridique) pour la réconciliation plateforme
  (TRK07 / mode pool de `FileSystemEncheresV6PdfSource`). Chaque nom de fichier contient le `no_ba`
  comme jeton délimité (`bordereau-7001.pdf`, `avoir-7050.pdf`) → rapprochement par numéro de bordereau.

## Cas couverts (scénario ISATECH)

| Bordereau | Cas démontré | Détail |
|---|---|---|
| `7001` (B) | **Vente normale** (régime 5, assujetti 20 %) + **encaissement** | adjudication + frais acheteur taxables ; ligne de règlement (type 3, CB). |
| `7002` (B) | **Régime de la marge** (régime 6) + **encaissement** | adjudication marge (TVA jamais distincte, art. 297 E CGI) ; frais acheteur taxables même si l'adjudication est exonérée (F03 §2.3) ; règlement espèces. |
| `7003` (B) | **Acheteur professionnel (B2B)** | `acheteur_societe` + `acheteur_siren` renseignés (fictif `800000002`, non attribué dans SIRENE) → flux e-invoicing B2B selon les capacités du plug-in PA. |
| `7050` (A) | **Avoir** lettré | `bordereau_ou_avoir = "A"`, `no_ba_lettrage = "7001"` → annulation de la vente `7001`. |

Les régimes de la source (`5`, `6`) correspondent exactement aux régimes de la table de mapping
[`../tva-mapping-cmp-v1.json`](../tva-mapping-cmp-v1.json) — aucun régime de la démo n'est non couvert.

## Garde-fou d'envoi en démo

La table de mapping CMP est **NON VALIDÉE** (en attente expert-comptable, voir
[`../DECISIONS-FISCALES.md`](../DECISIONS-FISCALES.md)) : en démo, le garde-fou d'envoi (PIP01b)
**suspend tout envoi réel** vers la PA. C'est volontaire — la démo montre l'extraction, le pipeline,
les blocages TVA expliqués en français et la console, sans transmettre à l'administration tant que la
table n'est pas validée.

## Mode d'emploi (agent en mode fixtures)

1. Copier ce dossier sur la machine de démo (ex. `C:\ProgramData\Liakont\cmp\fixtures-demo`).
2. Configurer l'agent avec [`../agent-cmp.json.example`](../agent-cmp.json.example) (mode fixtures :
   `extraction.fixturesPath` pointe sur ce dossier ; `extraction.pdfPoolPath` sur `pdf-pool/`).
3. Démarrer l'agent : il extrait les bordereaux fictifs et les pousse à la plateforme de démo.
