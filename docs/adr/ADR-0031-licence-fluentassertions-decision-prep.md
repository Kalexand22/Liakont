# ADR-0031 — Licence FluentAssertions : préparation de décision (downgrade 7.x / licence Xceed / migration AwesomeAssertions)

- **Statut** : Proposé (préparation de décision — **arbitrage opérateur DEC-2 requis avant exécution**)
- **Date** : 2026-06-20
- **Contexte décisionnel** : item RDF17 (source RL-TR-1, redline ADR fondateurs `tasks/redline-adr-fondateurs.md` §3) ; ADR-0003 (stack & paquets agent) dont l'exemption « paquets de test » est amendée par cet ADR ; règle CLAUDE.md « No new packages added without ADR » + règle métier 7 (aucune donnée client dans le code)

## Contexte

`FluentAssertions` est devenu une **dépendance commerciale** à partir de la branche **8.x** : depuis
janvier 2025 son éditeur (Xceed) impose une **licence payante** pour tout usage hors strictement
non-commercial. La branche **7.x** reste sous licence **Apache 2.0 gratuite** (corrections de bugs
critiques uniquement, plus d'évolution fonctionnelle).

Le paquet est épinglé **`8.2.0`** dans **trois** catalogues de versions centralisés :

| Catalogue | Portée | Fichier |
|---|---|---|
| Plateforme (racine) | `.NET 10` — modules Liakont + Host + socle vendored | `Directory.Packages.props:34` |
| Agent | `.NET Framework 4.8` — solution agent | `agent/Directory.Packages.props:25` |
| OnSiteSignature | Client signature sur place (ADR-0030) | `clients/OnSiteSignature/Directory.Packages.props:18` |

**Blast radius mesuré** (fichiers `.cs` portant `using FluentAssertions` au 2026-06-20) :

| Portée | Fichiers |
|---|---|
| **Total dépôt** | **739** |
| dont catalogue racine (hors agent / OnSiteSignature) | 663 (dont 15 fichiers de test du **socle vendored** `Stratum.*`) |
| dont agent | 72 |
| dont OnSiteSignature | 4 |

L'exposition est donc large en nombre de fichiers, mais **build-time only** : `FluentAssertions` est
un paquet de **test**, jamais livré au runtime, sans effet sur le produit, la correction fiscale ni
l'agent en production. C'est pourquoi la dette est **P2** (et non P1) — voir RL-TR-1.

**Prémisse caduque d'ADR-0003.** ADR-0003 §Conséquences exemptait les paquets de test (xUnit,
Test.Sdk, FluentAssertions) d'avenant « si la version est synchronisée », sur la prémisse implicite
que ces paquets sont **gratuits et interchangeables**. Cette prémisse est **devenue fausse** pour
FluentAssertions ≥ 8.x (commercial). Le présent ADR acte cette caducité et sort FluentAssertions de
l'exemption (avenant 2026-06-20 d'ADR-0003).

## Décision

**Cet ADR ne tranche pas l'exécution** : il prépare la décision **DEC-2** (arbitrage Karl). Aucune
migration n'est exécutée dans l'item RDF17 — l'EXÉCUTION est un item ultérieur, **après** arbitrage.
Ci-dessous l'analyse comparée des trois options et la **recommandation**.

### Option (a) — Downgrade vers FluentAssertions 7.x

