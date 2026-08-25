# Mobile application architecture

Account Island's iOS and Android applications are first-class accounting
clients. They are not limited receipt-capture or approval companions. A user
should receive the same company access, accounting capabilities, and permission
outcomes on mobile as they receive on the web.

This document defines the boundary required before mobile screens are built. It
does not choose a UI framework. Native, .NET MAUI, Flutter, and React Native
clients must all use the same versioned API and security contracts.

## Product boundary

The mobile applications target functional parity for company accounting:

- dashboard, notifications, and organisation switching;
- contacts, products, sales, purchases, receipts, and payments;
- requisitions, purchase orders, matching, and approvals;
- banking, statement import, coding, transfers, and reconciliation;
- inventory, fixed assets, projects, journals, and accounting periods;
- financial, ageing, cashflow, budget, group, and tax reporting;
- company settings, team access, documents, and audit history.

Platform-operator administration remains a separate surface. A platform role
does not grant company-ledger access, and the mobile accounting client must not
turn it into implicit tenant access.

Feature delivery can be phased, but API and navigation design must assume this
full scope from the beginning. Mobile-only restrictions must be based on a real
device limitation or security decision, not on a separate reduced permission
model.

## Required architecture

```text
iOS app                 Android app
       \                 /
        Typed mobile client
                 |
        /api/mobile/v1
                 |
 Authentication, tenant context, permission and validation adapters
                 |
 Existing application services and domain rules
                 |
 ApplicationDbContext / document storage / background workers
```

The API belongs in the existing ASP.NET Core host initially. It should call the
same application services used by Blazor instead of duplicating posting,
approval, tax, or access logic in controllers. A separate deployable API can be
extracted later if scaling or release isolation requires it.

Do not expose EF entities directly. Mobile contracts must be explicit records
with stable JSON names, nullability, validation, and versioning independent of
database migrations.

## Authentication and sessions

The current web application uses ASP.NET Core Identity cookies. Mobile requires
OAuth 2.1/OpenID Connect authorization code flow with PKCE. Do not create a
custom username/password-to-JWT endpoint.

The selected identity provider or authorization server must support:

- short-lived access tokens and rotating refresh tokens;
- passkeys and existing two-factor requirements;
- refresh-token family revocation and reuse detection;
- device/session listing and remote sign-out;
- verified email and account lockout behavior matching the web application;
- secure system-browser authentication rather than embedded credential forms;
- account deletion and data export entry points required by app stores.

Access tokens identify the user, not an organisation. Every company request
uses an organisation ID in its route and repeats the existing server-side
membership, accountant-engagement, role, and branch/division checks.

Biometric unlock only protects locally stored refresh credentials and drafts.
It never replaces server authentication or an approval permission check.

## Authorization model

Mobile must preserve these existing boundaries:

- `OrganisationRole`: Owner, Administrator, Accountant, Bookkeeper, Payroll,
  Sales, and ReadOnly;
- direct company memberships and separately scoped accountant engagements;
- active/suspended tenant status;
- branch-wide and division-specific access grants;
- group roles that do not imply access to each company ledger;
- approval-policy levels and separation of requester and approver;
- platform administrator access that does not imply company membership.

The API should expose a capability document after organisation selection:

```http
GET /api/mobile/v1/organisations/{organisationId}/capabilities
```

The response supplies UI hints such as `canPostJournals`, `canManageContacts`,
`canManageTeam`, accessible branches/divisions, and available approval actions.
These hints control presentation only. Every query and command must independently
enforce the same rule in the relevant application service.

As the API is introduced, broad checks such as `CanPostJournalsAsync` should be
split into named capabilities where business behavior differs. For example,
posting a sales invoice, maintaining bank rules, approving a requisition, and
closing a period should not remain coupled forever merely because they currently
share the same role set.

## API conventions

All endpoints use `/api/mobile/v1`. Organisation resources use:

```text
/api/mobile/v1/organisations/{organisationId}/...
```

Conventions:

- JSON uses camel case and UTC ISO 8601 timestamps.
- Fiji business dates use `yyyy-MM-dd` and remain distinct from timestamps.
- Money is returned as a decimal JSON number plus an ISO 4217 currency code.
- Lists use cursor pagination with deterministic ordering.
- Validation and authorization failures use RFC 9457 Problem Details.
- Commands accept an `Idempotency-Key` header and persist the result for replay.
- Mutable resources use ETags or an explicit version to reject stale updates.
- Long-running imports and report exports return an operation resource.
- Deletion of posted accounting history is never introduced through the API;
  existing void and reversal services remain authoritative.
