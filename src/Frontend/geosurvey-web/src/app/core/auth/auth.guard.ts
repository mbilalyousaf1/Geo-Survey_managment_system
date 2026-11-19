import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { Scope } from '../api.models';

export function requireScope(scope: Scope): CanActivateFn {
  return async () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    return (
      (await auth.ensureSession(scope)) ||
      router.parseUrl(scope === 'Platform' ? '/platform/sign-in' : '/sign-in')
    );
  };
}
