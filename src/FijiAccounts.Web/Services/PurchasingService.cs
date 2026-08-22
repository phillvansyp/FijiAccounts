using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record SupplierBillLineRequest(string Description, decimal Quantity, decimal UnitPrice, VatTreatment VatTreatment, Guid ExpenseAccountId, Guid? ProductItemId = null);
public sealed record SupplierBillRequest(Guid OrganisationId, Guid SupplierId, string SupplierReference, DateOnly BillDate, DateOnly DueDate, IReadOnlyList<SupplierBillLineRequest> Lines);
public sealed record SupplierBillAttachmentRequest(string FileName, string ContentType, long OriginalSize, byte[] Content, bool IsCompressed);
public sealed record SupplierPaymentRequest(Guid OrganisationId, Guid SupplierBillId, DateOnly Date, string Reference, decimal Amount, Guid BankAccountId);

public sealed class PurchasingService(
    ApplicationDbContext db,
    TenantAccessService access,
    JournalPostingService posting,
    BankReconciliationService reconciliation,
    NotificationService notifications)
{
    public Task<SupplierBill> PostBillAsync(
        string userId,
        SupplierBillRequest request,
        CancellationToken ct = default) =>
        PostBillCoreAsync(
            userId,
            request,
            draftId: null,
            attachment: null,
            ct);

    public Task<SupplierBill> PostDraftBillAsync(
        string userId,
        Guid draftId,
        SupplierBillRequest request,
        SupplierBillAttachmentRequest? attachment = null,
        CancellationToken ct = default) =>
        PostBillCoreAsync(
            userId,
            request,
            draftId,
            attachment,
            ct);

    private async Task<SupplierBill> PostBillCoreAsync(
        string userId,
        SupplierBillRequest request,
        Guid? draftId,
        SupplierBillAttachmentRequest? attachment,
        CancellationToken ct)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot post bills for this organisation.");
        var organisation = await db.Organisations.SingleAsync(x => x.Id == request.OrganisationId, ct);
        var jurisdiction = IslandJurisdictions.Get(organisation.CountryCode);
        if (!jurisdiction.TaxPackEnabled) throw new InvalidOperationException($"The {jurisdiction.CountryName} tax pack is not yet enabled. Transactions are locked until its rules have been verified.");
        if (request.DueDate < request.BillDate || request.Lines.Count == 0) throw new InvalidOperationException("Enter a valid bill date, due date and at least one line.");
        if (!await db.BusinessParties.AnyAsync(x => x.Id == request.SupplierId && x.OrganisationId == request.OrganisationId && x.IsActive && (x.Type & PartyType.Supplier) != 0, ct)) throw new InvalidOperationException("Select an active supplier in this organisation.");
        var expenseIds = request.Lines.Select(x => x.ExpenseAccountId).Distinct().ToArray();
        var expenses = await db.LedgerAccounts.Where(x => x.OrganisationId == request.OrganisationId && x.IsActive && expenseIds.Contains(x.Id) && (x.Type == AccountType.Expense || x.Type == AccountType.Asset)).ToDictionaryAsync(x => x.Id, ct);
        if (expenses.Count != expenseIds.Length) throw new InvalidOperationException("Every bill line must use an active expense or asset account.");
        var controls = await db.LedgerAccounts
    .Where(x =>
        x.OrganisationId == request.OrganisationId &&
        x.IsActive &&
        (x.Code == "1150" || x.Code == "2000"))
    .ToDictionaryAsync(
        x => x.Code,
        ct);

if (!controls.TryGetValue(
        "1150",
        out var vatReceivable) ||
    vatReceivable.Type != AccountType.Asset)
{
    throw new InvalidOperationException(
        "VAT Receivable (1150) must be an active Asset account.");
}

if (!controls.TryGetValue(
        "2000",
        out var accountsPayable) ||
    accountsPayable.Type != AccountType.Liability)
{
    throw new InvalidOperationException(
        "Accounts Payable (2000) must be an active Liability account.");
}
        var schedule = new FijiVatSchedule();
        var lines = request.Lines.Select(x => { if (string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0 || x.UnitPrice < 0) throw new InvalidOperationException("Every bill line needs a description, positive quantity and non-negative price."); var tax = schedule.CalculateFromExclusive(new Money(x.Quantity * x.UnitPrice, organisation.BaseCurrency).Round(), request.BillDate, x.VatTreatment); return new SupplierBillLine { Description = x.Description.Trim(), Quantity = x.Quantity, UnitPrice = x.UnitPrice, VatTreatment = x.VatTreatment, VatRate = tax.Rate, NetAmount = tax.Exclusive.Amount, VatAmount = tax.Vat.Amount, GrossAmount = tax.Inclusive.Amount, ExpenseAccountId = x.ExpenseAccountId, ProductItemId = x.ProductItemId }; }).ToList();
        var trackedIds =
    lines
        .Where(x => x.ProductItemId != null)
        .Select(x => x.ProductItemId!.Value)
        .Distinct()
        .ToArray();

var tracked =
    await db.ProductItems
        .Where(x =>
            x.OrganisationId == request.OrganisationId &&
            x.Kind == ProductKind.TrackedItem &&
            trackedIds.Contains(x.Id))
        .ToDictionaryAsync(
            x => x.Id,
            ct);

foreach (var line in lines.Where(
    x =>
        x.ProductItemId != null &&
        tracked.ContainsKey(x.ProductItemId.Value)))
{
    var item =
        tracked[line.ProductItemId!.Value];

    if (item.InventoryAccountId is null ||
        line.ExpenseAccountId != item.InventoryAccountId)
    {
        throw new InvalidOperationException(
            $"Set opening stock and inventory accounts for {item.Code} before purchasing it.");
    }

    var inventoryAccount =
    await db.LedgerAccounts
        .SingleOrDefaultAsync(
            x =>
                x.Id == item.InventoryAccountId.Value &&
                x.OrganisationId == request.OrganisationId,
            ct);

if (inventoryAccount is null ||
    !inventoryAccount.IsActive ||
    inventoryAccount.Type != AccountType.Asset)
{
    throw new InvalidOperationException(
        $"Inventory account for {item.Code} ({inventoryAccount?.Code ?? item.InventoryAccountId.Value.ToString()}) must be an active Asset account.");
}
}

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        SupplierBillDraft? draft = null;

        if (draftId is Guid id)
        {
            draft =
                await db.SupplierBillDrafts
                    .SingleOrDefaultAsync(
                        x =>
                            x.Id == id &&
                            x.OrganisationId == request.OrganisationId,
                        ct);

            if (draft is null)
            {
                throw new InvalidOperationException(
                    "Supplier bill draft not found.");
            }
        }

        var sequence = (await db.SupplierBills.Where(x => x.OrganisationId == request.OrganisationId).MaxAsync(x => (long?)x.SequenceNumber, ct) ?? 0) + 1;
        var bill = new SupplierBill { OrganisationId = request.OrganisationId, SupplierId = request.SupplierId, SequenceNumber = sequence, BillNumber = $"BILL-{sequence:D6}", SupplierReference = request.SupplierReference.Trim(), BillDate = request.BillDate, DueDate = request.DueDate, Currency = organisation.BaseCurrency, Status = BillStatus.Posted, Subtotal = lines.Sum(x => x.NetAmount), VatTotal = lines.Sum(x => x.VatAmount), Total = lines.Sum(x => x.GrossAmount), CreatedByUserId = userId, Lines = lines };
        var journalLines = lines.GroupBy(x => x.ExpenseAccountId).Select(x => new JournalLineInput(x.Key, bill.BillNumber, x.Sum(y => y.NetAmount), 0)).ToList();
        if (bill.VatTotal > 0)
{
    journalLines.Add(
        new(
            vatReceivable.Id,
            bill.BillNumber,
            bill.VatTotal,
            0));
}

journalLines.Add(
    new(
        accountsPayable.Id,
        bill.BillNumber,
        0,
        bill.Total));
        var journal = await posting.PostAsync(userId, new(request.OrganisationId, request.BillDate, bill.BillNumber, $"Supplier bill {bill.SupplierReference}", journalLines), ct); bill.PostedJournalId = journal.Id;
        foreach (var line in lines.Where(x => x.ProductItemId != null && tracked.ContainsKey(x.ProductItemId.Value))) { var item = tracked[line.ProductItemId!.Value]; item.AverageCost = InventoryValuation.WeightedAverage(item.QuantityOnHand, item.AverageCost, line.Quantity, line.UnitPrice); item.QuantityOnHand += line.Quantity; db.InventoryMovements.Add(new InventoryMovement { OrganisationId = request.OrganisationId, ProductItemId = item.Id, MovementDate = request.BillDate, Type = InventoryMovementType.AdjustmentIncrease, QuantityChange = line.Quantity, UnitCost = line.UnitPrice, ValueChange = InventoryValuation.MovementValue(line.Quantity, line.UnitPrice), Reference = bill.BillNumber, Note = "Automatic stock receipt from supplier bill", PostedJournalId = journal.Id, PostedByUserId = userId }); }
        db.SupplierBills.Add(bill);

        db.AuditEvents.Add(
            Audit(
                request.OrganisationId,
                userId,
                "SupplierBillPosted",
                nameof(SupplierBill),
                bill.Id,
                new
                {
                    bill.BillNumber,
                    bill.Total,
                    bill.VatTotal
                }));

        if (attachment is not null)
        {
            db.SupplierBillAttachments.Add(
                new SupplierBillAttachment
                {
                    OrganisationId =
                        request.OrganisationId,

                    SupplierBillId =
                        bill.Id,

                    FileName =
                        attachment.FileName,

                    ContentType =
                        attachment.ContentType,

                    OriginalSize =
                        attachment.OriginalSize,

                    StoredSize =
                        attachment.Content.LongLength,

                    IsCompressed =
                        attachment.IsCompressed,

                    Content =
                        attachment.Content,

                    UploadedByUserId =
                        userId
                });
        }

        if (draft is not null)
        {
            db.SupplierBillDrafts.Remove(draft);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return bill;
    }

    public async Task<SupplierPayment> PayBillAsync(string userId, SupplierPaymentRequest request, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot pay bills for this organisation.");
        var bill = await db.SupplierBills.Include(x => x.Supplier).SingleOrDefaultAsync(x => x.Id == request.SupplierBillId && x.OrganisationId == request.OrganisationId, ct) ?? throw new InvalidOperationException("Supplier bill not found.");
        if (bill.Status is BillStatus.Voided or BillStatus.Credited)
{
    throw new InvalidOperationException(
        "Only outstanding posted supplier bills can be paid.");
}
        var outstanding = bill.Total - bill.AmountPaid - bill.AmountCredited; if (request.Amount <= 0 || request.Amount > outstanding) throw new InvalidOperationException($"Payment must be between $0.01 and ${outstanding:N2}.");
        var bank = await db.LedgerAccounts.SingleOrDefaultAsync(x => x.Id == request.BankAccountId && x.OrganisationId == request.OrganisationId && x.IsActive && x.IsBankAccount, ct) ?? throw new InvalidOperationException("Select an active bank account.");
        var payable =
    await db.LedgerAccounts.SingleOrDefaultAsync(
        x =>
            x.OrganisationId == request.OrganisationId &&
            x.Code == "2000" &&
            x.IsActive,
        ct);

if (payable is null ||
    payable.Type != AccountType.Liability)
{
    throw new InvalidOperationException(
        "Accounts Payable (2000) must be an active Liability account.");
}
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var journal = await posting.PostAsync(userId, new(request.OrganisationId, request.Date, request.Reference, $"Payment for {bill.BillNumber}", [new(payable.Id, bill.BillNumber, request.Amount, 0), new(bank.Id, bill.BillNumber, 0, request.Amount)]), ct);
        var payment = new SupplierPayment { OrganisationId = request.OrganisationId, SupplierId = bill.SupplierId, SupplierBillId = bill.Id, PaymentDate = request.Date, Reference = request.Reference.Trim(), Amount = request.Amount, BankAccountId = bank.Id, PostedJournalId = journal.Id, CreatedByUserId = userId };
        bill.AmountPaid += request.Amount; bill.Status = bill.AmountPaid + bill.AmountCredited == bill.Total ? BillStatus.Paid : BillStatus.PartPaid;

        if (bill.Status == BillStatus.Paid)
        {
            await notifications.ResolveSupplierBillNotificationsAsync(
                request.OrganisationId,
                bill.Id,
                publishUpdate: false,
                ct: ct);
        } db.SupplierPayments.Add(payment); db.AuditEvents.Add(Audit(request.OrganisationId, userId, "SupplierPaymentRecorded", nameof(SupplierPayment), payment.Id, new { bill.BillNumber, payment.Amount }));
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); notifications.PublishOrganisationUpdate(request.OrganisationId); return payment;
    }

    public async Task<SupplierBill> VoidBillAsync(string userId, Guid organisationId, Guid billId, DateOnly voidDate, string reason, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot void bills for this organisation.");
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Enter a reason for voiding the bill.");
        var bill = await db.SupplierBills.Include(x => x.Lines).ThenInclude(x => x.ProductItem).SingleOrDefaultAsync(x => x.Id == billId && x.OrganisationId == organisationId, ct) ?? throw new InvalidOperationException("Supplier bill not found.");
        if (bill.Status == BillStatus.Voided) throw new InvalidOperationException("This bill has already been voided.");

        var hasPaymentHistory =
    await db.SupplierPayments.AnyAsync(
        x =>
            x.SupplierBillId == bill.Id &&
            x.OrganisationId == organisationId,
        ct);

var hasCreditHistory =
    await db.SupplierCreditNotes.AnyAsync(
        x =>
            x.SupplierBillId == bill.Id &&
            x.OrganisationId == organisationId,
        ct);
        if (bill.AmountPaid > 0 ||
    bill.AmountCredited > 0 ||
    hasPaymentHistory ||
    hasCreditHistory ||
    bill.Status is BillStatus.PartPaid or
        BillStatus.Paid or
        BillStatus.Credited)
{
    throw new InvalidOperationException(
        "A paid or credited bill cannot be voided. Reverse payments first; supplier credits remain permanent audit records.");
}
        var receipts = await db.InventoryMovements.Where(x => x.OrganisationId == organisationId && x.Reference == bill.BillNumber && x.QuantityChange > 0).ToListAsync(ct);
        foreach (var receipt in receipts) { var item = bill.Lines.Select(x => x.ProductItem).First(x => x?.Id == receipt.ProductItemId)!; if (item.QuantityOnHand < receipt.QuantityChange) throw new InvalidOperationException($"Cannot void this bill because {item.Code} no longer has all received units on hand."); var remainingValue = InventoryValuation.MovementValue(item.QuantityOnHand, item.AverageCost) - receipt.ValueChange; if (remainingValue < 0) throw new InvalidOperationException($"Cannot void this bill because it would make the value of {item.Code} negative."); }
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var original = await db.PostedJournals.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == bill.PostedJournalId && x.OrganisationId == organisationId, ct);
        var reversalLines = original.Lines.Select(x => new JournalLineInput(x.LedgerAccountId, $"Void {bill.BillNumber}", x.Credit, x.Debit, x.BranchId, x.DivisionId)).ToList();
        var journal = await posting.PostAsync(userId, new(organisationId, voidDate, $"VOID-{bill.BillNumber}", $"Void supplier bill {bill.SupplierReference}: {reason.Trim()}", reversalLines), ct);
        foreach (var receipt in receipts) { var item = bill.Lines.Select(x => x.ProductItem).First(x => x?.Id == receipt.ProductItemId)!; var oldValue = InventoryValuation.MovementValue(item.QuantityOnHand, item.AverageCost); item.QuantityOnHand -= receipt.QuantityChange; item.AverageCost = item.QuantityOnHand == 0 ? 0 : decimal.Round((oldValue - receipt.ValueChange) / item.QuantityOnHand, 4, MidpointRounding.AwayFromZero); db.InventoryMovements.Add(new InventoryMovement { OrganisationId = organisationId, ProductItemId = item.Id, MovementDate = voidDate, Type = InventoryMovementType.PurchaseReturn, QuantityChange = -receipt.QuantityChange, UnitCost = receipt.UnitCost, ValueChange = -receipt.ValueChange, Reference = $"VOID-{bill.BillNumber}", Note = $"Stock removed by supplier bill void: {reason.Trim()}", PostedJournalId = journal.Id, PostedByUserId = userId }); }
        var billVoid = new SupplierBillVoid
{
    OrganisationId = organisationId,
    SupplierBillId = bill.Id,
    VoidDate = voidDate,
    Reason = reason.Trim(),
    PostedJournalId = journal.Id,
    CreatedByUserId = userId
};

