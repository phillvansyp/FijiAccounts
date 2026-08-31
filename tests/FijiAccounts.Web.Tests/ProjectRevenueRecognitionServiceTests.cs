using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ProjectRevenueRecognitionServiceTests
{
    [Fact]
    public async Task CostToCost_ReportsContractAssetAndLiabilityAsAtSelectedDate()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (branch, division) = await DefaultDimensionAsync(test);
        var project = await CreateActiveProjectAsync(
            test, division.Id, "JOB-WIP", 100_000m, 80_000m,
            ProjectRevenueRecognitionMethod.CostToCost);

        await test.Posting.PostAsync(test.UserId, new(
            test.Organisation.Id,
            new DateOnly(2026, 8, 20),
            "WIP-COST",
            "Project costs",
            [
                new(test.Account("6000").Id, "Project costs", 40_000m, 0m,
                    ProjectId: project.Id),
                new(test.Account("1000").Id, "Cash", 0m, 40_000m)
            ],
            branch.Id,
            division.Id));
        await test.Posting.PostAsync(test.UserId, new(
            test.Organisation.Id,
            new DateOnly(2026, 8, 21),
            "WIP-REVENUE",
            "Posted project revenue",
            [
                new(test.Account("1100").Id, "Receivable", 30_000m, 0m),
                new(test.Account("4000").Id, "Project revenue", 0m, 30_000m,
                    ProjectId: project.Id)
            ],
            branch.Id,
            division.Id));
        await test.Posting.PostAsync(test.UserId, new(
            test.Organisation.Id,
            new DateOnly(2026, 9, 1),
            "WIP-FUTURE-REVENUE",
            "Future posted project revenue",
            [
                new(test.Account("1100").Id, "Receivable", 40_000m, 0m),
                new(test.Account("4000").Id, "Project revenue", 0m, 40_000m,
                    ProjectId: project.Id)
            ],
            branch.Id,
            division.Id));
        var service = new ProjectRevenueRecognitionService(
            test.Db, new ProjectService(test.Db, test.Access));

        var august = Assert.Single(await service.GetAsync(
            test.UserId, test.Organisation.Id, new DateOnly(2026, 8, 31)));
        var september = Assert.Single(await service.GetAsync(
            test.UserId, test.Organisation.Id, new DateOnly(2026, 9, 30)));

        Assert.Equal(50m, august.CompletionPercent);
        Assert.Equal(50_000m, august.RecognizedRevenue);
        Assert.Equal(30_000m, august.PostedRevenue);
        Assert.Equal(20_000m, august.ContractAsset);
        Assert.Equal(0m, august.ContractLiability);
        Assert.Equal(10_000m, august.ExpectedGrossProfitToDate);
        Assert.Equal(0m, september.ContractAsset);
        Assert.Equal(20_000m, september.ContractLiability);
    }

    [Fact]
    public async Task CertifiedClaims_UsesApprovedWorkAndTracksRetentionAgainstPostedInvoice()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (_, division) = await DefaultDimensionAsync(test);
        var project = await CreateActiveProjectAsync(
            test, division.Id, "JOB-CLAIM-WIP", 100_000m, 80_000m,
            ProjectRevenueRecognitionMethod.CertifiedClaims, retentionPercent: 10m);
        var claims = new ProjectProgressClaimService(
            test.Db, test.Access, test.SalesInvoices);
        var claim = await claims.CreateAsync(test.UserId, new(
            test.Organisation.Id,
            project.Id,
            "PC-WIP-001",
            "Certified August works",
            new DateOnly(2026, 8, 25),
            40_000m,
            0m,
            test.Account("4000").Id,
            VatTreatment.ZeroRated));
        await claims.SubmitAsync(test.UserId, test.Organisation.Id, claim.Id);
        await claims.DecideAsync(
            test.UserId, test.Organisation.Id, claim.Id, true, "Certified");
        var approvedClaim = await test.Db.ProjectProgressClaims
            .SingleAsync(x => x.Id == claim.Id);
        approvedClaim.DecidedAt = new DateTimeOffset(
            2026, 8, 25, 0, 0, 0, TimeSpan.Zero);
        await test.Db.SaveChangesAsync();
        var invoice = await claims.GenerateDraftInvoiceAsync(
            test.UserId,
            test.Organisation.Id,
            claim.Id,
            new DateOnly(2026, 8, 25),
            new DateOnly(2026, 9, 24));
        await test.SalesInvoices.PostDraftAsync(
            test.UserId, test.Organisation.Id, invoice.Id);

        var result = Assert.Single(await new ProjectRevenueRecognitionService(
            test.Db, new ProjectService(test.Db, test.Access)).GetAsync(
                test.UserId, test.Organisation.Id, new DateOnly(2026, 8, 31)));

        Assert.Equal(ProjectRevenueRecognitionMethod.CertifiedClaims, result.Method);
        Assert.Equal(40m, result.CompletionPercent);
        Assert.Equal(40_000m, result.RecognizedRevenue);
        Assert.Equal(36_000m, result.PostedRevenue);
        Assert.Equal(4_000m, result.ContractAsset);
        Assert.Equal(40_000m, result.CertifiedWorkValue);
        Assert.Equal(4_000m, result.OutstandingRetention);
    }

    [Fact]
    public async Task ActiveProject_CannotChangeRevenueRecognitionMethod()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (_, division) = await DefaultDimensionAsync(test);
        var projects = new ProjectService(test.Db, test.Access);
        var project = await CreateActiveProjectAsync(
            test, division.Id, "JOB-POLICY", 100_000m, 80_000m,
            ProjectRevenueRecognitionMethod.CostToCost);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            projects.SaveAsync(test.UserId, new ProjectRequest(
                test.Organisation.Id,
                project.Id,
                project.ProjectNumber,
                project.Name,
                project.Description,
                project.DivisionId,
                project.CustomerId,
                project.StartDate,
                project.ExpectedCompletionDate,
                project.OriginalContractValue,
                project.OpeningApprovedVariationValue,
                project.ForecastCost,
                project.RetentionPercent,
                ProjectRevenueRecognitionMethod.CertifiedClaims)));

        Assert.Contains("cannot be changed", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Project> CreateActiveProjectAsync(
        AccountingTestDatabase test,
        Guid divisionId,
        string number,
        decimal contractValue,
        decimal forecastCost,
        ProjectRevenueRecognitionMethod method,
        decimal retentionPercent = 0m)
    {
        var projects = new ProjectService(test.Db, test.Access);
        var project = await projects.SaveAsync(test.UserId, new ProjectRequest(
            test.Organisation.Id,
            null,
            number,
            number,
            null,
            divisionId,
            test.Customer.Id,
            new DateOnly(2026, 8, 1),
            null,
            contractValue,
            0m,
            forecastCost,
            retentionPercent,
            method));
        return await projects.ChangeStatusAsync(
            test.UserId, test.Organisation.Id, project.Id, ProjectStatus.Active);
    }

    private static async Task<(Branch Branch, Division Division)> DefaultDimensionAsync(
        AccountingTestDatabase test)
    {
        var branch = await test.Db.Branches.Include(x => x.Divisions)
            .SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.IsDefault);
        return (branch, branch.Divisions.Single(x => x.IsDefault));
    }
}
