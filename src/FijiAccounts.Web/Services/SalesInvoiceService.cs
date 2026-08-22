using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record SalesInvoiceLineRequest(string Description, decimal Quantity, decimal UnitPrice, VatTreatment VatTreatment, Guid RevenueAccountId, Guid? ProductItemId = null, string? CustomerPurchaseOrderNumber = null);
public sealed record SalesInvoiceRequest(Guid OrganisationId, Guid CustomerId, DateOnly IssueDate, DateOnly DueDate, IReadOnlyList<SalesInvoiceLineRequest> Lines);

public sealed class SalesInvoiceService(ApplicationDbContext db, TenantAccessService access, JournalPostingService posting)
{
    public async Task<SalesInvoice> CreateDraftAsync(string userId, SalesInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot create invoices for this organisation.");
        }
        var organisation = await db.Organisations.SingleAsync(x => x.Id == request.OrganisationId, cancellationToken);
        var lines = await PrepareLinesAsync(organisation, request, cancellationToken);
        var sequence = (await db.SalesInvoices.Where(x => x.OrganisationId == request.OrganisationId).MaxAsync(x => (long?)x.SequenceNumber, cancellationToken) ?? 0) + 1;
        var invoice = new SalesInvoice { OrganisationId = request.OrganisationId, CustomerId = request.CustomerId, SequenceNumber = sequence, InvoiceNumber = $"DRAFT-{sequence:D6}", IssueDate = request.IssueDate, DueDate = request.DueDate, Currency = organisation.BaseCurrency, Status = InvoiceStatus.Draft, Subtotal = lines.Sum(x => x.NetAmount), VatTotal = lines.Sum(x => x.VatAmount), Total = lines.Sum(x => x.GrossAmount), CreatedByUserId = userId, Lines = lines };
        db.SalesInvoices.Add(invoice); db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, EventType = "SalesInvoiceDraftCreated", EntityType = nameof(SalesInvoice), EntityId = invoice.Id.ToString(), UserId = userId, JsonData = JsonSerializer.Serialize(new { invoice.InvoiceNumber, invoice.Total, Lines = lines.Count }) }); await db.SaveChangesAsync(cancellationToken); return invoice;
    }

    public async Task<SalesInvoice> PostDraftAsync(string userId, Guid organisationId, Guid invoiceId, CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot post invoices for this organisation.");
        var invoice = await db.SalesInvoices.Include(x => x.Lines).ThenInclude(x => x.ProductItem).SingleOrDefaultAsync(x => x.Id == invoiceId && x.OrganisationId == organisationId, cancellationToken) ?? throw new InvalidOperationException("Invoice not found.");
        if (invoice.Status != InvoiceStatus.Draft) throw new InvalidOperationException("Only draft invoices can be posted.");
        var controls = await db.LedgerAccounts
    .Where(x =>
        x.OrganisationId == organisationId &&
        x.IsActive &&
        (x.Code == "1100" || x.Code == "2100"))
    .ToDictionaryAsync(
        x => x.Code,
        cancellationToken);

if (!controls.TryGetValue("1100", out var receivables) ||
    receivables.Type != AccountType.Asset)
{
    throw new InvalidOperationException(
        "Accounts Receivable (1100) must be an active Asset account.");
}

if (!controls.TryGetValue("2100", out var vatPayable) ||
    vatPayable.Type != AccountType.Liability)
{
    throw new InvalidOperationException(
        "VAT Payable (2100) must be an active Liability account.");
}
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var organisation =
            await db.Organisations.SingleAsync(
                x => x.Id == organisationId,
                cancellationToken);

        invoice.InvoiceNumber =
            AllocateSalesInvoiceNumber(organisation);
        var journalLines = new List<JournalLineInput> { new(receivables.Id, invoice.InvoiceNumber, invoice.Total, 0) };
        journalLines.AddRange(invoice.Lines.GroupBy(x => x.RevenueAccountId).Select(x => new JournalLineInput(x.Key, invoice.InvoiceNumber, 0, x.Sum(y => y.NetAmount))));
        if (invoice.VatTotal > 0) journalLines.Add(new(vatPayable.Id, invoice.InvoiceNumber, 0, invoice.VatTotal));
        await AddInventorySaleLinesAsync(
    organisationId,
    invoice.Lines,
    journalLines,
    cancellationToken);
        var journal = await posting.PostAsync(userId, new(organisationId, invoice.IssueDate, invoice.InvoiceNumber, $"Sales invoice {invoice.InvoiceNumber}", journalLines), cancellationToken);
        RecordSaleMovements(invoice, journal.Id, userId); invoice.Status = InvoiceStatus.Posted; invoice.PostedJournalId = journal.Id; db.AuditEvents.Add(new AuditEvent { OrganisationId = organisationId, EventType = "SalesInvoicePosted", EntityType = nameof(SalesInvoice), EntityId = invoice.Id.ToString(), UserId = userId, JsonData = JsonSerializer.Serialize(new { invoice.InvoiceNumber, invoice.Total, invoice.VatTotal }) }); await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return invoice;
    }

    public async Task<SalesInvoice> UpdateDraftAsync(string userId, Guid invoiceId, SalesInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot edit invoices for this organisation.");
        var invoice = await db.SalesInvoices.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == invoiceId && x.OrganisationId == request.OrganisationId, cancellationToken) ?? throw new InvalidOperationException("Invoice not found.");
        if (invoice.Status != InvoiceStatus.Draft) throw new InvalidOperationException("Only draft invoices can be edited.");
        var organisation = await db.Organisations.SingleAsync(x => x.Id == request.OrganisationId, cancellationToken); var lines = await PrepareLinesAsync(organisation, request, cancellationToken);
        var existingLines =
    invoice.Lines.ToList();

