# Conformat — Instructions de travail (Claude Code)

## Orchestration Mode

When invoked with "autonomous orchestration" or via `orchestration/prompt.md`:
1. Read and follow `orchestration/protocol.md` exactly — it overrides interactive workflow
2. The manifest at `orchestration/manifest.yaml` is the authoritative backlog
3. Runtime state lives in `$ORCH_REPO` (separate repo, see `.claude/settings.json`)
4. All existing verification rules (verify-fast, codex-review) still apply
5. ONE item per session. Never attempt multiple items.
6. Never modify `protocol.md` or `manifest.yaml` during a session

For normal interactive sessions (human-driven), ignore orchestration files entirely.

---

### 1. Plan Mode Default
- Enter plan mode for ANY non-trivial task (3+ steps or architectural decisions)
- If something goes sideways, STOP and re-plan immediately — don't keep pushing
- Use plan mode for verification steps, not just building
- Write detailed specs upfront to reduce ambiguity
- When a request has multiple plausible interpretations, surface them and ask — don't pick silently and run

### 2. Subagent Strategy
- Use subagents liberally to keep main context window clean
- Offload research, exploration, and parallel analysis to subagents
- For complex problems, throw more compute at it via subagents
- One task per subagent for focused execution

### 3. Self-Improvement Loop
- After ANY correction from the user: update `tasks/lessons.md` with the pattern
- Write rules for yourself that prevent the same mistake
- Ruthlessly iterate on these lessons until mistake rate drops
- Review lessons at session start

### 4. Verification Before Done
- Never mark a task complete without proving it works
- Run tests, check logs, demonstrate correctness
- Ask yourself: "Would a staff engineer approve this?"

### 5. Demand Elegance (Balanced)
- For non-trivial changes: pause and ask "is there a more elegant way?"
- If a fix feels hacky: implement the elegant solution
- Skip this for simple, obvious fixes — don't over-engineer
- Challenge your own work before presenting it

### 6. Autonomous Bug Fixing
- When given a bug report: just fix it. Don't ask for hand-holding
- Point at logs, errors, failing tests — then resolve them
- Go fix failing CI tests without being told how

## Task Management

1. **Plan First**: Write plan to `tasks/todo.md` with checkable items
2. **Verify Plan**: Check in before starting implementation
3. **Track Progress**: Mark items complete as you go
4. **Explain Changes**: High-level summary at each step
5. **Document Results**: Add review section to `tasks/todo.md`
6. **Capture Lessons**: Update `tasks/lessons.md` after corrections

## Core Principles

- **Simplicity First**: Make every change as simple as possible. Impact minimal code.
- **No Laziness**: Find root causes. No temporary fixes. Senior developer standards.
- **Surgical Changes**: Every modified line must trace directly to the request.
  - Don't "improve" adjacent code, comments, or formatting that isn't part of the task
  - Don't refactor things that aren't broken; match existing style
  - Clean up orphans *created by* your change — do NOT delete pre-existing dead code unless asked

## Règles métier non négociables (produit de conformité fiscale)

Conformat transmet des données fiscales à l'administration via des Plateformes Agréées.
Une erreur ici engage la responsabilité fiscale du client. Ces règles sont des **P1
automatiques en review** :

1. **Montants en `decimal`, jamais float/double.** Arrondi commercial half-up, 2 décimales.
2. **Aucune règle fiscale inventée.** Toute catégorie TVA, tout code VATEX, tout seuil vient
   de `docs/conception/F*.md`. Si la spec ne tranche pas : bloquer l'item, ne pas deviner.
3. **Bloquer plutôt qu'envoyer faux.** Jamais affaiblir une validation Blocking en Warning
   pour faire passer un test ou un envoi.
4. **Piste d'audit et coffre d'archive immuables.** `DocumentEvent` et `MappingChangeLog` sont
   append-only, le coffre d'archive est WORM. Aucun code d'update/delete, aucune purge automatique.
5. **Lecture seule stricte de la base source.** Aucun INSERT/UPDATE/DELETE, aucun verrou,
   aucune transaction d'écriture sur la base du client.
6. **Frontières de la généricité (blueprint.md §2 et §6) :**
   - `Gateway.Core` ne référence JAMAIS un plug-in (ni source, ni PA)
   - Un plug-in ne référence que le Core, jamais un autre plug-in
   - `Gateway.App` (console) ne référence QUE Api + ApiClient — jamais Core, plug-ins ou SQLite
7. **Aucune donnée client dans le code.** Table TVA réelle, SIREN, chaîne ODBC, compte PA :
   tout est paramétrage dans `deployments/<client>/`. Le code n'embarque que des EXEMPLES
   fictifs dans `config/exemples/`.
8. **Aucune fonctionnalité produit ne dépend de ce qu'UN PA sait faire.** Le comportement
   est piloté par les capacités déclarées du plug-in (`PaCapabilities`), jamais par un flag
   de configuration produit ni par un `if (pa is B2Brouter)`.
9. **Un seul écrivain sur le Tracking : le Service.** Aucun autre processus n'ouvre la base
   en écriture (le CLI de secours uniquement quand le Service est arrêté, sous mutex).
