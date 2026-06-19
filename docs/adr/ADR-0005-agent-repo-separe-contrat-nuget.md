# ADR-0005 — L'agent dans un dépôt Git séparé ; contrat partagé via NuGet versionné

- **Statut** : Accepté (2026-06-03). Mise en œuvre outillage **différée** (la cible reste valide ;
  la *fenêtre* « scission bon marché » est révolue — voir §Conséquences, réévaluation du 2026-06-20).
- **Date** : 2026-06-03 ; §Conséquences réévaluées le 2026-06-20 (RDL10, redline ADR-0005/0006/0007).
- **Contexte décisionnel** : `blueprint.md` §3-4, `docs/conception/F12` (architecture agent),
  `docs/conception/F13` (installateur + profils intégrateur), `CLAUDE.md` règles 5-6,
  `orchestration/protocol.md`, `tools/verify-fast.ps1`, ADR-0001 (pivot — option B « cross-repo »
  déjà pesée).

## Contexte

La conception actuelle place l'agent dans une **solution séparée mais le MÊME dépôt** que la
plateforme (`agent/` dans le repo `Liakont` — `blueprint.md` §4). Deux mécaniques en dépendent :

- `tools/verify-fast.ps1` build **les deux solutions** du même repo (`src/Liakont.sln` +
  `agent/Liakont.Agent.sln`), avec un mode bootstrap tant qu'une solution n'existe pas.
- `orchestration/protocol.md` suppose un **dépôt unique** (clones, branches `feat/agent`,
  sous-branches, merge-back, `$ORCH_REPO` partagé). ADR-0001 a d'ailleurs rejeté l'option B
  (Host séparé dans le repo Stratum) en notant qu'« une orchestration multi-agents cross-repo
  n'est pas gérée par le protocole ».

Le besoin : faire de l'agent un **dépôt Git distinct**. Justifications, indépendantes du
« par client » :

- **Cycle de vie** différent : net48 (agent) vs net10 (plateforme) ; auto-update de flotte vs
  déploiement d'instance.
- **Surface de sécurité** : l'agent tourne chez le client, près de la base legacy, manipule des
  secrets DPAPI — l'isoler limite le risque croisé.
- **CI dédiée** (net48 x86/x64) et possibilité de confier / auditer le code agent sans exposer
  toute la plateforme.

État réel **au moment de la décision (2026-06-03)** : l'agent était au stade **squelette**
(SOL01/SOL02 — projets `Liakont.Agent.*` + `Liakont.Agent.Contracts` + tests d'architecture), les
lots AGT/ADP/OPS n'étant pas implémentés. La décision a donc tablé sur « rien de substantiel à
déplacer aujourd'hui » et sur une bascule **bon marché**.

> ⚠️ **Cette prémisse est PÉRIMÉE (réévaluation du 2026-06-20, RDL10).** Les segments `agent`,
> `adapter-encheresv6` et la part agent de `deploiement-toolkit` sont livrés : l'agent compte
> aujourd'hui **8 projets de production** (≈ 190 fichiers `.cs`, ≈ 15 900 LOC) — extraction ODBC
> réelle, transport/buffer/auto-update, installeur WinForms, updater et 3 adaptateurs. La fenêtre
> « scission bon marché » est **révolue** : la cible (dépôt séparé + contrat NuGet) reste valide,
> mais la bascule est désormais un **chantier de migration** à planifier explicitement, pas une
> formalité. L'inventaire de couplage exhaustif et la séquence de transition sont en §Conséquences.

## Décision

1. **Un dépôt produit unique séparé** : `liakont-agent`. C'est un PRODUIT partagé par tous les
   clients — **pas** un template forké/cloné par client final.
2. **Contenu** : le cœur générique (`Liakont.Agent`, `Liakont.Agent.Core`, `Liakont.Agent.Cli`,
   `Liakont.Agent.Installer` — F13), l'**updater de flotte** (`Liakont.Agent.Updater`, AGT04 /
   ADR-0013 — il fait partie du produit agent : c'est lui qui applique l'auto-update non négociable
   du point 5) + les **adaptateurs en plug-ins**, un par logiciel source
   (`Liakont.Agent.Adapters.EncheresV6`, futurs Sage/AS400…). Un adaptateur varie selon le
   **logiciel**, jamais selon le client. Les **adaptateurs de démonstration** `DemoErpA`/`DemoErpB`
   (ADR-0023 D5) sont **embarqués dans l'agent** et clairement marqués démo — ils éprouvent le
   câblage `IExtractor` contre de vraies sources ODBC (données fictives sous `deployments/demo-local/`,
   règle n°7) ; ils **restent donc dans le dépôt agent** comme adaptateurs de référence, ils ne sont
   PAS reclassés en fixtures de test (un service installé doit les connaître — ADR-0023 D5).