db.SalesInvoiceLines.RemoveRange(existingLines);

foreach (var line in lines)
{
    line.SalesInvoiceId = invoice.Id;
    line.SalesInvoice = invoice;
    db.SalesInvoiceLines.Add(line);
}

invoice.CustomerId = request.CustomerId;
invoice.IssueDate = request.IssueDate;
invoice.DueDate = request.DueDate;
invoice.Subtotal = lines.Sum(x => x.NetAmount);
invoice.VatTotal = lines.Sum(x => x.VatAmount);
invoice.Total = lines.Sum(x => x.GrossAmount);
        db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, EventType = "SalesInvoiceDraftUpdated", EntityType = nameof(SalesInvoice), EntityId = invoice.Id.ToString(), UserId = userId, JsonData = JsonSerializer.Serialize(new { invoice.InvoiceNumber, invoice.Total, Lines = lines.Count }) }); await db.SaveChangesAsync(cancellationToken); return invoice;
    }

    public async Task<SalesInvoice> VoidAsync(string userId, Guid organisationId, Guid invoiceId, DateOnly voidDate, CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot void invoices for this organisation.");
        var invoice = await db.SalesInvoices.Include(x => x.Lines).ThenInclude(x => x.ProductItem).SingleOrDefaultAsync(x => x.Id == invoiceId && x.OrganisationId == organisationId, cancellationToken) ?? throw new InvalidOperationException("Invoice not found.");
        if (invoice.AmountPaid > 0 ||
    invoice.AmountCredited > 0 ||
    invoice.Status is InvoiceStatus.Paid or
        InvoiceStatus.PartPaid or
        InvoiceStatus.Credited)
{
    throw new InvalidOperationException(
        "A paid or credited invoice cannot be voided. Reverse payments first; sales credits remain permanent audit records.");
}

if (invoice.Status != InvoiceStatus.Posted ||
    invoice.PostedJournalId is null)
{
    throw new InvalidOperationException(
        "Only unpaid posted invoices can be voided.");
}
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var original = await db.PostedJournals.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == invoice.PostedJournalId && x.OrganisationId == organisationId, cancellationToken);
        var reversal = original.Lines.Select(x => new JournalLineInput(x.LedgerAccountId, $"Void {invoice.InvoiceNumber}", x.Credit, x.Debit, x.BranchId, x.DivisionId)).ToList(); var journal = await posting.PostAsync(userId, new(organisationId, voidDate, $"VOID-{invoice.InvoiceNumber}", $"Void sales invoice {invoice.InvoiceNumber}", reversal), cancellationToken);
        var issues = await db.InventoryMovements.Where(x => x.OrganisationId == organisationId && x.Reference == invoice.InvoiceNumber && x.QuantityChange < 0).ToListAsync(cancellationToken);
        foreach (var issue in issues) { var item = invoice.Lines.Select(x => x.ProductItem).First(x => x?.Id == issue.ProductItemId)!; var quantity = -issue.QuantityChange; item.QuantityOnHand += quantity; db.InventoryMovements.Add(new InventoryMovement { OrganisationId = organisationId, ProductItemId = item.Id, MovementDate = voidDate, Type = InventoryMovementType.SalesReturn, QuantityChange = quantity, UnitCost = issue.UnitCost, ValueChange = -issue.ValueChange, Reference = $"VOID-{invoice.InvoiceNumber}", Note = "Stock restored by invoice void", PostedJournalId = journal.Id, PostedByUserId = userId }); }
        var invoiceVoid = new SalesInvoiceVoid
{
    OrganisationId = organisationId,
    SalesInvoiceId = invoice.Id,
    VoidDate = voidDate,
    PostedJournalId = journal.Id,
    CreatedByUserId = userId
};

