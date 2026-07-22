# DevDynamics v2.0 — GitHub App authentication and personal dashboards

**Status:** proposal, not scheduled. v1.0 RC1 is the current baseline (`v1.0-rc1`).

v1.0 is deliberately single-tenant: one shared registry of tracked repositories,
one Admin API key guarding writes, and a dashboard that shows the same data to
everyone. v2.0's premise is that the same analytics become materially more useful
when they are scoped to *you* — your repositories, including private ones, and
your contribution history.

This document exists to make that change tractable later, and to record which
parts of v1.0 were built to accommodate it.

---

## 1. What v1.0 already got right for this

Three v1.0 decisions carry directly into v2.0 and should not be redesigned:

| v1.0 asset | Why it matters in v2.0 |
|---|---|
| `TrackedRepositories` registry (`Provider` + `ExternalId` identity) | Repositories are already runtime data, not code. Adding an owner is one nullable column, not a re-architecture. |
| `ISourceControlProvider` seam with provider-neutral records | Token acquisition changes; the ingestion pipeline does not. A GitHub App installation token is still just a bearer credential handed to the provider. |
| Sync cursors + the four uniqueness constraints | Per-user sync of an already-tracked repository cannot duplicate rows. Two users tracking `facebook/react` share one ingested copy. |

The last point is the important one. **Repositories are shared; access to them is
per-user.** v2.0 should not ingest the same repository twice because two people
track it.

---

## 2. Authentication: GitHub App, not OAuth App

A GitHub App is the right instrument here, and the reasoning is not
interchangeable with OAuth:

- **Installation tokens are scoped to selected repositories.** The user picks
  which repositories DevDynamics may read, in GitHub's own UI. We never hold a
  credential that can read everything they can read.
- **Rate limits are per-installation**, not shared against one PAT. v1.0's single
  token is the current ceiling on ingestion throughput.
- **Tokens are short-lived** (1 hour) and minted from a signed JWT. A database
  compromise leaks nothing durable — unlike a stored PAT.
- **Revocation is the user's**, via GitHub's installation settings, and takes
  effect without us doing anything.

Two credential flows are needed and they are not the same thing:

1. **User identity** — OAuth web flow (`/login/oauth/authorize`) to learn *who*
   is signed in. Yields a user access token; used once to read `/user`, then
   discarded. We do not need ongoing user-token access.
2. **Repository data** — App installation tokens, minted per installation from
   the App's private key, used by the sync worker. This is what reads commits and
   pull requests.

Session handling: an HttpOnly, `Secure`, `SameSite=Lax` cookie carrying a signed
session ID. **Not** a JWT in `localStorage` — v1.0 already puts the admin key in
`sessionStorage` rather than `localStorage` for the same reason, and a bearer
token reachable from JavaScript is strictly worse than a cookie the page cannot
read.

### Secrets this introduces

The App's **private key** is a new class of secret for this project — it signs
JWTs that mint installation tokens, so it is the crown jewel. It belongs in an
environment variable (or a secret manager), never in the repository, and it
should be rotatable without a redeploy of the frontend. The same applies to the
App's client secret and the session-signing key.

---

## 3. Data model changes

Additive; no existing table is restructured.

```
Users
  Id, Provider, ExternalId (unique), Login, Name, AvatarUrl,
  CreatedAt, LastSeenAt

Installations
  Id, Provider, ExternalInstallationId (unique), AccountLogin,
  AccountType, SuspendedAt, CreatedAt

UserInstallations            -- a user may administer several installations
  UserId, InstallationId     -- (composite PK)

RepositoryAccess             -- which installation grants which repository
  InstallationId, RepositoryId, GrantedAt
  -- (composite PK; RepositoryId -> TrackedRepositories)

TrackedRepositories
  + IsPrivate      bit not null default 0
  + Visibility     -- public repositories stay world-readable as in v1.0
```

The access check becomes: *a user may read a repository if it is public, or if
some installation they belong to has access to it.* One join, expressible as an
EF Core query filter so it cannot be forgotten at a call site.

**This is the highest-risk change in v2.0.** A missing predicate leaks private
repository data. It should be enforced in one place — a global query filter on
the analytics context — and covered by tests that assert a user cannot read a
repository they were never granted, not merely that they *can* read their own.

---

## 4. Personal dashboards

Once identity exists, three views become possible that v1.0 cannot offer:

- **My activity** — the existing contributor detail page, resolved to the signed-in
  user automatically, across every repository they have access to rather than
  only the tracked set.
- **My repositories** — the registry filtered to their installations, with
  per-user pinning (v1.0's `IsPinned` becomes per-user rather than global).
- **Comparison** — their metrics against the aggregate of repositories they can
  see. This must be framed carefully: commit counts are not a performance
  measure, and the UI should not imply otherwise.

Saved filter presets and per-user default date ranges are the natural follow-on,
since `Users` finally gives them somewhere to live.

---

## 5. What has to change in the sync worker

v1.0 syncs a fixed set on an interval. v2.0 has a variable set, per installation,
with per-installation rate limits. Concretely:

- Token acquisition moves behind an `IInstallationTokenProvider` with a cache
  keyed by installation, refreshing before the 1-hour expiry.
- Scheduling becomes per-installation rather than global, so one busy
  installation cannot starve another.
- **Webhooks replace polling** where possible: `push`, `pull_request`, and
  `installation_repositories` events keep data fresh without burning rate limit.
  Signature verification (`X-Hub-Signature-256`) is mandatory — an unverified
  webhook endpoint is an unauthenticated write path into the database.
- `installation` / `installation_repositories` `removed` events must revoke
  access promptly, or a user keeps seeing a repository after uninstalling.

---

## 6. Cost and hosting

This is the part most likely to force a decision.

v1.0 fits free tiers because the data set is bounded and shared. v2.0's footprint
grows with users × their repositories, and private repositories cannot be
deduplicated across unrelated installations the way popular public ones can.

- **Azure SQL free tier** (100k vCore-seconds/month, auto-pause) is already the
  binding constraint at 2,873 commits. A few dozen active installations would
  exceed it.
- **Render free tier** cold starts are tolerable for a portfolio demo and are not
  tolerable for a tool people sign into.

So v2.0 realistically implies a paid tier. That should be an explicit decision
before implementation starts, not something discovered mid-build.

---

## 7. Suggested sequencing

Each step is independently shippable and leaves the app working.

1. **Users + sign-in only.** OAuth identity, session cookie, avatar in the header.
   No behaviour change to analytics. Proves the auth flow in isolation.
2. **Installations + `RepositoryAccess`, public repositories only.** The access
   predicate goes in and is tested while nothing private is at stake — if it is
   wrong, nothing leaks.
3. **Private repositories.** Only after step 2's filter is covered by tests.
4. **Personal dashboard views.** Pure frontend on top of an API that already
   scopes correctly.
5. **Webhooks.** Replaces polling; largest rate-limit win, and safe to defer.
6. **Retire the Admin API key** — it becomes redundant once real identity exists,
   and leaving two authorization systems in place is how one of them rots.

Steps 1–3 are the substance. Steps 4–6 are comparatively mechanical.

---

## 8. Out of scope

Named explicitly so they don't accrete into v2.0:

- Organizations, teams, and role hierarchies beyond installation membership
- GitLab or Azure DevOps providers (the seam exists; using it is its own project)
- Billing, plans, or usage quotas
- Any metric presented as an individual performance score
