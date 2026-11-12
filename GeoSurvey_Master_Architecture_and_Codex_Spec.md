---
status: Working baseline --- decisions captured from architecture
  mentoring
subtitle: Requirements, architecture decisions, implementation roadmap
  and AI coding instructions
title: GeoSurvey --- Master Software Architecture & Codex Build
  Specification
version: 1.0
---

# GeoSurvey --- Master Software Architecture & Codex Build Specification

## 1. Purpose of this document

This document is the working source of truth for building **GeoSurvey**,
a multi-organization GIS field survey management platform.

The project will be developed with **Codex**. Codex should use this
document as its primary product and architecture brief, but it must not
attempt to generate the entire system in one pass. Work should proceed
in small, reviewable vertical slices, with tests and documentation at
every step.

GeoSurvey manages property/land-parcel surveys. Organizations use the
same application platform, while each organization's operational data is
stored in its **own database**. The platform supports web and mobile
clients, GIS parcel data, field survey workflows, GPS capture,
photographs, assignments, reporting, and audit history.

### 1.1 Product goals

1.  Provide a reusable SaaS application that multiple independent
    organizations can use.
2.  Keep each organization's operational data isolated in its own
    database.
3.  Allow organizations to manage projects, parcels, users, roles,
    permissions, and geographical assignments.
4.  Enable surveyors to complete detailed property surveys at the actual
    parcel location.
5.  Capture GPS coordinates and camera photographs as part of survey
    evidence.
6.  Preserve survey history and parcel geometry history.
7.  Provide interactive GIS maps and scalable parcel loading.
8.  Support online-first work with limited offline draft capability.
9.  Provide reliable background processing, notifications, audit
    history, and reporting.
10. Build the product as a modular monolith first, without premature
    microservices or Kubernetes complexity.

### 1.2 Primary users and roles

The initial business roles are:

-   **Platform Administrator**: creates and provisions organizations,
    assigns their region and manages platform-level operations.
-   **Organization Administrator**: manages users, custom roles,
    permissions, projects, and organization configuration.
-   **Surveyor**: sees current assignments, visits assigned parcels,
    captures GPS and photographs, and creates/submits surveys.
-   **Supervisor**: has oversight of assigned projects/areas and can
    perform permitted supervisory and correction-related actions.

The authorization model must support custom roles and permissions,
multiple roles per user, project-specific assignments, and specific
geographical-area assignments. Do not rely on role names alone for
access decisions.

------------------------------------------------------------------------

## 2. Requirements baseline

The following requirements are captured from the design discussions.
"Confirmed" means the user selected the decision. "Proposed" means it
was recommended as a design detail but not separately approved. "Open"
means Codex must not invent a final product rule.

### 2.1 Product and workflow requirements

  -----------------------------------------------------------------------
  ID                      Requirement             Status
  ----------------------- ----------------------- -----------------------
  REQ-001                 The product is a        Confirmed
                          property/land-parcel    
                          field survey management 
                          system.                 

  REQ-002                 Initial roles include   Confirmed
                          Platform Administrator, 
                          Organization            
                          Administrator,          
                          Surveyor, and           
                          Supervisor.             

  REQ-003                 Survey work is assigned Confirmed
                          by geographical area.   

  REQ-004                 The system starts from  Confirmed
                          pre-existing parcel     
                          datasets.               

  REQ-005                 The initial operating   Confirmed
                          model is online-first,  
                          with limited offline    
                          drafts.                 

  REQ-006                 Submitted surveys are   Confirmed; exact
                          automatically accepted  meaning requires
                          by the configured       clarification before
                          workflow.               production

  REQ-007                 The survey is a         Confirmed
                          detailed property       
                          survey.                 

  REQ-008                 A parcel can have       Confirmed
                          multiple survey records 
                          and historical survey   
                          records must be         
                          preserved.              

  REQ-009                 Geographical areas can  Confirmed
                          be subdivided for       
                          assignment.             

  REQ-010                 Users need an           Confirmed
                          interactive GIS map.    

  REQ-011                 Surveyors capture GPS   Confirmed
                          coordinates while       
                          physically at the       
                          parcel location.        

  REQ-012                 Strict location         Confirmed
                          validation is required. 

  REQ-013                 Survey photographs must Confirmed
                          be captured using the   
                          device camera; gallery  
                          selection is not the    
                          intended workflow.      

  REQ-014                 A survey supports draft Confirmed
                          and final submission    
                          states.                 

  REQ-015                 Corrections must use a  Confirmed
                          traceable amendment     
                          workflow.               

  REQ-016                 The selected correction Confirmed; exact
                          approach is automatic   authorization and
                          correction.             meaning need a business
                                                  rule

  REQ-017                 Users see only their    Confirmed
                          current assignment, not 
                          historical assignments  
                          as active work.         

  REQ-018                 Unfinished drafts are   Confirmed; retention
                          deleted according to    timing and ownership
                          the selected            rules remain open
                          draft-cleanup decision. 

  REQ-019                 Accepted, draft, and    Confirmed
                          not-surveyed progress   
                          metrics are shown       
                          separately.             

  REQ-020                 Dashboards are          Confirmed
                          role-based.             

  REQ-021                 The platform supports   Confirmed
                          multiple organizations. 

  REQ-022                 Each organization has   Confirmed
                          an independent account  
                          and its own operational 
                          database.               

  REQ-023                 Platform Administrator  Confirmed
                          creates organizations.  

  REQ-024                 Organization            Confirmed
                          Administrator manages   
                          their organization's    
                          users.                  

  REQ-025                 Organizations can       Confirmed
                          define custom roles and 
                          permissions.            

  REQ-026                 A user may have         Confirmed
                          multiple roles.         

  REQ-027                 Users can be assigned   Confirmed
                          to specific projects.   

  REQ-028                 Users can be assigned   Confirmed
                          to specific             
                          geographical areas.     
  -----------------------------------------------------------------------

