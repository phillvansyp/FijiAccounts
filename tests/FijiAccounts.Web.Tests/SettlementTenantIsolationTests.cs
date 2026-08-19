using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SettlementTenantIsolationTests
{
    [Fact]
    public async Task RecordAsync_RejectsInvoiceFromAnotherOrganisation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var other =
            await CreateOtherOrganisationAsync(test);

        var otherRevenue =
            Account(other.Accounts, "4000");

        var otherBank =
            Account(other.Accounts, "1000");

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    OrganisationId: other.Organisation.Id,
                    CustomerId: other.Customer.Id,
                    IssueDate: new DateOnly(2026, 8, 18),
                    DueDate: new DateOnly(2026, 9, 17),
                    Lines:
                    [
                        new SalesInvoiceLineRequest(
                            Description: "Cross-tenant receipt test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: otherRevenue.Id)
                    ]));

        var receipts =
            new CustomerReceiptService(
                test.Db,
                test.Access,
                test.Posting,
                test.Reconciliation);

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var receiptCountBefore =
            await test.Db.CustomerReceipts.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                receipts.RecordAsync(
                    test.UserId,
                    new CustomerReceiptRequest(
                        OrganisationId: test.Organisation.Id,
                        SalesInvoiceId: invoice.Id,
                        Date: new DateOnly(2026, 8, 19),
                        Reference: "CROSS-TENANT-RECEIPT-001",
                        Amount: invoice.Total,
                        BankAccountId: otherBank.Id)));

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            receiptCountBefore,
            await test.Db.CustomerReceipts.CountAsync());

        var reloaded =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(0m, reloaded.AmountPaid);
        Assert.Equal(InvoiceStatus.Posted, reloaded.Status);
    }

    [Fact]
    public async Task PayBillAsync_RejectsSupplierBillFromAnotherOrganisation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var other =
            await CreateOtherOrganisationAsync(test);

        var otherExpense =
            Account(other.Accounts, "6500");

        var otherBank =
            Account(other.Accounts, "1000");

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: other.Organisation.Id,
                    SupplierId: other.Supplier.Id,
                    SupplierReference: "CROSS-TENANT-BILL-001",
                    BillDate: new DateOnly(2026, 8, 18),
                    DueDate: new DateOnly(2026, 9, 17),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Cross-tenant payment test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: otherExpense.Id)
                    ]));

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var paymentCountBefore =
            await test.Db.SupplierPayments.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.PayBillAsync(
                    test.UserId,
                    new SupplierPaymentRequest(
                        OrganisationId: test.Organisation.Id,
                        SupplierBillId: bill.Id,
                        Date: new DateOnly(2026, 8, 19),
                        Reference: "CROSS-TENANT-PAYMENT-001",
                        Amount: bill.Total,
                        BankAccountId: otherBank.Id)));

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            paymentCountBefore,
            await test.Db.SupplierPayments.CountAsync());

        var reloaded =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(0m, reloaded.AmountPaid);
        Assert.Equal(BillStatus.Posted, reloaded.Status);
    }

    private static async Task<OtherOrganisation> CreateOtherOrganisationAsync(
        AccountingTestDatabase test)
    {
        var organisation =
            new Organisation
            {
                LegalName = "Other Organisation Limited",
                TradingName = "Other Organisation",
                CountryCode = "FJ",
                BaseCurrency = "FJD",
                TaxLabel = "VAT",
                Kind = OrganisationKind.Business
            };

        test.Db.Organisations.Add(organisation);

        test.Db.OrganisationMemberships.Add(
            new OrganisationMembership
            {
                OrganisationId = organisation.Id,
                Organisation = organisation,
                UserId = test.UserId,
                Role = OrganisationRole.Owner
            });

        var accounts =
            FijiStarterChart.For(organisation.Id)
                .ToList();

        test.Db.LedgerAccounts.AddRange(accounts);

        var customer =
            new BusinessParty
            {
                OrganisationId = organisation.Id,
                Organisation = organisation,
                Name = "Other Organisation Customer",
                Type = PartyType.Customer,
                IsActive = true
            };

        var supplier =
            new BusinessParty
            {
                OrganisationId = organisation.Id,
                Organisation = organisation,
                Name = "Other Organisation Supplier",
                Type = PartyType.Supplier,
                IsActive = true
            };

        test.Db.BusinessParties.AddRange(
            customer,
            supplier);

        await test.Db.SaveChangesAsync();

        return new OtherOrganisation(
            organisation,
            customer,
            supplier,
            accounts);
    }

    private static LedgerAccount Account(
        IReadOnlyCollection<LedgerAccount> accounts,
        string code) =>
        accounts.Single(x => x.Code == code);

    private sealed record OtherOrganisation(
        Organisation Organisation,
        BusinessParty Customer,
        BusinessParty Supplier,
        IReadOnlyCollection<LedgerAccount> Accounts);
}