using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierPaymentApprovalTests
{
    [Fact]
    public async Task EnabledControlRequiresIndependentApprovalBeforePosting()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.RequireSupplierPaymentApproval = true;
        var approver = await AddMemberAsync(test, OrganisationRole.Administrator, "payment-approver@example.com");
        await test.Db.SaveChangesAsync();
        var bill = await PostBillAsync(test, "PAY-APPROVAL-001");
        var request = PaymentRequest(test, bill);

        var direct = await Assert.ThrowsAsync<InvalidOperationException>(
            () => test.Purchasing.PayBillAsync(test.UserId, request));
        Assert.Contains("approval", direct.Message, StringComparison.OrdinalIgnoreCase);

        var approval = await test.Purchasing.RequestPaymentApprovalAsync(test.UserId, request);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Purchasing.ApprovePaymentAsync(test.UserId, test.Organisation.Id, approval.Id));
        Assert.Empty(await test.Db.SupplierPayments.ToListAsync());
        Assert.Empty(await test.Db.PostedJournals.Where(x => x.Reference == request.Reference).ToListAsync());

        var payment = await test.Purchasing.ApprovePaymentAsync(
            approver.Id, test.Organisation.Id, approval.Id);

        var saved = await test.Db.SupplierPaymentApprovals.AsNoTracking().SingleAsync(x => x.Id == approval.Id);
        var paidBill = await test.Db.SupplierBills.AsNoTracking().SingleAsync(x => x.Id == bill.Id);
        Assert.Equal(SupplierPaymentApprovalStatus.Approved, saved.Status);
        Assert.Equal(approver.Id, saved.DecidedByUserId);
        Assert.Equal(payment.Id, saved.SupplierPaymentId);
        Assert.Equal(BillStatus.Paid, paidBill.Status);
        Assert.Equal(bill.Total, paidBill.AmountPaid);
        Assert.Equal(0m, await test.AccountBalanceAsync("2000"));
        Assert.Equal(-bill.Total, await test.AccountBalanceAsync("1000"));

        var events = await test.Db.AuditEvents.AsNoTracking()
            .Where(x => x.EntityType == nameof(SupplierPaymentApproval) && x.EntityId == approval.Id.ToString())
            .OrderBy(x => x.Id)
            .Select(x => x.EventType)
            .ToListAsync();
        Assert.Equal(["SupplierPaymentApprovalRequested", "SupplierPaymentApprovalApproved"], events);
    }

    [Fact]
    public async Task RejectionRequiresIndependentEligibleUserAndPreservesBill()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.RequireSupplierPaymentApproval = true;
        var approver = await AddMemberAsync(test, OrganisationRole.Administrator, "payment-rejector@example.com");
        await test.Db.SaveChangesAsync();
        var bill = await PostBillAsync(test, "PAY-APPROVAL-002");
        var approval = await test.Purchasing.RequestPaymentApprovalAsync(
            test.UserId, PaymentRequest(test, bill));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Purchasing.RejectPaymentAsync(test.UserId, test.Organisation.Id, approval.Id, "Self rejection"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Purchasing.RejectPaymentAsync(approver.Id, test.Organisation.Id, approval.Id, " "));
        await test.Purchasing.RejectPaymentAsync(
            approver.Id, test.Organisation.Id, approval.Id, "Supporting evidence is incomplete");

        var saved = await test.Db.SupplierPaymentApprovals.AsNoTracking().SingleAsync(x => x.Id == approval.Id);
        var unchangedBill = await test.Db.SupplierBills.AsNoTracking().SingleAsync(x => x.Id == bill.Id);
        Assert.Equal(SupplierPaymentApprovalStatus.Rejected, saved.Status);
        Assert.Equal("Supporting evidence is incomplete", saved.RejectionReason);
        Assert.Equal(0m, unchangedBill.AmountPaid);
        Assert.Equal(BillStatus.Posted, unchangedBill.Status);
        Assert.Empty(await test.Db.SupplierPayments.ToListAsync());
    }

    [Fact]
    public async Task PendingRequestsCannotExceedOutstandingBalance()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.RequireSupplierPaymentApproval = true;
        await test.Db.SaveChangesAsync();
        var bill = await PostBillAsync(test, "PAY-APPROVAL-003");
        var first = PaymentRequest(test, bill) with { Amount = 60m };
        await test.Purchasing.RequestPaymentApprovalAsync(test.UserId, first);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Purchasing.RequestPaymentApprovalAsync(
                test.UserId, first with { Reference = "PAY-APPROVAL-003-B", Amount = 50m }));

        Assert.Contains("pending", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await test.Db.SupplierPaymentApprovals.ToListAsync());
    }

    [Fact]
    public async Task OwnerOnlyPolicySnapshotRejectsAdministratorApproval()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.RequireSupplierPaymentApproval = true;
        var administrator = await AddMemberAsync(test, OrganisationRole.Administrator, "payment-admin@example.com");
        var owner = await AddMemberAsync(test, OrganisationRole.Owner, "payment-owner@example.com");
        await test.Db.SaveChangesAsync();
        var policies = new PurchaseApprovalPolicyService(test.Db, test.Access);
        var policy = await policies.CreateAsync(test.UserId, new PurchaseApprovalPolicyRequest(
            test.Organisation.Id, "Owner payment threshold", 50m, null,
            PurchaseApprovalRequirement.OwnerOnly));
        var bill = await PostBillAsync(test, "PAY-APPROVAL-004");

        var approval = await test.Purchasing.RequestPaymentApprovalAsync(
            test.UserId, PaymentRequest(test, bill));
        Assert.Equal(policy.Id, approval.PurchaseApprovalPolicyId);
        Assert.Equal(PurchaseApprovalRequirement.OwnerOnly, approval.RequiredApproval);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.Purchasing.ApprovePaymentAsync(administrator.Id, test.Organisation.Id, approval.Id));

        await policies.DeleteAsync(test.UserId, test.Organisation.Id, policy.Id);
        await test.Purchasing.ApprovePaymentAsync(owner.Id, test.Organisation.Id, approval.Id);
        var saved = await test.Db.SupplierPaymentApprovals.AsNoTracking().SingleAsync(x => x.Id == approval.Id);
        Assert.Equal(SupplierPaymentApprovalStatus.Approved, saved.Status);
        Assert.Equal(PurchaseApprovalRequirement.OwnerOnly, saved.RequiredApproval);
        Assert.Null(saved.PurchaseApprovalPolicyId);
    }

    [Fact]
    public async Task RequesterCanWithdrawPendingRequestButAnotherUserCannot()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.RequireSupplierPaymentApproval = true;
        var other = await AddMemberAsync(test, OrganisationRole.Administrator, "payment-other@example.com");
        await test.Db.SaveChangesAsync();
        var bill = await PostBillAsync(test, "PAY-APPROVAL-005");
        var approval = await test.Purchasing.RequestPaymentApprovalAsync(
            test.UserId, PaymentRequest(test, bill));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.Purchasing.WithdrawPaymentApprovalAsync(other.Id, test.Organisation.Id, approval.Id));
        await test.Purchasing.WithdrawPaymentApprovalAsync(
            test.UserId, test.Organisation.Id, approval.Id);

        var saved = await test.Db.SupplierPaymentApprovals.AsNoTracking().SingleAsync(x => x.Id == approval.Id);
        Assert.Equal(SupplierPaymentApprovalStatus.Withdrawn, saved.Status);
        Assert.Equal(test.UserId, saved.DecidedByUserId);
        Assert.Empty(await test.Db.SupplierPayments.ToListAsync());
    }

    private static async Task<SupplierBill> PostBillAsync(AccountingTestDatabase test, string reference) =>
        await test.Purchasing.PostBillAsync(test.UserId, new SupplierBillRequest(
            test.Organisation.Id,
            test.Supplier.Id,
            reference,
            new DateOnly(2026, 8, 26),
            new DateOnly(2026, 9, 25),
            [new SupplierBillLineRequest("Approval workflow test", 1m, 100m,
                VatTreatment.OutOfScope, test.Account("6500").Id)]));

    private static SupplierPaymentRequest PaymentRequest(AccountingTestDatabase test, SupplierBill bill) =>
        new(test.Organisation.Id, bill.Id, new DateOnly(2026, 8, 26),
            $"PAY-{bill.SupplierReference}", bill.Total, test.Account("1000").Id);

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
            Organisation = test.Organisation,
            UserId = user.Id,
            User = user,
            Role = role
        });
        await test.Db.SaveChangesAsync();
        return user;
    }
}
