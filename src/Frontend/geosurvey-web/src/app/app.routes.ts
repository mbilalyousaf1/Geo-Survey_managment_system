import { Routes } from '@angular/router';
import { requireScope } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'sign-in' },
  {
    path: 'platform/sign-in',
    title: 'Platform sign in · GeoSurvey',
    data: { scope: 'Platform' },
    loadComponent: () => import('./features/auth/sign-in').then((m) => m.SignIn),
  },
  {
    path: 'platform/mfa',
    title: 'Verify your identity · GeoSurvey',
    loadComponent: () => import('./features/auth/mfa').then((m) => m.Mfa),
  },
  {
    path: 'platform/organizations',
    title: 'Organizations · GeoSurvey',
    canActivate: [requireScope('Platform')],
    loadComponent: () =>
      import('./features/organizations/organizations').then((m) => m.Organizations),
  },
  {
    path: 'platform/organizations/:publicId',
    title: 'Organization onboarding · GeoSurvey',
    canActivate: [requireScope('Platform')],
    loadComponent: () =>
      import('./features/organizations/organization-detail').then((m) => m.OrganizationDetail),
  },
  {
    path: 'activate',
    title: 'Activate administrator · GeoSurvey',
    loadComponent: () => import('./features/auth/activation').then((m) => m.Activation),
  },
  {
    path: 'sign-in',
    title: 'Organization sign in · GeoSurvey',
    data: { scope: 'Organization' },
    loadComponent: () => import('./features/auth/sign-in').then((m) => m.SignIn),
  },
  {
    path: 'workspace',
    title: 'Organization workspace · GeoSurvey',
    canActivate: [requireScope('Organization')],
    loadComponent: () => import('./features/workspace/workspace').then((m) => m.Workspace),
  },
  { path: '**', redirectTo: 'sign-in' },
];
