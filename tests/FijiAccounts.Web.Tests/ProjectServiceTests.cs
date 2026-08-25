using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ProjectServiceTests
{
    [Fact]
    public async Task SaveAsync_CreatesDimensionProjectWithForecastControlsAndAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (branch, division) = await DefaultDimension(test);
        var service = new ProjectService(test.Db, test.Access);

        var project = await service.SaveAsync(test.UserId, Request(
            test, division.Id, "JOB-001", original: 1_000_000m,
            variations: 125_000m, forecastCost: 900_000m, retention: 10m));

        Assert.Equal(branch.Id, project.BranchId);
        Assert.Equal(division.Id, project.DivisionId);
        Assert.Equal(1_125_000m, project.RevisedContractValue);
        Assert.Equal(225_000m, project.ForecastMargin);
        Assert.Equal(112_500m, project.RetentionExposure);
        Assert.Equal(ProjectStatus.Draft, project.Status);
        Assert.Contains(await test.Db.AuditEvents.ToListAsync(), x =>
            x.EntityId == project.Id.ToString() && x.EventType == "ProjectCreated");
    }

    [Fact]
    public async Task VariationApproval_DrivesRevisedContractValueAndAuditHistory()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (_, division) = await DefaultDimension(test);
        var service = new ProjectService(test.Db, test.Access);
        var project = await service.SaveAsync(test.UserId, Request(
            test, division.Id, "JOB-VAR", original: 100_000m, variations: 10_000m));
        await service.ChangeStatusAsync(
            test.UserId, test.Organisation.Id, project.Id, ProjectStatus.Active);

        var variation = await service.CreateVariationAsync(test.UserId, new(
            test.Organisation.Id, project.Id, "VO-001", "Additional drainage",
            "Client-requested drainage works", 25_000m, new DateOnly(2026, 9, 1)));
        await service.SubmitVariationAsync(
            test.UserId, test.Organisation.Id, variation.Id);
        await service.DecideVariationAsync(
            test.UserId, test.Organisation.Id, variation.Id, true, "Client approved");

        var reloaded = Assert.Single(await service.ListAsync(
            test.UserId, test.Organisation.Id));
        var approved = Assert.Single(reloaded.Variations);
        Assert.Equal(ProjectVariationStatus.Approved, approved.Status);
        Assert.Equal(10_000m, reloaded.OpeningApprovedVariationValue);
        Assert.Equal(35_000m, reloaded.ApprovedVariationValue);
        Assert.Equal(135_000m, reloaded.RevisedContractValue);
        Assert.Equal(test.UserId, approved.DecidedByUserId);
        Assert.Equal(
            ["ProjectVariationCreated", "ProjectVariationSubmitted", "ProjectVariationApproved"],
            await test.Db.AuditEvents.AsNoTracking()
                .Where(x => x.EntityType == nameof(ProjectVariation) && x.EntityId == variation.Id.ToString())
                .OrderBy(x => x.Id)
                .Select(x => x.EventType)
                .ToArrayAsync());
    }

    [Fact]
    public async Task VariationDecision_RejectsInvalidContractReductionAndRequiresRejectionReason()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (_, division) = await DefaultDimension(test);
        var service = new ProjectService(test.Db, test.Access);
        var project = await service.SaveAsync(test.UserId,
            Request(test, division.Id, "JOB-NEG", original: 100_000m));
        await service.ChangeStatusAsync(
            test.UserId, test.Organisation.Id, project.Id, ProjectStatus.Active);
        var variation = await service.CreateVariationAsync(test.UserId, new(
            test.Organisation.Id, project.Id, "VO-NEG", "Scope deletion", null,
            -150_000m, new DateOnly(2026, 9, 2)));
        await service.SubmitVariationAsync(test.UserId, test.Organisation.Id, variation.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DecideVariationAsync(
                test.UserId, test.Organisation.Id, variation.Id, true, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DecideVariationAsync(
                test.UserId, test.Organisation.Id, variation.Id, false, " "));
        await service.DecideVariationAsync(
            test.UserId, test.Organisation.Id, variation.Id, false, "Not accepted by client");

        var reloaded = Assert.Single(await service.ListAsync(
            test.UserId, test.Organisation.Id));
        Assert.Equal(0m, reloaded.ApprovedVariationValue);
        Assert.Equal(ProjectVariationStatus.Rejected, Assert.Single(reloaded.Variations).Status);
    }

    [Fact]
    public async Task VariationDecision_RequiresOwnerOrAdministrator()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (_, division) = await DefaultDimension(test);
        var service = new ProjectService(test.Db, test.Access);
        var project = await service.SaveAsync(test.UserId,
            Request(test, division.Id, "JOB-AUTH"));
        await service.ChangeStatusAsync(
            test.UserId, test.Organisation.Id, project.Id, ProjectStatus.Active);
        var bookkeeper = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "variation-bookkeeper@example.com",
            NormalizedUserName = "VARIATION-BOOKKEEPER@EXAMPLE.COM",
            Email = "variation-bookkeeper@example.com",
            NormalizedEmail = "VARIATION-BOOKKEEPER@EXAMPLE.COM",
            EmailConfirmed = true
        };
        test.Db.Users.Add(bookkeeper);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = bookkeeper.Id,
            User = bookkeeper,
            Role = OrganisationRole.Bookkeeper
        });
        await test.Db.SaveChangesAsync();

        var variation = await service.CreateVariationAsync(bookkeeper.Id, new(
            test.Organisation.Id, project.Id, "VO-AUTH", "Approved authority test",
            null, 5_000m, new DateOnly(2026, 9, 3)));
        await service.SubmitVariationAsync(
            bookkeeper.Id, test.Organisation.Id, variation.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.DecideVariationAsync(
                bookkeeper.Id, test.Organisation.Id, variation.Id, true, null));
    }

    [Fact]
    public async Task AddCostCodeAsync_RejectsDuplicateWithinProject()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (_, division) = await DefaultDimension(test);
        var service = new ProjectService(test.Db, test.Access);
        var project = await service.SaveAsync(test.UserId,
            Request(test, division.Id, "JOB-002"));
        var request = new ProjectCostCodeRequest(
            test.Organisation.Id, project.Id, "LAB", "Labour", 250_000m);

        var code = await service.AddCostCodeAsync(test.UserId, request);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddCostCodeAsync(test.UserId, request));

        Assert.Equal("LAB", code.Code);
        Assert.Contains("already", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await test.Db.ProjectCostCodes.ToListAsync());
    }

    [Fact]
    public async Task ChangeStatusAsync_EnforcesLifecycleAndClosesCompletedProject()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (_, division) = await DefaultDimension(test);
        var service = new ProjectService(test.Db, test.Access);
        var project = await service.SaveAsync(test.UserId,
            Request(test, division.Id, "JOB-003"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChangeStatusAsync(test.UserId, test.Organisation.Id,
                project.Id, ProjectStatus.Completed));
        await service.ChangeStatusAsync(test.UserId, test.Organisation.Id,
            project.Id, ProjectStatus.Active);
        var completed = await service.ChangeStatusAsync(test.UserId,
            test.Organisation.Id, project.Id, ProjectStatus.Completed);

        Assert.Equal(ProjectStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedDate);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddCostCodeAsync(test.UserId, new ProjectCostCodeRequest(
                test.Organisation.Id, project.Id, "CLOSED", "Closed", 1m)));
    }

    [Fact]
    public async Task RestrictedMember_OnlyListsGrantedDivisionAndCannotMaintainOtherProject()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var nadi = await structures.AddBranchAsync(
            test.UserId, test.Organisation.Id, "NADI", "Nadi");
        var retail = await structures.AddDivisionAsync(
            test.UserId, test.Organisation.Id, nadi.Id, "RETAIL", "Retail");
        var servicesDivision = await structures.AddDivisionAsync(
            test.UserId, test.Organisation.Id, nadi.Id, "SERV", "Services");
        var member = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "project-manager@example.com",
            NormalizedUserName = "PROJECT-MANAGER@EXAMPLE.COM",
            Email = "project-manager@example.com",
            NormalizedEmail = "PROJECT-MANAGER@EXAMPLE.COM",
            EmailConfirmed = true
        };
        test.Db.Users.Add(member);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = member.Id,
            User = member,
            Role = OrganisationRole.Bookkeeper
        });
        await test.Db.SaveChangesAsync();
        await test.Access.SetDimensionAccessModeAsync(test.UserId,
            test.Organisation.Id, member.Id, DimensionAccessMode.Restricted);
        await test.Access.AddDimensionAccessGrantAsync(test.UserId,
            test.Organisation.Id, member.Id, nadi.Id, retail.Id);
        var service = new ProjectService(test.Db, test.Access);
        var retailProject = await service.SaveAsync(test.UserId,
            Request(test, retail.Id, "RETAIL-01"));
        await service.SaveAsync(test.UserId,
            Request(test, servicesDivision.Id, "SERV-01"));

        var visible = await service.ListAsync(member.Id, test.Organisation.Id);
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SaveAsync(member.Id, Request(test, servicesDivision.Id, "DENIED")));

        Assert.Equal(retailProject.Id, Assert.Single(visible).Id);
        Assert.Contains("division", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_RejectsDuplicateProjectNumberAndInvalidForecastInputs()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (_, division) = await DefaultDimension(test);
        var service = new ProjectService(test.Db, test.Access);
        await service.SaveAsync(test.UserId, Request(test, division.Id, "JOB-004"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(test.UserId, Request(test, division.Id, "JOB-004")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(test.UserId, Request(
                test, division.Id, "JOB-005", retention: 101m)));
    }

    private static ProjectRequest Request(
        AccountingTestDatabase test,
        Guid divisionId,
        string number,
        decimal original = 100_000m,
        decimal variations = 0m,
        decimal forecastCost = 80_000m,
        decimal retention = 5m) =>
        new(
            test.Organisation.Id,
            null,
            number,
            $"Project {number}",
            "Project control test",
            divisionId,
            test.Customer.Id,
            new DateOnly(2026, 8, 1),
            new DateOnly(2027, 3, 31),
            original,
            variations,
            forecastCost,
            retention);

    private static async Task<(Branch Branch, Division Division)> DefaultDimension(
        AccountingTestDatabase test)
    {
        var branch = await test.Db.Branches.Include(x => x.Divisions)
            .SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.IsDefault);
        return (branch, branch.Divisions.Single(x => x.IsDefault));
    }
}
