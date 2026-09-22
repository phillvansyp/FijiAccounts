using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class EmployeeReceiptTests
{
    private static EmployeeReceiptService Service(AccountingTestDatabase db) => new(db.Db, new DatabaseImmutableDocumentStore(db.Db));
    private static readonly byte[] Photo = [137,80,78,71,13,10,26,10,0,1];
    private static Task<EmployeeReceipt> Submit(EmployeeReceiptService service, string user, Guid org, Guid request) =>
        service.SubmitAsync(user, org, request, "Hardware shop", "Site supplies", new DateOnly(2026, 1, 1), 25m, "FJD", true, "receipt.png", Photo);

    [Fact]
    public async Task AssignedReceiptProfileGrantsOwnReceiptAccessWithoutAccountsAndRevokesImmediately()
    {
        await using var db = await AccountingTestDatabase.CreateAsync();
        var service = Service(db);
        var profiles = new OrganisationPermissionProfileService(db.Db, db.Access);
        const string employee = "receipt-profile-employee";
        db.Db.Users.Add(new ApplicationUser { Id = employee, UserName = employee, Email = "receipts@example.com", NormalizedEmail = "RECEIPTS@EXAMPLE.COM", EmailConfirmed = true });
        db.Db.OrganisationMemberships.Add(new OrganisationMembership { OrganisationId = db.Organisation.Id, UserId = employee, Role = OrganisationRole.ReadOnly });
        await db.Db.SaveChangesAsync();
        var profile = await profiles.CreateAsync(db.UserId, db.Organisation.Id,
            new("Employee receipts", null, false, false, false, false, CanAddReceipts: true, CanViewAccounts: false));
        await profiles.AssignAsync(db.UserId, db.Organisation.Id, employee, profile.Id);
        Assert.Single(await service.OrganisationsAsync(employee));
        Assert.Null(await db.Access.FindAsync(employee, db.Organisation.Id));
        Assert.False(await db.Access.CanPostJournalsAsync(employee, db.Organisation.Id));
        Assert.False(await db.Access.CanManageTeamAsync(employee, db.Organisation.Id));
        var receipt = await Submit(service, employee, db.Organisation.Id, Guid.NewGuid());
        Assert.Single(await service.ListAsync(employee, db.Organisation.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReviewAsync(employee, db.Organisation.Id, receipt.Id, 0, true, null));
        // An old contributor grant must not override the assigned profile's explicit denial.
        db.Db.ReceiptContributors.Add(new ReceiptContributor { OrganisationId = db.Organisation.Id, UserId = employee });
        await db.Db.SaveChangesAsync();
        await profiles.UpdateAsync(db.UserId, db.Organisation.Id, profile.Id,
            new("Employee receipts", null, false, false, false, false, CanAddReceipts: false, CanViewAccounts: false));
        Assert.Empty(await service.OrganisationsAsync(employee));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadAsync(employee, db.Organisation.Id, receipt.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => profiles.UpdateAsync(db.UserId, db.Organisation.Id, profile.Id,
            new("Invalid profile", null, false, true, false, false, CanAddReceipts: true, CanViewAccounts: false)));
    }

    [Fact]
    public async Task ReceiptOnlyEmployeeCanSubmitButCannotAccessAccountsOrAnotherEmployeesReceipt()
    {
        await using var db = await AccountingTestDatabase.CreateAsync();
        var service = Service(db);
        foreach (var id in new[] { "employee-a", "employee-b" }) {
            db.Db.Users.Add(new ApplicationUser { Id = id, UserName = id, Email = id + "@example.com", NormalizedEmail = (id + "@example.com").ToUpperInvariant(), EmailConfirmed = true });
        }
        await db.Db.SaveChangesAsync();
        await service.AddContributorAsync(db.UserId, db.Organisation.Id, "employee-a@example.com");
        await service.AddContributorAsync(db.UserId, db.Organisation.Id, "employee-b@example.com");
        var request = Guid.NewGuid();
        var receipt = await Submit(service, "employee-a", db.Organisation.Id, request);
        Assert.Equal(receipt.Id, (await Submit(service, "employee-a", db.Organisation.Id, request)).Id);
        Assert.Single(await service.ListAsync("employee-a", db.Organisation.Id));
        Assert.Empty(await service.ListAsync("employee-b", db.Organisation.Id));
        Assert.Null(await service.ReadAsync("employee-b", db.Organisation.Id, receipt.Id));
        Assert.Empty(await db.Access.ListAsync("employee-a"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReviewAsync("employee-a", db.Organisation.Id, receipt.Id, 0, true, null));
        await service.ReviewAsync(db.UserId, db.Organisation.Id, receipt.Id, 0, true, null);
        Assert.Equal("Approved", (await service.ListAsync("employee-a", db.Organisation.Id)).Single().Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReviewAsync(db.UserId, db.Organisation.Id, receipt.Id, 0, true, null));
        await service.RemoveContributorAsync(db.UserId, db.Organisation.Id, "employee-a");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadAsync("employee-a", db.Organisation.Id, receipt.Id));
        Assert.NotNull(await service.ReadAsync(db.UserId, db.Organisation.Id, receipt.Id));
    }

    [Fact]
    public async Task OwnerCannotApproveOwnReceiptAndInvalidFilesAreRejected()
    {
        await using var db = await AccountingTestDatabase.CreateAsync();
        var service = Service(db);
        var receipt = await Submit(service, db.UserId, db.Organisation.Id, Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReviewAsync(db.UserId, db.Organisation.Id, receipt.Id, 0, true, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(db.UserId, db.Organisation.Id, Guid.NewGuid(), "Shop", "Supplies", new DateOnly(2026,1,1), 25, "FJD", false, "receipt.html", Photo));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Submit(service, "outsider", db.Organisation.Id, Guid.NewGuid()));
        var group = new OrganisationGroup { Name = "Suspended company", Status = TenantStatus.Suspended }; db.Db.OrganisationGroups.Add(group); db.Organisation.OrganisationGroup = group; await db.Db.SaveChangesAsync();
        Assert.Empty(await service.OrganisationsAsync(db.UserId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadAsync(db.UserId, db.Organisation.Id, receipt.Id));
    }
}
