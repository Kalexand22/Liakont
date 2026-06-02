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
- Feature specs: `docs/conception/` (F01-F11 — the functional source of truth)
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

Each agent runs in its **own full clone** of the source repo (e.g., `Conformat`, `Conformat2`,
`Conformat3`). Synchronization happens via the remote (git push/fetch).

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

1. **Check if committed**: search `git log --oneline` for a commit matching the
   item's expected pattern (e.g., `feat(<scope>): <item_id>` or the item title).
   - If no commit found → auto-recovery fails. Item stays `stale`. Skip it.
2. **Verify the commit**: run `powershell -ExecutionPolicy Bypass -File tools/verify-fast.ps1`.
   - If **FAIL** → auto-recovery fails. Item stays `stale`. Skip it.
3. **Run codex-review**: run `powershell -ExecutionPolicy Bypass -File tools/codex-review.ps1 -Base "<commit>~1"`.
   - If **P1 findings**: fix them autonomously, commit fixes, re-run review (standard loop).
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

1. Read `orchestration/manifest.yaml` — the authoritative backlog (index only).
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

---

## Step 2 — Select + Claim Item

1. For each item in the manifest:
   - Check its `status` in state.yaml.
   - **If the item is not in state.yaml → skip it (absent = done, purged).**
   - An item is **eligible** if:
     - `status` = `pending` (explicitly present in state.yaml)
     - ALL items in `depends_on` have `status` = `done` in state.yaml
     - `executor` is `claude` (not `human`) — read from `orchestration/items/<lot>.yaml`
     - **The item's segment is unlocked**: if the segment declares `depends_on_gate`
       (a single gate or a list), ALL of those gates must have `status` = `done` in state.yaml
     - **No active agent is working on a dependency**: none of the item's `depends_on` have
       `status` = `claimed` or `in_progress` (prevents two agents from working on dependent items)
2. Check gates separately:
   - A gate (`type: gate`) is **gate-ready** if all its `depends_on` have `status: done`.
   - If a gate is gate-ready and its status is `pending`:
     - Update via `tools/orch-state.ps1 update -ItemId <id> -Status gate_pending`
     - Append transition to events.jsonl
     - If this is the segment's gate, and the segment branch has commits:
       - Push the branch: `git push -u origin <branch>`
       - Create PR: `gh pr create --base main --head <branch> --title "<gate title>" --body "..."`
       - Update segment status via orch-state.ps1
     - Write session-log entry to `$ORCH_REPO/session-log/`
     - Release slot (delete `$ORCH_REPO/leases/slot-$SLOT_ID.yaml`)
     - **EXIT 0** with message "Gate <id> is ready for human validation."
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

1. Determine the item's segment: find which segment in the manifest contains the item's `lot`.
2. Get the segment branch name from `segments.<segment>.branch` (e.g., `feat/core-foundation`).
3. Define the sub-branch name: `<segment-branch>-<item_id>` (e.g., `feat/core-foundation-PIV01`).
   **Dash separator, never a slash**: git refs are stored as files, so `feat/socle` (file) and
   `feat/socle/SOL01` (would require `feat/socle` to be a directory) cannot coexist.

