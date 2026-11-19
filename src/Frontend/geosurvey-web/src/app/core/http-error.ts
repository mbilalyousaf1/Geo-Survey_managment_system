import { HttpErrorResponse } from '@angular/common/http';

/** Keep provider details and untrusted server strings out of the interface. */
export function errorMessage(
  error: unknown,
  fallback = 'The request could not be completed. Please try again.',
): string {
  if (!(error instanceof HttpErrorResponse)) return fallback;
  if (error.status === 0)
    return 'The server could not be reached. Check your connection and try again.';
  if (error.status === 400 || error.status === 422)
    return 'Check your entries and try again. The request may have expired or already been used.';
  if (error.status === 401)
    return 'The credentials or verification code could not be accepted. Check them and try again.';
  if (error.status === 403)
    return 'You do not have access to this action, or your organization is not available.';
  if (error.status === 404)
    return 'This resource is not available. It may have changed since the page was opened.';
  if (error.status === 409)
    return 'The account or resource has changed. Refresh this page and try again.';
  if (error.status === 429)
    return 'Too many attempts. Wait before trying again. Repeated failures may temporarily block sign-in.';
  if (error.status >= 500)
    return 'The server could not complete this request. Please try again shortly.';
  return fallback;
}
