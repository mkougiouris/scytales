# Driving Licence Onboarding Workflow

A .NET 10 backend for the Scytales *Senior .NET Developer* assignment. A citizen submits an application with a face
photo; an asynchronous workflow validates it, runs a simulated photo check and risk assessment, and ends in
**Approved**, **Rejected**, **Failed** or **PendingManualReview**.

| Document | What's in it |
|---|---|
| **This README** | Running it, architecture, decisions, reliability, what was left out, AI usage |
| [`INSTRUCTIONS.md`](INSTRUCTIONS.md) | Full run guide: walkthrough, configuration, observability, troubleshooting |
| [`docs/implementation-steps.md`](docs/implementation-steps.md) | Decision log D-1 … D-19, each with its alternatives and trade-offs |
| [`docs/requirements.md`](docs/requirements.md) | The brief restated as numbered requirements (FR / RL / BN / DL) |

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

Submit an application. It's **`multipart/form-data`**, not the brief's JSON body (D-1):

```bash
curl -X POST http://localhost:5080/api/applications \
  -F firstName=John -F lastName=Doe -F dateOfBirth=1990-05-20 -F nationalId=AB123456 \
  -F email=john.doe@example.com -F phoneNumber=+306900000000 -F "address=Example Street 10" \
  -F licenceCategory=B -F "photo=@samples/approve.png;type=image/png"
```

