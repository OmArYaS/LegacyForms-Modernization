import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { FormsService } from '../core/forms.service';
import { AuthService } from '../core/auth.service';
import type { WorkloadRow } from '../core/models';
import { statusLabel } from '../core/api-messages';

/**
 * Landing page: the queue counters (the modern face of the legacy
 * vw_workload / usp_WorkloadSummary view) plus quick links into the inbox.
 * Employees see their own; supervisors and admins see the full queues.
 */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="page-header">
      <h1>Welcome back, {{ firstName() }}</h1>
      <p>
        @if (isEmployee()) {
          Your requests at a glance.
        } @else {
          The approval queues at a glance — the counters behind usp_WorkloadSummary.
        }
      </p>
    </header>

    @if (loading()) {
      <div class="loading">Loading workload…</div>
    } @else if (error(); as msg) {
      <div class="global-error">{{ msg }}</div>
    } @else {
      <div class="kpi-grid">
        @for (row of rows(); track row.roleRequired + row.status) {
          <div class="card kpi">
            <div class="label">{{ statusLabel(row.status) }} · {{ row.roleRequired }} queue</div>
            <div class="value">{{ row.pendingSubmissions }}</div>
            <div class="sub">awaiting decision</div>
          </div>
        }
        @if (rows().length === 0) {
          <div class="card kpi">
            <div class="label">Everything decided</div>
            <div class="value">0</div>
            <div class="sub">no pending approvals</div>
          </div>
        }
      </div>

      <div class="mt">
        <a class="btn primary" routerLink="/submissions">Open the submissions inbox →</a>
        @if (!isEmployee()) {
          <span class="muted"> reviews on step 1 & 2 land here first.</span>
        }
      </div>
    }
  `,
})
export class DashboardComponent {
  private readonly forms = inject(FormsService);
  protected readonly auth = inject(AuthService);

  readonly rows = signal<WorkloadRow[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly isEmployee = computed(() => !this.auth.role().includes('supervisor') && !this.auth.role().includes('admin'));

  readonly firstName = computed(() => (this.auth.user() ?? '').charAt(0).toUpperCase() + (this.auth.user() ?? '').slice(1));

  constructor() {
    void this.load();
  }

  protected statusLabel = statusLabel;

  private async load(): Promise<void> {
    try {
      this.rows.set(await firstValueFrom(this.forms.workload()));
      this.error.set(null);
    } catch (err) {
      this.error.set('Could not load the workload view.');
    } finally {
      this.loading.set(false);
    }
  }
}