db.SupplierBillVoids.Add(billVoid);
        bill.Status = BillStatus.Voided; db.AuditEvents.Add(Audit(organisationId, userId, "SupplierBillVoided", nameof(SupplierBill), bill.Id, new { bill.BillNumber, reason, ReversalJournalId = journal.Id, StockReturns = receipts.Count })); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return bill;
    }

    public async Task<SupplierPaymentReversal> ReversePaymentAsync(string userId, Guid organisationId, Guid paymentId, DateOnly reversalDate, string reason, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot reverse supplier payments for this organisation.");
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Enter a reason for reversing the payment.");
        var payment = await db.SupplierPayments.Include(x => x.SupplierBill).SingleOrDefaultAsync(x => x.Id == paymentId && x.OrganisationId == organisationId, ct) ?? throw new InvalidOperationException("Supplier payment not found.");
        if (await db.SupplierPaymentReversals.AnyAsync(x => x.SupplierPaymentId == paymentId, ct)) throw new InvalidOperationException("This payment has already been reversed.");
        if (payment.SupplierBill.Status == BillStatus.Voided) throw new InvalidOperationException("A payment on a voided bill cannot be reversed here.");
        var completedReconciliationExists =
    await reconciliation.IsInsideCompletedReconciliationAsync(
        organisationId,
        payment.BankAccountId,
        payment.PaymentDate,
        ct);

if (completedReconciliationExists)
{
    throw new InvalidOperationException(
        "A supplier payment inside a completed bank reconciliation period cannot be reversed.");
}
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var original = await db.PostedJournals.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == payment.PostedJournalId && x.OrganisationId == organisationId, ct);
        var reference = $"REV-{payment.Reference}"; var lines = original.Lines.Select(x => new JournalLineInput(x.LedgerAccountId, $"Reverse payment {payment.Reference}", x.Credit, x.Debit, x.BranchId, x.DivisionId)).ToList();
        var journal = await posting.PostAsync(userId, new(organisationId, reversalDate, reference, $"Reverse supplier payment: {reason.Trim()}", lines), ct);
        var reversal = new SupplierPaymentReversal { OrganisationId = organisationId, SupplierPaymentId = payment.Id, ReversalDate = reversalDate, Reason = reason.Trim(), PostedJournalId = journal.Id, CreatedByUserId = userId };
        payment.SupplierBill.AmountPaid -= payment.Amount; if (payment.SupplierBill.AmountPaid < 0) throw new InvalidOperationException("Payment history is inconsistent and cannot be reversed."); var remaining = payment.SupplierBill.Total - payment.SupplierBill.AmountPaid - payment.SupplierBill.AmountCredited; payment.SupplierBill.Status = remaining <= 0 ? BillStatus.Credited : payment.SupplierBill.AmountPaid > 0 || payment.SupplierBill.AmountCredited > 0 ? BillStatus.PartPaid : BillStatus.Posted;
        db.SupplierPaymentReversals.Add(reversal); db.AuditEvents.Add(Audit(organisationId, userId, "SupplierPaymentReversed", nameof(SupplierPaymentReversal), reversal.Id, new { payment.Reference, payment.Amount, reason, ReversalJournalId = journal.Id })); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); notifications.PublishOrganisationUpdate(organisationId); return reversal;
    }

    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, string entityType, Guid entityId, object data) => new() { OrganisationId = organisationId, UserId = userId, EventType = eventType, EntityType = entityType, EntityId = entityId.ToString(), JsonData = JsonSerializer.Serialize(data) };
}
