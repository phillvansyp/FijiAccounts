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
        var service = new PurchaseRequisitionService(test.Db, test.Access, test.PurchaseOrders);
        var approver = await AddAdministratorAsync(test);
        var request = await RequestAsync(test);

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
        var service = new PurchaseRequisitionService(test.Db, test.Access, test.PurchaseOrders);
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
        var service = new PurchaseRequisitionService(test.Db, test.Access, test.PurchaseOrders);
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
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "approver@example.com",
            NormalizedUserName = "APPROVER@EXAMPLE.COM",
            Email = "approver@example.com",
            NormalizedEmail = "APPROVER@EXAMPLE.COM",
            EmailConfirmed = true
        };
        test.Db.Users.Add(user);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = user.Id,
            Role = OrganisationRole.Administrator
        });
        await test.Db.SaveChangesAsync();
        return user;
    }
}
