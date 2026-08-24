using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using FijiAccounts.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ProjectProfitabilityTests
{
    [Fact]
    public async Task TaggedSalesInvoiceAndSupplierBill_DriveProfitabilityAndCreditsNetActuals()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (branch, division) = await DefaultDimension(test);
        var projects = new ProjectService(test.Db, test.Access);
        var project = await CreateActiveProject(test, projects, division.Id, "JOB-DOC", 900m);
        var costCode = await projects.AddCostCodeAsync(test.UserId,
            new(test.Organisation.Id, project.Id, "MAT", "Materials", 900m));

        var invoice = await test.SalesInvoices.CreateAndPostAsync(test.UserId, new(
            test.Organisation.Id, test.Customer.Id, new(2026, 8, 20), new(2026, 9, 20),
            [new("Project services", 1m, 1_000m, VatTreatment.ZeroRated,
                test.Account("4000").Id, ProjectId: project.Id)],
            branch.Id, division.Id));
        var bill = await test.Purchasing.PostBillAsync(test.UserId, new(
            test.Organisation.Id, test.Supplier.Id, "PROJECT-BILL-001",
            new(2026, 8, 21), new(2026, 9, 21),
            [new("Project materials", 1m, 600m, VatTreatment.ZeroRated,
                test.Account("6000").Id, ProjectId: project.Id,
                ProjectCostCodeId: costCode.Id)], branch.Id, division.Id));

        var service = new ProjectProfitabilityService(test.Db, projects);
        var result = Assert.Single(await service.GetAsync(test.UserId, test.Organisation.Id));
        Assert.Equal(1_000m, result.ActualRevenue);
        Assert.Equal(600m, result.ActualCost);
        Assert.Equal(project.Id, (await test.Db.PostedJournalLines
            .SingleAsync(x => x.PostedJournalId == invoice.PostedJournalId &&
                x.LedgerAccountId == test.Account("4000").Id)).ProjectId);
        Assert.Equal(costCode.Id, (await test.Db.PostedJournalLines
            .SingleAsync(x => x.PostedJournalId == bill.PostedJournalId &&
                x.LedgerAccountId == test.Account("6000").Id)).ProjectCostCodeId);

        await new SalesCreditNoteService(test.Db, test.Access, test.Posting).CreateAsync(
            test.UserId, new(test.Organisation.Id, invoice.Id, new(2026, 8, 22),
                "Reverse project invoice", invoice.Total, false));
        await new SupplierCreditNoteService(test.Db, test.Access, test.Posting).CreateAsync(
            test.UserId, new(test.Organisation.Id, bill.Id, new(2026, 8, 22),
                "Reverse project bill", bill.Total, false));

        result = Assert.Single(await service.GetAsync(test.UserId, test.Organisation.Id));
        Assert.Equal(0m, result.ActualRevenue);
        Assert.Equal(0m, result.ActualCost);
        Assert.Equal(0m, result.ActualMargin);
    }

    [Fact]
    public async Task ApprovedPurchaseOrder_IsCommittedUntilCancelled()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (_, division) = await DefaultDimension(test);
        var projects = new ProjectService(test.Db, test.Access);
        var project = await CreateActiveProject(test, projects, division.Id, "JOB-PO", 1_000m);
        var costCode = await projects.AddCostCodeAsync(test.UserId,
            new(test.Organisation.Id, project.Id, "SUB", "Subcontract", 700m));
        var order = await test.PurchaseOrders.CreateDraftAsync(test.UserId, new(
            test.Organisation.Id, test.Supplier.Id, new(2026, 8, 20), new(2026, 9, 20),
            "PROJECT-PO", "Project commitment",
            [new("Subcontract work", 3m, 100m, test.Account("6000").Id,
                ProjectId: project.Id, ProjectCostCodeId: costCode.Id)],
            DivisionId: division.Id));
        var service = new ProjectProfitabilityService(test.Db, projects);

        Assert.Equal(0m, Assert.Single(await service.GetAsync(
            test.UserId, test.Organisation.Id)).CommittedCost);
        await test.PurchaseOrders.ApproveAsync(test.UserId, test.Organisation.Id, order.Id);
        var approved = Assert.Single(await service.GetAsync(test.UserId, test.Organisation.Id));

        Assert.Equal(300m, approved.CommittedCost);
        Assert.Equal(700m, approved.UncommittedForecastCost);
        Assert.Equal(300m, Assert.Single(approved.CostCodes).CommittedCost);
        Assert.Equal(400m, Assert.Single(approved.CostCodes).RemainingBudgetAfterCommitment);

        await test.PurchaseOrders.CancelAsync(test.UserId, test.Organisation.Id, order.Id);
        Assert.Equal(0m, Assert.Single(await service.GetAsync(
            test.UserId, test.Organisation.Id)).CommittedCost);
    }

    [Fact]
    public async Task SalesInvoiceDraft_RejectsProjectFromAnotherDimension()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (defaultBranch, defaultDivision) = await DefaultDimension(test);
        var structures = new EnterpriseStructureService(test.Db);
        var otherBranch = await structures.AddBranchAsync(
            test.UserId, test.Organisation.Id, "NORTH", "Northern");
        var otherDivision = await structures.AddDivisionAsync(
            test.UserId, test.Organisation.Id, otherBranch.Id, "FIELD", "Field");
        var projects = new ProjectService(test.Db, test.Access);
        var project = await CreateActiveProject(test, projects, otherDivision.Id, "JOB-NORTH", 100m);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.SalesInvoices.CreateDraftAsync(test.UserId, new(
                test.Organisation.Id, test.Customer.Id, new(2026, 8, 20), new(2026, 9, 20),
                [new("Wrong project", 1m, 100m, VatTreatment.ZeroRated,
                    test.Account("4000").Id, ProjectId: project.Id)],
                defaultBranch.Id, defaultDivision.Id)));

        Assert.Contains("branch and division", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await test.Db.SalesInvoices.ToListAsync());
    }

    [Fact]
    public async Task TaggedJournalLines_DriveActualRevenueCostAndCostCodePerformance()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (branch, division) = await DefaultDimension(test);
        var projects = new ProjectService(test.Db, test.Access);
        var project = await CreateActiveProject(test, projects, division.Id, "JOB-A", 800m);
        var costCode = await projects.AddCostCodeAsync(test.UserId,
            new(test.Organisation.Id, project.Id, "LAB", "Labour", 800m));

        await test.Posting.PostAsync(test.UserId, new(
            test.Organisation.Id, new(2026, 8, 20), "PROJECT-REVENUE", "Project revenue",
            [
                new(test.Account("1000").Id, "Receivable", 1_000m, 0m),
                new(test.Account("4000").Id, "Project revenue", 0m, 1_000m,
                    ProjectId: project.Id)
            ], branch.Id, division.Id));
        await test.Posting.PostAsync(test.UserId, new(
            test.Organisation.Id, new(2026, 8, 21), "PROJECT-COST", "Project cost",
            [
                new(test.Account("6000").Id, "Project labour", 600m, 0m,
                    ProjectId: project.Id, ProjectCostCodeId: costCode.Id),
                new(test.Account("1000").Id, "Cash", 0m, 600m)
            ], branch.Id, division.Id));

        var result = Assert.Single(await new ProjectProfitabilityService(test.Db, projects)
            .GetAsync(test.UserId, test.Organisation.Id));
        var labour = Assert.Single(result.CostCodes);

        Assert.Equal(1_000m, result.ActualRevenue);
        Assert.Equal(600m, result.ActualCost);
        Assert.Equal(400m, result.ActualMargin);
        Assert.Equal(200m, result.ForecastCostToComplete);
        Assert.Equal(75m, result.CostProgressPercent);
        Assert.Equal(0m, result.UncodedActualCost);
        Assert.Equal(600m, labour.ActualCost);
        Assert.Equal(200m, labour.RemainingBudgetAfterCommitment);
        Assert.Equal(2, await test.Db.PostedJournalLines.CountAsync(x => x.ProjectId == project.Id));
    }

    [Fact]
    public async Task PostAsync_RejectsProjectFromAnotherJournalDimension()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (defaultBranch, defaultDivision) = await DefaultDimension(test);
        var structures = new EnterpriseStructureService(test.Db);
        var otherBranch = await structures.AddBranchAsync(
            test.UserId, test.Organisation.Id, "WEST", "Western");
        var otherDivision = await structures.AddDivisionAsync(
            test.UserId, test.Organisation.Id, otherBranch.Id, "OPS", "Operations");
        var projects = new ProjectService(test.Db, test.Access);
        var project = await CreateActiveProject(test, projects, otherDivision.Id, "JOB-WEST", 100m);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Posting.PostAsync(test.UserId, new(
                test.Organisation.Id, new(2026, 8, 20), "WRONG-DIM", "Wrong dimension",
                [
                    new(test.Account("1000").Id, "Debit", 10m, 0m, ProjectId: project.Id),
                    new(test.Account("4000").Id, "Credit", 0m, 10m)
                ], defaultBranch.Id, defaultDivision.Id)));

        Assert.Contains("branch and division", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await test.Db.PostedJournals.ToListAsync());
    }

    [Fact]
    public async Task PostAsync_RejectsCostCodeFromAnotherProject()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (branch, division) = await DefaultDimension(test);
        var projects = new ProjectService(test.Db, test.Access);
        var project = await CreateActiveProject(test, projects, division.Id, "JOB-ONE", 100m);
        var otherProject = await CreateActiveProject(test, projects, division.Id, "JOB-TWO", 100m);
        var otherCode = await projects.AddCostCodeAsync(test.UserId,
            new(test.Organisation.Id, otherProject.Id, "OTHER", "Other project", 50m));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Posting.PostAsync(test.UserId, new(
                test.Organisation.Id, new(2026, 8, 20), "WRONG-CODE", "Wrong cost code",
                [
                    new(test.Account("6000").Id, "Expense", 10m, 0m,
                        ProjectId: project.Id, ProjectCostCodeId: otherCode.Id),
                    new(test.Account("1000").Id, "Credit", 0m, 10m)
                ], branch.Id, division.Id)));

        Assert.Contains("cost code", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await test.Db.PostedJournals.ToListAsync());
    }

    private static async Task<Project> CreateActiveProject(
        AccountingTestDatabase test,
        ProjectService projects,
        Guid divisionId,
        string number,
        decimal forecastCost)
    {
        var project = await projects.SaveAsync(test.UserId, new(
            test.Organisation.Id, null, number, number, null, divisionId,
            test.Customer.Id, new(2026, 8, 1), null, 1_000m, 0m,
            forecastCost, 0m));
        return await projects.ChangeStatusAsync(
            test.UserId, test.Organisation.Id, project.Id, ProjectStatus.Active);
    }

    private static async Task<(Branch Branch, Division Division)> DefaultDimension(
        AccountingTestDatabase test)
    {
        var branch = await test.Db.Branches.Include(x => x.Divisions)
            .SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.IsDefault);
        return (branch, branch.Divisions.Single(x => x.IsDefault));
    }
}
