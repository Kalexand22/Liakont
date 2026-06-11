# Orchestration Protocol — Session Instructions (v1: Multi-Agent)

You are running in **AUTONOMOUS ORCHESTRATION MODE**. Follow these steps exactly.
Do not ask questions. Do not deviate from the protocol.

## References

- Backlog: `orchestration/manifest.yaml` (index — id, lot, priority, depends_on, blueprint)
- Item details: `orchestration/items/<lot>.yaml` (description, acceptance, type, executor, blueprint)
- Blueprints: `orchestration/blueprints/<blueprint>.yaml`
- Lessons: `tasks/lessons.md`
- Architecture docs: `docs/architecture/`
- Product blueprint: `blueprint.md`
- Feature specs: `docs/conception/` (F01-F12 — the functional source of truth; F12 is the
  platform/agent architecture spec of the 2026-06-03 pivot)
- Definition of done: `docs/architecture/definition-of-done.md`

### External state repo (`$ORCH_REPO`)

Runtime state lives in a **separate repository** to avoid merge conflicts and enable
multi-agent parallelism.

- **Path**: `$ORCH_REPO` env var (set in `.claude/settings.json`)
- Config: `$ORCH_REPO/config.yaml`
- State: `$ORCH_REPO/state.yaml`
- Leases: `$ORCH_REPO/leases/slot-<N>.yaml`
- Event journal: `$ORCH_REPO/events.jsonl`
- Session logs: `$ORCH_REPO/session-log/`
- Runtime archives (purged state entries, old session data): `$ORCH_REPO/archive/`
  (NOT the same as `orchestration/archive/` in the source repo, which archives
  completed lot/segment DEFINITIONS — see MANIFEST-CONVENTIONS.md)

### Multi-agent model: separate clones

Each agent runs in its **own full clone** of the source repo (e.g., `Liakont`, `Liakont2`,
`Liakont3`). Synchronization happens via the remote (git push/fetch).

- Each clone has its own `.claude/settings.json` with `$ORCH_REPO` pointing to the **same**
  shared orchestration repo.
- Agents never share a working directory. Each Claude Code instance = one clone = one slot.
- The agent determines which clone it's in from its current working directory.

---

## Step 0 — Slot Acquisition + Recovery

### Slot acquisition

Multiple agents can run in parallel, each occupying a **slot**. The number of slots
is defined by `max_parallel` in `$ORCH_REPO/config.yaml`.

1. Read `$ORCH_REPO/config.yaml` to get `max_parallel` (default: 3),
   `lease_duration_minutes` (default: 120) and `heartbeat_interval_minutes` (default: 10).
2. Scan `$ORCH_REPO/leases/slot-*.yaml` files:
   - For each slot from 1 to `max_parallel`:
     - If `$ORCH_REPO/leases/slot-<N>.yaml` does not exist → slot is free.
     - If it exists and `expires_at` > now → slot is occupied. Skip.
     - If it exists and `expires_at` <= now → slot is expired. Delete the file and treat as free.
   - Take the first free slot.
3. If no free slot → **EXIT 0** with message "All slots occupied."
4. Acquire the slot atomically: create `$ORCH_REPO/leases/slot-$SLOT_ID.yaml` in
   **exclusive-create mode** (PowerShell: `[System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew)`
   then write — `CreateNew` throws if the file already exists, which closes the TOCTOU race
   where two agents see the same free slot).
   (fields: session_id, slot_id, clone_path, hostname, acquired_at, heartbeat_at,
   expires_at = now + `lease_duration_minutes` from config.yaml).
   If the exclusive create fails (race lost): re-read, try next slot. No free slots → EXIT 0.

### Recovery

5. Read `$ORCH_REPO/state.yaml` via `tools/orch-state.ps1 read`.
   - If any item has status `claimed` or `in_progress` and its slot's lease is expired or absent:
     - Mark that item as `stale` via `tools/orch-state.ps1 update -ItemId <id> -Status stale`
     - Append transition to events.jsonl
     - **Do NOT delete, stash, drop, or reset any code.**
     - Then attempt **auto-recovery** (see below). If auto-recovery fails, leave item `stale`.

### Auto-Recovery of Stale Items

When an item is `stale` (session died before completing), the orchestrator
attempts automatic recovery **before** selecting the next item:

0. **Sync the working tree with `main` first** (`git fetch origin --prune`, then
   `git merge origin/main --no-edit`). Recovery runs `verify-fast` (step 2), whose `manifest-sanity`
   reads the **working-tree** manifest; on a clone behind `main` a stale manifest fails reciprocity
   and would sink an otherwise-valid recovery for the wrong reason. The sync has **two distinct
   failure modes** — handle **both**, and in **neither** touch the leftover changes (recovery is
   non-destructive: never stash/reset):
   - **Dirty working tree** — the dead session left uncommitted changes (likely, since it died
     mid-work). If they touch files the merge would update, `git merge` fails **before starting**
     (`Your local changes … would be overwritten by merge`). This is **not** a conflict, and
     `git merge --abort` would then answer `fatal: There is no merge to abort`. So do **not** run
     `--abort`: detect the dirty tree up front (`git status --porcelain` non-empty) and **skip the
     merge entirely** → auto-recovery fails gracefully, item stays `stale` (the operator inspects the
     leftover work). This is the most common recovery failure mode — it must not be reduced to "conflict".
   - **Non-trivial merge conflict** — the merge started, then conflicted: `git merge --abort` →
     auto-recovery fails, item stays `stale`, skip it (the operator resolves the segment↔main
     divergence — never force it).
1. **Check if committed**: search `git log --oneline` for a commit matching the
   item's expected pattern (e.g., `feat(<scope>): <item_id>` or the item title).
   - If no commit found → auto-recovery fails. Item stays `stale`. Skip it.
2. **Verify the commit**: run `powershell -ExecutionPolicy Bypass -File tools/verify-fast.ps1`.
   - If **FAIL** → auto-recovery fails. Item stays `stale`. Skip it.
3. **Run codex-review** scoped to the recovered commit **alone**:
   `powershell -ExecutionPolicy Bypass -File tools/codex-review.ps1 -Commit "<commit>"`.
   Do **NOT** use `-Base "<commit>~1"` here: step 0 merged `origin/main`, so `HEAD` is now the merge
   commit and `codex-review`'s `-Base` does a three-point diff (`git diff "<commit>~1...HEAD"`) that
   would balloon the review to **the entire merge of `main`** — wasting tokens, surfacing unrelated
   `main` findings that sink an otherwise-valid recovery, and (worst) letting the autonomous fix loop
   modify `main` code unrelated to the item. `-Commit` reviews exactly the recovered work
   (`git show <commit>`).
   - If **P1 findings**: fix them autonomously and **commit the fix** as a new commit `<fix-sha>`,
     then re-review **that fix commit alone** — `tools/codex-review.ps1 -Commit "<fix-sha>" -Round N`
     (a fresh `git show <fix-sha>` each round; `<fix-sha>` is the latest fix commit, never the
     original `<commit>`, which is frozen and would just resurface the same findings). Do **not**
     fall back to a `-Base` re-review here: with the `origin/main` merge commit interposed between
     the recovered work and the fix, **any** `-Base` ref earlier than the fix re-includes that merge
     — the same balloon. `-Commit` per round is the only scope that captures "the fix alone".
   - If fixes fail after 3 rounds → auto-recovery fails. Item stays `stale`. Skip it.
4. **Mark done**: `tools/orch-state.ps1 update -ItemId <id> -Status done`
5. **Write recovery session log**: `$ORCH_REPO/session-log/<session_id>_<item_id>_RECOVERY.md`
6. **Append event** to events.jsonl: `stale → done (auto-recovered)`.
7. **Clear the slot's entry** in `active_sessions`.
8. Continue to Step 1 — the recovered item's dependents are now unblocked.

**Guardrails:**
- Auto-recovery never deletes, resets, or force-pushes code.
- Auto-recovery only promotes `stale` → `done`. It never creates new work.
- If verify-fast or codex-review cannot run (tool error), auto-recovery fails gracefully.

---

## Step 1 — Read State

### Step 1.0 — Fetch; read the backlog from `main` (do this FIRST)

The manifest is the operator-owned backlog and is **authoritative on `main`**. The copy in the
current checkout is the *previous* session's segment branch — unrelated to the item this session
will pick — and may have drifted behind `main`. A stale manifest is the root cause of
`manifest-sanity` reciprocity failures (state references items the stale manifest does not declare)
and of selecting superseded items. Do **not** merge that arbitrary branch here: a merge before any
item is selected would create an unpushed merge commit and could block the whole session on a
conflict in a segment it was not even going to touch. Instead:

```bash
git fetch origin --prune
```

Then read the manifest and run selection (Step 1.1, Step 2) against the **`main` copy**:
`git show origin/main:orchestration/manifest.yaml`. No branch merge, no working-tree change, no
pre-selection block. The segment branch the session actually works on is brought up to date with
`main` in Step 3 §4 — once the correct segment is known — which is what removes the manual
"merge main + realign clones" sweep an operator otherwise does after bumping the manifest.

1. Read the manifest from `main` — `git show origin/main:orchestration/manifest.yaml` — the
   authoritative backlog (index only). Per Step 1.0, the copy in the current checkout may be stale;
   `main` is canonical for the backlog index.
2. Read `$ORCH_REPO/state.yaml` via `tools/orch-state.ps1 read` — current statuses.
   Note: item details (description, acceptance, executor, blueprint) live in
   `orchestration/items/<lot>.yaml`. Read the relevant lot file in Step 4 when executing.
   - If state.yaml does not exist: **STOP — EXIT 1.** The state repo is versioned and
     state.yaml must always exist there. A missing state.yaml means `$ORCH_REPO` points to
     the wrong place or the clone is broken. Never recreate it (recreating it would resurrect
     items already done and purged — absent = done).
   - **CRITICAL — absent = done**: If an item exists in the manifest but is NOT present
     in state.yaml, it has already been completed and purged. **Never re-add it.**
     **Never treat it as new.** Only items explicitly listed in state.yaml are actionable.
3. Read `tasks/lessons.md` — apply any relevant lessons to this session.

### Step 1.4 — Reconcile human-merge gates

Since manifest v11 every **segment gate is a human-merge gate** (`executor: human`): the runner
runs the segment checks and opens a PR to main, and a human merges it (the "human merge to main"
control required by CLAUDE.md). To close the loop without a second manual step, run the
reconciliation **before selecting work**:

```bash
powershell -ExecutionPolicy Bypass -File tools/orch-reconcile-gates.ps1
```

It flips any gate still `blocked`/`gate_pending` whose segment branch is **already merged** into
main to `done` (and appends an event). It NEVER merges, and only promotes a gate when a merged PR
for that exact branch exists — safe and idempotent. This makes a gate's completion hands-off once
the human clicks merge. The legacy auto-merge path (`blueprint: auto-gate-item`, Step 5c) is
**deprecated for any runnable gate**: it degraded to `blocked` on every gate because merging to
main is a human action. Only the already-`done` GATE_CORE_FOUNDATION and GATE_PA_FRAMEWORK still
carry it (terminal — they will not re-run); do not wire a new gate to it.

---

## Step 2 — Select + Claim Item

1. For each item in the manifest:
   - Check its `status` in state.yaml.
   - **If the item is not in state.yaml → skip it (absent = done, purged).**
   - An item is **eligible** if:
     - `status` = `pending` (explicitly present in state.yaml)
     - ALL items in `depends_on` have `status` = `done` in state.yaml
     - it is **actionable by Claude** — EITHER a work item with `executor: claude` (the default),
       OR an **automated integration gate** (`type: gate` whose `executor` is NOT `human`, i.e. the
       default `claude`; these carry `blueprint: auto-gate-item`). Human gates (`executor: human`)
       are NOT eligible here — they are handled in step 2.2.
     - **The item's segment is unlocked**: if the segment declares `depends_on_gate`
       (a single gate or a list), ALL of those gates must have `status` = `done` in state.yaml
       (an automated gate's own segment is always unlocked — it IS the segment's gate)
     - **No active agent is working on a dependency**: none of the item's `depends_on` have
       `status` = `claimed` or `in_progress` (prevents two agents from working on dependent items)
