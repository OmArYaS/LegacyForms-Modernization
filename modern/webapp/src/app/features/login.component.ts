import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from '../core/auth.service';
import { friendlyProblem } from '../core/api-messages';

interface DemoUser {
  userName: string;
  displayName: string;
  hint: string;
}

/**
 * Development sign-in screen. The production build replaces this flow with the
 * Azure AD redirect via MSAL — the Auth service contract stays identical
 * (`token` in storage), so only this component is swapped out.
 */
@Component({
  selector: 'app-login',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="login-wrap">
      <div class="card login-card">
        <div class="brand"><span class="logo">✺</span> ModernForms</div>
        <p class="muted">Sign in to the forms & approvals portal.</p>

        <div class="demo-grid">
          @for (u of users; track u.userName) {
            <button class="btn ghost demo-user" (click)="signIn(u)" [disabled]="busy()">
              <span>
                <strong>{{ u.displayName }}</strong>
                <div class="muted demo-hint">{{ u.hint }}</div>
              </span>
              <span class="badge checking">{{ u.userName }}</span>
            </button>
          }
        </div>

        @if (error(); as msg) {
          <div class="global-error mt">{{ msg }}</div>
        }

        <p class="muted fine-print">
          Selecting a profile exchanges the demo user id for a signed JWT carrying the
          employee key (<em>oid</em>) and role claims — the same token contract the
          Azure AD tenant issues in production.
        </p>
      </div>
    </div>
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly users: DemoUser[] = [
    { userName: 'ossama', displayName: 'Ossama Nabil', hint: 'Admin — template governance, final finance approvals' },
    { userName: 'mona', displayName: 'Mona Sharif', hint: 'Supervisor — the decision queue' },
    { userName: 'amira', displayName: 'Amira Hassan', hint: 'Employee — accounting' },
    { userName: 'tarek', displayName: 'Tarek Fahmy', hint: 'Employee — support' },
  ];

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  async signIn(user: DemoUser): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.login(user.userName);
      // A real navigation keeps the guard as the single source of truth for access.
      await this.router.navigateByUrl('/dashboard');
    } catch (err) {
      this.error.set(friendlyProblem(err));
    } finally {
      this.busy.set(false);
    }
  }
}
