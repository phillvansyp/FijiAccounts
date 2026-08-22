# Enterprise structure

The enterprise hierarchy extends the existing tenant model:

```text
OrganisationGroup
    Organisation (company/legal entity)
        Branch
            Division
```

`Organisation` remains the company entity and accounting tenant. It already owns
the legal identity, tax jurisdiction, base currency, chart of accounts,
memberships, and every financial record. Introducing a second `Company` table
would duplicate that boundary and require moving all existing tenant keys without
adding a distinct domain concept.

## Security boundary

Memberships and accountant engagements continue to grant access to an individual
`Organisation`. Belonging to the same `OrganisationGroup` does not grant access to
another company. Explicit group Owner, Administrator and Viewer roles control the
group structure independently from company-ledger access.

Company owners and administrators always retain access to every branch and
division. Other direct members default to all dimensions for compatibility, but
can be changed to restricted access with branch-wide or individual-division
grants. Restricted selections are enforced when posting and when opening core
sales, purchasing and journal records. Accountant engagements continue to use
their company-level engagement access until engagement-specific dimension grants
are introduced.

Profit & Loss, Balance Sheet and Trial Balance reports can be run for all
permitted transactions, one branch, or one division. The scope is applied to
posted journal lines for both the selected period and its comparison period, and
restricted members cannot select dimensions outside their grants.

## Compatibility

The enterprise-structure migration creates one group for each existing
organisation. Existing flat branches become `Branch` records, and existing
departments become divisions of the default branch. Where no branch or division
exists, the migration creates `Main Branch` and `General` defaults.

The legacy `OrganisationUnit` records remain in place during the transition so
existing settings screens continue to work. They can be retired after the new
branch/division management UI and financial-document dimensions are complete.

## Transaction dimensions

Posted journal lines are the authoritative source for branch and division
reporting. Every new posting receives an active branch and division, defaulting
to Main Branch and General when no dimension is selected. A journal can allocate
individual lines to different divisions, and reversals preserve the original
allocations. Existing journal lines are backfilled to their company's default
branch and division.

Sales invoices and supplier bills now capture a selected branch and division,
including while they are drafts. Their receipts, supplier payments, credit
notes, voids and reversals inherit that source-document dimension. Existing
documents are backfilled to the company default (or from their related source
document for receipts and payments). Recurring templates still use the default
dimension until template-level allocation is added.

## Next phases

1. ~~Replace flat organisation-unit settings with hierarchy management.~~
2. ~~Add group administration and multi-company creation.~~
3. Add branch and division selectors to each financial document workflow. The
   shared journal dimension, manual journals, sales invoices, supplier bills,
   receipts, payments and related credits/reversals are complete. Recurring
   templates and the remaining banking/inventory workflows are still pending.
4. ~~Add explicit branch/division access grants.~~
5. Add scoped and consolidated reporting. Branch/division financial statements
   are complete; multi-company group consolidation remains pending.
