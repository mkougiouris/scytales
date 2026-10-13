# Implementation Steps

> Companion to [`requirements.md`](requirements.md). Requirement IDs (FR-x, RL-x, D-x, …) refer to that document.
> Decisions were taken one at a time, before building; each entry records the choice and its rationale.

---

## 1. Decision Log

| ID | Decision | Choice | Status |
|---|---|---|---|
| D-4 | Database | PostgreSQL (Docker) + explicit `bigint` concurrency token | 🟢 Accepted |
| D-1 | Photo handling | Single `multipart/form-data` request → separate `ApplicationPhoto` table, persisted in the same transaction | 🟢 Accepted |
| D-2 | Workflow engine | Custom domain-driven state machine on the `Application` aggregate | 🟢 Accepted |
| D-3 | Queue / messaging | MassTransit v8 + RabbitMQ, EF Core transactional outbox (API) on PostgreSQL; worker without inbox, per-step commits | 🟢 Accepted |
| D-5 | Simulated photo quality score | Deterministic score derived from the photo bytes, behind `IPhotoQualityScorer` (Application-layer port) | 🟢 Accepted |
| D-6 | Watchlist & thresholds configuration | `appsettings.json` section → validated options class, env-var overridable | 🟢 Accepted |
| D-7 | Error contract for invalid transitions | RFC 9457 `ProblemDetails`: `409` / `404` / `400` / `413` | 🟢 Accepted |
| D-8 | Reliability approach (RL-1 … RL-7) | Consolidated answers below; mechanisms come from D-2, D-3 and D-4 | 🟢 Accepted |
| D-9 | AI usage disclosure | Dedicated README section, kept up to date as we go | 🟢 Accepted |
| D-10 | Business rules shape | One policy per step behind a Domain interface, default implementations swapped via DI | 🟢 Accepted |
| D-11 | Watchlist lookup | Application-layer port `IWatchlist`; the domain receives `isWatchlisted` | 🟢 Accepted |
| D-12 | Unit test stack | xUnit v3 + Shouldly, on Microsoft Testing Platform (`global.json`) | 🟢 Accepted |
| D-13 | Worker host | Minimal web host serving only `/health/live` + `/health/ready`; no migrations (the API applies them); JSON console logs | 🟢 Accepted |
| D-14 | API host | Minimal APIs, 202 + `Location` on submit, ProblemDetails via one exception handler, built-in OpenAPI + Swagger UI, migrations on startup | 🟢 Accepted |
| D-15 | Local runtime | Docker Compose project `scytales`: one multi-target Dockerfile, health-ordered startup, `docker compose up -d` from the root | 🟢 Accepted |
| D-16 | Integration tests | Testcontainers (Postgres + RabbitMQ) with the real API (on Kestrel) and Worker hosts in-process | 🟢 Accepted |
| D-17 | Idempotent submission (BN-2) | Optional `Idempotency-Key` header, stored on the aggregate with a request hash under a unique index; replay → same application, reuse for a different request → `422` | 🟢 Accepted |
| D-18 | Observability (BN-5) | OpenTelemetry traces, metrics and logs from both hosts over OTLP to the .NET Aspire Dashboard in Compose; one span per workflow step | 🟢 Accepted |
| D-19 | Infrastructure tests | Testcontainers PostgreSQL only, real migrations and `AddInfrastructure()` wiring; both concurrency guards and the idempotency index tested deterministically | 🟢 Accepted |

---

### D-4 — Database: PostgreSQL

**Choice:** PostgreSQL in Docker (official `postgres` Alpine image), accessed through EF Core (Npgsql provider).

**Why**
- It's the database the brief suggests, so it needs no defending.
- Open-source licence: no EULA and no SA password rules.
- Small multi-arch image (runs natively on Intel and Apple Silicon), low memory use, ready in a few seconds. That
  makes the reviewer's first `docker compose up` quick.
- If we later choose a PostgreSQL-backed queue or outbox for D-3, everything stays in one database.

**Concurrency: explicit `Version` token**
- Each `Application` has a `long Version` (`bigint`) column, configured in EF Core with `.IsConcurrencyToken()`.
- EF Core adds `WHERE "Version" = @original` to every UPDATE. If no row matches, it throws
  `DbUpdateConcurrencyException`, so a stale or duplicate writer loses instead of silently overwriting (RL-1, RL-4).
- PostgreSQL doesn't auto-generate this value (unlike SQL Server's `rowversion`), so **we increment it
  ourselves**, in exactly one place: the domain method that changes state (`Apply(...)` → `Version++`).
  Because every status change goes through that method, the token can't be forgotten.
- **Two guards, in practice (found by the Infrastructure tests, D-19):** every transition also inserts a history
  entry keyed by (application, version), and EF runs that INSERT before the UPDATE. So when two writers race
  from the same version, the **history key** rejects the second first (a unique violation). The `Version`
  check still catches a row changed without a history entry. The unit of work turns both into
  `ConcurrencyConflictException` (`409`), and each path has its own test.
- We chose this over Npgsql's built-in `xmin` system column because it's explicit, works with any database
  provider, and doubles as a readable revision number for the history and logs.

