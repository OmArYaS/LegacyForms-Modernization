import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { AuthService } from './core/auth.service';
import { LoginComponent } from './features/login.component';

/** Entrance gate: anonymous users get the sign-in screen, everything else is routed. */
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, LoginComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (auth.user()) {
      <router-outlet />
    } @else {
      <app-login />
    }
  `,
})
export class AppComponent {
  protected readonly auth = inject(AuthService);
}
