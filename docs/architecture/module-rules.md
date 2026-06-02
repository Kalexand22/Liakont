# Règles inter-modules — les frontières de Conformat

> Document des frontières d'architecture (item SOL03). Il rend explicites et **vérifiables** les
> règles de couplage entre projets. Source : `blueprint.md` §2 et §6, `docs/adr/0001-socle-technique.md`
> §7, `docs/conception/F01-F02` (modèle pivot), CLAUDE.md (« Règles métier non négociables »).
> Aucune règle n'est inventée.
>
> Documents liés : [repo-standards.md](repo-standards.md), [testing-strategy.md](testing-strategy.md).

---

## 1. Le principe : la généricité sur deux axes

Conformat est un **produit générique**, pas un développement spécifique. Le Core ne connaît
**ni les sources ni les Plateformes Agréées (PA)**. Deux axes de plug-ins symétriques l'entourent
(blueprint.md §2) :

- **Plug-ins source** (`IExtractor`) : lisent une base legacy → produisent un modèle pivot.
- **Plug-ins PA** (`IPaClient` + `PaCapabilities`) : envoient vers une plateforme agréée.

Tout couplage qui trahit cette généricité est un **P1** en review (CLAUDE.md règle 6/14/16).

## 2. Frontières de références entre projets

Table normative (blueprint.md §6, ADR-0001 §7). « Référence » = `ProjectReference` dans le `.csproj`.

