import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ActivationInfo, ActivationResult } from '../../core/api.models';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http-error';

@Component({
  selector: 'app-activation',
  imports: [DatePipe, FormsModule, RouterLink],
  templateUrl: './activation.html',
})
export class Activation implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private token = '';
  readonly info = signal<ActivationInfo | null>(null);
  readonly activated = signal<ActivationResult | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  password = '';
  confirmation = '';

  async ngOnInit(): Promise<void> {
    const fragment = window.location.hash.slice(1);
    // Remove sensitive link material before any request or subsequent navigation.
    window.history.replaceState(
      window.history.state,
      '',
      window.location.pathname + window.location.search,
    );
    try {
      this.token = decodeURIComponent(fragment);
    } catch {
      this.token = '';
    }
    if (!/^[A-F0-9]{64}$/.test(this.token)) {
      this.token = '';
      this.error.set(
        'This activation link is incomplete or invalid. Ask your platform administrator for a new invitation.',
      );
      return;
    }
    this.busy.set(true);
    try {
      this.info.set(
        await this.auth.authPost<ActivationInfo>('/activation/inspect', { token: this.token }),
      );
    } catch {
      this.error.set(
        'This invitation is unavailable. It may have expired, been replaced, or already been used. Ask your platform administrator for a new invitation.',
      );
    } finally {
      this.busy.set(false);
    }
  }

  async submit(form: NgForm): Promise<void> {
    if (this.busy() || !this.info()) return;
    this.error.set('');
    if (form.invalid) {
      form.control.markAllAsTouched();
      return;
    }
    const length = [...this.password.normalize('NFC')].length;
    if (length < 15 || length > 128) {
      this.error.set('Choose a password with 15–128 characters. Spaces are allowed.');
      return;
    }
    if (this.password !== this.confirmation) {
      this.error.set('The passwords do not match. Enter the same password in both fields.');
      return;
    }
    this.busy.set(true);
    try {
      this.activated.set(
        await this.auth.authPost<ActivationResult>('/activation/complete', {
          token: this.token,
          password: this.password,
        }),
      );
      this.token = '';
      this.info.set(null);
    } catch (error) {
      this.error.set(
        errorMessage(
          error,
          'The invitation or password could not be accepted. Check the invitation is current and use a less common password.',
        ),
      );
    } finally {
      this.password = '';
      this.confirmation = '';
      this.busy.set(false);
    }
  }
  ngOnDestroy(): void {
    this.token = '';
    this.password = '';
    this.confirmation = '';
  }
}