### 2.2 Architecture and technology decisions

  ----------------------------------------------------------------------------
  ID                      Area                    Decision
  ----------------------- ----------------------- ----------------------------
  ADR-001                 Backend architecture    Modular Monolith + Clean
                                                  Architecture

  ADR-002                 Request handling        Hybrid: services for simple
                                                  operations; CQRS/MediatR for
                                                  complex workflows

  ADR-003                 Data access             Hybrid: direct EF Core for
                                                  simple operations; specific
                                                  repositories for complex
                                                  GIS/business queries

  ADR-004                 Validation              Hybrid: Data Annotations for
                                                  simple input validation;
                                                  FluentValidation for complex
                                                  rules

  ADR-005                 Dependency injection    Appropriate Scoped,
                                                  Transient, and Singleton
                                                  lifetimes; DbContext is
                                                  Scoped

  ADR-006                 API responses           Standard success response;
                                                  ProblemDetails for errors

  ADR-007                 Logging                 Serilog + centralized cloud
                                                  monitoring (planned
                                                  Application Insights)

  ADR-008                 API versioning          URL versioning,
                                                  e.g. `/api/v1/...`

  ADR-009                 Testing                 Unit + Integration +
                                                  End-to-End + Performance
                                                  testing

  ADR-010                 Security                JWT, custom RBAC,
                                                  resource-based
                                                  authorization, tenant
                                                  isolation, rate limiting,
                                                  audit logging, input
                                                  validation, security
                                                  monitoring

  ADR-011                 GIS responsibilities    Frontend handles map
                                                  interaction; backend
                                                  validates and performs
                                                  authoritative
                                                  spatial/business
                                                  calculations

  ADR-012                 Background processing   Hybrid Hangfire +
                                                  BackgroundService

  ADR-013                 Web/mobile              REST APIs + SignalR
                          communication           

  ADR-014                 API endpoint convention User initially selected
                                                  action-style naming;
                                                  architect recommended a
                                                  hybrid convention:
                                                  resource-based REST for CRUD
                                                  and explicit action
                                                  endpoints for business
                                                  commands

  ADR-015                 Pagination              Hybrid: offset pagination
                                                  for normal lists; cursor
                                                  pagination for large
                                                  datasets

  ADR-016                 Filtering               Query parameters for
                                                  filters, sorting, search,
                                                  and date ranges; no
                                                  general-purpose dynamic
                                                  query builder in MVP

  ADR-017                 Token lifecycle         Short-lived access token +
                                                  refresh token rotation and
                                                  reuse detection

  ADR-018                 File upload             User selected direct API
                                                  upload. Architect recommends
                                                  streaming and size limits;
                                                  direct-to-object-storage for
                                                  large files is a future
                                                  optimization

  ADR-019                 API documentation       OpenAPI/Swagger + Postman
                                                  collection

  ADR-020                 Angular structure       Hybrid feature-based
                                                  architecture

  ADR-021                 Angular state           Services + RxJS for ordinary
                                                  state; NgRx only where
                                                  complex shared state
                                                  justifies it

  ADR-022                 Web map                 Leaflet

  ADR-023                 Containerization        Docker

  ADR-024                 CI/CD                   Advanced pipeline: build,
                                                  tests, security scan, Docker
                                                  image, staging, approval,
                                                  production

  ADR-025                 Repository              Monorepo

  ADR-026                 Cloud                   Microsoft Azure

  ADR-027                 Secrets                 Azure Key Vault; prefer
                                                  Managed Identity

  ADR-028                 Encryption              TLS in transit,
                                                  database/object-storage
                                                  encryption at rest,
                                                  encrypted backups

  ADR-029                 Audit                   Immutable/tamper-resistant
                                                  audit records

  ADR-030                 Spatial API formats     Native spatial data
                                                  internally; GeoJSON at API
                                                  boundary; WKT/WKB where
                                                  useful

  ADR-031                 CRS                     GPS capture in WGS 84
                                                  (EPSG:4326); preserve source
                                                  CRS for imports; transform
                                                  for suitable
                                                  processing/calculations

  ADR-032                 Geometry validation     Basic validation
                                                  synchronously;
                                                  expensive/large-dataset
                                                  validation in background

  ADR-033                 Parcel geometry history Versioned geometry; preserve
                                                  previous boundaries

  ADR-034                 Map data loading        Hybrid viewport-based
                                                  loading, clustering by zoom,
                                                  and simplified geometries

  ADR-035                 Development method      Vertical slices, feature by
                                                  feature

  ADR-036                 MVP                     Survey MVP: foundation,
                                                  GIS/parcels, assignments,
                                                  map, GPS, property survey,
                                                  camera, submission
  ----------------------------------------------------------------------------

### 2.3 Database design decisions and caveats

  ------------------------------------------------------------------------
  ID                      Decision                 Status / caveat
  ----------------------- ------------------------ -----------------------
  DB-001                  One combined Global      Confirmed
                          Identity + Tenant        
                          Registry database per    
                          control-plane            
                          deployment.              

  DB-002                  Each organization has    Confirmed
                          its own operational      
                          database.                

  DB-003                  Tenant databases are     Confirmed at HLD level
                          placed in regions        
                          according to tenant      
                          hosting/data-residency   
                          configuration.           

  DB-004                  Organization database    Confirmed
                          tables use module-based  
                          SQL schemas such as      
                          `project`, `gis`,        
                          `survey`, `assignment`,  
                          `identity`,              
                          `notification`, `audit`, 
                          and `files`.             

  DB-005                  Hybrid keys: internal    Confirmed
                          integer primary key plus 
                          public GUID identifier.  

  DB-006                  Enforce relationships    Confirmed
                          with SQL foreign keys    
                          and use EF Core          
                          navigation properties.   

  DB-007                  User selected hard       Confirmed, but
                          delete.                  conflicts with
                                                   survey/geometry history
                                                   and immutable audit
                                                   goals; see data
                                                   deletion policy below

  DB-008                  Store timestamps in UTC. Confirmed

  DB-009                  Parcel boundary storage  Confirmed
                          selected SQL Server      
                          `geometry`               
                          (Polygon/MultiPolygon as 
                          appropriate).            

  DB-010                  GPS coordinates selected Confirmed
                          as decimal latitude and  
                          longitude fields.        

  DB-011                  Preserve multiple        Confirmed
                          surveys and historical   
                          survey records per       
                          parcel.                  
  ------------------------------------------------------------------------

