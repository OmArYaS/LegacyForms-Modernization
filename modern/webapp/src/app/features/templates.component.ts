import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { FormsService } from '../core/forms.service';
import { AuthService } from '../core/auth.service';
import type { Template } from '../core/models';
import { friendlyProblem } from '../core/api-messages';

/**
 * The template catalogue — the admin-style screen that used to require
 * editing database rows. Admins can also run the schema inspector, the
 * modern face of the database-procedure sanity checks
 * (usp_GetActiveTemplates + the structural rules from FormSchemaRules).
 */
@Component({
  selector: 'app-templates',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="page-header">
      <h1>Form templates</h1>
      <p>The routing catalogue. The schema JSON is contract — it drives the renderer in both eras.</p>
    </header>

    @if (loading()) {
      <div class="loading">Loading templates…</div>
    } @else if (error(); as msg) {
      <div class="global-error">{{ msg }}</div>
    } @else {
      @for (t of templates(); track t.templateId) {
        <div class="card template-card">
          <div class="template-head">
            <div>
              <strong>{{ t.title }}</strong>
              <div class="muted mono">{{ t.templateKey }}</div>
            </div>
            <div>
              @if (isAdmin()) {
                <button class="btn ghost" (click)="inspect(t)" [disabled]="inspecting() === t.templateKey">Run schema inspection</button>
              }
            </div>
          </div>

          @if (t.description) {
            <p class="muted">{{ t.description }}</p>
          }

          @if (inspectResult() && inspectKey() === t.templateKey; as resultBlock) {
            @if (inspectResult()!.length === 0) {
              <div class="notice">Schema inspection passed — the template is renderable and routable.</div>
            } @else {
              <div class="global-error">
                @for (f of inspectResult()!; track f) {
                  <div>{{ f }}</div>
                }
              </div>
            }
          }
        </div>
      }
    }
  `,
})
export class TemplatesComponent {
  private readonly forms = inject(FormsService);
  private readonly auth = inject(AuthService);

  protected readonly templates = signal<Template[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly inspecting = signal<string | null>(null);
  protected readonly inspectKey = signal<string | null>(null);
  protected readonly inspectResult = signal<string[] | null>(null);

  protected readonly isAdmin = () => this.auth.role().includes('admin');

  constructor() {
    void this.load();
  }

  protected async inspect(template: Template): Promise<void> {
    this.inspecting.set(template.templateKey);
    try {
      const findings = await firstValueFrom(this.forms.inspect(template.templateKey));
      this.inspectResult.set(findings);
      this.inspectKey.set(template.templateKey);
    } catch (err) {
      this.error.set(friendlyProblem(err));
    } finally {
      this.inspecting.set(null);
    }
  }

  private async load(): Promise<void> {
    try {
      this.templates.set(await firstValueFrom(this.forms.templates()));
      this.error.set(null);
    } catch (err) {
      this.error.set(friendlyProblem(err));
    } finally {
      this.loading.set(false);
    }
  }
}
