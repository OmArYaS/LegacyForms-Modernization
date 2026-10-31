import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

/** Attaches the bearer token when present (dev stub or MSAL-sourced in prod wiring). */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).token;
  return token
    ? next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }))
    : next(req);
};
