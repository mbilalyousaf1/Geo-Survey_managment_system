import {
  HttpClient,
  HttpContext,
  HttpContextToken,
  HttpErrorResponse,
  HttpHeaders,
} from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom, map } from 'rxjs';
import {
  ApiResponse,
  EnrollmentMaterial,
  MfaChallenge,
  Scope,
  SessionResponse,
} from '../api.models';

export const AUTH_OPERATION = new HttpContextToken<boolean>(() => false);
const API = '/api/v1';
interface SessionEvent {
  type: 'signed-in' | 'signed-out';
  scope: Scope;
  sessionPublicId: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly current = signal<SessionResponse | null>(null);
  readonly session = computed(() => this.current()?.user ?? null);
  readonly challenge = signal<MfaChallenge | null>(null);
  readonly signingOut = signal(false);
  readonly notice = signal('');
  private refreshFlight: Promise<boolean> | null = null;
  private readonly channel =
    typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel('geosurvey-session');

  constructor() {
    inject(DestroyRef).onDestroy(() => this.channel?.close());
    if (this.channel) {
      this.channel.onmessage = ({ data }: MessageEvent<SessionEvent>) => {
        const existing = this.current();
        if (!existing || !data || !['signed-in', 'signed-out'].includes(data.type)) return;
        if (
          (data.type === 'signed-in' && data.sessionPublicId !== existing.sessionPublicId) ||
          (data.type === 'signed-out' && data.sessionPublicId === existing.sessionPublicId)
        ) {
          this.expire('Your browser session changed in another tab. Sign in again to continue.');
        }
      };
    }
  }

  accessToken(): string | null {
    return this.current()?.accessToken ?? null;
  }

  async platformPassword(email: string, password: string): Promise<void> {
    this.challenge.set(null);
    const challenge = await this.authPost<MfaChallenge>('/auth/platform/password', {
      email,
      password,
    });
    this.challenge.set(challenge);
    this.notice.set('');
  }

  enrollment(): Promise<EnrollmentMaterial> {
    return this.authPost('/auth/platform/enrollment', {
      challengeToken: this.requireChallenge().challengeToken,
    });
  }

  async confirmEnrollment(code: string): Promise<string[]> {
    const result = await this.authPost<{ recoveryCodes: string[] }>(
      '/auth/platform/enrollment/confirm',
      {
        challengeToken: this.requireChallenge().challengeToken,
        code,
      },
    );
    this.challenge.set(null);
    return result.recoveryCodes;
  }

  async verify(code: string, recovery: boolean): Promise<void> {
    const challengeToken = this.requireChallenge().challengeToken;
    await this.withBrowserLock(async () => {
      const result = await this.postWithCsrf<SessionResponse>(
        recovery ? '/auth/platform/recovery' : '/auth/platform/verify',
        recovery ? { challengeToken, recoveryCode: code } : { challengeToken, code },
      );
      this.accept(result, 'Platform');
    });
    this.challenge.set(null);
  }

  async organizationPassword(
    organizationCode: string,
    email: string,
    password: string,
  ): Promise<void> {
    await this.withBrowserLock(async () => {
      const result = await this.postWithCsrf<SessionResponse>('/auth/organization/password', {
        organizationCode,
        email,
        password,
      });
      this.accept(result, 'Organization');
    });
  }

  async ensureSession(scope: Scope): Promise<boolean> {
    if (this.current()) return this.current()?.user.scope === scope;
    return this.refresh(scope);
  }

