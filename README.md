# LegacyForms → ModernForms — a strangler-fig modernization programme

![Legacy .NET Framework 4.7.2](https://img.shields.io/badge/legacy-MVC%205%20%2B%20Knockout%20·%20net472-6b3074) ![Modern .NET 10](https://img.shields.io/badge/modern-.NET%2010-LTS-blueviolet) ![Angular 17](https://img.shields.io/badge/Angular-17-dd0031) ![MySQL 8](https://img.shields.io/badge/MySQL-8-procs%20%2B%20JSON-4479a1) ![Tests](https://img.shields.io/badge/tests-xUnit%2056%20passing-green) [![Build Status](https://img.shields.io/badge/Azure%20DevOps-CI-passing)](#ci)

A complete **legacy modernization case study for the Siemens platform track**: a fifteen-year-old
ASP.NET **MVC 5 + Knockout.js + jQuery** forms/approvals portal where all of the business rules
live in **parameterized MySQL stored procedures** — and the modern replacement built under it
piece by piece: **ASP.NET Core (Clean Architecture) + EF Core + Angular**, with the legacy
procedure contract kept as the single source of truth while the strangler swallows it.

> Built as a portfolio piece for my Siemens TA application — it demonstrates exactly the shape of
> work I would own on a platform team: inventory a brownfield system, keep it safe (both eras
> build green in CI on every push), migrate screens slice by slice, and prove behavioral parity
> with tests that walk the same rule book the old procedures enforce.

---

## The two eras, side by side

| | 🏚️ Legacy era (`legacy/`) | 🏗️ Modern era (`modern/`) |
| --- | --- | --- |
| Runtime | .NET Framework **4.7.2** (SDK-style csproj + reference assemblies — builds with plain `dotnet build`) | .NET **10** LTS |
| UI | MVC 5 + Knockout.js 3.5 + jQuery 3.6 (schema-driven form generator, session-cookie auth) | Angular **17** standalone (schema-driven renderer + `@if/@for` control flow) |
| Server | ASP.NET MVC 5, `FormsGateway` calling MySQL procedures directly | ASP.NET Core Web API, Clean Architecture (Domain → Application → Infrastructure → Api) |
| Data | MySQL 8 **stored procedures** (`usp_Transition`, `usp_GetSubmission`, `usp_WorkloadSummary`, …) | EF Core **code-first** (Sqlite demo / SqlServer / AzureSql profiles; MySQL stays through the legacy procedures in phase 1 and switches providers in phase 3) |
| Rules home | `SIGNAL '45000'` with `MESSAGE_TEXT='FORMS:<CODE>'` inside the procs | The same codes thrown from the Application layer (`BusinessRuleException`) → RFC 9457 ProblemDetails |
| Auth | ASP.NET Forms Authentication + `employee_key` sessions | JWT (`oid` = employee key, `roles` claims) — dev stub now, Azure AD SSO in phase 3 |

**The point of the exercise:** the legacy system is *not* rewritten in one heroic PR. The modern
slice consumes the same workflow rules (walked one by one in `database/mysql/02_stored_procedures.sql`),
serves the same JSON the legacy screens expected, and both eras stay deployable while employees
migrate off the old portal route by route.

## The shared contract: `FORMS:*` codes

Old and new speak the same error dictionary — the legacy jQuery handlers read `FORMS:<CODE>`
splats from failed procedure calls; the modern SPA reads ProblemDetails `code` fields. Both feed
the same UI copy:

| Code | HTTP | Meaning |
| --- | --- | --- |
| `SUBMISSION_NOT_FOUND` | 404 | no such submission |
| `NOT_SUBMISSION_OWNER` | 403 | employees read/withdraw only their own requests |
| `TEMPLATE_NOT_FOUND` | 404 | template missing or inactive |
| `REQUESTOR_INACTIVE` | 403 | unknown/inactive employee key |
| `TERMINAL_STATE` | 409 | submission already closed |
| `NO_PENDING_STEP` | 409 | inconsistent approval state |
| `ROLE_MISMATCH` | 403 | the pending step needs another gate |
| `WITHDRAW_TOO_LATE` | 409 | a reviewer already decided |
| `CONCURRENT_UPDATE` | 409 | stale version (EF concurrency token) |
| `INVALID_DECISION` / `INVALID_PAYLOAD` / `INVALID_STATUS` / `STORE_CONFLICT` | 400/409 | input & store guards |

## Repo map

```
LegacyForms-Modernization/
├── database/mysql/                 # the legacy data contract (schema, procedures, seed)
│   ├── 01_schema.sql               #   legacy-parity tables + vw_workload
│   ├── 02_stored_procedures.sql    #   usp_* — THE rule book; mirrors also live in modern C#
│   └── 03_seed.sql                 #   demo dataset (usr_amira/tarek/mona/ossama)
├── legacy/LegacyForms.Web/         # era-authentic MVC 5 portal (net472, builds green)
│   ├── Controllers/                #   AJAX endpoints catching MySqlException 1644 → FORMS codes
│   ├── Data/FormsGateway.cs        #   parameterized procedure gateway
│   └── Views/                      #   Knockout index / details / New with schema-driven generator
├── modern/
│   ├── ModernForms.sln
│   ├── src/
│   │   ├── ModernForms.Domain/       # entities, enums, schema document + rules
│   │   ├── ModernForms.Application/  # FormService (workflow engine), DTOs, contracts
│   │   ├── ModernForms.Infrastructure/ # EF Core: Sqlite/SqlServer/AzureSql, migrations, seed
│   │   └── ModernForms.Api/          # controllers, JWT auth, ProblemDetails middleware
│   ├── tests/ModernForms.Tests/      # xUnit 56: schema + workflow mirrors + API contracts
│   └── webapp/                       # Angular 17 SPA (standalone, signals, OnPush)
├── docs/                             # migration-strategy · legacy-inventory · modern-architecture
└── pipelines/azure-pipelines.yml     # CI: legacy net472 build ∥ modern build + tests + Angular
```

## Quickstart

**Modern slice (zero external dependencies):**

```bash
cd modern/src/ModernForms.Api
dotnet run                # migrations + demo seed on a disposable SQLite DB → http://localhost:6000/swagger
```

```bash
cd modern/webapp
npm install
npm start                 # Angular dev server → http://localhost:4200 (proxies /api → :6000)
```

**Demo logins** (development JWT stub, `Auth:Mode=Development`): `amira`/`tarek` (employees),
`mona` (supervisor), `ossama` (admin) — the login screen posts `POST /api/auth/token`.

**Legacy portal** (needs IIS Express + MySQL with `database/mysql/*.sql` applied):

```bash
# build it anywhere with the SDK:
dotnet build legacy/LegacyForms.Web/LegacyForms.Web.csproj -c Release
# deploy to IIS: the classic publish-to-folder route (see docs/migration-strategy.md)
```

**Modern tests only:**

```bash
dotnet test modern/ModernForms.sln
```

## Testing (what actually gets walked)

- **Schema rules** (`tests/.../Domain/FieldSchemaTests.cs`) — the payload validator + workflow
  snapshot invariants (ordered 1..N steps, `supervisor|admin` gates) that `usp_SnapshotWorkflow`
  used to own.
- **Workflow mirrors** (`tests/.../Application/FormWorkflowTests.cs`) — every decision branch of
  `usp_Transition` exercised against a real relational store: pending-step walk, recipient gates,
  withdraw window, optimistic concurrency (`CONCURRENT_UPDATE`), terminal-state guard.
- **API contract tests** (`tests/.../Api/ApiContractTests.cs`) — the full HTTP pipeline with the
  seeded demo database: 401/403/404/409 paths, the two-step approval flow (supervisor → admin),
  the staleness trap, the withdraw window, admin-only template inspection.

## Migration strategy (summary)

The programme runs in four phases — the full document lives in
[`docs/migration-strategy.md`](docs/migration-strategy.md), screen-by-screen inventory in
[`docs/legacy-inventory.md`](docs/legacy-inventory.md), and the modern architecture in
[`docs/modern-architecture.md`](docs/modern-architecture.md).

1. **Freeze and chord** — legacy parity tables + procedure contracts documented; CI keeps both eras green.
2. **UI module cutover** — Angular swaps the Knockout screens route by route behind a proxy; the legacy COM+ of forms auth is replaced by the JWT/SSO contract at the edge.
3. **Data lift** — requests created in the modern era still flow through the `usp_*` procedures;
   the decision engine mirrors them in C# for read paths and phases out writes last.
4. **Retire** — LINQ over EF Core replaces the remaining procedure call sites; the MySQL database
   switches providers; legacy portal serves a 301 map.

## Roadmap

- [x] Legacy data contract (schema + procedures + seed) captured as versioned SQL
- [x] Legacy MVC 5 portal builds green on modern tooling (SDK-style + reference assemblies)
- [x] Modern clean-architecture slice with EF Core migrations and provider switch
- [x] Workflow engine mirroring `usp_Transition` + optimistic concurrency
- [x] Angular 17 SPA (schema-driven renderer, decision panel, workload dashboard)
- [x] 56 xUnit tests incl. full-pipeline contract tests
- [ ] Draft editing + re-submit flow (modern era)
- [ ] Azure AD SSO tenant wiring (MSAL) replacing the dev JWT stub
- [ ] Notification fan-out on decisions (employee inbox)
- [ ] OpenTelemetry traces across API → database

## License

MIT — see [LICENSE](LICENSE).
