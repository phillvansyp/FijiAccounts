using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class JournalPostingAccountingTests
{
    [Fact]
    public async Task PostAsync_WhenAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var account = test.Account("4000");
        account.IsActive = false;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var auditCountBefore =
            await test.Db.AuditEvents.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.Posting.PostAsync(
                        test.UserId,
                        new JournalPostRequest(
                            OrganisationId: test.Organisation.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reference: "INACTIVE-ACCOUNT-001",
                            Description: "Inactive account test",
                            Lines:
                            [
                                new(
                                    test.Account("1000").Id,
                                    "Debit",
                                    100m,
                                    0m),
                                new(
                                    account.Id,
                                    "Credit",
                                    0m,
                                    100m)
                            ])));

        Assert.Contains(
            "active",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            auditCountBefore,
            await test.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task PostAsync_WhenAccountBelongsToAnotherOrganisation_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var otherOrganisation =
            new Organisation
            {
                LegalName = "Other Organisation Limited",
                CountryCode = "FJ",
                BaseCurrency = "FJD",
                TaxLabel = "VAT",
                Kind = OrganisationKind.Business
            };

        test.Db.Organisations.Add(otherOrganisation);

        var otherAccount =
            new LedgerAccount
            {
                OrganisationId = otherOrganisation.Id,
                Organisation = otherOrganisation,
                Code = "9999",
                Name = "Other Organisation Account",
                Type = AccountType.Revenue,
                IsActive = true
            };

        test.Db.LedgerAccounts.Add(otherAccount);

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var auditCountBefore =
            await test.Db.AuditEvents.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Posting.PostAsync(
                    test.UserId,
                    new JournalPostRequest(
                        OrganisationId: test.Organisation.Id,
                        Date: new DateOnly(2026, 8, 20),
                        Reference: "WRONG-TENANT-ACCOUNT-001",
                        Description: "Wrong tenant account test",
                        Lines:
                        [
                            new(
                                test.Account("1000").Id,
                                "Debit",
                                100m,
                                0m),
                            new(
                                otherAccount.Id,
                                "Credit",
                                0m,
                                100m)
                        ])));

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            auditCountBefore,
            await test.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task PostAsync_TrimsReferenceDescriptionAndLineDescriptions()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var journal =
            await test.Posting.PostAsync(
                test.UserId,
                new JournalPostRequest(
                    OrganisationId: test.Organisation.Id,
                    Date: new DateOnly(2026, 8, 20),
                    Reference: "  TRIM-001  ",
                    Description: "  Trimmed journal  ",
                    Lines:
                    [
                        new(
                            test.Account("1000").Id,
                            "  Debit line  ",
                            100m,
                            0m),
                        new(
                            test.Account("4000").Id,
                            "  Credit line  ",
                            0m,
                            100m)
                    ]));

        var stored =
            await test.Db.PostedJournals
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.Id == journal.Id);

        Assert.Equal(
            "TRIM-001",
            stored.Reference);

        Assert.Equal(
            "Trimmed journal",
            stored.Description);

        Assert.Contains(
            stored.Lines,
            x => x.Description == "Debit line");

        Assert.Contains(
            stored.Lines,
            x => x.Description == "Credit line");
    }

    [Fact]
    public async Task PostAsync_SequenceNumbersIncrementWithinOrganisation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var first =
            await PostSimpleAsync(
                test,
                "SEQ-001");

        var second =
            await PostSimpleAsync(
                test,
                "SEQ-002");

        Assert.Equal(
            first.SequenceNumber + 1,
            second.SequenceNumber);
    }

    [Fact]
    public async Task PostAsync_SequenceNumbersAreScopedToOrganisation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var first =
            await PostSimpleAsync(
                test,
                "ORG-SEQ-001");

        var otherOrganisation =
            new Organisation
            {
                LegalName = "Second Sequence Organisation",
                CountryCode = "FJ",
                BaseCurrency = "FJD",
                TaxLabel = "VAT",
                Kind = OrganisationKind.Business
            };

        test.Db.Organisations.Add(otherOrganisation);

        test.Db.OrganisationMemberships.Add(
            new OrganisationMembership
            {
                OrganisationId = otherOrganisation.Id,
                Organisation = otherOrganisation,
                UserId = test.UserId,
                Role = OrganisationRole.Owner
            });

        var accounts =
            FijiStarterChart.For(
                    otherOrganisation.Id)
                .ToList();

        test.Db.LedgerAccounts.AddRange(accounts);

        await test.Db.SaveChangesAsync();

        var bank =
            accounts.Single(x => x.Code == "1000");

        var revenue =
            accounts.Single(x => x.Code == "4000");

        var secondOrganisationJournal =
            await test.Posting.PostAsync(
                test.UserId,
                new JournalPostRequest(
                    OrganisationId: otherOrganisation.Id,
                    Date: new DateOnly(2026, 8, 20),
                    Reference: "ORG-SEQ-OTHER",
                    Description: "Other organisation sequence",
                    Lines:
                    [
                        new(
                            bank.Id,
                            "Debit",
                            100m,
                            0m),
                        new(
                            revenue.Id,
                            "Credit",
                            0m,
                            100m)
                    ]));

        Assert.Equal(
            first.SequenceNumber,
            secondOrganisationJournal.SequenceNumber);
    }

    [Fact]
    public async Task PostAsync_Success_CreatesSingleJournalPostedAudit()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var journal =
            await PostSimpleAsync(
                test,
                "AUDIT-JOURNAL-001");

        var audits =
            await test.Db.AuditEvents
                .AsNoTracking()
                .Where(x =>
                    x.EntityType ==
                        nameof(PostedJournal) &&
                    x.EntityId ==
                        journal.Id.ToString() &&
                    x.EventType ==
                        "JournalPosted")
                .ToListAsync();

        var audit =
            Assert.Single(audits);

        Assert.Equal(
            test.UserId,
            audit.UserId);

        Assert.Equal(
            test.Organisation.Id,
            audit.OrganisationId);

        Assert.Contains(
            "AUDIT-JOURNAL-001",
            audit.JsonData,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAsync_Success_PersistsPostingUserAndDate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var journal =
            await PostSimpleAsync(
                test,
                "POSTING-META-001");

        var stored =
            await test.Db.PostedJournals
                .AsNoTracking()
                .SingleAsync(x => x.Id == journal.Id);

        Assert.Equal(
            test.UserId,
            stored.PostedByUserId);

        Assert.Equal(
            new DateOnly(2026, 8, 20),
            stored.EntryDate);

        Assert.NotEqual(
            default,
            stored.PostedAt);
    }

    private static Task<PostedJournal> PostSimpleAsync(
        AccountingTestDatabase test,
        string reference)
    {
        return test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                OrganisationId: test.Organisation.Id,
                Date: new DateOnly(2026, 8, 20),
                Reference: reference,
                Description: "Journal posting test",
                Lines:
                [
                    new(
                        test.Account("1000").Id,
                        "Debit",
                        100m,
                        0m),
                    new(
                        test.Account("4000").Id,
                        "Credit",
                        0m,
                        100m)
                ]));
    }
}