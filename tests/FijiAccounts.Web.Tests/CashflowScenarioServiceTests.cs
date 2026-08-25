using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class CashflowScenarioServiceTests
{
    [Fact]
    public async Task ManagerCanCreateAddRemoveAndArchiveAuditedScenario()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = Service(test);
        var scenario = await service.CreateAsync(test.UserId,
            new(test.Organisation.Id, "  Downside case  ", "Higher costs"));

        var adjustment = await service.AddEventAsync(test.UserId, new(
            test.Organisation.Id, scenario.Id,
            CashflowScenarioEventKind.PlannedPayment,
            CashflowScenarioFrequency.Monthly,
            "Rent increase", 250m,
            DateOnly.FromDateTime(DateTime.Today).AddDays(1),
            DateOnly.FromDateTime(DateTime.Today).AddMonths(3), null));

        var configuration = await service.GetAsync(test.UserId, test.Organisation.Id);
        Assert.True(configuration.CanManage);
        Assert.Equal("Downside case", Assert.Single(configuration.Scenarios).Name);
        Assert.Single(configuration.Scenarios[0].Events);
        var comparison = await service.CompareAsync(
            test.UserId, test.Organisation.Id, scenario.Id,
            DateOnly.FromDateTime(DateTime.Today));
        Assert.True(comparison.Adjusted.Next12Months.ExpectedPayments >
                    comparison.Baseline.Next12Months.ExpectedPayments);

        await service.RemoveEventAsync(test.UserId, test.Organisation.Id, adjustment.Id);
        await service.ArchiveAsync(test.UserId, test.Organisation.Id, scenario.Id);

        Assert.Empty((await service.GetAsync(test.UserId, test.Organisation.Id)).Scenarios);
        Assert.Equal(
            ["CashflowScenarioCreated", "CashflowScenarioEventAdded", "CashflowScenarioEventRemoved", "CashflowScenarioArchived"],
            await test.Db.AuditEvents.OrderBy(x => x.Id).Select(x => x.EventType).ToListAsync());
    }

    [Fact]
    public async Task ReceiptDelaySnapshotsOutstandingInvoiceAndValidatesRevisedDate()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var invoice = new SalesInvoice
        {
            OrganisationId = test.Organisation.Id,
            CustomerId = test.Customer.Id,
            SequenceNumber = 1,
            InvoiceNumber = "INV-SLOW-001",
            IssueDate = today,
            DueDate = today.AddDays(7),
            Status = InvoiceStatus.Posted,
            Subtotal = 900m,
            Total = 900m,
            AmountPaid = 100m,
            CreatedByUserId = test.UserId
        };
        test.Db.SalesInvoices.Add(invoice);
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        var scenario = await service.CreateAsync(test.UserId,
            new(test.Organisation.Id, "Collections risk", null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddEventAsync(test.UserId, new(
            test.Organisation.Id, scenario.Id,
            CashflowScenarioEventKind.CustomerReceiptDelay,
            CashflowScenarioFrequency.Monthly,
            "Delay", 1m, invoice.DueDate, null, invoice.Id)));
        var adjustment = await service.AddEventAsync(test.UserId, new(
            test.Organisation.Id, scenario.Id,
            CashflowScenarioEventKind.CustomerReceiptDelay,
            CashflowScenarioFrequency.Monthly,
            "Delay", 1m, invoice.DueDate.AddDays(30), null, invoice.Id));

        Assert.Equal(800m, adjustment.Amount);
        Assert.Equal(invoice.DueDate, adjustment.OriginalDate);
        Assert.Equal("INV-SLOW-001", adjustment.SourceReference);
        Assert.Equal(CashflowScenarioFrequency.OneOff, adjustment.Frequency);
    }

    [Fact]
    public async Task ReadOnlyUserCanCompareButCannotManageScenarios()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = Service(test);
        var scenario = await service.CreateAsync(test.UserId,
            new(test.Organisation.Id, "Visible plan", null));
        await test.Db.OrganisationMemberships
            .Where(x => x.OrganisationId == test.Organisation.Id && x.UserId == test.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));

        var configuration = await service.GetAsync(test.UserId, test.Organisation.Id);
        Assert.False(configuration.CanManage);
        Assert.Single(configuration.Scenarios);
        await service.CompareAsync(test.UserId, test.Organisation.Id, scenario.Id,
            DateOnly.FromDateTime(DateTime.Today));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(test.UserId,
            new(test.Organisation.Id, "Forbidden", null)));
    }

    [Fact]
    public async Task RejectsDuplicateNamesAndCrossOrganisationScenarioUse()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = Service(test);
        var scenario = await service.CreateAsync(test.UserId,
            new(test.Organisation.Id, "Base case", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(test.UserId,
            new(test.Organisation.Id, " base CASE ", null)));
        var other = new Organisation { LegalName = "Other Limited", Kind = OrganisationKind.Business };
        test.Db.Organisations.Add(other);
        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CompareAsync(
            test.UserId, other.Id, scenario.Id, DateOnly.FromDateTime(DateTime.Today)));
    }

    private static CashflowScenarioService Service(AccountingTestDatabase test) =>
        new(test.Db, test.Access, new CashflowForecastService(test.Db));
}