**Docker**
- Compose `healthcheck` runs `pg_isready`; the API and worker wait on it with
  `depends_on: condition: service_healthy`.
- Credentials and database name come from `.env`.

---

### D-1 — Photo: single multipart request, atomic persistence

**Choice:** `POST /api/applications` as **`multipart/form-data`**. The personal details are form fields and the
face photo is a `photo` file part. The API stores the application, the photo, the initial history entry and the
outbound message **in one database transaction**.

**Why multipart (not base64 / separate endpoint)**
- Multipart is the standard way to send a file and its metadata in one request. Base64 makes the payload ~33%
  bigger, and the whole thing has to be buffered as a JSON string.
- A separate upload endpoint means two calls, an application that exists for a while without its photo, and
  orphaned uploads to clean up.
- ASP.NET Core binds it with `[FromForm]` plus `IFormFile`, and Swagger shows a **file picker**, so reviewers can
  try it straight from Swagger UI.

**Persistence design**
- The photo lives in its own **`ApplicationPhoto`** entity/table, separate from `Application`, so status and
  history queries never load the bytes. Its columns are exactly what's needed to rebuild the original upload:

  | Column | Type | Notes |
  |---|---|---|
  | `ApplicationId` | `uuid` | **Primary key and foreign key** → `Application.Id` (shared key, 1:1, cascade delete) |
  | `FileName` | `text` | Original file name as uploaded |
  | `ContentType` | `text` | Declared by the client; the worker re-checks it against the magic bytes |
  | `FileSize` | `bigint` | Bytes; lets the size rule be checked without loading the blob |
  | `Content` | `bytea` | The file bytes |

- Using the application's ID as the photo's key enforces **at most one photo per application** in the database
  itself, with no extra unique index. No photo means no row, which is exactly what the "no photo provided" rule
  (FR-5.2.1) checks.
- **Domain model:** `ApplicationPhoto` is an **owned value object** of the `Application` aggregate
  (`Application.Photo`), mapped to its own table. Like `ApplicantDetails`, it has no lifecycle of its own.
  - *Accepted trade-off (for now):* EF Core always loads owned types with their owner, so loading the aggregate
    also loads the photo bytes. We keep this small by having the worker load the aggregate **once** per message
    and run every step, and by serving status/history through projections that never touch the photo table.
    **To revisit in the next iteration:** move the bytes to blob storage, keeping a storage key in the value
    object.
- **One transaction** covers `Application` + `ApplicationPhoto` + the first `ApplicationHistory` row
  (`ApplicationCreated`) + the outbox message. This is also the answer to RL-2, "API saves but fails to publish".
- An `IPhotoStore` abstraction hides the storage. The production path is blob storage (S3 / Azure Blob) with only
  a reference kept in the DB. Note: then the save is no longer one atomic transaction, so you upload the blob
  first, commit the DB row second, and sweep up orphaned blobs.
- The queue message carries **only the `applicationId`**, never the photo bytes.

**Boundary between the API and the workflow** ⚠️ the important part

The brief defines "no photo", "unsupported type", "too small", "under 18" and "invalid email" as **workflow
failures** (FR-5.1 / FR-5.2), and they must show up in the audit trail. If the API rejected them with a `400`,
those rules would never run. So:

| Where | Checks | Outcome |
|---|---|---|
| **API** (protection only) | Request is well-formed multipart; body ≤ hard limit (e.g. 5 MB via `[RequestSizeLimit]`) | `400` / `413`, nothing stored |
| **Worker** (business rules) | Missing fields, email, national ID, age, photo present, type, minimum size, quality score | Application moves to `Failed`, with the reason in the history |

- The worker checks the file type from its **magic bytes** (JPEG/PNG signature), not the client's `Content-Type`
  header.
- The photo is optional in the API's form contract so that FR-5.2.1 ("no photo provided") can actually happen.

---

### D-2 — Workflow engine: domain-driven state machine

**Choice:** The workflow is our own code. The `Application` aggregate owns its status, and every change goes
through one method, `Apply(...)`, which checks it against an explicit transition table.

**Why**
- The workflow is linear, has 8 states, and its checks all run inside one worker. Temporal, Elsa or a MassTransit
  saga would add infrastructure and indirection without giving us anything we'd actually use.
- **One set of rules for API and worker:** the manual approve/reject endpoints use the same aggregate methods as
  the worker. An invalid transition returns `409` right away, inside the HTTP request (FR-6.2).
- **Invalid transitions are blocked in one place** (RL-4): the transition table, enforced by the aggregate.
- The `Version` concurrency token (D-4) is incremented inside `Apply(...)`, so two writers racing on the
  same application can't both win.
- Each transition also adds an `ApplicationHistory` entry in the same unit of work, so the audit trail (FR-7)
  can't miss a transition.
- Pure domain logic, so it can be unit-tested with no bus, database or host.

**Trade-off:** we write the retry and resume logic ourselves rather than getting it from a workflow engine.
This is acceptable here: the queue (D-3) provides at-least-once redelivery, and because the current state is
saved in the database, a re-run starts from where the previous attempt stopped.

