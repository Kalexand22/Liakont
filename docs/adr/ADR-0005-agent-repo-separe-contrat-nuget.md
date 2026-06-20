# ADR-0005 — L'agent dans un dépôt Git séparé ; contrat partagé via NuGet versionné

- **Statut** : **Reporté / Conditionnel** (re-statué le 2026-06-20 — RDF12). *Décision de cible toujours
  valable*, mais sa **mise en œuvre n'est plus rattachée à un déclencheur vivant** : le déclencheur
  d'origine (« démarrage du segment `agent`, lots AGT ») est **entièrement écoulé** (GATE_AGENT done) **sans
  que la bascule ait eu lieu**. État réel : l'agent (`Liakont.Agent.Cli/Core`, tests) **et** la plateforme
  (`Liakont.Host`, modules `Ingestion`/`Documents`/`Pipeline`/`Transmission`/PA…) consomment encore
  `Liakont.Agent.Contracts` par **ProjectReference cross-solution**, pas par PackageReference NuGet. Voir
  l'avenant 2026-06-20 (re-statut + date butoir + conditions de déclenchement).
  *(Statut antérieur : « Accepté (2026-06-03), mise en œuvre outillage différée au démarrage du segment
  `agent` ».)*
- **Date** : 2026-06-03 (re-statué le 2026-06-20 — RDF12)
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

État réel au moment de la décision : l'agent est au stade **squelette** (SOL01/SOL02 — projets
`Liakont.Agent.*` + `Liakont.Agent.Contracts` + tests d'architecture). Les lots **AGT/ADP/OPS ne
sont pas implémentés** : aucune logique d'extraction/buffer/push réelle, pas d'installateur, pas
d'adaptateur ODBC. On est **très en amont** du segment `agent` (priorité 5500+) — il n'y a rien de
substantiel à déplacer aujourd'hui.

## Décision

1. **Un dépôt produit unique séparé** : `liakont-agent`. C'est un PRODUIT partagé par tous les
   clients — **pas** un template forké/cloné par client final.
2. **Contenu** : le cœur générique (`Liakont.Agent`, `Liakont.Agent.Core`, `Liakont.Agent.Cli`,
   `Liakont.Agent.Installer` — F13) + les **adaptateurs en plug-ins**, un par logiciel source
   (`Liakont.Agent.Adapters.EncheresV6`, futurs Sage/AS400…). Un adaptateur varie selon le
   **logiciel**, jamais selon le client.
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

**À traiter au démarrage du segment `agent` (mise en œuvre DIFFÉRÉE — PAS maintenant)** :

- `orchestration/protocol.md` : étendre l'orchestration au **multi-repo** (quel dépôt pour quel
  segment, `build-agent-context`, branches, merge-back, partage `$ORCH_REPO`). C'est précisément le
  point qu'ADR-0001 signalait non géré — chantier à valider avec l'humain, à ne pas improviser.
- `tools/verify-fast.ps1` : le Step 4 (build/test agent) **migre** vers le dépôt agent ; le dépôt
  plateforme ne build plus l'agent. Même discipline (verify-fast + codex-review) dans le repo agent.
- **Référence du contrat (couplage à défaire)** : aujourd'hui `Liakont.Agent.Core` référence
  `Agent.Contracts` par **ProjectReference cross-solution** (`..\..\..\src\Contracts\...`) — exactement
  le couplage que cet ADR remplace. La bascule substitue une **PackageReference NuGet** à cette
  ProjectReference et **adapte les tests d'architecture** qui codent en dur le chemin
  `src/Contracts/Liakont.Agent.Contracts/...` (`agent/tests/.../AgentProjectReferenceTests.cs`) ainsi
  que l'hypothèse « la plateforme n'est pas publiée en paquet » (commentaires de
  `AgentProjectReferenceTests` / `ContractsPurityTests`).
- `orchestration/manifest.yaml` : les segments `agent` et `adapter-encheresv6` **ciblent le dépôt
  agent** (introduire un champ `repo:` au niveau segment dans `MANIFEST-CONVENTIONS.md`).
- **CI** : pipeline net48 x86/x64 dans le dépôt agent ; publication de `Agent.Contracts` sur le feed
  privé depuis la plateforme.

**Garde-fous de cet ADR** :

- Il **acte la cible**, il ne **déplace pas** le squelette agent et **ne crée pas** le dépôt. Aucune
  incohérence n'est introduite : tant que la bascule n'est pas faite, l'agent reste dans `agent/` et
  `verify-fast` continue de builder les deux solutions.
- Il **ne modifie pas** l'orchestration runtime (`manifest.yaml`, `state.yaml`, `protocol.md`).

## Alternatives rejetées