2. Check **human** gates separately (`type: gate` with `executor: human`):
   - A human gate is **gate-ready** if all its `depends_on` have `status: done`.
   - If a human gate is gate-ready and its status is `pending`:
     - Update via `tools/orch-state.ps1 update -ItemId <id> -Status gate_pending`
     - Append transition to events.jsonl
     - If this is the segment's gate, and the segment branch has commits:
       - Push the branch: `git push -u origin <branch>`
       - Create PR: `gh pr create --base main --head <branch> --title "<GATE_ID> — <gate title>" --body "..."`
         The title MUST start with the gate id followed by a delimiter (space/dash):
         `tools/orch-reconcile-gates.ps1` only flips a gate to `done` on a merged PR whose
         title starts with its gate id (an older merged PR on the same segment branch must
         never be mistaken for the gate PR — PR #15/#41 incident, 2026-06-11).
       - Update segment status via orch-state.ps1
     - Write session-log entry to `$ORCH_REPO/session-log/`
     - Release slot (delete `$ORCH_REPO/leases/slot-$SLOT_ID.yaml`)
     - **EXIT 0** with message "Gate <id> is ready for human validation."
   - **Automated integration gates** (`type: gate`, `executor` ≠ `human`, `blueprint: auto-gate-item`)
     are NOT handled here. They are selected and **claimed like any eligible item** in step 2.1
     (sorted by `priority`), then executed via the `auto-gate-item` blueprint: Step 3 (auto-gate
     variant) → Step 4 → Step 5c (merge to main). No human, no PR-and-exit — the gate runs to
     `done` autonomously when verify + tests + integration review are green, degrading to a human
     (`blocked`) only on a red check or a non-trivial merge conflict.
3. If no eligible items exist:
   - Release slot
   - **EXIT 0** with message "No actionable items."
4. Sort eligible items by `priority` (ascending). Take the first one.
5. **Claim the item atomically** using the locking script:
   ```bash
   powershell -ExecutionPolicy Bypass -File tools/orch-state.ps1 claim \
     -ItemId "<item_id>" -SlotId "$SLOT_ID" -SessionId "$SESSION_ID" \
     -ClonePath "$(pwd)" -Subbranch "<subbranch>"
   ```
   - If the script returns non-zero (item already claimed by another agent): go back to step 4
     and pick the next eligible item.
   - If successful: the item is now `claimed` and `active_sessions.slot-N` is set.

---

## Step 3 — Setup Branch

The agent is already in its own clone. It just needs to create the sub-branch.

**Auto-gate variant (action `gate-checkout-segment`).** If the claimed item is an automated
integration gate (`type: gate`, `executor` ≠ `human`, `blueprint: auto-gate-item`):
  - Determine the segment whose `gate` field equals this gate id (e.g. `GATE_CORE_FOUNDATION`
    → segment `core-foundation`, branch `feat/core-foundation`).
  - `git fetch origin`, `git checkout <segment-branch>`, `git pull origin <segment-branch>`.
    This variant intentionally does **not** `git merge origin/main` (unlike §4 below): `auto-gate-item`
    is deprecated and its only carriers — GATE_CORE_FOUNDATION / GATE_PA_FRAMEWORK — are terminal
    (`done`, never re-run). If a new automated gate is ever wired here (discouraged — see Step 1.4),
    add the same `git merge origin/main --no-edit` so its `manifest-sanity` runs against current `main`.
  - Do **NOT** create a sub-branch. The auto-gate runs verify/tests/review on the segment
    branch itself and then merges it into main (Step 5c). Skip the rest of Step 3.
  - (At claim time in Step 2.5, pass the **segment branch name** as `-Subbranch` — an auto-gate
    has no sub-branch; the field just records where the work happens.)

1. Determine the item's segment from the **`main` manifest** — the same authoritative copy read in
   Step 1.0/1.1 (`git show origin/main:orchestration/manifest.yaml`): find which segment contains the
   item's `lot`. Do **not** read the working-tree manifest here — it is only synced with `main` in §4
   below, so a lot↔segment mapping (or a brand-new segment) added on `main` since the last session
   would be missing and could route the item to the wrong branch (or fail to resolve the segment).
2. Get the segment branch name from `segments.<segment>.branch` in that same `main` manifest
   (e.g., `feat/core-foundation`).
3. Define the sub-branch name: `<segment-branch>-<item_id>` (e.g., `feat/core-foundation-PIV01`).
   **Dash separator, never a slash**: git refs are stored as files, so `feat/socle-v6` (file) and
   `feat/socle-v6/SOL01` (would require `feat/socle-v6` to be a directory) cannot coexist.

4. `git fetch origin`, checkout segment branch (create from `base` if it does not exist yet),
   `git pull`, then **sync it with main: `git merge origin/main --no-edit`** (no-op when already
   current; on a non-trivial conflict: `git merge --abort`, mark the claimed item `blocked`, append
   the transition to `events.jsonl`, write session-log, release slot, EXIT 1). This brings the
   segment branch — and the sub-branch created from it — up to date with `main` (manifest, specs,
   and any upstream segment merged since), so the build and `manifest-sanity` run against current
   `main`. Then `git checkout -b "$SEGMENT_BRANCH-<item_id>"`.

5. All subsequent work happens on the sub-branch in this clone.

---

## Step 4 — Execute (Blueprint-Driven)

1. Read the item's `description` and `acceptance` criteria from `orchestration/items/<lot>.yaml`
   (keyed by item id). The manifest contains only routing fields (id, lot, priority, depends_on, blueprint);
   the lot file contains execution context (type, executor, blueprint, description, acceptance).
2. Determine the item's blueprint:
   - Use the lot file `blueprint` field if present, otherwise check the manifest.
   - Default: `module-work-item`.
3. Read the blueprint from `orchestration/blueprints/<blueprint>.yaml`.
4. Execute each node in the blueprint, in sequence:
   - **deterministic** nodes: run the specified `action`.
     NOTE: shell one-liners in this protocol use POSIX syntax (`&&`, `rm`) — run them via the
     Bash tool, never via PowerShell (where `&&` is a parse error on Windows PowerShell 5.1).
     - `build-agent-context` → `powershell -ExecutionPolicy Bypass -File tools/build-agent-context.ps1 -ItemId <item_id>`, then read all listed files.
     - `verify-fast` → `powershell -ExecutionPolicy Bypass -File tools/verify-fast.ps1`
     - `run-tests` → `powershell -ExecutionPolicy Bypass -File tools/run-tests.ps1` (full unit + integration test suite — Category=E2E excluded, see `run-e2e`)
     - `run-e2e` → `powershell -ExecutionPolicy Bypass -File tools/run-e2e.ps1` (Playwright E2E suite — script delivered by SOL05; before SOL05 is done, blueprints using this action cannot run)
     - `codex-review` → `powershell -ExecutionPolicy Bypass -File tools/codex-review.ps1 -Base "<segment-branch>"`
       (with `-Round N` on re-runs). For an **automated integration gate** the base is `main`
       (`-Base main`) — it reviews the complete segment diff, not one item's. **`-Base` is MANDATORY in orchestration**: the review node
       runs after `commit_apply`, so the working tree is clean — without `-Base`, there is
       nothing to review and the run fails with exit 3.
       **Exit codes**: 0 = review ran on the sub-branch diff and is CLEAN. 2 = review ran and
       has P1/P2 findings (triggers the `fix_review` node). 3 = nothing was reviewed (caller
       error — fix the -Base argument, never treat as clean). Any other code = the review could
       not run (tool failure — retry per the node's retry policy, never treat as clean).
     - `git-commit-apply` → `git add -A && git commit -F .orch-commit-msg && rm .orch-commit-msg`. Requires that the preceding `commit_compose` node (delegated to `orch-commit`) has written the message file at the repo root. The file is gitignored (see `.gitignore`), so `git add -A` will not stage it. If `.orch-commit-msg` is missing, mark the item `blocked` and exit.
     - `gate-checkout-segment` → see Step 3 (auto-gate variant). Checkout the segment branch, no sub-branch. Automated gates only.
     - `merge-back` → see Step 5a.
     - `integrate-to-main` → see Step 5c. Merge the segment branch into main. Automated gates only — the single sanctioned exception to "never merge to main".
     - `finalize-publish` → see Step 5b.
   - **agentic** nodes:
     - If the node declares `subagent: <name>`, the orchestrator MUST delegate execution to that custom subagent via the Agent tool: `Agent(subagent_type: "<name>", prompt: "<self-contained brief: item id, scope, file paths, findings or log path>")`. **Inline execution by the main orchestrator is forbidden for delegated nodes** — that defeats the entire token-routing system.
     - If the node has no `subagent` field, the main orchestrator (Opus) executes the node inline using its own judgment.
     - Subagent prompts must be self-contained: the subagent has no memory of prior nodes. Pass exact paths, exact findings, exact file:line references — never "see above" or "as discussed".
   - **conditional** nodes (have a `condition` field): skip if condition is not met.
   - **loop-back** nodes (have a `loop_back_to` field): after execution, return to the specified node and resume from there (e.g., `fix_review` loops back to `verify` → `commit` → `review`).
   - **retry** nodes: on failure, retry up to `retry.max` times. On exhaustion, set item to `retry.on_exhaust` status, write session-log, release slot, **EXIT 1**.
5. Track each node execution: record start time, duration, and status for the session log.
6. Between nodes, renew the lease heartbeat if more than `heartbeat_interval_minutes`
   (from config.yaml) have elapsed:
   - Update `heartbeat_at` and `expires_at` in `$ORCH_REPO/leases/slot-$SLOT_ID.yaml`.
7. After all nodes complete, update item status:
   `tools/orch-state.ps1 update -ItemId <id> -Status done`

### Subagent routing

Blueprint nodes that benefit from a smaller/cheaper model declare a `subagent: <name>` field. When such a node is reached, the orchestrator MUST delegate to that custom subagent via the Agent tool — never execute it inline.

Custom subagents are defined in `.claude/agents/<name>.md` and are versioned with the repo (so every clone picks them up automatically).

| Subagent | Model | Triggered by | Tools |
|---|---|---|---|
| `orch-fix-verify` | sonnet | `fix_verify` node when `verify-fast` fails | Read, Edit, Grep, Glob, Bash |
| `orch-fix-review` | sonnet | `fix_review` node when `codex-review` reports P1/P2 | Read, Edit, Grep, Glob, Bash |
| `orch-fix-tests` | sonnet | `fix_tests` node when `run-tests` fails | Read, Edit, Grep, Glob, Bash |
| `orch-commit` | haiku | `commit_compose` node (writes `.orch-commit-msg`) | Read, Bash, Write |
| `orch-finalize` | haiku | `finalize_compose_log` node (writes session log) | Read, Write, Bash |

**Routing rules:**

1. Nodes with `subagent: <name>` → delegate via Agent tool with `subagent_type: <name>`. The subagent runs in its own context with the model declared in its frontmatter.
2. Nodes without `subagent` → execute inline in the main orchestrator (Opus).
3. **Deterministic nodes never declare a subagent** — they run scripts, no LLM is involved.
4. **Composition vs publication split**: `commit` and `finalize` are split into two nodes each. The composition (LLM work — message text, log narrative) is delegated to a haiku subagent. The publication (shell commands — `git commit`, append events.jsonl, release lease) runs deterministically in the orchestrator. This isolates the LLM cost to the part that actually needs a model.

**Why this exists:** the main orchestrator runs on Opus (large context). Without delegation, every fix loop, every commit message, and every session log burns Opus tokens — which is wasteful for mechanical tasks. Subagent routing pushes those tasks to the cheapest model that can handle them, while keeping plan/implement on Opus where judgment matters.

### Subagent isolation for verbose nodes

Deterministic nodes that produce verbose output (`verify-fast`, `run-tests`, `codex-review`)
SHOULD be executed so their full output stays out of the main agent context.
The scripts write detailed logs to `.verify-fast.log` / `.run-tests.log` in the repo root;
the main agent only consumes the compact summary from stdout.
If the main agent needs details (e.g., to diagnose a failure), it reads the log file on demand.

### Node execution details

- **verify + fix_verify loop**: if verify fails, execute fix_verify (agentic), then re-run verify.
  Increment `retry_count` in state.yaml on each verify failure.
- **review + fix_review loop**: if review has P1 or fixable P2 findings, execute fix_review (agentic),
  then re-run verify (to confirm fixes don't break anything), commit, and re-run review with `-Round N`.
  Document any accepted P2s with justification.
- P2 findings are never silently skipped. Every P2 is either fixed or explicitly accepted
  with a documented reason in the session log.

---

## Step 5a — Merge Back

After the review loop is clean and all work is committed on the sub-branch:

1. `git push -u origin "$SUBBRANCH"`
2. `git checkout "$SEGMENT_BRANCH" && git pull origin "$SEGMENT_BRANCH" && git merge --no-ff "$SUBBRANCH" -m "merge: $ITEM_ID from slot-$SLOT_ID"`
3. If **non-trivial conflicts**: `git merge --abort`, mark `blocked`, write session-log, release slot, EXIT 1.
4. `git push origin "$SEGMENT_BRANCH"`
5. Optional cleanup: `git branch -d "$SUBBRANCH" && git push origin --delete "$SUBBRANCH"`

**Note (remote configured 2026-06-07):** `origin` (https://github.com/Kalexand22/Liakont.git) is
configured — the push/pull steps above are **mandatory**, every merge is pushed. The earlier
local-only mode (push/pull skipped) is obsolete.

---

## Step 5b — Finalize (split: compose + publish)

Finalize is split into two nodes so that the LLM-heavy composition is delegated to a haiku subagent, and the mechanical publication runs deterministically in the orchestrator.

### Step 5b.1 — `finalize_compose_log` (delegated to `orch-finalize`)

The orchestrator delegates this node to the `orch-finalize` subagent (haiku) with a self-contained brief:
- session_id, item_id, item_title, blueprint, slot, clone_path
- node execution table (id, status, started_at, duration, notes) — collected by the orchestrator throughout the session
- review rounds summary (per round: P1/P2 counts, fixed list)
- accepted P2 list with justifications
- total duration
- final status (done / failed / blocked)
- sub-branch name, segment branch name, commit count

The subagent writes one file: `$ORCH_REPO/session-log/<session_id>_<item_id>.md`.
It does NOT touch git, the slot lease, or `events.jsonl`.

### Step 5b.2 — `finalize_publish` (deterministic, inline in orchestrator)

After the session log file exists, the orchestrator runs these steps in order — no LLM:

1. Append completion event to `$ORCH_REPO/events.jsonl`.
2. `tools/orch-state.ps1 release -SlotId "$SLOT_ID"`
3. In `$ORCH_REPO`: `git add state.yaml events.jsonl session-log/ && git commit -m "session: $SESSION_ID — $ITEM_ID done (slot-$SLOT_ID)"`
4. Check if the **next eligible item** after this one would be a **human** gate (`executor: human`):
   - If yes: mark the gate as `gate_pending`, push the segment branch, create a PR (see Step 2).
   - An **automated integration gate** (`executor` ≠ `human`) is NOT set up here — leave it
     `pending`; the next session claims and runs it via the `auto-gate-item` blueprint (Step 2.1).
5. Release the lease file: delete `$ORCH_REPO/leases/slot-$SLOT_ID.yaml`.
6. **EXIT 0**.

If the session log file is missing when `finalize_publish` runs, mark the item `blocked`, write a recovery note in `events.jsonl`, release the slot, and EXIT 1.

---

## Step 5c — Integrate to Main (automated gates only)

Runs only for the `integrate_to_main` node of an `auto-gate-item` blueprint, **after**
verify + run-tests + integration review (`codex-review -Base main`) are all green on the
segment branch. This is the **ONLY** sanctioned exception to the rule "never merge to main":
it is scoped to gates with `executor` ≠ `human`, and it never runs on a red check.

1. **With a remote configured** (PR auto-merge — keeps an audit trail; now the only mode):
   - `git push origin <segment-branch>`
   - `gh pr create --base main --head <segment-branch> --title "<GATE_ID> — <gate title>" --body "Automated integration gate: verify + run-tests + integration review green on <segment-branch>. Session log in $ORCH_REPO."` (title MUST start with the gate id — see the Step 1.4 reconcile contract)
   - `gh pr merge --merge` (auto-merges; if branch protection requires CI checks, add `--auto`
     so the PR lands when checks pass). A squash/rebase policy may replace `--merge` per repo
     convention — never force.
2. **Local-merge fallback (OBSOLETE)** — `origin` is now configured, so this path no longer applies; use option 1:
   - `git checkout main`
   - `git merge --no-ff <segment-branch> -m "integrate(<segment>): <gate-id> — verify+tests+review green"`
3. **Non-trivial conflict** (or `gh pr merge` rejected): `git merge --abort` (or `gh pr close`),
   mark the gate `blocked` via `orch-state.ps1`, write a session-log note, release the slot,
   **EXIT 1**. Auto-integration degrades to a human — it never force-merges and never resolves
   a semantic conflict on its own.
4. On success: `main` now contains the whole segment. Let `finalize_publish` (Step 5b.2) set the
   gate `done` and release the slot. Downstream segments (`base: main`) will branch from this
   updated `main` — which is exactly what keeps a later agent from starting on a `main` that is
   missing the upstream segment's code.

**Guardrails:**
- `integrate_to_main` is the only node permitted to `git checkout main`.
- It runs only when the three preceding checks are green; a red check sends the gate to
  `fix_*` (which loops back to `verify`), never to `integrate_to_main`.
- It never deletes the segment branch (other clones may still be syncing); sub-branch cleanup
  is unchanged (Step 5a).

---

## Concurrency: `tools/orch-state.ps1`

All state.yaml mutations go through `tools/orch-state.ps1`, which uses file locking
to prevent concurrent corruption. **Agents must NEVER edit state.yaml directly.**

### Commands

```powershell
# Read current state (no lock needed)
tools/orch-state.ps1 read

# Claim an item atomically (lock + verify pending + set claimed + update active_sessions)
tools/orch-state.ps1 claim -ItemId PIV01 -SlotId 2 -SessionId "orch-..." -ClonePath "C:\Source\Liakont2" -Subbranch "feat/core-foundation-PIV01"

# Update item status (lock + write)
tools/orch-state.ps1 update -ItemId PIV01 -Status done

# Release a slot (lock + clear active_sessions entry)
tools/orch-state.ps1 release -SlotId 2
```

The script uses a named `[System.Threading.Mutex]` to ensure only one agent modifies
state.yaml at a time.

---

## Rules

- **Fetch at start; sync the segment branch with `main` at checkout.** First action of every
  session (Step 1.0): `git fetch origin --prune`, then read the manifest / run selection against
  `origin/main` (the authoritative backlog — do not trust the possibly-stale copy in the current
  checkout). When the target segment branch is checked out (Step 3 §4): `git pull origin
  <segment-branch>` then `git merge origin/main --no-edit`, to pick up sibling work AND any
  manifest/spec/upstream-segment change merged to `main`. A segment branch left behind `main` reads
  a stale manifest → `manifest-sanity` reciprocity failure. On a non-trivial main-merge conflict:
  mark the claimed item `blocked` and EXIT 1 (the operator resolves the divergence). (`origin` is
  configured — pull and push are mandatory.)
- **ONE item per agent per session.** Implement, verify, review, commit — all in one session.
- **Each agent works in its own clone.** Clones sync via the remote (git push/fetch).
- **All state.yaml mutations go through `tools/orch-state.ps1`.** Never edit state.yaml directly.
- **Sub-branches are named `<segment-branch>-<item_id>`** (dash, never slash — ref namespace collision). Merged back via `--no-ff`.
- **Never merge to main — except an automated integration gate.** Work items and sub-branches
  never merge to main; they leave the segment branch for the gate. The ONE exception is a gate
  with `executor` ≠ `human` (`blueprint: auto-gate-item`): after verify + run-tests + integration
  review are green, its `integrate_to_main` node merges the segment branch into main (Step 5c).
  Human gates (`executor: human`) still stop at PR creation for a human to merge.
- **Always push after merge-back.** After merging the sub-branch into the segment branch, `git push origin <segment-branch>` immediately (`origin` is configured — pushing is mandatory). Other slots/agents depend on the remote to see your work. The default Claude Code "don't push unless asked" rule does NOT apply during orchestration — pushing is part of the pipeline.
- **Stay on the segment branch.** After merge-back, remain on the segment branch (e.g. `feat/core-foundation`). Never checkout `main` — the segment branch is the working base for all items in the segment. The ONLY exception is the `integrate_to_main` node of an automated gate (Step 5c), which checks out `main` to merge the finished segment into it.
- **Never force-push.** Always regular push.
- **Non-destructive recovery.** Never stash-drop, reset --hard, or delete branches (except cleanup of own sub-branch after successful merge-back).
- **Conventional commits.** Follow docs/architecture/repo-standards.md for commit messages
  (file created by SOL04 — until then, use the conventional-commits standard: `type(scope): subject`).
- **All CLAUDE.md rules apply.** Verification, review, coding standards — everything.
- **Regulatory caution.** Liakont is a tax-compliance product. Items touching TVA mapping (TVA*),
  validation rules (VAL*), or the audit trail (TRK*) must NEVER invent fiscal rules: every rule
  comes from `docs/conception/` or `docs/market/`. If a fiscal decision is missing
  (e.g., régime 6, TVA sur débits), mark the item `blocked` with a note — do not guess.
- **If anything unexpected happens** (tool error, git conflict, network failure):
  mark the item as `blocked` with notes, write session-log, release slot, EXIT.
- **Renew the lease heartbeat** during long operations (every `heartbeat_interval_minutes` from config.yaml).
- **P2 findings are not silently skipped.** Every P2 is either fixed or explicitly accepted
  with a documented reason in the session log. Unaddressed P2s are a false-green.
- **Session logs go to `$ORCH_REPO`**, not to the source repo. No "chore: session log" commits in the source repo.
