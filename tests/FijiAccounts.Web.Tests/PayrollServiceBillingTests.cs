using FijiAccounts.Web.Services;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class PayrollServiceBillingTests
{
    private static PayrollServiceCharge Charge() => new(Guid.NewGuid(), Guid.NewGuid(), "Payroll customer",
        "customer@example.test", new(2026, 9, 22), new(2026, 9, 29), new(2026, 9, 29), new(2026, 10, 28),
        "FJD", 100, 12.50m, 112.50m, "PI-TEST-20260929", "Original monthly service charges", "1 Test Road, Suva", "123456789");

    [Fact]
    public async Task RetryCreatesOneInvoiceAndJournalAndNextPeriodReusesCustomer()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new PayrollServiceBillingService(test.Db, test.SalesInvoices);
        var charge = Charge();
        var first = await service.ImportAsync(test.Organisation.Id, charge, default);
        var again = await service.ImportAsync(test.Organisation.Id, charge, default);
        Assert.Equal(first, again);
        Assert.Single(await test.Db.SalesInvoices.ToListAsync());
        Assert.Single(await test.Db.Set<PayrollServiceBillingImport>().ToListAsync());
        var invoice = await test.Db.SalesInvoices.SingleAsync();
        Assert.Equal(112.50m, invoice.Total);
        Assert.Equal(0m, invoice.AmountPaid);
        var customer = invoice.CustomerId;
        await service.ImportAsync(test.Organisation.Id, charge with { BillId = Guid.NewGuid(), PeriodStart = new(2026, 10, 29), PeriodEnd = new(2026, 11, 28) }, default);
        Assert.All(await test.Db.SalesInvoices.ToListAsync(), x => Assert.Equal(customer, x.CustomerId));
    }

    [Fact]
    public async Task ChangedSnapshotCannotReplaceAnImportedInvoice()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new PayrollServiceBillingService(test.Db, test.SalesInvoices);
        var charge = Charge();
        await service.ImportAsync(test.Organisation.Id, charge, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(test.Organisation.Id, charge with { CustomerName = "Different customer" }, default));
        Assert.Single(await test.Db.SalesInvoices.ToListAsync());
    }

    [Fact]
    public async Task TaxMismatchRollsBackInvoiceContactAndImport()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var contacts = await test.Db.BusinessParties.CountAsync();
        var service = new PayrollServiceBillingService(test.Db, test.SalesInvoices);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(test.Organisation.Id, Charge() with { Tax = 15, Total = 115 }, default));
        Assert.Contains("tax calculation differs", error.Message);
        test.Db.ChangeTracker.Clear();
        Assert.Empty(await test.Db.SalesInvoices.ToListAsync());
        Assert.Empty(await test.Db.Set<PayrollServiceBillingImport>().ToListAsync());
        Assert.Equal(contacts, await test.Db.BusinessParties.CountAsync());
    }

    [Fact]
    public async Task UnregisteredIssuerIsNotAutomaticallyRegisteredOrChargedVat()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.IsVatRegistered = false;
        test.Organisation.VatRegistrationDate = null;
        await test.Db.SaveChangesAsync();
        var service = new PayrollServiceBillingService(test.Db, test.SalesInvoices);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(test.Organisation.Id, Charge(), default));
        Assert.Contains("registration", error.Message);
        test.Db.ChangeTracker.Clear();
        Assert.Empty(await test.Db.SalesInvoices.ToListAsync());
        Assert.False((await test.Db.Organisations.SingleAsync(x => x.Id == test.Organisation.Id)).IsVatRegistered);
    }

    [Fact]
    public void SharedKeyMustBeConfiguredAndMatch()
    {
        Assert.False(PayrollServiceBillingService.Authenticate(null, null));
        Assert.False(PayrollServiceBillingService.Authenticate("short", "short"));
        Assert.False(PayrollServiceBillingService.Authenticate(new('a', 64), new('b', 64)));
        Assert.True(PayrollServiceBillingService.Authenticate(new('a', 64), new('a', 64)));
    }
    [Fact]
    public async Task EmployeeCountIsIncludedAndRetryKeepsInvoiceNumberAndTotals()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new PayrollServiceBillingService(test.Db, test.SalesInvoices);
        var charge = Charge() with { Detail = "Distinct employees active during September 2026: 20 (first 40 at FJD 5.00 each, then FJD 2.00 each) = FJD 100.00\n" };
        var first = await service.ImportAsync(test.Organisation.Id, charge, default);
        var invoice = await test.Db.SalesInvoices.Include(x => x.Lines).SingleAsync();
        Assert.Contains("20 employees", invoice.Lines.Single().Description);
        // Previously issued imports have the older generic description.
        var oldDescription = $"Payroll Island services {charge.PeriodStart:dd MMM yyyy}–{charge.PeriodEnd:dd MMM yyyy} · {charge.Reference}";
        await test.Db.SalesInvoiceLines.Where(x => x.SalesInvoiceId == invoice.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.Description, oldDescription));
        test.Db.ChangeTracker.Clear();
        Assert.Equal(first, await service.ImportAsync(test.Organisation.Id, charge, default));
        Assert.Contains("20 employees", (await test.Db.SalesInvoices.Include(x => x.Lines).SingleAsync()).Lines.Single().Description);
        Assert.Equal(112.50m, invoice.Total);
        Assert.Single(await test.Db.SalesInvoices.ToListAsync());
    }
}