- **Template cloné/forké par client final** : divergence du code, perte de l'auto-update unifié, un
  bug à patcher N fois, dette. Contredit le principe produit n°1 (`blueprint.md` §2,
  `tasks/lessons.md` 2026-06-02). **Rejetée.**
- **Rester dans le dépôt `Liakont`** (statu quo du blueprint) : couple les cycles de vie agent et
  plateforme dans un même repo / une même CI. **Acceptable transitoirement** (c'est l'état jusqu'à
  la bascule), mais ce n'est pas la cible. La tension cross-repo notée par ADR-0001 est réelle ; on
  l'assume en **différant** la mise en œuvre au moment où le segment agent démarre, plutôt qu'en
  renonçant à la séparation.

## Avenant 2026-06-20 — Re-statut « Reporté / Conditionnel » + date butoir (RDF12)

- **Statut** : Accepté — **Date** : 2026-06-20 — **Item** : RDF12 (redline ADR fondateurs, finding RL-AGT-3)

### Constat

L'ADR liait sa mise en œuvre au **démarrage du segment `agent`** (« à traiter au démarrage du segment
agent — PAS maintenant »). Ce déclencheur est **entièrement consommé** : le segment `agent` (lots AGT) est
livré et **GATE_AGENT est `done`**. Or **la bascule décrite n'a pas été faite** :

- `Liakont.Agent.Core` et `Liakont.Agent.Cli` référencent `Liakont.Agent.Contracts` par **ProjectReference
  cross-solution** (`..\..\..\src\Contracts\Liakont.Agent.Contracts\Liakont.Agent.Contracts.csproj`), pas par
  PackageReference NuGet ;
- côté plateforme, le même `Liakont.Agent.Contracts` est consommé par **ProjectReference** depuis
  `Liakont.Host` et de nombreux modules (`Ingestion`, `Documents`, `Pipeline`, `Transmission`, PA…) ;
- l'agent vit toujours dans `agent/` du **dépôt unique** `Liakont` ; aucun dépôt `liakont-agent` n'existe ;
  `verify-fast` build encore **les deux** solutions du même repo ; `manifest.yaml` n'a pas de champ `repo:`.

L'ADR n'introduisait **aucune incohérence** tant que la bascule n'était pas faite (ses propres garde-fous le
prévoient). Le seul problème est de **gouvernance** : un ADR « Accepté » avec un déclencheur **mort** se lit à
tort comme « en attente de » alors que rien ne le déclenchera plus.

### Décision (re-statut, sans rouvrir la cible)

1. **Re-statué « Reporté / Conditionnel ».** La **cible reste valable** (dépôt agent séparé + contrat NuGet
   versionné — bénéfices d'isolation/CI/cycle de vie inchangés) ; ce qui change est qu'elle **n'est plus
   pilotée par un jalon d'orchestration** mais par les **conditions de déclenchement** ci-dessous.
2. **Conditions de déclenchement (la première rencontrée rouvre l'exécution)** :
   - **partage / audit externe du code agent** (confier l'agent à un tiers sans exposer la plateforme — la
     justification « surface de sécurité » de l'ADR) ;
   - **divergence des cadences de release** agent (net48, auto-update de flotte) vs plateforme (net10) au
     point que le dépôt/CI unique devienne une gêne réelle ;
   - **re-convergence NuGet du socle** (option D d'ADR-0001) : le moment où l'on monte l'infrastructure de
     packaging est l'occasion naturelle de publier aussi `Liakont.Agent.Contracts`.
3. **Date butoir (backstop, non un engagement commercial).** À défaut d'un déclencheur ci-dessus, **ré-arbitrer
   au plus tard à la mise en production V1**, jalon adossé à l'**échéance légale e-reporting** rappelée en
   contexte d'ADR-0001 (**septembre 2026**) : à cette revue, soit on exécute la bascule, soit on **acte
   explicitement le statu quo** (dépôt unique) comme choix V1 et on referme l'ADR — plutôt que de le laisser
   « Accepté / en attente » indéfiniment. Cette date est un **point de revue**, pas une promesse de livraison.
4. **Aucune exécution ici.** Cet avenant **ne déplace pas** le code, **ne crée pas** le dépôt et **ne touche
   pas** l'orchestration runtime : item **DOC pur** (redline). La bascule reste le chantier décrit en
   « Conséquences », à valider avec l'humain.

## Références

- `blueprint.md` §2-4 (généricité, structure du dépôt, orientation cible agent)
- `docs/conception/F13-Installateur-Agent-Profils-Integrateur.md` (installateur + profils intégrateur)
- `docs/conception/F12-Architecture-Plateforme-Agent.md` (architecture agent, contrat d'ingestion)
- `docs/adr/ADR-0001-pivot-plateforme-agent.md` (option B cross-repo pesée), `ADR-0003` (stack agent)
- `orchestration/protocol.md`, `tools/verify-fast.ps1`