**Important spatial design note:** SQL Server `geometry` is planar.
Area/distance calculations must use a suitable projected coordinate
system or an explicitly appropriate geodesic calculation. Do not
calculate accurate ground area by applying planar calculations directly
to longitude/latitude coordinates. The original source CRS and
transformation metadata must be preserved for imported datasets.

### 2.4 Deletion, history, and audit policy --- required reconciliation

Three selected goals must coexist: hard delete, historical survey
records, and immutable audit. They cannot be applied indiscriminately to
the same records.

Use this safe implementation baseline unless the product owner changes
it:

-   Unsubmitted temporary drafts and expired temporary import artifacts
    may be physically deleted under a defined retention policy.
-   Accepted/submitted surveys, survey amendments, parcel geometry
    versions, and audit events must not be physically deleted through
    ordinary application workflows.
-   Operational records that should disappear from normal screens should
    use a lifecycle/status or archival mechanism rather than destructive
    deletion.
-   Any legally required erasure process must be a separately
    authorized, audited, retention-aware administrative process.
-   Audit events must not contain secrets, access tokens, or unnecessary
    sensitive personal data.

This is an architect's reconciliation of the selected requirements, not
a claim that the user explicitly approved every detail. Confirm the
final retention and legal-erasure policy before production.

------------------------------------------------------------------------

## 3. System context and high-level architecture

### 3.1 Client applications

1.  **Angular Web Application**
    -   Platform administration.
    -   Organization administration.
    -   Project and area management.
    -   GIS parcel browsing and management.
    -   Dashboards and reporting.
    -   Survey supervision and correction views.
2.  **.NET MAUI Mobile Application**
    -   Surveyor authentication.
    -   Current assignment list.
    -   Assigned parcel map/list.
    -   GPS capture and accuracy metadata.
    -   Camera-only photo capture.
    -   Detailed survey forms.
    -   Draft and final submission.
    -   Limited offline draft storage in SQLite.
    -   Manual synchronization when connectivity returns.

### 3.2 Backend

Use one ASP.NET Core modular monolith initially. Modules are logically
isolated and communicate through explicit application contracts. Do not
create a distributed microservice architecture for the MVP.

Suggested logical modules:

-   Platform/Organizations
-   Identity
-   Tenant Provisioning and Tenant Resolution
-   Projects
-   GIS/Parcels
-   Assignments
-   Surveys
-   Files
-   Notifications
-   Reporting
-   Audit
-   Import/Background Processing

### 3.3 Multi-tenant database topology

``` text
                       Angular Web
                           |
                       .NET MAUI
                           |
                    ASP.NET Core API
                           |
                  Tenant Resolution Layer
                           |
              +------------+------------+
              |                         |
       Global Control Plane       Tenant Database Resolver
       (Identity + Registry)             |
              |                 +--------+--------+
       Organizations            |        |        |
       Users/Identity         Region A Region B Region C
       Tenant Registry           |        |        |
       DB references          Org DB  Org DB  Org DB
                              Tenant  Tenant  Tenant
                                A       B       C
```

The global database stores platform identity and tenant metadata. Each
tenant's operational database stores that organization's projects,
parcels, surveys, assignments, tenant-specific roles/permissions,
notifications, and audit records.

**Security invariant:** Never trust a tenant ID, organization ID,
database name, or connection string supplied by the client. Resolve the
authenticated user's permitted organization through trusted server-side
identity and tenant registry data. Authorize every operation against the
resolved tenant, project, and geographical assignment.

### 3.4 Tenant provisioning lifecycle

1.  Platform Administrator creates an organization.
2.  Platform validates the organization name, unique slug/key, selected
    region, and plan/configuration.
3.  Provisioning service creates the organization database from a
    versioned schema/migration baseline.
4.  Provisioning service stores the database reference in the global
    tenant registry.
5.  Initial Organization Administrator is invited/created through the
    identity workflow.
6.  Organization setup is marked ready only after migrations and health
    checks succeed.
7.  Provisioning must be idempotent and resumable. Do not expose raw
    connection strings to clients or logs.

Use a provisioning state machine such as `Pending`, `Provisioning`,
`Ready`, `Failed`, and `Suspended`. Exact operational states can be
refined during implementation.

------------------------------------------------------------------------

## 4. Proposed backend solution structure

Start with a maintainable monorepo. Keep modules inside the modular
monolith. The structure below is a proposed baseline; do not create
dozens of empty projects before they are needed.

``` text
GeoSurvey/
├── src/
│   ├── Backend/
│   │   ├── GeoSurvey.Api/
│   │   ├── BuildingBlocks/
│   │   │   ├── Common/
│   │   │   ├── Contracts/
│   │   │   ├── TenantResolution/
│   │   │   ├── Observability/
│   │   │   └── Security/
│   │   ├── Modules/
│   │   │   ├── Organizations/
│   │   │   ├── Identity/
│   │   │   ├── Projects/
│   │   │   ├── GIS/
│   │   │   ├── Assignments/
│   │   │   ├── Surveys/
│   │   │   ├── Files/
│   │   │   ├── Notifications/
│   │   │   ├── Reporting/
│   │   │   └── Audit/
│   │   └── Workers/
│   │       └── GeoSurvey.Worker/
│   ├── Frontend/
│   │   └── geosurvey-web/
│   └── Mobile/
│       └── GeoSurvey.Mobile/
├── tests/
│   ├── Unit/
│   ├── Integration/
│   ├── Architecture/
│   └── EndToEnd/
├── deploy/
│   ├── docker/
│   ├── azure/
│   └── pipelines/
├── docs/
│   ├── architecture/
│   ├── decisions/
│   ├── api/
│   └── operations/
├── scripts/
├── docker-compose.yml
├── README.md
├── AGENTS.md
└── GeoSurvey.sln
```

### 4.1 Module internal organization

A module can begin with folders rather than separate assemblies. Split
into separate projects only when there is a real boundary or build/test
benefit.

``` text
Modules/Surveys/
├── Domain/
│   ├── Entities/
│   ├── ValueObjects/
│   ├── Enums/
│   ├── Events/
│   └── Rules/
├── Application/
│   ├── Commands/
│   ├── Queries/
│   ├── DTOs/
│   ├── Validators/
│   ├── Interfaces/
│   └── Services/
├── Infrastructure/
│   ├── Persistence/
│   ├── Configurations/
│   ├── Repositories/
│   └── Services/
└── Presentation/
    └── Endpoints/
```

