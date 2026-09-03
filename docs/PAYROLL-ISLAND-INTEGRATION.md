# Payroll Island integration handover

## Product boundary

Payroll Island is the source of truth for employees, pay items, statutory
calculations, payslips and finalised pay runs. Account Island is the source of
truth for ledger account mapping, journal approval and posting, bank
reconciliation, financial reporting and the accounting audit trail.

Account Island pulls aggregate data from Payroll Island. Payroll Island must not
send employee names, bank account numbers, tax identifiers, payslips or line-by-line
employee earnings through this contract.

The Account Island foundation is available at:

```text
/o/{accountIslandOrganisationId}/integrations/payroll-island
```

An owner or administrator configures the Payroll Island address, Payroll Island
organisation ID, read-only token and ledger mappings. A user with accounting
posting permission can synchronise pay runs and post a retrieved run.

## What Account Island now supports

- HTTPS-only Payroll Island connections.
- Access tokens encrypted using the existing persistent ASP.NET data-protection
  key ring. Tokens are never returned to the browser after saving.
- Separate mappings for gross wages, employer FNPF, net wages payable, PAYE,
  FNPF and other deductions.
- Aggregate finalised-pay-run imports with an immutable external ID, revision
  and semantic payload hash.
- Duplicate-safe retries.
- Imported PAYE, FNPF, wage and other-deduction payment summaries.
- Draft review before journal posting.
- Atomic payroll journal posting through the normal tenant, accounting-period,
  branch/division and audit controls.
- Correction detection. A later revision of an already-posted pay run is held
  as `CorrectionRequired`; it cannot silently post another journal.

Payment records are currently evidence and matching targets. They do not post
directly to the bank ledger. Account Island bank statement reconciliation remains
the source of truth for cash movement.

## Authentication required from Payroll Island

Payroll Island must allow an authorised business administrator to issue and
revoke a long random bearer token with this single scope:

```text
account-island.payroll.read
```

The token must be restricted to one Payroll Island organisation and must only
read finalised aggregate pay runs and their payment summaries. Show the token
once at creation. Store only a slow or keyed hash in Payroll Island, record its
creation, last use and revocation, and support rotation without changing the
Payroll Island organisation ID.

Every request from Account Island contains:

```http
Authorization: Bearer <token>
Accept: application/json
X-Account-Island-Contract: 2026-09-01
```

Do not accept the token in a URL or query string. Require TLS and return `401`
for an invalid or revoked token and `403` when the token does not cover the
requested organisation.

## Endpoint Payroll Island must implement

```http
GET /api/account-island/v1/organisations/{payrollOrganisationId}/pay-runs?after={cursor}
```

Rules:

1. Return finalised pay runs only.
2. Return at most 500 pay runs and keep the complete response below 5 MB.
3. Order changes deterministically by finalisation/update sequence.
4. `nextCursor` is an opaque high-water mark. It must advance only through data
   included in the response. Return the current high-water mark even when no
   changes are available.
5. A retry with the same `after` cursor must return the same logical records.
6. `externalPayRunId` is permanent. `revision` starts at 1 and increases whenever
   exported accounting totals or payment records change.
7. The same external ID and revision must remain semantically immutable.
8. Payment records must use stable IDs and stable ordering. Account Island also
   canonicalises their order before duplicate checking.

Successful response:

