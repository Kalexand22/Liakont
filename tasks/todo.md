# AGT04 — Auto-update de l'agent

Spec : F12 §2.5 / §3.3 (426) / §7 décision D6 ; AGT.yaml (item AGT04).
Frontières : l'agent ne référence que `Liakont.Agent.Contracts`, lecture seule, HTTPS sortant,
aucune logique métier, BCL net48 uniquement (aucun paquet nouveau — ADR-0003).

## Décisions de conception (autonomes — session d'orchestration)

1. **Modèle de confiance (ADR-0013).** `updateUrl` (config heartbeat) pointe le **manifeste de
   version** (JSON : `version`, `packageUrl`, `packageSha256`). `versionManifestSignature` (config)
   est la signature RSA des octets bruts du manifeste, vérifiée contre une **clé publique provisionnée
   à l'installation** (`C:\ProgramData\Liakont\update-signing.pubkey.xml`). Fail-closed : pas de clé →
   refus. Le hash SHA-256 du paquet téléchargé est comparé à celui du manifeste signé. Un manifeste
   non signé / signature invalide / hash non concordant → refus + signalement. Garde anti-downgrade
   (jamais vers une version ≤ courante).
2. **Updater = exe séparé autonome** (`Liakont.Agent.Updater`, sans Core) — « ne tourne jamais depuis
   le dossier qu'il remplace » : lancé détaché, copié dans un dossier updater. Contrat Core↔updater =
   **arguments de ligne de commande** (couplage faible) + un fichier statut JSON.
3. **Deux déclencheurs, consommateurs réels (null-safe, précédent AGT03 : câblé au niveau reporter,
   assemblage host différé) :** heartbeat `updateRequired` → `HeartbeatReporter` ; push 426 →
   `AgentRunCycle` (fanion en mémoire sur le coordinateur partagé). Différé si run en cours (le
   coordinateur sonde le verrou de run via `IRunActivityProbe`).
4. **Signalement** = log opérateur français + `AutoUpdateStateStore` (statut JSON), surfacé au
   heartbeat suivant via `LastError` (consommateur réel).

## Étapes

- [ ] ADR-0013 (modèle de confiance + points durs Windows).
- [ ] Core/Update : modèles (manifeste, résultat/statut), vérificateurs (signature RSA, hash),
      seams (package source HTTPS, launcher détaché, sonde de run), `AutoUpdateStateStore`,
      `IAutoUpdateService` + `AutoUpdateCoordinator`.
- [ ] Câblage : `HeartbeatReporter` (updateRequired + surfaçage statut), `AgentRunCycle` (426),
      `AgentPaths` (chemins update).
- [ ] Projet `Liakont.Agent.Updater` (exe) : engine + abstractions + impls réelles (SCM, swap, santé)
      + Program.
- [ ] Tests : coordinateur (nominal, signature KO, hash KO, différé run, clé absente, downgrade),
      vérificateurs, store, déclencheurs (heartbeat + 426), `UpdaterEngine` (nominal, rollback, timeout).
- [ ] Solution : ajouter les 2 projets (Updater + Updater.Tests) avec mapping x86/x64.
- [ ] verify-fast (plateforme + agent x86) vert, run-tests vert, codex-review propre.

## Revue / résultats

(à compléter en fin d'item)
