using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class PurchaseRequisitionServiceTests
{
    [Fact]
    public async Task SubmittedRequisitionRequiresIndependentApprovalAndConvertsAtomically()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var policies = new PurchaseApprovalPolicyService(test.Db, test.Access);
        var service = new PurchaseRequisitionService(test.Db, test.Access, test.PurchaseOrders, policies);
        var approver = await AddAdministratorAsync(test);
        var request = await RequestAsync(test);
        var projects = new ProjectService(test.Db, test.Access);
        var project = await projects.SaveAsync(test.UserId, new(
            test.Organisation.Id, null, "JOB-REQ", "Requisition project", null,
            request.DivisionId, test.Customer.Id, new DateOnly(2026, 8, 1), null,
            1_000m, 0m, 500m, 0m));
        project = await projects.ChangeStatusAsync(
            test.UserId, test.Organisation.Id, project.Id, ProjectStatus.Active);
        var costCode = await projects.AddCostCodeAsync(test.UserId,
            new(test.Organisation.Id, project.Id, "SUP", "Supplies", 500m));
        request = request with
        {
            Lines =
            [
                request.Lines[0] with
                {
                    ProjectId = project.Id,
                    ProjectCostCodeId = costCode.Id
                }
            ]
        };

        var requisition = await service.CreateDraftAsync(test.UserId, request);
        await service.SubmitAsync(test.UserId, test.Organisation.Id, requisition.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(test.UserId, test.Organisation.Id, requisition.Id));
        await service.ApproveAsync(approver.Id, test.Organisation.Id, requisition.Id);
        var order = await service.ConvertToPurchaseOrderAsync(
            test.UserId,
            test.Organisation.Id,
            requisition.Id);

        var saved = await test.Db.PurchaseRequisitions.AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == requisition.Id);
        Assert.Equal("PR-000001", saved.RequisitionNumber);
        Assert.Equal(PurchaseRequisitionStatus.Converted, saved.Status);
        Assert.Equal(approver.Id, saved.ApprovedByUserId);
        Assert.Equal(100m, saved.Total);
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.Equal(requisition.Id, order.PurchaseRequisitionId);
        Assert.Equal(requisition.RequisitionNumber, order.SupplierReference);
        var orderLine = Assert.Single(order.Lines);
        Assert.Equal(project.Id, orderLine.ProjectId);
        Assert.Equal(costCode.Id, orderLine.ProjectCostCodeId);
        Assert.Equal(100m, Assert.Single(await new ProjectProfitabilityService(test.Db, projects)
            .GetAsync(test.UserId, test.Organisation.Id)).CommittedCost);
        Assert.Empty(await test.Db.PostedJournals.ToListAsync());
        var orderApproval = await test.Db.AuditEvents.AsNoTracking().SingleAsync(x =>
            x.EntityType == nameof(PurchaseOrder) &&
            x.EntityId == order.Id.ToString() &&
            x.EventType == "PurchaseOrderApprovedFromRequisition");
        Assert.Equal(approver.Id, orderApproval.UserId);

        var audits = await test.Db.AuditEvents.AsNoTracking()
            .Where(x => x.EntityType == nameof(PurchaseRequisition) && x.EntityId == requisition.Id.ToString())
            .OrderBy(x => x.Id)
            .Select(x => x.EventType)
            .ToListAsync();
        Assert.Equal(
            [
                "PurchaseRequisitionCreated",
                "PurchaseRequisitionSubmitted",
                "PurchaseRequisitionApproved",
                "PurchaseRequisitionConverted"
            ],
            audits);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConvertToPurchaseOrderAsync(test.UserId, test.Organisation.Id, requisition.Id));
        Assert.Single(await test.Db.PurchaseOrders.Where(x => x.PurchaseRequisitionId == requisition.Id).ToListAsync());
    }

    [Fact]
    public async Task IndependentAdministratorCanRejectSubmittedRequisitionWithReason()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var policies = new PurchaseApprovalPolicyService(test.Db, test.Access);
        var service = new PurchaseRequisitionService(test.Db, test.Access, test.PurchaseOrders, policies);
        var approver = await AddAdministratorAsync(test);
        var requisition = await service.CreateDraftAsync(test.UserId, await RequestAsync(test));
        await service.SubmitAsync(test.UserId, test.Organisation.Id, requisition.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RejectAsync(test.UserId, test.Organisation.Id, requisition.Id, "Self rejection"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RejectAsync(approver.Id, test.Organisation.Id, requisition.Id, " "));
        await service.RejectAsync(approver.Id, test.Organisation.Id, requisition.Id, "Budget not available");

        var saved = await test.Db.PurchaseRequisitions.AsNoTracking().SingleAsync(x => x.Id == requisition.Id);
        Assert.Equal(PurchaseRequisitionStatus.Rejected, saved.Status);
        Assert.Equal(approver.Id, saved.RejectedByUserId);
        Assert.Equal("Budget not available", saved.RejectionReason);
        Assert.Null(saved.ApprovedByUserId);
        Assert.Empty(await test.Db.PurchaseOrders.ToListAsync());
    }

    [Fact]
    public async Task CreateRejectsCrossTenantDimensionAndReferencesWithoutAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var policies = new PurchaseApprovalPolicyService(test.Db, test.Access);
        var service = new PurchaseRequisitionService(test.Db, test.Access, test.PurchaseOrders, policies);
        var request = await RequestAsync(test);
        var otherOrganisation = new Organisation { LegalName = "Other Limited", Kind = OrganisationKind.Business };
        test.Db.Organisations.Add(otherOrganisation);
        new EnterpriseStructureService(test.Db).AddDefaultFor(otherOrganisation, test.UserId);
        await test.Db.SaveChangesAsync();
        var otherDivision = await test.Db.Divisions.AsNoTracking()
            .SingleAsync(x => x.Branch.OrganisationId == otherOrganisation.Id && x.IsDefault);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateDraftAsync(test.UserId, request with { DivisionId = otherDivision.Id }));

        Assert.Empty(await test.Db.PurchaseRequisitions.ToListAsync());
        Assert.Empty(await test.Db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task AmountPolicyIsSnapshottedAndEnforcesOwnerOnlyApproval()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var policies = new PurchaseApprovalPolicyService(test.Db, test.Access);
        var service = new PurchaseRequisitionService(test.Db, test.Access, test.PurchaseOrders, policies);
        var administrator = await AddMemberAsync(test, OrganisationRole.Administrator, "administrator@example.com");
        var independentOwner = await AddMemberAsync(test, OrganisationRole.Owner, "owner@example.com");
        var policy = await policies.CreateAsync(test.UserId, new PurchaseApprovalPolicyRequest(
            test.Organisation.Id,
            "CFO threshold",
            50m,
            null,
            PurchaseApprovalRequirement.OwnerOnly));
        var requisition = await service.CreateDraftAsync(test.UserId, await RequestAsync(test));

        await service.SubmitAsync(test.UserId, test.Organisation.Id, requisition.Id);
        var submitted = await test.Db.PurchaseRequisitions.AsNoTracking().SingleAsync(x => x.Id == requisition.Id);
        Assert.Equal(policy.Id, submitted.PurchaseApprovalPolicyId);
        Assert.Equal(PurchaseApprovalRequirement.OwnerOnly, submitted.RequiredApproval);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ApproveAsync(administrator.Id, test.Organisation.Id, requisition.Id));

        await policies.DeleteAsync(test.UserId, test.Organisation.Id, policy.Id);
        await service.ApproveAsync(independentOwner.Id, test.Organisation.Id, requisition.Id);

        var approved = await test.Db.PurchaseRequisitions.AsNoTracking().SingleAsync(x => x.Id == requisition.Id);
        Assert.Equal(PurchaseRequisitionStatus.Approved, approved.Status);
        Assert.Equal(PurchaseApprovalRequirement.OwnerOnly, approved.RequiredApproval);
        Assert.Null(approved.PurchaseApprovalPolicyId);
    }

    [Fact]
    public async Task PolicyRangesCannotOverlapWithinSameScopeAndSpecificScopeWins()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var policies = new PurchaseApprovalPolicyService(test.Db, test.Access);
        var request = await RequestAsync(test);
        await policies.CreateAsync(test.UserId, new PurchaseApprovalPolicyRequest(
            test.Organisation.Id,
            "Organisation high value",
            50m,
            500m,
            PurchaseApprovalRequirement.OwnerOnly));
        await Assert.ThrowsAsync<InvalidOperationException>(() => policies.CreateAsync(
            test.UserId,
            new PurchaseApprovalPolicyRequest(
                test.Organisation.Id,
                "Overlapping organisation rule",
                100m,
                200m,
                PurchaseApprovalRequirement.OwnerOrAdministrator)));
        var divisionPolicy = await policies.CreateAsync(test.UserId, new PurchaseApprovalPolicyRequest(
            test.Organisation.Id,
            "Division delegation",
            0m,
            200m,
            PurchaseApprovalRequirement.OwnerOrAdministrator,
            request.BranchId,
            request.DivisionId));

        var resolved = await policies.ResolveAsync(
            test.Organisation.Id,
            request.BranchId,
            request.DivisionId,
            100m);

        Assert.Equal(divisionPolicy.Id, resolved?.Id);
        Assert.Equal(2, await test.Db.PurchaseApprovalPolicies.CountAsync());
    }

    private static async Task<PurchaseRequisitionRequest> RequestAsync(AccountingTestDatabase test)
    {
        var branch = await test.Db.Branches.AsNoTracking().Include(x => x.Divisions)
            .SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.IsDefault);
        var division = branch.Divisions.Single(x => x.IsDefault);
        return new PurchaseRequisitionRequest(
            test.Organisation.Id,
            branch.Id,
            division.Id,
            test.Supplier.Id,
            new DateOnly(2026, 8, 24),
            new DateOnly(2026, 9, 7),
            "Replace office supplies",
            [new PurchaseRequisitionLineRequest("Office supplies", 2m, 50m, test.Account("6500").Id)]);
    }

    private static async Task<ApplicationUser> AddAdministratorAsync(AccountingTestDatabase test)
        => await AddMemberAsync(test, OrganisationRole.Administrator, "approver@example.com");

    private static async Task<ApplicationUser> AddMemberAsync(
        AccountingTestDatabase test,
        OrganisationRole role,
        string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true
        };
        test.Db.Users.Add(user);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = user.Id,
            Role = role
        });
        await test.Db.SaveChangesAsync();
        return user;
    }
}