4. `git fetch origin`, checkout segment branch (create from `base` if it does not exist yet),
   `git pull`, then `git checkout -b "$SEGMENT_BRANCH-<item_id>"`.

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
   - **deterministic** nodes: run the specified `action`:
     - `build-agent-context` → `powershell -ExecutionPolicy Bypass -File tools/build-agent-context.ps1 -ItemId <item_id>`, then read all listed files.
     - `verify-fast` → `powershell -ExecutionPolicy Bypass -File tools/verify-fast.ps1`
     - `run-tests` → `powershell -ExecutionPolicy Bypass -File tools/run-tests.ps1` (full unit + integration test suite)
     - `codex-review` → `powershell -ExecutionPolicy Bypass -File tools/codex-review.ps1 -Base "<segment-branch>"`
       (with `-Round N` on re-runs). **`-Base` is MANDATORY in orchestration**: the review node
       runs after `commit_apply`, so the working tree is clean — without `-Base`, there is
       nothing to review and the run fails with exit 3.
       **Exit codes**: 0 = review ran on the sub-branch diff and is CLEAN. 2 = review ran and
       has P1/P2 findings (triggers the `fix_review` node). 3 = nothing was reviewed (caller
       error — fix the -Base argument, never treat as clean). Any other code = the review could
       not run (tool failure — retry per the node's retry policy, never treat as clean).
     - `git-commit-apply` → `git add -A && git commit -F .orch-commit-msg && rm .orch-commit-msg`. Requires that the preceding `commit_compose` node (delegated to `orch-commit`) has written the message file at the repo root. The file is gitignored (see `.gitignore`), so `git add -A` will not stage it. If `.orch-commit-msg` is missing, mark the item `blocked` and exit.
     - `merge-back` → see Step 5a.
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

**Note (no remote yet):** while the repo has no `origin` remote configured, the push/pull steps
are skipped — merges remain local. As soon as a remote exists, pushing becomes mandatory.

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
4. Check if the **next eligible item** after this one would be a gate:
   - If yes: mark the gate as `gate_pending`, push the segment branch, create a PR (see Step 2).
5. Release the lease file: delete `$ORCH_REPO/leases/slot-$SLOT_ID.yaml`.
6. **EXIT 0**.

If the session log file is missing when `finalize_publish` runs, mark the item `blocked`, write a recovery note in `events.jsonl`, release the slot, and EXIT 1.

---

## Concurrency: `tools/orch-state.ps1`

All state.yaml mutations go through `tools/orch-state.ps1`, which uses file locking
to prevent concurrent corruption. **Agents must NEVER edit state.yaml directly.**

### Commands

```powershell
# Read current state (no lock needed)
tools/orch-state.ps1 read

# Claim an item atomically (lock + verify pending + set claimed + update active_sessions)
tools/orch-state.ps1 claim -ItemId PIV01 -SlotId 2 -SessionId "orch-..." -ClonePath "C:\Source\Conformat2" -Subbranch "feat/core-foundation-PIV01"

# Update item status (lock + write)
tools/orch-state.ps1 update -ItemId PIV01 -Status done

# Release a slot (lock + clear active_sessions entry)
tools/orch-state.ps1 release -SlotId 2
```

The script uses a named `[System.Threading.Mutex]` to ensure only one agent modifies
state.yaml at a time.

---

## Rules

- **Pull before starting.** First action of every session: `git pull origin <segment-branch>` to pick up work pushed by other agents/slots since the last session (skip while no remote is configured).
- **ONE item per agent per session.** Implement, verify, review, commit — all in one session.
- **Each agent works in its own clone.** Clones sync via the remote (git push/fetch).
- **All state.yaml mutations go through `tools/orch-state.ps1`.** Never edit state.yaml directly.
- **Sub-branches are named `<segment-branch>-<item_id>`** (dash, never slash — ref namespace collision). Merged back via `--no-ff`.
- **Never merge to main.** Leave the segment branch for human PR review.
- **Always push after merge-back.** After merging the sub-branch into the segment branch, `git push origin <segment-branch>` immediately (once a remote exists). Other slots/agents depend on the remote to see your work. The default Claude Code "don't push unless asked" rule does NOT apply during orchestration — pushing is part of the pipeline.
- **Stay on the segment branch.** After merge-back, remain on the segment branch (e.g. `feat/core-foundation`). Never checkout `main` — the segment branch is the working base for all items in the segment.
- **Never force-push.** Always regular push.
- **Non-destructive recovery.** Never stash-drop, reset --hard, or delete branches (except cleanup of own sub-branch after successful merge-back).
- **Conventional commits.** Follow docs/architecture/repo-standards.md for commit messages.
- **All CLAUDE.md rules apply.** Verification, review, coding standards — everything.
- **Regulatory caution.** Conformat is a tax-compliance product. Items touching TVA mapping (TVA*),
  validation rules (VAL*), or the audit trail (TRK*) must NEVER invent fiscal rules: every rule
  comes from `docs/conception/` or `docs/market/`. If a fiscal decision is missing
  (e.g., régime 6, TVA sur débits), mark the item `blocked` with a note — do not guess.
- **If anything unexpected happens** (tool error, git conflict, network failure):
  mark the item as `blocked` with notes, write session-log, release slot, EXIT.
- **Renew the lease heartbeat** during long operations (every `heartbeat_interval_minutes` from config.yaml).
- **P2 findings are not silently skipped.** Every P2 is either fixed or explicitly accepted
  with a documented reason in the session log. Unaddressed P2s are a false-green.
- **Session logs go to `$ORCH_REPO`**, not to the source repo. No "chore: session log" commits in the source repo.
