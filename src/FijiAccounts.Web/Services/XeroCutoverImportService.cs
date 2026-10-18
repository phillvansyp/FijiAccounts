using System.Globalization;
using System.Text;
using System.Text.Json;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record XeroCutoverImportResult(
    Guid OpeningJournalId,
    int OpeningBalanceLines,
    int OpenSalesInvoices,
    int OpenSupplierBills,
    int FixedAssets,
    int ArchivedSourceRecords);

/// <summary>
/// Creates the live cutover position without replaying Xero history twice.
/// Historical source objects remain in the immutable snapshot archive. The live
/// ledger receives one balanced opening journal and the open AR/AP documents
/// needed to continue collection and payment work in Account Island.
/// </summary>
public sealed class XeroCutoverImportService(
    ApplicationDbContext db,
    TenantAccessService access,
    IImmutableDocumentStore storage,
    JournalPostingService posting,
    XeroImportPreparationService preparation)
{
    private static readonly HashSet<string> FinancialDatasets = new(StringComparer.OrdinalIgnoreCase)
    {
        "sales-invoices", "bills", "sales-credits", "supplier-credits", "payments",
        "batch-payments", "prepayments", "overpayments", "bank-transactions",
        "bank-transfers", "manual-journals", "journals", "purchase-orders", "quotes",
        "repeating-invoices", "budgets", "assets"
    };

    public async Task<XeroCutoverImportResult> BuildAsync(
        string userId, Guid organisationId, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You cannot create the Xero cutover for this organisation.");

        var migration = await db.XeroMigrations.Include(x => x.Files)
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, ct)
            ?? throw new InvalidOperationException("Save the Xero migration setup first.");
        if (migration.CutoverDate is null)
            throw new InvalidOperationException("Choose the Account Island start date first.");
        if (!migration.ManagementApproved || !XeroMigrationService.AllChecksComplete(migration))
            throw new InvalidOperationException("Complete the seven reconciliations and record management approval before anything is posted live.");

        var supplied = migration.Files.Select(x => x.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = XeroMigrationService.RequiredCategories.Where(x => !supplied.Contains(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Add every required Xero export first. Missing: " + string.Join(", ", missing) + ".");

        if (await db.XeroMigrationMappings.AnyAsync(x => x.XeroMigrationId == migration.Id &&
                x.Status == XeroMigrationMappingStatus.Suggested, ct))
            throw new InvalidOperationException("Confirm or exclude every outstanding mapping before creating the cutover.");

        var pendingMasters = await db.XeroMigrationRecords.AnyAsync(x => x.XeroMigrationId == migration.Id &&
            new[] { "accounts", "contacts", "currencies", "items" }.Contains(x.DatasetKey) &&
            x.Status != XeroMigrationRecordStatus.Imported && x.Status != XeroMigrationRecordStatus.Skipped, ct);
        if (pendingMasters)
            throw new InvalidOperationException("Add the prepared accounts, contacts, currencies and items before creating the cutover.");

        await EnsureNoUnallocatedCreditsAsync(organisationId, migration.Id, ct);

        var prior = await FindOpeningJournalAsync(organisationId, migration.Id, ct);
        PostedJournal opening;
        if (prior is null)
        {
            var balances = await ReadTrialBalanceAsync(organisationId, migration, ct);
            opening = await PostOpeningJournalAsync(userId, organisationId, migration, balances, ct);
        }
        else opening = prior;

        var openSales = await ImportOpenSalesInvoicesAsync(userId, organisationId, migration, opening, ct);
        var openBills = await ImportOpenSupplierBillsAsync(userId, organisationId, migration, opening, ct);
        var fixedAssets = await ImportFixedAssetsAsync(userId, organisationId, migration, opening, ct);

        var records = await db.XeroMigrationRecords
            .Where(x => x.XeroMigrationId == migration.Id && FinancialDatasets.Contains(x.DatasetKey))
            .ToListAsync(ct);
        var archived = 0;
        foreach (var record in records.Where(x => x.Status is not XeroMigrationRecordStatus.Imported and
                     not XeroMigrationRecordStatus.Skipped and not XeroMigrationRecordStatus.Archived))
        {
            record.Status = XeroMigrationRecordStatus.Archived;
            record.TargetEntityType = "Immutable Xero history";
            record.ReviewMessage = null;
            record.ImportedAt = DateTimeOffset.UtcNow;
            record.ImportedByUserId = userId;
            archived++;
        }

        migration.Status = XeroMigrationStatus.ApprovedForCutover;
        migration.UpdatedAt = DateTimeOffset.UtcNow;
        migration.UpdatedByUserId = userId;
        db.XeroMigrationImportRuns.Add(new XeroMigrationImportRun
        {
            OrganisationId = organisationId,
            XeroMigrationId = migration.Id,
            Status = XeroMigrationImportRunStatus.Completed,
            IsDryRun = false,
            SourceRecordCount = records.Count,
            ImportedCount = openSales + openBills + fixedAssets,
            SkippedCount = archived,
            Summary = $"Opening journal {opening.Reference} posted; {openSales} open sales invoice(s), {openBills} open supplier bill(s), and {fixedAssets} fixed asset(s) added; {archived} historical record(s) retained in the source archive.",
            StartedByUserId = userId,
            CompletedAt = DateTimeOffset.UtcNow
        });
        db.AuditEvents.Add(Audit(organisationId, userId, "XeroCutoverBuilt", migration.Id, new
        {
            OpeningJournalId = opening.Id,
            OpeningLines = opening.Lines.Count,
            OpenSalesInvoices = openSales,
            OpenSupplierBills = openBills,
            FixedAssets = fixedAssets,
            ArchivedSourceRecords = archived
        }));
        await db.SaveChangesAsync(ct);
        await preparation.RefreshIssuesAsync(userId, organisationId, migration.Id, ct);
        return new(opening.Id, opening.Lines.Count, openSales, openBills, fixedAssets, archived);
    }

    private async Task EnsureNoUnallocatedCreditsAsync(Guid organisationId, Guid migrationId, CancellationToken ct)
    {
        var checks = new[]
        {
            (Dataset: "sales-credits", Root: "CreditNotes", Label: "customer credit note"),
            (Dataset: "supplier-credits", Root: "CreditNotes", Label: "supplier credit note"),
            (Dataset: "prepayments", Root: "Prepayments", Label: "prepayment"),
            (Dataset: "overpayments", Root: "Overpayments", Label: "overpayment")
        };
        var open = new List<string>();
        foreach (var check in checks)
        {
            var records = await ReadDatasetAsync(organisationId, migrationId, check.Dataset, check.Root, ct);
            var count = records.Count(x => Decimal(x, "RemainingCredit") > 0m || Decimal(x, "AmountDue") > 0m);
            if (count > 0) open.Add($"{count} {check.Label}{(count == 1 ? string.Empty : "s")}");
        }
        if (open.Count > 0)
            throw new InvalidDataException("Allocate or refund the remaining Xero credits before the final freeze, then copy Xero again. Outstanding: " + string.Join(", ", open) + ". This prevents the customer and supplier ledgers from starting with hidden credits.");
    }

    private async Task<int> ImportFixedAssetsAsync(
        string userId, Guid organisationId, XeroMigration migration, PostedJournal opening, CancellationToken ct)
    {
        var sources = await ReadDatasetAsync(organisationId, migration.Id, "assets", "Assets", ct);
        if (sources.Count == 0) return 0;
        var expense = await GetOrCreateDepreciationAccountAsync(organisationId, "6800", "Depreciation Expense", false, ct);
        var accumulated = await GetOrCreateDepreciationAccountAsync(organisationId, "1590", "Accumulated Depreciation", true, ct);
        var count = 0;
        foreach (var source in sources)
        {
            var externalId = Text(source, "AssetId", "AssetID", "AssetNumber");
            var record = await FindRecordAsync(migration.Id, "assets", externalId, ct);
            if (record?.Status == XeroMigrationRecordStatus.Imported) continue;
            var number = Short(Text(source, "AssetNumber"), 40, "XERO-" + externalId[..Math.Min(30, externalId.Length)]);
            if (await db.FixedAssets.AnyAsync(x => x.OrganisationId == organisationId && x.AssetNumber == number, ct))
                throw new InvalidDataException($"Fixed asset number '{number}' already exists in Account Island.");
            var assetTypeId = Text(source, "AssetTypeId", "AssetTypeID");
            var typeMapping = await db.XeroMigrationMappings.AsNoTracking().FirstOrDefaultAsync(x =>
                x.XeroMigrationId == migration.Id && x.Kind == XeroMigrationMappingKind.AssetType &&
                x.SourceKey == assetTypeId && x.Status == XeroMigrationMappingStatus.Confirmed, ct);
            var assetAccountId = typeMapping?.TargetEntityId
                ?? throw new InvalidDataException($"The Xero asset type for '{Text(source, "AssetName", "AssetNumber")}' is not mapped to an asset account.");
            var settings = Property(source, "BookDepreciationSetting");
            var rate = Decimal(settings, "DepreciationRate");
            var lifeYears = Decimal(settings, "EffectiveLifeYears");
            var lifeMonths = lifeYears > 0 ? (int)Math.Round(lifeYears * 12m) : rate > 0 ? (int)Math.Round(1200m / rate) : 60;
            var asset = new FixedAsset
            {
                OrganisationId = organisationId, AssetNumber = number,
                Name = Short(Text(source, "AssetName"), 160, number),
                VehicleRegistration = NullIfBlank(Short(Text(source, "RegistrationNumber"), 20, string.Empty)),
                AcquisitionDate = Date(source, "PurchaseDate"), Cost = Decimal(source, "PurchasePrice"),
                ResidualValue = Decimal(settings, "ResidualValue"), UsefulLifeMonths = Math.Max(1, lifeMonths),
                AssetAccountId = assetAccountId, DepreciationExpenseAccountId = expense.Id,
                AccumulatedDepreciationAccountId = accumulated.Id,
                IsActive = !Text(source, "AssetStatus", "Status").Equals("DISPOSED", StringComparison.OrdinalIgnoreCase),
                CreatedByUserId = userId
            };
            var accumulatedAmount = Decimal(settings, "CurrentAccumDepreciationAmount");
            if (accumulatedAmount > 0)
            {
                asset.DepreciationEntries.Add(new FixedAssetDepreciation
                {
                    ThroughDate = migration.CutoverDate!.Value.AddDays(-1), Amount = accumulatedAmount,
                    PostedJournalId = opening.Id, PostedByUserId = userId
                });
            }
            db.FixedAssets.Add(asset);
            Link(record, asset.Id, nameof(FixedAsset), userId);
            count++;
        }
        await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task<LedgerAccount> GetOrCreateDepreciationAccountAsync(
        Guid organisationId, string preferredCode, string name, bool contraAsset, CancellationToken ct)
    {
        var account = await db.LedgerAccounts.FirstOrDefaultAsync(x => x.OrganisationId == organisationId &&
            (x.Name == name || x.Code == preferredCode), ct);
        if (account is not null) return account;
        var code = preferredCode;
        var suffix = 1;
        while (await db.LedgerAccounts.AnyAsync(x => x.OrganisationId == organisationId && x.Code == code, ct))
            code = preferredCode[..Math.Min(17, preferredCode.Length)] + suffix++;
        account = new LedgerAccount
        {
            OrganisationId = organisationId, Code = code, Name = name,
            Type = contraAsset ? FijiAccounts.Domain.Accounting.AccountType.Asset : FijiAccounts.Domain.Accounting.AccountType.Expense,
            IsSystemAccount = true, IsActive = true
        };
        db.LedgerAccounts.Add(account);
        return account;
    }

    private async Task<PostedJournal?> FindOpeningJournalAsync(Guid organisationId, Guid migrationId, CancellationToken ct)
    {
        var audit = await db.AuditEvents.AsNoTracking().Where(x => x.OrganisationId == organisationId &&
            x.EventType == "XeroOpeningBalancePosted" && x.EntityId == migrationId.ToString())
            .OrderByDescending(x => x.OccurredAt).FirstOrDefaultAsync(ct);
        if (audit is null) return null;
        using var evidence = JsonDocument.Parse(audit.JsonData);
        if (!evidence.RootElement.TryGetProperty("OpeningJournalId", out var id) || !Guid.TryParse(id.GetString(), out var journalId))
            return null;
        return await db.PostedJournals.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == journalId, ct);
    }

    private async Task<IReadOnlyList<OpeningBalance>> ReadTrialBalanceAsync(
        Guid organisationId, XeroMigration migration, CancellationToken ct)
    {
        var file = migration.Files.Where(x => x.Category == "Trial balance" &&
                Path.GetExtension(x.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.UploadedAt).FirstOrDefault()
            ?? throw new InvalidOperationException("Add the final Xero Trial Balance as CSV. PDF and XLSX copies can be retained as evidence, but the cutover posting needs the CSV columns.");
        var bytes = await storage.ReadVerifiedAsync(organisationId, file.ImmutableDocumentObjectId, ct);
        var table = ParseCsv(Encoding.UTF8.GetString(bytes));
        var headerIndex = table.FindIndex(row => FindColumn(row, "Debit") >= 0 && FindColumn(row, "Credit") >= 0);
        if (headerIndex < 0)
            throw new InvalidDataException("The Trial Balance CSV must contain Debit and Credit columns.");
        var header = table[headerIndex];
        var codeColumn = FindColumn(header, "Account Code", "Code");
        var nameColumn = FindColumn(header, "Account", "Account Name", "Name");
        var debitColumn = FindColumn(header, "Debit", "YTD Debit");
        var creditColumn = FindColumn(header, "Credit", "YTD Credit");
        if (nameColumn < 0 && codeColumn < 0)
            throw new InvalidDataException("The Trial Balance CSV must identify each account by code or name.");

        var accounts = await db.LedgerAccounts.Where(x => x.OrganisationId == organisationId && x.IsActive).ToListAsync(ct);
        var results = new List<OpeningBalance>();
        foreach (var row in table.Skip(headerIndex + 1))
        {
            var code = Cell(row, codeColumn).Trim();
            var name = Cell(row, nameColumn).Trim();
            if ((code + name).Contains("total", StringComparison.OrdinalIgnoreCase)) continue;
            var debit = Money(Cell(row, debitColumn));
            var credit = Money(Cell(row, creditColumn));
            if (debit == 0m && credit == 0m) continue;
            var account = accounts.FirstOrDefault(x => code.Length > 0 && x.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                ?? accounts.FirstOrDefault(x => name.Length > 0 && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (account is null)
                throw new InvalidDataException($"Trial Balance account '{(code.Length > 0 ? code : name)}' has no Account Island account. Import or map the chart first.");
            results.Add(new(account.Id, account.Code + " · " + account.Name, debit, credit));
        }
        if (results.Count < 2) throw new InvalidDataException("The Trial Balance CSV did not contain enough non-zero account balances.");
        var debitTotal = decimal.Round(results.Sum(x => x.Debit), 2);
        var creditTotal = decimal.Round(results.Sum(x => x.Credit), 2);
        if (debitTotal != creditTotal)
            throw new InvalidDataException($"The Trial Balance is not balanced. Debits are {debitTotal:N2} and credits are {creditTotal:N2}.");
        return results;
    }

    private async Task<PostedJournal> PostOpeningJournalAsync(
        string userId, Guid organisationId, XeroMigration migration,
        IReadOnlyList<OpeningBalance> balances, CancellationToken ct)
    {
        var journal = await posting.PostAsync(userId, new JournalPostRequest(
            organisationId,
            migration.CutoverDate!.Value.AddDays(-1),
            $"XERO-OPEN-{migration.CutoverDate:yyyyMMdd}",
            $"Verified Xero closing Trial Balance for migration from {migration.SourceOrganisationName}",
            balances.Select(x => new JournalLineInput(x.AccountId, "Xero opening balance · " + x.Label, x.Debit, x.Credit)).ToArray(),
            Currency: "NZD"), ct);
        db.AuditEvents.Add(Audit(organisationId, userId, "XeroOpeningBalancePosted", migration.Id, new
        {
            OpeningJournalId = journal.Id.ToString(), migration.CutoverDate,
            Debit = balances.Sum(x => x.Debit), Credit = balances.Sum(x => x.Credit)
        }));
        await db.SaveChangesAsync(ct);
        return journal;
    }

    private async Task<int> ImportOpenSalesInvoicesAsync(
        string userId, Guid organisationId, XeroMigration migration, PostedJournal opening, CancellationToken ct)
    {
        var sources = await ReadDatasetAsync(organisationId, migration.Id, "sales-invoices", ct);
        var sequence = await db.SalesInvoices.Where(x => x.OrganisationId == organisationId).MaxAsync(x => (long?)x.SequenceNumber, ct) ?? 0;
        var count = 0;
        foreach (var source in sources.Where(x => Decimal(x, "AmountDue") > 0m && !Text(x, "Status").Equals("VOIDED", StringComparison.OrdinalIgnoreCase)))
        {
            var externalId = Text(source, "InvoiceID", "InvoiceNumber");
            var record = await FindRecordAsync(migration.Id, "sales-invoices", externalId, ct);
            if (record?.Status == XeroMigrationRecordStatus.Imported) continue;
            var contactId = await ResolveContactAsync(migration.Id, source, ct);
            var number = Short(Text(source, "InvoiceNumber"), 40, "XERO-" + externalId[..Math.Min(30, externalId.Length)]);
            if (await db.SalesInvoices.AnyAsync(x => x.OrganisationId == organisationId && x.InvoiceNumber == number, ct))
                throw new InvalidDataException($"Sales invoice number '{number}' already exists in Account Island.");
            var rate = Decimal(source, "CurrencyRate"); if (rate <= 0m) rate = 1m;
            var total = Decimal(source, "Total"); var due = Decimal(source, "AmountDue");
            var invoice = new SalesInvoice
            {
                OrganisationId = organisationId, CustomerId = contactId, SequenceNumber = ++sequence,
                InvoiceNumber = number, IssueDate = Date(source, "Date"), DueDate = Date(source, "DueDate"),
                Currency = Short(Text(source, "CurrencyCode"), 3, "NZD"), ExchangeRateToBase = rate,
                TransactionSubtotal = Decimal(source, "SubTotal"), TransactionVatTotal = Decimal(source, "TotalTax"), TransactionTotal = total,
                Subtotal = Round(Decimal(source, "SubTotal") * rate), VatTotal = Round(Decimal(source, "TotalTax") * rate), Total = Round(total * rate),
                TransactionAmountPaid = total - due, AmountPaid = Round((total - due) * rate),
                Status = total == due ? InvoiceStatus.Posted : InvoiceStatus.PartPaid,
                PostedJournalId = opening.Id, CreatedByUserId = userId
            };
            invoice.Lines = await SalesLinesAsync(migration.Id, source, rate, ct);
            db.SalesInvoices.Add(invoice);
            Link(record, invoice.Id, nameof(SalesInvoice), userId);
            count++;
        }
        await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task<int> ImportOpenSupplierBillsAsync(
        string userId, Guid organisationId, XeroMigration migration, PostedJournal opening, CancellationToken ct)
    {
        var sources = await ReadDatasetAsync(organisationId, migration.Id, "bills", ct);
        var sequence = await db.SupplierBills.Where(x => x.OrganisationId == organisationId).MaxAsync(x => (long?)x.SequenceNumber, ct) ?? 0;
        var count = 0;
        foreach (var source in sources.Where(x => Decimal(x, "AmountDue") > 0m && !Text(x, "Status").Equals("VOIDED", StringComparison.OrdinalIgnoreCase)))
        {
            var externalId = Text(source, "InvoiceID", "InvoiceNumber");
            var record = await FindRecordAsync(migration.Id, "bills", externalId, ct);
            if (record?.Status == XeroMigrationRecordStatus.Imported) continue;
            var supplierId = await ResolveContactAsync(migration.Id, source, ct);
            var reference = Short(Text(source, "InvoiceNumber", "Reference"), 80, "XERO-" + externalId);
            if (await db.SupplierBills.AnyAsync(x => x.OrganisationId == organisationId && x.SupplierId == supplierId && x.SupplierReference == reference, ct))
                throw new InvalidDataException($"Supplier bill reference '{reference}' already exists in Account Island.");
            var rate = Decimal(source, "CurrencyRate"); if (rate <= 0m) rate = 1m;
            var total = Decimal(source, "Total"); var due = Decimal(source, "AmountDue");
            var bill = new SupplierBill
            {
                OrganisationId = organisationId, SupplierId = supplierId, SequenceNumber = ++sequence,
                BillNumber = Short("XERO-" + externalId, 40, reference), SupplierReference = reference,
                BillDate = Date(source, "Date"), DueDate = Date(source, "DueDate"),
                Currency = Short(Text(source, "CurrencyCode"), 3, "NZD"), ExchangeRateToBase = rate,
                TransactionSubtotal = Decimal(source, "SubTotal"), TransactionVatTotal = Decimal(source, "TotalTax"), TransactionTotal = total,
                Subtotal = Round(Decimal(source, "SubTotal") * rate), VatTotal = Round(Decimal(source, "TotalTax") * rate), Total = Round(total * rate),
                TransactionAmountPaid = total - due, AmountPaid = Round((total - due) * rate),
                Status = total == due ? BillStatus.Posted : BillStatus.PartPaid,
                PostedJournalId = opening.Id, CreatedByUserId = userId
            };
            bill.Lines = await BillLinesAsync(migration.Id, source, rate, ct);
            db.SupplierBills.Add(bill);
            Link(record, bill.Id, nameof(SupplierBill), userId);
            count++;
        }
        await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task<List<SalesInvoiceLine>> SalesLinesAsync(Guid migrationId, JsonElement source, decimal rate, CancellationToken ct)
    {
        var result = new List<SalesInvoiceLine>();
        foreach (var line in Array(source, "LineItems"))
        {
            var accountId = await ResolveAccountAsync(migrationId, Text(line, "AccountCode"), ct);
            var net = Decimal(line, "LineAmount"); var tax = Decimal(line, "TaxAmount");
            result.Add(new SalesInvoiceLine
            {
                Description = Short(Text(line, "Description"), 300, "Xero invoice line"), Quantity = Decimal(line, "Quantity", 1m),
                UnitPrice = Round(Decimal(line, "UnitAmount") * rate), TransactionUnitPrice = Decimal(line, "UnitAmount"),
                VatTreatment = await ResolveTaxAsync(migrationId, Text(line, "TaxType"), ct), VatRate = net == 0 ? 0 : tax / net,
                TransactionNetAmount = net, TransactionVatAmount = tax, TransactionGrossAmount = net + tax,
                NetAmount = Round(net * rate), VatAmount = Round(tax * rate), GrossAmount = Round((net + tax) * rate), RevenueAccountId = accountId
            });
        }
        if (result.Count == 0) throw new InvalidDataException("An open Xero sales invoice has no line details. Re-copy Xero with invoice line items enabled.");
        return result;
    }

    private async Task<List<SupplierBillLine>> BillLinesAsync(Guid migrationId, JsonElement source, decimal rate, CancellationToken ct)
    {
        var result = new List<SupplierBillLine>();
        foreach (var line in Array(source, "LineItems"))
        {
            var accountId = await ResolveAccountAsync(migrationId, Text(line, "AccountCode"), ct);
            var net = Decimal(line, "LineAmount"); var tax = Decimal(line, "TaxAmount");
            result.Add(new SupplierBillLine
            {
                Description = Short(Text(line, "Description"), 300, "Xero bill line"), Quantity = Decimal(line, "Quantity", 1m),
                UnitPrice = Round(Decimal(line, "UnitAmount") * rate), TransactionUnitPrice = Decimal(line, "UnitAmount"),
                VatTreatment = await ResolveTaxAsync(migrationId, Text(line, "TaxType"), ct), VatRate = net == 0 ? 0 : tax / net,
                TransactionNetAmount = net, TransactionVatAmount = tax, TransactionGrossAmount = net + tax,
                NetAmount = Round(net * rate), VatAmount = Round(tax * rate), GrossAmount = Round((net + tax) * rate), ExpenseAccountId = accountId
            });
        }
        if (result.Count == 0) throw new InvalidDataException("An open Xero supplier bill has no line details. Re-copy Xero with invoice line items enabled.");
        return result;
    }

    private async Task<Guid> ResolveContactAsync(Guid migrationId, JsonElement invoice, CancellationToken ct)
    {
        var contact = Property(invoice, "Contact");
        var key = Text(contact, "ContactID", "Name");
        var mapping = await db.XeroMigrationMappings.AsNoTracking().FirstOrDefaultAsync(x => x.XeroMigrationId == migrationId &&
            x.Kind == XeroMigrationMappingKind.Contact && x.SourceKey == key && x.Status == XeroMigrationMappingStatus.Confirmed, ct);
        return mapping?.TargetEntityId ?? throw new InvalidDataException($"The Xero contact '{Text(contact, "Name", "ContactID")}' is not mapped.");
    }

    private async Task<Guid> ResolveAccountAsync(Guid migrationId, string code, CancellationToken ct)
    {
        var mapping = await db.XeroMigrationMappings.AsNoTracking().FirstOrDefaultAsync(x => x.XeroMigrationId == migrationId &&
            x.Kind == XeroMigrationMappingKind.Account && x.SourceKey == code && x.Status == XeroMigrationMappingStatus.Confirmed, ct);
        return mapping?.TargetEntityId ?? throw new InvalidDataException($"The Xero account '{code}' is not mapped.");
    }

    private async Task<VatTreatment> ResolveTaxAsync(Guid migrationId, string key, CancellationToken ct)
    {
        var mapping = await db.XeroMigrationMappings.AsNoTracking().FirstOrDefaultAsync(x => x.XeroMigrationId == migrationId &&
            x.Kind == XeroMigrationMappingKind.TaxRate && x.SourceKey == key && x.Status == XeroMigrationMappingStatus.Confirmed, ct);
        return Enum.TryParse<VatTreatment>(mapping?.TargetDisplayName, true, out var treatment) ? treatment : VatTreatment.Standard;
    }

    private async Task<XeroMigrationRecord?> FindRecordAsync(Guid migrationId, string dataset, string externalId, CancellationToken ct) =>
        await db.XeroMigrationRecords.SingleOrDefaultAsync(x => x.XeroMigrationId == migrationId && x.DatasetKey == dataset && x.ExternalId == externalId, ct);

    private static void Link(XeroMigrationRecord? record, Guid id, string type, string userId)
    {
        if (record is null) return;
        record.Status = XeroMigrationRecordStatus.Imported; record.TargetEntityId = id; record.TargetEntityType = type;
        record.ImportedAt = DateTimeOffset.UtcNow; record.ImportedByUserId = userId; record.ReviewMessage = null;
    }

    private Task<List<JsonElement>> ReadDatasetAsync(Guid organisationId, Guid migrationId, string dataset, CancellationToken ct) =>
        ReadDatasetAsync(organisationId, migrationId, dataset, "Invoices", ct);

    private async Task<List<JsonElement>> ReadDatasetAsync(Guid organisationId, Guid migrationId, string dataset, string rootProperty, CancellationToken ct)
    {
        var snapshots = await db.XeroApiSnapshots.AsNoTracking()
            .Where(x => x.XeroMigrationId == migrationId && x.DatasetKey == dataset)
            .ToListAsync(ct);
        var snapshot = snapshots.OrderByDescending(x => x.RetrievedAt).FirstOrDefault();
        if (snapshot is null) return [];
        var bytes = await storage.ReadVerifiedAsync(organisationId, snapshot.ImmutableDocumentObjectId, ct);
        using var document = JsonDocument.Parse(bytes);
        var result = new List<JsonElement>();
        if (!document.RootElement.TryGetProperty("pages", out var pages)) return result;
        foreach (var page in pages.EnumerateArray())
            if (page.TryGetProperty(rootProperty, out var values))
                result.AddRange(values.EnumerateArray().Select(x => x.Clone()));
        return result;
    }

    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>(); var row = new List<string>(); var cell = new StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = !quoted; }
            else if (ch == ',' && !quoted) { row.Add(cell.ToString()); cell.Clear(); }
            else if ((ch == '\r' || ch == '\n') && !quoted)
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear(); if (row.Any(x => x.Length > 0)) rows.Add(row.ToArray()); row.Clear();
            }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }

    private static int FindColumn(string[] row, params string[] names) => System.Array.FindIndex(row,
        value => names.Any(name => value.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)));
    private static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index] : string.Empty;
    private static decimal Money(string value) => decimal.TryParse(value.Trim().Replace("$", "").Replace(",", "").Replace("(", "-").Replace(")", ""),
        NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount) ? amount : 0m;
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Short(string value, int max, string fallback) { value = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim(); return value[..Math.Min(max, value.Length)]; }
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static JsonElement Property(JsonElement value, string name) => value.TryGetProperty(name, out var found) ? found : default;
    private static IEnumerable<JsonElement> Array(JsonElement value, string name) => value.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.Array ? found.EnumerateArray() : [];
    private static string Text(JsonElement value, params string[] names)
    {
        foreach (var name in names) if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var found) && found.ValueKind != JsonValueKind.Null) return found.ToString();
        return string.Empty;
    }
    private static decimal Decimal(JsonElement value, string name, decimal fallback = 0m) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var found) &&
        decimal.TryParse(found.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : fallback;
    private static DateOnly Date(JsonElement value, string name)
    {
        var text = Text(value, name);
        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        if (text.StartsWith("/Date(") && long.TryParse(new string(text.Skip(6).TakeWhile(char.IsDigit).ToArray()), out var milliseconds))
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime);
        throw new InvalidDataException($"Xero did not supply a valid {name} value.");
    }

    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, Guid entityId, object data) => new()
    {
        OrganisationId = organisationId, UserId = userId, EventType = eventType,
        EntityType = nameof(XeroMigration), EntityId = entityId.ToString(), JsonData = JsonSerializer.Serialize(data)
    };

    private sealed record OpeningBalance(Guid AccountId, string Label, decimal Debit, decimal Credit);
}
