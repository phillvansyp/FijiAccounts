using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SalesQuoteAccountingTests
{
    [Fact]
    public async Task ConvertAcceptedQuoteToDraftInvoice_CreatesDraftWithoutPostingJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new SalesQuoteService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var quote =
            await service.CreateAsync(
                test.UserId,
                new SalesQuoteRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    QuoteDate: new DateOnly(2026, 8, 20),
                    ExpiryDate: new DateOnly(2026, 9, 20),
                    Lines:
                    [
                        new SalesInvoiceLineRequest(
                            Description: "Consulting services",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Equal(100m, quote.Subtotal);
        Assert.Equal(12.50m, quote.VatTotal);
        Assert.Equal(112.50m, quote.Total);

        await service.SetStatusAsync(
            test.UserId,
            test.Organisation.Id,
            quote.Id,
            QuoteStatus.Sent);

        await service.SetStatusAsync(
            test.UserId,
            test.Organisation.Id,
            quote.Id,
            QuoteStatus.Accepted);

        var invoice =
            await service.ConvertToDraftInvoiceAsync(
                test.UserId,
                test.Organisation.Id,
                quote.Id);

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Null(invoice.PostedJournalId);

        Assert.Equal(100m, invoice.Subtotal);
        Assert.Equal(12.50m, invoice.VatTotal);
        Assert.Equal(112.50m, invoice.Total);

        var storedQuote =
            await test.Db.SalesQuotes
                .AsNoTracking()
                .SingleAsync(x => x.Id == quote.Id);

        Assert.Equal(
            QuoteStatus.Invoiced,
            storedQuote.Status);

        Assert.Equal(
            invoice.Id,
            storedQuote.ConvertedInvoiceId);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
public async Task ConvertToDraftInvoiceAsync_WhenQuoteIsNotAccepted_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new SalesQuoteService(
            test.Db,
            test.Access,
            test.SalesInvoices);

    var quote =
        await service.CreateAsync(
            test.UserId,
            new SalesQuoteRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                QuoteDate: new DateOnly(2026, 8, 20),
                ExpiryDate: DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Consulting services",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.ConvertToDraftInvoiceAsync(
                    test.UserId,
                    test.Organisation.Id,
                    quote.Id));

    Assert.Contains(
        "accepted",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());
}

[Fact]
public async Task ConvertToDraftInvoiceAsync_WhenQuoteHasExpired_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new SalesQuoteService(
            test.Db,
            test.Access,
            test.SalesInvoices);

    var quote =
        await service.CreateAsync(
            test.UserId,
            new SalesQuoteRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                QuoteDate: DateOnly.FromDateTime(DateTime.Today.AddDays(-10)),
                ExpiryDate: DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Consulting services",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

    await service.SetStatusAsync(
        test.UserId,
        test.Organisation.Id,
        quote.Id,
        QuoteStatus.Sent);

    await service.SetStatusAsync(
        test.UserId,
        test.Organisation.Id,
        quote.Id,
        QuoteStatus.Accepted);

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.ConvertToDraftInvoiceAsync(
                    test.UserId,
                    test.Organisation.Id,
                    quote.Id));

    Assert.Contains(
        "expired",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());
}

[Fact]
public async Task ConvertToDraftInvoiceAsync_WhenQuoteAlreadyInvoiced_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new SalesQuoteService(
            test.Db,
            test.Access,
            test.SalesInvoices);

    var quote =
        await service.CreateAsync(
            test.UserId,
            new SalesQuoteRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                QuoteDate: new DateOnly(2026, 8, 20),
                ExpiryDate: DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Consulting services",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

    await service.SetStatusAsync(
        test.UserId,
        test.Organisation.Id,
        quote.Id,
        QuoteStatus.Sent);

    await service.SetStatusAsync(
        test.UserId,
        test.Organisation.Id,
        quote.Id,
        QuoteStatus.Accepted);

    var firstInvoice =
        await service.ConvertToDraftInvoiceAsync(
            test.UserId,
            test.Organisation.Id,
            quote.Id);

    var invoiceCountBefore =
        await test.Db.SalesInvoices.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.ConvertToDraftInvoiceAsync(
                    test.UserId,
                    test.Organisation.Id,
                    quote.Id));

    Assert.Contains(
        "accepted",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        invoiceCountBefore,
        await test.Db.SalesInvoices.CountAsync());

    Assert.NotEqual(
        Guid.Empty,
        firstInvoice.Id);
}

    [Fact]
public async Task SetStatusAsync_DraftToSentToAccepted_IsAllowed()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new SalesQuoteService(
            test.Db,
            test.Access,
            test.SalesInvoices);

    var quote =
        await service.CreateAsync(
            test.UserId,
            new SalesQuoteRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                QuoteDate: new DateOnly(2026, 8, 20),
                ExpiryDate: DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Consulting services",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

    await service.SetStatusAsync(
        test.UserId,
        test.Organisation.Id,
        quote.Id,
        QuoteStatus.Sent);

    await service.SetStatusAsync(
        test.UserId,
        test.Organisation.Id,
        quote.Id,
        QuoteStatus.Accepted);

    var stored =
        await test.Db.SalesQuotes
            .AsNoTracking()
            .SingleAsync(x => x.Id == quote.Id);

    Assert.Equal(
        QuoteStatus.Accepted,
        stored.Status);
}

