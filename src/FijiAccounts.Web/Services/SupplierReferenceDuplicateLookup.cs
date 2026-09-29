using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record SupplierReferenceMatch(Guid Id, string Label, bool IsDraft);

public static class SupplierReferenceDuplicateLookup
{
    public static async Task<SupplierReferenceMatch?> FindAsync(
        ApplicationDbContext db,
        Guid organisationId,
        Guid supplierId,
        string reference,
        Guid? currentDraftId = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = reference.Trim().ToUpperInvariant();
        if (organisationId == Guid.Empty || supplierId == Guid.Empty || normalized.Length == 0)
            return null;

        var posted = await db.SupplierBills.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId &&
                        x.SupplierId == supplierId &&
                        x.Status != BillStatus.Voided &&
                        x.SupplierReference.Trim().ToUpper() == normalized)
            .Select(x => new { x.Id, x.BillNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (posted is not null)
            return new(posted.Id, posted.BillNumber, false);

        var draft = await db.SupplierBillDrafts.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId &&
                        x.SupplierId == supplierId &&
                        x.Id != currentDraftId &&
                        x.SupplierReference.Trim().ToUpper() == normalized)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);
        return draft is null ? null : new(draft.Id, "an existing draft bill", true);
    }

    public static string Error(string reference, SupplierReferenceMatch match) =>
        $"Supplier reference {reference.Trim()} already exists on {match.Label} for this supplier. Open the existing bill or enter a different reference.";
}
