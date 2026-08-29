# Fiji compliance register

This is a living engineering control document, not legal advice. Every rule requires an official source, effective dates, automated tests, and practitioner approval before production release.

| Area | Current implementation | Required next control |
|---|---|---|
| VAT rates | 9% from 2016; 15% from 1 Aug 2023; 12.5% from 1 Aug 2025 | Validate transitional and earlier transactions; review every budget cycle |
| VAT treatments | Standard, zero-rated, exempt, out of scope | Encode supply classifications from legislation with citations |
| Tax invoices and credit notes | Posting validation, immutable identity snapshots, invoice classification, VAT totals, and printable documents implemented against the 1 Aug 2024 regulations | Obtain Fiji tax-practitioner review of classifications, thresholds, wording, and samples before production release |
| VMS/EFD | Provider-neutral POS-to-SDC contracts and pre-submission validation implemented; live FRCS adapter is not connected | Register for the FRCS Sandbox, implement the current versioned V-SDC adapter, preserve signed fiscal responses, run FRCS test cases, and complete accreditation |
| VAT registration | Organisation status, effective date, TIN, and business address captured; taxable invoice posting is blocked unless registration is active; rolling 12-month posted taxable turnover monitoring, expected taxable turnover for the next 12 months, and 80%/threshold alerts are implemented | Obtain practitioner review of the historical and forecast alert calculations |
| Income tax | Not implemented | Add workpapers after practitioner-approved specification |
| PAYE/payroll | Not implemented | Version schedules and obligations by effective date |
| Record retention | Seven-year deletion safeguards and export audit events implemented for contact documents, supplier-bill attachments, bank-statement source files and their imported transaction batches | Obtain practitioner review and move files to immutable object storage |

Primary sources:

- https://www.laws.gov.fj/Acts/ViewSection/79117
- https://www.laws.gov.fj/Acts/ViewSection/79125
- https://www.laws.gov.fj/Acts/ViewSection/62518
- https://www.frcs.org.fj/wp-content/uploads/2023/11/VAT-Guide-01.11.2-Online-version.pdf
- https://www.frcs.org.fj/wp-content/uploads/2025/01/Non-Individual-Registration-by-Taxpayer-.pdf
- https://frcs.org.fj/our-services/taxation-section/non-individuals/reporting-and-paying-taxes/vat-guide/
- https://frcs.org.fj/our-services/vat-monitoring-system-vms/vms-faqs/
- https://frcs.org.fj/our-services/vat-monitoring-system-vms/efd-accreditation-instructions/
- https://frcs.org.fj/our-services/vat-monitoring-system-vms/vms-phase-3-guide/
- https://tap.sandbox.vms.frcs.org.fj/help/view/131140990/Changelog/en-US

Tax-document classification snapshots are stamped `FJ-VAT-REGS-2024-08-01` so later legal changes can be introduced without silently rewriting previously issued documents. VMS/EFD integration and accreditation remain a separate release gate.
