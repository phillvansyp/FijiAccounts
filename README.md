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

### Phase 1 — Fiji accounting core

1. Tenant context, invitations, permissions and accountant client switching
2. Chart of accounts, immutable posting, periods and audit events
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

### Phase 3 — Fiji payroll

- Build effective-dated PAYE, statutory deductions, payroll liabilities,
  journals and compliance reporting from a practitioner-approved specification.

### Phase 4 — Time, attendance and rostering

- Add employee scheduling, attendance capture, approvals and payroll-ready time
  inputs after the payroll calculation boundary is stable.

### Phase 5 — Three-click payroll

- Combine approved employee, payroll and time data into the streamlined payroll
  experience only after the preceding compliance and workflow controls are
  proven.
