using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ProjectProgressClaimServiceTests
{
    [Fact]
    public async Task ApprovedClaim_GeneratesOneProjectCodedDraftInvoiceAndTracksRetention()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var project = await CreateActiveProjectAsync(test, retentionPercent: 10m);
        var service = new ProjectProgressClaimService(
            test.Db, test.Access, test.SalesInvoices);

        var claim = await service.CreateAsync(test.UserId, new(
            test.Organisation.Id,
            project.Id,
            "PC-001",
            "Works completed through August",
            new DateOnly(2026, 8, 31),
            50_000m,
            0m,
            test.Account("4000").Id,
            VatTreatment.Standard));
        await service.SubmitAsync(test.UserId, test.Organisation.Id, claim.Id);
        await service.DecideAsync(
            test.UserId, test.Organisation.Id, claim.Id, true, "Certified");
        var invoice = await service.GenerateDraftInvoiceAsync(
            test.UserId,
            test.Organisation.Id,
            claim.Id,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 10, 1));

        Assert.Equal(5_000m, claim.RetentionHeldAmount);
        Assert.Equal(45_000m, claim.CertifiedAmount);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(45_000m, invoice.Subtotal);
        Assert.Equal(project.Id, Assert.Single(invoice.Lines).ProjectId);
        var reloaded = await new ProjectService(test.Db, test.Access)
            .ListAsync(test.UserId, test.Organisation.Id);
        var savedProject = Assert.Single(reloaded);
        var savedClaim = Assert.Single(savedProject.ProgressClaims);
        Assert.Equal(ProjectProgressClaimStatus.Invoiced, savedClaim.Status);
        Assert.Equal(invoice.Id, savedClaim.SalesInvoiceId);
        Assert.Equal(50_000m, savedProject.CertifiedWorkValue);
        Assert.Equal(5_000m, savedProject.OutstandingRetention);
        Assert.Equal(
            [
                "ProjectProgressClaimCreated",
                "ProjectProgressClaimSubmitted",
                "ProjectProgressClaimApproved",
                "ProjectProgressClaimInvoiced"
            ],
            await test.Db.AuditEvents.AsNoTracking()
                .Where(x => x.EntityType == nameof(ProjectProgressClaim) &&
                    x.EntityId == claim.Id.ToString())
                .OrderBy(x => x.Id)
                .Select(x => x.EventType)
                .ToArrayAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateDraftInvoiceAsync(
                test.UserId,
                test.Organisation.Id,
                claim.Id,
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public async Task Approval_EnforcesContractCeilingAndOutstandingRetention()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var project = await CreateActiveProjectAsync(test, retentionPercent: 10m);
        var service = new ProjectProgressClaimService(
            test.Db, test.Access, test.SalesInvoices);
        var revenueAccountId = test.Account("4000").Id;

        var first = await CreateClaimAsync(
            service, test, project.Id, "PC-001", 50_000m, 0m, revenueAccountId);
        await service.SubmitAsync(test.UserId, test.Organisation.Id, first.Id);
        await service.DecideAsync(
            test.UserId, test.Organisation.Id, first.Id, true, null);

        var release = await CreateClaimAsync(
            service, test, project.Id, "PC-002", 0m, 5_000m, revenueAccountId);
        await service.SubmitAsync(test.UserId, test.Organisation.Id, release.Id);
        await service.DecideAsync(
            test.UserId, test.Organisation.Id, release.Id, true, "Final retention release");

        var overRelease = await CreateClaimAsync(
            service, test, project.Id, "PC-003", 0m, 1m, revenueAccountId);
        await service.SubmitAsync(test.UserId, test.Organisation.Id, overRelease.Id);
        var retentionException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DecideAsync(
                test.UserId, test.Organisation.Id, overRelease.Id, true, null));
        Assert.Contains("outstanding retention", retentionException.Message,
            StringComparison.OrdinalIgnoreCase);

        var overContract = await CreateClaimAsync(
            service, test, project.Id, "PC-004", 50_001m, 0m, revenueAccountId);
        await service.SubmitAsync(test.UserId, test.Organisation.Id, overContract.Id);
        var contractException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DecideAsync(
                test.UserId, test.Organisation.Id, overContract.Id, true, null));
        Assert.Contains("contract value", contractException.Message,
            StringComparison.OrdinalIgnoreCase);

        var reloaded = Assert.Single(await new ProjectService(test.Db, test.Access)
            .ListAsync(test.UserId, test.Organisation.Id));
        Assert.Equal(50_000m, reloaded.CertifiedWorkValue);
        Assert.Equal(0m, reloaded.OutstandingRetention);
    }

    [Fact]
    public async Task Bookkeeper_CanSubmitButCannotApproveClaim()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var project = await CreateActiveProjectAsync(test, retentionPercent: 5m);
        var bookkeeper = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "claim-bookkeeper@example.com",
            NormalizedUserName = "CLAIM-BOOKKEEPER@EXAMPLE.COM",
            Email = "claim-bookkeeper@example.com",
            NormalizedEmail = "CLAIM-BOOKKEEPER@EXAMPLE.COM",
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
        var service = new ProjectProgressClaimService(
            test.Db, test.Access, test.SalesInvoices);

        var claim = await service.CreateAsync(bookkeeper.Id, new(
            test.Organisation.Id,
            project.Id,
            "PC-AUTH",
            "Authority test",
            new DateOnly(2026, 8, 31),
            10_000m,
            0m,
            test.Account("4000").Id,
            VatTreatment.Standard));
        await service.SubmitAsync(bookkeeper.Id, test.Organisation.Id, claim.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.DecideAsync(
                bookkeeper.Id, test.Organisation.Id, claim.Id, true, null));
    }

    private static Task<ProjectProgressClaim> CreateClaimAsync(
        ProjectProgressClaimService service,
        AccountingTestDatabase test,
        Guid projectId,
        string claimNumber,
        decimal work,
        decimal release,
        Guid revenueAccountId) =>
        service.CreateAsync(test.UserId, new(
            test.Organisation.Id,
            projectId,
            claimNumber,
            $"Claim {claimNumber}",
            new DateOnly(2026, 8, 31),
            work,
            release,
            revenueAccountId,
            VatTreatment.Standard));

    private static async Task<Project> CreateActiveProjectAsync(
        AccountingTestDatabase test,
        decimal retentionPercent)
    {
        var branch = await test.Db.Branches.Include(x => x.Divisions)
            .SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.IsDefault);
        var division = branch.Divisions.Single(x => x.IsDefault);
        var projects = new ProjectService(test.Db, test.Access);
        var project = await projects.SaveAsync(test.UserId, new ProjectRequest(
            test.Organisation.Id,
            null,
            "JOB-CLAIM",
            "Claim test project",
            null,
            division.Id,
            test.Customer.Id,
            new DateOnly(2026, 8, 1),
            new DateOnly(2027, 3, 31),
            100_000m,
            0m,
            80_000m,
            retentionPercent));
        await projects.ChangeStatusAsync(
            test.UserId, test.Organisation.Id, project.Id, ProjectStatus.Active);
        return project;
    }
}