| Projet | Référence | Ne référence **jamais** |
|---|---|---|
| `Gateway.Core` | (rien) | aucun plug-in (source ni PA) |
| `Gateway.PaClients.*` | Core uniquement | un autre plug-in |
| `Gateway.Adapters.*` | Core uniquement | un autre plug-in |
| `Gateway.Api` | (rien) | le Core (sinon `App` l'atteindrait par transitivité) |
| `Gateway.ApiClient` | Api | le Core |
| `Gateway.Service` | Core + Api + tous les plug-ins | — (c'est la **composition root** unique) |
| `Gateway.App` (WPF) | Api + ApiClient | Core, plug-ins, SQLite |
| `Gateway.Cli` | Core + plug-ins | — (mode secours, accès direct) |

**Énoncé des invariants** (chacun est un P1 s'il est violé) :

1. **Le Core ne référence jamais un plug-in**, ni source ni PA (blueprint.md §2 règle 4).
2. **Un plug-in ne référence que le Core**, jamais un autre plug-in.
3. **La console (`Gateway.App`) ne référence que `Api` + `ApiClient`** — jamais le Core, jamais un
   plug-in, jamais une dépendance de persistance (SQLite/Dapper). Elle parle à l'API HTTP via
   `ApiClient` (blueprint.md §3, CLAUDE.md règle 6).
4. **Le Service est la seule composition root** : lui seul assemble Core + Api + plug-ins.

## 3. Comment la frontière est garantie (et pourquoi le compilateur ne suffit pas)

Le compilateur ne casse **que** sur un *cycle* de références : `Core → plug-in` échoue à la
compilation parce que le plug-in référence déjà le Core. Mais `App → Core` ou `plug-in → plug-in`
**compileraient sans erreur** — or ce sont des P1.

L'invariant est donc gardé par un **test automatisé** exécuté par `verify-fast` :
`tests/Gateway.Core.Tests/ProjectReferenceBoundaryTests.cs` (ADR-0001 §7, Conséquences). Ce test :

- lit les `ProjectReference` de chaque `.csproj` et échoue sur toute frontière interdite du
  tableau §2 ;
- inspecte en plus les `PackageReference` de `Gateway.App` et échoue si un package de persistance
  (`System.Data.SQLite.Core`, `Dapper`) y apparaît — car l'accès base de `App` se ferait par
  package, pas par projet, et la console ne doit accéder aux données que **via l'API**.

> Conséquence pour les agents : ajouter une référence interdite ne sera pas attrapé par le
> compilateur mais par ce test. Ne jamais l'affaiblir ou le désactiver pour « faire passer »
> (faux vert = P1, blueprint.md §8).

## 4. Rôles et interdits par couche (`Gateway.Core`)

blueprint.md §6, complété par F01-F02 pour le pivot :

| Couche | Responsabilité | Interdit |
|---|---|---|
| `Pivot` | Représentation neutre (EN 16931), contrat `IExtractor` | **Tout calcul**, accès réseau, accès fichier |
| `TvaMapping` | Code régime source → catégorie/taux/VATEX via table paramétrée | Règles fiscales en dur, table client embarquée |
| `Validation` | Détecter avant envoi tout ce qui serait rejeté | Correction automatique des données |
| `Tracking` | États, anti-doublons, piste d'audit, coffre d'archive | Purge auto, update/delete d'events, accès hors Service |
| `PaClient` (abstraction) | Contrat `IPaClient` + `PaCapabilities` | Référencer un plug-in concret |
| Plug-ins PA | Implémenter `IPaClient` pour UNE plateforme | Fuiter leurs types hors de leur assembly |
| Plug-ins source | Implémenter `IExtractor` pour UN logiciel | Écrire/verrouiller la base source, voir le Tracking ou les PA |
| `Pipeline` | Orchestration extract → check → send → sync | Logique métier (déléguée aux couches) |
| `Service` | Hôte : ordonnanceur + pipeline + API + accès base | — |
| `App` (console) | UI cliente de l'API | Accès direct base / Core / plug-ins |
| `Cli` | Utilitaire setup/secours | Toute logique métier |

## 5. Règles métier transverses (non négociables)

Ces règles s'appliquent à tout le code et sont des **P1** en review.

### 5.1 Le pivot ne calcule rien
Le pivot porte des montants **déjà calculés par le système source** ; il ne recalcule jamais
(« lire les montants calculés par Magic, ne pas recalculer », F01-F02 §règle 2). Le mapping TVA
est central et paramétré (F3), jamais fait par l'adaptateur (F01-F02 R3).

### 5.2 Montants en `decimal`, jamais `float`/`double`
`decimal` partout, arrondi commercial half-up à 2 décimales. L'adaptateur arrondit **au plus tard**
à la sortie de l'extraction et conserve l'original brut dans `SourceData` (les bases legacy stockent
des flottants sales). Aucune tolérance dans les réconciliations de totaux (BR-CO-15 est fatale).
Source : blueprint.md §7, F01-F02 §règle 1. Un `float`/`double` sur un montant est un P1.

### 5.3 Tout champ absent = `null`, jamais une valeur devinée
L'adaptateur extrait ce qui existe et met `null` sur ce qui manque ; c'est la **Validation (F4)**
qui décide si l'absence est bloquante — jamais l'adaptateur (F01-F02 §règle 3, R4). Aucune valeur
inventée pour combler un trou.

### 5.4 Aucune règle fiscale inventée
Toute catégorie TVA, tout code VATEX, tout seuil vient de `docs/conception/F*.md`. Si la spec ne
tranche pas (régime 6, TVA sur débits, OperationCategory…), l'item passe en `blocked` avec le nom
de la décision manquante et son propriétaire (expert-comptable du client) — **deviner = risque
fiscal client** (CLAUDE.md règle 2, blueprint.md §10/§11).

### 5.5 Bloquer plutôt qu'envoyer faux
Jamais affaiblir une validation `Blocking` en `Warning` pour faire passer un test ou un envoi
(CLAUDE.md règle 3). Un régime non mappé bloque le document ; le comptable complète la table via
la console (blueprint.md §11).

## 6. Capacités, pas types concrets

Aucune fonctionnalité produit ne dépend de ce qu'**UN** PA sait faire. Le comportement est piloté
par les **capacités déclarées** du plug-in (`PaCapabilities`), jamais par :

- un `if (pa is B2Brouter)` ou tout test sur le type concret d'un PA ;
- un flag de configuration produit doublonnant une capacité ;
- une fonctionnalité désactivée « parce qu'un PA ne le supporte pas ».

Si un PA ne supporte pas l'e-reporting de paiement, c'est **le PA** qui est limité (capacité
absente) — le produit, lui, reste complet. Source : blueprint.md §2 règle 2, CLAUDE.md règles 8/16.
Toute violation est un P1.

## 7. Frontière code / paramétrage (aucune donnée client dans le code)

- **`src/`** : produit générique. Aucune donnée d'un client : ni table TVA réelle, ni SIREN, ni
  raison sociale, ni chaîne ODBC, ni compte PA.
- **`config/exemples/`** : configs et tables d'**exemple** à codes **fictifs** uniquement (tests,
  démo).
- **`deployments/<client>/`** : tout le paramétrage réel (table TVA validée par l'expert-comptable,
  SIREN, chaîne ODBC, comptes PA, secrets DPAPI). Exclu du dépôt par `.gitignore` (`deployments/*/`,
  seul `deployments/README.md` est suivi).

Source : blueprint.md §2 règle 1, deployments/README.md, CLAUDE.md règles 7/15. Une donnée client
dans `src/` ou `config/exemples/` est un P1.

## 8. Frontières d'exécution (mono-écrivain, lecture seule, audit immuable)

- **Un seul écrivain sur le Tracking : le Service.** Aucun autre processus n'ouvre la base en
  écriture ; le CLI de secours uniquement quand le Service est arrêté, sous mutex global
  (blueprint.md §3, CLAUDE.md règle 9/17). Un accès en écriture au Tracking hors du Service est un P1.
- **Lecture seule stricte de la base source.** Aucun INSERT/UPDATE/DELETE, aucun verrou, aucune
  transaction d'écriture sur la base du client dans un plug-in source (CLAUDE.md règle 5/13). P1.
- **Piste d'audit et coffre d'archive immuables.** `DocumentEvent` et `MappingChangeLog` sont
  append-only ; le coffre d'archive est WORM. Aucun code d'update/delete, aucune purge automatique
  d'une table d'audit (CLAUDE.md règle 4/12, blueprint.md §6). P1.
- **Secrets chiffrés.** Clé API PA et credentials SMTP : DPAPI (machine scope), jamais en clair
  dans un fichier versionné ou un log (CLAUDE.md règle 10/18). P1.

## 9. Messages opérateur en français

Tout message destiné à l'opérateur (comptable, IT) est en français, avec le numéro de document et
l'action corrective (CLAUDE.md règle 11). Cela vaut pour la validation, le tracking et la console.
