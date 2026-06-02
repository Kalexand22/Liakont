# Conventions du dépôt Conformat

> Document de conventions du repo (item SOL03). Source de vérité pour le style de code, le
> nommage, la structure des dossiers, les commits et les PR. Toute règle ci-dessous se justifie
> par `blueprint.md`, `docs/adr/0001-socle-technique.md`, le `.editorconfig` à la racine ou
> `docs/conception/`. Rien n'est inventé.
>
> Documents liés : [module-rules.md](module-rules.md) (frontières inter-modules),
> [testing-strategy.md](testing-strategy.md) (pyramide et catégories de tests).

---

## 1. Conventions C# (`.editorconfig` appliqué)

Le fichier `.editorconfig` à la racine est la source exécutable de ces règles ; il s'applique à
tout le dépôt. Les points saillants :

| Règle | Valeur | Portée | Sévérité |
|---|---|---|---|
| Indentation | 4 espaces (2 pour `.csproj`/`.props`/`.xml`/`.xaml`/`.json`/`.yaml`/`.ps1`) | tous fichiers | — |
| Fins de ligne | `crlf` | tous fichiers | — |
| Encodage | `utf-8` | tous fichiers | — |
| Espaces de fin de ligne | supprimés (sauf `.md`) | tous fichiers | — |
| Saut de ligne final | obligatoire | tous fichiers | — |
| Déclarations de namespace | `file_scoped` (C# 10+, supporté par net48 + `LangVersion latest`) | `.cs` | suggestion |
| Directives `using` | **hors** du namespace (convention .NET Framework classique) | `.cs` | **error** |
| `var` | uniquement quand le type est évident | `.cs` | suggestion |
| Types et membres publics | **PascalCase** | `.cs` | **error** |
| Paramètres et variables locales | **camelCase** | `.cs` | **error** |

> ⚠️ **`.ps1` avec BOM UTF-8.** PowerShell 5.1 corrompt les accents d'un fichier UTF-8 sans BOM.
> Les scripts `.ps1` doivent être enregistrés en UTF-8 **avec** BOM ; toute lecture/écriture de
> fichier en .NET doit spécifier l'encodage UTF-8 explicitement (tasks/lessons.md, 2026-06-02).

### Montants : `decimal`, jamais `float`/`double`

Règle absolue, **non vérifiable par `.editorconfig`**, contrôlée en review : tout montant est un
`decimal`. Un `float`/`double` sur un montant est un **P1** sans exception (blueprint.md §7,
CLAUDE.md). Arrondi commercial half-up à 2 décimales. Voir [module-rules.md](module-rules.md) §5.

## 2. Nommage des projets et des namespaces

- **Projets** : `Gateway.<Module>` (ex. `Gateway.Core`, `Gateway.PaClients.B2Brouter`,
  `Gateway.Adapters.EncheresV6`). Les projets de test suffixent `.Tests`.
- **Namespaces** : préfixés `Conformat.` via `RootNamespace = Conformat.$(MSBuildProjectName)`
  dans `Directory.Build.props`. Le namespace racine d'un projet `Gateway.Core` est donc
  `Conformat.Gateway.Core` (ADR-0001 §5, `Directory.Build.props`).
- **Plug-ins source** : `Gateway.Adapters.<Système>` (ex. `Gateway.Adapters.EncheresV6`).
- **Plug-ins PA** : `Gateway.PaClients.<Plateforme>` (ex. `Gateway.PaClients.B2Brouter`,
  `Gateway.PaClients.SuperPdp`, `Gateway.PaClients.Fake`).

## 3. Structure des dossiers

```
/                          racine de la solution et de l'orchestration
├─ blueprint.md            doctrine d'architecture (à lire en premier)
├─ CLAUDE.md / AGENTS.md   instructions de travail des agents (miroir Codex)
├─ .editorconfig           conventions de code exécutables
├─ Directory.Build.props   propriétés MSBuild communes (net48, plateformes, namespaces)
├─ Directory.Packages.props versions de packages centralisées (CPM)
├─ src/                    code source — un dossier par projet Gateway.*
│  └─ Gateway.sln          la solution
├─ tests/                  projets de test (un par assembly testé + contrat PA)
├─ config/exemples/        configs et table TVA d'EXEMPLE (codes fictifs, pour tests/démo)
├─ deployments/            PARAMÉTRAGE par déploiement client (jamais du code, jamais versionné)
├─ docs/
│  ├─ conception/          specs fonctionnelles F01-F11 (source de vérité produit)
│  ├─ market/              analyses marché et offre commerciale
│  ├─ architecture/        conventions du repo (ce document) + ADR + smoke-checklists
│  └─ adr/                 Architecture Decision Records
├─ tools/                  scripts de vérification et d'orchestration (.ps1)
├─ tasks/                  plans de travail (todo.md) et leçons (lessons.md)
└─ orchestration/          backlog, protocole, blueprints, items
```

La structure cible de `src/` (rôle de chaque dossier de `Gateway.Core/`) est fixée par
blueprint.md §4 et détaillée dans [module-rules.md](module-rules.md).

## 4. Stack et gestion des packages

- **Framework** : `net48` uniquement, projets SDK-style. Plateformes `x86;x64` (x86 par défaut,
  contraignante pour les drivers ODBC Pervasive 32 bits). `TreatWarningsAsErrors = true` :
  un avertissement casse le build (ADR-0001 §1, §2, §4, §6 ; `Directory.Build.props`).
- **Versions centralisées (CPM)** : `Directory.Packages.props` déclare toutes les versions ;
  les `.csproj` référencent un package **sans** attribut `Version` (ADR-0001 §5).
- **Tout nouveau package nécessite un ADR** (blueprint.md §4/§5). La liste autorisée au socle :
  Newtonsoft.Json, Dapper, System.Data.SQLite.Core, MahApps.Metro (produit) ;
  Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio (tests).

## 5. Conventions de commit (Conventional Commits)

Format : `type(scope): sujet`. Le sujet est en français, à l'impératif, sans point final, ≤ 72
caractères. Le corps (optionnel) explique le *pourquoi*.

**Types** : `feat`, `fix`, `refactor`, `test`, `docs`, `ci`, `build`, `chore`.

**Scope** : le segment ou le lot concerné, en minuscules (ex. `socle`, `core`, `piv`, `tva`,
`trk`, `wpf`). En orchestration, le scope est le **segment** et le sujet commence par l'**id de
l'item** suivi d'un tiret cadratin :

```
feat(socle): SOL01 — scaffold Gateway.sln avec 10 produit + 8 projets de test
ci(socle): SOL02 — ajoute la CI GitHub Actions (matrice x86+x64)
docs(socle): SOL03 — documentation d'architecture du repo
```

Ce format est exigé par le protocole d'orchestration (`orchestration/protocol.md`, règle
« Conventional commits »), qui pointe vers ce document. Le message de commit d'un item
d'orchestration est composé par le subagent `orch-commit` puis appliqué tel quel.

