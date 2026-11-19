import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { interval } from 'rxjs';
import { Invitation, Organization } from '../../core/api.models';
import { errorMessage } from '../../core/http-error';
import { OrganizationsService } from './organizations.service';

@Component({
  selector: 'app-organization-detail',
  imports: [DatePipe, RouterLink],
  templateUrl: './organization-detail.html',
})
export class OrganizationDetail implements OnInit, OnDestroy {
  private readonly api = inject(OrganizationsService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly publicId = this.route.snapshot.paramMap.get('publicId') ?? '';
  readonly organization = signal<Organization | null>(null);
  readonly invitation = signal<Invitation | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly copied = signal(false);
  readonly invitationUrl = signal('');

  async ngOnInit(): Promise<void> {
    await this.load();
    interval(6000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        void this.load();
      });
  }
  async load(): Promise<void> {
    if (this.loading() || this.busy()) return;
    this.loading.set(true);
    try {
      this.organization.set(await this.api.detail(this.publicId));
      this.error.set('');
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.loading.set(false);
    }
  }
  async retry(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      this.organization.set(await this.api.retry(this.publicId));
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
  async issueInvitation(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.invitation.set(null);
    this.invitationUrl.set('');
    this.copied.set(false);
    try {
      const invitation = await this.api.invitation(this.publicId);
      this.invitation.set(invitation);
      this.invitationUrl.set(
        `${window.location.origin}/activate#${encodeURIComponent(invitation.token)}`,
      );
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
  async copyInvitation(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.invitationUrl());
      this.copied.set(true);
    } catch {
      this.error.set('Copy is unavailable. Select the activation link and copy it manually.');
    }
  }
  hideInvitation(): void {
    this.invitation.set(null);
    this.invitationUrl.set('');
  }
  ngOnDestroy(): void {
    this.hideInvitation();
  }
}
