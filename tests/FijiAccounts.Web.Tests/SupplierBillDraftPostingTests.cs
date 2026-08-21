using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillDraftPostingTests
{
    [Fact]
    public async Task PostDraftBillAsync_PostsBillAndRemovesDraft()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var draft =
            new SupplierBillDraft
            {
                OrganisationId =
                    test.Organisation.Id,

                SupplierId =
                    test.Supplier.Id,

                SupplierReference =
                    "DRAFT-POST-001",

                BillDate =
                    new DateOnly(
                        2026,
                        8,
                        20),

                DueDate =
                    new DateOnly(
                        2026,
                        9,
                        19),

                Description =
                    "Draft office supplies",

                Quantity =
                    1m,

                UnitPrice =
                    100m,

                VatTreatment =
                    VatTreatment.Standard,

                ExpenseAccountId =
                    test.Account("6500").Id,

                CreatedByUserId =
                    test.UserId
            };

        test.Db.SupplierBillDrafts.Add(draft);

        await test.Db.SaveChangesAsync();

        var bill =
            await test.Purchasing.PostDraftBillAsync(
                test.UserId,
                draft.Id,
                new SupplierBillRequest(
                    OrganisationId:
                        test.Organisation.Id,

                    SupplierId:
                        test.Supplier.Id,

                    SupplierReference:
                        "DRAFT-POST-001",

                    BillDate:
                        new DateOnly(
                            2026,
                            8,
                            20),

                    DueDate:
                        new DateOnly(
                            2026,
                            9,
                            19),

                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description:
                                "Draft office supplies",

                            Quantity:
                                1m,

                            UnitPrice:
                                100m,

                            VatTreatment:
                                VatTreatment.Standard,

                            ExpenseAccountId:
                                test.Account("6500").Id)
                    ]));

        Assert.NotEqual(
            Guid.Empty,
            bill.Id);

        Assert.NotEqual(
            Guid.Empty,
            bill.PostedJournalId);

        Assert.Equal(
            112.50m,
            bill.Total);

        Assert.False(
            await test.Db.SupplierBillDrafts
                .AnyAsync(
                    x => x.Id == draft.Id));

        Assert.True(
            await test.Db.SupplierBills
                .AnyAsync(
                    x => x.Id == bill.Id));
    }
}