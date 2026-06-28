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

## Orchestration (`demo.ps1`)

`demo.ps1` automatise **uniquement la partie infra déterministe et sans secret** (le reste du parcours
est piloté par la console — voir le runbook ci-dessous, car la création des tenants, la saisie des
secrets PA et l'enrôlement des agents passent par la console, par conception de sécurité) :

| Action | Effet |
|---|---|
| `.\demo.ps1 source` | crée la base `EncheresV6_Demo` (si absente), importe le schéma + données (sauf si déjà présentes ou `-Force`), injecte les SIREN démo (`inject-demo-siren.sql`), crée/maj le **login SQL lecture seule** `liakont_encheres_ro` (`db_datareader`, aucun droit d'écriture — CLAUDE.md n°5), écrit la chaîne ODBC dans `.secrets.local.json` (gitignoré). |
| `.\demo.ps1 agent-config` | génère `agent/volontaire/agent.json` (dossier 2) et `agent/judiciaire/agent.json` (dossier 1) — **gabarits** avec `apiKey`/`odbcConnectionString` à remplacer par leurs valeurs **chiffrées DPAPI** (sur le poste agent), et imprime la chaîne ODBC en clair à chiffrer. |
| `.\demo.ps1 status` | état de la base source (présence des données, répartition régime×dossier, login RO). |

> Le DPAPI est **lié au poste** : la chaîne ODBC et la clé API se chiffrent sur la machine **où tourne
> l'agent** (`Liakont.Agent.Cli.exe encrypt`), jamais ici. `agent/` et `.secrets.local.json` sont gitignorés.

## Seed des tenants (`tenant-seed/`)

Deux jeux de paramétrage **fictifs** (CLAUDE.md n°7), un par tenant — format `ImportTenantSeed` (OPS03) :

- `tenant-seed/volontaire/` — SVV INNEXA, **SIREN fictif `976543215`** (vérifié non attribué, data.gouv).
- `tenant-seed/judiciaire/` — SCP INNEXA, **SIREN fictif `960123453`** (vérifié non attribué, data.gouv).

> **L'identité légale (SIREN + raison sociale ci-dessus) se saisit À LA MAIN** dans la console à la
> création du tenant — elle n'est **jamais seedée** (BUG-14). `tenant-profile.json` ne porte que le
> **paramétrage** (fiscal / planification / seuils).

Chaque dossier porte `tenant-profile.json`, `pa-accounts.json` (compte **`Fake`** Staging, **sans
secret**) et `mapping-tva.json`.

> **`fiscal.operationCategory` = `"PrestationServices"`** (et non `null`, ni `"Mixte"`) : la nature
> d'opération est remplie par la plateforme sur CHAQUE document au read-time, et le job des **documents
> ordinaires** (factures clients / notes d'honoraires) en DÉRIVE le TT-81 — `PrestationServices` → **TPS1**,
> `LivraisonBiens` → **TLB1** ; `Mixte` → **fail-closed** (TT-81 indéterminable). Un OVV qui facture des
> honoraires d'inventaire est dominé par le SERVICE → `PrestationServices` ; les **bordereaux** d'enchères
> (marge/taxable/export), eux, dérivent leur TT-81 de leur PROPRE job (TMA1/TLB1/TNT1, jamais d'`operationCategory`)
> donc ce réglage ne les affecte pas — il leur suffit d'une nature **non nulle** (le CHECK bloque sinon, voir
> `DocumentCheckEvaluator`). ⚠️ **Limite tenant-level** : une rare facture de **biens** (« caisse de vins »)
> serait taguée TPS1 au lieu de TLB1 — l'opérateur la corrige (la nature est par-tenant, pas par-document).
> La fixer ne lève **aucun** garde-fou d'envoi (PIP01 reste actif tant que la table n'est pas validée).
> `vatOnDebits` / `reportingFrequency` restent `null` (vraies décisions EC).

**`mapping-tva.json` — le point qui rend la marge observable.** Le pipeline mappe TOUJOURS en part
`Autre` (`CheckTvaMapping.LinePart` est une constante). La table mappe donc les **codes régime réels**
de la base en part `Autre` :

| Code source | Catégorie | Sens | Source |
|---|---|---|---|
| `6` (« Non assujetti 20 % ») | **E + VATEX-EU-J**, 0 % | **régime de la marge** (enchères B2C) | F03 §2.3 / §3 (décision Karl) |
| `5` (« Assujetti 20 % ») | S, 20 % | adjudication taxable | F03 §2.1 « 20→S » |
| `20` (**taux** effectif) | S, 20 % | **document ORDINAIRE** (facture client / note d'honoraires) taxable | F03 §2.9 |
| `5.5` (**taux** effectif) | S, 5,5 % | document ordinaire au taux réduit | F03 §2.9 |

> Les bordereaux d'enchères sont clés par leur **code régime** (`5`/`6` de `ligne_pv`) ; les **documents
> ordinaires** (factures clients / notes d'honoraires, hors enchères) sont clés par leur **TAUX effectif**
> (`taux_tva` des factures ; `montant_tva/montant_ht` recouvré des notes) — `code_tva` étant NON fiable.
> Clé de taux **unifiée** factures + notes. Un document ordinaire à taux MIXTES (honoraires 20 % + frais
> 0 %) reste **fail-closed** (le `0` n'est pas mappé).

Tous les **autres** régimes (1, 2, 3, 7, 8, 9, 0) restent **non mappés** → le CHECK les **bloque**
(`defaultBehavior: Block`) avec un verdict opérateur : c'est **voulu** (fail-closed, CLAUDE.md n°2/3 — le
caractère marge/hors-champ/exonéré d'un régime non assujetti se tranche régime-par-régime par
l'expert-comptable). Karl complète la table en console pendant la recette. La table est **NON VALIDÉE**
(`validatedDate: null`) : le garde-fou PIP01 refuse tout **envoi réel** tant qu'elle ne l'est pas.

## Runbook — parcours observable bout-en-bout

Pré-requis : Docker Desktop, `sqlcmd` + ODBC Driver 17 for SQL Server, l'agent compilé (net48 Release
x64), et `encheresv6-demo-sqlserver.sql` présent (sinon `build-sqlserver-from-samples.ps1`).

1. **Plateforme propre.** `deployments\bucodi\demo.ps1 reset` → Host `http://localhost:8090`, Keycloak,
   login `sysadmin / Test@1234` (1er login : mot de passe + 2FA). On réutilise le stack Bucodi comme
   **instance propre** (le branding est cosmétique ; base à 0 tenant, realm partagé ADR-0021).
2. **Base source + login lecture seule.** `deployments\encheres-demo\demo.ps1 source`.
3. **Créer les 2 tenants** (console, sysadmin → *Clients* → *Nouveau client*) : `volontaire` et
   `judiciaire` (le provisioning crée base + migrations + `company_id`).
4. **Paramétrer chaque tenant** (console) — source de vérité = `tenant-seed/<inst>/` :
   - *Paramétrage fiscal* → renseigner la **nature d'opération = `PrestationServices`** (sinon le CHECK
     bloque **tous** les documents ; pilote le TT-81 des documents ordinaires → TPS1 — voir l'encadré
     « Seed des tenants ») ;
   - *Paramétrage fiscal* → saisir les règles de mapping : `5`→S 20 %, `6`→E+VATEX-EU-J (bordereaux),
     `20`→S 20 %, `5.5`→S 5,5 % (documents ordinaires factures/notes) ;
   - *Plateforme Agréée* → ajouter le compte **`Fake`** (Staging) ;
   - **publier le SIREN** (action d'onboarding) pour rendre l'envoi exerçable ;
   - **enrôler un agent** → noter sa **clé API**.
5. **Configurer + lancer les 2 agents.** `demo.ps1 agent-config` → chiffrer (sur le poste agent) la
   chaîne ODBC + la clé API (`Liakont.Agent.Cli.exe encrypt`), les coller dans les `agent.json`, copier
   sous `C:\ProgramData\Liakont\<instance>\agent.json`, puis installer/lancer (modèle :
   `deployments\demo-local\install-services.ps1`) ou faire un run ponctuel.
6. **Déclencher B4 + observer.** B4 (« E-reporting B2C de la marge, tous les tenants ») est un job de
   fan-out à **cadence de déploiement** (non planifié automatiquement) : crée sa planification via
   l'**admin des planifications** (console, geste opérateur identique à la prod) avec un cron court
   (ex. `*/2 * * * *`). Puis on observe :
   - **`/traitements`** : le run B4 (docs traités/agrégés) ;
   - **`/documents`** : B2B passants (acheteur à SIREN), bloqués `BUYER_LOOKS_PROFESSIONAL` (société
     sans SIREN), B2C-marge marqués, régimes non mappés bloqués ;
   - l'agrégat de marge est **émis via la PA Fake** (en mémoire, id factice) → **aucune ligne réelle**.

> Le journal des émissions de marge (`pipeline.b2c_margin_emissions` : agrégat jour×devise×taux, état
> Pending→Issued, id PA) n'a **pas encore de page console dédiée** — observable pour l'instant via
> `/traitements` + `/documents`. La vue dédiée est un lot suivant (décision « lean d'abord »).

## Envoi RÉEL SuperPDP (séparé — sur décision)

La démo s'arrête **avant** tout envoi réel. Pour produire une vraie ligne au **sandbox SuperPDP** :
remplacer en console le compte PA `Fake` par **`SuperPdp` (Staging)**, saisir les secrets OAuth2 du
sandbox, **valider** la table de mapping (lever le garde-fou PIP01), puis relancer B4. ⚠️ Cela crée une
**vraie ligne serveur** — à décider explicitement avant de lancer.
