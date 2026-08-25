using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class EnterpriseWorkflowDimensionTests
{
    [Fact]
    public async Task RestrictedMember_BankBalancesOnlyIncludeGrantedDimensions()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var permittedBranch = await structures.AddBranchAsync(test.UserId, test.Organisation.Id, "NADI", "Nadi Branch");
        var permittedDivision = await structures.AddDivisionAsync(test.UserId, test.Organisation.Id, permittedBranch.Id, "OPS", "Operations");
        var defaultBranch = await test.Db.Branches.Include(x => x.Divisions).SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.IsDefault);
        var defaultDivision = defaultBranch.Divisions.Single(x => x.IsDefault);
        var member = RestrictedMember(test.Organisation.Id);
        test.Db.Users.Add(member.User);
        test.Db.OrganisationMemberships.Add(member.Membership);
        await test.Db.SaveChangesAsync();
        await test.Access.SetDimensionAccessModeAsync(test.UserId, test.Organisation.Id, member.User.Id, DimensionAccessMode.Restricted);
        await test.Access.AddDimensionAccessGrantAsync(test.UserId, test.Organisation.Id, member.User.Id, permittedBranch.Id, permittedDivision.Id);
        await PostBankReceipt(test, defaultBranch.Id, defaultDivision.Id, 100m, "DEFAULT-BANK");
        await PostBankReceipt(test, permittedBranch.Id, permittedDivision.Id, 250m, "NADI-BANK");

        var balances = await test.BankAccounts.GetBalancesAsync(member.User.Id, test.Organisation.Id);

        Assert.Equal(250m, balances[test.Account("1000").Id]);
    }

    [Fact]
    public async Task InventoryAdjustment_PersistsSelectedDimensionOnMovementAndJournal()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var branch = await structures.AddBranchAsync(test.UserId, test.Organisation.Id, "NADI", "Nadi Branch");
        var division = await structures.AddDivisionAsync(test.UserId, test.Organisation.Id, branch.Id, "OPS", "Operations");
        var item = TrackedItem(test.Organisation.Id, "DIM-STOCK");
        test.Db.ProductItems.Add(item);
        await test.Db.SaveChangesAsync();
        var service = new InventoryService(test.Db, test.Access, test.Posting);

        var movement = await service.AdjustAsync(
            test.UserId,
            new InventoryAdjustmentRequest(
                test.Organisation.Id,
                item.Id,
                new DateOnly(2026, 8, 26),
                5m,
                12m,
                1m,
                test.Account("1200").Id,
                test.Account("5000").Id,
                "DIM-STOCK-001",
                "Nadi opening stock",
                branch.Id,
                division.Id));

        Assert.Equal(branch.Id, movement.BranchId);
        Assert.Equal(division.Id, movement.DivisionId);
        var journal = await test.LoadJournalAsync(movement.PostedJournalId);
        Assert.All(journal.Lines, line =>
        {
            Assert.Equal(branch.Id, line.BranchId);
            Assert.Equal(division.Id, line.DivisionId);
        });
    }

    [Fact]
    public async Task BankTransfer_PersistsSelectedDimensionOnTransferAndJournal()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var branch = await structures.AddBranchAsync(test.UserId, test.Organisation.Id, "LAU", "Lautoka Branch");
        var division = await structures.AddDivisionAsync(test.UserId, test.Organisation.Id, branch.Id, "ADMIN", "Administration");
        var secondBank = await test.BankAccounts.CreateAsync(
            test.UserId,
            new CreateBankAccountRequest(
                test.Organisation.Id,
                "1010",
                "Reserve account",
                null,
                0m,
                new DateOnly(2026, 8, 26)));
        var service = new BankTransferService(test.Db, test.Access, test.Posting);

        var transfer = await service.PostAsync(
            test.UserId,
            new BankTransferRequest(
                test.Organisation.Id,
                test.Account("1000").Id,
                secondBank.Id,
                new DateOnly(2026, 8, 26),
                "DIM-TRF-001",
                "Move funds to reserve",
                250m,
                branch.Id,
                division.Id));

        Assert.Equal(branch.Id, transfer.BranchId);
        Assert.Equal(division.Id, transfer.DivisionId);
        var journal = await test.LoadJournalAsync(transfer.PostedJournalId);
        Assert.All(journal.Lines, line =>
        {
            Assert.Equal(branch.Id, line.BranchId);
            Assert.Equal(division.Id, line.DivisionId);
        });
    }

    [Fact]
    public async Task RestrictedMember_CannotAdjustInventoryOutsideGrantedDimension()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var permittedBranch = await structures.AddBranchAsync(test.UserId, test.Organisation.Id, "NADI", "Nadi Branch");
        var permittedDivision = await structures.AddDivisionAsync(test.UserId, test.Organisation.Id, permittedBranch.Id, "OPS", "Operations");
        var defaultBranch = await test.Db.Branches.Include(x => x.Divisions).SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.IsDefault);
        var defaultDivision = defaultBranch.Divisions.Single(x => x.IsDefault);
        var member = RestrictedMember(test.Organisation.Id);
        test.Db.Users.Add(member.User);
        test.Db.OrganisationMemberships.Add(member.Membership);
        var item = TrackedItem(test.Organisation.Id, "LOCKED-STOCK");
        test.Db.ProductItems.Add(item);
        await test.Db.SaveChangesAsync();
        await test.Access.SetDimensionAccessModeAsync(test.UserId, test.Organisation.Id, member.User.Id, DimensionAccessMode.Restricted);
        await test.Access.AddDimensionAccessGrantAsync(test.UserId, test.Organisation.Id, member.User.Id, permittedBranch.Id, permittedDivision.Id);
        var service = new InventoryService(test.Db, test.Access, test.Posting);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AdjustAsync(
            member.User.Id,
            new InventoryAdjustmentRequest(
                test.Organisation.Id,
                item.Id,
                new DateOnly(2026, 8, 26),
                1m,
                10m,
                0m,
                test.Account("1200").Id,
                test.Account("5000").Id,
                "LOCKED-STOCK-001",
                null,
                defaultBranch.Id,
                defaultDivision.Id)));

        Assert.Equal(0m, await test.Db.ProductItems.Where(x => x.Id == item.Id).Select(x => x.QuantityOnHand).SingleAsync());
        Assert.False(await test.Db.InventoryMovements.AnyAsync(x => x.ProductItemId == item.Id));
    }

    private static ProductItem TrackedItem(Guid organisationId, string code) => new()
    {
        OrganisationId = organisationId,
        Code = code,
        Name = code,
        Kind = ProductKind.TrackedItem,
        SaleTaxTreatment = VatTreatment.Standard,
        PurchaseTaxTreatment = VatTreatment.Standard,
        IsActive = true
    };

    private static Task<PostedJournal> PostBankReceipt(
        AccountingTestDatabase test,
        Guid branchId,
        Guid divisionId,
        decimal amount,
        string reference) =>
        test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 8, 26),
                reference,
                reference,
                [
                    new JournalLineInput(test.Account("1000").Id, reference, amount, 0m),
                    new JournalLineInput(test.Account("4000").Id, reference, 0m, amount)
                ],
                branchId,
                divisionId));

    private static (ApplicationUser User, OrganisationMembership Membership) RestrictedMember(Guid organisationId)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "enterprise-restricted@example.com",
            NormalizedUserName = "ENTERPRISE-RESTRICTED@EXAMPLE.COM",
            Email = "enterprise-restricted@example.com",
            NormalizedEmail = "ENTERPRISE-RESTRICTED@EXAMPLE.COM",
            EmailConfirmed = true
        };
        return (user, new OrganisationMembership
        {
            OrganisationId = organisationId,
            UserId = user.Id,
            User = user,
            Role = OrganisationRole.Bookkeeper
        });
    }
}
