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
| `Newtonsoft.Json` | 13.0.4 | **Transport** côté agent (enveloppes de lot/heartbeat — voir avenant RDF15) | `blueprint.md` §3.2/§5. Déclaré au catalogue ; référencé par les items AGT (client HTTP / transport). Le **pivot hashé** passe par le writer canonique manuel (`CanonicalJson`, ADR-0007), **sans** Newtonsoft. |
| `System.Data.SQLite.Core` | 1.0.119 | Buffer local WAL de l'agent | Reprise sur coupure réseau (`blueprint.md` §3.2). Déclaré ; référencé par les items AGT (buffer local + reprise réseau). |
| `Microsoft.NETFramework.ReferenceAssemblies` | 1.0.3 | **Build-only** (`PrivateAssets=all`) | Permet de compiler net48 via le SDK .NET sans pack de ciblage installé (CI / poste sans Visual Studio). Pas une dépendance d'exécution. |
| `StyleCop.Analyzers` | 1.2.0-beta.556 | **Build-only** (`PrivateAssets=all`) | Aligne l'enforcement de style agent avec la plateforme (même version, même suppressions `.editorconfig`). |

## Conséquences

- Tout NOUVEAU paquet agent ultérieur exige un avenant à cet ADR.
- Le contrat `Liakont.Agent.Contracts` reste SANS aucun paquet (netstandard2.0, BCL seul).
- Les versions de tests (xUnit, Test.Sdk, FluentAssertions) sont alignées sur le catalogue
  plateforme (`Directory.Packages.props` racine), couverts par le socle — aucun avenant requis
  pour ces paquets si la version est synchronisée. **⚠️ Prémisse amendée le 2026-06-20 (RDF17) :
  cette exemption supposait des paquets de test gratuits et interchangeables — devenu FAUX pour
  `FluentAssertions` ≥ 8.x (commercial). FluentAssertions est désormais SORTI de l'exemption et
  gouverné par ADR-0031 — voir l'avenant RDF17 ci-dessous.**

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

## Avenant 2026-06-20 (RDF15, RL-PKG-2 + RL-SER-2) — Newtonsoft = transport (pas pivot) + fil v1 PascalCase canonique

### Contexte

Deux petites dettes du contrat agent, sourcées de la redline ADR fondateurs :

1. **Libellé trompeur.** L'ADR (table de décision) qualifiait `Newtonsoft.Json` de
   « sérialisation du **pivot** EN 16931 ». C'est faux : le **pivot** (le payload **hashé**,
   anti-doublon PIV04) est produit par l'**unique** writer canonique manuel de
   `Liakont.Agent.Contracts` (`CanonicalJson` / `CanonicalJsonWriter`, ADR-0007), **sans aucune
   dépendance Newtonsoft**. Newtonsoft ne sert qu'aux **enveloppes de transport NON hashées**
   (lot `PushBatchRequestDto`, heartbeat) et à la traçabilité `SourceData` des adaptateurs. Le
   rôle exact est donc **transport**, jamais l'empreinte d'intégrité.
2. **Patch en retard.** `Newtonsoft.Json 13.0.3` accusait un patch de retard.

### Décision

1. **Bump `Newtonsoft.Json` 13.0.3 → 13.0.4** (`agent/Directory.Packages.props`). Le **plancher**
   de currency (`tools/package-currency-policy.json`, avenant RDF07) est relevé à `13.0.4` et son
   `note` corrigé (« transport », plus « pivot ») — un downgrade sous 13.0.4 échoue désormais la CI.
2. **Libellé corrigé** dans la table de décision (rôle = transport) et dans le commentaire du
   catalogue agent.
3. **Le fil v1 EST le canonique PascalCase, non négociable.** L'agent émet ses propriétés en
   **PascalCase** (`ContractVersion`, `Documents`, …). Côté plateforme, la liaison `System.Text.Json`
   du minimal-API utilise les défauts « Web » (`PropertyNamingPolicy = camelCase` **+**
   `PropertyNameCaseInsensitive = true`) : le fil PascalCase ne se lie que **grâce** à l'insensibilité
   à la casse. Un durcissement futur (camelCase strict, `PropertyNameCaseInsensitive = false`)
   **casserait la liaison SILENCIEUSEMENT** (`Documents` vide, `ContractVersion` null — aucune
   exception). Cette propriété est désormais **documentée comme non négociable** dans
   `docs/architecture/contrat-agent-v1.md` §3.2 et **gardée par un test négatif** (voir §Conséquences).

