using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankCodingMemoryServiceTests
{
    [Fact]
    public async Task RememberAsync_MatchesNormalizedPayeeAndExactSignedAmount()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BankCodingMemoryService(test.Db, test.Access);
        var division = await test.Db.Divisions.SingleAsync();

        await service.RememberAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                "  payment   to 9808313648 ",
                -8156m,
                test.Account("2500").Id,
                division.Id,
                "Loan repayment",
                VatTreatment.OutOfScope));

        var same = await service.FindAsync(
            test.UserId,
            test.Organisation.Id,
            "PAYMENT TO 9808313648",
            -8156m);
        var differentAmount = await service.FindAsync(
            test.UserId,
            test.Organisation.Id,
            "PAYMENT TO 9808313648",
            -8155m);

        Assert.NotNull(same);
        Assert.Equal("2500", same.TargetAccount.Code);
        Assert.Equal(division.Id, same.DivisionId);
        Assert.Equal("Loan repayment", same.Description);
        Assert.Equal(VatTreatment.OutOfScope, same.VatTreatment);
        Assert.Null(differentAmount);
    }

    [Fact]
    public async Task RememberAsync_ReplacesSavedCodingForTheSamePayeeAndAmount()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BankCodingMemoryService(test.Db, test.Access);
        var division = await test.Db.Divisions.SingleAsync();

        var request = new RememberBankCodingRequest(
            test.Organisation.Id,
            "Maintenance fee",
            -5m,
            test.Account("6500").Id,
            division.Id,
            "Monthly maintenance fee",
            VatTreatment.Standard);

        await service.RememberAsync(test.UserId, request);
        await service.RememberAsync(
            test.UserId,
            request with
            {
                TargetAccountId = test.Account("6400").Id,
                Description = "Bank maintenance fee",
                VatTreatment = VatTreatment.OutOfScope
            });

        var saved = await service.FindAsync(
            test.UserId,
            test.Organisation.Id,
            "maintenance fee",
            -5m);

        Assert.NotNull(saved);
        Assert.Equal("6400", saved.TargetAccount.Code);
        Assert.Equal("Bank maintenance fee", saved.Description);
        Assert.Equal(VatTreatment.OutOfScope, saved.VatTreatment);
        Assert.Single(await test.Db.BankCodingMemories.ToListAsync());
    }
}
