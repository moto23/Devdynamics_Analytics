# DevDynamics — GitHub Analytics Platform

A developer analytics platform that ingests real data from the GitHub REST API
and reports commit trends, pull request cycle time, contributor insights and
repository-level analytics over any set of tracked repositories.

**Live:** https://devdynamics-frontend.onrender.com
**API:** https://devdynamics-api.onrender.com/graphql

Repositories are managed at runtime. Adding, removing, enabling or disabling a
repository is a database change through the API — never a code change. The
platform scales from one repository to hundreds without modification.

---

## Architecture

```
┌─────────────────────────────┐
│  Angular 19 (standalone)    │   devdynamics-frontend.onrender.com
│  Apollo Client · ngx-charts │
└──────────────┬──────────────┘
               │ GraphQL over HTTPS
┌──────────────▼──────────────────────────────────────────┐
│  ASP.NET Core 8  ·  HotChocolate GraphQL                │
│                                                          │
│   Query    (public)          Mutation (X-Admin-Key)      │
│   summaryStats               addRepository               │
│   commitTrends               removeRepository            │
│   prCycleTime                setRepositoryActive         │
│   contributors               syncRepository              │
│   trackedRepositories        syncAllRepositories         │
│                                    │                     │
│                    RepositoryRegistryService             │
│                                    │ enqueue             │
│                    SyncQueue ─▶ SyncWorker (hosted)      │
│                                    │                     │
│                       ISourceControlProvider  ◀── seam   │
│                                    │                     │
│                            GitHubProvider                │
└───────────────┬─────────────────────────────┬────────────┘
                │ EF Core 8                   │ REST
┌───────────────▼──────────────┐   ┌──────────▼───────────┐
│  Azure SQL Database          │   │  GitHub REST API     │
│  TrackedRepositories         │   │  authenticated       │
│  Commits · PullRequests      │   │  paginated · ETags   │
│  Contributors                │   │  rate-limit aware    │
└──────────────────────────────┘   └──────────────────────┘
```

### Design notes

**The registry drives everything.** The sync worker reads only from
`TrackedRepositories`; analytics scope to active rows. No repository name or
count appears anywhere in the source.

**Provider-neutral domain.** `ISourceControlProvider` exchanges neutral records
(`RepositoryDescriptor`, `CommitRecord`, `PullRequestRecord`,
`ContributorRecord`). Transport concerns — pagination, conditional requests,
retries, rate limiting — live inside the provider implementation. Adding GitLab,
Azure DevOps or Bitbucket is one class plus one DI registration; the schema,
sync engine and analytics are untouched.

**Identity is `(Provider, ExternalId)`,** not a name. Repository transfers and
username changes resolve to the same row rather than creating duplicates.

**Idempotency is enforced by the database,** not by application logic:

| Constraint | Guarantees |
|---|---|
| `Commits (RepositoryId, Sha)` UNIQUE | a commit is inserted at most once |
| `PullRequests (RepositoryId, Number)` UNIQUE | a PR is updated in place on re-sync |
| `Contributors (Provider, ExternalId)` UNIQUE | one identity across all repositories |
| `TrackedRepositories (Provider, FullName)` UNIQUE | a repository cannot be added twice |

**Pull requests sync by `updated_at`, not `created_at`.** A PR mutates after
creation — an open PR is later merged. Syncing by creation date would
permanently miss merges on older PRs.

---

## Technology stack

| Layer | Technology |
|---|---|
| Frontend | Angular 19 (standalone components), TypeScript, Apollo Client, ngx-charts |
| API | ASP.NET Core 8, HotChocolate 15 (GraphQL) |
| Data access | Entity Framework Core 8 |
| Database | Azure SQL Database (SQL Server) |
| Ingestion | GitHub REST API v2022-11-28 |
| Hosting | Render (static site + Docker web service) |

---

## Features

- **Dynamic repository management** — add, remove, enable, disable and resync any
  public repository at runtime
- **Real GitHub ingestion** — repositories, commits, pull requests and contributors
- **Commit trends** — commits per day, filterable by repository, contributor and date
- **PR cycle time** — average hours from open to merge, bucketed by merge date over
  merged pull requests only
- **Contributor insights** — ranked activity, with optional exclusion of bot accounts
- **Incremental synchronisation** — cursor-based, resumable, idempotent
- **Background sync worker** — queued, non-blocking, recovers interrupted runs
- **Rate-limit aware** — conditional requests, graceful stop at a configurable floor
- **Admin-protected mutations** — analytics stay public, management requires a key

---

## Setup

### Prerequisites
- .NET 8 SDK
- Node.js 20+
- A SQL Server database (Azure SQL free tier works)
- A GitHub personal access token

### Backend

```bash
cd DevDynamics.API

# Secrets stay outside the repository
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "<sql-server-connection-string>"
dotnet user-secrets set "GitHub:Token" "<github-token>"
dotnet user-secrets set "Admin:ApiKey" "<random-admin-key>"

dotnet run
```

Migrations apply automatically at startup. With no connection string the API
falls back to a local SQLite file so it still runs.

### Frontend

```bash
cd devdynamics-frontend
npm install
npm start          # http://localhost:4200
```

`src/environments/environment.ts` points at `http://localhost:5236/graphql`;
`environment.prod.ts` targets the deployed API.

### GitHub token

Create a **classic** token at *Settings → Developer settings → Personal access
tokens*. For public repositories, **check no scopes at all** — an unscoped token
still receives the full 5,000 requests/hour while having no write access to your
account. Do not grant `repo`; it is not needed and grants full access to your
private repositories.

