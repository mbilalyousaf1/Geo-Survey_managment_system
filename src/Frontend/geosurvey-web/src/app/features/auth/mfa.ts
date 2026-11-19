import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { EnrollmentMaterial } from '../../core/api.models';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http-error';

@Component({
  selector: 'app-mfa',
  imports: [FormsModule, RouterLink, DatePipe],
  templateUrl: './mfa.html',
})
export class Mfa implements OnInit, OnDestroy {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly material = signal<EnrollmentMaterial | null>(null);
  readonly recoveryCodes = signal<string[]>([]);
  readonly copied = signal(false);
  readonly enrollment = this.auth.challenge()?.step === 'Enrollment';
  readonly recoveryPattern = '\\s*[A-Fa-f0-9]{4}(-[A-Fa-f0-9]{4}){7}\\s*';
  recovery = false;
  code = '';
  saved = false;

  async ngOnInit(): Promise<void> {
    if (!this.auth.challenge()) {
      await this.router.navigateByUrl('/platform/sign-in');
      return;
    }
    if (!this.enrollment) return;
    this.busy.set(true);
    try {
      this.material.set(await this.auth.enrollment());
    } catch (error) {
      this.error.set(
        errorMessage(
          error,
          'Authenticator setup could not be started. Return to sign-in and try again.',
        ),
      );
    } finally {
      this.busy.set(false);
    }
  }

  async submit(form: NgForm): Promise<void> {
    if (this.busy()) return;
    this.error.set('');
    if (form.invalid) {
      form.control.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    try {
      if (this.enrollment) {
        this.recoveryCodes.set(await this.auth.confirmEnrollment(this.code.trim()));
        this.material.set(null);
      } else {
        await this.auth.verify(
          this.recovery ? this.code.trim().toUpperCase() : this.code.trim(),
          this.recovery,
        );
        await this.router.navigateByUrl('/platform/organizations');
      }
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.code = '';
      this.busy.set(false);
    }
  }

  toggleRecovery(): void {
    this.recovery = !this.recovery;
    this.code = '';
    this.error.set('');
  }

  async copyCodes(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.recoveryCodes().join('\n'));
      this.copied.set(true);
    } catch {
      this.error.set(
        'Copy is unavailable. Select the codes and save them in your password manager.',
      );
    }
  }

  async finish(): Promise<void> {
    if (!this.saved) return;
    this.recoveryCodes.set([]);
    this.auth.notice.set(
      'Authenticator setup is complete. Sign in with your password and a fresh authenticator code.',
    );
    await this.router.navigateByUrl('/platform/sign-in');
  }

  ngOnDestroy(): void {
    this.auth.challenge.set(null);
    this.material.set(null);
    this.recoveryCodes.set([]);
    this.code = '';
  }
}
