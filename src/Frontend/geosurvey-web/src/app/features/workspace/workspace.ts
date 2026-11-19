import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiResponse, TenantSetup } from '../../core/api.models';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http-error';

@Component({ selector: 'app-workspace', templateUrl: './workspace.html' })
export class Workspace implements OnInit {
  private readonly http = inject(HttpClient);
  readonly auth = inject(AuthService);
  readonly setup = signal<TenantSetup | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  async ngOnInit(): Promise<void> {
    await this.load();
  }
  async load(): Promise<void> {
    if (this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    this.setup.set(null);
    try {
      const response = await firstValueFrom(
        this.http.get<ApiResponse<TenantSetup>>('/api/v1/tenant/setup'),
      );
      if (
        response.data.organizationPublicId !== this.auth.session()?.organizationPublicId ||
        response.data.state !== 'Ready' ||
        !response.data.databaseIdentityVerified ||
        !response.data.administratorAccess
      ) {
        this.error.set(
          'Workspace verification could not be completed. Contact your platform administrator.',
        );
        return;
      }
      this.setup.set(response.data);
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.loading.set(false);
    }
  }
}