### 4.2 Dependency rules

-   Domain must not depend on Application, Infrastructure, API, EF Core,
    or external services.
-   Application depends on Domain and abstractions.
-   Infrastructure implements Application abstractions and depends on
    Domain/Application.
-   API is the composition root and exposes HTTP endpoints.
-   A module must not directly manipulate another module's DbContext or
    internal tables.
-   Cross-module interactions use public application contracts, domain
    events, or integration events.
-   Avoid circular module dependencies.
-   Do not add a generic repository for every entity by default.

------------------------------------------------------------------------

## 5. Frontend architecture

### 5.1 Angular

Use feature-based organization:

``` text
geosurvey-web/src/app/
├── core/
│   ├── auth/
│   ├── guards/
│   ├── interceptors/
│   ├── tenant-context/
│   └── layout/
├── shared/
│   ├── components/
│   ├── directives/
│   ├── pipes/
│   └── utilities/
├── features/
│   ├── organizations/
│   ├── users/
│   ├── projects/
│   ├── gis/
│   ├── parcels/
│   ├── assignments/
│   ├── surveys/
│   ├── notifications/
│   ├── reports/
│   └── audit/
└── app.routes.ts
```

Within a feature, use components, pages, services, models, and state
folders where appropriate.

State management: - Services + RxJS for ordinary server state and local
feature state. - Use NgRx only for genuinely complex shared state, such
as a multi-component GIS workspace or complex survey workflow. - Do not
introduce NgRx everywhere by default. - Use route guards for navigation
UX, but enforce all authorization on the backend.

The user selected Leaflet for maps. PrimeNG was recommended but not
selected; UI component library remains open. Do not assume PrimeNG is
approved.

### 5.2 .NET MAUI

Proposed feature areas:

-   Authentication
-   Assignments
-   Maps
-   GPS
-   Property Survey
-   Camera
-   Drafts
-   Synchronization
-   Notifications
-   Profile

Use SQLite for limited offline drafts. The initial mobile mode is
online-first. Offline support is not full offline operation.

Offline draft rules: - Local drafts are not accepted/final records. -
Preserve client-generated unique IDs to make synchronization
idempotent. - Store pending photos securely and retain them until server
confirmation. - Manual synchronization is selected. - Server validates
current tenant, assignment, permissions, GPS rules, and payload on
sync. - Do not silently overwrite a newer server record. - Provide clear
per-item sync status and retry behavior.

------------------------------------------------------------------------

## 6. Core domain model --- initial conceptual baseline

The following is a conceptual model, not a finalized ERD. Confirm
field-level requirements and relationships before generating production
migrations.

### 6.1 Global control-plane entities

-   `User`
-   `Organization`
-   `Region`
-   `TenantRegistryEntry`
-   `TenantDatabaseReference`
-   Organization-scoped account association (exact schema to be designed;
    not a shared login with organization memberships)
-   `RefreshToken` / `TokenFamily`
-   `PlatformRole` / platform permission structures

**OPEN-001 resolved by the product owner on 2026-11-12:** Use separate
accounts per organization. A person using multiple organizations has
independent account records and credentials for each organization, even
when the same email address is used. Matching email addresses must not
link accounts, share credentials, or grant cross-organization access.

This decision preserves the combined global identity + tenant registry
database selected in DB-001. Global storage describes where identity
records live, not a shared login identity across organizations. The exact
account schema, organization-code rules, account recovery, and
platform-administrator account model require design before implementation.
See [decision record 0003](docs/decisions/0003-separate-organization-accounts.md).

**Organization login approach approved on 2026-11-12:** Organization users
sign in with organization code + email + password on both web and mobile.
The server resolves the code through trusted registry data and validates
credentials for that organization's account. The client-provided code
selects the intended organization; it does not establish authenticated
tenant context, grant access, or supply a database name/connection string.
Subsequent tenant access must remain bound to the authenticated account
and server-side authorization.

See [decision record 0004](docs/decisions/0004-organization-code-login.md)
for the login approach. Exact authentication API/schema and
platform-administrator login remain undecided.

**Organization-code rules approved on 2026-11-12:** The Platform
Administrator chooses the code when creating an organization. Trim
surrounding spaces and convert uppercase letters to lowercase before
validation, storage, uniqueness checking, and login lookup. The normalized
code must be 3-50 characters using only lowercase English letters
(`a-z`), digits (`0-9`), and hyphens. Leading, trailing, and consecutive
hyphens are invalid. Codes are unique across the platform and remain fixed
after organization creation. The organization's display name is separate.
For example, ` ACME-Surveys ` normalizes to `acme-surveys`.

These are approved behavior rules, not implemented database constraints.
Retired/deleted code reuse and reserved codes remain unspecified.
See [decision record 0005](docs/decisions/0005-organization-code-rules.md).

### 6.2 Organization database entities

**Organization and access** - `OrganizationSettings` - `UserProfile` or
tenant membership reference - `Role` - `Permission` - `UserRole` -
`RolePermission`

**Projects and geography** - `Project` - `GeographicalArea` -
`AreaHierarchy` or parent-child area relationship - `ProjectArea` -
`ProjectUserAssignment` - `AreaAssignment` - `Parcel`

**GIS** - `ParcelGeometryVersion` - `GISDataset` - `GISImportJob` -
`GISImportStagingRecord` - `GISImportError`

**Survey** - `Survey` - `SurveyDetail` (split into domain-specific
detail entities after field requirements are known) - `SurveyPhoto` /
`SurveyAttachment` - `SurveyAmendment` - `SurveyGPSCapture` -
`SurveyStatusHistory`

**Platform capabilities** - `Notification` - `AuditEvent` -
`FileMetadata` - `OutboxEvent` - `BackgroundJob` tracking where needed

### 6.3 Key relationships

-   One organization has many projects.
-   One project has many project areas and parcels (subject to the
    selected dataset model).
-   Geographical areas can have parent/child relationships to support
    subdivision.
-   Users can have multiple roles.
-   Roles have many permissions.
-   Users can have project-specific and geographical-area-specific
    assignments.
