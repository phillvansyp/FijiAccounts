using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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

        var auditEvents = await test.Db.AuditEvents
            .AsNoTracking()
            .Where(x => x.EntityId == party.Id.ToString())
            .OrderBy(x => x.Id)
            .ToListAsync();

        Assert.Equal(
            ["BusinessPartyCreated", "CustomerDefaultsUpdated", "SupplierDefaultsUpdated"],
            auditEvents.Select(x => x.EventType));
        Assert.All(auditEvents, audit =>
        {
            Assert.Equal(test.Organisation.Id, audit.OrganisationId);
            Assert.Equal(test.UserId, audit.UserId);
            Assert.Equal(nameof(BusinessParty), audit.EntityType);
        });

        using var customerEvidence = JsonDocument.Parse(auditEvents[1].JsonData);
        Assert.Equal(
            "Standard",
            customerEvidence.RootElement.GetProperty("Old").GetProperty("VatTreatment").GetString());
        Assert.Equal(
            "ZeroRated",
            customerEvidence.RootElement.GetProperty("New").GetProperty("VatTreatment").GetString());
        Assert.Equal(
            15,
            customerEvidence.RootElement.GetProperty("New").GetProperty("DueDays").GetInt32());
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
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LearnCustomerSalesDefaultsAsync(
                test.UserId,
                new LearnCustomerSalesDefaultsRequest(
                    test.Organisation.Id,
                    test.Customer.Id,
                    test.Account("4000").Id,
                    VatTreatment.Standard)));

        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task SavingUnchangedDefaults_DoesNotCreateAuditNoise()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyService(test.Db, test.Access);
        var salesAccount = test.Account("4000");
        var expenseAccount = test.Account("6000");
        var party = await service.CreateAsync(
            test.UserId,
            CreateRequest(test.Organisation.Id, salesAccount.Id, expenseAccount.Id));

        await service.UpdateCustomerDefaultsAsync(
            test.UserId,
            new UpdateCustomerDefaultsRequest(
                test.Organisation.Id,
                party.Id,
                salesAccount.Id,
                VatTreatment.Standard,
                PaymentTermType.DaysAfterDocumentDate,
                30));
        await service.UpdateSupplierDefaultsAsync(
            test.UserId,
            new UpdateSupplierDefaultsRequest(
                test.Organisation.Id,
                party.Id,
                expenseAccount.Id,
                VatTreatment.Standard,
                PaymentTermType.DaysAfterDocumentDate,
                30));

        var auditEvents = await test.Db.AuditEvents
            .AsNoTracking()
            .Where(x => x.EntityId == party.Id.ToString())
            .ToListAsync();

        Assert.Single(auditEvents);
        Assert.Equal("BusinessPartyCreated", auditEvents[0].EventType);
    }

    [Fact]
    public async Task InvoiceCoding_LearnsOnlyMissingCustomerDefaultsAndAuditsOnce()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyService(test.Db, test.Access);
        var salesAccount = test.Account("4000");
        var request = CreateRequest(test.Organisation.Id, null, null) with
        {
            Type = PartyType.Customer,
            DefaultSalesVatTreatment = null,
            DefaultPurchaseVatTreatment = null
        };
        var party = await service.CreateAsync(test.UserId, request);

        await service.LearnCustomerSalesDefaultsAsync(
            test.UserId,
            new LearnCustomerSalesDefaultsRequest(
                test.Organisation.Id,
                party.Id,
                salesAccount.Id,
                VatTreatment.ZeroRated));
        await service.LearnCustomerSalesDefaultsAsync(
            test.UserId,
            new LearnCustomerSalesDefaultsRequest(
                test.Organisation.Id,
                party.Id,
                salesAccount.Id,
                VatTreatment.Standard));

        var stored = await test.Db.BusinessParties
            .AsNoTracking()
            .SingleAsync(x => x.Id == party.Id);
        Assert.Equal(salesAccount.Id, stored.DefaultSalesAccountId);
        Assert.Equal(VatTreatment.ZeroRated, stored.DefaultSalesVatTreatment);

        var learnedEvents = await test.Db.AuditEvents
            .AsNoTracking()
            .Where(x =>
                x.EntityId == party.Id.ToString() &&
                x.EventType == "CustomerDefaultsLearnedFromInvoice")
            .ToListAsync();
        var audit = Assert.Single(learnedEvents);
        using var evidence = JsonDocument.Parse(audit.JsonData);
        Assert.Equal(
            salesAccount.Id.ToString(),
            evidence.RootElement.GetProperty("New").GetProperty("SalesAccountId").GetString());
        Assert.Equal(
            "ZeroRated",
            evidence.RootElement.GetProperty("New").GetProperty("VatTreatment").GetString());
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
