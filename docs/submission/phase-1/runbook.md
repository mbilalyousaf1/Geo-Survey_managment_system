# Local setup and demonstration

Run from the repository root. Required: .NET SDK from global.json, Node/npm versions from the web package, and Docker Desktop with Linux containers running. The repository's tested desktop environment is Windows/PowerShell; the CI workflow also defines an Ubuntu/pwsh acceptance job, whose hosted execution must be reported separately.

## Fresh reviewer demo

```powershell
dotnet restore GeoSurvey.sln
dotnet build GeoSurvey.sln --configuration Release --no-restore
cd src/Frontend/geosurvey-web
npm ci
cd ../../..
./scripts/Start-Phase1Demo.ps1
```

The last command prints the local HTTPS URL (default https://localhost:5443) and a newly created private directory under the operator's temporary directory. It generates separate HTTPS, signing, and Data Protection certificates/secrets, starts SQL, applies the global migrations and role permissions, then builds/starts the API, provisioning worker, and web host. Certificates/secrets are mounted individually; the API never receives the worker/SA/bootstrap credentials or the full private directory.

Before returning, startup waits for the web-to-API HTTPS health path using the generated certificate as its trust anchor. The bounded probe fails startup if the application does not become ready; container start order alone is not treated as readiness. Browser acceptance also respects the real authentication request limit rather than disabling the policy.

The HTTPS certificate is local-only/self-signed. No system trust store is changed. Accept that local development certificate in the browser or supply an independently trusted local deployment setup; do not disable verification for unrelated hosts. The internal web-to-API proxy verifies the generated certificate.

Create the first Platform Administrator with the printed private directory:

```powershell
dotnet src/Backend/GeoSurvey.LocalDemo/bin/Release/net10.0/GeoSurvey.LocalDemo.dll bootstrap "<private-demo-directory>"
```

This calls the actual operator-only bootstrap command with its restricted connection. Enter email and password at the interactive/masked prompts. No password argument/default password is supported. Bootstrap remains permanently complete after success; it does not replace or reset existing accounts.

## Demonstration sequence

1. Open the printed URL, choose Platform sign-in, and enter the operator-created credentials.
2. Set up the displayed TOTP secret in an authenticator app, submit a current code, and save the ten one-time recovery codes privately. They cannot be redisplayed. Return to password entry and sign in with a fresh code from the next time step; an already accepted step cannot be reused.
3. Create an organization with a unique immutable code, display name, local-demo region, and initial administrator email. Watch its real status move to AwaitingAdministrator after database migration/health checks.
4. Issue its administrator invitation. Copy the displayed link privately; it carries a random token in the URL fragment, not a server query string. Open it in a separate browser context and set a new password. The invitation expires in 24 hours and is single-use.
5. Sign in as that Organization Administrator using organization code, email, and the chosen password. The workspace displays actual tenant identity/schema/authorization checks.
6. Repeat for another organization, reusing the same email with a different password. Both accounts remain independent. Show the correct workspace in each separate browser context. Wrong-organization credentials fail; a Platform session cannot query the tenant workspace. The automated acceptance scenario checks these boundaries and hostile selector headers.
7. Demonstrate logout and, optionally, password plus an unused recovery code for Platform sign-in. Refresh tokens stay HttpOnly; access tokens stay in browser memory. Do not expose credentials, enrollment secrets, recovery codes, or private configuration in screenshots/submission material.

## Automated acceptance environment

Use a fresh directory/ports separate from any manual demo. Synthetic credentials are generated only for this explicitly marked acceptance environment and are saved privately outside source, never printed.

```powershell
./scripts/Start-Phase1Demo.ps1 -Acceptance -HttpsPort 5543 -SqlPort 14433
cd src/Frontend/geosurvey-web
npx playwright install chromium
cd ../../..
./scripts/Test-Phase1Demo.ps1 -DemoRoot "<private-acceptance-directory>"
```

The real browser harness reads the private fixture and drives the running Docker application. It does not intercept API calls to fake success. Credential-bearing traces/video/screenshots are disabled. Its sanitized summary is under src/Frontend/geosurvey-web/test-results/phase-1-browser-summary.json. Use a fresh acceptance environment for a complete unskipped run; diagnostic resume is explicitly identified by the harness and is not full acceptance evidence.

## Validation, restart, and shutdown

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
./scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
cd src/Frontend/geosurvey-web
npm run build
npm test -- --watch=false
npm exec -- prettier --check src angular.json package.json playwright.config.ts e2e tsconfig.json tsconfig.app.json tsconfig.spec.json .prettierrc
cd ../../..
./scripts/Stop-Phase1Demo.ps1 -DemoRoot "<private-demo-directory>"
./scripts/Start-Phase1Demo.ps1 -DemoRoot "<private-demo-directory>" -NoBuild
```

Keep the private directory and its encrypted Data Protection ring with the SQL volume to preserve existing authenticator material. Stop preserves them by default. `-RemoveOwnedVolumes` on the stop command deletes only that configured Compose project's volumes and must be used only when intentionally discarding an owned demo/test database. No global Docker pruning, production cleanup, or recursive private-directory deletion is performed.

The API and worker are not published as direct host ports. Only the web HTTPS port and loopback SQL operator port are published. The API uses a restricted runtime login; the worker's separate management credentials create/migrate tenant databases. If a worker crashes between database creation and attaching its ownership property, it fails closed: verify the database against the reserved organization/profile before controlled repair/retry. Never adopt or delete an unverified existing database.

OpenAPI is available at `<demo-origin>/openapi/v1.json`. Import the collection with a private local environment; do not save/export populated credential variables or response captures into the submission.

## Package for submission

```powershell
./scripts/Package-Phase1Submission.ps1 -PrivateDemoRoot "<private-acceptance-directory>"
```

The archive and source manifest appear under artifacts/phase-1/. Package after checking the validation report; packaging does not execute or prove tests. The optional private root enables an additional scan for that run's generated passwords/TOTP secret without adding the private files to the archive. No commit or push is performed.
