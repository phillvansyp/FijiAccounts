# Employee receipts and owner overview

The mobile-friendly `/receipts` workspace uses Account Island sign-in. Owners
can also open it from the organisation menu: **Receipts & owner overview**.

## Employee workflow

1. Register an Account Island account and verify the email.
2. The company owner adds that email under **Employee receipt access**.
3. Open `/receipts`, choose the company and upload a JPEG, PNG or PDF up to 10 MB.
4. Enter merchant, receipt date, currency, amount and business purpose. Indicate
   whether company funds or personal funds paid for the purchase.
5. Submit and follow the review status. Employees can download only their own
   receipts, and cannot see company balances or another employee's submissions.

## Owner workflow

The overview uses existing Account Island calculations for cash, receivables
and payables. The receipt inbox shows employee submissions with original files.
Owners approve or return submissions with a reason. A second owner must review
an owner's own receipt. Access can be removed without deleting submitted files.

Receipt contributors are a separate access list, **not** accounting members.
Adding a contributor never gives them access to company ledgers or reports.
Every query, file download and command checks organisation and user access.
Suspended organisation groups cannot use the workspace.

## Accounting boundary

An approved receipt means approved for accounting review, not paid, reimbursed,
posted or bank-reconciled. This first version stores supporting paperwork and
review decisions. It does not automatically create supplier bills, reimbursements
or bank matches. Owners continue those steps in the existing accounting screens.
No changes are made to existing bills or payroll expenses.

## Implementation and rollout

- Original files use the existing immutable document store; downloads require
  authentication and use attachment responses with `no-store` and `nosniff`.
- File extension and signature checks accept JPEG, PNG and PDF only.
- A unique submission reference prevents duplicate records on retries.
- Review version checks reject stale decisions; decisions and access changes
  are recorded in the organisation audit trail.
- The migration adds `EmployeeReceipts` and `ReceiptContributors` tables.
- This is an online browser app, not an App Store/Play Store package. Offline
  capture, OCR and malware scanning are not added by this change; public native
  rollout should follow the document processing requirements in
  `MOBILE-APPLICATION-ARCHITECTURE.md`.

Validation: `EmployeeReceiptTests` exercises receipt-only permissions, isolation,
retry handling, owner review, access removal, own-receipt approval prevention,
file rejection and suspended organisation access. Existing mobile access tests
cover the dashboard permission boundaries.
