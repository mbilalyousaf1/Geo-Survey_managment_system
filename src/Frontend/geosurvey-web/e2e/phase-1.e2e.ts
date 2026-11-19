import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { totp } from './totp.mjs';

interface Credentials {
  email: string;
  password: string;
  baseUrl: string;
  totpSecret?: string;
}

function persistPrivateAuthenticator(secret: string): void {
  const path = process.env['GEOSURVEY_ACCEPTANCE_CREDENTIALS']!;
  const fixture = JSON.parse(readFileSync(path, 'utf8')) as Credentials;
  writeFileSync(path, JSON.stringify({ ...fixture, totpSecret: secret }, null, 2) + '\n');
}

async function nextAuthenticatorStep(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 30_000 - (Date.now() % 30_000) + 500));
}
interface TestOrganization {
  code: string;
  name: string;
  publicId: string;
  password: string;
  context: BrowserContext;
  page: Page;
}

// The complete multi-user browser scenario shares one IP with a 30/minute
// authentication limiter. Pace real actions instead of weakening that policy.
const authRequests: number[] = [];
function observeAuthentication(page: Page): void {
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname;
    if (path.startsWith('/api/v1/auth/') || path.startsWith('/api/v1/activation/'))
      authRequests.push(Date.now());
  });
}
async function paceAuthentication(reserve = 4): Promise<void> {
  for (;;) {
    const now = Date.now();
    while (authRequests.length && authRequests[0] <= now - 61_000) authRequests.shift();
    if (authRequests.length + reserve <= 30) return;
    await new Promise((resolve) =>
      setTimeout(resolve, Math.min(30_000, authRequests[0] + 61_000 - now)),
    );
  }
}

function credentials(): Credentials {
  const path = process.env['GEOSURVEY_ACCEPTANCE_CREDENTIALS'];
  if (!path) throw new Error('Private acceptance credential file is required');
  const value = JSON.parse(readFileSync(path, 'utf8')) as Credentials;
  const url = new URL(value.baseUrl);
  if (
    url.protocol !== 'https:' ||
    !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname) ||
    !value.email ||
    !value.password
  )
    throw new Error('A local HTTPS disposable acceptance environment is required');
  return { ...value, baseUrl: url.origin };
}

async function platformPassword(page: Page, fixture: Credentials): Promise<void> {
  await paceAuthentication();
  await page.goto(`${fixture.baseUrl}/platform/sign-in`);
  await expect(page.locator('.auth-layout')).toHaveCSS('display', 'grid');
  await page.getByLabel('Email address', { exact: true }).fill(fixture.email);
  await page.getByLabel('Password', { exact: true }).fill(fixture.password);
  await page.getByRole('button', { name: 'Continue securely' }).click();
  await expect(page).toHaveURL(/\/platform\/mfa$/);
}

async function createOrganization(
  page: Page,
  browser: Browser,
  fixture: Credentials,
  suffix: string,
  recipient: string,
): Promise<TestOrganization> {
  const code = `accept-${suffix}`;
  const name = `Acceptance ${suffix}`;
  const password = `Acceptance ${randomBytes(20).toString('hex')} passphrase`;
  await paceAuthentication(8);
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('link', { name: 'Organizations', exact: true })
    .click();
  await expect(page).toHaveURL(/\/platform\/organizations$/);
  await page.getByRole('button', { name: '+ Create organization', exact: true }).click();
  await page.getByLabel('Organization name', { exact: true }).fill(name);
  await page.getByLabel('Organization code', { exact: true }).fill(code);
  await page.getByLabel('Initial administrator email', { exact: true }).fill(recipient);
  await page.getByLabel('Region profile', { exact: true }).selectOption('local-demo');
  await page.getByRole('button', { name: 'Create and begin provisioning' }).click();
  await expect(page).toHaveURL(/\/platform\/organizations\/[a-f0-9-]{36}$/i);
  const publicId = new URL(page.url()).pathname.split('/').at(-1)!;
  await expect(
    page.getByRole('button', { name: 'Issue activation invitation', exact: true }),
  ).toBeVisible({ timeout: 150_000 });
  await page.getByRole('button', { name: 'Issue activation invitation', exact: true }).click();
  const link = await page.getByLabel('Private activation link', { exact: true }).inputValue();
  if (!new URL(link).hash || new URL(link).search)
    throw new Error('Invitation must use a URL fragment');
  const context = await browser.newContext({
    ignoreHTTPSErrors: true,
    viewport: { width: 1280, height: 900 },
  });
  context.on('page', observeAuthentication);
  const recipientPage = await context.newPage();
  await recipientPage.goto(link);
  await expect(recipientPage.getByLabel('Choose a password', { exact: true })).toBeVisible();
  if (new URL(recipientPage.url()).hash) throw new Error('Activation fragment was not cleared');
  await recipientPage.getByLabel('Choose a password', { exact: true }).fill(password);
  await recipientPage.getByLabel('Confirm password', { exact: true }).fill(password);
  await recipientPage
    .getByRole('button', { name: 'Activate administrator account', exact: true })
    .click();
  await recipientPage
    .getByRole('link', { name: 'Continue to organization sign in', exact: true })
    .click();
  await recipientPage.getByLabel('Email address', { exact: true }).fill(recipient);
  await recipientPage.getByLabel('Password', { exact: true }).fill(password);
  await recipientPage.getByRole('button', { name: 'Sign in to workspace', exact: true }).click();
  await expect(recipientPage).toHaveURL(/\/workspace$/);
  await expect(recipientPage.getByRole('heading', { name, exact: true })).toBeVisible();
  await expect(recipientPage.getByRole('status')).toContainText('Workspace verified');
  await expect(recipientPage.locator('dd').filter({ hasText: publicId })).toBeVisible();
  await page.getByRole('button', { name: 'Hide link', exact: true }).click();
  return { code, name, publicId, password, context, page: recipientPage };
}

