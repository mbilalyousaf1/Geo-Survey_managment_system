import { Component, inject, signal } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Scope } from '../../core/api.models';
import { errorMessage } from '../../core/http-error';

@Component({
  selector: 'app-sign-in',
  imports: [FormsModule, RouterLink],
  templateUrl: './sign-in.html',
})
export class SignIn {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly platform = this.route.snapshot.data['scope'] === 'Platform';
  readonly scope: Scope = this.platform ? 'Platform' : 'Organization';
  readonly busy = signal(false);
  readonly error = signal('');
  email = '';
  password = '';
  organizationCode = this.route.snapshot.queryParamMap.get('organization') ?? '';

  async submit(form: NgForm): Promise<void> {
    if (this.busy()) return;
    this.error.set('');
    if (form.invalid) {
      form.control.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    try {
      if (this.platform) {
        await this.auth.platformPassword(this.email.trim(), this.password);
        await this.router.navigateByUrl('/platform/mfa');
      } else {
        await this.auth.organizationPassword(
          this.organizationCode.trim(),
          this.email.trim(),
          this.password,
        );
        await this.router.navigateByUrl('/workspace');
      }
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.password = '';
      this.busy.set(false);
    }
  }
}
