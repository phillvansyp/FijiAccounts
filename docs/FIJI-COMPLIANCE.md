# Fiji compliance register

This is a living engineering control document, not legal advice. Every rule requires an official source, effective dates, automated tests, and practitioner approval before production release.

| Area | Current implementation | Required next control |
|---|---|---|
| VAT rates | 9% from 2016; 15% from 1 Aug 2023; 12.5% from 1 Aug 2025 | Validate transitional and earlier transactions; review every budget cycle |
| VAT treatments | Standard, zero-rated, exempt, out of scope | Encode supply classifications from legislation with citations |
| Tax invoices | Not implemented | Add statutory fields and threshold rules |
| VMS/EFD | Not implemented | Build against FRCS protocol and complete accreditation |
| VAT registration | Planned organisation compliance profile | Add turnover monitoring and alerts |
| Income tax | Not implemented | Add workpapers after practitioner-approved specification |
| PAYE/payroll | Not implemented | Version schedules and obligations by effective date |
| Record retention | Audit architecture planned | Confirm periods and immutable storage policy |

Primary sources:

- https://frcs.org.fj/our-services/taxation-section/non-individuals/reporting-and-paying-taxes/vat-guide/
- https://frcs.org.fj/our-services/vat-monitoring-system-vms/vms-faqs/
- https://frcs.org.fj/our-services/vat-monitoring-system-vms/efd-accreditation-instructions/
