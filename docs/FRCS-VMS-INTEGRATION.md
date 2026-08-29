# FRCS VMS integration boundary

This document records the Phase 2 engineering boundary. It is not evidence of
FRCS accreditation and must not be used to describe Account Island as an
accredited POS or EFD product.

## Selected role

Account Island is being designed as the POS/invoicing-system component of an
Electronic Fiscal Device. It will submit invoice data to an accredited Sales
Data Controller (SDC) and preserve the signed fiscal response. It will not
implement Secure Element signing inside the accounting ledger.

The initial target is the connected V-SDC scenario. The provider-neutral
gateway also permits a later E-SDC adapter without changing invoice or journal
domain logic. FRCS states that V-SDC requires internet connectivity, while an
E-SDC supports semi-connected operation.

## Implemented foundation

- Provider-neutral fiscal invoice submission and result contracts.
- Explicit invoice, transaction and payment classifications.
- Support for split payments and dynamically supplied SDC tax labels.
- Validation of required items, labels, two-decimal half-up line totals and
  payment equality before any SDC call.
- A recovery operation for the last signed result after an uncertain network
  outcome.
- Durable fiscal-document workflow records for invoices and credit notes, with
  separate source links, request hashes, attempt counts,
  prepared/submitting/recovery/rejected/accepted states and audit events.
- Immutable acceptance handling that preserves the signed payload, SDC invoice
  number, SDC time, verification URL and QR data and rejects replacement.
- A submission orchestrator that routes accepted and rejected responses and
  locks uncertain transport outcomes into recovery instead of risking a
  duplicate invoice.
- A Development-only SDC simulator whose numbers, QR data and signed payloads
  are explicitly marked simulated; non-Development environments reject all
  submissions until a real accredited adapter is configured.
- An organisation fiscalisation-readiness page and durable submission register.
- An invoice submission factory that maps transaction-currency gross line
  amounts, buyer TIN and split payments while refusing draft document numbers
  or any VAT treatment without an externally verified SDC tax label.
- Audited organisation settings for all four VAT-treatment labels and the
  default payment type. Activation requires every label and is permitted only
  against the Development simulator until an accredited adapter exists.
- An opt-in invoice gate that reserves the final document number once, locks
  the prepared draft, recovers interrupted attempts and posts the accounting
  journal only after the simulator returns an accepted fiscal response.
- A printable fiscal-response section that accepts only safe PNG/JPEG QR image
  data, validates verification links and visibly separates simulated receipts.
- A refund submission factory that references the original accepted SDC
  invoice and blocks mixed-treatment credits until line-level allocation exists.
- Idempotent, recoverable credit-note refund preparation in the same audited
  fiscal register. The register identifies the document type and links back to
  the source invoice or credit note.
- Durable credit-note drafts with VAT-inclusive allocations back to the
  original invoice lines. Drafts are excluded from statements, VAT and
  receivables until the refund is accepted and the accounting journal posts.
- A credit-note posting gate that submits or recovers the fiscal refund before
  updating Accounts Receivable, output VAT or the source invoice balance.
- Per-line remaining-credit enforcement so repeated drafts cannot over-credit
  or over-return an original invoice line.
- Tracked-item fiscal refunds that restore proportional quantity and cost only
  in the same database transaction as the credit journal and posted status.
- Credit-note recovery screens that distinguish prepared, rejected, uncertain,
  accepted and posted states and guide the user to retry or recover safely.
- Durable fiscal credit-note reversal drafts that submit a sale correction
  referencing the accepted refund. Ledger and returned-stock reversals remain
  blocked until the correction is accepted or safely recovered.
- Durable fiscal invoice-void drafts that submit a refund referencing the
  accepted original invoice. The reversing journal and tracked-stock return
  remain blocked through rejection or an uncertain response and post only
  after acceptance or safe recovery.
- Invoice screens and the fiscal register expose prepared, rejected, uncertain,
  accepted and posted void states so interrupted work can be resumed without
  creating a duplicate fiscal document.

The contracts intentionally contain no FRCS endpoint, certificate-store or
wire-format assumptions. The official protocols are public, versioned and
subject to change, and environment details must not be hard-coded.

## Next integration gate

The offline accepted-response controls now cover invoice posting, credit-note
refunds, credit-note reversals and invoice voids, including mixed VAT, tracked
stock, explicit rejection and uncertain-response recovery. The remaining VMS
work depends on verified Sandbox access and an accredited SDC contract; the
development simulator must not be presented as FRCS connectivity.

The external integration slice remains deferred until Account Island can:

1. Register as a vendor in the FRCS Sandbox and obtain developer certificates.
2. Capture the current VMS Phase 3 environment configuration and tax labels
   from the SDC rather than inventing or hard-coding them.
3. Implement a versioned V-SDC adapter with mutual-certificate authentication,
   status checks, invoice submission, timeout recovery and redacted logging.
4. Execute FRCS test cases, prepare product documentation and complete both the
   technical and administrative accreditation reviews.

## Primary sources reviewed 29 August 2026

- https://frcs.org.fj/our-services/vat-monitoring-system-vms/efd-accreditation-instructions/
- https://frcs.org.fj/our-services/vat-monitoring-system-vms/vms-phase-3-guide/
- https://tap.sandbox.vms.frcs.org.fj/help/view/1539512816/Create-Invoice/en-US
- https://tap.sandbox.vms.frcs.org.fj/help/view/1148052678/Identification-of-Environments-and-Important-Endpoints/en-US
- https://tap.sandbox.vms.frcs.org.fj/help/view/131140990/Changelog/en-US

FRCS registration, Sandbox testing and formal accreditation remain release
gates. A successful local or Sandbox test is not accreditation.
