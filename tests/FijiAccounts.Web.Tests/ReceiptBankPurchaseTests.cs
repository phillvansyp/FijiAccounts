using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ReceiptBankPurchaseTests
{
    private static readonly byte[] Photo = [137,80,78,71,13,10,26,10,0,1];

    private static ReceiptBankPurchaseService CreateService(AccountingTestDatabase t) =>
        new(t.Db, new EmployeeReceiptService(t.Db, new DatabaseImmutableDocumentStore(t.Db)),
            t.BankCoding, t.Reconciliation, t.Purchasing, t.Access);

    private static async Task<(EmployeeReceipt Receipt, BankStatementLine Statement)> PrepareAsync(
        AccountingTestDatabase t, bool coded)
    {
        var date = new DateOnly(2026, 1, 15);
        var receipts = new EmployeeReceiptService(t.Db, new DatabaseImmutableDocumentStore(t.Db));
        var receipt = await receipts.SubmitAsync(t.UserId, t.Organisation.Id, Guid.NewGuid(),
            "Hardware shop", "Site supplies", date, 25m, "FJD", false,
            "receipt.png", Photo, amountsIncludeVat: true);
        var statement = new BankStatementLine {
            OrganisationId = t.Organisation.Id, BankAccountId = t.Account("1000").Id,
            TransactionDate = date, Description = "HARDWARE SHOP EFTPOS", Amount = -25m
        };
        t.Db.BankStatementLines.Add(statement);
        await t.Db.SaveChangesAsync();
        if (coded)
            await t.BankCoding.PostAndReconcileAsync(t.UserId,
                new BankTransactionCodingRequest(t.Organisation.Id, statement.Id,
                    "6500", "Site supplies", VatTreatment.OutOfScope));
        return (receipt, statement);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedBankMatchPostsOnePaidBillAndKeepsReceiptAttachment(bool alreadyCoded)
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var (receipt, statement) = await PrepareAsync(t, alreadyCoded);
        var bill = await CreateService(t).ApprovePostAndPayAsync(t.UserId, t.Organisation.Id,
            receipt.Id, receipt.Version, statement.Id, t.Supplier.Id, t.Account("6500").Id,
            VatTreatment.OutOfScope);

        Assert.Equal(BillStatus.Paid, bill.Status);
        Assert.Equal(25m, bill.AmountPaid);
        Assert.Equal(bill.Id, (await t.Db.EmployeeReceipts.AsNoTracking()
            .SingleAsync(x => x.Id == receipt.Id)).LinkedSupplierBillId);
        Assert.Single(await t.Db.SupplierBills.ToListAsync());
        Assert.Single(await t.Db.SupplierPayments.ToListAsync());
        Assert.NotNull((await t.Db.BankStatementLines.AsNoTracking()
            .SingleAsync(x => x.Id == statement.Id)).ReconciledAt);
        Assert.Single(await t.Db.SupplierBillAttachments
            .Where(x => x.SupplierBillId == bill.Id).ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(t)
            .ApprovePostAndPayAsync(t.UserId, t.Organisation.Id, receipt.Id,
                receipt.Version, statement.Id, t.Supplier.Id, t.Account("6500").Id,
                VatTreatment.OutOfScope));
    }

    [Fact]
    public async Task CompletedReconciliationBlocksConversionWithoutChangingReceiptOrBill()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var (receipt, statement) = await PrepareAsync(t, coded: true);
        t.Db.BankReconciliationSessions.Add(new BankReconciliationSession {
            OrganisationId = t.Organisation.Id, BankAccountId = statement.BankAccountId,
            StatementStartDate = statement.TransactionDate,
            StatementEndDate = statement.TransactionDate,
            IsCompleted = true, CreatedByUserId = t.UserId
        });
        await t.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(t)
            .ApprovePostAndPayAsync(t.UserId, t.Organisation.Id, receipt.Id,
                receipt.Version, statement.Id, t.Supplier.Id, t.Account("6500").Id,
                VatTreatment.OutOfScope));
        Assert.Contains("completed reconciliation", error.Message);
        Assert.Equal("Submitted", (await t.Db.EmployeeReceipts.AsNoTracking()
            .SingleAsync(x => x.Id == receipt.Id)).Status);
        Assert.Empty(await t.Db.SupplierBills.ToListAsync());
        Assert.NotNull((await t.Db.BankStatementLines.AsNoTracking()
            .SingleAsync(x => x.Id == statement.Id)).ReconciledAt);
    }

    [Fact]
    public async Task InvalidVatAfterReopeningCodingRollsBackTheOriginalBankMatch()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var (receipt, statement) = await PrepareAsync(t, coded: true);
        receipt.AmountsIncludeVat = false;
        receipt.Amount = 20m;
        receipt.VatAmount = 5m;
        await t.Db.SaveChangesAsync();
        var originalMatchId = (await t.Db.BankStatementLines.AsNoTracking()
            .SingleAsync(x => x.Id == statement.Id)).MatchedPostedJournalLineId;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(t)
            .ApprovePostAndPayAsync(t.UserId, t.Organisation.Id, receipt.Id,
                receipt.Version, statement.Id, t.Supplier.Id, t.Account("6500").Id,
                VatTreatment.Standard));

        Assert.Equal("Submitted", (await t.Db.EmployeeReceipts.AsNoTracking()
            .SingleAsync(x => x.Id == receipt.Id)).Status);
        Assert.Empty(await t.Db.SupplierBills.ToListAsync());
        Assert.Empty(await t.Db.SupplierPayments.ToListAsync());
        var unchanged = await t.Db.BankStatementLines.AsNoTracking()
            .SingleAsync(x => x.Id == statement.Id);
        Assert.Equal(originalMatchId, unchanged.MatchedPostedJournalLineId);
        Assert.NotNull(unchanged.ReconciledAt);
    }
}
