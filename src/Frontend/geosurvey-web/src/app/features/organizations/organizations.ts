import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule, NgForm } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { interval } from 'rxjs';
import { CreateOrganization, Organization, Page, Region } from '../../core/api.models';
import { errorMessage } from '../../core/http-error';
import { OrganizationsService } from './organizations.service';

@Component({
  selector: 'app-organizations',
  imports: [DatePipe, FormsModule, RouterLink],
  templateUrl: './organizations.html',
})
export class Organizations implements OnInit {
  private readonly api = inject(OrganizationsService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly result = signal<Page<Organization> | null>(null);
  readonly regions = signal<Region[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly createError = signal('');
  showCreate = false;
  page = 1;
  draft: CreateOrganization = { code: '', displayName: '', regionKey: '', administratorEmail: '' };

  async ngOnInit(): Promise<void> {
    void this.loadRegions();
    await this.load();
    interval(6000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        void this.load();
      });
  }
  async load(): Promise<void> {
    if (this.loading()) return;
    this.loading.set(true);
    try {
      this.result.set(await this.api.list(this.page));
      this.error.set('');
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.loading.set(false);
    }
  }
  async loadRegions(): Promise<void> {
    try {
      const regions = await this.api.regions();
      this.regions.set(regions);
      if (regions.length === 1) this.draft.regionKey = regions[0].key;
      this.createError.set('');
    } catch (error) {
      this.createError.set(errorMessage(error));
    }
  }
  async create(form: NgForm): Promise<void> {
    if (form.invalid || this.saving()) {
      form.control.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.createError.set('');
    try {
      const organization = await this.api.create({
        ...this.draft,
        code: this.draft.code.trim(),
        displayName: this.draft.displayName.trim(),
        administratorEmail: this.draft.administratorEmail.trim(),
      });
      await this.router.navigate(['/platform/organizations', organization.publicId]);
    } catch (error) {
      this.createError.set(errorMessage(error));
    } finally {
      this.saving.set(false);
    }
  }
  changePage(direction: number): void {
    if (this.loading()) return;
    this.page += direction;
    void this.load();
  }
}
