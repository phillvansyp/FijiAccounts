using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using FijiAccounts.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class EmployeeReceiptTests
{
    private static EmployeeReceiptService Service(AccountingTestDatabase db) => new(db.Db, new DatabaseImmutableDocumentStore(db.Db));
    private static readonly byte[] Photo = [137,80,78,71,13,10,26,10,0,1];
    private static Task<EmployeeReceipt> Submit(EmployeeReceiptService service, string user, Guid org, Guid request) =>
        service.SubmitAsync(user, org, request, "Hardware shop", "Site supplies", new DateOnly(2026, 1, 1), 25m, "FJD", true, "receipt.png", Photo);

    [Fact]
    public async Task ReceiptsOnlyInvitationGrantsSubmissionWithoutAnyLedgerAccess()
    {
        await using var db = await AccountingTestDatabase.CreateAsync();
        var invitations = new OrganisationInvitationService(db.Db, db.Access);
        var issued = await invitations.IssueAsync(db.UserId, db.Organisation.Id, "receipt-invite@example.com", OrganisationRole.ReceiptsOnly);
        var renewed = await invitations.ReissueAsync(db.UserId, db.Organisation.Id,
            (await invitations.ListPendingAsync(db.UserId, db.Organisation.Id)).Single().Id);
        Assert.Equal(OrganisationRole.ReceiptsOnly, renewed.Role);
        const string employee = "receipt-invited-employee";
        db.Db.Users.Add(new ApplicationUser { Id = employee, UserName = employee, Email = "receipt-invite@example.com", EmailConfirmed = true });
        await db.Db.SaveChangesAsync();
        Assert.False((await invitations.AcceptAsync(employee, "receipt-invite@example.com", issued.Token)).Succeeded);
        Assert.True((await invitations.AcceptAsync(employee, "receipt-invite@example.com", renewed.Token)).Succeeded);
        Assert.Null(await db.Access.FindAsync(employee, db.Organisation.Id));
        Assert.False(await db.Access.CanManageTeamAsync(employee, db.Organisation.Id));
        Assert.False(await db.Access.CanPostJournalsAsync(employee, db.Organisation.Id));
        Assert.False(await db.Access.CanManageContactsAsync(employee, db.Organisation.Id));
        var service = Service(db);
        Assert.Single(await service.OrganisationsAsync(employee));
        await Submit(service, employee, db.Organisation.Id, Guid.NewGuid());
        Assert.Single(await service.ListAsync(employee, db.Organisation.Id));
        Assert.True((await invitations.AcceptAsync(employee, "receipt-invite@example.com", renewed.Token)).Succeeded);
        Assert.Null(await db.Access.FindAsync(employee, db.Organisation.Id));
    }

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
        await service.ReviewAsync(db.UserId, db.Organisation.Id, receipt.Id, 0, true, null, createDraft: true);
        Assert.Equal("Approved", (await service.ListAsync("employee-a", db.Organisation.Id)).Single().Status);
        Assert.NotNull((await service.ListAsync("employee-a", db.Organisation.Id)).Single().LinkedSupplierBillDraftId);
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

    [Fact]
    public async Task VatExcludedReceiptUsesGrossTotalToSuggestExistingBankAndPurchaseRecords()
    {
        await using var db = await AccountingTestDatabase.CreateAsync();
        var date = new DateOnly(2026, 1, 1);
        var bill = await db.Purchasing.PostBillAsync(db.UserId,
            new SupplierBillRequest(db.Organisation.Id, db.Supplier.Id, "RECEIPT-25", date, date,
                [new SupplierBillLineRequest("Supplies", 1m, 25m, VatTreatment.OutOfScope, db.Account("6500").Id)]));
        db.Db.BankStatementLines.Add(new BankStatementLine {
            OrganisationId = db.Organisation.Id, BankAccountId = db.Account("1000").Id,
            TransactionDate = date.AddDays(1), Description = "Hardware shop card", Amount = -25m });
        await db.Db.SaveChangesAsync();

        var service = Service(db);
        var receipt = await service.SubmitAsync(db.UserId, db.Organisation.Id, Guid.NewGuid(),
            "Hardware shop", "Site supplies", date, 20m, "FJD", false, "receipt.png", Photo,
            amountsIncludeVat: false, vatAmount: 5m);

        Assert.False(receipt.AmountsIncludeVat);
        Assert.Equal(25m, receipt.TotalPaid);
        var matches = await service.FindMatchesAsync(db.UserId, db.Organisation.Id, receipt);
        Assert.Contains(matches.Purchases, x => x.Url.EndsWith(bill.Id.ToString()));
        Assert.Single(matches.BankTransactions);
        Assert.False(matches.BankTransactions[0].Reconciled);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.FindMatchesAsync("outsider", db.Organisation.Id, receipt));
    }

    [Fact]
    public async Task ReceiptDefaultsToVatIncludedAndPersonalPaymentsDoNotSuggestCompanyBankLines()
    {
        await using var db = await AccountingTestDatabase.CreateAsync();
        var service = Service(db);
        var receipt = await Submit(service, db.UserId, db.Organisation.Id, Guid.NewGuid());
        Assert.True(receipt.AmountsIncludeVat);
        Assert.Equal(receipt.Amount, receipt.TotalPaid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(db.UserId, db.Organisation.Id,
            Guid.NewGuid(), "Hardware shop", "Site supplies", receipt.ReceiptDate, 25m, "FJD", true,
            "receipt.png", Photo, amountsIncludeVat: true, vatAmount: 5m));
        db.Db.BankStatementLines.Add(new BankStatementLine {
            OrganisationId = db.Organisation.Id, BankAccountId = db.Account("1000").Id,
            TransactionDate = receipt.ReceiptDate, Description = "Hardware shop", Amount = -receipt.TotalPaid });
        await db.Db.SaveChangesAsync();
        var matches = await service.FindMatchesAsync(db.UserId, db.Organisation.Id, receipt);
        Assert.Empty(matches.BankTransactions);
    }

    [Fact]
    public async Task ApprovalAttachesToConfirmedBillWithoutCreatingAnotherPurchase()
    {
        await using var db = await AccountingTestDatabase.CreateAsync();
        var service = Service(db);
        await AddEmployeeAsync(db, service);
        var receipt = await Submit(service, "receipt-employee", db.Organisation.Id, Guid.NewGuid());
        var bill = await db.Purchasing.PostBillAsync(db.UserId,
            new SupplierBillRequest(db.Organisation.Id, db.Supplier.Id, "RECEIPT-MATCH",
                receipt.ReceiptDate, receipt.ReceiptDate,
                [new SupplierBillLineRequest("Site supplies", 1m, 25m,
                    VatTreatment.OutOfScope, db.Account("6500").Id)]));

        await service.ReviewAsync(db.UserId, db.Organisation.Id, receipt.Id, receipt.Version,
            true, null, supplierBillId: bill.Id);

        var approved = await db.Db.EmployeeReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id);
        Assert.Equal(bill.Id, approved.LinkedSupplierBillId);
        Assert.Null(approved.LinkedSupplierBillDraftId);
        Assert.Equal("Approved", approved.Status);
        Assert.Single(await db.Db.SupplierBills.ToListAsync());
        Assert.Empty(await db.Db.SupplierBillDrafts.ToListAsync());
        var attachment = await db.Db.SupplierBillAttachments.SingleAsync(x => x.SupplierBillId == bill.Id);
        Assert.Equal(receipt.DocumentId, attachment.ImmutableDocumentObjectId);
        Assert.Equal(Photo, await new DatabaseImmutableDocumentStore(db.Db)
            .ReadVerifiedAsync(db.Organisation.Id, attachment.ImmutableDocumentObjectId!.Value));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReviewAsync(db.UserId,
            db.Organisation.Id, receipt.Id, receipt.Version, true, null, supplierBillId: bill.Id));
    }

    [Fact]
    public async Task ApprovalCreatesEditableDraftAndDeletingItReopensReceipt()
    {
        await using var db = await AccountingTestDatabase.CreateAsync();
        var service = Service(db);
        await AddEmployeeAsync(db, service);
        var receipt = await Submit(service, "receipt-employee", db.Organisation.Id, Guid.NewGuid());

        await service.ReviewAsync(db.UserId, db.Organisation.Id, receipt.Id, receipt.Version,
            true, null, createDraft: true);
        var approved = await db.Db.EmployeeReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id);
        var draft = await db.Db.SupplierBillDrafts.SingleAsync(x => x.Id == approved.LinkedSupplierBillDraftId);
        Assert.Equal(Photo, draft.AttachmentContent);
        Assert.Null(draft.ExpenseAccountId);
        Assert.Empty(await db.Db.SupplierBills.ToListAsync());

        var drafts = new SupplierBillDraftService(db.Db, db.Access);
        Assert.True(await drafts.DeleteAsync(db.UserId, db.Organisation.Id, draft.Id));
        var reopened = await db.Db.EmployeeReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id);
        Assert.Equal("Submitted", reopened.Status);
        Assert.Null(reopened.LinkedSupplierBillDraftId);
    }

    [Fact]
    public async Task PostingReceiptDraftKeepsOriginalDocumentAndLinksPostedBill()
    {
        await using var db = await AccountingTestDatabase.CreateAsync();
        var service = Service(db);
        await AddEmployeeAsync(db, service);
        var receipt = await Submit(service, "receipt-employee", db.Organisation.Id, Guid.NewGuid());
        await service.ReviewAsync(db.UserId, db.Organisation.Id, receipt.Id, receipt.Version,
            true, null, createDraft: true);
        var draft = await db.Db.SupplierBillDrafts.SingleAsync();
        var request = new SupplierBillRequest(db.Organisation.Id, db.Supplier.Id,
            draft.SupplierReference, draft.BillDate, draft.DueDate,
            [new SupplierBillLineRequest(draft.Description, 1m, receipt.TotalPaid,
                VatTreatment.OutOfScope, db.Account("6500").Id)],
            draft.BranchId, draft.DivisionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Purchasing.PostDraftBillAsync(
            db.UserId, draft.Id, request));
        var attachment = new SupplierBillAttachmentRequest(receipt.FileName, receipt.ContentType,
            Photo.LongLength, Photo, false);
        var bill = await db.Purchasing.PostDraftBillAsync(db.UserId, draft.Id, request, attachment);

        var linked = await db.Db.EmployeeReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id);
        Assert.Equal(bill.Id, linked.LinkedSupplierBillId);
        Assert.Null(linked.LinkedSupplierBillDraftId);
        Assert.False(await db.Db.SupplierBillDrafts.AnyAsync(x => x.Id == draft.Id));
        Assert.Single(await db.Db.SupplierBillAttachments.Where(x => x.SupplierBillId == bill.Id).ToListAsync());
    }

    private static async Task AddEmployeeAsync(AccountingTestDatabase db, EmployeeReceiptService service)
    {
        const string user = "receipt-employee";
        db.Db.Users.Add(new ApplicationUser { Id = user, UserName = user,
            Email = "receipt-employee@example.com", NormalizedEmail = "RECEIPT-EMPLOYEE@EXAMPLE.COM",
            EmailConfirmed = true });
        await db.Db.SaveChangesAsync();
        await service.AddContributorAsync(db.UserId, db.Organisation.Id, "receipt-employee@example.com");
    }
}