- Correlation ID, device ID, client version, and actor user ID are attached to
  audit and diagnostic records.

Every posting, payment, approval, reversal, import, upload, and invitation
command requires idempotency. Mobile retries are normal and must not create a
second financial event.

## Documents and receipt capture

Receipt and supplier-document capture is a core mobile workflow:

1. Capture from camera, photo library, or file provider.
2. Correct orientation and allow crop/retake before upload.
3. Create an upload session and stream the file without loading it fully into
   application memory.
4. Validate MIME signature, extension, size, organisation access, and quota.
5. Store the original immutably and create a preview derivative.
6. Run malware scanning before the document becomes available to other users.
7. Extract candidate supplier, date, reference, totals, VAT, currency, and line
   data asynchronously.
8. Present extracted values as an editable draft; never post extracted data
   without user confirmation.
9. Preserve the original, extraction result, corrections, submitter, device,
   and final accounting link in the audit trail.

Initial endpoints:

```text
POST /organisations/{id}/document-uploads
PUT  /organisations/{id}/document-uploads/{uploadId}/content
POST /organisations/{id}/document-uploads/{uploadId}/complete
GET  /organisations/{id}/document-uploads/{uploadId}
POST /organisations/{id}/supplier-bill-drafts/from-document
```

Object storage should replace local-disk storage before public mobile release.
Upload sessions must be short-lived and restricted to one organisation.

## Offline behavior

Mobile should remain useful on unreliable connections, but accounting truth
stays on the server.

Allowed offline:

- cached read-only lists and record summaries;
- receipt images waiting for upload;
- sales invoice, quote, contact, bill, requisition, and journal drafts;
- approval comments prepared but not yet submitted.

Online-only:

- posting or voiding accounting documents;
- recording or reversing payments and receipts;
- approving or rejecting controlled transactions;
- bank reconciliation completion;
- period close/reopen;
- team, permission, and organisation-setting changes.

The outbox must encrypt local data, preserve the idempotency key, show a clear
pending/failed state, and require the server response before showing an action
as posted or approved. Conflict responses must be resolved by the user rather
than silently overwriting newer server state.

## Notifications

Apple Push Notification service and Firebase Cloud Messaging carry only an
opaque notification ID and routing hint. Customer, supplier, amount, tax, bank,
and document details must not appear in the push payload.

The app fetches the authorised notification after opening. Device registrations
are per user installation, revocable, and updated when a user signs out or loses
organisation access. Useful notification classes include:

- requisition, purchase-order, and supplier-payment approvals;
- overdue invoices and bills;
- document expiry and processing failure;
- bank import/reconciliation completion;
- invitation and security events.

## Capability matrix

