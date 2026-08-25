using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class FijiTaxDocumentComplianceTests
{
    [Fact]
    public void StandardSupply_FromUnregisteredOrganisation_IsRejected()
    {
        var organisation = Organisation();
        organisation.IsVatRegistered = false;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            FijiTaxDocumentCompliance.ClassifyAndValidate(
                organisation, Recipient(), new DateOnly(2026, 8, 26), 56.25m, [Line(VatTreatment.Standard)]));

        Assert.Contains("registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StandardSupply_BeforeRegistrationDate_IsRejected()
    {
        var organisation = Organisation();
        organisation.VatRegistrationDate = new DateOnly(2026, 9, 1);

        Assert.Throws<InvalidOperationException>(() =>
            FijiTaxDocumentCompliance.ClassifyAndValidate(
                organisation, Recipient(), new DateOnly(2026, 8, 26), 56.25m, [Line(VatTreatment.Standard)]));
    }

    [Fact]
    public void StandardSupply_AtOneHundredOrLess_IsSimplified()
    {
        var result = FijiTaxDocumentCompliance.ClassifyAndValidate(
            Organisation(), Recipient(address: null), new DateOnly(2026, 8, 26), 100m, [Line(VatTreatment.Standard)]);

        Assert.True(result.IsTaxInvoice);
        Assert.True(result.IsSimplified);
        Assert.Equal(FijiTaxDocumentCompliance.CurrentComplianceVersion, result.ComplianceVersion);
    }

    [Fact]
    public void FullTaxInvoice_RequiresRecipientAddress()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FijiTaxDocumentCompliance.ClassifyAndValidate(
                Organisation(), Recipient(address: null), new DateOnly(2026, 8, 26), 101m, [Line(VatTreatment.Standard)]));

        Assert.Contains("customer's address", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExemptOnlySupply_IsCommercialInvoice()
    {
        var organisation = Organisation();
        organisation.IsVatRegistered = false;
        var result = FijiTaxDocumentCompliance.ClassifyAndValidate(
            organisation, Recipient(address: null),
            new DateOnly(2026, 8, 26), 250m, [Line(VatTreatment.Exempt)]);

        Assert.False(result.IsTaxInvoice);
        Assert.False(result.IsSimplified);
    }

    private static Organisation Organisation() => new()
    {
        LegalName = " Fiji Supplier Limited ",
        Tin = " 123456789 ",
        BusinessAddress = " 1 Test Street, Suva ",
        CountryCode = "FJ",
        IsVatRegistered = true,
        VatRegistrationDate = new DateOnly(2020, 1, 1)
    };

    private static BusinessParty Recipient(string? address = "2 Customer Road, Nadi") => new()
    {
        Name = "Customer Limited",
        Address = address,
        Type = PartyType.Customer
    };

    private static SalesInvoiceLine Line(VatTreatment treatment) => new()
    {
        Description = "Service",
        Quantity = 1m,
        UnitPrice = 50m,
        VatTreatment = treatment,
        VatRate = treatment == VatTreatment.Standard ? 0.125m : 0m,
        NetAmount = 50m,
        VatAmount = treatment == VatTreatment.Standard ? 6.25m : 0m,
        GrossAmount = treatment == VatTreatment.Standard ? 56.25m : 50m
    };
}