-   A parcel can have many surveys over time.
-   A parcel can have many geometry versions.
-   A survey can have multiple photographs/attachments and GPS capture
    metadata.
-   A survey amendment references the original survey and records the
    change, actor, timestamp, and reason.
-   Audit events are append-only and reference the actor and affected
    entity where possible.

### 6.4 Identifier strategy

User selected hybrid identifiers: - Internal integer `Id` as the
relational primary key. - Public GUID `PublicId` for external/API
identification.

Use unique constraints on `PublicId`. Do not expose sequential internal
IDs in public APIs. Decide whether GUIDs are generated by the
application or database and use one consistent strategy.

### 6.5 Time strategy

Store timestamps in UTC. Return ISO 8601 timestamps with UTC indication.
Convert to the user/organization display timezone in the clients. Store
the organization's IANA or otherwise well-defined timezone identifier as
configuration; do not infer timezone from server location.

------------------------------------------------------------------------

## 7. Survey lifecycle and business rules

The user selected: - Detailed property survey. - Draft and final
submission. - Automatic acceptance. - Multiple surveys per parcel with
historical records. - Traceable correction workflow. - Automatic
correction option. - Current assignment only. - Separate accepted,
draft, and not-surveyed metrics.

### 7.1 Proposed initial lifecycle

``` text
Not Surveyed
    |
    v
Draft
    |
    v
Submitted
    |
    v
Accepted (automatic acceptance rule)
    |
    v
Amendment / Correction (new traceable record)
```

This is a proposed interpretation. "Automatic acceptance" needs a
precise definition: - Does every valid final submission immediately
become Accepted? - Are there validation failures that block
acceptance? - Does a supervisor have any post-submission review role? -
Does automatic correction mean the system applies a correction
automatically after validation, or an authorized user can apply it
without a separate approval step?

Do not invent these business rules. Implement explicit state transitions
behind a domain service and keep the acceptance policy configurable
until clarified.

### 7.2 History and amendments

-   Never overwrite a previously accepted survey with new values.
-   Preserve prior survey records.
-   A correction should create an amendment/change record linked to the
    original survey.
-   Store who initiated the correction, who/what applied it, when it
    happened, reason, before/after values, and relevant evidence.
-   Use a transaction to persist the amendment and audit event
    consistently.
-   Prevent concurrent updates using a concurrency token (e.g. SQL
    Server `rowversion`) where appropriate.

### 7.3 Progress metrics

Show separate metrics: - Accepted surveys. - Draft surveys. -
Not-surveyed parcels.

Define each metric using documented business predicates and test those
predicates. Avoid counting survey rows when the metric is intended to
count distinct parcels. Decide how parcels with multiple surveys are
counted; for progress, normally count distinct parcels in each mutually
exclusive state based on the latest/current valid survey state.

------------------------------------------------------------------------

## 8. GIS and spatial architecture

### 8.1 Data flow

-   Mobile GPS is captured as decimal latitude and longitude in WGS 84
    (EPSG:4326).
-   Preserve accuracy estimate, capture timestamp, device/source
    metadata, and validation result.
-   Parcel boundary storage uses SQL Server `geometry` with appropriate
    SRID and Polygon/MultiPolygon handling.
-   Imported dataset original CRS must be stored with import metadata.
-   Transform to an appropriate projected CRS for area/distance
    calculations.
-   API exposes GeoJSON for Leaflet.
-   WKT/WKB may be used for internal interchange or specialized
    processing.

### 8.2 Geometry validation

Synchronous validation: - Geometry exists and is non-empty. - Expected
geometry type. - Coordinates/SRID are present and supported. - Basic
validity checks. - Payload size and vertex count limits.

Background validation for large imports: - Self-intersections and
invalid rings. - Duplicate geometries. - Overlap checks where business
rules require them. - Geometry repair candidates (never silently alter
source geometry without recording the operation). - CRS compatibility
and transformation. - Import error report.

### 8.3 Parcel geometry versioning

Each geometry update creates a new version: - Parcel reference. -
Version number. - Geometry. - SRID/CRS metadata. - Effective
timestamp. - Created by. - Change reason/source. - Validation status. -
Current-version indicator or effective range.

Never silently overwrite historical geometry.

### 8.4 Map performance

Use: - Viewport/bounding-box queries. - Zoom-dependent clustering. -
Simplified geometry at low zoom levels. - Detailed geometry at high zoom
levels. - Pagination or feature limits. - Spatial indexes after
measuring query patterns. - Cache only where invalidation and tenant
boundaries are well defined.

Do not load 100,000 parcel geometries into the browser at once.

------------------------------------------------------------------------

## 9. API design baseline

Base route: `/api/v1`.

Use resource-based routes for standard CRUD and explicit action routes
for business commands.

Examples:

``` http
GET    /api/v1/projects
POST   /api/v1/projects
GET    /api/v1/projects/{publicId}
PATCH  /api/v1/projects/{publicId}

GET    /api/v1/parcels?projectId=...&areaId=...&status=...
GET    /api/v1/parcels/map?bbox=...&zoom=...
GET    /api/v1/parcels/{publicId}

POST   /api/v1/parcels/{publicId}/geometry-versions
POST   /api/v1/surveys
PUT    /api/v1/surveys/{publicId}/draft
POST   /api/v1/surveys/{publicId}/submit
POST   /api/v1/surveys/{publicId}/amendments

GET    /api/v1/assignments/current
POST   /api/v1/projects/{projectId}/assignments
```

These are examples, not a finalized API contract. Define
request/response DTOs and authorization per endpoint before
implementation.

### 9.1 HTTP conventions

-   GET for retrieval.
-   POST for creation or explicit commands.
-   PUT for full replacement where appropriate.
-   PATCH for partial updates.
-   DELETE only where deletion policy permits it.
-   Use correct status codes (`200`, `201`, `202`, `204`, `400`, `401`,
    `403`, `404`, `409`, `422`, `429`, `500` as appropriate).
-   Use `ProblemDetails` for errors.
-   Include correlation/trace identifiers.
-   Validate pagination limits and filter allowlists.
-   Do not accept arbitrary SQL, arbitrary EF property names, or
    unrestricted dynamic expressions from clients.

### 9.2 Pagination

Offset example: `GET /api/v1/projects?pageNumber=1&pageSize=25`

