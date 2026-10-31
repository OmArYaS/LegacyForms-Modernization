-- ══════════════════════════════════════════════════════════════════════════
--  LegacyForms stored procedures — the contract both eras speak.
--
--  Why parameterized procedures only: the 2010-era decision that "no string
--  ever reaches the parser" has held this long in production. The modern
--  gateway keeps the same signature discipline so the legacy rules (state
--  machine, optimistic concurrency, audit) survive the UI migration intact.
--  Phase 3 of the migration replaces the procs with EF Core LINQ — the rules
--  are mirrored in ModernForms.Application.FormService and covered by tests.
--
--  Requires MySQL 8 (JSON and JSON_TABLE).
--
--  Error convention: SIGNAL SQLSTATE '45000' with
--  SET MESSAGE_TEXT = 'FORMS:<CODE>' — both gateways parse the code out.
-- ══════════════════════════════════════════════════════════════════════════

USE legacyforms;

DELIMITER //

-- ── usp_GetActiveTemplates ────────────────────────────────────────────────
-- Directory for the chooser in both UIs.
DROP PROCEDURE IF EXISTS usp_GetActiveTemplates//
CREATE PROCEDURE usp_GetActiveTemplates ()
BEGIN
  SELECT t.template_id   AS TemplateId,
         t.template_key  AS TemplateKey,
         t.title         AS Title,
         t.description   AS Description,
         t.field_schema  AS FieldSchema
  FROM form_template t
  WHERE t.is_active = 1
  ORDER BY t.title;
END//

-- ── usp_ListSubmissions ───────────────────────────────────────────────────
-- Parameters:
--   pi_employee_id caller id (owner gate for retail callers)
--   pi_role        'employee' | 'supervisor' | 'admin'
--   pi_status      NULL or one of the status labels
--   pi_pi_page     1-based page number
--   pi_page_size   clamped 1..100 inside
-- Rules:
--   employees only ever see their own submissions — enforced here, not in the UI.
DROP PROCEDURE IF EXISTS usp_ListSubmissions//
CREATE PROCEDURE usp_ListSubmissions (
  IN pi_employee_id INT UNSIGNED,
  IN pi_role        VARCHAR(16),
  IN pi_status      VARCHAR(16),
  IN pi_page        INT,
  IN pi_page_size   INT
)
BEGIN
  DECLARE app_page_size INT UNSIGNED DEFAULT LEAST(GREATEST(COALESCE(pi_page_size, 25), 1), 100);
  DECLARE app_page      INT UNSIGNED DEFAULT GREATEST(COALESCE(pi_page, 1), 1);

  SELECT s.submission_id  AS SubmissionId,
         t.template_key   AS TemplateKey,
         t.title          AS TemplateTitle,
         s.requestor_id   AS RequestorId,
         e.full_name      AS RequestorName,
         s.status         AS Status,
         s.submitted_at   AS SubmittedAt,
         s.closed_at      AS ClosedAt,
         s.decision_note  AS DecisionNote,
         s.version        AS Version
  FROM form_submission s
  JOIN form_template t ON t.template_id = s.template_id
  JOIN employee      e ON e.employee_id = s.requestor_id
  WHERE (pi_status IS NULL OR s.status = pi_status)
    AND (pi_role <> 'employee' OR s.requestor_id = pi_employee_id)
  ORDER BY s.submitted_at DESC, s.submission_id DESC
  LIMIT app_page_size OFFSET (app_page - 1) * app_page_size;
END//

-- ── usp_GetSubmission ─────────────────────────────────────────────────────
-- One call returns three result sets: header, ordered steps, event trail —
-- the shape both the Knockout viewmodel and the Angular detail page hydrate.
-- Access rule enforced: employees may read only their own submissions.
DROP PROCEDURE IF EXISTS usp_GetSubmission//
CREATE PROCEDURE usp_GetSubmission (
  IN pi_submission_id BIGINT UNSIGNED,
  IN pi_employee_id   INT UNSIGNED,
  IN pi_role          VARCHAR(16)
)
BEGIN
  SELECT COUNT(*) INTO @row_present
  FROM form_submission WHERE submission_id = pi_submission_id;

  IF @row_present = 0 THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:SUBMISSION_NOT_FOUND';
  END IF;

  SELECT COUNT(*) INTO @owns_row
  FROM form_submission
  WHERE submission_id = pi_submission_id
    AND requestor_id  = pi_employee_id;

  IF pi_role = 'employee' AND @owns_row = 0 THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:NOT_SUBMISSION_OWNER';
  END IF;

  SELECT s.submission_id  AS SubmissionId,
         t.template_key   AS TemplateKey,
         t.title          AS TemplateTitle,
         t.field_schema   AS FieldSchema,
         s.requestor_id   AS RequestorId,
         e.full_name      AS RequestorName,
         s.status         AS Status,
         s.payload_json   AS Payload,
         s.submitted_at   AS SubmittedAt,
         s.closed_at      AS ClosedAt,
         s.decision_note  AS DecisionNote,
         s.version        AS Version
  FROM form_submission s
  JOIN form_template t ON t.template_id = s.template_id
  JOIN employee      e ON e.employee_id = s.requestor_id
  WHERE s.submission_id = pi_submission_id;

  SELECT a.step_id        AS StepId,
         a.step_sequence  AS StepSequence,
         a.role_required  AS RoleRequired,
         a.decision       AS Decision,
         a.decided_by     AS DecidedBy,
         d.full_name      AS DecidedByName,
         a.decided_at     AS DecidedAt,
         a.note           AS Note
  FROM approval_step a
  LEFT JOIN employee d ON d.employee_id = a.decided_by
  WHERE a.submission_id = pi_submission_id
  ORDER BY a.step_sequence;

  SELECT ev.event_type   AS EventType,
         ev.actor_id     AS ActorId,
         ev.detail       AS Detail,
         ev.occurred_at  AS OccurredAt
  FROM form_event ev
  WHERE ev.submission_id = pi_submission_id
  ORDER BY ev.occurred_at, ev.event_id;
