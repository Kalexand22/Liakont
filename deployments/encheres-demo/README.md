# Démo « Enchères » observable — base source EncheresV6 (SQL Server, vraie data)

Base SQL Server SOURCE de démonstration, **reconstruite depuis la vraie base EncheresV6** (Pervasive/Zen `ENT222`)
pour le parcours observable bout-en-bout : **source SQL → agent (ODBC lecture seule) → plateforme → job B4 (marge B2C) → SuperPDP sandbox → console**.

> ⚠️ Base **DÉMO** (noms fictifs côté source, CLAUDE.md n°7). Les SIREN/SIRET sont rendus **valides (Luhn)** côté
> Liakont là où requis (émetteurs + acheteurs B2B). Jamais de donnée client réelle versionnée ici.

## Contenu
- `build-sqlserver-from-samples.ps1` — **générateur** : lit les extractions réelles (`EncheresExtract\samples\*.json`)
  et produit le script SQL Server ci-dessous. Schéma fidèle (vrais noms de tables/colonnes), accents ré-encodés
  (UTF-8 lu comme Win1252), padding CHAR binaire (NUL) nettoyé, gros volumes (`stock_lots`/`requisitions`) filtrés
  sur ce qui est référencé par `ligne_pv`.
- `encheresv6-demo-sqlserver.sql` — **base générée** (~4,5 Mo, 18 tables, ~4 400 lignes de vraie data). À exécuter
  sur une instance SQL Server de démo (schéma `[enc]`).

## Modèle (le détail porte la marge)
- **Vente** : `entete_pv` + `ligne_pv` (pièce maîtresse : 1 ligne/lot adjugé, lie `no_lot`/`no_requi`/acheteur/`no_ba`/`no_bv`/`code_regime_tva`/`prix_total_adjuge`/`total_frais_acheteur`).
- **Acheteur** : `entete_ba` + `lignes_ba` (honoraire acheteur = `montant_frais_ht` + `montant_tva_frais`).
- **Vendeur** : `entete_bv` + `lignes_bv` (honoraire vendeur = `mtt_frais_ht` + `mtt_tva_frais`).
- Lots `stock_lots`, réquisitions `requisitions`, vendeurs `vendeurs_societes`, régimes `Regime_tva`, factures `…facture_clien`/`ligne_facture_client`, honoraires d'inventaire `…notes_hono`, `dossiers_inv`.

## Les 2 sociétés = 2 tenants (`entete_etude`)
| num_entete | Société | Activité | Plage no_ba |
|---|---|---|---|
| **2** | **SVV INNEXA** (Yann Le MOUEL sarl, SIRET 442 593 182, agrément 2002-265) | **Ventes volontaires** (B2C-marge) | 100xxx |
| **1** | **SCP INNEXA** (Maître Yann Le MOUEL, commissaire-priseur, Membre de Drouot) | **Ventes judiciaires** | 2000xxx |

## Marge B2C (décision Karl, ancrée F03 §2.4/§2.5)
Lots au **régime 6 « Non assujetti 20% »** (`Regime_tva.assujetti_tva=false` → déclencheur de la marge, F03 §3),
côté **acheteur particulier** : **marge = honoraire acheteur TTC + honoraire vendeur TTC** (les deux jambes ;
TTC = HT + TVA des colonnes `…frais_ht` + `…tva_frais`), agrégée jour×devise×taux → **TMA1 / rôle SE**.
Régime 5 « Assujetti 20% » = vente **taxable** normale. Cas complets (BA+BV générés) : PV 85 (22 lots), 63, 58…

## Génération / application
```
# (sur la machine ayant les extractions samples\)
powershell -ExecutionPolicy Bypass -File .\build-sqlserver-from-samples.ps1 -SamplesDir "<...>\EncheresExtract\samples"
# puis, sur l'instance SQL Server de démo :
sqlcmd -S <serveur> -d <base> -i encheresv6-demo-sqlserver.sql
```
L'agent se connecte ensuite en ODBC lecture seule (SQL Server) sur cette base.

## Reste à câbler (voir `tasks/plan-demo-encheres-observable.md`)
- **Instance SQL Server** (conteneur dans le stack démo) + exécution de ce script.
- **Agent EncheresV6** : lire ce vrai schéma (`ligne_pv` + `lignes_ba`/`lignes_bv` frais) → pivot ; **2 instances** (volontaire/judiciaire) filtrant par plage `no_ba` → **2 tenants**.
- **Plateforme** : synthèse de la déclaration B2C-marge (honoraires acheteur+vendeur TTC, sans TVA distincte) — l'agent attache les honoraires bruts, la dérivation est plateforme (CLAUDE.md n°6).
- **Console** : vue de la sortie B4 (agrégat TMA1/SE, émission, id SuperPDP) ; instance propre + run observé.