### Conséquences

- `clients/OnSiteSignature` (ADR-0030) garde sa propre `Newtonsoft.Json` (catalogue distinct, **hors**
  périmètre de cet ADR et de la gouvernance de currency) — non touchée par cet avenant.
- Garde de non-régression : `AgentContractJsonBindingTests` (Host) ajoute un **contrôle négatif** qui
  désérialise le fil PascalCase de référence avec les **mêmes** options réelles que la production mais
  `PropertyNameCaseInsensitive = false`, et prouve que les documents **disparaissent silencieusement**
  (binding dégradé, pas d'exception) — rendant la fragilité VISIBLE en CI. Le fil reste PascalCase
  canonique : on ne change pas la liaison de production, on interdit de la durcir sans casser ce test.

## Avenant 2026-06-20 (RDF17, RL-TR-1) — Prémisse « paquets de test gratuits » caduque : FluentAssertions sorti de l'exemption

### Contexte

La clause §Conséquences « les paquets de test (xUnit, Test.Sdk, FluentAssertions)… aucun avenant
requis » reposait sur une **prémisse implicite** : ces paquets sont **gratuits et interchangeables**.
Cette prémisse est **devenue fausse** pour `FluentAssertions` : depuis janvier 2025, la branche **8.x
est une dépendance commerciale** (licence Xceed), tandis que la 7.x reste gratuite (Apache 2.0). Le
paquet est épinglé `8.2.0` dans **3 catalogues** (racine, agent, OnSiteSignature) et touché par
**739 fichiers** de test — un coût de sortie croissant. Dette **P2** : paquet **build-time only**,
jamais livré, sans impact runtime ni correction fiscale.

### Décision

1. **`FluentAssertions` est SORTI de l'exemption « paquets de test »** d'ADR-0003 : un paquet de test
   à **licence commerciale** n'est plus couvert par la clause « aucun avenant requis ». Sa
   gouvernance (et la décision downgrade 7.x / licence Xceed / migration AwesomeAssertions) est
   portée par **ADR-0031** (préparation de décision DEC-2).
2. **Aucune exécution dans RDF17** : l'item RDF17 ne fait qu'**acter la caducité** et produire l'ADR
   comparatif. Le choix et la migration sont un **item ultérieur après arbitrage Karl (DEC-2)**.
3. **xUnit / Test.Sdk restent dans l'exemption** (gratuits) — seul FluentAssertions en est retiré.

### Conséquences

- Tant que DEC-2 n'est pas tranchée, `FluentAssertions 8.2.0` reste en place — dette P2 **tracée**,
  sans impact runtime.
- À l'exécution (item ultérieur), l'option retenue gouverne sa version dans
  `tools/package-currency-policy.json` et porte sa propre preuve (`verify-fast` + `run-tests`). Voir
  ADR-0031 §Conséquences pour le détail par option.

## Références

- `blueprint.md` §5
- `CLAUDE.md` (checklist post-dev + règle « nouveau package = ADR »)
- `agent/Directory.Packages.props`, `Directory.Packages.props` (racine)
- `tools/package-currency-policy.json`, `tools/lint-package-currency.ps1`, `tools/test-package-currency-lint.ps1`
- Item SOL02, Item RDF07 (avenant, source RL-PKG-1 — `tasks/redline-adr-fondateurs.md`)
- Item RDF15 (avenant, sources RL-PKG-2 + RL-SER-2 — `tasks/redline-adr-fondateurs.md`)
- Item RDF17 (avenant, source RL-TR-1 — `tasks/redline-adr-fondateurs.md`), **ADR-0031** (préparation de décision FluentAssertions, DEC-2)
- `docs/architecture/contrat-agent-v1.md` §3.2 (fil v1 PascalCase canonique), ADR-0007 (writer canonique)
- CVE-2025-6965 (SQLite, fix 3.50.2), CVE-2025-29088 (SQLite, fix 3.49.1)
