# Driving Licence Onboarding Workflow

A .NET 10 backend for the Scytales *Senior .NET Developer* assignment. A citizen submits an application with a face
photo; an asynchronous workflow validates it, runs a simulated photo check and risk assessment, and ends in
**Approved**, **Rejected**, **Failed** or **PendingManualReview**.

| Document | What's in it |
|---|---|
| **This README** | How to run it, the architecture, the key decisions, reliability, assumptions, AI usage |
| [`INSTRUCTIONS.md`](INSTRUCTIONS.md) | Full run guide: walkthrough, configuration, observability, troubleshooting |
| [`implementation-steps.md`](implementation-steps.md) | Decision log D-1 … D-18: every choice with its alternatives and trade-offs |
| [`requirements.md`](requirements.md) | The brief restated as numbered requirements (FR / RL / BN / DL) |

---

## Quick start

Requires Docker with Compose v2.20+. From the repository root:

```bash
docker compose up
```

| Service | URL |
|---|---|
| API + Swagger UI | http://localhost:5080/swagger |
| Telemetry dashboard (traces, metrics, logs) | http://localhost:18888 |
| RabbitMQ management (`scytales` / `scytales`) | http://localhost:15672 |
| Worker health | http://localhost:5081/health/ready |

Submit an application. The request is **`multipart/form-data`**, not the JSON body shown in the brief (see D-1
below):

```bash
curl -X POST http://localhost:5080/api/applications \
  -F firstName=John -F lastName=Doe -F dateOfBirth=1990-05-20 -F nationalId=AB123456 \
  -F email=john.doe@example.com -F phoneNumber=+306900000000 -F "address=Example Street 10" \
  -F licenceCategory=B -F "photo=@samples/approve.png;type=image/png"
```

