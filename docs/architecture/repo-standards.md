# Standards du dépôt Liakont

> Conventions de code, de nommage, de structure, de commit et de PR du dépôt.
> **Sources** (rien n'est inventé ici) : `blueprint.md` v2 (§4 structure, §5 stack, §11 gouvernance),
> `docs/conception/F12-Architecture-Plateforme-Agent.md`, `CLAUDE.md` (règles métier non
> négociables), `docs/architecture/definition-of-done.md`, `orchestration/protocol.md`, le
> `.editorconfig` racine (hérité du socle Stratum vendored) et les `Directory.Build.props` /
> `Directory.Packages.props` des deux solutions. Toute règle ci-dessous renvoie à sa source.
>
> Documents frères : [`module-rules.md`](module-rules.md) (frontières des modules),
> [`testing-strategy.md`](testing-strategy.md) (pyramide et catégories de tests).

---

## 1. Périmètre

Le dépôt porte **deux solutions** (`blueprint.md` §4, §5) :

| Solution | Chemin | Runtime | Plateformes | Rôle |
|---|---|---|---|---|
| **Plateforme** | `src/Liakont.sln` | .NET 10 LTS | AnyCPU | Tout le métier : ingestion, TVA, validation, états, envoi PA, archive, console, supervision |
| **Agent** | `agent/Liakont.Agent.sln` | .NET Framework 4.8 | **x86 ET x64** | Extraction ODBC lecture seule + buffer + push HTTPS + heartbeat (aucune logique métier) |

> `net48` jamais 4.7, jamais .NET moderne ; x86 ET x64 imposés par les drivers ODBC Pervasive
> 32-bit (`blueprint.md` §5). Le contrat partagé `src/Contracts/Liakont.Agent.Contracts`
> (netstandard2.0) est référencé par les **deux** solutions.

---

## 2. Conventions C# (`.editorconfig` racine)

Le `.editorconfig` racine (`root = true`) est **hérité du socle Stratum vendored** et appliqué aux
deux solutions. Les analyzers tournent **en build** (pas en option) — voir §4. Règles structurantes :

| Règle | Valeur | Sévérité |
|---|---|---|
| Namespaces | `file_scoped` (file-scoped namespaces) | **error** |
| Placement des `using` | `inside_namespace` | **error** |
| Types (classe, struct, interface, enum, delegate, event, méthode, propriété) | PascalCase | **error** |
| Paramètres et variables locales | camelCase | **error** |
| Champs privés | `_camelCase` (préfixe `_`) | **error** |
| Interfaces | préfixe `I` + PascalCase | **error** |
| Accolades | toujours (`csharp_prefer_braces`) | warning |
| `var` | proscrit pour les types intégrés, autorisé quand le type est apparent | suggestion |
| `using System.*` en premier | `dotnet_sort_system_directives_first = true` | — |
| Indentation | 4 espaces (`.cs`) ; **2 espaces** pour `.csproj/.props/.targets/.xml/.json/.yaml/.yml` | — |
| Fin de ligne / charset | CRLF / UTF-8 ; espaces de fin supprimés (sauf `.md`) ; newline finale | — |

**Règles StyleCop neutralisées** (par choix de convention, dans le `.editorconfig`) : `SA1633`
(en-tête de fichier non requis), `SA1600`/`SA0001` (doc XML non requise en phase 1), `SA1101`
(pas de `this.`), `SA1309` (en conflit avec `_camelCase`), `SA1601`/`SA1602`. Les projets de test
(`**/Tests.*/**`, `**/Stratum.Tests.*/**`) relâchent en plus `CA1707`, `SA1116`, `SA1117`,
`SA1202`, `SA1210`, `SA1512` (compatibilité des conventions de nommage xUnit).

> **Montants en `decimal`, jamais `float`/`double`** (`CLAUDE.md` n°1, `blueprint.md` §8) : règle
> non exprimable en `.editorconfig`, vérifiée en review (**P1 automatique**). Voir
> [`module-rules.md`](module-rules.md) §9.

### 2.1 Encodage des fichiers (leçon outillage)

- Fichiers `.cs`, `.md`, `.json`, `.yaml` : **UTF-8**.
- Fichiers **`.ps1` : UTF-8 AVEC BOM obligatoire**. PowerShell 5.1 corrompt le parsing d'un `.ps1`
  UTF-8 sans BOM dès qu'il contient un caractère non-ASCII (tiret cadratin, accents). L'outil
  `Write` (réécriture complète) produit de l'UTF-8 **sans** BOM : après tout `Write` d'un `.ps1`,
  restaurer le BOM (`[System.IO.File]::WriteAllText($f, $c, [System.Text.UTF8Encoding]::new($true))`).
  Préférer `Edit` (remplacement partiel) qui préserve l'encodage. Source : `tasks/lessons.md`
  (2026-06-02, 2026-06-03).

---

## 3. Nommage des projets et namespaces

| Catégorie | Préfixe | Exemple | Règle |
|---|---|---|---|
| Code **Liakont** (plateforme) | `Liakont.*` | `Liakont.Host`, `Liakont.Modules.Ingestion` | Tout code propre au produit |
| Socle **vendored** (copie Stratum) | `Stratum.*` | `Stratum.Common.Infrastructure`, `Stratum.Modules.Identity` | **Noms conservés** : provenance + re-convergence NuGet future (`blueprint.md` §4, ADR-0001/0002) |
| Agent | `Liakont.Agent.*` | `Liakont.Agent.Core`, `Liakont.Agent.Adapters.EncheresV6` | Solution net48 séparée |
| Plug-ins PA | `Liakont.PaClients.*` | `Liakont.PaClients.B2Brouter`, `Liakont.PaClients.Fake` | Un assembly par plateforme agréée |
| Contrats inter-modules | suffixe `.Contracts` | `Stratum.Modules.Audit.Contracts` | Seule surface d'accès d'un module à un autre (voir module-rules §3) |
| Contrat agent↔plateforme | `Liakont.Agent.Contracts` | (netstandard2.0, DTOs purs) | Référencé par les deux solutions |

> **Ne jamais renommer un projet `Stratum.*`** : son nom est la clé de traçabilité vers le commit
> source consigné dans `docs/architecture/provenance-socle-stratum.md`.

---

## 4. Gestion des packages et propriétés de build

- **Versions centralisées** : `Directory.Packages.props` (un par solution). Aucune version n'est
  déclarée dans un `.csproj`.
- **Tout nouveau package nécessite un ADR** (les deux côtés — `blueprint.md` §5).
- Propriétés communes (`Directory.Build.props`) :

| Propriété | Plateforme (`src/`) | Agent (`agent/`) |
|---|---|---|
| `TargetFramework` | `net10.0` | `net48` |
| `Platforms` | (AnyCPU) | `x86;x64` |
| `LangVersion` | (défaut SDK) | `latest` |
| `Nullable` | `enable` | `enable` |
| `ImplicitUsings` | `enable` | `disable` (net48) |
| `TreatWarningsAsErrors` | `true` | `true` |
| `EnforceCodeStyleInBuild` | `true` | `true` |
| `AnalysisLevel` | `latest-recommended` | `latest-recommended` |
| Analyzers | `StyleCop.Analyzers` (`PrivateAssets=all`) | idem + `Microsoft.NETFramework.ReferenceAssemblies` |

> Zéro warning toléré : `TreatWarningsAsErrors=true` des deux côtés. Le style est appliqué **dans la
> compilation** (`EnforceCodeStyleInBuild=true`) — un écart au `.editorconfig` fait échouer le build.

---

## 5. Structure du dépôt (`blueprint.md` §4)

```
Liakont/
├─ blueprint.md, CLAUDE.md, AGENTS.md, .editorconfig
├─ src/                       ★ PLATEFORME (.NET 10) — pattern Stratum
│  ├─ Host/Liakont.Host/      Composition root : Blazor + API + enregistrement modules + branding
│  ├─ Common/                 ★ socle Stratum vendored (Abstractions, Infrastructure, UI, Testing)
│  ├─ Modules/                Identity/Job/Notification/Audit + Party.Contracts (vendored, carve D1) + modules métier Liakont
│  ├─ PaClients/              Liakont.PaClients.Fake / .B2Brouter / .SuperPdp
│  └─ Contracts/Liakont.Agent.Contracts/   DTOs du contrat agent↔plateforme (netstandard2.0)
├─ tests/                     Tests plateforme (architecture, unit, integration, contrats, E2E)
├─ agent/                     ★ AGENT (.NET Framework 4.8) — solution séparée
│  ├─ src/                    Liakont.Agent / .Core / .Adapters.EncheresV6 / .Cli / .Installer
│  └─ tests/
├─ deploy/                    docker/ (appliance, compose, realm) + provisioning/ (scripts)
├─ config/exemples/           Exemples FICTIFS (table TVA, paramétrage) pour tests/démo
├─ deployments/<client>/      ★ Paramétrage VERSIONNÉ par client (seed à importer) — jamais dans src/
├─ docs/                      conception (F*), adr, architecture, market, references
├─ orchestration/             manifest, items, blueprints, protocole
├─ tasks/                     pilotage interactif (todo, decisions, lessons, analyses)
└─ tools/                     verify-fast, run-tests, run-e2e, codex-review, orch-state
```

> **Frontière de données** (`blueprint.md` §2 règle 1, `CLAUDE.md` n°7) : aucune donnée d'UN client
> dans `src/` ni `config/exemples/`. Table TVA réelle, SIREN, chaîne ODBC, compte PA → toujours
> `deployments/<client>/` ou paramétrage de tenant en base. `config/exemples/` n'embarque que des
> codes **fictifs**.

> **Orientation cible** (ADR-0005) : l'agent migrera vers un dépôt Git séparé `liakont-agent` et
> `Liakont.Agent.Contracts` sera partagé via NuGet versionné. Jusqu'à la bascule (lots AGT/SOL02),
> l'agent reste sous `agent/` et `verify-fast` build les deux solutions.

---

## 6. Conventions de commit

- **Conventional commits** : `type(scope): sujet` (`orchestration/protocol.md`,
  `definition-of-done.md` ligne 10).
- `type` ∈ `feat`, `fix`, `docs`, `chore`, `test`, `refactor`, `ci`, `build`.
- `scope` = le périmètre touché : nom de module (`ingestion`, `tvamapping`, `validation`,
  `archive`, `identity`…), `orchestration`, `agent`, `ci`, `docs`, `session`.
- Sujet à l'impératif, concis ; le corps explique le **pourquoi** si non trivial.
- Messages opérateur et commentaires métier **en français** (`CLAUDE.md` n°12).

> En orchestration autonome, le message est composé par le sous-agent `orch-commit` (haiku) dans
> `.orch-commit-msg` (gitignoré), puis appliqué par `git commit -F` (voir `orchestration/protocol.md`
> §5b). Hors orchestration, suivre le standard ci-dessus.

---

## 7. Branches, PR et gates (`blueprint.md` §10–11, `orchestration/protocol.md`)

- **Un item = une branche = un objectif** (bornage strict du périmètre).
- **Branches de segment** : `feat/<segment>` (ex. `feat/socle-v6`, `feat/core-foundation`).
- **Sous-branches d'item** : `<branche-segment>-<id>` avec un **tiret**, jamais un slash
  (collision du namespace de refs git : `feat/socle-v6` est un fichier, pas un répertoire — voir
  `tasks/lessons.md` 2026-06-02). Mergées `--no-ff` dans la branche de segment.
- **L'IA ne merge JAMAIS dans `main`** — sauf une **gate d'intégration automatique**
  (`executor` ≠ `human`, blueprint `auto-gate-item`) après verify + tests + review d'intégration
  verts (`orchestration/protocol.md` §5c). Les **gates humaines** s'arrêtent à la création de PR ;
  un humain valide et merge.
- Aucun commit n'est *review-ready* sans une review `codex-review` sur l'arbre de travail courant
  (`-Base` obligatoire en orchestration) ; toute modification après review rouvre le cycle.
- **Jamais de force-push.** Récupération non destructive (jamais `reset --hard`, `stash drop`, ni
  suppression de branche hors nettoyage de sa propre sous-branche après merge-back).

---

## 8. Socle vendored (règle de provenance)

Le socle Stratum (`src/Common/*` + modules Identity/Job/Notification/Audit + le carve
`Party.Contracts`, décision D1) est une **copie** tracée
dans `docs/architecture/provenance-socle-stratum.md` (commit source, date, fichiers copiés). Toute
modification locale d'un fichier `Stratum.*` est autorisée **mais doit être consignée dans ce
fichier** — une modification non consignée est un **P1** en review (`CLAUDE.md` n°11, n°20 ;
`blueprint.md` §4). `verify-fast` doit échouer sur un `Stratum.*` modifié non consigné (SOL03).