3. **Le contrat partagé via NuGet versionné** : `Liakont.Agent.Contracts` (netstandard2.0, aucune
   logique) est publié sur un feed privé et **consommé** par l'agent (net48) **et** par le module
   `Ingestion` de la plateforme (net10), au lieu d'une référence projet intra-repo. La
   non-régression des deux côtés reste garantie par les **golden files de contrat** (`blueprint.md`
   §3.2).
4. **Trois axes de variabilité distincts** (à ne jamais confondre) : **profil intégrateur**
   (branding/visibilité, installateur — F13) ; **config tenant** (ODBC, SIREN, compte PA, clé API —
   `deployments/<client>/` + DPAPI) ; **adaptateur** (logiciel source — plug-in). « Générique par
   intégrateur » : un `.exe` paramétré par profil sert à installer chez N clients ; la config
   tenant est saisie à l'installation, jamais embarquée.
5. **Ce qui NE varie pas** : le cœur de l'agent. Patché une fois pour toute la flotte (auto-update)
   — invariant non négociable pour un produit de conformité fiscale (un bug d'extraction non
   propagé = client en non-conformité silencieuse).

## Conséquences

**Positif** : isolation de sécurité, CI dédiée, cycles de vie découplés, auditabilité du code agent.

> **Réévaluation du 2026-06-20 (RDL10).** La §Conséquences ci-dessous a été **réécrite** après que
> les segments agent ont été livrés. La cible (dépôt séparé + contrat NuGet) est inchangée ; ce qui
> change, c'est l'**ampleur de la bascule** : ce n'est plus une formalité de squelette mais un
> chantier de migration dont l'inventaire de couplage doit être **exhaustif et à jour** sous peine
> de faux-vert (deux ancres de hash SHA-256 qui divergent, preuve net48 perdue). Les **gardes
> exécutables** correspondantes (pureté déclarative du contrat, anti-dérive des chemins
> cross-solution, comptage des golden côté agent) sont portées par **RDL11** ; le déplacement de
> `PivotReconciliation` hors du paquet publiable par **RDL12**.

### Inventaire de bascule (état au 2026-06-20 — à re-vérifier au démarrage effectif du chantier)

**1. Code agent à déplacer (≈ 190 fichiers, ≈ 15 900 LOC) — 8 projets de production**, aujourd'hui
sous `agent/src/` :

| Projet | Rôle | Va au dépôt agent |
|---|---|---|
| `Liakont.Agent` | hôte/service Windows | oui |
| `Liakont.Agent.Core` | extraction, buffer, push, cycle de run | oui |
| `Liakont.Agent.Cli` | ligne de commande | oui |
| `Liakont.Agent.Installer` | installeur WinForms + profils intégrateur (F13) | oui |
| `Liakont.Agent.Updater` | auto-update de flotte (AGT04 / ADR-0013) | oui (produit, point 5) |
| `Liakont.Agent.Adapters.EncheresV6` | adaptateur ODBC réel | oui (plug-in) |
| `Liakont.Agent.Adapters.DemoErpA` | adaptateur **démo** embarqué (ADR-0023 D5) | oui (réf. démo, pas fixtures) |
| `Liakont.Agent.Adapters.DemoErpB` | adaptateur **démo** embarqué (ADR-0023 D5) | oui (réf. démo, pas fixtures) |

Plus les 7 projets de tests agent (`agent/tests/`) et leur outillage (`agent/tests/_shared/`).

**2. Couplages cross-solution `agent/` → dépôt plateforme à défaire (recensement complet)** —
sous-comptés par la rédaction d'origine (« 1 ProjectReference ») ; le réel est :

- **6 `ProjectReference` vers `src/Contracts/Liakont.Agent.Contracts`** (chemin
  `..\..\..\src\Contracts\...`) : 2 en production (`Liakont.Agent.Core`, `Liakont.Agent.Cli`) +
  4 en tests (`*.Adapters.DemoErpA.Tests`, `*.Adapters.DemoErpB.Tests`, `*.Adapters.EncheresV6.Tests`,
  `*.Core.Tests`). La bascule substitue **une `PackageReference` NuGet** (`Liakont.Agent.Contracts`,
  feed privé) à **chacune** de ces 6 références, et adapte les tests d'architecture qui codent en dur
  le chemin `src/Contracts/...` + l'hypothèse « la plateforme n'est pas publiée en paquet »
  (`AgentProjectReferenceTests`, `ContractsPurityTests`).
