using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record IntercompanySourceDocument(
    IntercompanyDocumentType Type,
    Guid Id,
    Guid OrganisationId,
    string OrganisationName,
    DateOnly Date,
    string Reference,
    string Currency,
    decimal Amount);

public sealed record IntercompanyTagView(
    Guid Id,
    Guid OrganisationId,
    string OrganisationName,
    Guid CounterpartyOrganisationId,
    string CounterpartyOrganisationName,
    IntercompanyDocumentType DocumentType,
    Guid SourceDocumentId,
    DateOnly DocumentDate,
    string Reference,
    string Currency,
    decimal Amount,
    bool IsMatched);

public sealed record IntercompanyMatchView(
    Guid Id,
    IntercompanyMatchStatus Status,
    IntercompanyTagView Left,
    IntercompanyTagView Right,
    decimal AmountDifference,
    bool HasCurrencyMismatch,
    Guid? GroupEliminationJournalId)
{
    public bool IsExact => AmountDifference == 0m && !HasCurrencyMismatch;
    public bool CanCreateElimination =>
        Status == IntercompanyMatchStatus.Confirmed &&
        IsExact &&
        GroupEliminationJournalId is null &&
        ((Left.DocumentType == IntercompanyDocumentType.SalesInvoice &&
          Right.DocumentType == IntercompanyDocumentType.SupplierBill) ||
         (Right.DocumentType == IntercompanyDocumentType.SalesInvoice &&
          Left.DocumentType == IntercompanyDocumentType.SupplierBill));
}

public sealed record IntercompanyReconciliationDashboard(
    Guid GroupId,
    string GroupName,
    string Currency,
    bool CanManage,
    IReadOnlyList<GroupSetupCompany> Companies,
    IReadOnlyList<IntercompanySourceDocument> AvailableDocuments,
    IReadOnlyList<IntercompanyTagView> Tags,
    IReadOnlyList<IntercompanyMatchView> Matches)
{
    public int ExceptionCount =>
        Tags.Count(x => !x.IsMatched) +
        Matches.Count(x => x.Status == IntercompanyMatchStatus.Proposed && !x.IsExact);
}

public sealed record TagIntercompanyDocumentRequest(
    Guid CurrentOrganisationId,
    IntercompanyDocumentType DocumentType,
    Guid SourceDocumentId,
    Guid CounterpartyOrganisationId);

