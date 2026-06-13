# Variante de seed — vertical enchères (lot FIX304)

`mapping-tva.json` de ce dossier est une **variante** de la table de mapping TVA d'exemple, propre au
**vertical enchères**. Elle illustre le découpage **adjudication / frais acheteur** et le **régime de la
marge** (art. 297 A CGI, F03 §2.3).

## Quand l'utiliser

Uniquement si le **vertical enchères est activé** pour le tenant
(`AuctionVerticalSettings.Enabled = true`). Sans le vertical, le pipeline générique mappe toujours en
part `Autre` (voir `CheckTvaMapping.LinePart` / `ConsultedMappingParts`) : les règles `Adjudication` /
`Frais` de cette variante seraient alors signalées **mortes** par le contrôle de cohérence (FIX03,
motif `PartNotConsulted`).

## Seed par défaut

Le seed **par défaut** (vertical OFF) est [`../mapping-tva.json`](../mapping-tva.json) : il couvre les
régimes des documents de démo (`tools/dev-seed-demo-docs.ps1` : 20 / 10 / 5.5 / 0) en part `Autre`, sans
règle morte.

## Comment l'appliquer

L'import lit le `mapping-tva.json` à la **racine** du dossier de seed du tenant (un sous-dossier n'est
jamais importé automatiquement). Pour utiliser cette variante : copier ce fichier à la place de
`../mapping-tva.json` dans le dossier de seed **et** activer le vertical enchères du tenant.

Données **strictement fictives** (codes régimes inventés) — aucune donnée client réelle
(blueprint.md §2, CLAUDE.md n°7/15). Table **NON VALIDÉE** : le garde-fou d'envoi (PIP01) suspend tout
envoi réel tant que l'expert-comptable ne l'a pas validée dans la console.
