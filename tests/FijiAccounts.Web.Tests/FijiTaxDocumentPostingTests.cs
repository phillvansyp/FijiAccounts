using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class FijiTaxDocumentPostingTests
{
    [Fact]
    public async Task Posting_CapturesImmutableTaxInvoiceIdentitySnapshot()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        var invoice = await CreateInvoiceAsync(test);

        Assert.True(invoice.IsTaxInvoice);
        Assert.False(invoice.IsSimplifiedTaxInvoice);
        Assert.Equal(FijiTaxDocumentCompliance.CurrentComplianceVersion, invoice.TaxDocumentComplianceVersion);
        Assert.Equal(test.Organisation.LegalName, invoice.SupplierNameSnapshot);
        Assert.Equal(test.Organisation.BusinessAddress, invoice.SupplierAddressSnapshot);
        Assert.Equal(test.Customer.Name, invoice.RecipientNameSnapshot);
        Assert.Equal(test.Customer.Address, invoice.RecipientAddressSnapshot);
    }

    [Fact]
    public async Task Posting_TaxableSupplyWithoutActiveRegistration_IsAtomic()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.IsVatRegistered = false;
        test.Organisation.VatRegistrationDate = null;
        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateInvoiceAsync(test));

        Assert.Empty(await test.Db.SalesInvoices.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.PostedJournals.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreditNote_CapturesOriginalAndAdjustedVatAmounts()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await CreateInvoiceAsync(test);
        var service = new SalesCreditNoteService(test.Db, test.Access, test.Posting);

        var credit = await service.CreateAsync(test.UserId, new(
            test.Organisation.Id, invoice.Id, new DateOnly(2026, 8, 27), "Price adjustment", invoice.Total / 2m, false));

        Assert.Equal(invoice.VatTotal, credit.OriginalInvoiceVatAmount);
        Assert.Equal(invoice.VatTotal - credit.VatTotal, credit.AdjustedInvoiceVatAmount);
    }

    [Fact]
    public async Task MixedRatePartialCreditRequiresAndUsesExplicitVatAdjustment()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await test.SalesInvoices.CreateAndPostAsync(test.UserId, new(
            test.Organisation.Id,
            test.Customer.Id,
            new DateOnly(2026, 8, 26),
            new DateOnly(2026, 9, 25),
            [
                new("Standard service", 1m, 100m, VatTreatment.Standard, test.Account("4000").Id),
                new("Zero-rated service", 1m, 100m, VatTreatment.ZeroRated, test.Account("4000").Id)
            ]));
        var service = new SalesCreditNoteService(test.Db, test.Access, test.Posting);
        var request = new SalesCreditNoteRequest(
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 27),
            "Zero-rated service credit",
            100m,
            false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(test.UserId, request));
        Assert.Contains("VAT adjustment", error.Message, StringComparison.OrdinalIgnoreCase);

        var credit = await service.CreateAsync(
            test.UserId,
            request with { VatAmount = 0m });

        Assert.Equal(100m, credit.Subtotal);
        Assert.Equal(0m, credit.VatTotal);
        Assert.Equal(invoice.VatTotal, credit.AdjustedInvoiceVatAmount);
        Assert.Single(await test.Db.SalesCreditNotes.ToListAsync());
    }

    private static Task<SalesInvoice> CreateInvoiceAsync(AccountingTestDatabase test) =>
        test.SalesInvoices.CreateAndPostAsync(test.UserId, new(
            test.Organisation.Id,
            test.Customer.Id,
            new DateOnly(2026, 8, 26),
            new DateOnly(2026, 9, 25),
            [new("Professional services", 1m, 100m, VatTreatment.Standard, test.Account("4000").Id)]));
}
