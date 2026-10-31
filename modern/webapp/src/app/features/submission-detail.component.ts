import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { FormsService } from '../core/forms.service';
import { AuthService } from '../core/auth.service';
import type { FieldDef, SubmissionDetail, WorkflowStepDef } from '../core/models';
import { friendlyProblem, statusLabel } from '../core/api-messages';
import { shortDate, statusClass } from '../core/format';

/**
 * The request workspace — the modernized /Submissions/Details screen, plus the
 * decision panel that used to live in the Knockout viewmodel. The schema
 * document travels verbatim from the template and drives the field rendering,
 * so adding a template field needs zero SPA code (same promise as the
 * legacy schema-driven generator).
 */
@Component({
  selector: 'app-submission-detail',
  standalone: true,
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <div class="loading">Loading the request…</div>
    } @else if (error(); as msg) {
      <div class="global-error">{{ msg }}</div>
      <a routerLink="/submissions" class="btn ghost">← Back to the inbox</a>
    } @else if (detail(); as d) {
      <a routerLink="/submissions" class="crumb">← Inbox</a>
      <header class="page-header">
        <h1>{{ d.templateTitle }} <span class="mono muted">(#{{ d.submissionId }})</span></h1>
        <p>
          <span class="badge {{ statusClass(d.status) }}">{{ statusLabel(d.status) }}</span>
          <span class="muted"> · by {{ d.requestorName }}</span>
          <span class="muted"> · version {{ d.version }} @if (d.submittedAt) { · submitted {{ d.submittedAt | date: 'medium' }} }</span>
        </p>
      </header>

      @if (flash(); as msg) {
        <div class="notice">{{ msg }}</div>
      }

      <div class="detail-grid">
        <section class="card">
          <h2>Answers</h2>
          @for (field of fields(); track field.key) {
            <div class="form-row">
              <label [class.required]="field.required">{{ field.label }}</label>
              @switch (field.type) {
                @case ('multiline') {
                  <textarea readonly rows="3">{{ values()[field.key] ?? '' }}</textarea>
                }
                @case ('select') {
                  <span class="badge checking">{{ values()[field.key] ?? '—' }}</span>
                }
                @default {
                  <span>{{ values()[field.key] ?? '—' }}</span>
                }
              }
            </div>
          }
          @if (note(); as n) {
            <div class="form-row">
              <label>Decision note</label>
              <span class="muted">{{ n }}</span>
            </div>
          }
        </section>

        <section class="card">
          <h2>Approval trail</h2>
          <ol class="steps">
            @for (step of d.steps; track step.stepId) {
              <li>
                <div class="step-line">
                  <strong>Step {{ step.stepSequence }}</strong>
                  <span class="badge {{ step.decision }}">{{ step.decision }}</span>
                  <span class="muted">{{ step.roleRequired }} gate</span>
                </div>
                @if (step.decidedByName) {
                  <div class="muted fine-print">
                    decided by <strong>{{ step.decidedByName }}</strong>
                    @if (step.decidedAt) { · {{ step.decidedAt | date: 'medium' }} }
                    @if (step.note) { · “{{ step.note }}” }
                  </div>
                }
              </li>
            }
          </ol>
        </section>

        <section class="card span-2">
          <h2>Audit trail</h2>
          @if (d.events.length === 0) {
            <div class="muted">No events recorded.</div>
          } @else {
            <ul class="events">
              @for (ev of d.events; track $index) {
                <li><span class="mono">{{ ev.eventType }}</span> — {{ ev.detail ? ev.detail + ' · ' : '' }}{{ ev.occurredAt | date: 'medium' }}</li>
              }
            </ul>
          }
        </section>
      </div>

      @if (canDecide()) {
        <section class="card mt">
          <h2>Record a decision</h2>
          <div class="decide-row">
            <input class="input" type="text" placeholder="Optional note for the audit trail…" [value]="note()" (input)="onNote($event)" />
            <button class="btn success" [disabled]="busy()" (click)="decide('approved')">Approve</button>
            <button class="btn danger" [disabled]="busy()" (click)="decide('rejected')">Reject</button>
          </div>
          <p class="muted fine-print">
            Deciding asserts version {{ d.version }} — a stale refresh reports
            <span class="mono">FORMS:CONCURRENT_UPDATE</span> (HTTP 409).
          </p>
        </section>
      }

      @if (canWithdraw()) {
        <section class="card mt">
          <h2>Withdraw the request</h2>
          <div class="decide-row">
            <input class="input" type="text" placeholder="Why are you withdrawing it?" [value]="note()" (input)="onNote($event)" />
            <button class="btn danger" [disabled]="busy()" (click)="decide('withdrawn')">Withdraw</button>
          </div>
        </section>
      }
    }
  `,
})
export class SubmissionDetailComponent {
  /** Routed via withComponentInputBinding — /submissions/:id. */
  readonly id = input.required<string>();

  private readonly forms = inject(FormsService);
  private readonly auth = inject(AuthService);

  protected readonly detail = signal<SubmissionDetail | null>(null);
  protected readonly values = signal<Record<string, string | number>>({});
  protected readonly fields = signal<FieldDef[]>([]);
  protected readonly note = signal('');
  protected readonly busy = signal(false);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly flash = signal<string | null>(null);

  protected shortDate = shortDate;
  protected statusClass = statusClass;
  protected statusLabel = statusLabel;

  protected readonly canDecide = computed(() => {
    const role = this.auth.role();
    return role.includes('supervisor') || role.includes('admin');
  });

  protected readonly canWithdraw = computed(() => {
    const body = this.detail();
    if (!body) return false;
    const status = body.status;
    // Employees withdraw their own pending requests; staff use the decide panel instead.
    return (status === 'submitted' || status === 'in_review')
      && !this.canDecide()
      && body.requestorName === this.currentName();
  });

  protected readonly currentName = computed(() => {
    const token = this.auth.token;
    if (!token) return '';
    try {
      const payload = JSON.parse(atob(token.split('.')[1]!.replaceAll('-', '+').replaceAll('_', '/')));
      return payload['unique_name'] ?? payload['name'] ?? '';
    } catch {
      return '';
    }
  });

  constructor() {
    void this.load();
  }

  protected onNote(event: Event): void {
    this.note.set((event.target as HTMLInputElement).value);
  }

  protected async decide(decision: string): Promise<void> {
    const body = this.detail();
    if (!body) return;

    this.busy.set(true);
    this.flash.set(null);
    this.error.set(null);
    try {
      const result = await firstValueFrom(
        this.forms.decide(body.submissionId, decision, this.note() || null, body.version));
      this.flash.set(`Decision recorded — the request is now ${statusLabel(result.status)} (version ${result.newVersion}).`);
      await this.load(); // reload so the trail and the worker version refresh together
    } catch (err) {
      this.error.set(friendlyProblem(err));
      if (this.detail() && (err as { http?: number }).http === 409) {
        void this.load(); // a concurrent writer raised CONCURRENT_UPDATE — pull the fresh state
      }
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(!this.flash());
    try {
      const body = await firstValueFrom(this.forms.submission(Number(this.id())));

      this.detail.set(body);
      try {
        this.values.set(JSON.parse(body.payload));
      } catch {
        this.values.set({});
      }
      try {
        const schema = JSON.parse(body.templateFieldSchema) as { fields?: FieldDef[]; workflow?: { steps?: WorkflowStepDef[] } };
        this.fields.set(schema.fields ?? []);
      } catch {
        this.fields.set([]);
      }
      this.error.set(null);
    } catch (err) {
      this.error.set(friendlyProblem(err));
    } finally {
      this.loading.set(false);
    }
  }
}