| Critère | Évaluation |
|---|---|
| **Coût licence** | 0 € (Apache 2.0 gratuit). |
| **Blast radius** | Jusqu'à **739 fichiers** potentiellement impactés : l'API a évolué entre 7.x et 8.x (suppression d'API obsolètes, signatures modifiées). Migration manuelle + revue par fichier touché. |
| **Maintenance** | Branche **gelée** : 7.x ne reçoit que des correctifs critiques, aucune évolution. Cul-de-sac technique à terme. |
| **Risque** | Effort de downgrade **croissant** avec le temps (le nombre de fichiers augmente). Pas de chemin de sortie : on s'enferme sur une branche morte. |

### Option (b) — Acheter la licence commerciale Xceed

| Critère | Évaluation |
|---|---|
| **Coût licence** | **Récurrent**, par développeur et par an (ordre de grandeur communiqué par l'éditeur : **14,95 – 130 $/dev/an** selon l'édition — **à reconfirmer au moment de la décision**, prix éditeur susceptible d'évoluer). |
| **Blast radius** | **0 ligne** : on garde `8.2.0`, aucun changement de code ni de catalogue. |
| **Maintenance** | Évolutions 8.x suivies tant que la licence court. |
| **Risque** | **OPEX récurrent pour un outil build-time only** = mauvais rapport valeur/coût. Suivi de licence à gouverner (renouvellement, nombre de sièges). Ambiguïté du « licensee » en **marque grise** (qui détient la licence : éditeur, intégrateur, client final ?). |

### Option (c) — Migrer vers AwesomeAssertions (fork communautaire)

`AwesomeAssertions` est un **fork communautaire** de FluentAssertions, sous licence **Apache 2.0
gratuite**, conçu comme **remplacement « drop-in »**.

| Critère | Évaluation |
|---|---|
| **Coût licence** | 0 € (Apache 2.0 gratuit, maintenu par la communauté). |
| **Blast radius** | **≈ 3 lignes de catalogue**, à condition de **rester en ≤ v8** : la branche **AwesomeAssertions 8.x est un drop-in de FluentAssertions 8.x et conserve le namespace `FluentAssertions`** → les 739 `using FluentAssertions;` **restent valides sans modification**. Seul l'**id de paquet** change (`FluentAssertions` → `AwesomeAssertions`) dans les 3 catalogues. ⚠️ La **v9 renomme le namespace** en `AwesomeAssertions` (+ assembly `AwesomeAssertions.dll`) — y monter **toucherait les 739 fichiers**. Donc épingler une **8.x pré-9**. |
| **Maintenance** | Fork **activement maintenu**, qui suit l'API moderne 8.x (on **conserve** l'API actuelle, contrairement au downgrade). |
| **Risque** | Risque de **gouvernance projet** (fork communautaire vs éditeur établi) — à pondérer pour un paquet **build-time only**, donc faible. Apache 2.0 (et non MIT) : compatible avec nos usages internes. |

### Synthèse comparative

| | (a) 7.x | (b) Licence Xceed | (c) AwesomeAssertions ≤ v8 |
|---|---|---|---|
| Coût | 0 € | OPEX récurrent /dev/an | 0 € |
| Lignes touchées | jusqu'à 739 fichiers | 0 | ≈ 3 (catalogues) |
| API conservée | non (régression 8→7) | oui | **oui** |
| Pérennité | branche gelée | tant que licence payée | fork maintenu |
| Verrou fournisseur | non | oui (récurrent) | non |

## Recommandation

**Option (c) — migrer vers AwesomeAssertions, épinglé sur la dernière 8.x pré-9**, dans les trois
catalogues (`Directory.Packages.props` racine, `agent/Directory.Packages.props`,
`clients/OnSiteSignature/Directory.Packages.props`).

Justification : c'est la seule option qui cumule **coût nul**, **conservation de l'API 8.x**,
**absence de verrou fournisseur** et un **blast radius minimal (≈ 3 lignes de catalogue)** grâce à la
conservation du namespace `FluentAssertions` en ≤ v8 — au lieu de réécrire 739 fichiers (option a) ou
d'assumer un OPEX récurrent pour un outil build-time only (option b).

- **Repli** : si la gouvernance du fork communautaire est jugée insuffisante au moment de l'arbitrage,
  l'option (a) downgrade 7.x reste un repli **gratuit** (au prix d'un effort de migration et d'une
  branche gelée).
- **Option (b) déconseillée** : OPEX récurrent + ambiguïté du licensee en marque grise pour un paquet
  qui n'est jamais livré.

**Cette recommandation est soumise à l'arbitrage DEC-2 (Karl).** Tant que DEC-2 n'est pas tranchée,
`FluentAssertions 8.2.0` reste en place (dette **P2 tracée**, sans impact runtime).

## Conséquences (à l'EXÉCUTION — item ultérieur, hors RDF17)

Lorsque l'option retenue sera exécutée (item séparé, après DEC-2) :

- **Si (c) AwesomeAssertions** : remplacer l'`id` de paquet `FluentAssertions` par `AwesomeAssertions`
  dans les 3 catalogues, épinglé sur une **8.x pré-9** (namespace `FluentAssertions` préservé →
  aucun `using` à modifier) ; vérifier l'analyzer/assembly de test ; **gouverner la version** dans
  `tools/package-currency-policy.json` (plancher) ; mettre à jour la mention
  `docs/architecture/testing-strategy.md` §Assertions ; ne **jamais** monter en v9 sans un item
  dédié (renommage de namespace = 739 fichiers).
- **Si (a) downgrade 7.x** : aligner les 3 catalogues sur la dernière **7.x**, corriger les usages
  d'API supprimées en 8→7, relever le plancher de currency en conséquence.
- **Si (b) licence** : aucune modification de code ; **acter la licence comme paramétrage de
  déploiement/contrat** (jamais une donnée client dans le code, règle 7) et gouverner le suivi de
  sièges.
- Dans **tous** les cas, l'item d'exécution porte sa propre preuve (`verify-fast` + `run-tests`
  verts sur l'arbre migré) — RDF17 n'exécute rien.

## Références

- Item **RDF17** (source RL-TR-1) — `tasks/redline-adr-fondateurs.md` §2 (ADR-0003, RL-TR-1) et §3
  (time-bombs licence / décision opérateur, DEC-2)
- **ADR-0003** — Stack & paquets agent (exemption « paquets de test » amendée le 2026-06-20, voir
  avenant RDF17)
- `Directory.Packages.props` (racine), `agent/Directory.Packages.props`,
  `clients/OnSiteSignature/Directory.Packages.props`
- `tools/package-currency-policy.json` (gouvernance de currency — à étendre à l'exécution)
- `docs/architecture/testing-strategy.md` §Assertions (mention FluentAssertions, à mettre à jour à l'exécution)
- FluentAssertions 8.x = licence commerciale Xceed (depuis janv. 2025) ; 7.x = Apache 2.0 (gratuit, correctifs critiques)
- AwesomeAssertions — fork communautaire Apache 2.0, drop-in de FluentAssertions ; v8.x conserve le
  namespace `FluentAssertions`, **v9 le renomme** en `AwesomeAssertions` (+ `AwesomeAssertions.dll`)