Cursor example: `GET /api/v1/parcels?cursor=...&pageSize=500`

Cursor must be opaque and stable. Large GIS/map queries should be
viewport-oriented rather than relying only on page numbers.

### 9.3 Authentication and token security

-   Short-lived access token.
-   Refresh token rotation.
-   Detect refresh-token reuse and revoke the affected token family.
-   Store refresh tokens securely (hash at rest where applicable).
-   Angular should prefer a secure HttpOnly, Secure, SameSite cookie
    strategy where compatible with the deployment and CSRF protections;
    do not put long-lived refresh tokens in localStorage.
-   MAUI should use platform secure storage.
-   Use HTTPS only.
-   Do not log tokens or credentials.

### 9.4 SignalR

Use SignalR for real-time notifications and progress events, not as a
replacement for REST persistence.

-   Authenticate hub connections.
-   Resolve tenant context server-side.
-   Authorize group membership server-side.
-   Never allow clients to join arbitrary tenant/project groups by
    supplying an ID without authorization.
-   Use REST/persistent notification inbox as the source of truth.
-   Support reconnect and polling fallback as already selected at HLD
    level.

------------------------------------------------------------------------

## 10. File and object storage

The user selected direct API upload. Implement the first version with: -
Streaming request bodies; avoid loading large files fully into memory. -
Maximum file sizes and allowed content types. - Server-side content
validation; do not trust file extensions. - Malware scanning/quarantine
where available. - Randomized storage keys; never use client filenames
as storage paths. - Store file metadata in the tenant database. - Use
tenant-scoped storage paths/containers and authorization on every
download. - Store photographs and GIS datasets in object storage, not as
large binary columns in SQL Server. - Persist upload state and clean up
incomplete uploads. - Keep a future option for direct-to-Blob upload for
large GIS datasets.

Example metadata: - `PublicId` - `Tenant/Organization reference` -
`RelatedEntityType` - `RelatedEntityPublicId` - `StorageKey` -
`OriginalFileName` (metadata only, safely encoded) - `ContentType` -
`SizeBytes` - `Checksum` - `UploadedBy` - `UploadedAtUtc` -
`ScanStatus` - `RetentionState`

------------------------------------------------------------------------

## 11. Background processing and reliability

Use Hangfire for durable scheduled/retryable jobs and BackgroundService
for lightweight continuous processing.

Potential jobs: - GIS import validation. - GIS dataset promotion. -
Report generation. - Photo processing/thumbnail creation. - Notification
fan-out. - Cleanup of expired drafts and incomplete uploads. - Outbox
event dispatch.

Reliability rules: - Jobs must be idempotent. - Use bounded retries with
backoff. - Separate transient failures from permanent validation
failures. - Record job status, attempt count, error summary, and tenant
reference. - Ensure tenant-specific database resolution in each job. -
Do not let one tenant's heavy jobs starve every other tenant. - Use
priority queues with fairness/quotas as required by the HLD decision. -
Avoid logging sensitive payloads. - Use an Outbox pattern when a
database transaction must reliably publish an event after commit.

------------------------------------------------------------------------

## 12. Security and authorization

### 12.1 Authorization layers

Every request must pass: 1. Authentication. 2. Organization/tenant
resolution. 3. Organization membership validation. 4. Permission check.
5. Project assignment check. 6. Geographical-area scope check. 7.
Resource-level access check.

A user's multiple roles should aggregate permissions according to a
documented policy. Start with additive permissions, but explicit
deny/override semantics must not be invented; keep the first version
simple and deterministic.

### 12.2 Tenant isolation invariants

-   Tenant database connection comes only from trusted registry data.
-   No client-supplied connection string or database name.
-   No cross-tenant EF context reuse.
-   Every background job has an explicit tenant reference and resolves
    the correct database.
-   Cache keys include tenant identity.
-   SignalR groups are tenant-scoped and authorized.
-   Object storage paths and downloads are tenant-scoped.
-   Logs include safe tenant/correlation context but no secrets.
-   Integration tests must prove that Tenant A cannot read or modify
    Tenant B data.

### 12.3 Secrets and encryption

-   Azure Key Vault for production secrets.
-   Managed Identity for service-to-service access where possible.
-   TLS for network traffic.
-   Encryption at rest for SQL, Blob Storage, and backups.
-   No secrets in source control or Docker images.
-   Use development user-secrets or local environment configuration for
    local development.

### 12.4 Immutable audit

Audit events should be append-only for application identities: -
Actor. - Tenant. - Action. - Entity type and public identifier. -
Timestamp UTC. - Correlation ID. - Before/after values where
justified. - Reason/command metadata.

Protect audit storage with restricted database permissions and, where
required, immutable retention storage. "Immutable" must be enforced by
permissions/retention controls, not merely by hiding an edit button in
the UI.

------------------------------------------------------------------------

## 13. Observability and operations

-   Serilog structured logging.
-   Azure Application Insights / Azure Monitor for traces, metrics, and
    exceptions.
-   Correlation ID across HTTP requests, background jobs, and SignalR
    events.
-   Health checks for API, database, object storage, and job processing.
-   Dashboards for API latency, error rates, job failures, queue depth,
    database connection failures, and tenant provisioning failures.
-   Alert on security-relevant events and repeated tenant resolution
    failures.
-   Never log credentials, access tokens, full sensitive survey
    payloads, or unnecessary precise location history.

------------------------------------------------------------------------

## 14. Deployment architecture

Target cloud: Microsoft Azure.

Suggested deployment components: - Azure Container Registry for
images. - Azure Container Apps or an appropriate managed container
hosting service for API/worker containers. - Azure SQL Database for
global control-plane and tenant databases. - Azure Blob Storage for
photos and GIS files. - Azure Key Vault for secrets. - Azure
Monitor/Application Insights for monitoring. - Azure DevOps or GitHub
Actions for CI/CD.

The exact Azure hosting product, regional topology, SQL elastic-pool
placement, and cost model must be validated against scale, residency,
availability, and budget before production. Do not treat the
architecture brief as a final cloud bill of materials.

### 14.1 Environments

-   Local development.
-   Automated test.
-   Staging.
-   Production.

Use separate secrets, storage, databases, and service identities per
environment. Never use production data in local development without an
approved anonymization/security process.

