# Account Island

A Fiji-first, multi-tenant accounting SaaS for businesses, staff, and accounting practices.

## Foundation included

- Registration, login, passkeys and two-factor authentication
- Self-service business and accounting-practice creation
- Tenant memberships with explicit roles
- Separate accountant-to-client engagements
- Balanced double-entry journals
- Effective-dated Fiji VAT calculation
- Automated accounting and tax-boundary tests

## Run locally

```powershell
dotnet run --project src/FijiAccounts.Web
```

Open the HTTPS address shown in the terminal. Development uses SQLite; production is intended to use PostgreSQL with row-level tenant isolation.

## Production deployment

Production deployment is performed by a GitHub Actions self-hosted runner after
each push to `main`. See [docs/deployment.md](docs/deployment.md) for server,
Docker, HTTPS, runner, backup, and first-data-migration setup.

## Platform administration

The operator dashboard is available at `/platform`. Access requires the
`PlatformAdministrator` Identity role and is separate from organisation Owner
or Administrator access.

Set `PlatformAdmin:Email` through user secrets or deployment configuration to
bootstrap the operator role for an existing account. In Development only, when
no email is configured, the first existing user is assigned the role. Sign out
and back in after the first assignment so the authentication cookie receives
the role claim.

The demo-data controls are available at `/platform/demo` and are additionally
restricted to the Development environment.

## Compliance boundary

Software tests support compliance but do not constitute legal or FRCS approval. Tax classifications, filing outputs, payroll schedules, retention requirements, and VMS fiscalisation must be verified against current legislation and signed off by a qualified Fiji tax practitioner. Fiscal-invoice/POS functionality must complete applicable FRCS accreditation before being represented as approved.

## Roadmap

Development is deliberately gated so regulatory and workforce products are not
built on an unfinished accounting foundation.

The iOS and Android applications are planned as full accounting clients with
permission parity, not limited companion apps. Their API, authentication,
offline, receipt-capture, approval, and store-release architecture is defined in
[docs/MOBILE-APPLICATION-ARCHITECTURE.md](docs/MOBILE-APPLICATION-ARCHITECTURE.md).

The active market scope is Fiji first, with New Zealand supported on the same
country-aware foundation. Australia and further jurisdictions are deferred until
the Fiji product base is releaseable and proven.

### Release operations gate — finish before public release

The application and deployment workflow contain the required backup replication,
checksum verification, restore validation, health checks and failure-alert
plumbing. The remaining work depends on production infrastructure and is deferred
while product development continues:

1. Provision a separate owner-controlled backup host and restricted SSH account.
2. Configure the protected off-host backup secrets described in
   [docs/deployment.md](docs/deployment.md#off-host-backups-and-operations-alerts).
3. Configure the operations-alert recipient and verify delivery using the
   production email provider.
4. Run and record an off-host restore drill, including checksum and database
   integrity verification.
5. Confirm deployment and public-health failure alerts before release sign-off.

This gate is not complete merely because local backups and automated tests pass.
No production backup or alert coverage should be claimed until the configured
off-host workflow and restore drill have been verified.

### Phase 1 — Fiji accounting core

1. Tenant context, invitations, permissions and accountant client switching
2. Chart of accounts, immutable posting, period close, accountant handover packs,
   immutable pack history, schedule review, final sign-off, approval-linked
   year-end adjustments and audit events
3. Customers, suppliers, invoices, bills, credit notes and payments
4. VAT workpapers and return preparation
5. Bank imports and reconciliation
6. Financial statements, budgets and management reports
7. Enterprise dimensions, consolidation and eliminations
8. Fixed assets, inventory and multi-currency accounting
9. Country-aware Fiji demo data and accounting gap closure

Exit gate: a Fiji business can run its complete accounting operation and produce
the required financial and tax workpapers. Fiji rules must be verified from
primary sources and signed off by a qualified practitioner.

### Phase 2 — FRCS integration

- Implement the applicable VMS/EFD protocol without coupling it to the core
  posting engine.
- Complete required FRCS testing and accreditation before representing the
  product as approved.

### Phase 3 — Payroll Island integration

- Keep employee records, effective-dated PAYE/FNPF calculations, payslips and
  finalised pay runs in Payroll Island.
- Pull aggregate finalised pay runs and payment summaries into Account Island,
  map them to payroll liabilities, and post controlled payroll journals.
- Complete the versioned interface described in
  [docs/PAYROLL-ISLAND-INTEGRATION.md](docs/PAYROLL-ISLAND-INTEGRATION.md), then
  add approval-controlled correction reversals and bank-payment matching.

### Phase 4 — Time, attendance and rostering

- Add employee scheduling, attendance capture, approvals and payroll-ready time
  inputs after the payroll calculation boundary is stable.

### Phase 5 — Three-click payroll

- Combine approved employee, payroll and time data into the streamlined payroll
  experience only after the preceding compliance and workflow controls are
  proven.
