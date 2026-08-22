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

## Compatibility

The enterprise-structure migration creates one group for each existing
organisation. Existing flat branches become `Branch` records, and existing
departments become divisions of the default branch. Where no branch or division
exists, the migration creates `Main Branch` and `General` defaults.

The legacy `OrganisationUnit` records remain in place during the transition so
existing settings screens continue to work. They can be retired after the new
branch/division management UI and financial-document dimensions are complete.

## Next phases

1. ~~Replace flat organisation-unit settings with hierarchy management.~~
2. ~~Add group administration and multi-company creation.~~
3. Add branch and division assignments to financial documents.
4. Add explicit branch/division access grants.
5. Add scoped and consolidated reporting.
