using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record RecurringSalesInvoiceLineRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    VatTreatment VatTreatment,
    Guid RevenueAccountId,
    Guid? ProductItemId = null);

public sealed record RecurringSalesInvoiceRequest(
    Guid OrganisationId,
    Guid CustomerId,
    RecurringSalesInvoiceFrequency Frequency,
    DateOnly StartDate,
    int DueDays,
    IReadOnlyList<RecurringSalesInvoiceLineRequest> Lines);

public sealed class RecurringSalesInvoiceService(
    ApplicationDbContext db,
    TenantAccessService access,
    SalesInvoiceService salesInvoices)
{
    public async Task<RecurringSalesInvoice> CreateAsync(
        string userId,
        RecurringSalesInvoiceRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot create recurring invoices for this organisation.");
        }

        if (request.DueDays < 0)
        {
            throw new InvalidOperationException(
                "Due days cannot be negative.");
        }

        if (request.Lines.Count == 0)
        {
            throw new InvalidOperationException(
                "Enter at least one recurring invoice line.");
        }

        var customerExists =
            await db.BusinessParties.AnyAsync(
                x =>
                    x.Id == request.CustomerId &&
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    (x.Type & PartyType.Customer) != 0,
                ct);

        if (!customerExists)
        {
            throw new InvalidOperationException(
                "Select an active customer in this organisation.");
        }

        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Description) ||
                line.Quantity <= 0 ||
                line.UnitPrice < 0)
            {
                throw new InvalidOperationException(
                    "Every recurring invoice line needs a description, positive quantity and non-negative price.");
            }
        }

        var revenueAccountIds =
            request.Lines
                .Select(x => x.RevenueAccountId)
                .Distinct()
                .ToArray();

        var revenueAccountCount =
            await db.LedgerAccounts.CountAsync(
                x =>
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    revenueAccountIds.Contains(x.Id) &&
                    x.Type == AccountType.Revenue,
                ct);

        if (revenueAccountCount != revenueAccountIds.Length)
        {
            throw new InvalidOperationException(
                "Every recurring invoice line must use an active revenue account.");
        }

        var productItemIds =
            request.Lines
                .Where(x => x.ProductItemId != null)
                .Select(x => x.ProductItemId!.Value)
                .Distinct()
                .ToArray();

        var productItemCount =
            await db.ProductItems.CountAsync(
                x =>
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    productItemIds.Contains(x.Id),
                ct);

        if (productItemCount != productItemIds.Length)
        {
            throw new InvalidOperationException(
                "A selected catalogue item is unavailable.");
        }

        var recurring =
            new RecurringSalesInvoice
            {
                OrganisationId = request.OrganisationId,
                CustomerId = request.CustomerId,
                Frequency = request.Frequency,
                StartDate = request.StartDate,
                NextInvoiceDate = request.StartDate,
                DueDays = request.DueDays,
                CreatedByUserId = userId,
                Lines = request.Lines
                    .Select(x =>
                        new RecurringSalesInvoiceLine
                        {
                            Description = x.Description.Trim(),
                            Quantity = x.Quantity,
                            UnitPrice = x.UnitPrice,
                            VatTreatment = x.VatTreatment,
                            RevenueAccountId = x.RevenueAccountId,
                            ProductItemId = x.ProductItemId
                        })
                    .ToList()
            };

        db.RecurringSalesInvoices.Add(recurring);

        db.AuditEvents.Add(
            new AuditEvent
            {
                OrganisationId = request.OrganisationId,
                EventType = "RecurringSalesInvoiceCreated",
                EntityType = nameof(RecurringSalesInvoice),
                EntityId = recurring.Id.ToString(),
                UserId = userId,
                JsonData = JsonSerializer.Serialize(
                    new
                    {
                        recurring.CustomerId,
                        recurring.Frequency,
                        recurring.StartDate,
                        recurring.NextInvoiceDate,
                        recurring.DueDays,
                        LineCount = recurring.Lines.Count
                    })
            });

        await db.SaveChangesAsync(ct);

        return recurring;
    }

    public async Task<RecurringSalesInvoice> UpdateAsync(
        string userId,
        Guid recurringSalesInvoiceId,
        RecurringSalesInvoiceRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot update recurring invoices for this organisation.");
        }

        if (request.DueDays < 0)
        {
            throw new InvalidOperationException(
                "Due days cannot be negative.");
        }

        if (request.Lines.Count == 0)
        {
            throw new InvalidOperationException(
                "Enter at least one recurring invoice line.");
        }

        var customerExists =
            await db.BusinessParties.AnyAsync(
                x =>
                    x.Id == request.CustomerId &&
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    (x.Type & PartyType.Customer) != 0,
                ct);

        if (!customerExists)
        {
            throw new InvalidOperationException(
                "Select an active customer in this organisation.");
        }

        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Description) ||
                line.Quantity <= 0 ||
                line.UnitPrice < 0)
            {
                throw new InvalidOperationException(
                    "Every recurring invoice line needs a description, positive quantity and non-negative price.");
            }
        }

        var revenueAccountIds =
            request.Lines
                .Select(x => x.RevenueAccountId)
                .Distinct()
                .ToArray();

        var revenueAccountCount =
            await db.LedgerAccounts.CountAsync(
                x =>
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    revenueAccountIds.Contains(x.Id) &&
                    x.Type == AccountType.Revenue,
                ct);

        if (revenueAccountCount != revenueAccountIds.Length)
        {
            throw new InvalidOperationException(
                "Every recurring invoice line must use an active revenue account.");
        }

        var productItemIds =
            request.Lines
                .Where(x => x.ProductItemId != null)
                .Select(x => x.ProductItemId!.Value)
                .Distinct()
                .ToArray();

        var productItemCount =
            await db.ProductItems.CountAsync(
                x =>
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    productItemIds.Contains(x.Id),
                ct);

        if (productItemCount != productItemIds.Length)
        {
            throw new InvalidOperationException(
                "A selected catalogue item is unavailable.");
        }

        db.ChangeTracker.Clear();

        var recurring =
            await db.RecurringSalesInvoices
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == recurringSalesInvoiceId &&
                        x.OrganisationId == request.OrganisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Recurring sales invoice not found.");

        if (recurring.Status == RecurringSalesInvoiceStatus.Ended)
        {
            throw new InvalidOperationException(
                "An ended recurring sales invoice cannot be edited.");
        }

        var existingLines =
            recurring.Lines.ToList();

        db.RecurringSalesInvoiceLines.RemoveRange(
            existingLines);

        await db.SaveChangesAsync(ct);

        db.ChangeTracker.Clear();

        recurring =
            await db.RecurringSalesInvoices
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == recurringSalesInvoiceId &&
                        x.OrganisationId == request.OrganisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Recurring sales invoice not found.");

        recurring.CustomerId = request.CustomerId;
        recurring.Frequency = request.Frequency;
        recurring.StartDate = request.StartDate;
        recurring.NextInvoiceDate = request.StartDate;
        recurring.DueDays = request.DueDays;

        var replacementLines =
            request.Lines
                .Select(x =>
                    new RecurringSalesInvoiceLine
                    {
                        RecurringSalesInvoiceId =
                            recurring.Id,
                        Description =
                            x.Description.Trim(),
                        Quantity =
                            x.Quantity,
                        UnitPrice =
                            x.UnitPrice,
                        VatTreatment =
                            x.VatTreatment,
                        RevenueAccountId =
                            x.RevenueAccountId,
                        ProductItemId =
                            x.ProductItemId
                    })
                .ToList();

        foreach (var line in replacementLines)
        {
            db.RecurringSalesInvoiceLines.Add(line);
        }

        db.AuditEvents.Add(
            new AuditEvent
            {
                OrganisationId = request.OrganisationId,
                EventType = "RecurringSalesInvoiceUpdated",
                EntityType = nameof(RecurringSalesInvoice),
                EntityId = recurring.Id.ToString(),
                UserId = userId,
                JsonData = JsonSerializer.Serialize(
                    new
                    {
                        recurring.CustomerId,
                        recurring.Frequency,
                        recurring.StartDate,
                        recurring.NextInvoiceDate,
                        recurring.DueDays,
                        LineCount = replacementLines.Count
                    })
            });

        await db.SaveChangesAsync(ct);

        return recurring;
    }
    public async Task<RecurringSalesInvoice> SetActiveAsync(
        string userId,
        Guid organisationId,
        Guid recurringSalesInvoiceId,
        bool isActive,
        DateOnly? resumeFromDate = null,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot manage recurring invoices for this organisation.");
        }

        var recurring =
            await db.RecurringSalesInvoices
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == recurringSalesInvoiceId &&
                        x.OrganisationId == organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Recurring sales invoice not found.");

        if (isActive &&
            recurring.Status == RecurringSalesInvoiceStatus.Ended)
        {
            throw new InvalidOperationException(
                "An ended recurring sales invoice cannot be resumed.");
        }

        if (isActive &&
            resumeFromDate is DateOnly resumeDate &&
            recurring.NextInvoiceDate < resumeDate)
        {
            recurring.NextInvoiceDate =
                resumeDate;
        }

        recurring.IsActive = isActive;
        recurring.Status =
            isActive
                ? RecurringSalesInvoiceStatus.Active
                : RecurringSalesInvoiceStatus.Paused;

        db.AuditEvents.Add(
            new AuditEvent
            {
                OrganisationId = organisationId,
                EventType =
                    isActive
                        ? "RecurringSalesInvoiceResumed"
                        : "RecurringSalesInvoicePaused",
                EntityType = nameof(RecurringSalesInvoice),
                EntityId = recurring.Id.ToString(),
                UserId = userId,
                JsonData = JsonSerializer.Serialize(
                    new
                    {
                        recurring.IsActive,
                        recurring.Status,
                        recurring.NextInvoiceDate
                    })
            });

        await db.SaveChangesAsync(ct);

        return recurring;
    }
    public async Task<RecurringSalesInvoice> EndAsync(
        string userId,
        Guid organisationId,
        Guid recurringSalesInvoiceId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot manage recurring invoices for this organisation.");
        }

        var recurring =
            await db.RecurringSalesInvoices
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == recurringSalesInvoiceId &&
                        x.OrganisationId == organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Recurring sales invoice not found.");

        recurring.IsActive = false;
        recurring.Status =
            RecurringSalesInvoiceStatus.Ended;

        db.AuditEvents.Add(
            new AuditEvent
            {
                OrganisationId = organisationId,
                EventType = "RecurringSalesInvoiceEnded",
                EntityType = nameof(RecurringSalesInvoice),
                EntityId = recurring.Id.ToString(),
                UserId = userId,
                JsonData = JsonSerializer.Serialize(
                    new
                    {
                        recurring.IsActive,
                        recurring.Status,
                        recurring.NextInvoiceDate
                    })
            });

        await db.SaveChangesAsync(ct);

        return recurring;
    }
    public async Task<IReadOnlyList<SalesInvoice>> GenerateDueAsync(
        string userId,
        Guid organisationId,
        DateOnly throughDate,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot generate recurring invoices for this organisation.");
        }

        var templates =
            await db.RecurringSalesInvoices
                .Include(x => x.Lines)
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.IsActive &&
                    x.Status == RecurringSalesInvoiceStatus.Active &&
                    x.NextInvoiceDate <= throughDate)
                .OrderBy(x => x.NextInvoiceDate)
                .ToListAsync(ct);

        var generated =
            new List<SalesInvoice>();

        foreach (var template in templates)
        {
            while (template.IsActive &&
                   template.Status ==
                       RecurringSalesInvoiceStatus.Active &&
                   template.NextInvoiceDate <= throughDate)
            {
                var scheduledDate =
                    template.NextInvoiceDate;

                var alreadyGenerated =
                    await db.RecurringSalesInvoiceGenerations
                        .AnyAsync(
                            x =>
                                x.RecurringSalesInvoiceId ==
                                    template.Id &&
                                x.ScheduledDate ==
                                    scheduledDate,
                            ct);

                if (alreadyGenerated)
                {
                    template.NextInvoiceDate =
                        GetNextDate(
                            scheduledDate,
                            template.Frequency,
                            template.StartDate);

                    continue;
                }

                var dueDate =
                    scheduledDate.AddDays(
                        template.DueDays);

                var invoice =
                    await salesInvoices.CreateAndPostAsync(
                        userId,
                        new SalesInvoiceRequest(
                            template.OrganisationId,
                            template.CustomerId,
                            scheduledDate,
                            dueDate,
                            template.Lines
                                .Select(x =>
                                    new SalesInvoiceLineRequest(
                                        x.Description,
                                        x.Quantity,
                                        x.UnitPrice,
                                        x.VatTreatment,
                                        x.RevenueAccountId,
                                        x.ProductItemId))
                                .ToList()),
                        ct);

                db.RecurringSalesInvoiceGenerations.Add(
                    new RecurringSalesInvoiceGeneration
                    {
                        OrganisationId =
                            organisationId,
                        RecurringSalesInvoiceId =
                            template.Id,
                        ScheduledDate =
                            scheduledDate,
                        SalesInvoiceId =
                            invoice.Id,
                        GeneratedByUserId =
                            userId
                    });

                template.NextInvoiceDate =
                    GetNextDate(
                        scheduledDate,
                        template.Frequency,
                        template.StartDate);

                db.AuditEvents.Add(
                    new AuditEvent
                    {
                        OrganisationId =
                            organisationId,
                        EventType =
                            "RecurringSalesInvoiceGenerated",
                        EntityType =
                            nameof(RecurringSalesInvoice),
                        EntityId =
                            template.Id.ToString(),
                        UserId =
                            userId,
                        JsonData =
                            JsonSerializer.Serialize(
                                new
                                {
                                    ScheduledDate =
                                        scheduledDate,
                                    SalesInvoiceId =
                                        invoice.Id,
                                    invoice.InvoiceNumber
                                })
                    });

                await db.SaveChangesAsync(ct);

                generated.Add(invoice);
            }
        }

        return generated;
    }

    internal async Task<IReadOnlyList<SalesInvoice>> GenerateDueAutomaticallyAsync(
        Guid organisationId,
        DateOnly throughDate,
        CancellationToken ct = default)
    {
        var templates =
            await db.RecurringSalesInvoices
                .Include(x => x.Lines)
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.IsActive &&
                    x.Status == RecurringSalesInvoiceStatus.Active &&
                    x.NextInvoiceDate <= throughDate)
                .OrderBy(x => x.NextInvoiceDate)
                .ToListAsync(ct);

        var generated =
            new List<SalesInvoice>();

        foreach (var template in templates)
        {
            while (template.IsActive &&
                   template.Status == RecurringSalesInvoiceStatus.Active &&
                   template.NextInvoiceDate <= throughDate)
            {
                var scheduledDate =
                    template.NextInvoiceDate;

                var alreadyGenerated =
                    await db.RecurringSalesInvoiceGenerations
                        .AnyAsync(
                            x =>
                                x.RecurringSalesInvoiceId == template.Id &&
                                x.ScheduledDate == scheduledDate,
                            ct);

                if (alreadyGenerated)
                {
                    template.NextInvoiceDate =
                        GetNextDate(
                            scheduledDate,
                            template.Frequency,
                            template.StartDate);

                    await db.SaveChangesAsync(ct);

                    continue;
                }

                var nextInvoiceDate =
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
                    var invoice =
                        await salesInvoices.CreateAndPostAutomaticallyAsync(
                            organisationId,
                            new SalesInvoiceRequest(
                                template.OrganisationId,
                                template.CustomerId,
                                scheduledDate,
                                scheduledDate.AddDays(template.DueDays),
                                template.Lines
                                    .Select(x =>
                                        new SalesInvoiceLineRequest(
                                            x.Description,
                                            x.Quantity,
                                            x.UnitPrice,
                                            x.VatTreatment,
                                            x.RevenueAccountId,
                                            x.ProductItemId))
                                    .ToList()),
                            ct);

                    db.RecurringSalesInvoiceGenerations.Add(
                        new RecurringSalesInvoiceGeneration
                        {
                            OrganisationId = organisationId,
                            RecurringSalesInvoiceId = template.Id,
                            ScheduledDate = scheduledDate,
                            SalesInvoiceId = invoice.Id,
                            GeneratedByUserId = "system"
                        });

                    template.NextInvoiceDate = nextInvoiceDate;
                    db.AuditEvents.Add(
                        new AuditEvent
                        {
                            OrganisationId = organisationId,
                            EventType = "RecurringSalesInvoiceGenerated",
                            EntityType = nameof(RecurringSalesInvoice),
                            EntityId = template.Id.ToString(),
                            UserId = "system",
                            JsonData = JsonSerializer.Serialize(
                                new
                                {
                                    ScheduledDate = scheduledDate,
                                    SalesInvoiceId = invoice.Id,
                                    invoice.InvoiceNumber
                                })
                        });

                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    generated.Add(invoice);
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
        RecurringSalesInvoiceFrequency frequency,
        DateOnly startDate)
    {
        return frequency switch
        {
            RecurringSalesInvoiceFrequency.Weekly =>
                date.AddDays(7),

            RecurringSalesInvoiceFrequency.Monthly =>
                AddMonthsPreservingDay(
                    date,
                    1,
                    startDate.Day),

            RecurringSalesInvoiceFrequency.Quarterly =>
                AddMonthsPreservingDay(
                    date,
                    3,
                    startDate.Day),

            RecurringSalesInvoiceFrequency.Yearly =>
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
}
