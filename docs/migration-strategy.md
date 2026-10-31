# Migration strategy — strangler fig, in phases

This document is the plan of record for replacing the fifteen-year-old LegacyForms portal with
ModernForms without ever going dark. The legacy system keeps serving until the modern slice has
swallowed it one route, one screen, one rule at a time — with the MySQL procedures remaining the
authoritative rule book until the very last cut.

## Philosophy

1. **The procedure contract is the truth.** Every rule in
   `database/mysql/02_stored_procedures.sql` — terminal-state guards, pending-step walks, role
   gates, withdraw rules, `FORMS:*` error codes — is mirrored in C# and walked by tests in
   `modern/tests/`. Tests fail if a mirror drifts.
2. **Both eras build in CI at every commit.** `pipelines/azure-pipelines.yml` compiles the
   net472 portal and runs the modern test suite on every push. If the legacy build dies, migration
   has broken the thing it promised to protect.
3. **Cutover is reversible at every step.** Any Angular route can be rolled back to the MVC action
   (URL map below), and any modern write path can be re-routed to the procedures, because both
   eras read and write the same-shaped tables.

## Phases

### Phase 0 — inventory & freeze (done)

- Full screen/viewmodel/gateway inventory with the modern counterpart per screen
  (`docs/legacy-inventory.md`).
- Legacy database schema lifted verbatim to versioned SQL (`database/mysql/01_schema.sql`) with
  the parity tables and `vw_workload` view.
- CI stabilised: both eras build green; contract tests walk the procedure rules.

### Phase 1 — strangler cutover (done here)

- The Angular SPA is served beside the legacy portal (proxy `/api` against ModernForms API).
- New submissions/decisions may go through the modern API immediately — the modern store is
  schema-identical to the MySQL tables (`form_submission`, `approval_step`, …), so reports and
  the legacy `vw_workload` view keep answering.
- Authentication: the portal switches to the JWT identity contract (`oid` = `employee_key`,
  `roles`) issued by the SSO tenant; the legacy form-auth cookie is read in parallel and accepted
  during the transition window (short-lived, purpose-built shim screen).

### Phase 2 — route-by-route UI retirement (done here, scaled per rollout)

Legacy route → modern route/action map (also see `docs/legacy-inventory.md`):

| Legacy (MVC) | Modern | Notes |
| --- | --- | --- |
| `/Account/Login` | `POST /api/auth/token` + SPA login | preschool of MSAL redirect in phase 3 |
| `/Submissions` (Index) | `/submissions` (Angular list) | same ordering/filters; `mine` toggle for staff |
| `/Submissions/Details/{id}` | `/submissions/{id}` | same three blocks: header, steps, events |
| `/Submissions/Create?template=` | SPA "New request" flow (dashboard → chooser) | payload validated by schema rules before routing |
| AJAX `POST /Submissions/{id}/Decide` | `POST /api/submissions/{id}/decide` | same decision matrix; 409 on stale version |
| AJAX `POST /Submissions/Create` | `POST /api/submissions` | 201 + Location header |

Each row cuts over independently. The legacy action is kept alive (serving a cache-friendly
redirect page) until telemetry shows no more hits.

### Phase 3 — data lift & provider switch

- Reads move to EF Core first (identical columns), writes still through procedures.
- Then writes: the `usp_CreateSubmission` / `usp_SnapshotWorkflow` / `usp_Transition` call sites
  are replaced by `FormService` method calls (the mirrors are already in production use for reads).
- Provider switch: `Database:Provider` moves `SqlServer|AzureSql` primary for the modern store;
  MySQL keeps running the legacy procedures during the sliding window for entities left behind.
- Rollback: flip the provider/Gateway switch back; both schemas are live side by side until the
  final cutover is signed off.

### Phase 4 — retire

- Legacy portal serves 301s for the URL map; procedures remain in the database for a quarter
  ("tombstoned" with a redirect to the API) before being dropped.
- MySQL database is decommissioned after the final backup + data migration.

## Deployment & rollback mechanics

- CI produces two artifacts: `legacy-build` (net472 portal) and `modernforms-api-drop` +
  `modernforms-web-drop`.
- Modern API deploys to an Azure App Service **staging slot** (same slot model as the FinTrack
  programme): migrate + seed steps run in the slot start, `/health` smoke gates the swap.
- Angular bundles publish as static content with the slot swap (documented deployment steps in
  `docs/modern-architecture.md` § Deployment).
- **Rollback is a re-swap**: the slot still holds the previous bits; no database rollback is needed
  while both stores agree on schema shape.

## Risks & controls

| Risk | Control |
| --- | --- |
| Mirror drift between procs and C# | tests walk both rule books; CI blocks drift |
| Schema drift under parallel writes | EF config keeps legacy-parity column names & types; migrations reviewed against `01_schema.sql` in PRs |
| Auth shim exploitation | transition window limited; tokens carry `employee_key` (`oid`) — same identity contract audited end to end |
| Dual-provider EF confusion | single `FormsDbContext`; provider selected once at composition root (`Database:Provider`) |
| MySQL→SQLite storage subtleties | demo data only in SQLite; production runs on SqlServer/AzureSql profiles with MySQL contracts documented |
