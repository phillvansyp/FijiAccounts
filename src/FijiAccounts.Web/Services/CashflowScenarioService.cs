using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record CashflowScenarioInvoiceOption(
    Guid Id,
    string InvoiceNumber,
    string CustomerName,
    DateOnly DueDate,
    decimal OutstandingAmount);

public sealed record CashflowScenarioConfiguration(
    string Currency,
    bool CanManage,
    IReadOnlyList<CashflowScenario> Scenarios,
    IReadOnlyList<CashflowScenarioInvoiceOption> OutstandingInvoices);

public sealed record CashflowScenarioComparison(
    CashflowScenario Scenario,
    decimal OpeningCash,
    CashflowForecast Baseline,
    CashflowForecast Adjusted);

public sealed record CreateCashflowScenarioRequest(
    Guid OrganisationId,
    string Name,
    string? Description);

public sealed record AddCashflowScenarioEventRequest(
    Guid OrganisationId,
    Guid ScenarioId,
    CashflowScenarioEventKind Kind,
    CashflowScenarioFrequency Frequency,
    string Title,
    decimal Amount,
    DateOnly EventDate,
    DateOnly? EndDate,
    Guid? SalesInvoiceId);

public sealed class CashflowScenarioService(
    ApplicationDbContext db,
    TenantAccessService access,
    CashflowForecastService forecasts)
{
    public async Task<CashflowScenarioConfiguration> GetAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, organisationId);
        var organisation = await db.Organisations
            .AsNoTracking()
            .SingleAsync(x => x.Id == organisationId, cancellationToken);
        var scenarios = await db.CashflowScenarios
            .AsNoTracking()
            .Include(x => x.Events)
            .Where(x => x.OrganisationId == organisationId && !x.IsArchived)
            .ToListAsync(cancellationToken);
        scenarios = scenarios
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Name)
            .ToList();
        var invoices = await db.SalesInvoices
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.AmountPaid + x.AmountCredited < x.Total &&
                x.Status != InvoiceStatus.Draft &&
                x.Status != InvoiceStatus.Voided)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.InvoiceNumber)
            .Select(x => new CashflowScenarioInvoiceOption(
                x.Id,
                x.InvoiceNumber,
                x.Customer.Name,
                x.DueDate,
                x.Total - x.AmountPaid - x.AmountCredited))
            .ToListAsync(cancellationToken);

        return new(
            organisation.BaseCurrency,
            await access.CanPostJournalsAsync(userId, organisationId),
            scenarios,
            invoices);
    }

    public async Task<CashflowScenario> CreateAsync(
        string userId,
        CreateCashflowScenarioRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireManagerAsync(userId, request.OrganisationId);
        var name = CleanRequired(request.Name, 120, "scenario name");
        var existingNames = await db.CashflowScenarios
            .AsNoTracking()
            .Where(x => x.OrganisationId == request.OrganisationId)
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);
        if (existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A cashflow scenario with that name already exists.");
        }

        var scenario = new CashflowScenario
        {
            OrganisationId = request.OrganisationId,
            Name = name,
            Description = CleanOptional(request.Description, 500, "description"),
            CreatedByUserId = userId
        };
        db.CashflowScenarios.Add(scenario);
        db.AuditEvents.Add(Audit(
            request.OrganisationId,
            userId,
            "CashflowScenarioCreated",
            nameof(CashflowScenario),
            scenario.Id,
            new { scenario.Name, scenario.Description }));
        await db.SaveChangesAsync(cancellationToken);
        return scenario;
    }

    public async Task<CashflowScenarioEvent> AddEventAsync(
        string userId,
        AddCashflowScenarioEventRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireManagerAsync(userId, request.OrganisationId);
        if (!Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.Frequency))
        {
            throw new InvalidOperationException("Select a valid scenario event type and frequency.");
        }

        var scenario = await db.CashflowScenarios.SingleOrDefaultAsync(
            x =>
                x.Id == request.ScenarioId &&
                x.OrganisationId == request.OrganisationId &&
                !x.IsArchived,
            cancellationToken)
            ?? throw new InvalidOperationException("The selected cashflow scenario is not active.");
        var title = CleanRequired(request.Title, 160, "event title");
        SalesInvoice? invoice = null;
        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var frequency = request.Frequency;
        DateOnly? endDate = request.EndDate;
        string? sourceReference = null;
        DateOnly? originalDate = null;

        if (request.Kind == CashflowScenarioEventKind.CustomerReceiptDelay)
        {
            if (request.SalesInvoiceId is not Guid invoiceId)
            {
                throw new InvalidOperationException("Select the customer invoice to delay.");
            }

            invoice = await db.SalesInvoices
                .Include(x => x.Customer)
                .SingleOrDefaultAsync(x =>
                    x.Id == invoiceId &&
                    x.OrganisationId == request.OrganisationId &&
                    x.AmountPaid + x.AmountCredited < x.Total &&
                    x.Status != InvoiceStatus.Draft &&
                    x.Status != InvoiceStatus.Voided,
                    cancellationToken)
                ?? throw new InvalidOperationException("The selected customer invoice is no longer outstanding.");
            if (request.EventDate <= invoice.DueDate)
            {
                throw new InvalidOperationException("The revised receipt date must be after the invoice due date.");
            }

            amount = invoice.Total - invoice.AmountPaid - invoice.AmountCredited;
            frequency = CashflowScenarioFrequency.OneOff;
            endDate = null;
            sourceReference = invoice.InvoiceNumber;
            originalDate = invoice.DueDate;
        }
        else
        {
            if (amount <= 0m)
            {
                throw new InvalidOperationException("The scenario amount must be greater than zero.");
            }

            if (frequency == CashflowScenarioFrequency.Monthly)
            {
                if (endDate is null || endDate < request.EventDate)
                {
                    throw new InvalidOperationException("Monthly scenario events require an end date on or after the first event date.");
                }
            }
            else
            {
                endDate = null;
            }
        }

        var adjustment = new CashflowScenarioEvent
        {
            CashflowScenarioId = scenario.Id,
            Kind = request.Kind,
            Frequency = frequency,
            Title = title,
            Amount = amount,
            EventDate = request.EventDate,
            EndDate = endDate,
            SalesInvoiceId = invoice?.Id,
            SourceReference = sourceReference,
            OriginalDate = originalDate,
            CreatedByUserId = userId
        };
        scenario.UpdatedAt = DateTimeOffset.UtcNow;
        db.CashflowScenarioEvents.Add(adjustment);
        db.AuditEvents.Add(Audit(
            request.OrganisationId,
            userId,
            "CashflowScenarioEventAdded",
            nameof(CashflowScenarioEvent),
            adjustment.Id,
            new
            {
                ScenarioId = scenario.Id,
                ScenarioName = scenario.Name,
                EventKind = adjustment.Kind.ToString(),
                adjustment.Title,
                adjustment.Amount,
                adjustment.EventDate,
                adjustment.EndDate,
                adjustment.SourceReference,
                adjustment.OriginalDate
            }));
        await db.SaveChangesAsync(cancellationToken);
        return adjustment;
    }

    public async Task RemoveEventAsync(
        string userId,
        Guid organisationId,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        await RequireManagerAsync(userId, organisationId);
        var adjustment = await db.CashflowScenarioEvents
            .Include(x => x.CashflowScenario)
            .SingleOrDefaultAsync(x =>
                x.Id == eventId &&
                x.CashflowScenario.OrganisationId == organisationId &&
                !x.CashflowScenario.IsArchived,
                cancellationToken)
            ?? throw new InvalidOperationException("The scenario event was not found.");
        adjustment.CashflowScenario.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "CashflowScenarioEventRemoved",
            nameof(CashflowScenarioEvent),
            adjustment.Id,
            new
            {
                ScenarioId = adjustment.CashflowScenarioId,
                ScenarioName = adjustment.CashflowScenario.Name,
                EventKind = adjustment.Kind.ToString(),
                adjustment.Title,
                adjustment.Amount
            }));
        db.CashflowScenarioEvents.Remove(adjustment);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ArchiveAsync(
        string userId,
        Guid organisationId,
        Guid scenarioId,
        CancellationToken cancellationToken = default)
    {
        await RequireManagerAsync(userId, organisationId);
        var scenario = await db.CashflowScenarios.SingleOrDefaultAsync(
            x => x.Id == scenarioId && x.OrganisationId == organisationId && !x.IsArchived,
            cancellationToken)
            ?? throw new InvalidOperationException("The cashflow scenario was not found.");
        scenario.IsArchived = true;
        scenario.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "CashflowScenarioArchived",
            nameof(CashflowScenario),
            scenario.Id,
            new { scenario.Name, EventCount = await db.CashflowScenarioEvents.CountAsync(x => x.CashflowScenarioId == scenario.Id, cancellationToken) }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CashflowScenarioComparison> CompareAsync(
        string userId,
        Guid organisationId,
        Guid scenarioId,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, organisationId);
        var scenario = await db.CashflowScenarios
            .AsNoTracking()
            .Include(x => x.Events)
            .SingleOrDefaultAsync(x =>
                x.Id == scenarioId &&
                x.OrganisationId == organisationId &&
                !x.IsArchived,
                cancellationToken)
            ?? throw new InvalidOperationException("The selected cashflow scenario is not active.");
        var openingCash = await db.PostedJournalLines
            .AsNoTracking()
            .Where(x =>
                x.PostedJournal.OrganisationId == organisationId &&
                x.PostedJournal.EntryDate <= asAt &&
                x.LedgerAccount.IsBankAccount)
            .SumAsync(x => (decimal?)(x.Debit - x.Credit), cancellationToken) ?? 0m;
        var baseline = await forecasts.GetAsync(organisationId, asAt, cancellationToken);
        var adjusted = await forecasts.GetForScenarioAsync(
            organisationId,
            asAt,
            scenarioId,
            cancellationToken);
        return new(scenario, openingCash, baseline, adjusted);
    }

    private async Task RequireAccessAsync(string userId, Guid organisationId)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException("You do not have access to this organisation.");
        }
    }

    private async Task RequireManagerAsync(string userId, Guid organisationId)
    {
        await RequireAccessAsync(userId, organisationId);
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException("You do not have permission to manage cashflow scenarios.");
        }
    }

    private static string CleanRequired(string value, int maxLength, string label)
    {
        var clean = value.Trim();
        if (clean.Length is < 1 || clean.Length > maxLength)
        {
            throw new InvalidOperationException($"Enter a {label} of {maxLength} characters or fewer.");
        }

        return clean;
    }

    private static string? CleanOptional(string? value, int maxLength, string label)
    {
        var clean = value?.Trim();
        if (string.IsNullOrEmpty(clean)) return null;
        if (clean.Length > maxLength)
        {
            throw new InvalidOperationException($"The {label} must be {maxLength} characters or fewer.");
        }

        return clean;
    }

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        string entityType,
        Guid entityId,
        object evidence) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        };
}
