# ADR-0001 — Socle technique : .NET Framework 4.8, projets SDK-style, builds x86/x64, architecture Service/API mono-écrivain

- **Statut** : Accepté
- **Date** : 2026-06-02
- **Item** : SOL01 (segment `socle`)
- **Références** : `blueprint.md` §3, §4, §9 ; `docs/conception/` ; `orchestration/items/SOL.yaml`

## Contexte

Conformat est une passerelle de conformité facturation électronique **on-premise** déployée chez
des clients dont le parc peut être ancien (Windows 7 SP1 / Server 2008 R2) et dont les pilotes ODBC
des bases legacy (Pervasive) sont fréquemment **32 bits**. Le produit doit rester un EXE simple,
sans dépendance lourde, traitant quelques milliers de documents par an. Ce premier ADR fige les
choix de socle qui conditionnent tous les lots suivants.

## Décision

### 1. Framework : .NET Framework 4.8 uniquement
Cible `net48` (jamais 4.7, jamais .NET 8+). Justification : portée legacy maximale (Windows 7 SP1 /
Server 2008 R2), .NET Framework 4.8 étant le dernier runtime préinstallé ou installable sur ces
systèmes sans dépendance supplémentaire.

### 2. Projets SDK-style ciblant `net48`
Tous les `.csproj` sont au format SDK-style (`<Project Sdk="Microsoft.NET.Sdk">`). On obtient
l'outillage moderne (`dotnet build`, `dotnet test`, restore NuGet, analyzers) tout en livrant un
binaire .NET Framework. La compilation WPF sur `net48` via `<UseWPF>true</UseWPF>` est validée avec
le SDK .NET 10.

### 3. Tests : xUnit
xUnit + `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio`, exécutés par `dotnet test` sur le
runtime net48. Pyramide détaillée dans `testing-strategy.md` (lot SOL03).

### 4. Plateformes : x86 et x64, x86 par défaut de la solution
`<Platforms>x86;x64</Platforms>` (x86 déclaré en premier), chemins de sortie conventionnels
par plateforme (`bin\x86\…`, `bin\x64\…`). Aucune plateforme `AnyCPU` déclarée.
- **x86** est la plateforme contraignante : les pilotes ODBC Pervasive legacy sont souvent 32 bits ;
  le process doit pouvoir tourner en 32 bits. **x86 est livrée et obligatoire.**
