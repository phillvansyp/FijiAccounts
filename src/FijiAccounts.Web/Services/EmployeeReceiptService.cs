using System.Text.Json;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record ReceiptMatchCandidate(Guid Id, string Description, DateOnly Date, bool Reconciled, string Url);
public sealed record ReceiptMatchSummary(IReadOnlyList<ReceiptMatchCandidate> Purchases, IReadOnlyList<ReceiptMatchCandidate> BankTransactions);

public sealed class EmployeeReceiptService(ApplicationDbContext db, IImmutableDocumentStore storage)
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public Task<bool> IsOwnerAsync(string user, Guid org) => db.OrganisationMemberships.AnyAsync(m =>
        m.UserId == user && m.OrganisationId == org && m.Role == OrganisationRole.Owner &&
        
        (m.Organisation.OrganisationGroupId == null || m.Organisation.OrganisationGroup!.Status == TenantStatus.Active));

    public async Task<bool> CanReviewOwnReceiptAsync(string user, Guid org) =>
        await IsOwnerAsync(user, org) &&
        await db.OrganisationMemberships.CountAsync(m =>
            m.OrganisationId == org && m.Role == OrganisationRole.Owner) == 1;

    public async Task<List<Organisation>> OrganisationsAsync(string user) => await db.Organisations.AsNoTracking()
        .Where(o =>
            (o.OrganisationGroupId == null || o.OrganisationGroup!.Status == TenantStatus.Active) &&
            (db.OrganisationMemberships.Any(m => m.OrganisationId == o.Id && m.UserId == user &&
                (m.Role == OrganisationRole.Owner || (m.PermissionProfileId != null ? m.PermissionProfile!.CanAddReceipts : m.Role == OrganisationRole.ReceiptsOnly))) ||
             (!db.OrganisationMemberships.Any(m => m.OrganisationId == o.Id && m.UserId == user && m.PermissionProfileId != null) &&
              db.ReceiptContributors.Any(m => m.OrganisationId == o.Id && m.UserId == user && m.Active))))
        .OrderBy(o => o.LegalName).ToListAsync();

    private async Task RequireAccess(string user, Guid org)
    {
        if (!(await OrganisationsAsync(user)).Any(o => o.Id == org)) throw new UnauthorizedAccessException();
    }

    public async Task AddContributorAsync(string user, Guid org, string email)
    {
        if (!await IsOwnerAsync(user, org)) throw new UnauthorizedAccessException();
        var normalized = email.Trim().ToUpperInvariant();
        var employee = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized && u.EmailConfirmed);
        if (employee is null) throw new InvalidOperationException("Ask the employee to register and verify their Account Island email first.");
        if (await db.OrganisationMemberships.AnyAsync(m => m.OrganisationId == org && m.UserId == employee.Id && m.PermissionProfileId != null))
            throw new InvalidOperationException("This employee uses a permission profile. Enable Add receipts under Team & permissions.");
        var grant = await db.ReceiptContributors.SingleOrDefaultAsync(x => x.OrganisationId == org && x.UserId == employee.Id);
        if (grant is null) db.ReceiptContributors.Add(new ReceiptContributor { OrganisationId = org, UserId = employee.Id });
        else grant.Active = true;
        Audit(user, org, "ReceiptContributorAdded", employee.Id, new { Email = email.Trim() });
        await db.SaveChangesAsync();
    }

    public async Task<Dictionary<string, string>> ContributorsAsync(string user, Guid org)
    {
        if (!await IsOwnerAsync(user, org)) throw new UnauthorizedAccessException();
        return await db.Users.Where(u => db.ReceiptContributors.Any(c => c.OrganisationId == org && c.UserId == u.Id && c.Active))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? "Employee");
    }

    public async Task<Dictionary<string, string>> SubmittersAsync(string user, Guid org)
    {
        if (!await IsOwnerAsync(user, org)) throw new UnauthorizedAccessException();
        return await db.Users.Where(u => db.EmployeeReceipts.Any(r => r.OrganisationId == org && r.SubmittedByUserId == u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? "Employee");
    }

    public async Task RemoveContributorAsync(string user, Guid org, string employee)
    {
        if (!await IsOwnerAsync(user, org)) throw new UnauthorizedAccessException();
        var grant = await db.ReceiptContributors.SingleOrDefaultAsync(x => x.OrganisationId == org && x.UserId == employee);
        if (grant is null) return;
        grant.Active = false;
        Audit(user, org, "ReceiptContributorRemoved", employee, new { });
        await db.SaveChangesAsync();
    }

    public async Task<List<EmployeeReceipt>> ListAsync(string user, Guid org)
    {
        await RequireAccess(user, org);
        var owner = await IsOwnerAsync(user, org);
        var rows = await db.EmployeeReceipts.AsNoTracking().Where(x => x.OrganisationId == org &&
            (owner || x.SubmittedByUserId == user)).ToListAsync();
        return rows.OrderByDescending(x => x.SubmittedAt).ToList();
    }

    public async Task<EmployeeReceipt> SubmitAsync(string user, Guid org, Guid request, string merchant,
        string purpose, DateOnly date, decimal amount, string currency, bool personal, string filename, byte[] content,
        bool amountsIncludeVat = true, decimal vatAmount = 0)
    {
        await RequireAccess(user, org);
        if (request == Guid.Empty) throw new InvalidOperationException("A submission reference is required.");
        var previous = await db.EmployeeReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.OrganisationId == org &&
            x.SubmittedByUserId == user && x.RequestId == request);
        if (previous is not null) return previous;
        if (string.IsNullOrWhiteSpace(merchant) || merchant.Trim().Length > 160 ||
            string.IsNullOrWhiteSpace(purpose) || purpose.Trim().Length > 1000 || amount <= 0 || amount > 999999999 ||
            decimal.Round(amount, 2) != amount || date == default || date > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            throw new InvalidOperationException("Enter the merchant, purpose, receipt date and a positive amount with up to two decimal places.");
        if (currency.Length != 3 || !currency.All(c => c is >= 'A' and <= 'Z'))
            throw new InvalidOperationException("Enter a three-letter currency, for example FJD.");
        if (vatAmount < 0 || vatAmount > 999999999 || decimal.Round(vatAmount, 2) != vatAmount ||
            (amountsIncludeVat && vatAmount != 0) || amount + vatAmount > 999999999)
            throw new InvalidOperationException("Enter a valid VAT amount with up to two decimal places, or leave it at zero when VAT is included.");
        if (string.IsNullOrWhiteSpace(filename) || filename.Length > 255 || Path.GetFileName(filename) != filename ||
            content.Length < 8 || content.Length > MaximumBytes)
            throw new InvalidOperationException("Choose a receipt photo or PDF up to 10 MB.");
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        var type = extension switch
        {
            ".pdf" when content.AsSpan(0, 5).SequenceEqual("%PDF-"u8) => "application/pdf",
            ".png" when content.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) => "image/png",
            ".jpg" or ".jpeg" when content[0] == 255 && content[1] == 216 && content[2] == 255 => "image/jpeg",
            _ => throw new InvalidOperationException("Choose a JPEG, PNG or PDF receipt.")
        };
        var stored = storage.Stage(org, user, content);
        var receipt = new EmployeeReceipt { OrganisationId = org, SubmittedByUserId = user, RequestId = request,
            Merchant = merchant.Trim(), Purpose = purpose.Trim(), ReceiptDate = date, Amount = amount, Currency = currency,
            AmountsIncludeVat = amountsIncludeVat, VatAmount = vatAmount,
            PaidPersonally = personal, DocumentId = stored.Id, FileName = filename, ContentType = type };
        db.EmployeeReceipts.Add(receipt);
        Audit(user, org, "ReceiptSubmitted", receipt.Id.ToString(), new { receipt.Merchant, receipt.Amount, receipt.Currency, amountsIncludeVat, vatAmount, personal });
        await db.SaveChangesAsync();
        return receipt;
    }

    public async Task<ReceiptMatchSummary> FindMatchesAsync(string user, Guid org, EmployeeReceipt receipt)
    {
        if (!await IsOwnerAsync(user, org) || receipt.OrganisationId != org) throw new UnauthorizedAccessException();
        var from = DateOnly.FromDayNumber(Math.Max(DateOnly.MinValue.DayNumber, receipt.ReceiptDate.DayNumber - 7));
        var to = DateOnly.FromDayNumber(Math.Min(DateOnly.MaxValue.DayNumber, receipt.ReceiptDate.DayNumber + 7));
        var total = receipt.TotalPaid;
        var bills = await db.SupplierBills.AsNoTracking()
            .Where(x => x.OrganisationId == org && x.Status != BillStatus.Voided &&
                x.Currency == receipt.Currency && x.TransactionTotal == total &&
                x.BillDate >= from && x.BillDate <= to)
            .Select(x => new { x.Id, x.BillNumber, x.BillDate, Supplier = x.Supplier.Name })
            .ToListAsync();
        var purchases = bills.OrderByDescending(x => x.Supplier.Contains(receipt.Merchant, StringComparison.OrdinalIgnoreCase) ||
                receipt.Merchant.Contains(x.Supplier, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => Math.Abs(x.BillDate.DayNumber - receipt.ReceiptDate.DayNumber))
            .Select(x => new ReceiptMatchCandidate(x.Id, $"{x.Supplier} · {x.BillNumber}", x.BillDate, false,
                $"/o/{org}/purchases/{x.Id}"))
            .ToList();

        var bank = new List<ReceiptMatchCandidate>();
        var baseCurrency = await db.Organisations.AsNoTracking().Where(x => x.Id == org)
            .Select(x => x.BaseCurrency).SingleAsync();
        if (!receipt.PaidPersonally && receipt.Currency == baseCurrency)
        {
            var lines = await db.BankStatementLines.AsNoTracking()
                .Where(x => x.OrganisationId == org && x.Amount == -total &&
                    x.TransactionDate >= from && x.TransactionDate <= to)
                .Select(x => new { x.Id, x.Description, x.Reference, x.TransactionDate, x.ReconciledAt })
                .ToListAsync();
            bank = lines.OrderByDescending(x => x.Description.Contains(receipt.Merchant, StringComparison.OrdinalIgnoreCase) ||
                    (x.Reference != null && x.Reference.Contains(receipt.Merchant, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(x => Math.Abs(x.TransactionDate.DayNumber - receipt.ReceiptDate.DayNumber))
                .Take(3)
                .Select(x => new ReceiptMatchCandidate(x.Id, x.Description, x.TransactionDate, x.ReconciledAt is not null,
                    $"/o/{org}/banking"))
                .ToList();
        }
        return new ReceiptMatchSummary(purchases, bank);
    }

    public async Task ReviewAsync(string user, Guid org, Guid id, int version, bool approve, string? note,
        Guid? supplierBillId = null, bool createDraft = false)
    {
        if (!await IsOwnerAsync(user, org)) throw new UnauthorizedAccessException();
        var receipt = await db.EmployeeReceipts.SingleOrDefaultAsync(x => x.Id == id && x.OrganisationId == org)
            ?? throw new InvalidOperationException("Receipt not found.");
        var selfReview = receipt.SubmittedByUserId == user;
        if (selfReview && !await CanReviewOwnReceiptAsync(user, org))
            throw new InvalidOperationException("Another owner must review your own receipt.");
        if (receipt.Status != "Submitted" || receipt.Version != version) throw new InvalidOperationException("This receipt has changed. Refresh before reviewing it.");
        if (approve && (supplierBillId.HasValue == createDraft))
            throw new InvalidOperationException("Choose a confirmed purchase bill or create a new purchase draft.");
        if (!approve && (supplierBillId.HasValue || createDraft))
            throw new InvalidOperationException("A returned receipt cannot be attached to a purchase.");
        if (note?.Length > 1000 || (!approve && string.IsNullOrWhiteSpace(note)))
            throw new InvalidOperationException("Give a reason when returning a receipt (up to 1,000 characters).");
        if (approve && supplierBillId is Guid billId)
        {
            var from = DateOnly.FromDayNumber(Math.Max(DateOnly.MinValue.DayNumber, receipt.ReceiptDate.DayNumber - 7));
            var to = DateOnly.FromDayNumber(Math.Min(DateOnly.MaxValue.DayNumber, receipt.ReceiptDate.DayNumber + 7));
            var bill = await db.SupplierBills.SingleOrDefaultAsync(x => x.Id == billId && x.OrganisationId == org &&
                x.Status != BillStatus.Voided && x.Currency == receipt.Currency &&
                x.TransactionTotal == receipt.TotalPaid && x.BillDate >= from && x.BillDate <= to)
                ?? throw new InvalidOperationException("The purchase bill no longer matches this receipt. Refresh and review it again.");
            if (!await db.SupplierBillAttachments.AnyAsync(x => x.SupplierBillId == bill.Id &&
                x.ImmutableDocumentObjectId == receipt.DocumentId))
            {
                var content = await storage.ReadVerifiedAsync(org, receipt.DocumentId);
                var attachment = SupplierBillAttachmentService.CreateValidated(org, bill.Id, user,
                    new SupplierBillAttachmentRequest(receipt.FileName, receipt.ContentType,
                        content.LongLength, content, false));
                attachment.ImmutableDocumentObjectId = receipt.DocumentId;
                db.SupplierBillAttachments.Add(attachment);
                db.AuditEvents.Add(SupplierBillAttachmentService.AddedAudit(org, user, bill, attachment));
            }
            receipt.LinkedSupplierBillId = bill.Id;
        }
        else if (approve && createDraft)
        {
            var organisation = await db.Organisations.SingleAsync(x => x.Id == org);
            var division = await db.Divisions.AsNoTracking()
                .Where(x => x.IsActive && x.Branch.IsActive && x.Branch.OrganisationId == org)
                .OrderByDescending(x => x.Branch.IsDefault).ThenByDescending(x => x.IsDefault)
                .Select(x => new { x.Id, x.BranchId }).FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Set up an active branch and division before creating a purchase draft.");
            var normalizedMerchant = receipt.Merchant.Trim().ToUpperInvariant();
            var supplierId = await db.BusinessParties.AsNoTracking()
                .Where(x => x.OrganisationId == org && x.IsActive && (x.Type & PartyType.Supplier) != 0 &&
                    x.Name.ToUpper() == normalizedMerchant)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync();
            var content = await storage.ReadVerifiedAsync(org, receipt.DocumentId);
            var description = $"{receipt.Merchant}: {receipt.Purpose}";
            var draft = new SupplierBillDraft {
                OrganisationId = org, BranchId = division.BranchId, DivisionId = division.Id,
                SupplierId = supplierId, SupplierReference = $"RECEIPT-{receipt.Id.ToString("N")[..12]}",
                BillDate = receipt.ReceiptDate,
                DueDate = PaymentTermCalculator.CalculateDueDate(receipt.ReceiptDate,
                    organisation.DefaultSupplierBillPaymentTermType, organisation.DefaultSupplierBillDueDays),
                Currency = receipt.Currency,
                ExchangeRateToBase = receipt.Currency == organisation.BaseCurrency ? 1m : 0m,
                Description = description[..Math.Min(description.Length, 300)],
                Quantity = 1m, UnitPrice = receipt.Amount, AmountsIncludeVat = receipt.AmountsIncludeVat,
                VatTreatment = receipt.AmountsIncludeVat || receipt.VatAmount > 0 ? VatTreatment.Standard : VatTreatment.OutOfScope,
                AttachmentFileName = receipt.FileName, AttachmentContentType = receipt.ContentType,
                AttachmentOriginalSize = content.LongLength, AttachmentContent = content,
                CreatedByUserId = user
            };
            db.SupplierBillDrafts.Add(draft);
            receipt.LinkedSupplierBillDraftId = draft.Id;
            Audit(user, org, "ReceiptPurchaseDraftCreated", id.ToString(), new { DraftId = draft.Id, receipt.TotalPaid });
        }
        receipt.Status = approve ? "Approved" : "Returned";
        receipt.ReviewNote = note?.Trim(); receipt.ReviewedByUserId = user;
        receipt.ReviewedAt = DateTimeOffset.UtcNow; receipt.Version++;
        Audit(user, org, "ReceiptReviewed", id.ToString(), new { receipt.Status, receipt.ReviewNote, SelfReviewed = selfReview,
            receipt.LinkedSupplierBillId, receipt.LinkedSupplierBillDraftId });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw new InvalidOperationException("Another owner has already reviewed this receipt. Refresh to see their decision."); }
    }

    public async Task<(EmployeeReceipt Receipt, byte[] Content)?> ReadAsync(string user, Guid org, Guid id)
    {
        await RequireAccess(user, org);
        var owner = await IsOwnerAsync(user, org);
        var receipt = await db.EmployeeReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OrganisationId == org &&
            (owner || x.SubmittedByUserId == user));
        return receipt is null ? null : (receipt, await storage.ReadVerifiedAsync(org, receipt.DocumentId));
    }

    private void Audit(string user, Guid org, string action, string id, object detail) => db.AuditEvents.Add(new AuditEvent {
        OrganisationId = org, UserId = user, EventType = action, EntityType = "EmployeeReceipt", EntityId = id,
        JsonData = JsonSerializer.Serialize(detail) });
}
