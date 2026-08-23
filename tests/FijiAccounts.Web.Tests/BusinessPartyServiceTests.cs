using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BusinessPartyServiceTests
{
    [Fact]
    public async Task Manager_CanCreateContactAndUpdateDefaults()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyService(test.Db, test.Access);
        var salesAccount = test.Account("4000");
        var expenseAccount = test.Account("6000");

        var party =
            await service.CreateAsync(
                test.UserId,
                CreateRequest(
                    test.Organisation.Id,
                    salesAccount.Id,
                    expenseAccount.Id));
        await service.UpdateCustomerDefaultsAsync(
            test.UserId,
            new UpdateCustomerDefaultsRequest(
                test.Organisation.Id,
                party.Id,
                salesAccount.Id,
                VatTreatment.ZeroRated,
                PaymentTermType.DayOfFollowingMonth,
                15));
        await service.UpdateSupplierDefaultsAsync(
            test.UserId,
            new UpdateSupplierDefaultsRequest(
                test.Organisation.Id,
                party.Id,
                expenseAccount.Id,
                VatTreatment.Standard,
                PaymentTermType.EndOfFollowingMonth,
                0));

        var stored =
            await test.Db.BusinessParties
                .AsNoTracking()
                .SingleAsync(x => x.Id == party.Id);

        Assert.Equal("Combined Contact", stored.Name);
        Assert.Equal("contact@example.com", stored.Email);
        Assert.Equal(PartyType.Customer | PartyType.Supplier, stored.Type);
        Assert.Equal(PaymentTermType.DayOfFollowingMonth, stored.DefaultSalesInvoicePaymentTermType);
        Assert.Equal(15, stored.DefaultSalesInvoiceDueDays);
        Assert.Equal(salesAccount.Id, stored.DefaultSalesAccountId);
        Assert.Equal(VatTreatment.ZeroRated, stored.DefaultSalesVatTreatment);
        Assert.Equal(expenseAccount.Id, stored.DefaultPurchaseAccountId);
        Assert.Equal(VatTreatment.Standard, stored.DefaultPurchaseVatTreatment);
        Assert.Equal(PaymentTermType.EndOfFollowingMonth, stored.DefaultSupplierBillPaymentTermType);
    }

    [Fact]
    public async Task ReadOnlyMember_CannotMutateContactsThroughService()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyService(test.Db, test.Access);

        await test.Db.OrganisationMemberships
            .Where(x =>
                x.UserId == test.UserId &&
                x.OrganisationId == test.Organisation.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateAsync(
                test.UserId,
                CreateRequest(test.Organisation.Id, null, null)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateCustomerDefaultsAsync(
                test.UserId,
                new UpdateCustomerDefaultsRequest(
                    test.Organisation.Id,
                    test.Customer.Id,
                    null,
                    null,
                    PaymentTermType.DaysAfterDocumentDate,
                    7)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateSupplierDefaultsAsync(
                test.UserId,
                new UpdateSupplierDefaultsRequest(
                    test.Organisation.Id,
                    test.Supplier.Id,
                    null,
                    null,
                    PaymentTermType.DaysAfterDocumentDate,
                    7)));
    }

    [Fact]
    public async Task Create_RejectsPurchaseAccountOutsideOrganisation()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyService(test.Db, test.Access);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(
                    test.UserId,
                    CreateRequest(
                        test.Organisation.Id,
                        null,
                        Guid.NewGuid())));

        Assert.Contains(
            "from this organisation",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static CreateBusinessPartyRequest CreateRequest(
        Guid organisationId,
        Guid? salesAccountId,
        Guid? purchaseAccountId) =>
        new(
            organisationId,
            " Combined Contact ",
            " contact@example.com ",
            " TIN-123 ",
            PartyType.Customer | PartyType.Supplier,
            salesAccountId,
            VatTreatment.Standard,
            purchaseAccountId,
            VatTreatment.Standard,
            PaymentTermType.DaysAfterDocumentDate,
            30,
            PaymentTermType.DaysAfterDocumentDate,
            30);
}
