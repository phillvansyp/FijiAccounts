using System.Data;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record SalesCreditNoteAllocation(Guid SalesInvoiceLineId, decimal GrossAmount);

public sealed record SalesCreditNoteDraftRequest(
    Guid OrganisationId,
    Guid SalesInvoiceId,
    DateOnly Date,
    string Reason,
    IReadOnlyCollection<SalesCreditNoteAllocation> Lines,
    bool RestockTrackedItems);

public sealed class FiscalisedSalesCreditNotePostingService(
    ApplicationDbContext db,
    TenantAccessService access,
    JournalPostingService posting,
    FiscalCreditNoteSubmissionFactory submissionFactory,
    FiscalisationWorkflowService workflow,
    FiscalisationOrchestratorService orchestrator)
{
    public async Task<SalesCreditNote> CreateDraftAsync(
        string userId,
        SalesCreditNoteDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId))
            throw new UnauthorizedAccessException("You cannot issue credit notes for this organisation.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidOperationException("Enter a reason for the credit note.");
        if (request.Lines.Count == 0)
            throw new InvalidOperationException("Allocate the credit to at least one invoice line.");

        var invoice = await db.SalesInvoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.Id == request.SalesInvoiceId &&
                x.OrganisationId == request.OrganisationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");
        var organisation = await db.Organisations.AsNoTracking()
            .SingleAsync(x => x.Id == request.OrganisationId, cancellationToken);
        if (!string.Equals(invoice.Currency, organisation.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Foreign-currency sales credits are not available until foreign settlement accounting is enabled.");
        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Voided or InvoiceStatus.Credited)
            throw new InvalidOperationException("This invoice cannot be credited.");

        var duplicate = request.Lines.GroupBy(x => x.SalesInvoiceLineId).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException("Each invoice line can be allocated only once.");

        var sourceById = invoice.Lines.ToDictionary(x => x.Id);
        var sourceLineIds = sourceById.Keys.ToArray();
        var previouslyAllocatedByLine = await db.SalesCreditNoteLines
            .Where(x =>
                sourceLineIds.Contains(x.SalesInvoiceLineId) &&
                !db.SalesCreditNoteReversals.Any(r =>
                    r.SalesCreditNoteId == x.SalesCreditNoteId &&
                    r.Status == SalesCreditNoteReversalStatus.Posted))
            .GroupBy(x => x.SalesInvoiceLineId)
            .Select(x => new { SalesInvoiceLineId = x.Key, GrossAmount = x.Sum(y => y.GrossAmount) })
            .ToDictionaryAsync(x => x.SalesInvoiceLineId, x => x.GrossAmount, cancellationToken);
        var lines = new List<SalesCreditNoteLine>();
        foreach (var allocation in request.Lines)
        {
            if (!sourceById.TryGetValue(allocation.SalesInvoiceLineId, out var source))
                throw new InvalidOperationException("A credit allocation does not belong to the original invoice.");
            var gross = decimal.Round(allocation.GrossAmount, 2, MidpointRounding.AwayFromZero);
            var availableForLine = source.TransactionGrossAmount -
                previouslyAllocatedByLine.GetValueOrDefault(source.Id);
            if (gross <= 0m || gross > availableForLine)
                throw new InvalidOperationException($"The allocation for {source.Description} must be between $0.01 and ${availableForLine:N2}.");

            decimal net;
            decimal vat;
            if (gross == source.TransactionGrossAmount)
            {
                net = source.TransactionNetAmount;
                vat = source.TransactionVatAmount;
            }
            else if (source.TransactionGrossAmount > 0m)
            {
                net = decimal.Round(gross * source.TransactionNetAmount / source.TransactionGrossAmount, 2, MidpointRounding.AwayFromZero);
                vat = gross - net;
            }
            else
            {
                net = gross;
                vat = 0m;
            }

            lines.Add(new SalesCreditNoteLine
            {
                SalesInvoiceLineId = source.Id,
                Description = source.Description,
                VatTreatment = source.VatTreatment,
                VatRate = source.VatRate,
                NetAmount = net,
                VatAmount = vat,
                GrossAmount = gross,
                RevenueAccountId = source.RevenueAccountId,
                ProductItemId = source.ProductItemId,
                ProjectId = source.ProjectId,
                ProjectCostCodeId = source.ProjectCostCodeId
            });
        }

        var total = lines.Sum(x => x.GrossAmount);
        var reservedDrafts = await db.SalesCreditNotes
            .Where(x => x.SalesInvoiceId == invoice.Id && x.Status == SalesCreditNoteStatus.Draft)
            .SumAsync(x => (decimal?)x.Total, cancellationToken) ?? 0m;
        var available = invoice.Total - invoice.AmountPaid - invoice.AmountCredited - reservedDrafts;
        if (total <= 0m || total > available)
            throw new InvalidOperationException($"Credit allocations must total between $0.01 and ${available:N2}.");

        var previouslyCreditedVat = await db.SalesCreditNotes
            .Where(x =>
                x.SalesInvoiceId == invoice.Id &&
                x.Status == SalesCreditNoteStatus.Posted &&
                !db.SalesCreditNoteReversals.Any(r =>
                    r.SalesCreditNoteId == x.Id &&
                    r.Status == SalesCreditNoteReversalStatus.Posted))
            .SumAsync(x => (decimal?)x.VatTotal, cancellationToken) ?? 0m;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var sequence = (await db.SalesCreditNotes
            .Where(x => x.OrganisationId == request.OrganisationId)
            .MaxAsync(x => (long?)x.SequenceNumber, cancellationToken) ?? 0) + 1;
        var credit = new SalesCreditNote
        {
            OrganisationId = request.OrganisationId,
            SalesInvoiceId = invoice.Id,
            SequenceNumber = sequence,
            CreditNoteNumber = $"CN-{sequence:D6}",
            CreditDate = request.Date,
            Reason = request.Reason.Trim(),
            Currency = invoice.Currency,
            Subtotal = lines.Sum(x => x.NetAmount),
            VatTotal = lines.Sum(x => x.VatAmount),
            Total = total,
            OriginalInvoiceVatAmount = invoice.VatTotal,
            AdjustedInvoiceVatAmount = Math.Max(0m, invoice.VatTotal - previouslyCreditedVat - lines.Sum(x => x.VatAmount)),
            Status = SalesCreditNoteStatus.Draft,
            RestockTrackedItems = request.RestockTrackedItems,
            CreatedByUserId = userId,
            Lines = lines
        };
        db.SalesCreditNotes.Add(credit);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = request.OrganisationId,
            UserId = userId,
            EventType = "SalesCreditNoteDraftCreated",
            EntityType = nameof(SalesCreditNote),
            EntityId = credit.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new { credit.CreditNoteNumber, invoice.InvoiceNumber, credit.Total, credit.VatTotal, LineCount = lines.Count })
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return credit;
    }

    public async Task<SalesCreditNote> PostAsync(
        string userId,
        Guid organisationId,
        Guid creditNoteId,
        CancellationToken cancellationToken = default)
    {
        var credit = await LoadDraftAsync(organisationId, creditNoteId, cancellationToken);
        var configuration = await db.FiscalisationConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken);
        if (configuration?.IsEnabled == true)
        {
            var originalRecord = await db.FiscalisationRecords.AsNoTracking()
                .SingleOrDefaultAsync(x =>
                    x.SalesInvoiceId == credit.SalesInvoiceId &&
                    x.OrganisationId == organisationId,
                    cancellationToken)
                ?? throw new InvalidOperationException("The original invoice has no fiscal record.");
            var record = await db.FiscalisationRecords.SingleOrDefaultAsync(x =>
                x.SalesCreditNoteId == credit.Id && x.OrganisationId == organisationId,
                cancellationToken);
            if (record is null)
            {
                var submission = submissionFactory.Create(
                    credit,
                    credit.SalesInvoice,
                    originalRecord,
                    FiscalisationConfigurationService.TaxLabels(configuration),
                    [new FiscalPayment(credit.Total, configuration.DefaultPaymentType)],
                    DateTimeOffset.UtcNow,
                    userId);
                record = await workflow.PrepareCreditNoteAsync(userId, organisationId, credit.Id, submission, cancellationToken);
            }

            if (record.Status == FiscalisationStatus.Submitting)
                record = await workflow.MarkRecoveryRequiredAsync(userId, organisationId, record.Id, "INTERRUPTED_SUBMISSION", "The prior refund submission was interrupted and must be recovered.", cancellationToken);
            if (record.Status == FiscalisationStatus.RecoveryRequired)
                record = await orchestrator.RecoverAsync(userId, organisationId, record.Id, cancellationToken);
            else if (record.Status is FiscalisationStatus.Prepared or FiscalisationStatus.Rejected)
                record = await orchestrator.SubmitAsync(userId, organisationId, record.Id, cancellationToken);

            if (record.Status != FiscalisationStatus.Accepted)
                throw new InvalidOperationException(record.Status == FiscalisationStatus.RecoveryRequired
                    ? "The fiscal refund response is uncertain. Recover it before posting the credit note."
                    : record.ErrorMessage ?? "The fiscal refund was not accepted.");
        }

        return await PostAccountingAsync(userId, credit, cancellationToken);
    }

    private async Task<SalesCreditNote> PostAccountingAsync(string userId, SalesCreditNote credit, CancellationToken cancellationToken)
    {
        if (!await access.CanPostJournalsAsync(userId, credit.OrganisationId))
            throw new UnauthorizedAccessException("You cannot issue credit notes for this organisation.");
        if (credit.Status != SalesCreditNoteStatus.Draft)
            return credit;

        var controls = await db.LedgerAccounts.Where(x =>
                x.OrganisationId == credit.OrganisationId && x.IsActive && (x.Code == "1100" || x.Code == "2100"))
            .ToDictionaryAsync(x => x.Code, cancellationToken);
        if (!controls.TryGetValue("1100", out var receivables) || receivables.Type != AccountType.Asset)
            throw new InvalidOperationException("Accounts Receivable (1100) must be an active Asset account.");
        if (!controls.TryGetValue("2100", out var vatPayable) || vatPayable.Type != AccountType.Liability)
            throw new InvalidOperationException("VAT Payable (2100) must be an active Liability account.");

        var journalLines = credit.Lines
            .GroupBy(x => new { x.RevenueAccountId, x.ProjectId, x.ProjectCostCodeId })
            .Select(x => new JournalLineInput(x.Key.RevenueAccountId, credit.CreditNoteNumber, x.Sum(y => y.NetAmount), 0m, ProjectId: x.Key.ProjectId, ProjectCostCodeId: x.Key.ProjectCostCodeId))
            .ToList();
        if (credit.VatTotal > 0m)
            journalLines.Add(new(vatPayable.Id, credit.CreditNoteNumber, credit.VatTotal, 0m));
        journalLines.Add(new(receivables.Id, credit.CreditNoteNumber, 0m, credit.Total));

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var restockPlans = credit.RestockTrackedItems
            ? await PrepareRestockAsync(credit, cancellationToken)
            : [];
        foreach (var plan in restockPlans.Where(x => x.Value > 0m))
        {
            journalLines.Add(new(plan.InventoryAccount.Id, $"Return {plan.Item.Code}", plan.Value, 0m));
            journalLines.Add(new(plan.CostAccount.Id, $"Reverse cost {plan.Item.Code}", 0m, plan.Value));
        }

        var journal = await posting.PostAsync(userId, new(
            credit.OrganisationId,
            credit.CreditDate,
            credit.CreditNoteNumber,
            $"Credit note for {credit.SalesInvoice.InvoiceNumber}: {credit.Reason}",
            journalLines,
            credit.SalesInvoice.BranchId,
            credit.SalesInvoice.DivisionId), cancellationToken);

        foreach (var plan in restockPlans)
        {
            plan.Item.QuantityOnHand += plan.Quantity;
            db.InventoryMovements.Add(new InventoryMovement
            {
                OrganisationId = credit.OrganisationId,
                BranchId = plan.Issue.BranchId,
                DivisionId = plan.Issue.DivisionId,
                ProductItemId = plan.Item.Id,
                MovementDate = credit.CreditDate,
                Type = InventoryMovementType.SalesReturn,
                QuantityChange = plan.Quantity,
                UnitCost = plan.Issue.UnitCost,
                ValueChange = plan.Value,
                Reference = credit.CreditNoteNumber,
                Note = $"Stock returned by credit of {credit.SalesInvoice.InvoiceNumber}",
                PostedJournalId = journal.Id,
                PostedByUserId = userId
            });
        }

        credit.PostedJournalId = journal.Id;
        credit.Status = SalesCreditNoteStatus.Posted;
        credit.SalesInvoice.AmountCredited += credit.Total;
        var remaining = credit.SalesInvoice.Total - credit.SalesInvoice.AmountPaid - credit.SalesInvoice.AmountCredited;
        credit.SalesInvoice.Status = remaining <= 0m
            ? InvoiceStatus.Credited
            : credit.SalesInvoice.AmountPaid > 0m || credit.SalesInvoice.AmountCredited > 0m
                ? InvoiceStatus.PartPaid
                : InvoiceStatus.Posted;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = credit.OrganisationId,
            UserId = userId,
            EventType = "SalesCreditNotePosted",
            EntityType = nameof(SalesCreditNote),
            EntityId = credit.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new { credit.CreditNoteNumber, credit.SalesInvoice.InvoiceNumber, credit.Total, credit.VatTotal, FiscalRecordAccepted = true })
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return credit;
    }

    private async Task<List<RestockPlan>> PrepareRestockAsync(
        SalesCreditNote credit,
        CancellationToken cancellationToken)
    {
        var creditedGrossByProduct = credit.Lines
            .Where(x => x.ProductItemId is not null)
            .GroupBy(x => x.ProductItemId!.Value)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.GrossAmount));
        if (creditedGrossByProduct.Count == 0)
            return [];

        var originalGrossByProduct = credit.SalesInvoice.Lines
            .Where(x => x.ProductItemId is not null)
            .GroupBy(x => x.ProductItemId!.Value)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.TransactionGrossAmount));
        var issues = await db.InventoryMovements
            .Where(x =>
                x.OrganisationId == credit.OrganisationId &&
                x.Reference == credit.SalesInvoice.InvoiceNumber &&
                x.QuantityChange < 0m &&
                creditedGrossByProduct.Keys.Contains(x.ProductItemId))
            .ToListAsync(cancellationToken);
        var items = credit.SalesInvoice.Lines
            .Where(x => x.ProductItem is not null)
            .Select(x => x.ProductItem!)
            .DistinctBy(x => x.Id)
            .ToDictionary(x => x.Id);
        var accountIds = items.Values
            .Where(x => creditedGrossByProduct.ContainsKey(x.Id))
            .SelectMany(x => new[] { x.InventoryAccountId, x.CostAdjustmentAccountId })
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var accounts = await db.LedgerAccounts
            .Where(x =>
                x.OrganisationId == credit.OrganisationId &&
                x.IsActive &&
                accountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var plans = new List<RestockPlan>();
        foreach (var issue in issues)
        {
            if (!items.TryGetValue(issue.ProductItemId, out var item))
                throw new InvalidOperationException("A tracked item on the original stock issue is no longer available.");
            if (item.InventoryAccountId is not Guid inventoryAccountId ||
                item.CostAdjustmentAccountId is not Guid costAccountId)
                throw new InvalidOperationException($"Inventory accounts are missing for {item.Code}.");
            if (!accounts.TryGetValue(inventoryAccountId, out var inventoryAccount) ||
                inventoryAccount.Type != AccountType.Asset)
                throw new InvalidOperationException($"Inventory account for {item.Code} must be an active Asset account.");
            if (!accounts.TryGetValue(costAccountId, out var costAccount) ||
                costAccount.Type != AccountType.Expense)
                throw new InvalidOperationException($"Cost adjustment account for {item.Code} must be an active Expense account.");

            var originalGross = originalGrossByProduct.GetValueOrDefault(item.Id);
            var ratio = originalGross <= 0m
                ? 0m
                : creditedGrossByProduct[item.Id] / originalGross;
            if (ratio <= 0m || ratio > 1m)
                throw new InvalidOperationException($"The stock return allocation for {item.Code} is invalid.");
            plans.Add(new(
                issue,
                item,
                inventoryAccount,
                costAccount,
                decimal.Round(-issue.QuantityChange * ratio, 4, MidpointRounding.AwayFromZero),
                decimal.Round(-issue.ValueChange * ratio, 2, MidpointRounding.AwayFromZero)));
        }

        return plans;
    }

    private sealed record RestockPlan(
        InventoryMovement Issue,
        ProductItem Item,
        LedgerAccount InventoryAccount,
        LedgerAccount CostAccount,
        decimal Quantity,
        decimal Value);

    private Task<SalesCreditNote> LoadDraftAsync(Guid organisationId, Guid creditNoteId, CancellationToken cancellationToken) =>
        db.SalesCreditNotes
            .Include(x => x.Lines)
                .ThenInclude(x => x.SalesInvoiceLine)
                    .ThenInclude(x => x.ProductItem)
            .Include(x => x.SalesInvoice)
                .ThenInclude(x => x.Lines)
                    .ThenInclude(x => x.ProductItem)
            .SingleAsync(x => x.Id == creditNoteId && x.OrganisationId == organisationId, cancellationToken);
}
