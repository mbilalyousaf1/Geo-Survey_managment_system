import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AUTH_OPERATION, AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/v1/') || request.context.get(AUTH_OPERATION))
    return next(request);
  const auth = inject(AuthService);
  const token = auth.accessToken();
  const authorized = token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;
  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !token)
        return throwError(() => error);
      return from(auth.refresh()).pipe(
        switchMap((refreshed) => {
          if (!refreshed) return throwError(() => error);
          return next(
            request.clone({ setHeaders: { Authorization: `Bearer ${auth.accessToken()}` } }),
          ).pipe(
            catchError((retryError: unknown) => {
              if (retryError instanceof HttpErrorResponse && retryError.status === 401)
                auth.expire();
              return throwError(() => retryError);
            }),
          );
        }),
      );
    }),
  );
};