END//

-- ── usp_CreateSubmission ──────────────────────────────────────────────────
-- Creates a draft or direct-submission. Returns the new id as a scalar set.
-- Payload-level validation (required fields) happens in application code in
-- both eras; the proc is the last line of defence for template existence.
DROP PROCEDURE IF EXISTS usp_CreateSubmission//
CREATE PROCEDURE usp_CreateSubmission (
  IN pi_template_id   INT UNSIGNED,
  IN pi_requestor_id  BIGINT UNSIGNED,
  IN pi_payload       JSON,
  IN pi_submit_now    TINYINT(1),
  OUT po_submission_id BIGINT UNSIGNED
)
BEGIN
  IF NOT EXISTS (SELECT 1 FROM form_template
                 WHERE template_id = pi_template_id AND is_active = 1) THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:TEMPLATE_NOT_FOUND';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM employee
                 WHERE employee_id = pi_requestor_id AND active = 1) THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:REQUESTOR_INACTIVE';
  END IF;

  START TRANSACTION;

  INSERT INTO form_submission (template_id, requestor_id, status, payload_json, submitted_at, version)
  VALUES (pi_template_id, pi_requestor_id, IF(pi_submit_now = 1, 'submitted', 'draft'),
          pi_payload, IF(pi_submit_now = 1, NOW(), NULL), 1);

  SET po_submission_id = LAST_INSERT_ID();

  INSERT INTO form_event (submission_id, event_type, actor_id, detail)
  VALUES (po_submission_id, 'created', pi_requestor_id, IF(pi_submit_now = 1, 'submitted', 'draft'));

  IF pi_submit_now = 1 THEN
    CALL usp_SnapshotWorkflow(po_submission_id);
  END IF;

  COMMIT;
END//

-- ── usp_SnapshotWorkflow ──────────────────────────────────────────────────
-- Copies the template workflow from field_schema into approval_step for one
-- submission. Runs inside the caller's transaction (MySQL 8 JSON_TABLE).
DROP PROCEDURE IF EXISTS usp_SnapshotWorkflow//
CREATE PROCEDURE usp_SnapshotWorkflow (IN pi_submission_id BIGINT UNSIGNED)
BEGIN
  INSERT INTO approval_step (submission_id, step_sequence, role_required, decision)
  SELECT pi_submission_id, js.seq, js.role, 'pending'
  FROM JSON_TABLE (
    (SELECT field_schema FROM form_template
     WHERE template_id = (SELECT template_id FROM form_submission WHERE submission_id = pi_submission_id)),
    '$.workflow.steps[*]'
    COLUMNS (seq  INT UNSIGNED PATH '$.sequence',
             role VARCHAR(16) PATH '$.role')
  ) AS js;
END//