db.SalesInvoiceVoids.Add(invoiceVoid);
        invoice.Status = InvoiceStatus.Voided; db.AuditEvents.Add(new AuditEvent { OrganisationId = organisationId, EventType = "SalesInvoiceVoided", EntityType = nameof(SalesInvoice), EntityId = invoice.Id.ToString(), UserId = userId, JsonData = JsonSerializer.Serialize(new { invoice.InvoiceNumber, ReversalJournalId = journal.Id, voidDate, StockReturns = issues.Count }) }); await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return invoice;
    }

    private async Task<List<SalesInvoiceLine>> PrepareLinesAsync(Organisation organisation, SalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        var jurisdiction = IslandJurisdictions.Get(organisation.CountryCode); if (!jurisdiction.TaxPackEnabled) throw new InvalidOperationException($"The {jurisdiction.CountryName} tax pack is not yet enabled. Transactions are locked until its rules have been verified.");
        if (request.DueDate < request.IssueDate || request.Lines.Count == 0) throw new InvalidOperationException("Enter valid invoice dates and at least one line.");
        if (!await db.BusinessParties.AnyAsync(x => x.Id == request.CustomerId && x.OrganisationId == request.OrganisationId && x.IsActive && (x.Type & PartyType.Customer) != 0, cancellationToken)) throw new InvalidOperationException("Select an active customer in this organisation.");
        var accountIds = request.Lines.Select(x => x.RevenueAccountId).Distinct().ToArray(); if (await db.LedgerAccounts.CountAsync(x => x.OrganisationId == request.OrganisationId && x.IsActive && accountIds.Contains(x.Id) && x.Type == AccountType.Revenue, cancellationToken) != accountIds.Length) throw new InvalidOperationException("Every line must use an active revenue account.");
        var schedule = new FijiVatSchedule(); return request.Lines.Select(x => { if (string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0 || x.UnitPrice < 0) throw new InvalidOperationException("Each invoice line needs a description, positive quantity and non-negative price."); var tax = schedule.CalculateFromExclusive(new Money(x.Quantity * x.UnitPrice, organisation.BaseCurrency).Round(), request.IssueDate, x.VatTreatment); return new SalesInvoiceLine { Description = x.Description.Trim(), CustomerPurchaseOrderNumber = string.IsNullOrWhiteSpace(x.CustomerPurchaseOrderNumber) ? null : x.CustomerPurchaseOrderNumber.Trim(), Quantity = x.Quantity, UnitPrice = x.UnitPrice, VatTreatment = x.VatTreatment, VatRate = tax.Rate, NetAmount = tax.Exclusive.Amount, VatAmount = tax.Vat.Amount, GrossAmount = tax.Inclusive.Amount, RevenueAccountId = x.RevenueAccountId, ProductItemId = x.ProductItemId }; }).ToList();
    }

    internal Task<SalesInvoice> CreateAndPostAutomaticallyAsync(
        Guid organisationId,
        SalesInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        return CreateAndPostAsync(
            "system",
            request,
            cancellationToken,
            skipPermissionCheck: true);
    }
    public async Task<SalesInvoice> CreateAndPostAsync(
    string userId,
    SalesInvoiceRequest request,
    CancellationToken cancellationToken = default,
    bool skipPermissionCheck = false)
    {
        if (!skipPermissionCheck &&
            !await access.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot create invoices for this organisation.");
        }
        var organisation = await db.Organisations.SingleAsync(x => x.Id == request.OrganisationId, cancellationToken);
        var jurisdiction = IslandJurisdictions.Get(organisation.CountryCode);
        if (!jurisdiction.TaxPackEnabled) throw new InvalidOperationException($"The {jurisdiction.CountryName} tax pack is not yet enabled. Transactions are locked until its rules have been verified.");
        if (request.DueDate < request.IssueDate) throw new InvalidOperationException("The due date cannot be before the issue date.");
        if (request.Lines.Count == 0) throw new InvalidOperationException("An invoice needs at least one line.");
        if (!await db.BusinessParties.AnyAsync(x => x.Id == request.CustomerId && x.OrganisationId == request.OrganisationId && x.IsActive && (x.Type & PartyType.Customer) != 0, cancellationToken)) throw new InvalidOperationException("Select an active customer in this organisation.");

        var revenueIds = request.Lines.Select(x => x.RevenueAccountId).Distinct().ToArray();
        var revenue = await db.LedgerAccounts.Where(x => x.OrganisationId == request.OrganisationId && x.IsActive && revenueIds.Contains(x.Id) && x.Type == AccountType.Revenue).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (revenue.Count != revenueIds.Length) throw new InvalidOperationException("Every invoice line must use an active revenue account.");
        var controlAccounts = await db.LedgerAccounts
    .Where(x =>
        x.OrganisationId == request.OrganisationId &&
        x.IsActive &&
        (x.Code == "1100" || x.Code == "2100"))
    .ToDictionaryAsync(
        x => x.Code,
        cancellationToken);

if (!controlAccounts.TryGetValue(
        "1100",
        out var receivables) ||
    receivables.Type != AccountType.Asset)
{
    throw new InvalidOperationException(
        "Accounts Receivable (1100) must be an active Asset account.");
}

if (!controlAccounts.TryGetValue(
        "2100",
        out var vatPayable) ||
    vatPayable.Type != AccountType.Liability)
{
    throw new InvalidOperationException(
        "VAT Payable (2100) must be an active Liability account.");
}

        var vatSchedule = new FijiVatSchedule();
        var lines = request.Lines.Select(line =>
        {
            if (line.Quantity <= 0 || line.UnitPrice < 0) throw new InvalidOperationException("Invoice quantities must be positive and prices cannot be negative.");
            var net = new Money(line.Quantity * line.UnitPrice, organisation.BaseCurrency).Round(); var tax = vatSchedule.CalculateFromExclusive(net, request.IssueDate, line.VatTreatment);
            return new SalesInvoiceLine { Description = line.Description.Trim(), CustomerPurchaseOrderNumber = string.IsNullOrWhiteSpace(line.CustomerPurchaseOrderNumber) ? null : line.CustomerPurchaseOrderNumber.Trim(), Quantity = line.Quantity, UnitPrice = line.UnitPrice, VatTreatment = line.VatTreatment, VatRate = tax.Rate, NetAmount = tax.Exclusive.Amount, VatAmount = tax.Vat.Amount, GrossAmount = tax.Inclusive.Amount, RevenueAccountId = line.RevenueAccountId, ProductItemId = line.ProductItemId };
        }).ToList();

        var productIds = lines.Where(x => x.ProductItemId != null).Select(x => x.ProductItemId!.Value).Distinct().ToArray();
        var products = await db.ProductItems.Where(x => x.OrganisationId == request.OrganisationId && productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var line in lines.Where(x => x.ProductItemId != null)) line.ProductItem = products.GetValueOrDefault(line.ProductItemId!.Value) ?? throw new InvalidOperationException("A selected catalogue item is unavailable.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        await db.Entry(organisation)
            .ReloadAsync(cancellationToken);

        var sequence = (await db.SalesInvoices.Where(x => x.OrganisationId == request.OrganisationId).MaxAsync(x => (long?)x.SequenceNumber, cancellationToken) ?? 0) + 1;

        var invoiceNumber =
            AllocateSalesInvoiceNumber(organisation);

        var invoice = new SalesInvoice { OrganisationId = request.OrganisationId, CustomerId = request.CustomerId, SequenceNumber = sequence, InvoiceNumber = invoiceNumber, IssueDate = request.IssueDate, DueDate = request.DueDate, Currency = organisation.BaseCurrency, Status = InvoiceStatus.Posted, Subtotal = lines.Sum(x => x.NetAmount), VatTotal = lines.Sum(x => x.VatAmount), Total = lines.Sum(x => x.GrossAmount), CreatedByUserId = userId, Lines = lines };
        db.SalesInvoices.Add(invoice); await db.SaveChangesAsync(cancellationToken);

        var journalLines = new List<JournalLineInput> { new(receivables.Id, invoice.InvoiceNumber, invoice.Total, 0) };
        journalLines.AddRange(lines.GroupBy(x => x.RevenueAccountId).Select(x => new JournalLineInput(x.Key, invoice.InvoiceNumber, 0, x.Sum(y => y.NetAmount))));
        if (invoice.VatTotal > 0) journalLines.Add(new(vatPayable.Id, invoice.InvoiceNumber, 0, invoice.VatTotal));
        await AddInventorySaleLinesAsync(
    request.OrganisationId,
    lines,
    journalLines,
    cancellationToken);
        var journal =
            skipPermissionCheck
                ? await posting.PostAutomaticallyAsync(
                    new JournalPostRequest(
                        request.OrganisationId,
                        request.IssueDate,
                        invoice.InvoiceNumber,
                        $"Sales invoice {invoice.InvoiceNumber}",
                        journalLines),
                    cancellationToken)
                : await posting.PostAsync(
                    userId,
                    new JournalPostRequest(
                        request.OrganisationId,
                        request.IssueDate,
                        invoice.InvoiceNumber,
                        $"Sales invoice {invoice.InvoiceNumber}",
                        journalLines),
                    cancellationToken);
        RecordSaleMovements(invoice, journal.Id, userId); invoice.PostedJournalId = journal.Id;
        db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, EventType = "SalesInvoicePosted", EntityType = nameof(SalesInvoice), EntityId = invoice.Id.ToString(), UserId = userId, JsonData = JsonSerializer.Serialize(new { invoice.InvoiceNumber, invoice.Total, invoice.VatTotal }) });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return invoice;
    }

    private async Task AddInventorySaleLinesAsync(
    Guid organisationId,
    IEnumerable<SalesInvoiceLine> lines,
    List<JournalLineInput> journalLines,
    CancellationToken ct)
{
    foreach (var group in lines
        .Where(x => x.ProductItem?.Kind == ProductKind.TrackedItem)
        .GroupBy(x => x.ProductItem!))
    {
        var item = group.Key;
        var quantity = group.Sum(x => x.Quantity);

        if (quantity > item.QuantityOnHand)
        {
            throw new InvalidOperationException(
                $"Insufficient stock for {item.Code}. {item.QuantityOnHand:N4} is available.");
        }

        if (item.InventoryAccountId is null ||
            item.CostAdjustmentAccountId is null)
        {
            throw new InvalidOperationException(
                $"Set opening stock and inventory accounts for {item.Code} before selling it.");
        }

        var accountIds =
            new[]
            {
                item.InventoryAccountId.Value,
                item.CostAdjustmentAccountId.Value
            };

        var accounts =
            await db.LedgerAccounts
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.IsActive &&
                    accountIds.Contains(x.Id))
                .ToDictionaryAsync(
                    x => x.Id,
                    ct);

        if (!accounts.TryGetValue(
                item.InventoryAccountId.Value,
                out var inventoryAccount) ||
            inventoryAccount.Type != AccountType.Asset)
        {
            throw new InvalidOperationException(
                $"Inventory account for {item.Code} must be an active Asset account.");
        }

        if (!accounts.TryGetValue(
                item.CostAdjustmentAccountId.Value,
                out var costAccount) ||
            costAccount.Type != AccountType.Expense)
        {
            throw new InvalidOperationException(
                $"Cost adjustment account for {item.Code} must be an active Expense account.");
        }

        var value =
            InventoryValuation.MovementValue(
                quantity,
                item.AverageCost);

        if (value > 0)
        {
            journalLines.Add(
                new(
                    costAccount.Id,
                    $"Cost of {item.Code}",
                    value,
                    0));

            journalLines.Add(
                new(
                    inventoryAccount.Id,
                    $"Stock issued {item.Code}",
                    0,
                    value));
        }
    }
}

    private static string AllocateSalesInvoiceNumber(
        Organisation organisation)
    {
        if (organisation.NextSalesInvoiceNumber < 1)
        {
            throw new InvalidOperationException(
                "The next sales invoice number must be at least 1.");
        }

        var invoiceNumber =
            $"{organisation.SalesInvoicePrefix}{organisation.NextSalesInvoiceNumber:D6}";

        organisation.NextSalesInvoiceNumber++;

        return invoiceNumber;
    }

    private void RecordSaleMovements(SalesInvoice invoice, Guid journalId, string userId)
    {
        foreach (var group in invoice.Lines.Where(x => x.ProductItem?.Kind == ProductKind.TrackedItem).GroupBy(x => x.ProductItem!))
        {
            var item = group.Key; var quantity = group.Sum(x => x.Quantity); var value = InventoryValuation.MovementValue(quantity, item.AverageCost); item.QuantityOnHand -= quantity;
            db.InventoryMovements.Add(new InventoryMovement { OrganisationId = invoice.OrganisationId, ProductItemId = item.Id, MovementDate = invoice.IssueDate, Type = InventoryMovementType.AdjustmentDecrease, QuantityChange = -quantity, UnitCost = item.AverageCost, ValueChange = -value, Reference = invoice.InvoiceNumber, Note = "Automatic stock issue from sales invoice", PostedJournalId = journalId, PostedByUserId = userId });
        }
    }
}