---

## Environment variables

Configuration binds with `__` (double underscore) mapping to `:`.

| Variable | Required | Description |
|---|---|---|
| `ConnectionStrings__Default` | yes | SQL Server connection string. Falls back to SQLite when unset. |
| `GitHub__Token` | yes | GitHub PAT. Without it, syncs fail cleanly; ingested data still serves. |
| `Admin__ApiKey` | yes | Guards management mutations. **Fails closed** when unset. |
| `GitHub__SyncWindowDays` | no | First-sync lookback. Default `90`. |
| `GitHub__MaxPagesPerResource` | no | Page budget per resource per run. Default `20`. |
| `GitHub__PageSize` | no | Items per page, max 100. Default `100`. |
| `GitHub__RateLimitFloor` | no | Stop syncing below this many remaining calls. Default `100`. |
| `GitHub__OverlapBufferMinutes` | no | Re-request window absorbing clock skew. Default `60`. |
| `GitHub__WriteBatchSize` | no | Rows per database round trip. Default `250`. |
| `Database__*` | no | Retry count, delays, command timeout, startup wait. |
| `Cors__AllowedOrigins__0` | no | Allowed origins; falls back to the deployed frontend. |

**No secret is ever committed.** Locally they live in .NET user-secrets, outside
the repository; in production they are Render environment variables.

---

## GitHub integration

Follows GitHub REST API guidance:

- **Authenticated requests** — `Authorization: Bearer`, explicit `User-Agent`,
  pinned `X-GitHub-Api-Version: 2022-11-28`
- **Pagination** — follows the `Link` header's `rel="next"`, capped by a
  configurable page budget
- **Conditional requests** — `If-None-Match` on the first page of each resource;
  a `304 Not Modified` costs no rate limit
- **Incremental sync** — commits by `since`, pull requests by `updated_at`
- **Rate limits** — reads `x-ratelimit-remaining` / `x-ratelimit-reset` and stops
  cleanly at the configured floor, marking the repository `PartiallySynced`
- **Retries** — honours `Retry-After` for primary and secondary limits;
  exponential backoff with jitter on `5xx`; no retry on `4xx`
- **Terminal failures** — a deleted, renamed or private repository is deactivated
  rather than retried indefinitely

Cost per repository per sync is at most `1 + 2 × MaxPagesPerResource` requests
(41 with defaults).

---

## Demo workflow

```graphql
# 1. What is currently tracked?
query { trackedRepositories { id fullName syncStatus totalCommits totalPullRequests } }

# 2. Track any public repository  (header: X-Admin-Key: <key>)
mutation { addRepository(fullName: "vercel/next.js") { success message } }

# 3. Analytics, automatically including the new repository
query {
  summaryStats { totalCommits totalPullRequests mergedPullRequests contributorCount avgPrCycleHours }
  commitTrends(excludeBots: true) { date commits }
  prCycleTime { date avgHours mergedCount }
  contributors { login commits isBot }
}

# 4. Scope to one repository and a date range
query {
  commitTrends(
    repository: "dotnet/efcore"
    startDate: "2026-06-01T00:00:00.000Z"
    endDate: "2026-07-22T23:59:59.999Z"
    excludeBots: true
  ) { date commits }
}

# 5. Disable without losing history, or remove entirely
mutation { setRepositoryActive(id: 5, isActive: false) { success } }
mutation { removeRepository(id: 5) { success } }
```

Operational endpoints: `/health` (liveness), `/db-info` (provider, migrations,
connectivity — no credentials), `/benchmark` (query optimization comparison).

---

## Screenshots

> To add: dashboard with KPI cards and charts, repositories page with sync
> status. The dashboard receives its full visual treatment in the UI phase; the
> current interface is functional rather than final.

---

## Current dataset

Six demo repositories, seeded only into an empty registry and removable like any
other row:

| Metric | Value |
|---|---|
| Commits | 2,873 |
| Pull requests | 3,611 (2,507 merged) |
| Contributors | 270 |
| Repositories | 6 |
| Average PR cycle time | 150.7 hours |

---

## Known limitations

- **Sync window defaults to 90 days.** Older history requires raising
  `GitHub__SyncWindowDays` and re-syncing.
- **Page budget caps very large repositories.** `supabase/supabase` stops at
  2,000 pull requests with default settings and reports `PartiallySynced`.
- **Contributor display names populate incrementally.** The name comes from
  commit metadata, so a contributor first seen through a pull request has no
  name until they appear as a commit author.
- **Sync is sequential and single-instance.** Correct at this scale; many
  repositories would want concurrency and a durable queue.
- **No scheduled sync.** Syncs are triggered through the API.
- **Azure SQL serverless auto-pauses** when idle, so the first request after a
  quiet period waits for a resume. Startup waits for this explicitly.
- **Render free tier spins down** when idle; the first request pays a cold start.
- **`DevActivities`** still holds legacy synthetic rows, retained only so the
  query optimization benchmark stays reproducible. Unused by the dashboard.

---

## Roadmap

- Advanced analytics: cross-contributor comparison, activity distribution,
  repository-level breakdowns
- Chart.js visualisations with a redesigned, responsive dashboard
- Repository management UI for add / remove / enable / disable / resync
- "Continue with GitHub" entry experience
- Scheduled background synchronisation
- Additional providers: GitLab, Azure DevOps, Bitbucket — the abstraction is
  already in place
