# Job Module — Scenarios

## SC-JOB-001: Enqueue a job

**Given** a valid job type and payload
**When** IJobQueue.EnqueueAsync is called
**Then** a new job is persisted with status Pending, priority 0, max_retries 3.

## SC-JOB-002: Enqueue with custom priority and schedule

**Given** a valid job type, payload, priority=10, scheduledAt=future
**When** IJobQueue.EnqueueAsync is called
**Then** a new job is persisted with the specified priority and scheduled_at.

## SC-JOB-003: Acquire pending job (SKIP LOCKED)

**Given** a pending job in the queue with scheduled_at <= now
**When** AcquireNextPendingJobAsync is called within a transaction
**Then** the job row is locked (FOR UPDATE SKIP LOCKED) and returned.

## SC-JOB-004: Concurrent workers skip locked jobs

**Given** a single pending job locked by worker A
**When** worker B calls AcquireNextPendingJobAsync
**Then** worker B skips the locked row and returns null (or a different job).

## SC-JOB-005: Job completes successfully

**Given** a running job
**When** MarkCompleted is called
**Then** status = Completed, completed_at is set, job.job.completed event is published.

## SC-JOB-006: Job fails with retries remaining

**Given** a running job with retry_count < max_retries
**When** MarkFailed is called
**Then** status returns to Pending, retry_count increments, error_message is set.

## SC-JOB-007: Job fails with max retries exhausted

**Given** a running job with retry_count = max_retries - 1
**When** MarkFailed is called
**Then** status = Dead, retry_count = max_retries, job.job.dead_lettered event is published.

## SC-JOB-008: Dead job cannot be retried

**Given** a Dead job
**When** MarkRunning is called
**Then** InvalidOperationException is thrown (INV-JOB-002).

## SC-JOB-009: Invalid status transition rejected

**Given** a Completed job
**When** MarkRunning is called
**Then** InvalidOperationException is thrown (INV-JOB-002).

## SC-JOB-010: Worker polls and executes a pending job

**Given** a pending job in the queue
**When** JobWorker polls the queue
**Then** the job is acquired, handler is resolved from DI, handler.HandleAsync is called, status = Completed.

## SC-JOB-011: Worker retries failed job

**Given** a job that fails on first execution (retry_count < max_retries)
**When** JobWorker processes the job
**Then** status returns to Pending with retry_count incremented, job.job.failed event published.

## SC-JOB-012: Worker dead-letters after max retries

**Given** a job that fails on every execution
**When** JobWorker processes it max_retries times
**Then** status = Dead, job.job.dead_lettered event published, no further processing.

## SC-JOB-013: Worker handles no available jobs gracefully

**Given** no pending jobs in the queue
**When** JobWorker polls
**Then** no error, worker waits for next polling cycle.

## SC-JOB-014: Worker handles missing handler gracefully

**Given** a pending job with an unregistered type
**When** JobWorker attempts to execute
**Then** job is marked Failed with descriptive error message, worker does not crash.
