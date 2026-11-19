import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { Workspace } from './workspace';

describe('verified organization workspace', () => {
  const organizationPublicId = '33333333-3333-3333-3333-333333333333';
  const data = {
    organizationPublicId,
    organizationCode: 'alpha',
    organizationName: 'Alpha organization',
    regionKey: 'local-demo',
    state: 'Ready',
    schemaVersion: '1',
    databaseIdentityVerified: true,
    administratorAccess: true,
  };
  let backend: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Workspace],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: { session: signal({ organizationPublicId, email: 'admin@example.test' }) },
        },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });
  afterEach(() => backend.verify());
  it('renders actual identity-marker and grant verification from the API', async () => {
    const fixture = TestBed.createComponent(Workspace);
    fixture.detectChanges();
    backend.expectOne('/api/v1/tenant/setup').flush({ data });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Workspace verified');
    expect(fixture.nativeElement.textContent).toContain('Alpha organization');
    expect(fixture.nativeElement.textContent).toContain(organizationPublicId);
  });
  it('does not show a workspace returned for a different organization', async () => {
    const fixture = TestBed.createComponent(Workspace);
    fixture.detectChanges();
    backend
      .expectOne('/api/v1/tenant/setup')
      .flush({ data: { ...data, organizationPublicId: '44444444-4444-4444-4444-444444444444' } });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'verification could not be completed',
    );
    expect(fixture.nativeElement.textContent).not.toContain('Alpha organization');
    expect(fixture.componentInstance.setup()).toBeNull();
  });
  it('does not claim success when the server cannot verify the tenant grant', async () => {
    const fixture = TestBed.createComponent(Workspace);
    fixture.detectChanges();
    backend
      .expectOne('/api/v1/tenant/setup')
      .flush({ data: { ...data, administratorAccess: false } });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.alert.success')).toBeNull();
    expect(fixture.componentInstance.setup()).toBeNull();
  });
});
