using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillAttachmentPostingTests
{
    [Fact]
    public async Task PostBillAsync_WithAttachment_PostsBillAndBothAuditEvents()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        var bill = await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                "93091439",
                new DateOnly(2026, 6, 24),
                new DateOnly(2026, 7, 24),
                [
                    new SupplierBillLineRequest(
                        "Contract",
                        1m,
                        78.26m,
                        VatTreatment.Standard,
                        test.Account("6500").Id)
                ]),
            new SupplierBillAttachmentRequest(
                "FJ_Account_Contract_93091439.pdf",
                "application/pdf",
                4,
                [1, 2, 3, 4],
                false));

        Assert.True(await test.Db.SupplierBills.AnyAsync(x => x.Id == bill.Id));
        Assert.True(await test.Db.SupplierBillAttachments.AnyAsync(x => x.SupplierBillId == bill.Id));
        Assert.Contains(
            await test.Db.AuditEvents.Where(x => x.EntityId == bill.Id.ToString()).ToListAsync(),
            x => x.EventType == "SupplierBillPosted");
        Assert.Contains(
            await test.Db.AuditEvents.Where(x => x.EntityId == bill.Id.ToString()).ToListAsync(),
            x => x.EventType == "SupplierBillDocumentAdded");
    }
}
