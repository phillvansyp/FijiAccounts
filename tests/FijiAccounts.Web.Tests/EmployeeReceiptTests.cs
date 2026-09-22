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