### 14.2 CI/CD pipeline

1.  Pull request validation.
2.  Restore dependencies.
3.  Build.
4.  Unit tests.
5.  Integration tests.
6.  Architecture tests.
7.  Static/security/dependency scans.
8.  Build Docker images.
9.  Publish images to registry.
10. Deploy to staging.
11. Smoke/E2E tests.
12. Manual production approval.
13. Deploy production.
14. Verify health and monitor rollback criteria.

Database migrations must be reviewed and applied in a controlled
deployment step. Avoid having every application replica independently
run migrations at startup.

------------------------------------------------------------------------

## 15. Testing strategy

Required test levels:

### Unit tests

-   Domain rules.
-   Survey state transitions.
-   Permission evaluation.
-   Geometry validation rules.
-   Progress metric calculations.
-   Token/refresh-token rules.

### Integration tests

-   EF Core mappings and constraints.
-   SQL Server spatial behavior.
-   Tenant database resolution.
-   API authentication and authorization.
-   File upload/download authorization.
-   Outbox and background jobs.
-   Transaction and concurrency behavior.

### End-to-End tests

-   Platform admin creates an organization.
-   Organization admin creates users/roles.
-   Project and areas are created.
-   Parcels are imported and displayed.
-   Surveyor receives current assignment.
-   Surveyor captures GPS and camera photo.
-   Survey is saved as draft and submitted.
-   Accepted/draft/not-surveyed metrics update correctly.
-   Amendment creates history and audit events.

### Security tests

-   Cross-tenant data access attempts.
-   Unauthorized project/area access.
-   Refresh-token reuse.
-   File path/IDOR attempts.
-   SignalR group authorization.
-   Rate limiting.
-   Malicious/oversized file uploads.

### Performance tests

-   Parcel viewport query under realistic load.
-   Map rendering with large parcel counts.
-   Concurrent survey submissions.
-   Background import queue throughput.
-   Tenant fairness and database resource contention.

------------------------------------------------------------------------

## 16. MVP implementation roadmap

Develop vertical slices. Each slice must include persistence, backend,
frontend/mobile where applicable, authorization, validation, tests, and
documentation.

### Phase 0 --- Repository and engineering foundation

Deliverables: - Monorepo. - Solution and project skeleton. - Coding
conventions. - Docker local development. - CI build/test pipeline. -
Health checks and structured logging. - Architecture decision records. -
No premature business features.

**Exit criteria:** A clean checkout can build and run locally with
documented commands.

### Phase 1 --- Platform identity and organization provisioning

Deliverables: - Global control-plane database. - Organization
registry. - Platform administrator authentication/authorization. -
Organization creation and provisioning workflow. - Tenant database
reference resolver. - Database migration/provisioning automation. -
Organization administrator onboarding.

**Exit criteria:** Platform admin can create a test organization;
provisioning creates an isolated tenant database; requests resolve the
correct tenant safely.

### Phase 2 --- Organization users, roles, and permissions

Deliverables: - Organization user management. - Custom roles and
permissions. - Multiple roles per user. - Permission evaluation. -
Project/area scope authorization foundations. - Angular role-aware
navigation (backend remains authoritative).

**Exit criteria:** An organization admin can manage users and
permissions without accessing another tenant.

### Phase 3 --- Project and geographical area management

Deliverables: - Project CRUD. - Geographical hierarchy and
subdivision. - Project-user assignments. - Area assignments. - Current
assignment views. - Authorization tests.

**Exit criteria:** Authorized managers can assign users to project areas
and surveyors can see only current permitted assignments.

### Phase 4 --- GIS dataset and parcel management

Deliverables: - Dataset upload. - Import job tracking. - Staging and
validation pipeline. - Parcel records and geometry. - CRS metadata. -
Geometry versioning. - Spatial indexes and viewport API. - Leaflet
parcel display.

**Exit criteria:** A test GIS dataset can be imported, validated,
queried by map viewport, and displayed without loading the entire
dataset.

### Phase 5 --- Field survey workflow

Deliverables: - Detailed survey form. - Draft save. - Final
submission. - Automatic acceptance policy (after its exact rule is
confirmed). - Multiple surveys per parcel. - Survey history. - GPS
capture and strict location validation. - Camera-only photo capture. -
Traceable amendments/corrections. - Separate progress metrics.

**Exit criteria:** A surveyor can complete a parcel survey at the parcel
location, submit evidence, and see the resulting status/history.

### Phase 6 --- Mobile limited offline drafts

Deliverables: - Local SQLite draft storage. - Secure temporary photo
storage. - Manual synchronization. - Idempotent client-generated IDs. -
Retry and sync status. - Server-side revalidation.

**Exit criteria:** A draft can be saved without network and synchronized
manually after connectivity returns without duplicate server records.

### Phase 7 --- Notifications, reporting, and operational hardening

Deliverables: - Persistent in-app notification inbox. - SignalR delivery
and polling fallback. - Role-based dashboards. - Reports. - Immutable
audit hardening. - Backup/restore tests. - Load/security tests. -
Production deployment runbook.

**Exit criteria:** Critical workflows are observable, recoverable,
tested, and deployable.

------------------------------------------------------------------------

## 17. Codex working instructions

Create an `AGENTS.md` at the repository root containing these rules.
Codex must follow them for every task.

### 17.1 How Codex should work

1.  Read this master specification and `AGENTS.md` before making
    changes.
2.  Do not implement the whole product in one response or one coding
    pass.
3.  Work only on the currently requested phase/task.
4.  Before coding, summarize:
    -   what will be changed,
    -   which modules/projects are affected,
    -   database/API impact,
    -   security implications,
    -   tests to add.
5.  If a business rule is marked Open or ambiguous, ask a concise
    question instead of inventing behavior.
6.  Prefer small, coherent changes over broad rewrites.
7.  Keep the modular monolith boundaries intact.
8.  Do not introduce microservices, Kubernetes, GraphQL, or extra
    infrastructure unless explicitly approved.
9.  Do not add dependencies without explaining why they are needed.
10. Do not expose tenant database names, connection strings, internal
    integer IDs, or secrets to clients.
11. Do not bypass authorization for convenience.
12. Do not create fake implementations or placeholder success responses
    for unfinished features.
