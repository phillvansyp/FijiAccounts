using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ReportTransactionTests
{
    [Fact]
    public async Task Attachments_AppearOnBillAndReversalWithExactOpenLinks()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bill = await test.Purchasing.PostBillAsync(test.UserId, new(test.Organisation.Id, test.Supplier.Id, "ATTACHED", new(2026, 8, 1), new(2026, 8, 31), [new("Office expense", 1, 40, VatTreatment.Standard, test.Account("6000").Id)]));
        var file = new SupplierBillAttachment { OrganisationId = test.Organisation.Id, SupplierBillId = bill.Id, FileName = "supplier-invoice.pdf", ContentType = "application/pdf", OriginalSize = 3, StoredSize = 3, Content = [1, 2, 3], UploadedByUserId = test.UserId };
        test.Db.SupplierBillAttachments.Add(file);
        await test.Db.SaveChangesAsync();
        await test.Purchasing.VoidBillAsync(test.UserId, test.Organisation.Id, bill.Id, new(2026, 8, 2), "Correct bill");
        var result = await new ReportTransactionService(test.Db, test.Access).GetAsync(test.UserId, test.Organisation.Id, "6000", new(2026, 8, 1), new(2026, 8, 31));
        Assert.Equal(2, result.Transactions.Count);
        foreach (var row in result.Transactions)
        {
            var attachment = Assert.Single(row.Attachments);
            Assert.Equal(file.FileName, attachment.FileName);
            Assert.Equal($"/api/o/{test.Organisation.Id}/purchases/{bill.Id}/attachments/{file.Id}", attachment.Url);
        }
    }

    [Fact]
    public async Task BankStatementAttachment_IsShownOnceForMatchedPosting()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var journal = await test.Posting.PostAsync(test.UserId, new(test.Organisation.Id, new(2026, 8, 10), "BANK-FEE", "Bank charge", [new(test.Account("6000").Id, "Fee", 25, 0), new(bank.Id, "Bank", 0, 25)]));
        var bankLine = await test.Db.PostedJournalLines.SingleAsync(x => x.PostedJournalId == journal.Id && x.LedgerAccountId == bank.Id);
        var batch = Guid.NewGuid();
        test.Db.BankStatementLines.Add(new BankStatementLine { OrganisationId = test.Organisation.Id, BankAccountId = bank.Id, TransactionDate = new(2026, 8, 10), Description = "Fee", Amount = -25, ImportBatchId = batch, MatchedPostedJournalLineId = bankLine.Id });
        test.Db.BankStatementImportDocuments.Add(new BankStatementImportDocument { OrganisationId = test.Organisation.Id, BankAccountId = bank.Id, ImportBatchId = batch, FileName = "august-statement.pdf", ContentType = "application/pdf", OriginalSize = 3, Content = [1, 2, 3], UploadedByUserId = test.UserId });
        await test.Db.SaveChangesAsync();
        var result = await new ReportTransactionService(test.Db, test.Access).GetAsync(test.UserId, test.Organisation.Id, "6000", new(2026, 8, 1), new(2026, 8, 31));
        var attachment = Assert.Single(Assert.Single(result.Transactions).Attachments);
        Assert.Equal("august-statement.pdf", attachment.FileName);
        Assert.Equal($"/api/o/{test.Organisation.Id}/banking/imports/{batch}/statement", attachment.Url);
    }

    [Fact]
    public async Task RestrictedReader_SeesOnlyPermittedDivisionTransactions()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var request = new SalesInvoiceRequest(test.Organisation.Id, test.Customer.Id, new(2026, 8, 1), new(2026, 8, 31), [new("Allowed work", 1, 100, VatTreatment.Standard, test.Account("4000").Id)]);
        var allowed = await test.SalesInvoices.CreateAndPostAsync(test.UserId, request);
        var branch = new Branch { OrganisationId = test.Organisation.Id, Code = "PRIVATE", Name = "Other branch" };
        var division = new Division { BranchId = branch.Id, Code = "PRIVATE", Name = "Other division" };
        test.Db.Branches.Add(branch); test.Db.Divisions.Add(division); await test.Db.SaveChangesAsync();
        await test.SalesInvoices.CreateAndPostAsync(test.UserId, request with { BranchId = branch.Id, DivisionId = division.Id });
        var membership = await test.Db.OrganisationMemberships.SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.UserId == test.UserId);
        membership.Role = OrganisationRole.ReadOnly; membership.DimensionAccessMode = DimensionAccessMode.Restricted;
        test.Db.OrganisationDimensionAccessGrants.Add(new OrganisationDimensionAccessGrant { OrganisationId = test.Organisation.Id, UserId = test.UserId, BranchId = allowed.BranchId!.Value, DivisionId = allowed.DivisionId });
        await test.Db.SaveChangesAsync();
        var service = new ReportTransactionService(test.Db, test.Access);
        var result = await service.GetAsync(test.UserId, test.Organisation.Id, "4000", new(2026, 8, 1), new(2026, 8, 31));
        Assert.Equal(100, result.Total);
        Assert.Equal(allowed.PostedJournalId, Assert.Single(result.Transactions).JournalId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(test.UserId, test.Organisation.Id, "4000", new(2026, 8, 1), new(2026, 8, 31), $"branch:{branch.Id}"));
    }

    [Fact]
    public async Task ClickedMonth_IncludesOnlyThatMonthAndLinksToInvoice()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var july = await test.SalesInvoices.CreateAndPostAsync(test.UserId, new(test.Organisation.Id, test.Customer.Id, new(2026, 7, 31), new(2026, 8, 31), [new("July work", 1, 100, VatTreatment.Standard, test.Account("4000").Id)]));
        var august = await test.SalesInvoices.CreateAndPostAsync(test.UserId, new(test.Organisation.Id, test.Customer.Id, new(2026, 8, 1), new(2026, 8, 31), [new("August work", 1, 200, VatTreatment.Standard, test.Account("4000").Id)]));
        var service = new ReportTransactionService(test.Db, test.Access);
        var result = await service.GetAsync(test.UserId, test.Organisation.Id, "4000", new(2026, 7, 1), new(2026, 7, 31));
        Assert.Equal(100, result.Total);
        var row = Assert.Single(result.Transactions);
        Assert.Equal(july.PostedJournalId, row.JournalId);
        Assert.EndsWith($"/sales/{july.Id}", row.SourceUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(test.Customer.Name, row.Contact);
        Assert.Empty(row.Attachments);
        var next = await service.GetAsync(test.UserId, test.Organisation.Id, "4000", new(2026, 8, 1), new(2026, 8, 31));
        Assert.Equal(200, next.Total);
        Assert.EndsWith($"/sales/{august.Id}", Assert.Single(next.Transactions).SourceUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpenseAndReversal_MatchReportSignsAndLinkOriginalBill()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bill = await test.Purchasing.PostBillAsync(test.UserId, new(test.Organisation.Id, test.Supplier.Id, "RENT-JUL", new(2026, 7, 15), new(2026, 8, 15), [new("Rent", 1, 40, VatTreatment.Standard, test.Account("6000").Id)]));
        await test.Purchasing.VoidBillAsync(test.UserId, test.Organisation.Id, bill.Id, new(2026, 8, 5), "Correct rent");
        var service = new ReportTransactionService(test.Db, test.Access);
        var july = await service.GetAsync(test.UserId, test.Organisation.Id, "6000", new(2026, 7, 1), new(2026, 7, 31));
        var august = await service.GetAsync(test.UserId, test.Organisation.Id, "6000", new(2026, 8, 1), new(2026, 8, 31));
        Assert.Equal(40, july.Total);
        Assert.Equal(-40, august.Total);
        Assert.EndsWith($"/purchases/{bill.Id}", Assert.Single(august.Transactions).SourceUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reversal", august.Transactions[0].SourceLabel);
        var report = await new FinancialReportService(test.Db).GetAsync(test.Organisation.Id, new(2026, 8, 1), new(2026, 8, 31));
        Assert.Equal(report.Balances.Single(x => x.Code == "6000").DisplayAmount, august.Total);
    }

    [Fact]
    public async Task NetZeroStillShowsBothTransactions_AndManualEntriesHaveJournalEvidence()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var journal = await test.Posting.PostAsync(test.UserId, new(test.Organisation.Id, new(2026, 8, 10), "MANUAL", "Expense correction", [new(test.Account("6000").Id, "Office costs", 25, 0), new(test.Account("1000").Id, "Cash", 0, 25)]));
        await test.Posting.PostAsync(test.UserId, new(test.Organisation.Id, new(2026, 8, 11), "REVERSE", "Reverse expense", [new(test.Account("6000").Id, "Reverse costs", 0, 25), new(test.Account("1000").Id, "Cash", 25, 0)]));
        var result = await new ReportTransactionService(test.Db, test.Access).GetAsync(test.UserId, test.Organisation.Id, "6000", new(2026, 8, 1), new(2026, 8, 31));
        Assert.Equal(0, result.Total);
        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal("Journal entry", result.Transactions[0].SourceLabel);
        Assert.EndsWith($"/journals/{journal.Id}", result.Transactions[0].SourceUrl);
    }

    [Fact]
    public async Task TrackingAndPermissions_CannotBroadenAccountAccess()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await test.SalesInvoices.CreateAndPostAsync(test.UserId, new(test.Organisation.Id, test.Customer.Id, new(2026, 8, 1), new(2026, 8, 31), [new("Work", 1, 100, VatTreatment.Standard, test.Account("4000").Id)]));
        var service = new ReportTransactionService(test.Db, test.Access);
        var scoped = await service.GetAsync(test.UserId, test.Organisation.Id, "4000", new(2026, 8, 1), new(2026, 8, 31), $"division:{invoice.DivisionId}");
        Assert.Equal(100, scoped.Total);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync("unrelated-user", test.Organisation.Id, "4000", new(2026, 8, 1), new(2026, 8, 31)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(test.UserId, test.Organisation.Id, "4000", new(2026, 8, 1), new(2026, 8, 31), $"division:{Guid.NewGuid()}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync(test.UserId, test.Organisation.Id, "unknown", new(2026, 8, 1), new(2026, 8, 31)));
        var empty = await service.GetAsync(test.UserId, test.Organisation.Id, "4000", new(2026, 6, 1), new(2026, 6, 30));
        Assert.Empty(empty.Transactions);
        Assert.Equal(0, empty.Total);
    }
}
