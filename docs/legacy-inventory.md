# Legacy inventory — every screen, viewmodel, and call, with its modern counterpart

This is the Phase 0 inventory promised in `docs/migration-strategy.md`: a screen-by-screen,
viewmodel-by-viewmodel census of the pre-modernization portal, paired with the modern artifact
that replaces it. Nothing in the legacy tree gets deleted until its row here has a shipped
modern replacement and the retirement note is ticked. The document is also the reason the
modern SPA route names (`modern/webapp/src/app/app.routes.ts`) look the way they do — they
mirror the legacy URL shapes on purpose (bookmark survival, support training, 301s in Phase 4).

## What was counted

Legacy sources are frozen in `legacy/` and are never modified by the migration (the strangler
discipline — see `docs/migration-strategy.md` § Philosophy):

| Legacy artifact | Path (frozen in `legacy/LegacyForms.Web/`) |
| --- | --- |
| MVC 5 portal project | `LegacyForms.Web.csproj` (net472, SDK-style reference build) |
| Controllers | `Controllers/LegacyControllerBase.cs`, `AccountController.cs`, `HomeController.cs`, `SubmissionsController.cs` |
| Razor views | `Views/Shared/_ViewStart.cshtml`, `Views/Shared/_Layout.cshtml`, `Views/Account/Login.cshtml`, `Views/Home/Index.cshtml`, `Views/Submissions/{Index,New,Details}.cshtml` |
| Data gateway | `Data/FormsGateway.cs` + row types (`TemplateRow`, `SubmissionListItem`, `SubmissionDetail`, `StepRow`, `EventRow`, `WorkloadRow`, `SubmissionIndexModel`) |
| Client scripts | `Scripts/jquery-3.6.0.min.js`, `Scripts/knockout-3.5.3.min.js` |
| Styling | `Content/site.css` (the `.chip`, `.state`, `.grid`, `.card-block` vocabulary lives on in the SPA design system) |
| Rule book | `database/mysql/02_stored_procedures.sql` — the six procedures listed below |

## Screen census

### 1. `Views/Account/Login.cshtml` — `AccountController.Login`

| Aspect | Legacy |
| --- | --- |
| Route | `GET/POST /Account/Login` |
| Viewmodel | `ViewBag.Error` on failure; no model binding |
| Behavior | Employee-key + password form; anti-forgery token; on success `StoreSession(employee_id, full_name, role)` into `Session`, then redirect to `/Submissions`. Checks `active` flag on the `employee` row (`SELECT ... WHERE employee_key = @key`). |
| Access rule | Signed-in users are bounced to the submissions list (`IsAuthenticated` guard) |
| Demo keys | `usr_amira`, `usr_tarek` (employee), `usr_mona` (supervisor), `usr_ossama` (admin) |

Legacy → Modern:

- `legacy/LegacyForms.Web/Views/Account/Login.cshtml` → `modern/webapp/src/app/features/login.component.ts` (rendered by `app.component.ts` as the entrance gate: no token → sign-in screen, token → router outlet)
- `POST /Account/Login` (form post, session cookie) → `POST /api/auth/token` (`modern/src/ModernForms.Api/Controllers/AccountController.cs`) issuing the signed JWT; `oid` = `employee_key`, `roles` = role claim — the identity contract in `docs/migration-strategy.md` Phase 1
- `Session.Clear()/Abandon()` (`LegacyControllerBase.SessionAbandon`) → drop the localStorage token (`modernforms.token`) + sign-out route in `layout.component.ts`
- The password-vs-`active` check → in the transition window the SSO tenant owns proof-of-identity; the demo provider stubs it in `Auth/DevelopmentJwtTokenIssuer` (documented, never shipped to prod)
- Password hashing policy → retired entirely (SSO story, `docs/migration-strategy.md` § Risks)

### 2. `Views/Home/Index.cshtml` — `HomeController.Index`

| Aspect | Legacy |
| --- | --- |
| Route | `GET /` |
| Viewmodel | none |
| Behavior | "Legacy scaffold probe" — a build/link sanity page, no data access |

Legacy → Modern: no function beyond a probe → retired with no replacement; `GET /` redirects to `/dashboard` in the SPA (`app.routes.ts` `pathMatch: 'full'`). In Phase 4 the legacy route serves a 301 to the SPA.

