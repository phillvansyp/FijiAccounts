using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class NewZealandJurisdictionTests
{
    [Theory]
    [InlineData("FJ", "JCR 2006 Limited (Fiji)")]
    [InlineData("NZ", "JCR 2006 Limited (NZ)")]
    public void Organisation_display_name_includes_clear_country_tag(
        string countryCode,
        string expected)
    {
        var organisation = new Organisation
        {
            LegalName = "JCR 2006 Limited",
            CountryCode = countryCode
        };

        Assert.Equal(expected, OrganisationDisplay.NameWithCountry(organisation));
    }

    [Fact]
    public async Task Creating_New_Zealand_company_applies_local_defaults_and_chart()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var company = await new EnterpriseStructureService(test.Db)
            .CreateStandaloneCompanyAsync(
                test.UserId,
                new CreateStandaloneCompanyRequest(
                    "Aotearoa Trading Limited",
                    null,
                    "091-160-412",
                    "NZ",
                    OrganisationKind.Business,
                    Nzbn: "9429-000-000-000",
                    NzCompanyNumber: "1-862-844"));

        var stored = await test.Db.Organisations.AsNoTracking().SingleAsync(x => x.Id == company.Id);
        var accounts = await test.Db.LedgerAccounts.AsNoTracking()
            .Where(x => x.OrganisationId == company.Id)
            .ToListAsync();

        Assert.Equal("NZD", stored.BaseCurrency);
        Assert.Equal("Pacific/Auckland", stored.TimeZoneId);
        Assert.Equal("GST", stored.TaxLabel);
        Assert.Equal(3, stored.FinancialYearEndMonth);
        Assert.Equal(31, stored.FinancialYearEndDay);
        Assert.Equal("091-160-412", stored.Tin);
        Assert.Equal("9429000000000", stored.Nzbn);
        Assert.Equal("1862844", stored.NzCompanyNumber);
        Assert.Contains(accounts, x => x.Code == "1150" && x.Name == "GST Receivable");
        Assert.Contains(accounts, x => x.Code == "2100" && x.Name == "GST Payable");
    }

    [Theory]
    [InlineData("091-160-412", true)]
    [InlineData("91160412", true)]
    [InlineData("123-456-789", false)]
    [InlineData("not-an-ird", false)]
    public void New_Zealand_IRD_number_validation_matches_the_official_checksum(
        string value,
        bool expected) =>
        Assert.Equal(expected, OrganisationIdentifierValidation.IsValidIrdNumber(value));

    [Theory]
    [InlineData("9429000000000", true)]
    [InlineData("9429000000001", false)]
    [InlineData("1234567890128", false)]
    public void NZBN_validation_requires_a_13_digit_New_Zealand_GLN(
        string value,
        bool expected) =>
        Assert.Equal(expected, OrganisationIdentifierValidation.IsValidNzbn(value));

    [Theory]
    [InlineData("1862844", "1862844")]
    [InlineData("1-862-844", "1862844")]
    [InlineData("104799", "104799")]
    public void New_Zealand_company_number_is_normalised(
        string value,
        string expected) =>
        Assert.Equal(
            expected,
            OrganisationIdentifierValidation.NormaliseNzCompanyNumber("NZ", value));

    [Fact]
    public async Task New_Zealand_invoice_posts_at_fifteen_percent_and_populates_GST101_workpaper()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        test.Organisation.BaseCurrency = "NZD";
        test.Organisation.TaxLabel = "GST";
        test.Organisation.TimeZoneId = "Pacific/Auckland";
        test.Organisation.BusinessAddress = "1 Queen Street, Auckland";
        await test.Db.SaveChangesAsync();

        var invoice = await test.SalesInvoices.CreateAndPostAsync(test.UserId, new(
            test.Organisation.Id,
            test.Customer.Id,
            new DateOnly(2026, 8, 29),
            new DateOnly(2026, 9, 28),
            [new("Consulting", 1m, 100m, VatTreatment.Standard, test.Account("4000").Id)]));
        var gstReturn = await new NewZealandGstReturnService(new VatWorkpaperService(test.Db))
            .GetInvoiceBasisAsync(test.Organisation.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.Equal(15m, invoice.VatTotal);
        Assert.Equal(115m, invoice.Total);
        Assert.Equal(TaxDocumentCompliance.NewZealandComplianceVersion, invoice.TaxDocumentComplianceVersion);
        Assert.Equal(115m, gstReturn.Box5TotalSalesAndIncome);
        Assert.Equal(15m, gstReturn.Box8GstOnSales);
        Assert.Equal(15m, gstReturn.Box15NetGst);
    }

    [Fact]
    public async Task New_Zealand_registration_threshold_is_sixty_thousand()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        test.Organisation.BaseCurrency = "NZD";
        test.Organisation.TaxLabel = "GST";
        test.Organisation.IsVatRegistered = false;
        test.Organisation.ExpectedTaxableTurnoverNext12Months = 60_000m;
        await test.Db.SaveChangesAsync();

        var assessment = await new VatTurnoverMonitorService(test.Db, test.Notifications, test.Access)
            .GetAssessmentAsync(test.Organisation.Id, new DateOnly(2026, 8, 29));

        Assert.Equal(60_000m, assessment.RegistrationThreshold);
        Assert.True(assessment.RequiresRegistration);
    }

    [Fact]
    public void Taxable_supply_information_over_two_hundred_requires_seller_GST_number()
    {
        var organisation = NewZealandOrganisation();
        organisation.Tin = null;

        var error = Assert.Throws<InvalidOperationException>(() =>
            TaxDocumentCompliance.ClassifyAndValidate(
                organisation,
                new BusinessParty { Name = "Buyer", Type = PartyType.Customer },
                new DateOnly(2026, 8, 29),
                200.01m,
                [StandardLine()]));

        Assert.Contains("GST number", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Taxable_supply_information_over_one_thousand_requires_buyer_contact_details()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            TaxDocumentCompliance.ClassifyAndValidate(
                NewZealandOrganisation(),
                new BusinessParty { Name = "Buyer", Type = PartyType.Customer },
                new DateOnly(2026, 8, 29),
                1_000.01m,
                [StandardLine()]));

        Assert.Contains("address, email or phone", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Organisation NewZealandOrganisation() => new()
    {
        LegalName = "Aotearoa Trading Limited",
        CountryCode = "NZ",
        BaseCurrency = "NZD",
        TaxLabel = "GST",
        Tin = "123-456-789",
        IsVatRegistered = true,
        VatRegistrationDate = new DateOnly(2020, 1, 1)
    };

    private static SalesInvoiceLine StandardLine() => new()
    {
        Description = "Service",
        Quantity = 1m,
        UnitPrice = 1m,
        VatTreatment = VatTreatment.Standard,
        VatRate = 0.15m,
        NetAmount = 1m,
        VatAmount = 0.15m,
        GrossAmount = 1.15m
    };
}