-- ── usp_Transition ────────────────────────────────────────────────────────
-- The approval engine. Every state change in both eras flows through here.
--
--   pi_decision  'approved' | 'rejected' | 'withdrawn'
--   pi_version   the caller's seen row version — stale callers lose loudly
--                instead of silently double-deciding.
--
-- Rules (mirrored in ModernForms.Application.FormService for the EF profile):
--   • first pending step is the decision target; earlier steps must be closed
--   • approve closes the step; remaining steps → in_review, none left → approved
--   • reject closes the submission and later pending steps stay as history rows
--   • withdraw only by the requestor while no step has been decided yet
--   • concurrent modification raises FORMS:CONCURRENT_UPDATE
DROP PROCEDURE IF EXISTS usp_Transition//
CREATE PROCEDURE usp_Transition (
  IN pi_submission_id BIGINT UNSIGNED,
  IN pi_actor_id      BIGINT UNSIGNED,
  IN pi_actor_role    VARCHAR(16),
  IN pi_decision      VARCHAR(16),
  IN pi_note          VARCHAR(500),
  IN pi_version       INT UNSIGNED,
  OUT po_new_version  INT UNSIGNED
)
BEGIN
  DECLARE v_db_status   VARCHAR(16)  DEFAULT NULL;
  DECLARE v_requestor   BIGINT UNSIGNED DEFAULT NULL;
  DECLARE v_db_version  INT UNSIGNED    DEFAULT NULL;
  DECLARE v_step_no     SMALLINT UNSIGNED DEFAULT NULL;
  DECLARE v_role_needed VARCHAR(16)     DEFAULT NULL;
  DECLARE v_remaining   INT UNSIGNED    DEFAULT 0;

  -- Fail loudly on stale writes or missing rows; no state is left half-written.
  SELECT status, requestor_id, version
    INTO v_db_status, v_requestor, v_db_version
  FROM form_submission
  WHERE submission_id = pi_submission_id;

  IF v_db_status IS NULL THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:SUBMISSION_NOT_FOUND';
  END IF;

  IF v_db_version <> pi_version THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:CONCURRENT_UPDATE';
  END IF;

  IF v_db_status NOT IN ('submitted', 'in_review') THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:TERMINAL_STATE';
  END IF;

  START TRANSACTION;

  IF pi_decision = 'withdrawn' THEN

    IF pi_actor_id <> v_requestor THEN
      ROLLBACK;
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:NOT_SUBMISSION_OWNER';
    END IF;

    SELECT COUNT(*) INTO v_remaining
    FROM approval_step
    WHERE submission_id = pi_submission_id AND decided_at IS NOT NULL;

    IF v_remaining > 0 THEN
      ROLLBACK;
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:WITHDRAW_TOO_LATE';
    END IF;

    UPDATE form_submission
    SET status = 'withdrawn', closed_at = NOW(), decision_note = pi_note, version = version + 1
    WHERE submission_id = pi_submission_id AND version = pi_version;

    SET po_new_version = pi_version + 1;
    INSERT INTO form_event (submission_id, event_type, actor_id, detail)
    VALUES (pi_submission_id, 'withdrawn', pi_actor_id, pi_note);
    COMMIT;

  ELSEIF pi_decision IN ('approved', 'rejected') THEN

    SELECT step_sequence, role_required INTO v_step_no, v_role_needed
    FROM approval_step
    WHERE submission_id = pi_submission_id AND decision = 'pending'
    ORDER BY step_sequence
    LIMIT 1;

    IF v_step_no IS NULL THEN
      ROLLBACK;
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:NO_PENDING_STEP';
    END IF;

    IF pi_actor_role <> v_role_needed AND pi_actor_role <> 'admin' THEN
      ROLLBACK;
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:ROLE_MISMATCH';
    END IF;

    UPDATE approval_step
    SET decision = pi_decision, decided_by = pi_actor_id, decided_at = NOW(), note = pi_note
    WHERE submission_id = pi_submission_id AND step_sequence = v_step_no;

    IF pi_decision = 'rejected' THEN
      UPDATE form_submission
      SET status = 'rejected', closed_at = NOW(), decision_note = pi_note, version = version + 1
      WHERE submission_id = pi_submission_id AND version = pi_version;
    ELSE
      SELECT COUNT(*) INTO v_remaining
      FROM approval_step
      WHERE submission_id = pi_submission_id AND decision = 'pending';

      IF v_remaining = 0 THEN
        UPDATE form_submission
        SET status = 'approved', closed_at = NOW(), version = version + 1
        WHERE submission_id = pi_submission_id AND version = pi_version;
      ELSE
        UPDATE form_submission
        SET status = 'in_review', version = version + 1
        WHERE submission_id = pi_submission_id AND version = pi_version;
      END IF;
    END IF;

    SET po_new_version = pi_version + 1;
    INSERT INTO form_event (submission_id, event_type, actor_id, detail)
    VALUES (pi_submission_id, CONCAT('step_', pi_decision), pi_actor_id, CONCAT('step ', v_step_no));
    COMMIT;

  ELSE
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'FORMS:INVALID_DECISION';
  END IF;
END//

-- ── usp_WorkloadSummary ───────────────────────────────────────────────────
-- Queue counters for the legacy dashboard widget and the modern queue card.
DROP PROCEDURE IF EXISTS usp_WorkloadSummary//
CREATE PROCEDURE usp_WorkloadSummary ()
BEGIN
  SELECT w.status        AS Status,
         w.role_required AS RoleRequired,
         w.pending_submissions AS PendingSubmissions
  FROM vw_workload w
  ORDER BY w.role_required, w.status;
END//

DELIMITER ;