10. **Secrets chiffrés.** Clé API PA et credentials SMTP : DPAPI, jamais en clair dans un
    fichier versionné ou un log.
11. **Messages opérateur en français**, avec numéro de document et action corrective.

## Verification Workflow

### After Every Dev Task (mandatory, no exception)
Claude owns the entire verification + review loop. The human only gives the objective and merges.

1. Code the task
2. Run `powershell -ExecutionPolicy Bypass -File tools/verify-fast.ps1` — must pass
3. If the item has integration tests: run `powershell -ExecutionPolicy Bypass -File tools/run-tests.ps1` — must pass
4. Run `powershell -ExecutionPolicy Bypass -File tools/codex-review.ps1` — review the working tree (round 1 = full review)
5. Read findings. Fix all P1/P2 findings autonomously.
6. After fixing, re-run with `-Round N` (increment each time)
7. Round 2+ = re-review (verify fixes only, not a full re-review). Loop until clean.
8. **Never declare a task complete before the review loop is clean**
9. Review MUST happen on the **current working tree**, not only on committed changes
10. If files changed after the last review, the review is stale — re-run it
11. Scripts/CI/config/docs changes require the same review discipline as production code

### Post-Dev Checklist
- [ ] `verify-fast` passes
- [ ] No new packages added without ADR
- [ ] No cross-boundary references (Core → Adapter forbidden)
- [ ] No float/double on amounts
- [ ] No fiscal rule without a traceable source in docs/conception/
- [ ] DocumentEvent stays append-only (no update/delete paths added)
- [ ] WPF items have ViewModel unit tests + updated smoke checklist
- [ ] Review has been re-run after the last code change
- [ ] Tests were EXECUTED, not just written (a written-but-never-run test is a false-green)

## Code Review Instructions (for the reviewer)

**Report only:**
1. Bugs probables
2. Régressions possibles
3. Problèmes de robustesse
4. Problèmes de sécurité (secrets en clair, données fiscales exposées)
5. Dette technique importante
6. Écarts aux conventions du repo (voir docs/architecture/ et blueprint.md)
7. Trous de test
8. Faux positifs / faux verts dans scripts, CI, verify, checks, or tooling
9. **float/double sur un montant est un P1.**
10. **Règle fiscale inventée (catégorie TVA, VATEX, seuil sans source dans docs/conception/) est un P1.**
11. **Affaiblissement d'une validation Blocking est un P1.**
12. **Chemin d'update/delete sur DocumentEvent, MappingChangeLog, le coffre d'archive (WORM) ou purge d'une table d'audit est un P1.**
13. **Écriture (ou verrou) sur la base source dans un adaptateur est un P1.**
14. **Violation des frontières est un P1 :** Core → plug-in, plug-in → plug-in, ou Gateway.App → Core/plug-ins/SQLite.
15. **Donnée client dans le code est un P1 :** SIREN réel, table TVA réelle, chaîne ODBC, compte PA hors de deployments/.**
16. **Dépendance à un PA concret hors de son plug-in est un P1 :** `if (pa is B2Brouter)`, flag produit doublonnant une capacité, fonctionnalité désactivée parce qu'« un PA ne le supporte pas ».
17. **Accès en écriture au Tracking hors du Service est un P1** (sauf CLI de secours sous mutex).
18. **Secret en clair (clé API, mot de passe SMTP) est un P1.**
19. **Item WPF sans tests ViewModel est un P1.** Le code-behind ne doit contenir que du câblage de vue.

**Format per finding:**
[P1] or [P2] | file:line | concrete description | suggested fix

**Rules:**
- P1 = blocking (bug, security, regression, fiscal correctness). P2 = important but non-blocking.
- No compliments, no summaries, no cosmetic suggestions.
- If everything is clean: "No findings."
- Be strict and concrete.
- Read docs/architecture/ and blueprint.md for project conventions before reviewing.
- Review the **diff and the behavior**, not only file presence or naming.
- Check the real failure mode of automation: `continue-on-error`, silent skips, pass-by-default logic.
- For scripts, verify what happens on empty state, dirty state, failure state, and partial state.
- For CI, verify that a failing validation step really fails the pipeline.

### Re-review after fixes (round > 1)

This review loop works in rounds. Round 1 is the initial full review. Rounds 2+ are **re-reviews after fixes**.

**When re-reviewing (round > 1), your scope is strictly limited:**
- ONLY verify that the fixes for previous findings are correct and complete.
- ONLY check for regressions introduced by the fixes themselves.
- Do NOT expand scope to find new unrelated issues in unchanged code.
- Do NOT re-review code that was not modified since the last round.
- A re-review is narrower than the initial review, not broader.
- If the fixes are correct and introduce no regressions: "No findings."
- Being overly strict on re-reviews creates infinite loops. That is a bug, not rigor.

## Review Validity Rules

- No commit is considered review-ready without a review on the current workspace state.
- No "LGTM" equivalent is valid if `verify-fast` failed or was not run.
- Any fix after review reopens the review cycle.
- Human merge remains mandatory even when all automated checks and review are green.
