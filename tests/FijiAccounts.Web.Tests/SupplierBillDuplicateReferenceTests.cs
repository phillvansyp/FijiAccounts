using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillDuplicateReferenceTests
{
    [Fact]
    public async Task PostBill_WhenActiveBillHasSameSupplierReference_ShowsExistingBill()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var request = Request(test);
        var original = await test.Purchasing.PostBillAsync(test.UserId, request);
        var journalCount = await test.Db.PostedJournals.CountAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => test.Purchasing.PostBillAsync(test.UserId, request));

        Assert.Equal(
            $"Supplier reference 93091439 already exists on {original.BillNumber} for this supplier. Open the existing bill or enter a different reference.",
            exception.Message);
        Assert.Equal(journalCount, await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task Lookup_MatchesCaseAndWhitespaceButIgnoresVoidedBills()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var request = Request(test) with { SupplierReference = "INV-AbC" };
        var bill = await test.Purchasing.PostBillAsync(test.UserId, request);

        var match = await SupplierReferenceDuplicateLookup.FindAsync(
            test.Db, test.Organisation.Id, test.Supplier.Id, " inv-abc ");
        Assert.Equal(bill.Id, match?.Id);

        await test.Purchasing.VoidBillAsync(test.UserId, test.Organisation.Id,
            bill.Id, new DateOnly(2026, 6, 24), "Incorrect VAT amount");
        Assert.Null(await SupplierReferenceDuplicateLookup.FindAsync(
            test.Db, test.Organisation.Id, test.Supplier.Id, "INV-ABC"));
    }

    [Fact]
    public async Task PostBill_WhenVoidedBillHasSameSupplierReference_AllowsCorrection()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var request = Request(test);
        var original = await test.Purchasing.PostBillAsync(test.UserId, request);
        await test.Purchasing.VoidBillAsync(
            test.UserId,
            test.Organisation.Id,
            original.Id,
            new DateOnly(2026, 6, 24),
            "Incorrect VAT amount");

        var corrected = await test.Purchasing.PostBillAsync(test.UserId, request);

        Assert.NotEqual(original.Id, corrected.Id);
        Assert.Equal(original.SupplierReference, corrected.SupplierReference);
        Assert.Equal(78.26m, corrected.Total);
    }

    private static SupplierBillRequest Request(AccountingTestDatabase test) =>
        new(
            test.Organisation.Id,
            test.Supplier.Id,
            "93091439",
            new DateOnly(2026, 6, 24),
            new DateOnly(2026, 7, 24),
            [
                new SupplierBillLineRequest(
                    "Contract",
                    1m,
                    69.56m,
                    VatTreatment.Standard,
                    test.Account("6500").Id)
            ]);
}
