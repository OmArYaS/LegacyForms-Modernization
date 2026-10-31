import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { AuthService } from '../core/auth.service';

/** Route shell: primary navigation on the left, routed feature content on the right. */
@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="app-shell">
      <aside class="sidenav">
        <div class="brand"><span class="logo">✺</span> ModernForms</div>

        <nav>
          <a routerLink="/dashboard" routerLinkActive="active">Dashboard</a>
          <a routerLink="/submissions" routerLinkActive="active">Submissions</a>
          <a routerLink="/templates" routerLinkActive="active">Templates</a>
        </nav>

        <div class="user-chip">
          <span>
            <strong>{{ auth.user() }}</strong>
            <div class="muted">{{ auth.role() || '—' }}</div>
          </span>
          <button class="btn ghost" (click)="logout()">Sign out</button>
        </div>
      </aside>

      <main class="content">
        <router-outlet />
      </main>
    </div>
  `,
})
export class LayoutComponent {
  protected readonly auth = inject(AuthService);

  logout(): void {
    this.auth.logout();
  }
}
