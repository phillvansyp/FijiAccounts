using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class EmployeeReceiptService(ApplicationDbContext db, IImmutableDocumentStore storage)
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public Task<bool> IsOwnerAsync(string user, Guid org) => db.OrganisationMemberships.AnyAsync(m =>
        m.UserId == user && m.OrganisationId == org && m.Role == OrganisationRole.Owner &&
        
        (m.Organisation.OrganisationGroupId == null || m.Organisation.OrganisationGroup!.Status == TenantStatus.Active));

    public async Task<List<Organisation>> OrganisationsAsync(string user) => await db.Organisations.AsNoTracking()
        .Where(o =>
            (o.OrganisationGroupId == null || o.OrganisationGroup!.Status == TenantStatus.Active) &&
            (db.OrganisationMemberships.Any(m => m.OrganisationId == o.Id && m.UserId == user && m.Role == OrganisationRole.Owner) ||
             db.ReceiptContributors.Any(m => m.OrganisationId == o.Id && m.UserId == user && m.Active)))
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
        string purpose, DateOnly date, decimal amount, string currency, bool personal, string filename, byte[] content)
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
            PaidPersonally = personal, DocumentId = stored.Id, FileName = filename, ContentType = type };
        db.EmployeeReceipts.Add(receipt);
        Audit(user, org, "ReceiptSubmitted", receipt.Id.ToString(), new { receipt.Merchant, receipt.Amount, receipt.Currency, personal });
        await db.SaveChangesAsync();
        return receipt;
    }

    public async Task ReviewAsync(string user, Guid org, Guid id, int version, bool approve, string? note)
    {
        if (!await IsOwnerAsync(user, org)) throw new UnauthorizedAccessException();
        var receipt = await db.EmployeeReceipts.SingleOrDefaultAsync(x => x.Id == id && x.OrganisationId == org)
            ?? throw new InvalidOperationException("Receipt not found.");
        if (receipt.SubmittedByUserId == user) throw new InvalidOperationException("Another owner must review your own receipt.");
        if (receipt.Status != "Submitted" || receipt.Version != version) throw new InvalidOperationException("This receipt has changed. Refresh before reviewing it.");
        if (note?.Length > 1000 || (!approve && string.IsNullOrWhiteSpace(note)))
            throw new InvalidOperationException("Give a reason when returning a receipt (up to 1,000 characters).");
        receipt.Status = approve ? "Approved" : "Returned";
        receipt.ReviewNote = note?.Trim(); receipt.ReviewedByUserId = user;
        receipt.ReviewedAt = DateTimeOffset.UtcNow; receipt.Version++;
        Audit(user, org, "ReceiptReviewed", id.ToString(), new { receipt.Status, receipt.ReviewNote });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { db.Entry(receipt).State = EntityState.Detached; throw new InvalidOperationException("Another owner has already reviewed this receipt. Refresh to see their decision."); }
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
