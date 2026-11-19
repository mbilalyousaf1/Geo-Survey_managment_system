import { HttpErrorResponse } from '@angular/common/http';
import { errorMessage } from './http-error';

describe('safe error messages', () => {
  it('does not render provider details or connection strings', () => {
    const message = errorMessage(
      new HttpErrorResponse({
        status: 500,
        error: { detail: 'Server=private-db;Password=secret' },
      }),
    );
    expect(message).toContain('server');
    expect(message).not.toContain('private-db');
    expect(message).not.toContain('secret');
  });
  it('explains rate limits and offline failures', () => {
    expect(errorMessage(new HttpErrorResponse({ status: 429 }))).toContain('Wait');
    expect(errorMessage(new HttpErrorResponse({ status: 0 }))).toContain('connection');
  });
});
