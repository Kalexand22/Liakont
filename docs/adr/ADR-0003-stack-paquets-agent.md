# ADR-0003 — Stack de paquets de l'agent (.NET Framework 4.8)

- **Statut** : Accepté
- **Date** : 2026-06-03
- **Contexte décisionnel** : `blueprint.md` §5 (stack agent), `CLAUDE.md` checklist « No new packages added without ADR », item SOL02

## Contexte

L'agent est une solution .NET Framework 4.8 distincte de la plateforme, avec sa propre gestion
de versions centralisée (`agent/Directory.Packages.props`). `blueprint.md` §5 acte la stack
technique de l'agent (built-in net48 + Newtonsoft.Json + SQLite). Cet ADR rend ces paquets
traçables conformément à la règle CLAUDE.md : tout nouveau paquet exige un ADR.

## Décision

| Paquet | Version | Rôle | Justification |
|---|---|---|---|
| `Newtonsoft.Json` | 13.0.3 | Sérialisation du pivot EN 16931 côté agent | `blueprint.md` §3.2/§5. Déclaré au catalogue ; référencé par les items AGT (client HTTP / transport). |
| `System.Data.SQLite.Core` | 1.0.119 | Buffer local WAL de l'agent | Reprise sur coupure réseau (`blueprint.md` §3.2). Déclaré ; référencé par les items AGT (buffer local + reprise réseau). |
| `Microsoft.NETFramework.ReferenceAssemblies` | 1.0.3 | **Build-only** (`PrivateAssets=all`) | Permet de compiler net48 via le SDK .NET sans pack de ciblage installé (CI / poste sans Visual Studio). Pas une dépendance d'exécution. |
| `StyleCop.Analyzers` | 1.2.0-beta.556 | **Build-only** (`PrivateAssets=all`) | Aligne l'enforcement de style agent avec la plateforme (même version, même suppressions `.editorconfig`). |

## Conséquences

- Tout NOUVEAU paquet agent ultérieur exige un avenant à cet ADR.
- Le contrat `Liakont.Agent.Contracts` reste SANS aucun paquet (netstandard2.0, BCL seul).
- Les versions de tests (xUnit, Test.Sdk, FluentAssertions) sont alignées sur le catalogue
  plateforme (`Directory.Packages.props` racine), couverts par le socle — aucun avenant requis
  pour ces paquets si la version est synchronisée.

## Références

- `blueprint.md` §5
- `CLAUDE.md` (checklist post-dev + règle « nouveau package = ADR »)
- `agent/Directory.Packages.props`
- Item SOL02
