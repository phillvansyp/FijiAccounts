# Employee receipts and owner overview

The mobile-friendly `/receipts` workspace uses Account Island sign-in. Owners
can also open it from the organisation menu: **Receipts & owner overview**.

## Employee workflow

1. For a new employee, use **Invite team member → Role → Receipts only**. They follow the invitation, create their login and open the receipt workspace. This grants receipt access immediately without account visibility or a separate profile-assignment step.
2. Under **Team & permissions → Permission profiles**, create an **Employee receipts**
   profile. Tick **Add receipts**, untick **View accounts and reports**, and leave
   the other permissions off. Assign the profile to the employee in **Assign
   permission profiles**. Existing verified users without a team membership can
   also receive receipt-only access under **Employee receipt access**.
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

Receipt-only permission profiles exclude account and report access. Legacy
receipt contributors are a separate access list, **not** accounting members.
An assigned permission profile takes precedence over a contributor grant,
including when **Add receipts** is switched off. Existing profiles retain their
previous account visibility by default; receipt submission defaults off.
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
