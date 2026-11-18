using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class FijiVatReconciliationTests
{
    private static readonly DateOnly Date = new(2026, 8, 18);

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task DirectBankCodingIsIncludedOnceAndReopeningOffsetsIt(int direction)
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var org = t.Organisation.Id;
        var statement = await t.Reconciliation.AddStatementLineAsync(t.UserId,
            new(org, t.Account("1000").Id, Date, "Taxable bank transaction", "BANK-TEST", direction * 112.50m));
        await t.BankCoding.PostAndReconcileAsync(t.UserId,
            new(org, statement.Id, direction < 0 ? "6500" : "4000", "Bank coding", VatTreatment.Standard));

        var service = new VatWorkpaperService(t.Db);
        var report = await service.GetAsync(org, Date, Date);
        var bank = Assert.Single(report.Reconciliation!.BankEntries);
        Assert.Equal(100m, bank.Net);
        Assert.Equal(12.50m, bank.Source.Tax);
        Assert.Equal(direction < 0, bank.Source.IsInput);
        Assert.Equal(12.50m, direction < 0 ? report.InputTax : report.OutputTax);
        Assert.Empty(report.Reconciliation.Differences);
        Assert.Empty((await service.GetAsync(org, Date.AddDays(1), Date.AddDays(1))).Reconciliation!.BankEntries);

        await t.BankCoding.ReopenCodingAsync(t.UserId, org, statement.Id);
        var reopened = await service.GetAsync(org, Date, Date);
        Assert.Equal(0m, reopened.InputTax);
        Assert.Equal(0m, reopened.OutputTax);
        Assert.Equal(2, reopened.Reconciliation!.BankEntries.Count);
        Assert.Empty(reopened.Reconciliation.Differences);
    }

    [Fact]
    public async Task StoredXeroCreditsFlagUnverifiedCoverageWithoutAddingLocalVat()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var org = t.Organisation.Id;
        var connection = new XeroConnection
        {
            OrganisationId = org, IsActive = false, ProtectedAccessToken = "", ProtectedRefreshToken = "",
            GrantedScopes = "", ConnectedByUserId = t.UserId, UpdatedByUserId = t.UserId
        };
        t.Db.XeroMirrorRecords.Add(new XeroMirrorRecord
        {
            OrganisationId = org, Connection = connection, DatasetKey = "supplier-credits",
            ExternalId = "credit-1", DisplayName = "Credit", SourceHash = "1", SourceJson = "{}"
        });
        await t.Db.SaveChangesAsync();
        var report = await new VatWorkpaperService(t.Db).GetAsync(org, Date, Date);
        Assert.True(report.Reconciliation!.HasXeroEvidence);
        Assert.Equal(0m, report.InputTax);
        Assert.Empty(report.Reconciliation.Differences);
    }

    [Fact]
    public async Task MatchingAPaidBillCountsVatOnceAndShowsManualControlMovementsAsDifferences()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var org = t.Organisation.Id;
        var bank = t.Account("1000").Id;
        var bill = await t.Purchasing.PostBillAsync(t.UserId, new(org, t.Supplier.Id, "VAT-BILL", Date, Date,
            [new("Office supplies", 1, 100m, VatTreatment.Standard, t.Account("6500").Id)]));
        var payment = await t.Purchasing.PayBillAsync(t.UserId, new(org, bill.Id, Date, "Paid bill", 112.50m, bank));
        var statement = await t.Reconciliation.AddStatementLineAsync(t.UserId, new(org, bank, Date, "Office supplies", "MATCH", -112.50m));
        var journal = await t.LoadJournalAsync(payment.PostedJournalId);
        await t.Reconciliation.ReconcileAsync(t.UserId, org, statement.Id, journal.Lines.Single(x => x.LedgerAccountId == bank).Id);

        var service = new VatWorkpaperService(t.Db);
        var billOnly = await service.GetAsync(org, Date, Date);
        Assert.Equal(12.50m, billOnly.InputTax);
        Assert.Equal(12.50m, billOnly.Reconciliation!.LedgerInput);
        Assert.Empty(billOnly.Reconciliation.BankEntries);
        Assert.Empty(billOnly.Reconciliation.Differences);

        var manual = await t.Posting.PostAsync(t.UserId, new(org, Date, "MANUAL-VAT", "Manual adjustment",
            [new(t.Account("1150").Id, "VAT adjustment", 2m, 0m), new(t.Account("6500").Id, "Expense adjustment", 0m, 2m)]));
        var adjusted = await service.GetAsync(org, Date, Date);
        Assert.Equal(12.50m, adjusted.InputTax);
        Assert.Equal(14.50m, adjusted.Reconciliation!.LedgerInput);
        var diff = Assert.Single(adjusted.Reconciliation.Differences);
        Assert.Equal(manual.Id, diff.Source.JournalId);
        Assert.Equal(2m, diff.Difference);
    }
}