### 3. `Views/Shared/_Layout.cshtml`

| Aspect | Legacy |
| --- | --- |
| Behavior | Topbar (brand + working era tag), session widget (`EmployeeName (EmployeeRole)` + POST logout with anti-forgery), nav dependent on sign-in state, jQuery + Knockout script includes, per-view `@section scripts` |

Legacy → Modern:

- Layout chrome → `modern/webapp/src/app/features/layout.component.ts` (topbar + role-aware nav)
- The era tag ("MVC 5 · Knockout" / "Strangler era…") → intentionally kept: the modern `layout.component.ts` shows the era badge so support staff can tell which system is answering
- Session widget + logout form → `auth.service.ts` (`modernforms.token` lifetimes) + layout sign-out
- Nav links → role-aware Angular routes (employees never see the templates-admin link)

### 4. `Views/Submissions/Index.cshtml` — `SubmissionsController.Index`

| Aspect | Legacy |
| --- | --- |
| Route | `GET /Submissions?status=&page=1` |
| Viewmodel | `SubmissionIndexModel { StatusFilter, Page, IsEmployee, Items }` |
| Behavior | Status filter chips (`all/submitted/in review/approved/rejected/withdrawn`), 25/page table, requestor column only for staff (supervisor/admin), state pills, `open` → Details. Matching is ordinal-insensitive against the filter list. |

Legacy → Modern:

- Screen → `modern/webapp/src/app/features/submissions.component.ts` (`/submissions`)
- `GET /Submissions` server-rendered list → `GET /api/submissions?status=&mine=&page=&pageSize=25` (`SubmissionsController.List`)
- `SubmissionIndexModel.IsEmployee` (staff-biased columns) → `mine=true` toggle driven by the role claim; requestor column for supervisor/admin with identical condition
- Filter chips → same chip vocabulary in `submissions.component.ts`; the `status` query parameter contract is unchanged (`submitted|in_review|approved|rejected|withdrawn`)
- Paging fields (`Page`, 25/page) → `PagedResult<SubmissionListItemDto>` (`Page`, `PageSize`, `TotalCount`, `Items`)

### 5. `Views/Submissions/New.cshtml` — `SubmissionsController.New` / `Create` (AJAX)

| Aspect | Legacy |
| --- | --- |
| Route | `GET /Submissions/New` (chooser screen) |
| Viewmodel | `IList<TemplateRow>` (passed in via `usp_GetActiveTemplates`) |
| Behavior | Per-template card with the field schema embedded as `data-schema`; jQuery renders the blank form from the schema (text/select/date/number inputs, `required`, `maxlength`, `min`), `collect()` builds the payload object, posts either *Submit now* or *Save as draft* through the Create AJAX endpoint, then redirects to Details on success |

Legacy → Modern:

- Screen → the SPA new-request flow: `/templates` chooser in `modern/webapp/src/app/features/templates.component.ts` opens the schema-driven creation form (the dashboard's "Start a form" shortcuts link to it)
- Schema-driven rendering → the same `FieldSchemaDocument` JSON consumed verbatim by the schema-driven renderer; identical field types/validation contract in both eras (`FormSchemaRules`)
- `POST /Submissions/Create` (`templateId`, `payloadJson`, `submitNow`) → `POST /api/submissions` with `{ templateKey, payloadJson, submitNow }` (`FormService.CreateAsync`) — 201 + `Location` header instead of a redirect wall
- Payload keys by `key` with `'' → null` normalization → implemented in the SPA `collect` equivalent, then schema-validated server-side (`INVALID_PAYLOAD` on drift)
- `$feedback.text('Not accepted: ' + res.code)` → `friendlyProblem()` in `modern/webapp/src/app/core/api-messages.ts`

### 6. `Views/Submissions/Details.cshtml` — `SubmissionsController.Details` / `Decide` (AJAX)

| Aspect | Legacy |
| --- | --- |
| Route | `GET /Submissions/Details/{id}` |
| Viewmodel | `SubmissionDetail` (header fields + `Steps` + `Events` from the three `usp_GetSubmission` result sets) |
| Behavior | Four blocks — form contents (Knockout `foreach: fields` over `data-schema`+`data-payload`), approval steps table, decision panel (only when `CanDecide || CanWithdraw`, embeds `data-version`), history trail. jQuery posts decisions with the current `version`; on success it reloads; on failure prints `humanize(CODE)` |
| Rights engine | `CanDecide`: status `submitted|in_review` and the *first pending step* requires the caller's role (admin discharges any step). `CanWithdraw`: requestor, open status, and no decision recorded on any step |

Legacy → Modern:

- Screen → `modern/webapp/src/app/features/submission-detail.component.ts` (`/submissions/:id`, schema-driven renderer mirrors the Knockout template)
- `CanDecide` / `CanWithdraw` (server-rendered `ViewBag`) → computed server-side in `FormService.DecideAsync` and again client-side in the detail component; the API never trusts the UI (403 via `ROLE_MISMATCH` / `NOT_SUBMISSION_OWNER`)
| `POST /Submissions/{id}/Decide` (`id, decision, note, version`) → `POST /api/submissions/{id}/decide` with `DecideRequest { Decision, Note, SeenVersion }` (`FormService.DecideAsync`)
- The `humanize()` map (stale view advice, role wording, terminal-state text) → `api-messages.ts` carries the same wording keyed to the same codes
- The decision buttons' `data-role` decoration → role claims on the JWT; the decide policy is the intention-preserving rewrite of `CanDecide`

### 7. `Views/Shared/_ViewStart.cshtml`

| Aspect | Legacy |
| --- | --- |
| Behavior | Sets/renders the layout — boilerplate |

No counterpart (Angular has no layout-recursion concept; `LayoutComponent` is a route shell).

## AJAX endpoints supplied to the Knockout viewmodels

Every legacy client posts through exactly two actions; both sit behind `RequireSession()` and
`ValidateAntiForgeryToken`. The modern API replaces the anti-forgery token + session with the
JWT bearer token, so the CSRF surface disappears with it (tokens are sent only by authorized
code, never attached by a browser automatically).

| Legacy AJAX call (`SubmissionsController`) | Modern API call | Response surface |
| --- | --- | --- |
| `POST /Submissions/Create` → `Json({ ok, submissionId })` | `POST /api/submissions` | 201 + `CreatedSubmissionDto`; `ProblemDetails` (`type` carries the legacy code) on rule breach |
| `POST /Submissions/{id}/Decide` → `Json({ ok, version, status })` | `POST /api/submissions/{id}/decide` | 200 + `DecisionResultDto` carrying the new version; `ProblemDetails` on rule breach |
| `RequireSession()` failure JSON `({ ok:false, code:'SESSION_EXPIRED' })` | 401 `WWW-Authenticate` → SPA gate (`app.component.ts` renders the sign-in screen; `authGuard` sends unauthenticated routes to `/`) | |

The `{ ok : false → HTTP status }` translation:

| Legacy shape | Modern HTTP status | Why |
| --- | --- | --- |
| `Json({ok:false, code:'SESSION_EXPIRED'})` | 401 | Identity contract instead of a hall pass |
| `Json({ok:false, code:'FORMS:<CODE>'})` (`MySqlException` 1644 catch) | 400/409 `ProblemDetails` with the same `FORMS:<CODE>` text in `type` | See the error contract table below |
| silent 500-style failures | 500 `ProblemDetails` envelope | Nothing internal leaks; codes stay client-stable |

## Identity inventory — `LegacyControllerBase`

| Mechanism | Legacy | Modern |
| --- | --- | --- |
| Identity source | `Session["EmployeeId" / "EmployeeName" / "EmployeeRole"]` | JWT claims (`oid` → `EmployeeKey`, `roles` role claim), mapped by `CurrentUser` |
| Access guard | `RequireSession(role?)` — redirect to `Account/Login` when anon, 403 page when role-mismatched | `[Authorize]` + per-endpoint `[Authorize(Policy = Policies.Admin)]`; middleware returns `ProblemDetails`; SPA `authInterceptor` pumps the bearer token and `authGuard` protects routes |
| Session teardown | `Session.Clear(); Session.Abandon();` | Token deletion in `auth.service.ts`; the entrance gate in `app.component.ts` re-renders the sign-in screen |
| Who may read what | Employees are filtered at the procedure level (`pi_role`) plus gateway read enforcement; controllers rely on `RequireSession` | `FormService.GetSubmissionAsync` enforces ownership on read (`NOT_SUBMISSION_OWNER`); list filters `mine=true` for staff; inspect route is admin-gated — the Razor `HttpGet` equivalents had one shared page |

## Data-access inventory — `Data/FormsGateway.cs`

The 2010-era gateway is `MySqlCommand`-only: every read a procedure, every argument a
`MySqlParameter`, zero dynamic SQL. The modern gateway honors the same contract shape — the
mirrors live in `modern/src/ModernForms.Application/Forms/FormService.cs` and
`TemplateService`, reading/writing the schema-identical tables documented in `database/mysql/01_schema.sql`.

| Gateway method | Procedure (fixed contract) | Modern counterpart | Notes |
| --- | --- | --- | --- |
| `GetActiveTemplates()` | `usp_GetActiveTemplates` | `TemplateService` / `GET /api/templates` | `TemplateDto` = the proc's `TemplateRow` columns; schema JSON travels verbatim |
| `ListSubmissions(employeeId, role, status, page, 25)` | `usp_ListSubmissions` | `FormService.ListSubmissionsAsync` / `GET /api/submissions` | Employees narrowed to own rows; staff see all + `mine` toggle; same `status` filter string |
| `GetSubmission(id, employeeId, role)` | `usp_GetSubmission` — three result sets: header, `Steps`, `Events` | `FormService.GetSubmissionAsync` / `GET /api/submissions/{id}` | EF Core ownership check (`NOT_SUBMISSION_OWNER`) plus `NotFoundException` (= legacy `InvalidOperationException`) → 404 |
| `CreateSubmission(templateId, requestorId, payloadJson, submitNow)` | `usp_CreateSubmission` with `po_submission_id` output param | `FormService.CreateAsync` / `POST /api/submissions` | Rule mirrors: `TEMPLATE_NOT_FOUND`, `REQUESTOR_INACTIVE`, `INVALID_PAYLOAD`; legacy-by-`templateId` — modern identifies by `templateKey` (keyed on `TemplateKey` in both stores) |
| `Transition(id, actorId, role, decision, note, seenVersion)` | `usp_Transition` with `po_new_version` output param | `FormService.DecideAsync` / `POST /api/submissions/{id}/decide` | The rule book lived here; EF version token gives the same drift throw (`CONCURRENT_UPDATE`, 409) |
| `WorkloadSummary()` | `usp_WorkloadSummary` (backed by `vw_workload`) | `FormService.WorkloadAsync` / `GET /api/submissions/workload` | Group-by translated client-side (`status/role/count` rows ranked `supervisor` first, then `admin`) |

### How the legacy output parameters survive

- `po_submission_id` → modern `CreatedSubmissionDto { SubmissionId, TemplateKey, Status, SubmittedAt, Version }` (201 body + `Location`)
- `po_new_version` → modern `DecisionResultDto { NewVersion, Status }`
- `MySqlException` with `ErrorCode == 1644` and `ExtractCode()` parsing
  `FORMS:<CODE>` → `ExceptionHandlingMiddleware` writes `ProblemDetails` with that code and
  an HTTP status chosen by the rule type

## Error contract — the codes travel verbatim

`humanize()` in `Submissions/Details.cshtml` is the oldest piece of API documentation in the
codebase; every code it names has a modern twin:

| Legacy `FORMS:` code (raised by `SIGNAL SQLSTATE '45000'`) | Raised by (proc) | Modern ProblemDetails code + status | Modern guard |
| --- | --- | --- | --- |
| `SUBMISSION_NOT_FOUND` | `usp_GetSubmission`, `usp_Transition` | 404 / `SUBMISSION_NOT_FOUND` | missing row in `FormService.DecideAsync` |
| `NOT_SUBMISSION_OWNER` | `usp_GetSubmission` (read gate), `usp_Transition` (withdraw gate) | 403 / `NOT_SUBMISSION_OWNER` | `GetSubmissionAsync` + withdraw path |
| `CONCURRENT_UPDATE` | concurrent mutation check (`FORMS:CONCURRENT_UPDATE` raise on version drift) | 409 / `CONCURRENT_UPDATE` | EF concurrency token on `Version` |
| `TERMINAL_STATE` | `usp_Transition` terminal guard | 409 / `TERMINAL_STATE` | status guard in `DecideAsync` |
| `WITHDRAW_TOO_LATE` | `usp_Transition` withdraw-after-decision guard | 409 / `WITHDRAW_TOO_LATE` | decided-step check in `DecideAsync` |
| `NO_PENDING_STEP` | `usp_Transition` pending-step walk | 409 / `NO_PENDING_STEP` | first-pending-step walk (`supervisor` before `admin`) |
| `ROLE_MISMATCH` | `usp_Transition` role gate | 403 / `ROLE_MISMATCH` | role gate in `DecideAsync` |
| `INVALID_DECISION` | `usp_Transition` decision vocabulary | 400 / `INVALID_DECISION` | `DecideAsync` orientation switch |
| `TEMPLATE_NOT_FOUND` | `usp_CreateSubmission` | 400 / `TEMPLATE_NOT_FOUND` | `CreateAsync` template probe |
| `REQUESTOR_INACTIVE` | `usp_CreateSubmission` `active`-flag check | 403 / `REQUESTOR_INACTIVE` | `CreateAsync` requestor probe |
| `INVALID_STATUS` (list filter) | not modeled in legacy (empty param only) | 400 / `INVALID_STATUS` | `ListSubmissionsAsync` filter guard |
| `INVALID_PAYLOAD` (schema rules) | not modeled in legacy — server-side template-active check only | 422 / `INVALID_PAYLOAD` | `FormSchemaRules` document walk |

Codes that exist only on the modern side close gaps the legacy procs left to luck; none of
them reuses a legacy name for a different rule.

## Modern-only screens (spawned by the migration, inventoried for completeness)

| Screen | Route | Purpose | In the legacy era |
| --- | --- | --- | --- |
| Dashboard workload widget | `/dashboard` (`dashboard.component.ts`) + `GET /api/submissions/workload` | Supervisor/admin queue counters | there was no dashboard; `usp_WorkloadSummary` was fetched by nobody in the MVC era |
| Templates catalog + schema inspector | `/templates` (`templates.component.ts`) + `GET /api/templates/{templateKey}/inspect` | Admin-only view of the schema contract and its rule walk | template rows were edited directly in SQL with no UI |
| Not-found and access-denied | SPA shells | Standardized `ProblemDetails` surfacing | plain redirects / 403 status page |

## Client-side stack inventory

| Legacy (frozen) | Modern |
| --- | --- |
| jQuery 3.6.0 DOM work (`Scripts/jquery-3.6.0.min.js`) | Angular 17 (standalone, signals, OnPush) via `npm run build` (`modern/webapp/package.json`) |
| Knockout 3.5.3 viewmodels (schema field loops + decision panel) | schema-driven renderer + typed `FormsService` calls |
| Razor `@section scripts` per-view wiring | component templates with `@for` control flow |
| `__RequestVerificationToken` anti-forgery headers | Authorization bearer header (`auth.interceptor.ts`) + `Proxy → /api` in `proxy.conf.json` |
| `Content/site.css` chip/state/grid vocabulary | `styles.scss` design system (same class names so the screens read identically) |

## Retirement order (per `docs/migration-strategy.md` Phase 2)

Strangler sequence — each row retires the legacy action without touching the rest:

1. **Detail** (`Submissions/Details` + its Decide AJAX) is the busy one — cutover
   first, because the modern decision panel speaks the same codes and the same
   optimistic-version guard, and the contract tests already walk that path end to end.
2. Then the **list** (`Submissions/Index`) — read-only; hybrid risk-free.
3. Then the **chooser** (`Submissions/New` + Create AJAX) — two-sided (reads templates from
   whichever store still serves them; writes go directly to the modern store from day one).
4. **Login** last: both eras must recognize each other's identity during the window
   (legacy session cookie ↔ JWT shim in Phase 1).
5. **Layout/ViewStart/Home probes** retire with the portal container itself in Phase 4; they
   exist to host the four above.

Ticking a row means: Angular route live, telemetry (URL hits) at zero, and `pipelines`
`legacy_era` still green on the untouched sources.