  refresh(scope = this.current()?.user.scope): Promise<boolean> {
    if (!scope) return Promise.resolve(false);
    if (this.refreshFlight) return this.refreshFlight;
    // Strict single-use cookies need a browser-wide lock, not only a tab-local mutex.
    if (!this.hasBrowserLocks()) {
      if (this.current())
        this.expire(
          'Sign in again to renew this session. This browser cannot coordinate secure session renewal.',
        );
      return Promise.resolve(false);
    }
    const previous = this.current();
    this.refreshFlight = this.withBrowserLock(async () => {
      try {
        const result = await this.postWithCsrf<SessionResponse>(
          `/auth/${scope.toLowerCase()}/refresh`,
          previous ? { expectedSessionPublicId: previous.sessionPublicId } : {},
        );
        if (
          result.user.scope !== scope ||
          (previous &&
            (result.sessionPublicId !== previous.sessionPublicId ||
              result.user.accountPublicId !== previous.user.accountPublicId))
        ) {
          this.expire('Your browser account changed. Sign in again to continue.');
          return false;
        }
        this.current.set(result);
        return true;
      } catch {
        if (previous) this.expire('Your session has ended. Sign in again to continue.');
        return false;
      }
    }).finally(() => {
      this.refreshFlight = null;
    });
    return this.refreshFlight;
  }

  async logout(): Promise<void> {
    const existing = this.current();
    if (!existing || this.signingOut()) return;
    this.signingOut.set(true);
    try {
      await this.authPost<void>(`/auth/${existing.user.scope.toLowerCase()}/logout`, {
        expectedSessionPublicId: existing.sessionPublicId,
      });
      this.channel?.postMessage({
        type: 'signed-out',
        scope: existing.user.scope,
        sessionPublicId: existing.sessionPublicId,
      } satisfies SessionEvent);
      this.current.set(null);
      this.challenge.set(null);
      this.notice.set('You have signed out.');
      await this.router.navigateByUrl(this.signInRoute(existing.user.scope));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409)
        this.expire('Your browser session changed. Sign in again to continue.');
      else
        this.notice.set(
          'Sign-out could not be confirmed. Please try again while connected to the server.',
        );
    } finally {
      this.signingOut.set(false);
    }
  }

  authPost<T>(path: string, body: unknown): Promise<T> {
    return this.withBrowserLock(() => this.postWithCsrf<T>(path, body));
  }

  expire(message = 'Your session has ended. Sign in again to continue.'): void {
    const scope = this.current()?.user.scope ?? 'Organization';
    this.current.set(null);
    this.challenge.set(null);
    this.notice.set(message);
    void this.router.navigateByUrl(this.signInRoute(scope));
  }

  private async postWithCsrf<T>(path: string, body: unknown): Promise<T> {
    const context = new HttpContext().set(AUTH_OPERATION, true);
    const csrf = await firstValueFrom(
      this.http.get<ApiResponse<{ requestToken: string }>>(`${API}/auth/csrf`, {
        context,
        withCredentials: true,
      }),
    );
    return firstValueFrom(
      this.http
        .post<ApiResponse<T> | null>(`${API}${path}`, body ?? null, {
          context,
          withCredentials: true,
          headers: new HttpHeaders({ 'X-CSRF-TOKEN': csrf.data.requestToken }),
        })
        .pipe(map((response) => response?.data as T)),
    );
  }

  private accept(result: SessionResponse, scope: Scope): void {
    if (result.user.scope !== scope || !result.accessToken || !result.sessionPublicId)
      throw new Error('Invalid session response');
    this.current.set(result);
    this.notice.set('');
    this.channel?.postMessage({
      type: 'signed-in',
      scope,
      sessionPublicId: result.sessionPublicId,
    } satisfies SessionEvent);
  }

  private requireChallenge(): MfaChallenge {
    const challenge = this.challenge();
    if (!challenge) throw new Error('Start sign-in again');
    return challenge;
  }
  private signInRoute(scope: Scope): string {
    return scope === 'Platform' ? '/platform/sign-in' : '/sign-in';
  }
  private hasBrowserLocks(): boolean {
    return typeof navigator !== 'undefined' && typeof navigator.locks?.request === 'function';
  }
  private async withBrowserLock<T>(operation: () => Promise<T>): Promise<T> {
    return this.hasBrowserLocks()
      ? await navigator.locks.request('geosurvey-browser-session', operation)
      : await operation();
  }
}
