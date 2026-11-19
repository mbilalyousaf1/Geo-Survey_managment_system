import { signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { NgForm } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { MfaChallenge } from '../../core/api.models';
import { AuthService } from '../../core/auth/auth.service';
import { Mfa } from './mfa';

describe('MFA screen', () => {
  const challenge = signal<MfaChallenge | null>(null);
  const codes = Array.from(
    { length: 10 },
    (_, index) => `${index.toString(16).padStart(4, '0')}-AAAA-BBBB-CCCC-DDDD-EEEE-FFFF-0000`,
  );
  const auth = {
    challenge,
    notice: signal(''),
    enrollment: vi.fn().mockResolvedValue({
      secret: 'SYNTHETICSETUP',
      provisioningUri: 'otpauth://totp/synthetic',
      expiresAtUtc: '2030-01-01T00:00:00Z',
    }),
    confirmEnrollment: vi.fn().mockResolvedValue(codes),
    verify: vi.fn().mockResolvedValue(undefined),
  };
  beforeEach(() => {
    challenge.set({
      step: 'Enrollment',
      challengeToken: 'synthetic-challenge',
      expiresAtUtc: '2030-01-01T00:00:00Z',
    });
    TestBed.configureTestingModule({
      imports: [Mfa],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
  });
  afterEach(() => vi.clearAllMocks());

  it('replaces enrollment secrets with ten recovery codes, then clears them before fresh sign-in', async () => {
    const fixture = TestBed.createComponent(Mfa);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('SYNTHETICSETUP');
    const component = fixture.componentInstance;
    component.code = '123456';
    await component.submit({ invalid: false } as NgForm);
    fixture.detectChanges();
    expect(auth.confirmEnrollment).toHaveBeenCalledWith('123456');
    expect(fixture.nativeElement.querySelectorAll('.recovery-codes li')).toHaveLength(10);
    expect(fixture.nativeElement.textContent).not.toContain('SYNTHETICSETUP');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await component.finish();
    expect(navigate).not.toHaveBeenCalled();
    component.saved = true;
    await component.finish();
    expect(component.recoveryCodes()).toHaveLength(0);
    expect(navigate).toHaveBeenCalledWith('/platform/sign-in');
  });

  it('uses password-verified recovery verification and clears code input after use', async () => {
    challenge.update((value) => (value ? { ...value, step: 'Verification' } : null));
    const fixture = TestBed.createComponent(Mfa);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.code = '123456';
    component.toggleRecovery();
    expect(component.code).toBe('');
    component.code = `  ${codes[0].toLowerCase()}  `;
    await component.submit({ invalid: false } as NgForm);
    expect(auth.verify).toHaveBeenCalledWith(codes[0], true);
    expect(component.code).toBe('');
    expect(auth.enrollment).not.toHaveBeenCalled();
  });

  it('accepts canonical recovery groups with surrounding whitespace and lowercase letters', async () => {
    challenge.update((value) => (value ? { ...value, step: 'Verification' } : null));
    const fixture = TestBed.createComponent(Mfa);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.componentInstance.toggleRecovery();
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('#verification-code') as HTMLInputElement;
    input.value = `  ${codes[0].toLowerCase()}  `;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    const form = fixture.debugElement.query(By.directive(NgForm)).injector.get(NgForm);
    expect(form.valid).toBe(true);
    expect(input.maxLength).toBeGreaterThan(39);
  });

  it('announces only the verification rejection after clearing a consumed recovery code', async () => {
    challenge.update((value) => (value ? { ...value, step: 'Verification' } : null));
    auth.verify.mockRejectedValueOnce(new HttpErrorResponse({ status: 401 }));
    const fixture = TestBed.createComponent(Mfa);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.componentInstance.toggleRecovery();
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('#verification-code') as HTMLInputElement;
    input.value = codes[0];
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.componentInstance.code).toBe('');
    const alerts = fixture.nativeElement.querySelectorAll('[role="alert"]');
    expect(alerts).toHaveLength(1);
    expect(alerts[0].textContent).toContain('could not be accepted');
  });
});