---

### D-3 — Queue: MassTransit v8 + RabbitMQ

**Choice:** RabbitMQ as the broker, with MassTransit **v8** (Apache-2.0) on top. MassTransit's EF Core
**transactional outbox** (API side) is stored in our PostgreSQL database. The worker deliberately runs **without** the
consumer inbox (see "Why no consumer inbox" below).

**Why**
- It matches the brief's wording and its Compose minimum (`queue` service), and the review questions are about
  consumers, duplicates and dead-lettering.
- Most of the reliability features come ready-made, so we explain and configure them rather than write them:
  outbox, inbox, retry and error queues.
- RabbitMQ's management UI lets reviewers see the queues, the messages and the `_error` queue.

**Message contract**
- `ApplicationSubmitted { ApplicationId }` is an event published by the API. It carries only the ID; the worker
  loads everything else from the database.

**How it fits together**

| Concern | Mechanism | Answers |
|---|---|---|
| Save + publish atomically | **Bus outbox**: `AddEntityFrameworkOutbox<…>(o => { o.UsePostgres(); o.UseBusOutbox(); })`. `Publish` writes to the outbox tables inside the same `SaveChanges` as the application, its photo and the first history entry. A background delivery service then forwards it to RabbitMQ. | RL-2 |
| Duplicate delivery | The consumer is **idempotent by state**: if the application is already past the step, it does nothing. **And** the `Version` concurrency token (D-4) ensures that if two workers race, only one write succeeds. | RL-1 |
| Worker crash mid-processing | RabbitMQ only removes a message after the worker acknowledges it, so an unacknowledged message is delivered again. Each workflow step commits its transition (and history entry) on its own, so a re-run picks up from the saved state instead of starting over. | RL-3 |
| Transient errors | `UseMessageRetry(r => r.Exponential(...))` | RL-6 |
| Permanent errors | After retries run out, MassTransit moves the message to `application-submitted_error` (the dead-letter queue) and publishes `Fault<ApplicationSubmitted>`. A fault consumer moves the application to `Failed` and records the reason in the history. `ApplicationNotFoundException` skips the retries, since retrying can't make the application appear. | RL-5, RL-6 |

**Why no consumer inbox (decided during the Infrastructure step)**
- MassTransit's EF consumer inbox wraps each message in **one transaction**. That would hold back our per-step
  commits until the whole message finished, so `GET /{id}` would never show the intermediate statuses, and a
  crash would re-run every step.