public sealed class IntercompanyReconciliationService(
    ApplicationDbContext db,
    GroupEliminationService eliminations)
{
    public async Task<IntercompanyReconciliationDashboard> GetAsync(
        string userId,
        Guid currentOrganisationId,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(userId, currentOrganisationId, false, cancellationToken);
        var tags = await LoadTagsAsync(access.Id, cancellationToken);
        var activeMatchTagPairs = await db.IntercompanyTransactionMatches
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == access.Id &&
                        x.Status != IntercompanyMatchStatus.Rejected)
            .Select(x => new { x.LeftTransactionTagId, x.RightTransactionTagId })
            .ToListAsync(cancellationToken);
        var activeMatchedTagIds = activeMatchTagPairs
            .SelectMany(x => new[] { x.LeftTransactionTagId, x.RightTransactionTagId })
            .ToHashSet();
        var tagViews = tags.Select(x => ToView(x, activeMatchedTagIds.Contains(x.Id))).ToList();
        var tagViewsById = tagViews.ToDictionary(x => x.Id);
        var matches = await db.IntercompanyTransactionMatches
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == access.Id)
            .ToListAsync(cancellationToken);
        matches = matches
            .OrderBy(x => x.Status)
            .ThenByDescending(x => x.ProposedAt)
            .Take(200)
            .ToList();
        var matchViews = matches
            .Where(x => tagViewsById.ContainsKey(x.LeftTransactionTagId) &&
                        tagViewsById.ContainsKey(x.RightTransactionTagId))
            .Select(x => new IntercompanyMatchView(
                x.Id,
                x.Status,
                tagViewsById[x.LeftTransactionTagId],
                tagViewsById[x.RightTransactionTagId],
                x.AmountDifference,
                x.HasCurrencyMismatch,
                x.GroupEliminationJournalId))
            .ToList();
        var documents = access.CanManage
            ? await LoadAvailableDocumentsAsync(access, tags, cancellationToken)
            : [];
        return new(
            access.Id,
            access.Name,
            access.PresentationCurrency,
            access.CanManage,
            access.Companies,
            documents,
            tagViews,
            matchViews);
    }

    public async Task TagAsync(
        string userId,
        TagIntercompanyDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(userId, request.CurrentOrganisationId, true, cancellationToken);
        if (!access.CompanyIds.Contains(request.CounterpartyOrganisationId))
        {
            throw new InvalidOperationException("Select a counterparty company in this organisation group.");
        }

        if (await db.IntercompanyTransactionTags.AnyAsync(
                x => x.DocumentType == request.DocumentType &&
                     x.SourceDocumentId == request.SourceDocumentId,
                cancellationToken))
        {
            throw new InvalidOperationException("That source document is already tagged as intercompany.");
        }

        var source = await LoadSourceAsync(
            request.DocumentType,
            request.SourceDocumentId,
            access.CompanyIds,
            cancellationToken);
        if (source.OrganisationId == request.CounterpartyOrganisationId)
        {
            throw new InvalidOperationException("The source company and counterparty must be different.");
        }

        var tag = new IntercompanyTransactionTag
        {
            OrganisationGroupId = access.Id,
            OrganisationId = source.OrganisationId,
            CounterpartyOrganisationId = request.CounterpartyOrganisationId,
            DocumentType = request.DocumentType,
            SourceDocumentId = request.SourceDocumentId,
            DocumentDate = source.Date,
            Reference = source.Reference,
            Currency = source.Currency,
            Amount = source.Amount,
            CreatedByUserId = userId
        };
        db.IntercompanyTransactionTags.Add(tag);
        db.AuditEvents.Add(Audit(
            request.CurrentOrganisationId,
            userId,
            "IntercompanyDocumentTagged",
            nameof(IntercompanyTransactionTag),
            tag.Id,
            new
            {
                access.Id,
                tag.OrganisationId,
                tag.CounterpartyOrganisationId,
                DocumentType = tag.DocumentType.ToString(),
                tag.SourceDocumentId,
                tag.Reference,
                tag.Currency,
                tag.Amount
            }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RefreshSuggestionsAsync(
        string userId,
        Guid currentOrganisationId,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(userId, currentOrganisationId, true, cancellationToken);
        var tags = await db.IntercompanyTransactionTags
            .Where(x => x.OrganisationGroupId == access.Id)
            .OrderBy(x => x.DocumentDate)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var existing = await db.IntercompanyTransactionMatches
            .Where(x => x.OrganisationGroupId == access.Id)
            .ToListAsync(cancellationToken);
        var unavailable = existing
            .Where(x => x.Status != IntercompanyMatchStatus.Rejected)
            .SelectMany(x => new[] { x.LeftTransactionTagId, x.RightTransactionTagId })
            .ToHashSet();
        var created = 0;
        foreach (var left in tags.Where(x => !unavailable.Contains(x.Id)))
        {
            if (unavailable.Contains(left.Id)) continue;
            var right = tags
                .Where(x =>
                    x.Id != left.Id &&
                    !unavailable.Contains(x.Id) &&
                    x.OrganisationId == left.CounterpartyOrganisationId &&
                    x.CounterpartyOrganisationId == left.OrganisationId &&
                    AreCompatible(left.DocumentType, x.DocumentType) &&
                    Math.Abs(x.DocumentDate.DayNumber - left.DocumentDate.DayNumber) <= 45 &&
                    !existing.Any(match => SamePair(match, left.Id, x.Id)))
                .OrderBy(x => x.Currency == left.Currency ? 0 : 1)
                .ThenBy(x => Math.Abs(x.Amount - left.Amount))
                .ThenBy(x => Math.Abs(x.DocumentDate.DayNumber - left.DocumentDate.DayNumber))
                .FirstOrDefault();
            if (right is null) continue;

            var first = left.Id.CompareTo(right.Id) < 0 ? left : right;
            var second = first.Id == left.Id ? right : left;
            var match = new IntercompanyTransactionMatch
            {
                OrganisationGroupId = access.Id,
                LeftTransactionTagId = first.Id,
                RightTransactionTagId = second.Id,
                AmountDifference = decimal.Round(
                    Math.Abs(first.Amount - second.Amount),
                    2,
                    MidpointRounding.AwayFromZero),
                HasCurrencyMismatch = first.Currency != second.Currency,
                ProposedByUserId = userId
            };
            db.IntercompanyTransactionMatches.Add(match);
            existing.Add(match);
            unavailable.Add(left.Id);
            unavailable.Add(right.Id);
            created++;
        }

        if (created == 0) return 0;
        db.AuditEvents.Add(Audit(
            currentOrganisationId,
            userId,
            "IntercompanyMatchSuggestionsRefreshed",
            nameof(OrganisationGroup),
            access.Id,
            new { MatchCount = created }));
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task ReviewMatchAsync(
        string userId,
        Guid currentOrganisationId,
        Guid matchId,
        bool confirm,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(userId, currentOrganisationId, true, cancellationToken);
        var match = await db.IntercompanyTransactionMatches.SingleOrDefaultAsync(
            x => x.Id == matchId && x.OrganisationGroupId == access.Id,
            cancellationToken)
            ?? throw new InvalidOperationException("The intercompany match was not found in this group.");
        if (match.GroupEliminationJournalId is not null)
        {
            throw new InvalidOperationException("A posted elimination match cannot be changed.");
        }

        if (confirm && (match.AmountDifference != 0m || match.HasCurrencyMismatch))
        {
            throw new InvalidOperationException("Resolve the amount or currency difference before confirming this match.");
        }

        match.Status = confirm
            ? IntercompanyMatchStatus.Confirmed
            : IntercompanyMatchStatus.Rejected;
        match.ReviewedAt = DateTimeOffset.UtcNow;
        match.ReviewedByUserId = userId;
        db.AuditEvents.Add(Audit(
            currentOrganisationId,
            userId,
            confirm ? "IntercompanyMatchConfirmed" : "IntercompanyMatchRejected",
            nameof(IntercompanyTransactionMatch),
            match.Id,
            new { access.Id, Status = match.Status.ToString() }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveTagAsync(
        string userId,
        Guid currentOrganisationId,
        Guid tagId,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(userId, currentOrganisationId, true, cancellationToken);
        var tag = await db.IntercompanyTransactionTags.SingleOrDefaultAsync(
            x => x.Id == tagId && x.OrganisationGroupId == access.Id,
            cancellationToken)
            ?? throw new InvalidOperationException("The intercompany tag was not found in this group.");
        var matches = await db.IntercompanyTransactionMatches
            .Where(x => x.LeftTransactionTagId == tag.Id || x.RightTransactionTagId == tag.Id)
            .ToListAsync(cancellationToken);
        if (matches.Any(x => x.Status != IntercompanyMatchStatus.Rejected ||
                             x.GroupEliminationJournalId is not null))
        {
            throw new InvalidOperationException("Reject the active match before removing this tag.");
        }

        db.IntercompanyTransactionMatches.RemoveRange(matches);
        db.IntercompanyTransactionTags.Remove(tag);
        db.AuditEvents.Add(Audit(
            currentOrganisationId,
            userId,
            "IntercompanyDocumentTagRemoved",
            nameof(IntercompanyTransactionTag),
            tag.Id,
            new { access.Id, tag.SourceDocumentId, DocumentType = tag.DocumentType.ToString() }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GroupEliminationJournal> PostEliminationAsync(
        string userId,
        Guid currentOrganisationId,
        Guid matchId,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(userId, currentOrganisationId, true, cancellationToken);
        var match = await db.IntercompanyTransactionMatches
            .Include(x => x.LeftTransactionTag)
            .Include(x => x.RightTransactionTag)
            .SingleOrDefaultAsync(
                x => x.Id == matchId && x.OrganisationGroupId == access.Id,
                cancellationToken)
            ?? throw new InvalidOperationException("The intercompany match was not found in this group.");
        if (match.Status != IntercompanyMatchStatus.Confirmed ||
            match.AmountDifference != 0m ||
            match.HasCurrencyMismatch)
        {
            throw new InvalidOperationException("Only a confirmed exact match can create an elimination.");
        }

        if (match.GroupEliminationJournalId is not null)
        {
            throw new InvalidOperationException("This match already has a posted elimination.");
        }

        var invoice = match.LeftTransactionTag.DocumentType == IntercompanyDocumentType.SalesInvoice
            ? match.LeftTransactionTag
            : match.RightTransactionTag.DocumentType == IntercompanyDocumentType.SalesInvoice
                ? match.RightTransactionTag
                : null;
        var bill = match.LeftTransactionTag.DocumentType == IntercompanyDocumentType.SupplierBill
            ? match.LeftTransactionTag
            : match.RightTransactionTag.DocumentType == IntercompanyDocumentType.SupplierBill
                ? match.RightTransactionTag
                : null;
        if (invoice is null || bill is null)
        {
            throw new InvalidOperationException("Automatic eliminations are available for matched sales invoices and supplier bills.");
        }

        if (invoice.Currency != access.PresentationCurrency)
        {
            throw new InvalidOperationException(
                $"Translate the match to {access.PresentationCurrency} before posting its elimination.");
        }

        var configurations = await db.IntercompanyAccountConfigurations
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == access.Id &&
                        ((x.OrganisationId == invoice.OrganisationId &&
                          x.CounterpartyOrganisationId == bill.OrganisationId) ||
                         (x.OrganisationId == bill.OrganisationId &&
                          x.CounterpartyOrganisationId == invoice.OrganisationId)))
            .ToListAsync(cancellationToken);
        var seller = configurations.SingleOrDefault(x => x.OrganisationId == invoice.OrganisationId)
            ?? throw new InvalidOperationException("Configure the seller's intercompany accounts before posting an elimination.");
        var buyer = configurations.SingleOrDefault(x => x.OrganisationId == bill.OrganisationId)
            ?? throw new InvalidOperationException("Configure the buyer's intercompany accounts before posting an elimination.");

        var accountIds = new[]
        {
            seller.RevenueAccountId,
            seller.ReceivableAccountId,
            buyer.ExpenseAccountId,
            buyer.PayableAccountId
        };
        var accounts = await ResolveEliminationAccountsAsync(access.Id, accountIds, cancellationToken);
        var amount = invoice.Amount;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var journal = await eliminations.PostAsync(
            userId,
            new(
                currentOrganisationId,
                invoice.DocumentDate >= bill.DocumentDate ? invoice.DocumentDate : bill.DocumentDate,
                $"IC-{match.Id:N}",
                $"Matched {invoice.Reference} to {bill.Reference}",
                [
                    Line(accounts[seller.RevenueAccountId], "Eliminate intercompany revenue", debit: amount),
                    Line(accounts[buyer.ExpenseAccountId], "Eliminate intercompany expense", credit: amount),
                    Line(accounts[buyer.PayableAccountId], "Eliminate intercompany payable", debit: amount),
                    Line(accounts[seller.ReceivableAccountId], "Eliminate intercompany receivable", credit: amount)
                ]),
            cancellationToken);
        match.GroupEliminationJournalId = journal.Id;
        db.AuditEvents.Add(Audit(
            currentOrganisationId,
            userId,
            "IntercompanyEliminationPosted",
            nameof(IntercompanyTransactionMatch),
            match.Id,
            new { access.Id, GroupEliminationJournalId = journal.Id, amount, access.PresentationCurrency }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return journal;
    }

    private async Task<IReadOnlyList<IntercompanySourceDocument>> LoadAvailableDocumentsAsync(
        GroupAccess access,
        IReadOnlyList<IntercompanyTransactionTag> tags,
        CancellationToken cancellationToken)
    {
        var tagged = tags.Select(x => (x.DocumentType, x.SourceDocumentId)).ToHashSet();
        var documents = new List<IntercompanySourceDocument>();
        var invoices = await db.SalesInvoices.AsNoTracking()
            .Where(x => access.CompanyIds.Contains(x.OrganisationId) &&
                        x.Status != InvoiceStatus.Draft && x.Status != InvoiceStatus.Voided)
            .Select(x => new SourceSnapshot(
                x.OrganisationId, x.Organisation.LegalName, x.Id, x.IssueDate,
                x.InvoiceNumber, x.Currency,
                x.TransactionSubtotal != 0m ? x.TransactionSubtotal : x.Subtotal))
            .ToListAsync(cancellationToken);
        AddDocuments(documents, invoices, IntercompanyDocumentType.SalesInvoice, tagged);
        var bills = await db.SupplierBills.AsNoTracking()
            .Where(x => access.CompanyIds.Contains(x.OrganisationId) && x.Status != BillStatus.Voided)
            .Select(x => new SourceSnapshot(
                x.OrganisationId, x.Organisation.LegalName, x.Id, x.BillDate,
                x.SupplierReference, x.Currency,
                x.TransactionSubtotal != 0m ? x.TransactionSubtotal : x.Subtotal))
            .ToListAsync(cancellationToken);
        AddDocuments(documents, bills, IntercompanyDocumentType.SupplierBill, tagged);
        var receipts = await db.CustomerReceipts.AsNoTracking()
            .Where(x => access.CompanyIds.Contains(x.OrganisationId))
            .Select(x => new SourceSnapshot(
                x.OrganisationId, x.Organisation.LegalName, x.Id, x.ReceiptDate,
                x.Reference, x.Currency,
                x.TransactionAmount != 0m ? x.TransactionAmount : x.Amount))
            .ToListAsync(cancellationToken);
        AddDocuments(documents, receipts, IntercompanyDocumentType.CustomerReceipt, tagged);
        var payments = await db.SupplierPayments.AsNoTracking()
            .Where(x => access.CompanyIds.Contains(x.OrganisationId))
            .Select(x => new SourceSnapshot(
                x.OrganisationId, x.Organisation.LegalName, x.Id, x.PaymentDate,
                x.Reference, x.Currency,
                x.TransactionAmount != 0m ? x.TransactionAmount : x.Amount))
            .ToListAsync(cancellationToken);
        AddDocuments(documents, payments, IntercompanyDocumentType.SupplierPayment, tagged);
        var generatedJournalIds = new HashSet<Guid>();
        generatedJournalIds.UnionWith(await db.SalesInvoices.AsNoTracking()
            .Where(x => access.CompanyIds.Contains(x.OrganisationId) && x.PostedJournalId != null)
            .Select(x => x.PostedJournalId!.Value)
            .ToListAsync(cancellationToken));
        generatedJournalIds.UnionWith(await db.SupplierBills.AsNoTracking()
            .Where(x => access.CompanyIds.Contains(x.OrganisationId))
            .Select(x => x.PostedJournalId)
            .ToListAsync(cancellationToken));
        generatedJournalIds.UnionWith(await db.CustomerReceipts.AsNoTracking()
            .Where(x => access.CompanyIds.Contains(x.OrganisationId))
            .Select(x => x.PostedJournalId)
            .ToListAsync(cancellationToken));
        generatedJournalIds.UnionWith(await db.SupplierPayments.AsNoTracking()
            .Where(x => access.CompanyIds.Contains(x.OrganisationId))
            .Select(x => x.PostedJournalId)
            .ToListAsync(cancellationToken));
        var journals = await db.PostedJournals.AsNoTracking()
            .Where(x => access.CompanyIds.Contains(x.OrganisationId) &&
                        !generatedJournalIds.Contains(x.Id))
            .Select(x => new SourceSnapshot(
                x.OrganisationId, x.Organisation.LegalName, x.Id, x.EntryDate,
                x.Reference, x.Currency, x.Lines.Sum(line => line.Debit)))
            .ToListAsync(cancellationToken);
        AddDocuments(documents, journals, IntercompanyDocumentType.Journal, tagged);
        return documents
            .Where(x => x.Amount > 0m)
            .OrderByDescending(x => x.Date)
            .ThenBy(x => x.OrganisationName)
            .ThenBy(x => x.Reference)
            .Take(500)
            .ToList();
    }

    private async Task<SourceSnapshot> LoadSourceAsync(
        IntercompanyDocumentType type,
        Guid id,
        IReadOnlyList<Guid> companyIds,
        CancellationToken cancellationToken)
    {
        SourceSnapshot? source = type switch
        {
            IntercompanyDocumentType.SalesInvoice => await db.SalesInvoices.AsNoTracking()
                .Where(x => x.Id == id && companyIds.Contains(x.OrganisationId) &&
                            x.Status != InvoiceStatus.Draft && x.Status != InvoiceStatus.Voided)
                .Select(x => new SourceSnapshot(
                    x.OrganisationId, x.Organisation.LegalName, x.Id, x.IssueDate,
                    x.InvoiceNumber, x.Currency,
                    x.TransactionSubtotal != 0m ? x.TransactionSubtotal : x.Subtotal))
                .SingleOrDefaultAsync(cancellationToken),
            IntercompanyDocumentType.SupplierBill => await db.SupplierBills.AsNoTracking()
                .Where(x => x.Id == id && companyIds.Contains(x.OrganisationId) && x.Status != BillStatus.Voided)
                .Select(x => new SourceSnapshot(
                    x.OrganisationId, x.Organisation.LegalName, x.Id, x.BillDate,
                    x.SupplierReference, x.Currency,
                    x.TransactionSubtotal != 0m ? x.TransactionSubtotal : x.Subtotal))
                .SingleOrDefaultAsync(cancellationToken),
            IntercompanyDocumentType.CustomerReceipt => await db.CustomerReceipts.AsNoTracking()
                .Where(x => x.Id == id && companyIds.Contains(x.OrganisationId))
                .Select(x => new SourceSnapshot(
                    x.OrganisationId, x.Organisation.LegalName, x.Id, x.ReceiptDate,
                    x.Reference, x.Currency,
                    x.TransactionAmount != 0m ? x.TransactionAmount : x.Amount))
                .SingleOrDefaultAsync(cancellationToken),
            IntercompanyDocumentType.SupplierPayment => await db.SupplierPayments.AsNoTracking()
                .Where(x => x.Id == id && companyIds.Contains(x.OrganisationId))
                .Select(x => new SourceSnapshot(
                    x.OrganisationId, x.Organisation.LegalName, x.Id, x.PaymentDate,
                    x.Reference, x.Currency,
                    x.TransactionAmount != 0m ? x.TransactionAmount : x.Amount))
                .SingleOrDefaultAsync(cancellationToken),
            IntercompanyDocumentType.Journal => await db.PostedJournals.AsNoTracking()
                .Where(x => x.Id == id && companyIds.Contains(x.OrganisationId))
                .Select(x => new SourceSnapshot(
                    x.OrganisationId, x.Organisation.LegalName, x.Id, x.EntryDate,
                    x.Reference, x.Currency, x.Lines.Sum(line => line.Debit)))
                .SingleOrDefaultAsync(cancellationToken),
            _ => null
        };
        if (source is null || source.Amount <= 0m)
        {
            throw new InvalidOperationException("Select an eligible posted source document in this organisation group.");
        }

        return source;
    }

    private async Task<Dictionary<Guid, EliminationAccount>> ResolveEliminationAccountsAsync(
        Guid groupId,
        IReadOnlyList<Guid> ledgerAccountIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.LedgerAccounts.AsNoTracking()
            .Where(x => ledgerAccountIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.Type,
                Mapping = db.GroupLedgerAccountMappings
                    .Where(mapping => mapping.OrganisationGroupId == groupId &&
                                      mapping.LedgerAccountId == x.Id &&
                                      mapping.GroupLedgerAccount.IsActive)
                    .Select(mapping => new
                    {
                        mapping.GroupLedgerAccount.Code,
                        mapping.GroupLedgerAccount.Name,
                        mapping.GroupLedgerAccount.Type
                    })
                    .SingleOrDefault()
            })
            .ToListAsync(cancellationToken);
        if (rows.Count != ledgerAccountIds.Distinct().Count())
        {
            throw new InvalidOperationException("An intercompany account is no longer available.");
        }

        return rows.ToDictionary(
            x => x.Id,
            x => x.Mapping is null
                ? new EliminationAccount(x.Code, x.Name, x.Type)
                : new EliminationAccount(x.Mapping.Code, x.Mapping.Name, x.Mapping.Type));
    }

    private async Task<List<IntercompanyTransactionTag>> LoadTagsAsync(
        Guid groupId,
        CancellationToken cancellationToken) =>
        await db.IntercompanyTransactionTags.AsNoTracking()
            .Include(x => x.Organisation)
            .Include(x => x.CounterpartyOrganisation)
            .Where(x => x.OrganisationGroupId == groupId)
            .OrderByDescending(x => x.DocumentDate)
            .ThenBy(x => x.Reference)
            .ToListAsync(cancellationToken);

    private static IntercompanyTagView ToView(IntercompanyTransactionTag x, bool isMatched) =>
        new(
            x.Id,
            x.OrganisationId,
            x.Organisation.LegalName,
            x.CounterpartyOrganisationId,
            x.CounterpartyOrganisation.LegalName,
            x.DocumentType,
            x.SourceDocumentId,
            x.DocumentDate,
            x.Reference,
            x.Currency,
            x.Amount,
            isMatched);

    private static void AddDocuments(
        ICollection<IntercompanySourceDocument> destination,
        IEnumerable<SourceSnapshot> sources,
        IntercompanyDocumentType type,
        IReadOnlySet<(IntercompanyDocumentType Type, Guid Id)> tagged)
    {
        foreach (var source in sources.Where(x => !tagged.Contains((type, x.Id))))
        {
            destination.Add(new(
                type,
                source.Id,
                source.OrganisationId,
                source.OrganisationName,
                source.Date,
                source.Reference,
                source.Currency,
                source.Amount));
        }
    }

    private static bool AreCompatible(IntercompanyDocumentType left, IntercompanyDocumentType right) =>
        (left == IntercompanyDocumentType.SalesInvoice && right == IntercompanyDocumentType.SupplierBill) ||
        (left == IntercompanyDocumentType.SupplierBill && right == IntercompanyDocumentType.SalesInvoice) ||
        (left == IntercompanyDocumentType.CustomerReceipt && right == IntercompanyDocumentType.SupplierPayment) ||
        (left == IntercompanyDocumentType.SupplierPayment && right == IntercompanyDocumentType.CustomerReceipt) ||
        (left == IntercompanyDocumentType.Journal && right == IntercompanyDocumentType.Journal);

    private static bool SamePair(IntercompanyTransactionMatch match, Guid leftId, Guid rightId) =>
        (match.LeftTransactionTagId == leftId && match.RightTransactionTagId == rightId) ||
        (match.LeftTransactionTagId == rightId && match.RightTransactionTagId == leftId);

    private static GroupEliminationLineInput Line(
        EliminationAccount account,
        string description,
        decimal debit = 0m,
        decimal credit = 0m) =>
        new(account.Code, account.Name, account.Type, description, debit, credit);

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

    private async Task<GroupAccess> RequireGroupAsync(
        string userId,
        Guid currentOrganisationId,
        bool requireManager,
        CancellationToken cancellationToken)
    {
        var group = await db.OrganisationGroups.AsNoTracking()
            .Where(x => x.Companies.Any(company => company.Id == currentOrganisationId))
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.PresentationCurrency,
                Companies = x.Companies.OrderBy(company => company.LegalName)
                    .Select(company => new GroupSetupCompany(
                        company.Id,
                        company.LegalName,
                        company.BaseCurrency))
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("This organisation does not belong to an organisation group.");
        var role = await db.OrganisationGroupMemberships.AsNoTracking()
            .Where(x => x.OrganisationGroupId == group.Id && x.UserId == userId)
            .Select(x => (OrganisationGroupRole?)x.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (role is not null)
        {
            if (requireManager && role == OrganisationGroupRole.Viewer)
            {
                throw new UnauthorizedAccessException("You do not have permission to manage intercompany reconciliation.");
            }

            return new(group.Id, group.Name, group.PresentationCurrency, group.Companies, role != OrganisationGroupRole.Viewer);
        }

        var managedIds = await db.OrganisationMemberships.AsNoTracking()
            .Where(x => x.UserId == userId &&
                        x.Organisation.OrganisationGroupId == group.Id &&
                        (x.Role == OrganisationRole.Owner || x.Role == OrganisationRole.Administrator))
            .Select(x => x.OrganisationId)
            .ToListAsync(cancellationToken);
        if (group.Companies.Any(x => !managedIds.Contains(x.Id)))
        {
            throw new UnauthorizedAccessException("You do not have access to this organisation group.");
        }

        return new(group.Id, group.Name, group.PresentationCurrency, group.Companies, true);
    }

    private sealed record SourceSnapshot(
        Guid OrganisationId,
        string OrganisationName,
        Guid Id,
        DateOnly Date,
        string Reference,
        string Currency,
        decimal Amount);

    private sealed record EliminationAccount(string Code, string Name, AccountType Type);

    private sealed record GroupAccess(
        Guid Id,
        string Name,
        string PresentationCurrency,
        IReadOnlyList<GroupSetupCompany> Companies,
        bool CanManage)
    {
        public IReadOnlyList<Guid> CompanyIds => Companies.Select(x => x.Id).ToList();
    }
}
