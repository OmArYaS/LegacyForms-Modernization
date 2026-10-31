import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';

import type {
  CreatedSubmission, DecisionResult, Paged, SubmissionDetail, SubmissionListItem,
  Template, WorkloadRow, ApiProblem,
} from './models';

/** Extracts the stable business code from a ProblemDetails response. */
export function problemOf(error: HttpErrorResponse): ApiProblem {
  return (typeof error.error === 'object' && error.error) ? error.error as ApiProblem : {};
}

/**
 * The one data gateway of the modern SPA. URI-for-URI this is the map from the
 * legacy screen actions (see docs/legacy-inventory.md): the submissions list,
 * the detail/agreement, create, decide and the workload queue.
 */
@Injectable({ providedIn: 'root' })
export class FormsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  templates(): Observable<Template[]> {
    return this.http.get<Template[]>(`${this.baseUrl}/templates`)
      .pipe(catchError(this.toProblem));
  }

  template(templateKey: string): Observable<Template> {
    return this.http.get<Template>(`${this.baseUrl}/templates/${templateKey}`)
      .pipe(catchError(this.toProblem));
  }

  inspect(templateKey: string): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/templates/${templateKey}/inspect`)
      .pipe(catchError(this.toProblem));
  }

  submissions(status: string | null, mine: boolean | null, page = 1, pageSize = 25): Observable<Paged<SubmissionListItem>> {
    const params = new URLSearchParams();
    if (status) params.set('status', status);
    if (mine !== null) params.set('mine', String(mine));
    params.set('page', String(page));
    params.set('pageSize', String(pageSize));
    const qs = params.toString();
    return this.http.get<Paged<SubmissionListItem>>(`${this.baseUrl}/submissions${qs ? `?${qs}` : ''}`)
      .pipe(catchError(this.toProblem));
  }

  submission(id: number | string): Observable<SubmissionDetail> {
    return this.http.get<SubmissionDetail>(`${this.baseUrl}/submissions/${id}`)
      .pipe(catchError(this.toProblem));
  }

  workload(): Observable<WorkloadRow[]> {
    return this.http.get<WorkloadRow[]>(`${this.baseUrl}/submissions/workload`)
      .pipe(catchError(this.toProblem));
  }

  create(templateKey: string, payloadJson: string, submitNow: boolean): Observable<CreatedSubmission> {
    return this.http.post<CreatedSubmission>(`${this.baseUrl}/submissions`, { templateKey, payloadJson, submitNow })
      .pipe(catchError(this.toProblem));
  }

  decide(id: number | string, decision: string, note: string | null, seenVersion: number): Observable<DecisionResult> {
    return this.http.post<DecisionResult>(`${this.baseUrl}/submissions/${id}/decide`, { decision, note, seenVersion })
      .pipe(catchError(this.toProblem));
  }

  private toProblem(err: HttpErrorResponse): Observable<never> {
    const problem = problemOf(err);
    return throwError(() => ({ http: err.status, ...problem }));
  }
}
