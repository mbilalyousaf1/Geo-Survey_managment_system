export interface ApiResponse<T> {
  data: T;
}
export type Scope = 'Platform' | 'Organization';
export interface UserProfile {
  accountPublicId: string;
  email: string;
  scope: Scope;
  organizationPublicId: string | null;
  organizationCode: string | null;
  organizationName: string | null;
}
export interface SessionResponse {
  accessToken: string;
  expiresAtUtc: string;
  sessionExpiresAtUtc: string;
  sessionPublicId: string;
  user: UserProfile;
}
export interface MfaChallenge {
  step: 'Enrollment' | 'Verification';
  challengeToken: string;
  expiresAtUtc: string;
}
export interface EnrollmentMaterial {
  secret: string;
  provisioningUri: string;
  expiresAtUtc: string;
}
export type OrganizationState =
  'Pending' | 'Provisioning' | 'AwaitingAdministrator' | 'Ready' | 'Failed' | 'Suspended';
export interface Organization {
  publicId: string;
  code: string;
  displayName: string;
  regionKey: string;
  state: OrganizationState;
  administratorEmail: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  failureCode: string | null;
}
export interface Page<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}
export interface Region {
  key: string;
  displayName: string;
}
export interface CreateOrganization {
  code: string;
  displayName: string;
  regionKey: string;
  administratorEmail: string;
}
export interface Invitation {
  token: string;
  expiresAtUtc: string;
  organizationCode: string;
  administratorEmail: string;
}
export interface ActivationInfo {
  organizationCode: string;
  organizationName: string;
  administratorEmail: string;
  expiresAtUtc: string;
}
export interface ActivationResult {
  organizationCode: string;
  administratorEmail: string;
}
export interface TenantSetup {
  organizationPublicId: string;
  organizationCode: string;
  organizationName: string;
  regionKey: string;
  state: OrganizationState;
  schemaVersion: string | number;
  databaseIdentityVerified: boolean;
  administratorAccess: boolean;
}
