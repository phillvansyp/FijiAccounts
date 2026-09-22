# Payroll Island customer billing

This connection creates sales invoices for Payroll Island account charges, separately from employee payroll imports. The issuer is configured server-side, never supplied by the caller.

## Configuration

Account Island environment:

- `PayrollServiceBilling__OrganisationId`: issuing organisation GUID.
- `PayrollServiceBilling__Token`: a generated shared secret (at least 32 characters).

Payroll Island deployment secret environment:

- `FIJI_PAYROLL_ACCOUNT_ISLAND_BILLING_URL`: `https://app.accountisland.com:8443/api/integrations/payroll-island/service-bills`
- `FIJI_PAYROLL_ACCOUNT_ISLAND_BILLING_TOKEN`: the same shared secret.

Both sides remain disabled until configured. Store the shared key only in private environment files, never in source control or browser code.

The chosen issuer is BOSS PTE Ltd (`563d6648-5abe-4fa0-b2ef-e373533581da`). Its supplied FRCS certificate confirms VAT registration effective 3 July 2026, TIN 2901828123, and Suite 1, Level 3 Jetpoint Complex, Nadi - Ba, Fiji. Do not substitute another organisation's tax details.

## Behaviour

- Payroll Island checks every five minutes for positive, collection-managed service bills.
- The original billing email provides the immutable amounts, period, reference and due date, including historical bills. Current pricing is never used to reconstruct an old charge.
- Customer address and tax number come from the payroll organisation settings.
- Account Island creates one posted sales invoice and customer connection. Source bill IDs and source customer/period are unique. Retrying after a timeout returns the existing invoice.
- An altered snapshot, missing required tax details, tax mismatch, unsupported currency or fiscalisation setup requires review. Failed imports roll back contact, invoice and journal together.
- The complete original billing detail is retained in `PayrollServiceBillingImport.SourceJson`.
- Existing billing emails continue through Payroll Island. This connector does not send another email.
- Previously paid bills are flagged for payment review rather than recreated as outstanding invoices. Free periods do not create receivables.
- Payment confirmation remains separate; invoice creation never marks an account paid or manufactures a bank receipt.

Payroll Island's billing history displays the linked invoice number or the last transfer error. Failed transfers are retried without changing the original snapshot.
