# Job Module

## Purpose

PostgreSQL-based asynchronous job queue for background task execution. Uses SELECT ... FOR UPDATE SKIP LOCKED for safe concurrent polling without external message brokers.

## Schema

`job`

## Entities

- **JobEntry** — A unit of background work with typed payload, status lifecycle, retry logic, and priority ordering.

## Key Interfaces

- `IJobQueue` — Cross-module contract for enqueuing typed jobs with priority and scheduling options.
- `IJobHandler<T>` — Cross-module contract for implementing job handlers (registered in DI by consuming modules).
- `IJobHandlerResolver` — Resolves and invokes the correct IJobHandler<T> by job type name at runtime.
- `IJobUnitOfWork` — Write-side transactional boundary including SKIP LOCKED acquisition.
- `IJobQueries` — Read-side queries for job status inspection.

## Background Services

- **JobWorker** — BackgroundService that polls `job.jobs` for pending jobs, acquires via SKIP LOCKED, resolves handler from DI, executes, and updates status. Configurable polling interval and batch size via `JobWorkerOptions`.
- **JobEventTypeRegistrar** — Registers job event types at startup.

## Events

- `job.job.completed` — Emitted when a job finishes successfully.
- `job.job.failed` — Emitted when a job fails but will be retried.
- `job.job.dead_lettered` — Emitted when a job exhausts its retries and is marked Dead.

## Endpoints

| Method | Path | Permission |
|--------|------|------------|
| GET | `/api/job/jobs/{id}` | `job.view` |
| GET | `/api/job/jobs?status=X` | `job.view` |
