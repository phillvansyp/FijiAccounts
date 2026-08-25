# Fiji compliance register

This is a living engineering control document, not legal advice. Every rule requires an official source, effective dates, automated tests, and practitioner approval before production release.

| Area | Current implementation | Required next control |
|---|---|---|
| VAT rates | 9% from 2016; 15% from 1 Aug 2023; 12.5% from 1 Aug 2025 | Validate transitional and earlier transactions; review every budget cycle |
| VAT treatments | Standard, zero-rated, exempt, out of scope | Encode supply classifications from legislation with citations |
| Tax invoices and credit notes | Posting validation, immutable identity snapshots, invoice classification, VAT totals, and printable documents implemented against the 1 Aug 2024 regulations | Obtain Fiji tax-practitioner review of classifications, thresholds, wording, and samples before production release |
| VMS/EFD | Not implemented | Build against FRCS protocol and complete accreditation |
| VAT registration | Organisation status, effective date, TIN, and business address captured; taxable invoice posting is blocked unless registration is active | Add turnover monitoring and alerts |
| Income tax | Not implemented | Add workpapers after practitioner-approved specification |
| PAYE/payroll | Not implemented | Version schedules and obligations by effective date |
| Record retention | Audit architecture planned | Confirm periods and immutable storage policy |

Primary sources:

- https://www.laws.gov.fj/Acts/ViewSection/79117
- https://www.laws.gov.fj/Acts/ViewSection/79125
- https://www.frcs.org.fj/wp-content/uploads/2023/11/VAT-Guide-01.11.2-Online-version.pdf
- https://frcs.org.fj/our-services/taxation-section/non-individuals/reporting-and-paying-taxes/vat-guide/
- https://frcs.org.fj/our-services/vat-monitoring-system-vms/vms-faqs/
- https://frcs.org.fj/our-services/vat-monitoring-system-vms/efd-accreditation-instructions/

Tax-document classification snapshots are stamped `FJ-VAT-REGS-2024-08-01` so later legal changes can be introduced without silently rewriting previously issued documents. VMS/EFD integration and accreditation remain a separate release gate.
