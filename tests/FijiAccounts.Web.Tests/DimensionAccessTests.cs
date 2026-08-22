using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class DimensionAccessTests
{
    [Fact]
    public async Task RestrictedMember_OnlyListsAndPostsToGrantedDimension()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var nadi = await structures.AddBranchAsync(test.Organisation.Id, "NADI", "Nadi Branch");
        var retail = await structures.AddDivisionAsync(test.Organisation.Id, nadi.Id, "RETAIL", "Retail");
        var member = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "restricted@example.com",
            NormalizedUserName = "RESTRICTED@EXAMPLE.COM",
            Email = "restricted@example.com",
            NormalizedEmail = "RESTRICTED@EXAMPLE.COM",
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

        await test.Access.SetDimensionAccessModeAsync(
            test.UserId,
            test.Organisation.Id,
            member.Id,
            DimensionAccessMode.Restricted);
        await test.Access.AddDimensionAccessGrantAsync(
            test.UserId,
            test.Organisation.Id,
            member.Id,
            nadi.Id,
            retail.Id);

        var accessible = await test.Access.ListAccessibleBranchesAsync(member.Id, test.Organisation.Id);
        var reportScope = await test.Access.GetReportDivisionScopeAsync(member.Id, test.Organisation.Id);
        var accounts = await test.Db.LedgerAccounts.AsNoTracking()
            .Where(x => x.OrganisationId == test.Organisation.Id)
            .ToListAsync();
        var bank = accounts.Single(x => x.Code == "1000");
        var revenue = accounts.Single(x => x.Code == "4000");
        var defaultBranch = await test.Db.Branches.AsNoTracking()
            .Include(x => x.Divisions)
            .SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.IsDefault);
        var defaultDivision = defaultBranch.Divisions.Single(x => x.IsDefault);

        Assert.Single(accessible);
        Assert.Equal(nadi.Id, accessible[0].Id);
        Assert.Equal(retail.Id, Assert.Single(accessible[0].Divisions).Id);
        Assert.NotNull(reportScope);
        Assert.Equal([retail.Id], reportScope);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.Posting.PostAsync(member.Id, Request(test.Organisation.Id, defaultBranch.Id, defaultDivision.Id, bank.Id, revenue.Id)));

        var journal = await test.Posting.PostAsync(
            member.Id,
            Request(test.Organisation.Id, nadi.Id, retail.Id, bank.Id, revenue.Id));

        Assert.All(journal.Lines, line =>
        {
            Assert.Equal(nadi.Id, line.BranchId);
            Assert.Equal(retail.Id, line.DivisionId);
        });
    }

    [Fact]
    public async Task Owner_CannotBeChangedToRestrictedDimensionAccess()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Access.SetDimensionAccessModeAsync(
                test.UserId,
                test.Organisation.Id,
                test.UserId,
                DimensionAccessMode.Restricted));

        Assert.Contains("retain access", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static JournalPostRequest Request(
        Guid organisationId,
        Guid branchId,
        Guid divisionId,
        Guid bankAccountId,
        Guid revenueAccountId) =>
        new(
            organisationId,
            new DateOnly(2026, 8, 23),
            $"DIM-{Guid.NewGuid():N}",
            "Dimension access test",
            [
                new JournalLineInput(bankAccountId, "Debit", 100m, 0m),
                new JournalLineInput(revenueAccountId, "Credit", 0m, 100m)
            ],
            branchId,
            divisionId);
}
