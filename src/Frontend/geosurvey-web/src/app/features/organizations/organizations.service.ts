import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import {
  ApiResponse,
  CreateOrganization,
  Invitation,
  Organization,
  Page,
  Region,
} from '../../core/api.models';

@Injectable({ providedIn: 'root' })
export class OrganizationsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/platform';
  regions(): Promise<Region[]> {
    return this.get('/regions');
  }
  list(pageNumber = 1): Promise<Page<Organization>> {
    return this.get(`/organizations?pageNumber=${pageNumber}&pageSize=25`);
  }
  detail(publicId: string): Promise<Organization> {
    return this.get(`/organizations/${encodeURIComponent(publicId)}`);
  }
  create(input: CreateOrganization): Promise<Organization> {
    return this.post('/organizations', input);
  }
  retry(publicId: string): Promise<Organization> {
    return this.post(`/organizations/${encodeURIComponent(publicId)}/retry`, null);
  }
  invitation(publicId: string): Promise<Invitation> {
    return this.post(
      `/organizations/${encodeURIComponent(publicId)}/administrator-invitation`,
      null,
    );
  }
  private get<T>(path: string): Promise<T> {
    return firstValueFrom(
      this.http.get<ApiResponse<T>>(`${this.base}${path}`).pipe(map((response) => response.data)),
    );
  }
  private post<T>(path: string, input: unknown): Promise<T> {
    return firstValueFrom(
      this.http
        .post<ApiResponse<T>>(`${this.base}${path}`, input)
        .pipe(map((response) => response.data)),
    );
  }
}