test('complete real Phase 1 onboarding and isolated workspaces', async ({
  page,
  browser,
  context,
}) => {
  let stage = 'load-private-fixture';
  authRequests.length = 0;
  observeAuthentication(page);
  context.on('page', observeAuthentication);
  let lastPlatformErrorStatus: number | null = null;
  let platformAuthorization: string | undefined;
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/platform/'))
      platformAuthorization = request.headers()['authorization'] ?? platformAuthorization;
  });
  const observe = (observedPage: Page) =>
    observedPage.on('response', (response) => {
      if (new URL(response.url()).pathname.startsWith('/api/v1/auth/') && response.status() >= 400)
        lastPlatformErrorStatus = response.status();
    });
  observe(page);
  context.on('page', observe);
  const contexts: BrowserContext[] = [];
  const passed = (check: string) =>
    test.info().annotations.push({ type: 'acceptance', description: check });
  try {
    const fixture = credentials();
    stage = 'platform-enrollment';
    await platformPassword(page, fixture);
    let secret = fixture.totpSecret;
    let recoveryCodes: string[] = [];
    let enrollmentStep = Math.floor(Date.now() / 30_000);
    if (!secret) {
      secret = (
        await page.getByLabel('Authenticator setup key', { exact: true }).textContent()
      )?.trim();
      if (!secret) throw new Error('Fresh unenrolled bootstrap fixture required');
      await paceAuthentication(2);
      const enrollmentAt = Date.now();
      enrollmentStep = Math.floor(enrollmentAt / 30_000);
      await page
        .getByLabel('Six-digit authenticator code', { exact: true })
        .fill(totp(secret, enrollmentAt));
      await page.getByRole('button', { name: 'Confirm authenticator', exact: true }).click();
      await expect(page.locator('.recovery-codes li')).toHaveCount(10);
      recoveryCodes = await page.locator('.recovery-codes li').allTextContents();
      // Authorized diagnostic persistence stays only in the operator's private fixture.
      // Recovery codes are never written to disk.
      persistPrivateAuthenticator(secret);
      await page
        .getByRole('checkbox', { name: 'I have saved all ten codes in a secure place.' })
        .check();
      await page.getByRole('button', { name: 'Continue to fresh sign-in', exact: true }).click();
      passed('platform-enrollment-and-one-time-codes');
    } else {
      test.info().annotations.push({
        type: 'skipped-check',
        description: 'fresh-enrollment-and-one-time-code-delivery',
      });
      // A resumed fixture may have just used its current step.
      await nextAuthenticatorStep();
      enrollmentStep = Math.floor(Date.now() / 30_000) - 1;
    }

    stage = 'password-and-authenticator-sign-in';
    await platformPassword(page, fixture);
    // Confirmation consumed its counter. Wait for the next real authenticator step.
    if (Math.floor(Date.now() / 30_000) <= enrollmentStep)
      await new Promise((resolve) =>
        setTimeout(resolve, (enrollmentStep + 1) * 30_000 - Date.now() + 500),
      );
    await paceAuthentication(2);
    await page.getByLabel('Six-digit authenticator code', { exact: true }).fill(totp(secret));
    await page.getByRole('button', { name: 'Verify and sign in', exact: true }).click();
    await expect(page).toHaveURL(/\/platform\/organizations$/);
    const refreshCookies = (await context.cookies()).filter((cookie) =>
      cookie.name.includes('Refresh'),
    );
    if (
      !refreshCookies.length ||
      refreshCookies.some(
        (cookie) => !cookie.httpOnly || !cookie.secure || cookie.sameSite !== 'Strict',
      )
    )
      throw new Error('Secure refresh cookie requirements failed');
    if (!(await page.evaluate(() => localStorage.length === 0 && sessionStorage.length === 0)))
      throw new Error('Browser credential persistence detected');
    passed('password-and-authenticator-sign-in');
    passed('secure-cookie-and-memory-token-transport');

    const unique = randomBytes(4).toString('hex');
    const recipient = `recipient-${unique}@example.test`;
    stage = 'first-organization-provisioning-and-activation';
    const alpha = await createOrganization(page, browser, fixture, `${unique}-a`, recipient);
    contexts.push(alpha.context);
    passed('first-organization-provisioning-activation-and-sign-in');
    stage = 'second-organization-provisioning-and-activation';
    const beta = await createOrganization(page, browser, fixture, `${unique}-b`, recipient);
    contexts.push(beta.context);
    passed('second-organization-provisioning-activation-and-sign-in');

    stage = 'same-email-independent-identities';
    if (alpha.publicId === beta.publicId || alpha.password === beta.password)
      throw new Error('Independent organization identities required');
    stage = 'first-workspace-reverification';
    const [workspaceRequest] = await Promise.all([
      alpha.page.waitForRequest(
        (request) => new URL(request.url()).pathname === '/api/v1/tenant/setup',
      ),
      alpha.page.getByRole('button', { name: 'Verify workspace', exact: true }).click(),
    ]);
    await expect(alpha.page.getByRole('heading', { name: alpha.name, exact: true })).toBeVisible();
    await expect(alpha.page.locator('dd').filter({ hasText: alpha.publicId })).toBeVisible();
    if (await alpha.page.locator('dd').filter({ hasText: beta.publicId }).count())
      throw new Error('Cross-organization workspace identity returned');
    stage = 'hostile-http-tenant-selectors-and-account-scopes';
    const organizationAuthorization = workspaceRequest.headers()['authorization'];
    if (!organizationAuthorization || !platformAuthorization)
      throw new Error('Actual signed credentials required for HTTP isolation checks');
    const hostileSetup = await alpha.context.request.get(
      `${fixture.baseUrl}/api/v1/tenant/setup?organizationPublicId=${beta.publicId}&database=untrusted-client-selector`,
      {
        headers: {
          Authorization: organizationAuthorization,
          'X-Tenant-ID': beta.publicId,
          'X-Organization-ID': beta.publicId,
          'X-Tenant-Database': 'untrusted-client-selector',
        },
      },
    );
    if (
      hostileSetup.status() !== 200 ||
      (await hostileSetup.json()).data.organizationPublicId !== alpha.publicId
    )
      throw new Error('Client selectors changed the authorized tenant');
    const platformWorkspace = await context.request.get(`${fixture.baseUrl}/api/v1/tenant/setup`, {
      headers: { Authorization: platformAuthorization },
    });
    const organizationPlatform = await alpha.context.request.get(
      `${fixture.baseUrl}/api/v1/platform/organizations`,
      { headers: { Authorization: organizationAuthorization } },
    );
    if (platformWorkspace.status() !== 403 || organizationPlatform.status() !== 403)
      throw new Error('Platform and organization scope boundary was not enforced');
    passed('hostile-http-tenant-selectors-and-account-scope-denials');
    stage = 'second-organization-logout';
    await paceAuthentication(8);
    await beta.page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await expect(beta.page).toHaveURL(/\/sign-in$/);
    stage = 'cross-organization-password-rejection';
    await beta.page.getByLabel('Organization code', { exact: true }).fill(beta.code);
    await beta.page.getByLabel('Email address', { exact: true }).fill(recipient);
    await beta.page.getByLabel('Password', { exact: true }).fill(alpha.password);
    const [rejectedPassword] = await Promise.all([
      beta.page.waitForResponse(
        (response) =>
          new URL(response.url()).pathname === '/api/v1/auth/organization/password' &&
          response.request().method() === 'POST',
      ),
      beta.page.getByRole('button', { name: 'Sign in to workspace', exact: true }).click(),
    ]);
    if (rejectedPassword.status() !== 401)
      throw new Error('Wrong organization password was not rejected');
    stage = 'cross-organization-password-error-announcement';
    await expect(beta.page.getByRole('alert')).toBeVisible();
    await expect(beta.page).toHaveURL(/\/sign-in$/);
    stage = 'correct-organization-password-sign-in';
    await beta.page.getByLabel('Password', { exact: true }).fill(beta.password);
    await beta.page.getByRole('button', { name: 'Sign in to workspace', exact: true }).click();
    await expect(beta.page.getByRole('heading', { name: beta.name, exact: true })).toBeVisible();
    passed('same-email-independent-passwords-and-workspace-identities');

    stage = 'second-platform-password-entry';
    const recoveryPage = await context.newPage();
    await platformPassword(recoveryPage, fixture);
    stage = 'second-platform-verification-entry';
    await paceAuthentication(2);
    if (recoveryCodes.length) {
      await recoveryPage
        .getByRole('button', { name: 'Use a recovery code instead', exact: true })
        .click();
      await recoveryPage
        .getByLabel('Unused recovery code', { exact: true })
        .fill(recoveryCodes[0].trim());
    } else {
      test.info().annotations.push({
        type: 'skipped-check',
        description: 'password-and-recovery-code-sign-in',
      });
      await nextAuthenticatorStep();
      await recoveryPage
        .getByLabel('Six-digit authenticator code', { exact: true })
        .fill(totp(secret));
    }
    stage = 'second-platform-verification-result';
    await recoveryPage.getByRole('button', { name: 'Verify and sign in', exact: true }).click();
    await expect(recoveryPage).toHaveURL(/\/platform\/organizations$/);
    stage = 'cross-tab-session-change-notification';
    await expect(page).toHaveURL(/\/platform\/sign-in$/);
    if (recoveryCodes.length) passed('password-and-unused-recovery-code-sign-in');
    passed('cross-tab-session-change-detection');

    stage = 'browser-wide-refresh-coordination';
    await paceAuthentication(6);
    const tabOne = await context.newPage();
    const tabTwo = await context.newPage();
    await Promise.all([
      tabOne.goto(`${fixture.baseUrl}/platform/organizations`),
      tabTwo.goto(`${fixture.baseUrl}/platform/organizations`),
    ]);
    await expect(tabOne.getByRole('heading', { name: 'Organizations', exact: true })).toBeVisible();
    await expect(tabTwo.getByRole('heading', { name: 'Organizations', exact: true })).toBeVisible();
    passed('real-cross-tab-single-use-refresh-coordination');

    stage = 'consumed-recovery-code-rejection';
    if (!recoveryCodes.length) {
      test.info().annotations.push({
        type: 'skipped-check',
        description: 'consumed-recovery-code-rejection',
      });
      return;
    }
    await paceAuthentication(8);
    await recoveryPage.getByRole('button', { name: 'Sign out', exact: true }).click();
    await platformPassword(recoveryPage, fixture);
    await recoveryPage
      .getByRole('button', { name: 'Use a recovery code instead', exact: true })
      .click();
    await recoveryPage
      .getByLabel('Unused recovery code', { exact: true })
      .fill(recoveryCodes[0].trim());
    await recoveryPage.getByRole('button', { name: 'Verify and sign in', exact: true }).click();
    await expect(recoveryPage.getByRole('alert')).toBeVisible();
    await expect(recoveryPage).toHaveURL(/\/platform\/mfa$/);
    passed('consumed-recovery-code-rejection');
  } catch (error) {
    test.info().annotations.push({ type: 'failed-stage', description: stage });
    if (lastPlatformErrorStatus)
      test.info().annotations.push({
        type: 'platform-http-error',
        description: String(lastPlatformErrorStatus),
      });
    const message = error instanceof Error ? error.message : '';
    const kind = message.includes('strict mode violation')
      ? 'ambiguous-locator'
      : message.toLowerCase().includes('timeout')
        ? 'timeout'
        : 'assertion-or-request';
    test.info().annotations.push({ type: 'failure-kind', description: kind });
    // Replace errors whose Playwright call logs may contain a typed secret.
    throw new Error(`Phase 1 acceptance failed at ${stage}. Sensitive diagnostics suppressed.`);
  } finally {
    await Promise.all(contexts.map((item) => item.close().catch(() => undefined)));
  }
});
