-- ══════════════════════════════════════════════════════════════════════════
--  Demo data — safe to run repeatedly against a fresh schema.
--  Employees mirror the demo profiles of both UIs:
--    usr_amira (employee) · usr_tarek (employee)
--    usr_mona  (supervisor) · usr_ossama (admin)
-- ══════════════════════════════════════════════════════════════════════════

USE legacyforms;

INSERT INTO employee (employee_key, full_name, email, department, role, active) VALUES
  ('usr_amira',  'Amira Hassan', 'amira.hassan@contoso.internal', 'Accounting', 'employee',   1),
  ('usr_tarek',  'Tarek Fahmy',  'tarek.fahmy@contoso.internal',  'Support',    'employee',   1),
  ('usr_mona',   'Mona Sharif',  'mona.sharif@contoso.internal',  'Operations', 'supervisor', 1),
  ('usr_ossama', 'Ossama Nabil', 'ossama.nabil@contoso.internal', 'Finance',    'admin',      1)
ON DUPLICATE KEY UPDATE full_name = VALUES(full_name);

INSERT INTO form_template (template_key, title, description, field_schema, is_active) VALUES
  ('leave-request', 'Leave request',
   'Annual or sick leave — supervisor sign-off required.',
   JSON_OBJECT(
     'fields', JSON_ARRAY(
       JSON_OBJECT('key', 'leaveType',  'label', 'Leave type',  'type', 'select',
                   'options', JSON_ARRAY('Annual', 'Sick', 'Unpaid'), 'required', true),
       JSON_OBJECT('key', 'fromDate',   'label', 'From',        'type', 'date',  'required', true),
       JSON_OBJECT('key', 'toDate',     'label', 'To',          'type', 'date',  'required', true),
       JSON_OBJECT('key', 'reason',     'label', 'Reason',      'type', 'text',  'required', true, 'maxLength', 500)
     ),
     'workflow', JSON_OBJECT('steps', JSON_ARRAY(
       JSON_OBJECT('sequence', 1, 'role', 'supervisor')
     ))
   ), 1),
  ('expense-claim', 'Expense claim',
   'Reimbursement request — finance review after supervisor approval.',
   JSON_OBJECT(
     'fields', JSON_ARRAY(
       JSON_OBJECT('key', 'category', 'label', 'Category', 'type', 'select',
                   'options', JSON_ARRAY('Travel', 'Meals', 'Equipment', 'Other'), 'required', true),
       JSON_OBJECT('key', 'amount',   'label', 'Amount (EGP)', 'type', 'number', 'required', true, 'min', 0),
       JSON_OBJECT('key', 'receiptRef','label', 'Receipt reference', 'type', 'text', 'required', true, 'maxLength', 60),
       JSON_OBJECT('key', 'notes',    'label', 'Notes', 'type', 'text', 'required', false, 'maxLength', 500)
     ),
     'workflow', JSON_OBJECT('steps', JSON_ARRAY(
       JSON_OBJECT('sequence', 1, 'role', 'supervisor'),
       JSON_OBJECT('sequence', 2, 'role', 'admin')
     ))
   ), 1),
  ('equipment-request', 'Equipment request',
   'Hardware or software request — one-step supervisor approval.',
   JSON_OBJECT(
     'fields', JSON_ARRAY(
       JSON_OBJECT('key', 'item',      'label', 'Item',       'type', 'text',   'required', true, 'maxLength', 200),
       JSON_OBJECT('key', 'justification', 'label', 'Business justification', 'type', 'text', 'required', true, 'maxLength', 500)
     ),
     'workflow', JSON_OBJECT('steps', JSON_ARRAY(
       JSON_OBJECT('sequence', 1, 'role', 'supervisor')
     ))
   ), 1)
ON DUPLICATE KEY UPDATE field_schema = VALUES(field_schema), title = VALUES(title);

-- ── Sample submissions (one of each interesting workflow state) ───────────
-- Leave request, fully approved (id 1)
CALL usp_CreateSubmission(1, 1, JSON_OBJECT('leaveType','Annual','fromDate','2026-05-11','toDate','2026-05-15','reason','Family trip'), 1, @sid1);
CALL usp_Transition(@sid1, 3, 'supervisor', 'approved', 'Enjoy — working days covered.', 1, @v1);

-- Expense claim, awaiting supervisor (id 2)
CALL usp_CreateSubmission(2, 2, JSON_OBJECT('category','Travel','amount',1840.50,'receiptRef','TRV-2026-0042','notes','Client visit.'), 1, @sid2);

-- Equipment request, rejected (id 3)
CALL usp_CreateSubmission(3, 2, JSON_OBJECT('item','42" television','justification','To watch training videos in the break room.'), 1, @sid3);
CALL usp_Transition(@sid3, 3, 'supervisor', 'rejected', 'Not business-critical this quarter.', 1, @v3);

-- Leave request still in draft (id 4) — never submitted, no steps snapshotted
CALL usp_CreateSubmission(1, 1, JSON_OBJECT('leaveType','Sick','fromDate','2026-06-01','toDate','2026-06-02','reason','Doctor visit'), 0, @sid4);
