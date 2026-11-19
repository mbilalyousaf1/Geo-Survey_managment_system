import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { OrganizationDetail } from './organization-detail';
import { OrganizationsService } from './organizations.service';

describe('organization administrator handoff', () => {
  const token = 'B'.repeat(64);
  const api = {
    detail: vi.fn().mockResolvedValue({
      publicId: '33333333-3333-3333-3333-333333333333',
      code: 'alpha',
      displayName: 'Alpha organization',
      regionKey: 'local-demo',
      state: 'AwaitingAdministrator',
      administratorEmail: 'admin@example.test',
      createdAtUtc: '2030-01-01T00:00:00Z',
      updatedAtUtc: '2030-01-01T00:00:00Z',
      failureCode: null,
    }),
    invitation: vi.fn().mockResolvedValue({
      token,
      expiresAtUtc: '2030-01-02T00:00:00Z',
      organizationCode: 'alpha',
      administratorEmail: 'admin@example.test',
    }),
  };
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [OrganizationDetail],
      providers: [
        provideRouter([]),
        { provide: OrganizationsService, useValue: api },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ publicId: '33333333-3333-3333-3333-333333333333' }),
            },
          },
        },
      ],
    }),
  );
  afterEach(() => vi.clearAllMocks());
  it('uses a fragment-only invitation and clears it when hidden', async () => {
    const fixture = TestBed.createComponent(OrganizationDetail);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.invitation).not.toHaveBeenCalled();
    await fixture.componentInstance.issueInvitation();
    fixture.detectChanges();
    const value = (fixture.nativeElement.querySelector('#activation-link') as HTMLTextAreaElement)
      .value;
    const url = new URL(value);
    expect(url.pathname).toBe('/activate');
    expect(url.search).toBe('');
    expect(url.hash).toBe(`#${token}`);
    fixture.componentInstance.hideInvitation();
    fixture.detectChanges();
    expect(fixture.componentInstance.invitationUrl()).toBe('');
    expect(fixture.nativeElement.querySelector('#activation-link')).toBeNull();
  });
});
