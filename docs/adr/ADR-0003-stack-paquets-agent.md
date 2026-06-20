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

## Avenant 2026-06-20 (RDF07, RL-PKG-1) — Politique de rafraîchissement SQLite + currency CI

### Contexte

`System.Data.SQLite.Core 1.0.119` embarque le moteur SQLite 3.46.1, sous **CVE-2025-6965**
(corrigée en SQLite 3.50.2) et **CVE-2025-29088** (corrigée en 3.49.1) ; la branche
`System.Data.SQLite` **1.x n'est plus maintenue (EOL)**. L'atténuation produit est forte : le buffer
local de l'agent est alimenté par l'agent lui-même (reprise réseau), **aucun input réseau hostile**
n'atteint le moteur SQLite. Le risque résiduel est **organisationnel** : un scanner de vulnérabilités
côté client (DSI, étude de sécurité) lèvera ces CVE et peut **bloquer un déploiement**.

### Décision

1. **Cible interop : SQLite >= 3.50.2.** Le paquet SQLite de l'agent doit, à terme, embarquer (ou
   interopérer avec) un moteur SQLite **>= 3.50.2**. Le bump effectif est un **item ultérieur**
   (effort M) : passer à `System.Data.SQLite.Core 2.x` **ne bundle plus le moteur natif** → il faudra
   **packager `SQLite.Interop.dll` par bitness** (x86 **et** x64, contrainte ODBC 32 bits inchangée,
   cf. CI agent). Jusqu'à ce bump, le retard est tracé comme **advisory** (voir §3), pas comme échec
   de build (atténuation produit retenue).
2. **Politique de rafraîchissement.** Les paquets tiers épinglés des deux catalogues centraux
   (`Directory.Packages.props` racine + `agent/Directory.Packages.props`) sont **gouvernés** : chaque
   paquet gouverné porte un **plancher** (`floor`) dans `tools/package-currency-policy.json`. Tout
   épinglage **sous** le plancher (downgrade / régression de currency) **échoue la CI**. Un retard
   connu et accepté (CVE atténuée, EOL) est déclaré en **advisory** : il reste **visible à chaque run**
   (annotation `::warning::`) sans bloquer, jusqu'à sa résolution.
3. **Garde CI de currency.** `tools/lint-package-currency.ps1` (auto-testé par
   `tools/test-package-currency-lint.ps1`, exécuté en CI **et** en `verify-fast`) applique cette
   politique sur les **deux** catalogues. Exit 0 = planchers respectés (alertes possibles) ;
   exit 1 = plancher violé ; exit 2 = configuration de lint cassée (jamais un PASS par défaut).

### Conséquences

- Quand le bump SQLite >= 3.50.2 est livré : **relever le plancher** `System.Data.SQLite.Core` dans
  `tools/package-currency-policy.json` et **retirer l'advisory** correspondant ; documenter le
  packaging `SQLite.Interop.dll` par bitness dans OPS05 (packaging agent).
- Ajouter un paquet à la gouvernance de currency = **édition de données** (`package-currency-policy.json`),
  jamais de code. Un paquet gouverné absent des deux catalogues fait échouer le lint (politique périmée).
- La currency CI n'effectue **aucun appel réseau** (pas de requête NuGet en CI) : elle compare des
  versions épinglées à une politique versionnée — déterministe et hors-ligne.

## Références

- `blueprint.md` §5
- `CLAUDE.md` (checklist post-dev + règle « nouveau package = ADR »)
- `agent/Directory.Packages.props`, `Directory.Packages.props` (racine)
- `tools/package-currency-policy.json`, `tools/lint-package-currency.ps1`, `tools/test-package-currency-lint.ps1`
- Item SOL02, Item RDF07 (avenant, source RL-PKG-1 — `tasks/redline-adr-fondateurs.md`)
- CVE-2025-6965 (SQLite, fix 3.50.2), CVE-2025-29088 (SQLite, fix 3.49.1)