[Fact]
public async Task SetStatusAsync_DraftDirectlyToAccepted_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new SalesQuoteService(
            test.Db,
            test.Access,
            test.SalesInvoices);

    var quote =
        await service.CreateAsync(
            test.UserId,
            new SalesQuoteRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                QuoteDate: new DateOnly(2026, 8, 20),
                ExpiryDate: DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Consulting services",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.SetStatusAsync(
                    test.UserId,
                    test.Organisation.Id,
                    quote.Id,
                    QuoteStatus.Accepted));

    Assert.Contains(
        "cannot be changed",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    var stored =
        await test.Db.SalesQuotes
            .AsNoTracking()
            .SingleAsync(x => x.Id == quote.Id);

    Assert.Equal(
        QuoteStatus.Draft,
        stored.Status);
}

[Fact]
public async Task SetStatusAsync_WhenQuoteIsInvoiced_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new SalesQuoteService(
            test.Db,
            test.Access,
            test.SalesInvoices);

    var quote =
        await service.CreateAsync(
            test.UserId,
            new SalesQuoteRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                QuoteDate: new DateOnly(2026, 8, 20),
                ExpiryDate: DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Consulting services",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

    await service.SetStatusAsync(
        test.UserId,
        test.Organisation.Id,
        quote.Id,
        QuoteStatus.Sent);

    await service.SetStatusAsync(
        test.UserId,
        test.Organisation.Id,
        quote.Id,
        QuoteStatus.Accepted);

    await service.ConvertToDraftInvoiceAsync(
        test.UserId,
        test.Organisation.Id,
        quote.Id);

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.SetStatusAsync(
                    test.UserId,
                    test.Organisation.Id,
                    quote.Id,
                    QuoteStatus.Expired));

    Assert.Contains(
        "invoiced",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);
}

    [Fact]
public async Task CreateAsync_WhenCustomerIsInactive_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new SalesQuoteService(
            test.Db,
            test.Access,
            test.SalesInvoices);

    test.Customer.IsActive = false;
    await test.Db.SaveChangesAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.CreateAsync(
                    test.UserId,
                    new SalesQuoteRequest(
                        OrganisationId: test.Organisation.Id,
                        CustomerId: test.Customer.Id,
                        QuoteDate: new DateOnly(2026, 8, 20),
                        ExpiryDate: DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                        Lines:
                        [
                            new SalesInvoiceLineRequest(
                                Description: "Consulting services",
                                Quantity: 1m,
                                UnitPrice: 100m,
                                VatTreatment: VatTreatment.Standard,
                                RevenueAccountId: test.Account("4000").Id)
                        ])));

    Assert.Contains(
        "active customer",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task CreateAsync_WhenRevenueAccountHasWrongType_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new SalesQuoteService(
            test.Db,
            test.Access,
            test.SalesInvoices);

    var revenue =
        test.Account("4000");

    revenue.Type = FijiAccounts.Domain.Accounting.AccountType.Asset;
    await test.Db.SaveChangesAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.CreateAsync(
                    test.UserId,
                    new SalesQuoteRequest(
                        OrganisationId: test.Organisation.Id,
                        CustomerId: test.Customer.Id,
                        QuoteDate: new DateOnly(2026, 8, 20),
                        ExpiryDate: DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                        Lines:
                        [
                            new SalesInvoiceLineRequest(
                                Description: "Consulting services",
                                Quantity: 1m,
                                UnitPrice: 100m,
                                VatTreatment: VatTreatment.Standard,
                                RevenueAccountId: revenue.Id)
                        ])));

    Assert.Contains(
        "revenue accounts",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task CreateAsync_WhenRevenueAccountIsInactive_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new SalesQuoteService(
            test.Db,
            test.Access,
            test.SalesInvoices);

    var revenue =
        test.Account("4000");

    revenue.IsActive = false;
    await test.Db.SaveChangesAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.CreateAsync(
                    test.UserId,
                    new SalesQuoteRequest(
                        OrganisationId: test.Organisation.Id,
                        CustomerId: test.Customer.Id,
                        QuoteDate: new DateOnly(2026, 8, 20),
                        ExpiryDate: DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                        Lines:
                        [
                            new SalesInvoiceLineRequest(
                                Description: "Consulting services",
                                Quantity: 1m,
                                UnitPrice: 100m,
                                VatTreatment: VatTreatment.Standard,
                                RevenueAccountId: revenue.Id)
                        ])));

    Assert.Contains(
        "revenue accounts",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);
}
}