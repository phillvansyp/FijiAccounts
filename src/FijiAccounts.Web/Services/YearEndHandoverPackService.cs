using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record YearEndHandoverPack(
    byte[] Content,
    string FileName,
    Guid SnapshotId,
    int Version,
    string Sha256);

public sealed class YearEndHandoverPackService(
    ApplicationDbContext db,
    TenantAccessService access,
    FinancialReportService financialReports,
    VatWorkpaperService vatWorkpapers,
    IImmutableDocumentStore storage)
{
    public async Task<YearEndHandoverPack> CreateAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "Only owners and administrators can export a year-end handover pack.");
        }

        var organisation = await db.Organisations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Organisation not found.");
        var period = await db.AccountingPeriods.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == periodId && x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Accounting period not found.");
        if (!period.IsLocked)
        {
            throw new InvalidOperationException(
                "Lock the accounting period before exporting its final handover pack.");
        }

        var report = await financialReports.GetAsync(
            organisationId,
            period.StartsOn,
            period.EndsOn,
            cancellationToken);
        var vat = await vatWorkpapers.GetAsync(
            organisationId,
            period.StartsOn,
            period.EndsOn,
            cancellationToken);

        var ledger = await db.PostedJournalLines.AsNoTracking()
            .Where(x =>
                x.PostedJournal.OrganisationId == organisationId &&
                x.PostedJournal.EntryDate >= period.StartsOn &&
                x.PostedJournal.EntryDate <= period.EndsOn)
            .OrderBy(x => x.PostedJournal.EntryDate)
            .ThenBy(x => x.PostedJournal.SequenceNumber)
            .ThenBy(x => x.LedgerAccount.Code)
            .Select(x => new
            {
                x.PostedJournal.EntryDate,
                x.PostedJournal.SequenceNumber,
                x.PostedJournal.Reference,
                AccountCode = x.LedgerAccount.Code,
                AccountName = x.LedgerAccount.Name,
                x.Description,
                x.Debit,
                x.Credit,
                BranchCode = x.Branch != null ? x.Branch.Code : "",
                DivisionCode = x.Division != null ? x.Division.Code : ""
            })
            .ToListAsync(cancellationToken);

        var reconciliations = await db.BankReconciliationSessions.AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.StatementStartDate <= period.EndsOn &&
                x.StatementEndDate >= period.StartsOn)
            .OrderBy(x => x.StatementEndDate)
            .ThenBy(x => x.BankAccount.Code)
            .Select(x => new
            {
                AccountCode = x.BankAccount.Code,
                AccountName = x.BankAccount.Name,
                x.StatementStartDate,
                x.StatementEndDate,
                x.OpeningStatementBalance,
                x.ClosingStatementBalance,
                x.LedgerBalance,
                x.Difference,
                x.IsCompleted,
                x.CompletedAt,
                x.CompletedByUserId
            })
            .ToListAsync(cancellationToken);

        var periodAudit =
            (await db.AuditEvents.AsNoTracking()
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.EntityType == nameof(AccountingPeriod) &&
                    x.EntityId == period.Id.ToString())
                .Select(x => new
                {
                    x.OccurredAt,
                    x.EventType,
                    x.UserId,
                    x.JsonData
                })
                .ToListAsync(cancellationToken))
            .OrderBy(x => x.OccurredAt)
            .ToList();

        var adjustments = await db.PostedJournals.AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.Purpose == JournalPurpose.YearEndAdjustment &&
                x.AdjustmentPeriodId == period.Id)
            .OrderBy(x => x.EntryDate)
            .ThenBy(x => x.SequenceNumber)
            .Select(x => new
            {
                x.EntryDate,
                x.SequenceNumber,
                x.Reference,
                x.Description,
                x.ApprovalReference,
                x.PostedAt,
                x.PostedByUserId,
                Amount = x.Lines.Sum(line => line.Debit)
            })
            .ToListAsync(cancellationToken);
        var yearEndReview = await db.YearEndReviews.AsNoTracking()
            .Include(x => x.Items)
            .ThenInclude(x => x.Attachments)
            .SingleOrDefaultAsync(
                x => x.OrganisationId == organisationId &&
                     x.AccountingPeriodId == period.Id,
                cancellationToken);

        var receivables = await GetReceivablesAsync(
            organisationId, period.EndsOn, cancellationToken);
        var payables = await GetPayablesAsync(
            organisationId, period.EndsOn, cancellationToken);
        var fixedAssets = await GetFixedAssetsAsync(
            organisationId, period.EndsOn, cancellationToken);
        var inventory = await GetInventoryAsync(
            organisationId, period.EndsOn, cancellationToken);

        var files = new List<PackFile>
        {
            Csv(
                "trial-balance.csv",
                "Account Code,Account Name,Debit,Credit",
                report.TrialBalance.Select(x => Row(x.Code, x.Name, x.Debit, x.Credit))),
            Csv(
                "profit-and-loss.csv",
                "Account Code,Account Name,Type,Amount",
                report.Balances
                    .Where(x => x.Type is AccountType.Revenue or AccountType.Expense)
                    .Select(x => Row(x.Code, x.Name, x.Type, x.DisplayAmount))),
            Csv(
                "balance-sheet.csv",
                "Account Code,Account Name,Type,Amount",
                report.Balances
                    .Where(x => x.Type is AccountType.Asset or AccountType.Liability or AccountType.Equity)
                    .Select(x => Row(x.Code, x.Name, x.Type, x.DisplayAmount))),
            Csv(
                "general-ledger.csv",
                "Date,Journal,Reference,Account Code,Account Name,Description,Debit,Credit,Branch,Division",
                ledger.Select(x => Row(
                    x.EntryDate,
                    $"J-{x.SequenceNumber:D6}",
                    x.Reference,
                    x.AccountCode,
                    x.AccountName,
                    x.Description,
                    x.Debit,
                    x.Credit,
                    x.BranchCode,
                    x.DivisionCode))),
            Csv(
                "year-end-adjustments.csv",
                "Date,Journal,Reference,Description,Approval Reference,Amount,Posted At,Posted By",
                adjustments.Select(x => Row(
                    x.EntryDate,
                    $"J-{x.SequenceNumber:D6}",
                    x.Reference,
                    x.Description,
                    x.ApprovalReference,
                    x.Amount,
                    x.PostedAt,
                    x.PostedByUserId))),
            Csv(
                "year-end-review.csv",
                "Area,Status,Notes,Query Assigned To,Query Due Date,Query Raised At,Query Raised By,Query Response,Query Responded At,Query Responded By,Query Resolved At,Query Resolved By,Attachment Count,Attachment Files,Reviewed At,Reviewed By,Final Approval Reference,Final Approved At,Final Approved By",
                yearEndReview?.Items
                    .OrderBy(x => x.Area)
                    .Select(x => Row(
                        ReviewAreaLabel(x.Area),
                        x.Status,
                        x.Notes,
                        x.QueryAssignedToUserId,
                        x.QueryDueDate,
                        x.QueryRaisedAt,
                        x.QueryRaisedByUserId,
                        x.QueryResponse,
                        x.QueryRespondedAt,
                        x.QueryRespondedByUserId,
                        x.QueryResolvedAt,
                        x.QueryResolvedByUserId,
                        x.Attachments.Count,
                        string.Join("; ", x.Attachments
                            .OrderBy(a => a.UploadedAt)
                            .Select(a => AttachmentPackPath(x.Area, a))),
                        x.ReviewedAt,
                        x.ReviewedByUserId,
                        yearEndReview.ApprovalReference,
                        yearEndReview.ApprovedAt,
                        yearEndReview.ApprovedByUserId)) ?? []),
            Csv(
                "vat-workpaper.csv",
                "Section,Standard Net,Tax,Zero Rated Net,Exempt Net,Out Of Scope Net",
                [
                    Row("Sales", vat.Sales.StandardNet, vat.Sales.StandardTax, vat.Sales.ZeroRatedNet, vat.Sales.ExemptNet, vat.Sales.OutOfScopeNet),
                    Row("Sales credits", vat.SalesCredits.Net, vat.SalesCredits.Tax, 0m, 0m, 0m),
                    Row("Purchases", vat.Purchases.StandardNet, vat.Purchases.StandardTax, vat.Purchases.ZeroRatedNet, vat.Purchases.ExemptNet, vat.Purchases.OutOfScopeNet),
                    Row("Supplier credits", vat.SupplierCredits.Net, vat.SupplierCredits.Tax, 0m, 0m, 0m),
                    Row("Net tax", 0m, vat.NetTax, 0m, 0m, 0m)
                ]),
            Csv(
                "bank-reconciliations.csv",
                "Account Code,Account Name,Statement Start,Statement End,Opening Balance,Closing Balance,Ledger Balance,Difference,Completed,Completed At,Completed By",
                reconciliations.Select(x => Row(
                    x.AccountCode,
                    x.AccountName,
                    x.StatementStartDate,
                    x.StatementEndDate,
                    x.OpeningStatementBalance,
                    x.ClosingStatementBalance,
                    x.LedgerBalance,
                    x.Difference,
                    x.IsCompleted,
                    x.CompletedAt,
                    x.CompletedByUserId))),
            Csv(
                "period-control-audit.csv",
                "Occurred At,Event,User,Evidence JSON",
                periodAudit.Select(x => Row(x.OccurredAt, x.EventType, x.UserId, x.JsonData))),
            Csv(
                "aged-receivables.csv",
                "Customer,Invoice,Issue Date,Due Date,Original Amount,Paid Through Cutoff,Credits Through Cutoff,Outstanding,Age Bucket",
                receivables.Select(x => Row(
                    x.Contact,
                    x.DocumentNumber,
                    x.DocumentDate,
                    x.DueDate,
                    x.OriginalAmount,
                    x.SettledAmount,
                    x.CreditedAmount,
                    x.OutstandingAmount,
                    x.AgeBucket))),
            Csv(
                "aged-payables.csv",
                "Supplier,Bill,Issue Date,Due Date,Original Amount,Paid Through Cutoff,Credits Through Cutoff,Outstanding,Age Bucket",
                payables.Select(x => Row(
                    x.Contact,
                    x.DocumentNumber,
                    x.DocumentDate,
                    x.DueDate,
                    x.OriginalAmount,
                    x.SettledAmount,
                    x.CreditedAmount,
                    x.OutstandingAmount,
                    x.AgeBucket))),
            Csv(
                "fixed-assets.csv",
                "Asset Number,Name,Acquisition Date,Cost,Residual Value,Useful Life Months,Accumulated Depreciation,Carrying Value,Status,Disposal Date,Proceeds,Gain Loss",
                fixedAssets.Select(x => Row(
                    x.AssetNumber,
                    x.Name,
                    x.AcquisitionDate,
                    x.Cost,
                    x.ResidualValue,
                    x.UsefulLifeMonths,
                    x.AccumulatedDepreciation,
                    x.CarryingValue,
                    x.Status,
                    x.DisposalDate,
                    x.Proceeds,
                    x.GainLoss))),
            Csv(
                "inventory-valuation.csv",
                "Item Code,Item Name,Quantity On Hand,Average Unit Cost,Carrying Value",
                inventory.Select(x => Row(
                    x.Code,
                    x.Name,
                    x.Quantity,
                    x.AverageUnitCost,
                    x.Value)))
        };

        if (yearEndReview is not null)
        {
            foreach (var item in yearEndReview.Items.OrderBy(x => x.Area))
            {
                foreach (var attachment in item.Attachments.OrderBy(x => x.UploadedAt))
                {
                    var stored = await storage.ReadVerifiedAsync(
                        organisationId,
                        attachment.ImmutableDocumentObjectId,
                        cancellationToken);
                    var original = YearEndReviewAttachmentService.RestoreAndValidate(
                        stored,
                        attachment.IsCompressed,
                        attachment.OriginalSize,
                        attachment.ContentType);
                    files.Add(new PackFile(
                        AttachmentPackPath(item.Area, attachment),
                        original,
                        1));
                }
            }
        }

        var generatedAt = DateTimeOffset.UtcNow;
        var snapshotId = Guid.NewGuid();
        var version = (await db.YearEndHandoverPackSnapshots
            .Where(x => x.AccountingPeriodId == period.Id)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var manifest = new
        {
            Format = "Account Island year-end handover pack",
            Version = 2,
            SnapshotId = snapshotId,
            SnapshotVersion = version,
            OrganisationId = organisation.Id,
            organisation.LegalName,
            organisation.CountryCode,
            Currency = organisation.BaseCurrency,
            PeriodId = period.Id,
            period.Name,
            period.StartsOn,
            period.EndsOn,
            period.LockedAt,
            period.LockedByUserId,
            YearEndReview = yearEndReview is null
                ? null
                : new
                {
                    yearEndReview.StartedAt,
                    yearEndReview.StartedByUserId,
                    yearEndReview.ApprovedAt,
                    yearEndReview.ApprovedByUserId,
                    yearEndReview.ApprovalReference,
                    ReviewedSchedules = yearEndReview.Items.Count(x =>
                        x.Status == YearEndReviewStatus.Reviewed),
                    TotalSchedules = yearEndReview.Items.Count
                },
            GeneratedAt = generatedAt,
            GeneratedByUserId = userId,
            Files = files.Select(x => new
            {
                x.Name,
                x.RowCount,
                Sha256 = Convert.ToHexString(SHA256.HashData(x.Content)).ToLowerInvariant()
            })
        };
        var manifestContent = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        var manifestSha256 = Convert.ToHexString(SHA256.HashData(manifestContent)).ToLowerInvariant();
        files.Insert(0, new PackFile("manifest.json", manifestContent, 1));

        byte[] content;
        using (var output = new MemoryStream())
        {
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    var entry = archive.CreateEntry(file.Name, CompressionLevel.Optimal);
                    entry.LastWriteTime = generatedAt;
                    await using var stream = entry.Open();
                    await stream.WriteAsync(file.Content, cancellationToken);
                }
            }

            content = output.ToArray();
        }

        var contentSha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var fileName =
            $"account-island-handover-{period.StartsOn:yyyyMMdd}-{period.EndsOn:yyyyMMdd}-v{version}.zip";
        var storedObject = storage.Stage(organisationId, userId, content);
        db.YearEndHandoverPackSnapshots.Add(new YearEndHandoverPackSnapshot
        {
            Id = snapshotId,
            OrganisationId = organisationId,
            AccountingPeriodId = period.Id,
            Version = version,
            FileName = fileName,
            ImmutableDocumentObjectId = storedObject.Id,
            Sha256 = contentSha256,
            ContentLength = content.LongLength,
            ManifestSha256 = manifestSha256,
            CreatedAt = generatedAt,
            CreatedByUserId = userId,
            ReviewApprovalReference = yearEndReview?.ApprovalReference,
            ReviewApprovedAt = yearEndReview?.ApprovedAt,
            ReviewApprovedByUserId = yearEndReview?.ApprovedByUserId
        });

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "YearEndHandoverPackExported",
            EntityType = nameof(AccountingPeriod),
            EntityId = period.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                period.Name,
                period.StartsOn,
                period.EndsOn,
                SnapshotId = snapshotId,
                Version = version,
                FileCount = files.Count,
                Size = content.Length,
                Sha256 = contentSha256,
                ManifestSha256 = manifestSha256
            })
        });
        await db.SaveChangesAsync(cancellationToken);

        return new YearEndHandoverPack(
            content,
            fileName,
            snapshotId,
            version,
            contentSha256);
    }

    public async Task<List<YearEndHandoverPackSnapshot>> ListAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanExportAsync(userId, organisationId);
        return await db.YearEndHandoverPackSnapshots.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId &&
                        x.AccountingPeriodId == periodId)
            .OrderByDescending(x => x.Version)
            .ToListAsync(cancellationToken);
    }

    public async Task<YearEndHandoverPack> DownloadAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanExportAsync(userId, organisationId);
        var snapshot = await db.YearEndHandoverPackSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == snapshotId &&
                     x.OrganisationId == organisationId &&
                     x.AccountingPeriodId == periodId,
                cancellationToken)
            ?? throw new InvalidOperationException("Handover pack version not found.");
        var content = await storage.ReadVerifiedAsync(
            organisationId,
            snapshot.ImmutableDocumentObjectId,
            cancellationToken);
        var actualHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (content.LongLength != snapshot.ContentLength ||
            !actualHash.Equals(snapshot.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The retained handover pack failed its integrity check.");
        }

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "YearEndHandoverPackVersionDownloaded",
            EntityType = nameof(AccountingPeriod),
            EntityId = periodId.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                SnapshotId = snapshot.Id,
                snapshot.Version,
                snapshot.Sha256,
                snapshot.ContentLength
            })
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(content, snapshot.FileName, snapshot.Id, snapshot.Version, snapshot.Sha256);
    }

    private async Task EnsureCanExportAsync(string userId, Guid organisationId)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "Only owners and administrators can access year-end handover packs.");
        }
    }

    private static PackFile Csv(
        string name,
        string header,
        IEnumerable<string> rows)
    {
        var materialised = rows.ToList();
        var text = new StringBuilder(header).Append("\r\n");
        foreach (var row in materialised)
        {
            text.Append(row).Append("\r\n");
        }

        return new PackFile(name, Encoding.UTF8.GetBytes(text.ToString()), materialised.Count);
    }

    private static string Row(params object?[] values) =>
        string.Join(',', values.Select(Value));

    private static string Value(object? value)
    {
        var text = value switch
        {
            null => "",
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.00", CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private async Task<List<AgeingDocument>> GetReceivablesAsync(
        Guid organisationId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var invoices = await db.SalesInvoices.AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.IssueDate <= asAt &&
                x.Status != InvoiceStatus.Draft)
            .Select(x => new
            {
                x.Id,
                Contact = x.Customer.Name,
                DocumentNumber = x.InvoiceNumber,
                x.IssueDate,
                x.DueDate,
                x.Total
            })
            .ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var voided = await db.SalesInvoiceVoids.AsNoTracking()
            .Where(x =>
                invoiceIds.Contains(x.SalesInvoiceId) &&
                x.Status == SalesInvoiceVoidStatus.Posted &&
                x.VoidDate <= asAt)
            .Select(x => x.SalesInvoiceId)
            .ToListAsync(cancellationToken);
        invoices = invoices.Where(x => !voided.Contains(x.Id)).ToList();
        invoiceIds = invoices.Select(x => x.Id).ToArray();

        var receipts = await db.CustomerReceiptAllocations.AsNoTracking()
            .Where(x =>
                invoiceIds.Contains(x.SalesInvoiceId) &&
                x.CustomerReceipt.ReceiptDate <= asAt)
            .Select(x => new { x.CustomerReceiptId, x.SalesInvoiceId, x.Amount })
            .ToListAsync(cancellationToken);
        var receiptIds = receipts.Select(x => x.CustomerReceiptId).Distinct().ToArray();
        var reversedReceipts = await db.CustomerReceiptReversals.AsNoTracking()
            .Where(x => receiptIds.Contains(x.CustomerReceiptId) && x.ReversalDate <= asAt)
            .Select(x => x.CustomerReceiptId)
            .ToListAsync(cancellationToken);
        var paid = receipts
            .Where(x => !reversedReceipts.Contains(x.CustomerReceiptId))
            .GroupBy(x => x.SalesInvoiceId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));

        var credits = await db.SalesCreditNotes.AsNoTracking()
            .Where(x =>
                invoiceIds.Contains(x.SalesInvoiceId) &&
                x.Status == SalesCreditNoteStatus.Posted &&
                x.CreditDate <= asAt)
            .Select(x => new { x.Id, x.SalesInvoiceId, x.Total })
            .ToListAsync(cancellationToken);
        var creditIds = credits.Select(x => x.Id).ToArray();
        var reversedCredits = await db.SalesCreditNoteReversals.AsNoTracking()
            .Where(x =>
                creditIds.Contains(x.SalesCreditNoteId) &&
                x.Status == SalesCreditNoteReversalStatus.Posted &&
                x.ReversalDate <= asAt)
            .Select(x => x.SalesCreditNoteId)
            .ToListAsync(cancellationToken);
        var credited = credits
            .Where(x => !reversedCredits.Contains(x.Id))
            .GroupBy(x => x.SalesInvoiceId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Total));

        return invoices.Select(x => new AgeingDocument(
                x.Contact,
                x.DocumentNumber,
                x.IssueDate,
                x.DueDate,
                x.Total,
                paid.GetValueOrDefault(x.Id),
                credited.GetValueOrDefault(x.Id),
                Math.Max(0m, x.Total - paid.GetValueOrDefault(x.Id) - credited.GetValueOrDefault(x.Id)),
                AgeBucket(asAt, x.DueDate)))
            .Where(x => x.OutstandingAmount > 0m)
            .OrderBy(x => x.Contact)
            .ThenBy(x => x.DueDate)
            .ToList();
    }

    private async Task<List<AgeingDocument>> GetPayablesAsync(
        Guid organisationId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var bills = await db.SupplierBills.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.BillDate <= asAt)
            .Select(x => new
            {
                x.Id,
                Contact = x.Supplier.Name,
                DocumentNumber = x.BillNumber,
                x.BillDate,
                x.DueDate,
                x.Total
            })
            .ToListAsync(cancellationToken);
        var billIds = bills.Select(x => x.Id).ToArray();
        var voided = await db.SupplierBillVoids.AsNoTracking()
            .Where(x => billIds.Contains(x.SupplierBillId) && x.VoidDate <= asAt)
            .Select(x => x.SupplierBillId)
            .ToListAsync(cancellationToken);
        bills = bills.Where(x => !voided.Contains(x.Id)).ToList();
        billIds = bills.Select(x => x.Id).ToArray();

        var payments = await db.SupplierPayments.AsNoTracking()
            .Where(x => billIds.Contains(x.SupplierBillId) && x.PaymentDate <= asAt)
            .Select(x => new { x.Id, x.SupplierBillId, x.Amount })
            .ToListAsync(cancellationToken);
        var paymentIds = payments.Select(x => x.Id).ToArray();
        var reversedPayments = await db.SupplierPaymentReversals.AsNoTracking()
            .Where(x => paymentIds.Contains(x.SupplierPaymentId) && x.ReversalDate <= asAt)
            .Select(x => x.SupplierPaymentId)
            .ToListAsync(cancellationToken);
        var paid = payments
            .Where(x => !reversedPayments.Contains(x.Id))
            .GroupBy(x => x.SupplierBillId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));

        var credits = await db.SupplierCreditNotes.AsNoTracking()
            .Where(x => billIds.Contains(x.SupplierBillId) && x.CreditDate <= asAt)
            .Select(x => new { x.Id, x.SupplierBillId, x.Total })
            .ToListAsync(cancellationToken);
        var creditIds = credits.Select(x => x.Id).ToArray();
        var reversedCredits = await db.SupplierCreditNoteReversals.AsNoTracking()
            .Where(x => creditIds.Contains(x.SupplierCreditNoteId) && x.ReversalDate <= asAt)
            .Select(x => x.SupplierCreditNoteId)
            .ToListAsync(cancellationToken);
        var credited = credits
            .Where(x => !reversedCredits.Contains(x.Id))
            .GroupBy(x => x.SupplierBillId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Total));

        return bills.Select(x => new AgeingDocument(
                x.Contact,
                x.DocumentNumber,
                x.BillDate,
                x.DueDate,
                x.Total,
                paid.GetValueOrDefault(x.Id),
                credited.GetValueOrDefault(x.Id),
                Math.Max(0m, x.Total - paid.GetValueOrDefault(x.Id) - credited.GetValueOrDefault(x.Id)),
                AgeBucket(asAt, x.DueDate)))
            .Where(x => x.OutstandingAmount > 0m)
            .OrderBy(x => x.Contact)
            .ThenBy(x => x.DueDate)
            .ToList();
    }

    private async Task<List<FixedAssetScheduleRow>> GetFixedAssetsAsync(
        Guid organisationId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var assets = await db.FixedAssets.AsNoTracking()
            .Include(x => x.DepreciationEntries)
            .Include(x => x.Disposal)
            .Where(x => x.OrganisationId == organisationId && x.AcquisitionDate <= asAt)
            .OrderBy(x => x.AssetNumber)
            .ToListAsync(cancellationToken);

        return assets.Select(asset =>
        {
            var depreciation = asset.DepreciationEntries
                .Where(x => x.ThroughDate <= asAt)
                .Sum(x => x.Amount);
            var disposed = asset.Disposal is not null && asset.Disposal.DisposalDate <= asAt;
            return new FixedAssetScheduleRow(
                asset.AssetNumber,
                asset.Name,
                asset.AcquisitionDate,
                asset.Cost,
                asset.ResidualValue,
                asset.UsefulLifeMonths,
                depreciation,
                disposed ? 0m : Math.Max(0m, asset.Cost - depreciation),
                disposed ? "Disposed" : "Held",
                disposed ? asset.Disposal!.DisposalDate : null,
                disposed ? asset.Disposal!.Proceeds : null,
                disposed ? asset.Disposal!.GainLoss : null);
        }).ToList();
    }

    private async Task<List<InventoryScheduleRow>> GetInventoryAsync(
        Guid organisationId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var positions = await db.InventoryMovements.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.MovementDate <= asAt)
            .GroupBy(x => new { x.ProductItemId, x.ProductItem.Code, x.ProductItem.Name })
            .Select(x => new
            {
                x.Key.Code,
                x.Key.Name,
                Quantity = x.Sum(y => y.QuantityChange),
                Value = x.Sum(y => y.ValueChange)
            })
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

        return positions.Select(x => new InventoryScheduleRow(
            x.Code,
            x.Name,
            x.Quantity,
            x.Quantity == 0m ? 0m : x.Value / x.Quantity,
            x.Value)).ToList();
    }

    private static string AgeBucket(DateOnly asAt, DateOnly dueDate)
    {
        var days = asAt.DayNumber - dueDate.DayNumber;
        return days <= 0 ? "Current"
            : days <= 30 ? "1-30 days"
            : days <= 60 ? "31-60 days"
            : days <= 90 ? "61-90 days"
            : "90+ days";
    }

    private static string ReviewAreaLabel(YearEndReviewArea area) => area switch
    {
        YearEndReviewArea.TrialBalance => "Trial balance",
        YearEndReviewArea.FinancialStatements => "Financial statements",
        YearEndReviewArea.VatWorkpaper => "VAT workpaper",
        YearEndReviewArea.BankReconciliations => "Bank reconciliations",
        YearEndReviewArea.AgedReceivables => "Aged receivables",
        YearEndReviewArea.AgedPayables => "Aged payables",
        YearEndReviewArea.FixedAssets => "Fixed assets",
        YearEndReviewArea.InventoryValuation => "Inventory valuation",
        YearEndReviewArea.YearEndAdjustments => "Year-end adjustments",
        _ => area.ToString()
    };

    private static string AttachmentPackPath(
        YearEndReviewArea area,
        YearEndReviewAttachment attachment) =>
        $"review-evidence/{area.ToString().ToLowerInvariant()}/{attachment.Id:N}-{attachment.FileName}";

    private sealed record PackFile(string Name, byte[] Content, int RowCount);
    private sealed record AgeingDocument(
        string Contact,
        string DocumentNumber,
        DateOnly DocumentDate,
        DateOnly DueDate,
        decimal OriginalAmount,
        decimal SettledAmount,
        decimal CreditedAmount,
        decimal OutstandingAmount,
        string AgeBucket);
    private sealed record FixedAssetScheduleRow(
        string AssetNumber,
        string Name,
        DateOnly AcquisitionDate,
        decimal Cost,
        decimal ResidualValue,
        int UsefulLifeMonths,
        decimal AccumulatedDepreciation,
        decimal CarryingValue,
        string Status,
        DateOnly? DisposalDate,
        decimal? Proceeds,
        decimal? GainLoss);
    private sealed record InventoryScheduleRow(
        string Code,
        string Name,
        decimal Quantity,
        decimal AverageUnitCost,
        decimal Value);
}
