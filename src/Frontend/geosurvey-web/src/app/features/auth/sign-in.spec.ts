import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { SignIn } from './sign-in';

describe('sign-in error recovery', () => {
  it('clears rejected credentials and announces one useful error without a second required-fields alert', async () => {
    const signIn = vi.fn().mockRejectedValue(new HttpErrorResponse({ status: 401 }));
    TestBed.configureTestingModule({
      imports: [SignIn],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { data: { scope: 'Organization' }, queryParamMap: convertToParamMap({}) },
          },
        },
        { provide: AuthService, useValue: { organizationPassword: signIn } },
      ],
    });
    const fixture = TestBed.createComponent(SignIn);
    fixture.detectChanges();
    await fixture.whenStable();
    for (const [selector, value] of [
      ['#organization-code', 'test-org'],
      ['#email', 'admin@example.test'],
      ['#password', 'a synthetic rejected password'],
    ]) {
      const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    await fixture.whenStable();
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(signIn).toHaveBeenCalledWith(
      'test-org',
      'admin@example.test',
      'a synthetic rejected password',
    );
    expect(fixture.componentInstance.password).toBe('');
    expect(fixture.componentInstance.email).toBe('admin@example.test');
    const alerts = fixture.nativeElement.querySelectorAll('[role="alert"]');
    expect(alerts).toHaveLength(1);
    expect(alerts[0].textContent).toContain('could not be accepted');
  });
});
