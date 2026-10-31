/** API contract mirrors (camelCase wire format from ASP.NET Core). */

export type FormStatus =
  | 'draft' | 'submitted' | 'in_review'
  | 'approved' | 'rejected' | 'withdrawn';

/** Field schema document — travels verbatim to the renderer, as in the legacy era. */
export interface FieldDef {
  key: string;
  label: string;
  type: 'text' | 'multiline' | 'number' | 'date' | 'select';
  required: boolean;
  options?: string[];
  min?: number | null;
  max?: number | null;
  maxLength?: number | null;
}

export interface WorkflowStepDef {
  sequence: number;
  role: 'supervisor' | 'admin';
}

export interface Template {
  templateId: number;
  templateKey: string;
  title: string;
  description: string | null;
  fieldSchema: string;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface SubmissionListItem {
  submissionId: number;
  templateKey: string;
  templateTitle: string;
  requestorId: number;
  requestorName: string;
  status: FormStatus;
  submittedAt: string | null;
  closedAt: string | null;
  decisionNote: string | null;
  version: number;
}

export interface StepDto {
  stepId: number;
  stepSequence: number;
  roleRequired: string;
  decision: string;
  decidedBy: number | null;
  decidedByName: string | null;
  decidedAt: string | null;
  note: string | null;
}

export interface EventDto {
  eventType: string;
  actorId: number | null;
  detail: string | null;
  occurredAt: string;
}

export interface SubmissionDetail {
  submissionId: number;
  templateKey: string;
  templateTitle: string;
  templateFieldSchema: string;
  requestorId: number;
  requestorName: string;
  status: FormStatus;
  payload: string;
  submittedAt: string | null;
  closedAt: string | null;
  decisionNote: string | null;
  version: number;
  steps: StepDto[];
  events: EventDto[];
}

/** Rendered payload: the JSON string plus the parsed key/value pairs for edit controls. */
export type PayloadValues = Record<string, string | number>;

export interface CreatedSubmission {
  submissionId: number;
  templateKey: string;
  status: FormStatus;
  submittedAt: string | null;
  version: number;
}

export interface DecisionResult {
  newVersion: number;
  status: FormStatus;
}

export interface WorkloadRow {
  status: string;
  roleRequired: string;
  pendingSubmissions: number;
}

/** RFC 9457 ProblemDetails shape the API returns with the stable `code` field. */
export interface ApiProblem {
  title?: string;
  status?: number;
  code?: string;
  detail?: string;
}

/** What the data services actually throw: the ProblemDetails plus the HTTP status. */
export type ApiError = ApiProblem & { http: number };
