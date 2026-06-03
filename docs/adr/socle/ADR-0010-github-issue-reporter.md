# ADR-0010: HttpClient for GitHub Issue Creation (BugCapture)

**Date:** 2026-03-28

**Status:** Accepted

---

## Context

BCF04 replaces the local-disk `BundleWriter` with a GitHub Issue reporter so that
bug/feature reports captured via BugCapture are submitted directly as GitHub Issues
instead of writing files to `reports/`.

Two approaches were evaluated:

| Criterion | Octokit.net | HttpClient (built-in) |
|-----------|-------------|------------------------|
| **New dependency** | Yes (NuGet) | No (framework-provided) |
| **API surface used** | Create Issue only | Create Issue only |
| **Auth** | Built-in token handling | One header line |
| **Maintenance** | Track Octokit versions | Stable REST API |
| **Encapsulation** | Leaks Octokit types | Fully internal |

We use exactly **one** GitHub API endpoint (`POST /repos/{owner}/{repo}/issues`).
Introducing a full SDK for a single call adds dependency weight, version coupling,
and violates the `allowed-dependencies.md` minimal-dependency principle.

---

## Decision

Use `System.Net.Http.HttpClient` with the GitHub REST API directly.

- Registered via `IHttpClientFactory` (`AddHttpClient<GitHubIssueReporter>`).
- Bearer token authentication via `Authorization` header.
- JSON serialization via `System.Text.Json` (already in use).
- Fallback to `BundleWriter` (local disk) on any HTTP/network error.

---

## Consequences

- **No new NuGet package required.** Zero dependency delta.
- The `GitHubIssueReporter` is fully internal to `Stratum.Common.Infrastructure`.
- If future needs require more GitHub API calls (e.g., uploading assets, managing
  labels), Octokit adoption can be reconsidered via a new ADR.
- The GitHub token is stored in `appsettings` / user-secrets (never committed).