13. Update tests and relevant docs with every feature.
14. Run relevant build, tests, formatting, and static checks before
    declaring completion.
15. Report exact commands run and any failures or skipped checks.
16. Never claim a test passed unless it was actually executed and
    passed.
17. Keep configuration environment-specific and secrets out of source
    control.
18. Use UTC for persisted timestamps.
19. Use `PublicId` in public API routes; internal integer IDs remain
    internal.
20. Preserve historical survey, geometry-version, amendment, and audit
    records.

### 17.2 Definition of done for each vertical slice

A task is complete only when: - The domain behavior is defined. -
Authorization is enforced server-side. - Input is validated. - Database
constraints/migrations are included if needed. - API contract is
documented. - Frontend/mobile integration is completed if in scope. -
Unit and relevant integration tests pass. - Tenant isolation is tested
where relevant. - Logs and errors are useful but do not leak sensitive
data. - Documentation is updated. - Local run instructions work.

### 17.3 First Codex task

Start with **Phase 0 only**.

Ask Codex to: - inspect the empty/current repository, - propose the
exact solution and folder structure, - create the initial
`GeoSurvey.sln`, - create the ASP.NET Core API skeleton, - create the
Angular app skeleton, - create the .NET MAUI project only if the
installed SDK/workload supports it; otherwise document the prerequisite
and do not block backend/web setup, - add Docker Compose for local
dependencies that are actually needed, - add a basic CI pipeline, - add
health checks and structured logging, - add initial architecture tests
or dependency rules, - write a README with exact setup commands.

Do not implement authentication, tenant provisioning, GIS, or survey
features in Phase 0.

------------------------------------------------------------------------

## 18. Open decisions to resolve before the affected phase

Do not block Phase 0 on all of these. Resolve each item just before
implementing the relevant feature.

  -------------------------------------------------------------------------
  ID                      Open question           Resolve before
  ----------------------- ----------------------- -------------------------
  OPEN-001                Resolved 2026-11-12:    Resolved; see section 6.1
                          separate accounts and
                          credentials per
                          organization, including
                          when email is reused.

  OPEN-002                Exact automatic         Survey submission
                          acceptance rule and     
                          validation failures     
                          that prevent            
                          acceptance.             

  OPEN-003                Exact meaning of        Amendment workflow
                          automatic correction    
                          and who may             
                          initiate/apply it.      

  OPEN-004                Draft deletion timing,  Draft cleanup
                          ownership, and whether  
                          a user can recover a    
                          deleted draft.          

  OPEN-005                Hard-delete exceptions, Deletion implementation
                          retention period, and   
                          legally required        
                          erasure procedure.      

  OPEN-006                Detailed property       Survey form
                          survey fields and       
                          mandatory/optional      
                          rules.                  

  OPEN-007                GPS acceptance          GPS validation
                          threshold, accuracy     
                          policy, allowed         
                          distance from parcel,   
                          and behavior when GPS   
                          accuracy is poor.       

  OPEN-008                Whether parcel point    GIS/parcel model
                          location, polygon       
                          boundary, or both are   
                          required for every      
                          parcel.                 

  OPEN-009                Supported import        GIS import
                          formats and CRS         
                          catalog.                

  OPEN-010                Whether parcel overlap  GIS validation
                          is always invalid or    
                          can be allowed in       
                          specific datasets.      

  OPEN-011                Exact region selection  Tenant
                          and data residency      provisioning/deployment
                          rules.                  

  OPEN-012                UI component library    Angular UI implementation
                          (PrimeNG was            
                          recommended but not     
                          selected).              

  OPEN-013                Exact Azure hosting     Cloud deployment
                          product and regional    
                          deployment topology.    

  OPEN-014                Upload limits and when  File upload
                          to move large files to  implementation
                          direct Blob upload.     

  OPEN-015                Final SQL spatial SRID  Area/distance
                          and projected CRS       calculations
                          strategy per            
                          region/project.         

  OPEN-016                Whether project/area    Assignment reporting
                          assignment history is   
                          visible to supervisors  
                          even though surveyors   
                          see current assignments 
                          only.                   
  -------------------------------------------------------------------------

------------------------------------------------------------------------

## 19. Risks and architectural guardrails

1.  **Tenant isolation is a critical security boundary.** Treat tenant
    resolution and database routing as security-sensitive
    infrastructure.
2.  **A database-per-tenant system creates operational work.** Automate
    migrations, backups, provisioning, health checks, and tenant
    database inventory.
3.  **Regional deployment increases complexity.** Start with a clear
    control-plane/tenant-region model and avoid unnecessary cross-region
    synchronous calls.
4.  **GIS data can be large.** Use viewport queries, spatial indexes,
    simplified geometries, and background import processing.
5.  **GPS is not proof of physical presence by itself.** Record accuracy
    and timestamp; define a realistic tolerance; account for poor signal
    and device limitations. Do not claim impossible precision.
6.  **Offline synchronization creates conflicts.** Use client-generated
    IDs, idempotency, explicit sync states, and server-side
    revalidation.
7.  **Immutable audit is not just an audit table.** Restrict
    write/delete permissions and protect retention.
8.  **Automatic acceptance needs exact rules.** Do not accidentally
    accept invalid or incomplete submissions.
9.  **Hard delete conflicts with historical land records.** Restrict
    physical deletion to approved temporary data.
10. **Do not overengineer the MVP.** Modular monolith, Docker, managed
    Azure services, and vertical slices are sufficient starting points.

------------------------------------------------------------------------

## 20. Immediate next action

Use this document as the master context in the Codex workspace.

**First instruction to Codex:**

> Read `GeoSurvey_Master_Architecture_and_Codex_Spec.md` and `AGENTS.md`
> completely. We are starting Phase 0 only. First inspect the repository
> and installed SDKs, then propose a concise implementation plan for the
> repository skeleton. Do not implement any business features yet. Do
> not make assumptions about open requirements. After I approve the
> plan, implement Phase 0 in small steps, run the checks, and update the
> README and architecture documentation.

**Build principle:** Design deliberately, implement incrementally, test
continuously, and preserve the decisions in this document. The goal is
not merely to generate code; it is to build a maintainable, secure GIS
SaaS product while learning real software system design.