- **x64** est également livrée (déploiements modernes, drivers 64 bits), construite explicitement.
- **x86 est aussi la plateforme par défaut de la solution.** `dotnet sln` génère une config solution
  « Any CPU » (qu'on ne peut pas supprimer) ; comme aucun projet ne déclare `AnyCPU`, cette config
  mappe chaque projet vers sa **1re** plateforme déclarée, soit **x86**. Conséquence voulue :
  `dotnet test Gateway.sln` **sans** `/p:Platform` (ce que font `verify-fast.ps1` pour les tests et
  `run-tests.ps1`) se résout en x86 — exactement la plateforme que `verify-fast.ps1` vient de
  construire (`/p:Platform=x86`). Sans cet alignement, le runner chercherait les assemblies dans un
  dossier de plateforme différent du build et échouerait sur un arbre propre (faux rouge de
  vérification). L'ordre `x86;x64` est donc significatif, pas cosmétique.
- **Restauration RID des exécutables.** Un exécutable net48 (`Gateway.Service`, `Gateway.Cli`,
  `Gateway.App`) se voit attribuer un RID (`win-x86`/`win-x64`) selon la plateforme. Or
  `dotnet restore Gateway.sln` se résout sur le défaut de solution (x86) et ne restaure alors que
  les assets `win-x86` ; le motif CI standard « restore une fois → `build --no-restore` par
  plateforme » casserait donc en x64 (`NETSDK1047`). On déclare `<RuntimeIdentifiers>win-x86;win-x64
  </RuntimeIdentifiers>` sur ces trois exécutables : une seule restauration couvre les deux
  plateformes. Les bibliothèques (Core, plug-ins, Api, ApiClient) et les projets de test n'ont pas
  de RID et n'en ont pas besoin.

### 5. Versions de packages centralisées (Central Package Management)
`Directory.Packages.props` (CPM) déclare toutes les versions ; les `.csproj` référencent les
packages **sans** attribut `Version`. `Directory.Build.props` centralise les propriétés communes
(`net48`, `LangVersion latest`, `Platforms`, `TreatWarningsAsErrors`, `RootNamespace` préfixé
`Conformat.`). **Tout nouveau package nécessite un ADR** (blueprint.md §4).

### 6. `TreatWarningsAsErrors = true`
Aucun avertissement n'est toléré : un warning casse le build. Cohérent avec « faux verts interdits »
(blueprint.md §8).

### 7. Frontières de références (composition root unique)
Les références inter-projets matérialisent la doctrine (blueprint.md §2, §6 ; CLAUDE.md règle 6) :

| Projet | Référence | Ne référence jamais |
|---|---|---|
| `Gateway.Core` | (rien) | aucun plug-in (source ni PA) |
| `Gateway.PaClients.*`, `Gateway.Adapters.*` | Core uniquement | un autre plug-in |
| `Gateway.Api` | (rien) | le Core (sinon App l'atteindrait par transitivité) |
| `Gateway.ApiClient` | Api | le Core |
| `Gateway.Service` | Core + Api + tous les plug-ins | — (c'est la **composition root**) |
| `Gateway.App` (WPF) | Api + ApiClient | Core, plug-ins, SQLite |
| `Gateway.Cli` | Core + plug-ins | — (mode secours, accès direct) |

### 8. Architecture Service/API mono-écrivain
Le **Service** est l'unique processus écrivain du Tracking SQLite (CLAUDE.md règle 9). La console
(`Gateway.App`) ne parle qu'à l'API (jamais à la base ni à une PA directement) ; le `Gateway.Cli`
n'écrit qu'en mode secours, Service arrêté, sous mutex global. Ce choix évite les corruptions
concurrentes de la piste d'audit et garde la console comme cliente mince.

## Conséquences

**Positives**
- Compatibilité legacy maximale ; déploiement on-premise simple (un EXE + SQLite).
- Frontières de références vérifiables dès le socle. **Précision :** le compilateur ne casse QUE sur
  un *cycle* (p. ex. `Core → plug-in`, puisque le plug-in référence déjà le Core). Les autres
  violations (`App → Core`, `plug-in → plug-in`) compileraient sans erreur — or ce sont des P1
  (CLAUDE.md règles 6/14). L'invariant est donc gardé par un **test automatisé**
  (`tests/Gateway.Core.Tests/ProjectReferenceBoundaryTests.cs`, exécuté par `verify-fast`) qui lit
  les `ProjectReference` des `.csproj` et échoue sur toute frontière interdite. Cas particulier de
  `App → SQLite` : l'accès base est un **package** (`System.Data.SQLite.Core` / `Dapper`), donc le
  test inspecte aussi les `PackageReference` de `Gateway.App` et échoue si un package de persistance
  y apparaît (la console n'accède aux données que via l'API).
- Outillage moderne sans renoncer au runtime legacy.

**Négatives / limites**
- `net48` ferme la porte aux API récentes de .NET 8+ (assumé : la portée legacy prime).
- La matrice 2 plateformes (x86/x64) double les builds en CI ; assumé (les deux sont livrés).
- Le bon fonctionnement de `verify-fast.ps1` repose sur le fait que la config solution « Any CPU »
  mappe vers x86 : c'est un point fragile que le lot SOL02 fiabilisera en figeant la plateforme
  testée dans verify-fast lui-même (plutôt que de dépendre du défaut de solution).
- WPF sur `net48` en SDK-style impose le SDK .NET récent sur la machine de build (pas le runtime
  cible).

## Alternatives écartées
- **.NET 8+ / `net8.0-windows`** : exclu — incompatible avec la portée Windows 7 SP1 / Server 2008 R2.
- **Projets `.csproj` legacy (non SDK-style)** : exclu — perte de `dotnet build`/CPM/analyzers modernes.
- **AnyCPU seul** : exclu — ne garantit pas le chargement des drivers ODBC 32 bits.
- **Déclarer AnyCPU comme plateforme produit** : exclu — inutile (la config solution « Any CPU »
  mappe déjà vers x86) et trompeur (laisserait croire à un binaire neutre alors que la livraison
  est x86 + x64).
- **Collapse du chemin de sortie (toutes plateformes dans `bin\Debug`)** : envisagé pour aligner
  build et tests, écarté — fragile (casse le restore quand posé dans Directory.Build.props, trop
  tardif dans Directory.Build.targets) ; l'ordre `x86;x64` règle le problème plus simplement.
