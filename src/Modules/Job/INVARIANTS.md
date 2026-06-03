# Job Module — Invariants

| ID | Rule | Enforcement |
|---|---|---|
| INV-JOB-001 | Job type must not be empty | JobEntry.Create() validation |
| INV-JOB-002 | Valid status transitions: Pending→Running, Running→Completed, Running→Failed(→Pending or →Dead) | JobEntry domain methods with AssertTransition |
| INV-JOB-003 | Dead jobs are not retried (retry_count >= max_retries → Dead, no transition out of Dead) | MarkFailed() logic + AssertTransition blocks Dead→Running |
