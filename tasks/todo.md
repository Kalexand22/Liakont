# Reprise multi-instances de l'agent (prérequis OPS05) — 2026-06-10

Session interactive, branche `feat/agent-multi-instance` (depuis main). Décision opérateur
2026-06-10 (engagement Isatech, serveur SaaS mutualisé type AZMUT : N bases clientes sur une
même machine = N agents, un service par base). Spec : OPS05 pt 5 (orchestration/items/OPS.yaml,
inscrit ce jour). Porte UNIQUEMENT la reprise du code agent (les scripts d'installation,
le packaging et le wizard restent dans OPS05/OPS08).

## Constat (verrous actuels)
- `AgentService.cs:23` + `AgentServiceInstaller.cs:29` + `Program.cs` : ServiceName « LiakontAgent » en dur
- `InterProcessRunLock.cs:24` : mutex `Global\LiakontAgentRun` partagé par toutes les instances
- `AgentPaths.cs` : racine `C:\ProgramData\Liakont` unique

## Conception
- Nouveau `Liakont.Agent.Core/AgentInstance.cs` : identité d'instance validée
  (`^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$`, noms réservés : logs, update-work, périphériques Windows),
  « Default » (insensible à la casse) = instance par défaut. Dérivations centralisées :
  ServiceName (`LiakontAgent` / `LiakontAgent$<nom>`), DisplayName, RunMutexName
  (`Global\LiakontAgentRun` / `Global\LiakontAgentRun-<nom>`), DataDirectory
  (`%ProgramData%\Liakont` / `%ProgramData%\Liakont\<nom>`).
  Parsing ligne de commande : `TryFromCommandLine(args, out instance, out remaining, out error)`
  (option `--instance <nom>`, partagée service + CLI).
- `AgentPaths` : `Initialize(AgentInstance)` au démarrage du process (garde : pas de bascule
  d'instance en cours de process), chemins dérivés de l'instance courante. Instance Default =
  chemins STRICTEMENT identiques à l'existant (compat installations déployées).
- `AgentService` : ctor prend l'instance → ServiceName dynamique.
- `AgentServiceInstaller` : paramètre d'installation `/instance=<nom>` (Context.Parameters) →
  ServiceName/DisplayName dynamiques + ImagePath enrichi de `--instance <nom>` (instances
  nommées seulement ; Default = ImagePath inchangé).
- `Program.cs` (service) : parse `--instance`, Initialize, passe `/instance=` à l'installeur,
  messages français avec le nom de service réel.
- CLI `Program.cs` : option globale `--instance` (avant le nom de commande), RunCommand
  verrouillé sur le mutex DE L'instance ; ligne d'usage ajoutée.
- Updater/MutexRunActivityProbe : déjà paramétrables (serviceName/mutexName en paramètres) —
  aucun changement ; le câblage prod futur passera par AgentInstance.

## Tâches
- [x] `AgentInstance.cs` (Core) + `AgentInstanceTests` (validation, dérivations, parsing args)
- [x] `AgentPaths.cs` : Initialize/Current + `ResetForTesting` (InternalsVisibleTo Core.Tests ajouté au csproj) + `AgentPathsTests`
- [x] `AgentService.cs` : ctor(AgentInstance)
- [x] `AgentServiceInstaller.cs` : /instance= + ImagePath (OnBeforeInstall/OnBeforeUninstall)
- [x] `Program.cs` service : --instance + messages avec le nom de service réel
- [x] CLI `Program.cs` : --instance global + mutex d'instance + usage (CommandRouter)
- [x] Amendement daté en tête de F12 (chemins §2.3/§2.4 = instance Default)
- [x] verify-fast (2 solutions) PASS, tests EXÉCUTÉS (agent: unit-tests 66 s, plateforme verte)
- [ ] codex-review boucle propre
- [ ] Commit (merge humain ensuite — PR)

## Review
- Round 1 : 1 P1 — mutex d'instance sensible à la casse alors que les chemins Windows ne le
  sont pas (« ClientA » service vs « clienta » CLI = même file SQLite mais deux verrous → runs
  concurrents possibles). Fix : composante instance du RunMutexName canonicalisée en
  majuscules + test `Case_variants_of_the_same_name_share_the_same_run_mutex`.
  verify-fast re-PASS après fix.
- Round 2 : 1 P2 — « éditer orchestration/items/OPS.yaml en session interactive viole le
  workflow ». **ACCEPTÉ avec justification** : l'inscription de l'exigence multi-instances dans
  OPS05/OPS08a/b est une décision OPÉRATEUR explicite du 2026-06-10 (Karl : « go »), prise hors
  orchestration comme les re-découpages v12-v17 du manifest. Aucun item ajouté ni retiré (pas de
  risque « manifest sans state »), aucun changement de manifest.yaml/protocol.md ; seules les
  descriptions d'items NON ENCORE EXÉCUTÉS (lot OPS, pas commencé) sont enrichies. La règle
  « ignore orchestration files » du CLAUDE.md s'applique au travail de dev autonome, pas aux
  décisions de backlog portées par l'opérateur.
- Note outillage : codex-review.ps1 sort exit 1 (« neither findings nor sentinel ») même quand
  le reviewer produit des findings exploitables — le parsing du sentinel ne reconnaît pas le
  format de sortie du moteur codex. À signaler (faux « engine failure », pattern lessons.md
  2026-06-02 n°2 sur les faux verts/rouges d'outillage) ; les findings ont été traités
  manuellement (P1 fixé, P2 accepté ci-dessus).