| Area | Current web route | Primary services | Mobile capability | Offline |
| --- | --- | --- | --- | --- |
| Organisations | `/organisations` | `TenantAccessService`, `EnterpriseStructureService` | List, switch, create, group/company structure | Cached list |
| Dashboard | `/o/{id}` | Forecast, intelligence, risk and control services | Full overview and work queue | Cached summary |
| Search | `/o/{id}/search` | Existing query services | Cross-module search and deep links | Recent records |
| Notifications | `/o/{id}/notifications` | `NotificationService` | List, filter, read, archive, deep link | Cached list |
| Contacts | `/o/{id}/contacts` | `BusinessPartyService` | Customers, suppliers, defaults, bank verification | Draft edits |
| Contact documents | `/o/{id}/contacts/{partyId}/documents` | `BusinessPartyDocumentService` | View, upload, classify, expiry | Upload outbox |
| Customer statements | `/o/{id}/contacts/{customerId}/statement` | Sales and receipt queries | View, share/export statement | Cached view |
| Products | `/o/{id}/products` | `ProductCatalogService` | Products/services, prices, active status | Draft edits |
| Sales invoices | `/o/{id}/sales` | `SalesInvoiceService` | List, draft, edit, post, copy, void | Drafts only |
| Sales credits | `/o/{id}/sales/credits/{creditId}` | `SalesCreditNoteService` | Issue and view credits, including explicit mixed-VAT adjustment | Draft only |
| Quotes | `/o/{id}/quotes` | `SalesQuoteService` | Create, update, accept/convert and view | Drafts only |
| Customer receipts | `/o/{id}/receipts` | `CustomerReceiptService` | Record, allocate and reverse receipts | Online commands |
| Supplier bills | `/o/{id}/purchases` | `PurchasingService`, `SupplierBillDraftService` | Capture, draft, post, pay, void | Capture/drafts |
| Bill attachments | Bill detail | `SupplierBillAttachmentService` | Camera/file upload, preview, download, remove | Upload outbox |
| Supplier credits | Bill detail | `SupplierCreditNoteService` | Issue, view and allocate supplier credits | Draft only |
| Requisitions | `/o/{id}/requisitions` | `PurchaseRequisitionService`, `PurchaseApprovalPolicyService` | Create, submit, approve, reject, withdraw | Draft only |
| Purchase orders | `/o/{id}/purchase-orders` | `PurchaseOrderService`, `PurchaseOrderMatchService` | Create, approve, send, receive, match and close | Draft only |
| Payment approvals | Bill detail/notifications | `PurchasingService`, `PurchaseApprovalPolicyService` | Request, approve, reject and withdraw | Online only |
| Bank accounts | `/o/{id}/banking` | `BankAccountService` | Accounts, imports, coding and reconciliation | Cached list |
| Bank transfers | `/o/{id}/bank-transfers` | `BankTransferService` | Create and reverse transfers | Online only |
| Bank rules | `/o/{id}/bank-rules` | `BankRuleService` | Create, edit, apply and deactivate | Draft edits |
| Reconciliation | Banking workflow | Reconciliation services | Match, explain, reconcile and reopen sessions | Online only |
| Inventory | `/o/{id}/inventory` | `InventoryService` | Items, movements, adjustments and valuation | Counts as drafts |
| Fixed assets | `/o/{id}/assets` | `FixedAssetService` | Register, dispose, depreciate and view schedule | Draft additions |
| Projects | `/o/{id}/projects` | Project services | Jobs, costs, documents, claims, variations, WIP and profitability | Draft capture |
| Journals | `/o/{id}/journals` | `JournalPostingService` | Draft, post, view and reverse journals | Drafts only |
| Accounts | `/o/{id}/accounts` | `ChartOfAccountsService` | View and maintain chart of accounts | Cached list |
| Periods | `/o/{id}/periods` | `AccountingPeriodService` | Readiness, close, reopen and lock controls | Online only |
| Budgets | `/o/{id}/budgets` | Budget services | Enter, scope, compare and report | Draft entry |
| Cashflow scenarios | `/o/{id}/scenarios` | `CashflowScenarioService` | Create, compare, archive and include overdue items | Draft changes |
| Financial reports | `/o/{id}/reports/{type}` | `FinancialReportService` | Profit and loss, balance sheet, trial balance | Cached exports |
| Ledger | `/o/{id}/general-ledger` | Journal/report queries | Filtered ledger and journal drill-through | Cached pages |
| Ageing | `/o/{id}/reports/aging/*` | Ageing queries | Receivable/payable ageing and drill-through | Cached report |
| Cash summary | `/o/{id}/reports/cash-summary` | Reporting services | Cash movement and source breakdown | Cached report |
| Group reporting | `/o/{id}/reports/group` | Group reporting, rates and eliminations | Consolidation, exchange rates and eliminations | Online changes |
| VAT centre | `/o/{id}/tax` | `VatWorkpaperService` | Workpaper, source drill-through and export | Cached report |
| Audit | `/o/{id}/audit` | Audit queries | Filter, inspect evidence and deep link | Cached pages |
| Settings | `/o/{id}/settings` | `OrganisationSettingsService`, structure services | Business, tax, numbering, automation and dimensions | Online changes |
| Team access | Organisation overview/settings | Invitations and tenant access services | Invite, role and branch/division grants | Online only |
| Account security | `/Account/Manage/*` | ASP.NET Core Identity | Profile, sessions, passkeys, MFA, recovery and deletion | Online only |

## Endpoint groups

The first API surface should be grouped by business capability rather than by
database table:

