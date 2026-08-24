using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace FijiAccounts.Web.Services;

public sealed record BankImportResult(int Imported, int Skipped, Guid BatchId);
public sealed record StatementPreviewLine(DateOnly Date, string Description, string? Reference, decimal Amount);
public sealed record StatementPreview(string Format, IReadOnlyList<StatementPreviewLine> Lines);
public sealed record BankStatementDocumentRequest(
    string FileName,
    string? ContentType,
    long OriginalSize,
    byte[] Content);
public sealed record BankStatementImportBatch(
    Guid BatchId,
    Guid BankAccountId,
    string BankAccountName,
    string Source,
    DateOnly FirstDate,
    DateOnly LastDate,
    int LineCount,
    decimal NetAmount,
    DateTimeOffset ImportedAt,
    bool CanDelete,
    Guid? DocumentId,
    string? DocumentFileName);
public sealed record BankStatementImportDeleteResult(Guid BatchId, int Deleted);

public sealed class BankStatementImportService(ApplicationDbContext db, TenantAccessService access)
{
    public async Task<StatementPreview> ReadAsync(
    Stream stream,
    string fileName,
    BankAccountKind accountKind = BankAccountKind.Bank,
    CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".pdf")
        {
            await using var buffered = new MemoryStream();
            await stream.CopyToAsync(buffered, ct);
            buffered.Position = 0;
            return new("PDF", ParsePdf(buffered, accountKind));
        }
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("The statement file is empty.");
        return extension switch
        {
            ".ofx" or ".qfx" => new("OFX", ParseOfx(text)),
            ".qif" => new("QIF", ParseQif(text)),
            ".csv" or ".txt" => new("CSV", ParseCsv(text)),
            _ => throw new InvalidOperationException("Unsupported format. Use CSV, OFX, QFX or QIF.")
        };
    }

    public async Task<BankImportResult> ImportAsync(
        string userId,
        Guid organisationId,
        Guid bankAccountId,
        IReadOnlyList<StatementPreviewLine> lines,
        string source,
        BankStatementDocumentRequest? document = null,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot import statements for this organisation.");
        if (!await db.LedgerAccounts.AnyAsync(x => x.Id == bankAccountId && x.OrganisationId == organisationId && x.IsActive && x.IsBankAccount, ct)) throw new InvalidOperationException("Select an active bank account.");
        var storedDocument = document is null
            ? null
            : CreateDocument(
                organisationId,
                bankAccountId,
                userId,
                document);
        if (lines.Count > 0)
{
    var earliestDate =
        lines.Min(x => x.Date);

    var latestDate =
        lines.Max(x => x.Date);

    var completedPeriods =
        await db.BankReconciliationSessions
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.BankAccountId == bankAccountId &&
                x.IsCompleted &&
                x.StatementStartDate <= latestDate &&
                x.StatementEndDate >= earliestDate)
            .Select(x => new
            {
                x.StatementStartDate,
                x.StatementEndDate
            })
            .ToListAsync(ct);

    var containsClosedDate =
        lines.Any(line =>
            completedPeriods.Any(period =>
                line.Date >= period.StatementStartDate &&
                line.Date <= period.StatementEndDate));

    if (containsClosedDate)
    {
        throw new InvalidOperationException(
            "Statement transactions cannot be imported inside a completed reconciliation period.");
    }
}
        var batchId = Guid.NewGuid(); var imported = 0; var skipped = 0;
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in lines)
        {
            var baseHash = Hash(row.Date, row.Description, row.Reference, row.Amount);
            var occurrence = occurrences.GetValueOrDefault(baseHash) + 1;
            occurrences[baseHash] = occurrence;
            var hash = occurrence == 1
                ? baseHash
                : HashOccurrence(baseHash, occurrence);
            if (await db.BankStatementLines.AnyAsync(x => x.OrganisationId == organisationId && x.BankAccountId == bankAccountId && x.SourceHash == hash, ct)) { skipped++; continue; }
            db.BankStatementLines.Add(new BankStatementLine { OrganisationId = organisationId, BankAccountId = bankAccountId, TransactionDate = row.Date, Description = row.Description, Reference = row.Reference, Amount = row.Amount, Source = source, ImportBatchId = batchId, SourceHash = hash }); imported++;
        }
        if (imported > 0)
        {
            if (storedDocument is not null)
            {
                storedDocument.ImportBatchId = batchId;
                db.BankStatementImportDocuments.Add(storedDocument);
            }

            db.AuditEvents.Add(new AuditEvent
            {
                OrganisationId = organisationId,
                UserId = userId,
                EventType = "BankStatementImported",
                EntityType = nameof(BankStatementLine),
                EntityId = batchId.ToString(),
                JsonData = JsonSerializer.Serialize(new
                {
                    bankAccountId,
                    source,
                    imported,
                    skipped,
                    StatementDocument = storedDocument is null
                        ? null
                        : new
                        {
                            storedDocument.Id,
                            storedDocument.FileName,
                            storedDocument.ContentType,
                            storedDocument.OriginalSize
                        }
                })
            });
            await db.SaveChangesAsync(ct);
        }
        return new(imported, skipped, batchId);
    }

    public async Task<IReadOnlyList<BankStatementImportBatch>> GetImportBatchesAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot view statement imports for this organisation.");
        }

        var importedLines = await db.BankStatementLines
            .AsNoTracking()
            .Include(x => x.BankAccount)
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.ImportBatchId != null)
            .ToListAsync(ct);

        var batchIds = importedLines
            .Select(x => x.ImportBatchId!.Value)
            .Distinct()
            .ToList();
        var documents = await db.BankStatementImportDocuments
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                batchIds.Contains(x.ImportBatchId))
            .ToDictionaryAsync(x => x.ImportBatchId, ct);

        return importedLines
            .GroupBy(x => x.ImportBatchId!.Value)
            .Select(group => new BankStatementImportBatch(
                group.Key,
                group.First().BankAccountId,
                group.First().BankAccount.Name,
                group.First().Source,
                group.Min(x => x.TransactionDate),
                group.Max(x => x.TransactionDate),
                group.Count(),
                group.Sum(x => x.Amount),
                group.Min(x => x.ImportedAt),
                group.All(x =>
                    x.ReconciledAt == null &&
                    x.MatchedPostedJournalLineId == null),
                documents.GetValueOrDefault(group.Key)?.Id,
                documents.GetValueOrDefault(group.Key)?.FileName))
            .OrderByDescending(x => x.ImportedAt)
            .ToList();
    }

    public async Task<BankStatementImportDeleteResult> DeleteImportBatchAsync(
        string userId,
        Guid organisationId,
        Guid batchId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot delete statement imports for this organisation.");
        }

        var lines = await db.BankStatementLines
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.ImportBatchId == batchId)
            .ToListAsync(ct);

        if (lines.Count == 0)
        {
            throw new InvalidOperationException(
                "Statement import not found.");
        }

        if (lines.Any(x =>
                x.ReconciledAt != null ||
                x.MatchedPostedJournalLineId != null))
        {
            throw new InvalidOperationException(
                "This import cannot be deleted because one or more transactions have been reconciled. Reopen those transactions first.");
        }

        var bankAccountId = lines[0].BankAccountId;
        var firstDate = lines.Min(x => x.TransactionDate);
        var lastDate = lines.Max(x => x.TransactionDate);
        var insideCompletedReconciliation =
            await db.BankReconciliationSessions
                .AsNoTracking()
                .AnyAsync(x =>
                    x.OrganisationId == organisationId &&
                    x.BankAccountId == bankAccountId &&
                    x.IsCompleted &&
                    x.StatementStartDate <= lastDate &&
                    x.StatementEndDate >= firstDate,
                    ct);

        if (insideCompletedReconciliation)
        {
            throw new InvalidOperationException(
                "This import cannot be deleted because it is inside a completed reconciliation period.");
        }

        var document = await db.BankStatementImportDocuments
            .SingleOrDefaultAsync(x =>
                x.OrganisationId == organisationId &&
                x.ImportBatchId == batchId,
                ct);

        var evidence = new
        {
            BankAccountId = bankAccountId,
            Source = lines[0].Source,
            LineCount = lines.Count,
            FirstDate = firstDate,
            LastDate = lastDate,
            NetAmount = lines.Sum(x => x.Amount),
            ImportedAt = lines.Min(x => x.ImportedAt),
            StatementDocument = document is null
                ? null
                : new
                {
                    document.Id,
                    document.FileName,
                    document.ContentType,
                    document.OriginalSize
                }
        };

        db.BankStatementLines.RemoveRange(lines);
        if (document is not null)
        {
            db.BankStatementImportDocuments.Remove(document);
        }
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "BankStatementImportDeleted",
            EntityType = nameof(BankStatementLine),
            EntityId = batchId.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        });

        await db.SaveChangesAsync(ct);
        return new BankStatementImportDeleteResult(batchId, lines.Count);
    }

    public async Task<BankStatementImportDocument?> GetDocumentAsync(
        string userId,
        Guid organisationId,
        Guid batchId,
        CancellationToken ct = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            return null;
        }

        return await db.BankStatementImportDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrganisationId == organisationId &&
                x.ImportBatchId == batchId,
                ct);
    }

    public async Task<BankImportResult> ImportCsvAsync(
    string userId,
    Guid organisationId,
    Guid bankAccountId,
    Stream stream,
    CancellationToken ct = default)
{
    var account = await db.LedgerAccounts
        .AsNoTracking()
        .SingleOrDefaultAsync(
            x => x.Id == bankAccountId &&
                 x.OrganisationId == organisationId &&
                 x.IsActive &&
                 x.IsBankAccount,
            ct)
        ?? throw new InvalidOperationException(
            "Select an active bank or card account.");

    var preview = await ReadAsync(
        stream,
        "statement.csv",
        account.BankAccountKind,
        ct);

    return await ImportAsync(
        userId,
        organisationId,
        bankAccountId,
        preview.Lines,
        preview.Format,
        ct: ct);
}

    private static List<StatementPreviewLine> ParseCsv(string text)
    {
        using var reader = new StringReader(text); var header = reader.ReadLine() ?? throw new InvalidOperationException("The CSV file is empty."); var delimiter = header.Count(x => x == ';') > header.Count(x => x == ',') ? ';' : ',';
        var headers = ParseRow(header, delimiter).Select(Normalize).ToArray(); var dateIndex = Find(headers, "date", "transactiondate", "valuedate", "postingdate"); var descriptionIndex = Find(headers, "description", "details", "narrative", "particulars", "transactiondetails"); var amountIndex = FindOptional(headers, "amount", "transactionamount"); var debitIndex = FindOptional(headers, "debit", "withdrawal", "moneyout"); var creditIndex = FindOptional(headers, "credit", "deposit", "moneyin"); var referenceIndex = FindOptional(headers, "reference", "ref", "chequenumber", "transactionid");
        if (amountIndex < 0 && debitIndex < 0 && creditIndex < 0) throw new InvalidOperationException("CSV needs Amount or Debit and Credit columns.");
        var result = new List<StatementPreviewLine>(); string? row; var rowNumber = 1;
        while ((row = reader.ReadLine()) is not null) { rowNumber++; if (string.IsNullOrWhiteSpace(row)) continue; var values = ParseRow(row, delimiter); if (values.Count <= Math.Max(dateIndex, descriptionIndex)) throw new InvalidOperationException($"CSV row {rowNumber} is incomplete."); if (!TryDate(values[dateIndex], out var date)) throw new InvalidOperationException($"CSV row {rowNumber} has an invalid date."); var amount = amountIndex >= 0 ? ParseAmount(Value(values, amountIndex)) : ParseAmount(Value(values, creditIndex), true) - ParseAmount(Value(values, debitIndex), true); if (amount == 0) continue; var description = values[descriptionIndex].Trim(); result.Add(new(date, description.Length == 0 ? "Bank transaction" : description, NullIfEmpty(Value(values, referenceIndex)), amount)); }
        return result;
    }

    private static List<StatementPreviewLine> ParseQif(string text)
    {
        var result = new List<StatementPreviewLine>(); DateOnly? date = null; decimal? amount = null; string? description = null; string? reference = null;
        foreach (var line in text.Replace("\r", "").Split('\n')) { if (line == "^") { if (date is not null && amount is not null && amount != 0) result.Add(new(date.Value, description ?? "Bank transaction", reference, amount.Value)); date = null; amount = null; description = reference = null; continue; } if (line.Length < 2) continue; switch (line[0]) { case 'D': if (TryDate(line[1..], out var parsed)) date = parsed; break; case 'T': amount = ParseAmount(line[1..]); break; case 'P': description = line[1..].Trim(); break; case 'M': description ??= line[1..].Trim(); break; case 'N': reference = NullIfEmpty(line[1..]); break; } }
        return result;
    }

    private static List<StatementPreviewLine> ParseOfx(string text)
    {
        var result = new List<StatementPreviewLine>();
        foreach (Match transaction in Regex.Matches(text, @"<STMTTRN>(.*?)</STMTTRN>", RegexOptions.Singleline | RegexOptions.IgnoreCase)) { var block = transaction.Groups[1].Value; var dateText = Tag(block, "DTPOSTED"); var amountText = Tag(block, "TRNAMT"); if (dateText is null || amountText is null || !TryOfxDate(dateText, out var date)) continue; var amount = ParseAmount(amountText); if (amount == 0) continue; var name = Tag(block, "NAME") ?? Tag(block, "MEMO") ?? "Bank transaction"; result.Add(new(date, WebUtility.HtmlDecode(name.Trim()), NullIfEmpty(Tag(block, "FITID")), amount)); }
        if (result.Count == 0) throw new InvalidOperationException("No transactions were found in the OFX/QFX statement."); return result;
    }

    private static List<StatementPreviewLine> ParsePdf(
    Stream stream,
    BankAccountKind accountKind)
{
    var result = new List<StatementPreviewLine>();
    var extractedCharacters = 0;
    var pageTexts = new List<string>();

    using var document = PdfDocument.Open(stream);

    foreach (var page in document.GetPages())
    {
        var pageText = ContentOrderTextExtractor.GetText(page);
        pageTexts.Add(pageText);
        extractedCharacters += pageText.Count(char.IsLetterOrDigit);
    }

    if (extractedCharacters < 20)
    {
        throw new InvalidOperationException(
            "This appears to be a scanned or image-only PDF. " +
            "Download a CSV/OFX statement, or use OCR when that option is enabled.");
    }

    var allText = string.Join("\n", pageTexts);

    var yearMatch = Regex.Match(
        allText,
        @"\b\d{1,2}\s+[A-Za-z]{3}\s+(?<year>20\d{2})\b");

    var statementYear = yearMatch.Success
        ? int.Parse(
            yearMatch.Groups["year"].Value,
            CultureInfo.InvariantCulture)
        : DateTime.Today.Year;

    /*
     * PDF bank statements frequently wrap a single transaction over
     * multiple physical text lines.
     *
     * Example:
     *
     * 30 Jul EFTPOS R B
     * PATELSUPERMARKET
     * 12.24 470.48
     *
     * Build logical rows first, then parse the completed transaction.
     */
    var logicalRows = new List<string>();
    string? currentRow = null;

    foreach (var pageText in pageTexts)
    {
        foreach (var rawLine in pageText.Replace("\r", "").Split('\n'))
        {
            var line = Regex.Replace(rawLine, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var startsWithDate = Regex.IsMatch(
                line,
                @"^\d{1,2}[/-]\d{1,2}(?:[/-]\d{2,4})?\s+" +
                @"|^\d{1,2}\s+[A-Za-z]{3}(?:\s+\d{2,4})?\s+");

            if (startsWithDate)
            {
                if (!string.IsNullOrWhiteSpace(currentRow))
                {
                    logicalRows.Add(currentRow);
                }

                currentRow = line;
                continue;
            }

            /*
             * Only append continuation text while we are already
             * building a dated transaction row.
             */
            if (!string.IsNullOrWhiteSpace(currentRow))
            {
                /*
                 * Stop obvious page/header/footer text from becoming
                 * part of a transaction.
                 */
                if (line.StartsWith("Statement Number", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("ABN ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Number of Debit Transactions", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("TOD :", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Rate Last Change", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                currentRow += " " + line;
            }
        }
    }

    if (!string.IsNullOrWhiteSpace(currentRow))
    {
        logicalRows.Add(currentRow);
    }

    decimal? previousBalance = null;

    foreach (var line in logicalRows)
    {
        var dateMatch = Regex.Match(
            line,
            @"^(?<date>" +
            @"\d{1,2}[/-]\d{1,2}(?:[/-]\d{2,4})?" +
            @"|" +
            @"\d{1,2}\s+[A-Za-z]{3}(?:\s+\d{2,4})?" +
            @")\s+(?<rest>.+)$");

        if (!dateMatch.Success)
        {
            continue;
        }

        var dateText = dateMatch.Groups["date"].Value;

        if (!Regex.IsMatch(dateText, @"\d{2,4}$"))
        {
            dateText += $" {statementYear}";
        }

        if (!TryDate(dateText, out var date))
        {
            continue;
        }

        var rest = dateMatch.Groups["rest"].Value;

        var amounts = Regex.Matches(
            rest,
            @"(?<!\w)" +
            @"(?:\(?-?\$?[\d,]+\.\d{2}\)?)" +
            @"(?:\s*(?:DR|CR))?" +
            @"(?!\w)");

        if (amounts.Count == 0)
        {
            continue;
        }

        /*
         * For statement rows containing both transaction value and
         * running balance, the final amount is the balance and the
         * preceding amount is the transaction value.
         */
        var descriptionEnd = amounts.Count > 1
            ? amounts[^2].Index
            : amounts[^1].Index;

        var description = rest[..descriptionEnd]
            .Trim(' ', '-', '|');

        var endingBalance = ParseAmount(
            Regex.Replace(
                amounts[^1].Value,
                @"\s*(DR|CR)$",
                "",
                RegexOptions.IgnoreCase));

        if (description.Contains(
                "BEGINNING BALANCE",
                StringComparison.OrdinalIgnoreCase))
        {
            previousBalance = endingBalance;
            continue;
        }

        if (description.Contains(
                "ENDING BALANCE",
                StringComparison.OrdinalIgnoreCase))
        {
            previousBalance = endingBalance;
            continue;
        }

        var transactionToken = amounts.Count > 1
            ? amounts[^2]
            : amounts[^1];

        var amountText = transactionToken.Value;

        var displayedAmount = ParseAmount(
            Regex.Replace(
                amountText,
                @"\s*(DR|CR)$",
                "",
                RegexOptions.IgnoreCase));

        var amount = displayedAmount;

        /*
         * Explicit DR/CR markers take precedence where present.
         */
        if (amountText.EndsWith(
                "DR",
                StringComparison.OrdinalIgnoreCase))
        {
            amount = -Math.Abs(displayedAmount);
        }
        else if (amountText.EndsWith(
                     "CR",
                     StringComparison.OrdinalIgnoreCase))
        {
            amount = Math.Abs(displayedAmount);
        }
        else if (amounts.Count > 1 &&
                 previousBalance is not null)
        {
            /*
             * Running balances are the safest way to determine
             * direction when PDF extraction loses Debit/Credit column
             * positions.
             *
             * Positive movement = money into the bank account.
             * Negative movement = money out of the bank account.
             */
            var movement = decimal.Round(
    endingBalance - previousBalance.Value,
    2,
    MidpointRounding.AwayFromZero);

/*
 * Our internal statement convention is always:
 *
 *   negative = charge / purchase / money out
 *   positive = receipt / payment / money in
 *
 * For asset bank/debit accounts the running-balance movement
 * already follows that convention.
 *
 * For a credit-card liability, an increasing statement balance
 * normally represents a new charge, so reverse the movement.
 */
var normalizedMovement =
    accountKind is BankAccountKind.CreditCard or BankAccountKind.Loan
        ? -movement
        : movement;

if (Math.Abs(
        Math.Abs(normalizedMovement) -
        Math.Abs(displayedAmount)) <= 0.02m)
{
    amount = normalizedMovement;
}
        }

        /*
 * Never import a balance-only row as a transaction.
 *
 * Some PDF statements place the statement date beside the ending
 * balance while PDF text extraction separates the "ENDING BALANCE"
 * heading from the amount. That can otherwise produce a fake
 * transaction such as:
 *
 * 31 Jul 2026   Bank transaction   413.98
 */
if (amounts.Count == 1 &&
    string.IsNullOrWhiteSpace(description))
{
    previousBalance = endingBalance;
    continue;
}

        if (amount == 0)
        {
            continue;
        }

        if (description.Length < 2)
{
    continue;
}

        result.Add(
            new StatementPreviewLine(
                date,
                description,
                null,
                amount));

        /*
         * Keep the running balance current after every successfully
         * parsed transaction.
         */
        if (amounts.Count > 1)
        {
            previousBalance = endingBalance;
        }
    }

    if (result.Count == 0)
    {
        throw new InvalidOperationException(
            "Text was found, but no transaction rows could be identified. " +
            "Download the statement as CSV, OFX, QFX or QIF.");
    }

    return result;
}

    private static string? Tag(string block, string name) { var match = Regex.Match(block, $@"<{name}>([^<\r\n]+)", RegexOptions.IgnoreCase); return match.Success ? match.Groups[1].Value : null; }
    private static bool TryOfxDate(string value, out DateOnly date) { var trimmed = value.Trim(); return DateOnly.TryParseExact(trimmed[..Math.Min(8, trimmed.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date); }
    private static decimal ParseAmount(string? value, bool emptyIsZero = false) { if (string.IsNullOrWhiteSpace(value) && emptyIsZero) return 0; var cleaned = (value ?? "").Trim().Replace(",", "").Replace("$", "").Replace("(", "-").Replace(")", ""); if (decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)) return amount; if (emptyIsZero) return 0; throw new InvalidOperationException($"Invalid statement amount '{value}'."); }
    private static string Value(List<string> values, int index) => index >= 0 && index < values.Count ? values[index] : "";
    private static int Find(string[] headers, params string[] names) => FindOptional(headers, names) is var index && index >= 0 ? index : throw new InvalidOperationException($"CSV needs a {names[0]} column.");
    private static int FindOptional(string[] headers, params string[] names) => Array.FindIndex(headers, x => names.Contains(x));
    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TryDate(string value, out DateOnly date) { foreach (var format in new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "dd-MM-yyyy", "d-M-yyyy", "dd-MM-yy", "d-M-yy", "MM/dd/yyyy", "M/d/yyyy", "dd MMM yyyy", "dd MMM yy" }) if (DateOnly.TryParseExact(value.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true; return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date); }
    private static string Hash(DateOnly date, string description, string? reference, decimal amount) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{date:yyyy-MM-dd}|{description.Trim().ToUpperInvariant()}|{reference?.Trim().ToUpperInvariant()}|{amount:F2}")));
    private static string HashOccurrence(string baseHash, int occurrence) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{baseHash}|{occurrence}")));
    private static List<string> ParseRow(string row, char delimiter) { var values = new List<string>(); var value = new StringBuilder(); var quoted = false; for (var i = 0; i < row.Length; i++) { var c = row[i]; if (c == '"') { if (quoted && i + 1 < row.Length && row[i + 1] == '"') { value.Append('"'); i++; } else quoted = !quoted; } else if (c == delimiter && !quoted) { values.Add(value.ToString()); value.Clear(); } else value.Append(c); } if (quoted) throw new InvalidOperationException("CSV contains an unclosed quoted field."); values.Add(value.ToString()); return values; }

    private static BankStatementImportDocument CreateDocument(
        Guid organisationId,
        Guid bankAccountId,
        string userId,
        BankStatementDocumentRequest request)
    {
        const int maximumBytes = 5 * 1024 * 1024;
        var fileName = request.FileName.Trim();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var hasPath = Path.GetFileName(fileName) != fileName;
        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".ofx" or ".qfx" => "application/x-ofx",
            ".qif" => "application/qif",
            _ => ""
        };
        var validPdf = extension != ".pdf" ||
            (request.Content.Length >= 5 &&
             request.Content[0] == (byte)'%' &&
             request.Content[1] == (byte)'P' &&
             request.Content[2] == (byte)'D' &&
             request.Content[3] == (byte)'F' &&
             request.Content[4] == (byte)'-');

        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            hasPath ||
            string.IsNullOrWhiteSpace(contentType) ||
            request.OriginalSize <= 0 ||
            request.OriginalSize > maximumBytes ||
            request.Content.LongLength != request.OriginalSize ||
            !validPdf)
        {
            throw new InvalidOperationException(
                "The statement attachment must be a valid PDF, CSV, OFX, QFX, QIF or text file no larger than 5 MB.");
        }

        return new BankStatementImportDocument
        {
            OrganisationId = organisationId,
            BankAccountId = bankAccountId,
            ImportBatchId = Guid.Empty,
            FileName = fileName,
            ContentType = contentType,
            OriginalSize = request.OriginalSize,
            Content = request.Content,
            UploadedByUserId = userId
        };
    }
}
