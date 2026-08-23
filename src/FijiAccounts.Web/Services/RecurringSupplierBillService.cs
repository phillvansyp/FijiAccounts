using System.Data;
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
        await RequireMaintenanceAccessAsync(
            userId,
            request.OrganisationId,
            "create recurring bills",
            ct);

        await ValidateRequestAsync(
            request,
            ct);

        var recurring =
            new RecurringSupplierBill
            {
                OrganisationId = request.OrganisationId,
                SupplierId = request.SupplierId,
                SupplierReference = request.SupplierReference.Trim(),
                Frequency = request.Frequency,
                StartDate = request.StartDate,
                NextBillDate = request.StartDate,
                DueDays = request.DueDays,
                CreatedByUserId = userId,
                Lines = BuildLines(request)
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
                    recurring.NextBillDate,
                    recurring.DueDays
                }));

        await db.SaveChangesAsync(ct);

        return recurring;
    }

    public async Task<RecurringSupplierBill> UpdateAsync(
        string userId,
        Guid recurringSupplierBillId,
        RecurringSupplierBillRequest request,
        CancellationToken ct = default)
    {
        await RequireMaintenanceAccessAsync(
            userId,
            request.OrganisationId,
            "update recurring bills",
            ct);

        await ValidateRequestAsync(
            request,
            ct);

        db.ChangeTracker.Clear();

        var recurring =
            await db.RecurringSupplierBills
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == recurringSupplierBillId &&
                        x.OrganisationId == request.OrganisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Recurring supplier bill not found.");

        var existingLines =
            recurring.Lines.ToList();

        db.RecurringSupplierBillLines.RemoveRange(
            existingLines);

        await db.SaveChangesAsync(ct);

        db.ChangeTracker.Clear();

        recurring =
            await db.RecurringSupplierBills
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == recurringSupplierBillId &&
                        x.OrganisationId == request.OrganisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Recurring supplier bill not found.");

        recurring.SupplierId = request.SupplierId;
        recurring.SupplierReference =
            request.SupplierReference.Trim();
        recurring.Frequency = request.Frequency;
        recurring.StartDate = request.StartDate;
        recurring.NextBillDate = request.StartDate;
        recurring.DueDays = request.DueDays;

        var replacementLines =
            BuildLines(request);

        foreach (var line in replacementLines)
        {
            line.RecurringSupplierBillId =
                recurring.Id;

            db.RecurringSupplierBillLines.Add(line);
        }

        db.AuditEvents.Add(
            Audit(
                request.OrganisationId,
                userId,
                "RecurringSupplierBillUpdated",
                nameof(RecurringSupplierBill),
                recurring.Id,
                new
                {
                    recurring.SupplierId,
                    recurring.SupplierReference,
                    recurring.Frequency,
                    recurring.StartDate,
                    recurring.NextBillDate,
                    recurring.DueDays,
                    LineCount = replacementLines.Count
                }));

        await db.SaveChangesAsync(ct);

        return recurring;
    }
    public async Task<RecurringSupplierBill> SetActiveAsync(
        string userId,
        Guid organisationId,
        Guid recurringSupplierBillId,
        bool isActive,
        DateOnly? resumeFromDate = null,
        CancellationToken ct = default)
    {
        await RequireMaintenanceAccessAsync(
            userId,
            organisationId,
            "maintain recurring bills",
            ct);

        db.ChangeTracker.Clear();

var recurring =
    await db.RecurringSupplierBills
        .Include(x => x.Lines)
        .SingleOrDefaultAsync(
            x =>
                x.Id == recurringSupplierBillId &&
                x.OrganisationId == organisationId,
            ct)
    ?? throw new InvalidOperationException(
        "Recurring supplier bill not found.");

        if (isActive &&
            recurring.Status == RecurringSupplierBillStatus.Ended)
        {
            throw new InvalidOperationException(
                "An ended recurring supplier bill cannot be resumed.");
        }

        if (isActive &&
            resumeFromDate is DateOnly resumeDate &&
            recurring.NextBillDate < resumeDate)
        {
            recurring.NextBillDate =
                resumeDate;
        }

        recurring.IsActive = isActive;
        recurring.Status =
            isActive
                ? RecurringSupplierBillStatus.Active
                : RecurringSupplierBillStatus.Paused;

        db.AuditEvents.Add(
            Audit(
                organisationId,
                userId,
                isActive
                    ? "RecurringSupplierBillResumed"
                    : "RecurringSupplierBillPaused",
                nameof(RecurringSupplierBill),
                recurring.Id,
                new
                {
                    recurring.IsActive,
                    recurring.NextBillDate
                }));

        await db.SaveChangesAsync(ct);

        return recurring;
    }

    public async Task<RecurringSupplierBill> EndAsync(
        string userId,
        Guid organisationId,
        Guid recurringSupplierBillId,
        CancellationToken ct = default)
    {
        await RequireMaintenanceAccessAsync(
            userId,
            organisationId,
            "end recurring bills",
            ct);

        var recurring =
            await db.RecurringSupplierBills
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == recurringSupplierBillId &&
                        x.OrganisationId == organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Recurring supplier bill not found.");

        recurring.IsActive = false;
        recurring.Status =
            RecurringSupplierBillStatus.Ended;

        db.AuditEvents.Add(
            Audit(
                organisationId,
                userId,
                "RecurringSupplierBillEnded",
                nameof(RecurringSupplierBill),
                recurring.Id,
                new
                {
                    recurring.NextBillDate
                }));

        await db.SaveChangesAsync(ct);

        return recurring;
    }

    private async Task RequireMaintenanceAccessAsync(
        string userId,
        Guid organisationId,
        string action,
        CancellationToken ct)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                $"You cannot {action} for this organisation.");
        }
    }

    private async Task ValidateRequestAsync(
        RecurringSupplierBillRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(
                request.SupplierReference))
        {
            throw new InvalidOperationException(
                "Enter a supplier reference.");
        }

        if (request.DueDays < 0)
        {
            throw new InvalidOperationException(
                "Due days cannot be negative.");
        }

        if (request.Lines.Count == 0)
        {
            throw new InvalidOperationException(
                "Enter at least one recurring bill line.");
        }

        var supplierExists =
            await db.BusinessParties.AnyAsync(
                x =>
                    x.Id == request.SupplierId &&
                    x.OrganisationId ==
                        request.OrganisationId &&
                    x.IsActive &&
                    (x.Type & PartyType.Supplier) != 0,
                ct);

        if (!supplierExists)
        {
            throw new InvalidOperationException(
                "Select an active supplier in this organisation.");
        }

        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(
                    line.Description) ||
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
                    x.OrganisationId ==
                        request.OrganisationId &&
                    x.IsActive &&
                    expenseAccountIds.Contains(x.Id) &&
                    (x.Type == AccountType.Expense ||
                     x.Type == AccountType.Asset))
                .ToDictionaryAsync(
                    x => x.Id,
                    ct);

        if (expenseAccounts.Count !=
            expenseAccountIds.Length)
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
                    x.OrganisationId ==
                        request.OrganisationId &&
                    x.IsActive &&
                    productItemIds.Contains(x.Id))
                .ToDictionaryAsync(
                    x => x.Id,
                    ct);

        if (productItems.Count !=
            productItemIds.Length)
        {
            throw new InvalidOperationException(
                "A selected catalogue item is unavailable.");
        }

        foreach (var line in request.Lines.Where(
                     x => x.ProductItemId != null))
        {
            var item =
                productItems[
                    line.ProductItemId!.Value];

            if (item.Kind ==
                    ProductKind.TrackedItem &&
                (item.InventoryAccountId is null ||
                 line.ExpenseAccountId !=
                    item.InventoryAccountId))
            {
                throw new InvalidOperationException(
                    $"Set opening stock and inventory accounts for {item.Code} before using it on a recurring supplier bill.");
            }
        }
    }

    private static List<RecurringSupplierBillLine> BuildLines(
        RecurringSupplierBillRequest request) =>
        request.Lines
            .Select(x =>
                new RecurringSupplierBillLine
                {
                    Description =
                        x.Description.Trim(),
                    Quantity = x.Quantity,
                    UnitPrice = x.UnitPrice,
                    VatTreatment = x.VatTreatment,
                    ExpenseAccountId =
                        x.ExpenseAccountId,
                    ProductItemId =
                        x.ProductItemId
                })
            .ToList();
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
                            template.Frequency,
                            template.StartDate);

                    await db.SaveChangesAsync(ct);

                    continue;
                }

                var dueDate =
                    scheduledDate.AddDays(template.DueDays);
                var nextBillDate =
                    GetNextDate(
                        scheduledDate,
                        template.Frequency,
                        template.StartDate);
                await using var transaction =
                    await db.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        ct);
                try
                {
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

                    template.NextBillDate = nextBillDate;
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
                    await transaction.CommitAsync(ct);
                    generated.Add(bill);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    db.ChangeTracker.Clear();
                    throw;
                }
            }
        }

        return generated;
    }

    internal static DateOnly GetNextDate(
        DateOnly date,
        RecurringSupplierBillFrequency frequency,
        DateOnly startDate)
    {
        return frequency switch
        {
            RecurringSupplierBillFrequency.Weekly =>
                date.AddDays(7),

            RecurringSupplierBillFrequency.Monthly =>
                AddMonthsPreservingDay(
                    date,
                    1,
                    startDate.Day),

            RecurringSupplierBillFrequency.Quarterly =>
                AddMonthsPreservingDay(
                    date,
                    3,
                    startDate.Day),

            RecurringSupplierBillFrequency.Yearly =>
                AddYearsPreservingDay(
                    date,
                    1,
                    startDate),

            _ => throw new ArgumentOutOfRangeException(
                nameof(frequency))
        };
    }

    private static DateOnly AddMonthsPreservingDay(
        DateOnly date,
        int months,
        int preferredDay)
    {
        var target =
            date.AddMonths(months);

        var day =
            Math.Min(
                preferredDay,
                DateTime.DaysInMonth(
                    target.Year,
                    target.Month));

        return new DateOnly(
            target.Year,
            target.Month,
            day);
    }

    private static DateOnly AddYearsPreservingDay(
        DateOnly date,
        int years,
        DateOnly startDate)
    {
        var targetYear =
            date.Year + years;

        var day =
            Math.Min(
                startDate.Day,
                DateTime.DaysInMonth(
                    targetYear,
                    startDate.Month));

        return new DateOnly(
            targetYear,
            startDate.Month,
            day);
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