## 6. Règles de branches et de PR

Le détail du cycle d'intégration multi-agents est dans `orchestration/protocol.md` ;
les invariants côté dépôt :

- **Branche par segment** : `feat/<segment>` (ex. `feat/socle`, `feat/core-foundation`),
  créée depuis `main`.
- **Sous-branche par item** : `<branche-segment>-<id_item>`, avec un **tiret**, jamais un slash
  — les refs git sont des fichiers : `feat/socle` (fichier) et `feat/socle/SOL01` (qui exigerait
  un répertoire `feat/socle`) ne peuvent pas coexister (tasks/lessons.md, 2026-06-02 ;
  protocol.md Step 3). Exemple : `feat/socle-SOL03`.
- **Merge `--no-ff`** de la sous-branche dans la branche de segment, message
  `merge: <id_item> from slot-<N>`.
- **Jamais de merge dans `main` par un agent.** Une PR de la branche de segment vers `main` est
  ouverte à la gate ; la validation fonctionnelle et le merge dans `main` sont **humains**
  (blueprint.md §10, definition-of-done.md « Pour les gates »).
- **Jamais de force-push.** Toujours un push régulier. Récupération non destructive (jamais de
  `reset --hard`, `stash drop` ni suppression de branche autre que sa propre sous-branche après
  merge réussi) — protocol.md « Rules ».

## 7. `.gitignore` — ce qui n'est jamais versionné

Le `.gitignore` à la racine matérialise des règles de sécurité, pas seulement de propreté :

- **Paramétrage client** : `deployments/*/` est exclu (seul `deployments/README.md` est suivi).
  Une table TVA réelle, un SIREN, une chaîne ODBC ou un compte PA ne sont **jamais** dans le
  dépôt produit (CLAUDE.md règles 7, 15 ; voir [module-rules.md](module-rules.md) §7).
- **Secrets** : `.env`, `*.pfx`, `*.p12`, `**/config.local.json`, `**/*staging-key*` (CLAUDE.md
  règle 10).
- **Bases de tracking** : `*.db`, `*.db-wal`, `*.db-shm`, `*.sqlite` — une base de tracking réelle
  est une piste d'audit client, jamais versionnée.
- **Logs d'outillage** : `.verify-fast.log`, `.run-tests.log`, `.codex-review.log` (consommés par
  les agents, non versionnés) et le transient `.orch-commit-msg`.

## 8. Vérification avant commit

Tout changement passe le pipeline décrit dans [testing-strategy.md](testing-strategy.md) §6 et
`docs/architecture/definition-of-done.md` : `verify-fast.ps1` (build + analyzers + tests
unitaires), puis `run-tests.ps1` si l'item a des tests d'intégration, puis `codex-review.ps1`
(rounds jusqu'à clean). Aucun faux vert n'est toléré (blueprint.md §8).
