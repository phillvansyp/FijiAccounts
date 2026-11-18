using System.Text;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class FijiManualWorkflowTests
{
    private static readonly DateOnly Date = new(2026, 8, 18);

    [Theory]
    [InlineData(VatTreatment.ZeroRated)]
    [InlineData(VatTreatment.Exempt)]
    public async Task DirectBankSaleAndReversalAppearInFrozenSchedule(VatTreatment treatment)
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var org = t.Organisation.Id;
        var statement = await t.Reconciliation.AddStatementLineAsync(t.UserId,
            new(org, t.Account("1000").Id, Date, "Sale", "BK-1000", 1000m));
        await t.BankCoding.PostAndReconcileAsync(t.UserId, new(org, statement.Id, "4000", "Sale", treatment));
        var workpapers = new VatWorkpaperService(t.Db);
        var service = new FijiVatFilingService(t.Db, t.Access, workpapers, new DatabaseImmutableDocumentStore(t.Db));
        var local = (await workpapers.GetAsync(org, Date, Date)).FilingFigures!;
        Assert.Equal(1000m, treatment == VatTreatment.ZeroRated ? local.ZeroRatedSales : local.ExemptSales);
        Assert.Equal(0m, local.OutputVat);
        var form = FijiVatFilingForm.FromLocal(local);
        await service.SaveAsync(t.UserId, org, Date, Date, local, form, false, 0, false);
        var saved = Assert.Single(await service.HistoryAsync(t.UserId, org, Date, Date));
        var bytes = await service.ReadScheduleAsync(t.UserId, org, saved.Id, default);
        Assert.Contains("1000.00", Encoding.UTF8.GetString(bytes!));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(t.UserId, org, Date, Date, local, form, false, 0, false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadScheduleAsync("other-user", org, saved.Id, default));
        await t.BankCoding.ReopenCodingAsync(t.UserId, org, statement.Id);
        var updated = (await workpapers.GetAsync(org, Date, Date)).FilingFigures!;
        Assert.Equal(0m, updated.ZeroRatedSales + updated.ExemptSales);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(t.UserId, org, Date, Date, local, form, false, saved.Id, false));
        Assert.Equal(bytes, await service.ReadScheduleAsync(t.UserId, org, saved.Id, default));
    }

    [Fact]
    public async Task MatchedZeroRatedReceiptCountsTheInvoiceOnce()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var org = t.Organisation.Id;
        var bank = t.Account("1000").Id;
        var invoice = await t.SalesInvoices.CreateAndPostAsync(t.UserId, new(org, t.Customer.Id, Date, Date,
            [new("Service", 1m, 1000m, VatTreatment.ZeroRated, t.Account("4000").Id)]));
        var statement = await t.Reconciliation.AddStatementLineAsync(t.UserId, new(org, bank, Date, "Payment", "RECEIPT", 1000m));
        await t.CustomerReceipts.RecordAsync(t.UserId, new(org, invoice.Id, Date, "RECEIPT", 1000m, bank, statement.Id));
        var report = await new VatWorkpaperService(t.Db).GetAsync(org, Date, Date);
        Assert.Equal(1000m, report.FilingFigures!.ZeroRatedSales);
        Assert.Equal(t.Customer.Name, Assert.Single(report.LocalSchedule).Source.Party);
        Assert.Empty(report.Reconciliation!.BankEntries);
    }

    [Fact]
    public async Task FiledReturnNeedsApprovalPaymentEvidenceAndPortalAgreement()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var org = t.Organisation.Id;
        var wp = new VatWorkpaperService(t.Db);
        var service = new FijiVatFilingService(t.Db, t.Access, wp, new DatabaseImmutableDocumentStore(t.Db));
        var local = (await wp.GetAsync(org, Date, Date)).FilingFigures!;
        var form = FijiVatFilingForm.FromLocal(local);
        form.PaymentsBasis = true;
        Task Save() => service.SaveAsync(t.UserId, org, Date, Date, local, form, true, 0, true);
        Assert.Contains("approval", (await Assert.ThrowsAsync<InvalidOperationException>(Save)).Message);
        form.ApprovalReference = "approval-1";
        form.ReverseChargePaid = 5;
        Assert.Contains("payment reference", (await Assert.ThrowsAsync<InvalidOperationException>(Save)).Message);
        form.PaymentEvidence = "payment-1";
        Assert.Contains("filing reference", (await Assert.ThrowsAsync<InvalidOperationException>(Save)).Message);
        form.FilingReference = "return-1";
        form.FiledOn = Date;
        Assert.Contains("Reconcile", (await Assert.ThrowsAsync<InvalidOperationException>(Save)).Message);
        form.ReverseChargeOnPortal = 5;
        await Save();
        var saved = Assert.Single(await service.HistoryAsync(t.UserId, org, Date, Date));
        Assert.True(saved.Snapshot.Filed);
        Assert.Equal(-5m, saved.Snapshot.Return.NetVat);
        Assert.Equal("return-1", saved.Snapshot.Return.FilingReference);
    }

    [Fact]
    public async Task ExternalReceiptCorrectionsKeepHistoryAndPreventDuplicateLinks()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var org = t.Organisation.Id;
        var service = new ExternalFiscalReceiptService(t.Db, t.Access);
        var request = new SalesInvoiceRequest(org, t.Customer.Id, Date, Date,
            [new("Service", 1m, 50m, VatTreatment.Standard, t.Account("4000").Id)]);
        var invoice = await t.SalesInvoices.CreateAndPostAsync(t.UserId, request);
        var second = await t.SalesInvoices.CreateAndPostAsync(t.UserId, request);
        var receipt = new ExternalFiscalReceipt("POS-1", "R-1", Date, "");
        await service.SaveAsync(t.UserId, org, invoice.Id, false, 0, receipt);
        var saved = Assert.Single(await service.ReadAsync(t.UserId, org, invoice.Id, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(t.UserId, org, second.Id, false, 0, receipt));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(t.UserId, org, invoice.Id, false, saved.Id, receipt));
        await service.SaveAsync(t.UserId, org, invoice.Id, false, saved.Id, receipt with { ReceiptNumber = "R-2", Note = "Corrected receipt number" });
        Assert.Equal(2, (await service.ReadAsync(t.UserId, org, invoice.Id, false)).Count);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadAsync("other-user", org, invoice.Id, false));
    }

    [Fact]
    public void StatementCoverageChecksDatesAndSignedBalance()
    {
        var coverage = new StatementCoverage { Start = Date, End = Date, Opening = 100, Closing = 80 };
        StatementPreviewLine[] rows = [new(Date, "Purchase", null, -20m)];
        Assert.Null(coverage.Check(rows));
        coverage.Closing = 75;
        Assert.Contains("5.00", coverage.Check(rows));
        coverage.Start = Date.AddDays(1);
        Assert.Contains("outside", coverage.Check(rows));
        coverage.End = null;
        Assert.Contains("both", coverage.Check(rows));
    }
}
