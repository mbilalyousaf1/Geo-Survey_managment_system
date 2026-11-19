import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SessionResponse } from '../api.models';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';

const initialSession: SessionResponse = {
  accessToken: 'synthetic-access-token',
  expiresAtUtc: '2030-01-01T01:00:00Z',
  sessionExpiresAtUtc: '2030-01-01T08:00:00Z',
  sessionPublicId: '11111111-1111-1111-1111-111111111111',
  user: {
    accountPublicId: '22222222-2222-2222-2222-222222222222',
    email: 'admin@example.test',
    scope: 'Organization',
    organizationPublicId: '33333333-3333-3333-3333-333333333333',
    organizationCode: 'test-org',
    organizationName: 'Test organization',
  },
};
class TestChannel {
  static current: TestChannel;
  onmessage: ((event: MessageEvent) => void) | null = null;
  postMessage = vi.fn();
  close = vi.fn();
  constructor() {
    TestChannel.current = this;
  }
}
const tick = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

describe('AuthService and bearer interceptor', () => {
  let auth: AuthService;
  let backend: HttpTestingController;
  let http: HttpClient;
  const navigate = vi.fn().mockResolvedValue(true);

  beforeEach(() => {
    vi.stubGlobal('BroadcastChannel', TestChannel);
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: {
        request: vi.fn(async (_name: string, callback: () => Promise<unknown>) => callback()),
      },
    });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigateByUrl: navigate } },
      ],
    });
    auth = TestBed.inject(AuthService);
    backend = TestBed.inject(HttpTestingController);
    http = TestBed.inject(HttpClient);
  });
  afterEach(() => {
    backend.verify();
    vi.unstubAllGlobals();
    vi.clearAllMocks();
  });

  async function csrf(): Promise<void> {
    const request = backend.expectOne('/api/v1/auth/csrf');
    expect(request.request.headers.has('Authorization')).toBe(false);
    expect(request.request.withCredentials).toBe(true);
    request.flush({ data: { requestToken: 'synthetic-csrf-token' } });
    await tick();
  }
  async function signIn(): Promise<void> {
    const pending = auth.organizationPassword(
      'test-org',
      'admin@example.test',
      'synthetic-password',
    );
    await csrf();
    const request = backend.expectOne('/api/v1/auth/organization/password');
    expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('synthetic-csrf-token');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ data: initialSession });
    await pending;
  }

  it('keeps a password challenge separate from an authenticated session', async () => {
    const pending = auth.platformPassword('platform@example.test', 'synthetic-password');
    await csrf();
    const request = backend.expectOne('/api/v1/auth/platform/password');
    expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('synthetic-csrf-token');
    request.flush({
      data: {
        step: 'Enrollment',
        challengeToken: 'synthetic-challenge',
        expiresAtUtc: '2030-01-01T00:00:00Z',
      },
    });
    await pending;
    expect(auth.challenge()?.step).toBe('Enrollment');
    expect(auth.session()).toBeNull();
    expect(auth.accessToken()).toBeNull();
  });

  it('keeps tokens in memory and never attaches bearer authorization to auth operations', async () => {
    const storage = vi.spyOn(Storage.prototype, 'setItem');
    await signIn();
    expect(auth.session()?.organizationCode).toBe('test-org');
    expect(auth.accessToken()).toBe(initialSession.accessToken);
    const pending = auth.platformPassword('platform@example.test', 'synthetic-password');
    await csrf();
    const request = backend.expectOne('/api/v1/auth/platform/password');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({
      data: {
        step: 'Verification',
        challengeToken: 'synthetic-challenge',
        expiresAtUtc: '2030-01-01T00:00:00Z',
      },
    });
    await pending;
    expect(storage).not.toHaveBeenCalled();
    expect(TestChannel.current.postMessage).toHaveBeenCalledWith({
      type: 'signed-in',
      scope: 'Organization',
      sessionPublicId: initialSession.sessionPublicId,
    });
    storage.mockRestore();
  });

  it('shares one refresh for simultaneous unauthorized resources and retries with the new token', async () => {
    await signIn();
    const first = firstValueFrom(http.get('/api/v1/tenant/first'));
    const second = firstValueFrom(http.get('/api/v1/tenant/second'));
    const requests = backend.match((request) => request.url.startsWith('/api/v1/tenant/'));
    requests.forEach((request) => {
      expect(request.request.headers.get('Authorization')).toBe(
        `Bearer ${initialSession.accessToken}`,
      );
      request.flush({}, { status: 401, statusText: 'Unauthorized' });
    });
    await csrf();
    const refresh = backend.expectOne('/api/v1/auth/organization/refresh');
    expect(refresh.request.body).toEqual({
      expectedSessionPublicId: initialSession.sessionPublicId,
    });
    expect(refresh.request.headers.has('Authorization')).toBe(false);
    refresh.flush({ data: { ...initialSession, accessToken: 'rotated-synthetic-access' } });
    await tick();
    const retries = backend.match((request) => request.url.startsWith('/api/v1/tenant/'));
    expect(retries).toHaveLength(2);
    retries.forEach((request) => {
      expect(request.request.headers.get('Authorization')).toBe('Bearer rotated-synthetic-access');
      request.flush({ ok: true });
    });
    await Promise.all([first, second]);
  });

  it('refuses to silently accept a different session during renewal', async () => {
    await signIn();
    const pending = auth.refresh();
    await csrf();
    backend.expectOne('/api/v1/auth/organization/refresh').flush({
      data: { ...initialSession, sessionPublicId: '44444444-4444-4444-4444-444444444444' },
    });
    expect(await pending).toBe(false);
    expect(auth.session()).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/sign-in');
  });

  it('requires fresh sign-in when browser-wide locks are unavailable', async () => {
    await signIn();
    Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined });
    expect(await auth.refresh()).toBe(false);
    expect(auth.session()).toBeNull();
    backend.expectNone('/api/v1/auth/organization/refresh');
  });

  it('invalidates the current view when another tab changes the account', async () => {
    await signIn();
    TestChannel.current.onmessage?.({
      data: { type: 'signed-in', scope: 'Platform', sessionPublicId: 'another-session' },
    } as MessageEvent);
    expect(auth.accessToken()).toBeNull();
    expect(auth.notice()).toContain('another tab');
    expect(navigate).toHaveBeenCalledWith('/sign-in');
  });

  it('clears memory only after server logout succeeds and informs other tabs without tokens', async () => {
    await signIn();
    const pending = auth.logout();
    await csrf();
    const logout = backend.expectOne('/api/v1/auth/organization/logout');
    expect(logout.request.body).toEqual({
      expectedSessionPublicId: initialSession.sessionPublicId,
    });
    logout.flush(null, { status: 204, statusText: 'No Content' });
    await pending;
    expect(auth.session()).toBeNull();
    expect(TestChannel.current.postMessage).toHaveBeenLastCalledWith({
      type: 'signed-out',
      scope: 'Organization',
      sessionPublicId: initialSession.sessionPublicId,
    });
  });

  it('leaves a newer browser cookie untouched when logout detects a stale tab', async () => {
    await signIn();
    const pending = auth.logout();
    await csrf();
    backend
      .expectOne('/api/v1/auth/organization/logout')
      .flush({}, { status: 409, statusText: 'Conflict' });
    await pending;
    expect(auth.session()).toBeNull();
    expect(auth.notice()).toContain('session changed');
    expect(TestChannel.current.postMessage).not.toHaveBeenCalledWith(
      expect.objectContaining({ type: 'signed-out' }),
    );
  });

  it('does not send tokens to unrelated origins', async () => {
    await signIn();
    const request = firstValueFrom(http.get('https://example.test/resource'));
    const external = backend.expectOne('https://example.test/resource');
    expect(external.request.headers.has('Authorization')).toBe(false);
    external.flush({ ok: true });
    await request;
  });
});
