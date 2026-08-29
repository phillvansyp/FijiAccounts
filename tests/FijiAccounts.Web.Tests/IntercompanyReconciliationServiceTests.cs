using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class IntercompanyReconciliationServiceTests
{
    [Fact]
    public async Task RefreshSuggestionsAsync_MatchesReciprocalInvoiceAndBillForReview()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var setup = await CreateReciprocalDocumentsAsync(test, 100m, 100m);
        var service = Service(test);

        await TagPairAsync(test, service, setup);
        var created = await service.RefreshSuggestionsAsync(test.UserId, test.Organisation.Id);
        var dashboard = await service.GetAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(1, created);
        var match = Assert.Single(dashboard.Matches);
        Assert.True(match.IsExact);
        Assert.Equal(IntercompanyMatchStatus.Proposed, match.Status);
        Assert.Equal(0, dashboard.ExceptionCount);
        Assert.All(dashboard.Tags, x => Assert.True(x.IsMatched));

        await service.ReviewMatchAsync(test.UserId, test.Organisation.Id, match.Id, true);
        dashboard = await service.GetAsync(test.UserId, test.Organisation.Id);
        Assert.Equal(IntercompanyMatchStatus.Confirmed, Assert.Single(dashboard.Matches).Status);
        Assert.Contains(
            await test.Db.AuditEvents.AsNoTracking().ToListAsync(),
            x => x.EventType == "IntercompanyMatchConfirmed");
    }

    [Fact]
    public async Task ReviewMatchAsync_BlocksAmountDifferenceAndAllowsRejectAndRetag()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var setup = await CreateReciprocalDocumentsAsync(test, 100m, 95m);
        var service = Service(test);
        await TagPairAsync(test, service, setup);
        await service.RefreshSuggestionsAsync(test.UserId, test.Organisation.Id);
        var dashboard = await service.GetAsync(test.UserId, test.Organisation.Id);
        var match = Assert.Single(dashboard.Matches);

        Assert.Equal(5m, match.AmountDifference);
        Assert.Equal(1, dashboard.ExceptionCount);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReviewMatchAsync(test.UserId, test.Organisation.Id, match.Id, true));
        Assert.Contains("difference", exception.Message, StringComparison.OrdinalIgnoreCase);

        await service.ReviewMatchAsync(test.UserId, test.Organisation.Id, match.Id, false);
        await service.RemoveTagAsync(test.UserId, test.Organisation.Id, match.Right.Id);
        Assert.Single(await test.Db.IntercompanyTransactionTags.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.IntercompanyTransactionMatches.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PostEliminationAsync_PostsFourLineGroupOnlyEliminationOnce()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var setup = await CreateReciprocalDocumentsAsync(test, 100m, 100m);
        var service = Service(test);
        await ConfigurePartnerAccountsAsync(test, setup.Second.Id);
        await TagPairAsync(test, service, setup);
        await service.RefreshSuggestionsAsync(test.UserId, test.Organisation.Id);
        var match = Assert.Single((await service.GetAsync(test.UserId, test.Organisation.Id)).Matches);
        await service.ReviewMatchAsync(test.UserId, test.Organisation.Id, match.Id, true);

        var journal = await service.PostEliminationAsync(
            test.UserId,
            test.Organisation.Id,
            match.Id);

        var stored = await test.Db.GroupEliminationJournals.AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == journal.Id);
        Assert.Equal(4, stored.Lines.Count);
        Assert.Equal(200m, stored.Lines.Sum(x => x.Debit));
        Assert.Equal(200m, stored.Lines.Sum(x => x.Credit));
        Assert.Equal(
            journal.Id,
            (await test.Db.IntercompanyTransactionMatches.AsNoTracking().SingleAsync()).GroupEliminationJournalId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostEliminationAsync(test.UserId, test.Organisation.Id, match.Id));
    }

    [Fact]
    public async Task TagAsync_RejectsViewerAndDuplicateSourceDocument()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var setup = await CreateReciprocalDocumentsAsync(test, 100m, 100m);
        var service = Service(test);
        var request = new TagIntercompanyDocumentRequest(
            test.Organisation.Id,
            IntercompanyDocumentType.SalesInvoice,
            setup.Invoice.Id,
            setup.Second.Id);
        await service.TagAsync(test.UserId, request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TagAsync(test.UserId, request));

        await test.Db.OrganisationGroupMemberships
            .Where(x => x.OrganisationGroupId == test.Organisation.OrganisationGroupId &&
                        x.UserId == test.UserId)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationGroupRole.Viewer));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.TagAsync(
                test.UserId,
                new(
                    test.Organisation.Id,
                    IntercompanyDocumentType.SupplierBill,
                    setup.Bill.Id,
                    test.Organisation.Id)));
    }

    private static IntercompanyReconciliationService Service(AccountingTestDatabase test) =>
        new(test.Db, new GroupEliminationService(test.Db));

    private static async Task TagPairAsync(
        AccountingTestDatabase test,
        IntercompanyReconciliationService service,
        ReciprocalSetup setup)
    {
        await service.TagAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                IntercompanyDocumentType.SalesInvoice,
                setup.Invoice.Id,
                setup.Second.Id));
        await service.TagAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                IntercompanyDocumentType.SupplierBill,
                setup.Bill.Id,
                test.Organisation.Id));
    }

    private static async Task ConfigurePartnerAccountsAsync(
        AccountingTestDatabase test,
        Guid secondOrganisationId)
    {
        var service = new GroupAccountMappingService(test.Db);
        var first = await Accounts(test.Db, test.Organisation.Id);
        var second = await Accounts(test.Db, secondOrganisationId);
        await service.SaveIntercompanyConfigurationAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                test.Organisation.Id,
                secondOrganisationId,
                first["1100"].Id,
                first["2000"].Id,
                first["4000"].Id,
                first["5000"].Id));
        await service.SaveIntercompanyConfigurationAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                secondOrganisationId,
                test.Organisation.Id,
                second["1100"].Id,
                second["2000"].Id,
                second["4000"].Id,
                second["5000"].Id));
    }

    private static async Task<Dictionary<string, LedgerAccount>> Accounts(
        ApplicationDbContext db,
        Guid organisationId) =>
        await db.LedgerAccounts
            .Where(x => x.OrganisationId == organisationId &&
                        new[] { "1100", "2000", "4000", "5000" }.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code);

    private static async Task<ReciprocalSetup> CreateReciprocalDocumentsAsync(
        AccountingTestDatabase test,
        decimal invoiceAmount,
        decimal billAmount)
    {
        var second = await new EnterpriseStructureService(test.Db).AddCompanyAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                "Second Fiji Trading Limited",
                null,
                null,
                "FJ",
                OrganisationKind.Business));
        var secondCustomer = new BusinessParty
        {
            OrganisationId = test.Organisation.Id,
            Name = second.LegalName,
            Type = PartyType.Customer
        };
        var firstSupplier = new BusinessParty
        {
            OrganisationId = second.Id,
            Name = test.Organisation.LegalName,
            Type = PartyType.Supplier
        };
        test.Db.BusinessParties.AddRange(secondCustomer, firstSupplier);
        await test.Db.SaveChangesAsync();
        var secondExpense = await test.Db.LedgerAccounts.SingleAsync(
            x => x.OrganisationId == second.Id && x.Code == "5000");
        var invoice = await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                secondCustomer.Id,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 9, 19),
                [
                    new(
                        "Intercompany services",
                        1m,
                        invoiceAmount,
                        VatTreatment.Exempt,
                        test.Account("4000").Id)
                ]));
        var bill = await test.Purchasing.PostBillAsync(
            test.UserId,
            new(
                second.Id,
                firstSupplier.Id,
                invoice.InvoiceNumber,
                new DateOnly(2026, 8, 21),
                new DateOnly(2026, 9, 20),
                [
                    new(
                        "Intercompany services",
                        1m,
                        billAmount,
                        VatTreatment.Exempt,
                        secondExpense.Id)
                ]));
        return new(second, invoice, bill);
    }

    private sealed record ReciprocalSetup(
        Organisation Second,
        SalesInvoice Invoice,
        SupplierBill Bill);
}
