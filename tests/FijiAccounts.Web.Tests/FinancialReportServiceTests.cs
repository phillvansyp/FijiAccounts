using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class FinancialReportServiceTests
{
    [Fact]
    public async Task GetAsync_WhenDateRangeIsInvalid_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var reports =
            new FinancialReportService(test.Db);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    reports.GetAsync(
                        test.Organisation.Id,
                        new DateOnly(2026, 8, 31),
                        new DateOnly(2026, 8, 1)));

        Assert.Contains(
            "start date cannot be after the end date",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_WhenOrganisationHasNoPostings_ReturnsEmptyReport()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var reports =
            new FinancialReportService(test.Db);

        var report =
            await reports.GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31));

        Assert.Empty(report.Balances);
        Assert.Empty(report.TrialBalance);
    }

    [Fact]
    public async Task GetAsync_DoesNotIncludePostingsFromAnotherOrganisation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var otherOrganisation =
            new Organisation
            {
                LegalName = "Other Reporting Organisation Limited",
                CountryCode = "FJ",
                BaseCurrency = "FJD",
                TaxLabel = "VAT",
                Kind = OrganisationKind.Business
            };

        test.Db.Organisations.Add(otherOrganisation);

        var otherBank =
            new LedgerAccount
            {
                OrganisationId = otherOrganisation.Id,
                Organisation = otherOrganisation,
                Code = "1000",
                Name = "Other Bank",
                Type = AccountType.Asset,
                IsBankAccount = true,
                IsActive = true
            };

        var otherRevenue =
            new LedgerAccount
            {
                OrganisationId = otherOrganisation.Id,
                Organisation = otherOrganisation,
                Code = "4000",
                Name = "Other Revenue",
                Type = AccountType.Revenue,
                IsActive = true
            };

        test.Db.LedgerAccounts.AddRange(
            otherBank,
            otherRevenue);

        await test.Db.SaveChangesAsync();

        var journal =
            new PostedJournal
            {
                OrganisationId = otherOrganisation.Id,
                EntryDate = new DateOnly(2026, 8, 18),
                Reference = "OTHER-REPORT-001",
                Description = "Other organisation revenue",
                PostedAt = DateTimeOffset.UtcNow,
                PostedByUserId = test.UserId,
                Lines =
                [
                    new PostedJournalLine
{
    LedgerAccountId = otherBank.Id,
    Description = "Other organisation bank",
    Debit = 100m,
    Credit = 0m
},
new PostedJournalLine
{
    LedgerAccountId = otherRevenue.Id,
    Description = "Other organisation revenue",
    Debit = 0m,
    Credit = 100m
}
                ]
            };

        test.Db.PostedJournals.Add(journal);
        await test.Db.SaveChangesAsync();

        var reports =
            new FinancialReportService(test.Db);

        var report =
            await reports.GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31));

        Assert.Empty(report.Balances);
        Assert.Empty(report.TrialBalance);

        Assert.True(
            await test.Db.PostedJournals
                .AsNoTracking()
                .AnyAsync(x =>
                    x.Id == journal.Id &&
                    x.OrganisationId == otherOrganisation.Id));
    }
}
