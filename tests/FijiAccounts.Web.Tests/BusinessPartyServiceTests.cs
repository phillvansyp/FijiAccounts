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
                0,
                " VAT-5599 "));

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
        Assert.Equal("VAT-5599", stored.VatRegistrationNumber);
        var supplierAccount = await test.Db.SupplierAccountProfiles
            .AsNoTracking()
            .SingleAsync(x => x.SupplierId == stored.Id);
        Assert.Equal("SUP-001", supplierAccount.AccountNumber);
        Assert.Equal("Primary", supplierAccount.Label);
        Assert.True(supplierAccount.IsDefault);

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
    public async Task Supplier_CanHaveMultipleLabelledAccountNumbers()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyService(test.Db, test.Access);
        var party = await service.CreateAsync(
            test.UserId,
            CreateRequest(test.Organisation.Id, test.Account("4000").Id, test.Account("6000").Id));
        var second = await service.AddSupplierAccountAsync(
            test.UserId,
            new SupplierAccountProfileRequest(
                test.Organisation.Id,
                party.Id,
                "Nadi",
                "SUP-002",
                true));

        var accounts = await test.Db.SupplierAccountProfiles
            .AsNoTracking()
            .Where(x => x.SupplierId == party.Id)
            .OrderBy(x => x.AccountNumber)
            .ToListAsync();
        Assert.Equal(2, accounts.Count);
        Assert.False(accounts[0].IsDefault);
        Assert.True(accounts[1].IsDefault);

        await service.DeleteSupplierAccountAsync(
            test.UserId,
            test.Organisation.Id,
            party.Id,
            second.Id);
        var remaining = await test.Db.SupplierAccountProfiles
            .AsNoTracking()
            .SingleAsync(x => x.SupplierId == party.Id);
        Assert.Equal("SUP-001", remaining.AccountNumber);
        Assert.True(remaining.IsDefault);
    }

    [Fact]
    public async Task SupplierBankDetails_RequireIndependentVerificationAndMaskAuditEvidence()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyService(test.Db, test.Access);
        var submitted = await service.SubmitSupplierBankAccountAsync(
            test.UserId,
            new SupplierBankAccountRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                "Test Supplier Limited",
                "Bank of Fiji",
                "01-2345-6789012-00"));

        Assert.False(submitted.IsVerified);
        Assert.False(submitted.IsDefault);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.VerifySupplierBankAccountAsync(
                test.UserId,
                test.Organisation.Id,
                test.Supplier.Id,
                submitted.Id));

        var verifier = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "bank-verifier@example.com",
            NormalizedUserName = "BANK-VERIFIER@EXAMPLE.COM",
            Email = "bank-verifier@example.com",
            NormalizedEmail = "BANK-VERIFIER@EXAMPLE.COM"
        };
        test.Db.Users.Add(verifier);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = verifier.Id,
            Role = OrganisationRole.Administrator
        });
        await test.Db.SaveChangesAsync();

        await service.VerifySupplierBankAccountAsync(
            verifier.Id,
            test.Organisation.Id,
            test.Supplier.Id,
            submitted.Id);

        var stored = await test.Db.SupplierBankAccounts
            .AsNoTracking()
            .SingleAsync(x => x.Id == submitted.Id);
        Assert.True(stored.IsVerified);
        Assert.True(stored.IsDefault);
        Assert.Equal(verifier.Id, stored.VerifiedByUserId);

        var audit = await test.Db.AuditEvents
            .AsNoTracking()
            .Where(x => x.EntityId == test.Supplier.Id.ToString() &&
                        x.EventType.StartsWith("SupplierBankAccount"))
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Equal(
            ["SupplierBankAccountSubmitted", "SupplierBankAccountVerified"],
            audit.Select(x => x.EventType));
        Assert.All(audit, x =>
        {
            using var evidence = JsonDocument.Parse(x.JsonData);
            Assert.Equal(
                "•••• 1200",
                evidence.RootElement.GetProperty("AccountNumber").GetString());
            Assert.DoesNotContain("01-2345-6789012-00", x.JsonData);
        });
    }

    [Fact]
    public async Task SupplierBankDetails_RejectDuplicateAndUnauthorisedVerification()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyService(test.Db, test.Access);
        var account = await service.SubmitSupplierBankAccountAsync(
            test.UserId,
            new SupplierBankAccountRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                "Test Supplier",
                null,
                "1234 5678"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitSupplierBankAccountAsync(
                test.UserId,
                new SupplierBankAccountRequest(
                    test.Organisation.Id,
                    test.Supplier.Id,
                    "Test Supplier",
                    null,
                    "1234-5678")));

        var bookkeeper = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "bank-bookkeeper@example.com",
            NormalizedUserName = "BANK-BOOKKEEPER@EXAMPLE.COM",
            Email = "bank-bookkeeper@example.com",
            NormalizedEmail = "BANK-BOOKKEEPER@EXAMPLE.COM"
        };
        test.Db.Users.Add(bookkeeper);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = bookkeeper.Id,
            Role = OrganisationRole.Bookkeeper
        });
        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.VerifySupplierBankAccountAsync(
                bookkeeper.Id,
                test.Organisation.Id,
                test.Supplier.Id,
                account.Id));
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
                    7,
                    null)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SubmitSupplierBankAccountAsync(
                test.UserId,
                new SupplierBankAccountRequest(
                    test.Organisation.Id,
                    test.Supplier.Id,
                    "Blocked account",
                    null,
                    "12345678")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LearnCustomerSalesDefaultsAsync(
                test.UserId,
                new LearnCustomerSalesDefaultsRequest(
                    test.Organisation.Id,
                    test.Customer.Id,
                    test.Account("4000").Id,
                    VatTreatment.Standard)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LearnSupplierPurchaseDefaultsAsync(
                test.UserId,
                new LearnSupplierPurchaseDefaultsRequest(
                    test.Organisation.Id,
                    test.Supplier.Id,
                    test.Account("6000").Id,
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
                30,
                "VAT-123"));

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
    public async Task BillCoding_LearnsOnlyMissingSupplierDefaultsAndAuditsOnce()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyService(test.Db, test.Access);
        var expenseAccount = test.Account("6000");
        var request = CreateRequest(test.Organisation.Id, null, null) with
        {
            Type = PartyType.Supplier,
            DefaultSalesVatTreatment = null,
            DefaultPurchaseVatTreatment = VatTreatment.Standard
        };
        var party = await service.CreateAsync(test.UserId, request);

        await service.LearnSupplierPurchaseDefaultsAsync(
            test.UserId,
            new LearnSupplierPurchaseDefaultsRequest(
                test.Organisation.Id,
                party.Id,
                expenseAccount.Id,
                VatTreatment.ZeroRated));
        await service.LearnSupplierPurchaseDefaultsAsync(
            test.UserId,
            new LearnSupplierPurchaseDefaultsRequest(
                test.Organisation.Id,
                party.Id,
                expenseAccount.Id,
                VatTreatment.Exempt));

        var stored = await test.Db.BusinessParties
            .AsNoTracking()
            .SingleAsync(x => x.Id == party.Id);
        Assert.Equal(expenseAccount.Id, stored.DefaultPurchaseAccountId);
        Assert.Equal(VatTreatment.Standard, stored.DefaultPurchaseVatTreatment);

        var learnedEvents = await test.Db.AuditEvents
            .AsNoTracking()
            .Where(x =>
                x.EntityId == party.Id.ToString() &&
                x.EventType == "SupplierDefaultsLearnedFromBill")
            .ToListAsync();
        var audit = Assert.Single(learnedEvents);
        using var evidence = JsonDocument.Parse(audit.JsonData);
        Assert.Equal(
            expenseAccount.Id.ToString(),
            evidence.RootElement.GetProperty("New").GetProperty("PurchaseAccountId").GetString());
        Assert.Equal(
            "Standard",
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
            30,
            " SUP-001 ",
            " VAT-123 ");
}