Then `GET /api/applications/{id}` and `/history`. [`samples/`](samples/README.md) has one photo per outcome;
national ID `XX000000` is on the watchlist. On Windows, see [INSTRUCTIONS §4.1](INSTRUCTIONS.md#41-submit-an-application).

---

## Architecture

```
Client ─▶ api ──┬─▶ PostgreSQL  application + photo + history + outbox message, one transaction
                └─▶ outbox ─▶ RabbitMQ ─▶ worker ─▶ one workflow step at a time, each saved with its history entry
```

| Project | Responsibility |
|---|---|
| `Scytales.Domain` | The `Application` aggregate, its transition table, the business-rule policies. No dependencies. |
| `Scytales.Application` | Use cases (submit, process, review, fail) and the ports they need. No MediatR. |
| `Scytales.Infrastructure` | EF Core + PostgreSQL, MassTransit + RabbitMQ, the simulated scorer and watchlist. |
| `Hosts/Scytales.Api` · `Worker` · `Hosting` | Minimal APIs + Swagger; the message consumer; shared health checks and OpenTelemetry. |

```
Submitted ─▶ ValidatingData ─▶ CheckingPhoto ─▶ RiskAssessment ─▶ Approved
                  │                  │                 └─────────▶ PendingManualReview ─▶ Approved | Rejected
                  └──────────────────┴─▶ Failed       (any automated state ─▶ Failed on a technical failure)
```

The aggregate is the **only** way to change status. One method checks the transition table, bumps a `Version`
concurrency token and appends the history entry. Each step's rules are a Domain policy, configured through
validated options. Manual approve and reject go through the same aggregate, so an invalid move is a `409`.

---

## Key decisions

Full entries, with alternatives, are in [`docs/implementation-steps.md`](docs/implementation-steps.md).

| | Decision | Why |
|---|---|---|
| D-1 | **Photo:** one multipart request, stored in its own table in the same transaction | One call, a file picker in Swagger, no orphaned uploads. The API only refuses malformed or oversized (`413`) requests, so bad data reaches the workflow and its failure is recorded. |
| D-2 | **Workflow:** a state machine on the aggregate | 8 linear states in one worker. A workflow engine would add infrastructure we wouldn't use. |
| D-3 | **Messaging:** MassTransit v8 + RabbitMQ, EF Core outbox | Outbox, retries, dead-lettering and fault events ready-made. No consumer inbox: it would hide the per-step progress. |
| D-4 | **PostgreSQL** with a `Version` token | Racing writers fail instead of overwriting each other. |
| D-5 | **Photo score** from a SHA-256 of the bytes | Deterministic, so a redelivered message reaches the same outcome. |
| D-7 | **Errors:** RFC 9457 ProblemDetails | `400` / `404` / `409` / `413` / `422` from one exception handler. |
| D-17 | **Idempotent submit:** optional `Idempotency-Key` header | A retry returns the same application; the key reused for different data gets `422`. |
| D-18 | **OpenTelemetry** → .NET Aspire Dashboard | One trace per submission across both hosts, down to each workflow step. The dashboard is just a container: no Aspire packages. |

---

## Reliability

| Question | Answer |
|---|---|
| **RL-1** Same message twice? | Nothing happens: each step only runs from its own status, and the `Version` token lets one of two racing writers save. Integration-tested. |
| **RL-2** Saved but not published? | Can't happen: the outbox commits the message with the application and delivers it once the broker is reachable. |
| **RL-3** Worker crashes mid-way? | The unacknowledged message is redelivered, and processing resumes from the last saved step. |
| **RL-4** Invalid transitions? | The transition table, enforced by the aggregate; the concurrency guards for races; `409` at the API. Integration-tested. |
| **RL-5** Failures recorded? | Business failures: a `→ Failed` history entry with the reason. Technical failures: after the retries, a fault consumer marks the application `Failed`, and the message stays in the `_error` queue. |
| **RL-6** Retries / dead letters? | Exponential backoff, then MassTransit's `_error` queue and a `Fault<T>` event. Messages can be inspected in the RabbitMQ UI; replaying needs the shovel plugin or a tool (not set up). |
| **RL-7** Before production? | See [Production improvements](#production-improvements). |

---

## Tests

```bash
dotnet test --project Tests/Scytales.Domain.Tests          # 220, no dependencies
dotnet test --project Tests/Scytales.Application.Tests     #  68, in-memory fakes
dotnet test --project Tests/Scytales.Infrastructure.Tests  #  14, needs Docker (~10 s)
dotnet test --project Tests/Scytales.Integration.Tests     #  37, needs Docker (~20 s)
```

- **Domain:** every transition, allowed and refused; every business rule at its boundaries.
- **Application:** the use cases against in-memory fakes, the idempotency race included.
- **Infrastructure** (D-19): the persistence adapter against real PostgreSQL. Both concurrency guards and the
  idempotency index are tested deterministically, and each test was checked to fail when its code is removed.
- **Integration:** the real API and Worker with PostgreSQL and RabbitMQ in Testcontainers: every outcome, the
  brief's JSON shapes, manual review, RL-1, RL-4, idempotency and health.

---

## Assumptions

- **`CheckingPhoto → Failed`** is inferred: the brief only shows `ValidatingData → Failed`.
- **Technical failures** move any automated state to `Failed` (`ProcessingFailed` in the transition table).
- **All personal fields are required**; the brief doesn't itemise them.
- **Ages are judged on the submission date** in a configured time zone, so a re-run gives the same answer.
- **The photo may be missing** from the request, so "no photo was provided" can happen as a workflow failure.
- **Reject's reason is optional**, as in the brief.

## Left out

- **Authentication on approve/reject, and the reviewer in the audit trail (BN-9).** The trail records every
  manual decision, but not *who* made it: that needs an identity first.
- **Workflow definition loaded from JSON (BN-8)**, on purpose. For 8 states, a transition table in code is
  easier to test and review.
- **Integration tests for a broker outage at submit (RL-2) and for retries running out (RL-5/RL-6).** Each
  needs its own system instance. The mechanisms are MassTransit's.
- **A monitor for applications stuck mid-workflow**, for example after a crash with no redelivery.
- **A real broker check in the API's health endpoint.** MassTransit reports a publish-only bus as Healthy even
  when the broker is down; the outbox keeps this harmless for correctness.

## Shortcuts

- **Photos are stored in PostgreSQL** (`bytea`), and **the watchlist is configuration**.
- **Idempotency keys are global and never expire.**
- **The API's mapping of a concurrency conflict to `409` isn't tested over HTTP**: the exception itself is
  (Infrastructure tests). The mapping is one line in the exception handler.
- **The telemetry dashboard** keeps data in memory and has no login. It's a local development tool.

## Production improvements

Blob storage for photos · authentication and roles, with the reviewer in the history · encryption at rest and
retention rules for personal data, none of it in logs · delayed redelivery for long outages · alerts on the
`_error` queue, and a replay tool · a monitor for stuck applications · a durable telemetry backend with alerts
and sampling · migrations as a deployment step · outbox clean-up · idempotency keys per client, with expiry ·
more worker instances · a plan for MassTransit v8's end of support (v9 is commercial; Wolverine is an alternative).

---

## Time spent

About **6 h 45 min**:

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

Built with **Claude Code** (Anthropic's Claude) as a pair programmer, one decision at a time.

- **AI-generated or assisted:** the requirements breakdown, first drafts of the decision log, most of the code,
  the tests, the Docker setup and the documentation.
- **Decided and reviewed by me:** the architecture and every decision-log entry. Each was proposed with
  alternatives and accepted or changed before any code was written. I also reviewed the generated code.
- **Verified by running it:** the full test suite with warnings as errors, the real containers with `curl`, and
  the telemetry dashboard in a browser.
- **What AI got wrong, or only the running system showed:**
  - A consumer inbox would have hidden the per-step progress, so it was dropped (D-3).
  - A newer EF Core Design package broke compilation and had to be pinned.
  - RabbitMQ crashed on Docker Desktop over its cookie file, which needed a workaround.
  - A suggested test expected `400` for a reject without a reason, which the brief allows.
  - The first OpenTelemetry setup flooded the dashboard with background queries; a span filter fixed it (D-18).
