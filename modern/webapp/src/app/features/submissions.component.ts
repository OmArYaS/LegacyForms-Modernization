import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { FormsService } from '../core/forms.service';
import { AuthService } from '../core/auth.service';
import type { SubmissionListItem } from '../core/models';
import { friendlyProblem, statusLabel } from '../core/api-messages';
import { shortDay, statusClass } from '../core/format';

/** Status filter chips — the exact vocabulary the API accepts (NULL = all). */
const STATUS_FILTERS: Array<{ key: string | null; label: string }> = [
  { key: null, label: 'All' },
  { key: 'draft', label: 'Drafts' },
  { key: 'submitted', label: 'Submitted' },
  { key: 'in_review', label: 'In review' },
  { key: 'approved', label: 'Approved' },
  { key: 'rejected', label: 'Rejected' },
  { key: 'withdrawn', label: 'Withdrawn' },
];

/**
 * The submissions inbox — the direct replacement of the legacy
 * /Submissions/Index screen. Employees are forever scoped to their own rows
 * server-side; supervisors and admins see the shared book.
 */
@Component({
  selector: 'app-submissions',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="page-header">
      <h1>Submissions</h1>
      <p>@if (isEmployee()) {
        Only your own requests are visible — the same rule usp_ListSubmissions enforced.
      } @else {
        The shared decision book. Filter, then open a request to record a decision.
      }</p>
    </header>

    <div class="toolbar">
      <div class="chips">
        @for (f of filters; track f.label) {
          <button class="chip" [class.active]="status() === f.key" (click)="filterBy(f.key)">{{ f.label }}</button>
        }
      </div>
      <label class="/tool-check muted">
        <input type="checkbox" [checked]="onlyMine()" (change)="toggleMine()" /> Only mine
      </label>
    </div>

    @if (loading()) {
      <div class="loading">Loading submissions…</div>
    } @else if (error(); as msg) {
      <div class="global-error">{{ msg }}</div>
    } @else if (items().length === 0) {
      <div class="card empty">Nothing here yet. Start from the <a routerLink="/dashboard">dashboard</a>.</div>
    } @else {
      <div class="table-wrap card">
        <table class="table">
          <thead>
            <tr>
              <th>#</th>
              <th>Form</th>
              <th>Requestor</th>
              <th>Status</th>
              <th>Submitted</th>
              <th style="text-align:right">Version</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            @for (s of items(); track s.submissionId) {
              <tr>
                <td class="mono">{{ s.submissionId }}</td>
                <td>{{ s.templateTitle }}</td>
                <td>{{ s.requestorName }}</td>
                <td><span class="badge {{ statusClass(s.status) }}">{{ statusLabel(s.status) }}</span></td>
                <td>{{ s.submittedAt ? shortDay(s.submittedAt) : '—' }}</td>
                <td class="mono" style="text-align:right">v{{ s.version }}</td>
                <td><a routerLink="/submissions/{{ s.submissionId }}">Open →</a></td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <div class="pager">
        <button class="btn ghost" [disabled]="page() <= 1" (click)="go(page() - 1)">← Prev</button>
        <span class="muted">Page {{ page() }} of {{ totalPages() }}</span>
        <button class="btn ghost" [disabled]="page() >= totalPages()" (click)="go(page() + 1)">Next →</button>
      </div>
    }
  `,
})
export class SubmissionsComponent {
  private readonly forms = inject(FormsService);
  private readonly auth = inject(AuthService);

  protected readonly filters = STATUS_FILTERS;

  protected readonly status = signal<string | null>(null);
  protected readonly onlyMine = signal(false);
  protected readonly page = signal(1);
  protected readonly totalPages = signal(1);
  protected readonly items = signal<SubmissionListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly isEmployee = computed(() => !this.auth.role().includes('supervisor') && !this.auth.role().includes('admin'));

  protected shortDay = shortDay;
  protected statusClass = statusClass;
  protected statusLabel = statusLabel;

  constructor() {
    void this.load();
  }

  protected async filterBy(status: string | null): Promise<void> {
    this.status.set(status);
    this.page.set(1);
    await this.load();
  }

  protected async toggleMine(): Promise<void> {
    this.onlyMine.set(!this.onlyMine());
    this.page.set(1);
    await this.load();
  }

  protected async go(page: number): Promise<void> {
    this.page.set(page);
    await this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const result = await firstValueFrom(
        this.forms.submissions(this.status(), this.isEmployee() ? null : this.onlyMine(), this.page(), 25));
      this.items.set(result.items);
      this.totalPages.set(Math.max(1, result.totalPages));
    } catch (err) {
      this.error.set(friendlyProblem(err));
    } finally {
      this.loading.set(false);
    }
  }
}
