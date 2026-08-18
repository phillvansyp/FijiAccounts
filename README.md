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

## Compliance boundary

Software tests support compliance but do not constitute legal or FRCS approval. Tax classifications, filing outputs, payroll schedules, retention requirements, and VMS fiscalisation must be verified against current legislation and signed off by a qualified Fiji tax practitioner. Fiscal-invoice/POS functionality must complete applicable FRCS accreditation before being represented as approved.

## Roadmap

1. Tenant context, invitations, permissions and accountant client switching
2. Chart of accounts, immutable posting, periods and audit events
3. Customers, suppliers, invoices, bills, credit notes and payments
4. VAT workpapers and return preparation
5. Bank imports and reconciliation
6. Financial statements, budgets and management reports
7. Payroll, assets, inventory and multi-currency
8. FRCS VMS integration and accreditation