Then `GET /api/applications/{id}` and `/history`. [`samples/`](samples/README.md) has one photo per outcome, and
national ID `XX000000` is on the watchlist. On Windows, see the curl notes in
[INSTRUCTIONS §4.1](INSTRUCTIONS.md#41-submit-an-application).

---

## Architecture

```
Client ─▶ api ──┬─▶ PostgreSQL  application + photo + history + outbox message, one transaction
                └─▶ outbox ─▶ RabbitMQ ─▶ worker ─▶ one workflow step at a time, each saved with its history entry
```

Clean architecture, four layers, no MediatR:

| Project | Responsibility |
|---|---|
| `Scytales.Domain` | The `Application` aggregate, its transition table, the business-rule policies. No dependencies. |
| `Scytales.Application` | Use cases (submit, process, review, fail), ports (repository, unit of work, publisher, photo scorer, watchlist), read queries. |
| `Scytales.Infrastructure` | EF Core + PostgreSQL, MassTransit + RabbitMQ (outbox, consumer, retries, fault handling), the simulated scorer and watchlist. |
| `Hosts/Scytales.Api` | Minimal APIs, ProblemDetails, OpenAPI + Swagger UI. Applies the migrations. |
| `Hosts/Scytales.Worker` | Consumes `ApplicationSubmitted`; serves only health endpoints. |
| `Hosts/Scytales.Hosting` | Shared by both hosts: health endpoints and OpenTelemetry. |

### Workflow

```
Submitted ─▶ ValidatingData ─▶ CheckingPhoto ─▶ RiskAssessment ─▶ Approved
                  │                  │                 └─────────▶ PendingManualReview ─▶ Approved | Rejected
                  └──────────────────┴─▶ Failed       (any automated state ─▶ Failed on a technical failure)
```

- The aggregate is the **only** way to change status. Every change goes through one method that checks the
  transition table, bumps a `Version` concurrency token and appends the history entry.
- Each step has its own policy (data validation, photo check, risk assessment) in the Domain. The thresholds,
  allowed categories and watchlist come from validated configuration (`WorkflowRules`, `Watchlist`).
- Manual approve and reject use the same aggregate, so an invalid transition is a `409` with `currentStatus` and
  `attemptedAction` in the ProblemDetails.

---

## Key decisions

Each has a full entry in [`implementation-steps.md`](implementation-steps.md).

| | Decision | Why |
|---|---|---|
| D-1 | **Photo:** one `multipart/form-data` request, stored in its own table in the same transaction | One call, a file picker in Swagger, no orphaned uploads. Base64 adds ~33%; a separate endpoint leaves applications without photos. The API only rejects malformed or oversized (`413`) requests: missing or bad data must reach the workflow, so the failure is recorded in the history. |
| D-2 | **Workflow:** custom state machine on the aggregate | 8 states, linear, all automated steps in one worker. Temporal, Elsa or a saga would add infrastructure and give us nothing we'd use. Pure domain code, easy to test. |
| D-3 | **Messaging:** MassTransit v8 + RabbitMQ, EF Core transactional outbox | Outbox, retries, dead-letter queue and fault events come ready-made, and the broker UI shows the queues. No consumer inbox: it would wrap all the steps in one transaction and hide the intermediate statuses. |
| D-4 | **PostgreSQL** with an explicit `bigint` `Version` token | Suggested by the brief, small image. The token makes racing writers fail instead of overwriting each other. |
| D-5 | **Photo quality score:** deterministic, from a SHA-256 of the bytes | Same photo, same score, so a redelivered message reaches the same outcome. Behind a port, so a real service can replace it. |
| D-7 | **Errors:** RFC 9457 ProblemDetails | `400` / `404` / `409` / `413` / `422` from one exception handler. |
| D-17 | **Idempotent submit:** optional `Idempotency-Key` header | Stored on the aggregate with a request hash under a unique index. A retry returns the same application (`Idempotent-Replayed: true`); the key reused for different data gets `422`. |
| D-18 | **Observability:** OpenTelemetry → .NET Aspire Dashboard | One trace per submission across both hosts, down to each workflow step and its SQL. Only the dashboard's container image is used: no Aspire packages, no AppHost. |

---

## Reliability

| Question | Answer |
|---|---|
| **RL-1** Same message twice? | Nothing happens. The consumer is idempotent by state (each step only runs from its own status), and the `Version` token lets only one of two racing writers save. Integration-tested by publishing the message again after each outcome. |
| **RL-2** Saved but not published? | Can't happen. The transactional outbox commits the message with the application, and a background service delivers it, retrying until the broker is back. |
| **RL-3** Worker crashes mid-way? | The message isn't acknowledged, so RabbitMQ redelivers it. Each step is committed on its own, so processing resumes from the last saved status. |
| **RL-4** Invalid transitions? | The transition table, enforced by the aggregate; the concurrency token for races; `409` at the API. Integration-tested with a simultaneous approve and reject. |
| **RL-5** How are failures recorded? | Business failures: a `→ Failed` history entry whose `details` gives the reason. Technical failures: after the retries, a fault consumer moves the application to `Failed` with the error summary, and the message stays in `application-submitted_error`. Structured JSON logs and traces throughout. |
| **RL-6** Retries / dead letters? | Exponential-backoff retries (`UseMessageRetry`), then MassTransit's `_error` queue (dead-letter queue) plus a `Fault<T>` event. The messages can be inspected in the RabbitMQ UI; replaying them needs the shovel plugin or a replay tool (not set up here). |
| **RL-7** Before production? | See [Production improvements](#production-improvements). |

**Bonus features done:** outbox, idempotency key, exponential retries, dead-letter queue, OpenTelemetry, health
checks, Testcontainers integration tests. **Not done:** workflow definition loaded from JSON (BN-8, skipped on
purpose: a transition table in code is easier to test and review for 8 states), authentication on the review
endpoints (BN-9).

---

## Tests

```bash
dotnet test --project Tests/Scytales.Domain.Tests        # 220 tests, no dependencies
dotnet test --project Tests/Scytales.Application.Tests   # 68 tests, in-memory fakes
dotnet test --project Tests/Scytales.Infrastructure.Tests # 14 tests, needs Docker (~10 s)
dotnet test --project Tests/Scytales.Integration.Tests   # 37 tests, needs Docker (~20 s)
```

- **Domain:** every transition (allowed and refused), every business rule at its boundaries, value objects.
- **Application:** the use cases against in-memory fakes, including the idempotency race and the workflow spans.
- **Infrastructure:** the persistence adapter against PostgreSQL in Testcontainers, with the real migrations.
  Both concurrency guards are tested deterministically. A second reviewer saving the same version is stopped by
  the history key (one entry per version). A row changed without history is stopped by the `Version` check.
  Each becomes `ConcurrencyConflictException` (`409`). Also covered: the idempotency key's unique index and
  lookup, the aggregate's save-and-reload mapping, the scorer's determinism and spread, the watchlist and the
  clock's precision. Removing any of the unit of work's exception translations fails a test (checked by doing
  it).
- **Integration:** PostgreSQL and RabbitMQ in Testcontainers, with the real API (on Kestrel) and Worker hosts.
  They cover every outcome, the brief's JSON shapes, manual review, the RL-1/RL-4 claims above, idempotency and
  health.

xUnit v3 + Shouldly on Microsoft Testing Platform.

---

## Assumptions

- **`CheckingPhoto → Failed`** is inferred. The brief's failure flow only shows `ValidatingData → Failed`, but
  its photo rules say the check "should fail".
- **Technical failures** (retries exhausted) move any automated state to `Failed`. That's in the transition
  table as `ProcessingFailed`.
- **All personal fields are required.** "Required fields are missing" isn't itemised in the brief.
- **Ages are judged on the submission date** in a configured time zone (`Europe/Athens`), so a re-run gives the
  same answer.
- **The photo can be missing from the request**, so that "no photo was provided" can actually happen as a
  workflow failure.
- **Reject's reason is optional**, as in the brief ("may accept a reason").

## Shortcuts and what's missing

- **No authentication** on approve and reject, so the history doesn't record *who* decided (BN-9).
- **Photos are stored in PostgreSQL** (`bytea`). Production would use blob storage and keep a reference.
- **Watchlist in configuration.** Production would use a table or a screening service.
- **Not covered by integration tests:** the broker being down at submit (RL-2) and retries running out
  (RL-5/RL-6). The mechanisms are MassTransit's; testing them needs a separate system instance each.
- **The API's `ConcurrencyConflictException` → `409` mapping isn't tested end to end.** The exception itself is
  (Infrastructure tests), but reaching it through HTTP needs two requests to overlap exactly, which a test can't
  arrange without timing hooks. The mapping is one line in the exception handler, next to the others.
- **The API's health check says the broker is Healthy even when it's down.** That's how MassTransit reports a
  publish-only bus. The outbox makes this harmless for correctness, but a real connection check would be better.
- **Nothing detects applications stuck in an intermediate status** (for example after a crash with no
  redelivery).
- **Idempotency keys are global and never expire.**
- **The telemetry dashboard** keeps data in memory and has no login. It's a local development tool.

## Production improvements

Blob storage for photos · authentication and role-based authorisation, with the reviewer recorded in the
history · encryption at rest for personal data, retention rules, no personal data in logs · delayed redelivery for
long outages · alerts on `_error` queue depth, plus a supported replay tool · a monitor for stuck applications ·
a durable telemetry backend, alerts and sampling · migrations as a deployment step rather than at startup ·
outbox clean-up · idempotency keys scoped per client, with expiry · horizontal scaling of workers · a plan for
MassTransit v8's end of support (v9 is commercial; Wolverine is an alternative).

---

## Time spent

About **6 h 45 min** in total:

| Time | Work |
|---:|---|
| 30 min | Requirements: reading the brief and restating it as numbered requirements |
| 45 min | Approach and stack: PostgreSQL, RabbitMQ + MassTransit, and a workflow on the domain model (the app is small and its invariants are controlled, so no workflow engine) |
| 1 h | Domain module: baseline and refinement |
| 1 h | Application module: baseline and refinement |
| 1 h | Hosts: the worker and API bootstrapping |
| 1 h | Test suites: domain, application, integration |
| 1 h | Refinements: idempotency key (D-17), OpenTelemetry (D-18), test hardening, clean-ups across the modules |
| 30 min | README, instructions and git setup |

---

## AI usage

Built with **Claude Code** (Anthropic's Claude) as a pair programmer, working one decision at a time.

- **AI-generated or assisted:** the requirements breakdown, first drafts of the decision log, most of the code,
  the tests, Docker setup and documentation.
- **Decided and reviewed by me:** the architecture and every entry in the decision log. Each was proposed with
  alternatives, discussed and accepted or changed before any code was written. I also reviewed the generated
  code.
- **How it was kept honest:** every step was verified by running it. That means the full test suite with
  warnings as errors, the real containers with `curl`, and the telemetry dashboard in a browser, not just
  compiling.
- **Things AI got wrong, or that only running the real system showed:**
  - Wrapping each message in MassTransit's consumer inbox would have hidden the per-step progress, so it was
    dropped (D-3).
  - A newer EF Core Design package broke compilation and had to be pinned.
  - RabbitMQ crashed on Docker Desktop because it couldn't read its cookie file, which needed a workaround.
  - A suggested test expected `400` for a reject without a reason, but the brief makes the reason optional.
  - The first OpenTelemetry setup flooded the dashboard with background queries; a span filter fixed it (D-18).