- The inbox adds nothing here: the worker publishes nothing (so there's no consumer outbox to protect), and
  duplicates are already harmless through the status checks plus the `Version` token.
- The API keeps the **bus outbox**, so saving and publishing stay atomic (RL-2).

**Docker**
- Official `rabbitmq` image with the management plugin, pinned to a specific tag. Management UI at `:15672`.
- Compose `healthcheck` runs `rabbitmq-diagnostics -q check_port_connectivity`; the API and worker wait for it to be healthy.
- ⚠️ On this Docker Desktop setup, RabbitMQ (Alpine and Debian images alike) crashed on startup with
  `.erlang.cookie: eacces`. Passing the cookie through `RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS` /
  `RABBITMQ_CTL_ERL_ARGS` (`-setcookie …`) fixed it; carry that into Compose.

**Trade-offs**
- **Licence:** MassTransit v9 is commercial; v8 is open-source but its support window is closing. We pin the
  latest v8.x and note it in the README. The outbox, retry and consumer patterns carry over to v9 or to
  Wolverine.
- **One more container** than a PostgreSQL-backed queue (Hangfire was considered as one). In exchange we get a
  ready-made outbox, real broker behaviour and alignment with the brief.

---

### D-5 — Simulated photo quality score: deterministic, from the photo bytes

**Choice:** The score (0–100) is computed from a hash of the photo's bytes, behind an `IPhotoQualityScorer`
interface.

**Layering:** the interface is a *port* to an external capability (a stand-in for a biometric/quality service),
so it lives in the **Application** layer, with the implementation in Infrastructure. The use case gets the score
and passes the **value** to `Application.CheckPhoto(photo, qualityScore, …)`, so the Domain needs no interface at
all.

**Why**
- **Repeatable:** the same photo always gets the same score, so outcomes can be reproduced in tests, in
  reviewer demos and when re-running a redelivered message. A random score could send the same application to
  a different outcome on a retry, which would break our idempotency argument (RL-1).
- **Looks random enough:** different photos spread across the range, so all three outcomes (fail / manual
  review / approve) happen naturally.
- **Swappable:** a real biometric service would replace the scorer implementation without touching the
  workflow. Tests use a fake scorer to force any score.

**How the score is used:** two configurable thresholds (D-6):
- below the **minimum quality score** → photo check fails → `Failed` (FR-5.2.4);
- below the **manual-review threshold** → risk flag → `PendingManualReview` (FR-5.3.4).

**Trade-off:** a reviewer can't choose a photo to get a particular score. We address this by bundling sample
photos in [`samples/`](samples/README.md): one per outcome (approve, review, low quality, too small,
unsupported type), each failing for exactly one reason. `samples/generate.cs` draws them and varies an
invisible PNG comment until the score lands well inside the wanted range, so they can be regenerated if the
thresholds change. The integration tests (D-16) submit them, so the documented outcomes are checked.

---

### D-6 — Watchlist & thresholds: validated options from configuration

**Choice:** One `appsettings.json` section bound to a strongly-typed options class, checked at startup, and
overridable by environment variables in Compose.

```jsonc
"WorkflowRules": {
  "MinimumAge": 18,                  // FR-5.1.4 → Failed
  "ManualReviewBelowAge": 21,        // FR-5.3.1 → PendingManualReview
  "AutoApprovedCategories": [ "B" ], // FR-5.3.2
  "TimeZoneId": "Europe/Athens",     // age rules use the submission's local date here
  "Photo": {
    "AllowedFormats": [ "Jpeg", "Png" ],           // FR-5.2.2 (detected from magic bytes)
    "MinFileSizeBytes": 10240,                     // FR-5.2.3
    "MinQualityScore": 30,                         // FR-5.2.4 → Failed
    "ManualReviewQualityThreshold": 60             // FR-5.3.4 → PendingManualReview
  }
},
"Watchlist": {
  "NationalIds": [ "XX000000" ]      // FR-5.3.3, read by the config-backed IWatchlist (D-11)
}
```

- `WorkflowRules` binds to the Domain record of the same name; the default policies (D-10) receive it.
- The watchlist is **reference data, not a threshold**, so it has its own section behind the `IWatchlist`
  port (D-11).
- **Business time zone:** age rules (18+ / under 21) use the submission's **local calendar date** in
  `TimeZoneId`. Otherwise a submission at 01:00 Athens time on an 18th birthday (22:00 UTC the day before) would
  fail. The policies receive the submission timestamp and convert it themselves. The zone is checked at startup.
  ⚠️ The containers need time-zone data: Debian-based .NET images include it, Alpine images need `tzdata`.

**Why**
- The brief only asks for the watchlist and threshold to be *configurable*. Standard .NET configuration does
  that with no extra infrastructure.
- `ValidateDataAnnotations()` plus a custom rule, run at startup (`ValidateOnStart()`), stops a misconfigured
  worker from starting. One important rule: `ManualReviewQualityThreshold` must be above `MinQualityScore`.
- Environment-variable overrides (`WorkflowRules__Photo__MinQualityScore=…`) let a reviewer change the rules
  in `docker-compose.yml` without rebuilding.
- The values above are illustrative; the real defaults get decided during implementation.

**Trade-off:** a watchlist in a config file is fine for a sample. In production it would be a database table or
an external screening service, and changing it shouldn't need a redeploy.

---

### D-7 — Error contract: ProblemDetails

**Choice:** All errors use the standard RFC 9457 `ProblemDetails` body (ASP.NET Core `AddProblemDetails()` plus
an `IExceptionHandler` that turns domain exceptions into responses).

| Situation | Status | Source |
|---|---|---|
| Approve/reject when not in `PendingManualReview` | `409 Conflict` | Domain `InvalidTransitionException` |
| Application was changed by someone else at the same moment | `409 Conflict` | `ConcurrencyConflictException`, from the history key or EF's `DbUpdateConcurrencyException` (D-4) |
| Unknown `applicationId` | `404 Not Found` | |
| Malformed request (e.g. not multipart, unparseable date) | `400 Bad Request` | Model binding |
| Upload over the hard size limit | `413 Payload Too Large` | `[RequestSizeLimit]` |

- The `409` responses add extension fields (`currentStatus`, `attemptedAction`) so the client can see *why*
  the transition was refused.
- Business-rule failures (under 18, invalid email, bad photo…) are **not** HTTP errors. They become a `Failed`
  status in the workflow (see D-1, API vs workflow).

**Why:** it's the built-in .NET standard, Swagger documents it, and every endpoint returns errors in the same
shape (FR-6.2, "proper error").

---

### D-8 — Reliability approach

Consolidated answers to the brief's questions. The mechanisms themselves are described in D-2, D-3 and D-4.

| ID | Question | Answer |
|---|---|---|
| RL-1 | Same message processed twice? | Two layers: the consumer is **idempotent by state** (does nothing if the application is already past the step); and the **`Version` concurrency token** means only one of two racing writers can save. |
| RL-2 | API saves but fails to publish? | Can't happen as a split: the **transactional outbox** writes the message in the same transaction as the application. If the broker is down, the outbox delivery service keeps retrying until it gets through. |
| RL-3 | Worker crashes mid-processing? | The message wasn't acknowledged, so RabbitMQ **redelivers** it. Each step commits its own transition, so processing **resumes from the saved state**. The deterministic score (D-5) means a re-run reaches the same outcome. |
| RL-4 | How are invalid transitions prevented? | The aggregate's **transition table** is the only way to change status (D-2); the concurrency token stops two writers from both succeeding; the API turns refusals into `409` (D-7). |
| RL-5 | How are failures recorded? | Business failures: a `→ Failed` history entry with the reason in `details`. Technical failures that run out of retries: the **fault consumer** moves the application to `Failed` with the error summary, and the message stays in the `_error` queue to investigate. Everything is also in the **structured logs**, tagged with `ApplicationId`. |
| RL-6 | Retries / dead-letter handling? | `UseMessageRetry` with **exponential backoff** for transient errors, then MassTransit moves the message to `<queue>_error` (dead-letter) and publishes `Fault<T>`. The messages can be inspected in the RabbitMQ UI. Moving them back needs the shovel plugin (not enabled here) or a replay tool; see RL-7. |
| RL-7 | Improve before production? | See list below. |

**RL-7: production improvements (candidates for the README)**
- **Photo storage:** move to blob storage (S3 / Azure Blob) and keep only a reference in the DB. Upload first,
  commit second, clean up orphaned blobs.
- **Security:** authentication and role-based authorisation on the manual review endpoints; record the
  reviewer's identity in the history.
- **Personal data:** encrypt the national ID and photo at rest; retention and deletion policies (GDPR);
  remove personal data from logs.
- **Idempotency keys** (D-17): scope them per client once there is authentication, and let them expire,
  e.g. a separate key store with a TTL instead of a column kept forever.
- **Delayed redelivery** (RabbitMQ delayed-exchange plugin) for longer outages, on top of the immediate retries.
- **Stuck-application monitor:** find applications sitting in an intermediate status too long and alert or
  requeue them.
- **Dead-letter tooling:** alerts on `_error` queue depth and a supported way to replay messages.
- **Observability** (D-18 covers the local picture): a durable telemetry backend instead of the in-memory dashboard;
  alerts on faults, `_error` queue depth and processing latency; sampling once traffic is real.
- **Operations:** run database migrations as a separate deployment step rather than at app startup; clean up
  old outbox/inbox rows; health checks feeding the orchestrator; scale workers horizontally.
- **Watchlist:** move it to a database table or external screening service (D-6).
- **Licence:** plan the move off MassTransit v8 as its support ends (to v9 commercial, or Wolverine).

---

### D-9 — AI usage disclosure

**Choice:** A dedicated **"AI usage"** section in the README, updated as we go rather than reconstructed at the
end.

**Contents**
- The tools used (Claude Code).
- What was AI-generated or AI-assisted: requirements breakdown, decision log, scaffolding, boilerplate, tests,
  docs.
- What was decided or reviewed by us: architecture and every decision in this log, plus review of all
  generated code.
- Anything notable an AI suggestion got wrong and we corrected.

**Why:** the brief explicitly allows AI and lists "what parts were generated or assisted by AI" as a review
question. Being specific and honest is worth more than a vague disclaimer.

---

### D-10 — Business rules: one policy per step, in the Domain

**Choice:** `IDataValidationPolicy`, `IPhotoCheckPolicy` and `IRiskAssessmentPolicy` are Domain interfaces,
with default implementations configured by `WorkflowRules`. The aggregate receives the policy, e.g.
`application.ValidateData(policy, now)`, evaluates it and applies the resulting transition itself.

**Why**
- The rules are business decisions, so they stay in the Domain. Moving them to the Application layer would leave
  the aggregate as a bag of state that services push around (an anemic domain model).
- Interfaces plus DI make them replaceable or extendable without touching the aggregate. A tested custom policy
  proves this.
- If a rule set grows, any single policy can later become a class per rule without changing its interface.

**What stays outside the Domain:** facts from the outside world. The photo quality score (`IPhotoQualityScorer`,
D-5) and watchlist membership (`IWatchlist`, D-11) are Application-layer ports; the use case gathers the facts
and passes plain values in.

**`now` parameters:** the aggregate's methods take the current time so that history timestamps and `UpdatedAt`
are deterministic and testable. The use case supplies it from an injected `TimeProvider`; the domain never reads
the system clock. All timestamps are converted to UTC (required by Npgsql's `timestamptz`).

---

### D-11 — Watchlist: Application-layer port

**Choice:** `IWatchlist.IsWatchlistedAsync(nationalId)` in the Application layer, with a config-backed
implementation for now. The use case looks the ID up and calls `application.AssessRisk(isWatchlisted, policy, now)`.

**Why:** the *rule* ("watchlisted → manual review") is domain logic; the *list* is reference data that in
production would come from a screening service. As a port, it can be swapped without touching the Domain.

---

### D-12 — Unit test stack

**Choice:** xUnit v3 with Shouldly for assertions, run on the Microsoft Testing Platform.

- FluentAssertions v8+ needs a commercial licence, so it's avoided; Shouldly is BSD-licensed.
- From the .NET 10 SDK, `dotnet test` runs xUnit v3 through the Microsoft Testing Platform. The root
  `global.json` opts in (`"test": { "runner": "Microsoft.Testing.Platform" }`) and pins the SDK to 10.0.x.
- No VSTest packages (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) are needed.

---

### D-13 — Worker host

**Choice:** `App/Hosts/Scytales.Worker` is a web host whose only endpoints are health checks. It wires
`AddApplication()`, `AddInfrastructure()` and `AddMessaging(Consumer)`.

- **`/health/live`** checks no dependencies, so a broker outage doesn't get the process restarted.
  **`/health/ready`** checks Postgres and the MassTransit bus and returns `503` unless both are fully healthy
  (ASP.NET's default would return `200` for *Degraded*). Verified live: broker down → `503`; broker back → `200`
  after about 14s, with MassTransit reconnecting on its own.
- **Migrations are the API's job**, so two processes never race over the schema. In Compose the worker waits
  for the API to be healthy.
- **Structured logging:** JSON console logs (UTC timestamps, every message template field as a property) by
  default, single-line text in Development. EF Core's command/update loggers are set to `Critical`: expected
  rejected saves (concurrency conflicts) would otherwise log as errors, and real failures still surface through
  the exceptions MassTransit and the handlers log.
- Configuration lives in `appsettings.json` (`WorkflowRules`, `Watchlist`, `Messaging`). Connection strings
  are empty there and supplied by Compose; `appsettings.Development.json` points at `localhost` for running
  outside Docker.

---

### D-14 — API host

**Choice:** `App/Hosts/Scytales.Api`, with Minimal API endpoints under `/api/applications` that only translate
HTTP to use cases.

| Endpoint | Success | Errors |
|---|---|---|
| `POST /api/applications` (multipart: 8 fields + optional `photo`) | `202 Accepted` + `Location: /api/applications/{id}`, body `{ applicationId, status }` | `400` malformed, `413` over `Api:MaxRequestBodyBytes` (5 MB) |
| `GET /api/applications/{id}` | `200 { applicationId, status, createdAt, updatedAt }` | `404` |
| `GET /api/applications/{id}/history` | `200 [ { timestamp, fromStatus, toStatus, event, details } ]` | `404` |
| `POST /api/applications/{id}/approve` | `200` status | `404`, `409` (+ `currentStatus`, `attemptedAction`) |
| `POST /api/applications/{id}/reject` (optional `{ "reason": … }`) | `200` status | `404`, `409` |

- **202, not 201:** the application is stored, but its outcome comes later; `Location` points at the status to
  poll.
- **Errors:** one `IExceptionHandler` maps exceptions to RFC 9457 ProblemDetails; `UseStatusCodePages` gives
  empty framework errors (binding failures, 413) the same shape. Only unexpected exceptions are logged as
  errors, and their 500 reveals nothing.
- **Antiforgery disabled** on submit: a token-less API without cookie auth.
- **OpenAPI:** the built-in `Microsoft.AspNetCore.OpenApi` document at `/openapi/v1.json`, with Swagger UI at
  `/swagger` (and `/` redirects there). Transformers make `photo` a plain binary field, so Swagger UI shows a
  file picker, and drop the url-encoded variant that can't carry a file. Enums are strings everywhere.
- **Migrations on startup** (`Database:MigrateOnStartup`, default `true`). The API owns the schema (D-13).
- Health endpoints (`/health/live`, `/health/ready`) are shared with the worker through
  `App/Hosts/Scytales.Hosting`. Readiness is decided only by checks tagged `ready`; checks tagged
  `informational` are listed with their own status (`affectsReadiness: false`) but don't count. The API's bus
  check is informational: thanks to the outbox the API keeps accepting submissions while the broker is down, and
  they're delivered once it's back (verified: broker stopped → API `200`, submit `202`, worker `503`; broker
  started → the submission was processed). Caveat: MassTransit reports a publish-only bus as `Healthy` even
  when the broker is down, so that line doesn't reveal an outage; a dedicated RabbitMQ connection check would.
- **JSON follows the brief's examples:** timestamps in UTC with a `Z` suffix (`UtcDateTimeOffsetConverter`),
  and `details` left out of history entries when empty, while `fromStatus: null` stays (it means "creation").
- **Microsecond timestamps:** Infrastructure replaces the application clock with `StoragePrecisionTimeProvider`,
  which truncates to PostgreSQL's microsecond precision. So an approve/reject response shows exactly the
  `updatedAt` a later read returns. Storage precision is an Infrastructure fact, so the Domain and the use cases
  are unaware of it.

---

### D-15 — Local runtime: Docker Compose

**Choice:** `Docker/docker-compose.yml` (project name `scytales`), included by a root `compose.yaml`, so
`docker compose up -d` works from the repository root. Setup instructions for reviewers are in
[`INSTRUCTIONS.md`](INSTRUCTIONS.md).

- **Services use the brief's names:** `api`, `worker`, `database` (postgres:17-alpine), `queue`
  (rabbitmq:4-management-alpine). Database and queue keep their data in named volumes.
- **One Dockerfile, two targets** (`Docker/Dockerfile`, `--target api|worker`): the hosts share every project
  but themselves, so they share the restore and build layers. The build context is the repository root, and
  `.dockerignore` limits it to `global.json` and `App/`.
- **Runtime image:** the standard .NET 10 ASP.NET image (Ubuntu 24.04), which includes tzdata for
  `WorkflowRules:TimeZoneId`, plus `curl` for the health check. The hosts run as the image's non-root `app` user.
- **Health-ordered startup:**
  - `database` is healthy on `pg_isready -h 127.0.0.1`. It checks over TCP on purpose: on a first start the
    image initialises the database on a temporary, socket-only server, which a socket check would report as
    ready.
  - `queue` is healthy on `rabbitmq-diagnostics check_port_connectivity` (the AMQP listener accepts connections).
  - `api` starts after both and is healthy on `/health/ready`. Since it migrates before it starts listening,
    "healthy" also means "schema migrated".
  - `worker` starts after a healthy `api`, `database` and `queue`.
  - Both hosts have `restart: unless-stopped`.
- **Defaults everywhere:** ports and credentials are `${VAR:-default}`, overridable from the environment or a
  `.env` file. RabbitMQ gets its own user (`guest` may only connect from localhost), and the Erlang cookie is
  passed as an argument (the `.erlang.cookie: eacces` workaround from D-3).
- **Local development:** `docker compose up -d database queue` provides the infrastructure, and the hosts'
  `appsettings.Development.json` already point at it.
- Verified from empty volumes: `up -d` returns after ~21s with `worker` started; all four are healthy at ~29s;
  data survives `down`/`up`.

---

### D-16 — Integration tests: Testcontainers, real hosts

**Choice:** `Tests/Scytales.Integration.Tests` runs the whole system per test run. PostgreSQL and RabbitMQ run in
Testcontainers, and the real API and Worker hosts run in-process through `WebApplicationFactory`. As in
Compose, the hosts share nothing but the database and the broker.

- **One fixture per assembly** (xUnit v3 `AssemblyFixture`). Every test submits its own applications, so the
  tests don't depend on each other or on their order.
- **Black-box client:** the tests use HTTP and JSON only, with their own response records, so they check the
  contract a client sees, not the application's types.
- **API on real Kestrel** (`UseKestrel`, new in .NET 10): the in-memory TestServer doesn't enforce request size
  limits, so it can't show the `413`. The oversized upload sends `Expect: 100-continue`, so that the server can
  refuse it before the body is sent, as a well-behaved client would.
- **Both hosts reference the test project with aliases** (`extern alias api` / `worker`): each declares a
  top-level `Program` in the global namespace.
- **One exception to black-box:** to show RL-1, the fixture publishes `ApplicationSubmitted` again on the
  worker's bus and waits, through a MassTransit consume observer, until the worker has consumed it, instead
  of sleeping and hoping.
- **Covered (37 tests):**
  - *Workflow:* submit returns `202` with `Location`; automatic approval with the full audit trail; the brief's
    JSON shape (`Z` timestamps, `fromStatus: null`, no empty `details`); each sample photo's documented
    outcome (D-5); `413`; `404`.
  - *Business rules (FR-5.1, FR-5.3):* invalid email, missing national ID, under 18 → `Failed`; aged 19,
    category `A`, watchlist → review.
  - *Manual review (FR-6):* approve and reject from review (with and without a reason); approve or reject on an
    approved or failed application → `409` with the history unchanged (one case per action: which statuses
    refuse is the Domain tests' job); unknown application → `404`.
  - *Reliability (D-8):* the same message consumed again after each outcome changes nothing (RL-1); a
    concurrent approve and reject → exactly one `200` and one `409`, one manual history entry (RL-4).
  - *Idempotency (D-17):* repeat → same application with `Idempotent-Replayed`; five concurrent submissions
    with one key → one application, processed once (the losers go through the unique-index path, checked
    by temporarily instrumenting the handler); different body → `422`; invalid key → `400`.
  - *Health (BN-6):* both hosts live and ready.
- **Not covered here:** broker outage at submit (RL-2) and retries running out (RL-5/RL-6). Each needs its own
  system instance (paused broker, failing scorer), and was left out to keep the suite fast.
- **Requires Docker.** The whole run, containers included, takes ~20s.

---

### D-17 — Idempotent submission: `Idempotency-Key` on the aggregate (BN-2)

**Choice:** `POST /api/applications` takes an optional `Idempotency-Key` header (the name from the IETF
draft, as used by Stripe and others). The key is stored **on the `Application` aggregate**, together with a
SHA-256 hash of the request, in the `applications` table under a **unique index**.

- **Why on the aggregate:** the key commits in the **same transaction** as the application, its photo, its
  first history entry and the outbox message, so there is no separate key table to keep consistent. The
  Domain stays dependency-free: to it the key is just an `IdempotencyKey` value object (key + request hash).
- **Flow:** with a key, the handler first looks it up (a scalar projection, never loading the photo).
  - *Found, same request hash* → nothing is stored or published; `202` with the **same** application id and
    `Location`, its **current** status, and an `Idempotent-Replayed: true` header.
  - *Found, different hash* → `422` ProblemDetails with the key in an `idempotencyKey` extension. This catches
    client bugs that would otherwise silently return the wrong application.
  - *Not found* → a normal submit.
- **Concurrent requests with one key:** both can miss the lookup; the unique index lets only one insert
  commit. The unit of work recognises that index by name and raises `DuplicateIdempotencyKeyException`; the
  handler then loads the winner and answers as a replay. The loser's outbox message was in its rolled-back
  transaction, so **no duplicate message** is ever sent.
- **Request hash:** SHA-256 over every form field and the photo (file name, content type, bytes),
  length-prefixed so field boundaries and null vs empty can't collide.
- **Validation at the edge:** a blank key or one over 255 characters is a `400`. Without the header, nothing
  changes (PostgreSQL unique indexes allow any number of NULLs).
- **Shortcuts (production would differ, see RL-7):** keys are global (there is no client identity yet, BN-9)
  and never expire.

---

### D-18 — Observability: OpenTelemetry to the Aspire Dashboard (BN-5)

**Choice:** Both hosts emit **traces, metrics and logs** with OpenTelemetry and export them over **OTLP** to the
standalone **.NET Aspire Dashboard**, a fifth Compose service (`dashboard`, http://localhost:18888), started with
everything else.

- **Why it earns its place:** the design is asynchronous (outbox → broker → worker), which is hard to see from
  logs alone. One trace now shows a submission end to end, across both processes: the HTTP request, its
  transaction, the outbox delivery, the RabbitMQ send, the worker's consume, every workflow step and every
  save. MassTransit carries the trace context through the outbox and the message headers by itself.
- **One place:** `AddObservability(serviceName)` in `Scytales.Hosting`, used by both hosts (DRY, like the
  health endpoints).
  - *Traces:* ASP.NET Core (health probes left out), MassTransit, Npgsql (so EF Core's SQL too), and the
    workflow steps.
  - *Metrics:* ASP.NET Core, .NET runtime, MassTransit, Npgsql.
  - *Logs:* the OpenTelemetry logging provider, next to the JSON console logs (TR-8), so each log line is linked
    to its trace.
- **Workflow step spans:** `WorkflowTelemetry` in the Application layer, a BCL `ActivitySource`
  (`Scytales.Workflow`), so the layer takes **no OpenTelemetry dependency**. One span per automated step,
  including its save, tagged with `application.id`, `workflow.from_status` / `workflow.event` /
  `workflow.to_status`, and `workflow.details` (the rule that fired). Spans only exist when a listener
  subscribes, so there is no cost without OpenTelemetry. Covered by unit tests with an `ActivityListener`.
- **Noise control, found by looking at the real dashboard:** at first every outbox poll and health-check query
  became a trace of its own and buried the useful ones. Npgsql now only traces commands that run inside another
  span, and doesn't trace pooled connection opens. Each submission is exactly one trace.
- **Export only when configured:** the OTLP exporter is switched on only when `OTEL_EXPORTER_OTLP_ENDPOINT` is
  set (Compose sets it; `appsettings.Development.json` points at the published `localhost:4317` for IDE runs).
  Tests and a plain `dotnet run` export nothing and need no collector.
- **Why the Aspire Dashboard:** one container for all three signals with a good trace view, no configuration.
  Jaeger shows traces only; a Grafana/Tempo/Loki/Prometheus stack is several containers for a local demo.
  OTLP keeps that choice open: pointing the endpoint elsewhere is a configuration change.
- **Shortcuts:** the dashboard keeps telemetry in memory and runs without authentication. It's a local
  development tool, not a production backend (RL-7). Everything is sampled; production would sample.

---

### D-19 — Infrastructure tests: the persistence adapter against real PostgreSQL

**Choice:** `Tests/Scytales.Infrastructure.Tests` runs PostgreSQL in Testcontainers, applies the real migrations,
and resolves services through the real `AddInfrastructure()` by their public interfaces. No broker, no hosts, and
no `InternalsVisibleTo`.

- **Why a separate project:** some Infrastructure behaviour can't be shown reliably through the integration
  tests. Two reviewers truly overlapping (both loading the same version) almost never happens over HTTP: the
  loser usually fails the transition check first. Here, two scopes load the same version deterministically.
- **Covered (14 tests):**
  - *Concurrency (D-4):* same-version race → `ConcurrencyConflictException` via the history key; a row changed
    without history (raw SQL) → `ConcurrencyConflictException` via the `Version` check. Both leave the data as
    the first writer saved it.
  - *Idempotency (D-17):* a duplicate key → `DuplicateIdempotencyKeyException`, with nothing stored; any number
    of applications without a key; the lookup returns the current status and the hash.
  - *Mapping:* the aggregate saves and reloads with its applicant, photo bytes and key intact.
  - *Services:* the scorer is deterministic, stays within 0–100 and produces all three outcome bands; the
    watchlist trims, ignores case and ignores blanks; the clock has microsecond precision.
- **Mutation-checked:** disabling each of the unit of work's three exception translations in turn fails exactly
  the test that covers it. That's also how we learned the history key, not the `Version` check, is what stops
  a same-version race (D-4).
- **Still not covered:** the API's `ConcurrencyConflictException` → `409` line, end to end. That would need an
  exact overlap over HTTP.
