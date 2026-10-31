import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface TokenResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
}

/** Local development identity stub; production swaps the Azure AD SSO flow via MSAL (docs/migration-strategy.md, phase 3). */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly user = signal<string | null>(this.readUser());
  readonly role = computed(() => this.firstRole());

  async login(userName: string): Promise<void> {
    const res = await firstValueFrom(this.http.post<TokenResponse>('/api/auth/token', { userName }));
    localStorage.setItem('modernforms.token', res.accessToken);
    localStorage.setItem('modernforms.user', userName);
    this.user.set(userName);
  }

  logout(): void {
    localStorage.removeItem('modernforms.token');
    localStorage.removeItem('modernforms.user');
    this.user.set(null);
  }

  get token(): string | null {
    return localStorage.getItem('modernforms.token');
  }

  private readUser(): string | null {
    return localStorage.getItem('modernforms.user');
  }

  private firstRole(): string {
    const payload = this.decode(this.token);
    const roles = payload?.['roles'];
    return Array.isArray(roles) ? roles.join(', ') : (roles as string) ?? '';
  }

  private decode(token: string | null): Record<string, unknown> | null {
    if (!token) return null;
    try {
      return JSON.parse(atob(token.split('.')[1]!.replaceAll('-', '+').replaceAll('_', '/')));
    } catch {
      return null;
    }
  }
}
