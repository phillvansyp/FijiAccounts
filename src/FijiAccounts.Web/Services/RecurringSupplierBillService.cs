using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record RecurringSupplierBillLineRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    FijiAccounts.Domain.Tax.VatTreatment VatTreatment,
    Guid ExpenseAccountId,
    Guid? ProductItemId = null);

public sealed record RecurringSupplierBillRequest(
    Guid OrganisationId,
    Guid SupplierId,
    string SupplierReference,
    RecurringSupplierBillFrequency Frequency,
    DateOnly StartDate,
    int DueDays,
    IReadOnlyList<RecurringSupplierBillLineRequest> Lines);

public sealed class RecurringSupplierBillService(
    ApplicationDbContext db,
    TenantAccessService access,
    PurchasingService purchasing)
{
    public async Task<RecurringSupplierBill> CreateAsync(
        string userId,
        RecurringSupplierBillRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot create recurring bills for this organisation.");
        }

        if (string.IsNullOrWhiteSpace(request.SupplierReference))
            throw new InvalidOperationException(
                "Enter a supplier reference.");

        if (request.DueDays < 0)
            throw new InvalidOperationException(
                "Due days cannot be negative.");

        if (request.Lines.Count == 0)
            throw new InvalidOperationException(
                "Enter at least one recurring bill line.");

        var supplierExists =
            await db.BusinessParties.AnyAsync(
                x =>
                    x.Id == request.SupplierId &&
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    (x.Type & PartyType.Supplier) != 0,
                ct);

        if (!supplierExists)
            throw new InvalidOperationException(
                "Select an active supplier in this organisation.");

                foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Description) ||
                line.Quantity <= 0 ||
                line.UnitPrice < 0)
            {
                throw new InvalidOperationException(
                    "Every recurring bill line needs a description, positive quantity and non-negative price.");
            }
        }

        var expenseAccountIds =
            request.Lines
                .Select(x => x.ExpenseAccountId)
                .Distinct()
                .ToArray();

        var expenseAccounts =
            await db.LedgerAccounts
                .Where(x =>
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    expenseAccountIds.Contains(x.Id) &&
                    (x.Type == AccountType.Expense ||
                     x.Type == AccountType.Asset))
                .ToDictionaryAsync(x => x.Id, ct);

        if (expenseAccounts.Count != expenseAccountIds.Length)
        {
            throw new InvalidOperationException(
                "Every recurring bill line must use an active expense or asset account.");
        }

        var productItemIds =
            request.Lines
                .Where(x => x.ProductItemId != null)
                .Select(x => x.ProductItemId!.Value)
                .Distinct()
                .ToArray();

        var productItems =
            await db.ProductItems
                .Where(x =>
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    productItemIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        if (productItems.Count != productItemIds.Length)
        {
            throw new InvalidOperationException(
                "A selected catalogue item is unavailable.");
        }

        foreach (var line in request.Lines.Where(
                     x => x.ProductItemId != null))
        {
            var item =
                productItems[line.ProductItemId!.Value];

            if (item.Kind == ProductKind.TrackedItem &&
                (item.InventoryAccountId is null ||
                 line.ExpenseAccountId != item.InventoryAccountId))
            {
                throw new InvalidOperationException(
                    $"Set opening stock and inventory accounts for {item.Code} before using it on a recurring supplier bill.");
            }
        }

        var recurring = new RecurringSupplierBill
        {
            OrganisationId = request.OrganisationId,
            SupplierId = request.SupplierId,
            SupplierReference = request.SupplierReference.Trim(),
            Frequency = request.Frequency,
            StartDate = request.StartDate,
            NextBillDate = request.StartDate,
            DueDays = request.DueDays,
            CreatedByUserId = userId,
            Lines = request.Lines.Select(x =>
                new RecurringSupplierBillLine
                {
                    Description = x.Description.Trim(),
                    Quantity = x.Quantity,
                    UnitPrice = x.UnitPrice,
                    VatTreatment = x.VatTreatment,
                    ExpenseAccountId = x.ExpenseAccountId,
                    ProductItemId = x.ProductItemId
                }).ToList()
        };

        db.RecurringSupplierBills.Add(recurring);

        db.AuditEvents.Add(
            Audit(
                request.OrganisationId,
                userId,
                "RecurringSupplierBillCreated",
                nameof(RecurringSupplierBill),
                recurring.Id,
                new
                {
                    recurring.SupplierId,
                    recurring.SupplierReference,
                    recurring.Frequency,
                    recurring.StartDate,
                    recurring.DueDays
                }));

        await db.SaveChangesAsync(ct);

        return recurring;
    }

    public async Task<IReadOnlyList<SupplierBill>> GenerateDueAsync(
        string userId,
        Guid organisationId,
        DateOnly throughDate,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot generate recurring bills for this organisation.");
        }

        var templates =
            await db.RecurringSupplierBills
                .Include(x => x.Lines)
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.IsActive &&
                    x.NextBillDate <= throughDate)
                .OrderBy(x => x.NextBillDate)
                .ToListAsync(ct);

        var generated = new List<SupplierBill>();

        foreach (var template in templates)
        {
            while (template.IsActive &&
                   template.NextBillDate <= throughDate)
            {
                var scheduledDate = template.NextBillDate;

                var alreadyGenerated =
                    await db.RecurringSupplierBillGenerations
                        .AnyAsync(
                            x =>
                                x.RecurringSupplierBillId ==
                                    template.Id &&
                                x.ScheduledDate == scheduledDate,
                            ct);

                if (alreadyGenerated)
                {
                    template.NextBillDate =
                        GetNextDate(
                            scheduledDate,
                            template.Frequency);

                    continue;
                }

                var dueDate =
                    scheduledDate.AddDays(template.DueDays);

                var bill =
                    await purchasing.PostBillAsync(
                        userId,
                        new SupplierBillRequest(
                            template.OrganisationId,
                            template.SupplierId,
                            BuildSupplierReference(
                                template.SupplierReference,
                                scheduledDate),
                            scheduledDate,
                            dueDate,
                            template.Lines
                                .Select(x =>
                                    new SupplierBillLineRequest(
                                        x.Description,
                                        x.Quantity,
                                        x.UnitPrice,
                                        x.VatTreatment,
                                        x.ExpenseAccountId,
                                        x.ProductItemId))
                                .ToList()),
                        ct);

                db.RecurringSupplierBillGenerations.Add(
                    new RecurringSupplierBillGeneration
                    {
                        OrganisationId = organisationId,
                        RecurringSupplierBillId = template.Id,
                        ScheduledDate = scheduledDate,
                        SupplierBillId = bill.Id,
                        GeneratedByUserId = userId
                    });

                template.NextBillDate =
                    GetNextDate(
                        scheduledDate,
                        template.Frequency);

                db.AuditEvents.Add(
                    Audit(
                        organisationId,
                        userId,
                        "RecurringSupplierBillGenerated",
                        nameof(RecurringSupplierBill),
                        template.Id,
                        new
                        {
                            ScheduledDate = scheduledDate,
                            SupplierBillId = bill.Id,
                            bill.BillNumber
                        }));

                await db.SaveChangesAsync(ct);

                generated.Add(bill);
            }
        }

        return generated;
    }

    internal static DateOnly GetNextDate(
        DateOnly date,
        RecurringSupplierBillFrequency frequency)
    {
        return frequency switch
        {
            RecurringSupplierBillFrequency.Weekly =>
                date.AddDays(7),

            RecurringSupplierBillFrequency.Monthly =>
                date.AddMonths(1),

            RecurringSupplierBillFrequency.Quarterly =>
                date.AddMonths(3),

            RecurringSupplierBillFrequency.Yearly =>
                date.AddYears(1),

            _ => throw new ArgumentOutOfRangeException(
                nameof(frequency))
        };
    }

    private static string BuildSupplierReference(
        string reference,
        DateOnly scheduledDate)
    {
        var suffix = scheduledDate.ToString("yyyyMMdd");
        var prefix = reference.Trim();

        const int maxLength = 80;
        var maxPrefixLength =
            maxLength - suffix.Length - 1;

        if (prefix.Length > maxPrefixLength)
            prefix = prefix[..maxPrefixLength];

        return $"{prefix}-{suffix}";
    }

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        string entityType,
        Guid entityId,
        object data) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            JsonData = JsonSerializer.Serialize(data)
        };
}