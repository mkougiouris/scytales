# Running the system locally

The whole system (API, background worker, PostgreSQL, RabbitMQ and a telemetry dashboard) starts with a single
Docker Compose command. You do not need the .NET SDK, a database or a broker on your machine; the images are built from source
the first time you start the system.

---

## 1. Prerequisites

| Tool | Version | Notes |
|---|---|---|
| Docker Desktop, or Docker Engine with the Compose plugin | Compose **v2.20+** | The root `compose.yaml` uses `include`. Check with `docker compose version`. |
| Free host ports | 5080, 5081, 5432, 5672, 15672, 18888, 4317 | Each one can be changed; see [§6](#6-configuration). |
| `curl` (optional) | any | Only used for the walkthrough in [§4](#4-try-it-out); Swagger UI works just as well. |

---

## 2. Start

From the repository root:

```bash
docker compose up -d
```

- The first run builds the `api` and `worker` images, which takes a few minutes. Later starts take seconds.
- The command returns once the last service, `worker`, has started; it becomes ready a few seconds later.
  `docker compose ps` shows when `api`, `worker`, `database` and `queue` are `(healthy)`. The `dashboard` has
  no health check; it shows as `Up`.
- Optional: `docker compose up -d --wait` blocks until every service is healthy, and fails if one is not.
  Handy in scripts.
- The Compose project is named **`scytales`**: containers are `scytales-api-1`, `scytales-worker-1`, and so on.

Startup order is handled for you:

```
database ──┐
queue ─────┼─▶ api (applies the database migrations) ──▶ worker
dashboard ─┘
```

The `dashboard` only needs to have started, not to be healthy: if it is missing, the hosts keep working and
their telemetry is dropped.

---

## 3. What is running

| Service | Purpose | URL on your machine |
|---|---|---|
| `api` | REST API, Swagger UI | http://localhost:5080/swagger |
| `worker` | Consumes `ApplicationSubmitted` messages and runs the workflow | http://localhost:5081/health/ready |
| `database` | PostgreSQL 17 | `localhost:5432` · database / user / password: `scytales` |
| `queue` | RabbitMQ 4 | `localhost:5672` · management UI http://localhost:15672 · user / password: `scytales` |
| `dashboard` | .NET Aspire Dashboard: traces, metrics and logs from both hosts ([§7](#7-observability-opentelemetry)) | http://localhost:18888 · no login |

Both .NET hosts expose `/health/live` (is the process up?) and `/health/ready` (can it reach the database and the
broker?).

---

## 4. Try it out

The easiest way is **Swagger UI** at http://localhost:5080/swagger: `POST /api/applications` has a form with a
file picker for the photo. The same steps with `curl`:

### 4.1 Submit an application

The request is `multipart/form-data`: the personal details as form fields, plus the face photo as a file.
Run the commands from the repository root to use the sample photos in [`samples/`](samples/README.md), or use
any JPEG or PNG of at least 10 KB.

```bash
curl -X POST http://localhost:5080/api/applications \
  -F firstName=John -F lastName=Doe -F dateOfBirth=1990-05-20 -F nationalId=AB123456 \
  -F email=john.doe@example.com -F phoneNumber=+306900000000 -F "address=Example Street 10" \
  -F licenceCategory=B -F "photo=@samples/approve.png;type=image/png"
```

The API answers immediately with `202 Accepted`; the workflow runs in the worker:

```json
{ "applicationId": "01a13400-678e-7685-a159-ea972202e12c", "status": "Submitted" }
```

> **Windows:** in PowerShell call `curl.exe` (not `curl`) and put the commands on one line, or replace each `\` line
> ending with a backtick. In Git Bash, give an absolute photo path in Windows style (`C:/Users/...`): a `/c/...`
> path is not readable by Windows `curl`, and the photo is silently left out of the request.

### 4.2 Follow it

```bash
curl http://localhost:5080/api/applications/{applicationId}           # current status
curl http://localhost:5080/api/applications/{applicationId}/history   # audit trail, oldest first
```

Processing takes well under a second, so the status is usually final by the time you ask.

### 4.3 Steer the outcome

The checks are simulated; these inputs trigger each path:

| To get | Submit |
|---|---|
| **Approved** automatically | an adult aged 21 or over, category `B`, a photo that passes the quality checks (`samples/approve.png`) |
| **PendingManualReview** | any one of: age 18–20 · a category other than `B` (e.g. `A`) · national ID `XX000000` (on the watchlist) · a photo with a quality score between 30 and 59 (`samples/manual-review.png`) |
| **Failed** | any one of: no photo · an invalid email · a missing field · age under 18 · a non-JPEG/PNG file (`samples/fail-unsupported-type.bmp`) · a file under 10 KB (`samples/fail-too-small.png`) · a quality score below 30 (`samples/fail-low-quality.png`) |

The quality score (0–100) is derived from the file's bytes: the same file always gets the same score, but your
own photo can land in any bucket, so the samples have known scores ([`samples/README.md`](samples/README.md)).
The history's `details` explain every review or failure, e.g. `"National ID is on the watchlist"`.

### 4.4 Decide a manual review

For an application in `PendingManualReview`:

```bash
curl -X POST http://localhost:5080/api/applications/{applicationId}/approve

curl -X POST http://localhost:5080/api/applications/{applicationId}/reject \
  -H "Content-Type: application/json" \
  -d '{ "reason": "Photo does not meet quality requirements" }'   # the body is optional
```

Approving or rejecting an application in any other status returns `409 Conflict`, with the application's
`currentStatus` and the `attemptedAction` in the response.

### 4.5 Retry safely with an idempotency key

Add `-H "Idempotency-Key: <any unique value>"` to the submit command in [§4.1](#41-submit-an-application). Sending
the same request again with the same key creates nothing new: you get the first application back (its current
status) with an `Idempotent-Replayed: true` header. Reusing the key with different data returns
`422 Unprocessable Entity`.

---

## 5. Everyday commands

| Task | Command |
|---|---|
| Status of the services | `docker compose ps` |
| Follow the logs (JSON, one line per event) | `docker compose logs -f api worker` |
| Stop, keeping the data | `docker compose down` |
| Stop and delete all data (database and queue) | `docker compose down -v` |
| Rebuild after changing the code | `docker compose up -d --build` |
| Run only the infrastructure (to debug the hosts from an IDE) | `docker compose up -d database queue dashboard` |

With only the infrastructure running, `dotnet run` (or F5) on `App/Hosts/Scytales.Api` and
`App/Hosts/Scytales.Worker` works without any changes: their `Development` settings already point at
`localhost:5432`, `localhost:5672` and the dashboard's OTLP endpoint on `localhost:4317`. Start the API first,
because it applies the migrations.

---

## 6. Configuration

Every value has a working default. To override one, set an environment variable or put it in a `.env` file at
the repository root:

| Variable | Default | Used for |
|---|---|---|
| `API_PORT` | `5080` | Host port of the API |
| `WORKER_PORT` | `5081` | Host port of the worker's health endpoints |
| `DATABASE_PORT` | `5432` | Host port of PostgreSQL |
| `QUEUE_PORT` | `5672` | Host port of RabbitMQ (AMQP) |
| `QUEUE_MANAGEMENT_PORT` | `15672` | Host port of the RabbitMQ management UI |
| `DASHBOARD_PORT` | `18888` | Host port of the telemetry dashboard UI |
| `DASHBOARD_OTLP_PORT` | `4317` | Host port of the dashboard's OTLP/gRPC endpoint (for hosts run from an IDE) |
| `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD` | `scytales` | Database name and credentials |
| `RABBITMQ_USER` / `RABBITMQ_PASSWORD` | `scytales` | Broker credentials |
| `RABBITMQ_ERLANG_COOKIE` | `scytales-local-cookie` | Erlang cookie shared by the broker and its CLI tools |

Example `.env`, for a machine where PostgreSQL already uses port 5432:

```dotenv
DATABASE_PORT=55432
```

The business rules (ages, auto-approved categories, photo thresholds, watchlist, time zone) live in
`App/Hosts/Scytales.Worker/appsettings.json`. They can also be overridden per container with the usual
ASP.NET Core variables, for example `WorkflowRules__MinimumAge=17` or `Watchlist__NationalIds__0=AB123456`
under the `worker` service's `environment`.

---

## 7. Observability (OpenTelemetry)

Both hosts send **traces, metrics and structured logs** over OpenTelemetry (OTLP) to the `dashboard` service,
the standalone [.NET Aspire Dashboard](https://aka.ms/dotnet/aspire/standalone-dashboard). Open
http://localhost:18888: it needs no login.

### 7.1 What you can see

Submit an application ([§4.1](#41-submit-an-application)), then open **Traces**. Each request is one trace,
and a submission's trace follows the whole asynchronous flow across both hosts:

```
POST /api/applications/                            scytales-api
├─ postgresql (application, photo, history and outbox message, one transaction)
└─ outbox send → outbox process
   └─ rabbitmq send ApplicationSubmitted            scytales-api
      └─ application-submitted receive / process    scytales-worker
         ├─ postgresql (load the application)
         ├─ workflow Submitted        ─┐
         ├─ workflow ValidatingData    │ one span per workflow step, each with its own save
         ├─ workflow CheckingPhoto     │ tagged application.id, workflow.event, workflow.to_status
         └─ workflow RiskAssessment   ─┘ and workflow.details (the reason, when there is one)
```

- **Structured logs:** every log line from both hosts, with its trace. Open a log line to jump to its trace, or
  a trace to see its logs.
- **Metrics:** HTTP requests, .NET runtime, MassTransit (messages consumed, published, faulted) and Npgsql
  (commands, connection pool).

The dashboard keeps telemetry **in memory**: restarting the container clears it.

### 7.2 How it is set up

`AddObservability()` in `App/Hosts/Scytales.Hosting/Observability.cs` configures both hosts. The exporter only
switches on when `OTEL_EXPORTER_OTLP_ENDPOINT` is set: Compose points it at `http://dashboard:18889`, and the
`Development` settings at the published `localhost:4317`. Tests export nothing. The design, including how lone
background queries are kept out of the traces, is in
[D-18](docs/implementation-steps.md#d-18--observability-opentelemetry-to-the-aspire-dashboard-bn-5).

To send telemetry elsewhere (Jaeger, Seq, Grafana, a collector), point `OTEL_EXPORTER_OTLP_ENDPOINT` at it in the
`x-host-environment` block of `Docker/docker-compose.yml`. Remove the variable to switch export off.

---

## 8. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `Bind for 0.0.0.0:5432 failed: port is already allocated` (or another port) | Something on your machine already uses that port. Change it in `.env` ([§6](#6-configuration)). |
| `include` is reported as an unknown key | Docker Compose is older than v2.20. Update Docker Desktop, or run `docker compose -f Docker/docker-compose.yml up -d`. |
| `queue` keeps restarting with `.erlang.cookie: eacces` in its logs | A Docker Desktop file-permission issue with RabbitMQ's cookie file. The compose file already works around it by passing the cookie as an argument; if it still appears, reset the queue's data volume with `docker compose down -v`. |
| A service stays `(unhealthy)` | `docker compose logs <service>` shows why. `curl http://localhost:5080/health/ready` names the failing dependency. |
| An application stays in `Submitted` | The worker is not running or cannot reach the queue: check `docker compose ps` and `docker compose logs worker`. The message waits in the queue and is processed once the worker is back. |
| Changes to the code have no effect | The images are only built the first time. Rebuild with `docker compose up -d --build`. |
| The dashboard shows no traces | They arrive in batches every few seconds; refresh the page. After `docker compose restart dashboard` (or any recreation) earlier telemetry is gone, since it is kept in memory. |
