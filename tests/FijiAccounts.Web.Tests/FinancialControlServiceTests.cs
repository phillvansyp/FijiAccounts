using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class FinancialControlServiceTests
{
    [Fact]
    public async Task GetAsync_DetectsDuplicateBillsAndActivePayments()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (branchId, divisionId) = await DefaultScopeAsync(test);
        var date = DateOnly.FromDateTime(DateTime.Today);
        var journal = Journal(test, date);
        test.Db.PostedJournals.Add(journal);

        var bill1 = Bill(test, journal.Id, branchId, divisionId, 1,
            "INV 0042", date, 1250m);
        var bill2 = Bill(test, journal.Id, branchId, divisionId, 2,
            "inv-0042", date.AddDays(2), 1300m);
        var bill3 = Bill(test, journal.Id, branchId, divisionId, 3,
            "A-100", date, 900m);
        var bill4 = Bill(test, journal.Id, branchId, divisionId, 4,
            "B-200", date, 900m);
        var oldBill1 = Bill(test, journal.Id, branchId, divisionId, 5,
            "LEGACY-1", date.AddYears(-2), 700m);
        var oldBill2 = Bill(test, journal.Id, branchId, divisionId, 6,
            "legacy 1", date.AddYears(-2), 700m);
        test.Db.SupplierBills.AddRange(
            bill1, bill2, bill3, bill4, oldBill1, oldBill2);

        var payment1 = Payment(test, journal.Id, branchId, divisionId,
            bill3.Id, "PAY-77", date, 450m);
        var payment2 = Payment(test, journal.Id, branchId, divisionId,
            bill3.Id, "pay 77", date, 450m);
        var reversedPayment = Payment(test, journal.Id, branchId, divisionId,
            bill3.Id, "PAY-77", date, 450m);
        var oldPayment1 = Payment(test, journal.Id, branchId, divisionId,
            oldBill1.Id, "OLD-PAY", date.AddYears(-2), 300m);
        var oldPayment2 = Payment(test, journal.Id, branchId, divisionId,
            oldBill1.Id, "old pay", date.AddYears(-2), 300m);
        test.Db.SupplierPayments.AddRange(
            payment1, payment2, reversedPayment, oldPayment1, oldPayment2);
        test.Db.SupplierPaymentReversals.Add(new SupplierPaymentReversal
        {
            OrganisationId = test.Organisation.Id,
            SupplierPaymentId = reversedPayment.Id,
            ReversalDate = date,
            Reason = "Test reversal",
            PostedJournalId = journal.Id,
            CreatedByUserId = test.UserId
        });
        await test.Db.SaveChangesAsync();

        var result = await new FinancialControlService(test.Db, test.Access)
            .GetAsync(test.UserId, test.Organisation.Id);

        Assert.Collection(
            result.Alerts,
            alert =>
            {
                Assert.Equal(FinancialControlAlertType.DuplicateSupplierBill, alert.Type);
                Assert.Equal(FinancialControlSeverity.High, alert.Severity);
                Assert.Equal(2, alert.MatchingTransactions);
                Assert.Contains("INV 0042", alert.Explanation);
            },
            alert =>
            {
                Assert.Equal(FinancialControlAlertType.DuplicateSupplierPayment, alert.Type);
                Assert.Equal(FinancialControlSeverity.High, alert.Severity);
                Assert.Equal(2, alert.MatchingTransactions);
            },
            alert =>
            {
                Assert.Equal(FinancialControlAlertType.DuplicateSupplierBill, alert.Type);
                Assert.Equal(FinancialControlSeverity.Watch, alert.Severity);
                Assert.Equal(900m, alert.Amount);
            });
        Assert.Equal(2, result.HighRiskCount);
    }

    [Fact]
    public async Task GetAsync_RequiresTenantAccessAndHonoursRestrictedDimensionScope()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (branchId, divisionId) = await DefaultScopeAsync(test);
        var date = DateOnly.FromDateTime(DateTime.Today);
        var journal = Journal(test, date);
        test.Db.PostedJournals.Add(journal);
        test.Db.SupplierBills.AddRange(
            Bill(test, journal.Id, branchId, divisionId, 1, "DUP-1", date, 100m),
            Bill(test, journal.Id, branchId, divisionId, 2, "dup 1", date, 100m));

        var restrictedUser = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "restricted-controls@example.com",
            NormalizedUserName = "RESTRICTED-CONTROLS@EXAMPLE.COM",
            Email = "restricted-controls@example.com",
            NormalizedEmail = "RESTRICTED-CONTROLS@EXAMPLE.COM"
        };
        test.Db.Users.Add(restrictedUser);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = restrictedUser.Id,
            Role = OrganisationRole.ReadOnly,
            DimensionAccessMode = DimensionAccessMode.Restricted
        });
        await test.Db.SaveChangesAsync();

        var service = new FinancialControlService(test.Db, test.Access);
        var restricted = await service.GetAsync(
            restrictedUser.Id, test.Organisation.Id);

        Assert.Empty(restricted.Alerts);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetAsync(Guid.NewGuid().ToString(), test.Organisation.Id));
    }

    [Fact]
    public async Task GetAsync_FlagsUnverifiedAndRecentlyChangedSupplierBankDetails()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (branchId, divisionId) = await DefaultScopeAsync(test);
        var date = DateOnly.FromDateTime(DateTime.Today);
        var journal = Journal(test, date);
        test.Db.PostedJournals.Add(journal);
        test.Db.SupplierBills.Add(
            Bill(test, journal.Id, branchId, divisionId, 1, "BANK-RISK", date, 2500m));
        test.Db.SupplierBankAccounts.AddRange(
            new SupplierBankAccount
            {
                OrganisationId = test.Organisation.Id,
                SupplierId = test.Supplier.Id,
                AccountName = "Pending destination",
                AccountNumber = "12345678",
                SubmittedByUserId = test.UserId,
                SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1)
            },
            new SupplierBankAccount
            {
                OrganisationId = test.Organisation.Id,
                SupplierId = test.Supplier.Id,
                AccountName = "Verified destination",
                AccountNumber = "87654321",
                SubmittedByUserId = "submitter",
                SubmittedAt = DateTimeOffset.UtcNow.AddDays(-2),
                VerifiedByUserId = test.UserId,
                VerifiedAt = DateTimeOffset.UtcNow.AddHours(-2),
                IsDefault = true
            });
        await test.Db.SaveChangesAsync();

        var result = await new FinancialControlService(test.Db, test.Access)
            .GetAsync(test.UserId, test.Organisation.Id);

        Assert.Collection(
            result.Alerts,
            alert =>
            {
                Assert.Equal(FinancialControlAlertType.UnverifiedSupplierBankAccount, alert.Type);
                Assert.Equal(FinancialControlSeverity.High, alert.Severity);
                Assert.Equal(2500m, alert.Amount);
            },
            alert =>
            {
                Assert.Equal(FinancialControlAlertType.RecentSupplierBankAccountChange, alert.Type);
                Assert.Equal(FinancialControlSeverity.Watch, alert.Severity);
                Assert.Equal(1, alert.MatchingTransactions);
            });
    }

    private static async Task<(Guid BranchId, Guid DivisionId)> DefaultScopeAsync(
        AccountingTestDatabase test)
    {
        var division = await test.Db.Divisions.AsNoTracking()
            .Include(x => x.Branch)
            .SingleAsync(x => x.Branch.OrganisationId == test.Organisation.Id);
        return (division.BranchId, division.Id);
    }

    private static PostedJournal Journal(
        AccountingTestDatabase test,
        DateOnly date) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            SequenceNumber = 1,
            EntryDate = date,
            Reference = "CONTROL-TEST",
            PostedAt = DateTimeOffset.UtcNow,
            PostedByUserId = test.UserId
        };

    private static SupplierBill Bill(
        AccountingTestDatabase test,
        Guid journalId,
        Guid branchId,
        Guid divisionId,
        long sequence,
        string reference,
        DateOnly date,
        decimal amount) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            BranchId = branchId,
            DivisionId = divisionId,
            SupplierId = test.Supplier.Id,
            SequenceNumber = sequence,
            BillNumber = $"CONTROL-{sequence}",
            SupplierReference = reference,
            BillDate = date,
            DueDate = date.AddDays(30),
            Status = BillStatus.Posted,
            Subtotal = amount,
            Total = amount,
            PostedJournalId = journalId,
            CreatedByUserId = test.UserId
        };

    private static SupplierPayment Payment(
        AccountingTestDatabase test,
        Guid journalId,
        Guid branchId,
        Guid divisionId,
        Guid billId,
        string reference,
        DateOnly date,
        decimal amount) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            BranchId = branchId,
            DivisionId = divisionId,
            SupplierId = test.Supplier.Id,
            SupplierBillId = billId,
            PaymentDate = date,
            Reference = reference,
            Amount = amount,
            BankAccountId = test.Account("1000").Id,
            PostedJournalId = journalId,
            CreatedByUserId = test.UserId
        };
}
