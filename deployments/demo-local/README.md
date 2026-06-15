# demo-local — sources SQL Server fictives pour tester l'agent en local

Bac à sable **local** servant à prouver l'extraction réelle de bout en bout d'un agent installé
(item AGT02, ADR-0023). **Données 100 % fictives** (CLAUDE.md n°7) : aucune donnée client réelle,
aucun SIREN/compte/chaîne ODBC de production. Ces scripts ne sont **pas** un déploiement client ;
ils créent deux bases *sources* de démonstration que les adaptateurs `DemoErpA`/`DemoErpB` lisent
**en lecture seule stricte** (CLAUDE.md n°5).

## Les deux schémas (volontairement différents)

| | `LiakontDemoErpA` (`DemoErpA`) | `LiakontDemoErpB` (`DemoErpB`) |
|---|---|---|
| Style | ERP normalisé, français | Facturation dénormalisée, anglais |
| Montants | `decimal(18,2)` | `float` (legacy → exerce float→decimal half-up) |
| Acheteur | table `clients` | colonnes en ligne sur `Invoice` |
| Régimes TVA | table `regimes_tva` séparée | code sur la ligne |
| Type de pièce | `FAC` / `AVO` | `I` / `C` |
| Numéros | `A-2026-NNNN` | `B-2026-NNNN` |

## Cas connus seedés (par vague de 6 pièces)

1. Facture simple, régime 20 %
2. Facture multi-lignes mixte (20 % + 10 % + 5,5 %)
3. Facture taux 0
4. Service, régime 10 %
5. **Avoir** référençant la facture (1) d'origine
6. Facture avec une ligne **régime 13 NON mappé** → finit volontairement `Blocked` en console
   (démontre `MAPPING_COVERAGE_MISSING`)

Les régimes 20/10/5.5/0 sont couverts par la table de mapping TVA du tenant `default` déjà seedé
(→ `S`/`AA`/`AA`/`Z`). Le SIREN émetteur attendu (`123456782`) n'est **pas** dans ces bases : il
provient de la config de l'agent (`adapterConfig.emitterSiren`), conforme à F01-F02 §4.3.

## Utilisation

```powershell
# Crée les bases + login lecture seule + 1 vague de données (re-jouable : -Waves N ajoute N vagues)
powershell -ExecutionPolicy Bypass -File deployments/demo-local/seed-demo.ps1 -Waves 1
```

Le script génère un mot de passe aléatoire pour le login lecture seule `liakont_demo_ro`, l'écrit
(avec les chaînes ODBC) dans `.secrets.local.json` **(gitignoré)**, et affiche les chaînes ODBC à
coller (chiffrées DPAPI) dans la config de l'agent à l'installation.

Prérequis : un SQL Server local en **auth mixte** (login SQL), pilote **ODBC Driver 17 for SQL
Server** (32 et 64-bit), `sqlcmd`.

## Fichiers

- `01-create-databases.sql` — bases + login lecture seule (`db_datareader`)
- `02-schema-erpA.sql` / `03-schema-erpB.sql` — schémas
- `04-seed-erpA.sql` / `05-seed-erpB.sql` — seeders re-jouables (numéros auto)
- `seed-demo.ps1` — orchestrateur idempotent