```json
{
  "payRuns": [
    {
      "externalPayRunId": "2d51b858-30d0-4f37-a647-676b7a44b072",
      "revision": 1,
      "payRunNumber": "PR-2026-018",
      "periodStart": "2026-08-15",
      "periodEnd": "2026-08-28",
      "paymentDate": "2026-08-28",
      "currency": "FJD",
      "employeeCount": 10,
      "grossEarnings": 10000.00,
      "employeePaye": 1000.00,
      "employeeFnpf": 800.00,
      "employerFnpf": 1000.00,
      "otherDeductions": 200.00,
      "netPay": 8000.00,
      "payments": [
        {
          "externalPaymentId": "PR-2026-018-NET",
          "kind": "NetWages",
          "status": "Paid",
          "dueDate": "2026-08-28",
          "paidDate": "2026-08-28",
          "amount": 8000.00,
          "reference": "WAGES-PR-2026-018"
        },
        {
          "externalPaymentId": "PR-2026-018-PAYE",
          "kind": "Paye",
          "status": "Expected",
          "dueDate": "2026-09-30",
          "paidDate": null,
          "amount": 1000.00,
          "reference": "PAYE-PR-2026-018"
        },
        {
          "externalPaymentId": "PR-2026-018-FNPF",
          "kind": "Fnpf",
          "status": "Expected",
          "dueDate": "2026-09-30",
          "paidDate": null,
          "amount": 1800.00,
          "reference": "FNPF-PR-2026-018"
        },
        {
          "externalPaymentId": "PR-2026-018-OTHER",
          "kind": "OtherDeduction",
          "status": "Expected",
          "dueDate": "2026-09-30",
          "paidDate": null,
          "amount": 200.00,
          "reference": "OTHER-PR-2026-018"
        }
      ]
    }
  ],
  "nextCursor": "pay-run-change:1842"
}
```

Accepted payment kinds are `NetWages`, `Paye`, `Fnpf` and `OtherDeduction`.
Accepted payment statuses are `Expected`, `Paid` and `Cancelled`. A paid record
must contain `paidDate`.

All money values must be non-negative, have no more than two decimal places and
use the Account Island organisation's base currency. Gross earnings must be
positive and the pay run must balance exactly:

```text
grossEarnings + employerFnpf
= netPay + employeePaye + employeeFnpf + employerFnpf + otherDeductions
```

Non-cancelled payment records must also total their corresponding liability:
`NetWages = netPay`, `Paye = employeePaye`,
`Fnpf = employeeFnpf + employerFnpf`, and
`OtherDeduction = otherDeductions`. Multiple records of one kind are allowed.

`payRunNumber` is limited to 72 characters. External IDs are limited to 120
characters and may contain letters, numbers, dots, underscores, colons and
hyphens.

## Accounting created by Account Island

Posting a ready pay run creates one immutable `Payroll` journal dated on the
payment date:

| Account mapping | Debit | Credit |
|---|---:|---:|
| Gross wages expense | Gross earnings | |
| Employer FNPF expense | Employer FNPF | |
| Net wages payable | | Net pay |
| PAYE payable | | Employee PAYE |
| FNPF payable | | Employee FNPF + employer FNPF |
| Other deductions payable | | Other deductions |

Zero-value lines are omitted. The journal reference is
`PAYROLL-{payRunNumber}`.

Recommended Fiji chart accounts are separate Wages Payable, PAYE Payable, FNPF
Payable, Other Payroll Deductions and Employer FNPF Expense accounts. Account
Island allows the existing combined payroll-liability account to be used while
those accounts are being created.

## Corrections and reversals

Payroll Island must never edit revision 1 after Account Island has retrieved it.
Publish revision 2 under the same `externalPayRunId`.

- If the earlier revision is still an Account Island draft, it is superseded and
  the latest revision becomes ready to post.
- If an earlier revision was posted and its journal inputs changed, the new
  revision is held for correction review and cannot post automatically.
- If only payment records changed, the latest revision remains linked to the
  existing journal. No duplicate journal or correction warning is created.

Before production use, complete the next Account Island slice: create an
approval-controlled reversal of the earlier payroll journal and a replacement
journal linked to the new revision. Until then, correct a posted payroll run only
through the accountant-controlled manual reversal process.

## Payroll Island delivery checklist

1. Add organisation-scoped token creation, rotation, revocation and audit events.
2. Implement the endpoint and exact contract above.
3. Add database change sequencing for the opaque cursor.
4. Add contract tests using the example payload.
5. Test token isolation across two organisations.
6. Test an empty sync, duplicate retry, revision 2, revoked token and payload
   larger than the limit.
7. Deploy to a non-production Payroll Island environment with trusted HTTPS.
8. In an Account Island test organisation, create the recommended ledger
   accounts, save the connection and run Sync now.
9. Confirm the draft journal totals before posting.
10. Obtain Fiji payroll-practitioner approval of PAYE/FNPF calculations and due
    dates. The integration transfers Payroll Island's results; it does not certify
    those calculations.

Do not connect live payroll or employee data until the token isolation, revision
handling, accounting mappings and practitioner review have all been verified.
