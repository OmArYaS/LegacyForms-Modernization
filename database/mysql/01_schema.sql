-- ══════════════════════════════════════════════════════════════════════════
--  LegacyForms· modernization programme — shared physical schema (MySQL 8)
--  Both eras run against this schema: the MVC 5 portal calls the stored
--  procedures directly; the modern API reaches the same procedures through
--  the MySqlConnector gateway during phase 1 and replaces them with EF Core
--  LINQ in phase 3 (see docs/migration-strategy.md).
--
--  Conventions
--  • utf8mb4 — full Unicode payloads and notes.
--  • All identifiers snake_case so they read naturally in raw SQL.
--  • DECIMAL-free domain: this platform stores JSON payloads, timestamps
--    and counters; money never flows through forms.
-- ══════════════════════════════════════════════════════════════════════════

CREATE DATABASE IF NOT EXISTS legacyforms
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE legacyforms;

-- ── Employees ─────────────────────────────────────────────────────────────
-- The legacy table. Older than the platform itself — a sketchy BIGINT id,
-- a "full_name" that doubles as display name, and an email that some
-- provisioning paths leave empty. Phase 2 migrates these rows behind
-- external ids (usr_*) carried by Azure AD.
CREATE TABLE IF NOT EXISTS employee (
  employee_id   BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  employee_key  VARCHAR(64)     NOT NULL,          -- AAD object id suffix (usr_*)
  full_name     VARCHAR(160)    NOT NULL,
  email         VARCHAR(190)    NULL,
  department    VARCHAR(80)     NULL,
  role          ENUM ('employee','supervisor','admin') NOT NULL DEFAULT 'employee',
  active        TINYINT(1)      NOT NULL DEFAULT 1,
  created_at    DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (employee_id),
  UNIQUE KEY uq_employee_key (employee_key),
  KEY ix_employee_role (role)
) ENGINE = InnoDB;

-- ── Form templates ────────────────────────────────────────────────────────
-- Field layout and the approval workflow both live in field_schema as JSON,
-- exactly one document per template. The legacy UI (Knockout) and the modern
-- SPA (Angular) render from the same document — no fork of the schema.
CREATE TABLE IF NOT EXISTS form_template (
  template_id     INT UNSIGNED   NOT NULL AUTO_INCREMENT,
  template_key    VARCHAR(64)    NOT NULL,       -- 'leave-request', 'expense-claim', …
  title           VARCHAR(120)   NOT NULL,
  description     VARCHAR(500)   NULL,
  field_schema    JSON           NOT NULL,       -- { fields:[…], workflow:{ steps:[…] } }
  is_active       TINYINT(1)     NOT NULL DEFAULT 1,
  created_at      DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at      DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (template_id),
  UNIQUE KEY uq_template_key (template_key),
  FULLTEXT KEY ft_template_search (title, description)
) ENGINE = InnoDB;

-- ── Submissions ───────────────────────────────────────────────────────────
-- Workflow state machine: draft → submitted → in_review → approved / rejected;
-- submitted and in_review may additionally move to withdrawn by the requestor.
-- version is a monotonic row version that optimistic concurrency checks bump;
-- the stored procedures refuse stale writes rather than last-write-wins.
CREATE TABLE IF NOT EXISTS form_submission (
  submission_id     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  template_id       INT UNSIGNED    NOT NULL,
  requestor_id      BIGINT UNSIGNED NOT NULL,
  status            ENUM ('draft','submitted','in_review','approved','rejected','withdrawn') NOT NULL DEFAULT 'draft',
  payload_json      JSON            NOT NULL,
  submitted_at      DATETIME        NULL,
  closed_at         DATETIME        NULL,
  decision_note     VARCHAR(500)    NULL,
  version           INT UNSIGNED    NOT NULL DEFAULT 1,
  created_at        DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (submission_id),
  KEY ix_submission_status (status),
  KEY ix_submission_requestor (requestor_id),
  KEY ix_submission_status_submitted (status, submitted_at),
  CONSTRAINT fk_submission_template FOREIGN KEY (template_id) REFERENCES form_template (template_id),
  CONSTRAINT fk_submission_requestor FOREIGN KEY (requestor_id) REFERENCES employee (employee_id)
) ENGINE = InnoDB;

-- ── Approval steps ────────────────────────────────────────────────────────
-- Snapshotted from field_schema.workflow at submit time so that later
-- template edits never re-shape in-flight requests.
CREATE TABLE IF NOT EXISTS approval_step (
  step_id        BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  submission_id  BIGINT UNSIGNED NOT NULL,
  step_sequence  SMALLINT UNSIGNED NOT NULL,
  role_required  ENUM ('supervisor','admin') NOT NULL,
  decision       ENUM ('pending','approved','rejected') NOT NULL DEFAULT 'pending',
  decided_by     BIGINT UNSIGNED  NULL,
  decided_at     DATETIME         NULL,
  note           VARCHAR(500)     NULL,
  PRIMARY KEY (step_id),
  UNIQUE KEY uq_step_submission_sequence (submission_id, step_sequence),
  KEY ix_step_role_decision (role_required, decision),
  CONSTRAINT fk_step_submission FOREIGN KEY (submission_id) REFERENCES form_submission (submission_id),
  CONSTRAINT fk_step_decider    FOREIGN KEY (decided_by)    REFERENCES employee (employee_id)
) ENGINE = InnoDB;

-- ── Audit trail ───────────────────────────────────────────────────────────
-- Append-only; both eras write through the transition procedure so no code
-- path can skip it.
CREATE TABLE IF NOT EXISTS form_event (
  event_id      BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  submission_id BIGINT UNSIGNED  NOT NULL,
  event_type    VARCHAR(40)      NOT NULL,
  actor_id      BIGINT UNSIGNED  NULL,
  detail        VARCHAR(400)     NULL,
  occurred_at   DATETIME         NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (event_id),
  KEY ix_event_submission (submission_id, occurred_at),
  CONSTRAINT fk_event_submission FOREIGN KEY (submission_id) REFERENCES form_submission (submission_id)
) ENGINE = InnoDB;

-- ── View (modernization phase 2 consumed this before the API did) ─────────
-- The workload summary both the legacy dashboard widget and the modern
-- "my queue" card render. Indexed-cover for MySQL 8.x: the ENUM columns can
-- not be indexed directly in the view, so a join against approval_step does
-- the counting with the ix_step_role_decision covering index.
CREATE OR REPLACE VIEW vw_workload AS
  SELECT
    s.status,
    a.role_required,
    COUNT(DISTINCT s.submission_id) AS pending_submissions
  FROM form_submission s
  JOIN approval_step a
    ON a.submission_id = s.submission_id
   AND a.decision      = 'pending'
  WHERE s.status IN ('submitted', 'in_review')
  GROUP BY s.status, a.role_required;