- **5 `Compile`-link cross-solution vers `tests/_shared/contract-v1/*.cs`** dans
  `Liakont.Agent.Core.Tests` — dont le **lecteur canonique de référence** `PivotCanonicalReader.cs`
  et les golden tests (`PivotContractGoldenTests.cs`, `ContractFixtures.cs`, `ContractFixtureTests.cs`,
  `CanonicalEnumGuardTests.cs`). C'est l'ancre qui prouve **net48** que l'agent et la plateforme
  partagent le MÊME hash de contrat.
- **1 copie de fixtures cross-solution** : `None Include="..\..\..\tests\fixtures\contrat-v1\*.json"`
  (`CopyToOutputDirectory`) dans `Liakont.Agent.Core.Tests` — les golden `.json` figés (empreintes
  SHA-256 de référence).

**3. Décision — vecteur de partage cross-repo du contrat de hash (à acter AVANT la bascule).**
À la scission, les liens `Compile`/`None` ci-dessus ne peuvent plus pointer dans l'autre dépôt.
Deux issues seulement, dont une seule est acceptable :

- ❌ **Dupliquer** le lecteur + les golden dans le dépôt agent → deux ancres SHA-256 qui **divergent
  silencieusement** : exactement le faux-vert que `F12 §3.4` interdit. **Rejeté.**
- ✅ **Partager via paquet** : publier un `Liakont.Agent.Contracts.TestKit` (NuGet, `contentFiles` :
  le lecteur `PivotCanonicalReader.cs`, les golden tests et les fixtures `.json`) **consommé des deux
  côtés** (net48 agent + net10 plateforme). Source UNIQUE des golden ; un changement de contrat casse
  les deux côtés au même endroit. **Vecteur retenu** (à spécifier au chantier ; cf. garde RDL11 qui
  empêche le lien de « disparaître » silencieusement du run agent).

**4. Séquence de transition `verify-fast` / CI (codent le mono-repo en dur aujourd'hui).** À la
bascule, dans l'ordre, en gardant la **preuve de hash net48 re-jouée** à tout moment :

1. Publier `Liakont.Agent.Contracts` (+ le `.TestKit`) sur le feed privé depuis la plateforme.
2. Dépôt agent : remplacer les 6 `ProjectReference` et les liens `_shared`/fixtures par les
   `PackageReference` correspondantes ; CI net48 x86/x64 dédiée (aujourd'hui le **job `agent:`** de
   `.github/workflows/ci.yml` — build x86 + x64 + tests `ci-test.ps1` + self-tests packaging).
3. Dépôt plateforme : **retirer le Step 4** de `tools/verify-fast.ps1` (build/test
   `agent/Liakont.Agent.sln`) **et** le job `agent:` de la CI ; sortir `SOL02` du gardien bootstrap
   (`Test-SolItemPending`) — sinon la garde anti faux-vert exigera une solution agent qui n'est plus là.
4. La plateforme **continue de re-jouer** la preuve de hash via le golden empaqueté (`.TestKit`
   consommé par un test net10 côté `src/`), pour que le retrait du build agent local ne supprime PAS
   la preuve net48↔net10 (la preuve vient désormais du paquet, plus du lien de fichier).

**5. Orchestration multi-repo (`orchestration/protocol.md`).** Étendre l'orchestration au multi-repo
(quel dépôt pour quel segment, `build-agent-context`, branches, merge-back, partage `$ORCH_REPO`) —
le point qu'ADR-0001 signalait non géré. Les segments **entièrement agent** — `agent` (lots `AGT`) et
`adapter-encheresv6` (lots `ADP`) — **ciblent le dépôt agent** : matérialisé par un champ **`repo:` au
niveau segment**, désormais **réservé/spécifié dans `MANIFEST-CONVENTIONS.md`** (défaut = dépôt
plateforme courant ; à renseigner sur ces deux segments au chantier).
⚠️ **`deploiement-toolkit` est un cas MIXTE, pas un segment-cible** : ses lots `[OPS, BRD, DOC]`
mêlent des items **agent** (OPS05 packaging agent, OPS08a/b/c installeur WinForms) et des items
**plateforme** (OPS03 assistant tenant, OPS01a appliance, OPS07 publication de versions). Un `repo:`
unique au niveau segment ne peut PAS exprimer ce partage : il **doit d'abord être scindé** (part agent
/ part plateforme) au chantier de migration AVANT qu'on puisse y router un `repo:` — il n'est donc PAS
listé comme cible `repo: liakont-agent` tant que ce découpage n'est pas tranché. Chantier à valider
avec l'humain, à ne pas improviser.

