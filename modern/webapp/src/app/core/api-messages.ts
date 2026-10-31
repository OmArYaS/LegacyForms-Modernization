import type { ApiError } from './models';

/** Stable ProblemDetails `code` values — the exact FORMS:* vocabulary the legacy procedures SIGNAL(ed). */
const KNOWN_CODES: Record<string, string> = {
  SUBMISSION_NOT_FOUND: 'That submission could not be found.',
  NOT_SUBMISSION_OWNER: 'You can only work with your own submissions.',
  TEMPLATE_NOT_FOUND: 'That form template is not available.',
  REQUESTOR_INACTIVE: 'Your employee record is inactive or unknown.',
  TERMINAL_STATE: 'That submission is already closed.',
  NO_PENDING_STEP: 'No approval step awaits a decision right now.',
  ROLE_MISMATCH: 'That step needs a different approver role.',
  WITHDRAW_TOO_LATE: 'A reviewer already decided — this request can no longer be withdrawn.',
  CONCURRENT_UPDATE: 'The submission changed while you were away. Reload and retry.',
  INVALID_DECISION: 'Decisions are approved, rejected or withdrawn.',
  INVALID_PAYLOAD: 'Some answers miss the form rules — check the highlighted fields.',
  INVALID_STATUS: 'Unknown status filter.',
  UNKNOWN_USER: 'That demo user is not configured.',
  STORE_CONFLICT: 'The database rejected the write — likely a rule constraint. Retry after refreshing.',
  RESOURCE_NOT_FOUND: 'The requested resource could not be found.',
};

/** Maps a thrown service error ({ http, ...problem }) to a user-facing sentence. */
export function friendlyProblem(error: unknown): string {
  const problem = error as Partial<ApiError> | null;
  if (problem && typeof problem.code === 'string' && KNOWN_CODES[problem.code]) {
    return KNOWN_CODES[problem.code]!;
  }
  if (problem?.detail) return problem.detail;
  if (problem?.http === 403) return 'You are not allowed to perform this action.';
  if (problem?.http === 404) return 'The requested resource could not be found.';
  if (problem?.http === 401) return 'Session expired. Please sign in again.';
  if (problem?.http === 409) return 'Conflict — reload and try again.';
  return 'Something went wrong. Please try again.';
}

/** Human-pretty rendering of the lowercase status vocabulary. */
export function statusLabel(status: string): string {
  switch (status) {
    case 'in_review': return 'In review';
    default: return status.charAt(0).toUpperCase() + status.slice(1);
  }
}
