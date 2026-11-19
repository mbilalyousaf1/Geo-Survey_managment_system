import { TestBed } from '@angular/core/testing';
import { NgForm } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Activation } from './activation';

describe('activation screen', () => {
  const token = 'A'.repeat(64);
  const info = {
    organizationCode: 'alpha',
    organizationName: 'Alpha organization',
    administratorEmail: 'recipient@example.test',
    expiresAtUtc: '2030-01-01T00:00:00Z',
  };
  const post = vi.fn();
  beforeEach(() => {
    window.history.replaceState(null, '', `/activate#${token}`);
    post.mockReset().mockImplementation(async (path: string) => {
      expect(window.location.hash).toBe('');
      return path.endsWith('/inspect')
        ? info
        : { organizationCode: 'alpha', administratorEmail: info.administratorEmail };
    });
    TestBed.configureTestingModule({
      imports: [Activation],
      providers: [provideRouter([]), { provide: AuthService, useValue: { authPost: post } }],
    });
  });
  afterEach(() => window.history.replaceState(null, '', '/'));

  it('removes the URL fragment before inspecting the invitation', async () => {
    const fixture = TestBed.createComponent(Activation);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(window.location.hash).toBe('');
    expect(post).toHaveBeenCalledWith('/activation/inspect', { token });
    expect(fixture.nativeElement.textContent).toContain('recipient@example.test');
    expect(fixture.nativeElement.textContent).not.toContain(token);
  });
  it('rejects malformed links without sending their contents to the server', async () => {
    window.history.replaceState(null, '', '/activate#not-an-invitation');
    const fixture = TestBed.createComponent(Activation);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(post).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain('invalid');
    expect(window.location.hash).toBe('');
  });
  it('validates Unicode password length and confirmation, then clears password inputs after activation', async () => {
    const fixture = TestBed.createComponent(Activation);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.password = '😀'.repeat(8);
    component.confirmation = component.password;
    await component.submit({ invalid: false } as NgForm);
    expect(component.error()).toContain('15–128');
    expect(post).toHaveBeenCalledTimes(1);
    component.password = 'A synthetic test passphrase';
    component.confirmation = 'A different passphrase';
    await component.submit({ invalid: false } as NgForm);
    expect(component.error()).toContain('do not match');
    component.confirmation = component.password;
    await component.submit({ invalid: false } as NgForm);
    fixture.detectChanges();
    expect(component.password).toBe('');
    expect(component.confirmation).toBe('');
    expect(fixture.nativeElement.textContent).toContain('ready to sign in');
    expect(fixture.nativeElement.querySelector('input[type="password"]')).toBeNull();
  });
});