```text
/session
/devices
/organisations
/organisations/{id}/capabilities
/organisations/{id}/dashboard
/organisations/{id}/notifications
/organisations/{id}/contacts
/organisations/{id}/documents
/organisations/{id}/products
/organisations/{id}/sales-invoices
/organisations/{id}/sales-credit-notes
/organisations/{id}/quotes
/organisations/{id}/customer-receipts
/organisations/{id}/supplier-bills
/organisations/{id}/supplier-credit-notes
/organisations/{id}/requisitions
/organisations/{id}/purchase-orders
/organisations/{id}/payment-approvals
/organisations/{id}/banking
/organisations/{id}/inventory
/organisations/{id}/assets
/organisations/{id}/projects
/organisations/{id}/journals
/organisations/{id}/periods
/organisations/{id}/budgets
/organisations/{id}/cashflow-scenarios
/organisations/{id}/reports
/organisations/{id}/vat
/organisations/{id}/audit-events
/organisations/{id}/settings
/organisations/{id}/team
```

OpenAPI is the source for generated Swift/Kotlin or cross-platform typed clients.
Contract tests must verify that every command calls the existing application
service, returns stable Problem Details codes, and cannot cross an organisation
or dimension boundary.

## Delivery sequence

### Foundation

1. Commit and release the current enterprise-hardening baseline.
2. Select and configure the OAuth/OIDC authority with PKCE and refresh rotation.
3. Add `/api/mobile/v1/session`, organisations, capabilities, Problem Details,
   idempotency storage, API versioning, OpenAPI, and integration-test fixtures.
4. Add device registration, revocation, client-version enforcement, telemetry,
   and API rate limits.
5. Move uploaded files to production object storage with malware scanning.

Exit gate: a mobile client can authenticate, select only an authorised company,
discover its exact capabilities, retry a command safely, and lose access
immediately when membership or device session is revoked.

### Transaction slice

1. Dashboard, organisation switching, notifications, and global search.
2. Contacts, products, sales invoices, quotes, and customer receipts.
3. Receipt/supplier-document capture and supplier-bill drafts.
4. Requisitions, purchase orders, supplier payments, and approval actions.

Exit gate: a business can perform day-to-day money-in, money-out, capture, and
approval work from either mobile platform with the same accounting result as the
web application.

### Accounting parity

1. Banking, imports, coding, rules, transfers, and reconciliation.
2. Inventory, assets, projects, journals, periods, budgets, and scenarios.
3. Financial, ageing, cash, group, VAT, ledger, and audit reporting.
4. Organisation settings, team access, account security, exports, and remaining
   web-to-mobile parity gaps.

Exit gate: the capability matrix contains no unexplained mobile omissions and
permission-parity integration tests pass for every role.

### Store release

Complete accessibility testing, privacy nutrition labels/data safety forms,
account deletion, consent and tracking review, export-control declarations,
crash reporting, staged rollout, incident response, and support documentation
before production App Store and Google Play publication.

## First implementation slice

The first code slice should remain deliberately small while establishing the
correct permanent boundary:

```text
GET /api/mobile/v1/session
GET /api/mobile/v1/organisations
GET /api/mobile/v1/organisations/{id}/capabilities
GET /api/mobile/v1/organisations/{id}/dashboard
GET /api/mobile/v1/organisations/{id}/notifications
POST /api/mobile/v1/organisations/{id}/notifications/{notificationId}/read
```

Implementation status:

- complete: versioned route group, authenticated session, organisation list,
  organisation capabilities, dimension-scoped dashboard, notification read
  models and read command, persistent command idempotency, cursor pagination,
  device registration and revocation, client-version enforcement, partitioned
  API rate limits, OpenAPI generation, API-specific 401/403 behavior, and
  tenant/dimension permission tests;
- complete: self-hosted OpenIddict bearer-token authority using the existing
  ASP.NET Identity users, authorization code with S256 PKCE, rolling refresh
  tokens, device-bound access, and targeted authorization revocation;
- next: production signing/encryption certificate provisioning, iOS universal
  link and Android app link ownership, telemetry, and production document
  storage.

It must include authentication integration tests, tenant-crossing tests,
suspended-tenant tests, role/capability tests, dimension-scope tests, OpenAPI
generation, and a typed client build. Receipt upload and approvals should be the
next vertical slice after these foundations, not parallel ad hoc endpoints.

## Decisions required before API implementation

- OAuth/OIDC authority and ownership of the login user experience.
- Mobile UI framework and whether separate native teams are expected.
- Production object storage, scanning, and document-retention policy.
- OCR/document extraction provider and regional data-processing constraints.
- APNs/FCM delivery provider and notification-retention policy.
- Minimum supported iOS/Android versions and forced-upgrade policy.
- Offline-data classification, encryption, and remote-wipe expectations.
- Whether group consolidation administration is required in the first public
  mobile release or delivered later within the parity programme.