**Garde-fous de cet ADR** :

- Il **acte la cible**, il ne **déplace pas** le code agent et **ne crée pas** le dépôt. Aucune
  incohérence n'est introduite : tant que la bascule n'est pas faite, l'agent reste dans `agent/` et
  `verify-fast` continue de builder les deux solutions.
- La réévaluation du 2026-06-20 (RDL10) est **documentaire** : elle met l'inventaire à jour et tranche
  le vecteur de partage, elle ne **déclenche pas** la migration ni ne modifie l'orchestration runtime
  (`manifest.yaml`, `state.yaml`, `protocol.md`). Les gardes exécutables sont RDL11/RDL12.

### Placement de `PivotReconciliation` (RDL12, 2026-06-20)

La formule de réconciliation **BR-CO-13** (EN 16931, F04 §3.3 ; total HT = Σ lignes ± charges/remises
de niveau document, arrondi half-up) est portée par `PivotReconciliation`, **source UNIQUE** de la
validation bloquante `LineTotalsRule` (module `Validation`) et de l'affichage console du contrôle de
cohérence `DocumentLineProjection` (Host, FIX205). C'est de la **logique de validation fiscale** : par
la décision 3 ci-dessus (« le contrat NuGet n'a aucune logique ») et CLAUDE.md n°6 (« l'agent n'a
AUCUNE logique métier »), elle ne peut **pas** vivre dans `Liakont.Agent.Contracts`, qui sera publié et
consommé par l'agent net48 à la bascule.

- **Décision** : `PivotReconciliation` est déplacée de `src/Contracts/Liakont.Agent.Contracts` vers un
  assembly **plateforme-seul** `src/Platform/Liakont.Platform.Pivot` (net10), qui référence le contrat
  agent pour les DTOs/arrondi mais **n'est jamais référencé par l'agent** ni inclus dans le paquet du
  contrat. `LineTotalsRule` et `DocumentLineProjection` y référent. **Aucune valeur calculée ne change**
  (déplacement, pas réécriture) : `LineTotalsRule` reste la source unique BR-CO-13, inchangée.
- **Après RDL12, le paquet publiable du contrat ne porte plus que** writer canonique + hasher +
  arrondi (`PivotRounding`) + DTOs — exactement ce dont l'agent a besoin.
- **Garde anti-régression** : `ContractArchitectureTests.Contracts_Assembly_Carries_No_Fiscal_Reconciliation_Logic`
  échoue si un type `*Reconciliation` réapparaît dans le contrat agent ; la pureté BCL du contrat
  (`ContractCouplingGuardTests`, `ContractsPurityTests`) et les gardes de frontière agent restent vertes.

## Alternatives rejetées

- **Template cloné/forké par client final** : divergence du code, perte de l'auto-update unifié, un
  bug à patcher N fois, dette. Contredit le principe produit n°1 (`blueprint.md` §2,
  `tasks/lessons.md` 2026-06-02). **Rejetée.**
- **Rester dans le dépôt `Liakont`** (statu quo du blueprint) : couple les cycles de vie agent et
  plateforme dans un même repo / une même CI. **Acceptable transitoirement** (c'est l'état jusqu'à
  la bascule), mais ce n'est pas la cible. La tension cross-repo notée par ADR-0001 est réelle ; on
  l'assume en **différant** la mise en œuvre au moment où le segment agent démarre, plutôt qu'en
  renonçant à la séparation.

## Références

- `blueprint.md` §2-4 (généricité, structure du dépôt, orientation cible agent)
- `docs/conception/F13-Installateur-Agent-Profils-Integrateur.md` (installateur + profils intégrateur)
- `docs/conception/F12-Architecture-Plateforme-Agent.md` (architecture agent, contrat d'ingestion)
- `docs/adr/ADR-0001-pivot-plateforme-agent.md` (option B cross-repo pesée), `ADR-0003` (stack agent)
- `docs/adr/ADR-0013` (updater de flotte), `ADR-0023` D5 (adaptateurs de démo `DemoErpA`/`DemoErpB`)
- `docs/conception/F12-Architecture-Plateforme-Agent.md` §3.4 (anti faux-vert : une seule ancre de hash)
- `orchestration/protocol.md`, `tools/verify-fast.ps1` (Step 4), `.github/workflows/ci.yml` (job `agent:`)
- `orchestration/MANIFEST-CONVENTIONS.md` (champ `repo:` au niveau segment)
- `tasks/redline-adr-005-006-007.md` (findings A5-coupling-1/5, A5-fix-1/4/5 ; RDL10/RDL11/RDL12)
